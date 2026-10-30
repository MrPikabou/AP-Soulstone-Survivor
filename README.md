# Soulstone Survivors × Archipelago

Turns Soulstone Survivors (Steam, 1.5d2 / 1.5.3.2, IL2CPP) into an Archipelago
custom game: characters and weapons become items, map clears and achievements
become checks.

Everything here lives in your save folder (`...\LocalLow\Game Smithing\Soulstone Survivors\ap-soulstone`).
The game ignores unknown folders, so it is safe to keep it there.

## Safety

* **Save slot 0 is never touched.** The plugin refuses to read or write any slot
  other than `AllowedProfileIndex` (default `1`), enforced on every command.
* A copy of the slot-1 saves from before any modding is in
  `backups/profile1-baseline/`.
* Nothing is deleted from a save; items are only ever added.

## Layout

| Path | What it is |
| --- | --- |
| `mod/` | BepInEx IL2CPP plugin (`SoulstoneAP.dll`) that reads the live save and applies items |
| `world/soulstone_survivors/` | Archipelago world package (items, locations, goal) + client |
| `world/soulstone_survivors/catalog.json` | Item/location ids dumped from the installed game build |
| `tools/` | Dev helpers: metadata dumper, bridge probe, catalog dump, server/client stdio feeders |
| `.tools/` | Local .NET SDK, Archipelago 0.6.8 source, Python venv |
| `thunderstore/` | Manifest, page README, changelog and 256×256 icon for the public upload |
| `deploy.sh` / `deploy_world.sh` | Build+install the plugin / copy the world into Archipelago |
| `play.sh` | Start the Archipelago server and the game client together |
| `launcher.py` | Desktop window: pick a seed, start the server, connect a slot, launch the game |

## How it works

There are two equivalent ways to play: straight from the game, or via the Python
client. Both apply the same items and report the same checks, so you can use
whichever you prefer (running both is harmless — the server de-duplicates).

**In game (recommended).** The plugin talks to the server itself:

```
Archipelago server  <-- websocket -->  SoulstoneAP.dll  -->  live save (slot 1)
```

An **Archipelago panel with the AP logo** opens once the save you picked has
loaded, so it does not sit on top of the save-slot screen. Enter the server
`host:port`, your slot name and password, and hit Connect. **F8** toggles the
panel at any time (including on the save-slot screen). The panel is built with
uGUI + TextMeshPro, but its text
fields are **not** TMP_InputField: the game's menu logic reassigns the
EventSystem selection every frame (click selects all, then deselects), so the
fields capture keystrokes directly from the Input System into their own buffers.
Click a field to focus it, click outside the panel to drop focus. IMGUI is not
usable in this build at all (`GUI.DoTextField` throws "Method unstripping failed").
Under the connection status the panel keeps the last five items it received
(readable name plus the player who sent them), so it is obvious what the
multiworld has handed over.
Settings are saved to the plugin config, and the world sends its item/location
id mapping in `slot_data`, so the panel needs no external files.

Two config switches are worth knowing:

* `Archipelago/AutoConnect` (default `false`) — join the saved server/slot on
  launch without touching the panel.
* `Panel/OpenAfterSaveLoad` (default `true`) — open the panel by itself the
  first time a save slot is loaded.
* `Panel/ShowOnSlotScreen` (default `false`) — show the panel on the save-slot
  screen at boot as well (the older behaviour).
* `Safety/AllowedProfileIndex` (default `1`) — the only save slot the mod will
  read or write.
* `Gating/ItemOnlyUnlocks` (default `true`) — characters, rune nodes and the two
  endgame modes may only be unlocked by an item; the in-game achievement routes
  are blocked (see below).
* `Blacksmith/IgnoreWeaponLevelRequirements` (default `true`) — a weapon the save
  already owns can be crafted and levelled at the blacksmith without the vanilla
  achievement behind it (`reach prestige level 10 with <character>`). Weapons the
  multiworld has not handed out still use the game's own check, so nothing can be
  crafted ahead of the item.

Items received before you pick a save slot are held and retried every second
(`AP: holding ...` in `BepInEx/LogOutput.log`) until the profile loads. After
items are applied the mod writes the affected profile files with the game's own
`PlayerDataUtil.RequestSaveFiles`, so grants survive closing the game. A
character grant also creates the progression entry the game expects (otherwise it
logs `Could not load Progression Data for characterId [...]`).

A grant also re-runs the game's own reward scans for the unlock
(`UpdateCraftWeaponAchievements`, `UpdateUnlockCharacterAchievements`, …) and
rebuilds an already-open blacksmith or character screen, because the game
otherwise only re-scans the save when a profile loads — without that, a weapon
written into `UnlockedWeapons` stayed inert (no craft achievement, no blacksmith
entry) until the next restart.

