import { afterEach, describe, expect, it } from 'vitest'
import type { Chapter } from '../api/types'
import { crossedBoundary, fadeLevel, FADE_SECONDS, sleepStore } from './sleepTimer'

const chapters: Chapter[] = [
  { sequence: 0, title: 'One', startOffsetSeconds: 0, endOffsetSeconds: 120 },
  { sequence: 1, title: 'Two', startOffsetSeconds: 120, endOffsetSeconds: 260 },
  { sequence: 2, title: 'Three', startOffsetSeconds: 260, endOffsetSeconds: 300 },
] as Chapter[]

describe('crossedBoundary', () => {
  it('finds the chapter start that playback ran across', () => {
    expect(crossedBoundary(chapters, 119.8, 120.05, 0.25)).toBe(120)
  })

  it('accounts for speed', () => {
    expect(crossedBoundary(chapters, 119.6, 120.1, 0.5)).toBe(120)
  })

  it('ignores moves within a chapter', () => {
    expect(crossedBoundary(chapters, 100, 100.25, 0.25)).toBeNull()
  })

  it('ignores a seek across a boundary', () => {
    // A 30 s skip from inside chapter one into chapter two.
    expect(crossedBoundary(chapters, 100, 130, 0.25)).toBeNull()
  })

  it('ignores going backwards', () => {
    expect(crossedBoundary(chapters, 121, 119, 0.25)).toBeNull()
  })

  it('needs chapters', () => {
    expect(crossedBoundary([], 10, 10.25, 0.25)).toBeNull()
  })

  it('counts a throttled update as playback if the time adds up', () => {
    // A background tab updated after 60 s at 1.5×.
    expect(crossedBoundary(chapters, 200, 290, 90)).toBe(260)
  })
})

describe('fadeLevel', () => {
  it('is full volume until the fade', () => {
    expect(fadeLevel({ kind: 'minutes', remaining: 300 })).toBe(1)
    expect(fadeLevel({ kind: 'off' })).toBe(1)
    expect(fadeLevel({ kind: 'chapter' })).toBe(1)
  })

  it('falls to nothing over the fade', () => {
    expect(fadeLevel({ kind: 'minutes', remaining: FADE_SECONDS / 2 })).toBeCloseTo(0.25)
    expect(fadeLevel({ kind: 'minutes', remaining: 0 })).toBe(0)
  })
})

describe('sleepStore', () => {
  afterEach(() => sleepStore.off())

  it('counts down and turns itself off when it runs out', () => {
    sleepStore.set({ kind: 'minutes', remaining: 2 })
    expect(sleepStore.countDown(1.5)).toBe(false)
    expect(sleepStore.get()).toEqual({ kind: 'minutes', remaining: 0.5 })
    expect(sleepStore.countDown(1)).toBe(true)
    expect(sleepStore.get()).toEqual({ kind: 'off' })
  })

  it('leaves an end-of-chapter timer alone', () => {
    sleepStore.set({ kind: 'chapter' })
    expect(sleepStore.countDown(100)).toBe(false)
    expect(sleepStore.get()).toEqual({ kind: 'chapter' })
  })

  it('re-renders once a second, then on every update during the fade', () => {
    let renders = 0
    const stop = sleepStore.subscribe(() => renders++)
    sleepStore.set({ kind: 'minutes', remaining: 60 })
    renders = 0
    for (let i = 0; i < 8; i++) sleepStore.countDown(0.25) // 2 s
    expect(renders).toBe(2)
    sleepStore.set({ kind: 'minutes', remaining: 5 })
    renders = 0
    for (let i = 0; i < 4; i++) sleepStore.countDown(0.25)
    expect(renders).toBe(4)
    stop()
  })
})
