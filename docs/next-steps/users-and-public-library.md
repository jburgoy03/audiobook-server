# Next steps: an admin, other users, and a public-domain library

Goal: Dean's account is the admin. Other people can sign in and listen to
public-domain books (LibriVox) as a test, without seeing the private library.

Status: **design, not started.** Written 2026-10-01, right after auth, sync and
deploy shipped.

## Where things stand

- Auth exists, but it's built for one person. Every signed-in user can do
  everything, including `POST /api/libraries` and scans, and `GET /api/libraries`
  shows server paths. **Nothing below is optional before a second account exists.**
- `User.IsAdmin` exists (the seeded account has it) but nothing reads it.
- Progress, devices and the conflict rule are already per user, so they need no
  changes for more users.
- The real library is public behind the login at `audiobooks.deanburgoyne.dev`.

## 1. Admin

**Put an `admin` claim in the principal** with a custom
`UserClaimsPrincipalFactory<User>` that adds it when `IsAdmin` is true, and define an
`Admin` authorization policy that requires it. Cookies, bearer tokens and refresh
all build their principal through that factory, so one place covers every client.

- Why a claim, not Identity roles: one flag is all this needs. Roles mean
  switching to `IdentityDbContext` with two more tables and a join. Revisit if a
  third kind of user appears.
- Changing `IsAdmin` must also call `UpdateSecurityStampAsync`. Otherwise an
  existing cookie keeps its old claim until the next stamp validation (30 minutes
  by default), and a bearer token until it expires.
- `GET /api/auth/me` returns `isAdmin`, so the web client can show admin controls.
  That's convenience only; the server enforces.

**Admin-only:** `GET`/`POST /api/libraries`, `GET /api/libraries/{id}`, scans, and
everything under a new `/api/admin` group (users, invites).

## 2. Who sees which books

**`Library.IsPublic`** (bool, default false, so existing libraries stay private).

- Admin: every library.
- Everyone else: public libraries only.

**Enforce it in one explicit place**, an extension like
`db.VisibleBooks(principal)`, used by every endpoint that touches a book: the list,
detail, cover, stream (`GET` and `HEAD`), and both progress reads and the progress
write.

- The alternative is an EF global query filter on `Book`. It's harder to forget,
  but it's invisible at the call site, and the scanner (which must see everything)
  would need `IgnoreQueryFilters` everywhere. Explicit is easier to read and to
  review. The integration tests make forgetting it fail loudly.
- **A book the user can't see is a 404, not a 403**, so the response doesn't confirm
  that the book exists. The same goes for posting progress against it.
- `GET /api/progress` filters to visible books. If a library becomes private later,
  rows stay in the table but stop being returned.

**Tests (a matrix):** {admin, user} × {public book, private book} × {list, detail,
cover, stream GET, stream HEAD, progress GET, progress POST}, plus 403 for a
non-admin on every admin endpoint. This is the regression that would leak the
private library, so it gets the most coverage.

Per-user grants (a `LibraryAccess` table: this friend can see that library) are
possible later. Not needed for a public-domain test.

## 3. Getting accounts to people

Options, from most closed to most open:

| | How | Fits | Cost |
|---|---|---|---|
| **A. Admin creates accounts** | `POST /api/admin/users`; Dean hands over a username and temporary passphrase | A handful of friends | Least code. Dean knows their first passphrase; force a change on first sign-in. |
| **B. Invite links** | Admin creates a single-use, expiring code; `/invite/:code` lets the person pick their own name and passphrase | Friends, family, anyone Dean chooses | An `Invites` table (store a hash of the code, `ExpiresAt`, `CreatedBy`, `UsedBy`), one public endpoint, one page |
| **C. Guest "Try it"** | A button on the sign-in page creates a throwaway account (public libraries only) and signs it in | Portfolio visitors, no credentials needed | Bot protection (Cloudflare Turnstile plus a rate limit), a cleanup job for idle guests, an `IsGuest` flag |
| **D. Open registration** | Anyone signs up | Not this project | Abuse handling for strangers' accounts and email verification; no upside over B + C |

**Recommendation: B now, C later.** Invites keep it closed while the public
library is a test, and nobody's passphrase passes through Dean. C is what makes the
portfolio link useful to a stranger, and builds on the same visibility rules.

Also:

