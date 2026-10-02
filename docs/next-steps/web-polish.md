# Next steps: web client polish

Small, independent fixes to the web client in `web/`. Status as of 2026-10-01.

Auth, the sign-in page, synced progress and the jump offer are covered in
[`auth-sync-deploy.md`](auth-sync-deploy.md), not here.

## Done (2026-10-01)

### Admin and credits (evening)

- `auth.ts` keeps `isAdmin` from `/api/auth/me`. Nothing renders it yet; the admin
  page will.
- The book page shows the library's credit ("Public domain · LibriVox") under the
  narrator line.

### Layout

- **One system.** Frame and gutter tokens (`--frame`, `--gutter`) shared by the
  masthead and every page, plus one column grid (`--cols`: 2 → 4 at 40rem → 6 at
  64rem, always even). `scrollbar-gutter: stable` stops the frame jumping 7px
  between pages that do and don't scroll. Shared left and right edges verified at
  1440/1024/768/375.
- **Library.** Both sections share the grid's tracks (subgrid). Section headings
  carry a full-width rule and counts. "Continue listening" features the most
  recent book: cover on two tracks, chapter line, read-only chapter-segmented
  progress bar, and Resume, which plays in place. Up to three more started books
  follow in the compact form.
- **Book page.** Desktop is two columns on the same six tracks (cover and details
  on two, player and chapters on four), both sticky; the chapter list scrolls in
  its own area and its scrollbar hangs in the page margin, so rows end on the
  timeline's right edge. Stacked below 64rem.
- **Player alignment.** Left items on the left edge, right items on the right
  edge, transport centred on the timeline's width (speed at the right end,
  status at the left). A container query stacks it when the player is narrow.
- **Covers.** Large frames (book page, featured book) always fill their tracks; a
  low-res image is drawn at most 1.5x its pixel size, centred on a blurred backdrop.

### Playback

- **App-level player.** `PlayerProvider` runs one active book above the routes and
  publishes it through `player/nowPlaying.ts` (`useSyncExternalStore`), so only
  readers re-render on time updates. Playback survives navigation.
- **Resume plays in place** on the library. A now-playing bar (smoked glass, red
  progress line) shows on every page except the active book's own. A book page
  for an inactive book shows the saved spot; play or a chapter click makes it
  active.
- **Chapter list follows playback** (desktop): scrolls to the current chapter on
  load and as playback moves, within the list only, and holds off for 5s after
  the user touches the list.

### Design: "Black, bone and blood"

Two rounds of feedback: the first redesign (gothic academic: blackletter, gilt,
leather) read as costume. The current direction:

- True black page, bone-white type, **EB Garamond only** (italics for authors and
  secondary text), cover art as the only colour. **Dark only**, by design.
- Red keeps one meaning: ribbon (covers, masthead mark, favicon), current
  chapter, playhead.
- **Icon set** in `components/Icons.tsx`: one 24-unit grid, one stroke weight
  (1.6), rounded joins. Play morphs into pause (shared point counts, so `d`
  interpolates in Chromium; elsewhere it swaps). Skip arcs turn while pressed.
  A red level meter moves on the current chapter while playing.
- **Masthead mark**: a ribbon hanging from the top edge of the page on the
  frame's left line, ending under the wordmark's baseline.
- **Feel**: press states on every control (fast in, slow out), hover only on
  hover-capable devices, no tap flash or double-tap delay, no text selection on
  rapid taps. Focus rings on the timeline and speed control only after keyboard
  use (`data-input` on `<html>`, set in `main.tsx`). Reduced motion stops all
  movement but keeps tints.
- **Speed menu** styled with `appearance: base-select` (Chromium); other
  browsers keep the native picker.
- Lint clean. The two `set-state-in-effect` errors were fixed by restructuring
  (book page keyed by id; resume position as initial state), not suppressed.

## Cleanup: done

The stray root `package.json`/`node_modules`, the unused Literata and Figtree
fonts, `web/public/icons.svg` and `web/src/main.tsx.new` are all gone.

## Remaining

1. **File-boundary gap.** Not measured yet. When a file ends, the next is loaded
   from scratch (`useBookPlayer`: `onEnded` → `loadFile`). Measure the silence
   first; if noticeable, preload the next file on a second hidden `Audio` in the
   last ~30s and swap on `ended`, or warm the cache with a ranged `fetch` of its
   first ~256 KB. The player now lives in `PlayerProvider`, so a second element
   belongs there too.
2. **Narrator tag.** Neither sample book has `composer` or `narrator`. Run
   `ffprobe -show_format` on a file of each and check `album_artist`, `performer`,
   `comment` before extending the precedence in `BookScanner`. Otherwise this waits
   for metadata enrichment (2g).
3. ~~**Folder cover images.**~~ **Done 2026-10-01** (scanner, `CoverSelector`). The
   largest image wins, folder or embedded; LibriVox covers work. To fix Lord of the
   Rings or The Name of the Wind's 175x175 art, put a `cover.jpg` in the book's
   folder and rescan (an image newer than the last scan triggers it).
4. **First-visit library. Now the priority item.** With nothing started (every new
   user of the public library), there is no featured book. Feature a suggestion or
   the most recently added book.
5. **Auto-scroll on tablet and phone.** The chapter list follows playback only on
   desktop. A "Jump to current chapter" control in the stacked layout would cover it.
6. **Full visual pass at 768 and 375** in the new design. Checked at 1024 so far.
7. **Light mode** was removed deliberately. If it comes back, it needs its own
   palette pass rather than inverted tokens.

## Working notes

- Dev needs three things running: `docker compose up -d` (Postgres),
  `dotnet run --project src/AudiobookServer.Api`, and `npm run dev` in `web/`.
  Open http://localhost:5173 and sign in (the account comes from the
  `Auth:SeedUser:*` user-secrets).
- **Vite's file watcher does not see files written through Claude's remote folder
  access.** After Claude edits files in `web/`, restart `npm run dev`. A possible
  fix: `server.watch.usePolling` in `vite.config.ts` (costs some CPU; node_modules
  is already excluded from watching).
- **Run npm in `web/`, or pass `--prefix web`.** From the repo root, a plain
  `npm install` creates a stray `package.json` there.
- Positions sync through the server now. Browser storage is only their offline
  cache, plus the speed setting and the per-browser device ID. Two browsers count
  as two devices, which is the easiest way to test the jump offer.
