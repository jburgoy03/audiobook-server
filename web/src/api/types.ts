// Hand-written mirrors of the API's response shapes. Swap for types generated
// from the OpenAPI document once these stop changing.

export interface Library {
  id: string
  name: string
  rootPath: string
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
  files: BookFile[]
  chapters: Chapter[]
}
