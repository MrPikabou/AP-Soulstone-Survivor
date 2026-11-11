using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SoulstoneAP
{
    /// <summary>Check list opened by the (!) badges; replaces the old F8 Maps tab.</summary>
    public sealed class CheckListPanel : MonoBehaviour
    {
        private const float W = 620f, H = 640f;
        private static CheckListPanel _instance;
        private GameObject _panel;
        private TextMeshProUGUI _title, _body;
        private RectTransform _content;
        private ScrollRect _scroll;
        private string _map = "", _character = "";
        private bool _global;
        private int _version = int.MinValue;
        private float _next;
        private bool _built;

        public static void Show(string map, string character)
        {
            if (_instance == null) return;
            _instance._global = false;
            _instance._map = map ?? "";
            _instance._character = character ?? "";
            _instance._version = int.MinValue;
            _instance.Open();
        }

        /// <summary>Portal cycles, lifetime counters and other checks with no map or character.</summary>
        public static void ShowGlobal()
        {
            if (_instance == null) return;
            _instance._global = true;
            _instance._map = "";
            _instance._character = "";
            _instance._version = int.MinValue;
            _instance.Open();
        }

        private void Awake() { _instance = this; DontDestroyOnLoad(gameObject); }
        private void OnDestroy() { if (_instance == this) _instance = null; }

        private void Update()
        {
            if (_panel == null || !_panel.activeSelf) return;
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) { _panel.SetActive(false); return; }
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.25f;
            var runtime = ApRuntime.Instance;
            var version = (runtime == null ? 0 : runtime.ProgressVersion) + LocationBook.Count;
            if (version != _version) { _version = version; Render(); }
            Wheel();
        }

        private void Open()
        {
            if (!_built) Build();
            if (_panel == null) return;
            _panel.SetActive(true);
            var label = _global ? "Global" : (string.IsNullOrEmpty(_character) ? Friendly(_map) : _character);
            var summary = LocationBook.SummaryFor(_map, _character, _global);
            _title.text = "Checks - " + label + (summary.Length > 0 ? "   " + summary : "");
            Render();
        }

        private static string Friendly(string map)
        {
            return !string.IsNullOrEmpty(map) && map.StartsWith("Survivors", StringComparison.Ordinal)
                ? map.Substring("Survivors".Length)
                : map;
        }

        private void Render()
        {
            if (_body == null) return;
            _body.text = LocationBook.Render(_map, _character, _global);
            _content.sizeDelta = new Vector2(_content.sizeDelta.x, Mathf.Max(H - 96f, _body.preferredHeight + 16f));
        }

        private void Wheel()
        {
            if (_scroll == null || _scroll.viewport == null) return;
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (!RectTransformUtility.RectangleContainsScreenPoint(_scroll.viewport, mouse.position.ReadValue(), null)) return;
            var notches = mouse.scroll.ReadValue().y / 120f;
            if (Mathf.Abs(notches) < 0.01f) return;
            var v = _scroll.velocity;
            v.y = Mathf.Clamp(v.y - notches * 90f, -900f, 900f);
            _scroll.velocity = v;
        }

        private void Build()
        {
            var go = new GameObject("SoulstoneAP_CheckList");
            DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 27000;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            var root = MakeRect("Panel", go.transform);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(W, H);
            root.gameObject.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.11f, 0.97f);
            _panel = root.gameObject;

            _title = MakeLabel(root, 20f, new Vector2(0f, -30f), new Vector2(W - 140f, 30f));
            AddButton(root, "X", new Vector2(W / 2f - 38f, -26f), new Vector2(40f, 30f));

            var viewport = MakeRect("Viewport", root);
            viewport.anchorMin = viewport.anchorMax = new Vector2(0.5f, 1f);
            viewport.pivot = new Vector2(0.5f, 1f);
            viewport.anchoredPosition = new Vector2(0f, -64f);
            viewport.sizeDelta = new Vector2(W - 40f, H - 96f);
            viewport.gameObject.AddComponent<Image>().color = new Color(0.04f, 0.04f, 0.06f, 1f);
            viewport.gameObject.AddComponent<RectMask2D>();

            _content = MakeRect("Content", viewport);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.sizeDelta = new Vector2(0f, H - 96f);

            var textRect = MakeRect("Body", _content);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12f, 8f);
            textRect.offsetMax = new Vector2(-12f, -8f);
            _body = textRect.gameObject.AddComponent<TextMeshProUGUI>();
            _body.fontSize = 14f;
            _body.alignment = TextAlignmentOptions.TopLeft;
            _body.color = new Color(0.85f, 0.87f, 0.92f, 1f);
            _body.enableWordWrapping = true;
            _body.raycastTarget = false;

            _scroll = viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.content = _content;
            _scroll.viewport = viewport;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.inertia = true;
            _scroll.decelerationRate = 0.12f;
            _scroll.scrollSensitivity = 0f;

            _built = true;
            _panel.SetActive(false);
        }

        private static RectTransform MakeRect(string name, Transform parent)
        {
            var go = new GameObject(name);
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        private static TextMeshProUGUI MakeLabel(RectTransform parent, float size, Vector2 pos, Vector2 dims)
        {
            var rect = MakeRect("Label", parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = dims;
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.fontSize = size;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;
            return label;
        }

        private void AddButton(RectTransform parent, string text, Vector2 pos, Vector2 size)
        {
            var rect = MakeRect("Button", parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.3f, 0.31f, 0.36f, 1f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener((UnityEngine.Events.UnityAction)(() => _panel.SetActive(false)));
            var label = MakeLabel(rect, 16f, Vector2.zero, size);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            label.text = text;
        }
    }
}
