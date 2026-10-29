using System;
using System.Collections.Generic;

namespace SoulstoneAP
{
    /// <summary>One endgame location the world generated for this seed.</summary>
    internal sealed class EndgameSpec
    {
        public int Id;
        public string Kind;        // "curse" | "ritual" | "titan"
        public string MapId;       // curse only
        public int Curse;          // minimum curse intensity, curse only
        public string Weapon;      // optional, curse only
        public string Character;   // optional, ritual / titan only
        public int Characters = 1; // "with N different characters"
        public int Tier;           // rituals 1..3, sigils 1..5
        public int Dynamic;        // the +N endless tier
    }

    /// <summary>
    /// Evaluates the seed's endgame checks against the live save.
    ///
    /// Specs arrive in slot data as `endgame: { "&lt;locationId&gt;": { ... } }`, so the
    /// world owns the difficulty tuning and the plugin only answers yes/no. That
    /// keeps the check list and the detection in lockstep across seeds.
    /// </summary>
    internal static class EndgameChecks
    {
        private static readonly List<EndgameSpec> Specs = new List<EndgameSpec>();

        public static int Count => Specs.Count;

        public static void Load(Dictionary<string, object> slotData)
        {
            Specs.Clear();
            if (slotData == null || !slotData.TryGetValue("endgame", out var raw)) return;
            LoadTable(JsonParser.AsObject(raw));
        }

        /// <summary>
        /// Loads the table straight from JSON. Used by the external Python client,
        /// which holds the slot data but never sees the server's Connected packet
        /// inside the game.
        /// </summary>
        public static string LoadJson(string json)
        {
            var table = JsonParser.AsObject(JsonParser.Parse(json));
            if (table == null) return "ERR endgame spec is not an object";
            Specs.Clear();
            LoadTable(table);
            return "OK " + Specs.Count + " endgame checks";
        }

        private static void LoadTable(Dictionary<string, object> table)
        {
            if (table == null) return;

            foreach (var entry in table)
            {
                if (!int.TryParse(entry.Key, out var id)) continue;
                var spec = JsonParser.AsObject(entry.Value);
                if (spec == null) continue;

                Specs.Add(new EndgameSpec
                {
                    Id = id,
                    Kind = JsonParser.GetString(spec, "kind", ""),
                    MapId = JsonParser.GetString(spec, "map", ""),
                    Curse = JsonParser.GetInt(spec, "curse"),
                    Weapon = JsonParser.GetString(spec, "weapon"),
                    Character = JsonParser.GetString(spec, "character"),
                    Characters = Math.Max(1, JsonParser.GetInt(spec, "chars", 1)),
                    Tier = JsonParser.GetInt(spec, "tier"),
                    Dynamic = JsonParser.GetInt(spec, "dynamic"),
                });
            }
        }

        /// <summary>Every check that is currently satisfied. Sending is de-duplicated by the caller.</summary>
        public static List<int> Evaluate(GameSnapshot snapshot)
        {
            var done = new List<int>();
            if (snapshot == null || Specs.Count == 0) return done;

            foreach (var spec in Specs)
            {
                if (Satisfied(spec, snapshot)) done.Add(spec.Id);
            }

            return done;
        }

        // ------------------------------------------------------------- debugging

        /// <summary>
        /// Runs injected for testing. They are handed to every snapshot, so the
        /// normal reporting path sends the checks to the server, but nothing is
        /// written to the save and they vanish when the game restarts.
        /// </summary>
        private static readonly List<MatchRecord> Injected = new List<MatchRecord>();

        public static void AppendInjected(GameSnapshot snapshot)
        {
            if (snapshot == null || Injected.Count == 0) return;
            snapshot.Matches.AddRange(Injected);
        }

        /// <summary>Pretends one Void Fields run was cleared and reports what that earns.</summary>
        public static string Inject(GameSnapshot snapshot, string mapId, int curse, string weapon, string character)
        {
            var json = new Json().StartObject();
            if (snapshot == null)
            {
                json.Name("ok").Value(false);
                json.Name("error").Value("no save loaded - pick the allowed profile in game first");
                return json.EndObject().ToString();
            }

            var record = new MatchRecord
            {
                MapId = mapId ?? "",
                CharacterId = character ?? "",
                WeaponId = weapon ?? "",
                CurseLevel = curse,
                Victory = true,
                GameMode = (int)GameModes.Survivor,
            };

            var before = new HashSet<int>(Evaluate(snapshot));
            Injected.Add(record);
            snapshot.Matches.Add(record);

            var matched = new List<int>();
            foreach (var id in Evaluate(snapshot))
            {
                if (!before.Contains(id)) matched.Add(id);
            }

            json.Name("ok").Value(true);
            json.Name("map").Value(mapId ?? "");
            json.Name("curse").Value(curse);
            json.Name("weapon").Value(weapon ?? "");
            json.Name("character").Value(character ?? "");
            json.Name("newChecks").Value(matched.Count);
            json.Name("ids").StartArray();
            foreach (var id in matched) json.Value(id);
            json.EndArray();
            json.Name("injected").Value(Injected.Count);

            return json.EndObject().ToString();
        }

