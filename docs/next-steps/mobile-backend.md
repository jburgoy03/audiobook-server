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

### B3 — Cover thumbnails

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
