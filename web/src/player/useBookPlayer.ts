import { useCallback, useEffect, useRef, useState } from 'react'
import { api } from '../api/client'
import type { BookDetail } from '../api/types'
import { chapterIndexAt, clampPosition, locate, totalDuration } from './timeline'
import { loadPosition, loadRate, savePosition, saveRate } from './storage'

const SAVE_INTERVAL_MS = 30_000

// Within this many seconds of the end, a saved position counts as finished and
// the book reopens at the start rather than on the last few seconds.
const FINISHED_MARGIN_SECONDS = 5

/** Where this browser left off, or the start if the book was finished. */
export function resumePosition(book: BookDetail) {
  const start = loadPosition(book.id) ?? 0
  if (start >= totalDuration(book.files) - FINISHED_MARGIN_SECONDS) return 0
  return clampPosition(book.files, start)
}

/**
 * Plays a multi-file book as one timeline through a single <audio> element.
 *
 * The element only ever knows about one file. Everything the UI sees is in book
 * seconds: position = current file's start offset + audio.currentTime. Seeks are
 * translated back into (file, offset); a seek into another file swaps the src,
 * and the offset is applied once the new file's metadata has loaded.
 *
 * One hook instance serves one book, for its whole life: PlayerProvider keys it
 * by activation, so the starting point and autoplay are read once, as initial
 * state, and later changes to the options are ignored.
 */
