# Auth, position sync and deployment

Status as of 2026-10-01: **done**, and deployed. This file records what was built and
why, and the loose ends. The server runbook is in [`docs/deploy.md`](../deploy.md);
what comes next is in [`users-and-public-library.md`](users-and-public-library.md).

## A. Auth (Phase 2e): done

**ASP.NET Core Identity, one user, two kinds of client.**

- **The web client uses a cookie** (`audiobook.auth`: HttpOnly, SameSite=Strict,
  Secure whenever the request arrived over HTTPS). This answers the media problem:
  `<audio src>` and `<img src>` can't send an `Authorization` header, but they do
  send cookies, and the client is always same-origin (Vite's proxy in development,
  served by the API in production).
- **Other clients (Android) use bearer tokens.** Media3 can attach headers.
- **A policy scheme picks per request**: an `Authorization: Bearer` header means
  bearer, anything else means the cookie (`Api/Auth/AuthSetup.cs`).
- **Tokens are Identity's own (`AddBearerToken`), not JWTs.** They're opaque,
  encrypted with the same data-protection keys as the cookie, and refreshable with a
  security-stamp check, so a password change ends every session. This departs from
  the original "JWT plus refresh tokens" plan. It costs third-party verifiability,
  which nothing here needs, and saves hand-rolling refresh-token storage and
  rotation.
- **Data-protection keys are persisted** (`data/keys` locally, `/data/keys` in the
  container). In memory they'd change on every restart and sign everyone out.
- **Secure by default.** A fallback policy requires a signed-in user on every
  endpoint. Only login, refresh, logout, the OpenAPI document (Development) and the
  web client's static files opt out. Unknown `/api/*` routes are 404, never the
  SPA's `index.html`.
- **No registration.** `UserSeeder` creates the account at startup from
  `Auth:SeedUser:Username` / `Auth:SeedUser:Password`, only when no user exists.
  (Since 2026-10-01 that account is the admin, and other users are on the way: see
  `users-and-public-library.md`.)
- **Passwords:** 12 characters minimum, no character-class rules. **Lockout:** 5
  failures lock the account for 5 minutes (login answers 429).
- `User` is now an `IdentityUser<Guid>` on an `IdentityUserContext` (no role
  tables). Identity's tables are named `UserClaims`, `UserLogins`, `UserTokens`.
  Migration `AddIdentity` renamed `Username` to `UserName`.

Endpoints: `POST /api/auth/login?useCookies=true|false`, `POST /api/auth/refresh`,
`POST /api/auth/logout`, `GET /api/auth/me`.

**Web client.** `web/src/auth/auth.ts` holds the sign-in state (checked with `/me`
at startup, because the cookie is HttpOnly). `App` renders `PlayerProvider` and the
now-playing bar only while signed in. Signing out, or any 401, therefore unmounts
the player, which pauses the audio, saves the position and removes the bar. The
final save is flushed before the logout request, while the cookie is still valid.
An `<audio>` error triggers a `/me` check, since a media element can't report a 401.

## Testing: decided, done

The deferred Testcontainers suite now exists (`tests/.../Integration`):
`WebApplicationFactory<Program>` against a real `postgres:17-alpine` container, one
container per run.

- **Auth:** every route answers 401 (not a redirect) when anonymous, including
  `HEAD` on the stream. Wrong password, cookie flags, cookie and bearer both reaching
  the stream and cover, refresh (including forged and access-token-as-refresh),
  logout, lockout.
- **Progress:** storage, rejection response, overrides and same-device rewinds,
  clamping, future timestamps, finished state, 404 for unknown books, concurrent
  first reports.
- **ProgressRulesTests:** the conflict rule as a pure function.

89 passed, 2 skipped (the sample-file probe tests). Integration tests need Docker;
skip them with `dotnet test --filter Category!=Integration`.

## B. Position sync (Phase 2f): done

`GET /api/progress` (all books, most recent first), `GET /api/books/{id}/progress`
(204 when none), `POST /api/progress`.

**The conflict rule** (`Core/Progress/ProgressRules.cs`, pure and unit-tested):

1. Nothing stored yet: accept.
2. `override`: accept. The web client sends it once the playing book has reconciled
   with the server (see below). The device that is playing is authoritative.
3. A newer report from the device that wrote the stored row: accept. This is what
   makes rewinding possible.
4. `isFinished`: accept. Finished ranks past every position.
5. Stored row is finished: reject.
6. Otherwise **furthest wins** (ties accepted).

