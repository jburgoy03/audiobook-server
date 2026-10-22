import { useCallback, useMemo } from 'react'
import { useLocation, useNavigate, useSearchParams } from 'react-router'
import { loadLibrarySort, saveLibrarySort } from '../player/storage'
import { BOOK_STATUSES, SORT_KEYS, type BookStatus, type SortKey } from './search'

/**
 * The library page's search, sort and filters, kept in the URL:
 * `/library?q=murakami&sort=added&status=unstarted&status=inProgress`.
 *
 * The URL rather than component state, so Back from a book returns to the same
 * results, a reload keeps them, and a link can carry them.
 *
 * The one exception is sort: the last sort picked is saved per browser and becomes
 * the default, so a plain `/` opens in your order. Search and status filters are
 * not saved: coming back to a library that silently hides books is confusing,
 * while a remembered order hides nothing.
 */
export interface LibraryQuery {
  /** As typed, not normalized: the search box shows exactly what was entered. */
  q: string
  sort: SortKey
  /** Empty means no status filter. Several are alternatives (OR). */
  statuses: readonly BookStatus[]
}

/** Where All books lives. The home page (/) holds shelves instead. */
export const LIBRARY_PATH = '/library'

/** The sort when neither the URL nor this browser has one. */
export const DEFAULT_SORT: SortKey = 'title'

/** The sort a URL without `sort=` means: the one this browser saved, else title. */
export function defaultSort(): SortKey {
  return loadLibrarySort() ?? DEFAULT_SORT
}

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
export function parseLibraryQuery(params: URLSearchParams, fallbackSort: SortKey = DEFAULT_SORT): LibraryQuery {
  const sort = params.get('sort')
  const statuses = new Set(params.getAll('status').filter(isStatus))
  return {
    q: params.get('q') ?? '',
    sort: isSortKey(sort) ? sort : fallbackSort,
    statuses: BOOK_STATUSES.filter((s) => statuses.has(s)),
  }
}

/**
 * Writes the query into a copy of `params`, leaving any other params alone.
 * Defaults are left out, so an unfiltered library is plain `/`. A blank search
 * (only spaces) is left out too, since it matches everything. Pass the same
 * `fallbackSort` the reader will parse with, or a dropped sort reads back wrong.
 */
export function writeLibraryQuery(
  params: URLSearchParams,
  query: LibraryQuery,
  fallbackSort: SortKey = DEFAULT_SORT,
): URLSearchParams {
  const next = new URLSearchParams(params)
  next.delete('q')
  next.delete('sort')
  next.delete('status')
  if (query.q.trim() !== '') next.set('q', query.q)
  if (query.sort !== fallbackSort) next.set('sort', query.sort)
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
 *
 * Used off the library page too (the masthead search on the home page): there a
 * change goes to All books with the query, as a new history entry, so Back
 * returns home. The toolbar stays mounted across that move, so typing carries on.
 */
export function useLibraryQuery(): [LibraryQuery, (change: Partial<LibraryQuery>) => void] {
  const [params, setParams] = useSearchParams()
  const { pathname } = useLocation()
  const navigate = useNavigate()
  const onLibrary = pathname === LIBRARY_PATH
  // Keyed on the string so a new URLSearchParams with the same contents doesn't
  // look like a new query to memoized consumers.
  const key = params.toString()
  // Read each render (one localStorage read): every sort change re-renders via the
  // URL, so the saved value is never stale here.
  const fallback = defaultSort()
  const query = useMemo(() => parseLibraryQuery(new URLSearchParams(key), fallback), [key, fallback])

  const update = useCallback(
    (change: Partial<LibraryQuery>) => {
      // Only an explicit pick saves. Opening a link with ?sort=length shows that
      // order without replacing your saved one.
      if (change.sort) saveLibrarySort(change.sort)
      const fallbackNow = defaultSort()
      if (!onLibrary) {
        const next = writeLibraryQuery(new URLSearchParams(), { ...parseLibraryQuery(new URLSearchParams(), fallbackNow), ...change }, fallbackNow)
        const search = next.toString()
        navigate(search ? `${LIBRARY_PATH}?${search}` : LIBRARY_PATH)
        return
      }
      setParams(
        (current) =>
          writeLibraryQuery(current, { ...parseLibraryQuery(current, fallbackNow), ...change }, fallbackNow),
        { replace: true },
      )
    },
    [setParams, onLibrary, navigate],
  )

  // Off the library, the URL's params belong to another page; show an empty query.
  return [onLibrary ? query : parseLibraryQuery(new URLSearchParams(), fallback), update]
}
