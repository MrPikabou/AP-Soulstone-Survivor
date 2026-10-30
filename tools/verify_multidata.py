#!/usr/bin/env python3
"""Checks a generated multiworld zip the same way archipelago.gg does on upload.

The hosting site (``WebHostLib.upload.process_multidata``) pops the checksum out
of each game's datapackage, re-hashes the rest and rejects the file when the two
disagree. Running this first tells you whether an upload will be accepted.

Usage, from the ap-soulstone folder:
    python tools/verify_multidata.py                  # every zip in output/
    python tools/verify_multidata.py path/to/seed.zip
"""
import glob
import os
import sys
import warnings
import zipfile

warnings.filterwarnings("ignore")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
AP_ROOT = os.path.join(ROOT, ".tools", "archipelago")

if not os.path.isdir(AP_ROOT):
    raise SystemExit(f"bundled Archipelago not found at {AP_ROOT}")

sys.path.insert(0, AP_ROOT)

import ModuleUpdate  # noqa: E402

ModuleUpdate.update = lambda *args, **kwargs: None
ModuleUpdate.install_pkg_resources = lambda *args, **kwargs: None

import MultiServer  # noqa: E402
from worlds.AutoWorld import data_package_checksum  # noqa: E402

GAME = "Soulstone Survivors"


def check(path: str) -> bool:
    with zipfile.ZipFile(path) as archive:
        multidata_names = [name for name in archive.namelist() if name.endswith(".archipelago")]
        if not multidata_names:
            print(f"{os.path.basename(path):42s} no multidata")
            return False
        multidata = MultiServer.Context.decompress(archive.read(multidata_names[0]))

    package = multidata.get("datapackage", {}).get(GAME)
    if package is None:
        print(f"{os.path.basename(path):42s} no {GAME} datapackage")
        return False

    package = dict(package)
    original = package.pop("checksum")
    calculated = data_package_checksum({key: value for key, value in sorted(package.items())})
    ok = original == calculated
    print(
        f"{os.path.basename(path):42s} {'OK  ' if ok else 'FAIL'} "
        f"{len(package['location_name_to_id']):4d} locations  "
        f"recorded:{original[:8]} calculated:{calculated[:8]}"
    )
    if not ok:
        print("    upload would be rejected - regenerate this seed with the current world")
    return ok


def main() -> int:
    paths = sys.argv[1:] or sorted(glob.glob(os.path.join(ROOT, "output", "*.zip")))
    if not paths:
        print("no seeds found - generate one first")
        return 1
    results = [check(path) for path in paths]
    return 0 if all(results) else 1


if __name__ == "__main__":
    raise SystemExit(main())
