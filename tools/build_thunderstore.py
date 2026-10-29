#!/usr/bin/env python3
"""Builds the plugin and packs a Thunderstore-ready zip.

Thunderstore wants a zip whose *root* holds manifest.json, README.md and a
256x256 icon.png, with the mod payload beside them (BepInEx/plugins/<dll>).
The version in manifest.json is taken from mod/Plugin.cs so the package can
never disagree with the DLL it carries.

Usage:
    python tools/build_thunderstore.py            # build + pack
    python tools/build_thunderstore.py --no-build # package the last build
"""
import json
import os
import re
import shutil
import struct
import subprocess
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PACKAGE_DIR = os.path.join(ROOT, "thunderstore")
STAGE = os.path.join(ROOT, "build", "thunderstore")
DIST = os.path.join(ROOT, "dist")
PLUGIN_CS = os.path.join(ROOT, "mod", "Plugin.cs")
BUILT_DLL = os.path.join(ROOT, "mod", "bin", "Release", "net6.0", "SoulstoneAP.dll")

REQUIRED_ROOT = ("manifest.json", "README.md", "icon.png")
ICON_SIZE = 256
DESCRIPTION_LIMIT = 250
NAME_PATTERN = re.compile(r"^[A-Za-z0-9_]{1,128}$")
DEPENDENCY_PATTERN = re.compile(r"^[A-Za-z0-9_]+-[A-Za-z0-9_]+-(\d+\.\d+\.\d+)$")


GAME_CANDIDATES = (
    r"C:\Program Files (x86)\Steam\steamapps\common\Soulstone Survivors",
    r"C:\Program Files\Steam\steamapps\common\Soulstone Survivors",
    r"D:\Steam\steamapps\common\Soulstone Survivors",
    r"D:\SteamLibrary\steamapps\common\Soulstone Survivors",
)


def game_dir():
    """Same lookup env.sh does, so this works without sourcing it first."""
    for key in ("SOULSTONE_DIR", "GAME_DIR"):
        value = os.environ.get(key)
        if value and os.path.isdir(value):
            return value
    for candidate in GAME_CANDIDATES:
        if os.path.isdir(candidate):
            return candidate
    return None


def plugin_version() -> str:
    source = open(PLUGIN_CS, encoding="utf-8").read()
    match = re.search(r'public const string Version = "([^"]+)"', source)
    if not match:
        raise SystemExit("could not find Plugin.Version in mod/Plugin.cs")
    return match.group(1)


def build() -> None:
    dotnet = os.environ.get("DOTNET") or os.path.join(ROOT, ".tools", "dotnet", "dotnet.exe")
    if not os.path.isfile(dotnet):
        raise SystemExit(f"dotnet not found at {dotnet}")
    env = dict(os.environ)
    tools = os.path.join(ROOT, ".tools")
    env.update({
        "TEMP": os.path.join(tools, "tmp"),
        "TMP": os.path.join(tools, "tmp"),
        "APPDATA": os.path.join(tools, "appdata", "roaming"),
        "LOCALAPPDATA": os.path.join(tools, "appdata", "local"),
        "DOTNET_CLI_HOME": os.path.join(tools, "home"),
        "NUGET_PACKAGES": os.path.join(tools, "nuget"),
        "DOTNET_NOLOGO": "1",
        "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
    })
    for path in (env["TEMP"], env["APPDATA"], env["LOCALAPPDATA"], env["DOTNET_CLI_HOME"], env["NUGET_PACKAGES"]):
        os.makedirs(path, exist_ok=True)
    command = [dotnet, "build", os.path.join(ROOT, "mod", "SoulstoneAP.csproj"), "-c", "Release", "-v", "q", "--nologo"]
    # Interop assemblies are read from the game install; tell MSBuild where it is.
    found = game_dir()
    if found:
        command.append(f"-p:GameDir={found}")
    else:
        print("warning: game install not found - set SOULSTONE_DIR if the build fails", file=sys.stderr)
    subprocess.run(command, check=True, env=env)


