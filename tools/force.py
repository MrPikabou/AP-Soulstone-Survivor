#!/usr/bin/env python3
"""Force a trap, knockback or death link without waiting for the multiworld.

Local (needs the game running with a run active, uses the mod's loopback bridge):
    python tools/force.py effect "Elite Ambush"
    python tools/force.py effect "Cursed Power"
    python tools/force.py effect Knockback

Through the Archipelago server (needs a seed with the matching link enabled):
    python tools/force.py deathlink
    python tools/force.py knockbacklink

Deliver a real trap item through the server console:
    python tools/force.py item "Trap: Swarm"
"""
import asyncio
import json
import os
import socket
import sys
import time
import urllib.parse
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
COMMANDS_FILE = os.path.join(ROOT, "server_cmds.txt")

DEFAULT_ADDRESS = os.environ.get("AP_ADDRESS", "localhost:38281")
DEFAULT_SLOT = os.environ.get("AP_SLOT", "Survivor")
BRIDGE_PORT = int(os.environ.get("AP_BRIDGE_PORT", "47821"))


def bridge(request: str, timeout: float = 20.0) -> str:
    with socket.create_connection(("127.0.0.1", BRIDGE_PORT), timeout=timeout) as sock:
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
        return b"".join(chunks).decode("utf-8").strip()


def force_effect(name: str) -> int:
    try:
        reply = bridge("EFFECT name=" + urllib.parse.quote(name))
    except OSError as error:
        print(f"bridge unreachable on 127.0.0.1:{BRIDGE_PORT} ({error})")
        return 1
    print(reply)
    if "unknown command EFFECT" in reply:
        print("hint: the running game still has an older plugin loaded - restart it to pick up the new DLL")
    return 0 if reply.startswith("OK") else 1


async def force_bounce(tag: str, address: str, slot: str) -> int:
    """Joins as `slot` and bounces, so use a spare test slot, not the live one."""
    import websockets

    async with websockets.connect(f"ws://{address}") as socket:
        await socket.recv()  # RoomInfo
        await socket.send(json.dumps([{
            "cmd": "Connect",
            "game": "Soulstone Survivors",
            "name": slot,
            "uuid": str(uuid.uuid4()),
            "version": {"major": 0, "minor": 6, "build": 8, "class": "Version"},
            "items_handling": 0,
            "tags": ["DeathLink", "KnockbackLink"],
            "password": "",
        }]))

        while True:
            message = json.loads(await asyncio.wait_for(socket.recv(), timeout=15))
            if isinstance(message, dict):
                message = [message]
            for packet in message:
                if packet.get("cmd") in ("Connected", "ConnectionRefused"):
                    if packet.get("cmd") == "ConnectionRefused":
                        print("refused:", packet.get("errors"))
                        return 1
                    await socket.send(json.dumps([{
                        "cmd": "Bounce",
                        "tags": [tag],
                        "data": {
                            "time": int(time.time()),
                            "source": slot,
                            "cause": "forced by tools/force.py",
                        },
                    }]))
                    print(f"sent {tag} bounce to {address}")
                    await asyncio.sleep(1.0)
                    return 0


def force_item(name: str, slot: str) -> int:
    if not os.path.exists(COMMANDS_FILE):
        print(f"{COMMANDS_FILE} not found - start the server with tools/server_ctl.py or play.sh")
        return 1
    with open(COMMANDS_FILE, "a", encoding="utf-8") as handle:
        handle.write(f"/send {slot} {name}\n")
    print(f"queued: /send {slot} {name}")
    return 0


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 1

    action = sys.argv[1].lower()
    arguments = sys.argv[2:]

    if action == "effect":
        if not arguments:
            print('usage: python tools/force.py effect "Elite Ambush"')
            return 1
        return force_effect(" ".join(arguments))

    if action in ("deathlink", "knockbacklink"):
        address = arguments[0] if arguments else DEFAULT_ADDRESS
        slot = arguments[1] if len(arguments) > 1 else DEFAULT_SLOT
        tag = "DeathLink" if action == "deathlink" else "KnockbackLink"
        return asyncio.run(force_bounce(tag, address, slot))

    if action == "item":
        if not arguments:
            print('usage: python tools/force.py item "Trap: Swarm"')
            return 1
        slot = os.environ.get("AP_SLOT", DEFAULT_SLOT)
        return force_item(" ".join(arguments), slot)

    print(f"unknown action '{action}'")
    print(__doc__)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
