import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router'
import { api } from '../api/client'
import type { BookDetail, BookSummary } from '../api/types'
import { Cover } from '../components/Cover'
import { PlayPauseIcon } from '../components/Icons'
import { ChapterTimeline } from '../player/ChapterTimeline'
import { useActivate, useActiveBookId, useLivePlayer } from '../player/nowPlaying'
import { progressStore, useProgressEntries } from '../player/progress'
import { chapterIndexAt } from '../player/timeline'

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
  /** Latest activity from any device, for ordering. */
  lastActivity: string
}

// A display rule, not sync state: a book counts as started after a minute.
// Finished is the server's IsFinished, set when a book plays to its end.
// Neither applies to the active book, which is always first (see LibraryPage).
const STARTED_AFTER_SECONDS = 60

// The featured book plus up to three more, which fills one row of the
// six-column grid (each takes two columns).
const MAX_IN_PROGRESS = 4

/** Where this browser would resume the book: the same point Resume plays from. */
function progressFor(book: BookSummary): Progress | null {
  const point = progressStore.resumePoint(book.id)
  if (!point || book.durationSeconds <= 0) return null
  if (point.isFinished || point.position < STARTED_AFTER_SECONDS) return null
  return {
    position: point.position,
    fraction: point.position / book.durationSeconds,
    lastActivity: point.lastActivity,
  }
}

/**
 * The book you're listening to (the active one, playing or paused), or else the
 * one you last listened to, given the width of the page: the cover,
 * where you are (chapter and a chapter-segmented progress bar, the same picture
 * as the player's timeline), and a Resume button that plays it right here. Once
 * it's the active book, everything on it is live and the button pauses.
 *
 * The summary list has no chapters, so this fetches the one book's detail. Until
 * it arrives, the bar is drawn as a single segment and the chapter line is blank.
 *
 * `savedPosition` is only used while the book isn't active; an active book
 * always shows the player's own position, so nothing here waits for a save.
 */
function FeaturedBook({ book, savedPosition }: { book: BookSummary; savedPosition: number }) {
  const activate = useActivate()
  const live = useLivePlayer(book.id)
  const [detail, setDetail] = useState<BookDetail | null>(null)
  const [starting, setStarting] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    api
      .book(book.id, controller.signal)
      .then(setDetail)
      .catch(() => {
        // The feature still works without chapters.
      })
    return () => controller.abort()
  }, [book.id])

  const position = live ? live.position : savedPosition
  const fraction = book.durationSeconds > 0 ? position / book.durationSeconds : 0
  const chapters = detail?.chapters ?? []
  const chapterIndex = chapterIndexAt(chapters, position)
  const chapter = chapterIndex >= 0 ? chapters[chapterIndex] : undefined
  const href = `/books/${book.id}`
  const playing = live?.playing ?? false

  const onButton = async () => {
    if (live) {
      live.toggle()
      return
    }
    // Starting needs the file list. It's usually here already; if the click beat
    // the fetch, get it now. The click still counts as permission to play.
    setStarting(true)
    try {
      activate(detail ?? (await api.book(book.id)), { autoplay: true })
    } catch {
      // Leave the button as it was; the book page will show the real error.
    } finally {
      setStarting(false)
    }
  }

  return (
    <article className="feature grid" aria-labelledby={`feature-${book.id}`}>
      {/* The title link is the accessible one; the cover is a larger mouse target. */}
      <Link to={href} className="feature-cover" tabIndex={-1} aria-hidden="true">
        <Cover
          bookId={book.id}
          title={book.title}
          hasCover={book.hasCover}
          progress={fraction}
          className="cover-capped"
        />
      </Link>

      <div className="feature-head">
        <h3 className="feature-title" id={`feature-${book.id}`}>
          <Link to={href}>{book.title}</Link>
        </h3>
        <p className="muted">{book.author ?? 'Unknown author'}</p>
      </div>

      <div className="feature-progress">
        <p className="feature-chapter">
          {chapter ? (
            <>
              <span>{chapter.title}</span>
              <span className="muted">
                Chapter {chapterIndex + 1} of {chapters.length}
              </span>
            </>
          ) : (
            ' '
          )}
        </p>
        <ChapterTimeline chapters={chapters} total={book.durationSeconds} value={position} />
        <p className="times">
          <span>{Math.floor(fraction * 100)}% listened</span>
          <span>{formatLength(book.durationSeconds - position)} left</span>
        </p>
        <button
          type="button"
          className="resume"
          onClick={onButton}
          aria-busy={starting || (live?.loading && !playing) ? true : undefined}
        >
          <PlayPauseIcon playing={playing} size={20} />
          {playing ? 'Pause' : 'Resume'}
        </button>
      </div>
    </article>
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

  // Recomputed whenever the store changes: a refresh from the server can start,
  // move or finish books here. (The active book shows its live position anyway.)
  const entries = useProgressEntries()
  const progress = useMemo(() => {
    const map = new Map<string, Progress>()
    for (const book of books ?? []) {
      const p = progressFor(book)
      if (p) map.set(book.id, p)
    }
    return map
    // progressFor reads the store, whose snapshot is `entries`.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [books, entries])

  // The active book leads, playing or paused, however briefly it has played:
  // the now-playing bar already shows it, so the library must agree. Saved
  // positions can't decide that, since the player saves every 30s and a book
  // only counts as started after a minute. The rest follow by last activity.
  // Their saved positions are exact: the player saves on pause and when
  // another book replaces it.
  const activeId = useActiveBookId()
  const inProgress = useMemo(() => {
    const all = books ?? []
    const active = activeId ? all.find((b) => b.id === activeId) : undefined
    const rest = all
      .filter((b) => b.id !== activeId && progress.has(b.id))
      .sort((a, b) => progress.get(b.id)!.lastActivity.localeCompare(progress.get(a.id)!.lastActivity))
    return (active ? [active, ...rest] : rest).slice(0, MAX_IN_PROGRESS)
  }, [books, progress, activeId])

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

  const [featured, ...others] = inProgress
  const totalSeconds = books.reduce((sum, b) => sum + b.durationSeconds, 0)

  return (
    <>
      {featured && (
        <section className="shelf" aria-labelledby="continue-heading">
          <div className="section-head">
            <h2 id="continue-heading">Continue listening</h2>
          </div>
          <FeaturedBook
            key={featured.id}
            book={featured}
            savedPosition={progress.get(featured.id)?.position ?? 0}
          />

          {others.length > 0 && (
            <ul className="shelf-list continue-list continue-more grid">
              {others.map((b) => {
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
          )}
        </section>
      )}

      <section className="shelf" aria-labelledby="all-heading">
        <div className="section-head">
          <h2 id="all-heading">All books</h2>
          <span className="section-count">
            {books.length} {books.length === 1 ? 'book' : 'books'}, {formatLength(totalSeconds)}
          </span>
        </div>
        <ul className="shelf-list grid">
          {books.map((b) => (
            <li key={b.id}>
              <Link to={`/books/${b.id}`} className="book-tile">
                {b.id === activeId ? (
                  <LiveCover book={b} savedFraction={progress.get(b.id)?.fraction} />
                ) : (
                  <Cover
                    bookId={b.id}
                    title={b.title}
                    hasCover={b.hasCover}
                    progress={progress.get(b.id)?.fraction}
                  />
                )}
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
