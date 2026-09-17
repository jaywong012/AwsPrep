import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { api } from '../api'
import type { LessonDetail, LessonSummary, LessonsResponse, MasteryStatus } from '../api/types'
import type { useCertifications } from '../hooks/useCertifications'
import { Banner, CertPicker, EmptyState, Meter, ServiceIcon, Skeleton } from '../components/Ui'
import { Link } from '../router'
import { getCachedLessons, invalidateLessonCache, setCachedLessons } from './lessonCache'
import { LessonPrintDocument } from '../components/LessonPrint'
import { printRenderedDocument } from '../components/printDocument'

type Certs = ReturnType<typeof useCertifications>

/**
 * How the server ranked each topic, said in the learner's terms. The order of the list is
 * computed from their own answer history, so the list has to explain itself or it looks arbitrary.
 */
const MASTERY_UI: Record<MasteryStatus, { label: string; hint: string; tone: string }> = {
  Weak: { label: 'Weak', hint: 'You are getting these wrong — start here', tone: 'weak' },
  Learning: { label: 'Learning', hint: 'Getting there, not solid yet', tone: 'learning' },
  Untested: { label: 'Not tested', hint: 'No questions answered on this yet', tone: 'untested' },
  Strong: { label: 'Strong', hint: 'You answer these reliably', tone: 'strong' },
}

/**
 * The list is filtered by what a lesson teaches, not by how well you are doing at it.
 *
 * The exam rewards two habits that do not mix well in one sitting: recognising which AWS service
 * solves a described problem is recall and drills well in bulk, while applying a principle is
 * reasoning and wants thinking time. Commercial is kept apart from both because support plans and
 * purchasing options are neither - they are things you buy, memorised as a table.
 */
type Kind = 'all' | 'Service' | 'Concept' | 'Commercial'

const KINDS: { id: Kind; label: string; hint: string }[] = [
  { id: 'all', label: 'Everything', hint: 'The whole curriculum, in study order' },
  { id: 'Service', label: 'Services', hint: 'Something you deploy, configure or call' },
  { id: 'Concept', label: 'Concepts', hint: 'An idea you apply — nothing to launch' },
  { id: 'Commercial', label: 'Buying & support', hint: 'How you pay for AWS and get help' },
]

const PAGE_SIZE = 10

/**
 * Where the learner was in the list: which kind, which category, whether they were hiding what
 * they had finished, and which page.
 *
 * Kept per certification, because the categories of one are not the categories of another -
 * carrying "Encryption" from CLF-C02 over to AIF-C01 would filter the list down to nothing and
 * look like a broken page. Stored in localStorage rather than sessionStorage so it survives a
 * browser restart, the same way the chosen certification already does.
 */
interface ViewState {
  kind: Kind
  category: string
  notDoneOnly: boolean
  page: number
}

const VIEW_KEY = 'awscert.lessons.view'

const DEFAULT_VIEW: ViewState = { kind: 'all', category: '', notDoneOnly: false, page: 1 }

function readAllViews(): Record<string, Partial<ViewState>> {
  try {
    const raw = JSON.parse(localStorage.getItem(VIEW_KEY) ?? '{}') as unknown
    return raw && typeof raw === 'object' ? (raw as Record<string, Partial<ViewState>>) : {}
  } catch {
    return {}
  }
}

function readViewState(code: string | undefined): ViewState {
  if (!code) return DEFAULT_VIEW
  const saved = readAllViews()[code] ?? {}
  return {
    kind: saved.kind ?? DEFAULT_VIEW.kind,
    category: saved.category ?? DEFAULT_VIEW.category,
    notDoneOnly: saved.notDoneOnly ?? DEFAULT_VIEW.notDoneOnly,
    page: saved.page ?? DEFAULT_VIEW.page,
  }
}

function writeViewState(code: string, view: ViewState) {
  try {
    localStorage.setItem(VIEW_KEY, JSON.stringify({ ...readAllViews(), [code]: view }))
  } catch {
    // Storage blocked or full: the list just opens at the top next time. Nothing else uses this.
  }
}

