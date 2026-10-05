import { Link, NavLink, useLocation, useSearchParams } from 'react-router'
import { logout } from '../auth/auth'
import { parseLibraryQuery, writeLibraryQuery } from '../library/libraryQuery'
import { STATUS_LABELS, type BookStatus } from '../library/search'

/** The library's views, in sidebar order. "All books" is no status filter. */
const VIEWS: readonly { label: string; status: BookStatus | null }[] = [
  { label: 'All books', status: null },
  { label: STATUS_LABELS.inProgress, status: 'inProgress' },
  { label: STATUS_LABELS.unstarted, status: 'unstarted' },
  { label: STATUS_LABELS.finished, status: 'finished' },
]

/**
 * The desktop sidebar: wordmark, the library's views, and the account links at
 * the bottom. Shown from 64em; narrower screens keep the masthead (CSS decides).
 *
 * A view is the status filter as one choice: picking one keeps the search and
 * sort and replaces the status. The masthead's chips on narrow screens allow
 * several statuses at once; a combination like that highlights no view here.
 *
 * Room to grow, which is why it's a sidebar: collections (libraries) once the
 * book list carries a library id, and genres (docs/next-steps/library-search.md).
 */
export function Sidebar({ isAdmin }: { isAdmin: boolean }) {
  const { pathname } = useLocation()
  const [params] = useSearchParams()
  const onLibrary = pathname === '/'
  // Off the library page, a view starts a fresh library rather than carrying
  // the book page's (empty) params.
  const current = onLibrary ? parseLibraryQuery(params) : parseLibraryQuery(new URLSearchParams())

  const hrefFor = (status: BookStatus | null) => {
    const next = writeLibraryQuery(onLibrary ? params : new URLSearchParams(), {
      ...current,
      statuses: status ? [status] : [],
    }).toString()
    return next ? `/?${next}` : '/'
  }
  const isActive = (status: BookStatus | null) =>
    onLibrary &&
    (status === null ? current.statuses.length === 0 : current.statuses.length === 1 && current.statuses[0] === status)

  return (
    <aside className="sidebar">
      <Link to="/" className="wordmark">
        <span className="ribbon-mark" aria-hidden="true" />
        Audiobooks
      </Link>

      <nav className="sidebar-group" aria-labelledby="sidebar-library">
        <span className="sidebar-label" id="sidebar-library">
          Library
        </span>
        {VIEWS.map((view) => (
          <Link
            key={view.label}
            to={hrefFor(view.status)}
            className="sidebar-link"
            aria-current={isActive(view.status) ? 'page' : undefined}
          >
            {view.label}
          </Link>
        ))}
      </nav>

      <div className="sidebar-account">
        {/* Convenience only: the server enforces admin on every admin route. */}
        {isAdmin && (
          <NavLink to="/admin" className="sidebar-link sidebar-quiet">
            Admin
          </NavLink>
        )}
        <button type="button" className="sidebar-link sidebar-quiet" onClick={() => void logout()}>
          Sign out
        </button>
      </div>
    </aside>
  )
}
