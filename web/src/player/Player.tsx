import { useState } from 'react'
import type { BookDetail } from '../api/types'
import { NextIcon, PauseIcon, PlayIcon, PreviousIcon, SkipIcon } from '../components/Icons'
import { ChapterTimeline } from './ChapterTimeline'
import { chapterIndexAt, formatTime } from './timeline'
import { useBookPlayer } from './useBookPlayer'

const RATES = [0.8, 1, 1.1, 1.25, 1.5, 1.75, 2]

export function Player({ book }: { book: BookDetail }) {
  const p = useBookPlayer(book)

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
            {p.playing ? <PauseIcon /> : <PlayIcon />}
          </button>
          <button type="button" className="control" onClick={() => p.skip(30)} aria-label="Forward 30 seconds">
            <SkipIcon forward />
          </button>
          <button type="button" className="control" onClick={p.nextChapter} aria-label="Next chapter">
            <NextIcon />
          </button>
        </div>

        <div className="player-footer">
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
          <span className="status" role="status">
            {p.error ?? (p.loading ? 'Loading…' : '')}
          </span>
        </div>
      </section>

      {book.chapters.length > 1 && (
        <section className="chapters" aria-labelledby="chapters-heading">
          <h2 id="chapters-heading">Chapters</h2>
          <ol>
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
