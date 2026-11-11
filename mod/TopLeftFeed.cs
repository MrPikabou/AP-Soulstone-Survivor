using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SoulstoneAP
{
    /// <summary>Top-left feed of the last items and checks, F9 to toggle.</summary>
    public sealed class TopLeftFeed : MonoBehaviour
    {
        private const int MaxRows = 6;
        private const float Width = 380f;
        private const float RowHeight = 30f;
        private const float Lifetime = 9f;
        private const float FadeStart = 6.5f;
        private static Sprite _logo;
        private static readonly Dictionary<string, Sprite> CharacterIcons = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private readonly List<GameObject> _rows = new List<GameObject>();
        private readonly List<CanvasGroup> _groups = new List<CanvasGroup>();
        private readonly List<Image> _icons = new List<Image>();
        private readonly List<TextMeshProUGUI> _texts = new List<TextMeshProUGUI>();
        private readonly List<Image> _accents = new List<Image>();
        private readonly List<Outline> _outlines = new List<Outline>();
        private readonly ApRuntime.FeedEntry[] _shown = new ApRuntime.FeedEntry[MaxRows];
        private readonly bool[] _rowOn = new bool[MaxRows];
        private RectTransform _root;
        private bool _visible = true;
        private float _next;

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame) _visible = !_visible;
            if (!_visible)
            {
                if (_root != null) _root.gameObject.SetActive(false);
                return;
            }
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.2f;
            try
            {
                if (_root == null) Build();
                Refresh();
            }
            catch (Exception e) { Plugin.Logger?.LogWarning("TopLeftFeed: " + e.Message); }
        }

        private void Build()
        {
            var go = new GameObject("SoulstoneAP_Feed");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 24500;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            _root = new GameObject("Root").AddComponent<RectTransform>();
            _root.SetParent(go.transform, false);
            _root.anchorMin = _root.anchorMax = new Vector2(0f, 1f);
            _root.pivot = new Vector2(0f, 1f);
            _root.anchoredPosition = new Vector2(16f, -16f);
            _root.sizeDelta = new Vector2(Width, MaxRows * RowHeight);

            for (var i = 0; i < MaxRows; i++) MakeRow(i);
            Plugin.Logger?.LogInfo("TopLeftFeed: canvas ready");
        }

        private void MakeRow(int index)
        {
            var row = new GameObject("Row" + index);
            var background = row.AddComponent<Image>();
            background.color = new Color(0.05f, 0.05f, 0.08f, 0.78f);
            background.raycastTarget = false;

            var outline = row.AddComponent<Outline>();
            outline.effectDistance = new Vector2(2f, -2f);
            outline.useGraphicAlpha = false;
            outline.effectColor = new Color(1f, 1f, 1f, 0.30f);
            var group = row.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            var rect = row.GetComponent<RectTransform>();
            rect.SetParent(_root, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(0f, -index * RowHeight);
            rect.sizeDelta = new Vector2(Width, RowHeight - 4f);

            var iconGo = new GameObject("Icon");
            var icon = iconGo.AddComponent<Image>();
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            var iconRect = iconGo.GetComponent<RectTransform>();
            iconRect.SetParent(rect, false);
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new Vector2(6f, 0f);
            iconRect.sizeDelta = new Vector2(24f, 24f);

            var textGo = new GameObject("Text");
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.fontSize = 13f;
            text.alignment = TextAlignmentOptions.Left;
            text.color = new Color(0.94f, 0.95f, 0.98f, 1f);
            text.raycastTarget = false;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.SetParent(rect, false);
            textRect.anchorMin = new Vector2(0f, 0f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.offsetMin = new Vector2(36f, 2f);
            textRect.offsetMax = new Vector2(-8f, -2f);

            var accentGo = new GameObject("Accent");
            var accent = accentGo.AddComponent<Image>();
            accent.raycastTarget = false;
            var accentRect = accentGo.GetComponent<RectTransform>();
            accentRect.SetParent(rect, false);
            accentRect.anchorMin = new Vector2(0f, 0f);
            accentRect.anchorMax = new Vector2(0f, 1f);
            accentRect.pivot = new Vector2(0f, 0.5f);
            accentRect.anchoredPosition = Vector2.zero;
            accentRect.sizeDelta = new Vector2(4f, 0f);

            row.SetActive(false);
            _rows.Add(row);
            _groups.Add(group);
            _icons.Add(icon);
            _texts.Add(text);
            _accents.Add(accent);
            _outlines.Add(outline);
        }

        private void Refresh()
        {
            var runtime = ApRuntime.Instance;
            var feed = runtime?.Feed;
            var now = Time.unscaledTime;

            for (var i = 0; i < MaxRows; i++)
            {
                var entry = feed == null || i >= feed.Count ? null : feed[feed.Count - 1 - i];
                var age = entry == null ? float.MaxValue : now - entry.Time;
                if (entry == null || age > Lifetime)
                {
                    if (_rowOn[i])
                    {
                        _rowOn[i] = false;
                        _rows[i].SetActive(false);
                    }
                    _shown[i] = null;
                    continue;
                }

                if (!_rowOn[i])
                {
                    _rowOn[i] = true;
                    _rows[i].SetActive(true);
                }

                // TMP rebuilds its mesh on every text/color assignment, so only
                // touch a row when the feed slot actually changes.
                if (!ReferenceEquals(_shown[i], entry))
                {
                    _shown[i] = entry;
                    var accent = Accent(entry.Class);
                    _texts[i].text = entry.Text;
                    _texts[i].color = Tint(entry.Class);
                    _accents[i].color = accent;
                    _outlines[i].effectColor = new Color(accent.r, accent.g, accent.b, 0.55f);
                    _icons[i].sprite = IconFor(entry);
                    _icons[i].color = Color.white;
                }

                var alpha = age <= FadeStart ? 1f : Mathf.Clamp01(1f - (age - FadeStart) / (Lifetime - FadeStart));
                if (!Mathf.Approximately(_groups[i].alpha, alpha)) _groups[i].alpha = alpha;
            }
        }

        private static Color Accent(string category)
        {
            switch (category ?? "")
            {
                case "progression": return new Color(1f, 0.78f, 0.34f, 1f);
                case "useful": return new Color(0.44f, 0.70f, 1f, 1f);
                case "filler": return new Color(0.60f, 0.64f, 0.70f, 1f);
                case "trap": return new Color(1f, 0.42f, 0.42f, 1f);
                case "check": return new Color(0.48f, 0.88f, 0.64f, 1f);
                case "goal": return new Color(1f, 0.82f, 0.40f, 1f);
                case "chat": return new Color(0.79f, 0.71f, 1f, 1f);
                default: return new Color(0.75f, 0.79f, 0.86f, 1f);
            }
        }

        private static Color Tint(string category)
        {
            var accent = Accent(category);
            return Color.Lerp(accent, Color.white, category == "filler" ? 0.45f : 0.55f);
        }

        private static Sprite IconFor(ApRuntime.FeedEntry entry)
        {
            if (entry.Kind == "Rune" && !string.IsNullOrEmpty(entry.Key))
            {
                var node = RuneNodes.ForRuneId(entry.Key);
                if (node?.Icon != null) return node.Icon;
            }

            if (entry.Kind == "Char" && !string.IsNullOrEmpty(entry.Key))
            {
                if (CharacterIcons.TryGetValue(entry.Key, out var cached) && cached != null) return cached;
                var sprite = CharacterIcon(entry.Key);
                if (sprite != null)
                {
                    CharacterIcons[entry.Key] = sprite;
                    return sprite;
                }
            }

            return Logo();
        }

        private static Sprite CharacterIcon(string character)
        {
            try
            {
                foreach (var item in Resources.FindObjectsOfTypeAll<CharacterScreenListItem>())
                {
                    if (item == null || item.CharacterId.ToString() != character) continue;
                    var value = item.GetType().GetProperty("CharacterIcon")?.GetValue(item);
                    if (value is Image image) return image.sprite;
                    if (value is Sprite sprite) return sprite;
                }
            }
            catch { }
            return null;
        }

        private static Sprite Logo()
        {
            if (_logo != null) return _logo;
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                foreach (var name in assembly.GetManifestResourceNames())
                {
                    if (!name.EndsWith("ap-logo.png", StringComparison.OrdinalIgnoreCase)) continue;
                    using var stream = assembly.GetManifestResourceStream(name);
                    using var memory = new MemoryStream();
                    stream?.CopyTo(memory);
                    var texture = new Texture2D(2, 2);
                    texture.LoadImage(memory.ToArray());
                    _logo = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f));
                    break;
                }
            }
            catch (Exception e) { Plugin.Logger?.LogWarning("TopLeftFeed: logo unavailable: " + e.Message); }
            return _logo;
        }
    }
}
