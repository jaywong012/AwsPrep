import { useCallback, useEffect, useState } from 'react'

const STORAGE_KEY = 'awscert.sidebar'

/** Below this the sidebar is icons-only whatever the stored preference says. */
const NARROW = '(max-width: 900px)';

/**
 * Whether the sidebar is showing labels or just icons.
 *
 * The choice is remembered per browser, the same way the theme is - someone who collapses it has
 * decided they know the five icons and would rather have the room, and being asked to decide
 * again on every reload would undo the point of it.
 *
 * Narrow viewports override the preference rather than overwriting it: a 240px rail on a phone is
 * most of the screen, but expanding it again on a wide monitor should still give back the labels
 * the person chose. So the stored value is left alone and only the effective value changes.
 */
export function useSidebar() {
  const [preferred, setPreferred] = useState<boolean>(() => {
    try {
      return localStorage.getItem(STORAGE_KEY) === 'collapsed'
    } catch {
      return false
    }
  })

  const [narrow, setNarrow] = useState<boolean>(
    () => window.matchMedia?.(NARROW).matches ?? false,
  )

  useEffect(() => {
    const query = window.matchMedia?.(NARROW)
    if (!query) return

    const onChange = (e: MediaQueryListEvent) => setNarrow(e.matches)
    query.addEventListener('change', onChange)
    return () => query.removeEventListener('change', onChange)
  }, [])

  const toggle = useCallback(() => {
    setPreferred((current) => {
      const next = !current
      try {
        localStorage.setItem(STORAGE_KEY, next ? 'collapsed' : 'expanded')
      } catch {
        // The sidebar just opens at its default next time. Nothing else uses this.
      }
      return next
    })
  }, [])

  return {
    collapsed: preferred || narrow,
    /** True when the width is forcing it, so the toggle can say it is not in charge. */
    forced: narrow,
    toggle,
  }
}
