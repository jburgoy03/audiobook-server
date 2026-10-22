import { useMemo } from 'react'
import { AuthorCard } from '../components/AuthorCard'
import { LibraryNotice } from '../components/BookTiles'
import { groupByAuthor } from '../library/authors'
import { useBooks } from '../library/useLibrary'

/**
 * Every author, A to Z, each with a few covers and a book count. Far shorter to
 * scan than every book: most authors have several.
 */
export function AuthorsPage() {
  const { books, error } = useBooks()
  const authors = useMemo(() => groupByAuthor(books ?? []), [books])

  if (!books || books.length === 0 || error) return <LibraryNotice books={books} error={error} />

  return (
    <section className="shelf" aria-labelledby="authors-heading">
      <div className="section-head">
        <h2 id="authors-heading">Authors</h2>
        <span className="section-count">{authors.length}</span>
      </div>
      <ul className="shelf-list grid">
        {authors.map((a) => (
          <li key={a.slug}>
            <AuthorCard author={a} />
          </li>
        ))}
      </ul>
    </section>
  )
}
