import logging
import re
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

from . import balance, data, endgame
from .Items import (
    CHARACTER_PREFIX,
    CRAFT_ITEMS,
    CRAFT_PREFIX,
    CURSE_TIER_ITEM_BY_NUMBER,
    CURSE_TIER_ITEM_VALUES,
    CURSE_TIER_ITEMS,
    CURSE_TIER_PREFIX,
    FILLER_NAME,
    FILLER_VARIETY_ITEMS,
    filler_amounts,
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
    WEAPON_ITEMS,
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
    SoulstoneLocation,
    location_name_to_id,
)

GAME_NAME = "Soulstone Survivors"
GAME_VERSION = "1.5d2"

# The Red portal is the Boss Rush ladder; the Purple portal is Endless. The
# display names are what the locations use, while these ids stay stable.
_PORTAL_GOAL_ACHIEVEMENTS = {
    "red": ("CompleteBossRushCycle3",),
    "purple": ("CompleteEndlessCycle3",),
    "both": ("CompleteBossRushCycle3", "CompleteEndlessCycle3"),
}


class SoulstoneKnockbackLink(Toggle):
    """Taking damage sends a shove to other players, and their hits shove you."""

    display_name = "Knockback Link"
    default = 0


class SoulstoneAchievementChecks(Toggle):
    """Include achievement checks. Off makes the seed maps-only.

    Character unlock achievements are always omitted: receiving a
    `Character: <name>` item completes them through the game's own unlock
    scans, so they would pay out from the item itself.
    """

    display_name = "Achievement Checks"
    default = 1


class SoulstoneWeaponUnlocks(Toggle):
    """Weapons are items.

    On (default): the 88 blacksmith weapon unlocks are useful items, and every
    craft achievement (CraftWeapon<Char><n>, CraftAllCharacterWeapons<Char>) is
    removed from the check pool, because crafting is not part of that seed.

    Off: weapons are crafted like in the normal game, and one licence per
    character (`Craft: <Character>`) gates the blacksmith. All craft achievements
    stay as checks and are reachable once the character's licence arrives.
    """

    display_name = "Weapons Are Items"
    default = 1


class SoulstoneCurseTierUnlocks(DefaultOnToggle):
    """Hand out the curse tiers (the I-VII badges on a map) as items.

    On (default): every tier is a progression item, and a curse check needs the
    tier that covers its intensity, so nothing above your unlocked tiers can be
    part of the seed's early work. The game's own tier progress is dropped while
    a run is live, so tiers only come from other players. Tier V/VI map
    achievements also require their matching tier item.

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
    """Include the rune unlocks as items.

    Every rune is unlocked in game by a dedicated skill tree node. With this on,
    those nodes become items: receiving one unlocks the rune outright, ignoring
    its tier and prerequisite nodes. Rune nodes are not checks in new seeds: an
    item-granted rune would complete its own location and could chain another
    item to a third player. Older seeds keep the checks they were generated
    with. The tree itself stays yours, and every stat node is untouched.
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
    """Base Soulstones each filler item grants.

    Varied filler uses 50%, 75%, 125% and 150% of this value. With varied filler
    off, every filler item grants exactly this many Soulstones.
    """

    display_name = "Soulstones per Filler"
    range_start = 50
    range_end = 1000
    default = 100


class SoulstoneFillerVariety(DefaultOnToggle):
    """Split filler into four differently sized Soulstone stacks.

    The four variants average out to the same total value as the old single
    filler, so this adds variety without flooding the seed with currency.
    """

    display_name = "Varied Filler"


class SoulstoneMapsRequired(Range):
    """How many of the five standard map clears the maps goal needs."""

    display_name = "Maps Required for Victory"
    range_start = 1
    range_end = 5
    default = 5


class SoulstoneGoal(Choice):
    """What completes the seed.

    maps    - clear the first maps_required standard maps.
    portals - complete the selected Red/Purple cycle-3 portal checks.
    """

    display_name = "Victory Goal"
    option_maps = 0
    option_portals = 1
    default = 0


