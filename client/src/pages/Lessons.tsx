import { useCallback, useEffect, useMemo, useState } from 'react'
import { api } from '../api'
import type { LessonSummary, LessonsResponse, MasteryStatus } from '../api/types'
import type { useCertifications } from '../hooks/useCertifications'
import { Banner, CertPicker, EmptyState, Meter, ServiceIcon, Skeleton } from '../components/Ui'
import { Link } from '../router'

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

export default function Lessons({ certs }: { certs: Certs }) {
  const { certifications, selectedCode, select } = certs

  const [data, setData] = useState<LessonsResponse | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [kind, setKind] = useState<Kind>('all')
  const [notDoneOnly, setNotDoneOnly] = useState(false)
  const [category, setCategory] = useState<string>('')

  const load = useCallback(async () => {
    if (!selectedCode) return
    setData(null)
    setError(null)
    try {
      setData(await api.lessons.list(selectedCode))
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load lessons')
    }
  }, [selectedCode])

  useEffect(() => {
    void load()
  }, [load])

  const categories = useMemo(
    () => [...new Set(data?.topics.map((t) => t.category) ?? [])].sort(),
    [data],
  )

  // The server has already ranked the list, so filtering only ever removes rows - the order a
  // learner sees is still "what to study next" whichever filters are on.
  const visible = useMemo(() => {
    let topics = data?.topics ?? []
    if (kind !== 'all') topics = topics.filter((t) => t.kind === kind)
    if (category) topics = topics.filter((t) => t.category === category)
    if (notDoneOnly) topics = topics.filter((t) => !t.completed)
    return topics
  }, [data, kind, category, notDoneOnly])

  const remaining = useMemo(
    () => (data?.topics ?? []).filter((t) => !t.completed).length,
    [data],
  )

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
              <option value="">All categories</option>
              {categories.map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </select>
          </label>
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
                  {k.label}
                </button>
              ))}
            </div>

            <label className="check">
              <input
                type="checkbox"
                checked={notDoneOnly}
                onChange={(e) => setNotDoneOnly(e.target.checked)}
              />
              <span>Not done only{remaining > 0 ? ` (${remaining})` : ''}</span>
            </label>
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
            <ul className="lesson-list">
              {visible.map((topic) => (
                <LessonRow key={topic.slug} topic={topic} code={selectedCode} />
              ))}
            </ul>
          )}
        </>
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
