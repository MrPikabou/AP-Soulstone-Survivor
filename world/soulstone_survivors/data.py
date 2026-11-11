"""Static game data for the Soulstone Survivors world.

`catalog.json` is produced by the in-game bridge plugin (CATALOG command) and
holds the character / weapon / map / achievement ids for the installed game
build. If it is missing the world still imports, using the built-in character
list, so the package never hard-fails on a fresh checkout.
"""
import json
import os
import re
from importlib import resources

_HERE = os.path.dirname(os.path.abspath(__file__))
_CATALOG_PATH = os.path.join(_HERE, "catalog.json")


def _read_text():
    """Catalog text, whether the package is a folder or a zipped .apworld."""
    try:
        return resources.files(__package__).joinpath("catalog.json").read_text(encoding="utf-8")
    except (OSError, ValueError, AttributeError, TypeError, ModuleNotFoundError):
        pass
    if os.path.exists(_CATALOG_PATH):
        try:
            with open(_CATALOG_PATH, encoding="utf-8") as handle:
                return handle.read()
        except OSError:
            return None
    return None

# Characters that own achievements in build 1.5d2, including the auto-unlocked
# Barbarian and Blacksmith. Only used for help text when no catalog is loaded,
# so the option tooltip never lists an empty set.
KNOWN_ACHIEVEMENT_CHARACTERS = [
    "ArcaneWeaver", "Barbarian", "Beastmaster", "Blacksmith", "Bloodmage",
    "DeathKnight", "DemonHunter", "Elementalist", "Engineer", "HoundMaster",
    "Legionnaire", "Machinist", "MonkeyKing", "Myrmidon", "Necromancer",
    "Paladin", "Pirate", "Pyromancer", "Rogue", "Samurai", "Sentinel",
    "Shaman", "Spellbreaker",
]

# The character the game always unlocks, and the default Archipelago starter.
DEFAULT_CHARACTER = "Barbarian"

# Character ids used when no catalog is loaded (catalog.json missing).
FALLBACK_CHARACTERS = [
    "Pyromancer", "HoundMaster", "Spellbreaker", "ArcaneWeaver", "Sentinel",
    "Paladin", "Bloodmage", "Beastmaster", "Rogue", "Elementalist",
    "Legionnaire", "Necromancer", "DeathKnight", "MonkeyKing", "Engineer",
    "Myrmidon", "DemonHunter", "Pirate", "Shaman", "Machinist", "Samurai",
    "Blacksmith",
]

# Legacy single filler. New seeds with filler_variety use the tiers in Items.py;
# this name and amount remain for old seeds and when variety is off. The amount
# travels in slot_data, so the item name stays neutral.
FILLER_NAME = "Soulstones"
FILLER_AMOUNT = 100


def _read_catalog():
    text = _read_text()
    if text is None:
        return None
    try:
        return json.loads(text)
    except ValueError:
        return None


def _unique(values):
    seen = set()
    out = []
    for value in values:
        if not value or value in seen:
            continue
        seen.add(value)
        out.append(value)
    return out


_catalog = _read_catalog()

if _catalog is None:
    CHARACTERS = list(FALLBACK_CHARACTERS)
    CHARACTER_NAMES = list(FALLBACK_CHARACTERS)
    WEAPONS = []
    MAPS = []
    ACHIEVEMENTS = []
    RUNES = []
    AFFIX_TIERS = []
    _ACHIEVEMENT_ENTRIES = []
    HAS_CATALOG = False
