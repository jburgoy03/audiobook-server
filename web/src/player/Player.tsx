import { useEffect, useRef, useState } from 'react'
import type { BookDetail } from '../api/types'
import { LevelMeter, NextIcon, PlayPauseIcon, PreviousIcon, SkipIcon } from '../components/Icons'
import { ChapterTimeline } from './ChapterTimeline'
import { JumpPrompt } from './JumpPrompt'
import { useActivate, useLivePlayer, type Activate } from './nowPlaying'
import { useProgressEntries } from './progress'
import { loadRate, saveRate } from './storage'
import { chapterIndexAt, clampPosition, formatTime, totalDuration } from './timeline'
import { resumePosition, type BookPlayer } from './useBookPlayer'

const RATES = [0.8, 1, 1.1, 1.25, 1.5, 1.75, 2]

// After the listener touches the chapter list, leave its scroll position alone
// for this long, so following playback never yanks the list out from under them.
const USER_SCROLL_GRACE_MS = 5_000

/**
 * Scrolls `list` (and only `list`, never the page) so `item` is visible. A short
 * move, like the next chapter starting just below the current one, scrolls the
 * least distance; a jump to an item out of view centres it, so there is context
 * on both sides.
 */
function revealWithin(list: HTMLElement, item: HTMLElement) {
  const l = list.getBoundingClientRect()
  const r = item.getBoundingClientRect()
  const above = r.top < l.top
  const below = r.bottom > l.bottom
  if (!above && !below) return
  const outOfView = r.bottom <= l.top || r.top >= l.bottom
  const delta = outOfView
    ? r.top + r.height / 2 - (l.top + l.height / 2)
    : above
      ? r.top - l.top
      : r.bottom - l.bottom
  list.scrollTop += delta
}

/**
 * Stand-in controls for a book that isn't the active one: they show where this
 * browser left off, and any action makes this book the active one from there.
 * Playing, or picking a chapter, starts it; scrubbing or skipping moves the
 * starting point and leaves it paused.
 *
 * Unlike the live player, this follows the progress store: an idle book is one
 * a server refresh is allowed to update.
 */
function useIdlePlayer(book: BookDetail, activate: Activate): BookPlayer {
  useProgressEntries()
  const position = resumePosition(book)
  const [rate, setRateState] = useState(loadRate)
  const total = totalDuration(book.files)
  const chapterIndex = chapterIndexAt(book.chapters, position)

  const start = (at: number, autoplay: boolean) =>
    activate(book, { startAt: clampPosition(book.files, at), autoplay })
  const startChapter = (i: number) => {
    const c = book.chapters[i]
    if (c) start(c.startOffsetSeconds, true)
  }

  return {
    position,
    total,
    playing: false,
    loading: false,
    error: null,
    rate,
    chapterIndex,
    toggle: () => start(position, true),
    seek: (t: number) => start(t, false),
    skip: (d: number) => start(position + d, false),
    setRate: (r: number) => {
      // Speed is one setting for every book; the next book to play picks it up.
      saveRate(r)
      setRateState(r)
    },
    seekChapter: startChapter,
    previousChapter: () => startChapter(Math.max(0, chapterIndex - 1)),
    nextChapter: () => startChapter(Math.min(book.chapters.length - 1, chapterIndex + 1)),
    offer: null,
    acceptOffer: () => {},
    dismissOffer: () => {},
  }
}

