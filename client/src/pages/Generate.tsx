import { useEffect, useState } from 'react'
import { api } from '../api'
import type { Difficulty, GenerateResponse } from '../api/types'
import type { useCertifications } from '../hooks/useCertifications'
import { Banner, CertPicker, DifficultyBadge, Skeleton } from '../components/Ui'
import { useRouter } from '../router'

type Certs = ReturnType<typeof useCertifications>

const DIFFICULTIES: Difficulty[] = ['Easy', 'Medium', 'Hard']

export default function Generate({ certs }: { certs: Certs }) {
  const { query, navigate } = useRouter()
  const { certifications, selected, selectedCode, select, reload } = certs

  const [domainId, setDomainId] = useState<number | ''>('')
  const [difficulty, setDifficulty] = useState<Difficulty>('Medium')
  const [count, setCount] = useState(5)
  const [topicHint, setTopicHint] = useState('')

  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<GenerateResponse | null>(null)
  const [expanded, setExpanded] = useState<Set<string>>(new Set())

  // Prefill from a dashboard recommendation link, e.g. #/generate?domainId=3&difficulty=Hard
  useEffect(() => {
    const d = query.get('domainId')
    const diff = query.get('difficulty')
    const c = query.get('count')
    if (d) setDomainId(Number(d))
    if (diff && DIFFICULTIES.includes(diff as Difficulty)) setDifficulty(diff as Difficulty)
    if (c) setCount(Math.max(1, Math.min(20, Number(c) || 5)))
  }, [query])

  async function submit(e: React.FormEvent) {
    e.preventDefault()
    if (!selected) return
    setBusy(true)
    setError(null)
    setResult(null)
    try {
      const response = await api.questions.generate({
        certificationCode: selected.code,
        domainId: domainId === '' ? null : domainId,
        difficulty,
        count,
        topicHint: topicHint.trim() === '' ? null : topicHint.trim(),
      })
      setResult(response)
      await reload()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Generation failed')
    } finally {
      setBusy(false)
    }
  }

  function toggle(id: string) {
    setExpanded((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  const domainName = selected?.domains.find((d) => d.id === domainId)?.name

  return (
    <div className="stack">
      <div className="page-head">
        <h1>Generate practice questions</h1>
        <p>
          Questions are written against the official exam blueprint, validated, deduped against your bank,
          and saved with an explanation.
        </p>
      </div>

      <form className="card" onSubmit={submit}>
        <div className="form-grid">
          <CertPicker certifications={certifications} selectedCode={selectedCode} onSelect={select} />

          <label className="field">
            <span>Domain</span>
            <select
              value={domainId}
              onChange={(e) => setDomainId(e.target.value === '' ? '' : Number(e.target.value))}
            >
              <option value="">All domains</option>
              {selected?.domains.map((d) => (
                <option key={d.id} value={d.id}>
                  {d.name} ({d.weightPercent}%)
                </option>
              ))}
            </select>
          </label>

          <label className="field">
            <span>Difficulty</span>
            <select value={difficulty} onChange={(e) => setDifficulty(e.target.value as Difficulty)}>
              {DIFFICULTIES.map((d) => (
                <option key={d} value={d}>
                  {d}
                </option>
              ))}
            </select>
          </label>

          <label className="field">
            <span>How many</span>
            <input
              type="number"
              min={1}
              max={20}
              value={count}
              onChange={(e) => setCount(Math.max(1, Math.min(20, Number(e.target.value) || 1)))}
            />
          </label>

          <label className="field wide">
            <span>Topic focus — optional</span>
            <input
              type="text"
              maxLength={300}
              placeholder="e.g. Bedrock knowledge bases, embeddings, RAG evaluation"
              value={topicHint}
              onChange={(e) => setTopicHint(e.target.value)}
            />
          </label>
        </div>

        <div className="button-row">
          <button type="submit" className="primary" disabled={busy || !selected}>
            {busy ? 'Writing questions…' : `Generate ${count} ${difficulty.toLowerCase()} question${count === 1 ? '' : 's'}`}
          </button>
          <span className="muted small">
            {domainName ? `Domain: ${domainName}` : 'Model picks domains by exam weight'}
          </span>
        </div>

        {busy && (
          <>
            <Skeleton lines={3} />
            <p className="muted small">
              A batch of {count} usually takes 10–30 seconds. Free-tier rate limits are retried automatically.
            </p>
          </>
        )}
        {error && <Banner kind="error">{error}</Banner>}
      </form>

      {result && (
        <section className="card">
          <div className="card-head">
            <h2>
              Added {result.created} of {result.requested} to your bank
            </h2>
          </div>

          {result.warning && <Banner kind="warning">{result.warning}</Banner>}
          {(result.duplicates > 0 || result.rejected > 0) && (
            <p className="muted small">
              Skipped {result.duplicates} duplicate{result.duplicates === 1 ? '' : 's'} and {result.rejected} that
              failed validation.
            </p>
          )}

          <div className="button-row">
            <button type="button" className="primary" onClick={() => navigate('/')}>
              Practise these now
            </button>
            <button type="button" className="ghost" onClick={() => navigate('/bank')}>
              Open question bank
            </button>
          </div>

          <ol className="question-list">
            {result.questions.map((q) => (
              <li key={q.id} className="question">
                <div className="question-head">
                  <DifficultyBadge value={q.difficulty} />
                  {q.domainName && <span className="chip">{q.domainName}</span>}
                  {q.type === 'MultipleChoice' && <span className="chip accent">Select all that apply</span>}
                  <span className="spacer" />
                  <button type="button" className="small" onClick={() => toggle(q.id)}>
                    {expanded.has(q.id) ? 'Hide answer' : 'Show answer'}
                  </button>
                </div>
                <p className="stem">{q.stem}</p>

                {expanded.has(q.id) && (
                  <>
                    <ul className="options">
                      {q.options.map((o) => (
                        <li key={o.label} className={o.isCorrect ? 'correct' : undefined}>
                          <span className="opt-label" aria-hidden="true">
                            {o.label}
                          </span>
                          <span>{o.text}</span>
                        </li>
                      ))}
                    </ul>
                    {q.explanation && (
                      <p className="explanation">
                        <strong>Why: </strong>
                        {q.explanation}
                      </p>
                    )}
                  </>
                )}
              </li>
            ))}
          </ol>
        </section>
      )}
    </div>
  )
}
