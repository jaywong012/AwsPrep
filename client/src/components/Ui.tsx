import { useEffect, useRef, useState, type ReactNode } from 'react'
import type { Certification } from '../api/types'

export function Spinner({ label = 'Loading…' }: { label?: string }) {
  return (
    <div className="spinner-row" role="status">
      <span className="spinner" aria-hidden="true" />
      <span>{label}</span>
    </div>
  )
}

/** Skeleton block used while a panel's data is in flight. */
export function Skeleton({ lines = 3 }: { lines?: number }) {
  return (
    <div className="skeleton" aria-hidden="true">
      {Array.from({ length: lines }, (_, i) => (
        <span key={i} style={{ width: `${100 - i * 12}%` }} />
      ))}
    </div>
  )
}

export function Banner({
  kind,
  children,
}: {
  kind: 'error' | 'warning' | 'info' | 'success'
  children: ReactNode
}) {
  const icon = { error: '!', warning: '!', info: 'i', success: '✓' }[kind]
  return (
    <div className={`banner ${kind}`} role={kind === 'error' ? 'alert' : 'status'}>
      <span className="banner-icon" aria-hidden="true">
        {icon}
      </span>
      <div>{children}</div>
    </div>
  )
}

export function EmptyState({
  title,
  body,
  action,
}: {
  title: string
  body: string
  action?: ReactNode
}) {
  return (
    <div className="empty">
      <h3>{title}</h3>
      <p>{body}</p>
      {action}
    </div>
  )
}

/** Circular score gauge. Pure SVG so it needs no chart dependency. */
export function ScoreDial({
  value,
  target,
  caption,
  size = 132,
}: {
  value: number
  target: number
  caption?: string
  size?: number
}) {
  const clamped = Math.max(0, Math.min(100, value))
  const stroke = 11
  const r = (size - stroke) / 2
  const circumference = 2 * Math.PI * r
  const dash = (clamped / 100) * circumference
  const state = clamped >= target ? 'pass' : clamped >= target - 15 ? 'near' : 'low'

  return (
    <div className={`dial ${state}`} style={{ width: size }}>
      <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`} role="img"
        aria-label={`${clamped.toFixed(0)} percent, pass mark ${target} percent`}>
        <circle className="dial-track" cx={size / 2} cy={size / 2} r={r} strokeWidth={stroke} fill="none" />
        <circle
          className="dial-value"
          cx={size / 2}
          cy={size / 2}
          r={r}
          strokeWidth={stroke}
          fill="none"
          strokeDasharray={`${dash} ${circumference - dash}`}
          strokeLinecap="round"
          transform={`rotate(-90 ${size / 2} ${size / 2})`}
        />
      </svg>
      <div className="dial-label">
        <strong>{clamped.toFixed(0)}%</strong>
        {caption && <span>{caption}</span>}
      </div>
    </div>
  )
}

/** Horizontal meter with a pass-mark tick. */
export function Meter({
  value,
  target,
  tone,
}: {
  value: number
  target?: number
  tone?: 'auto' | 'neutral'
}) {
  const clamped = Math.max(0, Math.min(100, value))
  const state =
    tone === 'neutral' || target === undefined
      ? 'neutral'
      : clamped >= target
        ? 'pass'
        : clamped >= target - 15
          ? 'near'
          : 'low'
  return (
    <div className={`meter ${state}`} role="img" aria-label={`${clamped.toFixed(0)} percent`}>
      <span className="meter-fill" style={{ width: `${clamped}%` }} />
      {target !== undefined && <span className="meter-tick" style={{ left: `${target}%` }} />}
    </div>
  )
}

export function CertPicker({
  certifications,
  selectedCode,
  onSelect,
  label = 'Certification',
}: {
  certifications: Certification[]
  selectedCode: string
  onSelect: (code: string) => void
  label?: string
}) {
  return (
    // "CLF-C02 — AWS Certified Cloud Practitioner" is a long option, and a select cannot
    // ellipsis its own value, so this field asks for more of the row than its neighbours.
    <label className="field wide-field">
      <span>{label}</span>
      <select value={selectedCode} onChange={(e) => onSelect(e.target.value)}>
        {certifications.map((c) => (
          <option key={c.code} value={c.code}>
            {c.code} — {c.name}
          </option>
        ))}
      </select>
    </label>
  )
}

/**
 * The official AWS icon for a lesson topic, served from public/aws-icons/<slug>.svg.
 *
 * Keyed on the slug rather than sent by the API: the icon is a client asset, so the server has no
 * reason to know about it, and adding a topic means dropping one more file in that folder.
 * A topic with no icon file renders nothing rather than a broken image.
 */
/**
 * The AWS Cloud logo, shown for a topic with no icon of its own.
 *
 * 25 of the 103 CLF-C02 topics are ideas rather than services - capex versus opex, least
 * privilege - so there is no service icon to draw and never will be. Rendering nothing left
 * those rows with their text starting where every other row's icon was, which reads as a broken
 * image rather than as a topic without one.
 *
 * The file is the AWS Cloud logo from the same official icon package as the rest, copied under
 * a name no slug can take: a slug is lowercase letters, digits and hyphens, so it can never
 * begin with an underscore and can never collide with this.
 */
const DEFAULT_ICON = '/aws-icons/_default.svg'

export function ServiceIcon({ slug, title, size = 40 }: { slug: string; title: string; size?: number }) {
  const [useDefault, setUseDefault] = useState(false)
  const [failed, setFailed] = useState(false)

  // Going from one lesson to the next keeps this component mounted, so without the reset a
  // topic that fell back once would keep showing the default for every topic after it.
  useEffect(() => {
    setUseDefault(false)
    setFailed(false)
  }, [slug])

  // Nothing at all only if the default is missing too, which would otherwise retry the same
  // broken URL forever.
  if (failed) return null

  return (
    <img
      className="service-icon"
      src={useDefault ? DEFAULT_ICON : `/aws-icons/${slug}.svg`}
      width={size}
      height={size}
      loading="lazy"
      // Decorative: the topic title is always next to it, so announcing the icon would
      // just make a screen reader say the service name twice.
      alt=""
      aria-hidden="true"
      title={title}
      onError={() => (useDefault ? setFailed(true) : setUseDefault(true))}
    />
  )
}

export function DifficultyBadge({ value }: { value: string }) {
  return <span className={`chip difficulty-${value.toLowerCase()}`}>{value}</span>
}

/** In-page confirm, replacing window.confirm so an exam is never interrupted by a browser dialog. */
export function ConfirmDialog({
  title,
  body,
  confirmLabel,
  cancelLabel = 'Keep going',
  onConfirm,
  onCancel,
}: {
  title: string
  body: string
  confirmLabel: string
  cancelLabel?: string
  onConfirm: () => void
  onCancel: () => void
}) {
  const confirmRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    confirmRef.current?.focus()
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onCancel()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onCancel])

  return (
    <div className="modal-backdrop" onClick={onCancel}>
      <div
        className="modal"
        role="dialog"
        aria-modal="true"
        aria-label={title}
        onClick={(e) => e.stopPropagation()}
      >
        <h3>{title}</h3>
        <p>{body}</p>
        <div className="button-row end">
          <button type="button" className="ghost" onClick={onCancel}>
            {cancelLabel}
          </button>
          <button type="button" className="primary" ref={confirmRef} onClick={onConfirm}>
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  )
}
