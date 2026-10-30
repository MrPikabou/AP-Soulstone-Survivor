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
