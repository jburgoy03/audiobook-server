import { useSyncExternalStore } from 'react'
import { api, HttpError } from '../api/client'
import type { ProgressDto } from '../api/types'
import {
  deviceId,
  deviceName,
  loadProgressCache,
  saveProgressCache,
  takeLegacyPositions,
} from './storage'

/**
 * Listening progress, synced with the server and cached in the browser.
 *
 * Reads are synchronous on purpose: a book's starting point is read as initial
 * state when it's activated, and the library lays itself out from it, and neither
 * can wait for a request. So reads come from memory, filled from the browser cache
 * at load and refreshed from GET /api/progress after sign-in and whenever the tab
 * becomes visible again. Writes go to memory and the cache first, then to the
 * server; a write the server hasn't confirmed stays `pending` and is retried on
 * the next refresh, so listening offline (or with an expired session) loses nothing.
 *
 * Each book keeps two positions:
 * - `local`: where this browser last was. A book resumes here.
 * - `server`: what the server holds, which may come from another device.
 * Keeping them apart is what lets the player say "further along on your phone,
 * jump?" instead of silently moving you. A position from the server never moves a
 * playing book: the player only reads the store when a book starts, and the jump
 * offer is the listener's decision.
 */

export interface LocalProgress {
  position: number
  /** When playback reached this point, by this browser's clock. */
  reportedAt: string
  isFinished: boolean
  /** Not yet confirmed by the server. */
  pending: boolean
  /** Sent as an override (see ProgressRules on the server); kept for retries. */
  override: boolean
}

export interface ServerProgress {
  position: number
  reportedAt: string
  isFinished: boolean
  deviceId: string | null
  deviceName: string | null
}

export interface BookProgress {
  local: LocalProgress | null
  server: ServerProgress | null
}

export type ProgressEntries = Readonly<Record<string, BookProgress>>

/** Where a book stands for display and resuming on this browser. */
export interface ResumePoint {
  position: number
  isFinished: boolean
  /** The latest activity on the book from any device, for "continue listening" order. */
  lastActivity: string
}

// The snapshot is replaced on every change, never mutated, so useSyncExternalStore
// can compare it by reference.
let entries: ProgressEntries = loadProgressCache<BookProgress>()
// Whether `server` reflects the server during this page's life, rather than only
// what was cached last time. The player won't act as authoritative until it does.
let loaded = false
let started = false
const listeners = new Set<() => void>()
const inflight = new Set<Promise<unknown>>()
// Per book, so two saves of the same book are sent in order, never overlapping.
const queues = new Map<string, Promise<unknown>>()

function emit() {
  for (const listener of listeners) listener()
}

function update(next: Record<string, BookProgress>) {
  entries = next
  saveProgressCache(entries)
  emit()
}

function setBook(bookId: string, change: (current: BookProgress) => BookProgress | null) {
  const current = entries[bookId] ?? { local: null, server: null }
  const next: Record<string, BookProgress> = { ...entries }
  const changed = change(current)
  if (changed) next[bookId] = changed
  else delete next[bookId]
  update(next)
}

/** ISO 8601 in UTC with a Z, like toISOString, so timestamps compare as strings. */
function normalizeTime(value: string): string {
  const ms = Date.parse(value)
  return Number.isNaN(ms) ? new Date(0).toISOString() : new Date(ms).toISOString()
}

function fromDto(dto: ProgressDto): ServerProgress {
  return {
    position: dto.positionSeconds,
    // The server writes "+00:00" with microseconds; the browser writes "Z" with
    // milliseconds. One format, so ordering by string comparison is ordering by time.
    reportedAt: normalizeTime(dto.reportedAt),
    isFinished: dto.isFinished,
    deviceId: dto.deviceId,
    deviceName: dto.deviceName,
  }
}

function track<T>(promise: Promise<T>): Promise<T> {
  inflight.add(promise)
  void promise.finally(() => inflight.delete(promise)).catch(() => {})
  return promise
}

/** Sends this browser's position for a book, if the server hasn't confirmed it yet. */
function send(bookId: string, keepalive = false): Promise<void> {
  const previous = queues.get(bookId) ?? Promise.resolve()
  const next = previous.then(async () => {
    const local = entries[bookId]?.local
    if (!local?.pending) return
    try {
      const result = await api.reportProgress(
        {
          bookId,
          positionSeconds: local.position,
          reportedAt: local.reportedAt,
          deviceId: deviceId(),
          deviceName: deviceName(),
          isFinished: local.isFinished,
          override: local.override,
        },
        keepalive,
      )
      setBook(bookId, (current) => ({
        // Accepted or not, the server has decided this report. Only clear `pending`
        // if nothing newer was written while the request was in flight.
        local:
          current.local && current.local.reportedAt === local.reportedAt
            ? { ...current.local, pending: false }
            : current.local,
        server: fromDto(result.progress),
      }))
    } catch (e) {
      // The book is gone (removed by a rescan): forget it. Anything else (offline,
      // signed out) leaves the report pending for the next refresh.
      if (e instanceof HttpError && e.status === 404) setBook(bookId, () => null)
    }
  })
  queues.set(bookId, next.catch(() => {}))
  return track(next)
}

