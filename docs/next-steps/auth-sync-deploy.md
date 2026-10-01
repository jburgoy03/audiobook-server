# Next steps: auth, position sync, and deployment

The larger pieces that turn the local prototype into the deployed flagship.
Recommended order: auth (2e) → position sync (2f) → deploy (Phase 4). Nothing should
be exposed publicly before auth exists. A Tailscale-only deploy could come earlier.

## A. Auth (Phase 2e)

Plan from the project doc: ASP.NET Core Identity, JWT access plus refresh tokens.
`User` and `Device` entities already exist.

**Open design problem: media requests can't carry a bearer token.** The browser's
`<audio src>` and `<img src>` (stream and cover endpoints) send no `Authorization`
header. Options:

- **Cookie auth for the web client** (HttpOnly, Secure, SameSite=Strict). Works
  because the web client is same-origin: through the Vite proxy in development, and
  served from the same host in production.
- **Bearer tokens for Android.** Media3 can attach headers through its DataSource
  factory, so the Android client doesn't need cookies.
- Accept both with a policy scheme that picks cookie or bearer per request.
- Alternative: short-lived signed stream URLs (token in the query string). More
  moving parts; only worth it if a client can do neither cookies nor headers.

Scope for a first pass:

- Single user. Registration disabled; seed the first account from configuration or
  a one-off command.
- Endpoints: login, refresh, logout, current user.
- Protect every `/api` route, including stream and cover.
- Web: login page, an auth context, and a redirect to login on 401.
- Playback outlives pages now (`PlayerProvider` sits above the routes), so logout,
  or a 401 on a stream request, must also stop the active book. Otherwise audio
  keeps playing, and the now-playing bar keeps showing, for a signed-out user.

Note: the project doc lists Testcontainers integration tests as a prerequisite for
2e. The decision to skip tests for now was deliberate; revisit it here, since auth is
where regressions are least visible.

## B. Position sync (Phase 2f)

### Server

The schema is already in place: `PlaybackPosition` with a unique `(UserId, BookId)`,
`PositionSeconds`, `ReportedAt` (client time) separate from `UpdatedAt` (server
time), `DeviceId`, and `IsFinished`.

- `POST /api/progress` with `{ bookId, positionSeconds, reportedAt, deviceId, isFinished }`.
- `GET /api/progress` for all of the user's books (feeds "Continue listening" and the
  featured book), and `GET /api/books/{id}/progress`.
- Conflict rule from the project doc: furthest position wins, unless the older report
  is well ahead, in which case flag it rather than silently overwrite. "Finished" is
  its own state, not just "position at the end". Client clock skew is a known gap.
- Validate on the server: clamp positions to the book's duration. `BookTimeline`
  (Core/Playback) is still waiting for an endpoint and can back this.
- Speed (`rate`) is one setting for all books today. It can stay per-browser, or
  become a per-user preference; it doesn't need the conflict machinery.

### Web client: where positions are read and written today

As of the 2026-10-01 web polish, playback is app-level: `PlayerProvider` (above
the routes) runs one active book at a time through `useBookPlayer`, and publishes
it to a small store (`player/nowPlaying.ts`). The library's Resume button plays in
place, and playback continues across pages; a now-playing bar shows it elsewhere.

Every position read and write goes through `web/src/player/storage.ts`, which is
the seam to replace. Callers:

| Where | What it does | Sync concern |
|---|---|---|
| `useBookPlayer` `save()` | Writes on pause, at each file boundary, every 30s while playing, on `pagehide`/`visibilitychange`, and when the book stops being active (another book activated) | Becomes `POST /api/progress`. The page-hide save needs `fetch(..., { keepalive: true })` or `sendBeacon`. Navigating between pages no longer saves, because playback no longer stops. |
| `resumePosition()` (`useBookPlayer.ts`) | Reads the start point when a book is activated, as **synchronous** initial state | Can't await the server. Read from a client-side progress cache filled from `GET /api/progress` (see below). |
| `useIdlePlayer` (`Player.tsx`) | Shows the saved position for a book page whose book isn't active | Same cache. |
| `progressFor()` (`LibraryPage.tsx`) | Picks "Continue listening" books and the featured one, ordered by `savedAt` | Order by server `ReportedAt`; use `IsFinished` instead of the local thresholds. |
| `FINISHED_MARGIN_SECONDS` (twice) and `STARTED_AFTER_SECONDS` | Local notions of "finished" and "started" | Move "finished" to the server. "Started" can stay a client display rule. |

Suggested shape:

- A progress store next to `nowPlaying.ts` (same `useSyncExternalStore` pattern):
  loaded from `GET /api/progress` at startup and when the tab becomes visible again,
  and written through to the server. Browser storage stays as its offline cache, so
  every read above stays synchronous.
- **The active book on this device is authoritative while it's playing.** A refresh
  from the server must not move the live player. Apply incoming positions only to
  inactive books, and check the active book's server position only when it's
  activated (e.g. "you're further ahead on your phone: jump to 14:32?" rather than
  silently jumping).
- A per-browser device ID (a UUID in browser storage) for `deviceId`; the `Device`
  entity already exists.
- On first login, upload any positions already saved locally (they predate sync).

## C. Deploy (Phase 4)

What the server needs once code is there:

- Apply migrations, including `AddDurationProvenance`.
- **Forced rescan** (`POST /api/libraries/{id}/scan?force=true`): a normal scan skips
  unchanged files, so it would keep the old drifted mp3 durations and extract no
  covers.
- `Covers:Directory` pointed at a persistent volume.
- Normalize `RelativePath` separators to `/` before any Windows-built data reaches
  the server database (cleanup backlog item).

Packaging:

- The `aspnet` base image has no ffmpeg; the Dockerfile must install it (the scanner
  uses ffprobe and ffmpeg).
- Serve the built web client from the API (`UseStaticFiles` + `MapFallbackToFile`) so
  production stays same-origin: no CORS, and cookie auth just works. nginx in front
  is the alternative.
- Compose stack under `/opt/docker/audiobook`, library mounted read-only
  (`/mnt/media/audiobooks:/library:ro`), reusing the existing Postgres container.
- GitHub Actions builds and pushes to GHCR, then deploys over Tailscale with an
  ephemeral auth key.
- Public exposure through the existing Cloudflare Tunnel only after auth. The public
  demo uses LibriVox recordings; the real library stays Tailscale-only.
