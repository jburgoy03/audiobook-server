# Deploying to the server

The runbook for the Ubuntu server. Development happens on Windows; the server
only builds and runs the image.

## Shape

| | |
|---|---|
| Image | `audiobook-server:latest`, built from the repo root (`Dockerfile`) |
| Stack | `/opt/docker/audiobook/` (`docker-compose.yml` and `.env`, copied from `deploy/`) |
| Container | `audiobook-api`, listening on 8080 inside |
| Database | the existing `audiobook-db` container, over the `audiobook_default` network |
| Libraries | `/mnt/media/audiobooks` (private, `Audiobooks`) and `/mnt/media/librivox` (public, `LibriVox`), each mounted **read-only at the same path** (the `Libraries` rows say those paths) |
| Covers and keys | named volume `audiobook-data` at `/data` (`/data/covers`, `/data/keys`) |
| Private access | `http://100.83.139.4:5043`, Tailscale IP only (not the LAN, not the internet) |
| Public access | `https://audiobooks.deanburgoyne.dev` via the `homelab` Cloudflare Tunnel |

The image is one process: the API serves the built web client from `wwwroot`, with a
fallback to `index.html` so client routes like `/books/:id` survive a reload. One
origin means no CORS, and the auth cookie works without configuration.

Why these choices:

- **ffmpeg is installed in the image.** The `aspnet` base has none, and the scanner
  needs `ffprobe` (durations, chapters, mp3 packet counts) and `ffmpeg` (covers).
- **Host port 5043, not 8080.** On this server, qBittorrent's web UI (published by
  Gluetun) is likely on 8080.
- **Same library path inside and out.** The library was scanned outside a container,
  so `Libraries.RootPath` is `/mnt/media/audiobooks`. Mounting it elsewhere would
  make every stream 404.
- **The tunnel reaches the API by container name.** `cloudflared` (in the portfolio
  stack) and `audiobook-api` share `portfolio_portfolio-net`; no host port is
  involved. The API honours `X-Forwarded-Proto` from the tunnel, so the cookie is
  `Secure` on the public hostname. Over Tailscale it isn't, because browsers refuse
  to store a `Secure` cookie on plain HTTP.

## `.env`

`/opt/docker/audiobook/.env`, mode 600, never committed. Template:
`deploy/.env.example`.

| Key | Meaning |
|---|---|
| `POSTGRES_PASSWORD` | Same as `~/projects/audiobook/.env` (what `audiobook-db` was created with) |
| `BIND_ADDRESS` | Server's Tailscale IP, `100.83.139.4` |
| `HOST_PORT` | `5043` |
| `DB_NETWORK` | `audiobook_default` |
| `TUNNEL_NETWORK` | `portfolio_portfolio-net` |
| `SEED_USERNAME` / `SEED_PASSWORD` | Creates the account on first start, only if no user exists. Safe to remove afterwards. On a fresh install, `admin create-user <name> --admin` (below) does the same without a passphrase in `.env`. |
| `GOOGLE_BOOKS_API_KEY` | Optional. Lets the admin page's blurb fetch ask Google Books (first, ahead of Open Library). Without it, Open Library only. A Google Cloud API key, "Public data", restricted to the Books API. Locally: user-secret `Blurbs:GoogleBooksApiKey`. |

Editing without an editor (no arrow keys over some terminals): set values with
`sed`, and the passphrase with `read -s` so it never shows or lands in history:

```
sed -i "s|^POSTGRES_PASSWORD=.*|$(grep '^POSTGRES_PASSWORD=' ~/projects/audiobook/.env)|" /opt/docker/audiobook/.env
read -rsp 'Passphrase: ' P && sed -i '/^SEED_PASSWORD=/d' /opt/docker/audiobook/.env && printf 'SEED_PASSWORD=%s\n' "$P" >> /opt/docker/audiobook/.env && unset P && echo
```

No `$` in values: Compose interpolates them.

## Updating

From Windows: commit and push. Then on the server (one at a time):

