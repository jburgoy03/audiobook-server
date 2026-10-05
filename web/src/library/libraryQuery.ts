import { useCallback, useMemo } from 'react'
import { useSearchParams } from 'react-router'
import { BOOK_STATUSES, SORT_KEYS, type BookStatus, type SortKey } from './search'

/**
 * The library page's search, sort and filters, kept in the URL:
 * `/?q=murakami&sort=added&status=unstarted&status=inProgress`.
 *
 * The URL rather than component state, so Back from a book returns to the same
 * results, a reload keeps them, and a link can carry them. Nothing is stored.
 */
export interface LibraryQuery {
  /** As typed, not normalized: the search box shows exactly what was entered. */
  q: string
  sort: SortKey
  /** Empty means no status filter. Several are alternatives (OR). */
  statuses: readonly BookStatus[]
}

export const DEFAULT_SORT: SortKey = 'title'

export const EMPTY_QUERY: LibraryQuery = { q: '', sort: DEFAULT_SORT, statuses: [] }

const isSortKey = (value: string | null): value is SortKey =>
  value !== null && (SORT_KEYS as readonly string[]).includes(value)

const isStatus = (value: string): value is BookStatus =>
  (BOOK_STATUSES as readonly string[]).includes(value)

/**
 * Reads the query from search params. Unknown or repeated values are ignored
 * rather than rejected: a hand-edited or outdated link still opens the library.
 * Statuses come back in a fixed order, so equal filters compare equal.
 */
export function parseLibraryQuery(params: URLSearchParams): LibraryQuery {
  const sort = params.get('sort')
  const statuses = new Set(params.getAll('status').filter(isStatus))
  return {
    q: params.get('q') ?? '',
    sort: isSortKey(sort) ? sort : DEFAULT_SORT,
    statuses: BOOK_STATUSES.filter((s) => statuses.has(s)),
  }
}

/**
 * Writes the query into a copy of `params`, leaving any other params alone.
 * Defaults are left out, so an unfiltered library is plain `/`. A blank search
 * (only spaces) is left out too, since it matches everything.
 */
export function writeLibraryQuery(params: URLSearchParams, query: LibraryQuery): URLSearchParams {
  const next = new URLSearchParams(params)
  next.delete('q')
  next.delete('sort')
  next.delete('status')
  if (query.q.trim() !== '') next.set('q', query.q)
  if (query.sort !== DEFAULT_SORT) next.set('sort', query.sort)
  for (const status of BOOK_STATUSES) {
    if (query.statuses.includes(status)) next.append('status', status)
  }
  return next
}

/**
 * Whether anything narrows the list. Sort doesn't: it reorders, it hides nothing,
 * so the Continue listening shelf stays for a sorted but unfiltered library.
 */
export function isFiltering(query: LibraryQuery): boolean {
  return query.q.trim() !== '' || query.statuses.length > 0
}

/**
 * The query from the URL, and a setter that changes part of it.
 *
 * Every change replaces the current history entry rather than adding one, so
 * typing "murakami" isn't eight Back presses, and Back from the library leaves it
 * rather than stepping through filter changes. Back from a book page still
 * returns to the filtered list, because the URL carries it.
 */
export function useLibraryQuery(): [LibraryQuery, (change: Partial<LibraryQuery>) => void] {
  const [params, setParams] = useSearchParams()
  // Keyed on the string so a new URLSearchParams with the same contents doesn't
  // look like a new query to memoized consumers.
  const key = params.toString()
  const query = useMemo(() => parseLibraryQuery(new URLSearchParams(key)), [key])

  const update = useCallback(
    (change: Partial<LibraryQuery>) => {
      setParams((current) => writeLibraryQuery(current, { ...parseLibraryQuery(current), ...change }), {
        replace: true,
      })
    },
    [setParams],
  )

  return [query, update]
}
