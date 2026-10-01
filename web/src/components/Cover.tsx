import { api } from '../api/client'

interface CoverProps {
  bookId: string
  title: string
  hasCover: boolean
  /** 0–1. Shows the ribbon and a progress line when the book has been started. */
  progress?: number
  className?: string
}

/**
 * Covers arrive in any shape and size (a 175px square, a 646x1000 portrait), so
 * every cover sits in a square frame: the image fits inside, and a blurred copy of
 * itself fills the rest. Books with no embedded art get a plain cloth cover.
 */
export function Cover({ bookId, title, hasCover, progress, className }: CoverProps) {
  const started = progress !== undefined && progress > 0
  const url = api.coverUrl(bookId)

  return (
    <div className={['cover', className].filter(Boolean).join(' ')}>
      {hasCover ? (
        <>
          <img className="cover-backdrop" src={url} alt="" aria-hidden="true" />
          <img className="cover-image" src={url} alt="" loading="lazy" />
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
