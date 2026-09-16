import { useEffect, useState } from 'react'
import { api } from '../api'
import type { ExamResult, ExamSession } from '../api/types'
import { Banner, Meter, ScoreDial, Skeleton } from '../components/Ui'
import { Link } from '../router'

export default function Result({ sessionId }: { sessionId?: string }) {
  const [result, setResult] = useState<ExamResult | null>(null)
  const [session, setSession] = useState<ExamSession | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [filter, setFilter] = useState<'all' | 'wrong'>('wrong')
  const [showReview, setShowReview] = useState(false)

  useEffect(() => {
    if (!sessionId) {
      setError('No session id in the URL.')
      return
    }
    // submit is idempotent: it returns the stored result if the session is already scored.
    Promise.all([api.exams.submit(sessionId), api.exams.get(sessionId)])
      .then(([r, s]) => {
        setResult(r)
        setSession(s)
      })
      .catch((e) => setError(e instanceof Error ? e.message : 'Could not load the result'))
  }, [sessionId])

  if (error) return <Banner kind="error">{error}</Banner>
  if (!result || !session) {
    return (
      <div className="card">
        <Skeleton lines={5} />
      </div>
    )
  }

  const minutes = Math.floor(result.secondsSpent / 60)
  const seconds = result.secondsSpent % 60
  const wrongItems = session.items.filter((i) => i.isCorrect !== true)
  const reviewItems = filter === 'wrong' ? wrongItems : session.items

  return (
    <div className="stack">
      <section className="card">
        <div className="result-head">
          <ScoreDial
            value={result.scorePercent}
            target={result.passingScore}
            caption={`pass ${result.passingScore}%`}
            size={150}
          />

          <div className="result-summary">
            <span className="eyebrow">{result.certificationCode}</span>
            <h1>{result.passed ? 'Passed' : 'Not passed yet'}</h1>
            <p className="muted small">
              {result.passed
                ? `${result.scorePercent.toFixed(0)}% is above the ${result.passingScore}% pass mark. Try a full timed exam to confirm under time pressure.`
                : `You need ${result.passingScore}% to pass — that is ${Math.max(
                    1,
                    Math.ceil((result.passingScore / 100) * result.total) - result.correct,
                  )} more correct on a set this size.`}
            </p>

            <div className="stat-row">
              <div className="stat">
                <strong className="num">
                  {result.correct}/{result.total}
                </strong>
                <span>correct</span>
              </div>
              <div className="stat">
                <strong className="num">{result.unanswered}</strong>
                <span>unanswered</span>
              </div>
              <div className="stat">
                <strong className="num">
                  {minutes}m {seconds}s
                </strong>
                <span>on questions</span>
              </div>
            </div>
          </div>
        </div>

        <div className="button-row">
          <button type="button" className="primary" onClick={() => setShowReview((v) => !v)}>
            {showReview ? 'Hide review' : `Review ${wrongItems.length > 0 ? `${wrongItems.length} missed` : 'answers'}`}
          </button>
          <Link to="/" className="button-link">
            Practise again
          </Link>
          <Link to="/generate" className="button-link">
            Generate more questions
          </Link>
        </div>
      </section>

      <section className="card">
        <div className="card-head">
          <h2>Domain breakdown</h2>
          <span className="muted small">Tick marks the {result.passingScore}% pass mark</span>
        </div>
        <div className="domain-rows">
          {result.domainBreakdown.map((d) => (
            <div className="domain-row" key={d.domain}>
              <span className="domain-name" title={d.domain}>
                {d.domain}
              </span>
              <span className="chip">
                {d.correct}/{d.total}
              </span>
              <Meter value={d.accuracyPercent} target={result.passingScore} />
              <span className="domain-meta">
                <span>{d.accuracyPercent}% correct</span>
              </span>
            </div>
          ))}
        </div>
      </section>

      {showReview && (
        <section className="card">
          <div className="card-head">
            <h2>Question review</h2>
            <div className="button-row">
              <button
                type="button"
                className={filter === 'wrong' ? '' : 'ghost'}
                onClick={() => setFilter('wrong')}
              >
                Missed ({wrongItems.length})
              </button>
              <button type="button" className={filter === 'all' ? '' : 'ghost'} onClick={() => setFilter('all')}>
                All ({session.items.length})
              </button>
            </div>
          </div>

          {reviewItems.length === 0 && (
            <Banner kind="success">Every question in this session was correct.</Banner>
          )}

          <ol className="question-list">
            {reviewItems.map((item) => {
              const picked = item.selectedLabels?.split(',') ?? []
              return (
                <li key={item.questionId} className="question">
                  <div className="question-head">
                    <span className={`chip ${item.isCorrect ? 'pass' : 'fail'}`}>
                      {item.isCorrect ? '✓ Correct' : picked.length === 0 ? 'Skipped' : '✕ Incorrect'}
                    </span>
                    {item.question.domainName && <span className="chip">{item.question.domainName}</span>}
                    <span className="muted small">Q{item.order}</span>
                  </div>
                  <p className="stem">{item.question.stem}</p>
                  <ul className="options">
                    {item.question.options.map((o) => {
                      const classes = [
                        o.isCorrect ? 'correct' : '',
                        picked.includes(o.label) && !o.isCorrect ? 'wrong' : '',
                      ]
                        .filter(Boolean)
                        .join(' ')
                      return (
                        <li key={o.label} className={classes}>
                          <span className="opt-label" aria-hidden="true">
                            {o.label}
                          </span>
                          <span>
                            {o.text}
                            {picked.includes(o.label) && <span className="muted small"> — your answer</span>}
                          </span>
                        </li>
                      )
                    })}
                  </ul>
                  {item.question.explanation && (
                    <p className="explanation">
                      <strong>Why: </strong>
                      {item.question.explanation}
                    </p>
                  )}
                </li>
              )
            })}
          </ol>
        </section>
      )}
    </div>
  )
}
