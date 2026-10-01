# Web client

The browser player for AudiobookServer: a library page and a book page with a
chapter-aware player. React 19, TypeScript, Vite, React Router.

## Running it

The client needs the API and its database running. From the repo root:

1. `docker compose up -d` (Postgres)
2. Once per machine, the account the API creates on first start:
   `dotnet user-secrets set "Auth:SeedUser:Username" "<name>" --project src/AudiobookServer.Api`,
   and the same for `Auth:SeedUser:Password` (12+ characters)
3. `dotnet run --project src/AudiobookServer.Api` (API on http://localhost:5043)
4. In `web/`: `npm install`, then `npm run dev`, and open http://localhost:5173 and sign in

Vite proxies `/api` to the API, so the browser only ever talks to one origin and
no CORS setup is needed. Range headers pass through the proxy, so seeking works.
In production the API serves the built client itself, so it's one origin there too.
That's what lets the auth cookie cover `<audio>` and `<img>` requests, which can't
carry a bearer token.

Other scripts: `npm run build` (typecheck and production build to `dist/`),
`npm run lint`.

## Layout

- `src/auth`: sign-in state (`auth.ts`) and the sign-in page. The cookie is
  HttpOnly, so the client learns whether it's signed in from `GET /api/auth/me`, and
  from any 401.
- `src/api`: `fetch` wrappers. A 401 anywhere signs the app out.
- `src/pages`: the library and book pages.
- `src/player`: playback and progress. `useBookPlayer` drives one `<audio>` element across a
  book's files. `timeline.ts` mirrors the server's `BookTimeline`: every position
  is a number of seconds on the book's single timeline, translated to a file and
  an offset only at the element.
- `src/components`: cover art and icons.
- `src/index.css`: the whole design system. Tokens are at the top.

## Design

"Black, bone and blood": a true black page, bone-white type in EB Garamond (one
family; italics for authors and secondary text), and the books' cover art as the
only colour. Dark only, by design. Red keeps one meaning, where you are in a
book: the ribbon on started covers and in the masthead, the current chapter,
and the playhead.

The icon set (`src/components/Icons.tsx`) is drawn on one 24-unit grid with one
stroke weight and rounded joins. Motion only answers the listener: play morphs
into pause, the skip arcs turn while pressed, and a level meter moves on the
current chapter while audio plays. All of it stops under reduced motion.

Layout is one frame (`--frame`, `--gutter`) and one column grid (`--cols`,
`--col-gap`) shared by every page, so left and right edges line up everywhere.
Library tiles take one track, "Continue listening" items two, and on desktop the
book page splits two tracks (cover) and four (player).

## Playback

`src/player/PlayerProvider.tsx` sits above the routes and runs one active book at
a time, so playback survives navigation. It renders only while signed in, so
signing out (or a 401) stops playback and removes the now-playing bar. The live player is published through
`src/player/nowPlaying.ts`. The library's Resume button plays in place, and a
now-playing bar shows the active book on every other page.

## State

Positions sync through the server (`src/player/progress.ts`). Reads are
synchronous from an in-memory store, cached in browser storage and refreshed from
`GET /api/progress` after sign-in and when the tab becomes visible. Writes go to
the store first and then the server, and retry if they fail. A refresh never moves
the playing book: if another device is further ahead, the player offers a jump
(`JumpPrompt.tsx`). Speed and the per-browser device ID stay in browser storage
(`src/player/storage.ts`). Details and the conflict rule:
`docs/next-steps/auth-sync-deploy.md`.
