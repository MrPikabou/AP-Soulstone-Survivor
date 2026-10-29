# Soulstone Survivors Archipelago

An in-game client for [Archipelago](https://archipelago.gg) multiworld randomizer runs in
Soulstone Survivors. Unlocks that the game would normally hand you are instead delivered by
other players' checks, and the mod gives you a connect panel inside the game.

This mod is the **game side**. To share or join a run you also need the
`Soulstone Survivors` world file (`soulstone_survivors.apworld`) for the Archipelago
program that generates the seed — the host installs it, players joining a room do not.

## Requirements

- Soulstone Survivors (Steam), game version **1.5d2**. The mod patches game internals, so a
  game update can break it until it is rebuilt.
- The BepInEx **IL2CPP x64** pack, which the mod manager installs for you as a dependency.
- The Archipelago world file on the machine that generates the seed.

## Install

Install through the Thunderstore Mod Manager or r2modman like any other mod — the BepInEx pack
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
| Characters (22 of 23, Barbarian is your starter) | `Character: <name>` |
| Blacksmith weapons (tiers 2-5) | `Weapon: <id>` |
| Skill tree rune nodes | `Rune: <id>` |
| Unholy Cathedral / Titan Hunt | mode items |
| Curse tiers I-VII | `Curse Tier <n>` |
| Blacksmith upgrade ceiling | `Weapon Upgrade +3 ... Unlimited` |
| The five Void Fields maps | `Map: <mapId>` |

Most of them can be turned back into vanilla behaviour per seed with the world's options
(`weapon_unlocks`, `rune_unlocks`, `map_unlocks`, `curse_tier_unlocks`,
`weapon_upgrade_unlocks`, `endgame_mode_unlocks`), and the mod follows what the seed says.

Also in the pool: **Resource Cache** (10% of a random resource you already hold, or roughly one
character level of XP), soulstone fillers, and traps.

## Links

- DeathLink: a death elsewhere offers you a choice of three negative Powers for your current run.
- KnockbackLink: hits shove and are shoved.
- Traps: elite ambushes, boss summons, swarms, and forced negative Powers.

## Config highlights

| Key | Default | Effect |
| --- | --- | --- |
| `Safety/AllowedProfileIndex` | `1` | The only save slot the mod may touch |
| `Safety/EnableGrants` | `true` | Allow items to be written to the save |
| `Gating/ItemOnlyUnlocks` | `true` | Characters, runes and endgame modes come from items only |
| `Gating/ItemOnlyMaps` / `ItemOnlyWeapons` | `true` | A map / weapon needs its item before it can be played or crafted |
| `Blacksmith/IgnoreWeaponLevelRequirements` | `true` | Weapons you own can be upgraded without the character-level gate |
| `Archipelago/AutoConnect` | `false` | Join the saved server/slot automatically on launch |
| `Panel/OpenAfterSaveLoad` | `true` | Pop the panel up once after your save loads |

## Notes

- Unlocks already present in a save are never revoked; the mod only blocks new ones.
- Traps act on the live run, so they land when they arrive rather than being queued.
- Character tooltips still mention achievement requirements; only the behaviour changed.
