# Soulstone Survivors Archipelago

An in-game client for [Archipelago](https://archipelago.gg) multiworld randomizer runs in
Soulstone Survivors. Unlocks that the game would normally hand you are instead delivered by
other players' checks, and the mod gives you a connect panel inside the game.

This mod is the **game side**. To share or join a run you also need the
`Soulstone Survivors` world file (`soulstone_survivors.apworld`) for the Archipelago
program that generates the seed - the host installs it, players joining a room do not.
APWorld --> `https://github.com/MrPikabou/AP-Soulstone-Survivor/releases`

## Requirements

- Soulstone Survivors (Steam), game version **1.5d2**. The mod patches game internals, so a
  game update can break it until it is rebuilt.
- The BepInEx **IL2CPP x64** pack, which the mod manager installs for you as a dependency.
- The Archipelago world file on the machine that generates the seed.

## Install

Install through the Thunderstore Mod Manager or r2modman like any other mod - the BepInEx pack
comes with it. Launch the game once, then edit `BepInEx/config/soulstone.ap.bridge.cfg` if you
want to change defaults.

**It only ever writes to one save slot.** `Safety/AllowedProfileIndex` defaults to `1`, meaning
the **second** save slot in the game's list; slot `0` is never read or written. Create a fresh
run in that slot for the Archipelago seed, and leave your main save alone.

## Connecting

1. Pick the save in the slot the mod is allowed to touch.
2. Press **F8** to open the Archipelago panel (it also opens once by itself after a save loads).
3. Enter `host:port`, the slot name and the password, then **Connect**.

The status line shows the mod version, your slot, how many items the seed has and how many
endgame checks it carries. Once connected, received items are applied to the save and the checks
you complete are sent automatically.

## What comes from other players

With a default seed, all of these are items and the local routes are blocked:

| Unlock | How it arrives |
| --- | --- |
| Characters (22 randomizable; Barbarian is the game's free starter) | `Character: <name>` |
| Blacksmith weapons (weapons-as-items mode) | `Weapon: <id>` |
| Blacksmith crafting licence (crafting mode) | `Craft: <Character>` |
| Skill tree rune nodes (items only) | `Rune: <id>` |
| Unholy Cathedral / Titan Hunt | mode items |
| Curse tiers I-VII | `Curse Tier <n>` |
| Blacksmith upgrade ceiling | `Weapon Upgrade +3 ... Unlimited` |
| The five Void Fields maps | `Map: <mapId>` |

Most of them can be turned back into vanilla behaviour per seed with the world's options
(`weapon_unlocks`, `rune_unlocks`, `map_unlocks`, `curse_tier_unlocks`,
`weapon_upgrade_unlocks`, `endgame_mode_unlocks`), and the mod follows what the seed says.

New seeds also balance the longest achievement checks. `balance_checks` (default on)
gates high prestige behind a growing owned-character roster: 35/50/100/150 need
2/4/8/14 characters, full skill trees need 8, the 8/14/22 skill-tree counts need
8/14/22, and the Red/Purple portal cycles 1/2/3 need 2/4/8. The 500,000 enemy-kill
check and the other huge lifetime counters use the same roster bands. These are real
character-item requirements, not event gates or new items.

New seeds no longer create `Unlock<character>` or `UnlockCharacterCount<number>` checks.
Receiving a `Character: <name>` item runs the game's unlock scans, which complete those
achievements, so keeping them would let the item complete its own check and chain another
item to a third player. Older seeds keep their generated unlock checks; regenerate to remove
them.

Character-scoped achievement checks now require that character's item in logic, so a
character unlock cannot be placed on its own prestige, skill-tree, craft or match check.
Character items are also kept off every check that requires a character roster, preventing
roster checks from locking a required character. Barbarian is auto-unlocked and has no
item, so its checks do not require one, but they still use the roster gate.

`starting_character` picks your starter. The default `barbarian` keeps the game's free
character; any catalog character (snake_case, e.g. `arcane_weaver`) starts unlocked from
the multiworld and its item is removed from the pool, while `random` chooses one of the
23. Barbarian is always unlocked by the game, so another starter is added on top of it.

Map affix-tier achievements are shown as `CompleteCurseLevel<Map><Tier>` (for example,
`CompleteCurseLevelCavern2`). The game-side ids are unchanged. With curse tiers as
items, the tier V and VI map checks require `Curse Tier V`/`VI`, so they are not in
sphere 1.

Boss Rush and Endless cycle checks are shown as `CompleteRedPortalCycle<N>` and
`CompletePurplePortalCycle<N>`. The underlying `CompleteBossRushCycle` and
`CompleteEndlessCycle` ids are unchanged, and older seeds keep their original labels.
`max_portal_cycle` caps the ladder: `1` keeps only the two cycle-1 checks and drops
cycles 2 and 3; the default `3` keeps all six.

`goal: portals` is an alternative victory path. `portal_goal` selects `red`,
`purple`, or `both` cycle-3 portal checks (default `both`). It needs
`achievement_checks: true` and `max_portal_cycle: 3`; the HUD then shows
`n/N portals` instead of `n/N maps`.

New seeds also no longer create `Rune:` locations, so receiving a rune item cannot complete
a check and chain another item to a third player. Older seeds keep the rune checks they were
generated with; regenerate to remove the chain.

Also in the pool: **Resource Cache** (10% of a random resource you already hold, or roughly one
character level of XP), varied soulstone fillers (`Shard`, `Cluster`, `Cache`, `Hoard`), and
traps. Filler variety is on by default and uses 50/75/125/150% of the seed's
`filler_soulstones` value; turn `filler_variety` off for the old single stack.

## Links

- DeathLink: a death elsewhere offers you a choice of three negative Powers for your current run, including screen burdens such as blur, colourblind filters, deep-fried visuals, a 5-second blackout, a blacked-out half of the screen, and inverted movement controls.
- KnockbackLink: hits shove and are shoved.
- Traps: elite ambushes, boss summons, swarms, and forced negative Powers.

## Config highlights

| Key | Default | Effect |
| --- | --- | --- |
| `Safety/AllowedProfileIndex` | `1` | The only save slot the mod may touch |
| `Safety/EnableGrants` | `true` | Allow items to be written to the save |
| `Gating/ItemOnlyUnlocks` | `true` | Characters, runes and endgame modes come from items only |
| `Gating/ItemOnlyMaps` / `ItemOnlyWeapons` | `true` | A map needs its item before it can be played; a weapon needs its item (weapons-as-items mode) or its character's `Craft:` licence (crafting mode) before the blacksmith will make it |
| `Blacksmith/IgnoreWeaponLevelRequirements` | `true` | Weapons you own can be upgraded without the character-level gate |
| `Archipelago/AutoConnect` | `false` | Join the saved server/slot automatically on launch |
| `Panel/OpenAfterSaveLoad` | `true` | Pop the panel up once after your save loads |
| `HUD/Sounds` | `true` | Short tone for progression items and traps |

## Notes

- Unlocks already present in a save are never revoked; the mod only blocks new ones.
- Traps act on the live run, so they land when they arrive rather than being queued.
- Character tooltips still mention achievement requirements; only the behaviour changed.

### Weapon modes

`weapon_unlocks` picks one of two exclusive modes.

- **On (default), weapons are items.** The 88 `Weapon: <id>` unlocks arrive as
  useful items, and all 110 craft achievements (22 craft-all, 88 craft-weapon)
  are removed from the check pool, because crafting is not part of that seed.
- **Off, weapons are crafted.** No weapon items. The blacksmith works like the
  normal game, and a `Craft: <Character>` licence (22 items, progression) is
  needed before that character's weapons can be made. The 110 craft achievements
  stay as checks, gated on the licence.

Licences are kept in the mod config so they survive a restart, and cleared when
the seed name changes. Test one with the bridge:

```
GRANT item=Craft:Barbarian
```

### Check list on the map and character screens

Map cards and character tiles carry a small `! done/total` button in their
top-left corner. Clicking it opens a check window for that map or character,
grouped into doable now / blocked (with the missing items named) / completed.
Escape or X closes it.

F8 is back to Checks and Console only, with a `Global` button for checks that belong to
no map or character (portal cycles, lifetime counters, skill-tree counts and other global
achievements). Under the log are `Hint`, `Missing`,
`Goal`, `Release` and `Collect` buttons; they send the matching server command
and put its reply in the log. A top-right HUD shows the connection dot, server,
retry countdown and goal progress (`n/N maps` or `n/N portals`).

Unholy Cathedral and Titan Hunt mode panels carry the same button, listing
ritual and sigil checks.

Unlocked runes show their full in-game logo at the skill tree node, on top of the smoke, scaled with zoom.

F9 toggles a top-left feed of the last items and checks, with rune art, character portraits and the AP logo. Rows and F8 log lines are coloured by classification (gold progression, blue useful, grey filler, red trap, green check), and `HUD/Sounds` can play a short tone for progression items and traps.
