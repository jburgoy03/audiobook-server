import { useSyncExternalStore } from 'react'
import type { Chapter } from '../api/types'
import { chapterIndexAt } from './timeline'

/**
 * The sleep timer: one for the whole app, like volume, so it carries on when
 * another book starts and needs no player to be set. The engine (useBookPlayer)
 * drives it from the audio element's own time updates and acts on it.
 *
 * - Minutes count down **only while audio plays**. Pausing to answer the door
 *   keeps the time you had; with wall-clock time, resuming after a long pause
 *   would pause again at once. The cost: a timer left on survives a pause of any
 *   length.
 * - The last FADE_SECONDS fade the volume out, then playback pauses and steps
 *   back over the faded stretch, so resuming replays what you half heard.
 * - End of chapter means the chapter you're in when playback reaches its end:
 *   skipping ahead moves the target with you. It stops exactly on the boundary,
 *   with no fade (the chapter's own pause is the fade).
 *
 * Not remembered across reloads, deliberately: a timer that reappears after a
 * reload would pause a book nobody asked to time.
 *
 * The Android app applies the same rules (sharedLogic playback/SleepClock.kt).
 */
export type SleepTimer =
  | { kind: 'off' }
  | { kind: 'minutes'; remaining: number }
  | { kind: 'chapter' }

export const SLEEP_MINUTES = [5, 15, 30, 45, 60]
export const FADE_SECONDS = 10

// How far the position may stray from what the elapsed time predicts and still
// count as playback rather than a seek. Time updates come every ~250 ms; a seek
// across a chapter boundary moves seconds to minutes.
const NATURAL_SLACK_SECONDS = 2

/**
 * The chapter start that playback ran across going from `from` to `to`, or null:
 * no boundary crossed, or the move was a seek (a skip, a scrub, Next chapter).
 * `expected` is how far playback should have moved: elapsed time × speed.
 */
export function crossedBoundary(chapters: Chapter[], from: number, to: number, expected: number): number | null {
  if (to <= from || Math.abs(to - from - expected) > NATURAL_SLACK_SECONDS) return null
  const i = chapterIndexAt(chapters, from)
  if (i < 0 || chapterIndexAt(chapters, to) <= i) return null
  return chapters[i + 1]?.startOffsetSeconds ?? null
}

/**
 * The volume multiplier while a minutes timer runs out: 1 until the last
 * FADE_SECONDS, then down to 0. Squared, like the volume slider, so the fade
 * sounds even rather than doing nothing for a while and then dropping away.
 */
export function fadeLevel(timer: SleepTimer): number {
  if (timer.kind !== 'minutes' || timer.remaining >= FADE_SECONDS) return 1
  const x = Math.max(0, timer.remaining) / FADE_SECONDS
  return x * x
}

let state: SleepTimer = { kind: 'off' }
const listeners = new Set<() => void>()

function emit() {
  for (const listener of listeners) listener()
}

export const sleepStore = {
  get: (): SleepTimer => state,
  subscribe(listener: () => void) {
    listeners.add(listener)
    return () => {
      listeners.delete(listener)
    }
  },

  set(next: SleepTimer) {
    state = next
    emit()
  },

  off() {
    if (state.kind !== 'off') sleepStore.set({ kind: 'off' })
  },

  /**
   * Counts down a minutes timer by `seconds` of playback. Returns true when it
   * has run out (and is now off). Readers re-render once a second, the
   * countdown's resolution, except during the fade, when the volume follows
   * every update.
   */
  countDown(seconds: number): boolean {
    if (state.kind !== 'minutes' || seconds <= 0) return false
    const before = state.remaining
    const remaining = before - seconds
    if (remaining <= 0) {
      sleepStore.set({ kind: 'off' })
      return true
    }
    state = { kind: 'minutes', remaining }
    if (Math.ceil(remaining) !== Math.ceil(before) || remaining < FADE_SECONDS + 1) emit()
    return false
  },
}

export function useSleepTimer(): SleepTimer {
  return useSyncExternalStore(sleepStore.subscribe, sleepStore.get)
}
