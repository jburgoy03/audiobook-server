# Mobile backend — plan

Planned 2026-10-04. What the API needs before the native clients: Android now
(Kotlin Multiplatform shared core, Compose UI), iPhone later (same shared core,
SwiftUI UI). Built so the iPhone app needs no further backend redesign.

## Why anything changes at all

The web client ships inside the API's image, so the API and its only client have
always deployed together: a breaking change cost nothing. A phone app is installed
once and updated whenever its owner updates it, and a self-hoster's server may be
older or newer than the app. From the first APK onward, **the API is a published
contract.**

## What already works (audit of `28c8478`)

- **Bearer auth.** `AddBearerToken`: access token 1 h, **refresh token 30 days**.
  Refresh re-checks the security stamp, so a passphrase change, reset, disable or
  admin change ends a phone's session at its next refresh (within the hour).
  Identity's refresh tokens are stateless, not one-time-use: two refreshes racing
  from the app both succeed. (To pin with a test.)
- **Policy scheme** picks bearer for any `Authorization: Bearer` request, so stream
  (GET and HEAD, ranges), cover, progress and every admin route already work for an
  app. Integration tests cover bearer on stream, cover and refresh.
- **Devices have names.** `ProgressReport` carries `deviceId` and `deviceName`;
  `ProgressDto` returns `deviceName`, so the jump offer can say "Pixel 8 is ahead".
- **Offline progress.** `ReportedAt` vs `UpdatedAt`, same-device-newer-wins and the
  future-timestamp clamp already make a replayed queue safe.
- **Downloads by `(bookId, sequence)`** with `SizeBytes`, `StartOffsetSeconds` and
  chapters in `GET /api/books/{id}`: enough to cache the timeline offline.
- `mustChangePassword` on `/me`; everything else 403 until it's changed.

## Gaps found

1. **The contract is anonymous types.** `/api/books` and `/api/books/{id}` return
   `new { … }`, so the OpenAPI document (which exists, but only in Development) has
   no named, reusable schemas. Fine for a client in the same repo; weak for one
   written against a document.
2. **Two error shapes.** 14 places return `{ error = "…" }`; login returns
   ProblemDetails. An app wants one shape.
3. **No server probe.** Nothing anonymous answers "is this an AudiobookServer, and
   which API version?", which the app needs when a user types a URL (and every
   self-hosted install is a different URL).
4. **No content version per book.** A rescan replaces files wholesale; a download
   made before it can silently stop matching the server's offsets.
5. **Covers are full size.** "Largest image wins" means a phone grid can pull
   megabytes over cellular.
6. **iPhone streaming.** AVPlayer has no documented way to add request headers
   (there is an undocumented `AVURLAsset` option and a resource-loader workaround).
   Casting has the same problem. Not needed for Android.

## Milestones

### B1 — The contract (before any app code) — done 2026-10-04

Rules now in `docs/api-contract.md`.

- Named records for every response the apps use (`BookSummaryDto`, `BookDetailDto`,
  `BookFileDto`, `ChapterDto`), replacing the anonymous types. Same JSON, so the web
  client is unaffected; a test serialises one and compares field names to catch
  accidental renames.
- One error shape: `TypedResults.Problem` / `ValidationProblem` everywhere,
  `AddProblemDetails()` for unhandled errors. Web client updated to read `detail`.
- OpenAPI generated at build time and committed as `docs/openapi.json` (see
  Decisions). The Kotlin DTOs are written against it; CI fails if a field disappears.
- `GET /api/server-info` (anonymous): `{ product: "AudiobookServer", version,
  apiVersion: 1 }`. Cost: reveals the version, as Jellyfin's does.
- **Compatibility rule**, written into the README: additive changes only (new
  fields, new routes; clients ignore unknown fields). A breaking change gets a new
  route and bumps `apiVersion`; the app compares and says "update the server" or
  "update the app" instead of failing to parse.

### B2 — Downloads — done 2026-10-04

