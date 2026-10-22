import { Link } from 'react-router'
import { authorHref, coverBooks, type Author } from '../library/authors'
import { formatLength } from '../library/format'
import { Cover } from './Cover'

/**
 * An author as a card: up to three of their covers fanned like a stack of
 * books, the name, and how many books. One book shows its cover alone.
 */
export function AuthorCard({ author, showLength = false }: { author: Author; showLength?: boolean }) {
  const covers = coverBooks(author, 3)
  const count = author.books.length
  const seconds = author.books.reduce((sum, b) => sum + b.durationSeconds, 0)
  return (
    <Link to={authorHref(author.name)} className="author-card">
      <span className="author-stack" data-count={covers.length}>
        {covers.map((b) => (
          <Cover key={b.id} bookId={b.id} title={b.title} hasCover={b.hasCover} />
        ))}
      </span>
      <span className="book-title">{author.name}</span>
      <span className="muted">
        {count} {count === 1 ? 'book' : 'books'}
        {showLength && `, ${formatLength(seconds)}`}
      </span>
    </Link>
  )
}
