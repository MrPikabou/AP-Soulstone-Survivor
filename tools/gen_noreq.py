#!/usr/bin/env python3
"""Runs Archipelago's Generate.py without its interactive requirements check.

The local test venv has no kivy/mpyq (generation does not need them), and
ModuleUpdate prompts for input when they are missing, which hangs a scripted run.

Usage, from anywhere:
    python tools/gen_noreq.py --player_files_path players --outputpath out
"""
import os
import runpy
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
AP_ROOT = os.path.join(ROOT, ".tools", "archipelago")

if not os.path.isdir(AP_ROOT):
    raise SystemExit(f"bundled Archipelago not found at {AP_ROOT}")

START = os.getcwd()
os.chdir(AP_ROOT)
sys.path.insert(0, AP_ROOT)

# The working directory changes, so any path the caller passed must be absolute.
for index, argument in enumerate(sys.argv):
    if argument in ("--player_files_path", "--outputpath") and index + 1 < len(sys.argv):
        sys.argv[index + 1] = os.path.abspath(os.path.join(START, sys.argv[index + 1]))

import ModuleUpdate  # noqa: E402

ModuleUpdate.update = lambda *args, **kwargs: None
ModuleUpdate.install_pkg_resources = lambda *args, **kwargs: None

sys.argv = ["Generate.py"] + sys.argv[1:]
runpy.run_path("Generate.py", run_name="__main__")
