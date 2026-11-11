using System;
using System.Collections.Generic;
using UnityEngine;

namespace SoulstoneAP
{
    /// <summary>One rune-unlocking node in the game's skill trees.</summary>
    internal sealed class RuneNode
    {
        public string RuneId;      // "RuneAddAllWeaponSkills"
        public string NodeId;      // the skill tree item id
        public int IdHash;         // what the save stores
        public string Character;   // owner, or empty for the shared tree
        public Sprite Icon;        // the game's rune artwork, null if unavailable
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
        private static Dictionary<int, RuneNode> _byHash;
        private static Dictionary<string, RuneNode> _byRuneId;
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
                    _all = found;
                    return _all;
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
                        Icon = SafeIcon(rune),
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
            _byHash = new Dictionary<int, RuneNode>(_all.Count);
            _byRuneId = new Dictionary<string, RuneNode>(_all.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var node in _all)
            {
                if (!_byHash.ContainsKey(node.IdHash)) _byHash.Add(node.IdHash, node);
                _byRuneId[node.RuneId] = node;
            }
            return _all;
        }

        private static Sprite SafeIcon(SkillTreeUnlockRuneSO rune)
        {
            try { return rune.GetSkillIcon(); }
            catch { return null; }
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

        /// <summary>The rune a skill tree node unlocks, matched by the save hash.</summary>
        public static RuneNode ForHash(int hash)
        {
            All();
            return _byHash != null && _byHash.TryGetValue(hash, out var node) ? node : null;
        }

        /// <summary>The rune with this id, indexed once when the tree is read.</summary>
        public static RuneNode ForRuneId(string runeId)
        {
            if (string.IsNullOrEmpty(runeId)) return null;
            All();
            return _byRuneId != null && _byRuneId.TryGetValue(runeId, out var node) ? node : null;
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
