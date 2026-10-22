import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router'
import { api } from '../api/client'
import type { BookDetail, BookSummary } from '../api/types'
import { Cover } from '../components/Cover'
import { PlayPauseIcon } from '../components/Icons'
import { ChapterTimeline } from '../player/ChapterTimeline'
import { useActivate, useActiveBookId, useLivePlayer } from '../player/nowPlaying'
import { progressStore } from '../player/progress'
import { chapterIndexAt, clampPosition } from '../player/timeline'
import { formatLength } from './format'
import type { Progress } from './useLibrary'

// The featured book plus up to three more, which fills one row of the
// six-column grid (each takes two columns).
const MAX_IN_PROGRESS = 4

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
 *
 * The bar is a seek bar, as in the player: dragging shows the drag position and
 * seeks once, on release. On the active book that's a live seek; otherwise it
 * makes the book active at that point, paused (the book page's idle rule).
 *
 * `fresh`: a book nobody has started (the first-visit pick). Same layout, but the
 * button says Play and the line under the bar gives its length, not 0% listened.
 * Once it plays it's the active book, and this is an ordinary feature again.
 */
function FeaturedBook({
  book,
  savedPosition,
  fresh = false,
}: {
  book: BookSummary
  savedPosition: number
  fresh?: boolean
}) {
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

  // While dragging, everything shows the drag position.
  const [scrub, setScrub] = useState<number | null>(null)
  const position = scrub ?? (live ? live.position : savedPosition)
  // Seeking an inactive book needs its file list, to start in the right file.
  const seekable = live !== null || detail !== null

  const commitScrub = () => {
    if (scrub === null) return
    const target = scrub
    setScrub(null)
    if (live) live.seek(target)
    else if (detail) activate(detail, { startAt: clampPosition(detail.files, target), autoplay: false })
  }
  const fraction = book.durationSeconds > 0 ? position / book.durationSeconds : 0
  const chapters = detail?.chapters ?? []
  const chapterIndex = chapterIndexAt(chapters, position)
  const chapter = chapterIndex >= 0 ? chapters[chapterIndex] : undefined
  const href = `/books/${book.id}`
  const playing = live?.playing ?? false
  const unstarted = fresh && !live

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
        <ChapterTimeline
          chapters={chapters}
          total={book.durationSeconds}
          value={position}
          onScrub={seekable ? setScrub : undefined}
          onCommit={seekable ? commitScrub : undefined}
        />
        <p className="times">
          {unstarted ? (
            <>
              <span>
                {book.chapters} {book.chapters === 1 ? 'chapter' : 'chapters'}
              </span>
              <span>{formatLength(book.durationSeconds)}</span>
            </>
          ) : (
            <>
              <span>{Math.floor(fraction * 100)}% listened</span>
              <span>{formatLength(book.durationSeconds - position)} left</span>
            </>
          )}
        </p>
        <button
          type="button"
          className="resume"
          onClick={onButton}
          aria-busy={starting || (live?.loading && !playing) ? true : undefined}
        >
          <PlayPauseIcon playing={playing} size={20} />
          {playing ? 'Pause' : unstarted ? 'Play' : 'Resume'}
        </button>
      </div>
    </article>
  )
}

/**
 * A book in progress as a compact item: the whole cover is a play button that
 * starts the book right here; the title and author link to the book's page.
 * The play icon on the cover is only a hint (hover, or always on touch).
 *
 * Used for Up next beside or under the featured book, and on phones for the
 * whole Continue listening shelf, the active book included. So it follows the
 * player: on the active book the cover pauses and resumes it, and its progress
 * and time left are live. `progress` is undefined for an active book that hasn't
 * yet been saved past the minute (see LibraryPage).
 */
