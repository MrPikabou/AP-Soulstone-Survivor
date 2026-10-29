using System;
using System.Collections.Generic;
using BepInEx.Logging;
using ECM2.Characters;
using UnityEngine;

namespace SoulstoneAP
{
    /// <summary>Locates the live player during a run. Everything here is main-thread only.</summary>
    internal static class PlayerRefs
    {
        private static ManualLogSource Log => Plugin.Logger;

        public static bool TryGet(out Entity entity)
        {
            entity = null;
            try
            {
                var character = UnityEngine.Object.FindObjectOfType<PlayerCharacter>();
                if (character == null) return false;
                entity = character.Entity;
                return entity != null;
            }
            catch (Exception error)
            {
                Log?.LogWarning("AP: could not read the player entity: " + error.Message);
                return false;
            }
        }
    }

    /// <summary>
    /// Negative versions of the game's own level-up Powers. They go through the
    /// same Stats pipeline the cards use, so they stack exactly like real powers
    /// and last until the run ends.
    /// </summary>
    internal static class NegativePowers
    {
        internal sealed class Power
        {
            public string Name;
            public string Effect;
            public Action<Entity, StatsComponent> Apply;
        }

        private static readonly List<string> Applied = new List<string>();

        public static int Count => Applied.Count;

        private static void Add(StatsComponent component, Action<Stats> change)
        {
            var stats = new Stats();
            change(stats);
            component.AddStats(stats, 1f);
        }

        /// <summary>Max health has to be a flat loss, so it is computed at apply time.</summary>
        private static void LoseMaxHealth(StatsComponent component)
        {
            var current = component.GetMaxHealth();
            var loss = Math.Max(5f, current * 0.15f);
            var stats = new Stats();
            stats.MaxHealth = -loss;
            component.AddStats(stats, 1f);
        }

        public static readonly List<Power> All = new List<Power>
        {
            new Power
            {
                Name = "Feeble Strikes", Effect = "-10% damage",
                Apply = (_, c) => Add(c, s => s.DamageModifier = -0.10f),
            },
            new Power
            {
                Name = "Sluggish Colossus", Effect = "-10% damage, -15% move speed",
                Apply = (_, c) => Add(c, s => { s.DamageModifier = -0.10f; s.MovementSpeedModifier = -0.15f; }),
            },
            new Power
            {
                Name = "Half-Hearted", Effect = "-10% attack speed",
                Apply = (_, c) => Add(c, s => s.AttackSpeed = -0.10f),
            },
            new Power
            {
                Name = "Cramped", Effect = "-15% area",
                Apply = (_, c) => Add(c, s => s.AreaModifier = -0.15f),
            },
            new Power
            {
                Name = "Lethargic", Effect = "-5% crit chance",
                Apply = (_, c) => Add(c, s => s.CritChance = -0.05f),
            },
            new Power
            {
                Name = "Merciful", Effect = "-20% crit damage",
                Apply = (_, c) => Add(c, s => s.CritPotency = -0.20f),
            },
            new Power
            {
                Name = "Frail", Effect = "-15% max health",
                Apply = (_, c) => LoseMaxHealth(c),
            },
            new Power
            {
                Name = "Brittle", Effect = "-8 armor",
                Apply = (_, c) => Add(c, s => s.ArmorModifier = -8f),
            },
            new Power
            {
                Name = "Defenceless", Effect = "-5% block chance",
                Apply = (_, c) => Add(c, s => s.BlockChance = -0.05f),
            },
            new Power
            {
                Name = "Leaden", Effect = "-10% move speed",
                Apply = (_, c) => Add(c, s => s.MovementSpeedModifier = -0.10f),
            },
            new Power
            {
                Name = "Repellent", Effect = "-20% experience, -50% pickup range",
                Apply = (_, c) => Add(c, s => { s.ExperienceModifier = -0.20f; s.CollectRange = -0.50f; }),
            },
            new Power
            {
                Name = "Breakable", Effect = "+2 damage taken",
                Apply = (_, c) => Add(c, s => s.DamageReduction = 2f),
            },
            new Power
            {
                Name = "Battered", Effect = "-20% healing",
                Apply = (_, c) => Add(c, s => s.HealingModifier = -0.20f),
            },
            new Power
            {
                Name = "Exhausted", Effect = "-1 dash",
                Apply = (e, c) => c.AddDashCount(-1),
            },
        };

