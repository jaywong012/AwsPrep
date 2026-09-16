using AwsCertPrep.Api.Data;
using AwsCertPrep.Api.Domain;
using AwsCertPrep.Api.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace AwsCertPrep.Api.Ml;

/// <summary>One answered exam question, flattened into ML.NET features.</summary>
public class AnswerFeatures
{
    public string Domain { get; set; } = "";
    public float Difficulty { get; set; }          // 0 easy, 1 medium, 2 hard
    public float IsMultiResponse { get; set; }      // 0/1
    public float SecondsSpent { get; set; }
    public float OptionCount { get; set; }
    public float AttemptIndex { get; set; }         // how many answers the user had given before this one
    public bool Correct { get; set; }               // label
}

public class CorrectnessPrediction
{
    [ColumnName("PredictedLabel")] public bool WillAnswerCorrectly { get; set; }
    public float Probability { get; set; }
    public float Score { get; set; }
}

/// <summary>
/// Predicts exam readiness from the user's answer history.
/// Uses an ML.NET logistic-regression pipeline once there is enough data
/// (>= MinTrainingRows), and falls back to weighted observed accuracy before that.
/// </summary>
public class ReadinessService(AppDbContext db, IMemoryCache cache, ILogger<ReadinessService> logger)
{
    private const int MinTrainingRows = 40;

    /// <summary>
    /// Training and 3-fold cross-validation take real CPU, and the projection only moves when
    /// the learner answers something new, so a result is cached against the answer history it
    /// was computed from. A new answer changes the key; nothing has to be invalidated by hand.
    /// </summary>
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(15);

    public async Task<ReadinessDto> AnalyzeAsync(string certificationCode, string userKey, CancellationToken ct)
    {
        var cert = await db.Certifications
            .Include(c => c.Domains)
            .FirstOrDefaultAsync(c => c.Code == certificationCode, ct)
            ?? throw new KeyNotFoundException($"Unknown certification code '{certificationCode}'.");

        var answers = await db.ExamSessionQuestions
            .Where(i => i.ExamSession!.UserKey == userKey
                     && i.ExamSession.CertificationId == cert.Id
                     && i.AnsweredAt != null)
            .OrderBy(i => i.AnsweredAt)
            .Select(i => new
            {
                Domain = i.Question!.Domain != null ? i.Question.Domain.Name : "Unclassified",
                i.Question.Difficulty,
                i.Question.Type,
                OptionCount = i.Question.Options.Count,
                i.SecondsSpent,
                i.AnsweredAt,
                Correct = i.IsCorrect == true
            })
            .ToListAsync(ct);

        var cacheKey = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"readiness:{cert.Code}:{userKey}:{answers.Count}:{answers.LastOrDefault()?.AnsweredAt?.Ticks ?? 0}");

        if (cache.TryGetValue(cacheKey, out ReadinessDto? cached) && cached is not null)
            return cached;

        var rows = answers
            .Select((a, idx) => new AnswerFeatures
            {
                Domain = a.Domain,
                Difficulty = (int)a.Difficulty,
                IsMultiResponse = a.Type == QuestionType.MultipleChoice ? 1f : 0f,
                SecondsSpent = Math.Min(a.SecondsSpent, 600),
                OptionCount = a.OptionCount,
                AttemptIndex = idx,
                Correct = a.Correct
            })
            .ToList();

        var observed = rows
            .GroupBy(r => r.Domain)
            .ToDictionary(
                g => g.Key,
                g => (Answered: g.Count(), Accuracy: (decimal)g.Average(x => x.Correct ? 1d : 0d) * 100m));

        var (method, predictor, confidence) = TryTrain(rows);

        var domainReadiness = new List<DomainReadinessDto>();
        foreach (var d in cert.Domains.OrderByDescending(d => d.WeightPercent))
        {
            observed.TryGetValue(d.Name, out var obs);

            var predicted = predictor is null
                ? FallbackAccuracy(obs.Answered, obs.Accuracy)
                : PredictDomainAccuracy(predictor, d.Name, rows);

            domainReadiness.Add(new DomainReadinessDto(
                d.Name,
                d.WeightPercent,
                obs.Answered,
                Math.Round(obs.Accuracy, 1),
                Math.Round(predicted, 1),
                Status(predicted, obs.Answered, cert.PassingScore)));
        }

        // Weight each domain by its share of the real exam.
        var totalWeight = domainReadiness.Sum(d => d.WeightPercent);
        var projected = totalWeight == 0
            ? 0m
            : domainReadiness.Sum(d => d.PredictedAccuracy * d.WeightPercent) / totalWeight;

        var recommendations = BuildRecommendations(domainReadiness, projected, cert.PassingScore, rows.Count);

        var result = new ReadinessDto(
            cert.Code,
            method,
            Math.Round(projected, 1),
            cert.PassingScore,
            projected >= cert.PassingScore,
            Math.Round(confidence, 2),
            rows.Count,
            domainReadiness,
            recommendations);

