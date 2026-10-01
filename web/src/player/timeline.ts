import type { BookFile, Chapter } from '../api/types'

// Client-side mirror of the server's BookTimeline. A book is one continuous
// timeline; each file owns the half-open interval [start, start + duration).
// Unlike the server, which returns null out of range, the player clamps:
// a seek past the end should land at the end, not do nothing.

export interface FileLocation {
  /** Index into book.files (not the file's sequence, though today they match). */
  index: number
  /** Seconds into that file. */
  offset: number
}

export function totalDuration(files: BookFile[]): number {
  const last = files[files.length - 1]
  return last ? last.startOffsetSeconds + last.durationSeconds : 0
}

export function clampPosition(files: BookFile[], position: number): number {
  return Math.min(Math.max(position, 0), totalDuration(files))
}

export function locate(files: BookFile[], position: number): FileLocation {
  const t = clampPosition(files, position)
  for (let i = files.length - 1; i >= 0; i--) {
    const f = files[i]
    if (t >= f.startOffsetSeconds) {
      return { index: i, offset: Math.min(t - f.startOffsetSeconds, f.durationSeconds) }
    }
  }
  return { index: 0, offset: 0 }
}

/** Index of the chapter containing the position, or -1 if there are no chapters. */
export function chapterIndexAt(chapters: Chapter[], position: number): number {
  for (let i = chapters.length - 1; i >= 0; i--) {
    if (position >= chapters[i].startOffsetSeconds) return i
  }
  return chapters.length > 0 ? 0 : -1
}

export function formatTime(seconds: number): string {
  const s = Math.max(0, Math.floor(seconds))
  const h = Math.floor(s / 3600)
  const m = Math.floor((s % 3600) / 60)
  const sec = s % 60
  const mm = String(m).padStart(h > 0 ? 2 : 1, '0')
  const ss = String(sec).padStart(2, '0')
  return h > 0 ? `${h}:${mm}:${ss}` : `${mm}:${ss}`
}
