using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;

namespace SoulstoneAP
{
    /// <summary>
    /// Makes characters, runes and the two endgame modes item-only.
    ///
    /// The game's own routes are still there — an unlock achievement can award a
    /// character, a mode unlocks when its RequiredAchievement completes, and a
    /// rune node can be bought with skill points. Each of those would hand out
    /// something the multiworld is supposed to provide, so they are blocked
    /// unless the grant is the one doing it (<see cref="Granting"/>).
    /// </summary>
    internal static class ItemGating
    {
        private const string HarmonyId = "soulstone.ap.itemgating";

        /// <summary>Set while the mod itself is writing an item into the save.</summary>
        public static bool Granting;

        private static readonly HashSet<string> Logged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static List<string> _modeAchievements;
        private static bool _mapGetterLogged;

        public static void Apply(ConfigEntry<bool> enabled)
        {
            var log = Plugin.Logger;
            if (!enabled.Value)
            {
                log.LogInfo("ItemGating: characters / runes / modes keep their normal unlock routes");
                return;
            }

            // Each patch stands alone: one hook failing must not take the others
            // with it (that is exactly how the mode gate got lost once).
            TryPatch(log, "characters can only be unlocked by items", () =>
            {
                var target = AccessTools.Method(typeof(PlayerProfileUnlockedCharacters), "AddUnlockedCharacter");
                if (target == null) throw new MissingMemberException("PlayerProfileUnlockedCharacters.AddUnlockedCharacter");
                new Harmony(HarmonyId).Patch(target, prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(ItemGating), nameof(BlockAchievementCharacter))));
            }, "characters keep the achievement route");

