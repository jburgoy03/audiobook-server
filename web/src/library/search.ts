import type { BookSummary } from '../api/types'

/**
 * Library search, sort and status: pure functions over the list the library page
 * already holds. Client-side on purpose: GET /api/books returns every visible book,
 * so filtering it per keystroke is instant well past a thousand books. If the list
 * is ever paginated, matching moves to the server (see
 * docs/next-steps/library-search.md) and these rules are the spec for it.
 */

/**
 * Text as search compares it: lowercase, diacritics dropped ("Brontë" → "bronte"),
 * apostrophes removed so "Ender's" matches "enders", every other run of
 * punctuation or space turned into one space.
 */
export function normalize(text: string): string {
  return text
    .normalize('NFD')
    .replace(/\p{M}/gu, '')
    .toLowerCase()
    .replace(/['’‘`]/g, '')
    .replace(/[^\p{L}\p{N}]+/gu, ' ')
    .trim()
}

/** The query's words, normalized. Empty for a blank or punctuation-only query. */
export function queryWords(query: string): string[] {
  const normalized = normalize(query)
  return normalized === '' ? [] : normalized.split(' ')
}

/**
 * Whether a book matches: every word of the query appears somewhere in the title
 * or author, in any order, as a substring ("murakami wood" finds Norwegian Wood;
 * "lamora" finds The Lies of Locke Lamora). An empty query matches everything.
 *
 * Deliberately no typo tolerance: fuzzy matching is a heuristic that gives odd
 * hits on short queries. Title and author arrive with overrides applied, so a
 * corrected title is searched as corrected.
 */
export function matchesQuery(book: Pick<BookSummary, 'title' | 'author'>, query: string): boolean {
  const words = queryWords(query)
  if (words.length === 0) return true
  const haystack = normalize(`${book.title} ${book.author ?? ''}`)
  return words.every((word) => haystack.includes(word))
}

export type SortKey = 'title' | 'author' | 'added' | 'length'

export const SORT_KEYS: readonly SortKey[] = ['title', 'author', 'added', 'length']

export const SORT_LABELS: Readonly<Record<SortKey, string>> = {
  title: 'Title',
  author: 'Author',
  added: 'Recently added',
  length: 'Length',
}

// Base sensitivity: case and accents don't separate titles. Numeric: "Book 2"
// before "Book 10".
const collator = new Intl.Collator(undefined, { sensitivity: 'base', numeric: true })

type Sortable = Pick<BookSummary, 'id' | 'title' | 'author' | 'addedAt' | 'durationSeconds'>

const byTitle = (a: Sortable, b: Sortable) =>
  collator.compare(a.title, b.title) || a.id.localeCompare(b.id)

/**
 * The comparator for a sort. Ties fall back to title (then id, so the order is
 * stable across reloads).
 *
 * - title: as written. A leading "The" counts; skipping articles is a
 *   language-specific heuristic, left out until the order bothers anyone.
 * - author: the full author string, not the surname. Guessing that the last word
 *   is the surname fails on "Ursula K. Le Guin". Books without an author go last.
 * - added: newest first.
 * - length: shortest first.
 */
export function compareBooks(sort: SortKey): (a: Sortable, b: Sortable) => number {
  switch (sort) {
    case 'title':
      return byTitle
    case 'author':
      return (a, b) => {
        if (a.author === null || b.author === null) {
          if (a.author !== b.author) return a.author === null ? 1 : -1
          return byTitle(a, b)
        }
        return collator.compare(a.author, b.author) || byTitle(a, b)
      }
    case 'added':
      // ISO 8601 timestamps from the server compare as strings.
      return (a, b) => b.addedAt.localeCompare(a.addedAt) || byTitle(a, b)
    case 'length':
      return (a, b) => a.durationSeconds - b.durationSeconds || byTitle(a, b)
  }
}

/**
 * A display rule, not sync state: a book counts as started from a minute in, so a
 * few seconds of a misclick don't put it in Continue listening. Shared by that
 * shelf and the status filter so the two can't disagree.
 */
export const STARTED_AFTER_SECONDS = 60

export type BookStatus = 'unstarted' | 'inProgress' | 'finished'

export const BOOK_STATUSES: readonly BookStatus[] = ['unstarted', 'inProgress', 'finished']

export const STATUS_LABELS: Readonly<Record<BookStatus, string>> = {
  unstarted: 'Not started',
  inProgress: 'In progress',
  finished: 'Finished',
}

/**
 * Where a book stands, from its resume point (progressStore.resumePoint).
 * Finished is the server's IsFinished, set when a book plays to its end; it wins
 * over position. The active book is classified by its saved position like any
 * other, so a book that has only just started playing can still read as not
 * started until the player saves past the minute.
 */
export function bookStatus(point: { position: number; isFinished: boolean } | null): BookStatus {
  if (!point) return 'unstarted'
  if (point.isFinished) return 'finished'
  return point.position >= STARTED_AFTER_SECONDS ? 'inProgress' : 'unstarted'
}
