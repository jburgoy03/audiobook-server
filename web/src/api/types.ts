// Hand-written mirrors of the API's response shapes. Swap for types generated
// from the OpenAPI document once these stop changing.

export interface Library {
  id: string
  name: string
  rootPath: string
  /** Visible to every signed-in user, not just admins. */
  isPublic: boolean
  credit: string | null
  lastScanStartedAt: string | null
  lastScanCompletedAt: string | null
  books: number
}

/** One row of GET /api/books. */
export interface BookSummary {
  id: string
  title: string
  author: string | null
  durationSeconds: number
  hasCover: boolean
  files: number
  chapters: number
}

/** A file's place on the book's single timeline. Addressed by sequence, never by ID. */
export interface BookFile {
  sequence: number
  startOffsetSeconds: number
  durationSeconds: number
  mimeType: string | null
  sizeBytes: number
}

export interface Chapter {
  sequence: number
  title: string
  startOffsetSeconds: number
  endOffsetSeconds: number
}

/** GET /api/books/{id} */
export interface BookDetail {
  id: string
  title: string
  subtitle: string | null
  author: string | null
  narrator: string | null
  description: string | null
  publishedYear: number | null
  durationSeconds: number
  hasCover: boolean
  /** The library's credit line, e.g. "Public domain · LibriVox". */
  credit: string | null
  files: BookFile[]
  chapters: Chapter[]
}

/** GET /api/auth/me */
export interface CurrentUser {
  username: string
  /** Shows admin controls. Convenience only: the server enforces admin on its own. */
  isAdmin: boolean
}

/** What the server holds for one book: GET /api/progress, and inside a ProgressResult. */
export interface ProgressDto {
  bookId: string
  positionSeconds: number
  /** Client time when playback reached this point, not when the server stored it. */
  reportedAt: string
  updatedAt: string
  deviceId: string | null
  deviceName: string | null
  isFinished: boolean
}

/** Body of POST /api/progress. */
export interface ProgressReport {
  bookId: string
  positionSeconds: number
  reportedAt: string
  deviceId: string
  deviceName: string
  isFinished: boolean
  /** "This position is deliberate": bypasses furthest-wins. See ProgressRules on the server. */
  override: boolean
}

export type ProgressReason =
  | 'first'
  | 'override'
  | 'sameDevice'
  | 'finished'
  | 'further'
  | 'behind'
  | 'behindFinished'

/** Response of POST /api/progress. A rejection isn't an error: progress is what the server kept. */
export interface ProgressResult {
  accepted: boolean
  reason: ProgressReason
  progress: ProgressDto
}
