import { useState, type CSSProperties } from 'react'
import { api } from '../api/client'

interface CoverProps {
  bookId: string
  title: string
  hasCover: boolean
  /** 0–1. Shows the ribbon and a progress line when the book has been started. */
  progress?: number
  className?: string
}

// How far a cover may be scaled up past its real pixel size before it looks
// soft. Beyond this, the cover is drawn smaller instead (see --cover-max).
const MAX_UPSCALE = 1.5

/**
 * Covers arrive in any shape and size (a 175px square, a 646x1000 portrait), so
 * every cover sits in a square frame: the image fits inside, and a blurred copy of
 * itself fills the rest. Books with no embedded art get a plain cloth cover.
 *
 * Once the image loads, its largest sensible size is exposed as --cover-max, so a
 * large frame (the book page, the featured book) can draw a small image at a
 * size that stays sharp, without changing the frame's own size.
 */
export function Cover({ bookId, title, hasCover, progress, className }: CoverProps) {
  const started = progress !== undefined && progress > 0
  const url = api.coverUrl(bookId)
  const [maxSize, setMaxSize] = useState<number | null>(null)

  const style = maxSize ? ({ '--cover-max': `${maxSize}px` } as CSSProperties) : undefined

  return (
    <div className={['cover', className].filter(Boolean).join(' ')} style={style}>
      {hasCover ? (
        <>
          <img className="cover-backdrop" src={url} alt="" aria-hidden="true" />
          <img
            className="cover-image"
            src={url}
            alt=""
            loading="lazy"
            onLoad={(e) => {
              const img = e.currentTarget
              setMaxSize(Math.round(Math.max(img.naturalWidth, img.naturalHeight) * MAX_UPSCALE))
            }}
          />
        </>
      ) : (
        <div className="cover-cloth">
          <span>{title}</span>
        </div>
      )}
      {started && (
        <>
          <span className="cover-ribbon" aria-hidden="true" />
          <span className="cover-progress" aria-hidden="true">
            <span style={{ width: `${Math.min(progress, 1) * 100}%` }} />
          </span>
        </>
      )}
    </div>
  )
}
