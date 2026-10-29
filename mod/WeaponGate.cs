using System;
using BepInEx.Configuration;
using HarmonyLib;

namespace SoulstoneAP
{
    /// <summary>
    /// The blacksmith still asks for the vanilla achievement behind a weapon
    /// ("reach prestige level 10 with Barbarian") before it will craft or level
    /// it. Weapons now arrive from other players, so that ladder is skipped:
    /// a weapon the save already holds is craftable and upgradeable right away.
    ///
    /// The check is only bypassed for weapons the profile owns, so nothing can
    /// be crafted here that the multiworld has not handed out.
    /// </summary>
    internal static class WeaponGate
    {
        private const string HarmonyId = "soulstone.ap.weapongate";
        private static bool _logged;
        private static bool _warnedBroken;

        public static void Apply(ConfigEntry<bool> enabled)
        {
            var log = Plugin.Logger;
            if (!enabled.Value)
            {
                log.LogInfo("WeaponGate: blacksmith keeps its vanilla level requirements");
                return;
            }

            try
            {
                var target = AccessTools.Method(typeof(CraftingScreenPanel), "CheckHasRequiredAchievement");
                if (target == null)
                {
                    log.LogWarning("WeaponGate: CraftingScreenPanel.CheckHasRequiredAchievement not found - the game build changed, blacksmith keeps its vanilla gates");
                    return;
                }

                new Harmony(HarmonyId).Patch(
                    target,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(WeaponGate), nameof(AllowOwnedWeapon))));
                log.LogInfo("WeaponGate: blacksmith level requirements bypassed for weapons the save owns");
            }
            catch (Exception error)
            {
                log.LogError("WeaponGate: could not patch the blacksmith: " + error);
            }
        }

        /// <summary>Returns false to skip the vanilla achievement check.</summary>
        private static bool AllowOwnedWeapon(string weaponId, ref bool __result)
        {
            if (!Owned(weaponId)) return true; // not granted by the multiworld - run the game's own check

            __result = true;
            if (!_logged)
            {
                _logged = true;
                Plugin.Logger.LogInfo($"WeaponGate: '{weaponId}' passes the blacksmith requirement check without its achievement");
            }
            return false;
        }

        private static bool Owned(string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId)) return false;

            try
            {
                var profile = Global.PlayerProfile;
                if (profile == null) return false;
                if (profile.Index != Plugin.AllowedProfile.Value)
                {
                    if (!_warnedBroken)
                    {
                        _warnedBroken = true;
                        Plugin.Logger.LogWarning($"WeaponGate: profile {profile.Index} is active but only {Plugin.AllowedProfile.Value} may be modified - gate left vanilla");
                    }
                    return false;
                }

                foreach (var id in profile.UnlockedWeapons.UnlockedWeaponIds)
                {
                    if (string.Equals(id, weaponId, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch (Exception error)
            {
                if (!_warnedBroken)
                {
                    _warnedBroken = true;
                    Plugin.Logger.LogWarning("WeaponGate: could not read unlocked weapons: " + error.Message);
                }
            }

            return false;
        }
    }
}
