import { useEffect, useId, useRef, useState } from 'react'
import type { Theme } from '../hooks/useTheme'

const THEME_ITEM = {
  dark: { icon: '☀', label: 'Switch to light theme' },
  light: { icon: '☾', label: 'Switch to dark theme' },
} as const

/**
 * The signed-in account, and the two things you can do with it.
 *
 * The email doubles as the trigger rather than sitting beside one: it is already the widest thing
 * in the topbar, and a separate caret button next to it would be a second target for the same
 * idea. Theme and sign-out live behind it because neither is something you reach for mid-task -
 * putting them one click away buys back the room the address takes.
 *
 * Theme is passed in rather than read from useTheme here. Two components each calling the hook
 * would each hold their own copy of the state, and the one that did not do the toggling would go
 * on rendering the old label until something else re-rendered it.
 */
export function AccountMenu({
  email,
  theme,
  onToggleTheme,
  onSignOut,
  compact = false,
}: {
  email: string
  theme: Theme
  onToggleTheme: () => void
  onSignOut: () => void
  /** Collapsed sidebar: the trigger shrinks to the initial, and the address moves into the panel. */
  compact?: boolean
}) {
  const [open, setOpen] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)
  const triggerRef = useRef<HTMLButtonElement>(null)
  const menuId = useId()

  useEffect(() => {
    if (!open) return

    const onPointerDown = (e: MouseEvent | TouchEvent) => {
      if (!rootRef.current?.contains(e.target as Node)) setOpen(false)
    }

    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key !== 'Escape') return
      setOpen(false)
      // Escape means "put me back where I was", so the trigger takes focus again rather than
      // leaving it on an element that has just been removed from the document.
      triggerRef.current?.focus()
    }

    // Pointerdown rather than click: a click listener would also catch the very click that
    // opened the menu, on the way back up, and close it again immediately.
    document.addEventListener('pointerdown', onPointerDown)
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('pointerdown', onPointerDown)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [open])

  const themeItem = THEME_ITEM[theme]

  return (
    <div className="account-menu" ref={rootRef}>
      <button
        ref={triggerRef}
        type="button"
        className={`account-trigger${compact ? ' compact' : ''}`}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        title={email}
        aria-label={compact ? `Account: ${email}` : undefined}
        onClick={() => setOpen((o) => !o)}
      >
        {compact ? (
          <span className="account-initial" aria-hidden="true">
            {email.slice(0, 1).toUpperCase()}
          </span>
        ) : (
          <>
            <span className="account-email">{email}</span>
            <span className="account-caret" aria-hidden="true">
              ▾
            </span>
          </>
        )}
      </button>

      {open && (
        <div className="account-panel" id={menuId} role="menu">
          <p className="account-who" title={email}>
            {email}
          </p>

          <button
            type="button"
            role="menuitem"
            className="account-item"
            onClick={() => {
              onToggleTheme()
              setOpen(false)
            }}
          >
            <span className="account-item-icon" aria-hidden="true">
              {themeItem.icon}
            </span>
            {themeItem.label}
          </button>

          <button
            type="button"
            role="menuitem"
            className="account-item"
            onClick={() => {
              setOpen(false)
              onSignOut()
            }}
          >
            <span className="account-item-icon" aria-hidden="true">
              ⎋
            </span>
            Sign out
          </button>
        </div>
      )}
    </div>
  )
}