`RequestSaveAll` looks like the obvious call but silently writes nothing outside
the game's own flow — it reports success and the files never change.

**Python client (optional, better logging).**

```
soulstone_client.py  <-- 127.0.0.1:47821 -->  SoulstoneAP.dll
```

The plugin exposes a line protocol on loopback (`PING`, `STATE`, `CATALOG`,
`GRANT item=...`) and does all game access on Unity's main thread. `GRANT` takes
`Char:<id>`, `Weapon:<id>`, `Rune:<runeId>`, `Souls:<n>` (minor soulstones) or
`Souls:<currency>:<n>` for any of the six — `minor`, `corrupted`, `vile`,
`wicked`, `hateful`, `rogue` (`Souls:Vile:500`). The client
polls `STATE` once a second, sends `LocationChecks` for newly completed
maps/achievements and applies received items. Everything is idempotent, so
reconnects are safe.

## Sharing the world (.apworld)

```bash
python tools/build_apworld.py     # -> dist/soulstone_survivors.apworld
```

The archive contains the whole world package (`__init__.py`, `Items.py`,
`Locations.py`, `data.py`, `catalog.json`, `soulstone_client.py`), so it is self
contained — no game files needed to generate.

**For players:** in the Archipelago Launcher click **Install APWorld** and pick
the file (or drop it into `<Archipelago>/worlds/`), then restart the Launcher.
*Soulstone Survivors* appears in the game list, and **Generate Template
Options** writes a YAML with every option below already documented.

`catalog.json` is read through `importlib.resources`, so it also loads from
inside the zip — a plain `open()` on the package folder silently finds nothing
there, which would leave the packaged world with no checks at all.

Verified by removing the loose world folder, installing only the built
`.apworld` into `worlds/`, and generating a seed from a YAML that used
`disabled_characters: [rogue, Blacksmith]` (names are case-insensitive),
`max_prestige: 100` and `hidden_achievements: false`: 431 checks of 483, five
maps, no Rogue or Blacksmith checks, no hidden ones, no 150-tier prestige
checks. A default seed still generates 483 checks.

## Sharing the mod (Thunderstore)

```bash
python tools/build_thunderstore.py     # -> dist/SoulstoneArchipelago-<version>.zip
```

It builds the plugin, stages `thunderstore/` and packs the zip Thunderstore
expects: `manifest.json`, `README.md`, `CHANGELOG.md` and `icon.png` at the root
with the assembly at `BepInEx/plugins/SoulstoneAP.dll`. The version is copied
out of `Plugin.cs` into the manifest, so the package cannot disagree with the
DLL it carries — the failure mode that left `v0.7.3` in the panel once already.
The script also refuses a bad icon size, a name with illegal characters, an
over-long description and a malformed dependency string.

What lives where: `thunderstore/manifest.json` (name, description, dependency),
`thunderstore/README.md` (the page people read in the mod manager — keep the
mod-side instructions here, not the dev notes), `thunderstore/CHANGELOG.md`, and
`thunderstore/icon.png` (256×256, generated from `mod/assets/ap-logo.png`).

The dependency is `BepInEx-BepInExPack_IL2CPP-6.0.788`, the pack version the
mod is built and tested against. Bump it in the manifest when moving to a newer
pack, and re-test — interop assemblies come from that pack.

**Uploading** (one-time setup, then per release):

1. Sign in at <https://thunderstore.io>, create a team (the namespace cannot be
   changed later; it becomes part of every dependency string).
2. Team page → **Upload package** → pick the community (Soulstone Survivors) and
   the zip above.
3. The site returns the listing link immediately; mod managers can take up to
   ~35 minutes to show it. Published versions are immutable — a mistake means a
   new version number, not a re-upload.

There is also a CLI: `dotnet tool install -g tcli`, a `thunderstore.toml` with
`namespace`/`name`/`communities`, and `tcli publish --token <service-account
token>` (token from team → **Service Accounts**; keep it out of the repo).

The mod zip does **not** carry the `.apworld` or the Python client: the world is
installed by whoever generates the seed, and players joining a room only need
the plugin. Ship the `.apworld` next to the release (GitHub release, or a link in
`website_url`) and say so on the page.

### Options

