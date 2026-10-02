# Next steps: offering it as a self-hosted option

Goal (Dean, 2026-10-02): anyone with their own audiobook files can run this against
their own library, the way Jellyfin works for video. Wide adoption isn't the goal;
a clean, honest self-hosted release is. This doc is **tier 1: installable by someone
comfortable with Docker**, and nothing past it.

## Where it stands

Already fine for other people:

- One image (API plus web client, ffmpeg inside, non-root, `/data` volume).
- Accounts, admin page, `admin` command, library grants, public libraries.
- The walker finds books at any depth (`Author/Series/Title/` works), handles disc
  sets, and reads m4b, m4a, mp3, opus, ogg, flac, aac and wma.
- Sync that survives offline devices and multiple players (see below).

Tied to Dean's server:

- **Built on the server**, never published. Nobody else can `docker pull` it.
- **`deploy/docker-compose.yml`** assumes his networks (`audiobook_default`,
  `portfolio_portfolio-net`), a Postgres container from another compose file, a
  Tailscale bind address and his `/mnt/media` paths.
- **Migrations by hand** (`dotnet ef database update` on the server, with the SDK).
- **First admin** from `SEED_*` in `.env` or the `admin` command.
- **Personal values in code:** the blurb fetcher's User-Agent names
  `audiobooks.deanburgoyne.dev` (`Program.cs`); the admin page's scan note and blurb
  comments talk about Cloudflare and Tailscale; the add-library placeholder is
  `/mnt/media/…`.
- **Forwarded headers trusted from anyone** (`Program.cs`: `KnownIPNetworks` and
  `KnownProxies` cleared). Acceptable when only the tunnel and the tailnet can reach
  the port; not for a stranger who publishes port 8080 to the internet, where a forged
  `X-Forwarded-For` would put any address in the logs.
- **Login throttling lives in Cloudflare** (the "Login throttle" rule), not in the API.
  Identity's per-account lockout remains, but nothing slows guessing across accounts.
- **Names come from the book's own folder only** (`BookScanner.ParseDirectoryName`:
  `Title - Author` / `Title by Author`). An untagged `J.R.R. Tolkien/The Hobbit/`
  gets the title and no author, and that layout is the most common one elsewhere.
