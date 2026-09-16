import { useCallback, useEffect, useState } from 'react'

export type Theme = 'dark' | 'light'

const STORAGE_KEY = 'awscert.theme'

function stored(): Theme {
  const value = localStorage.getItem(STORAGE_KEY)
  if (value === 'dark' || value === 'light') return value

  // No stored choice, including the 'system' one earlier versions wrote: start from whatever the
  // OS is set to, then the toggle is an explicit two-way switch from there.
  return window.matchMedia?.('(prefers-color-scheme: light)').matches ? 'light' : 'dark'
}

/**
 * Dark or light, and nothing else. A third 'follow the system' state meant two clicks to get to
 * the theme you wanted and a state most people could not tell apart from the one it resolved to,
 * so the toggle is now just a switch: one click, the other theme.
 *
 * The choice is written to data-theme on <html>; CSS reads it, and falls back to
 * prefers-color-scheme only when the attribute is absent.
 */
export function useTheme() {
  const [theme, setTheme] = useState<Theme>(stored)

  useEffect(() => {
    document.documentElement.setAttribute('data-theme', theme)
    localStorage.setItem(STORAGE_KEY, theme)
  }, [theme])

  const cycle = useCallback(() => {
    setTheme((current) => (current === 'dark' ? 'light' : 'dark'))
  }, [])

  return { theme, setTheme, cycle }
}