        public static string ClearInjected()
        {
            var count = Injected.Count;
            Injected.Clear();
            return "OK cleared " + count + " injected run(s)";
        }

        /// <summary>What the save currently holds, plus which checks that satisfies.</summary>
        public static string Diagnose(GameSnapshot snapshot)
        {
            var json = new Json().StartObject();
            if (snapshot == null)
            {
                json.Name("ok").Value(false);
                json.Name("error").Value("no save loaded - pick the allowed profile in game first");
                return json.EndObject().ToString();
            }

            json.Name("ok").Value(true);
            json.Name("checks").Value(Specs.Count);
            json.Name("matches").Value(snapshot.Matches.Count);
            json.Name("rituals").Value(snapshot.Rituals.Count);
            json.Name("sigils").Value(snapshot.Sigils.Count);

            json.Name("curseByMap").StartObject();
            foreach (var pair in CurseByMap(snapshot))
                json.Name(pair.Key).Value(pair.Value);
            json.EndObject();

            json.Name("ritualBest").Value(Best(snapshot.Rituals));
            json.Name("sigilBest").Value(Best(snapshot.Sigils));

            var done = Evaluate(snapshot);
            json.Name("done").Value(done.Count);
            json.Name("doneIds").StartArray();
            foreach (var id in done) json.Value(id);
            json.EndArray();

            return json.EndObject().ToString();
        }

        /// <summary>
        /// Pretends one Void Fields victory happened and reports which checks that
        /// would satisfy. Nothing is written to the save; the fake record is dropped
        /// again before returning.
        /// </summary>
        public static string Simulate(GameSnapshot snapshot, string mapId, int curse, string weapon)
        {
            var json = new Json().StartObject();
            if (snapshot == null)
            {
                json.Name("ok").Value(false);
                json.Name("error").Value("no save loaded - pick the allowed profile in game first");
                return json.EndObject().ToString();
            }

            var before = new HashSet<int>(Evaluate(snapshot));
            snapshot.Matches.Add(new MatchRecord
            {
                MapId = mapId ?? "",
                CharacterId = "",
                WeaponId = weapon ?? "",
                CurseLevel = curse,
                Victory = true,
                GameMode = (int)GameModes.Survivor,
            });

            var matched = new List<int>();
            try
            {
                foreach (var id in Evaluate(snapshot))
                {
                    if (!before.Contains(id)) matched.Add(id);
                }
            }
            finally
            {
                snapshot.Matches.RemoveAt(snapshot.Matches.Count - 1);
            }

            json.Name("ok").Value(true);
            json.Name("map").Value(mapId ?? "");
            json.Name("curse").Value(curse);
            json.Name("weapon").Value(weapon ?? "");
            json.Name("newChecks").Value(matched.Count);
            json.Name("ids").StartArray();
            foreach (var id in matched) json.Value(id);
            json.EndArray();

            return json.EndObject().ToString();
        }

        /// <summary>The most recent match records, so it is obvious what the checks can see.</summary>
        public static string MatchDump(GameSnapshot snapshot, int count)
        {
            var json = new Json().StartObject();
            if (snapshot == null)
            {
                json.Name("ok").Value(false);
                json.Name("error").Value("no save loaded - pick the allowed profile in game first");
                return json.EndObject().ToString();
            }

            var matches = snapshot.Matches;
            var take = Math.Min(count <= 0 ? 12 : count, matches.Count);
            var cleared = 0;
            foreach (var match in matches)
            {
                if (Cleared(match) && match.GameMode == (int)GameModes.Survivor) cleared++;
            }

            json.Name("ok").Value(true);
            json.Name("total").Value(matches.Count);
            json.Name("clearedVoidFields").Value(cleared);
            json.Name("matches").StartArray();
            for (var i = matches.Count - take; i < matches.Count; i++)
            {
                var match = matches[i];
                json.StartObject();
                json.Name("map").Value(match.MapId ?? "");
                json.Name("character").Value(match.CharacterId ?? "");
                json.Name("weapon").Value(match.WeaponId ?? "");
                json.Name("curse").Value(match.CurseLevel);
                json.Name("victory").Value(match.Victory);
                json.Name("mode").Value(match.GameMode);
                json.Name("endless").Value(match.EndlessLevel);
                json.Name("cleared").Value(Cleared(match));
                json.EndObject();
            }
            json.EndArray();

            return json.EndObject().ToString();
        }

