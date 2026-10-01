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

Note: the project doc lists Testcontainers integration tests as a prerequisite for
2e. The decision to skip tests for now was deliberate; revisit it here, since auth is
where regressions are least visible.

## B. Position sync (Phase 2f)

The schema is already in place: `PlaybackPosition` with a unique `(UserId, BookId)`,
`PositionSeconds`, `ReportedAt` (client time) separate from `UpdatedAt` (server
time), `DeviceId`, and `IsFinished`.

- `POST /api/progress` with `{ bookId, positionSeconds, reportedAt, deviceId, isFinished }`.
- `GET /api/progress` for all of the user's books (feeds "Continue listening"), and
  `GET /api/books/{id}/progress`.
- Conflict rule from the project doc: furthest position wins, unless the older report
  is well ahead, in which case flag it rather than silently overwrite. "Finished" is
  its own state, not just "position at the end". Client clock skew is a known gap.
- Validate on the server: clamp positions to the book's duration. `BookTimeline`
  (Core/Playback) is still waiting for an endpoint and can back this.
- Web client: replace `web/src/player/storage.ts` with API calls, keep browser
  storage as an offline fallback, and on first login upload any positions already
  saved locally. Keep the current save points (pause, file change, every 30s, page
  hide); use `fetch(..., { keepalive: true })` or `sendBeacon` for the page-hide save.
- The library page's "started" and "finished" thresholds (`LibraryPage.tsx`) should
  move to the server's notion of finished.

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