Built as planned except where it's computed: in `Core/Scanning/ContentVersion.cs`
from sequence, path, size, mtime and duration (ms). Existing books get theirs on
their next scan, from stored files (no re-probe); `null` until then means unknown.

- `contentVersion` on the book summary and detail: a hash of each file's sequence,
  size, mtime and duration, computed at scan time and stored on `Book`
  (scanner-owned, like `Title`). The app compares it with its download and offers a
  re-download on mismatch. Deterministic; no heuristic.
- Position after a content change: the server already clamps to the new duration;
  the app keeps the timeline number. If a re-encode moved chapter boundaries, the
  position is approximately right, not exactly (flag it, don't solve it).

### B3 — Cover thumbnails — done 2026-10-04

Built as planned, made on first request rather than at scan time (no rescan needed,
scans stay fast), at most two ffmpeg processes at once, cleared when a book's cover is
replaced. Smaller originals are served as they are; a failed thumbnail falls back to
the original.

- `GET /api/books/{id}/cover?size=` with a small fixed set (e.g. 200, 400, 800) so
  the cache can't be filled with arbitrary sizes. Resized once with the ffmpeg
  already in the image, cached beside the original, ETag includes the size.
- Why ffmpeg: no new dependency (ImageSharp's licence would need checking against
  the self-hosting licence; SkiaSharp is MIT but heavy).

### B4 — Designed for, built later

- **Signed stream URLs** for the iPhone and casting:
  `…/stream?exp=…&sig=…`, an HMAC over user, book, sequence and expiry, keyed from
  the data-protection keys, minted by `GET /api/books/{id}/stream-urls`. The book
  and file addressing doesn't change, so adding it later breaks nothing. Cost: a URL
  is a bearer credential until it expires, so short expiries (e.g. the book's length
  plus margin) and they mustn't be logged.
- ETag / `If-None-Match` on `/api/books` and `/api/progress` (cheap refresh on big
  libraries); a batch progress endpoint; pagination. None needed at today's size.

### Tests

Server-info anonymous; every DTO's field names pinned; ProblemDetails on each error
path; `contentVersion` stable across a no-op rescan and changed when a file
changes; thumbnail sizes and ETags; two concurrent refreshes both succeed.

## The Kotlin Multiplatform split

Native UI and playback on each platform; everything with rules in it shared, so the
sync behaviour is written once.

| Shared (`commonMain`) | Android | iPhone (later) |
|---|---|---|
| Ktor client, DTOs (kotlinx.serialization), server-info check | Compose UI | SwiftUI UI |
| Token store interface + refresh (Ktor `Auth` plugin) | Keystore-backed store | Keychain-backed store |
| Progress store, offline queue, jump-offer rule | Media3 `MediaSessionService`, bearer header in the `DataSource.Factory` | AVPlayer (signed URLs, B4) |
| Timeline maths (book offset ↔ file + offset) | | |
| Download bookkeeping, `contentVersion` check | WorkManager transfers | background `URLSession` |
| Local database: Room (KMP since 2.7) or SQLDelight | | |

KMP rules out Retrofit (JVM only), hence Ktor. Room vs SQLDelight is the main open
library choice: Room is familiar from Android; SQLDelight's iOS story is older and
better proven. Decide at the start of the Android work.

## Order

B1 → B2 → (Android A1–A4: sign-in, library, playback, sync) → B3 when the library
grid exists → Android A5 downloads → … B4 when the iPhone app starts.

## Decisions (2026-10-04)

- **Thumbnails:** `?size=320|640|1080`, longest side, never upscaled. 320 for list
  rows and the notification, 640 for the grid, 1080 for the book page, now-playing
  and lock screen. No `size` returns the original.
- **Refresh lifetime:** stays 30 days.
- **OpenAPI:** generated at build time (`Microsoft.Extensions.ApiDescription.Server`)
  and committed as `docs/openapi.json`; CI fails on a removed field. Serving stays
  Development-only (one line to expose later). This replaces "served in every
  environment" in B1.
- **Local database:** Room (KMP). Classes-first like EF, the Android default; its
  iOS support is newer than SQLDelight's, a risk deferred to the iPhone app.
- **SDK levels:** `minSdk 33` (Android 13), `targetSdk`/`compileSdk 37` (Android 17).
- **Test hardware:** emulator for most work; a physical phone later for background
  playback, lock screen and Bluetooth controls.

## Android progress

- **A1 — skeleton and sign-in: done 2026-10-04.** KMP project in `mobile/`
  (`sharedLogic` shared with iOS, `sharedUI` Android-only Compose, `iosApp` SwiftUI
  shell). Server probe, bearer sign-in with single-flight refresh, Keystore-encrypted
  session, forced passphrase change, debug-only cleartext to `10.0.2.2`.
- **Found on the way: Android 17 local network protection.** Targeting API 37, an app
  needs the runtime `ACCESS_LOCAL_NETWORK` permission to reach private addresses (home
  LAN, Tailscale 100.x, the emulator's 10.0.2.2); without it connections time out.
  The app asks only when the server address looks local. Gap: a bare hostname that
  resolves to a LAN address isn't detected; offer the permission on a probe timeout.
- **A2 — library: done 2026-10-04.** Continue listening, all books grid, book page with
  chapters. Covers via Coil through the API's own Ktor client (auth interceptor; the
  token only goes to the signed-in server).
- **A3 — playback (3a, 3b): done 2026-10-04.** Shared timeline maths (mirrors
  web/src/player/timeline.ts). Media3 `MediaSessionService`: background, notification,
  lock screen, audio focus, pause on headphones out; per-request bearer header; a 401
  mid-book refreshes and resumes.
- **A3c + A6 — player screen: done 2026-10-04.** Chapter-scoped slider (book-wide is
  minutes per pixel), chapter skip with a 3 s "restart first" grace, ±30 s, speed
  0.8–2×, sleep timer (minutes, or end of chapter, parking on the boundary). Icons
  drawn as vectors in code. The library follows accepted reports without a refresh.
- **A4 — progress sync: done 2026-10-04.** `ProgressSync` (shared) mirrors the web's
  rules: reconcile on play, jump offer when another device is >30 s ahead, overrides only
  once reconciled with no offer open; saves every 30 s, on pause, seek and at the end.
  Reports come from the playback service (alive while audio plays), queued offline
  (one per book, SharedPreferences; Room waits for downloads). Verified both ways
  against the web client.
- **A5 — downloads: done 2026-10-04.** Room (KMP, 2.8.5, KSP 2.3.12) stores each book's
  details as sent plus per-file completion; files in app-private storage. A WorkManager
  foreground job per book, resuming with HTTP Range through the authorised client;
  Wi-Fi only by default. Playback uses local files when complete and not stale
  (`contentVersion`), streams otherwise; offline, a stale copy plays too. With no
  server the app opens the library of downloads (the session check no longer blocks
  it). Positions are cached on the device, and an unsent report beats an older server
  value, so resume works offline.
- **Design: done 2026-10-04.** The app follows web/src/index.css token for token
  ("black, bone and blood": red only means a position in a book), EB Garamond
  bundled as static TTFs under the OFL (licence ships in composeResources/files),
  and the web's icon paths. Phone-specific: a pinned masthead, and the player's
  draggable bar covers the current chapter (the whole book sits under it). Blurb
  emphasis (Open Library Markdown) renders as italic in both clients.
- **Lesson:** `async` directly in `viewModelScope` crashes the app on failure whatever
  `await` is wrapped in; parallel calls go inside `coroutineScope { }`.
- **Still to do:** ask for the notification permission (download progress is hidden
  without it); storage use in settings; clear Coil's cover cache on sign-out.
- **Open for the release build:** plain HTTP is refused outside debug. Self-hosters on
  Tailscale over HTTP would need it allowed (Jellyfin's app does). Decide then.