- **No LICENSE, no root README** (`web/README.md` is the Vite template's).

## What to lead with

Sync you can trust. Audiobookshelf, the established option, has repeated reports of
lost or stale progress: a newer server timestamp overwriting further local progress
in the app ([audiobookshelf-app #2024][abs-2024], fixed only in a fork), stale resume
after switching from web to app ([discussion #5033][abs-5033]), resets after
downloading ([#825][abs-825]). Here: `ReportedAt` separate from `UpdatedAt`, furthest
wins, the same device may rewind, finished ranks past everything, and a device that's
behind gets an explicit jump offer. Plus book identity by path, not inode (inode reuse
on network shares merges books there, [write-up][abs-inode]), and the mp3 duration
fix. The README should say so plainly, with the jump offer in a screenshot.

[abs-2024]: https://github.com/advplyr/audiobookshelf-app/issues/2024
[abs-5033]: https://github.com/advplyr/audiobookshelf/discussions/5033
[abs-825]: https://github.com/advplyr/audiobookshelf-app/issues/825
[abs-inode]: https://soundleafapp.com/server/audiobookshelf-wrong-metadata-new-books/

## Decisions for Dean (before starting)

1. **License.** AGPL-3.0 (changes to a hosted copy must be shared; common for
   self-hosted servers) or MIT (simplest, anything goes). GPL-family is the norm in
   this space (Jellyfin GPL-2.0, Audiobookshelf GPL-3.0).
2. **Name and image name.** "AudiobookServer" is a description, not a name. The image
   name (`ghcr.io/jburgoy03/<name>`) is hard to change once people pull it.
3. **Migrations on startup.** Dean's server deliberately migrates by hand ("a migration
   is a decision"). Strangers won't install a .NET SDK. Proposal: a setting
   `Database:MigrateOnStartup`, **on** in the published compose, **off** in Dean's.
4. **First-run protection** (item 5): a setup token printed in the container log
   (recommended), or the open first-visitor wizard Jellyfin uses.
5. **Default port.** 8080 inside; the example compose publishes something less
   collision-prone (8080 is often taken: Dean's own server has qBittorrent there).

## Milestone 1: a friend can install it

1. [ ] **LICENSE and root README.** README: what it is, a screenshot or two, the sync
   story, quick start (the compose file below), link to the install guide. Done when:
   a stranger understands what it is in one screen.
2. [ ] **CI publishes images.** GitHub Actions: on every push, build and run the tests
   (`dotnet test` including integration: Testcontainers works on GitHub's Ubuntu
   runners, which have Docker; the rescan test needs `ffmpeg` installed in the job); on a
   tag `v*`, build **amd64 and arm64** (Raspberry Pi, most NAS boxes) and push to GHCR as
   `:<version>` and `:latest`. Overlaps the CI/CD roadmap item: Dean's server can then
   pull instead of build. Done when: `docker pull ghcr.io/…:latest` works on an arm64
   machine.
3. [ ] **A generic compose file** (`deploy/self-host/docker-compose.yml` plus
   `.env.example`), separate from Dean's: the app and `postgres:17-alpine` together,
   named volumes for `/data` and the database, one library mounted read-only at
   `/audiobooks`, `POSTGRES_PASSWORD` the only required value. Done when: a fresh
   machine goes from nothing to a running server with `docker compose up -d`.
4. [ ] **Migrations on startup** behind the setting (decision 3), logged clearly, and
   refusing to start on a failed migration rather than running on a half-migrated
   schema. Document `pg_dump` before upgrading. Done when: upgrading is `docker compose
   pull && docker compose up -d`.
5. [ ] **First-run setup page.** While no account exists, the web client shows "Create
   the admin account" instead of sign-in. Risk: whoever reaches a fresh install first
   becomes admin. With a setup token (decision 4), the page asks for a one-time code
   the server logs at startup. `SEED_*` and `admin create-user` keep working. Done when:
   a fresh install needs no `.env` account values and no shell.
6. [ ] **No personal values.** User-Agent names the project and repo, not Dean's site;
   admin-page text speaks of "a reverse proxy with a request timeout" rather than
   Cloudflare and Tailscale (item 8 makes most of it moot); the add-library placeholder
   is `/audiobooks`. Done when: `grep -ri "deanburgoyne\|tailscale\|cloudflare" src web/src`
   finds only comments explaining Dean's own deployment, or nothing.

## Milestone 2: it holds up on someone else's library

7. [ ] **Folder layouts.** When tags don't give an author, use the parent folder:
   `Author/Title/` and `Author/Series/Title/` (series as a field later). Keep `Title -
   Author` working. Unit tests per layout, plus the existing backwards-`Author - Title`
   pin. Done when: an untagged `Tolkien/The Hobbit/` comes out as The Hobbit by Tolkien.
8. [ ] **Background scanning** (roadmap 2h item 9): `Channel<T>` + `IHostedService`,
   `POST …/scan` answers 202 with a job, the admin page polls status. Plus an optional
   scheduled scan (nightly). Done when: a 2,000-book first scan works through any
   reverse proxy, and new folders show up without a click.
9. [ ] **Trusted proxies as a setting.** `ForwardedHeaders:KnownNetworks` (CIDR list),
   defaulting to private ranges (10/8, 172.16/12, 192.168/16, Docker's networks), so a
   proxy on the same machine or LAN works out of the box and a directly exposed port
   ignores forged headers. Dean's compose sets what his tunnel needs. Done when: a
   forged `X-Forwarded-For` from outside the list is ignored (integration test).
10. [ ] **Login throttling in the API.** ASP.NET Core's rate limiter on
    `/api/auth/login`, per IP (needs item 9 so the IP is real), e.g. 5 per 10 s like
    the Cloudflare rule. Done when: the sixth rapid attempt from one address gets 429
    without Cloudflare.
11. [ ] **Measure a big library.** Generate 1,000 books of silent mp3s (the test helper
    in `AdminBookTests` already writes valid frames) and measure: first scan time, a
    no-change rescan, `GET /api/books` size and time, the library page's render. Add
    paging or search only if the numbers say so. Done when: the numbers are in this doc.
12. [ ] **Install guide** (`docs/self-hosting.md`): install, the library mount and file
    permissions (the container runs as a non-root user, uid 1654: the library must be
    readable by it, which a read-only mount of world-readable files satisfies; people
    used to linuxserver images will look for PUID/PGID), reverse proxy examples (Caddy,
    nginx, Cloudflare Tunnel, Tailscale), HTTPS (the auth cookie is `Secure` only over
    HTTPS), upgrading, backup and restore (`pg_dump` plus the `/data` volume: losing the
    keys ends every session), the `admin` command, "Locked out?".
13. [ ] **Security housekeeping.** `SECURITY.md` (how to report), Dependabot for NuGet,
    npm and the base images, a note on what's exposed without sign-in (login, logout,
    the static client: nothing else).

## Not in tier 1

iOS app, podcasts, ebooks, transcoding, Audible metadata, a plugin system, light mode,
invites (useful but not needed to install), Android distribution (its own phase).

## Order

Decisions first, then 1 → 6 (one or two sessions; 2 and 3 are most of it), a release
`v0.1.0` marked alpha, then 7 → 13. Dean's own server switches to the published image
at item 2, which is the best test that the image works without his build setup.
