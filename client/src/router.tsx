import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'

/**
 * Minimal hash router. The app has five flat views, so this replaces react-router-dom
 * (whose current releases all sit inside an open advisory range).
 * Routes look like #/exam/<id> and may carry a query string: #/generate?domainId=3
 */
/** Options for {@link RouterValue.navigate}. */
export interface NavigateOptions {
  /**
   * Replace the current history entry instead of pushing a new one. Used where landing on a
   * page was not the learner's choice - being bounced off the sign-in screen once signed in,
   * or being returned to where a session expired - so that Back does not lead somewhere that
   * immediately bounces them forward again.
   */
  replace?: boolean
}

interface RouterValue {
  path: string
  segments: string[]
  query: URLSearchParams
  navigate: (to: string, options?: NavigateOptions) => void
}

const RouterContext = createContext<RouterValue | null>(null)

function currentHash(): string {
  const hash = window.location.hash.replace(/^#/, '')
  return hash === '' ? '/' : hash
}

export function RouterProvider({ children }: { children: ReactNode }) {
  const [hash, setHash] = useState(currentHash)

  useEffect(() => {
    const onChange = () => {
      setHash(currentHash())
      window.scrollTo({ top: 0 })
    }
    window.addEventListener('hashchange', onChange)
    return () => window.removeEventListener('hashchange', onChange)
  }, [])

  const navigate = useCallback((to: string, options?: NavigateOptions) => {
    const next = to.startsWith('/') ? to : `/${to}`
    if (currentHash() === next) return

    if (options?.replace) {
      // replaceState does not fire hashchange, so the listener above never runs for this and the
      // state and scroll have to be driven by hand - the same two things it would have done.
      window.history.replaceState(null, '', `#${next}`)
      setHash(next)
      window.scrollTo({ top: 0 })
      return
    }

    window.location.hash = next
  }, [])

  const value = useMemo<RouterValue>(() => {
    const [path, search = ''] = hash.split('?')
    return {
      path,
      segments: path.split('/').filter(Boolean),
      query: new URLSearchParams(search),
      navigate,
    }
  }, [hash, navigate])

  return <RouterContext.Provider value={value}>{children}</RouterContext.Provider>
}

export function useRouter(): RouterValue {
  const ctx = useContext(RouterContext)
  if (!ctx) throw new Error('useRouter must be used inside RouterProvider')
  return ctx
}

export function Link({
  to,
  className,
  title,
  children,
}: {
  to: string
  className?: string
  /** Tooltip, and the accessible name when the label itself is hidden - a collapsed sidebar. */
  title?: string
  children: ReactNode
}) {
  const { navigate } = useRouter()
  return (
    <a
      href={`#${to}`}
      className={className}
      title={title}
      onClick={(e) => {
        e.preventDefault()
        navigate(to)
      }}
    >
      {children}
    </a>
  )
}
