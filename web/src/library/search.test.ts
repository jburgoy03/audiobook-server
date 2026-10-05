import { describe, expect, it } from 'vitest'
import type { BookSummary } from '../api/types'
import { bookStatus, compareBooks, matchesQuery, normalize, queryWords, type SortKey } from './search'

function book(overrides: Partial<BookSummary> & Pick<BookSummary, 'title'>): BookSummary {
  return {
    id: overrides.title,
    author: null,
    durationSeconds: 3600,
    hasCover: false,
    files: 1,
    chapters: 1,
    addedAt: '2026-01-01T00:00:00.000Z',
    ...overrides,
  }
}

describe('normalize', () => {
  it('lowercases and drops diacritics', () => {
    expect(normalize('Brontë')).toBe('bronte')
    expect(normalize('GARCÍA MÁRQUEZ')).toBe('garcia marquez')
  })

  it('removes apostrophes without splitting the word', () => {
    expect(normalize("Ender's Game")).toBe('enders game')
    expect(normalize('Ender’s Game')).toBe('enders game')
  })

  it('turns other punctuation into single spaces', () => {
    expect(normalize('  Dune: Part One — (Book 1)  ')).toBe('dune part one book 1')
    expect(normalize('Le Guin, Ursula K.')).toBe('le guin ursula k')
  })

  it('keeps non-Latin letters and digits', () => {
    expect(normalize('ノルウェイの森 1987')).toBe('ノルウェイの森 1987')
  })
})

describe('queryWords', () => {
  it('is empty for blank or punctuation-only queries', () => {
    expect(queryWords('')).toEqual([])
    expect(queryWords('   ')).toEqual([])
    expect(queryWords(' -- ')).toEqual([])
  })
})

describe('matchesQuery', () => {
  const norwegian = book({ title: 'Norwegian Wood', author: 'Haruki Murakami' })
  const lamora = book({ title: 'The Lies of Locke Lamora', author: 'Scott Lynch' })
  const anonymous = book({ title: 'Beowulf', author: null })

  it('matches everything on an empty query', () => {
    expect(matchesQuery(norwegian, '')).toBe(true)
    expect(matchesQuery(anonymous, '  ')).toBe(true)
  })

  it('matches words across title and author, in any order', () => {
    expect(matchesQuery(norwegian, 'murakami wood')).toBe(true)
    expect(matchesQuery(norwegian, 'wood murakami')).toBe(true)
  })

  it('matches partial words', () => {
    expect(matchesQuery(lamora, 'lamora')).toBe(true)
    expect(matchesQuery(lamora, 'lies of')).toBe(true)
    expect(matchesQuery(norwegian, 'mura')).toBe(true)
  })

  it('requires every word', () => {
    expect(matchesQuery(norwegian, 'murakami lynch')).toBe(false)
  })

  it('ignores case, accents and punctuation on both sides', () => {
    expect(matchesQuery(book({ title: 'Jane Eyre', author: 'Charlotte Brontë' }), 'BRONTE')).toBe(true)
    expect(matchesQuery(book({ title: "Ender's Game" }), 'enders')).toBe(true)
    expect(matchesQuery(book({ title: "Ender's Game" }), "ender's")).toBe(true)
    expect(matchesQuery(lamora, 'locke, lamora!')).toBe(true)
  })

  it('handles a missing author', () => {
    expect(matchesQuery(anonymous, 'beowulf')).toBe(true)
    expect(matchesQuery(anonymous, 'null')).toBe(false)
  })

  it('does not match across the title/author boundary as one word', () => {
    // "Wood" + "Haruki" must not join into "woodharuki".
    expect(matchesQuery(norwegian, 'woodharuki')).toBe(false)
  })

  it('has no typo tolerance', () => {
    expect(matchesQuery(norwegian, 'murakamu')).toBe(false)
  })
})

describe('compareBooks', () => {
  const books = [
    book({ id: 'a', title: 'book 10', author: 'Zed', addedAt: '2026-03-01T00:00:00.000Z', durationSeconds: 500 }),
    book({ id: 'b', title: 'Book 2', author: null, addedAt: '2026-01-01T00:00:00.000Z', durationSeconds: 100 }),
    book({ id: 'c', title: 'Émile', author: 'alice', addedAt: '2026-02-01T00:00:00.000Z', durationSeconds: 300 }),
    book({ id: 'd', title: 'Antigone', author: 'Alice', addedAt: '2026-02-01T00:00:00.000Z', durationSeconds: 300 }),
  ]
  const order = (sort: SortKey) => [...books].sort(compareBooks(sort)).map((b) => b.id)

  it('sorts titles case- and accent-insensitively, with numbers in numeric order', () => {
    expect(order('title')).toEqual(['d', 'b', 'a', 'c'])
  })

  it('sorts by the full author string, ties by title, missing authors last', () => {
    expect(order('author')).toEqual(['d', 'c', 'a', 'b'])
  })

  it('sorts newest first, ties by title', () => {
    expect(order('added')).toEqual(['a', 'd', 'c', 'b'])
  })

  it('sorts shortest first, ties by title', () => {
    expect(order('length')).toEqual(['b', 'd', 'c', 'a'])
  })

  it('falls back to id when titles are equal, so the order is stable', () => {
    const twins = [book({ id: 'y', title: 'Same' }), book({ id: 'x', title: 'same' })]
    expect(twins.sort(compareBooks('title')).map((b) => b.id)).toEqual(['x', 'y'])
  })
})

describe('bookStatus', () => {
  it('is not started without a resume point', () => {
    expect(bookStatus(null)).toBe('unstarted')
  })

  it('counts as started from a minute in', () => {
    expect(bookStatus({ position: 0, isFinished: false })).toBe('unstarted')
    expect(bookStatus({ position: 59.9, isFinished: false })).toBe('unstarted')
    expect(bookStatus({ position: 60, isFinished: false })).toBe('inProgress')
    expect(bookStatus({ position: 4000, isFinished: false })).toBe('inProgress')
  })

  it('finished wins over position', () => {
    expect(bookStatus({ position: 0, isFinished: true })).toBe('finished')
    expect(bookStatus({ position: 4000, isFinished: true })).toBe('finished')
  })
})
