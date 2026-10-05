# Library search, sort and genres

Planned 2026-10-05. The library has ~60 books and the All books grid is a long
scroll; at a few hundred it stops being usable. Two milestones: search, sort and
status filters first (cheap, solves the scrolling now), then curated genres (needs
data that doesn't exist yet).

Decisions (Dean, 2026-10-05):

- Search covers **title and author** only.
- "In progress" reuses the **60-second rule** from Continue listening.
- Genres are a **curated vocabulary assigned by the admin**, with catalogue
  categories shown as suggestions. Nothing is applied automatically.

---

## Milestone 1: search, sort, status (client-side)

**Shipped 2026-10-05** (without the library chip). Code: `web/src/library/`
(`search.ts`, `libraryQuery.ts`, `LibraryToolbar.tsx`, with Vitest tests). Where it
differs from the plan below, the "As built" notes say so.

### Where it runs

In the browser, against the list the library page already loads.
`GET /api/books` returns every book the caller may see (grants and overrides
applied), so filtering that array per keystroke is instant at 60 books and still
trivial at 1,000. No endpoint, no migration, nothing for Android to wait on (it can
filter its own copy the same way).

Tradeoff: this holds only while the list endpoint returns everything. If it is ever
paginated (a few thousand books), search moves server-side: `pg_trgm` or full-text
on `COALESCE(override, scanned)`. Self-hosting milestone 2's 1,000-book measurement
is the point to decide; nothing here blocks that.

Not searchable: blurbs (only `GET /api/books/{id}` carries `description`; adding it
to the list would roughly triple the payload) and narrators (not stored yet,
web-polish item 2). Narrators join the match when they exist.

### Matching

One pure function, `matchesQuery(book, query)` in `web/src/library/search.ts`:

- Normalise both sides: lowercase, NFD then drop combining marks (diacritics),
  punctuation to spaces, collapse whitespace.
- Split the query into words. **Every word must appear somewhere in
  `title + " " + author`, in any order** (substring match per word). "murakami wood"
  finds Norwegian Wood; "lamora" finds The Lies of Locke Lamora.
- Empty query matches everything.
- **No typo tolerance in v1.** Fuzzy matching is a heuristic that produces odd hits
  on short queries; revisit only if mistyping turns out to be a real annoyance.

Overrides are already applied to `title`/`author` in the list, so a fixed title is
searchable as fixed.

### Sort

Title (default), Author, Recently added (`addedAt`), Length.

Author sorts by the **full author string**, not the surname. Surname sort would
guess that the last word is the surname, which fails on "Ursula K. Le Guin" and on
narrator strings. Add it later only if the order bothers anyone.

Ties fall back to title.

### Filters

- **Status**: Not started / In progress / Finished, from the progress data the page
  already holds. In progress = more than 60 s saved and not finished, the same rule
  as Continue listening (shared helper, so the two can't drift). Not started = under
  60 s, not finished. *As built:* exactly 60 s counts as started, matching the
  existing Continue listening code. The active book counts by its saved position.
- **Library**: a chip per library, shown only when the caller sees more than one.
  *Not built yet:* `GET /api/books` doesn't say which library a book is in, so this
  needs `libraryId` on `BookSummary` first. Only an admin sees more than one
  library today, so it waits.

Filters combine with search with AND; choices within one filter combine with OR.

### UX

- Search box above "All books" on the library page.
  *As built:* in the **masthead**, on the library page only. At 75em and wider,
  search, chips and sort sit in the middle of the masthead row. Narrower, a search
  button beside the nav opens them as a band under the row; it starts open when the
  URL already filters, and shows a dot when closed with filters on. Above All books,
  the box would have jumped up the page on the first keystroke, as the shelves above
  it hide.
- While a query or filter is active, **Continue listening and the featured book are
  hidden**, so results start at the top. The now-playing bar stays.
- Result count beside the heading ("12 books").
- State lives in the URL via React Router search params:
  `?q=murakami&sort=added&status=unstarted&library=<id>`. Back, refresh and shared
  links keep the search; nothing stored. Typing replaces the history entry rather
  than pushing one per keystroke.
  *As built:* every change replaces the entry (Back from the library leaves it
  rather than undoing filters). The box keeps its own text and writes the URL
  120 ms after typing pauses; bound straight to the URL it dropped letters.
- Empty state: "No books match 'xyz'" with a Clear button that resets everything.
- Desktop: `/` focuses the search box (ignored while typing in another field);
  `Esc` in the box clears it. *As built:* a second `Esc` on the empty box leaves it
  (and closes the band on narrow screens).
- Built in the obvious form first; Dean decides by trying it: whether the box is
  sticky on phones, chips inline or behind a "Filter" button, whether sort is
  remembered per browser.

### Steps

1. `search.ts`: normalise, `matchesQuery`, sort comparators, status classification
   (shared with Continue listening). Unit tests for diacritics, punctuation,
   word order, empty query, the 60 s boundary.
2. Search params hook (`useLibraryQuery`): read and write `q`, `sort`, `status`,
   `library`; ignore unknown values.
3. Library page: search box, sort select, chips, result count, empty state, hide
   Continue listening and featured while filtering.
4. Keyboard shortcuts.
5. Type-check and lint, try at phone and desktop widths, deploy.

---

## Milestone 2: curated genres

### Why curated

No genre data exists, and every source is weak:

| Source | Reality |
|---|---|
| `genre` tag | Usually "Audiobook"/"Speech" or absent; LibriVox and most untagged downloads have nothing. |
| Google Books `categories` | Coarse, inconsistent: often just "Fiction", sometimes "Fiction / Fantasy / Epic". |
| Open Library `subjects` | Dozens of free-form subjects per work ("Magic", "Fiction, fantasy, general", "nyt:…"). |

Auto-mapping subjects to genres would be a keyword heuristic, wrong often enough to
need hand-fixing anyway. At ~60 books a guided manual pass takes minutes. So: a
small vocabulary the admin owns (expect 8–12 genres), assigned per book, with the
catalogues' raw categories shown as hints.

### Data model

- `Genres (Id, Name)`, `Name` unique (case-insensitive).
- `BookGenres (BookId, GenreId)`, composite key, cascade-deleted with the book or
  the genre.
- Migration `AddGenres`. The scanner never writes either table, same pattern as
  overrides and blurbs, so rescans keep them; the book row survives rescans, so the
  join stays valid.
- A join table, not a `text[]` column: renaming "Sci-Fi" to "Science fiction" is one
  row, and the vocabulary can be listed without scanning books.

### API

- `GET /api/books` and `GET /api/books/{id}` gain `genres: string[]` (names, sorted).
  Goes through `VisibleBooks` like everything else; add the field to the existing
  visibility rows rather than new routes.
- Admin (policy `Admin`):
  - `GET /api/admin/genres` (with book counts), `POST` (create), `PATCH /{id}`
    (rename), `DELETE /{id}` (removes it from every book; the page confirms with
    the count).
  - `PUT /api/admin/books/{id}/genres` with `{ genreIds }` **replaces the set**,
    like library grants: a repeat changes nothing, an unknown id is a 400 and
    changes nothing.
  - Suggestions: `POST /api/admin/books/{id}/genres/suggest` returns the raw
    catalogue categories/subjects for the book (same search as `BlurbFetcher`,
    title and author as listeners see them). Stores nothing; one book per request
    to stay clear of Cloudflare's 100 s. Optionally the blurb fetch returns them
    too, so a fresh fetch shows both.
- New admin routes get rows in the 403-for-non-admin tests; the tripwire covers the
  `genres` field on the book routes.

### Admin page

- **Genres** section (or a panel within Books): list with counts, add, rename,
  delete with an inline confirm.
- **Books** editor: a genre checklist per book, plus a "Suggest" button that shows
  the catalogue categories as plain hints beside it. Clicking a hint does not
  create a genre; the admin ticks an existing one or adds a new one deliberately,
  which keeps the vocabulary small.
- Books filter gains "No genre (n)", the backfill worklist.

### Library page

Genre chips beside the status chips, built from genres actually in use by visible
books (a listener never sees an empty genre or one only used in a library they
can't see). URL param `genre` (repeatable). Several genres: OR; with search and
other filters: AND.

### Steps

1. Migration, entities, `GenreAdmin` service (shared rules, like `AccountAdmin`),
   endpoints, integration tests (replace-the-set semantics, 400/404, rename,
   delete cascade, 403 for listeners, field visible to listeners).
2. Suggestions via the catalogues; `FakeBlurbFetcher` extended so tests stay
   offline.
3. Admin page: vocabulary, per-book checklist, Suggest, "No genre" filter.
4. Library page chips.
5. Deploy (one migration), then the backfill pass on the server.

### Later

- `admin` command support (`admin genre …`), if wanted.
- For self-hosters with large libraries, hand-tagging is a real cost: a bulk
  "apply suggestions where a category maps to an existing genre" step would earn
  its place then. Note in `self-hosting.md`, not tier 1.
- Android: render `genres` from the list and filter locally, same as the web.
