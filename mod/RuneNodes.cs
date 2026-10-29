using System;
using System.Collections.Generic;

namespace SoulstoneAP
{
    /// <summary>One rune-unlocking node in the game's skill trees.</summary>
    internal sealed class RuneNode
    {
        public string RuneId;      // "RuneAddAllWeaponSkills"
        public string NodeId;      // the skill tree item id
        public int IdHash;         // what the save stores
        public string Character;   // owner, or empty for the shared tree
    }

    /// <summary>
    /// Runes are not a standalone save list: they are unlocked by skill tree
    /// nodes of type <see cref="SkillTreeUnlockRuneSO"/>, which carry the rune id.
    /// The bundle keeps those nodes in their own list, so they can be mapped to
    /// runes without guessing from names.
    /// </summary>
    internal static class RuneNodes
    {
        private static List<RuneNode> _all;
        private static bool _failed;

        public static List<RuneNode> All()
        {
            if (_all != null) return _all;
            if (_failed) return new List<RuneNode>();

            var found = new List<RuneNode>();
            try
            {
                var bundle = Global.SkillTreeCollectionBundle;
                if (bundle == null)
                {
                    _failed = true;
                    return found;
                }

                foreach (var node in bundle._skillTreeUnlockRuneNodes)
                {
                    if (node == null) continue;
                    var rune = node.TryCast<SkillTreeUnlockRuneSO>();
                    if (rune == null) continue;

                    var runeId = rune.RuneId;
                    if (string.IsNullOrEmpty(runeId)) continue;
                    if (found.Exists(existing => string.Equals(existing.RuneId, runeId, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    found.Add(new RuneNode
                    {
                        RuneId = runeId,
                        NodeId = node.Id ?? "",
                        IdHash = node.IdHash,
                        Character = node.CharactersType.ToString(),
                    });
                }

                Plugin.Logger?.LogInfo($"RuneNodes: {found.Count} rune-unlock nodes found");
            }
            catch (Exception error)
            {
                _failed = true;
                Plugin.Logger?.LogWarning("RuneNodes: could not enumerate the skill tree: " + error.Message);
            }

            _all = found;
            return _all;
        }

        /// <summary>The node that unlocks a rune, resolved through the game's own lookup.</summary>
        public static SkillTreeItemSO FindNode(string runeId)
        {
            if (string.IsNullOrEmpty(runeId)) return null;
            try
            {
                var bundle = Global.SkillTreeCollectionBundle;
                if (bundle == null) return null;
                if (!bundle.TryGetSkillTreeItemByRuneId(runeId, out var item, out var _)) return null;
                return item;
            }
            catch (Exception error)
            {
                Plugin.Logger?.LogWarning("RuneNodes: lookup failed for '" + runeId + "': " + error.Message);
                return null;
            }
        }

        /// <summary>Rune ids the profile has already unlocked, read off the skill tree.</summary>
        public static List<string> Owned(PlayerProfile profile)
        {
            var owned = new List<string>();
            if (profile == null) return owned;

            foreach (var node in All())
            {
                try
                {
                    if (!profile.SkillTree.TryGetSkillTreeItem(node.IdHash, out var level, out var _)) continue;
                    if (level > 0) owned.Add(node.RuneId);
                }
                catch (Exception error)
                {
                    Plugin.Logger?.LogDebug("RuneNodes: could not read node " + node.NodeId + ": " + error.Message);
                }
            }

            return owned;
        }
    }
}
