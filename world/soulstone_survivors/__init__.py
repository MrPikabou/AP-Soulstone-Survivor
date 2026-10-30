import logging
from dataclasses import dataclass
from typing import Any, Dict

from BaseClasses import Item, ItemClassification, Region
from Options import (
    Choice,
    DeathLink,
    DefaultOnToggle,
    OptionError,
    OptionList,
    PerGameCommonOptions,
    Range,
    Toggle,
)
from worlds.AutoWorld import World, data_package_checksum

from . import data, endgame
from .Items import (
    CHARACTER_PREFIX,
    CURSE_TIER_ITEM_VALUES,
    CURSE_TIER_ITEMS,
    CURSE_TIER_PREFIX,
    FILLER_NAME,
    MAP_ITEM_PREFIX,
    MAP_ITEMS,
    MODE_ITEM_VALUES,
    MODE_ITEMS,
    PROGRESSION_ITEMS,
    RESOURCE_CACHE_NAME,
    RUNE_ITEMS,
    RUNE_PREFIX,
    TRAP_ITEMS,
    TRAP_PREFIX,
    USEFUL_ITEMS,
    WEAPON_CAP_ITEM_VALUES,
    WEAPON_CAP_ITEMS,
    WEAPON_CAP_PREFIX,
    WEAPON_CAP_TOP,
    WEAPON_PREFIX,
    SoulstoneItem,
    item_classifications,
    item_name_to_id,
)
from .Locations import (
    ACHIEVEMENT_LOCATIONS,
    ACHIEVEMENT_PREFIX,
    MAP_LOCATIONS,
    MAP_PREFIX,
    RUNE_LOCATIONS,
    SoulstoneLocation,
    location_name_to_id,
)

GAME_NAME = "Soulstone Survivors"
GAME_VERSION = "1.5d2"


class SoulstoneKnockbackLink(Toggle):
    """Taking damage sends a shove to other players, and their hits shove you."""

    display_name = "Knockback Link"
    default = 0


class SoulstoneAchievementChecks(Toggle):
    """Include the 483 achievements as checks. Off makes the seed maps-only."""

    display_name = "Achievement Checks"
    default = 1


class SoulstoneWeaponUnlocks(Toggle):
    """Include the 88 blacksmith weapon unlocks as useful items."""

    display_name = "Weapon Unlocks"
    default = 1


class SoulstoneCurseTierUnlocks(DefaultOnToggle):
    """Hand out the curse tiers (the I-VII badges on a map) as items.

    On (default): every tier is a progression item, and a curse check needs the
    tier that covers its intensity, so nothing above your unlocked tiers can be
    part of the seed's early work. The game's own tier progress is dropped while
    a run is live, so tiers only come from other players.

    Off: tiers fill up as you play, as in the base game.
    """

    display_name = "Curse Tier Unlocks"


class SoulstoneWeaponUpgradeUnlocks(DefaultOnToggle):
    """Hand out the blacksmith weapon upgrade ceiling as items.

    The blacksmith has no level cap of its own; it just gets more expensive. With
    this on (default) the mod blocks a weapon from going above the highest cap
    that arrived: nothing above level 1 at the start, then +3, +5, +8, +10, +15,
    then unlimited. Your own materials still pay for every upgrade - the items
    only raise how far you are allowed to go.

    Off leaves upgrades uncapped, as in the base game.
    """

    display_name = "Weapon Upgrade Caps"


class SoulstoneMapUnlocks(DefaultOnToggle):
    """Hand out the five Void Fields maps as items instead of assuming them.

    On (default): each map is a progression item and its "Clear <map>" check
    needs that item, so the maps come from other players. The five are registered
    as local early items, which puts them on your own earliest checks rather than
    somewhere late in the seed.

    Off: every map is unlocked from the start - no map items, no gating, and the
    plugin writes all five map progression entries into the save when it joins.
    """

    display_name = "Map Unlocks"


class SoulstoneEndgameModeUnlocks(DefaultOnToggle):
    """Hand out Unholy Cathedral and Titan Hunt as items instead of assuming them.

    Both modes are gated in game by a mode achievement. With this on, "Unholy
    Cathedral" and "Titan Hunt" go into the pool as progression items and every
    ritual / sigil check requires its mode, so those checks are only reachable
    once another player sends the mode. What happens inside the mode is vanilla:
    Sin then Hate then Heresy, and each sigil in order.

    Off leaves the modes as they are in your save - nothing to find, nothing to
    gate. Ignored when Endgame Checks is off, since there are no ritual or sigil
    checks to gate.
    """

    display_name = "Endgame Mode Unlocks"