else:
    CHARACTERS = _unique(
        entry["id"]
        for entry in _catalog.get("characters", [])
        if entry.get("id") and not entry.get("auto") and not entry.get("unavailable") and not entry.get("hidden")
    )
    WEAPONS = _unique(
        entry["id"]
        for entry in _catalog.get("weapons", [])
        if entry.get("id") and not entry.get("auto") and not entry.get("hidden")
    )

    # Characters that own craftable weapons, in catalog order: one crafting
    # licence item each when weapons are handed out as items.
    WEAPON_CHARACTERS = _unique(
        weapon[len("Weapon"):].split("-")[0] for weapon in WEAPONS
    )
    MAPS = _unique(
        entry["id"]
        for entry in _catalog.get("maps", [])
        if entry.get("id")
        and not entry.get("unavailable")
        and not entry.get("ignore")
        and not entry.get("crusade")
    )
    # Every character id in the build, including the auto-unlocked Barbarian:
    # achievements can exist for characters that are not randomized items.
    CHARACTER_NAMES = _unique(
        entry["id"] for entry in _catalog.get("characters", []) if entry.get("id")
    )
    _ACHIEVEMENT_ENTRIES = [
        (entry["id"], bool(entry.get("hidden")))
        for entry in _catalog.get("achievements", [])
        if entry.get("id") and not entry.get("disabled")
    ]
    # Runes are unlocked by dedicated skill tree nodes. The catalog carries the
    # node <-> rune mapping; only the rune ids matter here.
    RUNES = _unique(entry["id"] for entry in _catalog.get("runes", []) if entry.get("id"))
    # Curse tiers: each tier lists the curse intensity the game requires for it.
    AFFIX_TIERS = sorted(
        (
            (int(entry["tier"]), int(entry.get("requiredCurse") or 0))
            for entry in _catalog.get("affixTiers", [])
            if entry.get("tier")
        ),
    )
    ACHIEVEMENTS = _unique(achievement_id for achievement_id, _ in _ACHIEVEMENT_ENTRIES)
    HAS_CATALOG = True


# The installed game still calls these `CompleteAffixTierSurvivors<Map><Tier>`.
# Archipelago shows them as `CompleteCurseLevel<Map><Tier>` so the check name
# describes the player-facing curse tier rather than the internal affix name.
# Boss Rush and Endless use the red/purple portal names for the same reason.
# The game ids stay unchanged because the plugin reports those ids.
_AFFIX_ACHIEVEMENT_PATTERN = (
    re.compile(
        r"^CompleteAffixTier("
        + "|".join(re.escape(map_id) for map_id in sorted(MAPS, key=len, reverse=True))
        + r")(\d+)$"
    )
    if MAPS
    else None
)


def map_display_name(map_id):
    """Short map name used in Archipelago labels."""
    if map_id.startswith("Survivors"):
        return map_id[len("Survivors"):]
    return map_id


def affix_achievement_tier(achievement_id):
    """Affix/curse tier encoded in a map achievement id, or None."""
    match = _AFFIX_ACHIEVEMENT_PATTERN.match(achievement_id or "") if _AFFIX_ACHIEVEMENT_PATTERN else None
    return int(match.group(2)) if match else None


def achievement_location_name(achievement_id):
    """Archipelago label for an achievement, without the location prefix."""
    match = _AFFIX_ACHIEVEMENT_PATTERN.match(achievement_id or "") if _AFFIX_ACHIEVEMENT_PATTERN else None
    if match is not None:
        return "CompleteCurseLevel" + map_display_name(match.group(1)) + match.group(2)
    return portal_cycle_location_name(achievement_id) or achievement_id


_PORTAL_CYCLE_PATTERN = re.compile(r"^Complete(BossRush|Endless)Cycle(\d+)$")


def portal_cycle_location_name(achievement_id):
    """Archipelago label for a Boss Rush / Endless cycle, or None."""
    match = _PORTAL_CYCLE_PATTERN.match(achievement_id or "")
    if match is None:
        return None
    colour = "Red" if match.group(1) == "BossRush" else "Purple"
    return "Complete" + colour + "PortalCycle" + match.group(2)


def portal_cycle_number(achievement_id):
    """Cycle number for a Boss Rush / Endless achievement, or None."""
    match = _PORTAL_CYCLE_PATTERN.match(achievement_id or "")
    return int(match.group(2)) if match else None


def tier_for_curse(curse):
    """Lowest curse tier that covers a curse intensity (1 when unknown)."""
    tier = 0
    for number, required in AFFIX_TIERS:
        if required <= curse and number > tier:
            tier = number
    return tier or (AFFIX_TIERS[0][0] if AFFIX_TIERS else 1)


# Character names ordered longest-first so a suffix never matches a shorter,
# nested name in front of it.
_NAME_SUFFIXES = sorted(CHARACTER_NAMES, key=len, reverse=True)
_PRESTIGE_PATTERN = re.compile(r"^Prestige.*?(\d+)$")


def achievement_character(achievement_id):
    """The character an achievement is scoped to, or None for global ones.

    The catalog only ships ids, so the scope is read off the id itself:
    `UnlockPyromancer`, `PrestigePyromancer150`, `CompleteSkillTreePyromancer`,
    `CraftWeaponPyromancer1`, `CompleteMatchInLessThanTime12Pyromancer`.
    """
    stem = achievement_id.rstrip("0123456789")
    for name in _NAME_SUFFIXES:
        if stem.endswith(name):
            return name
    return None