        cache.Set(cacheKey, result, CacheLifetime);
        return result;
    }

    private (string Method, PredictionEngine<AnswerFeatures, CorrectnessPrediction>? Engine, decimal Confidence)
        TryTrain(List<AnswerFeatures> rows)
    {
        // Need both classes present, otherwise the trainer has nothing to separate.
        if (rows.Count < MinTrainingRows || rows.All(r => r.Correct) || rows.All(r => !r.Correct))
        {
            var confidence = Math.Min(1m, rows.Count / (decimal)MinTrainingRows) * 0.6m;
            return ("observed-accuracy", null, confidence);
        }

        try
        {
            var ml = new MLContext(seed: 42);
            var data = ml.Data.LoadFromEnumerable(rows);

            var pipeline = ml.Transforms.Categorical.OneHotEncoding("DomainEncoded", nameof(AnswerFeatures.Domain))
                .Append(ml.Transforms.Concatenate("Features",
                    "DomainEncoded",
                    nameof(AnswerFeatures.Difficulty),
                    nameof(AnswerFeatures.IsMultiResponse),
                    nameof(AnswerFeatures.SecondsSpent),
                    nameof(AnswerFeatures.OptionCount),
                    nameof(AnswerFeatures.AttemptIndex)))
                .Append(ml.Transforms.NormalizeMinMax("Features"))
                .Append(ml.BinaryClassification.Trainers.SdcaLogisticRegression(
                    labelColumnName: nameof(AnswerFeatures.Correct),
                    featureColumnName: "Features"));

            var model = pipeline.Fit(data);

            // Cross-validated AUC becomes the reported confidence.
            var cv = ml.BinaryClassification.CrossValidate(
                data, pipeline, numberOfFolds: 3, labelColumnName: nameof(AnswerFeatures.Correct));
            var auc = cv.Average(f => f.Metrics.AreaUnderRocCurve);
            var confidence = (decimal)Math.Clamp(auc, 0.5, 1.0);

            var engine = ml.Model.CreatePredictionEngine<AnswerFeatures, CorrectnessPrediction>(model);
            return ("ml.net-logistic-regression", engine, confidence);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ML.NET training failed; falling back to observed accuracy.");
            return ("observed-accuracy (training failed)", null, 0.4m);
        }
    }

    /// <summary>
    /// Averages the model's predicted probability over a representative spread of
    /// question shapes for the domain - an expected accuracy, not a single guess.
    /// </summary>
    private static decimal PredictDomainAccuracy(
        PredictionEngine<AnswerFeatures, CorrectnessPrediction> engine, string domain, List<AnswerFeatures> rows)
    {
        var medianSeconds = rows.Count == 0 ? 45f : rows.OrderBy(r => r.SecondsSpent).ElementAt(rows.Count / 2).SecondsSpent;
        var nextAttempt = rows.Count;

        var probes = new List<AnswerFeatures>();
        foreach (var difficulty in new[] { 0f, 1f, 2f })
        {
            foreach (var (multi, optionCount) in new[] { (0f, 4f), (1f, 5f) })
            {
                probes.Add(new AnswerFeatures
                {
                    Domain = domain,
                    Difficulty = difficulty,
                    IsMultiResponse = multi,
                    SecondsSpent = medianSeconds,
                    OptionCount = optionCount,
                    AttemptIndex = nextAttempt
                });
            }
        }

        // Single-answer questions dominate the real exam, so weight them 3:1.
        var weighted = probes.Sum(p => engine.Predict(p).Probability * (p.IsMultiResponse == 0 ? 3d : 1d));
        var weight = probes.Sum(p => p.IsMultiResponse == 0 ? 3d : 1d);
        return (decimal)(weighted / weight) * 100m;
    }

    private static decimal FallbackAccuracy(int answered, decimal accuracy)
    {
        if (answered == 0) return 0m;
        // Shrink small samples toward 50% so three lucky answers do not read as exam-ready.
        var trust = Math.Min(1m, answered / 10m);
        return accuracy * trust + 50m * (1 - trust);
    }

    private static string Status(decimal predicted, int answered, int passingScore)
    {
        if (answered == 0) return "no-data";
        if (predicted >= passingScore + 10) return "strong";
        if (predicted >= passingScore) return "on-track";
        if (predicted >= passingScore - 15) return "needs-work";
        return "weak";
    }

    private static List<string> BuildRecommendations(
        List<DomainReadinessDto> domains, decimal projected, int passingScore, int answerCount)
    {
        var recs = new List<string>();

        if (answerCount < MinTrainingRows)
            recs.Add($"Answer {MinTrainingRows - answerCount} more questions to switch from observed accuracy to the ML prediction.");

        // The domain name is quoted so the client can offer a "generate for this domain"
        // action next to the text instead of repeating the instruction in prose.
        var gaps = domains
            .Where(d => d.Status is "weak" or "needs-work")
            .OrderByDescending(d => (passingScore - d.PredictedAccuracy) * d.WeightPercent)
            .Take(3)
            .ToList();

        foreach (var d in gaps)
            recs.Add($"\"{d.Domain}\" is {d.WeightPercent:0.#}% of the exam and you are tracking at {d.PredictedAccuracy:0.#}%.");

        var untouched = domains.Where(d => d.Answered == 0).ToList();
        if (untouched.Count == 1)
            recs.Add($"\"{untouched[0].Domain}\" has no answers yet — practise it to get a real signal.");
        else if (untouched.Count > 1)
            recs.Add($"No answers yet in {untouched.Count} domains: {string.Join(", ", untouched.Select(d => d.Domain))}.");

        if (projected >= passingScore && gaps.Count == 0 && untouched.Count == 0)
            recs.Add($"Projected {projected:0.#}% clears the {passingScore}% pass mark. Take a full timed mock exam to confirm under time pressure.");

        return recs;
    }
}
