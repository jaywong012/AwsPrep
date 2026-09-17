import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError, api } from '../api'
import type { LessonDetail, Question } from '../api/types'
import { Banner, ServiceIcon, Skeleton, Spinner } from '../components/Ui'
import { LessonTutor } from '../components/LessonTutor'
import { Link, useRouter } from '../router'
import { invalidateLessonCache } from './lessonCache'
import { LessonPrintDocument } from '../components/LessonPrint'
import { printRenderedDocument } from '../components/printDocument'

/**
 * One lesson.
 *
 * The page is deliberately in two halves. Everything above "In practice" is seeded and verified -
 * the purpose, the pricing model, the link to the official AWS page - and everything below it is
 * generated on first open and cached. The split is shown to the learner rather than hidden, so
 * they know which sentences to trust absolutely and which to check against the docs link.
 */
export default function Lesson({ code, slug }: { code?: string; slug?: string }) {
  const { navigate } = useRouter()

  const [lesson, setLesson] = useState<LessonDetail | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [writingNotes, setWritingNotes] = useState(false)
  const [savingProgress, setSavingProgress] = useState(false)
  const [adminRequired, setAdminRequired] = useState(false)

  // Slugs this mount has already asked to generate. A ref, not state, because it must be read
  // by the effect in the same tick it is written: React's StrictMode runs effects twice in
  // development, and a second POST would be a second provider call for the same lesson.
  const requested = useRef(new Set<string>())

  const load = useCallback(async () => {
    if (!code || !slug) return
    setLesson(null)
    setError(null)
    try {
      setLesson(await api.lessons.get(code, slug))
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load this lesson')
    }
  }, [code, slug])

  useEffect(() => {
    void load()
  }, [load])

  // Left/right arrows walk the curriculum. Ignored while typing or with a modifier held, so
  // they never steal a keystroke from a field or from the browser's own back/forward.
  useEffect(() => {
    if (!lesson || !code) return

    const onKey = (e: KeyboardEvent) => {
      if (e.altKey || e.ctrlKey || e.metaKey || e.shiftKey) return

      const el = e.target as HTMLElement | null
      if (el && (el.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(el.tagName))) return

      const to = e.key === 'ArrowLeft' ? lesson.previous : e.key === 'ArrowRight' ? lesson.next : null
      if (!to) return

      e.preventDefault()
      navigate(`/lessons/${code}/${to.slug}`)
    }

    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [lesson, code, navigate])

  const writeNotes = useCallback(
    async (replaceExisting = false) => {
      if (!code || !slug) return

      requested.current.add(slug)
      setWritingNotes(true)
      setError(null)
      setAdminRequired(false)

      try {
        setLesson(
          replaceExisting
            ? await api.lessons.rewriteNotes(code, slug)
            : await api.lessons.writeNotes(code, slug),
        )

        // The list marks a lesson "Notes on first open" until they exist, and they now do.
        invalidateLessonCache(code)
      } catch (e) {
        // Rewriting replaces notes every learner shares, so the API gates it behind the operator
        // key in production, exactly as deleting a question is gated.
        if (e instanceof ApiError && (e.status === 403 || e.status === 503)) setAdminRequired(true)
        setError(e instanceof Error ? e.message : 'Could not write the notes')
      } finally {
        setWritingNotes(false)
      }
    },
    [code, slug],
  )

  // The read above never generates, so the verified facts paint immediately and the notes are
  // fetched after. Guarded by the ref so one mount asks at most once per slug: without it a
  // StrictMode double-invoke, or a failed attempt updating state, would spend a second call.
  useEffect(() => {
    if (!slug || !lesson || lesson.body || !lesson.aiConfigured) return
    if (requested.current.has(slug)) return

    void writeNotes()
  }, [slug, lesson, writeNotes])

  async function toggleDone() {
    if (!code || !slug || !lesson) return
    setSavingProgress(true)
    try {
      const result = await api.lessons.setProgress(code, slug, !lesson.completed)
      setLesson({ ...lesson, completed: result.completed })

      // The lessons list is cached for the tab, and this is the one thing on it that just
      // changed - without dropping it, going back shows this lesson as still not done.
      invalidateLessonCache(code)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not save your progress')
    } finally {
      setSavingProgress(false)
    }
  }

  if (!code || !slug) {
    return (
      <div className="card">
        <h2>No lesson selected</h2>
        <div className="button-row">
          <Link to="/lessons" className="button-link">
            Back to lessons →
          </Link>
        </div>
      </div>
    )
  }

  if (error && !lesson) {
    return (
      <div className="stack">
        <Banner kind="error">{error}</Banner>
        <div className="button-row">
          <button type="button" className="ghost" onClick={() => navigate('/lessons')}>
            ← Back to lessons
          </button>
          <button type="button" className="primary" onClick={() => void load()}>
            Try again
          </button>
        </div>
      </div>
    )
  }

  if (!lesson) {
    return (
      <div className="stack">
        <Spinner label="Opening the lesson — the first visit writes the notes, which takes a few seconds…" />
        <Skeleton lines={10} />
      </div>
    )
  }

  return (
    <div className="stack lesson">
      <div className="lesson-head">
        <button type="button" className="ghost small" onClick={() => navigate('/lessons')}>
          ← All lessons
        </button>

        <div className="lesson-title-row">
          <ServiceIcon slug={lesson.slug} title={lesson.title} size={56} />
          <h1>{lesson.title}</h1>
        </div>

        <div className="lesson-head-meta">
          <span className="chip">{lesson.category}</span>
          {lesson.domainName && (
            <span className="chip subtle">
              {lesson.domainName}
              {lesson.domainWeightPercent !== null && ` · ${lesson.domainWeightPercent}% of the exam`}
            </span>
          )}
          {lesson.mastery.accuracyPercent !== null && (
            <span className={`mastery ${lesson.mastery.status.toLowerCase()}`}>
              {lesson.mastery.accuracyPercent}% on your last {lesson.mastery.answered}{' '}
              {lesson.mastery.basis === 'Domain' ? 'questions in this domain' : 'questions on this topic'}
            </span>
          )}
        </div>
      </div>

      {error && <Banner kind="error">{error}</Banner>}
      {lesson.warning && <Banner kind="warning">{lesson.warning}</Banner>}
      {!lesson.body && !lesson.aiConfigured && (
        <Banner kind="info">
          No AI provider is configured, so this lesson shows its verified facts and official
          documentation links only. Set <code>Ai:Provider</code> and <code>Ai:ApiKey</code> to add
          the worked notes.
        </Banner>
      )}

      {/* Seeded half: hand-written and stable, never model-generated. The URLs are checked to
          resolve; the prose around them is not a quotation of those pages. */}
      <section className="card lesson-facts">
        <h2 className="section-title">The facts</h2>

        <dl className="fact-grid">
          <div>
            <dt>What it is for</dt>
            <dd>{lesson.purpose}</dd>
          </div>
          <div>
            <dt>How it is charged</dt>
            <dd>{lesson.pricingModel}</dd>
          </div>
        </dl>

        <div className="button-row">
          <a className="button-link" href={lesson.docsUrl} target="_blank" rel="noreferrer noopener">
            Official documentation ↗
          </a>
          {lesson.pricingUrl && (
            <a className="button-link ghost" href={lesson.pricingUrl} target="_blank" rel="noreferrer noopener">
              Official pricing ↗
            </a>
          )}
        </div>
      </section>

      {/* Generated half: written once per topic and cached. */}
      {lesson.body && (
        <section className="card lesson-body">
          <h2 className="section-title">In practice</h2>

          <p className="lesson-overview">{lesson.body.overview}</p>

          <Section title="When you would use it" bullets={lesson.body.useCases} />

          <div className="lesson-block">
            <h3>What it costs you in practice</h3>
            <p>{lesson.body.costNotes}</p>
          </div>

          <Section title="What it works with" bullets={lesson.body.integrations} />

          <div className="lesson-block">
            <h3>A real case</h3>
            <p>{lesson.body.realWorldExample}</p>
          </div>

          <Section title="Exam traps" bullets={lesson.body.examTraps} tone="traps" />

          <div className="button-row end">
            <button
              type="button"
              className="ghost small"
              disabled={writingNotes}
              onClick={() => void writeNotes(true)}
            >
              {writingNotes ? 'Rewriting…' : 'Rewrite these notes'}
            </button>
            {adminRequired && (
              <span className="muted small">
                Rewriting replaces these notes for everyone, so it needs the operator key — set it
                on the Bank page.
              </span>
            )}
          </div>
        </section>
      )}

      {/* The notes are written by a separate call, so this section stands in for them while that
          call is in flight and offers the retry when it failed - a free-tier provider shedding
          load is the common case, and a reload should not be the only way back. */}
      {!lesson.body && lesson.aiConfigured && (
        <section className="card">
          {writingNotes ? (
            <>
              <Spinner label="Writing the notes for this topic — this happens once, then it is cached…" />
              <Skeleton lines={8} />
            </>
          ) : (
            <div className="button-row">
              <button type="button" className="primary" onClick={() => void writeNotes()}>
                Write the notes for this topic
              </button>
            </div>
          )}
        </section>
      )}


      {lesson.practiceQuestions.length > 0 && (
        <section className="card">
          <h2 className="section-title">
            Check yourself
            <span className="section-note">
              {lesson.practiceQuestions.length} question
              {lesson.practiceQuestions.length === 1 ? '' : 's'} from your bank on this topic
            </span>
          </h2>

          <ol className="question-list">
            {lesson.practiceQuestions.map((q) => (
              <PracticeQuestion key={q.id} question={q} />
            ))}
          </ol>
        </section>
      )}

      {/* Curriculum order, so working straight through the syllabus needs no trip back to the
          list. The list's own order is personalised and shifts as you answer questions. */}
      {(lesson.previous || lesson.next) && (
        <nav className="lesson-steps" aria-label="Curriculum">
          {lesson.previous ? (
            <Link to={`/lessons/${code}/${lesson.previous.slug}`} className="lesson-step prev">
              <span className="lesson-step-dir">← Previous</span>
              <span className="lesson-step-title">{lesson.previous.title}</span>
            </Link>
          ) : (
            <span />
          )}
          {lesson.next && (
            <Link to={`/lessons/${code}/${lesson.next.slug}`} className="lesson-step next">
              <span className="lesson-step-dir">Next →</span>
              <span className="lesson-step-title">{lesson.next.title}</span>
            </Link>
          )}
        </nav>
      )}

      <div className="lesson-foot">
        <button
          type="button"
          className={lesson.completed ? 'ghost' : 'primary'}
          disabled={savingProgress}
          onClick={() => void toggleDone()}
        >
          {savingProgress
            ? 'Saving…'
            : lesson.completed
              ? '✓ Done — mark as not done'
              : 'Mark this lesson done'}
        </button>

        <button type="button" className="ghost" onClick={() => void printRenderedDocument()}>
          Export PDF
        </button>

        {lesson.related.length > 0 && (
          <nav className="lesson-related" aria-label="Related lessons">
            <span className="muted small">Related:</span>
            {lesson.related.map((r) => (
              <Link key={r.slug} to={`/lessons/${code}/${r.slug}`}>
                {r.title}
              </Link>
            ))}
          </nav>
        )}
      </div>

      <LessonTutor
        code={code}
        slug={lesson.slug}
        title={lesson.title}
        available={lesson.aiConfigured}
      />

      {/* Hidden until the browser prints. Rendered from the lesson already on screen, so the
          export is whatever you are looking at - no second fetch, nothing to go stale. */}
      <LessonPrintDocument
        lessons={[lesson]}
        title={lesson.title}
        subtitle={`${code} · ${lesson.category}`}
      />
    </div>
  )
}

function Section({ title, bullets, tone }: { title: string; bullets: string[]; tone?: string }) {
  if (bullets.length === 0) return null

  return (
    <div className={`lesson-block${tone ? ` ${tone}` : ''}`}>
      <h3>{title}</h3>
      <ul className="lesson-bullets">
        {bullets.map((b, i) => (
          <li key={i}>{b}</li>
        ))}
      </ul>
    </div>
  )
}

/**
 * One question from the bank, answerable in place.
 *
 * It used to render the options as plain text with a "Show answer" button, which made the block a
 * reading exercise: the only thing you could do was be told. Recall you have actually attempted is
 * what makes a self-test worth having, so the options are now real inputs and you commit to an
 * answer before anything is revealed.
 *
 * Deliberately local: this scores against the options already in the payload and posts nothing, so
 * it does not touch exam history or mastery. Checking yourself mid-lesson should not quietly move
 * the readiness numbers that the exam sessions are supposed to measure.
 */
function PracticeQuestion({ question }: { question: Question }) {
  const [picked, setPicked] = useState<string[]>([])
  const [checked, setChecked] = useState(false)

  const multi = question.type === 'MultipleChoice'
  const correctLabels = question.options.filter((o) => o.isCorrect).map((o) => o.label)
  const isCorrect =
    picked.length === correctLabels.length && correctLabels.every((l) => picked.includes(l))

  function toggle(label: string) {
    if (checked) return
    setPicked((prev) =>
      multi
        ? prev.includes(label)
          ? prev.filter((l) => l !== label)
          : [...prev, label].sort()
        : [label],
    )
  }

  function reset() {
    setPicked([])
    setChecked(false)
  }

  return (
    <li className="question">
      {multi && (
        <div className="question-head">
          <span className="chip accent">Select all that apply</span>
        </div>
      )}

      <p className="stem">{question.stem}</p>

      <ul className="options selectable">
        {question.options.map((o) => {
          const chosen = picked.includes(o.label)
          const classes = [
            checked ? 'locked' : '',
            chosen ? 'picked' : '',
            checked && o.isCorrect ? 'correct' : '',
            checked && chosen && !o.isCorrect ? 'wrong' : '',
          ]
            .filter(Boolean)
            .join(' ')

          return (
            <li key={o.label} className={classes}>
              <label>
                <input
                  type={multi ? 'checkbox' : 'radio'}
                  // Scoped to the question, so two questions on the page cannot share a radio group.
                  name={`practice-${question.id}`}
                  checked={chosen}
                  disabled={checked}
                  onChange={() => toggle(o.label)}
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

      {checked ? (
        <>
          <div className={`feedback${isCorrect ? '' : ' wrong'}`}>
            <span className="verdict">
              {isCorrect ? '✓ Correct' : `✕ Incorrect — answer: ${correctLabels.join(', ')}`}
            </span>
            {question.explanation && <p>{question.explanation}</p>}
          </div>
          <button type="button" className="small" onClick={reset}>
            Try again
          </button>
        </>
      ) : (
        <div className="button-row">
          <button
            type="button"
            className="small primary"
            disabled={picked.length === 0}
            onClick={() => setChecked(true)}
          >
            Check answer
          </button>
          {/* Still reachable without attempting: revising a topic you have already learned is a
              different job from testing yourself on it. */}
          <button type="button" className="small" onClick={() => setChecked(true)} hidden={picked.length > 0}>
            Show answer
          </button>
        </div>
      )}
    </li>
  )
}
