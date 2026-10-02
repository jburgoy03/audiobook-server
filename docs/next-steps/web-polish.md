# Next steps: web client polish

Small, independent fixes to the web client in `web/`. Status as of 2026-10-02 (afternoon).

Auth, the sign-in page, synced progress and the jump offer are covered in
[`auth-sync-deploy.md`](auth-sync-deploy.md), not here.

## Done (2026-10-02, afternoon)

- **Featured timeline seeks.** The library's featured bar is the player's seek bar:
  drag shows the position, seek on release. Active book: live seek. Inactive: becomes
  active at that point, paused (the book page's idle rule).
- **Bottom row plays in place.** Each compact item's whole cover is a play button
  (`ContinueItem`); the title links to the book. The play disc is a hint on hover
  and keyboard focus, always visible on touch. Playing moves the book to featured.
- **Now-playing bar on the book's own page too** (Dean's call), without the jump
  offer there (the player has it). The sticky desktop column subtracts the bar's
  height so the chapter list's last rows stay above it.
- Kept: the 60-second "started" threshold for the compact row. A briefly sampled
  book stays out of it until it's played past a minute (Dean is fine with that).

## Done (2026-10-02)

- **Admin page** (`/admin`): Listeners and Libraries. See `admin-accounts.md`.
- **Choose-your-passphrase page**, shown instead of the app while
  `mustChangePassword`.
- Masthead "Admin" link (`NavLink`, bone while on the page). Checked at 375.

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

0. ~~**Continue listening ignores the book that's playing.**~~ **Done 2026-10-02.**
   The section was built from saved positions (every 30s, and only past 60s). Now the
   active book (`nowPlaying`), playing or paused, is always first, live, whatever its
   position; the rest follow by last activity. `useActiveBookId()` returns only the id
   (a string snapshot), so the library re-renders when the active book changes, not on
   every time update. Compact items' "left" times needed nothing: they can no longer be
   the active book, and other books' saved positions are exact (the player saves on
   pause and when replaced). The All books tile of the active book reads the live
   position (`LiveCover`), so it no longer lags or lacks a ribbon under 60s.

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
   largest image wins, folder or embedded; LibriVox covers work. Missing covers are
   fetched with `scripts/fetch-covers.py` (Open Library, 2026-10-02; see
   `docs/deploy.md`); then a plain scan. Two fetched covers are small (Kafka on the
   Shore 6 KB, Blind Willow 5 KB) and may look soft; Murakami's are the Japanese
   editions' (Dean's choice). The Name of the Wind's 175x175 art still wants a
   `cover.jpg`.
4. ~~**First-visit library.**~~ **Done 2026-10-02.** With nothing active or in
   progress, the most recently added unfinished book is featured under "Newly added"
   (Play, length and chapter count instead of "0% listened"). `GET /api/books` now
   carries `addedAt`. Heuristic: no curation needed, changes as books are added, but
   arbitrary within one scan's batch. A hand-picked suggestion would need a per-book
   flag.
5. **Auto-scroll on tablet and phone.** The chapter list follows playback only on
   desktop. A "Jump to current chapter" control in the stacked layout would cover it.
6. **Full visual pass at 768 and 375** in the new design. Checked at 1024 so far.
7. **Metadata overrides.** Edit a book's title and author from the admin page, kept
   across rescans (a separate override column, since a rescan rewrites the tag-derived
   fields). Poor tags today: After the Quake (lowercase title; artist tag lists the
   narrators), "The Will of the Many (Unabridged)", "Blind Willow" for Blind Willow,
   Sleeping Woman. Overlaps with metadata enrichment (2g).
8. **Light mode** was removed deliberately. If it comes back, it needs its own
   palette pass rather than inverted tokens.

9. ~~**Volume control.**~~ **Done and deployed 2026-10-02.** Decisions (Dean):
   - **Both** a mute button (speaker icon) and a slider. On touch screens, mute only:
     hardware buttons set the level, and iPhone Safari ignores `audio.volume`
     entirely (only `muted` works). Detected by setting the volume and reading it
     back, not by guessing from the browser name; the slider hides where it can't work.
   - **In the now-playing bar only.** Tried first on the book page and the
     featured book as well; two on one page was one too many (Dean), and the bar
     shows on every page while a book is active, which is when volume matters.
   - **One app-wide store** (`player/volume.ts`, `useSyncExternalStore`), not
     per-player state, so every control shows and sets the same thing, an idle book's
     control behaves like the live one's, and each new `Audio` (one per activation)
     picks the level up. Files within a book reuse the element, so a file boundary
     can't lose it.
   - **Level remembered per browser** (like speed: one setting for every book, not
     synced, since loudness belongs to the device). **Mute is not remembered:** a
     tab that reloads silent looks broken.
   - Bone, not red (red means position). Native range input for keyboard and screen
     readers; dragging the slider while muted unmutes.
   - Hand test: level holds across a file boundary, a book switch and a reload; a
     reload is never muted; the phone shows only the mute button; 375 and 1024.

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
