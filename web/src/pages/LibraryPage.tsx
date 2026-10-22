import { useMemo } from 'react'
import { BookTiles, LibraryNotice, ViewToggle } from '../components/BookTiles'
import { formatLength } from '../library/format'
import { isFiltering, useLibraryQuery } from '../library/libraryQuery'
import { compareBooks, matchesQuery, STATUS_LABELS } from '../library/search'
import { statusOf, useBooks, useLibraryView, useProgress } from '../library/useLibrary'
import { useActiveBookId } from '../player/nowPlaying'

/**
 * All books, at /library: every book, narrowed and ordered by the search, status
 * filters and sort in the URL (see libraryQuery.ts). The home page's shelves and
 * the sidebar's views all open onto this page with a query.
 */
export function LibraryPage() {
  const { books, error } = useBooks()
  const { progress, entries } = useProgress(books)
  const [view, changeView] = useLibraryView()
  const activeId = useActiveBookId()

  const [query, setQuery] = useLibraryQuery()
  const filtering = isFiltering(query)
  const shown = useMemo(() => {
    const statuses = query.statuses
    return (books ?? [])
      .filter((b) => matchesQuery(b, query.q) && (statuses.length === 0 || statuses.includes(statusOf(b.id))))
      .sort(compareBooks(query.sort))
    // statusOf reads the store, whose snapshot is `entries`.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [books, query, entries])

  if (!books || books.length === 0 || error) return <LibraryNotice books={books} error={error} />

  const totalSeconds = books.reduce((sum, b) => sum + b.durationSeconds, 0)
  // A single status with no search reads as that view (the sidebar's choices);
  // anything else is a set of results.
  const heading =
    query.q.trim() === '' && query.statuses.length === 1
      ? STATUS_LABELS[query.statuses[0]]
      : filtering
        ? 'Results'
        : 'All books'

  return (
    <section className="shelf" aria-labelledby="all-heading">
      <div className="section-head">
        <h2 id="all-heading">{heading}</h2>
        {/* Announced as it changes, so a screen reader hears the count while typing. */}
        <div className="section-head-end">
          <span className="section-count" aria-live="polite">
            {filtering ? (
              `${shown.length} of ${books.length}`
            ) : (
              <>
                {books.length} {books.length === 1 ? 'book' : 'books'}
                {/* The total is a nicety; a phone needs the room for the toggle. */}
                <span className="section-count-extra">, {formatLength(totalSeconds)}</span>
              </>
            )}
          </span>
          <ViewToggle view={view} onChange={changeView} />
        </div>
      </div>
      {shown.length === 0 && (
        <div className="library-empty">
          <p>
            {query.q.trim() !== ''
              ? `No books match “${query.q.trim()}”${query.statuses.length > 0 ? ' with these filters' : ''}.`
              : 'No books match these filters.'}
          </p>
          <button type="button" className="pill" onClick={() => setQuery({ q: '', statuses: [] })}>
            Clear
          </button>
        </div>
      )}
      <BookTiles books={shown} view={view} progress={progress} activeId={activeId} />
    </section>
  )
}
