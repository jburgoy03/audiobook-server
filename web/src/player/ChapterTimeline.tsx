import type { Chapter } from '../api/types'
import { formatTime } from './timeline'

interface ChapterTimelineProps {
  chapters: Chapter[]
  total: number
  value: number
  /** Without these, the bar is a read-only picture of progress (no slider). */
  onScrub?: (value: number) => void
  onCommit?: () => void
  className?: string
}

/**
 * The seek bar, drawn as the book's chapters laid end to end: each segment is as
 * wide as its chapter is long. Finished chapters are filled, the current one fills
 * up to the playhead.
 *
 * Segments are positioned by percentage of the whole book rather than laid out
 * with gaps, so the playhead and the segments can't drift apart on long books.
 * A native range input sits on top, transparent, so keyboard and screen readers
 * get a real slider. Read-only bars (the library's progress) leave it out and are
 * hidden from assistive technology, since the text beside them says the same.
 */
export function ChapterTimeline({ chapters, total, value, onScrub, onCommit, className }: ChapterTimelineProps) {
  const interactive = onScrub !== undefined
  const segments =
    chapters.length > 0
      ? chapters
      : [{ sequence: 0, title: '', startOffsetSeconds: 0, endOffsetSeconds: total }]

  const pct = (seconds: number) => (total > 0 ? (seconds / total) * 100 : 0)

  return (
    <div
      className={['timeline', !interactive && 'timeline-static', className].filter(Boolean).join(' ')}
      aria-hidden={interactive ? undefined : true}
    >
      <div className="timeline-track" aria-hidden="true">
        {segments.map((c) => {
          const length = c.endOffsetSeconds - c.startOffsetSeconds
          const fill =
            value >= c.endOffsetSeconds
              ? 100
              : value <= c.startOffsetSeconds || length <= 0
                ? 0
                : ((value - c.startOffsetSeconds) / length) * 100
          const current = value >= c.startOffsetSeconds && value < c.endOffsetSeconds
          return (
            <span
              key={c.sequence}
              className={current ? 'segment current' : 'segment'}
              style={{ left: `${pct(c.startOffsetSeconds)}%`, width: `${pct(length)}%` }}
            >
              <span className="segment-fill" style={{ width: `${fill}%` }} />
            </span>
          )
        })}
      </div>
      <span className="timeline-head" aria-hidden="true" style={{ left: `${pct(value)}%` }} />
      {interactive && (
        <input
          className="timeline-input"
          type="range"
          min={0}
          max={total}
          step={1}
          value={value}
          aria-label="Position in book"
          aria-valuetext={`${formatTime(value)} of ${formatTime(total)}`}
          onChange={(e) => onScrub(Number(e.target.value))}
          onPointerUp={onCommit}
          onKeyUp={onCommit}
          onBlur={onCommit}
        />
      )}
    </div>
  )
}
