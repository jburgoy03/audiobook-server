import { useMemo } from 'react'
import { Link, useParams } from 'react-router'
import { BookTiles, LibraryNotice, ViewToggle } from '../components/BookTiles'
import { BackIcon } from '../components/Icons'
import { groupByAuthor } from '../library/authors'
import { formatLength } from '../library/format'
import { compareBooks } from '../library/search'
import { useBooks, useLibraryView, useProgress } from '../library/useLibrary'
import { useActiveBookId } from '../player/nowPlaying'

/**
 * One author's books, at /authors/:slug, in title order. Title order keeps a
 * series together when its titles share a name ("Mistborn: …"); real series
 * order waits for series data.
 */
export function AuthorPage() {
  const { slug = '' } = useParams()
  const { books, error } = useBooks()
  const { progress } = useProgress(books)
  const [view, changeView] = useLibraryView()
  const activeId = useActiveBookId()

  const author = useMemo(() => groupByAuthor(books ?? []).find((a) => a.slug === slug) ?? null, [books, slug])
  const sorted = useMemo(() => (author ? [...author.books].sort(compareBooks('title')) : []), [author])

  const back = (
    <Link to="/authors" className="back">
      <BackIcon />
      Authors
    </Link>
  )

  if (!books || books.length === 0 || error) return <LibraryNotice books={books} error={error} />
  if (!author) {
    return (
      <>
        {back}
        <p className="notice">No books by this author. They may have been renamed or removed by a rescan.</p>
      </>
    )
  }

  const seconds = author.books.reduce((sum, b) => sum + b.durationSeconds, 0)
  const count = author.books.length

  return (
    <>
      {back}
      <section className="shelf" aria-labelledby="author-heading">
        <div className="section-head">
          <h2 id="author-heading">{author.name}</h2>
          <div className="section-head-end">
            <span className="section-count">
              {count} {count === 1 ? 'book' : 'books'}
              <span className="section-count-extra">, {formatLength(seconds)}</span>
            </span>
            <ViewToggle view={view} onChange={changeView} />
          </div>
        </div>
        <BookTiles books={sorted} view={view} progress={progress} activeId={activeId} hideAuthor />
      </section>
    </>
  )
}
