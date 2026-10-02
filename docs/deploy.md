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
| `SEED_USERNAME` / `SEED_PASSWORD` | Creates the account on first start, only if no user exists. Safe to remove afterwards. |

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
next security-stamp check (30 minutes), so an admin's old cookie gets 403 on admin
endpoints in the meantime.

The API does not migrate on startup. That's deliberate: a migration is a decision,
and running it by hand keeps a bad one from being applied by a restart.

## Rescanning

**Interim.** The console snippets in this file are a stopgap until the admin page's
Libraries section (`docs/next-steps/admin-accounts.md`) and the `admin` command
(`docs/next-steps/admin-cli.md`) exist; both will replace them. Don't add more.

The scan endpoint needs a signed-in admin, so for now run it from the browser's
console on the site (F12 → Console; Chrome asks you to type `allow pasting` first).

**Use the Tailscale address (`http://100.83.139.4:5043`), never the public
hostname.** Cloudflare gives up on a request after 100 seconds (524), and the scan
runs on the request's cancellation token, so it would be cancelled partway. This
holds until scanning moves to a background service.

Signed in as an admin (library endpoints are admin-only). Pick the library by name,
since there's more than one:

```
const lib = (await (await fetch('/api/libraries')).json()).find(l => l.name === 'Audiobooks'); await (await fetch(`/api/libraries/${lib.id}/scan?force=true`, { method: 'POST' })).json()
```

`force=true` re-probes unchanged files. Use it after scanner changes (durations,
covers); a plain scan skips files whose mtime hasn't changed. The request returns
when the scan finishes (minutes: mp3 packet counting reads every file in full).

## The public-domain library

LibriVox recordings live in `/mnt/media/librivox`, one folder per book
(`Title - Author`), mounted read-only like the main library. The library row has `isPublic: true`, so every
signed-in user sees these books; the main library stays admin-only.

Fetch books with `scripts/librivox-fetch.py` (mp3s and the cover only; it skips the
per-track spectrogram PNGs, which would otherwise compete to be the cover):

```
python3 scripts/librivox-fetch.py /mnt/media/librivox "IDENTIFIER=Title - Author"
```

The identifier is the archive.org item (`archive.org/details/<identifier>`). Rerun
the same command after a failure: finished files are skipped. Then scan the
`LibriVox` library over Tailscale (the snippet above, with `'LibriVox'`).

**`/mnt/media` itself is immutable** (`chattr +i`, on the drive's top folder). New
folders *inside* `librivox` or `audiobooks` are fine, but a new top-level folder
needs the flag lifted and restored:

```
sudo chattr -i /mnt/media && mkdir /mnt/media/<name> && sudo chattr +i /mnt/media
```

Registered once, from the console on the Tailscale address:

```
await (await fetch('/api/libraries', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name: 'LibriVox', rootPath: '/mnt/media/librivox', isPublic: true, credit: 'Public domain · LibriVox' }) })).json()
```

Make a library public or private later with
`PATCH /api/libraries/{id}` and `{ "isPublic": false }`.

## Checks

- Reachable on the tunnel network, as `cloudflared` sees it:
  `docker run --rm --network portfolio_portfolio-net curlimages/curl -s -o /dev/null -w '%{http_code}\n' http://audiobook-api:8080/` → `200`
- A client device can't load `http://100.83.139.4:5043`? It isn't on the tailnet.
  `ssh media` uses the LAN, so SSH working proves nothing about Tailscale.

## Locked out?

There is no recovery path yet: a forgotten admin passphrase means editing the
database. `admin reset-password` (`docs/next-steps/admin-cli.md`) is the fix.

## Still manual

- No CI: images are built on the server. Planned: GitHub Actions → GHCR → deploy over
  Tailscale with an ephemeral auth key.
- Cloudflare dashboard: the public hostname route and a rate-limiting rule on
  `POST /api/auth/login` live there, not in this repo.
