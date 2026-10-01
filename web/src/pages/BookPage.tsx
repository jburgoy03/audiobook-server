import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router'
import { api } from '../api/client'
import type { BookDetail } from '../api/types'
import { Cover } from '../components/Cover'
import { BackIcon } from '../components/Icons'
import { Player } from '../player/Player'

export function BookPage() {
  const { id } = useParams()
  // Keyed by id: moving to another book remounts BookView, which resets its
  // state, so the fetch effect never has to clear the previous book by hand.
  return id ? <BookView key={id} id={id} /> : null
}

function BookView({ id }: { id: string }) {
  const [book, setBook] = useState<BookDetail | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    api
      .book(id, controller.signal)
      .then(setBook)
      .catch((e: unknown) => {
        if (!controller.signal.aborted) setError(String(e))
      })
    return () => controller.abort()
  }, [id])

  return (
    <>
      <Link to="/" className="back">
        <BackIcon />
        Library
      </Link>

      {error && (
        <p className="notice">
          This book couldn't be loaded. It may have been removed by a rescan.
          <br />
          <span className="muted">{error}</span>
        </p>
      )}
      {!error && !book && <p className="notice muted">Loading…</p>}

      {book && (
        <article className="book grid">
          <header className="book-aside">
            <Cover bookId={book.id} title={book.title} hasCover={book.hasCover} className="book-cover cover-capped" />
            <div className="book-heading">
              <h1>{book.title}</h1>
              {book.subtitle && <p className="book-subtitle">{book.subtitle}</p>}
              <p className="book-credit">by {book.author ?? 'Unknown author'}</p>
              {book.narrator && <p className="muted">Read by {book.narrator}</p>}
              {book.credit && <p className="muted">{book.credit}</p>}
            </div>
          </header>
          <div className="book-main">
            {/* The audio belongs to PlayerProvider; this is the book's controls,
                live if it's the active book and idle otherwise. */}
            <Player key={book.id} book={book} />
          </div>
        </article>
      )}
    </>
  )
}
