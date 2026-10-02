#!/usr/bin/env python3
"""
Fetches cover art from Open Library into book folders, as cover.jpg.

    python3 scripts/fetch-covers.py /mnt/media/audiobooks --dry-run \
        "Kafka on the Shore - Haruki Murakami" "11.22.63 - Stephen King"

    # A folder whose name isn't "Title - Author": say what to search for.
    python3 scripts/fetch-covers.py /mnt/media/audiobooks \
        "The Lord of the Rings Complete Audiobook Collection=The Lord of the Rings|J.R.R. Tolkien"

Each argument after the library root is a folder name, optionally followed by
=TITLE|AUTHOR to search for instead of what the name says. Folders are named
"Title - Author" or "Title by Author" in this library (see docs/deploy.md).

Folders are listed explicitly on purpose: a book with good embedded art doesn't need
this, and the scanner would prefer a catalogue image whenever it's larger.

--dry-run prints the match (title, first published, cover URL) without saving: check
it, since a translated book can match a foreign edition. A folder that already has
cover.jpg is skipped unless --force. Other images in the folder aren't touched; if
one is junk (a screenshot), rename it to something that isn't .jpg/.png, or it may
still win by size.

Then run a plain scan of the library: an image newer than the book's last scan
makes the scanner look at that book again and choose its cover.

Covers come from covers.openlibrary.org at the large size (typically 300-500 px
wide). Standard library only, so it runs on the server as is.
"""

import json
import os
import re
import sys
import urllib.error
import urllib.parse
import urllib.request

USER_AGENT = "audiobook-server fetch-covers (+https://audiobooks.deanburgoyne.dev)"
MIN_BYTES = 5_000  # anything smaller is a placeholder, not a cover


def get(url):
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    return urllib.request.urlopen(request, timeout=30)


def parse_folder(name):
    """'Title - Author', 'Title by Author', with an optional trailing ' m4b'."""
    name = re.sub(r"\s+m4b$", "", name, flags=re.IGNORECASE)
    match = re.match(r"^(?P<title>.+?)\s+by\s+(?P<author>.+)$", name, re.IGNORECASE) or \
        re.match(r"^(?P<title>.+?)\s+-\s+(?P<author>.+)$", name)
    return (match["title"], match["author"]) if match else (name, None)


def search(title, author):
    """Open Library's best match that has a cover, or None."""
    params = {"title": title, "fields": "title,author_name,first_publish_year,cover_i", "limit": "10"}
    if author:
        params["author"] = author
    with get("https://openlibrary.org/search.json?" + urllib.parse.urlencode(params)) as response:
        docs = json.load(response).get("docs", [])
    return next((d for d in docs if d.get("cover_i")), None)


def main(argv):
    flags = {a for a in argv if a.startswith("--")}
    args = [a for a in argv if not a.startswith("--")]
    unknown = flags - {"--dry-run", "--force"}
    if len(args) < 2 or unknown:
        print(__doc__)
        return 2

    root, entries = args[0], args[1:]
    dry_run, force = "--dry-run" in flags, "--force" in flags
    problems = 0

    for entry in entries:
        folder, _, override = entry.partition("=")
        path = os.path.join(root, folder)
        if not os.path.isdir(path):
            print(f"!! {folder}: no such folder")
            problems += 1
            continue

        target = os.path.join(path, "cover.jpg")
        if os.path.exists(target) and not force:
            print(f"-- {folder}: already has cover.jpg (--force to replace)")
            continue

        title, author = override.split("|", 1) if "|" in override else parse_folder(folder)
        try:
            doc = search(title.strip(), author.strip() if author else None)
        except (urllib.error.URLError, TimeoutError, ValueError) as e:
            print(f"!! {folder}: search failed ({e})")
            problems += 1
            continue

        if doc is None:
            print(f"!! {folder}: no match with a cover for '{title}' / '{author}'")
            problems += 1
            continue

        url = f"https://covers.openlibrary.org/b/id/{doc['cover_i']}-L.jpg?default=false"
        found = f"{doc.get('title')} ({', '.join(doc.get('author_name', [])[:2])}, {doc.get('first_publish_year', '?')})"

        if dry_run:
            print(f"?? {folder}\n   -> {found}\n   {url}")
            continue

        try:
            with get(url) as response:
                data = response.read()
        except (urllib.error.URLError, TimeoutError) as e:
            print(f"!! {folder}: download failed ({e})")
            problems += 1
            continue

        if len(data) < MIN_BYTES:
            print(f"!! {folder}: cover too small ({len(data)} bytes), not saved")
            problems += 1
            continue

        partial = target + ".part"
        with open(partial, "wb") as f:
            f.write(data)
        os.replace(partial, target)
        print(f"ok {folder}\n   -> {found}, {len(data) // 1024} KB")

    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