| Option | Default | Effect |
| --- | --- | --- |
| `death_link` | off | Received deaths offer a choice of negative Powers |
| `knockback_link` | off | Damage sends/receives shoves |
| `achievement_checks` | on | Include the 483 achievements as checks (off = 5 map checks only) |
| `disabled_characters` | none | Character names whose achievements are dropped from the check pool |
| `max_prestige` | 150 | Highest prestige tier that still counts (0 = no prestige checks) |
| `hidden_achievements` | on | Include the 11 hidden achievements as checks |
| `endgame_checks` | medium | Curse ladder, Unholy Cathedral rituals and Titan Hunt sigils (off/easy/medium/hard) |
| `weapon_unlocks` | on | Include the 88 blacksmith weapon unlocks as useful items |
| `rune_unlocks` | on | Include every rune as a usable item plus a "Rune: …" check |
| `map_unlocks` | on | The five Void Fields maps are items; off = all maps unlocked from the start |
| `curse_tier_unlocks` | on | Curse tiers I-VII are items, and curse checks need their tier |
| `weapon_upgrade_unlocks` | on | Blacksmith upgrade ceiling is items: +3, +5, +8, +10, +15, then unlimited |
| `endgame_mode_unlocks` | on | Unholy Cathedral / Titan Hunt are items, and their checks need them |
| `trap_count` | 5 | How many trap items go into the pool (0 disables them) |
| `resource_cache_count` | 8 | How many `Resource Cache` items go into the pool (0 disables them) |
| `filler_soulstones` | 100 | Soulstones each filler item grants |
| `maps_required` | 5 | Map clears needed for victory (1-5) |

YAML example:

```yaml
name: Survivor
game: Soulstone Survivors
Soulstone Survivors:
  achievement_checks: true
  disabled_characters: [Pyromancer, Necromancer]
  max_prestige: 100
  hidden_achievements: false
  endgame_checks: hard
  weapon_unlocks: true
  trap_count: 10
  filler_soulstones: 250
  maps_required: 3
  death_link: true
  knockback_link: true
```

### Runes

Runes have no unlock list of their own in the save: each one is granted by a
dedicated skill tree node (`SkillTreeUnlockRuneSO`, which carries the rune id).
The plugin dumps those nodes in the catalog, so every rune becomes

* an item — `Rune: <runeId>` — and
* a check — `Rune: <runeId>` — that ticks when the save owns that node.

Receiving the item unlocks the node outright at level 1, ignoring its tier
requirement and prerequisite nodes (`PlayerProfileSkillTree.Add` + a `SkillTree`
save), and the matching check is reported on the next poll. Everything else in
the tree stays yours: stat nodes are still bought with your own skill points, and
nothing blocks buying a rune node locally either — the item is a shortcut, not a
replacement. Because runes gate no other check they are classed as `useful`.

**After the first launch of a plugin that knows about runes**, refresh the
catalog so the world knows the rune ids:

```bash
python tools/dump_catalog.py      # game running, any profile
bash deploy_world.sh
python tools/build_apworld.py     # ship the refreshed catalog with the world
```

Without that step the world simply generates with no rune items or checks.

### Item-only unlocks

Four things the game would otherwise hand out on its own are forced to come
from the multiworld (`Gating/ItemOnlyUnlocks`, and only for the mod's own save
slot — every other slot behaves normally):

| Gate | Hook | Effect |
| --- | --- | --- |
| Characters | `PlayerProfileUnlockedCharacters.AddUnlockedCharacter` prefix, plus `CharacterScreenPanel.OnSelectPressed` / `ControllerPurchase` | an `Unlock <character>` achievement no longer adds the character, and the character screen's buy button is refused before it spends currency; only the item does |
| Weapons | `CraftingScreenPanel.DoOnCraftPressed` prefix | crafting a weapon the save does not own is refused, so the blacksmith can't unlock it; only a `Weapon: <id>` item does |
| Runes | `PlayerProfileSkillTree.Add` prefix | a `SkillTreeUnlockRuneSO` node cannot be bought with skill points, so nodes behind it stay locked until the rune item arrives |
| Modes | `AchievementCollectionSO.CompleteSpecificAchievement` prefix | the `RequiredAchievement` of Unholy Cathedral / Titan Hunt cannot complete by itself, so the mode opens only with its item |
| Maps | `MapItem.IsRequiredAchievementCompleted` getter + `MapItem.OnSelectPressed` prefix | the map button's lock is overridden with "has this map's item arrived", and the click is refused otherwise, so a map unlocked by the game's own achievements stays shut |

Weapon gating follows the seed, and the signal is the seed's own item table: if
it ships `weapon` items, crafting unknown weapons is blocked. Slot data also
carries `weapons_default` (true when `weapon_unlocks` is off), which turns the
gate back off — there is nothing to wait for in a seed with no weapon items. This
means seeds generated before the flag existed are gated correctly too.

