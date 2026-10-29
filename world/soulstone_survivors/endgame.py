"""Endgame checks: Void Fields curses, Unholy Cathedral rituals and Titan Hunt.

These checks carry random curse levels and weapons, so their ids are handed out
at generation time instead of living in `location_name_to_id`. The spec attached
to each one is what the plugin evaluates against the live save:

    curse  -> map, curse (minimum), optional weapon, optional character count
    ritual -> tier (Copper/Silver/Gold == Sin/Hate/Heresy), optional dynamic
              level (+N), optional character or character count
    titan  -> tier (1..5 == Corruption..Void), same optional fields

The game enforces the order itself (Hate needs Sin, Heresy needs Hate, sigil N
needs N-1, +N needs +N-1), so nothing here has to gate those chains.
"""
from __future__ import annotations

import zlib

from . import data
from .Items import CHARACTER_PREFIX, CURSE_TIER_PREFIX, MODE_ITEM_FOR_KIND, WEAPON_PREFIX

# Ids are derived from the check name rather than handed out by position, so the
# same check keeps the same id in every seed and two Soulstone slots in one room
# can never claim the same id for different names.
_ID_BASE = 100_000_000
_ID_RANGE = 100_000_000


def location_id(name: str) -> int:
    return _ID_BASE + (zlib.crc32(name.encode("utf-8")) % _ID_RANGE)

DIFFICULTY_OFF = "off"
DIFFICULTY_EASY = "easy"
DIFFICULTY_MEDIUM = "medium"
DIFFICULTY_HARD = "hard"
DIFFICULTIES = (DIFFICULTY_OFF, DIFFICULTY_EASY, DIFFICULTY_MEDIUM, DIFFICULTY_HARD)

# "with at least N different characters" checks added from medium upwards.
MULTI_CHARACTER_COUNT = 5
# Random weapon/curse combos per map, hard only.
WEAPON_COMBOS_PER_MAP = 2


class Check:
    """One generated location: its name, Archipelago id and detection spec."""

    __slots__ = ("name", "id", "spec")

    def __init__(self, name: str, location_id: int, spec: dict) -> None:
        self.name = name
        self.id = location_id
        self.spec = spec

    def __repr__(self) -> str:  # pragma: no cover - debugging aid
        return f"Check({self.name!r}, {self.id}, {self.spec!r})"


def enabled_weapons(characters, allow_weapons):
    """Weapons whose owner is in play, so a weapon check can always be reached."""
    if not allow_weapons:
        return []
    allowed = set(characters)
    return [weapon for weapon in data.WEAPONS if data.weapon_owner(weapon) in allowed]


def weapons_of(character):
    return [weapon for weapon in data.WEAPONS if data.weapon_owner(weapon) == character]


def build(world, difficulty, characters, allow_weapons):
    """All endgame checks for one seed, in a stable order."""
    if difficulty == DIFFICULTY_OFF:
        return []

    entries = []

    def add(kind, name, spec):
        spec = dict(spec)
        spec["kind"] = kind
        entries.append(Check(name, location_id(name), spec))

    thresholds = data.CURSE_THRESHOLDS[difficulty]

    # ---- Void Fields curse ladder
    for map_id in data.MAPS:
        for curse in thresholds:
            add("curse", f"Curse {curse}: {map_id}", {"map": map_id, "curse": curse})

    if difficulty in (DIFFICULTY_MEDIUM, DIFFICULTY_HARD):
        # One map gets a randomised "same curse, five different characters" goal
        # per map, drawn from the top of the ladder so it stays an endgame task.
        pool = thresholds[-3:]
        for map_id in data.MAPS:
            curse = world.random.choice(pool)
            add(
                "curse",
                f"Curse {curse}: {map_id} with {MULTI_CHARACTER_COUNT} characters",
                {"map": map_id, "curse": curse, "chars": MULTI_CHARACTER_COUNT},
            )

    if difficulty == DIFFICULTY_HARD and allow_weapons:
        eligible = enabled_weapons(characters, allow_weapons)
        if eligible:
            for map_id in data.MAPS:
                for _ in range(WEAPON_COMBOS_PER_MAP):
                    curse = world.random.randint(1, data.MAX_CURSE)
                    weapon = world.random.choice(eligible)
                    add(
                        "curse",
                        f"Curse {curse}: {map_id} / {weapon}",
                        {"map": map_id, "curse": curse, "weapon": weapon},
                    )

    # ---- Unholy Cathedral
    heresy = data.RITUALS[-1]
    for ritual in data.RITUALS:
        add("ritual", ritual["name"], {"tier": ritual["tier"]})

    if difficulty in (DIFFICULTY_MEDIUM, DIFFICULTY_HARD):
        add(
            "ritual",
            f"{heresy['name']} with {MULTI_CHARACTER_COUNT} characters",
            {"tier": heresy["tier"], "chars": MULTI_CHARACTER_COUNT},
        )

    for step in range(1, data.ENDLESS_DEPTH[difficulty] + 1):
        add("ritual", f"{heresy['name']} +{step}", {"tier": heresy["tier"], "dynamic": step})

    if difficulty == DIFFICULTY_HARD:
        for character in characters:
            for ritual in data.RITUALS:
                add(
                    "ritual",
                    f"{ritual['name']} as {character}",
                    {"tier": ritual["tier"], "character": character},
                )

    # ---- Titan Hunt
    corruption = data.SIGILS[0]
    for sigil in data.SIGILS:
        add("titan", sigil["name"], {"tier": sigil["tier"]})

    if difficulty in (DIFFICULTY_MEDIUM, DIFFICULTY_HARD):
        void = data.SIGILS[-1]
        add(
            "titan",
            f"{void['name']} with {MULTI_CHARACTER_COUNT} characters",
            {"tier": void["tier"], "chars": MULTI_CHARACTER_COUNT},
        )

    for step in range(1, data.ENDLESS_DEPTH[difficulty] + 1):
        add("titan", f"{corruption['name']} +{step}", {"tier": corruption["tier"], "dynamic": step})

    if difficulty == DIFFICULTY_HARD:
        for character in characters:
            for sigil in data.SIGILS:
                add(
                    "titan",
                    f"{sigil['name']} as {character}",
                    {"tier": sigil["tier"], "character": character},
                )

    return entries


