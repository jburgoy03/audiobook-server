import { useSyncExternalStore } from 'react'
import { loadVolume, saveVolume } from './storage'

/**
 * Volume is one setting for the whole app, not part of any book's player: the
 * book page, the featured book and the now-playing bar all show and set the same
 * thing, and each new audio element (one per activation) picks it up. The player
 * subscribes and applies it (see useBookPlayer), so a control needs no player.
 *
 * `level` is the slider's position, 0–1, remembered per browser like speed (how
 * loud is right belongs to the device, so it isn't synced). `muted` is not
 * remembered: a tab that reloads silent looks broken.
 */
export interface VolumeState {
  level: number
  muted: boolean
}

/**
 * Whether this browser lets a page set the volume. iPhone Safari doesn't:
 * `audio.volume` is read-only there and always 1, and only `muted` works. Tested
 * by setting it and reading it back rather than guessing from the browser name.
 */
export const canSetVolume = (() => {
  try {
    const probe = new Audio()
    probe.volume = 0.5
    return Math.abs(probe.volume - 0.5) < 0.01
  } catch {
    return false
  }
})()

// Where a muted level-0 slider goes when unmuted, so unmute is never silent.
const UNMUTE_FLOOR = 0.5

let state: VolumeState = { level: loadVolume(), muted: false }
const listeners = new Set<() => void>()

function set(next: VolumeState) {
  state = next
  for (const listener of listeners) listener()
}

export const volumeStore = {
  get: (): VolumeState => state,
  subscribe(listener: () => void) {
    listeners.add(listener)
    return () => {
      listeners.delete(listener)
    }
  },

  /** From the slider. Moving it unmutes, as in most players. */
  setLevel(level: number) {
    const clamped = Math.min(1, Math.max(0, level))
    saveVolume(clamped)
    set({ level: clamped, muted: false })
  },

  toggleMute() {
    if (state.muted || state.level === 0) {
      const level = state.level === 0 ? UNMUTE_FLOOR : state.level
      if (level !== state.level) saveVolume(level)
      set({ level, muted: false })
    } else {
      set({ ...state, muted: true })
    }
  },
}

/**
 * What the audio element should be set to. Loudness is heard roughly on a log
 * scale, so a linear slider puts nearly all the audible change in its bottom
 * fifth. Squaring is the usual approximation: halfway is a quarter of the power,
 * which sounds about half as loud. A heuristic, not a measurement.
 */
export function elementVolume(level: number): number {
  return level * level
}

export function useVolume(): VolumeState {
  return useSyncExternalStore(volumeStore.subscribe, volumeStore.get)
}
