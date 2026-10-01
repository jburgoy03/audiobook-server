import { useState, type FormEvent } from 'react'
import { checkSession, login, type AuthState } from './auth'

/**
 * The only page a signed-out visitor sees. There is no registration: the one
 * account is created on the server from configuration.
 */
export function LoginPage({ auth }: { auth: AuthState }) {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  if (auth.status === 'unreachable') {
    return (
      <section className="login">
        <h1>Can’t reach the library</h1>
        <p className="muted">Check that the API is running, then try again.</p>
        <p className="login-detail muted">{auth.message}</p>
        <button type="button" className="resume" onClick={() => void checkSession()}>
          Try again
        </button>
      </section>
    )
  }

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await login(username.trim(), password)
      // Success swaps this page for the app; nothing more to do here.
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
      setBusy(false)
    }
  }

  const signingOut = auth.status === 'signingOut'

  return (
    <section className="login" aria-labelledby="login-heading">
      <h1 id="login-heading">{signingOut ? 'Signing out…' : 'Sign in'}</h1>
      {auth.status === 'signedOut' && auth.expired && (
        <p className="muted">Your session ended. Sign in again to keep listening; your place is saved.</p>
      )}

      {!signingOut && (
        <form onSubmit={onSubmit} className="login-form">
          <label className="field">
            <span>Name</span>
            <input
              name="username"
              autoComplete="username"
              autoCapitalize="none"
              spellCheck={false}
              required
              value={username}
              onChange={(e) => setUsername(e.target.value)}
            />
          </label>
          <label className="field">
            <span>Passphrase</span>
            <input
              name="password"
              type="password"
              autoComplete="current-password"
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </label>
          <p className="login-error" role="alert">
            {error ?? ''}
          </p>
          <button type="submit" className="resume" disabled={busy} aria-busy={busy || undefined}>
            {busy ? 'Signing in…' : 'Sign in'}
          </button>
        </form>
      )}
    </section>
  )
}
