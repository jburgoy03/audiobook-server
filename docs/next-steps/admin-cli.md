# Next steps: a command-line tool for server jobs

Goal: the jobs that belong on the server, not in a browser, are commands run over
SSH:

```
docker exec audiobook-api ./AudiobookServer.Api admin reset-password dean
docker exec audiobook-api ./AudiobookServer.Api admin scan LibriVox --force
```

Status: **done 2026-10-02.** Usage is documented in `docs/deploy.md` ("The `admin` command", "Locked out?").

## Why

- **There is no recovery path today.** If Dean forgets his passphrase, or the admin
  account is disabled by mistake, nothing short of editing the database gets him
  back in. A command that needs shell access to the server is the right credential
  for that: whoever can run it already owns the box.
- **Scans without the 100-second limit.** A command runs the scan in-process, with
  no HTTP request for Cloudflare to cut off. Until background scanning exists, this
  is the reliable way to rescan the big library.
- **Repeatable and reviewable.** Commands live in the repo, are tested, and are
  documented in `docs/deploy.md`, instead of JavaScript pasted into a console.

## Shape

The same binary as the API. When the first argument is `admin`, `Program.cs` builds
the host (configuration, database, Identity, scanner: the same registrations), runs
the command in a scope, and exits **without starting the web server**. No second
project, no duplicated configuration, and it runs wherever the API runs: in the
container (`docker exec`), or locally (`dotnet run --project src/AudiobookServer.Api
-- admin …`).

Hand-rolled argument parsing: a handful of verbs doesn't need a library.

## Commands (first version)

| Command | Does |
|---|---|
| `admin users` | Lists users: name, admin, disabled, must-change. |
| `admin reset-password <user>` | Prompts for a new passphrase (no echo; never an argument, so it stays out of shell history) or, with `--temporary`, generates one and sets `MustChangePassword`. Clears lockout. Updates the security stamp. |
| `admin create-user <user> [--admin]` | Same prompt or `--temporary`. With `--admin`, uses `SetAdminAsync`. Replaces the `SEED_USERNAME`/`SEED_PASSWORD` dance on a fresh install. |
| `admin set-admin <user> true\|false` | `SetAdminAsync`. Refuses to remove the last admin. |
| `admin enable <user>` | Clears `LockoutEnd` and the failure count: the way back if the admin account itself gets disabled. |
| `admin libraries` | Lists libraries: name, path, public, book count, last scan. |
| `admin scan <library> [--force]` | Scans by name (or id) and prints the report. Ctrl+C cancels cleanly. |

Exit codes: 0 success, 1 failure (message on stderr), 2 bad usage.

`MustChangePassword` and the disable flag come from the admin-accounts work. If the
CLI is built first, `--temporary` and `enable` wait for it; `reset-password`,
`users`, `libraries` and `scan` don't.

## Tests

Integration, against the Testcontainers database: reset-password then sign in with
the new passphrase; create-user `--admin` gets the claim; set-admin refuses the last
admin; scan by name runs and reports; unknown verb exits 2.

## Docs to change when it lands

- `docs/deploy.md`: "Rescanning" becomes `docker exec … admin scan`; add a
  "Locked out?" section with `admin reset-password`.
- Remove the remaining console snippets (registering a library, rescans).
