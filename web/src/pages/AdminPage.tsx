import { useEffect, useRef, useState, type FormEvent } from 'react'
import { api, HttpError, UnauthorizedError } from '../api/client'
import type { AdminUser, Library, ScanReport, TemporaryPassword } from '../api/types'
import { useAuth } from '../auth/auth'

/**
 * Everything the admin does routinely: hand out accounts, and manage libraries.
 * Rendered only for admins (see App), but that's convenience: every call here
 * goes to an endpoint that enforces the Admin policy itself.
 */
export function AdminPage() {
  return (
    <div className="admin">
      <h1 className="admin-title">Admin</h1>
      <UsersSection />
      <LibrariesSection />
    </div>
  )
}

// ---------- Listeners ----------

type Grant = TemporaryPassword & { kind: 'created' | 'reset' }
type Pending = { id: string; action: 'reset' | 'disable' }

function UsersSection() {
  const auth = useAuth()
  const self = auth.status === 'signedIn' ? auth.username : null

  const [users, setUsers] = useState<AdminUser[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [name, setName] = useState('')
  const [adding, setAdding] = useState(false)
  // The one-time passphrase lives here and nowhere else: not in storage, not in
  // the URL. Leaving the page drops it.
  const [grant, setGrant] = useState<Grant | null>(null)
  const [confirming, setConfirming] = useState<Pending | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    api
      .adminUsers(controller.signal)
      .then(setUsers)
      .catch((e: unknown) => {
        if (!controller.signal.aborted) setError(describe(e))
      })
    return () => controller.abort()
  }, [])

  const refresh = async () => {
    try {
      setUsers(await api.adminUsers())
    } catch (e) {
      setError(describe(e))
    }
  }

  const onAdd = async (e: FormEvent) => {
    e.preventDefault()
    setAdding(true)
    setError(null)
    try {
      const created = await api.createUser(name.trim())
      setGrant({ ...created, kind: 'created' })
      setName('')
      await refresh()
    } catch (err) {
      setError(describe(err))
    } finally {
      setAdding(false)
    }
  }

  const act = async (user: AdminUser, action: 'reset' | 'disable' | 'enable') => {
    setConfirming(null)
    setBusyId(user.id)
    setError(null)
    try {
      if (action === 'reset') setGrant({ ...(await api.resetPassword(user.id)), kind: 'reset' })
      else if (action === 'disable') await api.disableUser(user.id)
      else await api.enableUser(user.id)
      await refresh()
    } catch (err) {
      setError(describe(err))
    } finally {
      setBusyId(null)
    }
  }

  return (
    <section className="admin-section" aria-labelledby="listeners-heading">
      <div className="section-head">
        <h2 id="listeners-heading">Listeners</h2>
        {users && <span className="section-count">{users.length}</span>}
      </div>

      <form className="admin-add" onSubmit={onAdd}>
        <label className="field">
          <span>Name</span>
          <input
            name="new-username"
            autoComplete="off"
            autoCapitalize="none"
            spellCheck={false}
            required
            value={name}
            onChange={(e) => setName(e.target.value)}
          />
        </label>
        <button type="submit" className="pill pill-solid" disabled={adding || !name.trim()}>
          {adding ? 'Adding…' : 'Add listener'}
        </button>
      </form>

      {grant && <GrantPanel grant={grant} onDone={() => setGrant(null)} />}
      {error && (
        <p className="admin-error" role="alert">
          {error}
        </p>
      )}

      {users && (
        <ul className="admin-list">
          {users.map((user) => {
            const isSelf = user.username === self
            const busy = busyId === user.id
            const pending = confirming?.id === user.id ? confirming.action : null
            return (
              <li key={user.id} className="admin-row">
                <div className="admin-row-text">
                  <span className="admin-row-name">{user.username}</span>
                  <span className="admin-row-meta muted">{describeUser(user, isSelf)}</span>
                </div>

                {/* Not for your own account: resetting it would sign you out
                    with a passphrase only this screen shows, and disabling it
                    is refused by the server. The admin command covers both. */}
                {!isSelf && (
                  <div className="admin-row-actions">
                    <button
                      type="button"
                      className="pill"
                      disabled={busy}
                      onClick={() => setConfirming({ id: user.id, action: 'reset' })}
                    >
                      Reset passphrase
                    </button>
                    {user.disabled ? (
                      <button type="button" className="pill" disabled={busy} onClick={() => void act(user, 'enable')}>
                        Enable
                      </button>
                    ) : (
                      <button
                        type="button"
                        className="pill"
                        disabled={busy}
                        onClick={() => setConfirming({ id: user.id, action: 'disable' })}
                      >
                        Disable
                      </button>
                    )}
                  </div>
                )}

                {pending && (
                  <div className="admin-confirm" role="group" aria-label="Confirm">
                    <p>
                      {pending === 'reset'
                        ? `Give ${user.username} a new temporary passphrase? Their current one stops working and they’re signed out everywhere.`
                        : `Disable ${user.username}? They can’t sign in, and open sessions end within five minutes.`}
                    </p>
                    <div className="admin-confirm-actions">
                      <button type="button" className="pill pill-solid" onClick={() => void act(user, pending)}>
                        {pending === 'reset' ? 'Reset' : 'Disable'}
                      </button>
                      <button type="button" className="pill pill-quiet" onClick={() => setConfirming(null)}>
                        Cancel
                      </button>
                    </div>
                  </div>
                )}
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}

function describeUser(user: AdminUser, isSelf: boolean): string {
  const parts: string[] = []
  if (isSelf) parts.push('you')
  if (user.isAdmin) parts.push('admin')
  if (user.disabled) parts.push('disabled')
  if (user.mustChangePassword) parts.push('hasn’t chosen a passphrase yet')
  parts.push(`added ${formatDate(user.createdAt)}`)
  parts.push(user.lastSeenAt ? `last listened ${formatDate(user.lastSeenAt)}` : 'hasn’t listened yet')
  return parts.join(' · ')
}

/**
 * The temporary passphrase, once. Copy needs the clipboard API, which browsers
 * only offer on HTTPS (and localhost): on the plain-HTTP Tailscale address the
 * button selects the text instead, ready for Ctrl+C or a long-press.
 */
function GrantPanel({ grant, onDone }: { grant: Grant; onDone: () => void }) {
  const secret = useRef<HTMLElement>(null)
  const [copied, setCopied] = useState(false)
  const canCopy = typeof navigator.clipboard?.writeText === 'function'

  const copy = async () => {
    if (canCopy) {
      try {
        await navigator.clipboard.writeText(grant.temporaryPassword)
        setCopied(true)
        return
      } catch {
        // Denied: fall back to selecting it.
      }
    }
    if (!secret.current) return
    const range = document.createRange()
    range.selectNodeContents(secret.current)
    const selection = window.getSelection()
    selection?.removeAllRanges()
    selection?.addRange(range)
  }

  return (
    <div className="grant" role="status">
      <p className="grant-lead">
        {grant.kind === 'created'
          ? `Added ${grant.username}. Their temporary passphrase:`
          : `New temporary passphrase for ${grant.username}:`}
      </p>
      <p className="grant-secret">
        <code ref={secret}>{grant.temporaryPassword}</code>
      </p>
      <p className="grant-note muted">
        They’ll choose their own when they first sign in. This won’t be shown again.
      </p>
      <div className="admin-confirm-actions">
        <button type="button" className="pill pill-solid" onClick={() => void copy()}>
          {copied ? 'Copied' : canCopy ? 'Copy' : 'Select'}
        </button>
        <button type="button" className="pill pill-quiet" onClick={onDone}>
          Done
        </button>
      </div>
    </div>
  )
}

// ---------- Libraries ----------

function LibrariesSection() {
  const [libraries, setLibraries] = useState<Library[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [adding, setAdding] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    api
      .libraries(controller.signal)
      .then(setLibraries)
      .catch((e: unknown) => {
        if (!controller.signal.aborted) setError(describe(e))
      })
    return () => controller.abort()
  }, [])

  const refresh = async () => {
    try {
      setLibraries(await api.libraries())
    } catch (e) {
      setError(describe(e))
    }
  }

  return (
    <section className="admin-section" aria-labelledby="libraries-heading">
      <div className="section-head">
        <h2 id="libraries-heading">Libraries</h2>
        {libraries && <span className="section-count">{libraries.length}</span>}
      </div>

      {error && (
        <p className="admin-error" role="alert">
          {error}
        </p>
      )}

      {libraries && (
        <ul className="admin-list">
          {libraries.map((library) => (
            <LibraryRow key={library.id} library={library} onChange={refresh} />
          ))}
        </ul>
      )}

      {adding ? (
        <AddLibraryForm
          onCancel={() => setAdding(false)}
          onAdded={async () => {
            setAdding(false)
            await refresh()
          }}
        />
      ) : (
        <button type="button" className="pill admin-add-library" onClick={() => setAdding(true)}>
          Add library
        </button>
      )}
    </section>
  )
}

/**
 * Only the public site is HTTPS: development and the Tailscale address are
 * plain HTTP. So HTTPS means the request goes through Cloudflare, which cuts it
 * off at 100 seconds, and the scan runs inside the request.
 */
const throughCloudflare = window.location.protocol === 'https:'

function LibraryRow({ library, onChange }: { library: Library; onChange: () => Promise<void> }) {
  const [credit, setCredit] = useState(library.credit ?? '')
  const [confirmingPublic, setConfirmingPublic] = useState(false)
  const [busy, setBusy] = useState<null | 'visibility' | 'credit' | 'scan' | 'force'>(null)
  const [report, setReport] = useState<ScanReport | null>(null)
  const [error, setError] = useState<string | null>(null)

  const run = async (what: NonNullable<typeof busy>, work: () => Promise<void>) => {
    setBusy(what)
    setError(null)
    try {
      await work()
      await onChange()
    } catch (e) {
      setError(describe(e))
    } finally {
      setBusy(null)
    }
  }

  const setPublic = (isPublic: boolean) => {
    setConfirmingPublic(false)
    void run('visibility', async () => {
      await api.updateLibrary(library.id, { isPublic })
    })
  }

  const saveCredit = (e: FormEvent) => {
    e.preventDefault()
    // An empty credit removes it; the server treats "" that way.
    void run('credit', async () => {
      await api.updateLibrary(library.id, { credit: credit.trim() })
    })
  }

  const scan = (force: boolean) => {
    setReport(null)
    void run(force ? 'force' : 'scan', async () => {
      setReport(await api.scanLibrary(library.id, force))
    })
  }

  const creditChanged = credit.trim() !== (library.credit ?? '')
  const scanning = busy === 'scan' || busy === 'force'

  return (
    <li className="admin-row library-row">
      <div className="admin-row-text">
        <span className="admin-row-name">{library.name}</span>
        <span className="admin-row-meta muted">
          {library.isPublic ? 'public: every listener sees it' : 'private: only admins see it'} ·{' '}
          {library.books} {library.books === 1 ? 'book' : 'books'} · {describeScan(library)}
        </span>
        <span className="admin-row-path">{library.rootPath}</span>
      </div>

      <div className="admin-row-actions">
        {library.isPublic ? (
          <button type="button" className="pill" disabled={busy !== null} onClick={() => setPublic(false)}>
            Make private
          </button>
        ) : (
          <button
            type="button"
            className="pill"
            disabled={busy !== null}
            onClick={() => setConfirmingPublic(true)}
          >
            Make public
          </button>
        )}
      </div>

      {confirmingPublic && (
        <div className="admin-confirm" role="group" aria-label="Confirm">
          <p>
            Make {library.name} public? Every listener will see its {library.books}{' '}
            {library.books === 1 ? 'book' : 'books'}, and any added to its folder later.
          </p>
          <div className="admin-confirm-actions">
            <button type="button" className="pill pill-solid" onClick={() => setPublic(true)}>
              Make public
            </button>
            <button type="button" className="pill pill-quiet" onClick={() => setConfirmingPublic(false)}>
              Cancel
            </button>
          </div>
        </div>
      )}

      <form className="library-credit" onSubmit={saveCredit}>
        <label className="field">
          <span>Credit, shown on each book</span>
          <input
            value={credit}
            placeholder="None"
            maxLength={200}
            onChange={(e) => setCredit(e.target.value)}
          />
        </label>
        {creditChanged && (
          <button type="submit" className="pill" disabled={busy !== null}>
            {busy === 'credit' ? 'Saving…' : 'Save'}
          </button>
        )}
      </form>

      <div className="library-scan">
        <div className="admin-confirm-actions">
          <button type="button" className="pill" disabled={busy !== null} onClick={() => scan(false)}>
            {busy === 'scan' ? 'Scanning…' : 'Scan'}
          </button>
          <button type="button" className="pill" disabled={busy !== null} onClick={() => scan(true)}>
            {busy === 'force' ? 'Rescanning…' : 'Force rescan'}
          </button>
        </div>
        <p className="library-scan-note muted">
          Scan picks up new and changed books. Force rescan re-reads every file; it’s for after scanner changes.
          {throughCloudflare &&
            ' Large scans can time out through the public site: use the Tailscale address, or admin scan on the server.'}
        </p>
        {scanning && <p className="library-scan-note muted">This can take a while for a big library.</p>}
        {report && <p className="library-report">{describeReport(report)}</p>}
        {error && (
          <p className="admin-error" role="alert">
            {error}
          </p>
        )}
      </div>
    </li>
  )
}

function describeScan(library: Library): string {
  const started = library.lastScanStartedAt
  const completed = library.lastScanCompletedAt
  if (!started && !completed) return 'never scanned'
  if (started && (!completed || Date.parse(started) > Date.parse(completed))) return `last scan didn’t finish (${formatDateTime(started)})`
  return `scanned ${formatDateTime(completed!)}`
}

function describeReport(r: ScanReport): string {
  const parts = [
    `${r.booksAdded} added`,
    `${r.booksUpdated} updated`,
    `${r.booksUnchanged} unchanged`,
    `${r.booksRemoved} removed`,
    r.failures === 1 ? '1 failure' : `${r.failures} failures`,
  ]
  if (r.filesDurationCorrected > 0) parts.push(`${r.filesDurationCorrected} mp3 durations corrected`)
  return parts.join(' · ')
}

function AddLibraryForm({ onCancel, onAdded }: { onCancel: () => void; onAdded: () => Promise<void> }) {
  const [name, setName] = useState('')
  const [rootPath, setRootPath] = useState('')
  const [credit, setCredit] = useState('')
  const [isPublic, setIsPublic] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await api.createLibrary({
        name: name.trim(),
        rootPath: rootPath.trim(),
        isPublic,
        credit: credit.trim() || null,
      })
      await onAdded()
    } catch (err) {
      // The server checks the path exists and says so.
      setError(describe(err))
      setBusy(false)
    }
  }

  return (
    <form className="library-new" onSubmit={onSubmit}>
      <label className="field">
        <span>Name</span>
        <input required value={name} onChange={(e) => setName(e.target.value)} />
      </label>
      <label className="field">
        <span>Folder on the server</span>
        <input
          required
          spellCheck={false}
          autoCapitalize="none"
          placeholder="/mnt/media/…"
          value={rootPath}
          onChange={(e) => setRootPath(e.target.value)}
        />
      </label>
      <label className="field">
        <span>Credit (optional)</span>
        <input maxLength={200} value={credit} onChange={(e) => setCredit(e.target.value)} />
      </label>
      <label className="check">
        <input type="checkbox" checked={isPublic} onChange={(e) => setIsPublic(e.target.checked)} />
        <span>Public: every listener sees its books</span>
      </label>
      {error && (
        <p className="admin-error" role="alert">
          {error}
        </p>
      )}
      <div className="admin-confirm-actions">
        <button type="submit" className="pill pill-solid" disabled={busy}>
          {busy ? 'Adding…' : 'Add library'}
        </button>
        <button type="button" className="pill pill-quiet" onClick={onCancel}>
          Cancel
        </button>
      </div>
      <p className="library-scan-note muted">
        In a container, the folder must also be mounted (deploy/docker-compose.yml). Scan it once it’s added.
      </p>
    </form>
  )
}

// ---------- Shared ----------

/** A message fit to show. A 403 here means the session lost admin, not that something broke. */
function describe(e: unknown): string {
  if (e instanceof UnauthorizedError) return 'Your session ended.'
  if (e instanceof HttpError) {
    if (e.status === 403) return 'You don’t have access to this.'
    return e.detail ?? e.message
  }
  return 'The server couldn’t be reached. Try again in a moment.'
}

const dateFormat = new Intl.DateTimeFormat(undefined, { day: 'numeric', month: 'short', year: 'numeric' })
const dateTimeFormat = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' })

function formatDate(iso: string): string {
  return dateFormat.format(new Date(iso))
}

function formatDateTime(iso: string): string {
  return dateTimeFormat.format(new Date(iso))
}
