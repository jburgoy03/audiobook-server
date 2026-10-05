import { useEffect, useId, useRef, useState } from 'react'
import { useLocation } from 'react-router'
import { CloseIcon, SearchIcon } from '../components/Icons'
import { isFiltering, useLibraryQuery } from './libraryQuery'
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
 * Wide screens have room in the middle of the masthead, so everything sits
 * there, always visible. Narrower ones get a search button beside the nav that
 * opens the controls as a band under the masthead row. The CSS decides which
 * (see "Library search" in index.css): this renders the button and the controls
 * both, and `open` only matters on a narrow screen. The band starts open
 * when the URL already filters, so a reload doesn't hide why the list is short.
 * Closed while filtering, the button carries a dot.
 *
 * The search box keeps its own text and writes it to the URL once typing pauses.
 * Bound straight to the URL, it dropped letters: the URL updates a beat after
 * each keystroke, and a fast typist's next key landed on the older value.
 * A change that didn't come from the box (Clear, Back) replaces its text.
 *
 * Keyboard: `/` opens and focuses the search from anywhere on the page (except
 * while typing in another field). `Esc` in the box clears it, or if it's
 * already empty, closes the band and leaves the box.
 */
export function LibrarySearch() {
  const { pathname } = useLocation()
  if (pathname !== '/') return null
  return <LibraryToolbar />
}

function LibraryToolbar() {
  const [query, onChange] = useLibraryQuery()
  const filtering = isFiltering(query)
  const panelId = useId()
  const input = useRef<HTMLInputElement>(null)
  const toggle = useRef<HTMLButtonElement>(null)
  const timer = useRef<number | undefined>(undefined)
  const [open, setOpen] = useState(filtering)
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
    setSent(q)
    onChange({ q })
  }
  const type = (q: string) => {
    setText(q)
    window.clearTimeout(timer.current)
    timer.current = window.setTimeout(() => send(q), SEARCH_DELAY_MS)
  }
  useEffect(() => () => window.clearTimeout(timer.current), [])

  // Focus once the band is displayed; focusing a hidden input does nothing.
  const focusSearch = () =>
    requestAnimationFrame(() => {
      input.current?.focus()
      input.current?.select()
    })

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== '/' || e.ctrlKey || e.metaKey || e.altKey || e.defaultPrevented) return
      if (isEditable(e.target)) return
      e.preventDefault()
      setOpen(true)
      focusSearch()
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [])

  const close = () => {
    setOpen(false)
    input.current?.blur()
    // On a wide screen the button isn't displayed and this does nothing.
    toggle.current?.focus()
  }

  const toggleStatus = (status: BookStatus) => {
    const on = query.statuses.includes(status)
    onChange({
      statuses: on ? query.statuses.filter((s) => s !== status) : [...query.statuses, status],
    })
  }

  return (
    <>
      <button
        ref={toggle}
        type="button"
        className="search-toggle"
        aria-label={open ? 'Close search' : 'Search the library'}
        aria-expanded={open}
        aria-controls={panelId}
        onClick={() => {
          if (open) close()
          else {
            setOpen(true)
            focusSearch()
          }
        }}
      >
        {open ? <CloseIcon /> : <SearchIcon />}
        {!open && filtering && <span className="search-toggle-dot" aria-hidden="true" />}
      </button>

      <search id={panelId} className="library-toolbar" data-open={open || undefined} aria-label="Find books">
        <input
          ref={input}
          className="library-search"
          type="search"
          aria-label="Search by title or author"
          placeholder="Search title or author"
          autoComplete="off"
          spellCheck={false}
          enterKeyHint="search"
          value={text}
          onChange={(e) => type(e.target.value)}
          onKeyDown={(e) => {
            if (e.key !== 'Escape') return
            // The native search field clears on Esc in some browsers but not all;
            // do it here so every browser behaves the same.
            e.preventDefault()
            if (text !== '') {
              setText('')
              send('')
            } else close()
          }}
        />

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
    </>
  )
}
