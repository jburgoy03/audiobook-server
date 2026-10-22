import type { BookSummary } from '../api/types'
import type { Author } from './authors'
import { compareBooks, type BookStatus } from './search'

/**
 * Which books the home page's shelves show. Pure, so the choices are testable;
 * the home page passes each book's status in rather than these reading the
 * progress store.
 *
 * Every shelf is a short row with a See all link into the full library, so none
 * of them needs to be complete, only a good first dozen.
 */

/** Books per shelf: two screens of a six-track row, about one on a phone. */
export const SHELF_SIZE = 12

/**
 * What counts as a short listen. A heuristic: about a working day of
 * commuting, or a long drive. Most novels run 8 to 15 hours.
 */
export const SHORT_LISTEN_SECONDS = 6 * 60 * 60

type StatusOf = (bookId: string) => BookStatus

/** Newest first, finished or not: it's what arrived, not what to play next. */
export function recentlyAdded(books: readonly BookSummary[], size = SHELF_SIZE): BookSummary[] {
  return [...books].sort(compareBooks('added')).slice(0, size)
}

/** Under SHORT_LISTEN_SECONDS and not finished, shortest first. */
export function shortListens(books: readonly BookSummary[], statusOf: StatusOf, size = SHELF_SIZE): BookSummary[] {
  return books
    .filter((b) => b.durationSeconds > 0 && b.durationSeconds < SHORT_LISTEN_SECONDS && statusOf(b.id) !== 'finished')
    .sort(compareBooks('length'))
    .slice(0, size)
}

/**
 * Books nobody has started, in an order that changes once a day, so older books
 * come round instead of the same dozen sitting there. Leaves out books already
 * on another shelf (`exclude`), so the shelves don't repeat each other.
 *
 * Daily rather than per visit: a row that reshuffles on every return is hard to
 * come back to ("it was here a minute ago").
 */
export function startSomethingNew(
  books: readonly BookSummary[],
  statusOf: StatusOf,
  seed: string,
  exclude: ReadonlySet<string> = new Set(),
  size = SHELF_SIZE,
): BookSummary[] {
  return books
    .filter((b) => !exclude.has(b.id) && statusOf(b.id) === 'unstarted')
    .map((b) => ({ b, key: hash(`${seed}:${b.id}`) }))
    .sort((x, y) => x.key - y.key || x.b.id.localeCompare(y.b.id))
    .slice(0, size)
    .map(({ b }) => b)
}

/** Authors with the most books first, then by name. */
export function topAuthors(authors: readonly Author[], size = SHELF_SIZE): Author[] {
  // groupByAuthor already sorted by name, and sort is stable.
  return [...authors].sort((a, b) => b.books.length - a.books.length).slice(0, size)
}

/** Today in local time, "2026-10-22": the seed for startSomethingNew. */
export function dailySeed(now: Date = new Date()): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`
}

/** FNV-1a, 32 bits. A shuffle key, not security. */
function hash(text: string): number {
  let h = 0x811c9dc5
  for (let i = 0; i < text.length; i++) {
    h ^= text.charCodeAt(i)
    h = Math.imul(h, 0x01000193)
  }
  return h >>> 0
}