function sendPending() {
  for (const [bookId, progress] of Object.entries(entries)) {
    if (progress.local?.pending) void send(bookId)
  }
}

/** Replaces every book's server half with what the server holds now. */
async function refresh(): Promise<void> {
  let list: ProgressDto[]
  try {
    list = await track(api.progress())
  } catch {
    return // Offline or signed out: keep the cache as it is.
  }
  const byBook = new Map(list.map((dto) => [dto.bookId, dto]))
  const next: Record<string, BookProgress> = {}
  for (const [bookId, progress] of Object.entries(entries)) {
    const dto = byBook.get(bookId)
    next[bookId] = { local: progress.local, server: dto ? fromDto(dto) : null }
    byBook.delete(bookId)
  }
  for (const [bookId, dto] of byBook) next[bookId] = { local: null, server: fromDto(dto) }
  loaded = true
  update(next)
  sendPending()
}

/**
 * Positions saved before sync existed become pending local positions, so the
 * first refresh uploads them. They're ordinary reports (not overrides), so
 * furthest-wins decides against anything already on the server. Finished-ness
 * was never stored, so it's inferred the way the old client did: within the last
 * five seconds.
 */
async function importLegacyPositions(): Promise<void> {
  const legacy = takeLegacyPositions()
  if (legacy.length === 0) return

  let durations = new Map<string, number>()
  try {
    durations = new Map((await api.books()).map((b) => [b.id, b.durationSeconds]))
  } catch {
    // Import without finished-ness rather than lose the positions.
  }

  const next: Record<string, BookProgress> = { ...entries }
  for (const { bookId, position, savedAt } of legacy) {
    if (next[bookId]?.local) continue
    const duration = durations.get(bookId)
    next[bookId] = {
      local: {
        position,
        reportedAt: normalizeTime(savedAt),
        isFinished: duration !== undefined && position >= duration - 5,
        pending: true,
        override: false,
      },
      server: next[bookId]?.server ?? null,
    }
  }
  update(next)
}

const onVisible = () => {
  if (document.visibilityState === 'visible') void refresh()
}
const onOnline = () => void refresh()

export const progressStore = {
  subscribe(listener: () => void) {
    listeners.add(listener)
    return () => {
      listeners.delete(listener)
    }
  },
  getSnapshot: (): ProgressEntries => entries,

  get: (bookId: string): BookProgress | undefined => entries[bookId],

  /** True once the server's state has been fetched since sign-in. */
  isLoaded: () => loaded,

  /**
   * Where to show and resume a book on this browser: its own last position if it
   * has one (another device being further ahead is offered, not imposed), else
   * the server's, for a book only ever played elsewhere.
   */
  resumePoint(bookId: string): ResumePoint | null {
    const progress = entries[bookId]
    const point = progress?.local ?? progress?.server
    if (!progress || !point) return null
    const times = [progress.local?.reportedAt, progress.server?.reportedAt].filter((t): t is string => !!t)
    return {
      position: point.position,
      isFinished: point.isFinished,
      lastActivity: times.sort().at(-1) ?? point.reportedAt,
    }
  },

  /**
   * Records this browser's position and sends it. Returns once sent (or failed);
   * callers don't need to wait, but sign-out does.
   */
  report(
    bookId: string,
    position: number,
    options: { isFinished: boolean; override: boolean; keepalive?: boolean },
  ): Promise<void> {
    setBook(bookId, (current) => ({
      server: current.server,
      local: {
        position,
        reportedAt: new Date().toISOString(),
        isFinished: options.isFinished,
        pending: true,
        override: options.override,
      },
    }))
    return send(bookId, options.keepalive)
  },

  /** After sign-in: migrate old positions, load the server's, upload anything pending. */
  async start(): Promise<void> {
    if (started) return
    started = true
    document.addEventListener('visibilitychange', onVisible)
    window.addEventListener('online', onOnline)
    await importLegacyPositions()
    await refresh()
  },

  /**
   * At sign-out. Forgets the server's half; keeps this browser's own positions,
   * including any still pending, which upload after the next sign-in.
   */
  stop(): void {
    started = false
    loaded = false
    document.removeEventListener('visibilitychange', onVisible)
    window.removeEventListener('online', onOnline)
    const next: Record<string, BookProgress> = {}
    for (const [bookId, progress] of Object.entries(entries)) {
      if (progress.local) next[bookId] = { local: progress.local, server: null }
    }
    update(next)
  },

  /** Resolves when every request in flight has finished. */
  async flush(): Promise<void> {
    while (inflight.size > 0) await Promise.allSettled([...inflight])
  },
}

/** Every book's progress; re-renders on any change. */
export function useProgressEntries(): ProgressEntries {
  return useSyncExternalStore(progressStore.subscribe, progressStore.getSnapshot)
}
