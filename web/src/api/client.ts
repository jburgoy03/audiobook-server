import type {
  AdminUser,
  BookDetail,
  BookSummary,
  CurrentUser,
  Library,
  NewLibrary,
  ProgressDto,
  ProgressReport,
  ProgressResult,
  ScanReport,
  TemporaryPassword,
} from './types'

/** The server answered 401. The auth store has already been told. */
export class UnauthorizedError extends Error {
  constructor(path: string) {
    super(`${path}: not signed in`)
    this.name = 'UnauthorizedError'
  }
}

/** Any other non-2xx answer. */
export class HttpError extends Error {
  readonly status: number
  readonly detail: string | null

  constructor(method: string, path: string, status: number, statusText: string, detail: string | null) {
    super(`${method} ${path} failed: ${status} ${statusText}`)
    this.name = 'HttpError'
    this.status = status
    this.detail = detail
  }
}

// Set by the auth store. A 401 anywhere means the session is gone (expired, or
// signed out in another tab), and the whole app has to react: stop playback and
// show the login page. Kept as a callback so this module doesn't import the store.
let onUnauthorized: () => void = () => {}

export function setUnauthorizedHandler(handler: () => void) {
  onUnauthorized = handler
}

interface RequestOptions {
  method?: string
  body?: unknown
  signal?: AbortSignal
  /** Survives the page closing (pagehide saves). Bodies must stay under 64 KB. */
  keepalive?: boolean
  /**
   * Whether a 401 signs the app out. Off for login (a 401 there is a wrong
   * password) and for the startup session check (a 401 there is just "not yet").
   */
  notifyUnauthorized?: boolean
}

async function request(path: string, options: RequestOptions = {}): Promise<Response> {
  const method = options.method ?? 'GET'
  // The auth cookie is HttpOnly and same-origin, so fetch sends it by default;
  // nothing here ever touches a token.
  const response = await fetch(path, {
    method,
    signal: options.signal,
    keepalive: options.keepalive,
    headers: {
      Accept: 'application/json',
      ...(options.body !== undefined ? { 'Content-Type': 'application/json' } : {}),
    },
    body: options.body !== undefined ? JSON.stringify(options.body) : undefined,
  })

  if (response.status === 401) {
    if (options.notifyUnauthorized ?? true) onUnauthorized()
    throw new UnauthorizedError(path)
  }
  if (!response.ok) {
    let detail: string | null = null
    try {
      const problem = (await response.json()) as { detail?: string; error?: string }
      detail = problem.detail ?? problem.error ?? null
    } catch {
      // No JSON body.
    }
    throw new HttpError(method, path, response.status, response.statusText, detail)
  }
  return response
}

async function getJson<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await request(path, { signal })
  return (await response.json()) as T
}

async function postJson<T>(path: string, body?: unknown, method = 'POST'): Promise<T> {
  const response = await request(path, { method, body })
  return (await response.json()) as T
}

/** For endpoints that answer 204. */
async function post(path: string, body?: unknown): Promise<void> {
  await request(path, { method: 'POST', body })
}

export const api = {
  libraries: (signal?: AbortSignal) => getJson<Library[]>('/api/libraries', signal),
  books: (signal?: AbortSignal) => getJson<BookSummary[]>('/api/books', signal),
  book: (id: string, signal?: AbortSignal) => getJson<BookDetail>(`/api/books/${id}`, signal),

  /** Only meaningful when the book's hasCover is true. Authenticated by the cookie. */
  coverUrl: (bookId: string) => `/api/books/${bookId}/cover`,

  /**
   * A URL, not a fetch: the <audio> element requests it, with its own Range
   * headers. It can't add an Authorization header, which is why the web client
   * authenticates with a cookie.
   */
  streamUrl: (bookId: string, sequence: number) => `/api/books/${bookId}/files/${sequence}/stream`,

  // ---- Auth ----

  async login(username: string, password: string): Promise<void> {
    await request('/api/auth/login?useCookies=true', {
      method: 'POST',
      body: { username, password },
      notifyUnauthorized: false,
    })
  },

  async logout(): Promise<void> {
    await request('/api/auth/logout', { method: 'POST', notifyUnauthorized: false })
  },

  /** 200 when signed in. Pass notify=false to ask without signing the app out on 401. */
  async me(notify = true): Promise<CurrentUser> {
    const response = await request('/api/auth/me', { notifyUnauthorized: notify })
    return (await response.json()) as CurrentUser
  },

  async changePassword(currentPassword: string, newPassword: string): Promise<void> {
    await post('/api/auth/change-password', { currentPassword, newPassword })
  },

  // ---- Admin: users ----
  // A 403 from these surfaces as an HttpError for the page to show; only a
  // 401 signs the app out.

  adminUsers: (signal?: AbortSignal) => getJson<AdminUser[]>('/api/admin/users', signal),
  createUser: (username: string) => postJson<TemporaryPassword>('/api/admin/users', { username }),
  resetPassword: (id: string) => postJson<TemporaryPassword>(`/api/admin/users/${id}/reset-password`),
  disableUser: (id: string) => post(`/api/admin/users/${id}/disable`),
  enableUser: (id: string) => post(`/api/admin/users/${id}/enable`),

  // ---- Admin: libraries ----

  createLibrary: (library: NewLibrary) => postJson<Library>('/api/libraries', library),
  updateLibrary: (id: string, changes: { isPublic?: boolean; credit?: string }) =>
    postJson<Library>(`/api/libraries/${id}`, changes, 'PATCH'),
  scanLibrary: (id: string, force: boolean) =>
    postJson<ScanReport>(`/api/libraries/${id}/scan${force ? '?force=true' : ''}`),

  // ---- Progress ----

  progress: (signal?: AbortSignal) => getJson<ProgressDto[]>('/api/progress', signal),

  async reportProgress(report: ProgressReport, keepalive = false): Promise<ProgressResult> {
    const response = await request('/api/progress', { method: 'POST', body: report, keepalive })
    return (await response.json()) as ProgressResult
  },
}
