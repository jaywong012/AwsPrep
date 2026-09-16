import { useCallback, useEffect, useState } from 'react'
import { ApiError, api, getAdminKey, setAdminKey } from '../api'
import type { Difficulty, Question, QuestionSource } from '../api/types'
import type { useCertifications } from '../hooks/useCertifications'
import { Banner, CertPicker, ConfirmDialog, DifficultyBadge, EmptyState, Skeleton } from '../components/Ui'
import { useRouter } from '../router'

// 'Reference' items come from the real exam-style bank shipped with the app, not from the LLM,
// so they are worth calling out as the ones whose wording matches the exam.
const SOURCE_LABELS: Record<QuestionSource, string> = {
  Ai: 'AI',
  Seed: 'Starter',
  Manual: 'Manual',
  Reference: 'Real exam style',
}

type Certs = ReturnType<typeof useCertifications>

const PAGE_SIZE = 20

export default function Bank({ certs }: { certs: Certs }) {
  const { navigate } = useRouter()
  const { certifications, selected, selectedCode, select, reload } = certs

  const [domainId, setDomainId] = useState<number | ''>('')
  const [difficulty, setDifficulty] = useState<Difficulty | ''>('')
  const [page, setPage] = useState(0)
  const [questions, setQuestions] = useState<Question[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [expanded, setExpanded] = useState<Set<string>>(new Set())
  const [pendingDelete, setPendingDelete] = useState<Question | null>(null)
  // The API gates deletion behind an operator key in production. The field only appears once a
  // delete is actually refused, so a local setup never sees it.
  const [adminKeyRequired, setAdminKeyRequired] = useState(false)
  const [adminKeyDraft, setAdminKeyDraft] = useState(getAdminKey)

  const load = useCallback(async () => {
    if (!selectedCode) return
    setQuestions(null)
    setError(null)
    try {
      const data = await api.questions.browse({
        certificationCode: selectedCode,
        domainId: domainId === '' ? null : domainId,
        difficulty: difficulty === '' ? null : difficulty,
        skip: page * PAGE_SIZE,
        take: PAGE_SIZE,
      })
      setQuestions(data)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load questions')
      setQuestions([])
    }
  }, [selectedCode, domainId, difficulty, page])

  useEffect(() => {
    void load()
  }, [load])

  async function remove(q: Question) {
    setPendingDelete(null)
    try {
      await api.questions.remove(q.id)
      setQuestions((qs) => (qs ? qs.filter((x) => x.id !== q.id) : qs))
      await reload()
    } catch (e) {
      if (e instanceof ApiError && (e.status === 403 || e.status === 503)) setAdminKeyRequired(true)
      setError(e instanceof Error ? e.message : 'Delete failed')
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

  return (
    <div className="stack">
      <div className="page-head">
        <h1>Question bank</h1>
        <p>
          Everything saved for {selectedCode}
          {selected ? ` — ${selected.questionCount} question${selected.questionCount === 1 ? '' : 's'}` : ''}.
        </p>
      </div>

      <div className="toolbar">
        <CertPicker
          certifications={certifications}
          selectedCode={selectedCode}
          onSelect={(code) => {
            select(code)
            setDomainId('')
            setPage(0)
          }}
        />
        <label className="field">
          <span>Domain</span>
          <select
            value={domainId}
            onChange={(e) => {
              setDomainId(e.target.value === '' ? '' : Number(e.target.value))
              setPage(0)
            }}
          >
            <option value="">All domains</option>
            {selected?.domains.map((d) => (
              <option key={d.id} value={d.id}>
                {d.name} ({d.questionCount})
              </option>
            ))}
          </select>
        </label>
        <label className="field">
          <span>Difficulty</span>
          <select
            value={difficulty}
            onChange={(e) => {
              setDifficulty(e.target.value as Difficulty | '')
              setPage(0)
            }}
          >
            <option value="">Any</option>
            <option value="Easy">Easy</option>
            <option value="Medium">Medium</option>
            <option value="Hard">Hard</option>
          </select>
        </label>
      </div>

      {error && <Banner kind="error">{error}</Banner>}

      {adminKeyRequired && (
        <div className="card">
          <label className="field wide">
            <span>Administrator key</span>
            <input
              type="password"
              value={adminKeyDraft}
              placeholder="Value of Admin:ApiKey on the API"
              autoComplete="off"
              onChange={(e) => setAdminKeyDraft(e.target.value)}
            />
          </label>
          <p className="muted small">
            Deleting questions changes a bank everyone shares, so the API asks for the operator key.
            It is stored in this browser only and sent with administrative calls.
          </p>
          <div className="button-row">
            <button
              type="button"
              className="primary"
              onClick={() => {
                setAdminKey(adminKeyDraft)
                setAdminKeyRequired(false)
                setError(null)
              }}
            >
              Save key
            </button>
            <button type="button" className="ghost" onClick={() => setAdminKeyRequired(false)}>
              Cancel
            </button>
          </div>
        </div>
      )}

      {questions === null && (
        <div className="card">
          <Skeleton lines={6} />
        </div>
      )}

      {questions?.length === 0 && !error && (
        <EmptyState
          title="No questions match"
          body="Either loosen the filters, or generate a batch for this certification and domain."
          action={
            <button type="button" className="primary" onClick={() => navigate('/generate')}>
              Generate questions
            </button>
          }
        />
      )}

      {questions && questions.length > 0 && (
        <>
          <ol className="question-list">
            {questions.map((q) => (
              <li key={q.id} className="question">
                <div className="question-head">
                  <DifficultyBadge value={q.difficulty} />
                  {q.domainName && <span className="chip">{q.domainName}</span>}
                  <span className="chip">{SOURCE_LABELS[q.source]}</span>
                  {q.type === 'MultipleChoice' && <span className="chip accent">Multi-response</span>}
                  <span className="spacer" />
                  <button type="button" className="small" onClick={() => toggle(q.id)}>
                    {expanded.has(q.id) ? 'Hide answer' : 'Show answer'}
                  </button>
                  <button
                    type="button"
                    className="icon"
                    title="Delete question"
                    aria-label="Delete question"
                    onClick={() => setPendingDelete(q)}
                  >
                    ✕
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
                    {q.serviceTags && <p className="muted small">Services: {q.serviceTags}</p>}
                  </>
                )}
              </li>
            ))}
          </ol>

          <div className="button-row">
            <button type="button" className="ghost" disabled={page === 0} onClick={() => setPage((p) => p - 1)}>
              ← Previous
            </button>
            <span className="muted small">Page {page + 1}</span>
            <button
              type="button"
              className="ghost"
              disabled={questions.length < PAGE_SIZE}
              onClick={() => setPage((p) => p + 1)}
            >
              Next →
            </button>
          </div>
        </>
      )}

      {pendingDelete && (
        <ConfirmDialog
          title="Delete this question?"
          body={pendingDelete.stem.length > 120 ? `${pendingDelete.stem.slice(0, 120)}…` : pendingDelete.stem}
          confirmLabel="Delete"
          cancelLabel="Cancel"
          onConfirm={() => void remove(pendingDelete)}
          onCancel={() => setPendingDelete(null)}
        />
      )}
    </div>
  )
}