Each block is logged once as `ItemGating: blocked …`, so a gate that never fires
is easy to tell from a gate that is not patched. The trick the mod uses is
`ItemGating.Granting`, set only while an item is being written: that lets the
item (and the achievement it implies, which is what sends the check) through.

Two consequences worth knowing: unlocks already in a save are never revoked, and
a skill tree reset removes granted rune nodes along with everything else — run
`/resync` to have the client re-apply the runes it already sent.

The in-game character tooltips still say "requires achievement …"; that text
lives in the localization assets, so it stays as is while the behaviour changes.

### Maps

A map exists in the save as a `PlayerProfileMapProgressionData` entry — that is
all the game keeps (there is no map-unlock flag anywhere in `MapDefinition` or
`MapCollectionSO`), which is why a fresh profile has one entry, not five.

With `map_unlocks` on (default) the five maps are `Map: <mapId>` progression
items, each `Clear <map>` check requires its map item, and all five are
registered as **local early items** — they land in that player's earliest
reachable checks (verified: sphere 1 holds exactly the five maps, Victory falls
into sphere 2) instead of somewhere mid-seed. Receiving one writes the map's
progression entry and saves `SaveFileKind.MapProgression`, so it shows up in map
select.

With it off nothing is gated and no map items exist; the plugin instead writes
all five entries the first time it joins a seed whose slot data says
`maps_default` (log: `unlocked N map(s) for this seed`).

