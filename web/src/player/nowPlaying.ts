import { createContext, useContext, useSyncExternalStore } from 'react'
import type { BookDetail } from '../api/types'
import type { BookPlayer } from './useBookPlayer'

/**
 * The app has one active book at a time, and its playback outlives any page:
 * Resume on the library starts it in place, and it keeps playing while you
 * browse. PlayerProvider owns the audio; everything else reads it from here.
 *
 * The live player state is published to a small external store rather than a
 * context value, so only the components that read it (the player, the now
 * playing bar, the featured book) re-render on every time update, not the
 * whole app.
 */
export interface NowPlaying {
  book: BookDetail
  player: BookPlayer
}

export interface ActivateOptions {
  /** Start playing as soon as the starting point has loaded. */
  autoplay?: boolean
  /** Book seconds to start at. Defaults to where this browser left off. */
  startAt?: number
}

/** Makes `book` the active book, replacing (and saving) whatever was active. */
export type Activate = (book: BookDetail, options?: ActivateOptions) => void

let current: NowPlaying | null = null
const listeners = new Set<() => void>()

export const nowPlayingStore = {
  get: (): NowPlaying | null => current,
  set(value: NowPlaying | null) {
    current = value
    for (const listener of listeners) listener()
  },
  subscribe(listener: () => void) {
    listeners.add(listener)
    return () => {
      listeners.delete(listener)
    }
  },
}

/** The active book and its live player, or null when nothing has been played. */
export function useNowPlaying() {
  return useSyncExternalStore(nowPlayingStore.subscribe, nowPlayingStore.get)
}

/** The live player for `bookId` if it's the active book, otherwise null. */
export function useLivePlayer(bookId: string) {
  const now = useNowPlaying()
  return now && now.book.id === bookId ? now.player : null
}

export const ActivateContext = createContext<Activate>(() => {
  throw new Error('useActivate needs a PlayerProvider above it.')
})

export function useActivate() {
  return useContext(ActivateContext)
}
