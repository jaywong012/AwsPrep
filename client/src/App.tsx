import { useEffect, useState } from 'react'
import { api } from './api'
import { Link, useRouter } from './router'
import { useCertifications } from './hooks/useCertifications'
import { useTheme } from './hooks/useTheme'
import Dashboard from './pages/Dashboard'
import Lessons from './pages/Lessons'
import Lesson from './pages/Lesson'
import Generate from './pages/Generate'
import Bank from './pages/Bank'
import Exam from './pages/Exam'
import Result from './pages/Result'
import History from './pages/History'

const NAV = [
  { to: '/', label: 'Dashboard' },
  { to: '/lessons', label: 'Lessons' },
  { to: '/generate', label: 'Generate' },
  { to: '/bank', label: 'Bank' },
  { to: '/history', label: 'History' },
]

const KNOWN_ROUTES = ['', 'lessons', 'generate', 'bank', 'exam', 'result', 'history']

const THEME_UI = {
  dark: { icon: '☾', label: 'Dark theme — switch to light' },
  light: { icon: '☀', label: 'Light theme — switch to dark' },
} as const

export default function App() {
  const { path, segments } = useRouter()
  const certs = useCertifications()
  const { theme, cycle } = useTheme()
  const [provider, setProvider] = useState<string | null>(null)

  useEffect(() => {
    api
      .system.health()
      .then((h) => setProvider(h.aiProvider))
      .catch(() => setProvider('unreachable'))
  }, [])

  const root = segments[0] ?? ''
  const offline = provider === 'Offline' || provider === 'unreachable'

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

        <nav className="nav" aria-label="Main">
          {NAV.map((item) => (
            <Link
              key={item.to}
              to={item.to}
              className={`nav-link${(item.to === '/' ? path === '/' : path.startsWith(item.to)) ? ' active' : ''}`}
            >
              {item.label}
            </Link>
          ))}
        </nav>

        <div className="topbar-end">
          {offline && (
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
          )}

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
      </main>

      <footer className="footer">
        Study aids for AWS certification practice — not official AWS exam content.
      </footer>
    </div>
  )
}