**The map buttons are not ours.** The map list is a set of `MapItem` prefabs,
and each one is locked by `MapItem.RequiredAchievement` /
`IsRequiredAchievementCompleted` — achievement state, which the mod does not
touch. So the game can still open a map on its own; the progression entry the
item writes is what the map screen reads for progress and affixes, not what
decides the lock. What *is* enforced is the payout: with `map_unlocks` on, both
the plugin and the Python client only report a `Clear <map>` check once that
map's item actually arrived (plugin log: `cleared X without its map item - check
N not sent`). So a self-unlocked map still earns nothing.

Bridge commands: `GRANT item=Map:SurvivorsForest`, and `MAPFULL map=all` (or
`MAPFULL map=SurvivorsDesert`) to mark a map 100% cleared for testing — that is
what makes its affix panel usable, and it does fire its `Clear` check if the map
item is in hand. `MAPRESET map=all|<mapId>` removes map progression entries, so
the maps have to arrive as items again; note that this also drops that map's
completeness, its affix selections and its curse tier rows, and that checks
already sent to the server stay sent.

### Curse tiers

The I-VII badges on a map are `AffixTier`s. The save keeps one
`PlayerProfileAffixTierProgressionData { Tier, Progression }` per tier per map,
and `AffixCollectionSO.GetRequiredCurseLevelForTier(tier)` says which curse
intensity a tier needs — the catalog ships that mapping as `affixTiers`.

With `curse_tier_unlocks` on (default), each tier is a `Curse Tier I … VII`
progression item. Receiving one writes tiers 1..N at `Progression = 1` into every
map's list (`SaveFileKind.MapProgression`), and every `Curse <n>: …` endgame check
requires the tier whose required intensity covers `n`, so a seed cannot expect
curse 40 before `Curse Tier V` exists.

The real thresholds, as dumped from the installed build: I at 0, II at 5, III at
11, IV at 21, V at 33, VI at 44, VII at 53 curse intensity. So curse 10 needs
`Curse Tier II`, curse 40 needs `Curse Tier V`, curse 62 needs `Curse Tier VII`.

Self-unlock is blocked by a sweep, not a hook: `PlayerProfileAffixTierProgressionData.set_Progression`
cannot be patched on this build (Harmony fails with an `IL Compile Error`), so the
plugin instead remembers the highest tier an item granted (`Gating/GrantedCurseTier`)
and, every few seconds while no run is live, resets any higher tier back to
`Progression = 0` and saves. The ceiling is **granted + 1**, because the badge row
always offers one tier past what the save holds - so a VII run you finish can bank
its progress instead of being wiped. Log line:
`cleared N curse tier entries above tier X (granted Y + the one the badge row offers)`.

Two consequences worth knowing:

* A tier earned mid-run is only wiped after the run ends, so the badge can light
  up during a run and go dark when you get back to the menu.
* `GrantedCurseTier` never drops on its own. If you start a *new* seed with
  `curse_tier_unlocks: false` and want the vanilla route back, set the config
  value to `0` by hand.

Bridge command: `GRANT item=Curse:5` (tier 5, and everything below it).

### Resource caches

`Resource Cache` is a useful item with no fixed value, and it rolls a coin flip
when it arrives — half pay out in resources, half in character XP.

**Resources.** The plugin looks at what the save already holds (the six soulstone
currencies plus every mineral in the inventory), picks one of those at random and
adds **10% of the current amount**, rounded up to at least 1. So the item scales
with the player instead of handing out a flat number, and it can never roll a
resource you have none of. With nothing in the bank at all it falls back to 25
minor soulstones.

**Character XP.** A random character the save owns gets prestige. The amount is
`PrestigeConfigSO.GetRequirementForLevel(level + 1)` scaled by the same
percentage, so it means "roughly a level" at any depth.

The level is written too, because the game only recomputes it inside its own
match-end code — a save with 8171 prestige sat at level 1 across a reload until
this was added. The rule is measured, not guessed: the requirement table is flat
(5000 for every level in this build), so `level = prestige / requirement + 1`,
never lowered, capped at 150. The prestige achievements are then refreshed with
the same updater the load path uses, so a grant can fire checks on the spot.
`PRESTIGE` dumps the per-character levels, prestige points and the first six table
entries if the table ever changes shape.

Count is set by `resource_cache_count` (default 8).

Bridge commands: `GRANT item=Resource:10` (number = percentage), `PRESTIGE`.

### Weapon upgrade caps

The blacksmith has no level ceiling of its own - upgrades only get more expensive.
With `weapon_upgrade_unlocks` on (default) that ceiling becomes an item ladder:
level **1** is the base and means no upgrades at all, then `Weapon Upgrade +3`,
`+5`, `+8`, `+10`, `+15`, and finally `Weapon Upgrade Unlimited`. Six items,
progression. There is deliberately no `+1` item: +1 is where the ladder starts.

Enforcement is two hooks, because one is not enough: the blacksmith panel spends
the materials *first* and only then raises the level. So `CraftingScreenPanel.DoOnCraftPressed`
refuses the press outright when the selected weapon already sits at the cap (no
materials are taken), and `PlayerProfileWeaponsLevel.UpgradeWeaponLevel(string)`
blocks any other path into the upgrade as a backstop. A weapon being crafted for
the first time (no level entry yet) always goes through.

The cap lives in the plugin config, nothing is written to the save, so a save
that already has weapons above the cap keeps them - only new upgrades are blocked.
Your own materials still pay for every upgrade.

One logic assumption to know about: the three `WeaponsUpgradedToArtifactRarityCount*`
checks require `Weapon Upgrade +15`, because the game only exposes
rarity-per-level at runtime and the top finite cap is the safe stand-in. If
Artifact actually lands lower than +15, tighten it there.

Bridge command: `GRANT item=WeaponCap:5` (or `WeaponCap:-1` for unlimited).

To test an upgrade without farming, materials can be added straight to the
inventory (`InventoryItemData { ItemId, Quantity }`, saved with
`SaveFileKind.Inventory`):

```powershell
# one material - ItemId names: CommonIron/Silver/Gold/Tin, UncommonSapphire/Emerald/Topaz/Ruby/Quartz,
# RareCobalt/Mithril/Palladium/Platinum/Titanium, EpicAdamantite/Demonite/Meteorite/Obsidian/Orichalcum,
# LegendaryAmber/Amethyst/Aquamarine/Diamond/Tourmaline
python tools/bridge_test.py "MATERIALS item=CommonSilver n=200"
python tools/bridge_test.py "MATERIALS item=UncommonSapphire n=200"

