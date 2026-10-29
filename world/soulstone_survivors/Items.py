from BaseClasses import Item, ItemClassification

from . import data

GAME_NAME = "Soulstone Survivors"

CHARACTER_PREFIX = "Character: "
WEAPON_PREFIX = "Weapon: "


class SoulstoneItem(Item):
    game = GAME_NAME


item_name_to_id = {}
item_classifications = {}

_next_id = 1
for _character in data.CHARACTERS:
    _name = CHARACTER_PREFIX + _character
    item_name_to_id[_name] = _next_id
    item_classifications[_name] = ItemClassification.progression
    _next_id += 1

for _weapon in data.WEAPONS:
    _name = WEAPON_PREFIX + _weapon
    item_name_to_id[_name] = _next_id
    item_classifications[_name] = ItemClassification.useful
    _next_id += 1

item_name_to_id[data.FILLER_NAME] = _next_id
item_classifications[data.FILLER_NAME] = ItemClassification.filler
_next_id += 1

# Traps act on the live run instead of the save. Names must match
# Traps.Names in mod/Archipelago/Effects.cs.
TRAP_PREFIX = "Trap: "
TRAP_NAMES = [
    "Cursed Power",
    "Knockback",
    "Elite Ambush",
    "Boss Summons",
    "Swarm",
]

for _trap in TRAP_NAMES:
    _name = TRAP_PREFIX + _trap
    item_name_to_id[_name] = _next_id
    item_classifications[_name] = ItemClassification.trap
    _next_id += 1

TRAP_ITEMS = [TRAP_PREFIX + trap for trap in TRAP_NAMES]

# Runes are skill tree nodes: receiving one unlocks it outright, prerequisites
# and tier requirements ignored. They gate no checks, so they stay useful.
RUNE_PREFIX = "Rune: "

for _rune in data.RUNES:
    _name = RUNE_PREFIX + _rune
    item_name_to_id[_name] = _next_id
    item_classifications[_name] = ItemClassification.useful
    _next_id += 1

RUNE_ITEMS = [RUNE_PREFIX + rune for rune in data.RUNES]

# The two endgame modes are gated in game by a mode-level achievement. Handing
# the mode over as an item completes that achievement; the ritual / sigil chains
# inside are then progressed exactly like vanilla.
MODE_ITEM_VALUES = {
    "Unholy Cathedral": "Mastery",
    "Titan Hunt": "Crusade",
}

for _mode_name in MODE_ITEM_VALUES:
    item_name_to_id[_mode_name] = _next_id
    item_classifications[_mode_name] = ItemClassification.progression
    _next_id += 1

# The five Void Fields maps. A map is unlocked in the save by the existence of
# its progression entry, so the item is a plain save write on the plugin side.
MAP_ITEM_PREFIX = "Map: "

for _map in data.MAPS:
    _name = MAP_ITEM_PREFIX + _map
    item_name_to_id[_name] = _next_id
    item_classifications[_name] = ItemClassification.progression
    _next_id += 1

MAP_ITEMS = [MAP_ITEM_PREFIX + map_id for map_id in data.MAPS]

# Curse tiers: the tier badges on a map. Tier N implies every lower tier, so the
# plugin writes 1..N when one arrives.
CURSE_TIER_PREFIX = "Curse Tier "
_ROMAN = ["I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X"]

CURSE_TIER_ITEM_VALUES = {}
for _tier, _required in data.AFFIX_TIERS:
    if not 1 <= _tier <= len(_ROMAN):
        continue
    _name = CURSE_TIER_PREFIX + _ROMAN[_tier - 1]
    item_name_to_id[_name] = _next_id
    item_classifications[_name] = ItemClassification.progression
    CURSE_TIER_ITEM_VALUES[_name] = str(_tier)
    _next_id += 1

CURSE_TIER_ITEMS = list(CURSE_TIER_ITEM_VALUES)

# Weapon upgrade caps. The blacksmith has no ceiling of its own, so the mod
# blocks levels above the highest cap that has arrived. Level 1 is the base, so
# the floor is 1 (no upgrades) and -1 means unlimited.
WEAPON_CAP_PREFIX = "Weapon Upgrade "
WEAPON_CAP_ITEM_VALUES = {}
for _cap in (3, 5, 8, 10, 15):
    _name = WEAPON_CAP_PREFIX + "+" + str(_cap)
    item_name_to_id[_name] = _next_id
    item_classifications[_name] = ItemClassification.progression
    WEAPON_CAP_ITEM_VALUES[_name] = str(_cap)
    _next_id += 1

_name = WEAPON_CAP_PREFIX + "Unlimited"
item_name_to_id[_name] = _next_id
item_classifications[_name] = ItemClassification.progression
WEAPON_CAP_ITEM_VALUES[_name] = "-1"
_next_id += 1

WEAPON_CAP_ITEMS = list(WEAPON_CAP_ITEM_VALUES)
WEAPON_CAP_TOP = WEAPON_CAP_PREFIX + "+15"

# Resource caches: the plugin picks a resource the save already holds and adds
# 10% of the current amount, so their value scales with the player.
RESOURCE_CACHE_NAME = "Resource Cache"
item_name_to_id[RESOURCE_CACHE_NAME] = _next_id
item_classifications[RESOURCE_CACHE_NAME] = ItemClassification.useful
_next_id += 1

MODE_ITEMS = list(MODE_ITEM_VALUES)

# Which endgame check family needs which mode item.
MODE_ITEM_FOR_KIND = {"ritual": "Unholy Cathedral", "titan": "Titan Hunt"}

FILLER_NAME = data.FILLER_NAME

# Every non-filler item that goes into the pool.
PROGRESSION_ITEMS = [CHARACTER_PREFIX + c for c in data.CHARACTERS]
USEFUL_ITEMS = [WEAPON_PREFIX + w for w in data.WEAPONS] + RUNE_ITEMS
POOL_ITEMS = (
    PROGRESSION_ITEMS
    + MODE_ITEMS
    + MAP_ITEMS
    + CURSE_TIER_ITEMS
    + WEAPON_CAP_ITEMS
    + USEFUL_ITEMS
    + TRAP_ITEMS
)
