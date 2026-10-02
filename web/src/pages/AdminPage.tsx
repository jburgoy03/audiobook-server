import { useEffect, useRef, useState, type FormEvent } from 'react'
import { api, HttpError, UnauthorizedError } from '../api/client'
import type { AdminBook, AdminUser, Library, ScanReport, TemporaryPassword } from '../api/types'
import { useAuth } from '../auth/auth'

/**
 * Everything the admin does routinely: hand out accounts, manage libraries, and
 * correct books' titles and authors.
 * Rendered only for admins (see App), but that's convenience: every call here
 * goes to an endpoint that enforces the Admin policy itself.
 */
export function AdminPage() {
  // Both sections need the libraries (listeners for their grants), so the list
  // lives here: making a library public updates the grants panel too.
  const [libraries, setLibraries] = useState<Library[] | null>(null)
  const [librariesError, setLibrariesError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    api
      .libraries(controller.signal)
      .then(setLibraries)
      .catch((e: unknown) => {
        if (!controller.signal.aborted) setLibrariesError(describe(e))
      })
    return () => controller.abort()
  }, [])

  const refreshLibraries = async () => {
    try {
      setLibraries(await api.libraries())
    } catch (e) {
      setLibrariesError(describe(e))
    }
  }

  return (
    <div className="admin">
      <h1 className="admin-title">Admin</h1>
      <UsersSection libraries={libraries ?? []} />
      <LibrariesSection libraries={libraries} error={librariesError} refresh={refreshLibraries} />
      <BooksSection libraries={libraries} />
    </div>
  )
}

// ---------- Listeners ----------

type Grant = TemporaryPassword & { kind: 'created' | 'reset' }
type Pending = { id: string; action: 'reset' | 'disable' | 'libraries' }