export default function Lessons({ certs }: { certs: Certs }) {
  const { certifications, selectedCode, select } = certs

  const [data, setData] = useState<LessonsResponse | null>(
    () => (selectedCode ? (getCachedLessons(selectedCode) ?? null) : null),
  )
  const [error, setError] = useState<string | null>(null)

  const [view, setView] = useState<ViewState>(() => readViewState(selectedCode))
  const { kind, category, notDoneOnly } = view

  const setKind = useCallback((next: Kind) => setView((v) => ({ ...v, kind: next, page: 1 })), [])
  const setCategory = useCallback(
    (next: string) => setView((v) => ({ ...v, category: next, page: 1 })),
    [],
  )
  const setNotDoneOnly = useCallback(
    (next: boolean) => setView((v) => ({ ...v, notDoneOnly: next, page: 1 })),
    [],
  )
  const setPage = useCallback((next: number) => setView((v) => ({ ...v, page: next })), [])

  const load = useCallback(async () => {
    if (!selectedCode) return

    const cached = getCachedLessons(selectedCode)
    if (cached) {
      setData(cached)
      setError(null)
      return
    }

    setData(null)
    setError(null)
    try {
      const fresh = await api.lessons.list(selectedCode)
      setCachedLessons(selectedCode, fresh)
      setData(fresh)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load lessons')
    }
  }, [selectedCode])

  useEffect(() => {
    void load()
  }, [load])

  const refresh = useCallback(() => {
    if (selectedCode) invalidateLessonCache(selectedCode)
    void load()
  }, [selectedCode, load])

  const [exportDocs, setExportDocs] = useState<LessonDetail[]>([])
  const [exportProgress, setExportProgress] = useState<{ done: number; total: number } | null>(null)

  // The server has already ranked the list, so filtering only ever removes rows - the order a
  // learner sees is still "what to study next" whichever filters are on.
  const visible = useMemo(() => {
    let topics = data?.topics ?? []
    if (kind !== 'all') topics = topics.filter((t) => t.kind === kind)
    if (category) topics = topics.filter((t) => t.category === category)
    if (notDoneOnly) topics = topics.filter((t) => !t.completed)
    return topics
  }, [data, kind, category, notDoneOnly])

  /**
   * How many rows each filter choice would actually leave.
   *
   * Every count is taken with the OTHER filters already applied, so the number beside a choice
   * is the number of lessons you get when you pick it. A count of the whole curriculum would
   * promise 22 concepts and then hand you an empty list, because the category you had selected
   * has none of them.
   */
  const counts = useMemo(() => {
    const topics = data?.topics ?? []
    const byKind = (t: LessonSummary) => kind === 'all' || t.kind === kind
    const byCategory = (t: LessonSummary) => !category || t.category === category
    const notDone = (t: LessonSummary) => !notDoneOnly || !t.completed

    const perKind = new Map<Kind, number>()
    for (const k of KINDS) {
      perKind.set(
        k.id,
        topics.filter((t) => (k.id === 'all' || t.kind === k.id) && byCategory(t) && notDone(t)).length,
      )
    }

    const perCategory = new Map<string, number>()
    for (const t of topics) {
      if (!byKind(t) || !notDone(t)) continue
      perCategory.set(t.category, (perCategory.get(t.category) ?? 0) + 1)
    }

    return {
      perKind,
      perCategory,
      allCategories: topics.filter((t) => byKind(t) && notDone(t)).length,
      remaining: topics.filter((t) => byKind(t) && byCategory(t) && !t.completed).length,
    }
  }, [data, kind, category, notDoneOnly])

  // Every category in the curriculum stays listed even when the current filters leave it empty:
  // a category that vanished as you typed would look like a bug, and "(0)" says the same thing
  // more honestly.
  const categories = useMemo(
    () => [...new Set(data?.topics.map((t) => t.category) ?? [])].sort(),
    [data],
  )

  const pageCount = Math.max(1, Math.ceil(visible.length / PAGE_SIZE))

  // A filter that shrinks the list can leave you on a page that no longer exists; land on the
  // last real one rather than on "no lessons here".
  const currentPage = Math.min(view.page, pageCount)

  const pageItems = useMemo(
    () => visible.slice((currentPage - 1) * PAGE_SIZE, currentPage * PAGE_SIZE),
    [visible, currentPage],
  )

  // Switching certification swaps in whatever that one was left on, rather than carrying over a
  // category it may not even have.
  const shownCode = useRef(selectedCode)
  useEffect(() => {
    if (shownCode.current === selectedCode) return
    shownCode.current = selectedCode
    setView(readViewState(selectedCode))
  }, [selectedCode])

  useEffect(() => {
    if (selectedCode) writeViewState(selectedCode, { ...view, page: currentPage })
  }, [selectedCode, view, currentPage])

  /**
   * Exports every lesson the current filters leave — not just the page on screen, because the
   * filters are how a learner says what they want to revise, and paging is incidental to that.
   *
   * The list only carries each topic's summary, so the written notes have to be fetched per
   * lesson. Six at a time: enough to make 100 lessons bearable, few enough that the export does
   * not look like a burst of traffic. A lesson that fails to load is left out rather than
   * failing the whole export, and the cover says how many made it.
   */
  const exportPdf = useCallback(async () => {
    if (!selectedCode || visible.length === 0) return

    const topics = visible
    setExportProgress({ done: 0, total: topics.length })

    const fetched: LessonDetail[] = new Array(topics.length)
    let next = 0

    const worker = async () => {
      while (next < topics.length) {
        const i = next++
        try {
          fetched[i] = await api.lessons.get(selectedCode, topics[i].slug)
        } catch {
          // Left undefined and filtered out below.
        }
        setExportProgress((p) => (p ? { ...p, done: p.done + 1 } : p))
      }
    }

    try {
      await Promise.all(Array.from({ length: Math.min(6, topics.length) }, worker))
      setExportDocs(fetched.filter(Boolean))
      await printRenderedDocument()
    } finally {
      setExportProgress(null)
      // Dropped once the dialog has taken its snapshot, so the DOM does not keep 100 lessons.
      setExportDocs([])
    }
  }, [selectedCode, visible])

  const exportSubtitle = useMemo(() => {
    const parts = [KINDS.find((k) => k.id === kind)?.label ?? 'Everything']
    if (category) parts.push(category)
    if (notDoneOnly) parts.push('not done yet')
    return `${selectedCode} · ${parts.join(' · ')}`
  }, [selectedCode, kind, category, notDoneOnly])

  const progressPercent =
    data && data.totalTopics > 0 ? (100 * data.completedTopics) / data.totalTopics : 0

  return (
    <div className="stack">
      <div className="page-head">
        <h1>Lessons</h1>
        <p>
          What to learn to answer {selectedCode} questions — every service, what it is for, what it
          costs, and what it is confused with.
        </p>
      </div>

      <div className="card">
        <div className="toolbar">
          <CertPicker certifications={certifications} selectedCode={selectedCode} onSelect={select} />

          <label className="field">
            <span>Category</span>
            <select value={category} onChange={(e) => setCategory(e.target.value)}>
              <option value="">All categories ({counts.allCategories})</option>
              {categories.map((c) => (
                <option key={c} value={c}>
                  {c} ({counts.perCategory.get(c) ?? 0})
                </option>
              ))}
            </select>
          </label>

          <button type="button" className="ghost" onClick={refresh} title="Reload the curriculum from the server">
            Refresh
          </button>
        </div>

        {data && data.totalTopics > 0 && (
          <div className="lesson-progress">
            <div className="lesson-progress-head">
              <strong>
                {data.completedTopics} of {data.totalTopics} topics done
              </strong>
              <span className="muted small">
                Ordered by what you get wrong in exams, not alphabetically.
              </span>
            </div>
            <Meter value={progressPercent} tone="neutral" />
          </div>
        )}
      </div>

      {error && <Banner kind="error">{error}</Banner>}

      {data && !data.aiConfigured && (
        <Banner kind="info">
          No AI provider is configured, so each lesson shows its verified facts and official
          documentation link, but not the generated notes. Set <code>Ai:Provider</code> and{' '}
          <code>Ai:ApiKey</code> to fill them in.
        </Banner>
      )}

      {!data && !error && <Skeleton lines={8} />}

      {data && data.totalTopics === 0 && (
        <EmptyState
          title="No curriculum for this certification yet"
          body={`The study catalogue currently covers CLF-C02. Switch certification, or add ${selectedCode} topics to LessonCatalog.cs on the server.`}
        />
      )}

      {data && data.totalTopics > 0 && (
        <>
          <div className="lesson-filters">
            <div className="segmented" role="tablist" aria-label="What the lesson teaches">
              {KINDS.map((k) => (
                <button
                  key={k.id}
                  type="button"
                  role="tab"
                  aria-selected={kind === k.id}
                  title={k.hint}
                  className={kind === k.id ? 'active' : undefined}
                  onClick={() => setKind(k.id)}
                >
                  {k.label} <span className="count">{counts.perKind.get(k.id) ?? 0}</span>
                </button>
              ))}
            </div>

            <div className="filter-end">
              <label className="check">
                <input
                  type="checkbox"
                  checked={notDoneOnly}
                  onChange={(e) => setNotDoneOnly(e.target.checked)}
                />
                <span>Not done only ({counts.remaining})</span>
              </label>

              <button
                type="button"
                className="ghost"
                disabled={exportProgress !== null || visible.length === 0}
                onClick={() => void exportPdf()}
                title="Export every lesson matching these filters as one PDF"
              >
                {exportProgress
                  ? `Preparing ${exportProgress.done}/${exportProgress.total}…`
                  : `Export PDF (${visible.length})`}
              </button>
            </div>
          </div>

          {visible.length === 0 ? (
            <EmptyState
              title="Nothing here"
              body={
                notDoneOnly
                  ? 'Every lesson matching these filters is done. Untick "Not done only" to read them again.'
                  : 'No lesson matches these filters. Try another kind or category.'
              }
            />
          ) : (
            <>
              <ul className="lesson-list">
                {pageItems.map((topic) => (
                  <LessonRow key={topic.slug} topic={topic} code={selectedCode} />
                ))}
              </ul>

              <nav className="pager" aria-label="Lesson pages">
                <span className="muted small">
                  {(currentPage - 1) * PAGE_SIZE + 1}–{(currentPage - 1) * PAGE_SIZE + pageItems.length} of{' '}
                  {visible.length}
                </span>

                <div className="pager-controls">
                  <button
                    type="button"
                    className="ghost"
                    disabled={currentPage === 1}
                    onClick={() => setPage(currentPage - 1)}
                  >
                    ← Previous
                  </button>

                  {Array.from({ length: pageCount }, (_, i) => i + 1).map((n) => (
                    <button
                      key={n}
                      type="button"
                      className={`page-dot${n === currentPage ? ' current' : ''}`}
                      aria-current={n === currentPage ? 'page' : undefined}
                      aria-label={`Page ${n} of ${pageCount}`}
                      onClick={() => setPage(n)}
                    >
                      {n}
                    </button>
                  ))}

                  <button
                    type="button"
                    className="ghost"
                    disabled={currentPage === pageCount}
                    onClick={() => setPage(currentPage + 1)}
                  >
                    Next →
                  </button>
                </div>
              </nav>
            </>
          )}
        </>
      )}

      {/* Rendered only while an export is in flight, and hidden except when printing. */}
      {exportDocs.length > 0 && (
        <LessonPrintDocument
          lessons={exportDocs}
          title={`${selectedCode} lessons`}
          subtitle={exportSubtitle}
        />
      )}
    </div>
  )
}

