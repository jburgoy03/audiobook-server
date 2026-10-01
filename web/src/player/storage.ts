// Playback state kept in the browser until server-side sync (2f) exists.
// Every access is wrapped: storage can be unavailable (private windows, blocked
// site data) and the player must still work without it.

export interface SavedPosition {
  position: number
  savedAt: string
}

const positionKey = (bookId: string) => `audiobook:position:${bookId}`
const RATE_KEY = 'audiobook:rate'

export function loadPosition(bookId: string): number | null {
  try {
    const raw = localStorage.getItem(positionKey(bookId))
    if (!raw) return null
    const saved = JSON.parse(raw) as SavedPosition
    return Number.isFinite(saved.position) ? saved.position : null
  } catch {
    return null
  }
}

/** Position plus when it was saved, for ordering "continue listening". */
export function loadSavedPosition(bookId: string): SavedPosition | null {
  try {
    const raw = localStorage.getItem(positionKey(bookId))
    if (!raw) return null
    const saved = JSON.parse(raw) as SavedPosition
    return Number.isFinite(saved.position) ? saved : null
  } catch {
    return null
  }
}

export function savePosition(bookId: string, position: number): void {
  try {
    const value: SavedPosition = { position, savedAt: new Date().toISOString() }
    localStorage.setItem(positionKey(bookId), JSON.stringify(value))
  } catch {
    // Best effort.
  }
}

export function loadRate(): number {
  try {
    const rate = Number(localStorage.getItem(RATE_KEY))
    return rate >= 0.5 && rate <= 3 ? rate : 1
  } catch {
    return 1
  }
}

export function saveRate(rate: number): void {
  try {
    localStorage.setItem(RATE_KEY, String(rate))
  } catch {
    // Best effort.
  }
}