function UsersSection({ libraries }: { libraries: Library[] }) {
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

  const hasPrivate = libraries.some((l) => !l.isPublic)

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
                  <span className="admin-row-meta muted">{describeUser(user, isSelf, libraries)}</span>
                </div>

                {/* Not for your own account: resetting it would sign you out
                    with a passphrase only this screen shows, and disabling it
                    is refused by the server. The admin command covers both. */}
                {!isSelf && (
                  <div className="admin-row-actions">
                    {/* An admin already sees every library. */}
                    {!user.isAdmin && hasPrivate && (
                      <button
                        type="button"
                        className="pill"
                        disabled={busy}
                        aria-expanded={pending === 'libraries'}
                        onClick={() =>
                          setConfirming(pending === 'libraries' ? null : { id: user.id, action: 'libraries' })
                        }
                      >
                        Libraries
                      </button>
                    )}
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

                {pending === 'libraries' && (
                  <LibraryAccessPanel
                    user={user}
                    libraries={libraries}
                    onCancel={() => setConfirming(null)}
                    onSaved={async () => {
                      setConfirming(null)
                      await refresh()
                    }}
                  />
                )}

                {(pending === 'reset' || pending === 'disable') && (
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

function describeUser(user: AdminUser, isSelf: boolean, libraries: Library[]): string {
  const parts: string[] = []
  if (isSelf) parts.push('you')
  if (user.isAdmin) parts.push('admin')
  else {
    // What they see beyond the public libraries, which everyone sees.
    const granted = libraries.filter((l) => !l.isPublic && user.libraryIds.includes(l.id))
    if (granted.length > 0) parts.push(`also sees ${granted.map((l) => l.name).join(', ')}`)
  }
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

/**
 * Which private libraries one listener may see. Public libraries are listed for
 * the whole picture but can't be unticked: everyone sees them. Saving sends the
 * complete set, and applies from the listener's next request; nobody is signed out.
 */
function LibraryAccessPanel({
  user,
  libraries,
  onCancel,
  onSaved,
}: {
  user: AdminUser
  libraries: Library[]
  onCancel: () => void
  onSaved: () => Promise<void>
}) {
  const [selected, setSelected] = useState(() => new Set(user.libraryIds))
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const original = new Set(user.libraryIds)
  const changed = selected.size !== original.size || [...selected].some((id) => !original.has(id))

  const toggle = (id: string, on: boolean) => {
    const next = new Set(selected)
    if (on) next.add(id)
    else next.delete(id)
    setSelected(next)
  }

  const save = async () => {
    setSaving(true)
    setError(null)
    try {
      await api.setUserLibraries(user.id, [...selected])
      await onSaved()
    } catch (e) {
      setError(describe(e))
      setSaving(false)
    }
  }

  return (
    <div className="admin-confirm" role="group" aria-label={`Libraries ${user.username} can see`}>
      <p>Which libraries can {user.username} see?</p>
      <ul className="access-list">
        {libraries.map((library) => (
          <li key={library.id}>
            <label className="access-option">
              <input
                type="checkbox"
                checked={library.isPublic || selected.has(library.id)}
                disabled={library.isPublic || saving}
                onChange={(e) => toggle(library.id, e.target.checked)}
              />
              <span>{library.name}</span>
              <span className="muted">
                {library.isPublic ? 'public, everyone' : `${library.books} ${library.books === 1 ? 'book' : 'books'}`}
              </span>
            </label>
          </li>
        ))}
      </ul>
      <p className="access-note muted">Applies at once. They stay signed in.</p>
      {error && (
        <p className="admin-error" role="alert">
          {error}
        </p>
      )}
      <div className="admin-confirm-actions">
        <button type="button" className="pill pill-solid" disabled={!changed || saving} onClick={() => void save()}>
          {saving ? 'Saving…' : 'Save'}
        </button>
        <button type="button" className="pill pill-quiet" disabled={saving} onClick={onCancel}>
          Cancel
        </button>
      </div>
    </div>
  )
}

// ---------- Libraries ----------

function LibrariesSection({
  libraries,
  error,
  refresh,
}: {
  libraries: Library[] | null
  error: string | null
  refresh: () => Promise<void>
}) {
  const [adding, setAdding] = useState(false)

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
          {library.isPublic ? 'public: every listener sees it' : 'private: admins and listeners you grant it to'} ·{' '}
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

// ---------- Books ----------

/**
 * Fixing what the scanner got wrong (titles and authors from bad tags or folder
 * names), and fetching blurbs. Overrides and blurbs live in columns the scanner
 * never writes, so rescans keep them. The editor shows the scanned value under
 * each field, so it's clear what's being replaced.
 *
 * Reloads when the libraries change (a scan, a library added), which is when the
 * book list can change. A plain filter, because the point is to find one book.
 */
function BooksSection({ libraries }: { libraries: Library[] | null }) {
  const [books, setBooks] = useState<AdminBook[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [query, setQuery] = useState('')
  const [editingId, setEditingId] = useState<string | null>(null)

  useEffect(() => {
    if (libraries === null) return
    const controller = new AbortController()
    api
      .adminBooks(controller.signal)
      .then(setBooks)
      .catch((e: unknown) => {
        if (!controller.signal.aborted) setError(describe(e))
      })
    return () => controller.abort()
  }, [libraries])

  const replace = (saved: AdminBook) =>
    setBooks((all) => all?.map((b) => (b.id === saved.id ? saved : b)) ?? null)

  const needle = query.trim().toLocaleLowerCase()
  const shown =
    books?.filter(
      (b) =>
        !needle ||
        [b.title, b.author, b.scannedTitle, b.scannedAuthor, b.relativePath].some((s) =>
          s?.toLocaleLowerCase().includes(needle),
        ),
    ) ?? []
  const multipleLibraries = (libraries?.length ?? 0) > 1

  return (
    <section className="admin-section" aria-labelledby="books-heading">
      <div className="section-head">
        <h2 id="books-heading">Books</h2>
        {books && <span className="section-count">{books.length}</span>}
      </div>

      <div className="admin-add">
        <label className="field">
          <span>Find a book</span>
          <input
            type="search"
            autoComplete="off"
            spellCheck={false}
            placeholder="Title, author or folder"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
          />
        </label>
      </div>

      {books && <BulkBlurbs books={books} onBook={replace} />}

      {error && (
        <p className="admin-error" role="alert">
          {error}
        </p>
      )}

      {books && shown.length === 0 && <p className="muted book-none">No books match.</p>}

      {books && (
        <ul className="admin-list">
          {shown.map((book) => {
            const editing = editingId === book.id
            const edited = book.titleOverride !== null || book.authorOverride !== null
            const meta = [book.author ?? 'no author']
            if (multipleLibraries) meta.push(book.libraryName)
            if (edited) meta.push('edited')
            meta.push(book.descriptionSource ? `blurb from ${book.descriptionSource}` : 'no blurb')
            return (
              <li key={book.id} className="admin-row">
                <div className="admin-row-text">
                  <span className="admin-row-name">{book.title}</span>
                  <span className="admin-row-meta muted">{meta.join(' · ')}</span>
                  <span className="admin-row-path">{book.relativePath}</span>
                </div>
                <div className="admin-row-actions">
                  <button
                    type="button"
                    className="pill"
                    aria-expanded={editing}
                    onClick={() => setEditingId(editing ? null : book.id)}
                  >
                    Edit
                  </button>
                </div>
                {editing && (
                  <BookEditor
                    book={book}
                    onCancel={() => setEditingId(null)}
                    onChanged={replace}
                    onSaved={(saved) => {
                      replace(saved)
                      setEditingId(null)
                    }}
                  />
                )}
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}

type BulkState =
  | { phase: 'idle' }
  | { phase: 'running'; done: number; total: number; found: number }
  | { phase: 'finished'; total: number; found: number; missing: string[]; failed: string[]; stopped: boolean }

/** Stop after this many failures in a row: the catalogues are down, or rate-limiting us. */
const MAX_FAILURES_IN_A_ROW = 3

/**
 * Fetches a blurb for every book that has none, one request per book, in order.
 * One at a time is deliberate: it's gentle on Open Library, and no request comes
 * near Cloudflare's 100-second limit. Leaving the page stops it after the current book.
 */
function BulkBlurbs({ books, onBook }: { books: AdminBook[]; onBook: (book: AdminBook) => void }) {
  const [state, setState] = useState<BulkState>({ phase: 'idle' })
  const stop = useRef(false)

  useEffect(
    () => () => {
      stop.current = true
    },
    [],
  )

  const missing = books.filter((b) => !b.description)

  const run = async () => {
    const queue = [...missing]
    stop.current = false
    let found = 0
    let failuresInARow = 0
    const notFound: string[] = []
    const failed: string[] = []

    for (let i = 0; i < queue.length; i++) {
      if (stop.current) break
      setState({ phase: 'running', done: i, total: queue.length, found })
      const book = queue[i]
      try {
        const result = await api.fetchBlurb(book.id)
        onBook(result.book)
        failuresInARow = 0
        if (result.found) found++
        else notFound.push(book.title)
      } catch (e) {
        if (e instanceof UnauthorizedError) return
        failed.push(`${book.title} (${describe(e)})`)
        if (++failuresInARow >= MAX_FAILURES_IN_A_ROW) {
          stop.current = true
        }
      }
    }

    setState({
      phase: 'finished',
      total: queue.length,
      found,
      missing: notFound,
      failed,
      stopped: stop.current,
    })
  }

  const running = state.phase === 'running'

  return (
    <div className="blurb-bulk">
      <div className="admin-confirm-actions">
        <button
          type="button"
          className="pill"
          disabled={running || missing.length === 0}
          onClick={() => void run()}
        >
          {missing.length === 0 ? 'Every book has a blurb' : `Fetch missing blurbs (${missing.length})`}
        </button>
        {running && (
          <button type="button" className="pill pill-quiet" onClick={() => (stop.current = true)}>
            Stop
          </button>
        )}
      </div>
      <p className="library-scan-note muted" role="status">
        {state.phase === 'idle' &&
          'From Open Library, then Google Books, by the title and author shown. Check them in each book’s editor.'}
        {state.phase === 'running' && `Fetching ${state.done + 1} of ${state.total}… ${state.found} found so far.`}
        {state.phase === 'finished' && describeBulk(state)}
      </p>
    </div>
  )
}

function describeBulk(s: Extract<BulkState, { phase: 'finished' }>): string {
  const parts = [`Found ${s.found} of ${s.total}.`]
  if (s.missing.length > 0) parts.push(`No blurb anywhere for: ${s.missing.join('; ')}.`)
  if (s.failed.length > 0) parts.push(`Couldn’t fetch: ${s.failed.join('; ')}.`)
  if (s.stopped) parts.push('Stopped early; run it again later for the rest.')
  return parts.join(' ')
}

/**
 * Title and author, prefilled with what listeners see now. An empty field, or one
 * set back to the scanned value, means no override: the server stores nothing and
 * the scanned value shows again (and follows future rescans). The blurb's buttons
 * act at once, apart from Save.
 */
function BookEditor({
  book,
  onCancel,
  onChanged,
  onSaved,
}: {
  book: AdminBook
  onCancel: () => void
  onChanged: (book: AdminBook) => void
  onSaved: (saved: AdminBook) => void
}) {
  const [title, setTitle] = useState(book.title)
  const [author, setAuthor] = useState(book.author ?? '')
  const [busy, setBusy] = useState<null | 'save' | 'fetch' | 'remove'>(null)
  const [error, setError] = useState<string | null>(null)
  const [blurbNote, setBlurbNote] = useState<string | null>(null)

  const changed = title.trim() !== book.title || author.trim() !== (book.author ?? '')

  const save = async (e: FormEvent) => {
    e.preventDefault()
    setBusy('save')
    setError(null)
    try {
      onSaved(await api.setBookMetadata(book.id, title, author))
    } catch (err) {
      setError(describe(err))
      setBusy(null)
    }
  }

  const blurb = async (action: 'fetch' | 'remove') => {
    setBusy(action)
    setError(null)
    setBlurbNote(null)
    try {
      if (action === 'fetch') {
        const result = await api.fetchBlurb(book.id)
        onChanged(result.book)
        setBlurbNote(
          result.found
            ? `Matched “${result.matchedTitle}”${result.matchedAuthor ? ` by ${result.matchedAuthor}` : ''}.`
            : 'No catalogue has a blurb for this title and author.',
        )
      } else {
        onChanged(await api.removeBlurb(book.id))
      }
    } catch (err) {
      setError(describe(err))
    } finally {
      setBusy(null)
    }
  }

  return (
    <form className="admin-confirm book-editor" onSubmit={save} aria-label={`Edit ${book.title}`}>
      <OverrideField
        label="Title"
        value={title}
        scanned={book.scannedTitle}
        maxLength={500}
        disabled={busy !== null}
        onChange={setTitle}
      />
      <OverrideField
        label="Author"
        value={author}
        scanned={book.scannedAuthor}
        maxLength={300}
        disabled={busy !== null}
        onChange={setAuthor}
      />
      <p className="access-note muted">Kept across rescans. Empty, or the scanned value, means no override.</p>

      <div className="blurb-admin">
        <span className="blurb-admin-label">
          Blurb{book.descriptionSource && <span className="muted"> · from {book.descriptionSource}</span>}
        </span>
        {book.description ? (
          <p className="blurb-admin-text">{book.description}</p>
        ) : (
          <p className="muted blurb-admin-none">None yet.</p>
        )}
        {blurbNote && <p className="muted blurb-admin-note">{blurbNote}</p>}
        <div className="admin-confirm-actions">
          <button type="button" className="pill" disabled={busy !== null || changed} onClick={() => void blurb('fetch')}>
            {busy === 'fetch' ? 'Fetching…' : book.description ? 'Fetch again' : 'Fetch blurb'}
          </button>
          {book.description && (
            <button type="button" className="pill" disabled={busy !== null} onClick={() => void blurb('remove')}>
              {busy === 'remove' ? 'Removing…' : 'Remove blurb'}
            </button>
          )}
        </div>
        {changed && <p className="muted blurb-admin-note">Save the title first: the search uses it.</p>}
      </div>

      {error && (
        <p className="admin-error" role="alert">
          {error}
        </p>
      )}
      <div className="admin-confirm-actions">
        <button type="submit" className="pill pill-solid" disabled={!changed || busy !== null}>
          {busy === 'save' ? 'Saving…' : 'Save'}
        </button>
        <button type="button" className="pill pill-quiet" disabled={busy !== null} onClick={onCancel}>
          {changed ? 'Cancel' : 'Close'}
        </button>
      </div>
    </form>
  )
}

function OverrideField({
  label,
  value,
  scanned,
  maxLength,
  disabled,
  onChange,
}: {
  label: string
  value: string
  scanned: string | null
  maxLength: number
  disabled: boolean
  onChange: (value: string) => void
}) {
  const differs = value.trim() !== '' && value.trim() !== (scanned ?? '')
  return (
    <div className="override-field">
      <label className="field">
        <span>{label}</span>
        <input
          value={value}
          maxLength={maxLength}
          spellCheck={false}
          placeholder={scanned ?? 'None'}
          disabled={disabled}
          onChange={(e) => onChange(e.target.value)}
        />
      </label>
      <p className="override-scanned muted">
        <span>Scanned: {scanned ? `“${scanned}”` : 'nothing'}</span>
        {differs && scanned && (
          <button type="button" className="pill pill-quiet" disabled={disabled} onClick={() => onChange(scanned)}>
            Use scanned
          </button>
        )}
      </p>
    </div>
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
