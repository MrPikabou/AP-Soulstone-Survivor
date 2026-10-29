"""Archipelago client for Soulstone Survivors.

Bridges the Archipelago server and the in-game BepInEx plugin:

  Archipelago  <-- websocket -->  this client  <-- loopback TCP -->  SoulstoneAP.dll

Run from the Archipelago folder:
    python -m worlds.soulstone_survivors.soulstone_client --connect localhost:38281
"""
from __future__ import annotations

import asyncio
import socket
import sys
import typing
import urllib.parse

import ModuleUpdate

ModuleUpdate.update()

import Utils

if __name__ == "__main__":
    Utils.init_logging("SoulstoneClient", exception_logger="Client")

from CommonClient import (
    ClientCommandProcessor,
    CommonContext,
    get_base_parser,
    gui_enabled,
    logger,
    server_loop,
)
from NetUtils import ClientStatus

import CommonClient as _common_client

GAME_NAME = "Soulstone Survivors"
BRIDGE_HOST = "127.0.0.1"
BRIDGE_PORT = 47821
POLL_SECONDS = 1.0

MAP_LOCATION_PREFIX = "Clear "
ACHIEVEMENT_PREFIX = "Achievement: "
RUNE_PREFIX = "Rune: "
MAP_ITEM_PREFIX = "Map: "
CHARACTER_ITEM_PREFIX = "Character: "
WEAPON_ITEM_PREFIX = "Weapon: "
FILLER_PREFIX = "Soulstones +"

# Slot data names the bridge command for items whose in-game name carries no
# prefix of its own (filler soulstones, the two endgame modes).
KIND_TO_PREFIX = {
    "char": "Char:",
    "weapon": "Weapon:",
    "rune": "Rune:",
    "mode": "Mode:",
    "map": "Map:",
    "curse": "Curse:",
    "weaponcap": "WeaponCap:",
    "souls": "Souls:",
    "trap": "Trap:",
}


# --------------------------------------------------------------------- bridge


class BridgeError(RuntimeError):
    pass


def bridge_request(request: str, timeout: float = 15.0) -> str:
    """Sends one command to the plugin and returns the decoded payload."""
    try:
        with socket.create_connection((BRIDGE_HOST, BRIDGE_PORT), timeout=timeout) as sock:
            sock.sendall((request + "\n").encode("utf-8"))
            sock.settimeout(timeout)
            buffer = b""
            while b"\n" not in buffer:
                chunk = sock.recv(65536)
                if not chunk:
                    break
                buffer += chunk
    except OSError as error:
        raise BridgeError(f"plugin not reachable on {BRIDGE_HOST}:{BRIDGE_PORT} ({error})") from error

    line = buffer.decode("utf-8", errors="replace").strip()
    if line.startswith("OK "):
        return urllib.parse.unquote(line[3:])
    if line.startswith("ERR "):
        raise BridgeError(line[4:])
    raise BridgeError(line or "empty response from plugin")


def bridge_state() -> dict:
    import json

    return json.loads(bridge_request("STATE"))


def bridge_grant(item: str) -> str:
    return bridge_request("GRANT item=" + urllib.parse.quote(item, safe=""))


def bridge_endgame(spec: dict) -> str:
    """Hands the seed's endgame check table to the plugin."""
    import json

    payload = json.dumps(spec, separators=(",", ":"))
    return bridge_request("ENDGAME spec=" + urllib.parse.quote(payload, safe=""))


# The bundled CommonClient does not keep slot_data on the context, but the
# endgame check table travels there. Wrap the packet handler so this client can
# see it; if a future Archipelago keeps it itself, the wrapper is a no-op.
_base_process_server_cmd = getattr(_common_client, "process_server_cmd", None)


async def _process_server_cmd_with_slot_data(ctx, args) -> None:
    try:
        if args.get("cmd") == "Connected" and isinstance(args.get("slot_data"), dict):
            ctx.slot_data = args["slot_data"]
    except Exception:  # noqa: BLE001 - never break the client over this
        pass
    await _base_process_server_cmd(ctx, args)


if _base_process_server_cmd is not None:
    _common_client.process_server_cmd = _process_server_cmd_with_slot_data


def item_to_bridge_command(item_name: str) -> typing.Optional[str]:
    if item_name.startswith(CHARACTER_ITEM_PREFIX):
        return "Char:" + item_name[len(CHARACTER_ITEM_PREFIX):]
    if item_name.startswith(WEAPON_ITEM_PREFIX):
        return "Weapon:" + item_name[len(WEAPON_ITEM_PREFIX):]
    if item_name.startswith(RUNE_PREFIX):
        return "Rune:" + item_name[len(RUNE_PREFIX):]
    if item_name.startswith(MAP_ITEM_PREFIX):
        return "Map:" + item_name[len(MAP_ITEM_PREFIX):]
    if item_name.startswith(FILLER_PREFIX):
        return "Souls:" + item_name[len(FILLER_PREFIX):]
    return None


