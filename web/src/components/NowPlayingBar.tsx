import { Link, useLocation } from 'react-router'
import { useNowPlaying } from '../player/nowPlaying'
import { formatTime } from '../player/timeline'
import { Cover } from './Cover'
import { PlayPauseIcon, SkipIcon } from './Icons'

/**
 * A slim bar along the bottom while a book is active, so playback started on
 * one page can be paused from any other. Hidden on the active book's own page,
 * where the full player already is.
 *
 * Same alignment rule as the player's transport row: the book on the frame's
 * left edge, controls centred on the frame, time remaining on the right edge.
 */
export function NowPlayingBar() {
  const now = useNowPlaying()
  const { pathname } = useLocation()
  if (!now) return null

  const { book, player: p } = now
  const href = `/books/${book.id}`
  if (pathname === href) return null

  const chapter = book.chapters[p.chapterIndex]
  const fraction = p.total > 0 ? Math.min(p.position / p.total, 1) : 0

  return (
    <>
      {/* Keeps the end of the page from being hidden under the bar. */}
      <div className="now-bar-spacer" aria-hidden="true" />
      <section className="now-bar" aria-label="Now playing">
        <div className="now-bar-progress" aria-hidden="true">
          <span style={{ width: `${fraction * 100}%` }} />
        </div>
        <div className="now-bar-inner frame">
          <Link to={href} className="now-bar-book">
            <Cover bookId={book.id} title={book.title} hasCover={book.hasCover} className="now-bar-cover" />
            <span className="now-bar-text">
              <span className="now-bar-title">{book.title}</span>
              <span className="muted">{chapter?.title ?? book.author ?? ''}</span>
            </span>
          </Link>

          <div className="now-bar-controls">
            <button type="button" className="control" onClick={() => p.skip(-30)} aria-label="Back 30 seconds">
              <SkipIcon size={26} />
            </button>
            <button
              type="button"
              className="control control-play now-bar-play"
              onClick={p.toggle}
              aria-label={p.playing ? 'Pause' : 'Play'}
            >
              <PlayPauseIcon playing={p.playing} size={22} />
            </button>
            <button type="button" className="control" onClick={() => p.skip(30)} aria-label="Forward 30 seconds">
              <SkipIcon size={26} forward />
            </button>
          </div>

          <span className="now-bar-time">{formatTime((p.total - p.position) / p.rate)} left</span>
        </div>
      </section>
    </>
  )
}
