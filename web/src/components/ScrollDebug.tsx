import { useEffect, useState } from 'react'

// Diagnostic for the iOS sticky-masthead bug: the page sometimes won't scroll
// back to the top under the pinned search row. Off unless asked for:
// ?debug=scroll turns it on (and remembers it for the tab), ?debug=off turns
// it off. It shows what the page believes about its own scroll position, so a
// screenshot taken while stuck says which part is wrong. Remove once fixed.
const STORAGE_KEY = 'debug-scroll'

function enabledFromUrl(): boolean {
  try {
    const flag = new URLSearchParams(window.location.search).get('debug')
    if (flag === 'scroll') sessionStorage.setItem(STORAGE_KEY, '1')
    if (flag === 'off') sessionStorage.removeItem(STORAGE_KEY)
    return sessionStorage.getItem(STORAGE_KEY) === '1'
  } catch {
    return false
  }
}

type Reading = Record<string, string>

function read(): Reading {
  const vv = window.visualViewport
  const masthead = document.querySelector<HTMLElement>('.masthead')
  const rect = masthead?.getBoundingClientRect()
  const px = (n: number | undefined) => (n === undefined ? '–' : n.toFixed(1))
  return {
    scrollY: px(window.scrollY),
    'doc.scrollTop': px(document.documentElement.scrollTop),
    'vv.offsetTop': px(vv?.offsetTop),
    'vv.pageTop': px(vv?.pageTop),
    'vv.height': px(vv?.height),
    innerHeight: px(window.innerHeight),
    scrollHeight: px(document.documentElement.scrollHeight),
    'masthead.top': px(rect?.top),
    'masthead.bottom': px(rect?.bottom),
    'sticky top': masthead ? getComputedStyle(masthead).top : '–',
  }
}

export function ScrollDebug() {
  const [enabled] = useState(enabledFromUrl)
  const [reading, setReading] = useState<Reading>({})

  useEffect(() => {
    if (!enabled) return
    let frame = 0
    const update = () => {
      cancelAnimationFrame(frame)
      frame = requestAnimationFrame(() => setReading(read()))
    }
    update()
    // Also on a timer: the bug may be a state nothing fires an event for.
    const interval = window.setInterval(update, 500)
    window.addEventListener('scroll', update, { passive: true })
    window.addEventListener('resize', update)
    window.visualViewport?.addEventListener('scroll', update)
    window.visualViewport?.addEventListener('resize', update)
    return () => {
      cancelAnimationFrame(frame)
      window.clearInterval(interval)
      window.removeEventListener('scroll', update)
      window.removeEventListener('resize', update)
      window.visualViewport?.removeEventListener('scroll', update)
      window.visualViewport?.removeEventListener('resize', update)
    }
  }, [enabled])

  if (!enabled) return null
  return (
    <pre
      aria-hidden="true"
      style={{
        position: 'fixed',
        left: 8,
        bottom: 'calc(6rem + env(safe-area-inset-bottom))',
        zIndex: 1000,
        margin: 0,
        padding: '6px 8px',
        background: 'rgba(0, 0, 0, 0.8)',
        color: '#7CFC00',
        font: '11px/1.35 ui-monospace, Menlo, monospace',
        border: '1px solid #7CFC00',
        borderRadius: 4,
        pointerEvents: 'none',
      }}
    >
      {Object.entries(reading)
        .map(([k, v]) => `${k.padEnd(16)}${v}`)
        .join('\n')}
    </pre>
  )
}