def granted_maps(ctx) -> typing.Optional[set[str]]:
    """Maps this slot received, or None when maps are not items in this seed.

    With `map_unlocks` on, the game can still open a map on its own (its buttons
    read achievement state), so a Clear check is only reported for a map whose
    item actually arrived.
    """
    slot_data = getattr(ctx, "slot_data", None) or {}
    if slot_data.get("maps_default") is not False:
        return None

    from worlds.soulstone_survivors.Items import item_name_to_id

    id_to_item = {item_id: name for name, item_id in item_name_to_id.items()}
    maps: set[str] = set()
    for network_item in ctx.items_received:
        name = id_to_item.get(network_item.item)
        if name and name.startswith(MAP_ITEM_PREFIX):
            maps.add(name[len(MAP_ITEM_PREFIX):])
    return maps


def state_to_locations(state: dict, allowed_maps: typing.Optional[set[str]] = None) -> set[str]:
    """Turns a plugin state snapshot into Archipelago location names."""
    from worlds.soulstone_survivors.Locations import location_name_to_id

    found: set[str] = set()

    for map_entry in state.get("maps", []):
        if (map_entry.get("normal") or 0) >= 0.999:
            map_id = map_entry.get("id", "")
            if allowed_maps is not None and map_id not in allowed_maps:
                continue
            name = MAP_LOCATION_PREFIX + map_id
            if name in location_name_to_id:
                found.add(name)

    for achievement in state.get("achievements", []):
        if achievement.get("done") or (achievement.get("progress") or 0) >= 0.999:
            name = ACHIEVEMENT_PREFIX + achievement.get("id", "")
            if name in location_name_to_id:
                found.add(name)

    # Runes are skill tree nodes; the plugin reports the ones the save has.
    for rune_id in state.get("runes") or []:
        name = RUNE_PREFIX + str(rune_id)
        if name in location_name_to_id:
            found.add(name)

    return found


def state_to_endgame_ids(state: dict) -> list[int]:
    """Endgame check ids the plugin already reports as satisfied.

    Curse ladder, Unholy Cathedral and Titan Hunt locations get per-seed ids, so
    they never appear in `location_name_to_id`; the plugin ships the ids in the
    seed's slot data and reports the ones the save satisfies.
    """
    ids: list[int] = []
    for value in state.get("endgame") or []:
        try:
            ids.append(int(value))
        except (TypeError, ValueError):
            continue
    return ids


# --------------------------------------------------------------------- client


class SoulstoneCommandProcessor(ClientCommandProcessor):
    def _cmd_bridge(self) -> None:
        """Show the state the game plugin is reporting."""
        try:
            state = bridge_state()
        except BridgeError as error:
            self.output(f"Bridge error: {error}")
            return
        if not state.get("ok"):
            self.output(f"Plugin reports: {state.get('error')}")
            return
        self.output(
            f"Profile {state.get('profile')} ({state.get('profileName')}): "
            f"{len(state.get('characters', []))} characters, "
            f"{len(state.get('weapons', []))} weapons, "
            f"{len(state.get('maps', []))} maps tracked, "
            f"{len(state.get('endgame') or [])} endgame checks done"
        )

    def _cmd_resync(self) -> None:
        """Re-apply every received item to the game."""
        self.ctx.applied_items = 0
        self.output("Re-applying all received items.")

    def _cmd_check(self, *name_parts: str) -> None:
        """Manually send a check, e.g. /check Clear SurvivorsCavern"""
        import asyncio

        from worlds.soulstone_survivors.Locations import location_name_to_id

        name = " ".join(name_parts)
        if name not in location_name_to_id:
            self.output(f"Unknown location '{name}'.")
            return
        asyncio.create_task(
            self.ctx.send_msgs(
                [{"cmd": "LocationChecks", "locations": [location_name_to_id[name]]}]
            )
        )
        self.output(f"Sent check for '{name}'.")


class SoulstoneContext(CommonContext):
    command_processor = SoulstoneCommandProcessor
    game = GAME_NAME
    items_handling = 0b111  # full remote

    def __init__(self, server_address: typing.Optional[str], password: typing.Optional[str]) -> None:
        super().__init__(server_address, password)
        self.applied_items: int = 0
        self.known_locations: set[str] = set()
        self.known_endgame: set[int] = set()
        self.slot_data: dict = {}
        self.endgame_spec_sent = False
        self.bridge_warned = False
        self.goal_sent = False

    async def server_auth(self, password_requested: bool = False) -> None:
        if password_requested and not self.password:
            await super().server_auth(password_requested)
        await self.get_username()
        await self.send_connect()


async def poll_game(ctx: SoulstoneContext) -> typing.Optional[dict]:
    try:
        return await asyncio.get_running_loop().run_in_executor(None, bridge_state)
    except BridgeError as error:
        if not ctx.bridge_warned:
            logger.info(f"Waiting for the game: {error}")
            ctx.bridge_warned = True
        return None


