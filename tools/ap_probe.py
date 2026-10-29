#!/usr/bin/env python3
"""Connects to an Archipelago server exactly like the in-game C# client does.

Used to verify handshake payloads and slot_data shape without launching the game:
    python tools/ap_probe.py localhost:38281 Survivor
"""
import asyncio
import json
import sys
import uuid

import websockets


async def main(address: str, slot: str, password: str = "") -> int:
    url = f"ws://{address}"
    async with websockets.connect(url) as socket:
        room = json.loads(await socket.recv())
        if isinstance(room, list):
            room = room[0]
        print("RoomInfo:", room.get("cmd"), "games:", len(room.get("games", [])) if "games" in room else "-")

        # Same body the plugin sends from SendConnect().
        await socket.send(json.dumps([{
            "cmd": "Connect",
            "game": "Soulstone Survivors",
            "name": slot,
            "uuid": str(uuid.uuid4()),
            "version": {"major": 0, "minor": 6, "build": 8, "class": "Version"},
            "items_handling": 0b111,
            "tags": [],
            "password": password,
        }]))

        while True:
            raw = await asyncio.wait_for(socket.recv(), timeout=15)
            messages = json.loads(raw)
            if isinstance(messages, dict):
                messages = [messages]
            for message in messages:
                cmd = message.get("cmd")
                if cmd == "Connected":
                    slot_data = message.get("slot_data", {})
                    print("Connected: slot", message.get("slot"), "players", len(message.get("slot_info", {})))
                    print("slot_data keys:", list(slot_data.keys()))
                    items = slot_data.get("items", {})
                    print("items:", len(items), "sample:", list(items.items())[:2])
                    print("maps:", slot_data.get("maps"))
                    print("achievements:", len(slot_data.get("achievements", {})))
                    print("raw slot_data bytes:", len(json.dumps(slot_data)))
                    return 0
                if cmd == "ConnectionRefused":
                    print("Refused:", message.get("errors"))
                    return 1
                print("other:", cmd)


if __name__ == "__main__":
    address = sys.argv[1] if len(sys.argv) > 1 else "localhost:38281"
    slot = sys.argv[2] if len(sys.argv) > 2 else "Survivor"
    raise SystemExit(asyncio.run(main(address, slot)))