            TryPatch(log, "rune nodes can only be unlocked by items", () =>
            {
                var target = AccessTools.Method(typeof(PlayerProfileSkillTree), "Add");
                if (target == null) throw new MissingMemberException("PlayerProfileSkillTree.Add");
                new Harmony(HarmonyId).Patch(target, prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(ItemGating), nameof(BlockLocalRuneBuy))));
            }, "rune nodes stay buyable");

            TryPatch(log, "blacksmith upgrades stop at the highest cap an item granted", () =>
            {
                // Both hooks matter: the panel one refuses the press before the
                // materials are spent, the level one is the backstop for any other
                // path into the upgrade.
                var press = AccessTools.Method(typeof(CraftingScreenPanel), "DoOnCraftPressed");
                if (press == null) throw new MissingMemberException("CraftingScreenPanel.DoOnCraftPressed");
                new Harmony(HarmonyId).Patch(press, prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(ItemGating), nameof(BlockBlacksmithPress))));

                var upgrade = AccessTools.Method(typeof(PlayerProfileWeaponsLevel), "UpgradeWeaponLevel");
                if (upgrade == null) throw new MissingMemberException("PlayerProfileWeaponsLevel.UpgradeWeaponLevel");
                new Harmony(HarmonyId).Patch(upgrade, prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(ItemGating), nameof(BlockWeaponUpgrade))));
            }, "the blacksmith upgrades without a ceiling");

            TryPatch(log, "characters cannot be bought before their item arrives", () =>
            {
                // Same pattern as the blacksmith: the buy button spends the currency
                // first and calls AddUnlockedCharacter after, which the character gate
                // drops. Refusing the press keeps the cost.
                var select = AccessTools.Method(typeof(CharacterScreenPanel), "OnSelectPressed");
                if (select == null) throw new MissingMemberException("CharacterScreenPanel.OnSelectPressed");
                new Harmony(HarmonyId).Patch(select, prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(ItemGating), nameof(BlockCharacterPurchase))));

                var controller = AccessTools.Method(typeof(CharacterScreenPanel), "ControllerPurchase");
                if (controller != null)
                    new Harmony(HarmonyId).Patch(controller, prefix: new HarmonyMethod(
                        AccessTools.Method(typeof(ItemGating), nameof(BlockCharacterPurchase))));
            }, "the character screen still sells characters");

            TryPatch(log, "maps can only be opened once their item arrived", () =>
            {
                // The map list reads achievement state, which is the game's own unlock
                // route: both the lock state and the click are overridden so a map
                // nobody sent stays shut.
                var completed = AccessTools.PropertyGetter(typeof(MapItem), "IsRequiredAchievementCompleted");
                if (completed == null) throw new MissingMemberException("MapItem.IsRequiredAchievementCompleted");
                new Harmony(HarmonyId).Patch(completed, prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(ItemGating), nameof(MapRequirementMet))));

                var select = AccessTools.Method(typeof(MapItem), "OnSelectPressed");
                if (select == null) throw new MissingMemberException("MapItem.OnSelectPressed");
                new Harmony(HarmonyId).Patch(select, prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(ItemGating), nameof(BlockMapSelection))));

                // The module's own SelectMap is what the list actually calls, so the
                // click is refused here too - OnSelectPressed never fires in practice.
                var pick = AccessTools.Method(typeof(MapSelectionLevelSelectionModule), "SelectMap");
                if (pick == null) throw new MissingMemberException("MapSelectionLevelSelectionModule.SelectMap");
                new Harmony(HarmonyId).Patch(pick, prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(ItemGating), nameof(BlockMapPick))));

                // The lock icon turned out not to follow that property, so the run
                // start is blocked as well: a map nobody sent cannot be played even
                // if its button looks open.
                var fight = AccessTools.Method(typeof(MapSelectionSurvivorsScreenPanel), "OnFightPressed");
                if (fight == null) throw new MissingMemberException("MapSelectionSurvivorsScreenPanel.OnFightPressed");
                new Harmony(HarmonyId).Patch(fight, prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(ItemGating), nameof(BlockMapFight))));
            }, "maps keep their achievement unlock");

            TryPatch(log, "endgame modes cannot be unlocked by their achievement", () =>
            {
                var target = AccessTools.Method(typeof(AchievementCollectionSO), "CompleteSpecificAchievement");
                if (target == null) throw new MissingMemberException("AchievementCollectionSO.CompleteSpecificAchievement");
                new Harmony(HarmonyId).Patch(target, prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(ItemGating), nameof(BlockRewardAchievement))));
            }, "modes keep the achievement route");

            // The property setter refuses to patch on this build (Harmony IL Compile
            // Error), so curse tiers fall back to the sweep in GameState, which
            // zeroes any progress above what the items granted.
            TryPatch(log, "curse tier progress is dropped as it happens", () =>
            {
                var target = AccessTools.PropertySetter(typeof(PlayerProfileAffixTierProgressionData), "Progression");
                if (target == null) throw new MissingMemberException("PlayerProfileAffixTierProgressionData.Progression");
                new Harmony(HarmonyId).Patch(target, prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(ItemGating), nameof(BlockTierProgress))));
            }, "curse tiers are clamped after the fact instead");
        }

        private static void TryPatch(BepInEx.Logging.ManualLogSource log, string okMessage, Action patch, string failureNote)
        {
            try
            {
                patch();
                log.LogInfo("ItemGating: " + okMessage);
            }
            catch (Exception error)
            {
                log.LogWarning($"ItemGating: could not patch for '{okMessage}' ({error.GetType().Name}: {error.Message}) - {failureNote}");
            }
        }

        /// <summary>
        /// Only the slot this mod owns is gated. Any other save (slot 0 in
        /// particular) keeps the game's normal unlock routes.
        /// </summary>
        private static bool OnAllowedProfile()
        {
            try
            {
                var profile = Global.PlayerProfile;
                if (profile == null) return false; // still loading or no slot picked
                return profile.Index == Plugin.AllowedProfile.Value;
            }
            catch (Exception error)
            {
                Plugin.Logger?.LogDebug("ItemGating: profile check failed: " + error.Message);
                return false;
            }
        }

        private static void Note(string what)
        {
            if (!Logged.Add(what)) return;
            Plugin.Logger?.LogInfo("ItemGating: blocked " + what + " (only an Archipelago item can unlock it)");
        }

        /// <summary>
        /// A character is only added when the item brought it; an achievement
        /// awarding the same character is dropped.
        /// </summary>
        private static bool BlockAchievementCharacter(PlayerProfileUnlockedCharacters __instance, CharactersType characterId)
        {
            if (Granting || __instance == null || !OnAllowedProfile()) return true;

            try
            {
                foreach (var existing in __instance.UnlockedCharacterIdsEnums)
                {
                    if (existing == characterId) return true; // already there, nothing to block
                }
            }
            catch (Exception error)
            {
                Plugin.Logger?.LogDebug("ItemGating: character check failed: " + error.Message);
                return true;
            }

            Note($"{characterId} from its unlock achievement");
            return false;
        }

        /// <summary>
        /// A rune node is a real skill tree node, so it can normally be bought
        /// with skill points. Only the item may create it; level-ups of a node
        /// that already exists still go through.
        /// </summary>
        private static bool BlockLocalRuneBuy(int level, SkillTreeItemSO skillTreeItem)
        {
            if (Granting || level <= 0 || skillTreeItem == null || !OnAllowedProfile()) return true;

            SkillTreeUnlockRuneSO rune;
            try
            {
                rune = skillTreeItem.TryCast<SkillTreeUnlockRuneSO>();
            }
            catch (Exception error)
            {
                Plugin.Logger?.LogDebug("ItemGating: rune cast failed: " + error.Message);
                return true;
            }

            if (rune == null) return true; // an ordinary stat node - buy away

            var runeId = rune.RuneId ?? skillTreeItem.Id;
            Note($"a local purchase of rune node '{runeId}'");
            return false;
        }

        /// <summary>
        /// Two achievements are hand-outs we do not want:
        ///  * "Unlock &lt;character&gt;" may only complete once the character is there,
        ///  * the RequiredAchievement of Unholy Cathedral / Titan Hunt may only
        ///    complete when the mode item is being applied.
        /// Every other achievement completes normally.
        /// </summary>
        private static bool BlockRewardAchievement(string achievementId)
        {
            if (Granting || string.IsNullOrEmpty(achievementId) || !OnAllowedProfile()) return true;

            try
            {
                if (ModeAchievements().Contains(achievementId))
                {
                    Note($"achievement '{achievementId}' unlocking an endgame mode");
                    return false;
                }

                var achievement = Global.AchievementCollection?.GetAchievementById(achievementId);
                if (achievement == null) return true;

                // `Unlock <character>` carries its target outright; checking the
                // typed subclass is safer than parsing RewardId.
                var unlock = achievement.TryCast<AchievementUnlockCharacterSO>();
                if (unlock == null) return true;

                var profile = Global.PlayerProfile;
                if (profile == null) return true;

                foreach (var unlocked in profile.UnlockedCharacters.UnlockedCharacterIdsEnums)
                {
                    if (unlocked == unlock.CharacterId) return true; // the item already arrived
                }

                Note($"achievement '{achievementId}' unlocking character '{unlock.CharacterId}'");
                return false;
            }
            catch (Exception error)
            {
                Plugin.Logger?.LogDebug("ItemGating: achievement check failed: " + error.Message);
                return true;
            }
        }

        /// <summary>
        /// Refuses the blacksmith's craft/upgrade press while the selected weapon is
        /// already at the cap. This is the important hook: the panel spends the
        /// materials first and only then raises the level, so blocking the level
        /// alone burned the cost for nothing.
        /// </summary>
        private static bool BlockBlacksmithPress(CraftingScreenPanel __instance)
        {
            if (Granting || __instance == null) return true;

            var cap = Plugin.GrantedWeaponCap?.Value ?? 1;
            if (cap < 0) return true;
            if (!OnAllowedProfile()) return true;

            string weaponId;
            try
            {
                weaponId = __instance.CurrentSelectedWeapon?.Definition?.WeaponId;
            }
            catch (Exception error)
            {
                Plugin.Logger?.LogDebug("ItemGating: blacksmith selection failed: " + error.Message);
                return true;
            }

            if (string.IsNullOrEmpty(weaponId)) return true;

            var profile = Global.PlayerProfile;
            if (profile == null || profile.WeaponsLevel == null) return true;

            // Crafting a weapon the save does not own *is* the self-unlock. In a
            // seed where weapons are items, that has to come from another player.
            if (GameState.WeaponsFromItems && !OwnsWeapon(profile, weaponId))
            {
                Note($"blacksmith crafting '{weaponId}' before its item arrived");
                return false;
            }

            int current;
            try
            {
                current = profile.WeaponsLevel.GetWeaponLevelByWeaponId(weaponId);
            }
            catch (Exception error)
            {
                Plugin.Logger?.LogDebug("ItemGating: weapon level lookup failed: " + error.Message);
                return true;
            }

            if (current <= 0) return true;   // crafted but not levelled yet
            if (current < cap) return true;  // the upgrade is inside the cap

            Note($"blacksmith press for '{weaponId}' at the cap (level {current} of {cap})");
            return false;
        }

        /// <summary>
        /// The buy button on the character screen. Only characters the save does not
        /// own are blocked, so picking an owned character still works.
        /// </summary>
        private static bool BlockCharacterPurchase(CharacterScreenPanel __instance)
        {
            if (Granting || __instance == null) return true;
            if (!OnAllowedProfile()) return true;

            var profile = Global.PlayerProfile;
            if (profile == null) return true;

            CharactersType wanted;
            try
            {
                wanted = __instance.CurrentlyInspectedCharacterId;
                foreach (var owned in profile.UnlockedCharacters.UnlockedCharacterIdsEnums)
                {
                    if (owned == wanted) return true;
                }
            }
            catch (Exception error)
            {
                Plugin.Logger?.LogDebug("ItemGating: character buy check failed: " + error.Message);
                return true;
            }

            Note($"buying character '{wanted}' before its item arrived");
            return false;
        }

        private static bool MapIsOpen(MapItem item)
        {
            if (!GameState.MapsFromItems || item == null) return true;

            string mapId;
            try
            {
                mapId = item.MapId;
            }
            catch (Exception error)
            {
                Plugin.Logger?.LogDebug("ItemGating: map id read failed: " + error.Message);
                return true;
            }

            if (string.IsNullOrEmpty(mapId)) return true;
            return GameState.GrantedMaps.Contains(mapId);
        }

        /// <summary>Refuses to start a run on a map whose item has not arrived.</summary>
        private static bool BlockMapFight(MapSelectionSurvivorsScreenPanel __instance)
        {
            if (Granting || !GameState.MapsFromItems || __instance == null) return true;

            MapItem selected;
            try
            {
                selected = __instance.LevelSelectionModule?.SelectedMapItem;
            }
            catch (Exception error)
            {
                Plugin.Logger?.LogDebug("ItemGating: selected map read failed: " + error.Message);
                return true;
            }

            if (selected == null || MapIsOpen(selected)) return true;

            Note($"starting a run on map '{selected.MapId}' before its item arrived");
            return false;
        }

        /// <summary>Overrides the map button's "achievement done" flag with our item.</summary>
        private static bool MapRequirementMet(MapItem __instance, ref bool __result)
        {
            if (Granting) return true;
            if (!GameState.MapsFromItems) return true;

            if (!_mapGetterLogged)
            {
                _mapGetterLogged = true;
                Plugin.Logger?.LogInfo($"ItemGating: map lock getter is consulted (first: '{__instance?.MapId}')");
            }

            try
            {
                __result = MapIsOpen(__instance);
            }
            catch (Exception error)
            {
                Plugin.Logger?.LogDebug("ItemGating: map flag failed: " + error.Message);
                return true;
            }

            return false;
        }

        private static bool BlockMapSelection(MapItem __instance)
        {
            if (Granting) return true;
            if (MapIsOpen(__instance)) return true;

            Note($"opening map '{__instance.MapId}' before its item arrived");
            return false;
        }

        /// <summary>The map list's real selection path: `SelectMap(MapItem)`.</summary>
        private static bool BlockMapPick(MapItem mapItem)
        {
            if (Granting) return true;
            if (MapIsOpen(mapItem)) return true;

            Note($"picking map '{mapItem.MapId}' before its item arrived");
            return false;
        }

        private static bool OwnsWeapon(PlayerProfile profile, string weaponId)
        {
            try
            {
                foreach (var id in profile.UnlockedWeapons.UnlockedWeaponIds)
                {
                    if (string.Equals(id, weaponId, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch (Exception error)
            {
                Plugin.Logger?.LogDebug("ItemGating: weapon ownership check failed: " + error.Message);
                return true;   // never hard-block on a failed lookup
            }

            return false;
        }

        /// <summary>
        /// The blacksmith has no level cap of its own, so the ceiling is whatever
        /// the highest Weapon Upgrade item set. Level 1 is the base a weapon starts
        /// at, and -1 means an Unlimited item already arrived.
        ///
        /// A weapon with no level entry yet is being crafted, not upgraded, so it
        /// is always allowed through.
        /// </summary>
        private static bool BlockWeaponUpgrade(PlayerProfileWeaponsLevel __instance, string weaponId)
        {
            if (Granting || __instance == null || string.IsNullOrEmpty(weaponId)) return true;

            var cap = Plugin.GrantedWeaponCap?.Value ?? 1;
            if (cap < 0) return true;
            if (!OnAllowedProfile()) return true;

            int current;
            try
            {
                current = __instance.GetWeaponLevelByWeaponId(weaponId);
            }
            catch (Exception error)
            {
                Plugin.Logger?.LogDebug("ItemGating: weapon level lookup failed: " + error.Message);
                return true;
            }

            if (current <= 0) return true;   // first craft, not an upgrade
            if (current < cap) return true;  // current + 1 stays inside the cap

            Note($"blacksmith upgrade of '{weaponId}' past level {cap} (currently {current})");
            return false;
        }

        /// <summary>
        /// A tier fills up as you play at its curse intensity. That gain is
        /// dropped unless an item is doing it.
        ///
        /// Only blocked while a run is live: the same setter is used when a save
        /// is read back in, and blocking that would throw away progress already
        /// on disk.
        /// </summary>
        private static bool BlockTierProgress(PlayerProfileAffixTierProgressionData __instance, float value)
        {
            if (Granting || __instance == null) return true;
            if (value <= __instance.Progression) return true;
            if (!OnAllowedProfile()) return true;
            if (!PlayerRefs.TryGet(out var _)) return true;

            Note($"curse tier {(int)__instance.Tier} progress ({__instance.Progression:0.00} -> {value:0.00})");
            return false;
        }

        /// <summary>The RequiredAchievement ids of the two endgame modes.</summary>
        private static List<string> ModeAchievements()
        {
            if (_modeAchievements != null) return _modeAchievements;

            var ids = new List<string>();
            try
            {
                foreach (var mode in Global.GameModeCollection.Modes)
                {
                    if (mode == null) continue;
                    var type = mode.GameModeType;
                    if (type != GameModes.Mastery && type != GameModes.Crusade) continue;
                    var requirement = mode.GameModeData?.RequiredAchievement;
                    if (requirement == null || string.IsNullOrEmpty(requirement.Id)) continue;
                    ids.Add(requirement.Id);
                }
            }
            catch (Exception error)
            {
                Plugin.Logger?.LogWarning("ItemGating: could not read the mode requirements: " + error.Message);
            }

            _modeAchievements = ids;
            return ids;
        }
    }
}
