import { useEffect, useState, type ReactNode } from 'react'
import { api } from './api'
import { Link, useRouter } from './router'
import { useAuth } from './auth'
import { useCertifications } from './hooks/useCertifications'
import { useTheme } from './hooks/useTheme'
import { Banner, Spinner } from './components/Ui'
import { Sidebar } from './components/Sidebar'
import { useSidebar } from './hooks/useSidebar'
import Dashboard from './pages/Dashboard'
import Lessons from './pages/Lessons'
import Lesson from './pages/Lesson'
import Generate from './pages/Generate'
import Bank from './pages/Bank'
import Exam from './pages/Exam'
import Result from './pages/Result'
import History from './pages/History'
import Login from './pages/Login'
import Register from './pages/Register'

const NAV = [
  { to: '/', label: 'Dashboard', icon: 'dashboard' },
  { to: '/lessons', label: 'Lessons', icon: 'lessons' },
  { to: '/generate', label: 'Generate', icon: 'generate' },
  { to: '/bank', label: 'Bank', icon: 'bank' },
  { to: '/history', label: 'History', icon: 'history' },
]

// 'login' and 'register' belong here or they render the 404 card. They are deliberately not in
// NAV: they are unreachable while signed in.
const KNOWN_ROUTES = [
  '',
  'lessons',
  'generate',
  'bank',
  'exam',
  'result',
  'history',
  'login',
  'register',
]

const THEME_UI = {
  dark: { icon: '☾', label: 'Dark theme — switch to light' },
  light: { icon: '☀', label: 'Light theme — switch to dark' },
} as const

/**
 * Whether the API is answering, and with what. Its own hook because both frames want it and
 * neither should have to own it: /api/health is deliberately unauthenticated, so the question is
 * worth answering on the sign-in screen too - which is exactly when "is it even running?" matters.
 */
function useProviderStatus() {
  const [provider, setProvider] = useState<string | null>(null)

  useEffect(() => {
    api.system
      .health()
      .then((h) => setProvider(h.aiProvider))
      .catch(() => setProvider('unreachable'))
  }, [])

  if (provider !== 'Offline' && provider !== 'unreachable') return null

  return (
    <span
      className="provider-badge muted"
      title={
        provider === 'unreachable'
          ? 'The API is not responding — start it and reload'
          : 'No AI provider configured — questions come from the local template bank'
      }
    >
      {provider === 'unreachable' ? 'API offline' : 'Offline'}
    </span>
  )
}

/**
 * The frame for someone who is not signed in.
 *
 * No sidebar: there is nowhere to navigate to yet, and a rail of five links that all bounce back
 * to the sign-in form would be furniture pretending to be navigation. So this keeps a slim header
 * with the brand and the one control that still applies - the theme, which has to stay reachable
 * when there is no account menu to put it in.
 */
function AnonShell({ children }: { children: ReactNode }) {
  const { theme, cycle } = useTheme()
  const offlineBadge = useProviderStatus()

  return (
    <div className="app">
      <a className="skip-link" href="#main">
        Skip to content
      </a>

      <header className="topbar">
        <Link to="/" className="brand">
          <span className="brand-mark">AWS</span>
          <span>CertPrep</span>
        </Link>

        <div className="topbar-end">
          {offlineBadge}

          <button
            type="button"
            className="theme-toggle"
            onClick={cycle}
            title={THEME_UI[theme].label}
            aria-label={THEME_UI[theme].label}
          >
            {THEME_UI[theme].icon}
          </button>
        </div>
      </header>

      <main className="main" id="main">
        {children}
      </main>

      <footer className="footer">
        Study aids for AWS certification practice — not official AWS exam content.
      </footer>
    </div>
  )
}

/** The frame for a signed-in learner: navigation down the left, content to the right of it. */
function AppShell({
  currentPath,
  user,
  onSignOut,
  children,
}: {
  currentPath: string
  user: { email: string }
  onSignOut: () => void
  children: ReactNode
}) {
  const { theme, cycle } = useTheme()
  const { collapsed, forced, toggle } = useSidebar()
  const offlineBadge = useProviderStatus()

  return (
    <div className="app app-shell">
      <a className="skip-link" href="#main">
        Skip to content
      </a>

      <Sidebar
        items={NAV}
        currentPath={currentPath}
        collapsed={collapsed}
        collapseForced={forced}
        onToggleCollapse={toggle}
        offlineBadge={offlineBadge}
        user={user}
        theme={theme}
        onToggleTheme={cycle}
        onSignOut={onSignOut}
      />

      <div className="app-body">
        <main className="main" id="main">
          {children}
        </main>

        <footer className="footer">
          Study aids for AWS certification practice — not official AWS exam content.
        </footer>
      </div>
    </div>
  )
}

export default function App() {
  const { path, segments, navigate } = useRouter()
  const auth = useAuth()
  const certs = useCertifications(auth.user?.userId ?? null)

  const root = segments[0] ?? ''

  // Signed in but still looking at the sign-in screen: send them on, replacing the entry so Back
  // does not land on a page that immediately bounces forward again.
  const { status, returnTo, clearReturnTo } = auth
  useEffect(() => {
    if (status !== 'authed') return
    if (root !== 'login' && root !== 'register') return

    navigate(returnTo ?? '/', { replace: true })
    clearReturnTo()
  }, [status, root, returnTo, navigate, clearReturnTo])

  // Only ever reached when a token exists, because the status is seeded from storage. So this is
  // a brief wait for /api/auth/me, not a flash of the sign-in form.
  if (auth.status === 'loading') {
    return (
      <AnonShell>
        <Spinner label="Signing you in…" />
      </AnonShell>
    )
  }

  // The API did not answer at all. Not the same as being signed out - the token may be fine - so
  // the token is kept and no sign-in form is offered.
  if (auth.status === 'unreachable') {
    return (
      <AnonShell>
        <div className="card">
          <h2>Could not reach the API</h2>
          <Banner kind="error">
            The API is not responding. Start it and try again — you are still signed in.
          </Banner>
          <div className="button-row">
            <button type="button" className="primary" onClick={auth.retry}>
              Try again
            </button>
          </div>
        </div>
      </AnonShell>
    )
  }

  // Rendered in place of the route rather than navigated to: no history entry, and the deep link
  // they asked for is still in the address bar for when they get back.
  if (auth.status === 'anon') {
    return <AnonShell>{root === 'register' ? <Register /> : <Login />}</AnonShell>
  }

  return (
    <AppShell
      currentPath={path}
      user={{ email: auth.user?.email ?? '' }}
      onSignOut={auth.signOut}
    >
      {root === '' && <Dashboard certs={certs} />}
      {/* #/lessons lists the curriculum; #/lessons/<code>/<slug> is one lesson. The code is in
          the path so a lesson link survives a change of selected certification. */}
      {root === 'lessons' && segments.length < 3 && <Lessons certs={certs} />}
      {root === 'lessons' && segments.length >= 3 && (
        <Lesson code={segments[1]} slug={segments[2]} />
      )}
      {root === 'generate' && <Generate certs={certs} />}
      {root === 'bank' && <Bank certs={certs} />}
      {root === 'exam' && <Exam sessionId={segments[1]} />}
      {root === 'result' && <Result sessionId={segments[1]} />}
      {root === 'history' && <History certs={certs} />}
      {!KNOWN_ROUTES.includes(root) && (
        <div className="card">
          <h2>Page not found</h2>
          <p className="muted">That route does not exist.</p>
          <div className="button-row">
            <Link to="/" className="button-link">
              Back to dashboard →
            </Link>
          </div>
        </div>
      )}
    </AppShell>
  )
}