async def apply_items(ctx: SoulstoneContext) -> None:
    # NetworkItem.item is an id in the receiving slot's game namespace, so the
    # world's own table is the authoritative mapping (no data package needed).
    from worlds.soulstone_survivors.Items import item_name_to_id

    id_to_item = {item_id: name for name, item_id in item_name_to_id.items()}

    while ctx.applied_items < len(ctx.items_received):
        network_item = ctx.items_received[ctx.applied_items]
        name = id_to_item.get(network_item.item)
        if name is None:
            logger.warning(f"Unknown item id {network_item.item} for this game; skipping.")
            ctx.applied_items += 1
            continue
        command = item_to_bridge_command(name)
        if command is None:
            # Filler is just called "Soulstones" and the modes are plain names;
            # slot data carries the bridge command for both.
            spec = (ctx.slot_data.get("items") or {}).get(str(network_item.item))
            if isinstance(spec, dict):
                prefix = KIND_TO_PREFIX.get(spec.get("kind"))
                if prefix is not None:
                    command = prefix + str(spec.get("value", ""))
        if command is None:
            logger.warning(f"No bridge command for '{name}'; skipping.")
            ctx.applied_items += 1
            continue

        try:
            reply = await asyncio.get_running_loop().run_in_executor(None, bridge_grant, command)
        except BridgeError as error:
            # Usually "no profile loaded yet" or the wrong save slot is active.
            logger.info(f"Holding '{name}' until the game is ready: {error}")
            return

        logger.info(f"Granted '{name}': {reply}")
        ctx.applied_items += 1


async def game_watcher(ctx: SoulstoneContext) -> None:
    while not ctx.exit_event.is_set():
        if not ctx.server or not ctx.slot:
            await asyncio.sleep(POLL_SECONDS)
            continue

        try:
            await apply_items(ctx)
        except Exception as error:  # noqa: BLE001 - never let the watcher die
            logger.exception(f"Item application failed: {error}")

        endgame_spec = (ctx.slot_data or {}).get("endgame")
        if not ctx.endgame_spec_sent and isinstance(endgame_spec, dict) and endgame_spec:
            try:
                reply = await asyncio.get_running_loop().run_in_executor(
                    None, bridge_endgame, endgame_spec
                )
            except BridgeError as error:
                logger.info(f"Waiting for the game to take the endgame checks: {error}")
            else:
                logger.info(f"Endgame checks handed to the plugin: {reply}")
                ctx.endgame_spec_sent = True

        state = await poll_game(ctx)
        if state is not None:
            ctx.bridge_warned = False
            if state.get("ok"):
                locations = state_to_locations(state, granted_maps(ctx))
                new_locations = {name for name in locations if name not in ctx.known_locations}
                if new_locations:
                    from worlds.soulstone_survivors.Locations import location_name_to_id

                    ids = [location_name_to_id[name] for name in new_locations]
                    await ctx.send_msgs([{"cmd": "LocationChecks", "locations": ids}])
                    logger.info(f"Checked {len(ids)} location(s): {', '.join(sorted(new_locations))}")
                ctx.known_locations = locations

                endgame_ids = state_to_endgame_ids(state)
                new_endgame = [i for i in endgame_ids if i not in ctx.known_endgame]
                if new_endgame:
                    await ctx.send_msgs(
                        [{"cmd": "LocationChecks", "locations": new_endgame}]
                    )
                    logger.info(f"Checked {len(new_endgame)} endgame location(s)")
                ctx.known_endgame = set(endgame_ids)

                from worlds.soulstone_survivors.Locations import MAP_LOCATIONS

                if not ctx.goal_sent and MAP_LOCATIONS and all(m in locations for m in MAP_LOCATIONS):
                    await ctx.send_msgs([{"cmd": "StatusUpdate", "status": ClientStatus.CLIENT_GOAL}])
                    logger.info("All maps cleared - goal sent.")
                    ctx.goal_sent = True

        await asyncio.sleep(POLL_SECONDS)


async def main(args) -> None:
    ctx = SoulstoneContext(args.connect, args.password)
    # AP 0.6's base parser has no --name, so the slot is passed with --slot.
    ctx.auth = getattr(args, "slot", None) or getattr(args, "name", None)
    ctx.server_task = asyncio.create_task(server_loop(ctx), name="ServerLoop")
    if gui_enabled:
        ctx.run_gui()
    ctx.run_cli()

    watcher = asyncio.create_task(game_watcher(ctx), name="SoulstoneWatcher")

    await ctx.exit_event.wait()
    ctx.server_address = None

    await watcher
    await ctx.shutdown()


if __name__ == "__main__":
    import colorama

    parser = get_base_parser(description="Soulstone Survivors client, text interfacing.")
    parser.add_argument("--slot", default=None, help="Slot name to connect as.")
    parsed_args, _ = parser.parse_known_args()
    colorama.init()
    asyncio.run(main(parsed_args))
    colorama.deinit()
    sys.exit(0)
