"""Artificial progression gates for long achievement checks.

Every achievement normally sits in the starting region, so a multiworld can put
another player's progression item on a Prestige 150 or 500,000-kill check. Those
checks take hours, and everyone waiting on that item stalls with them.

Archipelago's address-less event locations are collected in the same sweep as
the checks they unlock, so an event chain cannot actually hold a check back.
Instead these gates require real pooled character items: the more characters a
player owns, the later the long checks become reachable.

    balance band 35  requires  2 owned characters
    balance band 50  requires  4 owned characters
    balance band 100 requires  8 owned characters
    balance band 150 requires 14 owned characters

Full skill trees require 8 characters. The skill-tree count milestones require
their own count: 8, 14, or 22 characters. Red and Purple portal cycles 1-3
require 2, 4 or 8 owned characters. Character-scoped checks also require their
own character item.

These are logic gates, not placement weights, and they add no new items or ids.
Character items are kept off the checks that require a roster count, so a
character cannot be locked behind an achievement that needs it.
"""
from __future__ import annotations

import re

from . import data

# The first value is the ungated starting band. Each later value is the highest
# prestige tier that the corresponding balance band covers.
BANDS = (20, 35, 50, 100, 150)

# Minimum owned characters for each gated balance band.
ROSTER_BANDS = {
    35: 2,
    50: 4,
    100: 8,
    150: 14,
}

# A complete character skill tree is a long grind. Eight characters makes it a
# mid/late check without tying it to one specific roster member.
SKILL_TREE_ROSTER = 8

# Portal cycles are endgame achievements even though their ids carry no
# lifetime counter. Use the existing roster bands for their 2/4/8 gates.
PORTAL_CYCLE_BANDS = {1: 35, 2: 50, 3: 100}
PORTAL_CYCLE_ROSTER = {1: 2, 2: 4, 3: 8}

# Threshold -> highest band. A check at or below the threshold is held until
# the matching roster band is reached. The first entry for a family is the base
# band.
COUNTER_BANDS = {
    "ReachTotalEnemiesKilled": (
        (10_000, 20),
        (75_000, 35),
        (100_000, 50),
        (300_000, 100),
        (500_000, 150),
    ),
    "ReachTotalBossesKilled": (
        (200, 20),
        (400, 50),
        (600, 100),
        (700, 150),
    ),
    "ReachTotalElitesKilled": (
        (500, 20),
        (1_000, 35),
        (1_500, 50),
        (2_000, 100),
        (3_000, 150),
    ),
}

_COUNTER_PATTERNS = {
    prefix: re.compile(r"^" + re.escape(prefix) + r"(\d+)$")
    for prefix in COUNTER_BANDS
}
_SKILL_TREE_COUNT = re.compile(r"^CompleteSkillTreeCount(\d+)$")


def prestige_gate(level: int | None) -> int | None:
    """Balance band for a prestige achievement, or None when it is base-band."""
    if level is None:
        return None
    for tier in BANDS:
        if level <= tier:
            return None if tier == BANDS[0] else tier
    return BANDS[-1]


def counter_gate(achievement_id: str) -> int | None:
    """Balance band for a lifetime-kill achievement, or None when base-band."""
    for prefix, bands in COUNTER_BANDS.items():
        match = _COUNTER_PATTERNS[prefix].match(achievement_id)
        if match is None:
            continue
        count = int(match.group(1))
        for limit, tier in bands:
            if count <= limit:
                return None if tier == BANDS[0] else tier
        return bands[-1][1]
    return None


def skill_tree_gate(achievement_id: str) -> int | None:
    """Balance band for a full skill tree or a character-count milestone."""
    match = _SKILL_TREE_COUNT.match(achievement_id or "")
    if match:
        count = int(match.group(1))
        for limit, tier in (
            (8, 50),
            (14, 100),
            (22, 150),
        ):
            if count <= limit:
                return tier
        return 150
    if (achievement_id or "").startswith("CompleteSkillTree") and data.achievement_character(achievement_id):
        return 50
    return None


def portal_cycle_gate(achievement_id: str) -> int | None:
    """Balance band for a Boss Rush / Endless cycle, if it is 1-3."""
    return PORTAL_CYCLE_BANDS.get(data.portal_cycle_number(achievement_id))


def gate_tier(achievement_id: str) -> int | None:
    """The balance band needed by one achievement, if any."""
    return (
        prestige_gate(data.achievement_prestige_tier(achievement_id))
        or skill_tree_gate(achievement_id)
        or counter_gate(achievement_id)
        or portal_cycle_gate(achievement_id)
    )


def roster_requirement(achievement_id: str, tier: int | None) -> int:
    """Owned characters needed by one gated achievement.

    The count milestones use their own count, full skill trees use the shared
    skill-tree roster, and portal cycles use their 2/4/8 table. Everything else
    follows the band table.
    """
    cycle = data.portal_cycle_number(achievement_id)
    if cycle in PORTAL_CYCLE_ROSTER:
        return PORTAL_CYCLE_ROSTER[cycle]
    match = _SKILL_TREE_COUNT.match(achievement_id or "")
    if match:
        return int(match.group(1))
    if (achievement_id or "").startswith("CompleteSkillTree") and data.achievement_character(achievement_id):
        return SKILL_TREE_ROSTER
    return ROSTER_BANDS.get(tier or 0, 0)
