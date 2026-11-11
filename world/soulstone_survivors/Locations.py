from BaseClasses import Location

from . import data

GAME_NAME = "Soulstone Survivors"

MAP_PREFIX = "Clear "
ACHIEVEMENT_PREFIX = "Achievement: "
RUNE_PREFIX = "Rune: "


class SoulstoneLocation(Location):
    game = GAME_NAME


location_name_to_id = {}

_next_id = 10000
for _map in data.MAPS:
    location_name_to_id[MAP_PREFIX + _map] = _next_id
    _next_id += 1

_next_id = 11000
for _achievement in data.ACHIEVEMENTS:
    # Keep the catalog-order id stable while presenting the player-facing
    # curse-level name instead of the game's internal CompleteAffixTier id.
    location_name_to_id[ACHIEVEMENT_PREFIX + data.achievement_location_name(_achievement)] = _next_id
    _next_id += 1

# Legacy rune ids. New seeds no longer create these locations, but old seeds
# still need the ids for their generated checks.
_next_id = 12000
for _rune in data.RUNES:
    location_name_to_id[RUNE_PREFIX + _rune] = _next_id
    _next_id += 1

MAP_LOCATIONS = [MAP_PREFIX + m for m in data.MAPS]
ACHIEVEMENT_LOCATIONS = [ACHIEVEMENT_PREFIX + data.achievement_location_name(a) for a in data.ACHIEVEMENTS]
# Old seeds were generated before the CompleteAffixTier display rename. Their
# location ids are unchanged, so keep a legacy name table for the Python
# client; it is deliberately not part of the world's location_name_to_id.
LEGACY_ACHIEVEMENT_LOCATIONS = [ACHIEVEMENT_PREFIX + a for a in data.ACHIEVEMENTS]
legacy_location_name_to_id = {
    legacy: location_name_to_id[current]
    for legacy, current in zip(LEGACY_ACHIEVEMENT_LOCATIONS, ACHIEVEMENT_LOCATIONS)
}
RUNE_LOCATIONS = [RUNE_PREFIX + r for r in data.RUNES]
ALL_LOCATIONS = MAP_LOCATIONS + ACHIEVEMENT_LOCATIONS + RUNE_LOCATIONS

# Endgame checks are generated per seed (they carry random curse levels and
# weapons), so their ids come from endgame.location_id() and are registered into
# location_name_to_id at generation time.
CURSE_PREFIX = "Curse "
RITUAL_PREFIX = "Ritual"
TITAN_PREFIX = "Sigil"