# every mineral at once
python tools/bridge_test.py "MATERIALS n=500"
```

### Endgame modes

Unholy Cathedral and Titan Hunt are not open from the start: each `GameModeSO`
carries a `RequiredAchievement`, and completing it is the game's own unlock path.
With `endgame_mode_unlocks` on (default), two progression items — **Unholy
Cathedral** and **Titan Hunt** — go into the pool, and receiving one completes that
mode achievement in the save (`AchievementCollectionSO.CompleteSpecificAchievement`,
saved with the grant). Everything inside the mode stays vanilla: Sin → Hate →
Heresy, and Corruption → Vileness → Wickedness → Hate → Void, each +N in order.

Every ritual check then requires *Unholy Cathedral* and every sigil check requires
*Titan Hunt*, and neither item can be placed on a check it gates, so the chain
cannot junk-lock. Turn the option off to leave both modes as they are in your save;
it is ignored entirely when `endgame_checks: off`, since there is nothing to gate.
The bridge command behind the item is `Mode:Mastery` / `Mode:Crusade`.

### Filtering checks

`disabled_characters` drops every check that names the character: its `Unlock`,
`Prestige`, `CompleteSkillTree`, `CraftWeapon` / `CraftAllCharacterWeapons` and
`CompleteMatchInLessThanTime` achievements — 18 checks for a typical character,
36 for two. Names are case-insensitive and validated at generation time; a typo
fails loudly with the full list of valid names.

`max_prestige` caps the per-character prestige ladder. Prestige achievements ask
for tiers 5, 10, 20, 30, 40, 50, 60, 70, 100 and 150, so `100` drops the 23
150-tier checks, `70` also drops the 100 tier (45 checks total) and `0` removes
the whole ladder — 209 checks, including the prestige-50-on-multiple-characters
one, which goes away at any cap below 50.

`hidden_achievements: false` removes the 11 flags marked hidden in the catalog
(`UnlockBlacksmith`, the Valentine Altar / Cave Elemental / Sasquach curse-tier
ones, `CompleteCrusadeTier5` and friends).

Global counters are deliberately left alone — `UnlockCharacterCount22`,
`CompleteSkillTreeCount22`, `ReachTotalEnemiesKilled*`, the mastery tiers — they
track in-game progress across every character, so disabling one character's
checks does not make them unreachable. Character and weapon **items** are also
unchanged: a disabled character still arrives as an unlock, it just no longer
brings its own achievement checks.

### Endgame checks

`endgame_checks` adds checks for the three modes the achievements barely touch.
The set is built per seed, because the random curse levels and weapons in the
names have to stay stable for that seed and match what the plugin reports.

| | easy | medium | hard |
| --- | --- | --- | --- |
| Curse ladder (each map) | curse 10, 20 | 10/20/30/40/50 | 10/20/30/40/50/62 |
| "with 5 characters" curse | – | one random curse per map | same |
| random weapon combo | – | – | 2 per map, random curse 1-62 |
| Ritual of Sin / Hate / Heresy | all three | all three | all three + every character |
| Heresy endless | +1 | +1 … +5 | +1 … +10 |
| Sigil of Corruption … Void | all five | all five | all five + every character |
| Corruption endless (cycles) | +1 | +1 … +5 | +1 … +10 |
| Checks generated | ~20 | ~50 | ~250 |

Location names read as `Curse 30: SurvivorsCavern`, `Curse 47: SurvivorsCavern /
WeaponPirate-04`, `Curse 40: SurvivorsCavern with 5 characters`, `Ritual of
Sin`, `Ritual of Heresy +3`, `Sigil of the Void`, `Sigil of Corruption +2`, and
`Ritual of Hate as Necromancer` on hard.

**Logic.** A weapon check needs both the weapon and its owner, so those weapons
are classified `progression` instead of `useful`. To stop a check from gating the
items it needs, the character's own unlock (and all five of that character's
weapons) is barred from being shuffled into its checks; "with N characters"
checks bar every character unlock. A hard seed was verified to have no such
placement across all of its locations.

**Detection.** The plugin reads the save rather than the achievements:

* curses from the per-match records (`AffixLevel` plus map, weapon, character
  and `Result`), so a victory at or above the threshold counts;
* rituals from `MasteryProgression` (Copper/Silver/Gold = Sin/Hate/Heresy) and
  sigils from `CrusadeProgression` (Tier1-5 = Corruption…Void), each with the
  `+N` dynamic level the game stores per character.

The game enforces the order itself, so no AP-side unlocking is needed: Hate
needs Sin, Heresy needs Hate, sigil N needs N-1, and +N needs +N-1.

The world only covers generation. To actually receive items in game, players
also need the plugin: `BepInEx/plugins/SoulstoneAP.dll` (built by `bash deploy.sh`).

## Install / refresh

```bash
bash deploy.sh          # rebuild the plugin and copy it into the game + TSM profile
bash deploy_world.sh    # copy the world package into .tools/archipelago/worlds
python tools/dump_catalog.py   # only after a game update, with the game running
```

No machine-specific paths are baked into the scripts. `env.sh` looks for the game
in the usual Steam libraries and for the Mod Manager profile under `%APPDATA%`;
override either one when the guess misses:

```bash
SOULSTONE_DIR="E:/Games/Soulstone Survivors" bash deploy.sh
TSM_PROFILE="$APPDATA/r2modman/SoulstoneSurvivors/profiles/Default" bash deploy.sh
```

It also picks the .NET SDK from `.tools/dotnet` first (the system `dotnet` may be
a runtime-only install, which fails with "The application 'build' does not
exist"), then falls back to `PATH`.

BepInEx 6 (IL2CPP, bleeding-edge) is installed in the game folder; the plugin
lives in `BepInEx/plugins/SoulstoneAP.dll`. The plugin writes
`BepInEx/config/soulstone.ap.bridge.cfg` — change `Port` or `AllowedProfileIndex`
there if needed.

## Playing

1. Launch the game and pick **save slot 1** (the slot this mod is bound to).
2. Generate a seed with your player yaml, e.g.:

   ```bash
   cd .tools/archipelago
   SKIP_REQUIREMENTS_UPDATE=1 ../venv/Scripts/python.exe Generate.py \
     --player_files_path ../../players --outputpath ../../output
   ```

3. Either start only the server and connect from the in-game panel, or use the
desktop launcher (which starts server + Python client):

   ```bash
   python launcher.py
   ```

   ...which has the seed picker, server start/stop, connect (server / slot /
   password), a "launch game" button, live bridge status and a client log pane;
   or from the shell:

   ```bash
   bash play.sh output/AP_<seed>.zip
   ```

The client logs to `client.log` (and `.tools/archipelago/logs/SoulstoneClient_*.txt`).

**The game always asks for a save slot at startup.** Until you pick slot 1 the
plugin reports `no save slot selected yet`, and the client answers
`Holding '<item>' until the game is ready`. That is expected, not a failure —
it applies the moment the slot loads.

## Design

* **Items** — `Character: <Name>` (22 progression unlocks), `Weapon: <id>` (88
  weapons that are not auto-unlocked), 6 `Trap: ...` items, and
  `Soulstones +100` (filler currency).
* **Locations** — `Clear <map>` (5 standard maps) and `Achievement: <id>`
  (483 achievements) = 488 checks.
* **Goal** — clear every standard map in normal mode.

Logic is deliberately flat: in-game progression (clear a map to open the next)
still applies, but every check is considered reachable. The randomization comes
from which characters and weapons the multiworld gives you.

## Links and traps

### DeathLink - "Cursed Powers"

A received death does **not** kill you. Instead a modal prompt offers three
**negative Powers** and you pick one, exactly like a level-up card choice. They
are the inverse of real level-up cards, applied through the same `Stats` pipeline
the cards use, so they stack like a normal Power and last until the run ends.
Your own deaths are reported as DeathLink events too.

The picker is built from uGUI + TextMeshPro and runs on unscaled time, so it is
clickable even while the game is paused on the level-up screen. If several deaths
arrive at once the choices queue up and the next one opens after you pick.

| Negative power | Effect | Inverted from |
| --- | --- | --- |
| Feeble Strikes | -10% damage | Powerful Strikes |
| Sluggish Colossus | -10% damage, -15% move speed | Leviathan |
| Half-Hearted | -10% attack speed | Relentless |
| Cramped | -15% area | Expansive |
| Lethargic | -5% crit chance | Lethality |
| Merciful | -20% crit damage | Merciless |
| Frail | -15% max health | Behemoth |
| Brittle | -8 armor | Resilient |
| Defenceless | -5% block chance | Indomitable |
| Leaden | -10% move speed | Agile |
| Repellent | -20% experience, -50% pickup range | Magnetic |
| Breakable | +2 damage taken | Unbreakable |
| Battered | -20% healing | (healing powers) |
| Exhausted | -1 dash | Swift |

Enable with `death_link: true` in your player yaml. The panel shows the effect
(`| Death link: Frail`), and the log records every one.

### KnockbackLink

A custom option, not the standard AP DeathLink tag. Taking damage sends a
`KnockbackLink` bounce (rate-limited to one every 5 seconds) and received ones
shove your character with the game's own controller
(`SetVelocity` + `PauseGroundConstraint`). It only fires while grounded and not
dashing, and it is a real shove, not a teleport, so it cannot drop you off the
map. Enable with `knockback_link: true`.

### Traps

Six trap items are in the pool. Names live in
[`Effects.cs`](mod/Archipelago/Effects.cs) and
[`Items.py`](world/soulstone_survivors/Items.py) and must stay in sync.

| Trap | What it does |
| --- | --- |
| `Trap: Cursed Power` | Opens the 3-choice negative Power picker (the DeathLink effect) |
| `Trap: Knockback` | Shoves the player |
| `Trap: Elite Ambush` | Next elite spawn brings 2 extra |
| `Trap: Swarm` | Next elite spawn brings 5 extra |
| `Trap: Boss Summons` | Next boss event brings 1 extra boss |

The spawn traps raise the run's own `AffixGameplayData` counters. The game reads
them when the *next* elite/boss spawns, so a bump shows up at the next spawn
event instead of instantly (confirmed in game: a bumped run fought 3 bosses at
the 2nd boss).

That delay is deliberate, not a shortcut. Two more direct approaches were tried
and both **crashed the game** (native crash, not a managed exception):
`BossSpawnProcessor.SpawnBosses` called from outside the run's own flow, and
instantiating a live enemy with `Object.Instantiate` to place copies near the
player — entities created outside the game's pooling are not safe. Until a
pooled spawn entry point is found, the counters are the only safe path.

`Trap: Void Hunters` was dropped: `GameManagerVoidHunterComponent` only exists in
runs where a void-hunter affix is active, so in a normal run there is nothing to
call. Regenerate seeds after changing the trap list — item ids shift.

### Forcing effects for testing

You do not have to wait for the multiworld. [`tools/force.py`](tools/force.py) triggers
everything directly:

```bash
# straight into the running game, no server involved (uses the loopback bridge)
python tools/force.py effect "Elite Ambush"
python tools/force.py effect "Boss Summons"
python tools/force.py effect "Cursed Power"     # one random negative Power
python tools/force.py effect Knockback

