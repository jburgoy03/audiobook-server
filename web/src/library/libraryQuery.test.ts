import { describe, expect, it } from 'vitest'
import { EMPTY_QUERY, isFiltering, parseLibraryQuery, writeLibraryQuery } from './libraryQuery'

const parse = (search: string) => parseLibraryQuery(new URLSearchParams(search))
const write = (search: string, query: Parameters<typeof writeLibraryQuery>[1]) =>
  writeLibraryQuery(new URLSearchParams(search), query).toString()

describe('parseLibraryQuery', () => {
  it('defaults when the URL has nothing', () => {
    expect(parse('')).toEqual(EMPTY_QUERY)
  })

  it('reads search, sort and statuses', () => {
    expect(parse('?q=murakami+wood&sort=added&status=inProgress&status=finished')).toEqual({
      q: 'murakami wood',
      sort: 'added',
      statuses: ['inProgress', 'finished'],
    })
  })

  it('ignores unknown values instead of failing', () => {
    expect(parse('?sort=rating&status=abandoned&status=finished')).toEqual({
      q: '',
      sort: 'title',
      statuses: ['finished'],
    })
  })

  it('returns statuses in a fixed order without duplicates', () => {
    expect(parse('?status=finished&status=unstarted&status=finished').statuses).toEqual([
      'unstarted',
      'finished',
    ])
  })

  it('keeps the search exactly as typed', () => {
    expect(parse('?q=%20Ender%E2%80%99s%20').q).toBe(' Ender’s ')
  })
})

describe('writeLibraryQuery', () => {
  it('leaves defaults out, so an unfiltered library is a bare URL', () => {
    expect(write('', EMPTY_QUERY)).toBe('')
    expect(write('', { q: '   ', sort: 'title', statuses: [] })).toBe('')
  })

  it('writes what differs from the defaults', () => {
    expect(write('', { q: 'le guin', sort: 'length', statuses: ['finished', 'unstarted'] })).toBe(
      'q=le+guin&sort=length&status=unstarted&status=finished',
    )
  })

  it('replaces old values and keeps unrelated params', () => {
    expect(write('q=old&status=finished&other=1', { q: 'new', sort: 'title', statuses: [] })).toBe(
      'other=1&q=new',
    )
  })

  it('round-trips', () => {
    const query = { q: 'Brontë', sort: 'author' as const, statuses: ['inProgress' as const] }
    expect(parse(write('', query))).toEqual(query)
  })
})

describe('isFiltering', () => {
  it('is true for a search or a status, not for a sort alone', () => {
    expect(isFiltering(EMPTY_QUERY)).toBe(false)
    expect(isFiltering({ ...EMPTY_QUERY, sort: 'added' })).toBe(false)
    expect(isFiltering({ ...EMPTY_QUERY, q: '  ' })).toBe(false)
    expect(isFiltering({ ...EMPTY_QUERY, q: 'x' })).toBe(true)
    expect(isFiltering({ ...EMPTY_QUERY, statuses: ['finished'] })).toBe(true)
  })
})
