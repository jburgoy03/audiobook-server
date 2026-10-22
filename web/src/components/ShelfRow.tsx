import { useCallback, useEffect, useRef, useState, type ReactNode, type RefObject } from 'react'
import { Link } from 'react-router'
import { BackIcon, ForwardIcon } from './Icons'

/**
 * A home-page shelf: a heading, a See all link into the full list, and one row
 * of items that scrolls sideways.
 *
 * Items are sized to the page's grid tracks plus a sliver of the next one, so a
 * cut-off cover on the right edge says "there's more this way" without an arrow.
 *
 * Touch and trackpads scroll the row natively. A mouse gets two ways: previous
 * and next buttons that page by most of a row, and dragging (see useDragScroll).
 * The plain wheel is deliberately left to the page: turning it sideways over a
 * row would trap vertical scrolling whenever the cursor rests on one.
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

  // Whether the row is at either end, for the arrows. Runs on scroll, so it must
  // be cheap: at most once a frame, and a state change only when an end is
  // reached or left. Setting a fresh object on every scroll event re-rendered
  // every cover in the row many times a second, which made fast scrolls stutter.
  const frame = useRef<number | null>(null)
  const measure = useCallback(() => {
    if (frame.current !== null) return
    frame.current = requestAnimationFrame(() => {
      frame.current = null
      const el = row.current
      if (!el) return
      // A pixel of slack: fractional widths can leave scrollLeft just short of the end.
      const start = el.scrollLeft <= 1
      const end = el.scrollLeft + el.clientWidth >= el.scrollWidth - 1
      setEnds((prev) => (prev.start === start && prev.end === end ? prev : { start, end }))
    })
  }, [])
  useEffect(() => () => {
    if (frame.current !== null) cancelAnimationFrame(frame.current)
  }, [])

  useEffect(() => {
    measure()
    const el = row.current
    if (!el) return
    const observer = new ResizeObserver(measure)
    observer.observe(el)
    return () => observer.disconnect()
  }, [measure, children])

  useDragScroll(row)

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

// How far a release keeps gliding: velocity (px/ms) times this many ms.
const THROW_MS = 320

/**
 * Click-and-drag scrolling for a mouse, so a row moves under the hand the way it
 * does under a finger: the row follows the pointer, and on release it keeps
 * going in proportion to how fast it was thrown, then settles on the nearest
 * cover edge.
 *
 * Mouse only. Touch and pens already scroll natively, and taking their pointer
 * events would fight the browser's own momentum.
 *
 * A press that moves less than a few pixels is a click and opens the book. A
 * real drag swallows the click it ends with, so letting go over a cover doesn't
 * open it. Snapping is switched off while dragging (it would yank the row to
 * the nearest cover on every move) and back on once the glide ends.
 */
function useDragScroll(row: RefObject<HTMLElement | null>) {
  useEffect(() => {
    const el = row.current
    if (!el) return

    let pointerId: number | null = null
    let startX = 0
    let startLeft = 0
    let lastX = 0
    let lastT = 0
    let velocity = 0
    let dragging = false

    const settle = () => delete el.dataset.dragging

    const down = (e: PointerEvent) => {
      if (e.pointerType !== 'mouse' || e.button !== 0) return
      pointerId = e.pointerId
      startX = lastX = e.clientX
      lastT = e.timeStamp
      startLeft = el.scrollLeft
      velocity = 0
      dragging = false
    }

    const move = (e: PointerEvent) => {
      if (e.pointerId !== pointerId) return
      const dx = e.clientX - startX
      if (!dragging) {
        if (Math.abs(dx) < 6) return
        dragging = true
        el.setPointerCapture(e.pointerId)
        el.dataset.dragging = ''
      }
      el.scrollLeft = startLeft - dx
      const dt = e.timeStamp - lastT
      // Smoothed, so one jittery last sample doesn't decide the throw.
      if (dt > 0) velocity = 0.7 * ((e.clientX - lastX) / dt) + 0.3 * velocity
      lastX = e.clientX
      lastT = e.timeStamp
    }

    const up = (e: PointerEvent) => {
      if (e.pointerId !== pointerId) return
      pointerId = null
      if (!dragging) return

      const swallow = (click: MouseEvent) => {
        click.preventDefault()
        click.stopPropagation()
      }
      el.addEventListener('click', swallow, { capture: true, once: true })
      // If no click follows (released outside the row), don't eat the next real one.
      window.setTimeout(() => el.removeEventListener('click', swallow, { capture: true }), 50)

      // A pause before letting go is a placement, not a throw.
      const still = e.timeStamp - lastT > 80
      const projected = el.scrollLeft - (still ? 0 : velocity * THROW_MS)
      const first = el.firstElementChild as HTMLElement | null
      const gap = parseFloat(getComputedStyle(el).columnGap) || 0
      const step = first ? first.offsetWidth + gap : 1
      const target = Math.max(0, Math.round(projected / step) * step)
      const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches
      el.scrollTo({ left: target, behavior: reduce ? 'auto' : 'smooth' })
      if ('onscrollend' in el) el.addEventListener('scrollend', settle, { once: true })
      else window.setTimeout(settle, 600)
    }

    // Links and images are natively draggable; a drag here means scroll.
    const noNativeDrag = (e: DragEvent) => e.preventDefault()

    el.addEventListener('pointerdown', down)
    el.addEventListener('pointermove', move)
    el.addEventListener('pointerup', up)
    el.addEventListener('pointercancel', up)
    el.addEventListener('dragstart', noNativeDrag)
    return () => {
      el.removeEventListener('pointerdown', down)
      el.removeEventListener('pointermove', move)
      el.removeEventListener('pointerup', up)
      el.removeEventListener('pointercancel', up)
      el.removeEventListener('dragstart', noNativeDrag)
    }
  }, [row])
}