class SoulstonePortalGoal(Choice):
    """Which cycle-3 portal checks the portals goal requires.

    Red is the Boss Rush ladder; Purple is Endless. Ignored when goal is maps.
    """

    display_name = "Portal Goal"
    option_red = 0
    option_purple = 1
    option_both = 2
    default = 2


class SoulstoneDisabledCharacters(OptionList):
    """Valid names: CHARACTER_NAMES_PLACEHOLDER

    Character achievements to leave out of the check pool. Dropping a character
    removes every remaining check that names it: Prestige, CompleteSkillTree,
    CraftWeapon / CraftAllCharacterWeapons and CompleteMatchInLessThanTime (17
    catalog checks for a typical character; the Unlock check is already omitted
    in new seeds). Global counters like "complete 22 skill trees" stay in the
    pool. Names are case-insensitive. Barbarian is auto-unlocked and has no
    character item; every other character, including Blacksmith, is a normal
    item.
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


class SoulstoneMaxPortalCycle(Range):
    """Highest Red/Purple portal cycle that still counts as a check.

    Cycles are 1-3. 1 keeps cycle 1 and drops both families' cycle 2/3 checks;
    3 (the default) keeps all six.
    """

    display_name = "Max Portal Cycle Check"
    range_start = 1
    range_end = 3
    default = 3


class SoulstoneBalanceChecks(DefaultOnToggle):
    """Spread high-grind achievements across the playthrough.

    On (default): long checks require a growing roster of owned characters.
    Prestige bands 35/50/100/150 require 2/4/8/14 characters; a full skill tree
    requires 8; the 8/14/22 skill-tree count checks require 8/14/22; Red and
    Purple portal cycles 1/2/3 require 2/4/8. The 500,000 enemy-kill check and
    the other large lifetime counters use the same roster bands. Character-scoped
    checks also need their own character item.

    These are real item dependencies, not event gates or placement weights, and
    they add no items or ids. Character items are kept off the checks that
    require a roster count, so an achievement cannot lock its own unlock.

    Off: flat logic, where every achievement can appear in sphere 1.
    """

    display_name = "Balance High-Grind Checks"


class SoulstoneHiddenAchievements(DefaultOnToggle):
    """Include hidden achievements as checks (secret, unlisted in game).

    Character unlock achievements remain excluded even when hidden checks are on.
    """

    display_name = "Hidden Achievements"


class SoulstoneEndgameChecks(Choice):
    """Checks beyond the five maps and the achievements.

    Covers the Void Fields curse ladder (each map at increasing curse
    intensity), Unholy Cathedral rituals (Sin, Hate, Heresy) and Titan Hunt
    sigils (Corruption, Vileness, Wickedness, Hate, Void).

    off    - plain maps + achievements only.
    easy   - every map at curse 10 and 20, all three rituals, all five sigils,
             and the +N ladder for every sigil.
    medium - curse 10 to 50, randomised five-character checks, and endless up
             to +5.
    hard   - curse 10 to 62, two randomised curse/weapon combos per map, every
             ritual and sigil per character, and endless up to +7.
    insane - the hard checks with endless up to +10.
    custom - build your own with the Endgame Custom options below.

    Weapon checks are logic-gated: they need the weapon and its character, and
    the character's own unlock is never shuffled into a check it gates.
    """

    display_name = "Endgolad"
    option_off = 0
    option_easy = 1
    option_medium = 2
    option_hard = 3
    option_insane = 4
    option_custom = 5
    default = 2


class SoulstoneEndgameCurseMax(Range):
    """Custom only: highest curse level in the curse ladder (10 to 62, steps of 10).

    62 is the game's top intensity and is always added at the top when set.
    """

    display_name = "Endgame Custom: Curse Max"
    range_start = 0
    range_end = 62
    default = 50


class SoulstoneEndgameRitualMax(Range):
    """Custom only: highest Unholy Cathedral ritual tier to check (0 = none, 1 = Sin, 2 = Hate, 3 = Heresy)."""

    display_name = "Endgame Custom: Ritual Max"
    range_start = 0
    range_end = 3
    default = 3


class SoulstoneEndgameSigilMax(Range):
    """Custom only: highest Titan Hunt sigil to check (0 = none, 1 = Corruption ... 5 = Void)."""

    display_name = "Endgame Custom: Sigil Max"
    range_start = 0
    range_end = 5
    default = 5


class SoulstoneEndgameEndless(Range):
    """Custom only: how far the endless +N ladders go (Ritual of Heresy and every sigil get +1 to +N). 0 = none."""

    display_name = "Endgame Custom: Endless Max"
    range_start = 0
    range_end = 10
    default = 5


# The starter is selectable per slot. Barbarian is the game's auto-unlocked
# character (and has no `Character:` item), so it stays the default and keeps
# old YAMLs unchanged. Every other option is a normal character item: picking
# one hands it over at the start and removes it from the pool. Archipelago's
# Choice parser supplies `random`, which picks one of these options for you.
def _character_option_name(character):
    """ArcaneWeaver -> arcane_weaver, the YAML spelling of a Choice option."""
    return re.sub(r"(?<!^)(?=[A-Z])", "_", character).lower()


STARTING_CHARACTER_OPTIONS = {"barbarian": data.DEFAULT_CHARACTER}
_STARTING_CHARACTER_NAMESPACE = {
    "__module__": __name__,
    "__doc__": (
        "Which character you start with. `barbarian` keeps the game's own "
        "starter. Picking a character hands that unlock over at the start and "
        "removes its item from the pool; `random` picks one of them for you."
    ),
    "display_name": "Starting Character",
    "default": 0,
    "option_barbarian": 0,
}
for _index, _character in enumerate(data.CHARACTERS, start=1):
    _option_name = _character_option_name(_character)
    _STARTING_CHARACTER_NAMESPACE["option_" + _option_name] = _index
    STARTING_CHARACTER_OPTIONS[_option_name] = _character
    _compact_name = _character.lower()
    if _compact_name != _option_name:
        _STARTING_CHARACTER_NAMESPACE["alias_" + _compact_name] = _index

SoulstoneStartingCharacter = type(
    "SoulstoneStartingCharacter", (Choice,), _STARTING_CHARACTER_NAMESPACE
)


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
    starting_character: SoulstoneStartingCharacter
    max_prestige: SoulstoneMaxPrestige
    max_portal_cycle: SoulstoneMaxPortalCycle
    balance_checks: SoulstoneBalanceChecks
    hidden_achievements: SoulstoneHiddenAchievements
    endgame_checks: SoulstoneEndgameChecks
    endgame_curse_max: SoulstoneEndgameCurseMax
    endgame_ritual_max: SoulstoneEndgameRitualMax
    endgame_sigil_max: SoulstoneEndgameSigilMax
    endgame_endless: SoulstoneEndgameEndless
    map_unlocks: SoulstoneMapUnlocks
    curse_tier_unlocks: SoulstoneCurseTierUnlocks
    endgame_mode_unlocks: SoulstoneEndgameModeUnlocks
    weapon_unlocks: SoulstoneWeaponUnlocks
    weapon_upgrade_unlocks: SoulstoneWeaponUpgradeUnlocks
    rune_unlocks: SoulstoneRuneUnlocks
    trap_count: SoulstoneTrapCount
    resource_cache_count: SoulstoneResourceCount
    filler_soulstones: SoulstoneFillerSoulstones
    filler_variety: SoulstoneFillerVariety
    maps_required: SoulstoneMapsRequired
    goal: SoulstoneGoal
    portal_goal: SoulstonePortalGoal


class SoulstoneSurvivorsWorld(World):
    """
    Soulstone Survivors is a bullet-heaven survivor game. This world randomizes
    which characters and weapons you have access to: every character and weapon
    beyond the starting one is an item, and clearing maps / earning achievements
    are the checks. The goal is either clearing the first maps_required standard
    maps, or completing the selected Red/Purple cycle-3 portal checks.
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
        self.starting_character = STARTING_CHARACTER_OPTIONS[
            self.options.starting_character.current_key
        ]
        self.enabled_achievements = data.select_achievements(
            excluded_characters=excluded,
            max_prestige=self.options.max_prestige.value,
            include_hidden=bool(self.options.hidden_achievements.value),
            max_portal_cycle=self.options.max_portal_cycle.value,
        )

        # Receiving `Character: <name>` runs the game's unlock scans, which
        # complete that character's Unlock achievement and the character-count
        # milestones. Those checks would pay out from the item itself, so new
        # seeds leave them out. Older seeds keep the checks they were generated
        # with, since their slot data still names them.
        before = len(self.enabled_achievements)
        self.enabled_achievements = [
            achievement_id
            for achievement_id in self.enabled_achievements
            if not data.is_character_unlock(achievement_id)
        ]
        removed_unlock_checks = before - len(self.enabled_achievements)
        if removed_unlock_checks:
            logging.info(
                "%s: %d character unlock achievement(s) removed from the pool",
                self.player_name,
                removed_unlock_checks,
            )

        # Weapons as items means the blacksmith is not part of the seed, so the
        # achievements that ask the player to craft weapons can never fire and
        # must not sit in the pool as dead checks.
        if self.options.weapon_unlocks.value:
            before = len(self.enabled_achievements)
            self.enabled_achievements = [
                achievement_id
                for achievement_id in self.enabled_achievements
                if not (
                    achievement_id.startswith("CraftAllCharacterWeapons")
                    or achievement_id.startswith("CraftWeapon")
                )
            ]
            logging.info(
                "%s: weapons are items, %d craft achievement(s) removed from the pool",
                self.player_name,
                before - len(self.enabled_achievements),
            )

        self.goal = self.options.goal.current_key
        self.portal_goal = self.options.portal_goal.current_key
        if self.goal == "portals":
            portal_achievements = _PORTAL_GOAL_ACHIEVEMENTS[self.portal_goal]
            missing = [
                achievement_id
                for achievement_id in portal_achievements
                if achievement_id not in self.enabled_achievements
            ]
            if not self.options.achievement_checks.value or missing:
                raise OptionError(
                    "Soulstone Survivors: goal portals needs the cycle-3 checks enabled. "
                    "Set achievement_checks: true and max_portal_cycle: 3."
                )
            self.goal_locations = [
                ACHIEVEMENT_PREFIX + data.achievement_location_name(achievement_id)
                for achievement_id in portal_achievements
            ]
        else:
            required = max(1, min(self.options.maps_required.value, len(MAP_LOCATIONS)))
            self.goal_locations = MAP_LOCATIONS[:required]

        # High-prestige, skill-tree and large lifetime-counter checks require a
        # growing roster of owned characters when balance_checks is on. This is
        # a real item dependency, so it delays the checks in fill and in the
        # playthrough without adding items or changing ids.
        self.balance_requirements = {}
        if self.options.balance_checks.value and self.options.achievement_checks.value:
            for achievement_id in self.enabled_achievements:
                tier = balance.gate_tier(achievement_id)
                if tier is None:
                    continue
                self.balance_requirements[achievement_id] = tier
            logging.info(
                "%s: balance checks on, %d long achievement(s) behind roster gates",
                self.player_name,
                len(self.balance_requirements),
            )

        self.endgame_difficulty = self.options.endgame_checks.current_key
        self.endgame_characters = [
            character for character in data.CHARACTERS if character not in excluded
        ]
        profile = endgame.profile_for(
            self.endgame_difficulty,
            curse_max=self.options.endgame_curse_max.value,
            ritual_max=self.options.endgame_ritual_max.value,
            sigil_max=self.options.endgame_sigil_max.value,
            endless=self.options.endgame_endless.value,
        )
        self.endgame_entries = endgame.build(
            self,
            profile,
            self.endgame_characters,
            bool(self.options.weapon_unlocks.value),
        )
        self.endgame_logic_weapons = endgame.logic_weapons(self.endgame_entries)

        # Only meaningful when the seed actually has ritual / sigil checks.
        self.gate_curse_tiers = bool(self.options.curse_tier_unlocks.value) and bool(CURSE_TIER_ITEMS)
        self.weapon_cap_unlocks = bool(self.options.weapon_upgrade_unlocks.value) and bool(WEAPON_CAP_ITEMS)
        self.maps_as_items = bool(self.options.map_unlocks.value)

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
            "max portal cycle: %d, hidden: %s)",
            self.player_name,
            len(self.enabled_achievements),
            len(data.ACHIEVEMENTS),
            ", ".join(excluded) or "none",
            self.options.max_prestige.value,
            self.options.max_portal_cycle.value,
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
            names += [
                ACHIEVEMENT_PREFIX + data.achievement_location_name(achievement)
                for achievement in self.enabled_achievements
            ]
        # Rune unlocks are items only. A matching location would complete as
        # soon as the rune item arrived and could chain another item to a third
        # player. Older seeds keep the checks they were generated with.
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

        # The chosen starter is handed over immediately and its item leaves the
        # pool. Barbarian has no item, so the default seed is unchanged.
        starter_item = CHARACTER_PREFIX + self.starting_character
        if starter_item in names:
            names.remove(starter_item)
            self.multiworld.push_precollected(self.create_item(starter_item))

        if self.maps_as_items:
            # The first map is given at the start; the rest come from other players.
            start_map = MAP_ITEM_PREFIX + data.MAPS[0]
            self.multiworld.push_precollected(self.create_item(start_map))
            names += [name for name in MAP_ITEMS if name != start_map]
        if self.gate_curse_tiers:
            names += CURSE_TIER_ITEMS
        if self.gate_endgame_modes:
            names += MODE_ITEMS
        if self.weapon_cap_unlocks:
            names += WEAPON_CAP_ITEMS
        if self.options.weapon_unlocks.value:
            # Mode A: weapons arrive as items; craft achievements were filtered out.
            names += WEAPON_ITEMS
        else:
            # Mode B: crafting works like vanilla, one licence per character.
            names += CRAFT_ITEMS
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
            missing = location_count - len(pool)
            if self.options.filler_variety.value and FILLER_VARIETY_ITEMS:
                # Cycle the variants so every name appears equally often, then
                # shuffle twice so neither the names nor the order are predictable.
                variants = list(FILLER_VARIETY_ITEMS)
                self.random.shuffle(variants)
                filler_names = [variants[index % len(variants)] for index in range(missing)]
                self.random.shuffle(filler_names)
                pool += [self.create_item(name) for name in filler_names]
            else:
                pool += [self.create_item(FILLER_NAME) for _ in range(missing)]
        elif len(pool) > location_count:
            # More unlocks than checks (unusual, e.g. maps-only): keep map
            # items because every map check is gated by one, then fill the
            # remaining slots in pool order.
            map_names = {MAP_ITEM_PREFIX + map_id for map_id in data.MAPS}
            map_items = [item for item in pool if item.name in map_names]
            other_items = [item for item in pool if item.name not in map_names]
            pool = (map_items + other_items)[:location_count]

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

        # The tier V and VI map achievements are high-curse clears. While curse
        # tiers are items, require the matching tier so neither can sit in
        # sphere 1. Without that option there is no tier item to wait for.
        if self.gate_curse_tiers and self.options.achievement_checks.value:
            for achievement_id in self.enabled_achievements:
                tier = data.affix_achievement_tier(achievement_id)
                if tier not in (5, 6):
                    continue
                tier_item = CURSE_TIER_ITEM_BY_NUMBER.get(tier)
                if not tier_item:
                    continue
                name = ACHIEVEMENT_PREFIX + data.achievement_location_name(achievement_id)
                if name not in self.location_name_to_id:
                    continue
                location = self.multiworld.get_location(name, self.player)
                old_rule = location.access_rule

                def access(state, tier_item=tier_item, old_rule=old_rule, player=self.player):
                    return state.has(tier_item, player) and old_rule(state)

                location.access_rule = access

                # Keep every tier item off the high-tier checks. Blocking only
                # the matching item would still allow V and VI to swap checks
                # and lock each other out.
                blocked_tiers = frozenset(CURSE_TIER_ITEMS)
                old_item_rule = location.item_rule

                def item_rule(item, blocked=blocked_tiers, old_item_rule=old_item_rule, player=self.player):
                    return old_item_rule(item) and not (
                        item.player == player and item.name in blocked
                    )

                location.item_rule = item_rule

        # Mode B (weapons are crafted, not sent): the blacksmith only crafts a
        # character's weapons once that character's licence arrived, so every craft
        # achievement waits on it. Mode A filtered these achievements out already.
        if not self.options.weapon_unlocks.value and self.options.achievement_checks.value:
            for achievement_id in self.enabled_achievements:
                if not (
                    achievement_id.startswith("CraftAllCharacterWeapons")
                    or achievement_id.startswith("CraftWeapon")
                ):
                    continue
                character = data.achievement_character(achievement_id)
                if not character:
                    continue
                name = ACHIEVEMENT_PREFIX + data.achievement_location_name(achievement_id)
                if name not in self.location_name_to_id:
                    continue
                location = self.multiworld.get_location(name, self.player)
                gate = CRAFT_PREFIX + character

                def access(state, gate=gate, player=self.player):
                    return state.has(gate, player)

                location.access_rule = access

        # "Weapons upgraded to Artifact rarity" needs a level no early cap allows.
        # The top finite cap (+15) is the requirement rather than a measured
        # threshold, because the game only exposes rarity-per-level at runtime.
        if self.weapon_cap_unlocks and self.options.achievement_checks.value:
            for achievement_id in (
                "WeaponsUpgradedToArtifactRarityCount10",
                "WeaponsUpgradedToArtifactRarityCount30",
                "WeaponsUpgradedToArtifactRarityCount60",
            ):
                name = ACHIEVEMENT_PREFIX + data.achievement_location_name(achievement_id)
                if name not in self.location_name_to_id:
                    continue
                location = self.multiworld.get_location(name, self.player)

                def access(state, player=self.player):
                    return state.has(WEAPON_CAP_TOP, player)

                location.access_rule = access

        # Long checks require a growing roster of owned characters. The exact
        # count comes from balance.roster_requirement: Count8/14/22 use 8/14/22,
        # full skill trees use 8, portal cycles use 2/4/8, and prestige/counter
        # bands use 2/4/8/14.
        character_items = [
            name for name in item_name_to_id if name.startswith(CHARACTER_PREFIX)
        ]
        character_item_set = set(character_items)

        def has_roster(state, count, player):
            return sum(1 for item in character_items if state.has(item, player)) >= count

        for achievement_id, tier in self.balance_requirements.items():
            name = ACHIEVEMENT_PREFIX + data.achievement_location_name(achievement_id)
            if name not in self.location_name_to_id:
                continue
            location = self.multiworld.get_location(name, self.player)
            count = min(balance.roster_requirement(achievement_id, tier), len(character_items))
            old_rule = location.access_rule

            def access(state, count=count, old_rule=old_rule, player=self.player):
                return has_roster(state, count, player) and old_rule(state)

            location.access_rule = access

            old_item_rule = location.item_rule

            def item_rule(item, old_item_rule=old_item_rule, blocked=character_item_set, player=self.player):
                return old_item_rule(item) and not (item.player == player and item.name in blocked)

            location.item_rule = item_rule

        # Every character-scoped achievement also needs that character's item.
        # Character items are blocked from every character-scoped check, so a
        # character cannot be the reward for a location that needs it, and a
        # roster-gated check cannot hold one of the characters it requires.
        # Barbarian is auto-unlocked and has no character item, but its checks
        # still get the item rule and the roster gate above.
        if self.options.achievement_checks.value:
            for achievement_id in self.enabled_achievements:
                character = data.achievement_character(achievement_id)
                if not character:
                    continue
                name = ACHIEVEMENT_PREFIX + data.achievement_location_name(achievement_id)
                if name not in self.location_name_to_id:
                    continue
                location = self.multiworld.get_location(name, self.player)
                old_rule = location.access_rule
                character_item = CHARACTER_PREFIX + character
                has_character_item = character_item in item_name_to_id

                def access(state, character_item=character_item, has_item=has_character_item, old_rule=old_rule, player=self.player):
                    return (not has_item or state.has(character_item, player)) and old_rule(state)

                location.access_rule = access

                blocked = set(character_item_set)
                if (
                    not self.options.weapon_unlocks.value
                    and (
                        achievement_id.startswith("CraftAllCharacterWeapons")
                        or achievement_id.startswith("CraftWeapon")
                    )
                ):
                    blocked.add(CRAFT_PREFIX + character)

                old_item_rule = location.item_rule

                def item_rule(item, blocked=blocked, old_item_rule=old_item_rule, player=self.player):
                    return old_item_rule(item) and not (item.player == player and item.name in blocked)

                location.item_rule = item_rule

        victory = self.multiworld.get_location("Victory", self.player)
        victory.access_rule = lambda state: all(
            state.can_reach(name, "Location", self.player) for name in self.goal_locations
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
        amounts = filler_amounts(self.options.filler_soulstones.value)
        for name, item_id in item_name_to_id.items():
            if name.startswith(CHARACTER_PREFIX):
                items[str(item_id)] = {"kind": "char", "value": name[len(CHARACTER_PREFIX):]}
            elif name.startswith(WEAPON_PREFIX):
                items[str(item_id)] = {"kind": "weapon", "value": name[len(WEAPON_PREFIX):]}
            elif name in CURSE_TIER_ITEM_VALUES:
                items[str(item_id)] = {"kind": "curse", "value": CURSE_TIER_ITEM_VALUES[name]}
            elif name in WEAPON_CAP_ITEM_VALUES:
                items[str(item_id)] = {"kind": "weaponcap", "value": WEAPON_CAP_ITEM_VALUES[name]}
            elif name.startswith(CRAFT_PREFIX):
                items[str(item_id)] = {"kind": "craft", "value": name[len(CRAFT_PREFIX):]}
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
            elif name in amounts:
                items[str(item_id)] = {"kind": "souls", "value": str(amounts[name])}
            else:
                items[str(item_id)] = {"kind": "souls", "value": str(self.options.filler_soulstones.value)}

        maps = {map_id: location_name_to_id[MAP_PREFIX + map_id] for map_id in data.MAPS}
        # Empty when the seed is maps-only, so neither client can report an
        # achievement location the seed did not create.
        achievements = {}
        if self.options.achievement_checks.value:
            achievements = {
                achievement_id: location_name_to_id[
                    ACHIEVEMENT_PREFIX + data.achievement_location_name(achievement_id)
                ]
                for achievement_id in self.enabled_achievements
            }

        # New seeds have no rune locations. The empty table is explicit: older
        # seeds shipped a non-empty one, and the Python client uses it to keep
        # reporting those legacy checks.
        runes = {}

        # Per-check metadata for the in-game browser: where each check belongs and
        # which items have to arrive before it can be done. Keyed by location id so
        # the plugin never has to reproduce the world's naming rules.
        from .Items import MODE_ITEM_FOR_KIND

        info = {}
        for map_id in data.MAPS:
            name = MAP_PREFIX + map_id
            info[str(location_name_to_id[name])] = {
                "name": name,
                "map": map_id,
                "character": "",
                "needs": [MAP_ITEM_PREFIX + map_id] if self.maps_as_items else [],
            }

        if self.options.achievement_checks.value:
            for achievement_id in self.enabled_achievements:
                name = ACHIEVEMENT_PREFIX + data.achievement_location_name(achievement_id)
                character = data.achievement_character(achievement_id) or ""
                needs = []
                if character and CHARACTER_PREFIX + character in item_name_to_id:
                    needs.append(CHARACTER_PREFIX + character)
                tier = data.affix_achievement_tier(achievement_id)
                if self.gate_curse_tiers and tier in (5, 6):
                    tier_item = CURSE_TIER_ITEM_BY_NUMBER.get(tier)
                    if tier_item:
                        needs.append(tier_item)
                if (
                    not self.options.weapon_unlocks.value
                    and character
                    and (
                        achievement_id.startswith("CraftAllCharacterWeapons")
                        or achievement_id.startswith("CraftWeapon")
                    )
                ):
                    needs.append(CRAFT_PREFIX + character)
                if self.weapon_cap_unlocks and achievement_id in (
                    "WeaponsUpgradedToArtifactRarityCount10",
                    "WeaponsUpgradedToArtifactRarityCount30",
                    "WeaponsUpgradedToArtifactRarityCount60",
                ):
                    needs.append(WEAPON_CAP_TOP)
                info[str(location_name_to_id[name])] = {
                    "name": name, "map": "", "character": character, "needs": needs,
                }

        # No rune entries in location_info: new seeds do not create those checks.

        for entry in self.endgame_entries:
            spec = entry.spec
            kind = spec.get("kind", "")
            character = spec.get("character") or ""
            if not isinstance(character, str):
                character = ""
            needs = []
            if self.gate_endgame_modes:
                mode_item = MODE_ITEM_FOR_KIND.get(kind)
                if mode_item:
                    needs.append(mode_item)
            weapon = spec.get("weapon")
            if weapon and self.options.weapon_unlocks.value:
                needs.append(WEAPON_PREFIX + weapon)
            if character:
                needs.append(CHARACTER_PREFIX + character)
            map_label = spec.get("map") or (
                "Unholy Cathedral" if kind == "ritual" else "Titan Hunt" if kind == "titan" else ""
            )
            info[str(entry.id)] = {
                "name": entry.name,
                "map": map_label,
                "character": character,
                "needs": needs,
            }

        return {
            "game_version": GAME_VERSION,
            "has_catalog": data.HAS_CATALOG,
            "starting_character": self.starting_character,
            "death_link": bool(self.options.death_link.value),
            "knockback_link": bool(self.options.knockback_link.value),
            "balance_checks": bool(self.options.balance_checks.value),
            # New seeds use the CompleteCurseLevel and red/purple portal labels
            # for those checks. The Python client uses the legacy labels when
            # this is absent, which is how old seeds keep reporting.
            "curse_level_names": True,
            "items": items,
            "maps": maps,
            "achievements": achievements,
            "runes": runes,
            "maps_default": not self.maps_as_items,
            "goal": self.goal,
            "portal_goal": self.portal_goal,
            "goal_label": "portals" if self.goal == "portals" else "maps",
            "goal_locations": [
                location_name_to_id[name] for name in self.goal_locations
            ],
            # Older clients read goal_maps; they keep map seeds working and
            # simply show no goal chip on a portal seed.
            "goal_maps": [] if self.goal == "portals" else list(
                data.MAPS[:max(1, min(self.options.maps_required.value, len(data.MAPS)))]
            ),
            # False = weapons arrive as items, so crafting an unknown weapon at the
            # blacksmith is a self-unlock and the plugin refuses it.
            "weapons_default": not bool(self.options.weapon_unlocks.value),
            # True = weapons are crafted, so the blacksmith needs
            # `Craft: <Character>` before it will craft that character's weapons.
            "craft_licences": not bool(self.options.weapon_unlocks.value),
            "location_info": info,
            "endgame": endgame.slot_data(self.endgame_entries),
        }
