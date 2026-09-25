import { useState } from 'react'
import { Banner } from '../components/Ui'
import { Link } from '../router'
import { useAuth } from '../auth'

/** Matches IdentityOptions.Password.RequiredLength on the API. */
const MIN_PASSWORD_LENGTH = 10

export default function Register() {
  const { register } = useAuth()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function submit(e: React.FormEvent) {
    e.preventDefault()

    const address = email.trim()
    if (address === '') return setError('Enter your email address.')

    // The length and the match are worth catching here because they cost a round trip to learn.
    // Everything else the password policy asks for - a digit, a capital - is left to the server,
    // so there is one place that decides and no chance of the two disagreeing.
    if (password.length < MIN_PASSWORD_LENGTH) {
      return setError(`Use at least ${MIN_PASSWORD_LENGTH} characters.`)
    }
    if (password !== confirm) return setError('The two passwords do not match.')

    setBusy(true)
    setError(null)
    try {
      await register(address, password)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not create the account')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="stack auth-page">
      <div className="page-head">
        <h1>Create an account</h1>
        <p>One account keeps your progress together across browsers.</p>
      </div>

      <form className="card" onSubmit={submit}>
        <div className="form-grid">
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
              autoComplete="new-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </label>

          <label className="field wide">
            <span>Confirm password</span>
            <input
              type="password"
              autoComplete="new-password"
              value={confirm}
              onChange={(e) => setConfirm(e.target.value)}
            />
          </label>
        </div>

        <p className="muted small">
          At least {MIN_PASSWORD_LENGTH} characters, with a capital letter and a digit. There is no
          way to reset it — this app cannot send email.
        </p>

        <div className="button-row">
          <button type="submit" className="primary" disabled={busy}>
            {busy ? 'Creating…' : 'Create account'}
          </button>
        </div>

        {error && <Banner kind="error">{error}</Banner>}
      </form>

      <p className="muted small">
        Already have one? <Link to="/login">Sign in</Link>
      </p>
    </div>
  )
}