        public static string ApplyRandom(Entity entity)
        {
            var component = entity._stats;
            if (component == null) return "ERR no stats component";

            var power = All[UnityEngine.Random.Range(0, All.Count)];
            try
            {
                power.Apply(entity, component);
                Applied.Add(power.Name);
                return "OK " + power.Name + " (" + power.Effect + ")";
            }
            catch (Exception error)
            {
                return "ERR " + power.Name + ": " + error.Message;
            }
        }
    }

    /// <summary>Shoves the player using the game's own character controller.</summary>
    internal static class Knockback
    {
        public static string Push(Entity entity, float force)
        {
            var character = entity._character;
            if (character == null) return "ERR no character";

            if (!character.IsOnGround()) return "SKIP airborne";
            if (character.IsDashing()) return "SKIP dashing";

            var direction = UnityEngine.Random.insideUnitSphere;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) direction = Vector3.forward;
            direction.Normalize();

            var velocity = character.GetVelocity();
            character.SetVelocity(new Vector3(direction.x * force, velocity.y, direction.z * force));
            character.PauseGroundConstraint(0.25f);
            return "OK pushed";
        }
    }

    /// <summary>
    /// Trap effects. Spawning traps reuse the run's live affix data, which is what
    /// the game's own curse system drives its elite/boss/hunter spawners from.
    /// </summary>
    internal static class Traps
    {
        private static ManualLogSource Log => Plugin.Logger;

        public static readonly string[] Names =
        {
            "Cursed Power",
            "Knockback",
            "Elite Ambush",
            "Boss Summons",
            "Swarm",
        };

        public static string Apply(string name, Entity player)
        {
            switch (name)
            {
                case "Cursed Power":
                    CursedChoice.Show(player);
                    return "OK awaiting the player's choice";
                case "Knockback":
                    return Knockback.Push(player, 22f);
                case "Elite Ambush":
                    return BumpElites(2);
                case "Swarm":
                    return BumpElites(5);
                case "Boss Summons":
                    return BumpBosses(1);
                default:
                    return "ERR unknown trap '" + name + "'";
            }
        }

        /// <summary>
        /// Raises the run's own spawn counters. This is the only spawn path that is
        /// safe from the mod: instantiating enemy prefabs outside the game's pool and
        /// calling the boss processor directly both crashed the game in testing.
        /// The game reads these counters when the next elite/boss spawn happens, so
        /// the effect shows up at the next spawn event rather than instantly.
        /// </summary>
        private static string BumpElites(int count)
        {
            var data = LiveAffixData();
            if (data == null) return "ERR no live spawn state (not in a run?)";

            try
            {
                data.AdditionalEliteQuantity += count;
                return $"OK next elite spawn brings {count} extra (delayed by design)";
            }
            catch (Exception error)
            {
                return "ERR elite bump: " + error.Message;
            }
        }

        private static string BumpBosses(int count)
        {
            var data = LiveAffixData();
            if (data == null) return "ERR no live spawn state (not in a run?)";

            try
            {
                data.AdditionalBossQuantity += count;
                return $"OK next boss event brings {count} extra boss (delayed by design)";
            }
            catch (Exception error)
            {
                return "ERR boss bump: " + error.Message;
            }
        }

        /// <summary>The run keeps its curse/spawn state on the game manager's components.</summary>
        private static AffixGameplayData LiveAffixData()
        {
            try
            {
                var timed = UnityEngine.Object.FindObjectsOfType<GameManagerEliteEnemyTimedSpawnComponent>();
                for (var i = 0; i < timed.Length; i++)
                    if (timed[i] != null && timed[i].AffixGameplayData != null)
                        return timed[i].AffixGameplayData;

                var special = UnityEngine.Object.FindObjectsOfType<GameManagerAffixSpecialEntitiesComponent>();
                for (var i = 0; i < special.Length; i++)
                    if (special[i] != null && special[i]._affixGameplayData != null)
                        return special[i]._affixGameplayData;
            }
            catch (Exception error)
            {
                Log?.LogWarning("AP: could not read live spawn state: " + error.Message);
            }

            return null;
        }
    }
}
