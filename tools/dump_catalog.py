#!/usr/bin/env python3
"""Re-dumps the game catalog from the running game into the world package.

Run this after a game update so item/location ids match the installed build:
    python tools/dump_catalog.py
The game must be running (any profile is fine; the catalog does not need one).
"""
import json
import os
import sys
import urllib.parse

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from bridge_test import send  # noqa: E402

TARGET = os.path.join(os.path.dirname(HERE), "world", "soulstone_survivors", "catalog.json")


def main() -> int:
    reply = send("CATALOG", timeout=30.0)
    if not reply.startswith("OK "):
        print(f"Plugin error: {reply}")
        return 1

    data = json.loads(urllib.parse.unquote(reply[3:]))
    counts = {key: len(value) for key, value in data.items()}
    if not any(counts.values()):
        print("Catalog is empty - is the game still booting?")
        return 1

    with open(TARGET, "w", encoding="utf-8") as handle:
        json.dump(data, handle, indent=1)
    print(f"Wrote {TARGET}: {counts}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