def logic_weapons(entries):
    """Weapons that gate a check, so they must be progression items."""
    return {entry.spec["weapon"] for entry in entries if entry.spec.get("weapon")}


def apply_rules(world, entries):
    """Attach access and item rules so item-gated checks cannot junk-lock.

    Returns the weapon items that now gate a check and therefore have to be
    classified as progression.
    """
    player = world.player
    multiworld = world.multiworld
    characters = list(world.endgame_characters)
    character_items = [CHARACTER_PREFIX + character for character in characters]
    logic_weapons = set()
    gate_modes = bool(getattr(world, "gate_endgame_modes", False))
    gate_tiers = bool(getattr(world, "gate_curse_tiers", False))
    roman = ("I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X")

    for entry in entries:
        spec = entry.spec
        try:
            location = multiworld.get_location(entry.name, player)
        except KeyError:  # pragma: no cover - only if a location was filtered out
            continue

        character = spec.get("character")
        weapon = spec.get("weapon")
        required_chars = int(spec.get("chars", 1) or 1)

        family = set()
        # Unholy Cathedral / Titan Hunt are handed out as items; every check of
        # that family sits behind its mode, and the mode item is never put on a
        # check it gates (that would junk-lock the chain).
        mode_item = MODE_ITEM_FOR_KIND.get(spec.get("kind")) if gate_modes else None
        if mode_item:
            family.add(mode_item)

        # Curse checks need the tier item that covers their intensity.
        tier_item = None
        if gate_tiers and spec.get("kind") == "curse":
            number = data.tier_for_curse(int(spec.get("curse", 0) or 0))
            if 1 <= number <= len(roman):
                tier_item = CURSE_TIER_PREFIX + roman[number - 1]
                family.add(tier_item)

        if required_chars > 1:
            need = required_chars

            def access(state, need=need, character_items=character_items, player=player,
                       mode_item=mode_item, tier_item=tier_item):
                if mode_item and not state.has(mode_item, player):
                    return False
                if tier_item and not state.has(tier_item, player):
                    return False
                owned = 0
                for item in character_items:
                    if state.has(item, player):
                        owned += 1
                        if owned >= need:
                            return True
                return False

            location.access_rule = access
            # A character unlock placed here would be needed to reach it again.
            family = set(character_items)
        else:
            gates = [mode_item] if mode_item else []
            if tier_item:
                gates.insert(0, tier_item)
            if weapon:
                gates.append(WEAPON_PREFIX + weapon)
                logic_weapons.add(weapon)
                owner = data.weapon_owner(weapon)
                if owner:
                    character = character or owner
            if character:
                gates.append(CHARACTER_PREFIX + character)
                family.add(CHARACTER_PREFIX + character)
                family.update(WEAPON_PREFIX + w for w in weapons_of(character))

            if gates:
                gate_tuple = tuple(gates)

                def access(state, gates=gate_tuple, player=player):
                    return all(state.has(item, player) for item in gates)

                location.access_rule = access

        if family:
            blocked = frozenset(family)

            def item_rule(item, blocked=blocked, player=player):
                return not (item.player == player and item.name in blocked)

            location.item_rule = item_rule

    return logic_weapons


def slot_data(entries):
    """The id -> spec table the plugin and the Python client both read."""
    return {str(entry.id): dict(entry.spec) for entry in entries}