# a real trap item through the server console
echo '/send Survivor Trap: Swarm' >> server_cmds.txt
# or
python tools/force.py item "Trap: Swarm"

# DeathLink / KnockbackLink as a real network bounce
python tools/force.py deathlink localhost:38281 LinkTester
python tools/force.py knockbacklink localhost:38281 LinkTester
```

`effect` needs a run in progress and the current plugin build (the command is
`EFFECT name=<trap>` on the bridge, so `python tools/bridge_test.py "EFFECT name=Swarm"`
does the same thing). It applies the effect **locally only** — it does not send a
DeathLink to other players.

The `deathlink` / `knockbacklink` forms join the room as a second slot and bounce,
because Archipelago ignores bounces from players who are not in the seed. Use a
spare slot (`LinkTester` in a test seed), never your live one — connecting twice
as the same slot kicks the game off.

## Customizing checks and items

Most of this is now exposed as YAML options (see *Sharing the world* above) —
use those unless you are changing the structure itself. Everything is derived
from `catalog.json` by [`data.py`](world/soulstone_survivors/data.py), so you can
change what counts as a check without touching game data:

* **Drop achievements from the check list** — in `data.py` replace the
  `ACHIEVEMENTS` comprehension with a filter such as
  `if entry.get("id", "").startswith("CompleteAffixTier")`, or set
  `ACHIEVEMENTS = []` to make the seed maps-only.
* **Pick which maps count** — the `MAPS` filter skips unavailable/ignored/crusade
  maps; add a biome or name filter there.
* **Change item categories** — [`Items.py`](world/soulstone_survivors/Items.py):
  `CHARACTER_PREFIX`/`WEAPON_PREFIX` build names, `item_classifications` sets
  progression vs useful, and `data.FILLER_NAME` / `FILLER_AMOUNT` control the
  filler currency (raise `FILLER_AMOUNT` for bigger payouts).
* **Change the goal** — [`__init__.py`](world/soulstone_survivors/__init__.py):
  `MAP_LOCATIONS` in `set_rules` is the victory condition; swap it for an
  achievement subset if you prefer.

After editing: `bash deploy_world.sh`, then generate a fresh seed. Ids are stable
as long as `catalog.json` is unchanged, so existing seeds keep working.

## Known limitations

* **Unlocks are additive, by design.** The game never re-locks what you own, and
  the mod never removes anything, so anything you buy in the shop stays yours —
  the item pool just controls what you get *for free from the multiworld*.
* Character/weapon unlocks appear in the menu after the game reloads the profile
  (leaving to the main menu is enough); the game writes them to disk at its normal
  save points.
* Items wait until the game is running on slot 1. The client keeps retrying and
  logs `Holding '<item>' until the game is ready` in the meantime.
* Achievements only count once the game has them in its own progression list; a
  brand-new save reports them as they are earned.
* Map/achievement ids are from build 1.5d2. After a game update run
  `tools/dump_catalog.py` and re-generate.

## Troubleshooting

* `python tools/bridge_test.py PING` — verifies the plugin answered.
* `python tools/bridge_test.py STATE` — dumps the live save as the client sees it.
* `bridge_test.py` prints `ERR profile N is active but only profile 1 may be used`
  if you loaded the wrong save slot, and
  `ERR no save slot selected yet - pick save slot 1 in game` while the game is
  still on the slot-selection screen.
* Missing `jellyfish`, `bsdiff4`, `pathspec`, `setuptools` errors come from
  running Archipelago with a Python env that is not `.tools/venv`.
