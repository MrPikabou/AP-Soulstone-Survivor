using System;
using System.Collections.Generic;
using System.Text;

namespace SoulstoneAP
{
    /// <summary>One check as the world describes it.</summary>
    internal sealed class CheckInfo
    {
        public int Id;
        public string Name;
        public string Map;
        public string Character;
        public readonly List<string> Needs = new List<string>();
    }

    /// <summary>
    /// The seed's location table, loaded from slot data. Everything the browser
    /// shows is derived from it, so the plugin never duplicates world rules.
    /// </summary>
    internal static class LocationBook
    {
        private static readonly List<CheckInfo> All = new List<CheckInfo>();

        public static int Count => All.Count;

        /// <summary>Seed-provided label for one location id, or null.</summary>
        public static string NameForId(int id)
        {
            foreach (var info in All)
                if (info.Id == id) return info.Name;
            return null;
        }

        public static void Clear() => All.Clear();

        public static void Load(Dictionary<string, object> slotData)
        {
            Clear();
            if (!slotData.TryGetValue("location_info", out var raw))
            {
                Plugin.Logger?.LogWarning("AP: this seed has no location_info; badges open an empty list, regenerate with the current apworld");
                return;
            }
            var table = JsonParser.AsObject(raw);
            if (table == null)
            {
                var kind = raw == null ? "null" : raw.GetType().Name;
                Plugin.Logger?.LogWarning("AP: location_info could not be read (" + kind + ")");
                return;
            }

            foreach (var entry in table)
            {
                if (!int.TryParse(entry.Key, out var id)) continue;
                var spec = JsonParser.AsObject(entry.Value);
                if (spec == null) continue;

                var info = new CheckInfo
                {
                    Id = id,
                    Name = ApRuntime.Spaced(JsonParser.GetString(spec, "name", "")),
                    Map = JsonParser.GetString(spec, "map", ""),
                    Character = JsonParser.GetString(spec, "character", ""),
                };

                if (spec.TryGetValue("needs", out var needsRaw) && JsonParser.AsArray(needsRaw) is { } needs)
                    foreach (var need in needs)
                    {
                        var text = JsonParser.AsString(need);
                        if (!string.IsNullOrEmpty(text)) info.Needs.Add(text);
                    }

                All.Add(info);
            }

            Plugin.Logger?.LogInfo($"AP: location book loaded ({All.Count} checks)");
        }

        /// <summary>done/total for a map or a character, or for global checks when requested.</summary>
        public static string SummaryFor(string map, string character, bool global = false)
        {
            var runtime = ApRuntime.Instance;
            var done = 0;
            var total = 0;
            foreach (var info in All)
            {
                if (!Matches(info, map, character, global)) continue;
                total++;
                if (IsDone(runtime, info)) done++;
            }
            return total == 0 ? "" : done + "/" + total;
        }

        private static bool Matches(CheckInfo info, string map, string character, bool global)
        {
            // Global checks belong to neither a map nor a character: portal
            // cycles, lifetime counters and skill-tree counts.
            if (global)
                return string.IsNullOrEmpty(info.Map) && string.IsNullOrEmpty(info.Character);
            if (!string.IsNullOrEmpty(map) &&
                !string.Equals(info.Map, map, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrEmpty(character) &&
                !string.Equals(info.Character, character, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        /// <summary>The book as one scrollable string, optionally filtered.</summary>
        public static string Render(string map = null, string character = null, bool global = false)
        {
            var runtime = ApRuntime.Instance;
            if (All.Count == 0) return "no location data from this seed";

            var order = new List<string>();
            var buckets = new Dictionary<string, List<CheckInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in All)
            {
                if (!Matches(info, map, character, global)) continue;
                var key = global ? "Global" : (string.IsNullOrEmpty(character)
                    ? string.IsNullOrEmpty(info.Map) ? "Other" : ApRuntime.Spaced(info.Map)
                    : "Character: " + character);
                if (!buckets.TryGetValue(key, out var list))
                {
                    list = new List<CheckInfo>();
                    buckets[key] = list;
                    order.Add(key);
                }
                list.Add(info);
            }

            var text = new StringBuilder();
            if (order.Count == 0) return "no checks match this filter";
            foreach (var group in order)
            {
                var list = buckets[group];
                var done = 0;
                foreach (var info in list) if (IsDone(runtime, info)) done++;
                text.Append(group).Append("  ").Append(done).Append('/').Append(list.Count).Append('\n');

                Append(text, runtime, list, 0, "doable now");
                Append(text, runtime, list, 1, "blocked");
                Append(text, runtime, list, 2, "completed");
                text.Append('\n');
            }
            return text.ToString().TrimEnd('\n');
        }

        private static void Append(StringBuilder text, ApRuntime runtime, List<CheckInfo> list, int kind, string title)
        {
            var body = new StringBuilder();
            var count = 0;
            foreach (var info in list)
            {
                if (GroupOf(runtime, info) != kind) continue;
                count++;
                body.Append("  ").Append(DisplayName(info)).AppendLine();

                var details = Details(runtime, info, kind);
                if (details.Length > 0)
                    body.Append("      <size=70%><color=#9AA3B2>").Append(details).Append("</color></size>").AppendLine();
            }
            if (count == 0) return;
            text.Append(title).Append(" (").Append(count).Append(')').AppendLine().Append(body);
        }

        /// <summary>Short title: `Rune Extra Damage`, plain achievement names, map clears kept.</summary>
        private static string DisplayName(CheckInfo info)
        {
            var name = info.Name ?? "";
            if (name.StartsWith("Rune: ", StringComparison.Ordinal))
            {
                var value = name.Substring(6);
                if (value.StartsWith("Rune ", StringComparison.Ordinal)) value = value.Substring(5);
                else if (value.StartsWith("Rune", StringComparison.Ordinal)) value = value.Substring(4);
                return "Rune " + value;
            }
            if (name.StartsWith("Achievement: ", StringComparison.Ordinal)) return name.Substring(13);
            return name;
        }

        /// <summary>The small line under a check: who can do it and what it still needs.</summary>
        private static string Details(ApRuntime runtime, CheckInfo info, int kind)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(info.Character)) parts.Add(info.Character);
            if (kind == 1)
            {
                var missing = new List<string>();
                foreach (var need in info.Needs)
                    if (!NeedMet(runtime, need)) missing.Add(need);
                if (missing.Count > 0) parts.Add("needs " + string.Join(", ", missing));
            }
            return string.Join("  ", parts);
        }

        private static int GroupOf(ApRuntime runtime, CheckInfo info)
        {
            if (IsDone(runtime, info)) return 2;
            foreach (var need in info.Needs)
                if (!NeedMet(runtime, need)) return 1;
            return 0;
        }

        private static bool IsDone(ApRuntime runtime, CheckInfo info) => runtime != null && runtime.IsChecked(info.Id);

        private static bool NeedMet(ApRuntime runtime, string need)
        {
            if (string.IsNullOrEmpty(need)) return true;
            if (runtime != null && runtime.HasReceivedItem(need)) return true;

            const string character = "Character: ";
            const string map = "Map: ";
            const string craft = "Craft: ";
            if (need.StartsWith(character, StringComparison.Ordinal))
                return GameState.ProfileHasCharacter(need.Substring(character.Length));
            if (need.StartsWith(map, StringComparison.Ordinal))
                return GameState.ProfileHasMap(need.Substring(map.Length));
            if (need.StartsWith(craft, StringComparison.Ordinal))
                return GameState.LicensedCrafters.Contains(need.Substring(craft.Length));
            return false;
        }
    }
}