function LessonRow({ topic, code }: { topic: LessonSummary; code: string }) {
  const mastery = MASTERY_UI[topic.mastery.status]

  return (
    <li className={`lesson-row ${mastery.tone}`}>
      <Link to={`/lessons/${code}/${topic.slug}`} className="lesson-row-main">
        <ServiceIcon slug={topic.slug} title={topic.title} />

        <div className="lesson-row-body">
        <div className="lesson-row-head">
          <h3>{topic.title}</h3>
          {topic.completed && (
            <span className="chip done" title="You marked this lesson done">
              ✓ Done
            </span>
          )}
          {topic.isCore && !topic.completed && (
            <span className="chip accent" title="Heavily tested on this exam">
              Core
            </span>
          )}
        </div>

        <p className="lesson-purpose">{topic.purpose}</p>

        <div className="lesson-row-meta">
          <span className="chip">{topic.category}</span>
          {topic.domainName && <span className="chip subtle">{topic.domainName}</span>}
          {!topic.hasNotes && (
            <span className="chip subtle" title="The full notes are written the first time you open this lesson">
              Notes on first open
            </span>
          )}
        </div>
        </div>
      </Link>

      <div className="lesson-row-mastery" title={mastery.hint}>
        <span className={`mastery ${mastery.tone}`}>{mastery.label}</span>
        {topic.mastery.accuracyPercent !== null ? (
          <span className="muted small">
            {topic.mastery.accuracyPercent}% of {topic.mastery.answered}
            {/* A concept topic has no service to match on, so the figure is the whole domain's.
                Saying so stops six topics showing the same number as if each were measured. */}
            {topic.mastery.basis === 'Domain' && ' (domain)'}
          </span>
        ) : (
          <span className="muted small">no data</span>
        )}
      </div>
    </li>
  )
}
