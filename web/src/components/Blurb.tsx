import { useLayoutEffect, useRef, useState } from 'react'

/**
 * A book's blurb. Below 64rem: a few lines, fading out, with More when there's
 * more. On desktop the CSS shows all of it, and the measurement finds nothing
 * clipped, so More never appears there. Measured
 * rather than guessed from the length, since the column's width varies (full width
 * on phones, two tracks on desktop). The source is named under it: the text is
 * the catalogue's, not ours.
 */
export function Blurb({ text, source }: { text: string; source: string | null }) {
  const body = useRef<HTMLDivElement>(null)
  const [open, setOpen] = useState(false)
  const [overflows, setOverflows] = useState(false)

  useLayoutEffect(() => {
    const el = body.current
    if (!el) return
    const measure = () => setOverflows(el.scrollHeight > el.clientHeight + 1)
    const observer = new ResizeObserver(measure)
    observer.observe(el)
    return () => observer.disconnect()
  }, [])

  const paragraphs = text.split(/\n{2,}/)

  return (
    <section className="blurb" data-open={open || undefined} aria-label="About this book">
      <div ref={body} className="blurb-body" data-clipped={(!open && overflows) || undefined}>
        {paragraphs.map((p, i) => (
          <p key={i}>{p}</p>
        ))}
      </div>
      <div className="blurb-foot">
        {(overflows || open) && (
          <button type="button" className="blurb-more" aria-expanded={open} onClick={() => setOpen(!open)}>
            {open ? 'Less' : 'More'}
          </button>
        )}
        {source && <span className="muted">From {source}</span>}
      </div>
    </section>
  )
}