export function Player({ book }: { book: BookDetail }) {
  const activate = useActivate()
  const live = useLivePlayer(book.id)
  const idle = useIdlePlayer(book, activate)
  const p = live ?? idle

  // While dragging, everything shows the drag position, and the seek happens once
  // on release. Seeking on every input event would reload files repeatedly when
  // the drag crosses file boundaries.
  const [scrub, setScrub] = useState<number | null>(null)
  const shown = scrub ?? p.position

  const commitScrub = () => {
    if (scrub !== null) p.seek(scrub)
    setScrub(null)
  }

  const shownChapterIndex = chapterIndexAt(book.chapters, shown)
  const chapter = book.chapters[shownChapterIndex]
  // Remaining time is in listening time, so it shrinks with playback speed.
  const chapterLeft = chapter ? (chapter.endOffsetSeconds - shown) / p.rate : null

  const listRef = useRef<HTMLOListElement | null>(null)
  const lastUserScrollRef = useRef(0)

  // Any direct interaction with the list counts as the listener taking over:
  // wheel, touch, dragging the scrollbar (pointerdown), or keyboard scrolling.
  useEffect(() => {
    const list = listRef.current
    if (!list) return
    const mark = () => {
      lastUserScrollRef.current = Date.now()
    }
    const events = ['wheel', 'touchstart', 'pointerdown', 'keydown'] as const
    for (const e of events) list.addEventListener(e, mark, { passive: true })

    // How wide the list's scrollbar is (0 when the list doesn't scroll), for the
    // CSS that hangs it outside the frame. Rechecked when the layout changes.
    const measure = () => {
      list.style.setProperty('--scrollbar', `${list.offsetWidth - list.clientWidth}px`)
    }
    const observer = new ResizeObserver(measure)
    observer.observe(list)
    measure()

    return () => {
      observer.disconnect()
      for (const e of events) list.removeEventListener(e, mark)
    }
  }, [])

  // Keep the current chapter in view: on load, and whenever playback moves on.
  useEffect(() => {
    const list = listRef.current
    const item = list?.children[p.chapterIndex]
    if (!list || !(item instanceof HTMLElement)) return
    // Only when the list is its own scroll area (the desktop layout). In the
    // single-column layout the page itself scrolls, and moving the page to the
    // chapter would scroll the player out of sight.
    if (list.scrollHeight <= list.clientHeight) return
    if (Date.now() - lastUserScrollRef.current < USER_SCROLL_GRACE_MS) return
    revealWithin(list, item)
  }, [p.chapterIndex])

  return (
    <>
      <section className="player" aria-label="Player">
        <div className="now">
          <p className="now-chapter">{chapter?.title ?? book.title}</p>
          {chapter && (
            <p className="now-detail muted">
              <span>
                Chapter {shownChapterIndex + 1} of {book.chapters.length}
              </span>
              <span>{formatTime(chapterLeft ?? 0)} left in chapter</span>
            </p>
          )}
        </div>

        <JumpPrompt player={p} />

        <ChapterTimeline
          chapters={book.chapters}
          total={p.total}
          value={shown}
          onScrub={setScrub}
          onCommit={commitScrub}
        />
        <div className="times">
          <span>{formatTime(shown)}</span>
          <span>{formatTime((p.total - shown) / p.rate)} left</span>
        </div>

        <div className="transport">
          <span className="status" role="status">
            {p.error ?? (p.loading ? 'Loading…' : '')}
          </span>

          <div className="controls">
            <button type="button" className="control" onClick={p.previousChapter} aria-label="Previous chapter">
              <PreviousIcon />
            </button>
            <button type="button" className="control" onClick={() => p.skip(-30)} aria-label="Back 30 seconds">
              <SkipIcon />
            </button>
            <button
              type="button"
              className="control control-play"
              onClick={p.toggle}
              aria-label={p.playing ? 'Pause' : 'Play'}
            >
              <PlayPauseIcon playing={p.playing} />
            </button>
            <button type="button" className="control" onClick={() => p.skip(30)} aria-label="Forward 30 seconds">
              <SkipIcon forward />
            </button>
            <button type="button" className="control" onClick={p.nextChapter} aria-label="Next chapter">
              <NextIcon />
            </button>
          </div>

          <label className="speed">
            <span>Speed</span>
            <select value={p.rate} onChange={(e) => p.setRate(Number(e.target.value))}>
              {RATES.map((r) => (
                <option key={r} value={r}>
                  {r}×
                </option>
              ))}
            </select>
          </label>
        </div>
      </section>

      {book.chapters.length > 1 && (
        <section className="chapters" aria-labelledby="chapters-heading">
          <div className="section-head">
            <h2 id="chapters-heading">Chapters</h2>
            <span className="section-count">{book.chapters.length}</span>
          </div>
          <ol ref={listRef}>
            {book.chapters.map((c, i) => {
              const state = i < p.chapterIndex ? 'played' : i === p.chapterIndex ? 'current' : undefined
              return (
                <li key={c.sequence}>
                  <button
                    type="button"
                    className="chapter"
                    data-state={state}
                    aria-current={state === 'current' ? 'true' : undefined}
                    onClick={() => p.seekChapter(i)}
                  >
                    <span className="chapter-title">{c.title}</span>
                    {state === 'current' && <LevelMeter active={p.playing} />}
                    <span className="chapter-length">
                      {formatTime(c.endOffsetSeconds - c.startOffsetSeconds)}
                    </span>
                  </button>
                </li>
              )
            })}
          </ol>
        </section>
      )}
    </>
  )
}
