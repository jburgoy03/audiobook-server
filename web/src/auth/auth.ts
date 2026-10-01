import { useSyncExternalStore } from 'react'
import { flushSync } from 'react-dom'
import { api, HttpError, setUnauthorizedHandler, UnauthorizedError } from '../api/client'
import { progressStore } from '../player/progress'

/**
 * Whether anyone is signed in, as far as this tab knows. The cookie is HttpOnly,
 * so the only way to find out is to ask (GET /api/auth/me) at startup, and to
 * notice when any request answers 401.
 *
 * The app renders the player only while signed in (see App), so leaving the
 * signed-in state is what stops playback and removes the now-playing bar: the
 * player unmounts, and its cleanup pauses the audio and saves the position.
 */
export type AuthState =
  | { status: 'checking' }
  /** The API couldn't be reached at startup (not a 401). */
  | { status: 'unreachable'; message: string }
  | { status: 'signedOut'; expired?: boolean }
  | { status: 'signingOut' }
  | { status: 'signedIn'; username: string }

let state: AuthState = { status: 'checking' }
const listeners = new Set<() => void>()

function setState(next: AuthState) {
  state = next
  for (const listener of listeners) listener()
}

function subscribe(listener: () => void) {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

export function useAuth(): AuthState {
  return useSyncExternalStore(subscribe, () => state)
}

function signedIn(username: string) {
  setState({ status: 'signedIn', username })
  void progressStore.start()
}

// Any 401 while signed in: the session expired, or this user signed out in
// another tab. Pending positions stay in the browser and upload after sign-in.
setUnauthorizedHandler(() => {
  if (state.status !== 'signedIn') return
  progressStore.stop()
  setState({ status: 'signedOut', expired: true })
})

/** Asks the server whether the cookie is still good. Called once at startup. */
export async function checkSession(): Promise<void> {
  setState({ status: 'checking' })
  try {
    const me = await api.me(false)
    signedIn(me.username)
  } catch (e) {
    if (e instanceof UnauthorizedError) setState({ status: 'signedOut' })
    else setState({ status: 'unreachable', message: String(e) })
  }
}

/**
 * Re-checks the session after something failed in a way that can't report a
 * status: an <audio> element's error event says nothing about a 401. A 401 here
 * signs the app out through the handler above; other failures are left alone.
 */
export async function verifySession(): Promise<void> {
  if (state.status !== 'signedIn') return
  try {
    await api.me()
  } catch {
    // Handled (401) or not ours to handle (network).
  }
}

/** Resolves on success; throws an Error with a message fit to show on failure. */
export async function login(username: string, password: string): Promise<void> {
  try {
    await api.login(username, password)
  } catch (e) {
    if (e instanceof UnauthorizedError) throw new Error('That name and passphrase don’t match.', { cause: e })
    if (e instanceof HttpError && e.status === 429) {
      throw new Error(e.detail ?? 'Too many attempts. Try again in a few minutes.', { cause: e })
    }
    throw new Error('The server couldn’t be reached. Try again in a moment.', { cause: e })
  }
  const me = await api.me(false)
  signedIn(me.username)
}

export async function logout(): Promise<void> {
  if (state.status !== 'signedIn') return

  // flushSync renders the signed-out tree now, inside this call. The player
  // unmounts, and its layout cleanup starts the final position save, before
  // anything below runs. That save has to reach the server while the cookie is
  // still valid, so wait for it before signing out.
  flushSync(() => setState({ status: 'signingOut' }))
  await progressStore.flush()

  try {
    await api.logout()
  } catch {
    // The cookie may already be gone; signed out either way.
  }
  progressStore.stop()
  setState({ status: 'signedOut' })
}
