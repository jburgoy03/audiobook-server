import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router'
import { api } from '../api/client'
import type { BookSummary } from '../api/types'
import { Cover } from '../components/Cover'
import { loadSavedPosition } from '../player/storage'

/** "4h 12m", "38m". Rounded, because this is a glance, not a clock. */
function formatLength(seconds: number) {
  const minutes = Math.max(1, Math.round(seconds / 60))
  const h = Math.floor(minutes / 60)
  const m = minutes % 60
  return h > 0 ? (m > 0 ? `${h}h ${m}m` : `${h}h`) : `${m}m`
}

interface Progress {
  position: number
  fraction: number
  savedAt: string
}

// A book counts as started after a minute, and as finished within its last few
// seconds, matching the player's own resume rule.
const STARTED_AFTER_SECONDS = 60
const FINISHED_MARGIN_SECONDS = 5

function progressFor(book: BookSummary): Progress | null {
  const saved = loadSavedPosition(book.id)
  if (!saved || book.durationSeconds <= 0) return null
  if (saved.position < STARTED_AFTER_SECONDS) return null
  if (saved.position >= book.durationSeconds - FINISHED_MARGIN_SECONDS) return null
  return {
    position: saved.position,
    fraction: saved.position / book.durationSeconds,
    savedAt: saved.savedAt,
  }
}

export function LibraryPage() {
  const [books, setBooks] = useState<BookSummary[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    api
      .books(controller.signal)
      .then(setBooks)
      .catch((e: unknown) => {
        if (!controller.signal.aborted) setError(String(e))
      })
    return () => controller.abort()
  }, [])

  const progress = useMemo(() => {
    const map = new Map<string, Progress>()
    for (const book of books ?? []) {
      const p = progressFor(book)
      if (p) map.set(book.id, p)
    }
    return map
  }, [books])

  const inProgress = useMemo(
    () =>
      (books ?? [])
        .filter((b) => progress.has(b.id))
        .sort((a, b) => progress.get(b.id)!.savedAt.localeCompare(progress.get(a.id)!.savedAt))
        .slice(0, 3),
    [books, progress],
  )

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
    return (
      <p className="notice">
        No books yet. Add a library and run a scan, and they'll appear here.
      </p>
    )
  }

  return (
    <>
      {inProgress.length > 0 && (
        <section className="shelf" aria-labelledby="continue-heading">
          <h2 id="continue-heading">Continue listening</h2>
          <ul className="continue-list">
            {inProgress.map((b) => {
              const p = progress.get(b.id)!
              return (
                <li key={b.id}>
                  <Link to={`/books/${b.id}`} className="continue-item">
                    <Cover bookId={b.id} title={b.title} hasCover={b.hasCover} progress={p.fraction} />
                    <span className="continue-text">
                      <span className="book-title">{b.title}</span>
                      <span className="muted">{b.author ?? 'Unknown author'}</span>
                      <span className="continue-left">
                        {formatLength(b.durationSeconds - p.position)} left
                      </span>
                    </span>
                  </Link>
                </li>
              )
            })}
          </ul>
        </section>
      )}

      <section className="shelf" aria-labelledby="all-heading">
        <h2 id="all-heading">All books</h2>
        <ul className="book-grid">
          {books.map((b) => (
            <li key={b.id}>
              <Link to={`/books/${b.id}`} className="book-tile">
                <Cover
                  bookId={b.id}
                  title={b.title}
                  hasCover={b.hasCover}
                  progress={progress.get(b.id)?.fraction}
                />
                <span className="book-title">{b.title}</span>
                <span className="muted">{b.author ?? 'Unknown author'}</span>
                <span className="book-length">{formatLength(b.durationSeconds)}</span>
              </Link>
            </li>
          ))}
        </ul>
      </section>
    </>
  )
}