class SoulstoneRuneUnlocks(DefaultOnToggle):
    """Include the rune unlocks as items and checks.

    Every rune is unlocked in game by a dedicated skill tree node. With this on,
    those nodes become items: receiving one unlocks the rune outright, ignoring
    its tier and prerequisite nodes, and owning the rune is the matching check.
    You can still buy a rune node with your own skill points - the tree itself
    stays yours, and every stat node is untouched either way.
    """

    display_name = "Rune Unlocks"


class SoulstoneTrapCount(Range):
    """How many trap items to shuffle into the pool. 0 disables traps."""

    display_name = "Trap Count"
    range_start = 0
    range_end = 25
    default = 5


class SoulstoneResourceCount(Range):
    """How many Resource Cache items go into the pool. 0 disables them.

    A cache picks a random resource you already hold - one of the six soulstone
    currencies or a mineral - and adds 10% of your current amount of it.
    """

    display_name = "Resource Cache Count"
    range_start = 0
    range_end = 40
    default = 8


class SoulstoneFillerSoulstones(Range):
    """Soulstones each filler item grants."""

    display_name = "Soulstones per Filler"
    range_start = 50
    range_end = 1000
    default = 100


class SoulstoneMapsRequired(Range):
    """How many of the five standard map clears the goal needs."""

    display_name = "Maps Required for Victory"
    range_start = 1
    range_end = 5
    default = 5


class SoulstoneDisabledCharacters(OptionList):
    """Valid names: CHARACTER_NAMES_PLACEHOLDER

    Character achievements to leave out of the check pool. Dropping a character
    removes every check that names it: Unlock, Prestige, CompleteSkillTree,
    CraftWeapon / CraftAllCharacterWeapons and CompleteMatchInLessThanTime (18
    checks for a typical character). Global counters like "unlock 22 characters"
    or "complete 22 skill trees" stay in the pool. Names are case-insensitive.
    Barbarian (auto-unlocked) and Blacksmith (hidden) have achievements but are
    never randomizable items.
    """

    display_name = "Disabled Character Achievements"
    default = []


class SoulstoneMaxPrestige(Range):
    """Highest character prestige tier that still counts as a check.

    Prestige achievements ask for 5, 10, 20, 30, 40, 50, 60, 70, 100 or 150,
    so 100 keeps everything up to prestige 100 and drops the 150 checks.
    0 removes every prestige check, 150 (the default) keeps all of them.
    """

    display_name = "Max Prestige Check"
    range_start = 0
    range_end = 150
    default = 150


class SoulstoneHiddenAchievements(DefaultOnToggle):
    """Include the 11 hidden achievements as checks (secret, unlisted in game)."""

    display_name = "Hidden Achievements"


class SoulstoneEndgameChecks(Choice):
    """Checks beyond the five maps and the achievements.

    Covers the Void Fields curse ladder (each map at increasing curse
    intensity), Unholy Cathedral rituals (Sin, Hate, Heresy) and Titan Hunt
    sigils (Corruption, Vileness, Wickedness, Hate, Void).

    off  - plain maps + achievements only.
    easy - global clears: every map at curse 10 and 20, all three rituals, all
           five sigils, and Ritual of Heresy / Corruption +1.
    medium - curse 10 to 50, one randomised "five different characters" curse
           check per map, the five-character ritual/sigil clears, and endless
           up to +5.
    hard - adds curse 62, two randomised curse/weapon combos per map, every
           ritual and sigil per character, and endless up to +10.

    Weapon checks are logic-gated: they need the weapon and its character, and
    the character's own unlock is never shuffled into a check it gates.
    """

    display_name = "Endgame Checks"
    option_off = 0
    option_easy = 1
    option_medium = 2
    option_hard = 3
    default = 2


# The valid names come from the installed build's catalog, so they cannot be
# baked into the class body as a literal docstring. The known-good list keeps the
# help text populated even when no catalog is loaded.
_LISTED_CHARACTERS = data.ACHIEVEMENT_CHARACTERS or data.KNOWN_ACHIEVEMENT_CHARACTERS
SoulstoneDisabledCharacters.__doc__ = SoulstoneDisabledCharacters.__doc__.replace(
    "CHARACTER_NAMES_PLACEHOLDER", ", ".join(_LISTED_CHARACTERS)
)


