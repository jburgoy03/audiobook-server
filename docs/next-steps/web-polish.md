# Next steps: web client polish

Small, independent fixes to the web client in `web/`. Each can be done in one
sitting. Start with layout, because it is the one the user called out.

Current state (2026-10-01): library page with "Continue listening" and a cover grid;
book page with cover header, chapter-segmented timeline, transport controls, speed,
and a chapter list. Positions and speed are saved in browser storage. Design system
"bookcloth and ribbon" lives in `web/src/index.css` (tokens at the top).

## 1. Layout and alignment (do this first)

**Feedback:** "Everything is oddly centered and there is a lack of symmetry."

Likely causes, all in `web/src/index.css`. Confirm each at a wide window
(1440px+) before changing anything:

- **Two column systems.** `.masthead` and `.app` are a centered 1040px frame, but
  `.book` is capped at 760px and sits left inside it. On wide screens the book page
  hugs the left with a large empty area on the right, while the library grid uses
  the full 1040px. The two pages don't share a right edge.
- **Mixed alignment axes in the player.** The transport row is centered, the speed
  control and status are left-aligned underneath it, and `.now-detail` and `.times`
  are split left/right. Three alignment rules in one block reads as unbalanced.
- **Book header.** `.book-header` uses `align-items: end`, so the title sits at the
  bottom of the cover with dead space above it.
- **Library grids don't line up.** "Continue listening" uses 300px columns with
  112px covers; "All books" uses `auto-fill minmax(150px, 1fr)`. Their columns and
  edges don't match, and with only two books the grid leaves a ragged right side.

Direction to take (decide in the session, but pick one system and apply it everywhere):

- One frame width and one gutter, as tokens (`--frame`, `--gutter`), used by the
  masthead, both pages, and every section. Every left edge (wordmark, back link,
  cover, title, timeline, chapter list) lands on the same line; same for right edges.
- Book page on desktop: two columns, `minmax(220px, 300px) 1fr`. Left: cover, title,
  author, narrator (sticky). Right: player and chapter list. Stacked on mobile.
- Player: one axis. Either everything spans the timeline's full width (transport
  centered under it, speed at the right end of the transport row, status below), or
  everything left-aligned. Don't mix.
- Library: one shared column track for both sections, so "Continue listening" items
  span whole columns of the same grid as "All books".

Acceptance: screenshots at 1440, 1024, 768 and 375 wide, in light and dark mode,
show shared left and right edges and no section that looks offset.

## 2. Favicon

The tab still shows Vite's logo (`web/public/favicon.svg`, referenced in
`web/index.html`). Replace with a small ribbon/bookmark mark: `--ribbon` on
`--cloth`, legible at 16px. Also delete or rewrite the template `web/README.md`.

## 3. Scroll the chapter list to the current chapter

On The Name of the Wind, chapter 65 is far down a 92-item list. On load, and when
the current chapter changes, scroll the current item into view
(`scrollIntoView({ block: 'nearest' })`). Don't fight the user: skip the automatic
scroll if they scrolled the list in the last few seconds. Pairs well with making the
chapter list its own scroll area in the desktop two-column layout.

## 4. Close the gap at file boundaries

When a file ends, the next one is loaded from scratch, which leaves a short silence.
Measure it first; if it's noticeable:

- Option A: a second hidden `Audio` element preloads the next file in the last ~30s
  of the current one, and the two swap on `ended`.
- Option B: warm the browser cache with a ranged `fetch` of the next file's first
  ~256 KB, keeping a single element.

Logic lives in `web/src/player/useBookPlayer.ts` (`loadFile`, `onEnded`).

## 5. Narrator never shows

Neither sample book has a `composer` or `narrator` tag, so "Read by" never renders.
Run `ffprobe -show_format` on a file and check for other tag names (`album_artist`,
`performer`, `comment`) before adding to the precedence in `BookScanner`. Otherwise
this waits for metadata enrichment (2g).

## 6. Lint

Run `npm run lint` in `web/`. `eslint-plugin-react-hooks` v7 may flag the setState
calls inside effects in `BookPage.tsx` and `useBookPlayer.ts`; decide per case
whether to restructure or accept.

## Working notes

- Dev needs three things running: `docker compose up -d` (Postgres),
  `dotnet run --project src/AudiobookServer.Api`, and `npm run dev` in `web/`.
  Open http://localhost:5173.
- **Vite's file watcher does not see files written through Claude's remote folder
  access.** After Claude edits files in `web/`, restart `npm run dev`, or the browser
  keeps the old version. Edits made in VS Code are unaffected.
- Browser storage is per browser profile: the Claude app's built-in browser and your
  own browser keep separate positions.
