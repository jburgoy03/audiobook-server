import { useCallback, useLayoutEffect, useState, type ReactNode } from 'react'
import type { BookDetail } from '../api/types'
import { ActivateContext, nowPlayingStore, type Activate } from './nowPlaying'
import { useBookPlayer } from './useBookPlayer'

interface Activation {
  id: number
  book: BookDetail
  autoplay: boolean
  startAt?: number
}

let nextActivationId = 1

/**
 * Runs one book's playback and publishes it. Keyed by activation, so a new
 * activation gets a fresh hook and audio element, and the old one saves its
 * position and tears down. It renders nothing; the UI lives elsewhere.
 */
function Engine({ activation }: { activation: Activation }) {
  const player = useBookPlayer(activation.book, {
    autoplay: activation.autoplay,
    startAt: activation.startAt,
  })

  // Publish after every render, so readers see each time update.
  useLayoutEffect(() => {
    nowPlayingStore.set({ book: activation.book, player })
  })
  // Unpublish on unmount. React runs this before the next Engine's effects,
  // so a switch goes old → null → new, never new → null.
  useLayoutEffect(() => () => nowPlayingStore.set(null), [])

  return null
}

/** Owns the app's one active book. Sits above the routes, so playback survives navigation. */
export function PlayerProvider({ children }: { children: ReactNode }) {
  const [activation, setActivation] = useState<Activation | null>(null)

  const activate = useCallback<Activate>((book, options = {}) => {
    setActivation({
      id: nextActivationId++,
      book,
      autoplay: options.autoplay ?? false,
      startAt: options.startAt,
    })
  }, [])

  return (
    <ActivateContext value={activate}>
      {children}
      {activation && <Engine key={activation.id} activation={activation} />}
    </ActivateContext>
  )
}
