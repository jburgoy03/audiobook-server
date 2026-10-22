import type { BookSummary } from '../api/types'
import { normalize } from './search'

/** An author as the library groups them: every book whose author string matches. */
export interface Author {
  /** URL key: the normalized name with hyphens ("ursula-k-le-guin"). */
  slug: string
  /** The spelling most of their books use. */
  name: string
  books: BookSummary[]
}

/**
 * The author's URL key. Built on search's normalize, so "J.R.R. Tolkien",
 * "J. R. R. Tolkien" and "j r r tolkien" are one author, and so is "Brontë" with
 * "Bronte". Empty for a name that's only punctuation.
 */
export function authorSlug(name: string): string {
  return normalize(name).replace(/ /g, '-')
}

export function authorHref(name: string): string {
  return `/authors/${encodeURIComponent(authorSlug(name))}`
}

const collator = new Intl.Collator(undefined, { sensitivity: 'base', numeric: true })

/**
 * Books grouped by author, A to Z by name.
 *
 * The author string is taken as written: "Neil Gaiman, Terry Pratchett" is its
 * own author, not two. Splitting on commas and "and" is a heuristic that breaks
 * on names like "Martin Luther King, Jr.", so co-written books wait for real
 * contributor data. Books without an author aren't anyone's, so they're left out.
 */
export function groupByAuthor(books: readonly BookSummary[]): Author[] {
  const groups = new Map<string, { books: BookSummary[]; spellings: Map<string, number> }>()
  for (const book of books) {
    if (book.author === null) continue
    const slug = authorSlug(book.author)
    if (slug === '') continue
    let group = groups.get(slug)
    if (!group) {
      group = { books: [], spellings: new Map() }
      groups.set(slug, group)
    }
    group.books.push(book)
    group.spellings.set(book.author, (group.spellings.get(book.author) ?? 0) + 1)
  }

  const authors: Author[] = []
  for (const [slug, group] of groups) {
    // Most common spelling; ties go to the first seen (Map keeps insertion order).
    let name = ''
    let best = 0
    for (const [spelling, count] of group.spellings) {
      if (count > best) {
        name = spelling
        best = count
      }
    }
    authors.push({ slug, name, books: group.books })
  }
  return authors.sort((a, b) => collator.compare(a.name, b.name) || a.slug.localeCompare(b.slug))
}

/**
 * Most books first; equal counts stay in name order. Takes a list already in
 * name order (groupByAuthor's) and relies on sort being stable.
 */
export function byBookCount(authors: readonly Author[]): Author[] {
  return [...authors].sort((a, b) => b.books.length - a.books.length)
}

/** Up to `count` books to show for an author: ones with cover art first, then by title. */
export function coverBooks(author: Author, count: number): BookSummary[] {
  return [...author.books]
    .sort((a, b) => Number(b.hasCover) - Number(a.hasCover) || collator.compare(a.title, b.title))
    .slice(0, count)
}
