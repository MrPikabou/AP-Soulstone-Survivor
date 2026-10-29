#!/usr/bin/env python3
"""Packages world/soulstone_survivors into dist/soulstone_survivors.apworld.

The result is what players drop into their Archipelago install (Launcher ->
"Install APWorld", or lib/worlds/) to get the game in their world list.
"""
from pathlib import Path
import sys
import zipfile

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "world" / "soulstone_survivors"
OUTPUT_DIR = ROOT / "dist"
OUTPUT = OUTPUT_DIR / "soulstone_survivors.apworld"


def main() -> int:
    if not (SOURCE / "__init__.py").is_file():
        print(f"world package not found at {SOURCE}")
        return 1

    OUTPUT_DIR.mkdir(exist_ok=True)
    files = 0

    with zipfile.ZipFile(OUTPUT, "w", zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(SOURCE.rglob("*")):
            if not path.is_file():
                continue
            if "__pycache__" in path.parts or path.suffix == ".pyc":
                continue
            archive.write(path, path.relative_to(SOURCE.parent).as_posix())
            files += 1

    print(f"built {OUTPUT.relative_to(ROOT)} ({files} files, {OUTPUT.stat().st_size} bytes)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
