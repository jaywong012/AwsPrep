using System.Text.Json;
using System.Text.Json.Serialization;
using AwsCertPrep.Api.Data;
using AwsCertPrep.Api.Middleware;
using AwsCertPrep.Api.Ml;
using AwsCertPrep.Api.Application.Abstractions;
using AwsCertPrep.Api.Application.Exams;
using AwsCertPrep.Api.Application.Lessons;
using AwsCertPrep.Api.Application.Options;
using AwsCertPrep.Api.Infrastructure.Messaging;
using AwsCertPrep.Api.Infrastructure.Persistence;
using AwsCertPrep.Api.Services;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// Deployed environments log to an aggregator, which parses JSON rather than the human-formatted
// console lines that make local development readable.
if (!builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);
}

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddMemoryCache();

// Every request and response in this API is small JSON. Capping the body protects the process
// from having to buffer something large before a model binder ever looks at it.
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 128 * 1024);

// ---------- database ----------
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString, sql =>
{
    sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null);
    sql.CommandTimeout(30);
}));

// ---------- AI generation ----------
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));

var aiProvider = builder.Configuration[$"{AiOptions.SectionName}:Provider"] ?? "Offline";
var aiTimeout = TimeSpan.FromSeconds(
    builder.Configuration.GetValue<int?>($"{AiOptions.SectionName}:TimeoutSeconds") ?? 90);

switch (aiProvider.ToLowerInvariant())
{
    case "gemini":
        builder.Services.AddHttpClient<IAiTextCompletion, GeminiTextCompletion>(c =>
        {
            c.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
            c.Timeout = aiTimeout;
        });
        builder.Services.AddScoped<IQuestionGenerator, AiQuestionGenerator>();
        break;

    case "deepseek":
        builder.Services.AddHttpClient<IAiTextCompletion, DeepSeekTextCompletion>(c =>
        {
            c.BaseAddress = new Uri("https://api.deepseek.com/");
            c.Timeout = aiTimeout;
        });
        builder.Services.AddScoped<IQuestionGenerator, AiQuestionGenerator>();
        break;

    case "groq":
        builder.Services.AddHttpClient<IAiTextCompletion, GroqTextCompletion>(c =>
        {
            c.BaseAddress = new Uri("https://api.groq.com/");
            c.Timeout = aiTimeout;
        });
        builder.Services.AddScoped<IQuestionGenerator, AiQuestionGenerator>();
        break;

    default:
        // No provider: questions still come from the local template bank, and the lessons tab
        // serves its seeded facts without the generated depth.
        builder.Services.AddSingleton<IAiTextCompletion, OfflineTextCompletion>();
        builder.Services.AddSingleton<IQuestionGenerator, OfflineQuestionGenerator>();
        break;
}

// Real exam-style items, embedded at build time: they seed the bank and calibrate the prompt.
builder.Services.AddSingleton<ReferenceBank>();

builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));
builder.Services.AddScoped<AdminOnlyFilter>();

// ---------- application: mediator, unit of work, bound options ----------
builder.Services.AddStudyOptions(builder.Configuration);
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
builder.Services.AddMediator();

builder.Services.AddScoped<QuestionAuditService>();
builder.Services.AddScoped<ExamReadModel>();
builder.Services.AddScoped<LessonNoteWriter>();
builder.Services.AddScoped<LessonReadModel>();
builder.Services.AddScoped<ReadinessService>();

// ---------- limits, health, transport ----------
builder.Services.AddAppRateLimiting(builder.Configuration);

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database", tags: ["ready"])
    .AddCheck<QuestionBankHealthCheck>("question-bank", tags: ["ready"]);

builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
});

// Deployments behind a reverse proxy or ingress need the original scheme and client address:
// the first drives HTTPS redirection, the second partitions the global rate limiter.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

