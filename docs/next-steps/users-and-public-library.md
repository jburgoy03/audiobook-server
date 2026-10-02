# Next steps: an admin, other users, and a public-domain library

Goal: Dean's account is the admin. Other people can sign in and listen to
public-domain books (LibriVox) as a test, without seeing the private library.

Status: **sections 1, 2 and 6 done and deployed (2026-10-01).** Accounts (section 3)
are next, starting with admin-created accounts: the build plan is
[`admin-accounts.md`](admin-accounts.md). Written 2026-10-01, right after auth, sync
and deploy shipped; updated the same evening.

## Where things stand

- **Admin:** done. The `admin` claim comes from `IsAdmin` through
  `AudiobookClaimsPrincipalFactory`; the `Admin` policy guards every library
  endpoint; `/api/auth/me` returns `isAdmin`; `SetAdminAsync` changes the flag and
  the security stamp together.
- **Visibility:** done. `Library.IsPublic` (default false) and `Library.Credit`;
  `VisibleBooks(principal)` on every book, cover, stream and progress endpoint;
  404 for a book you can't see. `PATCH /api/libraries/{id}` flips a library.
- **Folder covers:** done (scanner). The largest image wins, folder or embedded.
- **LibriVox:** live. Four books in `/mnt/media/librivox`, registered as the public
  library `LibriVox` with the credit "Public domain · LibriVox".
- **Still one account.** Nobody but Dean can sign in yet, so the non-admin view has
  only been verified by the integration tests, not on the live site.
- Progress, devices and the conflict rule were already per user and needed no
  changes.

## Decisions (settled 2026-10-01)

- **Accounts: both.** Admin-created accounts first (Dean asked to create accounts
  from the UI), invites after. Admin-created accounts get a temporary passphrase and
  must change it at first sign-in.
- **The private library stays Dean's alone.** Admins see everything, everyone else
  sees public libraries only. No per-user grants table.
- **Public books carry a credit.** It's a property of the library (`Credit`), shown
  under the author on the book page.

## What the work turned up

- **Existing sessions keep their old claims.** After the deploy, Dean's cookie
  (issued by the previous build) was signed in but not admin, and got 403 on
  `/api/libraries` until he signed in again. That's the stale-claim case
  `SetAdminAsync` guards against, seen from the other side. Expect it after any
  deploy that adds claims.
- **`/mnt/media` is immutable** (`chattr +i` on the drive's top folder). Creating a
  folder directly under it fails with "Operation not permitted", even with sudo.
  Lift it, create the folder, put it back:
  `sudo chattr -i /mnt/media && mkdir /mnt/media/<name> && sudo chattr +i /mnt/media`.
- **The Internet Archive returns the odd 500** from its storage nodes.
  `scripts/librivox-fetch.py` retries three times with backoff and skips files it
  already has, so a rerun resumes.
- **The hyphen heuristic met real data.** "Through the Looking-glass and What Alice
  Found There" would have become "glass and What Alice Found There". The series
  prefix is no longer stripped when the hyphen is followed by a lowercase letter.
  "Looking-Glass" with a capital G is still mangled; a test pins that limit.
- **LibriVox tags are clean but thin.** `album`, `artist`, `title` per file; `track`
  on some books (Sherlock Holmes, `1/24`) but not others (Looking-Glass, so it falls
  back to natural sort on file names, which is correct here). No narrator tag and
  no embedded art, so `cover.jpg` is the cover for every one.

## 1. Admin (done)

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

## 2. Who sees which books (done)

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

## 3. Getting accounts to people (next: A, then B)

Decided: **both**. A is being built first; see [`admin-accounts.md`](admin-accounts.md).

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

## 6. The public-domain library (done)

Live with four solo readings, chosen as a test mix:

| Book | Reader | Files | Why |
|---|---|---|---|
| Pride and Prejudice (v3) | Karen Savage | 61 | long novel |
| The Strange Case of Dr Jekyll and Mr Hyde | Bob Neufeld | 5 | short novel |
| The Adventures of Sherlock Holmes (v2) | Ruth Golding | 24 | story collection; one `album`, so it stays one book |
| Through the Looking-Glass (v2) | Adrian Praetzellis | 10 | hyphenated title |

Fetched with `scripts/librivox-fetch.py` (mp3s and the cover only; see
`docs/deploy.md`). First scan: 4 added, 0 failures.

The design notes below are what this was built from.

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

1. [x] Admin claim, admin-only endpoints, `isAdmin` on `/me`, tests.
2. [x] `Library.IsPublic`, `VisibleBooks`, the visibility test matrix.
3. [x] Folder cover images in the scanner.
4. [x] LibriVox on the server: four books, mounted, registered as a public library,
   scanned over Tailscale.
5. [ ] **Cloudflare rate-limit rule on login.** Still not done. It must exist before
   a second account does.
6. [ ] **The admin page: Users (admin-created accounts) and Libraries (public
   toggle, credit, scan buttons).** Plan: [`admin-accounts.md`](admin-accounts.md).
   Replaces the browser-console snippets.
7. [ ] **A command-line tool for server jobs** (`admin reset-password`, `admin scan`,
   …), run over SSH with `docker exec`. Plan: [`admin-cli.md`](admin-cli.md). Gives a
   recovery path, which doesn't exist today.
8. [ ] Invites: table, endpoints, `/invite/:code` page, tests.
9. [ ] Background scanning. Less urgent once `admin scan` exists.
10. [ ] First-visit library (web-polish), which matters as soon as a new user signs in.
11. [ ] Later: guest "Try it" for the portfolio.
