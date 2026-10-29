#!/usr/bin/env python3
"""Inspect (and dry-run) the endgame checks against the live save.

    python tools/checks.py
        What the plugin reads right now: match records, ritual/sigil progress,
        best curse per map, and which endgame checks are satisfied.

    python tools/checks.py sim SurvivorsDesert 10
    python tools/checks.py sim SurvivorsDesert 47 WeaponPirate-04
        Pretend one Void Fields victory happened at that curse level (and weapon)
        and print the checks it would satisfy. Nothing is written to the save.

    python tools/checks.py inject SurvivorsDesert 10 [weaponId] [character]
        Pretend a run on that map was cleared at that curse level. The plugin
        holds it in memory, so the check is really sent to the Archipelago
        server, but nothing is written to the save and it disappears when the
        game restarts. Remove them with `python tools/checks.py inject clear`.

    python tools/checks.py matches [n]
        The last n match records the save actually holds (map, character,
        weapon, curse, victory, mode). Explains an empty curseByMap.

    python tools/checks.py sim-demo
        Runs the simulation for every ladder rung the current seed is tracking.

The game must be running with the plugin loaded and the allowed save slot picked.
"""
import json
import socket
import sys
import urllib.parse

BRIDGE_HOST = "127.0.0.1"
BRIDGE_PORT = int(__import__("os").environ.get("AP_BRIDGE_PORT", "47821"))


def bridge(request: str, timeout: float = 20.0) -> str:
    with socket.create_connection((BRIDGE_HOST, BRIDGE_PORT), timeout=timeout) as sock:
        sock.sendall((request + "\n").encode("utf-8"))
        sock.settimeout(timeout)
        chunks = []
        while True:
            chunk = sock.recv(65536)
            if not chunk:
                break
            chunks.append(chunk)
            if b"\n" in chunk:
                break
    reply = b"".join(chunks).decode("utf-8").strip()
    if reply.startswith("OK "):
        return urllib.parse.unquote(reply[3:])
    raise RuntimeError(reply)


def show(payload: dict) -> None:
    print(json.dumps(payload, indent=2, sort_keys=True))


def diagnose() -> int:
    payload = json.loads(bridge("CHECKS"))
    show(payload)
    if payload.get("ok"):
        print()
        print(f"{payload['checks']} endgame checks loaded; {payload['done']} already satisfied.")
        if not payload["curseByMap"]:
            print("No cleared Void Fields runs recorded yet (win, or reach a portal).")
    return 0 if payload.get("ok") else 1


def simulate(map_id: str, curse: int, weapon: str = "") -> int:
    request = f"SIM map={urllib.parse.quote(map_id)} curse={curse}"
    if weapon:
        request += " weapon=" + urllib.parse.quote(weapon)
    payload = json.loads(bridge(request))
    show(payload)
    if payload.get("ok"):
        print()
        print(f"{payload['newChecks']} check(s) would fire for a victory on "
              f"{payload['map']} at curse {payload['curse']}"
              + (f" with {payload['weapon']}" if payload.get("weapon") else ""))
    return 0 if payload.get("ok") else 1


def main() -> int:
    if len(sys.argv) < 2:
        return diagnose()

    action = sys.argv[1].lower()
    if action == "sim":
        if len(sys.argv) < 4:
            print("usage: python tools/checks.py sim <mapId> <curse> [weaponId]")
            return 1
        return simulate(sys.argv[2], int(sys.argv[3]), sys.argv[4] if len(sys.argv) > 4 else "")

    if action == "inject":
        if len(sys.argv) > 2 and sys.argv[2].lower() == "clear":
            print(bridge("INJECT clear=1"))
            return 0
        if len(sys.argv) < 4:
            print("usage: python tools/checks.py inject <mapId> <curse> [weaponId] [character]")
            return 1
        request = (
            "INJECT map=" + urllib.parse.quote(sys.argv[2])
            + " curse=" + sys.argv[3]
        )
        if len(sys.argv) > 4:
            request += " weapon=" + urllib.parse.quote(sys.argv[4])
        if len(sys.argv) > 5:
            request += " character=" + urllib.parse.quote(sys.argv[5])
        payload = json.loads(bridge(request))
        show(payload)
        if payload.get("ok"):
            print()
            print(f"Injected {payload['injected']} run(s); {payload['newChecks']} check(s) "
                  "should be sent to the server within a second.")
        return 0 if payload.get("ok") else 1

    if action == "matches":
        count = sys.argv[2] if len(sys.argv) > 2 else "12"
        payload = json.loads(bridge("MATCHES count=" + urllib.parse.quote(count)))
        if not payload.get("ok"):
            show(payload)
            return 1
        print(f"{payload['total']} records, {payload['clearedVoidFields']} cleared Void Fields run(s)")
        print()
        header = (
            f"{'map':<22}{'character':<14}{'curse':>6}{'portal':>8}  "
            f"{'result':<8}{'counts':<8}weapon"
        )
        print(header)
        print("-" * len(header))
        for match in payload["matches"]:
            result = "victory" if match["victory"] else "defeat"
            print(
                f"{match['map'] or '?':<22}{match['character'] or '-':<14}"
                f"{match['curse']:>6}{match.get('endless', 0):>8}  "
                f"{result:<8}{'yes' if match.get('cleared') else 'no':<8}"
                f"{match['weapon'] or '-'}"
            )
        print()
        print("A run counts when it was won, or when it reached a portal (the base map")
        print("is finished before a portal opens). Mode must be 0 (Void Fields).")
        return 0

    if action == "sim-demo":
        payload = json.loads(bridge("CHECKS"))
        maps = sorted(payload.get("curseByMap") or {})
        if not maps:
            print("no won runs recorded yet - pass a map id explicitly instead")
            return 1
        for map_id in maps:
            for curse in (10, 20, 30, 40, 50, 62):
                simulate(map_id, curse)
        return 0

    print(__doc__)
    return 1


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except ConnectionRefusedError:
        print(f"bridge not listening on {BRIDGE_HOST}:{BRIDGE_PORT} - is the game running?")
        raise SystemExit(1)
    except RuntimeError as error:
        print(f"plugin error: {error}")
        raise SystemExit(1)
