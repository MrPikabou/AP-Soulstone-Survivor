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
# (Barbarian) and hidden (Blacksmith) ones. Only used for help text when no
# catalog is loaded, so the option tooltip never lists an empty set.
KNOWN_ACHIEVEMENT_CHARACTERS = [
    "ArcaneWeaver", "Barbarian", "Beastmaster", "Blacksmith", "Bloodmage",
    "DeathKnight", "DemonHunter", "Elementalist", "Engineer", "HoundMaster",
    "Legionnaire", "Machinist", "MonkeyKing", "Myrmidon", "Necromancer",
    "Paladin", "Pirate", "Pyromancer", "Rogue", "Samurai", "Sentinel",
    "Shaman", "Spellbreaker",
]

# Characters that are unlocked from the start (CharacterDefinition.AutoUnlock).
FALLBACK_CHARACTERS = [
    "Pyromancer", "HoundMaster", "Spellbreaker", "ArcaneWeaver", "Sentinel",
    "Paladin", "Bloodmage", "Beastmaster", "Rogue", "Elementalist",
    "Legionnaire", "Necromancer", "DeathKnight", "MonkeyKing", "Engineer",
    "Myrmidon", "DemonHunter", "Pirate", "Shaman", "Machinist", "Samurai",
    "Blacksmith",
]

# The amount is per-seed (Soulstones per Filler option) and travels in slot_data,
# so the item name stays neutral.
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
    MAPS = _unique(
        entry["id"]
        for entry in _catalog.get("maps", [])
        if entry.get("id")
        and not entry.get("unavailable")
        and not entry.get("ignore")
        and not entry.get("crusade")
    )
    # Every character id in the build, including auto-unlocked and hidden ones:
    # achievements exist for characters that are never randomizable.
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

# Curse thresholds that count as checks, per check difficulty. Higher tiers keep
# the lower ones, so hard is a superset of medium and medium of easy.
CURSE_THRESHOLDS = {
    "easy": [10, 20],
    "medium": [10, 20, 30, 40, 50],
    "hard": [10, 20, 30, 40, 50, 62],
}

# How far the endless ladders go: Ritual of Heresy +N and Corruption +N.
ENDLESS_DEPTH = {"easy": 1, "medium": 5, "hard": 10}

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


def select_achievements(excluded_characters=(), max_prestige=None, include_hidden=True):
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
        kept.append(achievement_id)
    return kept
