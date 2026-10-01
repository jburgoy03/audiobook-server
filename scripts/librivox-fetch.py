#!/usr/bin/env python3
"""
Downloads LibriVox recordings from the Internet Archive into a library folder.

    python3 scripts/librivox-fetch.py /mnt/media/librivox \
        "pride_prejudice_krs_librivox=Pride and Prejudice - Jane Austen" ...

Each argument after the destination is IDENTIFIER=FOLDER: the archive.org item and
the folder to put it in. Only two kinds of file are fetched:

- the 64 kbps mp3s (*_64kb.mp3), which are what LibriVox publishes as "the" mp3s;
- the item's cover, the largest jpg that isn't a thumbnail, saved as cover.jpg.

Downloading the whole item would also bring a PNG spectrogram per track. The scanner
picks the largest image in a book's folder as its cover, so those must stay out.

Safe to rerun: files already present at the expected size are skipped, and each
download goes to a .part file that is renamed only when complete. Standard library
only, so it runs on the server as is.
"""

import json
import os
import sys
import urllib.parse
import urllib.request

USER_AGENT = "audiobook-server librivox-fetch (+https://audiobooks.deanburgoyne.dev)"


def get(url):
    return urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": USER_AGENT}), timeout=60)


def download(identifier, name, target, expected_size):
    if os.path.exists(target) and expected_size and os.path.getsize(target) == expected_size:
        print(f"  have {os.path.basename(target)}")
        return
    url = f"https://archive.org/download/{identifier}/{urllib.parse.quote(name)}"
    part = target + ".part"
    with get(url) as response, open(part, "wb") as out:
        while chunk := response.read(1 << 16):
            out.write(chunk)
    if expected_size and os.path.getsize(part) != expected_size:
        raise RuntimeError(f"{name}: got {os.path.getsize(part)} bytes, expected {expected_size}")
    os.replace(part, target)
    print(f"  got  {os.path.basename(target)}")


def fetch(identifier, folder, root):
    with get(f"https://archive.org/metadata/{identifier}") as response:
        item = json.load(response)
    files = item.get("files") or []
    if not files:
        raise RuntimeError(f"{identifier}: no such item, or it has no files")

    mp3s = sorted((f for f in files if f["name"].endswith("_64kb.mp3")), key=lambda f: f["name"])
    if not mp3s:
        raise RuntimeError(f"{identifier}: no _64kb.mp3 files")

    covers = [
        f for f in files
        if f["name"].lower().endswith(".jpg")
        and not f["name"].lower().endswith("_thumb.jpg")
        and not f["name"].startswith("__ia")
    ]
    cover = max(covers, key=lambda f: int(f.get("size") or 0), default=None)

    destination = os.path.join(root, folder)
    os.makedirs(destination, exist_ok=True)
    title = (item.get("metadata") or {}).get("title", identifier)
    print(f"{title} -> {destination} ({len(mp3s)} files{', cover' if cover else ', no cover'})")

    for f in mp3s:
        download(identifier, f["name"], os.path.join(destination, f["name"]), int(f.get("size") or 0))
    if cover:
        download(identifier, cover["name"], os.path.join(destination, "cover.jpg"), int(cover.get("size") or 0))


def main(argv):
    if len(argv) < 3 or any("=" not in a for a in argv[2:]):
        print(__doc__)
        return 2
    root = argv[1]
    for pair in argv[2:]:
        identifier, folder = pair.split("=", 1)
        fetch(identifier.strip(), folder.strip(), root)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
