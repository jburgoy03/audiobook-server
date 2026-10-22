import { describe, expect, it } from 'vitest'
import type { BookSummary } from '../api/types'
import { authorHref, authorSlug, coverBooks, groupByAuthor } from './authors'

let next = 0
const book = (author: string | null, title = `Book ${++next}`, hasCover = true): BookSummary => ({
  id: `b${++next}`,
  title,
  author,
  durationSeconds: 3600,
  hasCover,
  files: 1,
  chapters: 1,
  addedAt: '2026-10-01T00:00:00Z',
})

describe('authorSlug', () => {
  it('ignores case, accents and punctuation', () => {
    expect(authorSlug('J.R.R. Tolkien')).toBe('j-r-r-tolkien')
    expect(authorSlug('J. R. R. Tolkien')).toBe('j-r-r-tolkien')
    expect(authorSlug('Charlotte Brontë')).toBe('charlotte-bronte')
  })

  it('builds a URL-safe link', () => {
    expect(authorHref('Ursula K. Le Guin')).toBe('/authors/ursula-k-le-guin')
  })
})

describe('groupByAuthor', () => {
  it('groups spellings of one author under the most common one', () => {
    const books = [book('J. R. R. Tolkien'), book('J.R.R. Tolkien'), book('J.R.R. Tolkien')]
    const [tolkien] = groupByAuthor(books)
    expect(tolkien.name).toBe('J.R.R. Tolkien')
    expect(tolkien.books).toHaveLength(3)
  })

  it('sorts by name and leaves out books without an author', () => {
    const authors = groupByAuthor([book('Zadie Smith'), book(null), book('Andy Weir'), book('...')])
    expect(authors.map((a) => a.name)).toEqual(['Andy Weir', 'Zadie Smith'])
  })

  it('keeps co-written books as their own author', () => {
    const authors = groupByAuthor([book('Neil Gaiman'), book('Neil Gaiman, Terry Pratchett')])
    expect(authors).toHaveLength(2)
  })
})

describe('coverBooks', () => {
  it('prefers books with cover art, then title order', () => {
    const [author] = groupByAuthor([book('A', 'Charlie'), book('A', 'Alpha', false), book('A', 'Bravo')])
    expect(coverBooks(author, 3).map((b) => b.title)).toEqual(['Bravo', 'Charlie', 'Alpha'])
  })
})
