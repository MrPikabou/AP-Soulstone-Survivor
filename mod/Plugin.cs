using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace SoulstoneAP
{
    [BepInPlugin(Guid, "Soulstone Survivors Archipelago", Version)]
    public sealed class Plugin : BasePlugin
    {
        public const string Guid = "soulstone.ap.bridge";
        // Shown in the panel status line, so a game left running on an older
        // DLL is obvious instead of looking like a broken command.
        public const string Version = "1.0.1";

        public static Plugin Instance { get; private set; }

        internal static ManualLogSource Logger;

        internal static ConfigEntry<int> Port;
        internal static ConfigEntry<int> AllowedProfile;
        internal static ConfigEntry<bool> EnableGrants;
        internal static ConfigEntry<bool> BypassWeaponRequirements;
        internal static ConfigEntry<bool> EnforceItemGating;
        internal static ConfigEntry<int> GrantedCurseTier;
        internal static ConfigEntry<int> GrantedWeaponCap;
        internal static ConfigEntry<bool> ItemOnlyMaps;
        internal static ConfigEntry<bool> ItemOnlyWeapons;

        internal static ConfigEntry<string> Server;
        internal static ConfigEntry<string> Slot;
        internal static ConfigEntry<string> Password;
        internal static ConfigEntry<bool> AutoConnect;
        internal static ConfigEntry<bool> ShowPanelOnSlotScreen;
        internal static ConfigEntry<bool> OpenPanelAfterSaveLoad;

        /// <summary>How many of the seed's items have already been applied. Set to 0 to re-apply everything.</summary>
        internal static ConfigEntry<int> AppliedItems;

        public override void Load()
        {
            Instance = this;
            Logger = Log;

            Port = Config.Bind("Bridge", "Port", 47821,
                "Loopback TCP port the external Archipelago client connects to (optional; the in-game panel does not need it).");
            AllowedProfile = Config.Bind("Safety", "AllowedProfileIndex", 1,
                "Only this save slot may be read from or written to. Slot 0 is never touched.");
            EnableGrants = Config.Bind("Safety", "EnableGrants", true,
                "Allow Archipelago items to be applied to the allowed profile.");
            EnforceItemGating = Config.Bind("Gating", "ItemOnlyUnlocks", true,
                "Characters, rune nodes and the two endgame modes can only be unlocked by an Archipelago item; the in-game achievement routes are blocked.");
            GrantedCurseTier = Config.Bind("Gating", "GrantedCurseTier", 0,
                "Internal: highest curse tier an item has granted. Any tier above this is cleared from the save once no run is live. Set to 0 to stop the guard (tiers then unlock the vanilla way).");
            ItemOnlyMaps = Config.Bind("Gating", "ItemOnlyMaps", true,
                "A map can only be played once its Map: item has arrived. On by default and already active before connecting; a seed that ships no map items turns it off by itself.");
            ItemOnlyWeapons = Config.Bind("Gating", "ItemOnlyWeapons", true,
                "A weapon can only be crafted at the blacksmith once its Weapon: item has arrived. On by default; a seed that ships no weapon items turns it off by itself.");
            GrantedWeaponCap = Config.Bind("Gating", "GrantedWeaponCap", 1,
                "Internal: highest weapon level an item has granted (1 = no upgrades, -1 = unlimited). The blacksmith refuses levels above it.");
            BypassWeaponRequirements = Config.Bind("Blacksmith", "IgnoreWeaponLevelRequirements", true,
                "A weapon the save already owns can be crafted and levelled at the blacksmith without the character-level achievement that normally gates it.");

            Server = Config.Bind("Archipelago", "Server", "localhost:38281",
                "host:port of the Archipelago server, as typed in the in-game panel.");
            Slot = Config.Bind("Archipelago", "SlotName", "Survivor",
                "Slot name to join with.");
            Password = Config.Bind("Archipelago", "Password", "",
                "Password for the slot, if the room has one.");
            AutoConnect = Config.Bind("Archipelago", "AutoConnect", false,
                "Join the saved server/slot automatically on launch instead of using the panel.");
            ShowPanelOnSlotScreen = Config.Bind("Panel", "ShowOnSlotScreen", false,
                "Pop the panel up on the save-slot screen at boot. Off means it appears only after you pick your save (or via F8).");
            OpenPanelAfterSaveLoad = Config.Bind("Panel", "OpenAfterSaveLoad", true,
                "Open the panel once by itself when the save you picked has loaded. F8 always toggles it.");
            AppliedItems = Config.Bind("Archipelago", "AppliedItems", 0,
                "Internal: number of received items already applied. Reset to 0 to re-apply the whole seed.");

            GameState.Log = Log;
            // Both gates are active from the first frame, not once a slot is joined:
            // playing offline must not be a way around them.
            GameState.MapsFromItems = ItemOnlyMaps.Value;
            GameState.WeaponsFromItems = ItemOnlyWeapons.Value;
            Bridge.Handler = CommandRouter.Handle;
            try
            {
                Bridge.Start(Port.Value);
                Log.LogInfo($"Archipelago bridge listening on 127.0.0.1:{Port.Value}");
            }
            catch (Exception e)
            {
                Log.LogError($"Could not start bridge on port {Port.Value}: {e.Message}");
            }

            WeaponGate.Apply(BypassWeaponRequirements);
            ItemGating.Apply(EnforceItemGating);

            AddComponent<BridgePump>();
            AddComponent<ApRuntime>();
            AddComponent<ApPanel>();
            AddComponent<CursedChoice>();

            Log.LogInfo("In-game Archipelago panel ready (F8 to open).");
        }

        public override bool Unload()
        {
            Bridge.Stop();
            return true;
        }
    }

    /// <summary>Drives the loopback bridge from the Unity main thread.</summary>
    public sealed class BridgePump : MonoBehaviour
    {
        public BridgePump(IntPtr ptr) : base(ptr) { }

        private void Update()
        {
            Bridge.Pump();
            GameState.TickTierGuard();
        }
    }

    internal static class CommandRouter
    {
        public static string Handle(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return "ERR empty request";

            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts[0].ToUpperInvariant();
            var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 1; i < parts.Length; i++)
            {
                var eq = parts[i].IndexOf('=');
                if (eq <= 0) continue;
                var key = parts[i].Substring(0, eq);
                var value = Uri.UnescapeDataString(parts[i].Substring(eq + 1));
                args[key] = value;
            }

            switch (cmd)
            {
                case "PING":
                    return "OK pong";
                case "STATE":
                    return "OK " + Uri.EscapeDataString(GameState.SnapshotJson());
                case "CATALOG":
                    return "OK " + Uri.EscapeDataString(GameState.CatalogJson());
                case "GRANT":
                    if (!args.TryGetValue("item", out var item)) return "ERR missing item";
                    return GameState.Grant(item);
                // ENDGAME spec=<percentEncodedJson>: the external client forwards the
                // seed's endgame table, since only the in-game connection sees slot data.
                case "ENDGAME":
                    if (!args.TryGetValue("spec", out var spec)) return "ERR missing spec";
                    return EndgameChecks.LoadJson(spec);
                // CHECKS: what the save holds for the endgame checks right now.
                case "CHECKS":
                    return "OK " + Uri.EscapeDataString(EndgameChecks.Diagnose(GameState.Snapshot()));
                // INJECT map=… curse=… [weapon=…] [character=…]: pretend a run was
                // cleared. Held in memory only; INJECT clear=1 removes them.
                case "INJECT":
                {
                    if (args.TryGetValue("clear", out var clear) && clear != "0")
                        return EndgameChecks.ClearInjected();
                    if (!args.TryGetValue("map", out var injectMap)) return "ERR missing map";
                    if (!args.TryGetValue("curse", out var injectCurseText) ||
                        !int.TryParse(injectCurseText, out var injectCurse))
                        return "ERR missing or bad curse";
                    args.TryGetValue("weapon", out var injectWeapon);
                    args.TryGetValue("character", out var injectCharacter);
                    return "OK " + Uri.EscapeDataString(EndgameChecks.Inject(
                        GameState.Snapshot(), injectMap, injectCurse, injectWeapon, injectCharacter));
                }
                // MATERIALS [item=CommonSilver] [n=100]: add crafting materials so
                // blacksmith upgrades can be tested. No item = every mineral.
                case "MATERIALS":
                {
                    args.TryGetValue("item", out var material);
                    var amount = 100;
                    if (args.TryGetValue("n", out var amountText) &&
                        int.TryParse(amountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                        amount = parsed;
                    return GameState.AddMaterials(material, amount);
                }
                // PRESTIGE: what the save holds per character, plus the level table.
                case "PRESTIGE":
                    return GameState.PrestigeDump();
                // MAPRESET map=all|SurvivorsDesert: drop map progression entries, so
                // the maps have to arrive as items again. Takes completeness, affix
                // selections and curse tiers for those maps with it.
                case "MAPRESET":
                {
                    args.TryGetValue("map", out var resetWhich);
                    return GameState.ResetMaps(resetWhich);
                }
                // MAPFULL map=all|SurvivorsDesert: mark a map 100% cleared, which is
                // what makes its affix (curse) panel usable. Fires its Clear check too.
                case "MAPFULL":
                {
                    args.TryGetValue("map", out var which);
                    return GameState.CompleteMaps(which);
                }
                // MATCHES [count=N]: the most recent match records the save holds.
                case "MATCHES":
                {
                    var count = 12;
                    if (args.TryGetValue("count", out var countText)) int.TryParse(countText, out count);
                    return "OK " + Uri.EscapeDataString(EndgameChecks.MatchDump(GameState.Snapshot(), count));
                }
                // SIM map=SurvivorsDesert curse=10 [weapon=WeaponPyromancer-02]:
                // pretends one victory happened. Nothing is written to the save.
                case "SIM":
                {
                    if (!args.TryGetValue("map", out var map)) return "ERR missing map";
                    if (!args.TryGetValue("curse", out var curseText) ||
                        !int.TryParse(curseText, out var curse))
                        return "ERR missing or bad curse";
                    args.TryGetValue("weapon", out var weapon);
                    return "OK " + Uri.EscapeDataString(
                        EndgameChecks.Simulate(GameState.Snapshot(), map, curse, weapon));
                }
                // Local effect trigger for testing: EFFECT name=Cursed Power
                case "EFFECT":
                    if (!args.TryGetValue("name", out var effect)) return "ERR missing name";
                    if (!PlayerRefs.TryGet(out var player)) return "ERR no run active - start a run first";
                    return Traps.Apply(effect, player);
                default:
                    return "ERR unknown command " + cmd;
            }
        }
    }
}
