import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'
import { ApiError, api } from '../api'
import type { TutorTurn } from '../api/types'
import { Banner, Spinner } from './Ui'

/** Where the panel sits, as a top-left offset in viewport pixels. */
interface Position {
  x: number
  y: number
}

const POSITION_STORAGE = 'awscert.tutorPosition'
const OPEN_STORAGE = 'awscert.tutorOpen'
const FALLBACK_SIZE = { width: 380, height: 520 }
const EDGE_MARGIN = 20

/**
 * Whether the panel was left open. Moving between lessons unmounts this component while the next
 * lesson loads, so without remembering this the panel would slam shut on every prev/next - which
 * is exactly when someone is most likely to still be asking questions.
 */
function readStoredOpen(): boolean {
  try {
    return localStorage.getItem(OPEN_STORAGE) === '1'
  } catch {
    return false
  }
}

function writeStoredOpen(open: boolean) {
  try {
    localStorage.setItem(OPEN_STORAGE, open ? '1' : '0')
  } catch {
    // It will simply start closed next time.
  }
}

/** Storage throws in private mode and when site data is blocked, so every access is guarded. */
function readStoredPosition(): Position | null {
  try {
    const raw = localStorage.getItem(POSITION_STORAGE)
    if (!raw) return null
    const parsed = JSON.parse(raw) as Position
    return typeof parsed?.x === 'number' && typeof parsed?.y === 'number' ? parsed : null
  } catch {
    return null
  }
}

/**
 * The panel shrinks to its content, so its height is whatever the current conversation makes it -
 * short when empty, tall after a few answers. Positioning against a guessed height would leave it
 * hanging in mid-air, so the live element is measured instead and the constants are only a
 * fallback for the frame before it exists.
 */
function measure(el: HTMLElement | null) {
  return el ? { width: el.offsetWidth, height: el.offsetHeight } : FALLBACK_SIZE
}

/** Keeps the panel fully on screen - after a drag, after a resize, and on a smaller monitor. */
function clampToViewport(position: Position, size = FALLBACK_SIZE): Position {
  const maxX = Math.max(0, window.innerWidth - size.width - 8)
  const maxY = Math.max(0, window.innerHeight - size.height - 8)
  return {
    x: Math.min(Math.max(0, position.x), maxX),
    y: Math.min(Math.max(0, position.y), maxY),
  }
}

function defaultPosition(size = FALLBACK_SIZE): Position {
  return clampToViewport(
    {
      x: window.innerWidth - size.width - EDGE_MARGIN,
      y: window.innerHeight - size.height - EDGE_MARGIN,
    },
    size,
  )
}

/**
 * Ask-a-question widget for the lesson you are reading.
 *
 * Floating rather than docked in a column: the lesson is the page, and a panel that permanently
 * takes a third of the width makes the notes worse for the whole time you are not asking anything.
 * Closed it is one button in the corner; open it is a panel you can drag wherever it is least in
 * the way, and where you put it is remembered.
 *
 * The server already has the lesson's facts and notes, so questions can be asked the way you would
 * ask a person sitting next to you - "why would I use this instead of EFS?" needs no explanation
 * of what "this" is.
 */