def achievement_prestige_tier(achievement_id):
    """Prestige level an achievement asks for (`PrestigeRogue150` -> 150)."""
    match = _PRESTIGE_PATTERN.match(achievement_id)
    return int(match.group(1)) if match else None


_CHARACTER_UNLOCK_COUNT = re.compile(r"^UnlockCharacterCount\d+$")


def is_character_unlock(achievement_id):
    """True when receiving a character item completes this achievement.

    Character grants run the game's own unlock scans, which complete both
    `Unlock<Character>` and `UnlockCharacterCount<N>`. Keeping those checks
    would make the item complete a location by itself and could chain another
    item to a third player.
    """
    achievement_id = achievement_id or ""
    if _CHARACTER_UNLOCK_COUNT.match(achievement_id):
        return True
    return (
        achievement_id.startswith("Unlock")
        and achievement_character(achievement_id) is not None
    )


# Characters that actually have achievements, i.e. the ones worth disabling.
ACHIEVEMENT_CHARACTERS = sorted(
    {character for character in map(achievement_character, ACHIEVEMENTS) if character}
)

HIDDEN_ACHIEVEMENTS = [achievement_id for achievement_id, hidden in _ACHIEVEMENT_ENTRIES if hidden]


def resolve_character(name):
    """Case-insensitive lookup of a character name, or None if unknown."""
    key = (name or "").strip().lower()
    for candidate in ACHIEVEMENT_CHARACTERS:
        if candidate.lower() == key:
            return candidate
    return None


# --------------------------------------------------------------- endgame data

# Highest curse intensity the game offers on a Void Fields map.
MAX_CURSE = 62

# Unholy Cathedral rituals, in unlock order. The game stores these as
# DifficultyTierType Copper / Silver / Gold and only opens Hate after Sin and
# Heresy after Hate.
RITUALS = [
    {"name": "Ritual of Sin", "tier": 1, "internal": "Copper"},
    {"name": "Ritual of Hate", "tier": 2, "internal": "Silver"},
    {"name": "Ritual of Heresy", "tier": 3, "internal": "Gold"},
]

# Titan Hunt sigils, in unlock order, matching SigilDifficultyTierType.Tier1..5.
SIGILS = [
    {"name": "Sigil of Corruption", "tier": 1},
    {"name": "Sigil of Vileness", "tier": 2},
    {"name": "Sigil of Wickedness", "tier": 3},
    {"name": "Sigil of Hate", "tier": 4},
    {"name": "Sigil of the Void", "tier": 5},
]

# Weapon ids are `Weapon<Character>-NN`, so the owner can be read off the id even
# when no catalog is installed.
WEAPON_OWNERS = (
    {
        entry["id"]: entry["character"]
        for entry in _catalog.get("weapons", [])
        if entry.get("id") and entry.get("character")
    }
    if _catalog
    else {}
)

_WEAPON_PATTERN = re.compile(r"^Weapon(.+)-(\d+)$")


def weapon_owner(weapon_id):
    """Character that owns a weapon id."""
    if weapon_id in WEAPON_OWNERS:
        return WEAPON_OWNERS[weapon_id]
    match = _WEAPON_PATTERN.match(weapon_id or "")
    return match.group(1) if match else None


def weapon_slot(weapon_id):
    """Weapon number within its character: 1 is the starting weapon."""
    match = _WEAPON_PATTERN.match(weapon_id or "")
    return int(match.group(2)) if match else 0


def select_achievements(
    excluded_characters=(), max_prestige=None, include_hidden=True, max_portal_cycle=None
):
    """Achievement ids that stay in the check pool for one player's options."""
    excluded = {character.lower() for character in excluded_characters}
    kept = []
    for achievement_id, hidden in _ACHIEVEMENT_ENTRIES:
        if hidden and not include_hidden:
            continue
        character = achievement_character(achievement_id)
        if character is not None and character.lower() in excluded:
            continue
        tier = achievement_prestige_tier(achievement_id)
        if tier is not None and max_prestige is not None and tier > max_prestige:
            continue
        cycle = portal_cycle_number(achievement_id)
        if cycle is not None and max_portal_cycle is not None and cycle > max_portal_cycle:
            continue
        kept.append(achievement_id)
    return kept
