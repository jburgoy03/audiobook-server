import { describe, expect, it } from 'vitest'
import type { BookSummary } from '../api/types'
import { groupByAuthor } from './authors'
import type { BookStatus } from './search'
import { randomSeed, recentlyAdded, shortListens, startSomethingNew, topAuthors } from './shelves'

const HOUR = 3600
const book = (id: string, over: Partial<BookSummary> = {}): BookSummary => ({
  id,
  title: id,
  author: null,
  durationSeconds: 10 * HOUR,
  hasCover: true,
  files: 1,
  chapters: 1,
  addedAt: '2026-10-01T00:00:00Z',
  ...over,
})
const statuses = (map: Record<string, BookStatus>) => (id: string) => map[id] ?? 'unstarted'

describe('recentlyAdded', () => {
  it('is newest first, capped', () => {
    const books = [
      book('old', { addedAt: '2026-01-01T00:00:00Z' }),
      book('new', { addedAt: '2026-10-01T00:00:00Z' }),
      book('mid', { addedAt: '2026-05-01T00:00:00Z' }),
    ]
    expect(recentlyAdded(books, 2).map((b) => b.id)).toEqual(['new', 'mid'])
  })
})

describe('shortListens', () => {
  it('keeps unfinished books under six hours, shortest first', () => {
    const books = [
      book('long', { durationSeconds: 9 * HOUR }),
      book('five', { durationSeconds: 5 * HOUR }),
      book('two', { durationSeconds: 2 * HOUR }),
      book('done', { durationSeconds: HOUR }),
      book('empty', { durationSeconds: 0 }),
    ]
    expect(shortListens(books, statuses({ done: 'finished' })).map((b) => b.id)).toEqual(['two', 'five'])
  })
})

describe('startSomethingNew', () => {
  const books = Array.from({ length: 30 }, (_, i) => book(`b${i}`))

  it('only offers unstarted books not on another shelf', () => {
    const picks = startSomethingNew(books, statuses({ b0: 'inProgress', b1: 'finished' }), 'x', new Set(['b2']), 30)
    const ids = picks.map((b) => b.id)
    expect(ids).toHaveLength(27)
    expect(ids).not.toContain('b0')
    expect(ids).not.toContain('b1')
    expect(ids).not.toContain('b2')
  })

  it('is stable for a seed and changes with it', () => {
    const a = startSomethingNew(books, statuses({}), '2026-10-22').map((b) => b.id)
    const again = startSomethingNew(books, statuses({}), '2026-10-22').map((b) => b.id)
    const tomorrow = startSomethingNew(books, statuses({}), '2026-10-23').map((b) => b.id)
    expect(again).toEqual(a)
    expect(tomorrow).not.toEqual(a)
  })
})

describe('topAuthors', () => {
  it('puts the most books first, then name order', () => {
    const authors = groupByAuthor([
      book('1', { author: 'Brandon Sanderson' }),
      book('2', { author: 'Brandon Sanderson' }),
      book('3', { author: 'Andy Weir' }),
      book('4', { author: 'Zadie Smith' }),
    ])
    expect(topAuthors(authors).map((a) => a.name)).toEqual(['Brandon Sanderson', 'Andy Weir', 'Zadie Smith'])
  })
})

describe('randomSeed', () => {
  it('is 16 hex characters and differs between calls', () => {
    const a = randomSeed()
    expect(a).toMatch(/^[0-9a-f]{16}$/)
    expect(randomSeed()).not.toBe(a)
  })
})
