import type { BookDetail, BookSummary, Library } from './types'

async function getJson<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(path, { signal, headers: { Accept: 'application/json' } })
  if (!response.ok) {
    throw new Error(`GET ${path} failed: ${response.status} ${response.statusText}`)
  }
  return (await response.json()) as T
}

export const api = {
  libraries: (signal?: AbortSignal) => getJson<Library[]>('/api/libraries', signal),
  books: (signal?: AbortSignal) => getJson<BookSummary[]>('/api/books', signal),
  book: (id: string, signal?: AbortSignal) => getJson<BookDetail>(`/api/books/${id}`, signal),

  /** Only meaningful when the book's hasCover is true. */
  coverUrl: (bookId: string) => `/api/books/${bookId}/cover`,

  /** A URL, not a fetch: the <audio> element requests it, with its own Range headers. */
  streamUrl: (bookId: string, sequence: number) => `/api/books/${bookId}/files/${sequence}/stream`,
}
