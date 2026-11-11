using System;
using HarmonyLib;
using UnityEngine;

namespace SoulstoneAP
{
    /// <summary>
    /// Reverses the game's movement input while a DeathLink control burden is
    /// active. The hook only reads the effect state, so vanilla movement is
    /// untouched whenever no burden has been chosen.
    /// </summary>
    internal sealed class ControlEffects : MonoBehaviour
    {
        private const string HarmonyId = "soulstone.ap.controls";

        private static ControlEffects _instance;
        private static BepInEx.Logging.ManualLogSource Log => Plugin.Logger;

        private bool _inverted;
        private bool _seenPlayer;
        private float _runGoneAt;
        private float _nextRunCheck;

        public static bool Inverted => _instance != null && _instance._inverted;

        private void Awake()
        {
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        public static void Apply()
        {
            try
            {
                var target = AccessTools.Method(typeof(InputManager), "GetMovementValue");
                if (target == null)
                    throw new MissingMemberException("InputManager.GetMovementValue");

                new Harmony(HarmonyId).Patch(
                    target,
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(ControlEffects), nameof(InvertMovement))));
                Log?.LogInfo("ControlEffects: movement input hook installed");
            }
            catch (Exception error)
            {
                Log?.LogWarning("ControlEffects: could not hook movement input: " + error.Message);
            }
        }

        public static void EnableInvertedControls()
        {
            if (_instance == null)
            {
                Log?.LogWarning("ControlEffects: inverted controls have no live effect component");
                return;
            }

            _instance._inverted = true;
            _instance._seenPlayer = true;
        }

        /// <summary>Harmony postfix: negates the movement vector while the burden is active.</summary>
        private static void InvertMovement(ref Vector2 __result)
        {
            if (!Inverted) return;
            __result.x = -__result.x;
            __result.y = -__result.y;
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime < _nextRunCheck) return;
            _nextRunCheck = Time.unscaledTime + 0.3f;

            var alive = PlayerRefs.TryGet(out var player) &&
                        player != null &&
                        player._health != null &&
                        !player._health.HasBeenKilled &&
                        player._health.CurrentHealth > 0f;

            if (alive)
            {
                _seenPlayer = true;
                _runGoneAt = 0f;
                return;
            }

            if (!_seenPlayer) return;
            if (_runGoneAt <= 0f)
            {
                _runGoneAt = Time.unscaledTime;
                return;
            }

            if (Time.unscaledTime - _runGoneAt > 1f) Clear();
        }

        private void Clear()
        {
            _inverted = false;
            _seenPlayer = false;
            _runGoneAt = 0f;
        }
    }
}
