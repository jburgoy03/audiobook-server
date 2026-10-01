import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router'
import { api } from '../api/client'
import type { BookDetail } from '../api/types'
import { Cover } from '../components/Cover'
import { BackIcon } from '../components/Icons'
import { Player } from '../player/Player'

export function BookPage() {
  const { id } = useParams()
  const [book, setBook] = useState<BookDetail | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!id) return
    const controller = new AbortController()
    setBook(null)
    setError(null)
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
        <article className="book">
          <header className="book-header">
            <Cover bookId={book.id} title={book.title} hasCover={book.hasCover} className="book-cover" />
            <div className="book-heading">
              <h1>{book.title}</h1>
              {book.subtitle && <p className="book-subtitle">{book.subtitle}</p>}
              <p className="book-credit">by {book.author ?? 'Unknown author'}</p>
              {book.narrator && <p className="muted">Read by {book.narrator}</p>}
            </div>
          </header>
          {/* key: a different book gets a fresh player and audio element */}
          <Player key={book.id} book={book} />
        </article>
      )}
    </>
  )
}
