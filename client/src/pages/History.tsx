import { useEffect, useState } from 'react'
import { api } from '../api'
import type { ExamResult } from '../api/types'
import type { useCertifications } from '../hooks/useCertifications'
import { Banner, CertPicker, EmptyState, Meter, Skeleton } from '../components/Ui'
import { Link, useRouter } from '../router'

type Certs = ReturnType<typeof useCertifications>

export default function History({ certs }: { certs: Certs }) {
  const { navigate } = useRouter()
  const { certifications, selectedCode, select } = certs
  const [results, setResults] = useState<ExamResult[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    setResults(null)
    setError(null)
    api
      .exams.history(selectedCode)
      .then(setResults)
      .catch((e) => setError(e instanceof Error ? e.message : 'Could not load history'))
  }, [selectedCode])

  const best = results && results.length > 0 ? Math.max(...results.map((r) => r.scorePercent)) : null
  const passRate =
    results && results.length > 0
      ? Math.round((results.filter((r) => r.passed).length / results.length) * 100)
      : null

  return (
    <div className="stack">
      <div className="page-head">
        <h1>Your attempts</h1>
        <p>Completed practice sets and mock exams, newest first.</p>
      </div>

      <div className="toolbar">
        <CertPicker certifications={certifications} selectedCode={selectedCode} onSelect={select} />
        {best !== null && (
          <div className="stat-row" style={{ marginLeft: 'auto' }}>
            <div className="stat">
              <strong className="num">{results?.length}</strong>
              <span>attempts</span>
            </div>
            <div className="stat">
              <strong className="num">{best.toFixed(0)}%</strong>
              <span>best score</span>
            </div>
            <div className="stat">
              <strong className="num">{passRate}%</strong>
              <span>passed</span>
            </div>
          </div>
        )}
      </div>

      {error && <Banner kind="error">{error}</Banner>}
      {!results && !error && (
        <div className="card">
          <Skeleton lines={4} />
        </div>
      )}

      {results?.length === 0 && (
        <EmptyState
          title={`No attempts for ${selectedCode} yet`}
          body="Start a practice set from the dashboard and it shows up here with a full breakdown."
          action={
            <button type="button" className="primary" onClick={() => navigate('/')}>
              Go to dashboard
            </button>
          }
        />
      )}

      {results && results.length > 0 && (
        <div className="card">
          <table className="table">
            <thead>
              <tr>
                <th>Score</th>
                <th>Correct</th>
                <th>Unanswered</th>
                <th>Time</th>
                <th>Progress</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {results.map((r) => (
                <tr key={r.sessionId}>
                  <td>
                    <span className={`chip ${r.passed ? 'pass' : 'fail'}`}>{r.scorePercent.toFixed(0)}%</span>
                  </td>
                  <td className="num">
                    {r.correct}/{r.total}
                  </td>
                  <td className="num">{r.unanswered}</td>
                  <td className="num">
                    {Math.floor(r.secondsSpent / 60)}m {r.secondsSpent % 60}s
                  </td>
                  <td className="bar-cell">
                    <Meter value={r.scorePercent} target={r.passingScore} />
                  </td>
                  <td>
                    <Link to={`/result/${r.sessionId}`}>Review →</Link>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
