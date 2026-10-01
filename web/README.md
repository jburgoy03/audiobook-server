# Web client

The browser player for AudiobookServer: a library page and a book page with a
chapter-aware player. React 19, TypeScript, Vite, React Router.

## Running it

The client needs the API and its database running. From the repo root:

1. `docker compose up -d` (Postgres)
2. `dotnet run --project src/AudiobookServer.Api` (API on http://localhost:5043)
3. In `web/`: `npm install`, then `npm run dev`, and open http://localhost:5173

Vite proxies `/api` to the API, so the browser only ever talks to one origin and
no CORS setup is needed. Range headers pass through the proxy, so seeking works.

Other scripts: `npm run build` (typecheck and production build to `dist/`),
`npm run lint`.

## Layout

- `src/pages`: the library and book pages.
- `src/player`: playback. `useBookPlayer` drives one `<audio>` element across a
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
a time, so playback survives navigation. The live player is published through
`src/player/nowPlaying.ts`. The library's Resume button plays in place, and a
now-playing bar shows the active book on every other page.

## State

Playback position and speed live in browser storage, per browser profile. Server
sync replaces this in a later phase (see `docs/next-steps/auth-sync-deploy.md`).
