using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SoulstoneAP
{
    /// <summary>Top-right connection dot, server and goal progress, visible outside F8.</summary>
    public sealed class StatusHud : MonoBehaviour
    {
        private static Sprite _dot;
        private RectTransform _root;
        private Image _dotImage;
        private TextMeshProUGUI _server;
        private TextMeshProUGUI _state;
        private TextMeshProUGUI _goal;
        private int _goalTotal = -1;
        private int _goalCleared = -1;
        private string _goalLabel = "";
        private bool _goalVisible;
        private float _next;

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.25f;
            try
            {
                if (_root == null) Build();
                Refresh();
            }
            catch (System.Exception error) { Plugin.Logger?.LogWarning("StatusHud: " + error.Message); }
        }

        private void Build()
        {
            var go = new GameObject("SoulstoneAP_Status");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 24510;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _root = new GameObject("Root").AddComponent<RectTransform>();
            _root.SetParent(go.transform, false);
            _root.anchorMin = _root.anchorMax = new Vector2(1f, 1f);
            _root.pivot = new Vector2(1f, 1f);
            _root.anchoredPosition = new Vector2(-16f, -16f);
            _root.sizeDelta = new Vector2(310f, 54f);
            var background = _root.gameObject.AddComponent<Image>();
            background.color = new Color(0.05f, 0.05f, 0.08f, 0.78f);
            background.raycastTarget = false;

            var dotGo = new GameObject("Dot");
            _dotImage = dotGo.AddComponent<Image>();
            _dotImage.sprite = Dot();
            _dotImage.raycastTarget = false;
            var dotRect = dotGo.GetComponent<RectTransform>();
            dotRect.SetParent(_root, false);
            dotRect.anchorMin = dotRect.anchorMax = new Vector2(0f, 1f);
            dotRect.pivot = new Vector2(0f, 1f);
            dotRect.anchoredPosition = new Vector2(12f, -14f);
            dotRect.sizeDelta = new Vector2(10f, 10f);

            _server = Text("Server", "", 12f, TextAlignmentOptions.MidlineLeft,
                new Vector2(30f, -6f), new Vector2(190f, 18f));
            _state = Text("State", "", 11f, TextAlignmentOptions.MidlineLeft,
                new Vector2(30f, -27f), new Vector2(190f, 18f));
            _goal = Text("Goal", "", 12f, TextAlignmentOptions.MidlineRight,
                new Vector2(-12f, -16f), new Vector2(96f, 22f));
            _goal.color = new Color(1f, 0.83f, 0.40f, 1f);
            var goalRect = _goal.rectTransform;
            goalRect.anchorMin = goalRect.anchorMax = new Vector2(1f, 1f);
            goalRect.pivot = new Vector2(1f, 1f);
            Plugin.Logger?.LogInfo("StatusHud: canvas ready");
        }

        private TextMeshProUGUI Text(string name, string value, float size, TextAlignmentOptions align,
            Vector2 position, Vector2 dimensions)
        {
            var go = new GameObject(name);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.alignment = align;
            text.color = new Color(0.84f, 0.87f, 0.92f, 1f);
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(_root, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;
            return text;
        }

        private void Refresh()
        {
            var runtime = ApRuntime.Instance;
            var phase = runtime == null ? "off" : runtime.Phase;
            Color color;
            string state;
            switch (phase)
            {
                case "joined":
                    color = new Color(0.48f, 0.88f, 0.64f, 1f);
                    state = "connected";
                    break;
                case "joining":
                    color = new Color(1f, 0.78f, 0.34f, 1f);
                    state = "joining slot";
                    break;
                case "connecting":
                    color = new Color(1f, 0.78f, 0.34f, 1f);
                    state = "connecting";
                    break;
                case "retrying":
                    color = new Color(1f, 0.42f, 0.42f, 1f);
                    var left = runtime != null ? runtime.RetrySeconds : 0;
                    state = left > 0 ? "retry in " + left + "s" : "retrying";
                    break;
                default:
                    color = new Color(0.55f, 0.58f, 0.64f, 1f);
                    state = "not connected";
                    break;
            }

            if (_dotImage.color != color) _dotImage.color = color;

            var server = runtime != null && !string.IsNullOrEmpty(runtime.ServerLabel)
                ? runtime.ServerLabel
                : Plugin.Server.Value ?? "";
            if (!string.Equals(_server.text, server, StringComparison.Ordinal)) _server.text = server;
            if (!string.Equals(_state.text, state, StringComparison.Ordinal)) _state.text = state;
            if (_state.color != color) _state.color = color;

            var total = runtime != null ? runtime.GoalTotal : 0;
            var visible = total > 0;
            if (_goalVisible != visible)
            {
                _goalVisible = visible;
                _goal.gameObject.SetActive(visible);
            }

            if (visible)
            {
                var cleared = runtime.GoalCleared;
                var label = runtime.GoalLabel;
                if (_goalTotal != total || _goalCleared != cleared || !string.Equals(_goalLabel, label, StringComparison.Ordinal))
                {
                    _goalTotal = total;
                    _goalCleared = cleared;
                    _goalLabel = label;
                    _goal.text = cleared + "/" + total + " " + label;
                }
            }
        }

        private static Sprite Dot()
        {
            if (_dot != null) return _dot;
            const int size = 16;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            var center = (size - 1) / 2f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var inside = dx * dx + dy * dy <= 49f;
                    pixels[y * size + x] = new Color32(255, 255, 255, inside ? (byte)255 : (byte)0);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            _dot = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return _dot;
        }
    }
}
