import { useEffect, useRef, useState } from 'react'
import { useLocation } from 'react-router'
import { CloseIcon, SearchIcon } from '../components/Icons'
import { useLibraryQuery } from './libraryQuery'
import { BOOK_STATUSES, SORT_KEYS, SORT_LABELS, STATUS_LABELS, type BookStatus, type SortKey } from './search'

// How long typing pauses before the search reaches the URL (and the results).
const SEARCH_DELAY_MS = 120

/** Typing in one of these is typing, not a shortcut. */
function isEditable(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false
  return target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName)
}

/**
 * The library's search, status filters and sort, in the masthead. Only on the
 * library page; other pages keep a plain masthead.
 *
 * Always shown, at every width. It used to sit behind a search button on narrow
 * screens, and the button focused the box a frame after the tap, once the band
 * was displayed. Mobile browsers (iOS Safari above all) only raise the keyboard
 * for a focus that happens inside the tap itself, so the keyboard never came up.
 * A box that's already there is focused by the tap directly, and the keyboard
 * opens every time.
 *
 * On phones and tablets the masthead is sticky (see "Library search" in
 * index.css): the wordmark row scrolls away, and this stays pinned, so a search
 * is one tap from anywhere in the list. Chips and sort share one line that
 * scrolls sideways, which keeps the pinned bar short.
 *
 * The search box keeps its own text and writes it to the URL once typing pauses.
 * Bound straight to the URL, it dropped letters: the URL updates a beat after
 * each keystroke, and a fast typist's next key landed on the older value.
 * A change that didn't come from the box (Clear, Back) replaces its text.
 *
 * A new search scrolls the page back to the top, so the results start where you
 * can see them rather than wherever the list was scrolled to. The keyboard's
 * Search key (Enter) closes the keyboard, since on a phone it covers half the
 * results.
 *
 * Keyboard: `/` focuses the search from anywhere on the page (except while
 * typing in another field). `Esc` in the box clears it, or if it's already
 * empty, leaves the box.
 */
export function LibrarySearch() {
  const { pathname } = useLocation()
  if (pathname !== '/') return null
  return <LibraryToolbar />
}

function LibraryToolbar() {
  const [query, onChange] = useLibraryQuery()
  const input = useRef<HTMLInputElement>(null)
  const timer = useRef<number | undefined>(undefined)
  const [text, setText] = useState(query.q)
  // The search the box last sent. The URL differing from it means someone else
  // changed the search. Compared during render, React's pattern for state that
  // follows a prop, rather than in an effect.
  const [sent, setSent] = useState(query.q)
  if (query.q !== sent) {
    setSent(query.q)
    setText(query.q)
  }

  const send = (q: string) => {
    window.clearTimeout(timer.current)
    if (q === sent) return
    setSent(q)
    onChange({ q })
    // Instant, not smooth: a smooth scroll fights the list changing under it.
    if (window.scrollY > 0) window.scrollTo({ top: 0 })
  }
  const type = (q: string) => {
    setText(q)
    window.clearTimeout(timer.current)
    timer.current = window.setTimeout(() => send(q), SEARCH_DELAY_MS)
  }
  useEffect(() => () => window.clearTimeout(timer.current), [])

  const clear = () => {
    setText('')
    send('')
  }

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== '/' || e.ctrlKey || e.metaKey || e.altKey || e.defaultPrevented) return
      if (isEditable(e.target)) return
      e.preventDefault()
      input.current?.focus()
      input.current?.select()
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [])

  const toggleStatus = (status: BookStatus) => {
    const on = query.statuses.includes(status)
    onChange({
      statuses: on ? query.statuses.filter((s) => s !== status) : [...query.statuses, status],
    })
  }

  return (
    <search className="library-toolbar" aria-label="Find books">
      <div className="library-search-field">
        <SearchIcon size={18} />
        <input
          ref={input}
          className="library-search"
          type="search"
          aria-label="Search by title or author"
          placeholder="Search title or author"
          autoComplete="off"
          autoCorrect="off"
          autoCapitalize="none"
          spellCheck={false}
          enterKeyHint="search"
          value={text}
          onChange={(e) => type(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') {
              // Send now rather than after the pause, and put the keyboard away.
              e.preventDefault()
              send(text)
              input.current?.blur()
              return
            }
            if (e.key !== 'Escape') return
            // The native search field clears on Esc in some browsers but not all;
            // do it here so every browser behaves the same.
            e.preventDefault()
            if (text !== '') clear()
            else input.current?.blur()
          }}
        />
        {/* Our own clear button: the native one is missing in some mobile
            browsers and a 16px target where it exists. Keeps focus, so the
            keyboard stays up for the next search. */}
        {text !== '' && (
          <button
            type="button"
            className="library-search-clear"
            aria-label="Clear search"
            onPointerDown={(e) => e.preventDefault()}
            onClick={() => {
              clear()
              input.current?.focus()
            }}
          >
            <CloseIcon size={18} />
          </button>
        )}
      </div>

      <div className="library-filters">
        <div className="library-status" role="group" aria-label="Status">
          {BOOK_STATUSES.map((status) => (
            <button
              key={status}
              type="button"
              className="pill chip"
              aria-pressed={query.statuses.includes(status)}
              onClick={() => toggleStatus(status)}
            >
              {STATUS_LABELS[status]}
            </button>
          ))}
        </div>

        <label className="picker">
          <span>Sort</span>
          <select value={query.sort} onChange={(e) => onChange({ sort: e.target.value as SortKey })}>
            {SORT_KEYS.map((key) => (
              <option key={key} value={key}>
                {SORT_LABELS[key]}
              </option>
            ))}
          </select>
        </label>
      </div>
    </search>
  )
}
