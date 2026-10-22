import { Link } from 'react-router'
import type { BookSummary } from '../api/types'
import { formatLength } from '../library/format'
import type { Progress } from '../library/useLibrary'
import { useLivePlayer } from '../player/nowPlaying'
import type { LibraryView } from '../player/storage'
import { Cover } from './Cover'
import { GridIcon, ListIcon } from './Icons'

export function ViewToggle({ view, onChange }: { view: LibraryView; onChange: (view: LibraryView) => void }) {
  return (
    <div className="view-toggle" role="group" aria-label="Layout">
      <button type="button" aria-label="Covers" aria-pressed={view === 'grid'} onClick={() => onChange('grid')}>
        <GridIcon size={18} />
      </button>
      <button type="button" aria-label="List" aria-pressed={view === 'list'} onClick={() => onChange('list')}>
        <ListIcon size={18} />
      </button>
    </div>
  )
}

/**
 * A cover whose progress follows the player. Used only for the active book's
 * tile, so one small component re-renders on time updates, not the whole grid.
 */
function LiveCover({ book, savedFraction }: { book: BookSummary; savedFraction?: number }) {
  const live = useLivePlayer(book.id)
  const fraction =
    live && book.durationSeconds > 0 ? live.position / book.durationSeconds : savedFraction
  return <Cover bookId={book.id} title={book.title} hasCover={book.hasCover} progress={fraction} />
}

/** One book: cover, title, author and length, linking to its page. */
export function BookTile({
  book,
  progress,
  active,
  hideAuthor = false,
}: {
  book: BookSummary
  progress?: Progress
  active: boolean
  /** On an author's own page the name is the heading; repeating it on every tile is noise. */
  hideAuthor?: boolean
}) {
  return (
    <Link to={`/books/${book.id}`} className="book-tile">
      {active ? (
        <LiveCover book={book} savedFraction={progress?.fraction} />
      ) : (
        <Cover bookId={book.id} title={book.title} hasCover={book.hasCover} progress={progress?.fraction} />
      )}
      <span className="book-title">{book.title}</span>
      {!hideAuthor && <span className="muted">{book.author ?? 'Unknown author'}</span>}
      <span className="book-length">{formatLength(book.durationSeconds)}</span>
    </Link>
  )
}

/**
 * Books as covers or rows. One markup for both layouts: the same link, cover and
 * text, laid out as a tile or a row by CSS ("Library list" in index.css).
 */
export function BookTiles({
  books,
  view,
  progress,
  activeId,
  hideAuthor = false,
}: {
  books: readonly BookSummary[]
  view: LibraryView
  progress: Map<string, Progress>
  activeId: string | null
  hideAuthor?: boolean
}) {
  return (
    <ul className={view === 'list' ? 'book-list' : 'shelf-list grid'}>
      {books.map((b) => (
        <li key={b.id}>
          <BookTile book={b} progress={progress.get(b.id)} active={b.id === activeId} hideAuthor={hideAuthor} />
        </li>
      ))}
    </ul>
  )
}

/** Loading and failure, worded the same on every library page. Null once loaded. */
export function LibraryNotice({ books, error }: { books: BookSummary[] | null; error: string | null }) {
  if (error) {
    return (
      <p className="notice">
        The library couldn't be loaded. Check that the API is running, then reload.
        <br />
        <span className="muted">{error}</span>
      </p>
    )
  }
  if (!books) return <p className="notice muted">Loading library…</p>
  if (books.length === 0) {
    return <p className="notice">No books yet. Add a library and run a scan, and they'll appear here.</p>
  }
  return null
}
