import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { api } from '../api'
import type { AnswerResponse, ExamMode, ExamSession } from '../api/types'
import { Banner, ConfirmDialog, DifficultyBadge, Skeleton } from '../components/Ui'
import { Link, useRouter } from '../router'

/** Review is untimed and gives feedback per question, exactly like practice. */
const MODE_LABEL: Record<ExamMode, string> = {
  Practice: 'Practice',
  Exam: 'Timed exam',
  Review: 'Reviewing your mistakes',
}

export default function Exam({ sessionId }: { sessionId?: string }) {
  const { navigate } = useRouter()
  const [session, setSession] = useState<ExamSession | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [index, setIndex] = useState(0)
  const [selections, setSelections] = useState<Record<string, string[]>>({})
  const [feedback, setFeedback] = useState<Record<string, AnswerResponse>>({})
  const [saving, setSaving] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [secondsLeft, setSecondsLeft] = useState<number | null>(null)

  const questionStart = useRef<number>(Date.now())

  useEffect(() => {
    if (!sessionId) {
      setError('No exam session id in the URL.')
      return
    }
    api
      .exams.get(sessionId)
      .then((s) => {
        setSession(s)
        const restored: Record<string, string[]> = {}
        for (const item of s.items) {
          if (item.selectedLabels) restored[item.questionId] = item.selectedLabels.split(',')
        }
        setSelections(restored)
      })
      .catch((e) => setError(e instanceof Error ? e.message : 'Could not load the exam session'))
  }, [sessionId])

  const submit = useCallback(async () => {
    if (!sessionId || submitting) return
    setSubmitting(true)
    try {
      await api.exams.submit(sessionId)
      navigate(`/result/${sessionId}`)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Submit failed')
      setSubmitting(false)
    }
  }, [sessionId, submitting, navigate])

  const total = session?.items.length ?? 0
  const answeredCount = Object.keys(selections).length

  function requestSubmit() {
    if (answeredCount < total) setConfirmOpen(true)
    else void submit()
  }

  // Countdown for timed exams; auto-submits at zero.
  useEffect(() => {
    if (!session || session.mode !== 'Exam' || session.completedAt) return
    const deadline = new Date(session.startedAt).getTime() + session.durationMinutes * 60_000
    const tick = () => {
      const left = Math.max(0, Math.round((deadline - Date.now()) / 1000))
      setSecondsLeft(left)
      if (left === 0) void submit()
    }
    tick()
    const id = window.setInterval(tick, 1000)
    return () => window.clearInterval(id)
  }, [session, submit])

  useEffect(() => {
    questionStart.current = Date.now()
  }, [index])

  const item = session?.items[index]
  const currentSelection = useMemo(() => (item ? (selections[item.questionId] ?? []) : []), [item, selections])
  const revealed = item ? feedback[item.questionId] : undefined
  const isLast = index === total - 1

  const toggleOption = useCallback(
    (label: string) => {
      if (!item || feedback[item.questionId]) return
      const multi = item.question.type === 'MultipleChoice'
      setSelections((prev) => {
        const current = prev[item.questionId] ?? []
        const next = multi
          ? current.includes(label)
            ? current.filter((l) => l !== label)
            : [...current, label].sort()
          : [label]
        return { ...prev, [item.questionId]: next }
      })
    },
    [item, feedback],
  )

  const saveAnswer = useCallback(async () => {
    if (!sessionId || !item || currentSelection.length === 0 || saving) return
    setSaving(true)
    setError(null)
    try {
      const response = await api.exams.answer(sessionId, {
        questionId: item.questionId,
        selectedLabels: currentSelection,
        secondsSpent: Math.round((Date.now() - questionStart.current) / 1000),
      })
      if (response.revealAnswer) {
        setFeedback((prev) => ({ ...prev, [item.questionId]: response }))
      } else if (index < total - 1) {
        setIndex((i) => i + 1)
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not save the answer')
    } finally {
      setSaving(false)
    }
  }, [sessionId, item, currentSelection, saving, index, total])

  // Keyboard shortcuts: A–E to pick, Enter to check/advance, arrows to move.
  useEffect(() => {
    if (!item || confirmOpen) return
    const onKey = (e: KeyboardEvent) => {
      if (e.ctrlKey || e.metaKey || e.altKey) return
      const tag = (e.target as HTMLElement | null)?.tagName
      if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return

      const labels = item.question.options.map((o) => o.label)
      const key = e.key.toUpperCase()

      if (labels.includes(key)) {
        e.preventDefault()
        toggleOption(key)
        return
      }
      if (e.key === 'Enter') {
        e.preventDefault()
        if (revealed) {
          if (!isLast) setIndex((i) => i + 1)
        } else {
          void saveAnswer()
        }
        return
      }
      if (e.key === 'ArrowRight' && index < total - 1) setIndex((i) => i + 1)
      if (e.key === 'ArrowLeft' && index > 0) setIndex((i) => i - 1)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [item, revealed, isLast, index, total, toggleOption, saveAnswer, confirmOpen])

  if (error && !session) return <Banner kind="error">{error}</Banner>
  if (!session) {
    return (
      <div className="card">
        <Skeleton lines={6} />
      </div>
    )
  }

  if (session.completedAt) {
    return (
      <div className="card">
        <h2>This session is already submitted</h2>
        <p className="muted">Open the result to review every question and its explanation.</p>
        <div className="button-row">
          <Link to={`/result/${session.id}`} className="button-link">
            View result →
          </Link>
        </div>
      </div>
    )
  }

  const progress = total === 0 ? 0 : (answeredCount / total) * 100

  return (
    <div className="stack exam-focus">
      <div className="exam-bar">
        <span className="chip accent">{session.certificationCode}</span>
        <span className={`chip${session.mode === 'Review' ? ' review' : ''}`}>{MODE_LABEL[session.mode]}</span>

        <div className="exam-progress">
          <div className="counts">
            <span>
              Question {index + 1} of {total}
            </span>
            <span>{answeredCount} answered</span>
          </div>
          <div className="meter" role="img" aria-label={`${answeredCount} of ${total} answered`}>
            <span className="meter-fill" style={{ width: `${progress}%` }} />
          </div>
        </div>

        {secondsLeft !== null && (
          <div className={`timer${secondsLeft < 300 ? ' urgent' : ''}`}>
            {String(Math.floor(secondsLeft / 60)).padStart(2, '0')}:
            {String(secondsLeft % 60).padStart(2, '0')}
          </div>
        )}
      </div>

      {error && <Banner kind="error">{error}</Banner>}

      {item && (
        <section className="question">
          <div className="question-head">
            <DifficultyBadge value={item.question.difficulty} />
            {item.question.domainName && <span className="chip">{item.question.domainName}</span>}
            {item.question.type === 'MultipleChoice' && (
              <span className="chip accent">Select all that apply</span>
            )}
          </div>

          <p className="stem">{item.question.stem}</p>

          <ul className="options selectable">
            {item.question.options.map((o) => {
              const picked = currentSelection.includes(o.label)
              const isCorrect = revealed?.correctLabels.includes(o.label)
              const classes = [
                revealed ? 'locked' : '',
                picked ? 'picked' : '',
                revealed && isCorrect ? 'correct' : '',
                revealed && picked && !isCorrect ? 'wrong' : '',
              ]
                .filter(Boolean)
                .join(' ')
              return (
                <li key={o.label} className={classes}>
                  <label>
                    <input
                      type={item.question.type === 'MultipleChoice' ? 'checkbox' : 'radio'}
                      name={`q-${item.questionId}`}
                      checked={picked}
                      disabled={revealed !== undefined}
                      onChange={() => toggleOption(o.label)}
                    />
                    <span className="opt-label" aria-hidden="true">
                      {o.label}
                    </span>
                    <span>{o.text}</span>
                  </label>
                </li>
              )
            })}
          </ul>

          {revealed && (
            <div className={`feedback${revealed.isCorrect ? '' : ' wrong'}`}>
              <span className="verdict">
                {revealed.isCorrect ? '✓ Correct' : `✕ Incorrect — answer: ${revealed.correctLabels.join(', ')}`}
              </span>
              <p>{revealed.explanation}</p>
            </div>
          )}

          <div className="exam-actions">
            <button type="button" className="ghost" disabled={index === 0} onClick={() => setIndex((i) => i - 1)}>
              ← Previous
            </button>

            {!revealed && (
              <button
                type="button"
                className="primary"
                disabled={saving || currentSelection.length === 0}
                onClick={saveAnswer}
              >
                {saving ? 'Saving…' : session.mode === 'Exam' ? 'Save answer' : 'Check answer'}
              </button>
            )}

            {(revealed || session.mode === 'Exam') && !isLast && (
              <button type="button" className={revealed ? 'primary' : ''} onClick={() => setIndex((i) => i + 1)}>
                Next question →
              </button>
            )}

            <span className="spacer" />
            <span className="kbd-hint">
              <kbd>A</kbd>–<kbd>{item.question.options.at(-1)?.label ?? 'D'}</kbd> pick · <kbd>Enter</kbd>{' '}
              {revealed ? 'next' : 'check'} · <kbd>←</kbd> <kbd>→</kbd> move
            </span>
            <button type="button" disabled={submitting} onClick={requestSubmit}>
              {submitting ? 'Submitting…' : 'Finish & score'}
            </button>
          </div>
        </section>
      )}

      <div className="card">
        <span className="eyebrow">Jump to question</span>
        <div className="jump-grid">
          {session.items.map((it, i) => (
            <button
              key={it.questionId}
              type="button"
              className={`jump-dot${i === index ? ' current' : ''}${selections[it.questionId] ? ' answered' : ''}`}
              onClick={() => setIndex(i)}
              aria-label={`Question ${i + 1}${selections[it.questionId] ? ', answered' : ', unanswered'}`}
              aria-current={i === index}
            >
              {i + 1}
            </button>
          ))}
        </div>
      </div>

      {confirmOpen && (
        <ConfirmDialog
          title="Submit with unanswered questions?"
          body={`${total - answeredCount} of ${total} question${total - answeredCount === 1 ? ' is' : 's are'} still unanswered. They will be scored as incorrect.`}
          confirmLabel="Submit anyway"
          onConfirm={() => {
            setConfirmOpen(false)
            void submit()
          }}
          onCancel={() => setConfirmOpen(false)}
        />
      )}
    </div>
  )
}
