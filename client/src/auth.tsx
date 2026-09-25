import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import {
  ApiError,
  api,
  clearAdminKey,
  clearToken,
  getToken,
  purgeLegacyIdentity,
  setToken,
  setUnauthorizedHandler,
} from './api'
import type { CurrentUser } from './api'
import { invalidateLessonCache } from './pages/lessonCache'
import { useRouter } from './router'

/**
 * Where the session is in its life.
 *
 * `unreachable` is deliberately not folded into `anon`. A token that the API cannot be asked
 * about is not a token that was refused, and showing a sign-in form to someone whose API is down
 * invites them to sign in, fail, and conclude their password is wrong.
 */
type AuthStatus = 'loading' | 'authed' | 'anon' | 'unreachable'

interface AuthValue {
  status: AuthStatus
  user: CurrentUser | null
  /** True when the session ended on its own rather than by signing out. */
  sessionExpired: boolean
  /** Where they were when that happened, so signing in returns them to it. */
  returnTo: string | null
  signIn: (email: string, password: string) => Promise<void>
  register: (email: string, password: string) => Promise<void>
  signOut: () => void
  /** Re-runs the start-up check, for the "API unreachable" retry. */
  retry: () => void
  /** Clears `returnTo` once it has been used, so a later sign-in starts fresh. */
  clearReturnTo: () => void
}

const AuthContext = createContext<AuthValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const { path } = useRouter()

  // Seeded synchronously from storage, and this one line is what stops the sign-in form
  // flashing on every reload: someone holding a token goes to `loading` and waits for the
  // renewal below, never to `anon`. Deriving it after mount would render the form first.
  const [status, setStatus] = useState<AuthStatus>(() => (getToken() ? 'loading' : 'anon'))
  const [user, setUser] = useState<CurrentUser | null>(null)
  const [sessionExpired, setSessionExpired] = useState(false)
  const [returnTo, setReturnTo] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)

  // The current route, read by the 401 handler without making it depend on the route: a
  // dependency there would re-register the handler on every navigation.
  const pathRef = useRef(path)
  pathRef.current = path

  /** Everything one account left behind in this tab, dropped in one place. */
  const forgetSession = useCallback(() => {
    clearToken()
    clearAdminKey()

    // Keyed by certification code, not by account, and it holds each topic's done flag - so
    // without this the next person to sign in on this tab sees the last one's ticks.
    invalidateLessonCache()

    setUser(null)
  }, [])

  // Start-up: turn a stored token into a session, and roll it forward at the same time.
  //
  // This calls refresh rather than /me deliberately. Refresh answers the same question - is this
  // token still good, and whose is it - and hands back a new one dated from now. So the clock
  // restarts every time the app is opened, and anyone who uses it at all inside the token's life
  // is never signed out. That is what makes the session effectively permanent without a
  // refresh-token table to store, rotate and expire.
  useEffect(() => {
    purgeLegacyIdentity()

    if (!getToken()) {
      setStatus('anon')
      return
    }

    let cancelled = false
    setStatus('loading')

    void api.auth
      .refresh()
      .then((renewed) => {
        if (cancelled) return
        setToken(renewed.token)
        setUser({ userId: renewed.userId, email: renewed.email })
        setStatus('authed')
      })
      .catch((e: unknown) => {
        if (cancelled) return

        // No response at all: the token may be perfectly good and the API simply down. Keep it.
        if (e instanceof ApiError && e.status === 0) {
          setStatus('unreachable')
          return
        }

        forgetSession()
        setStatus('anon')
      })

    // StrictMode runs this twice in development; the flag keeps the first run's late answer from
    // overwriting the second's.
    return () => {
      cancelled = true
    }
  }, [attempt, forgetSession])

  // A tab left open for weeks would otherwise sail past the token's expiry while in use, and
  // discover it as a 401 halfway through an exam. Renewing on a timer keeps a long-lived tab as
  // fresh as a reload would.
  //
  // Twelve hours is chosen against the token's own lifetime rather than picked for feel: it is
  // short enough that even the shortest sensible lifetime gets several attempts before it lapses,
  // and long enough that the API is not answering refreshes nobody asked for.
  useEffect(() => {
    if (status !== 'authed') return

    const renew = () => {
      if (!getToken()) return

      void api.auth
        .refresh()
        .then((renewed) => setToken(renewed.token))
        .catch(() => {
          // Nothing to do here. A rejected token is already handled by the 401 handler below,
          // and a refresh that failed because the API is down should not sign anyone out - the
          // token they hold is still perfectly good.
        })
    }

    const timer = window.setInterval(renew, 12 * 60 * 60 * 1000)

    // A laptop that slept through the interval fires no timer, so coming back to the tab is its
    // own trigger.
    const onVisible = () => {
      if (document.visibilityState === 'visible') renew()
    }
    document.addEventListener('visibilitychange', onVisible)

    return () => {
      window.clearInterval(timer)
      document.removeEventListener('visibilitychange', onVisible)
    }
  }, [status])

  // A rejected token anywhere in the app ends the session here.
  useEffect(() => {
    setUnauthorizedHandler(() => {
      // Idempotent by construction: a page firing four requests at once produces four 401s, and
      // all four must add up to one sign-out. Remembering the route only on the first one keeps
      // the later ones from overwriting it with wherever we have landed by then.
      setReturnTo((current) => current ?? pathRef.current)
      setSessionExpired(true)
      forgetSession()
      setStatus('anon')
    })

    return () => setUnauthorizedHandler(null)
  }, [forgetSession])

  const accept = useCallback(
    (result: { token: string; userId: string; email: string }) => {
      // The cache is cleared on the way in as well as on the way out: signing in as a second
      // account without reloading the tab would otherwise inherit the first one's lessons.
      invalidateLessonCache()

      setToken(result.token)
      setUser({ userId: result.userId, email: result.email })
      setSessionExpired(false)
      setStatus('authed')
    },
    [],
  )

  const signIn = useCallback(
    async (email: string, password: string) => accept(await api.auth.login({ email, password })),
    [accept],
  )

  const register = useCallback(
    async (email: string, password: string) => accept(await api.auth.register({ email, password })),
    [accept],
  )

  const signOut = useCallback(() => {
    // No server call: the token is stateless, so there is nothing to tell the API. That also
    // means signing out can never fail because the API is down.
    forgetSession()
    setSessionExpired(false)
    setReturnTo(null)
    setStatus('anon')
  }, [forgetSession])

  const value = useMemo<AuthValue>(
    () => ({
      status,
      user,
      sessionExpired,
      returnTo,
      signIn,
      register,
      signOut,
      retry: () => setAttempt((n) => n + 1),
      clearReturnTo: () => setReturnTo(null),
    }),
    [status, user, sessionExpired, returnTo, signIn, register, signOut],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthValue {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider')
  return ctx
}