export function LessonTutor({
  code,
  slug,
  title,
  available,
}: {
  code: string
  slug: string
  title: string
  available: boolean
}) {
  const [open, setOpenState] = useState(readStoredOpen)

  const setOpen = useCallback((next: boolean) => {
    setOpenState(next)
    writeStoredOpen(next)
  }, [])
  const [position, setPosition] = useState<Position | null>(null)
  const [dragging, setDragging] = useState(false)

  const [turns, setTurns] = useState<TutorTurn[]>([])
  const [question, setQuestion] = useState('')
  const [asking, setAsking] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const panelRef = useRef<HTMLElement>(null)
  // The handlers read the ref, not the state: a pointermove can arrive in the same tick as the
  // pointerdown, before a state update has rendered, and reading stale state there would swallow
  // the first movement of every drag. The state exists only to put the class on the panel.
  const draggingRef = useRef(false)
  const transcriptRef = useRef<HTMLDivElement>(null)
  const dragOffset = useRef<Position>({ x: 0, y: 0 })

  // Resolved when the panel opens rather than in useState: the corner it parks in depends on the
  // window size and on the panel's own height, neither of which exists at first render. A layout
  // effect runs before paint, so the panel is never seen in the wrong place.
  useLayoutEffect(() => {
    if (!open) return

    const size = measure(panelRef.current)
    setPosition(clampToViewport(readStoredPosition() ?? defaultPosition(size), size))
  }, [open])

  // Two things can push the panel off screen without anyone dragging it: a window that shrinks
  // below where it sits, and the panel growing taller as answers are added under a position that
  // was only just clear of the bottom edge. Both re-clamp against the panel's current size.
  useEffect(() => {
    const reclamp = () =>
      setPosition((p) => (p ? clampToViewport(p, measure(panelRef.current)) : p))

    window.addEventListener('resize', reclamp)

    const observer = new ResizeObserver(reclamp)
    if (panelRef.current) observer.observe(panelRef.current)

    return () => {
      window.removeEventListener('resize', reclamp)
      observer.disconnect()
    }
  }, [open])

  // A new lesson is a new conversation. Carrying the old one over would leave the model
  // answering about the previous topic.
  useEffect(() => {
    setTurns([])
    setQuestion('')
    setError(null)
  }, [slug])

  useEffect(() => {
    transcriptRef.current?.scrollTo({ top: transcriptRef.current.scrollHeight, behavior: 'smooth' })
  }, [turns, asking])

  // Escape closes the panel, the convention for any floating surface.
  useEffect(() => {
    if (!open) return

    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false)
    }

    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open, setOpen])

  const onDragStart = useCallback(
    (e: React.PointerEvent<HTMLDivElement>) => {
      // Only the bar itself drags: a pointerdown on the close button must still close it.
      if ((e.target as HTMLElement).closest('button')) return
      if (!position) return

      dragOffset.current = { x: e.clientX - position.x, y: e.clientY - position.y }
      draggingRef.current = true
      setDragging(true)

      // Capture keeps the moves coming to the bar even when the pointer outruns the panel. It is
      // an optimisation, not a requirement, so a browser that refuses the capture still drags.
      try {
        e.currentTarget.setPointerCapture(e.pointerId)
      } catch {
        // Fall back to plain bubbling moves.
      }
    },
    [position],
  )

  const onDragMove = useCallback(
    (e: React.PointerEvent<HTMLDivElement>) => {
      if (!draggingRef.current) return

      setPosition(
        clampToViewport(
          { x: e.clientX - dragOffset.current.x, y: e.clientY - dragOffset.current.y },
          measure(panelRef.current),
        ),
      )
    },
    [],
  )

  const onDragEnd = useCallback((e: React.PointerEvent<HTMLDivElement>) => {
    if (!draggingRef.current) return

    draggingRef.current = false
    setDragging(false)

    // Persisted from inside the updater so it always writes the position the last move produced,
    // rather than whatever this callback happened to close over. Remembering where it was put is
    // the point of being draggable at all.
    setPosition((current) => {
      try {
        if (current) localStorage.setItem(POSITION_STORAGE, JSON.stringify(current))
      } catch {
        // It will simply reopen in the corner next time.
      }
      return current
    })

    try {
      e.currentTarget.releasePointerCapture(e.pointerId)
    } catch {
      // Nothing was captured, or the pointer is already gone.
    }
  }, [])

  async function ask(text: string) {
    const trimmed = text.trim()
    if (!trimmed || asking) return

    setAsking(true)
    setError(null)
    setQuestion('')

    try {
      const answer = await api.lessons.ask(code, slug, trimmed, turns)
      setTurns((prev) => [...prev, { question: trimmed, answer: answer.answer }])
    } catch (e) {
      if (e instanceof ApiError && e.status === 429) {
        setError('You have asked a lot of questions in the last hour. Wait a few minutes and try again.')
      } else {
        setError(e instanceof Error ? e.message : 'The tutor could not answer that')
      }
      // Put the question back so a failure does not lose what they typed.
      setQuestion(trimmed)
    } finally {
      setAsking(false)
    }
  }

  // No provider configured means no tutor at all - a launcher that only ever errors is worse
  // than no launcher.
  if (!available) return null

  if (!open) {
    return (
      <button
        type="button"
        className="tutor-launcher"
        onClick={() => setOpen(true)}
        title={`Ask about ${title}`}
        aria-label={`Ask a question about ${title}`}
      >
        <span aria-hidden="true">?</span>
        <span className="tutor-launcher-text">Ask</span>
      </button>
    )
  }

  return (
    <aside
      ref={panelRef}
      className={`tutor-panel${dragging ? ' dragging' : ''}`}
      style={position ? { left: position.x, top: position.y } : { visibility: 'hidden' }}
      role="dialog"
      aria-label={`Ask about ${title}`}
    >
      {/* The whole bar is the drag handle, so there is no thin target to hunt for. */}
      <div
        className="tutor-bar"
        onPointerDown={onDragStart}
        onPointerMove={onDragMove}
        onPointerUp={onDragEnd}
        onPointerCancel={onDragEnd}
      >
        <div className="tutor-bar-title">
          <strong>Ask about this lesson</strong>
          <span>It already knows what you are reading</span>
        </div>
        <button type="button" className="icon" onClick={() => setOpen(false)} aria-label="Close">
          ✕
        </button>
      </div>

      <div className="tutor-transcript" ref={transcriptRef}>
        {turns.length === 0 && !asking && (
          <p className="muted small tutor-empty">
            Ask anything about {title}. You can say “this” — it has the page in front of it.
          </p>
        )}

        {turns.map((turn, i) => (
          <div key={i} className="tutor-turn">
            <p className="tutor-question">{turn.question}</p>
            <p className="tutor-answer">{turn.answer}</p>
          </div>
        ))}

        {asking && <Spinner label="Thinking…" />}

        {/* An answer here is not a verified fact, so the documentation link stays one line away
            from every one of them. */}
        {turns.length > 0 && !asking && (
          <p className="tutor-by">Check anything surprising against the documentation link.</p>
        )}
      </div>

      {error && <Banner kind="error">{error}</Banner>}

      <form
        className="tutor-form"
        onSubmit={(e) => {
          e.preventDefault()
          void ask(question)
        }}
      >
        <label className="sr-only" htmlFor="tutor-input">
          Your question about {title}
        </label>
        <textarea
          id="tutor-input"
          rows={2}
          maxLength={500}
          placeholder="Why would I use this instead of…?"
          value={question}
          disabled={asking}
          onChange={(e) => setQuestion(e.target.value)}
          onKeyDown={(e) => {
            // Enter sends, Shift+Enter makes a new line — the convention everywhere else.
            if (e.key === 'Enter' && !e.shiftKey) {
              e.preventDefault()
              void ask(question)
            }
          }}
        />
        <button type="submit" className="primary" disabled={asking || question.trim().length === 0}>
          {asking ? 'Asking…' : 'Ask'}
        </button>
      </form>
    </aside>
  )
}