@dataclass
class SoulstoneOptions(PerGameCommonOptions):
    # A death does not kill you here: it hands you a choice of negative Powers.
    death_link: DeathLink
    knockback_link: SoulstoneKnockbackLink
    achievement_checks: SoulstoneAchievementChecks
    disabled_characters: SoulstoneDisabledCharacters
    max_prestige: SoulstoneMaxPrestige
    hidden_achievements: SoulstoneHiddenAchievements
    endgame_checks: SoulstoneEndgameChecks
    map_unlocks: SoulstoneMapUnlocks
    curse_tier_unlocks: SoulstoneCurseTierUnlocks
    endgame_mode_unlocks: SoulstoneEndgameModeUnlocks
    weapon_unlocks: SoulstoneWeaponUnlocks
    weapon_upgrade_unlocks: SoulstoneWeaponUpgradeUnlocks
    rune_unlocks: SoulstoneRuneUnlocks
    trap_count: SoulstoneTrapCount
    resource_cache_count: SoulstoneResourceCount
    filler_soulstones: SoulstoneFillerSoulstones
    maps_required: SoulstoneMapsRequired


class SoulstoneSurvivorsWorld(World):
    """
    Soulstone Survivors is a bullet-heaven survivor game. This world randomizes
    which characters and weapons you have access to: every character and weapon
    beyond the starting one is an item, and clearing maps / earning achievements
    are the checks. The goal is to clear every standard map in normal mode.
    """

    game = GAME_NAME
    item_name_to_id = item_name_to_id
    location_name_to_id = location_name_to_id

    options_dataclass = SoulstoneOptions
    options: SoulstoneOptions

    def generate_early(self) -> None:
        # Achievements are filtered per player, so the id tables stay global and
        # only the locations / slot data handed to this slot are narrowed.
        excluded = []
        unknown = []
        for raw in self.options.disabled_characters.value:
            text = str(raw).strip()
            if not text:
                continue
            canonical = data.resolve_character(text)
            if canonical is None:
                unknown.append(text)
            elif canonical not in excluded:
                excluded.append(canonical)

        if unknown:
            raise OptionError(
                f"Soulstone Survivors: unknown disabled_characters value(s) {', '.join(unknown)}. "
                f"Valid names: {', '.join(data.ACHIEVEMENT_CHARACTERS)}"
            )

        self.disabled_characters = excluded
        self.enabled_achievements = data.select_achievements(
            excluded_characters=excluded,
            max_prestige=self.options.max_prestige.value,
            include_hidden=bool(self.options.hidden_achievements.value),
        )

        self.endgame_difficulty = self.options.endgame_checks.current_key
        self.endgame_characters = [
            character for character in data.CHARACTERS if character not in excluded
        ]
        self.endgame_entries = endgame.build(
            self,
            self.endgame_difficulty,
            self.endgame_characters,
            bool(self.options.weapon_unlocks.value),
        )
        self.endgame_logic_weapons = endgame.logic_weapons(self.endgame_entries)

        # Only meaningful when the seed actually has ritual / sigil checks.
        self.gate_curse_tiers = bool(self.options.curse_tier_unlocks.value) and bool(CURSE_TIER_ITEMS)
        self.weapon_cap_unlocks = bool(self.options.weapon_upgrade_unlocks.value) and bool(WEAPON_CAP_ITEMS)
        self.maps_as_items = bool(self.options.map_unlocks.value)
        if self.maps_as_items:
            # Local early items land on this player's own first checks, so the
            # maps are in hand after a couple of clears instead of sphere six.
            for item_name in MAP_ITEMS:
                self.multiworld.local_early_items[self.player][item_name] = 1

        self.gate_endgame_modes = bool(self.options.endgame_mode_unlocks.value) and any(
            entry.spec.get("kind") in ("ritual", "titan") for entry in self.endgame_entries
        )

        # The datapackage is built from the world's location_name_to_id, and that
        # is what the server, tracker and !hint read names from. Endgame names are
        # seed specific, so they are registered here or every check shows up as
        # "Unknown location". AutoWorldRegister replaces the class's copy with a
        # filtered one, so the world's own dict is the one that matters.
        self.location_name_to_id.update(
            {entry.name: entry.id for entry in self.endgame_entries}
        )
        type(self).location_id_to_name.update(
            {entry.id: entry.name for entry in self.endgame_entries}
        )
        type(self).location_names = frozenset(self.location_name_to_id)
        self.refresh_data_package_checksum()

        logging.info(
            "%s: %d endgame checks (%s difficulty)",
            self.player_name,
            len(self.endgame_entries),
            self.endgame_difficulty,
        )

        logging.info(
            "%s: %d of %d achievements are checks (excluded characters: %s, max prestige: %d, "
            "hidden: %s)",
            self.player_name,
            len(self.enabled_achievements),
            len(data.ACHIEVEMENTS),
            ", ".join(excluded) or "none",
            self.options.max_prestige.value,
            bool(self.options.hidden_achievements.value),
        )

    def refresh_data_package_checksum(self) -> None:
        """Re-hash the game's datapackage after the seed's endgame names were added.

        ``worlds.network_data_package`` records a checksum next to a live reference
        to ``location_name_to_id``, computed once at startup. Updating that dict for
        the seed changes the datapackage that goes into the multidata, so the old
        checksum no longer describes it and the file fails to verify itself (the
        hosting site and any client comparing checksums reject it).
        """
        import worlds  # local: worlds is still importing while this module loads

        package = worlds.network_data_package["games"].get(self.game)
        if package is None:
            return
        package["location_name_to_id"] = dict(self.location_name_to_id)
        package["checksum"] = data_package_checksum(
            {key: value for key, value in sorted(package.items()) if key != "checksum"}
        )

    def active_locations(self):
        """Checks that exist for this seed, based on the player's options."""
        names = list(MAP_LOCATIONS)
        if self.options.achievement_checks.value:
            names += [ACHIEVEMENT_PREFIX + achievement for achievement in self.enabled_achievements]
        if self.options.rune_unlocks.value:
            names += RUNE_LOCATIONS
        names += [entry.name for entry in self.endgame_entries]
        return names

    def create_regions(self) -> None:
        menu = Region("Menu", self.player, self.multiworld)

        for name in self.active_locations():
            menu.locations.append(
                SoulstoneLocation(self.player, name, self.location_name_to_id[name], menu)
            )

        victory = SoulstoneLocation(self.player, "Victory", None, menu)
        victory.place_locked_item(Item("Victory", ItemClassification.progression, None, self.player))
        menu.locations.append(victory)

        self.multiworld.regions.append(menu)

    def create_items(self) -> None:
        names = list(PROGRESSION_ITEMS)
        if self.maps_as_items:
            names += MAP_ITEMS
        if self.gate_curse_tiers:
            names += CURSE_TIER_ITEMS
        if self.gate_endgame_modes:
            names += MODE_ITEMS
        if self.weapon_cap_unlocks:
            names += WEAPON_CAP_ITEMS
        if self.options.weapon_unlocks.value:
            names += USEFUL_ITEMS
        if self.options.rune_unlocks.value:
            names += RUNE_ITEMS

        trap_total = self.options.trap_count.value
        for index in range(trap_total):
            names.append(TRAP_ITEMS[index % len(TRAP_ITEMS)])

        for _ in range(self.options.resource_cache_count.value):
            names.append(RESOURCE_CACHE_NAME)

        pool = [self.create_item(name) for name in names]
        location_count = len(self.active_locations())

        if len(pool) < location_count:
            pool += [self.create_item(FILLER_NAME) for _ in range(location_count - len(pool))]
        elif len(pool) > location_count:
            # More unlocks than checks (unusual): keep the pool fillable by
            # dropping the least important items from the tail.
            pool = pool[:location_count]

        self.multiworld.itempool += pool

    def set_rules(self) -> None:
        # Logic is intentionally flat: in-game progression (clearing a map to
        # unlock the next one) still applies, but every check is reachable from
        # the start in Archipelago's logic. The randomization comes from which
        # characters and weapons you are given.
        if self.maps_as_items:
            for map_id in data.MAPS:
                location = self.multiworld.get_location(MAP_PREFIX + map_id, self.player)
                gate = MAP_ITEM_PREFIX + map_id

                def access(state, gate=gate, player=self.player):
                    return state.has(gate, player)

                location.access_rule = access

        # "Weapons upgraded to Artifact rarity" needs a level no early cap allows.
        # The top finite cap (+15) is the requirement rather than a measured
        # threshold, because the game only exposes rarity-per-level at runtime.
        if self.weapon_cap_unlocks:
            for achievement_id in (
                "WeaponsUpgradedToArtifactRarityCount10",
                "WeaponsUpgradedToArtifactRarityCount30",
                "WeaponsUpgradedToArtifactRarityCount60",
            ):
                name = ACHIEVEMENT_PREFIX + achievement_id
                if name not in self.location_name_to_id:
                    continue
                location = self.multiworld.get_location(name, self.player)

                def access(state, player=self.player):
                    return state.has(WEAPON_CAP_TOP, player)

                location.access_rule = access

        required = max(1, min(self.options.maps_required.value, len(MAP_LOCATIONS)))
        goal_locations = MAP_LOCATIONS[:required]

        victory = self.multiworld.get_location("Victory", self.player)
        victory.access_rule = lambda state: all(
            state.can_reach(name, "Location", self.player) for name in goal_locations
        )

        endgame.apply_rules(self, self.endgame_entries)
        self.multiworld.completion_condition[self.player] = lambda state: state.has(
            "Victory", self.player
        )

    def create_item(self, name: str) -> SoulstoneItem:
        classification = item_classifications.get(name, ItemClassification.filler)
        # A weapon that gates an endgame check is required to reach it, so it
        # cannot stay a swappable useful item.
        if name.startswith(WEAPON_PREFIX) and name[len(WEAPON_PREFIX):] in self.endgame_logic_weapons:
            classification = ItemClassification.progression
        return SoulstoneItem(
            name,
            classification,
            item_name_to_id.get(name),
            self.player,
        )

    def fill_slot_data(self) -> Dict[str, Any]:
        # The in-game client has no idea how this world assigned item/location
        # ids, so ship the mapping with the slot. Keys are strings because the
        # JSON reader in the plugin works on string-keyed objects.
        items = {}
        for name, item_id in item_name_to_id.items():
            if name.startswith(CHARACTER_PREFIX):
                items[str(item_id)] = {"kind": "char", "value": name[len(CHARACTER_PREFIX):]}
            elif name.startswith(WEAPON_PREFIX):
                items[str(item_id)] = {"kind": "weapon", "value": name[len(WEAPON_PREFIX):]}
            elif name in CURSE_TIER_ITEM_VALUES:
                items[str(item_id)] = {"kind": "curse", "value": CURSE_TIER_ITEM_VALUES[name]}
            elif name in WEAPON_CAP_ITEM_VALUES:
                items[str(item_id)] = {"kind": "weaponcap", "value": WEAPON_CAP_ITEM_VALUES[name]}
            elif name.startswith(MAP_ITEM_PREFIX):
                items[str(item_id)] = {"kind": "map", "value": name[len(MAP_ITEM_PREFIX):]}
            elif name in MODE_ITEM_VALUES:
                items[str(item_id)] = {"kind": "mode", "value": MODE_ITEM_VALUES[name]}
            elif name.startswith(RUNE_PREFIX):
                items[str(item_id)] = {"kind": "rune", "value": name[len(RUNE_PREFIX):]}
            elif name.startswith(TRAP_PREFIX):
                items[str(item_id)] = {"kind": "trap", "value": name[len(TRAP_PREFIX):]}
            elif name == RESOURCE_CACHE_NAME:
                items[str(item_id)] = {"kind": "resource", "value": "10"}
            else:
                items[str(item_id)] = {"kind": "souls", "value": str(self.options.filler_soulstones.value)}

        maps = {map_id: location_name_to_id[MAP_PREFIX + map_id] for map_id in data.MAPS}
        achievements = {
            achievement_id: location_name_to_id[ACHIEVEMENT_PREFIX + achievement_id]
            for achievement_id in self.enabled_achievements
        }

        runes = {
            rune_id: location_name_to_id[RUNE_PREFIX + rune_id]
            for rune_id in data.RUNES
            if self.options.rune_unlocks.value
        }

        return {
            "game_version": GAME_VERSION,
            "has_catalog": data.HAS_CATALOG,
            "death_link": bool(self.options.death_link.value),
            "knockback_link": bool(self.options.knockback_link.value),
            "items": items,
            "maps": maps,
            "achievements": achievements,
            "runes": runes,
            "maps_default": not self.maps_as_items,
            # False = weapons arrive as items, so crafting an unknown weapon at the
            # blacksmith is a self-unlock and the plugin refuses it.
            "weapons_default": not bool(self.options.weapon_unlocks.value),
            "endgame": endgame.slot_data(self.endgame_entries),
        }
