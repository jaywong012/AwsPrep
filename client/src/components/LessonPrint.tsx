import type { LessonDetail } from '../api/types'

/**
 * The printable form of one or more lessons, and the "export PDF" mechanism behind it.
 *
 * There is no PDF library here on purpose. Every browser already prints to PDF, and going
 * through it means the export has selectable text, real hyperlinks, working page breaks and the
 * reader's own paper size — none of which a canvas-to-PDF library gives you without a fight, and
 * all of which matter for something a learner will actually revise from. What the app supplies
 * is the document: this component renders it, `print.css` strips the app around it, and the
 * browser's own dialog does the saving.
 *
 * It renders into the live page rather than a popup window, because a popup is the first thing
 * an ad blocker eats.
 */
export function LessonPrintDocument({
  lessons,
  title,
  subtitle,
}: {
  lessons: LessonDetail[]
  title: string
  subtitle: string
}) {
  return (
    <div className="print-doc" aria-hidden="true">
      <header className="print-cover">
        <h1>{title}</h1>
        <p className="print-subtitle">{subtitle}</p>
        <p className="print-meta">
          {lessons.length} lesson{lessons.length === 1 ? '' : 's'} · exported{' '}
          {new Date().toLocaleDateString()} · AWS CertPrep
        </p>
        <p className="print-disclaimer">
          Study aid, not official AWS exam content. Confirm anything that matters against the AWS
          documentation link printed with each topic.
        </p>
      </header>

      {lessons.map((lesson) => (
        <article key={lesson.slug} className="print-lesson">
          <h2>{lesson.title}</h2>

          <p className="print-tags">
            {lesson.category}
            {lesson.domainName ? ` · ${lesson.domainName}` : ''}
            {lesson.domainWeightPercent !== null ? ` · ${lesson.domainWeightPercent}% of the exam` : ''}
          </p>

          <h3>What it is for</h3>
          <p>{lesson.purpose}</p>

          <h3>How it is charged</h3>
          <p>{lesson.pricingModel}</p>

          {lesson.body ? (
            <>
              <h3>In practice</h3>
              <p>{lesson.body.overview}</p>

              {lesson.body.useCases.length > 0 && (
                <>
                  <h3>When you would use it</h3>
                  <ul>
                    {lesson.body.useCases.map((u, i) => (
                      <li key={i}>{u}</li>
                    ))}
                  </ul>
                </>
              )}

              {lesson.body.costNotes && (
                <>
                  <h3>What it costs you in practice</h3>
                  <p>{lesson.body.costNotes}</p>
                </>
              )}

              {lesson.body.integrations.length > 0 && (
                <>
                  <h3>What it works with</h3>
                  <ul>
                    {lesson.body.integrations.map((n, i) => (
                      <li key={i}>{n}</li>
                    ))}
                  </ul>
                </>
              )}

              {lesson.body.realWorldExample && (
                <>
                  <h3>A real case</h3>
                  <p>{lesson.body.realWorldExample}</p>
                </>
              )}

              {lesson.body.examTraps.length > 0 && (
                <>
                  <h3>Exam traps</h3>
                  <ul>
                    {lesson.body.examTraps.map((t, i) => (
                      <li key={i}>{t}</li>
                    ))}
                  </ul>
                </>
              )}
            </>
          ) : (
            /* Said rather than silently omitted: a page that just stops after the pricing line
               reads like the export truncated it. */
            <p className="print-note">
              The written notes for this topic have not been generated yet — open the lesson in the
              app once and export again to include them.
            </p>
          )}

          <p className="print-source">
            Official documentation: {lesson.docsUrl}
            {lesson.pricingUrl ? ` · Pricing: ${lesson.pricingUrl}` : ''}
          </p>
        </article>
      ))}
    </div>
  )
}
