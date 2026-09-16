import { useCallback, useEffect, useRef, useState } from 'react'
import { api } from '../api'
import type { ExamMode, Readiness } from '../api/types'
import type { useCertifications } from '../hooks/useCertifications'
import { useRouter } from '../router'
import { Banner, EmptyState, Meter, ScoreDial, Skeleton } from '../components/Ui'

type Certs = ReturnType<typeof useCertifications>

const STATUS_LABEL: Record<string, string> = {
  strong: 'Strong',
  'on-track': 'On track',
  'needs-work': 'Needs work',
  weak: 'Weak',
  'no-data': 'Not practised',
}

export default function Dashboard({ certs }: { certs: Certs }) {
  const { navigate } = useRouter()
  const { certifications, selected, selectedCode, select, loading, error } = certs

  const [readiness, setReadiness] = useState<Readiness | null>(null)
  const [readinessError, setReadinessError] = useState<string | null>(null)
  const [starting, setStarting] = useState<string | null>(null)
  const [startError, setStartError] = useState<string | null>(null)
  const [reviewCount, setReviewCount] = useState<number | null>(null)

  useEffect(() => {
    if (!selectedCode) return
    setReadiness(null)
    setReadinessError(null)
    api
      .insights.readiness(selectedCode)
      .then(setReadiness)
      .catch((e) => setReadinessError(e instanceof Error ? e.message : 'Failed to load readiness'))
  }, [selectedCode])

  // "in your bank" comes from the certifications call, which the hook loads once on mount. It
  // goes stale as soon as questions are generated - from the Generate page, or out of band by a
  // bulk fill - so the dashboard refreshes it rather than showing a number that quietly rots.
  // Held in a ref so refreshing cannot itself re-trigger the effect.
  const reloadCerts = useRef(certs.reload)
  reloadCerts.current = certs.reload

  useEffect(() => {
    void reloadCerts.current()
  }, [selectedCode])

  // Refreshed alongside readiness so the count is current whenever the dashboard is.
  useEffect(() => {
    if (!selectedCode) return
    setReviewCount(null)
    api
      .exams.reviewCount(selectedCode)
      .then((r) => setReviewCount(r.count))
      .catch(() => setReviewCount(null))
  }, [selectedCode])

  const start = useCallback(
    async (mode: ExamMode, questionCount?: number) => {
      if (!selected) return
      setStarting(mode)
      setStartError(null)
      try {
        const session = await api.exams.start({
          certificationCode: selected.code,
          mode,
          questionCount: questionCount ?? null,
        })
        navigate(`/exam/${session.id}`)
      } catch (e) {
        setStartError(e instanceof Error ? e.message : 'Could not start the exam')
      } finally {
        setStarting(null)
      }
    },
    [selected, navigate],
  )

  if (loading) {
    return (
      <div className="card">
        <Skeleton lines={5} />
      </div>
    )
  }
  if (error) return <Banner kind="error">{error}</Banner>
  if (!selected) return <Banner kind="info">No certifications are configured.</Banner>

  const empty = selected.questionCount === 0

  return (
    <div className="stack">
      <div className="cert-strip" role="tablist" aria-label="Certification">
        {certifications.map((c) => (
          <button
            key={c.code}
            type="button"
            role="tab"
            aria-selected={c.code === selectedCode}
            className={`cert-tab${c.code === selectedCode ? ' selected' : ''}`}
            onClick={() => select(c.code)}
          >
            <strong>{c.code}</strong>
            <span>{c.questionCount} questions</span>
          </button>
        ))}
      </div>

      <div className="hero">
        <section className="card hero-main">
          <div>
            <span className="eyebrow">{selected.code}</span>
            <h1>{selected.name}</h1>
          </div>
          <p className="muted measure">{selected.description}</p>

          <div className="hero-facts">
            <div>
              <strong>{selected.passingScore}%</strong>
              pass mark
            </div>
            <div>
              <strong>{selected.examQuestionCount}</strong>
              questions on the real exam
            </div>
            <div>
              <strong>{selected.durationMinutes} min</strong>
              time limit
            </div>
            <div>
              <strong>{selected.questionCount}</strong>
              in your bank
            </div>
          </div>

          {startError && <Banner kind="error">{startError}</Banner>}

          {empty ? (
            <Banner kind="info">
              Your bank for {selected.code} is empty — generate questions to start practising.
            </Banner>
          ) : null}

          <div className="button-row">
            <button
              type="button"
              className="primary"
              disabled={starting !== null || empty}
              onClick={() => start('Practice', 10)}
            >
              {starting === 'Practice' ? 'Starting…' : 'Practice 10 questions'}
            </button>
            <button type="button" disabled={starting !== null || empty} onClick={() => start('Exam')}>
              {starting === 'Exam' ? 'Starting…' : `Timed mock exam · ${selected.examQuestionCount} q`}
            </button>
            {/* Only offered when there is something to review, so the button never leads to an
                empty session. Untimed, with feedback after each question. */}
            {reviewCount !== null && reviewCount > 0 && (
              <button
                type="button"
                className="review"
                disabled={starting !== null}
                onClick={() => start('Review', Math.min(reviewCount, 20))}
                title="The questions you most recently got wrong"
              >
                {starting === 'Review' ? 'Starting…' : `Review mistakes · ${reviewCount}`}
              </button>
            )}
            <button type="button" className="ghost" onClick={() => navigate('/generate')}>
              Generate questions
            </button>
          </div>
        </section>

        <section className="card readiness-card">
          <div className="card-head" style={{ width: '100%', justifyContent: 'center' }}>
            <h2>Exam readiness</h2>
          </div>

          {readinessError && <Banner kind="error">{readinessError}</Banner>}
          {!readiness && !readinessError && <Skeleton lines={4} />}

          {readiness && (
            <>
              <ScoreDial
                value={readiness.predictedScorePercent}
                target={readiness.passingScore}
                caption={`pass ${readiness.passingScore}%`}
              />
              <span
                className={`chip ${
                  readiness.answersAnalyzed === 0 ? '' : readiness.likelyToPass ? 'pass' : 'fail'
                }`}
              >
                {readiness.answersAnalyzed === 0
                  ? 'No attempts yet'
                  : readiness.likelyToPass
                    ? 'On track to pass'
                    : 'Not ready yet'}
              </span>
              <p className="muted small">
                {readiness.answersAnalyzed === 0
                  ? 'Answer some questions and a projection appears here.'
                  : `From ${readiness.answersAnalyzed} answers · ${
                      readiness.method.startsWith('ml.net')
                        ? `ML model, ${(readiness.confidence * 100).toFixed(0)}% confidence`
                        : 'observed accuracy so far'
                    }`}
              </p>
            </>
          )}
        </section>
      </div>

      {readiness && readiness.answersAnalyzed > 0 && (
        <section className="card">
          <div className="card-head">
            <h2>Domain strength</h2>
            <span className="muted small">Bars show predicted accuracy; tick marks the pass mark</span>
          </div>

          <div className="domain-rows">
            {readiness.domains.map((d) => (
              <div className="domain-row" key={d.domain}>
                <span className="domain-name" title={d.domain}>
                  {d.domain}
                </span>
                <span className={`chip status-${d.status}`}>{STATUS_LABEL[d.status] ?? d.status}</span>
                <Meter
                  value={d.answered === 0 ? 0 : d.predictedAccuracy}
                  target={readiness.passingScore}
                  tone={d.answered === 0 ? 'neutral' : 'auto'}
                />
                <span className="domain-meta">
                  <span>{d.weightPercent}% of exam</span>
                  <span>
                    {d.answered === 0
                      ? 'no answers yet'
                      : `${d.answered} answered · ${d.observedAccuracy}% correct so far`}
                  </span>
                  {d.answered > 0 && <span>predicted {d.predictedAccuracy}%</span>}
                </span>
              </div>
            ))}
          </div>
        </section>
      )}

      {readiness && readiness.recommendations.length > 0 && (
        <section className="card">
          <h2>What to do next</h2>
          <div className="rec-list">
            {readiness.recommendations.map((r) => {
              const domain = /"([^"]+)"/.exec(r)?.[1]
              const target = domain ? selected.domains.find((d) => d.name === domain) : undefined
              return (
                <div className="rec" key={r}>
                  <p>{r}</p>
                  {target && (
                    <button
                      type="button"
                      className="small"
                      onClick={() => navigate(`/generate?domainId=${target.id}&difficulty=Hard&count=10`)}
                    >
                      Generate 10
                    </button>
                  )}
                </div>
              )
            })}
          </div>
        </section>
      )}

      {empty && (
        <EmptyState
          title="Nothing to practise yet"
          body={`Generate a first batch of ${selected.code} questions — pick a domain and difficulty, and the AI writes them against the official exam blueprint.`}
          action={
            <button type="button" className="primary" onClick={() => navigate('/generate')}>
              Generate questions
            </button>
          }
        />
      )}
    </div>
  )
}