        private static Dictionary<string, int> CurseByMap(GameSnapshot snapshot)
        {
            var best = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var match in snapshot.Matches)
            {
                if (!Cleared(match)) continue;
                if (match.GameMode != (int)GameModes.Survivor) continue;
                if (string.IsNullOrEmpty(match.MapId)) continue;
                if (!best.TryGetValue(match.MapId, out var current) || match.CurseLevel > current)
                    best[match.MapId] = match.CurseLevel;
            }
            return best;
        }

        private static string Best(List<TierProgress> progress)
        {
            var found = false;
            var best = default(TierProgress);
            foreach (var entry in progress)
            {
                if (entry.Tier <= 0) continue;
                if (!found || entry.Tier > best.Tier ||
                    (entry.Tier == best.Tier && entry.DynamicLevel > best.DynamicLevel))
                {
                    best = entry;
                    found = true;
                }
            }

            return found
                ? best.Tier + "+" + best.DynamicLevel + " (" + (best.CharacterId ?? "") + ")"
                : "none";
        }

        private static bool Satisfied(EndgameSpec spec, GameSnapshot snapshot)
        {
            switch (spec.Kind)
            {
                case "curse":
                    return CurseDone(spec, snapshot);
                case "ritual":
                    return TierDone(spec, snapshot.Rituals);
                case "titan":
                    return TierDone(spec, snapshot.Sigils);
                default:
                    return false;
            }
        }

        // --------------------------------------------------------------- curses

        private static bool CurseDone(EndgameSpec spec, GameSnapshot snapshot)
        {
            var characters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var match in snapshot.Matches)
            {
                if (!Cleared(match)) continue;
                if (match.GameMode != (int)GameModes.Survivor) continue;
                if (!MapMatches(match.MapId, spec.MapId)) continue;
                if (match.CurseLevel < spec.Curse) continue;

                if (spec.Characters > 1)
                {
                    if (!string.IsNullOrEmpty(match.CharacterId)) characters.Add(match.CharacterId);
                    if (characters.Count >= spec.Characters) return true;
                    continue;
                }

                if (!string.IsNullOrEmpty(spec.Weapon) &&
                    !string.Equals(match.WeaponId, spec.Weapon, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// A run counts as cleared when it was won, or when the player took the
        /// Overlord / Endless portal and kept the same run going.
        ///
        /// Those portals only open after the base map is finished, so reaching a
        /// portal means the run was already completed at the curse level it
        /// started with. Dying three portals deep is not a loss for our purposes:
        /// the curse ladder rung was beaten before the portal was entered.
        /// </summary>
        private static bool Cleared(MatchRecord match)
        {
            if (match.Victory) return true;
            if (match.EndlessLevel >= 1) return true;
            if (match.EndlessCompletedPercent > 0.001f) return true;
            return false;
        }

        private static bool MapMatches(string actual, string wanted)
        {
            return string.Equals(actual, wanted, StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------- rituals / sigils

        private static bool TierDone(EndgameSpec spec, List<TierProgress> progress)
        {
            if (!string.IsNullOrEmpty(spec.Character))
            {
                foreach (var entry in progress)
                {
                    if (string.Equals(entry.CharacterId, spec.Character, StringComparison.OrdinalIgnoreCase))
                        return Reaches(entry, spec);
                }
                return false;
            }

            if (spec.Characters <= 1)
            {
                foreach (var entry in progress)
                {
                    if (Reaches(entry, spec)) return true;
                }
                return false;
            }

            var counted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in progress)
            {
                if (!Reaches(entry, spec)) continue;
                if (!string.IsNullOrEmpty(entry.CharacterId)) counted.Add(entry.CharacterId);
                if (counted.Count >= spec.Characters) return true;
            }
            return false;
        }

        private static bool Reaches(TierProgress entry, EndgameSpec spec)
        {
            if (entry.Tier <= 0) return false;
            if (entry.Tier > spec.Tier) return true;    // deeper ritual/sigil implies the earlier ones
            if (entry.Tier < spec.Tier) return false;
            return entry.DynamicLevel >= spec.Dynamic;
        }
    }
}