// ---------- CORS ----------
// Defaults to the local Vite origins. A deployment that serves the SPA from another origin must
// list it in Cors:AllowedOrigins; an unlisted origin is refused rather than silently allowed.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173", "http://localhost:4173"];

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(allowedOrigins)
    .WithHeaders("Content-Type", UserKeyAccessor.HeaderName, AdminOnlyFilter.HeaderName)
    // PUT is used by the idempotent writes: setting lesson progress, and rewriting lesson notes.
    .WithMethods("GET", "POST", "PUT", "DELETE")
    .SetPreflightMaxAge(TimeSpan.FromHours(1))));

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandling();

// Off in Development (the dev profile is plain http) and configurable everywhere else: a proxy
// that terminates TLS without forwarding X-Forwarded-Proto would otherwise redirect in a loop.
if (builder.Configuration.GetValue<bool?>("Hosting:UseHttpsRedirection") ?? !app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseSecurityHeaders();
app.UseResponseCompression();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseRateLimiter();
app.MapControllers();

// Liveness: answers without touching the database, so a database blip never recycles the pod.
app.MapGet("/api/health", (IQuestionGenerator generator) =>
        Results.Ok(new { status = "ok", aiProvider = generator.Provider }))
    .DisableRateLimiting();

// Readiness: the database is reachable and the question bank has something to serve.
app.MapHealthChecks("/api/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponse,
}).DisableRateLimiting();

// ---------- startup database work ----------
// Migrating on startup is convenient for one instance and wrong for several: two instances
// racing the same migration is how a deployment corrupts its own schema. So it follows the
// environment by default (on in Development, off elsewhere) and is overridable either way.
var migrateOnStartup = builder.Configuration.GetValue<bool?>("Database:MigrateOnStartup")
                       ?? app.Environment.IsDevelopment();
var seedOnStartup = builder.Configuration.GetValue<bool?>("Database:SeedOnStartup") ?? true;

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    try
    {
        if (migrateOnStartup)
        {
            await db.Database.MigrateAsync();
        }
        else
        {
            var pending = await db.Database.GetPendingMigrationsAsync();
            if (pending.Any())
            {
                app.Logger.LogWarning(
                    "Database:MigrateOnStartup is off and {Count} migration(s) are pending: {Migrations}. "
                    + "Apply them with 'dotnet ef database update' before serving traffic.",
                    pending.Count(), string.Join(", ", pending));
            }
        }

        if (seedOnStartup)
        {
            var referenceBank = scope.ServiceProvider.GetRequiredService<ReferenceBank>();
            await SeedData.EnsureSeededAsync(db, referenceBank);
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogCritical(ex, "Database startup work failed. The API cannot serve traffic.");
        throw;
    }
}

WarnAboutRiskyProductionConfiguration(app, aiProvider, allowedOrigins);

app.Run();

static async Task WriteHealthResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";

    await context.Response.WriteAsync(JsonSerializer.Serialize(new
    {
        status = report.Status.ToString(),
        totalDurationMs = (int)report.TotalDuration.TotalMilliseconds,
        checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            description = e.Value.Description,
        }),
    }));
}

/// <summary>
/// Says out loud, once, the settings that are fine locally and wrong in production. Misconfigured
/// deployments are far easier to spot in the first lines of a log than in a support ticket.
/// </summary>
static void WarnAboutRiskyProductionConfiguration(WebApplication app, string aiProvider, string[] allowedOrigins)
{
    if (app.Environment.IsDevelopment()) return;

    if (string.IsNullOrWhiteSpace(app.Configuration[$"{AdminOptions.SectionName}:ApiKey"]))
    {
        app.Logger.LogWarning(
            "Admin:ApiKey is not configured, so deleting questions and applying audit cleanups are disabled.");
    }

    if (allowedOrigins.All(o => o.Contains("localhost", StringComparison.OrdinalIgnoreCase)))
    {
        app.Logger.LogWarning(
            "Cors:AllowedOrigins still lists only localhost. A browser on the deployed origin will be refused.");
    }

    if (string.Equals(aiProvider, "Offline", StringComparison.OrdinalIgnoreCase))
    {
        app.Logger.LogInformation(
            "Ai:Provider is Offline: generation serves the built-in template bank instead of calling an LLM.");
    }
}
