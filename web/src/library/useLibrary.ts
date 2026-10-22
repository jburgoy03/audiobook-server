import { useEffect, useMemo, useState } from 'react'
import { api } from '../api/client'
import type { BookSummary } from '../api/types'
import { progressStore, useProgressEntries } from '../player/progress'
import { loadLibraryView, saveLibraryView, type LibraryView } from '../player/storage'
import { bookStatus, type BookStatus } from './search'

/**
 * What every library page needs: the book list, and where you are in each book.
 * Each page fetches the list itself. GET /api/books is one small request, and a
 * shared cache would outlive signing out and show one account's books to the next.
 */
export function useBooks(): { books: BookSummary[] | null; error: string | null } {
  const [books, setBooks] = useState<BookSummary[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    api
      .books(controller.signal)
      .then(setBooks)
      .catch((e: unknown) => {
        if (!controller.signal.aborted) setError(String(e))
      })
    return () => controller.abort()
  }, [])

  return { books, error }
}

export interface Progress {
  position: number
  fraction: number
  /** Latest activity from any device, for ordering. */
  lastActivity: string
}

/**
 * Where this browser would resume the book (the same point Resume plays from), if
 * it's in progress by bookStatus, the rule the status filter uses too. It doesn't
 * decide the active book, which is always first (see LibraryPage).
 */
export function progressFor(book: BookSummary): Progress | null {
  const point = progressStore.resumePoint(book.id)
  if (!point || book.durationSeconds <= 0) return null
  if (bookStatus(point) !== 'inProgress') return null
  return {
    position: point.position,
    fraction: point.position / book.durationSeconds,
    lastActivity: point.lastActivity,
  }
}

/** A book's status by its saved resume point (see bookStatus). */
export function statusOf(bookId: string): BookStatus {
  return bookStatus(progressStore.resumePoint(bookId))
}

/**
 * Progress for every book in progress, recomputed whenever the store changes: a
 * refresh from the server can start, move or finish books. `entries` is the
 * store's snapshot, for callers whose own memos read the store.
 */
export function useProgress(books: readonly BookSummary[] | null) {
  const entries = useProgressEntries()
  const progress = useMemo(() => {
    const map = new Map<string, Progress>()
    for (const book of books ?? []) {
      const p = progressFor(book)
      if (p) map.set(book.id, p)
    }
    return map
    // progressFor reads the store, whose snapshot is `entries`.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [books, entries])
  return { progress, entries }
}

/**
 * Covers or rows. A browser that never chose gets rows on a phone, where the
 * two-column grid shows four books a screen and rows show about a dozen, and
 * covers everywhere else. A heuristic on width; the toggle overrides it, and the
 * choice is kept per browser.
 */
function initialView(): LibraryView {
  const saved = loadLibraryView()
  if (saved) return saved
  return window.matchMedia('(max-width: 39.99rem)').matches ? 'list' : 'grid'
}

/** The covers-or-rows choice, saved per browser when changed. */
export function useLibraryView(): [LibraryView, (view: LibraryView) => void] {
  const [view, setView] = useState(initialView)
  const change = (next: LibraryView) => {
    setView(next)
    saveLibraryView(next)
  }
  return [view, change]
}
