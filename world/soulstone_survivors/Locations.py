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
    location_name_to_id[ACHIEVEMENT_PREFIX + _achievement] = _next_id
    _next_id += 1

_next_id = 12000
for _rune in data.RUNES:
    location_name_to_id[RUNE_PREFIX + _rune] = _next_id
    _next_id += 1

MAP_LOCATIONS = [MAP_PREFIX + m for m in data.MAPS]
ACHIEVEMENT_LOCATIONS = [ACHIEVEMENT_PREFIX + a for a in data.ACHIEVEMENTS]
RUNE_LOCATIONS = [RUNE_PREFIX + r for r in data.RUNES]
ALL_LOCATIONS = MAP_LOCATIONS + ACHIEVEMENT_LOCATIONS + RUNE_LOCATIONS

# Endgame checks are generated per seed (they carry random curse levels and
# weapons), so their ids come from endgame.location_id() and are registered into
# location_name_to_id at generation time.
CURSE_PREFIX = "Curse "
RITUAL_PREFIX = "Ritual"
TITAN_PREFIX = "Sigil"
