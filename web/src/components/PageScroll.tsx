import { useEffect, useLayoutEffect, useRef } from 'react'
import { useLocation, useNavigationType } from 'react-router'

// Where each history entry was scrolled to, keyed by the router's location key.
// In sessionStorage so a trip out to another site and back keeps it.
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

// True when this document was loaded by a reload (the button or pull-to-refresh).
function loadedByReload(): boolean {
  const entry = performance.getEntriesByType('navigation')[0] as PerformanceNavigationTiming | undefined
  return entry?.type === 'reload'
}

const USER_EVENTS = ['wheel', 'touchstart', 'keydown', 'pointerdown'] as const

/**
 * Calls `onGrow` each time the page grows, until `onGrow` returns true, a few
 * seconds pass, or the user scrolls or taps. Returns the function that stops it.
 */
function whilePageGrows(onGrow: () => boolean): () => void {
  const observer = new ResizeObserver(() => {
    if (onGrow()) stop()
  })
  const timeout = window.setTimeout(() => stop(), RESTORE_TIMEOUT_MS)
  const stop = () => {
    observer.disconnect()
    window.clearTimeout(timeout)
    USER_EVENTS.forEach((e) => window.removeEventListener(e, stop))
  }
  observer.observe(document.body)
  USER_EVENTS.forEach((e) => window.addEventListener(e, stop, { passive: true }))
  return stop
}

/**
 * Makes iOS WebKit recompute the sticky masthead.
 *
 * WebKit positions sticky elements on its scrolling thread, from offsets taken
 * at layout time. Laid out while the page was still an empty shell, those go
 * stale when the list arrives, and the masthead draws pinned even at the top
 * (the wordmark cut off) until a slow drag makes the main thread recompute
 * them. A one-pixel programmatic scroll and back does the same. A heuristic:
 * it is a no-op elsewhere, and invisible since it lands back where it started.
 */
function refreshSticky() {
  if (window.scrollY !== 0) return
  window.scrollTo(0, 1)
  requestAnimationFrame(() => {
    if (window.scrollY <= 1) window.scrollTo(0, 0)
  })
}

/**
 * The window's scroll position across pages.
 *
 * A new page (a link, PUSH or REPLACE) starts at the top. BrowserRouter leaves
 * the window where it was, so a link from halfway down home used to open the
 * next page halfway down too, with the masthead scrolled out of sight. A change
 * to the query alone (the library's chips and sort) keeps your place.
 *
 * A reload also starts at the top. The router reports it as POP, since the
 * history key survives it, so it used to restore the old position too; on a
 * phone that put the masthead straight into its pinned state.
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
  const firstRun = useRef(true)

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
        // Never below zero: iOS reports negative values while rubber-banding.
        positions.current[currentKey.current] = Math.max(window.scrollY, 0)
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
    const isFirstRun = firstRun.current
    firstRun.current = false
    currentKey.current = location.key
    const samePath = lastPath.current === location.pathname
    lastPath.current = location.pathname
    save(positions.current)

    // A fresh load or a reload: start at the top, and once the content has
    // arrived, make sure the sticky masthead knows it is at the top.
    if (isFirstRun && (navigationType !== 'POP' || loadedByReload())) {
      positions.current[location.key] = 0
      window.scrollTo(0, 0)
      return whilePageGrows(() => {
        refreshSticky()
        return false
      })
    }

    if (navigationType !== 'POP') {
      if (!samePath) window.scrollTo(0, 0)
      return
    }

    const target = positions.current[location.key] ?? 0
    const reached = () => {
      const max = document.documentElement.scrollHeight - window.innerHeight
      window.scrollTo(0, Math.min(target, Math.max(max, 0)))
      if (target === 0) refreshSticky()
      return max >= target
    }
    if (reached()) return

    // The page is still loading. Try again each time it grows.
    return whilePageGrows(reached)
  }, [location.key, location.pathname, navigationType])

  return null
}