A rejection isn't an error: the response carries `accepted`, `reason` and what the
server kept, so a client can tell "saved" from "another device is ahead".

Server-side details: positions clamped to the book's duration; `reportedAt` in the
future (beyond one minute) replaced with server time; the row is locked
(`SELECT … FOR UPDATE`) inside a transaction, so concurrent reports are decided one
at a time; a unique-key race on first insert is retried. Devices register on first
report; a device ID belonging to another user is ignored, not trusted.

**Known gaps, by design for now:**

- An old report that is far ahead (a stale phone position arriving after a
  deliberate rewind elsewhere) is accepted. The project doc's "flag it" idea needs a
  column; instead, the next device to start the book sees the jump offer and can
  decline.
- `reportedAt` is client time. Skew only affects the same-device rule (one device,
  one clock), and future timestamps are clamped.

**Web client** (`web/src/player/progress.ts`): a progress store using the same
`useSyncExternalStore` pattern as `nowPlaying.ts`.

- Reads are synchronous, from memory, filled from the browser cache at load and from
  `GET /api/progress` after sign-in and whenever the tab becomes visible.
- Each book keeps **`local`** (where this browser was; a book resumes here) and
  **`server`** (what the server holds, possibly from another device).
- Writes go to memory and the cache first, then the server. Unconfirmed writes stay
  `pending` and retry on the next refresh, so offline listening loses nothing.
- **The playing book is never moved by a refresh.** When a book starts, or play is
  pressed again, and another device is more than 30s ahead, the player offers
  "Further along on Pixel 8, at 4:12:05" with Jump there / Stay here
  (`JumpPrompt.tsx`, on the book page and in the now-playing bar). Until answered,
  reports are ordinary (furthest-wins), so this device can't overwrite the other.
  Either answer makes later reports overrides.
- Saves that would say nothing new (same position, same finished state) are skipped,
  so a paused tab doesn't keep re-asserting an old position on every focus change.
- **Device ID:** a UUID per browser profile (with a `getRandomValues` fallback,
  because `crypto.randomUUID` needs a secure context and the tailnet is plain HTTP).
  Device name from the user agent ("Firefox on Windows").
- **Finished** comes from the server's `IsFinished`, set when the last file ends.
  The old `FINISHED_MARGIN_SECONDS` heuristic is gone. "Started" (60s) stays a
  client display rule.
- **Pre-sync positions** (`audiobook:position:*`) are uploaded on first sign-in as
  ordinary reports, then removed.
- Speed stays per browser.

## C. Deploy (Phase 4): done, Tailscale and tunnel

See [`docs/deploy.md`](../deploy.md). In short: one image (API plus the built web
client, with ffmpeg), compose stack in `/opt/docker/audiobook`, library mounted
read-only, covers and keys on a volume, published on the Tailscale IP only, and
public through the existing Cloudflare Tunnel.

Done on the server on 2026-10-01: migrations applied (`AddDurationProvenance`,
`NormalizeRelativePaths`, `AddIdentity`), forced rescan: 0 added, 10 updated,
0 removed, **92 file durations corrected**, all of them The Name of the Wind. Every
other mp3 in the library (Dune, Lord of the Rings, The Wise Man's Fear) was within
0.1s per file, which confirms the drift belongs to one encoder.

Also done: `RelativePath` separators are normalised to `/` on every OS
(`LibraryPaths`), with a data migration for libraries rooted at a Windows drive.

## Loose ends

- [x] **Public hostname** (`audiobooks.deanburgoyne.dev` → `http://audiobook-api:8080`) loads the sign-in page over HTTPS (2026-10-01).
- [x] **Cookie flags on the public hostname:** HttpOnly, Secure, SameSite=Strict (checked in DevTools, 2026-10-01).
- [ ] **Cloudflare rate-limiting rule on `POST /api/auth/login`.** Lockout stops guessing, but anyone who knows the username can keep the account locked. A rate limit stops that at Cloudflare's edge. **Still open, and now a blocker:** it must exist before a second account does (see `admin-accounts.md`).
- [ ] **Cloudflare terms:** serving large media through the free plan is restricted. Fine at one listener; revisit before inviting others (see the users doc).
- [ ] **CI/CD:** the image is built on the server by hand. GitHub Actions → GHCR → deploy over Tailscale is still to do.
- [ ] Lord of the Rings has no cover anywhere. The scanner now reads folder images, so a `cover.jpg` in its folder (then a rescan) fixes it.
- [ ] Data-protection keys are stored unencrypted on the volume (startup warning). Acceptable on a single server: anyone who can read the volume owns the box.
