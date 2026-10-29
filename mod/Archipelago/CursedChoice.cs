using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SoulstoneAP
{
    /// <summary>
    /// Modal "choose your burden" prompt: three cursed powers, pick exactly one.
    /// Built as uGUI + TMP like the main panel, and driven by unscaled time so it
    /// stays clickable while the game is paused on the level-up screen.
    /// </summary>
    public sealed class CursedChoice : MonoBehaviour
    {
        private static CursedChoice _instance;

        private GameObject _canvas;
        private GameObject _root;
        private Entity _player;
        private int _pending;
        private float _previousTimeScale = 1f;
        private bool _pausedByUs;
        private bool _alreadyPaused;
        private PausePanelManager _pausePanel;
        private int _queued;

        private TextMeshProUGUI _counter;

        private static BepInEx.Logging.ManualLogSource Log => Plugin.Logger;

        public static bool IsShowing => _instance != null && _instance._root != null;

        private void Awake()
        {
            // Created once by Plugin.Load: AddComponent on a fresh GameObject
            // throws TypeInitializationException for managed components here.
            _instance = this;
        }

        public static void Show(Entity player)
        {
            if (_instance == null)
            {
                Log?.LogError("AP: cursed choice component is missing - is the plugin loaded?");
                return;
            }

            if (_instance._root != null)
            {
                _instance._player = player;
                _instance._pending++;
                _instance._queued = _instance._pending;
                _instance.RefreshCounter();
                return;
            }

            _instance._player = player;
            _instance._pending = 1;
            _instance._queued = 1;
            _instance.Build();
        }

        private void Build()
        {
            try
            {
                _canvas = new GameObject("SoulstoneAP_CursedChoiceCanvas");
                UnityEngine.Object.DontDestroyOnLoad(_canvas);

                var canvas = _canvas.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 30000;

                var scaler = _canvas.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;

                _canvas.AddComponent<GraphicRaycaster>();

                _root = new GameObject("CursedChoiceRoot");
                var rootRect = _root.AddComponent<RectTransform>();
                rootRect.SetParent(_canvas.transform, false);
                rootRect.anchorMin = Vector2.zero;
                rootRect.anchorMax = Vector2.one;
                rootRect.offsetMin = Vector2.zero;
                rootRect.offsetMax = Vector2.zero;

                var backdrop = MakeRect("Backdrop", rootRect, new Vector2(4000f, 2400f), Vector2.zero);
                var backdropImage = backdrop.gameObject.AddComponent<Image>();
                // Nearly opaque: the game's own pause panel sits behind this and
                // would otherwise show through.
                backdropImage.color = new Color(0.03f, 0.02f, 0.05f, 0.96f);
                backdropImage.raycastTarget = true;

                // Same feel as the game's own level-up screen: the world stops while you pick.
                Pause();

                var panel = MakeRect("Panel", rootRect, new Vector2(980f, 620f), new Vector2(0f, 0f));
                panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
                panel.pivot = new Vector2(0.5f, 0.5f);
                var panelImage = panel.gameObject.AddComponent<Image>();
                panelImage.color = new Color(0.09f, 0.07f, 0.12f, 0.98f);

                MakeText(panel, "Title", "A death reaches you", 30f, TextAlignmentOptions.Center,
                    new Color(1f, 0.85f, 0.4f), new Vector2(0f, 268f), new Vector2(900f, 44f));
                MakeText(panel, "Subtitle", "Choose the burden it leaves behind", 19f,
                    TextAlignmentOptions.Center, new Color(0.85f, 0.85f, 0.9f),
                    new Vector2(0f, 228f), new Vector2(900f, 30f));

                _counter = MakeText(panel, "Counter", "", 17f, TextAlignmentOptions.Center,
                    new Color(0.7f, 0.7f, 0.75f), new Vector2(0f, 196f), new Vector2(900f, 26f));
                RefreshCounter();

                var options = PickThree();
                for (var i = 0; i < options.Count; i++)
                {
                    var power = options[i];
                    MakeButton(panel, power, new Vector2(0f, 150f - i * 120f));
                }

                Log?.LogInfo("AP: cursed power choice shown");
            }
            catch (Exception error)
            {
                Log?.LogError("AP: cursed choice build failed: " + error);
                Close();
            }
        }

        private static List<NegativePowers.Power> PickThree()
        {
            var pool = new List<NegativePowers.Power>(NegativePowers.All);
            var picked = new List<NegativePowers.Power>();
            for (var i = 0; i < 3 && pool.Count > 0; i++)
            {
                var index = UnityEngine.Random.Range(0, pool.Count);
                picked.Add(pool[index]);
                pool.RemoveAt(index);
            }
            return picked;
        }

        private void MakeButton(RectTransform parent, NegativePowers.Power power, Vector2 position)
        {
            var rect = MakeRect("Choice_" + power.Name, parent, new Vector2(860f, 104f), position);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.16f, 0.13f, 0.2f, 1f);

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(0.28f, 0.22f, 0.34f, 1f);
            colors.pressedColor = new Color(0.36f, 0.26f, 0.42f, 1f);
            button.colors = colors;
            button.onClick.AddListener((UnityEngine.Events.UnityAction)(() => Choose(power)));

            MakeText(rect, "Name", power.Name, 26f, TextAlignmentOptions.Center, Color.white,
                new Vector2(0f, 20f), new Vector2(820f, 32f));
            MakeText(rect, "Effect", power.Effect, 18f, TextAlignmentOptions.Center,
                new Color(0.95f, 0.65f, 0.65f), new Vector2(0f, -20f), new Vector2(820f, 24f));
        }

        private void Choose(NegativePowers.Power power)
        {
            try
            {
                if (_player != null && _player._stats != null)
                {
                    power.Apply(_player, _player._stats);
                    Log?.LogInfo($"AP: cursed power chosen: {power.Name} ({power.Effect})");
                    ApRuntime.ShowNotice("Chosen: " + power.Name);
                }
                else
                {
                    Log?.LogWarning("AP: cursed power choice had no live player");
                }
            }
            catch (Exception error)
            {
                Log?.LogError("AP: applying cursed power failed: " + error);
            }

            _pending--;
            Close();
            if (_pending > 0 && _player != null)
            {
                _queued = _pending;
                Build();
            }
            else
            {
                _pending = 0;
                Unpause();
            }
        }

        /// <summary>
        /// Uses the game's own pause panel to freeze the run - that is the switch
        /// the game actually honours. timeScale is kept as a fallback and to keep
        /// the world stopped if the panel sets it too late in the frame.
        /// </summary>
        private void Pause()
        {
            if (_pausedByUs) return;
            _pausedByUs = true;
            _previousTimeScale = Time.timeScale;

            try
            {
                _pausePanel = UnityEngine.Object.FindObjectOfType<PausePanelManager>();
                _alreadyPaused = _pausePanel != null && _pausePanel.IsPaused;
                if (!_alreadyPaused) _pausePanel?.SetPauseState(true);
            }
            catch (Exception error)
            {
                Log?.LogWarning("AP: could not use the game pause panel: " + error.Message);
                _pausePanel = null;
                _alreadyPaused = false;
            }

            Time.timeScale = 0f;
            Log?.LogInfo($"AP: paused for the curse choice (panel={_pausePanel != null}, alreadyPaused={_alreadyPaused})");
        }

        private void Unpause()
        {
            if (!_pausedByUs) return;
            _pausedByUs = false;

            try
            {
                if (!_alreadyPaused) _pausePanel?.SetPauseState(false);
            }
            catch (Exception error)
            {
                Log?.LogWarning("AP: could not release the game pause: " + error.Message);
            }

            Time.timeScale = _previousTimeScale > 0f ? _previousTimeScale : 1f;
            _pausePanel = null;
            _alreadyPaused = false;
        }

        /// <summary>A prompt must never outlive the run it belongs to.</summary>
        private void Update()
        {
            if (_root == null) return;

            // The game's own menus write to timeScale, so keep re-asserting it.
            if (_pausedByUs && Time.timeScale != 0f) Time.timeScale = 0f;
            if (_pausedByUs && _pausePanel != null && !_pausePanel.IsPaused) _pausePanel.SetPauseState(true);

            try
            {
                var health = _player == null ? null : _player._health;
                if (health != null && !health.HasBeenKilled && health.CurrentHealth > 0f) return;

                Log?.LogInfo("AP: curse prompt closed because the run ended");
                _pending = 0;
                Close();
                Unpause();
            }
            catch
            {
                // reading a destroyed entity throws; treat that as the run ending
                _pending = 0;
                Close();
                Unpause();
            }
        }

        private void RefreshCounter()
        {
            if (_counter == null) return;
            _counter.text = _queued > 1 ? $"1 of {_queued} deaths" : "1 of 1 death";
        }

        public void Close()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            if (_canvas != null) UnityEngine.Object.Destroy(_canvas);
            _canvas = null;
            _counter = null;
        }

        private void OnDestroy()
        {
            Close();
            Unpause();
        }

        private static RectTransform MakeRect(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var go = new GameObject(name);
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static TextMeshProUGUI MakeText(RectTransform parent, string name, string content, float size,
            TextAlignmentOptions alignment, Color color, Vector2 position, Vector2 dimensions)
        {
            var rect = MakeRect(name, parent, dimensions, position);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            return text;
        }
    }
}
