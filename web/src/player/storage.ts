// Everything the web client keeps in browser storage, behind one seam.
// Every access is wrapped: storage can be unavailable (private windows, blocked
// site data) and the player must still work without it, just without memory.
//
// Positions themselves live in the progress store (progress.ts), which syncs with
// the server; this file only persists its offline cache. Speed and volume stay
// per browser.

const RATE_KEY = 'audiobook:rate'
const VOLUME_KEY = 'audiobook:volume'
const DEVICE_KEY = 'audiobook:device'
const PROGRESS_KEY = 'audiobook:progress'
// Before sync, each book had its own key. Read once, uploaded, then removed.
const LEGACY_POSITION_PREFIX = 'audiobook:position:'

function read(key: string): string | null {
  try {
    return localStorage.getItem(key)
  } catch {
    return null
  }
}

function write(key: string, value: string): void {
  try {
    localStorage.setItem(key, value)
  } catch {
    // Best effort.
  }
}

// ---- Playback speed: one setting for every book, per browser ----

export function loadRate(): number {
  const rate = Number(read(RATE_KEY))
  return rate >= 0.5 && rate <= 3 ? rate : 1
}

export function saveRate(rate: number): void {
  write(RATE_KEY, String(rate))
}

// ---- Volume: the slider's position (0–1), one setting for every book ----

export function loadVolume(): number {
  const stored = read(VOLUME_KEY)
  const level = stored === null ? NaN : Number(stored)
  return level >= 0 && level <= 1 ? level : 1
}

export function saveVolume(level: number): void {
  write(VOLUME_KEY, String(level))
}

// ---- This browser's identity, for the server's Device table ----

/**
 * crypto.randomUUID only exists in secure contexts (HTTPS or localhost). Over plain
 * HTTP on the tailnet it's missing, but getRandomValues isn't, so build a v4 UUID.
 */
function newUuid(): string {
  if (typeof crypto.randomUUID === 'function') return crypto.randomUUID()
  const b = crypto.getRandomValues(new Uint8Array(16))
  b[6] = (b[6] & 0x0f) | 0x40
  b[8] = (b[8] & 0x3f) | 0x80
  const hex = Array.from(b, (x) => x.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}

let deviceIdCache: string | null = null

/** A UUID per browser profile, created on first use. Stable unless site data is cleared. */
export function deviceId(): string {
  if (deviceIdCache) return deviceIdCache
  const stored = read(DEVICE_KEY)
  if (stored && /^[0-9a-f-]{36}$/i.test(stored)) {
    deviceIdCache = stored
  } else {
    deviceIdCache = newUuid()
    write(DEVICE_KEY, deviceIdCache)
  }
  return deviceIdCache
}

/** "Firefox on Windows": what other devices see in "further along on …". */
export function deviceName(): string {
  const ua = navigator.userAgent
  const browser = /Edg\//.test(ua)
    ? 'Edge'
    : /Firefox\//.test(ua)
      ? 'Firefox'
      : /Chrome\//.test(ua)
        ? 'Chrome'
        : /Safari\//.test(ua)
          ? 'Safari'
          : 'Browser'
  // Order matters: Android says Linux, and iOS says Mac OS X.
  const os = /Android/.test(ua)
    ? 'Android'
    : /iPhone|iPad/.test(ua)
      ? 'iOS'
      : /Windows/.test(ua)
        ? 'Windows'
        : /Mac OS X/.test(ua)
          ? 'macOS'
          : /Linux/.test(ua)
            ? 'Linux'
            : null
  return os ? `${browser} on ${os}` : browser
}

// ---- The progress store's offline cache ----

export function loadProgressCache<T>(): Record<string, T> {
  try {
    const raw = read(PROGRESS_KEY)
    const parsed: unknown = raw ? JSON.parse(raw) : null
    return parsed && typeof parsed === 'object' ? (parsed as Record<string, T>) : {}
  } catch {
    return {}
  }
}

export function saveProgressCache<T>(entries: Record<string, T>): void {
  write(PROGRESS_KEY, JSON.stringify(entries))
}

export interface LegacyPosition {
  bookId: string
  position: number
  savedAt: string
}

/** Positions saved before sync existed. Removes them as it reads them. */
export function takeLegacyPositions(): LegacyPosition[] {
  const found: LegacyPosition[] = []
  try {
    const keys: string[] = []
    for (let i = 0; i < localStorage.length; i++) {
      const key = localStorage.key(i)
      if (key?.startsWith(LEGACY_POSITION_PREFIX)) keys.push(key)
    }
    for (const key of keys) {
      try {
        const saved = JSON.parse(localStorage.getItem(key) ?? '') as { position?: unknown; savedAt?: unknown }
        if (typeof saved.position === 'number' && Number.isFinite(saved.position)) {
          found.push({
            bookId: key.slice(LEGACY_POSITION_PREFIX.length),
            position: saved.position,
            savedAt: typeof saved.savedAt === 'string' ? saved.savedAt : new Date(0).toISOString(),
          })
        }
      } catch {
        // A malformed entry is dropped with the rest.
      }
      localStorage.removeItem(key)
    }
  } catch {
    // Storage unavailable: nothing to migrate.
  }
  return found
}
