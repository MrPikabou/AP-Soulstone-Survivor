# Unreleased

- New `starting_character` option: start as any character, or `random`. A non-Barbarian starter is precollected and its item leaves the pool; the default `barbarian` keeps old YAMLs and seeds unchanged.
- The F8 panel has a `Global` button that opens the check window filtered to checks with no map or character (portal cycles, lifetime counters, skill-tree counts and other global achievements).
- New seeds omit all character unlock achievements (`Unlock<character>` and `UnlockCharacterCount8/14/22`). Receiving a `Character:` item runs the game's unlock scans and completes them, so they were free checks that could chain another item to a third player. Older seeds keep their generated unlock checks.
- Character-scoped achievements now require that character's item, preventing self-locks such as `Character: Samurai` being placed on `CompleteSkillTreeSamurai`. Character items are kept off checks that require a roster count, and full skill trees require 8 owned characters.
- Maps-only seeds (`achievement_checks: false`) no longer crash while setting weapon-cap or crafting rules for checks that do not exist, and their item pool keeps the map keys needed to reach the five map checks.
- Map affix-tier checks are labelled `CompleteCurseLevel<Map><Tier>` (for example, `CompleteCurseLevelCavern2`) instead of the internal `CompleteAffixTier` names. Tier V/VI checks require their matching `Curse Tier` item, so they cannot appear in sphere 1; older seeds keep their original labels and names.
- Boss Rush and Endless cycle checks are labelled `CompleteRedPortalCycle<N>` and `CompletePurplePortalCycle<N>` instead of `CompleteBossRushCycle<N>` and `CompleteEndlessCycle<N>`. The underlying game ids and location ids are unchanged.
- `max_portal_cycle` (1-3, default 3) caps the Red/Purple portal ladder. Setting it to 1 keeps only the two cycle-1 checks and drops both families' cycles 2 and 3, without changing item or location ids.
- `goal: portals` adds an alternative victory path: finish Red cycle 3, Purple cycle 3, or both (selected with `portal_goal`). It requires achievement checks and `max_portal_cycle: 3`, and the HUD shows `n/N portals`.
- High-grind achievements are balanced by default in new seeds. `balance_checks` puts prestige 35/50/100/150 behind 2/4/8/14 owned characters, full skill trees behind 8, the 8/14/22 skill-tree counts behind 8/14/22, and the Red/Purple portal cycles 1/2/3 behind 2/4/8; the largest lifetime counters use the same roster bands. These are real character-item requirements rather than event gates or new items. Set `balance_checks: false` for the old flat logic.
- Filler is varied by default: new seeds split Soulstone filler into Shard, Cluster, Cache and Hoard stacks at 50/75/125/150% of `filler_soulstones`. The four variants average out to the old total, so seeds get less repetition without a large currency increase. Set `filler_variety: false` for the old single `Soulstones` stack; existing seeds keep their original items.
- DeathLink choices can now include screen burdens: a 10% blur, a 30% centre blur, Deuteranopia and Tritanopia filters, a deep-fried look, a 5-second blackout, and a blacked-out left or right half of the screen. They clear when the run ends.
- Inverted Controls is a new DeathLink burden: movement input is reversed for the rest of the run.
- Incoming DeathLink and KnockbackLink events are applied again. The server forwards a bounce as `Bounced`, which the plugin was not listening for, so other players' deaths were silently dropped.
- F8 has Hint, Missing, Goal, Release and Collect buttons; server replies are written to the log.
- A top-right status HUD shows the connection dot, server, retry countdown and goal progress (`n/N maps`).
- Feed rows, F8 log lines and the (!) badges are coloured by classification; progression items and traps can play a short tone (`HUD/Sounds`).
- Rune unlocks are items only in new seeds. Their matching `Rune:` locations were removed because receiving a rune completed its own check and could chain another item to a third player. Older seeds keep their generated rune checks; the Python client only reports checks listed in the seed's rune table.

# 1.0.4

- The F8 panel now lists the last five items it received, each with a readable name
  (for example `Guaranteed Minimum Rarity (rune)`) and the player who sent it.
- README notes the new received list.

# 1.0.3

Bug fixes for joining a hosted room.

- Items received for a new seed are applied again. The `AppliedItems` counter used to
  carry over between rooms, so joining a different multiworld skipped every item below the
  old count and a fresh save received nothing (characters, weapons and runes included).
  The counter now belongs to the seed and slot it was built on.
- Writing the maps a seed unlocks by default no longer crashes the save with
  `NullReferenceException` in `PlayerDataUtil.RequestSaveMapProgression`. Hand-made map
  entries now create the affix lists the serializer walks, so the maps are actually saved.
- The websocket now offers per-message compression, which the server expects and used to
  warn about; a plain connection is still tried if that handshake fails.
- Items applied on a retry (received before the save slot was picked) persist their
  progress, so they are not handed out a second time after a restart.

# 1.0.2

Bug fixes.

- Connecting to a hosted room (for example an `archipelago.gg` room, which only speaks TLS) no
  longer fails silently: the panel picks `wss` for remote servers and `ws` for local ones, and
  falls back to the other scheme if the handshake fails. The status line now names the scheme
  it is trying.
- The server / slot / password fields accept Caps Lock, and support Ctrl+C and Ctrl+V.

# 1.0.1

First public release.

- In-game Archipelago panel (F8): server, slot, password, chat/log and live item/check status.
- Characters, weapons, runes, Unholy Cathedral, Titan Hunt, curse tiers I-VII, weapon upgrade
  caps and the five Void Fields maps delivered as items, with the game's own unlock routes
  blocked while a seed uses them.
- 483 achievement checks, curse ladders, Unholy Cathedral rituals and Titan Hunt sigils as
  locations; per-seed options to disable characters, cap prestige and hide hidden achievements.
- DeathLink (choice of three negative Powers), KnockbackLink, and five trap types.
- Resource Cache items that pay 10% of a resource you already hold, or roughly one character
  level of XP.
- `MAPRESET` bridge command to clear a map's progression entry when re-testing map unlocks.