export function useBookPlayer(
  book: BookDetail,
  { autoplay = false, startAt }: { autoplay?: boolean; startAt?: number } = {},
) {
  // Whether to start playing as soon as the starting point has loaded.
  const [autoplayOnLoad] = useState(autoplay)
  const audioRef = useRef<HTMLAudioElement | null>(null)
  const fileIndexRef = useRef(0)
  const pendingSeekRef = useRef<number | null>(null)
  const pendingPlayRef = useRef(false)
  // Where to start: an explicit point (a chapter picked on an idle book), or
  // where this browser left off.
  const [position, setPosition] = useState(() =>
    startAt !== undefined ? clampPosition(book.files, startAt) : resumePosition(book),
  )
  const positionRef = useRef(position)

  const [playing, setPlaying] = useState(false)
  const [loading, setLoading] = useState(true)
  const [rate, setRateState] = useState(loadRate)
  const [error, setError] = useState<string | null>(null)

  const total = totalDuration(book.files)

  const updatePosition = useCallback((value: number) => {
    positionRef.current = value
    setPosition(value)
  }, [])

  const save = useCallback(() => savePosition(book.id, positionRef.current), [book.id])

  /** Points the element at a file and queues the offset (and play) for when metadata arrives. */
  const loadFile = useCallback(
    (index: number, offset: number, autoplay: boolean) => {
      const audio = audioRef.current
      const file = book.files[index]
      if (!audio || !file) return

      fileIndexRef.current = index
      pendingSeekRef.current = offset
      pendingPlayRef.current = autoplay
      setLoading(true)
      setError(null)
      audio.src = api.streamUrl(book.id, file.sequence)
    },
    [book.files, book.id],
  )

  // One element per book. Created here rather than rendered, since nothing about
  // it is visual; the UI drives it entirely through the functions returned below.
  useEffect(() => {
    const audio = new Audio()
    audio.preload = 'auto'
    audio.defaultPlaybackRate = loadRate()
    audio.playbackRate = audio.defaultPlaybackRate
    audioRef.current = audio

    const onLoadedMetadata = () => {
      if (pendingSeekRef.current !== null) {
        audio.currentTime = pendingSeekRef.current
        pendingSeekRef.current = null
      }
      setLoading(false)
      if (pendingPlayRef.current) {
        pendingPlayRef.current = false
        audio.play().catch(() => setPlaying(false))
      }
    }

    const onTimeUpdate = () => {
      const file = book.files[fileIndexRef.current]
      if (file && pendingSeekRef.current === null) {
        updatePosition(file.startOffsetSeconds + audio.currentTime)
      }
    }

    const onEnded = () => {
      const next = fileIndexRef.current + 1
      if (next < book.files.length) {
        loadFile(next, 0, true)
        save()
      } else {
        updatePosition(totalDuration(book.files))
        setPlaying(false)
        save()
      }
    }

    const onPlay = () => setPlaying(true)
    const onPause = () => {
      setPlaying(false)
      save()
    }
    const onWaiting = () => setLoading(true)
    const onPlaying = () => setLoading(false)
    const onError = () => {
      setLoading(false)
      setPlaying(false)
      setError(`Could not load file ${fileIndexRef.current + 1} of ${book.files.length}.`)
    }

    audio.addEventListener('loadedmetadata', onLoadedMetadata)
    audio.addEventListener('timeupdate', onTimeUpdate)
    audio.addEventListener('ended', onEnded)
    audio.addEventListener('play', onPlay)
    audio.addEventListener('pause', onPause)
    audio.addEventListener('waiting', onWaiting)
    audio.addEventListener('playing', onPlaying)
    audio.addEventListener('error', onError)

    // Start at the initial position (from storage, or startAt).
    const { index, offset } = locate(book.files, positionRef.current)
    loadFile(index, offset, autoplayOnLoad)

    // Leaving the page or switching tabs is the last reliable moment to save.
    const onHide = () => save()
    window.addEventListener('pagehide', onHide)
    document.addEventListener('visibilitychange', onHide)

    return () => {
      save()
      window.removeEventListener('pagehide', onHide)
      document.removeEventListener('visibilitychange', onHide)
      audio.removeEventListener('loadedmetadata', onLoadedMetadata)
      audio.removeEventListener('timeupdate', onTimeUpdate)
      audio.removeEventListener('ended', onEnded)
      audio.removeEventListener('play', onPlay)
      audio.removeEventListener('pause', onPause)
      audio.removeEventListener('waiting', onWaiting)
      audio.removeEventListener('playing', onPlaying)
      audio.removeEventListener('error', onError)
      audio.pause()
      audio.removeAttribute('src')
      audio.load()
      audioRef.current = null
    }
  }, [book.id, book.files, loadFile, save, updatePosition, autoplayOnLoad])

  // Periodic save while playing, so a crash or killed tab loses at most 30s.
  useEffect(() => {
    if (!playing) return
    const id = window.setInterval(save, SAVE_INTERVAL_MS)
    return () => window.clearInterval(id)
  }, [playing, save])

  const seek = useCallback(
    (target: number) => {
      const audio = audioRef.current
      if (!audio) return
      const t = clampPosition(book.files, target)
      const { index, offset } = locate(book.files, t)

      if (index === fileIndexRef.current) {
        if (audio.readyState >= HTMLMediaElement.HAVE_METADATA) audio.currentTime = offset
        else pendingSeekRef.current = offset
      } else {
        loadFile(index, offset, !audio.paused || pendingPlayRef.current)
      }
      updatePosition(t)
    },
    [book.files, loadFile, updatePosition],
  )

  const skip = useCallback((delta: number) => seek(positionRef.current + delta), [seek])

  const toggle = useCallback(() => {
    const audio = audioRef.current
    if (!audio) return
    if (audio.paused) {
      // At the very end, play means start over.
      if (positionRef.current >= totalDuration(book.files) - 0.5) {
        seek(0)
      }
      audio.play().catch(() => setPlaying(false))
    } else {
      audio.pause()
    }
  }, [book.files, seek])

  const setRate = useCallback((value: number) => {
    const audio = audioRef.current
    if (audio) {
      // defaultPlaybackRate survives a src change; playbackRate alone resets to it.
      audio.defaultPlaybackRate = value
      audio.playbackRate = value
    }
    setRateState(value)
    saveRate(value)
  }, [])

  const chapterIndex = chapterIndexAt(book.chapters, position)

  const seekChapter = useCallback(
    (index: number) => {
      const chapter = book.chapters[index]
      if (chapter) seek(chapter.startOffsetSeconds)
    },
    [book.chapters, seek],
  )

  /** Restarts the current chapter, or goes to the previous one if within its first 3 seconds. */
  const previousChapter = useCallback(() => {
    const i = chapterIndexAt(book.chapters, positionRef.current)
    const chapter = book.chapters[i]
    if (!chapter) return
    const intoChapter = positionRef.current - chapter.startOffsetSeconds
    seekChapter(intoChapter > 3 || i === 0 ? i : i - 1)
  }, [book.chapters, seekChapter])

  const nextChapter = useCallback(() => {
    const i = chapterIndexAt(book.chapters, positionRef.current)
    if (i + 1 < book.chapters.length) seekChapter(i + 1)
  }, [book.chapters, seekChapter])

  // OS media controls: keyboard media keys, lock screen, browser media hub.
  // Positions reported here are book positions, so the OS shows the whole book.
  useEffect(() => {
    if (!('mediaSession' in navigator)) return
    const session = navigator.mediaSession
    const handlers: [MediaSessionAction, MediaSessionActionHandler][] = [
      ['play', () => toggle()],
      ['pause', () => toggle()],
      ['seekbackward', (d) => skip(-(d.seekOffset ?? 30))],
      ['seekforward', (d) => skip(d.seekOffset ?? 30)],
      ['seekto', (d) => d.seekTime !== undefined && seek(d.seekTime)],
      ['previoustrack', () => previousChapter()],
      ['nexttrack', () => nextChapter()],
    ]
    for (const [action, handler] of handlers) {
      try {
        session.setActionHandler(action, handler)
      } catch {
        // Not every browser supports every action.
      }
    }
    return () => {
      for (const [action] of handlers) {
        try {
          session.setActionHandler(action, null)
        } catch {
          // Ignore.
        }
      }
    }
  }, [toggle, skip, seek, previousChapter, nextChapter])

  const chapterTitle = book.chapters[chapterIndex]?.title
  useEffect(() => {
    if (!('mediaSession' in navigator)) return
    navigator.mediaSession.metadata = new MediaMetadata({
      title: chapterTitle ?? book.title,
      artist: book.author ?? undefined,
      album: book.title,
      // Shown in the OS media flyout and on lock screens. Needs an absolute URL.
      artwork: book.hasCover
        ? [{ src: new URL(api.coverUrl(book.id), window.location.origin).href }]
        : [],
    })
  }, [book.id, book.title, book.author, book.hasCover, chapterTitle])

  useEffect(() => {
    if (!('mediaSession' in navigator) || total <= 0) return
    navigator.mediaSession.playbackState = playing ? 'playing' : 'paused'
    try {
      navigator.mediaSession.setPositionState({
        duration: total,
        position: Math.min(position, total),
        playbackRate: rate,
      })
    } catch {
      // Throws if the values are momentarily inconsistent; the next update fixes it.
    }
  }, [playing, position, rate, total])

  return {
    position,
    total,
    playing,
    loading,
    error,
    rate,
    chapterIndex,
    toggle,
    seek,
    skip,
    setRate,
    seekChapter,
    previousChapter,
    nextChapter,
  }
}

/** Everything the UI can read from, and do to, a book's playback. */
export type BookPlayer = ReturnType<typeof useBookPlayer>
