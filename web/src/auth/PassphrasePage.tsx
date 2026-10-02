import { useState, type FormEvent } from 'react'
import { changePassword } from './auth'

const MIN_LENGTH = 12

/**
 * Shown instead of the app while the user holds a temporary passphrase (see
 * App). Same column as the sign-in page: it's the second half of signing in.
 *
 * The current field is there because the server checks it, and because it's
 * what proves this is the person holding the temporary one, not someone who
 * found an unlocked screen.
 */
export function PassphrasePage({ username }: { username: string }) {
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [confirm, setConfirm] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault()
    if (next.length < MIN_LENGTH) {
      setError(`Choose at least ${MIN_LENGTH} characters. A few words in a row works well.`)
      return
    }
    if (next !== confirm) {
      setError('The two new passphrases don’t match.')
      return
    }

    setBusy(true)
    setError(null)
    try {
      await changePassword(current, next)
      // Success swaps this page for the library.
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
      setBusy(false)
    }
  }

  return (
    <section className="login" aria-labelledby="passphrase-heading">
      <h1 id="passphrase-heading">Choose a passphrase</h1>
      <p className="muted">
        Welcome, {username}. The passphrase you were given was temporary. Choose your own: at least{' '}
        {MIN_LENGTH} characters, and only you will know it.
      </p>

      <form onSubmit={onSubmit} className="login-form">
        {/* Lets a password manager tie the new passphrase to the right name. */}
        <input type="text" name="username" autoComplete="username" value={username} readOnly hidden />
        <label className="field">
          <span>Temporary passphrase</span>
          <input
            name="current-password"
            type="password"
            autoComplete="current-password"
            required
            value={current}
            onChange={(e) => setCurrent(e.target.value)}
          />
        </label>
        <label className="field">
          <span>New passphrase</span>
          <input
            name="new-password"
            type="password"
            autoComplete="new-password"
            required
            value={next}
            onChange={(e) => setNext(e.target.value)}
          />
        </label>
        <label className="field">
          <span>New passphrase, again</span>
          <input
            name="confirm-password"
            type="password"
            autoComplete="new-password"
            required
            value={confirm}
            onChange={(e) => setConfirm(e.target.value)}
          />
        </label>
        <p className="login-error" role="alert">
          {error ?? ''}
        </p>
        <button type="submit" className="resume" disabled={busy} aria-busy={busy || undefined}>
          {busy ? 'Saving…' : 'Save and continue'}
        </button>
      </form>
    </section>
  )
}