1. `cd ~/projects/audiobook && git pull`
2. If there are new migrations: `dotnet build`, then
   `dotnet ef database update --project src/AudiobookServer.Core --startup-project src/AudiobookServer.Api`
   (uses the server's user-secret connection string).
3. `docker build -t audiobook-server:latest .`
4. If `deploy/docker-compose.yml` changed: `cp deploy/docker-compose.yml /opt/docker/audiobook/`
5. `cd /opt/docker/audiobook && docker compose up -d`
6. `docker logs audiobook-api --tail 30`

After a deploy that adds or changes claims (the `admin` claim did, on 2026-10-01),
**sign out and back in**. A cookie keeps the claims it was issued with until its
next security-stamp check (every 5 minutes since 2026-10-02; 30 before), so an old
cookie can get 403 in the meantime.

The API does not migrate on startup. That's deliberate: a migration is a decision,
and running it by hand keeps a bad one from being applied by a restart.

## The `admin` command

Server jobs run as the API binary with `admin` first, inside the running container.
It uses the container's own configuration (database, paths), runs, and exits; the
web server keeps running alongside. No sign-in: whoever can run `docker exec` already
controls the server.

```
docker exec audiobook-api ./AudiobookServer.Api admin users
```

| Command | Does |
|---|---|
| `users` | Lists accounts: admin, disabled, passphrase still temporary, last listened. |
| `create-user <name> [--admin] [--temporary]` | Creates an account. Prompts for the passphrase, or with `--temporary` prints one they must change at first sign-in. |
| `reset-password <name> [--temporary]` | Replaces a passphrase (prompted, or generated). Clears a lockout and ends their sessions. |
| `set-admin <name> true\|false` | Grants or removes admin. Refuses to remove the last admin. |
| `enable <name>` | Re-enables a disabled or locked-out account. |
| `libraries` | Lists libraries: public or private, book count, last scan. |
| `scan <library> [--force]` | Scans a library by name (or ID) and prints the report. Ctrl+C cancels. |

**Prompts need a terminal: `docker exec -it`.** Passphrases are never arguments, so
they stay out of shell history and `ps`. Without `-it` the prompt gets no input and
the command says so.

Exit codes: 0 success, 1 failure, 2 bad usage.

## Accounts

Day to day, from the **Admin** page (link in the masthead, admins only):

- **Add listener:** type a name. The page shows a temporary passphrase once, with
  Copy (on the HTTPS hostname; on the Tailscale address, which is plain HTTP, the
  button selects it instead). Hand it over; they choose their own at first sign-in,
  and until then the server refuses them everything else.
- **Reset passphrase:** a new temporary one, shown once; their sessions end.
- **Disable / Enable:** a disabled account can't sign in, and open sessions end within
  five minutes (refresh tokens at once; an Android access token within its hour).

- **Libraries:** which private libraries a listener sees, as a checklist (public
  ones are ticked for everyone). Applies on their next request; nobody is signed
  out. Removing access hides their positions on those books rather than deleting
  them, so giving it back restores them. Admins see every library regardless.

The same rules apply from the `admin` command (except library access, which is on
the Admin page only for now). Every listener sees the public libraries, plus any
private library granted to them on the Admin page.

## Libraries and scanning

From the **Admin** page: make a library public or private (making one public asks
first, with its book count), edit its credit, **Scan**, **Force rescan**, or add one.

**Big scans: use `admin scan` or the Tailscale address, never the public hostname.**
Cloudflare gives up on a request after 100 seconds (524), and a scan from the page
runs inside the request, so it would be cancelled partway. The page warns about this
on the public hostname. A small scan (a new LibriVox book) is fine anywhere. From the
server, with no time limit:

```
docker exec -it audiobook-api ./AudiobookServer.Api admin scan Audiobooks --force
```

`--force` re-probes unchanged files. Use it after scanner changes (durations,
covers); a plain scan skips files whose mtime hasn't changed. mp3 packet counting
reads every file in full, so a forced scan of the big library takes minutes.

## Adding books

Copy a book's folder into `/mnt/media/audiobooks` (private) or `/mnt/media/librivox`
(public), then run a **plain** scan of that library (Admin page, or `admin scan
<library>`). A plain scan skips unchanged files, so only the new book is probed.
Nothing watches the folders: a book appears only after a scan.

What the scanner expects:

- **One book per folder.** A folder whose subfolders are *all* disc folders
  (`cd 01 of 11`, `disc 3`, `CD1`) is one book, played disc by disc. A mix of disc
  folders and anything else is not merged; "Part 1" folders are separate books.
- **Name folders `Title - Author`** (or `Title by Author`). Tags win when present
  (`album` → title, `artist` → author), but many downloads have none, and then the
  folder name is all there is. `Author - Title` reads the same to the parser and comes
  out backwards. To flip a batch of `Author - Title` folders:
  `for d in *' - '*; do mv -n -- "$d" "${d#* - } - ${d%% - *}"; done` (check the list first).
- **A `cover.jpg`** in the folder wins whenever it's larger than the embedded art.
  For books with no art, fetch one from Open Library, dry run first (it prints the
  edition it matched; translated books often match the original-language edition):
  `python3 scripts/fetch-covers.py /mnt/media/audiobooks --dry-run "Title - Author" …`,
  then the same without `--dry-run`, then a plain scan. A folder named otherwise takes
  a search override: `"Folder Name=Title|Author"`. A junk image in a folder (a
  screenshot that wins by size) is taken out of the running by renaming it to a
  non-image extension (`.jpg.bak`). Open Library's cover server returns the odd 502:
  rerun, finished folders are skipped.
- Files in one folder with different `album` tags are taken to be a collection and
  split into one book per album.

Check before scanning (album/artist per folder, run inside the container):

```
docker exec audiobook-api sh -c 'for d in /mnt/media/audiobooks/*/; do echo "== $d"; find "$d" -type f \( -iname "*.mp3" -o -iname "*.m4b" -o -iname "*.m4a" \) | while read -r f; do ffprobe -v quiet -show_entries format_tags=album,artist -of csv=p=0 "$f" </dev/null; done | sort | uniq -c | sort -rn | head -6; done'
```

## The public-domain library

LibriVox recordings live in `/mnt/media/librivox`, one folder per book
(`Title - Author`), mounted read-only like the main library. The library row has
`isPublic: true`, so every signed-in user sees these books; the main library stays
admin-only unless granted to a listener (Admin → Listeners → Libraries).

Fetch books with `scripts/librivox-fetch.py` (mp3s and the cover only; it skips the
per-track spectrogram PNGs, which would otherwise compete to be the cover):

```
python3 scripts/librivox-fetch.py /mnt/media/librivox "IDENTIFIER=Title - Author"
```

The identifier is the archive.org item (`archive.org/details/<identifier>`). Prefer
solo readings: quality varies far more between multi-reader recordings. Rerun the same
command after a failure: finished files are skipped. Then scan it:
`docker exec -it audiobook-api ./AudiobookServer.Api admin scan LibriVox`, or
**Scan** on the Admin page. To drop a book, delete its folder and scan again: the book
and everyone's position in it are removed.

**`/mnt/media` itself is immutable** (`chattr +i`, on the drive's top folder). New
folders *inside* `librivox` or `audiobooks` are fine, but a new top-level folder
needs the flag lifted and restored:

```
sudo chattr -i /mnt/media && mkdir /mnt/media/<name> && sudo chattr +i /mnt/media
```

It's registered as `LibriVox`, public, with the credit "Public domain · LibriVox".
A new folder of this kind is added from the Admin page (**Add library**); it must
also be mounted in `deploy/docker-compose.yml`, at the same path, first.

## Checks

- Reachable on the tunnel network, as `cloudflared` sees it:
  `docker run --rm --network portfolio_portfolio-net curlimages/curl -s -o /dev/null -w '%{http_code}\n' http://audiobook-api:8080/` → `200`
- A client device can't load `http://100.83.139.4:5043`? It isn't on the tailnet.
  `ssh media` uses the LAN, so SSH working proves nothing about Tailscale.

## Locked out?

Over SSH on the server, one at a time:

1. See the state of things: `docker exec audiobook-api ./AudiobookServer.Api admin users`
2. Forgotten passphrase: `docker exec -it audiobook-api ./AudiobookServer.Api admin reset-password dean`
   (prompts twice; ends your other sessions; clears a lockout).
3. Account disabled, or still locked after too many attempts:
   `docker exec audiobook-api ./AudiobookServer.Api admin enable dean`
4. Sign in again.

Nothing else is needed: no database edits, no restart. If `./AudiobookServer.Api` is
ever missing from an image, `dotnet AudiobookServer.Api.dll admin …` is the same
command.

## The server's other open doors

The audiobook stack has no inbound port: the tunnel dials out, the API is published
on the Tailscale address only, and Postgres on `127.0.0.1`. The same machine runs
other stacks, though, and their exposure was reviewed on 2026-10-02 (`sudo ss -tlnp`).

**Accepted risk (Dean, 2026-10-02): Jellyfin's port 8096 is forwarded on the Spectrum
gateway**, over plain HTTP, for one friend (`jdbmedia.duckdns.org`). Barely used, so
the risk is known and judged minimal. What it means: sign-ins and session tokens cross
the internet unencrypted; bots scan 8096 and try passwords; some Jellyfin endpoints
answer without sign-in (item images and metadata by ID); and Jellyfin runs with
`network_mode: host`, so a compromise would sit on the server's network next to
Portainer. The real fix, if usage grows: share the server with the friend over
Tailscale and remove the forward.

Everything else listens on all interfaces but is LAN-only (not forwarded): SSH 22,
Portainer 9000, qBittorrent 8080 and 6881 (via Gluetun), FlareSolverr 8191, Radarr
7878, Sonarr 8989, Prowlarr 9696, Audiobookshelf 13378. Docker's published ports
bypass `ufw`, so the router is the only boundary.

**Open question: inbound IPv6.** The server has public IPv6 addresses
(`2603:6010:…`), and every service above also listens on `[::]`. If the gateway lets
inbound IPv6 through, they're on the internet with no forward at all. Test from
outside (a phone on cellular, Wi-Fi off, after https://test-ipv6.com shows IPv6):
`http://[2603:6010:b600:564::145c]:13378` should **not** load. If it loads, turn on the
gateway's IPv6 firewall, or publish ports on IPv4 only (`"0.0.0.0:<port>:<port>"`).

Hardening checklist (cheap, none done yet):

- [ ] Run the IPv6 test above.
- [ ] Router: port forwarding lists **8096 only**; **UPnP off**.
- [ ] Jellyfin: media mounted **read-only** (`:ro`); strong, unique passwords on every
      account; never sign in as admin from outside (use an ordinary account remotely);
      keep it updated; lockout after failed sign-ins on (Dashboard → Users, per user);
      drop `network_mode: host` if hardware transcoding still works with the device
      passed through (`devices: /dev/dri`) and 8096 published normally.
- [ ] Portainer (9000): strong password; it controls Docker, which means root on the
      server. Better: publish it on the Tailscale address only, like the audiobook API.
- [ ] FlareSolverr (8191): has no authentication. Only Prowlarr uses it, over the
      stack's Docker network, so remove its published port.
- [ ] qBittorrent 6881: unpublish (Mullvad has no port forwarding, so it receives
      nothing useful there).
- [ ] Radarr, Sonarr, Prowlarr: authentication on, and "Disabled for local addresses"
      off.
- [ ] SSH: `PasswordAuthentication no` in `sshd_config` (keys only; Tailscale SSH
      covers remote access). Never forward 22.

## Still manual

- No CI: images are built on the server. Planned: GitHub Actions → GHCR → deploy over
  Tailscale with an ephemeral auth key.
- Cloudflare dashboard: the public hostname route and the rate-limiting rule live
  there, not in this repo. The rule (Security → Security rules, "Login throttle",
  2026-10-02): URI path equals `/api/auth/login`, 5 requests per 10 seconds per IP,
  block for 10 seconds, which is everything the free plan allows (one rule, path only,
  10-second period and block). Verified: the sixth rapid attempt gets Cloudflare's 429
  (error 1015) and never reaches the API. It slows guessing across accounts; it can't
  stop someone locking out a known name (five tries fit in one window). `admin enable`
  undoes that. It doesn't apply over Tailscale.
