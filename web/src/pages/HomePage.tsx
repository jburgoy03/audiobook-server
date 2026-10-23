import { useMemo, useState } from 'react'
import { Navigate, useLocation } from 'react-router'
import { AuthorCard } from '../components/AuthorCard'
import { BookTile, LibraryNotice } from '../components/BookTiles'
import { ShelfRow } from '../components/ShelfRow'
import { groupByAuthor } from '../library/authors'
import { ContinueShelf } from '../library/ContinueShelf'
import { LIBRARY_PATH } from '../library/libraryQuery'
import {
  randomSeed,
  recentlyAdded,
  SHORT_LISTEN_SECONDS,
  shortListens,
  startSomethingNew,
  topAuthors,
} from '../library/shelves'
import { statusOf, useBooks, useProgress } from '../library/useLibrary'
import { useActiveBookId } from '../player/nowPlaying'

/**
 * The home page: what you're listening to, then short rows that each open onto
 * part of the library, so nobody has to scroll every book to find one. The full
 * list is at /library (All books), reached from each row's See all, the links
 * under the search box (phones and tablets), the sidebar, or by searching.
 *
 * Shelves with nothing on them are left out, so a new or small library doesn't
 * show empty rows.
 */
export function HomePage() {
  const { search } = useLocation()
  const { books, error } = useBooks()
  const { progress, entries } = useProgress(books)
  const activeId = useActiveBookId()
  // Once per mount, not inside the memo below: that reruns whenever progress
  // changes (every few seconds while something plays), which would reshuffle
  // the row under the listener. A new visit or a reload gets a new order.
  const [seed] = useState(randomSeed)

  const shelves = useMemo(() => {
    const all = books ?? []
    const recent = recentlyAdded(all)
    const authors = groupByAuthor(all)
    return {
      recent,
      fresh: startSomethingNew(all, statusOf, seed, new Set(recent.map((b) => b.id))),
      short: shortListens(all, statusOf),
      authors,
      topAuthors: topAuthors(authors),
    }
    // statusOf reads the progress store, whose snapshot is `entries`.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [books, entries, seed])

  // The library used to live here, so old links and bookmarks with a search or
  // filter (/?q=…) go on to where it lives now.
  const params = new URLSearchParams(search)
  if (params.has('q') || params.has('sort') || params.has('status')) {
    return <Navigate to={`${LIBRARY_PATH}${search}`} replace />
  }

  const notice = <LibraryNotice books={books} error={error} />
  if (!books || books.length === 0 || error) return notice

  const tile = (id: string) => {
    const book = books.find((b) => b.id === id)!
    return (
      <li key={id}>
        <BookTile book={book} progress={progress.get(id)} active={id === activeId} />
      </li>
    )
  }
  const shortHours = SHORT_LISTEN_SECONDS / 3600

  return (
    <>
      <ContinueShelf books={books} progress={progress} entries={entries} />

      <ShelfRow id="recent-heading" title="Recently added" seeAll={`${LIBRARY_PATH}?sort=added`}>
        {shelves.recent.map((b) => tile(b.id))}
      </ShelfRow>

      {shelves.fresh.length > 0 && (
        <ShelfRow id="fresh-heading" title="Start something new" seeAll={`${LIBRARY_PATH}?status=unstarted`}>
          {shelves.fresh.map((b) => tile(b.id))}
        </ShelfRow>
      )}

      {shelves.short.length > 0 && (
        <ShelfRow
          id="short-heading"
          title="Short listens"
          note={`under ${shortHours} hours`}
          seeAll={`${LIBRARY_PATH}?sort=length&status=unstarted&status=inProgress`}
        >
          {shelves.short.map((b) => tile(b.id))}
        </ShelfRow>
      )}

      {shelves.topAuthors.length > 0 && (
        <ShelfRow id="authors-heading" title="Authors" seeAll="/authors">
          {shelves.topAuthors.map((a) => (
            <li key={a.slug}>
              <AuthorCard author={a} />
            </li>
          ))}
        </ShelfRow>
      )}
    </>
  )
}