- **Disable, don't delete:** set `LockoutEnd` far in the future (Identity's own
  mechanism; the login path already honours it) and update the security stamp,
  which ends the user's sessions at the next validation. Deleting a user cascades
  their progress and devices.
- Login already answers the same 401 for an unknown user and a wrong passphrase, so
  it doesn't reveal which usernames exist. The invite endpoint must not reveal it
  either: "that name is taken" is unavoidable at signup, but only for someone
  holding a valid invite.
- **Lockout abuse grows with more users.** The Cloudflare rate-limiting rule on
  `POST /api/auth/login` (see `auth-sync-deploy.md`) should exist before inviting
  anyone.

## 4. Admin page in the web client

`/admin`, shown in the masthead only for admins:

- **Libraries:** name, root path, public or private toggle, book count, last scan,
  "Scan" and "Force rescan" buttons.
- **Users:** list, create invite (copy the link), disable or enable, reset
  passphrase.

It replaces the browser-console `fetch` used for rescans today.

## 5. Background scanning (prerequisite for scanning from the public site)

A scan runs inside the HTTP request and takes minutes. **Through Cloudflare, a
request that takes longer than 100 seconds gets a 524**, and the endpoint passes
the request's cancellation token to the scan. So the scan would be *cancelled* when
the tunnel gives up, partway through a library. Until this is fixed, **scan over the
Tailscale address** (`http://100.83.139.4:5043`), never the public hostname.

Fix (already on the roadmap): `POST …/scan` enqueues onto a `Channel<T>` and
returns 202 with a job ID. An `IHostedService` runs scans one at a time with the
app's lifetime token, not the request's. `GET /api/admin/scans/{id}` reports
progress, and the admin page polls it.

## 6. The public-domain library

**Source: LibriVox**, volunteer recordings of public-domain books, themselves
dedicated to the public domain. They're hosted on the Internet Archive. Each book
has a zip of mp3s (64 kbps, about 29 MB per hour) and a separate cover image.
"Public domain" here means in the US, which is where the server is.

- **Its own folder and library:** `/mnt/media/librivox`, mounted read-only like the
  main library (one more line in `deploy/docker-compose.yml`), registered as a
  second library with `IsPublic = true`. Keeping it on separate disk paths makes
  visibility a property of the folder, not of each book.
- **Folder cover images first** (web-polish item 3). LibriVox covers are separate
  files, not embedded, so without this every public book gets the cloth cover. It
  also fixes Lord of the Rings, if a `cover.jpg` is added to its folder (the folder
  is read-only only inside the container).
- **Check the tags on a couple of downloads** with `ffprobe -show_format` before
  trusting the scanner's precedence. LibriVox usually tags `album` with the title
  and `artist` with the author, and numbers tracks, but readers and eras vary.
  Some books are solo readings, some are collaborative (a different reader per
  chapter). Narrator is web-polish item 2.
- **Choosing books:** popular titles have several LibriVox versions, and quality
  varies by reader. Pick a well-rated solo reading per title. A good test mix:
  one long novel, one short one, one collection of stories (exercises the
  album-split logic), and one with a long multi-part title (exercises the
  series-prefix heuristic).
- **First visit** (web-polish item 4): a new user has nothing started, so
  "Continue listening" is empty. Feature a suggestion or the newest book.
- **Bandwidth and Cloudflare's terms:** several listeners streaming through the
  tunnel is more than one. At 64 kbps it's small, but it's still media through the
  free plan. Watch it.

## Order of work

1. Admin claim, admin-only endpoints, `isAdmin` on `/me`, tests. Safe to ship on its own.
2. `Library.IsPublic`, `VisibleBooks`, the visibility test matrix.
3. Folder cover images in the scanner.
4. LibriVox on the server: download a few books, mount, register as a public library, scan over Tailscale.
5. Cloudflare rate-limit rule on login.
6. Invites: table, endpoints, `/invite/:code` page, tests.
7. Admin page.
8. Background scanning.
9. Later: guest "Try it" for the portfolio.

## Open questions for Dean

- Invites (B), admin-created accounts (A), or both?
- Will anyone besides Dean ever see the private library? If yes, per-user grants
  come back into scope.
- Should public books carry a visible "Public domain · LibriVox" credit? LibriVox
  asks for none, but it's a nice touch, and honest about where the audio comes from.
