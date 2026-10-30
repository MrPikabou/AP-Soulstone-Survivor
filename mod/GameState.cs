using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace SoulstoneAP
{
    internal sealed class GameSnapshot
    {
        public readonly List<MapState> Maps = new List<MapState>();
        public readonly List<AchievementState> Achievements = new List<AchievementState>();
        // Endgame detection sources: finished Void Fields runs, and the per
        // character ritual / sigil bests the game keeps for the two endgame modes.
        public readonly List<MatchRecord> Matches = new List<MatchRecord>();
        public readonly List<TierProgress> Rituals = new List<TierProgress>();
        public readonly List<TierProgress> Sigils = new List<TierProgress>();
    }

    internal struct MatchRecord
    {
        public string MapId;
        public string CharacterId;
        public string WeaponId;
        public int CurseLevel;
        public bool Victory;
        public int GameMode;      // GameModes.Survivor == 0
        public int EndlessLevel;  // >0 once an Overlord / Endless portal was taken
        public float EndlessCompletedPercent;
        public int EndlessType;
    }

    internal struct TierProgress
    {
        public string CharacterId;
        public int Tier;          // 1..3 rituals, 1..5 sigils
        public int DynamicLevel;  // the +N endless tier
    }

    internal struct MapState
    {
        public string Id;
        public float Completeness;
    }

    internal struct AchievementState
    {
        public string Id;
        public bool Done;
    }

    /// <summary>
    /// Reads the live save from the running game and applies Archipelago items.
    /// Everything here runs on the Unity main thread (via <see cref="Bridge.Pump"/>).
    ///
    /// Hard rule: nothing is read from or written to a save slot other than
    /// Plugin.AllowedProfile, so a slot-0 career save can never be touched.
    /// </summary>
    internal static class GameState
    {
        public static ManualLogSource Log;

        /// <summary>
        /// True when the seed hands weapons out as items, which makes crafting a new
        /// weapon at the blacksmith a self-unlock and therefore forbidden. Set from
        /// slot data on every connection; false for a seed with no weapon items.
        /// </summary>
        public static bool WeaponsFromItems;

        /// <summary>
        /// True when the seed hands maps out as items, so a map is only playable once
        /// its `Map: <id>` item has arrived.
        /// </summary>
        public static bool MapsFromItems;

        /// <summary>Map ids this slot has actually received, kept across reconnects.</summary>
        public static readonly HashSet<string> GrantedMaps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static bool TryGetProfile(out PlayerProfile profile, out string error)
        {
            profile = null;
            error = null;
            try
            {
                profile = Global.PlayerProfile;
            }
            catch (Exception e)
            {
                error = "could not read Global.PlayerProfile: " + e.Message;
                return false;
            }

            if (profile == null)
            {
                error = $"no save slot selected yet - pick save slot {Plugin.AllowedProfile.Value} in game";
                return false;
            }

            int index;
            try
            {
                index = profile.Index;
            }
            catch (Exception e)
            {
                error = "could not read profile index: " + e.Message;
                return false;
            }

            if (index != Plugin.AllowedProfile.Value)
            {
                error = $"profile {index} is active but only profile {Plugin.AllowedProfile.Value} may be used";
                return false;
            }

            return true;
        }

        // ---------------------------------------------------------------- state

        /// <summary>Lightweight snapshot used by the in-game client for check detection.</summary>
        public static GameSnapshot Snapshot()
        {
            if (!TryGetProfile(out var profile, out _)) return null;

            var snapshot = new GameSnapshot();

            try
            {
                foreach (var map in profile.MapProgression.MapProgression)
                {
                    if (map == null) continue;
                    snapshot.Maps.Add(new MapState
                    {
                        Id = map.MapId ?? "",
                        Completeness = map.NormalModeCompleteness,
                    });
                }
            }
            catch (Exception e) { Log?.LogWarning("snapshot maps: " + e.Message); }

            try
            {
                foreach (var achievement in profile.AchievementProgression.Achievements)
                {
                    if (achievement == null) continue;
                    snapshot.Achievements.Add(new AchievementState
                    {
                        Id = achievement.AchievementId ?? "",
                        Done = achievement.UnlockDateAsTicks > 0L || achievement.Progression >= 0.999f,
                    });
                }
            }
            catch (Exception e) { Log?.LogWarning("snapshot achievements: " + e.Message); }

            try
            {
                foreach (var match in profile.GameStats.AllStatsPerMatch)
                {
                    if (match == null) continue;
                    // One unreadable entry must not hide the whole history.
                    try
                    {
                        snapshot.Matches.Add(new MatchRecord
                        {
                            MapId = match.MapId ?? "",
                            CharacterId = match.CharacterIdString ?? "",
                            WeaponId = match.WeaponId ?? "",
                            CurseLevel = match.AffixLevel,
                            Victory = match.Result == MatchResult.Victory,
                            GameMode = (int)match.GameMode,
                            EndlessLevel = match.EndlessLevel,
                            EndlessCompletedPercent = match.EndlessCompletedPercent,
                            EndlessType = (int)match.EndlessType,
                        });
                    }
                    catch (Exception e)
                    {
                        Log?.LogDebug("snapshot match skipped: " + e.Message);
                    }
                }
            }
            catch (Exception e) { Log?.LogWarning("snapshot matches: " + e.Message); }

            try
            {
                foreach (var ritual in profile.MasteryProgression.Progress)
                {
                    if (ritual == null) continue;
                    try
                    {
                        snapshot.Rituals.Add(new TierProgress
                        {
                            CharacterId = ritual.CharacterIdString ?? ritual.CharacterId ?? "",
                            Tier = (int)ritual.CharacterMasteryDifficultyTier,
                            DynamicLevel = ritual.CharacterMasteryDynamicLevel,
                        });
                    }
                    catch (Exception e)
                    {
                        Log?.LogDebug("snapshot ritual skipped: " + e.Message);
                    }
                }
            }
            catch (Exception e) { Log?.LogWarning("snapshot rituals: " + e.Message); }

            try
            {
                foreach (var sigil in profile.CrusadeProgression.Progress)
                {
                    if (sigil == null) continue;
                    try
                    {
                        snapshot.Sigils.Add(new TierProgress
                        {
                            CharacterId = sigil.CharacterIdString ?? sigil.CharacterId ?? "",
                            Tier = (int)sigil.DifficultyTier,
                            DynamicLevel = sigil.DynamicLevel,
                        });
                    }
                    catch (Exception e)
                    {
                        Log?.LogDebug("snapshot sigil skipped: " + e.Message);
                    }
                }
            }
            catch (Exception e) { Log?.LogWarning("snapshot sigils: " + e.Message); }

            // Test-only runs injected through the bridge; empty in normal play.
            EndgameChecks.AppendInjected(snapshot);

            return snapshot;
        }

        public static string SnapshotJson()
        {
            var json = new Json().StartObject();

            if (!TryGetProfile(out var profile, out var error))
            {
                json.Name("ok").Value(false);
                json.Name("error").Value(error);
                return json.EndObject().ToString();
            }

            json.Name("ok").Value(true);
            json.Name("profile").Value(profile.Index);

            string profileName = null;
            try { profileName = profile.Metadata?.ProfileName; } catch { /* optional */ }
            json.Name("profileName").Value(profileName);

            // Unlocked characters
            json.Name("characters").StartArray();
            try
            {
                foreach (var id in profile.UnlockedCharacters.UnlockedCharacterIds)
                {
                    if (id != null) json.Value(id);
                }
            }
            catch (Exception e) { Log?.LogWarning("characters: " + e.Message); }
            json.EndArray();

            // Unlocked weapons
            json.Name("weapons").StartArray();
            try
            {
                foreach (var id in profile.UnlockedWeapons.UnlockedWeaponIds)
                {
                    if (id != null) json.Value(id);
                }
            }
            catch (Exception e) { Log?.LogWarning("weapons: " + e.Message); }
            json.EndArray();

            // Map progression
            json.Name("maps").StartArray();
            try
            {
                foreach (var map in profile.MapProgression.MapProgression)
                {
                    if (map == null) continue;
                    json.StartObject();
                    json.Name("id").Value(map.MapId ?? "");
                    json.Name("normal").Value(map.NormalModeCompleteness);
                    json.Name("affixes").StartArray();
                    try
                    {
                        foreach (var affix in map.CompletedAffixes)
                            if (affix != null) json.Value(affix);
                    }
                    catch { /* older saves may not have the field populated */ }
                    json.EndArray();
                    json.Name("tiers").StartArray();
                    try
                    {
                        foreach (var tier in map.AffixTierProgression)
                        {
                            if (tier == null || (int)tier.Tier <= 0) continue;
                            json.StartObject();
                            json.Name("tier").Value((int)tier.Tier);
                            json.Name("progress").Value(tier.Progression);
                            json.EndObject();
                        }
                    }
                    catch (Exception tierError) { Log?.LogDebug("map tiers: " + tierError.Message); }
                    json.EndArray();
                    json.EndObject();
                }
            }
            catch (Exception e) { Log?.LogWarning("maps: " + e.Message); }
            json.EndArray();

            // Per-character prestige
            json.Name("charactersProgress").StartArray();
            try
            {
                foreach (var p in profile.CharacterProgression.Progress)
                {
                    if (p == null) continue;
                    json.StartObject();
                    json.Name("id").Value(p.CharacterIdString ?? "");
                    json.Name("prestigeLevel").Value(p.CharacterPrestigeLevel);
                    json.Name("prestige").Value(p.CharacterPrestige);
                    json.EndObject();
                }
            }
            catch (Exception e) { Log?.LogWarning("charactersProgress: " + e.Message); }
            json.EndArray();

            // Achievement progression
            json.Name("achievements").StartArray();
            try
            {
                foreach (var a in profile.AchievementProgression.Achievements)
                {
                    if (a == null) continue;
                    json.StartObject();
                    json.Name("id").Value(a.AchievementId ?? "");
                    json.Name("progress").Value(a.Progression);
                    json.Name("done").Value(a.UnlockDateAsTicks > 0L);
                    json.EndObject();
                }
            }
            catch (Exception e) { Log?.LogWarning("achievements: " + e.Message); }
            json.EndArray();

            // Runes the skill tree already unlocks.
            json.Name("runes").StartArray();
            try
            {
                foreach (var runeId in RuneNodes.Owned(profile)) json.Value(runeId);
            }
            catch (Exception e) { Log?.LogWarning("runes: " + e.Message); }
            json.EndArray();

            // Endgame checks (curse ladder, rituals, sigils) already satisfied.
            json.Name("endgame").StartArray();
            try
            {
                var snapshot = Snapshot();
                if (snapshot != null)
                {
                    foreach (var id in EndgameChecks.Evaluate(snapshot))
                        json.Value(id);
                }
            }
            catch (Exception e) { Log?.LogWarning("endgame: " + e.Message); }
            json.EndArray();

            return json.EndObject().ToString();
        }

        // -------------------------------------------------------------- catalog

        public static string CatalogJson()
        {
            var json = new Json().StartObject();

            json.Name("characters").StartArray();
            try
            {
                foreach (var def in Global.CharacterCollection.Characters)
                {
                    if (def == null) continue;
                    json.StartObject();
                    json.Name("id").Value(def.CharacterId.ToString());
                    json.Name("auto").Value(def.AutoUnlock);
                    json.Name("hidden").Value(def.Hidden);
                    json.Name("unavailable").Value(def.Unavailable);
                    json.EndObject();
                }
            }
            catch (Exception e) { Log?.LogWarning("catalog characters: " + e.Message); }
            json.EndArray();

            json.Name("weapons").StartArray();
            try
            {
                foreach (var perCharacter in Global.WeaponCollection.WeaponsPerCharacter)
                {
                    if (perCharacter == null) continue;
                    var owner = perCharacter.CharacterId.ToString();
                    foreach (var weapon in perCharacter.Definitions)
                    {
                        if (weapon == null) continue;
                        json.StartObject();
                        json.Name("id").Value(weapon.WeaponId ?? "");
                        json.Name("character").Value(owner);
                        json.Name("auto").Value(weapon.AutoUnlock);
                        json.Name("hidden").Value(weapon.Hidden);
                        json.Name("mainSkill").Value(weapon.MainSkillId ?? "");
                        json.EndObject();
                    }
                }
            }
            catch (Exception e) { Log?.LogWarning("catalog weapons: " + e.Message); }
            json.EndArray();

            json.Name("maps").StartArray();
            try
            {
                foreach (var map in Global.MapCollection.Maps)
                {
                    if (map == null) continue;
                    json.StartObject();
                    json.Name("id").Value(map.MapId ?? "");
                    json.Name("numericId").Value(map.NumericalId);
                    json.Name("biome").Value(map.Biome.ToString());
                    json.Name("unavailable").Value(map.Unavailable);
                    json.Name("ignore").Value(map.IgnoreInMapList);
                    json.Name("crusade").Value(map.IsCrusadeMap);
                    json.EndObject();
                }
            }
            catch (Exception e) { Log?.LogWarning("catalog maps: " + e.Message); }
            json.EndArray();

            // Curse tiers: the game maps a tier to the curse intensity that unlocks
            // it, which is what the world needs to gate curse checks on tier items.
            json.Name("affixTiers").StartArray();
            try
            {
                var collection = Global.AffixCollection;
                if (collection != null)
                {
                    foreach (var definition in collection.AffixTiers)
                    {
                        if (definition == null) continue;
                        var tier = (int)definition.Tier;
                        if (tier <= 0) continue;
                        json.StartObject();
                        json.Name("tier").Value(tier);
                        json.Name("requiredCurse").Value(collection.GetRequiredCurseLevelForTier(definition.Tier));
                        json.EndObject();
                    }
                }
            }
            catch (Exception e) { Log?.LogWarning("catalog affix tiers: " + e.Message); }
            json.EndArray();

            // Runes live in the skill tree as dedicated unlock nodes, so the catalog
            // ships the node <-> rune mapping the world needs for items and checks.
            json.Name("runes").StartArray();
            try
            {
                foreach (var rune in RuneNodes.All())
                {
                    json.StartObject();
                    json.Name("id").Value(rune.RuneId);
                    json.Name("node").Value(rune.NodeId);
                    json.Name("character").Value(rune.Character);
                    json.EndObject();
                }
            }
            catch (Exception e) { Log?.LogWarning("catalog runes: " + e.Message); }
            json.EndArray();

            json.Name("achievements").StartArray();
            try
            {
                foreach (var ach in Global.AchievementCollection.AllAchievements)
                {
                    if (ach == null) continue;
                    json.StartObject();
                    json.Name("id").Value(ach.Id ?? "");
                    json.Name("disabled").Value(ach.Disabled);
                    json.Name("hidden").Value(ach.HiddenUntilCompleted);
                    json.Name("rewardType").Value(ach.RewardType.ToString());
                    json.EndObject();
                }
            }
            catch (Exception e) { Log?.LogWarning("catalog achievements: " + e.Message); }
            json.EndArray();

            return json.EndObject().ToString();
        }

        // --------------------------------------------------------------- grants

        public static string Grant(string item)
        {
            if (!Plugin.EnableGrants.Value) return "ERR grants are disabled in the config";
            if (!TryGetProfile(out var profile, out var error)) return "ERR " + error;

            var colon = item.IndexOf(':');
            if (colon <= 0) return "ERR malformed item '" + item + "'";
            var kind = item.Substring(0, colon).ToLowerInvariant();
            var id = item.Substring(colon + 1);

            // The gating patches block the game's own unlock routes; this flag is
            // what lets the item through (and lets the achievement it implies
            // complete, so the matching check still fires).
            ItemGating.Granting = true;
            try
            {
                switch (kind)
                {
                    case "char":
                    {
                        if (!Enum.TryParse<CharactersType>(id, true, out var character) || character == CharactersType.None)
                            return "ERR unknown character '" + id + "'";

                        if (HasCharacter(profile, id)) return "OK already unlocked " + id;

                        profile.UnlockedCharacters.AddUnlockedCharacter(character);
                        EnsureCharacterProgress(profile, character, id);
                        Log?.LogInfo($"granted character {id}");
                        SyncAchievements(profile, unlockCharacter: true);
                        Save(SaveFileKind.UnlockedCharacters, SaveFileKind.CharacterProgression,
                            SaveFileKind.AchievementProgression, SaveFileKind.TrackableUnlocks);
                        RefreshWeaponScreens();
                        return "OK unlocked character " + id;
                    }
                    case "weapon":
                    {
                        if (HasWeapon(profile, id)) return "OK already unlocked " + id;

                        profile.UnlockedWeapons.UnlockedWeaponIds.Add(id);
                        Log?.LogInfo($"granted weapon {id}");
                        SyncAchievements(profile, unlockWeapon: true);
                        Save(SaveFileKind.UnlockedWeapons,
                            SaveFileKind.AchievementProgression, SaveFileKind.TrackableUnlocks);
                        RefreshWeaponScreens();
                        return "OK unlocked weapon " + id;
                    }
                    case "curse":
                    {
                        // Curse:<n>: unlock curse tiers 1..n on every map. The game
                        // keeps one progress entry per tier per map, and a tier is
                        // usable once its required curse intensity is reachable.
                        if (!int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tier) || tier <= 0)
                            return "ERR bad curse tier '" + id + "'";

                        var changed = 0;
                        foreach (var map in profile.MapProgression.MapProgression)
                        {
                            if (map == null) continue;
                            for (var level = 1; level <= tier; level++)
                            {
                                if (EnsureTier(map, (AffixTier)level)) changed++;
                            }
                        }

                        Log?.LogInfo($"granted curse tier {tier} ({changed} map/tier entries written)");
                        if (Plugin.GrantedCurseTier != null && tier > Plugin.GrantedCurseTier.Value)
                            Plugin.GrantedCurseTier.Value = tier;
                        Save(SaveFileKind.MapProgression);
                        return $"OK unlocked curse tier {tier}";
                    }
                    case "resource":
                    {
                        // Resource:<percent>: pick a resource the save already holds
                        // (one of the six soulstone currencies or a mineral) and add
                        // that share of the current amount. Scales with the player
                        // instead of handing out a flat number.
                        var percent = 10d;
                        if (!string.IsNullOrEmpty(id))
                            double.TryParse(id, NumberStyles.Float, CultureInfo.InvariantCulture, out percent);
                        if (percent <= 0 || percent > 200) percent = 10;

                        // Half the caches pay out in resources, half in character XP.
                        // The roll is a plain coin flip so a seed's mix stays unpredictable.
                        if (UnityEngine.Random.value < 0.5f)
                        {
                            var xp = GrantCharacterXp(profile, percent, out var xpDetail);
                            if (xp != null) return xp;
                            Log?.LogInfo("resource cache XP roll found nobody to level: " + xpDetail + " - paying resources instead");
                        }

                        return ResourceShare(profile, percent);
                    }
                    case "weaponcap":
                    {
                        // WeaponCap:<level> (or -1/unlimited): raise the blacksmith
                        // ceiling. Nothing is written to the save - the cap lives in
                        // the plugin config and is enforced on UpgradeWeaponLevel.
                        var text = (id ?? "").Trim();
                        int cap;
                        if (text.Equals("unlimited", StringComparison.OrdinalIgnoreCase) || text == "max" || text == "-1")
                            cap = -1;
                        else if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out cap) || cap <= 0)
                            return "ERR bad weapon cap '" + id + "'";

                        var previous = Plugin.GrantedWeaponCap?.Value ?? 1;
                        // Unlimited sticks; otherwise only ever raise the ceiling.
                        if (cap < 0 || previous < 0 || cap > previous)
                        {
                            Plugin.GrantedWeaponCap.Value = cap;
                            Plugin.Instance?.Config.Save();
                        }

                        var shown = cap < 0 ? "unlimited" : "+" + cap;
                        Log?.LogInfo($"weapon upgrade cap -> {shown} (was {(previous < 0 ? "unlimited" : "+" + previous)})");
                        return "OK weapon upgrades allowed to " + shown;
                    }
                    case "map":
                    {
                        // Map:<mapId>: a map is "unlocked" when its progression entry
                        // exists, which is all the save keeps for maps.
                        if (string.IsNullOrEmpty(id)) return "ERR missing map id";
                        if (HasMap(profile, id)) return "OK already unlocked " + id;

                        AddMapEntry(profile, id);
                        Log?.LogInfo($"granted map {id}");
                        Save(SaveFileKind.MapProgression);
                        return "OK unlocked map " + id;
                    }
                    case "mode":
                    {
                        // Mode:<Mastery|Crusade>: Unholy Cathedral and Titan Hunt are
                        // gated by a RequiredAchievement on the GameModeSO. Completing
                        // that achievement in the save is the game's own unlock path;
                        // what happens inside the mode stays vanilla.
                        if (!TryResolveMode(id, out var mode, out var modeName))
                            return "ERR unknown game mode '" + id + "'";
                        if (mode.UnlockedByDefault)
                            return "OK " + modeName + " is unlocked by default";

                        var requirement = mode.GameModeData?.RequiredAchievement;
                        if (requirement == null) return "OK " + modeName + " needs no unlock";
                        if (AchievementDone(profile, requirement.Id))
                            return "OK " + modeName + " already unlocked";

                        Global.AchievementCollection.CompleteSpecificAchievement(
                            requirement.Id, profile.AchievementProgression, profile.TrackableUnlocks);
                        Log?.LogInfo($"granted mode {modeName} via achievement {requirement.Id}");
                        SyncAchievements(profile);
                        Save(SaveFileKind.AchievementProgression, SaveFileKind.TrackableUnlocks);
                        return "OK unlocked " + modeName;
                    }
                    case "rune":
                    {
                        // Rune:<runeId>: unlock the skill tree node that grants it,
                        // regardless of tier requirements or prerequisite nodes.
                        var node = RuneNodes.FindNode(id);
                        if (node == null) return "ERR unknown rune '" + id + "'";

                        if (HasSkillNode(profile, node.IdHash))
                            return "OK already unlocked " + id;

                        profile.SkillTree.Add(1, node);
                        Log?.LogInfo($"granted rune {id} as skill node {node.Id}");
                        SyncAchievements(profile, unlockRune: true);
                        Save(SaveFileKind.SkillTree,
                            SaveFileKind.AchievementProgression, SaveFileKind.TrackableUnlocks);
                        return "OK unlocked rune " + id;
                    }
                    case "souls":
                    {
                        // Souls:<n>        - minor soulstones (the world's filler)
                        // Souls:<name>:<n> - any of the six currencies, e.g. Souls:Vile:500
                        var name = "minor";
                        var amountText = id;
                        var separator = id.IndexOf(':');
                        if (separator > 0)
                        {
                            name = id.Substring(0, separator);
                            amountText = id.Substring(separator + 1);
                        }

                        if (!int.TryParse(amountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
                            return "ERR bad soul amount '" + amountText + "'";

                        if (!TryAddCurrency(profile.Currencies, name, amount, out var field))
                            return "ERR unknown soulstone '" + name + "' - use minor, corrupted, vile, wicked, hateful or rogue";

                        Log?.LogInfo($"granted {amount} {field}");
                        Save(SaveFileKind.Currencies);
                        return $"OK granted {amount} {field}";
                    }
                    default:
                        return "ERR unknown item kind '" + kind + "'";
                }
            }
            catch (Exception e)
            {
                Log?.LogError($"grant {item} failed: {e}");
                return "ERR " + e.GetType().Name + ": " + e.Message;
            }
            finally
            {
                ItemGating.Granting = false;
            }
        }

        /// <summary>
        /// The game only re-runs its reward scans when a profile is loaded, so a
        /// weapon or character written straight into the save sits there unused
        /// until the next restart. These are the same updaters the load path
        /// calls, run right after the grant instead.
        /// </summary>
        private static void SyncAchievements(PlayerProfile profile, bool unlockCharacter = false, bool unlockWeapon = false, bool unlockRune = false)
        {
            try
            {
                var collection = Global.AchievementCollection;
                if (collection == null) return;

                var achievements = profile.AchievementProgression;
                var trackable = profile.TrackableUnlocks;

                if (unlockCharacter)
                {
                    collection.UpdateUnlockCharacterAchievements(profile.UnlockedCharacters, achievements, trackable);
                    collection.UpdateUnlockCharacterCountAchievements(profile.UnlockedCharacters, achievements, trackable);
                    collection.UpdateReachPrestigeLevelWithMultipleCharactersAchievements(
                        profile.UnlockedCharacters, profile.CharacterProgression, achievements, trackable);
                }

                if (unlockRune)
                {
                    collection.UpdateUnlockSkillTreeAchievements(profile.SkillTree, achievements, trackable);
                    collection.UpdateCompleteCharacterSkillTreeCountAchievements(
                        profile.CompletedCharacterSkillTrees, achievements, trackable);
                }

                if (unlockWeapon)
                {
                    // "Craft <weapon>" and "craft every weapon of X" - in vanilla
                    // these fire when the blacksmith crafts the weapon.
                    collection.UpdateCraftWeaponAchievements(profile.UnlockedWeapons, achievements, trackable);
                    collection.UpdateCraftAllCharacterWeaponsAchievements(
                        profile.UnlockedCharacters, profile.UnlockedWeapons, achievements, trackable);
                }
            }
            catch (Exception e)
            {
                Log?.LogWarning("achievement refresh: " + e.Message);
            }
        }

        /// <summary>
        /// Rebuilds the blacksmith and character screens if they happen to be open,
        /// so a weapon that just arrived is immediately shown and upgradeable.
        /// </summary>
        private static void RefreshWeaponScreens()
        {
            try
            {
                foreach (var panel in UnityEngine.Object.FindObjectsOfType<CraftingScreenPanel>())
                    RebuildBlacksmith(panel);
            }
            catch (Exception e) { Log?.LogDebug("blacksmith refresh: " + e.Message); }

            try
            {
                foreach (var panel in UnityEngine.Object.FindObjectsOfType<CharacterScreenPanel>())
                {
                    if (panel == null || !panel.IsSetUp) continue;
                    panel.ForceWeaponAreaRebuild();
                }
            }
            catch (Exception e) { Log?.LogDebug("character screen refresh: " + e.Message); }
        }

        /// <summary>
        /// The blacksmith has no public "rebuild" call, so its weapon row is
        /// rebuilt for the character of the weapon it currently has selected.
        /// </summary>
        private static void RebuildBlacksmith(CraftingScreenPanel panel)
        {
            if (panel == null || !panel.IsSetUp) return;

            try
            {
                var selected = panel.CurrentSelectedWeapon;
                if (selected == null || selected.Definition == null) return;

                if (!Global.WeaponCollection.TryGetWeaponDefinitionById(
                        selected.Definition.WeaponId, out var definition, out var characterId))
                    return;

                panel.SetupCharacterWeapons(characterId);
                panel.UpdateNotifications();
            }
            catch (Exception e) { Log?.LogDebug("blacksmith rebuild: " + e.Message); }
        }

        /// <summary>
        /// Writes the given profile files to disk. `RequestSaveAll` looks like the
        /// obvious call but silently writes nothing outside the game's own flow,
        /// so the per-file API is used instead.
        /// </summary>
        public static string Save(params SaveFileKind[] kinds)
        {
            if (!TryGetProfile(out var profile, out var error)) return "ERR " + error;
            if (kinds == null || kinds.Length == 0) return "OK";

            try
            {
                var util = PlayerDataUtil.Default;
                if (util == null) return "ERR save system unavailable";
                if (util.IsSaveInProgress()) return "BUSY";

                // Entries written by older builds may have null affix lists; the
                // serializer would throw on them exactly like a fresh one.
                foreach (var kind in kinds)
                {
                    if (kind != SaveFileKind.MapProgression) continue;
                    RepairMapEntries(profile);
                    break;
                }

                var files = new Il2CppStructArray<SaveFileKind>(kinds.Length);
                for (var i = 0; i < kinds.Length; i++) files[i] = kinds[i];
                util.RequestSaveFiles(profile, files);
                return "OK";
            }
            catch (Exception e)
            {
                Log?.LogError("save failed: " + e);
                return "ERR " + e.GetType().Name + ": " + e.Message;
            }
        }

        /// <summary>
        /// The game expects a progression entry for every unlocked character and
        /// logs "Could not load Progression Data" without one.
        /// </summary>
        private static void EnsureCharacterProgress(PlayerProfile profile, CharactersType character, string id)
        {
            try
            {
                foreach (var existing in profile.CharacterProgression.Progress)
                {
                    if (existing == null) continue;
                    if (string.Equals(existing.CharacterIdString, id, StringComparison.OrdinalIgnoreCase)) return;
                }

                var data = new PlayerProfileCharacterProgressionData();
                data.CharacterId = id;
                data.CharacterPrestigeLevel = 1;
                data.CharacterPrestige = 0f;
                profile.CharacterProgression.AddProgress(data);
                Log?.LogInfo("added progression entry for " + id);
            }
            catch (Exception e)
            {
                Log?.LogWarning("could not add progression entry for " + id + ": " + e.Message);
            }
        }

        /// <summary>
        /// The save holds six currencies. Names are matched loosely so
        /// `vile`, `Vile`, `VileSoulstones` and `vile_soulstones` all work;
        /// an empty or missing name means minor soulstones.
        /// </summary>
        private static bool TryAddCurrency(PlayerProfileCurrencies currencies, string name, int amount, out string field)
        {
            field = null;
            var key = (name ?? "").Trim().ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");
            if (key.EndsWith("soulstones")) key = key.Substring(0, key.Length - "soulstones".Length);

            switch (key)
            {
                case "":
                case "minor":
                    currencies.MinorSoulstones += amount;
                    field = "MinorSoulstones";
                    return true;
                case "corrupted":
                    currencies.CorruptedSoulstones += amount;
                    field = "CorruptedSoulstones";
                    return true;
                case "vile":
                    currencies.VileSoulstones += amount;
                    field = "VileSoulstones";
                    return true;
                case "wicked":
                    currencies.WickedSoulstones += amount;
                    field = "WickedSoulstones";
                    return true;
                case "hateful":
                    currencies.HatefulSoulstones += amount;
                    field = "HatefulSoulstones";
                    return true;
                case "rogue":
                    currencies.RogueSoulstones += amount;
                    field = "RogueSoulstones";
                    return true;
                default:
                    return false;
            }
        }

        private static int ReadCurrency(PlayerProfileCurrencies currencies, string key)
        {
            switch ((key ?? "").ToLowerInvariant())
            {
                case "minor": return currencies.MinorSoulstones;
                case "corrupted": return currencies.CorruptedSoulstones;
                case "vile": return currencies.VileSoulstones;
                case "wicked": return currencies.WickedSoulstones;
                case "hateful": return currencies.HatefulSoulstones;
                case "rogue": return currencies.RogueSoulstones;
                default: return 0;
            }
        }

        private static string CurrencyFieldName(string key)
        {
            switch ((key ?? "").ToLowerInvariant())
            {
                case "minor": return "MinorSoulstones";
                case "corrupted": return "CorruptedSoulstones";
                case "vile": return "VileSoulstones";
                case "wicked": return "WickedSoulstones";
                case "hateful": return "HatefulSoulstones";
                case "rogue": return "RogueSoulstones";
                default: return key ?? "";
            }
        }

        private static bool HasMap(PlayerProfile profile, string mapId)
        {
            try
            {
                foreach (var map in profile.MapProgression.MapProgression)
                {
                    if (map == null) continue;
                    if (string.Equals(map.MapId, mapId, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch (Exception e) { Log?.LogDebug("map lookup: " + e.Message); }
            return false;
        }

        private static void AddMapEntry(PlayerProfile profile, string mapId)
        {
            var entry = new PlayerProfileMapProgressionData();
            entry.MapId = mapId;
            entry.NormalModeCompleteness = 0f;
            entry.AffixesDisabled = false;

            // The save serializer walks all three lists, so a hand-made entry has to
            // create them: leaving them null crashes RequestSaveMapProgression with a
            // NullReferenceException and takes the whole save with it.
            entry.SelectedAffixes = new Il2CppSystem.Collections.Generic.List<string>();
            entry.CompletedAffixes = new Il2CppSystem.Collections.Generic.List<string>();
            entry.AffixTierProgression =
                new Il2CppSystem.Collections.Generic.List<PlayerProfileAffixTierProgressionData>();

            profile.MapProgression.MapProgression.Add(entry);
        }

        /// <summary>Gives every map entry the lists the save serializer walks.</summary>
        private static void RepairMapEntries(PlayerProfile profile)
        {
            try
            {
                foreach (var map in profile.MapProgression.MapProgression)
                {
                    if (map == null) continue;
                    if (map.SelectedAffixes == null)
                        map.SelectedAffixes = new Il2CppSystem.Collections.Generic.List<string>();
                    if (map.CompletedAffixes == null)
                        map.CompletedAffixes = new Il2CppSystem.Collections.Generic.List<string>();
                    if (map.AffixTierProgression == null)
                        map.AffixTierProgression =
                            new Il2CppSystem.Collections.Generic.List<PlayerProfileAffixTierProgressionData>();
                }
            }
            catch (Exception e) { Log?.LogDebug("map entry repair: " + e.Message); }
        }

        /// <summary>
        /// Gives a share of one already-owned resource back. Only resources the save
        /// actually holds are candidates, so the item can never roll an empty pool.
        /// </summary>
        /// <summary>
        /// One cache's worth of character XP, handed to a random character the save
        /// owns. The amount comes from the game's own prestige table
        /// (`PrestigeConfigSO.GetRequirementForLevel`), so it means "about a level"
        /// at any depth. Only the XP field is written; the level is left for the game
        /// to recompute, because the table's shape (per-level vs cumulative) is not
        /// visible from outside and guessing it in the save would be destructive.
        /// Returns null when there is nobody to level, so the caller can fall back.
        /// </summary>
        private static string GrantCharacterXp(PlayerProfile profile, double percent, out string detail)
        {
            detail = "none";
            var candidates = new List<PlayerProfileCharacterProgressionData>();
            try
            {
                foreach (var entry in profile.CharacterProgression.Progress)
                {
                    if (entry != null && !string.IsNullOrEmpty(entry.CharacterId)) candidates.Add(entry);
                }
            }
            catch (Exception e)
            {
                detail = "could not read character progression: " + e.Message;
                return null;
            }

            if (candidates.Count == 0)
            {
                detail = "no character progression entries";
                return null;
            }

            var pick = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            var level = pick.CharacterPrestigeLevel;

            float requirement;
            try
            {
                requirement = Global.PrestigeConfig.GetRequirementForLevel(level + 1);
            }
            catch (Exception e)
            {
                detail = "prestige table unreadable: " + e.Message;
                return null;
            }

            var gained = (int)Math.Round(requirement * percent / 100d, MidpointRounding.AwayFromZero);
            if (gained < 1) gained = 1;
            if (gained > 1_000_000) gained = 1_000_000;

            var before = pick.CharacterPrestige;
            var levelBefore = pick.CharacterPrestigeLevel;
            try
            {
                pick.CharacterPrestige = before + gained;

                // The game only recomputes the level inside its own match-end code,
                // so a grant has to do it. The measured rule is flat: every level
                // costs the same requirement (5000 in this build), and level 1 is
                // the floor. Never lowered, capped at the game's ceiling.
                if (requirement > 0f)
                {
                    var derived = (int)Math.Floor(pick.CharacterPrestige / requirement) + 1;
                    if (derived > 150) derived = 150;
                    if (derived > pick.CharacterPrestigeLevel) pick.CharacterPrestigeLevel = derived;
                }

                Save(SaveFileKind.CharacterProgression);
            }
            catch (Exception e)
            {
                detail = "write failed: " + e.Message;
                return null;
            }

            SyncPrestigeAchievements(profile);

            Log?.LogInfo($"character xp: {pick.CharacterId} level {levelBefore} -> {pick.CharacterPrestigeLevel}, " +
                         $"prestige {before} -> {pick.CharacterPrestige} (+{gained}, table says {requirement:0.#} per level)");
            return $"OK granted {gained} prestige to {pick.CharacterId} " +
                   $"(level {levelBefore} -> {pick.CharacterPrestigeLevel}, prestige {before} -> {pick.CharacterPrestige})";
        }

        /// <summary>
        /// A level written straight into the save does not fire the prestige
        /// achievements by itself, so run the same updater the load path uses.
        /// </summary>
        private static void SyncPrestigeAchievements(PlayerProfile profile)
        {
            try
            {
                var collection = Global.AchievementCollection;
                if (collection == null) return;

                foreach (var entry in profile.CharacterProgression.Progress)
                {
                    if (entry == null) continue;
                    collection.UpdatePrestigeLevelWithCharacterAchievements(
                        entry.CharacterIdAsEnum, entry.CharacterPrestigeLevel,
                        profile.AchievementProgression, profile.TrackableUnlocks);
                }

                collection.UpdateReachPrestigeLevelWithMultipleCharactersAchievements(
                    profile.UnlockedCharacters, profile.CharacterProgression,
                    profile.AchievementProgression, profile.TrackableUnlocks);

                Save(SaveFileKind.AchievementProgression, SaveFileKind.TrackableUnlocks);
            }
            catch (Exception e)
            {
                Log?.LogWarning("prestige achievement sync failed: " + e.Message);
            }
        }

        /// <summary>Diagnostic: what the save holds for characters and what the level table says.</summary>
        public static string PrestigeDump()
        {
            if (!TryGetProfile(out var profile, out var error)) return "ERR " + error;

            var json = new Json();
            json.StartObject();
            json.Name("characters").StartArray();
            try
            {
                foreach (var entry in profile.CharacterProgression.Progress)
                {
                    if (entry == null) continue;
                    json.StartObject();
                    json.Name("id").Value(entry.CharacterId ?? "");
                    json.Name("level").Value(entry.CharacterPrestigeLevel);
                    json.Name("prestige").Value(entry.CharacterPrestige);
                    json.EndObject();
                }
            }
            catch (Exception e) { Log?.LogDebug("prestige dump: " + e.Message); }
            json.EndArray();

            json.Name("requirements").StartArray();
            try
            {
                for (var level = 1; level <= 6; level++)
                {
                    json.StartObject();
                    json.Name("level").Value(level);
                    json.Name("requirement").Value(Global.PrestigeConfig.GetRequirementForLevel(level));
                    json.EndObject();
                }
            }
            catch (Exception e) { Log?.LogDebug("prestige table: " + e.Message); }
            json.EndArray();
            json.EndObject();

            return "OK " + Uri.EscapeDataString(json.ToString());
        }

        private static string ResourceShare(PlayerProfile profile, double percent)
        {
            var options = new List<string>();          // what the player sees
            var currencyKeys = new List<string>();     // null for a material
            var materialIds = new List<ItemId>();
            var amounts = new List<int>();

            try
            {
                foreach (var key in new[] { "minor", "corrupted", "vile", "wicked", "hateful", "rogue" })
                {
                    var value = ReadCurrency(profile.Currencies, key);
                    if (value <= 0) continue;
                    options.Add(CurrencyFieldName(key));
                    currencyKeys.Add(key);
                    materialIds.Add(ItemId.None);
                    amounts.Add(value);
                }

                foreach (var item in profile.Inventory.Items)
                {
                    if (item == null || item.Quantity <= 0) continue;
                    var raw = (int)item.ItemId;
                    if (raw < (int)ItemId.MineralCommon || raw > (int)ItemId.LegendaryTourmaline) continue;
                    options.Add(item.ItemId.ToString());
                    currencyKeys.Add(null);
                    materialIds.Add(item.ItemId);
                    amounts.Add(item.Quantity);
                }
            }
            catch (Exception e)
            {
                Log?.LogError("resource share failed while reading holdings: " + e.Message);
                return "ERR " + e.GetType().Name + ": " + e.Message;
            }

            if (options.Count == 0)
            {
                // Nothing to scale from yet: a small flat gift so the item is not lost.
                TryAddCurrency(profile.Currencies, "minor", 25, out var field);
                Save(SaveFileKind.Currencies);
                Log?.LogInfo("resource cache found nothing to scale from - gave 25 " + field);
                return "OK no holdings yet, granted 25 " + field;
            }

            var pick = UnityEngine.Random.Range(0, options.Count);
            var current = amounts[pick];
            var gained = (int)Math.Round(current * percent / 100d, MidpointRounding.AwayFromZero);
            if (gained < 1) gained = 1;

            try
            {
                if (currencyKeys[pick] != null)
                {
                    TryAddCurrency(profile.Currencies, currencyKeys[pick], gained, out _);
                    Save(SaveFileKind.Currencies);
                }
                else
                {
                    var existing = profile.Inventory.TryGetItemById(materialIds[pick]);
                    if (existing != null)
                    {
                        existing.Quantity += gained;
                        Save(SaveFileKind.Inventory);
                    }
                    else
                    {
                        var entry = new InventoryItemData();
                        entry.ItemId = materialIds[pick];
                        entry.Quantity = gained;
                        profile.Inventory.Items.Add(entry);
                        Save(SaveFileKind.Inventory);
                    }
                }
            }
            catch (Exception e)
            {
                Log?.LogError("resource share failed while granting: " + e.Message);
                return "ERR " + e.GetType().Name + ": " + e.Message;
            }

            Log?.LogInfo($"resource cache: +{gained} {options[pick]} ({percent:0.#}% of {current})");
            return $"OK granted {gained} {options[pick]} ({percent:0.#}% of {current})";
        }

        /// <summary>
        /// Adds crafting materials to the inventory, so blacksmith upgrades can be
        /// tested without farming. `id` is an ItemId name (CommonSilver,
        /// UncommonSapphire, RareMithril, ...); empty or "all" means every mineral.
        /// </summary>
        public static string AddMaterials(string id, int amount)
        {
            if (!TryGetProfile(out var profile, out var error)) return "ERR " + error;
            if (amount <= 0) return "ERR bad amount '" + amount + "'";

            try
            {
                var wanted = new List<ItemId>();
                if (string.IsNullOrWhiteSpace(id) || string.Equals(id.Trim(), "all", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var definition in Global.ItemCollection.Items)
                    {
                        if (definition == null || definition.ItemType != ItemType.Mineral) continue;
                        wanted.Add(definition.ItemId);
                    }
                }
                else if (!Enum.TryParse<ItemId>(id.Trim(), true, out var single) || single == ItemId.None)
                {
                    return "ERR unknown material '" + id + "' - e.g. CommonSilver, UncommonSapphire, RareMithril";
                }
                else
                {
                    wanted.Add(single);
                }

                if (wanted.Count == 0) return "ERR no materials found to add";

                var touched = 0;
                foreach (var itemId in wanted)
                {
                    var existing = profile.Inventory.TryGetItemById(itemId);
                    if (existing != null)
                    {
                        existing.Quantity += amount;
                        touched++;
                        continue;
                    }

                    var entry = new InventoryItemData();
                    entry.ItemId = itemId;
                    entry.Quantity = amount;
                    profile.Inventory.Items.Add(entry);
                    touched++;
                }

                Log?.LogInfo($"added {amount} of {touched} material(s) ({(string.IsNullOrWhiteSpace(id) ? "all" : id)})");
                Save(SaveFileKind.Inventory);
                return $"OK added {amount} to {touched} material(s)";
            }
            catch (Exception e)
            {
                Log?.LogError("adding materials failed: " + e);
                return "ERR " + e.GetType().Name + ": " + e.Message;
            }
        }

        /// <summary>
        /// Removes map progression entries, which is the only per-map state the save
        /// keeps. That takes the map's completeness, its affix selections and its
        /// curse tier rows with it, so tiers have to be granted again. Checks already
        /// sent to the server stay sent - this only clears the save side.
        /// </summary>
        public static string ResetMaps(string which)
        {
            if (!TryGetProfile(out var profile, out var error)) return "ERR " + error;

            var all = string.IsNullOrEmpty(which) || string.Equals(which.Trim(), "all", StringComparison.OrdinalIgnoreCase);
            var removed = 0;
            var names = new List<string>();

            try
            {
                var list = profile.MapProgression.MapProgression;
                for (var index = list.Count - 1; index >= 0; index--)
                {
                    var entry = list[index];
                    if (entry == null)
                    {
                        list.RemoveAt(index);
                        continue;
                    }

                    if (!all && !string.Equals(entry.MapId, which.Trim(), StringComparison.OrdinalIgnoreCase)) continue;

                    names.Add(entry.MapId ?? "?");
                    list.RemoveAt(index);
                    removed++;
                }
            }
            catch (Exception e)
            {
                Log?.LogError("resetting maps failed: " + e);
                return "ERR " + e.GetType().Name + ": " + e.Message;
            }

            if (removed == 0) return "OK nothing to remove";

            Save(SaveFileKind.MapProgression);
            Log?.LogInfo($"removed {removed} map progression entr{(removed == 1 ? "y" : "ies")}: {string.Join(", ", names)}");
            return $"OK removed {removed} map(s): {string.Join(", ", names)}";
        }

        /// <summary>
        /// Marks a map (or every map in the save) as fully cleared. The affix
        /// panel — the I-VII badges and the curse checkbox on the map screen —
        /// only comes alive for a map the save has beaten, so this is the test
        /// switch for seeing curse selection. Note that `Clear <map>` checks read
        /// the same value, so they will fire for whatever this touches.
        /// </summary>
        public static string CompleteMaps(string which)
        {
            if (!TryGetProfile(out var profile, out var error)) return "ERR " + error;

            var all = string.IsNullOrEmpty(which) || string.Equals(which.Trim(), "all", StringComparison.OrdinalIgnoreCase);
            var changed = 0;
            try
            {
                foreach (var map in profile.MapProgression.MapProgression)
                {
                    if (map == null) continue;
                    if (!all && !string.Equals(map.MapId, which.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                    if (map.NormalModeCompleteness >= 1f) continue;
                    map.NormalModeCompleteness = 1f;
                    changed++;
                }
            }
            catch (Exception e)
            {
                Log?.LogError("completing maps failed: " + e);
                return "ERR " + e.GetType().Name + ": " + e.Message;
            }

            if (changed == 0) return "OK nothing to complete";

            Save(SaveFileKind.MapProgression);
            Log?.LogInfo($"set {changed} map(s) to 100% completeness ({which ?? "all"})");
            return $"OK set {changed} map(s) to 100% completeness";
        }

        /// <summary>
        /// Used when the seed says maps are not items (map_unlocks off): every
        /// standard map is written into the save once, so a fresh profile can
        /// pick any of them without waiting for an item.
        /// </summary>
        public static string UnlockMaps(IEnumerable<string> mapIds)
        {
            if (!TryGetProfile(out var profile, out var error)) return "ERR " + error;

            var added = 0;
            try
            {
                foreach (var mapId in mapIds)
                {
                    if (string.IsNullOrEmpty(mapId) || HasMap(profile, mapId)) continue;
                    AddMapEntry(profile, mapId);
                    added++;
                }

                if (added > 0)
                {
                    Log?.LogInfo($"unlocked {added} map(s) for this seed (maps are not items)");
                    Save(SaveFileKind.MapProgression);
                }
            }
            catch (Exception e)
            {
                Log?.LogError("unlocking maps failed: " + e);
                return "ERR " + e.GetType().Name + ": " + e.Message;
            }

            return added > 0 ? "OK unlocked " + added + " map(s)" : "OK maps already unlocked";
        }

        // --- curse tier guard -------------------------------------------------
        // The Progression setter cannot be patched on this build (Harmony fails
        // with an IL Compile Error), so instead of intercepting the write the
        // save is swept: any tier above the highest one an item granted is reset
        // once no run is live. Progress is only ever lowered, never raised here.
        private static float _nextTierSweep;

        public static void TickTierGuard()
        {
            if (_nextTierSweep > 0f && UnityEngine.Time.unscaledTime < _nextTierSweep) return;
            _nextTierSweep = UnityEngine.Time.unscaledTime + 3f;

            // 0 = nothing granted yet, so leave the vanilla route alone rather than
            // wiping tiers the player earned before ever installing the mod.
            var granted = Plugin.GrantedCurseTier?.Value ?? 0;
            if (granted <= 0) return;

            // The badge row always offers one tier past what the save holds, so
            // that next tier's progress has to be allowed to stick - otherwise a
            // run you legitimately finish could never bank its tier.
            var ceiling = granted + 1;
            if (PlayerRefs.TryGet(out var _)) return;   // a run is live: the game owns the save right now
            if (!TryGetProfile(out var profile, out var _)) return;

            try
            {
                var cleared = 0;
                foreach (var map in profile.MapProgression.MapProgression)
                {
                    if (map?.AffixTierProgression == null) continue;
                    foreach (var entry in map.AffixTierProgression)
                    {
                        if (entry == null) continue;
                        if ((int)entry.Tier <= ceiling) continue;
                        if (entry.Progression <= 0f) continue;
                        entry.Progression = 0f;
                        cleared++;
                    }
                }

                if (cleared > 0)
                {
                    Log?.LogInfo($"cleared {cleared} curse tier entr{(cleared == 1 ? "y" : "ies")} above tier {ceiling} (granted {granted} + the one the badge row offers) - tiers only come from items");
                    Save(SaveFileKind.MapProgression);
                }
            }
            catch (Exception e)
            {
                Log?.LogError("curse tier guard failed: " + e.Message);
            }
        }

        /// <summary>Marks one tier complete on a map. Returns true when it changed something.</summary>
        private static bool EnsureTier(PlayerProfileMapProgressionData map, AffixTier tier)
        {
            var tiers = map.AffixTierProgression;
            if (tiers == null)
            {
                tiers = new Il2CppSystem.Collections.Generic.List<PlayerProfileAffixTierProgressionData>();
                map.AffixTierProgression = tiers;
            }

            foreach (var existing in tiers)
            {
                if (existing == null || existing.Tier != tier) continue;
                if (existing.Progression >= 1f) return false;
                existing.Progression = 1f;
                return true;
            }

            var entry = new PlayerProfileAffixTierProgressionData();
            entry.Tier = tier;
            entry.Progression = 1f;
            tiers.Add(entry);
            return true;
        }

        /// <summary>Highest tier the save has completed on the given map (0 = none).</summary>
        public static int HighestTier(PlayerProfileMapProgressionData map)
        {
            var highest = 0;
            if (map?.AffixTierProgression == null) return 0;
            foreach (var tier in map.AffixTierProgression)
            {
                if (tier == null || tier.Progression < 1f) continue;
                var value = (int)tier.Tier;
                if (value > highest) highest = value;
            }
            return highest;
        }

        /// <summary>The GameModeSO behind a `Mode:` item (`Mastery`, `Crusade`).</summary>
        private static bool TryResolveMode(string name, out GameModeDefinition mode, out string modeName)
        {
            mode = null;
            modeName = null;
            if (string.IsNullOrEmpty(name)) return false;

            try
            {
                foreach (var candidate in Global.GameModeCollection.Modes)
                {
                    if (candidate == null) continue;
                    var candidateName = candidate.GameModeType.ToString();
                    if (!string.Equals(candidateName, name.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                    mode = candidate;
                    modeName = candidateName;
                    return true;
                }
            }
            catch (Exception e)
            {
                Log?.LogWarning("game mode lookup: " + e.Message);
            }

            return false;
        }

        private static bool AchievementDone(PlayerProfile profile, string achievementId)
        {
            try
            {
                foreach (var achievement in profile.AchievementProgression.Achievements)
                {
                    if (achievement == null) continue;
                    if (!string.Equals(achievement.AchievementId, achievementId, StringComparison.OrdinalIgnoreCase)) continue;
                    return achievement.UnlockDateAsTicks > 0L || achievement.Progression >= 0.999f;
                }
            }
            catch (Exception e)
            {
                Log?.LogDebug("achievement lookup: " + e.Message);
            }

            return false;
        }

        private static bool HasSkillNode(PlayerProfile profile, int idHash)
        {
            try
            {
                return profile.SkillTree.TryGetSkillTreeItem(idHash, out var level, out var _) && level > 0;
            }
            catch (Exception e)
            {
                Log?.LogDebug("skill node lookup: " + e.Message);
                return false;
            }
        }

        private static bool HasCharacter(PlayerProfile profile, string id)
        {
            foreach (var existing in profile.UnlockedCharacters.UnlockedCharacterIds)
            {
                if (string.Equals(existing, id, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool HasWeapon(PlayerProfile profile, string id)
        {
            foreach (var existing in profile.UnlockedWeapons.UnlockedWeaponIds)
            {
                if (string.Equals(existing, id, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
