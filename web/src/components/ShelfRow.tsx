import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { Link } from 'react-router'
import { BackIcon, ForwardIcon } from './Icons'

/**
 * A home-page shelf: a heading, a See all link into the full list, and one row
 * of items that scrolls sideways.
 *
 * Items are sized to the page's grid tracks plus a sliver of the next one, so a
 * cut-off cover on the right edge says "there's more this way" without an arrow.
 * Touch and trackpads scroll the row directly; a mouse has no sideways scroll,
 * so pointer screens also get previous and next buttons, which page by most of
 * a row and disable at either end.
 */
export function ShelfRow({
  id,
  title,
  note,
  seeAll,
  children,
}: {
  id: string
  title: string
  /** A quiet qualifier after the title, like "Under 6 hours". */
  note?: string
  seeAll: string
  children: ReactNode
}) {
  const row = useRef<HTMLUListElement>(null)
  const [ends, setEnds] = useState({ start: true, end: true })

  const measure = useCallback(() => {
    const el = row.current
    if (!el) return
    // A pixel of slack: fractional widths can leave scrollLeft just short of the end.
    setEnds({ start: el.scrollLeft <= 1, end: el.scrollLeft + el.clientWidth >= el.scrollWidth - 1 })
  }, [])

  useEffect(() => {
    measure()
    const el = row.current
    if (!el) return
    const observer = new ResizeObserver(measure)
    observer.observe(el)
    return () => observer.disconnect()
  }, [measure, children])

  const page = (direction: 1 | -1) => {
    const el = row.current
    if (!el) return
    const smooth = !window.matchMedia('(prefers-reduced-motion: reduce)').matches
    el.scrollBy({ left: direction * el.clientWidth * 0.85, behavior: smooth ? 'smooth' : 'auto' })
  }

  return (
    <section className="shelf" aria-labelledby={id}>
      <div className="section-head">
        <h2 id={id}>
          {title}
          {note && <span className="section-note"> {note}</span>}
        </h2>
        <div className="section-head-end">
          {!(ends.start && ends.end) && (
            <span className="shelf-arrows">
              <button type="button" aria-label={`Previous in ${title}`} disabled={ends.start} onClick={() => page(-1)}>
                <BackIcon />
              </button>
              <button type="button" aria-label={`Next in ${title}`} disabled={ends.end} onClick={() => page(1)}>
                <ForwardIcon />
              </button>
            </span>
          )}
          <Link to={seeAll} className="see-all">
            See all
          </Link>
        </div>
      </div>
      <ul className="shelf-row" ref={row} onScroll={measure}>
        {children}
      </ul>
    </section>
  )
}
