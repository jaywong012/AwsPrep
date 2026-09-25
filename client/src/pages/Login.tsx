import { useState } from 'react'
import { Banner } from '../components/Ui'
import { Link } from '../router'
import { useAuth } from '../auth'

export default function Login() {
  const { signIn, sessionExpired } = useAuth()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function submit(e: React.FormEvent) {
    e.preventDefault()

    // Checked here rather than with the `required` attribute, so every complaint lands in the
    // one themed Banner below instead of a native bubble the theme cannot reach.
    const address = email.trim()
    if (address === '') return setError('Enter your email address.')
    if (password === '') return setError('Enter your password.')

    setBusy(true)
    setError(null)
    try {
      await signIn(address, password)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not sign in')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="stack auth-page">
      <div className="page-head">
        <h1>Sign in</h1>
        <p>Your question bank, exam history and lesson progress follow your account.</p>
      </div>

      {sessionExpired && (
        <Banner kind="warning">
          Your session expired. Sign in again to pick up where you left off.
        </Banner>
      )}

      <form className="card" onSubmit={submit}>
        <div className="form-grid">
          {/* .field.wide on both, or form-grid's auto-fit puts email and password side by side. */}
          <label className="field wide">
            <span>Email</span>
            <input
              type="email"
              inputMode="email"
              autoComplete="email"
              autoFocus
              value={email}
              onChange={(e) => setEmail(e.target.value)}
            />
          </label>

          <label className="field wide">
            <span>Password</span>
            <input
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </label>
        </div>

        <div className="button-row">
          <button type="submit" className="primary" disabled={busy}>
            {busy ? 'Signing in…' : 'Sign in'}
          </button>
        </div>

        {error && <Banner kind="error">{error}</Banner>}
      </form>

      <p className="muted small">
        No account yet? <Link to="/register">Create one</Link>
      </p>
    </div>
  )
}
