import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'

/**
 * Minimal hash router. The app has five flat views, so this replaces react-router-dom
 * (whose current releases all sit inside an open advisory range).
 * Routes look like #/exam/<id> and may carry a query string: #/generate?domainId=3
 */
interface RouterValue {
  path: string
  segments: string[]
  query: URLSearchParams
  navigate: (to: string) => void
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

  const navigate = useCallback((to: string) => {
    const next = to.startsWith('/') ? to : `/${to}`
    if (currentHash() === next) return
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
  children,
}: {
  to: string
  className?: string
  children: ReactNode
}) {
  const { navigate } = useRouter()
  return (
    <a
      href={`#${to}`}
      className={className}
      onClick={(e) => {
        e.preventDefault()
        navigate(to)
      }}
    >
      {children}
    </a>
  )
}
