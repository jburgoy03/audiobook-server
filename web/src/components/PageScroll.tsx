import { useEffect, useLayoutEffect, useRef } from 'react'
import { useLocation, useNavigationType } from 'react-router'

// Where each history entry was scrolled to, keyed by the router's location key.
// In sessionStorage so a reload, or a trip out to another site and back, keeps it.
const STORAGE_KEY = 'page-scroll'
// Stop waiting for a page to grow tall enough after this long.
const RESTORE_TIMEOUT_MS = 3000

function load(): Record<string, number> {
  try {
    return JSON.parse(sessionStorage.getItem(STORAGE_KEY) ?? '{}') as Record<string, number>
  } catch {
    return {}
  }
}

function save(positions: Record<string, number>) {
  try {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(positions))
  } catch {
    // Private mode or full storage: Back just starts at the top.
  }
}

/**
 * The window's scroll position across pages.
 *
 * A new page (a link, PUSH or REPLACE) starts at the top. BrowserRouter leaves
 * the window where it was, so a link from halfway down home used to open the
 * next page halfway down too, with the masthead scrolled out of sight. A change
 * to the query alone (the library's chips and sort) keeps your place.
 *
 * Back and Forward (POP) return to where that page was left. The browser's own
 * restoration can't do this here: it scrolls the moment the URL changes, while
 * the page is still an empty shell waiting on /api/books, so it got clamped to
 * the top. Instead this waits for the page to grow tall enough, then scrolls.
 * It gives up after a few seconds, or as soon as you scroll or tap yourself,
 * so it never yanks the page out from under you.
 */
export function PageScroll() {
  const location = useLocation()
  const navigationType = useNavigationType()
  const positions = useRef<Record<string, number>>(load())
  const currentKey = useRef(location.key)
  const lastPath = useRef<string | null>(null)

  // The router restores scroll now, not the browser.
  useEffect(() => {
    if (!('scrollRestoration' in history)) return
    const before = history.scrollRestoration
    history.scrollRestoration = 'manual'
    return () => {
      history.scrollRestoration = before
    }
  }, [])

  // Record the position for whichever entry is showing.
  useEffect(() => {
    let frame = 0
    const onScroll = () => {
      cancelAnimationFrame(frame)
      frame = requestAnimationFrame(() => {
        positions.current[currentKey.current] = window.scrollY
      })
    }
    const persist = () => save(positions.current)
    window.addEventListener('scroll', onScroll, { passive: true })
    window.addEventListener('pagehide', persist)
    return () => {
      cancelAnimationFrame(frame)
      window.removeEventListener('scroll', onScroll)
      window.removeEventListener('pagehide', persist)
    }
  }, [])

  useLayoutEffect(() => {
    currentKey.current = location.key
    const samePath = lastPath.current === location.pathname
    lastPath.current = location.pathname
    save(positions.current)

    if (navigationType !== 'POP') {
      if (!samePath) window.scrollTo(0, 0)
      return
    }

    const target = positions.current[location.key] ?? 0
    const reached = () => {
      const max = document.documentElement.scrollHeight - window.innerHeight
      window.scrollTo(0, Math.min(target, Math.max(max, 0)))
      return max >= target
    }
    if (reached()) return

    // The page is still loading. Try again each time it grows.
    const observer = new ResizeObserver(() => {
      if (reached()) stop()
    })
    const timeout = window.setTimeout(() => stop(), RESTORE_TIMEOUT_MS)
    const userEvents = ['wheel', 'touchstart', 'keydown', 'pointerdown'] as const
    const stop = () => {
      observer.disconnect()
      window.clearTimeout(timeout)
      userEvents.forEach((e) => window.removeEventListener(e, stop))
    }
    observer.observe(document.body)
    userEvents.forEach((e) => window.addEventListener(e, stop, { passive: true }))
    return stop
  }, [location.key, location.pathname, navigationType])

  return null
}