function ContinueItem({ book, progress }: { book: BookSummary; progress?: Progress }) {
  const activate = useActivate()
  const live = useLivePlayer(book.id)
  const [starting, setStarting] = useState(false)
  const position = live ? live.position : (progress?.position ?? 0)
  const fraction = book.durationSeconds > 0 ? position / book.durationSeconds : 0
  const playing = live?.playing ?? false

  const play = async () => {
    setStarting(true)
    try {
      activate(await api.book(book.id), { autoplay: true })
    } catch {
      // Leave it; the book page will show the real error.
    } finally {
      setStarting(false)
    }
  }

  return (
    <li data-active={live ? '' : undefined}>
      <button
        type="button"
        className="continue-cover"
        aria-label={`${playing ? 'Pause' : 'Play'} ${book.title}`}
        aria-busy={starting || (live?.loading && !playing) ? true : undefined}
        onClick={() => {
          if (live) live.toggle()
          else if (!starting) void play()
        }}
      >
        <Cover bookId={book.id} title={book.title} hasCover={book.hasCover} progress={fraction} />
        <span className="continue-play" aria-hidden="true">
          <span className="continue-play-disc">
            <PlayPauseIcon playing={playing} size={22} />
          </span>
        </span>
      </button>
      <Link to={`/books/${book.id}`} className="continue-item">
        <span className="continue-text">
          <span className="book-title">{book.title}</span>
          <span className="muted">{book.author ?? 'Unknown author'}</span>
          <span className="continue-left">{formatLength(book.durationSeconds - position)} left</span>
        </span>
      </Link>
    </li>
  )
}

/**
 * The top of the home page: the book you're listening to as a feature, the rest
 * in progress beside or under it, or on a first visit the newest book.
 */
export function ContinueShelf({
  books,
  progress,
  entries,
}: {
  books: BookSummary[]
  progress: Map<string, Progress>
  /** The progress store's snapshot, so the memos below follow it. */
  entries: unknown
}) {
  // The active book leads, playing or paused, however briefly it has played:
  // the now-playing bar already shows it, so the library must agree. Saved
  // positions can't decide that, since the player saves every 30s and a book
  // only counts as started after a minute. The rest follow by last activity.
  // Their saved positions are exact: the player saves on pause and when
  // another book replaces it.
  const activeId = useActiveBookId()
  const inProgress = useMemo(() => {
    const active = activeId ? books.find((b) => b.id === activeId) : undefined
    const rest = books
      .filter((b) => b.id !== activeId && progress.has(b.id))
      .sort((a, b) => progress.get(b.id)!.lastActivity.localeCompare(progress.get(a.id)!.lastActivity))
    return (active ? [active, ...rest] : rest).slice(0, MAX_IN_PROGRESS)
  }, [books, progress, activeId])

  // A first visit (every new listener of the public library) has nothing to
  // continue, so the newest book stands in: a heuristic, chosen because it needs
  // no curation and changes as books are added. Finished books aren't offered.
  const newest = useMemo(() => {
    if (inProgress.length > 0) return null
    let pick: BookSummary | null = null
    for (const b of books) {
      if (progressStore.resumePoint(b.id)?.isFinished) continue
      if (!pick || b.addedAt > pick.addedAt) pick = b
    }
    return pick
    // resumePoint reads the store, whose snapshot is `entries`.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [books, inProgress, entries])

  const [featured, ...others] = inProgress

  return (
    <>
      {!featured && newest && (
        <section className="shelf" aria-labelledby="newest-heading">
          <div className="section-head">
            <h2 id="newest-heading">Newly added</h2>
          </div>
          <FeaturedBook key={newest.id} book={newest} savedPosition={0} fresh />
        </section>
      )}

      {featured && (
        <section className="shelf continue-shelf" aria-labelledby="continue-heading">
          <div className="section-head">
            <h2 id="continue-heading">Continue listening</h2>
          </div>
          <FeaturedBook
            key={featured.id}
            book={featured}
            savedPosition={progress.get(featured.id)?.position ?? 0}
          />

          {/* Phones: every book in progress as a compact row, the active one
              first, in place of the feature and Up next (CSS shows one or the
              other; see "Continue listening on phones" in index.css). */}
          <ul className="continue-list continue-compact">
            {inProgress.map((b) => (
              <ContinueItem key={b.id} book={b} progress={progress.get(b.id)} />
            ))}
          </ul>

          {/* Under the feature, or beside it as "Up next" when the sidebar layout
              leaves room (see "Sidebar" in index.css). */}
          {others.length > 0 && (
            <div className="continue-next">
              <h3 className="continue-next-head">Up next</h3>
              <ul className="shelf-list continue-list continue-more grid">
                {others.map((b) => (
                  <ContinueItem key={b.id} book={b} progress={progress.get(b.id)} />
                ))}
              </ul>
            </div>
          )}
        </section>
      )}
    </>
  )
}