def read_icon_size(path: str) -> tuple:
    header = open(path, "rb").read(24)
    if header[:8] != b"\x89PNG\r\n\x1a\n":
        raise SystemExit(f"{path} is not a PNG")
    return struct.unpack(">II", header[16:24])


def stage(version: str) -> dict:
    manifest = json.load(open(os.path.join(PACKAGE_DIR, "manifest.json"), encoding="utf-8"))

    name = manifest.get("name", "")
    if not NAME_PATTERN.match(name):
        raise SystemExit(f"manifest name '{name}' must be letters, digits and underscores")
    if len(manifest.get("description", "")) > DESCRIPTION_LIMIT:
        raise SystemExit(f"manifest description is over {DESCRIPTION_LIMIT} characters")
    for dependency in manifest.get("dependencies", []):
        if not DEPENDENCY_PATTERN.match(dependency):
            raise SystemExit(f"dependency '{dependency}' is not Team-Package-Version")
    if not manifest.get("dependencies"):
        raise SystemExit("a BepInEx IL2CPP plugin must depend on BepInEx-BepInExPack_IL2CPP")

    manifest["version_number"] = version

    if shutil.os.path.isdir(STAGE):
        shutil.rmtree(STAGE)
    plugins = os.path.join(STAGE, "BepInEx", "plugins")
    os.makedirs(plugins)

    if not os.path.isfile(BUILT_DLL):
        raise SystemExit(f"built plugin missing: {BUILT_DLL} (run without --no-build)")
    shutil.copy2(BUILT_DLL, os.path.join(plugins, "SoulstoneAP.dll"))

    for name_in_zip in ("README.md", "CHANGELOG.md", "icon.png"):
        source = os.path.join(PACKAGE_DIR, name_in_zip)
        if os.path.isfile(source):
            shutil.copy2(source, os.path.join(STAGE, name_in_zip))
        elif name_in_zip in REQUIRED_ROOT:
            raise SystemExit(f"thunderstore/{name_in_zip} is required")

    width, height = read_icon_size(os.path.join(STAGE, "icon.png"))
    if (width, height) != (ICON_SIZE, ICON_SIZE):
        raise SystemExit(f"icon must be {ICON_SIZE}x{ICON_SIZE}, got {width}x{height}")

    with open(os.path.join(STAGE, "manifest.json"), "w", encoding="utf-8", newline="\n") as handle:
        json.dump(manifest, handle, indent=2)
        handle.write("\n")

    return manifest


def pack(manifest: dict, version: str) -> str:
    os.makedirs(DIST, exist_ok=True)
    archive = os.path.join(DIST, f"{manifest['name']}-{version}.zip")
    if os.path.exists(archive):
        os.remove(archive)

    entries = []
    for folder, _, files in os.walk(STAGE):
        for filename in sorted(files):
            absolute = os.path.join(folder, filename)
            relative = os.path.relpath(absolute, STAGE).replace(os.sep, "/")
            entries.append((absolute, relative))

    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as bundle:
        for absolute, relative in sorted(entries, key=lambda pair: pair[1]):
            bundle.write(absolute, relative)

    return archive


def verify(archive: str) -> None:
    with zipfile.ZipFile(archive) as bundle:
        names = bundle.namelist()
    missing = [name for name in REQUIRED_ROOT if name not in names]
    if missing:
        raise SystemExit("package is missing at the zip root: " + ", ".join(missing))
    dll = [name for name in names if name.startswith("BepInEx/plugins/") and name.endswith(".dll")]
    if not dll:
        raise SystemExit("package has no DLL under BepInEx/plugins/")
    if any(name.startswith("/") or "\\" in name for name in names):
        raise SystemExit("package has absolute or backslash paths")
    print(f"{os.path.basename(archive)} ({os.path.getsize(archive)} bytes)")
    for name in names:
        print("  " + name)


def main() -> int:
    version = plugin_version()
    if "--no-build" not in sys.argv:
        build()
    manifest = stage(version)
    archive = pack(manifest, version)
    verify(archive)
    print(f"\nUpload {archive} at https://thunderstore.io/ (team page -> Upload package)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
