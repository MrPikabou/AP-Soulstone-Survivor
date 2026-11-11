using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SoulstoneAP
{
    /// <summary>Full rune logos (slot frame plus art) over the smoke, at their nodes.</summary>
    public sealed class RuneOverlay : MonoBehaviour
    {
        private const float ScanInterval = 0.1f;
        private const float IdleScanInterval = 0.5f;
        private const float OwnedRefreshInterval = 0.5f;
        private static readonly Color Fill = new Color(0.05f, 0.05f, 0.08f, 0.86f);
        private static readonly Color Ink = new Color(0.95f, 0.9f, 0.72f, 1f);
        private static readonly Dictionary<Type, PropertyInfo> SlotFields = new Dictionary<Type, PropertyInfo>();
        private static readonly Dictionary<Type, PropertyInfo> IconFields = new Dictionary<Type, PropertyInfo>();

        private sealed class LabelView
        {
            public GameObject Go;
            public RectTransform Rect;
            public Image Frame;
            public Image Art;
            public TextMeshProUGUI Text;
            public bool Icon;
        }

        private readonly Dictionary<int, LabelView> _labels = new Dictionary<int, LabelView>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly List<int> _stale = new List<int>();
        private readonly Dictionary<int, RectTransform> _targets = new Dictionary<int, RectTransform>();
        private readonly HashSet<string> _owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private RectTransform _layer;
        private Canvas _canvas;
        private float _next;
        private float _nextOwned;
        private bool _warned;
        private bool _runActive;

        private void Update()
        {
            try
            {
                // The skill tree cannot be opened mid-run. Skip its scene-wide
                // scan entirely while gameplay is live, and drop any overlay
                // left over from the menus.
                if (PlayerRefs.TryGet(out _))
                {
                    if (!_runActive)
                    {
                        _runActive = true;
                        ClearLabels();
                    }
                    return;
                }

                _runActive = false;
                if (_layer == null) Build();
                if (Time.unscaledTime >= _next)
                {
                    _next = Time.unscaledTime + ScanInterval;
                    ScanTree();
                    // Only pay for the full scan while the tree overlay is visible.
                    _next = Time.unscaledTime + (_labels.Count > 0 ? ScanInterval : IdleScanInterval);
                }
                Follow();
            }
            catch (Exception e)
            {
                if (_warned) return;
                _warned = true;
                Plugin.Logger?.LogWarning("RuneOverlay: " + e);
            }
        }

        private void Build()
        {
            var go = new GameObject("SoulstoneAP_RuneOverlay");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 26500;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            _layer = go.GetComponent<RectTransform>();
            if (_layer == null) _layer = go.AddComponent<RectTransform>();
            Plugin.Logger?.LogInfo("RuneOverlay: canvas ready");
        }

        private void ScanTree()
        {
            _seen.Clear();
            var now = Time.unscaledTime;
            if (now >= _nextOwned)
            {
                // Owned runes come from the save; reading it every 0.1s was the
                // expensive part of the open-tree scan.
                _owned.Clear();
                foreach (var rune in GameState.OwnedRunes()) _owned.Add(rune);
                _nextOwned = now + OwnedRefreshInterval;
            }

            var runtime = ApRuntime.Instance;
            foreach (var item in Resources.FindObjectsOfTypeAll<SkillTreePanelItem>())
            {
                if (item == null || !item.gameObject.activeInHierarchy) continue;
                var node = RuneNodes.ForHash(item.IdHash);
                if (node == null || !Unlocked(item, node, runtime, _owned)) continue;

                var target = RectOf(item.transform);
                if (target == null) continue;

                var id = item.GetInstanceID();
                _seen.Add(id);
                Place(id, Label(id), target, item, node);
            }
            Prune();
        }

        private static bool Unlocked(SkillTreePanelItem item, RuneNode node, ApRuntime runtime, HashSet<string> owned)
        {
            if (item.Level > 0) return true;
            if (owned.Contains(node.RuneId)) return true;
            if (runtime == null) return false;
            return runtime.HasReceivedItem("Rune: " + node.RuneId) || runtime.HasReceivedItem("Rune:" + node.RuneId);
        }

        private void Place(int id, LabelView label, RectTransform target, SkillTreePanelItem item, RuneNode node)
        {
            if (label == null || label.Go == null) return;
            if (target == null) { label.Go.SetActive(false); return; }
            label.Go.SetActive(true);

            var point = target.TransformPoint(target.rect.center);
            var screen = RectTransformUtility.WorldToScreenPoint(null, point);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_layer, screen, null, out var local)) return;

            var rect = label.Rect;
            var frame = label.Frame;
            var art = label.Art;
            var text = label.Text;

            _targets[id] = target;
            var frameSprite = SpriteField(item, "SlotIcon");
            var artSprite = SpriteField(item, "Icon") ?? node.Icon;

            if (artSprite != null)
            {
                var bounds = ScreenSize(target);
                var size = Mathf.Clamp(Mathf.Max(bounds.x, bounds.y), 24f, 220f);
                rect.sizeDelta = new Vector2(size, size);
                rect.anchoredPosition = local;

                frame.enabled = frameSprite != null;
                frame.sprite = frameSprite;
                frame.color = Color.white;
                frame.preserveAspect = true;

                art.enabled = true;
                art.sprite = artSprite;
                art.color = Color.white;
                label.Icon = true;
                if (text != null && text.text.Length != 0) text.text = "";
                return;
            }

            frame.enabled = true;
            frame.sprite = null;
            frame.color = Fill;
            frame.preserveAspect = false;
            art.enabled = false;
            label.Icon = false;
            rect.sizeDelta = new Vector2(210f, 20f);
            rect.anchoredPosition = local + new Vector2(0f, 34f);
            if (text != null)
            {
                var pretty = Pretty(node.RuneId);
                if (text.text != pretty) text.text = pretty;
            }
        }

        private static Sprite SpriteField(SkillTreePanelItem item, string name)
        {
            try
            {
                var type = item.GetType();
                var cache = name == "SlotIcon" ? SlotFields : IconFields;
                if (!cache.TryGetValue(type, out var property))
                {
                    property = type.GetProperty(name);
                    cache[type] = property;
                }

                var value = property?.GetValue(item);
                if (value is Image image) return image.sprite;
                if (value is Sprite sprite) return sprite;
            }
            catch { }
            return null;
        }

        private LabelView Label(int id)
        {
            if (_labels.TryGetValue(id, out var existing) && existing.Go != null) return existing;

            var go = new GameObject("AP_Rune");
            var image = go.AddComponent<Image>();
            image.color = Fill;
            image.raycastTarget = false;
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(_layer, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(210f, 20f);

            var artGo = new GameObject("Art");
            artGo.transform.SetParent(rect, false);
            var art = artGo.AddComponent<Image>();
            art.color = Color.white;
            art.preserveAspect = true;
            art.raycastTarget = false;
            var artRect = artGo.GetComponent<RectTransform>();
            artRect.anchorMin = Vector2.zero;
            artRect.anchorMax = Vector2.one;
            artRect.offsetMin = Vector2.zero;
            artRect.offsetMax = Vector2.zero;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(rect, false);
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            text.fontSize = 11f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Ink;
            text.raycastTarget = false;

            var view = new LabelView
            {
                Go = go,
                Rect = rect,
                Frame = image,
                Art = art,
                Text = text,
            };
            _labels[id] = view;
            return view;
        }

        private void Follow()
        {
            if (_labels.Count == 0) return;
            foreach (var pair in _labels)
            {
                var view = pair.Value;
                if (view == null || view.Go == null || !view.Go.activeSelf) continue;
                if (!_targets.TryGetValue(pair.Key, out var target) || target == null) continue;

                var point = target.TransformPoint(target.rect.center);
                var screen = RectTransformUtility.WorldToScreenPoint(null, point);
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_layer, screen, null, out var local)) continue;

                var rect = view.Rect;
                if (view.Icon)
                {
                    var bounds = ScreenSize(target);
                    var size = Mathf.Clamp(Mathf.Max(bounds.x, bounds.y), 24f, 220f);
                    rect.sizeDelta = new Vector2(size, size);
                    rect.anchoredPosition = local;
                }
                else
                {
                    rect.anchoredPosition = local + new Vector2(0f, 34f);
                }
            }
        }

        private Vector2 ScreenSize(RectTransform target)
        {
            var min = RectTransformUtility.WorldToScreenPoint(null, target.TransformPoint(target.rect.min));
            var max = RectTransformUtility.WorldToScreenPoint(null, target.TransformPoint(target.rect.max));
            var scale = _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            return new Vector2(Mathf.Abs(max.x - min.x), Mathf.Abs(max.y - min.y)) / scale;
        }

        private void ClearLabels()
        {
            foreach (var pair in _labels)
                if (pair.Value?.Go != null) Destroy(pair.Value.Go);
            _labels.Clear();
            _targets.Clear();
            _owned.Clear();
            _next = 0f;
            _nextOwned = 0f;
        }

        private void Prune()
        {
            _stale.Clear();
            foreach (var pair in _labels)
                if (!_seen.Contains(pair.Key)) _stale.Add(pair.Key);
            foreach (var id in _stale)
            {
                if (_labels.TryGetValue(id, out var view) && view.Go != null) Destroy(view.Go);
                _labels.Remove(id);
                _targets.Remove(id);
            }
        }

        private static RectTransform RectOf(Transform target)
        {
            if (target == null) return null;
            if (target is RectTransform own) return own;
            var rect = target.GetComponent<RectTransform>();
            if (rect != null) return rect;
            rect = target.GetComponentInChildren<RectTransform>(true);
            return rect != null ? rect : target.GetComponentInParent<RectTransform>();
        }

        private static string Pretty(string runeId)
        {
            if (string.IsNullOrEmpty(runeId)) return "?";
            var body = runeId.StartsWith("Rune", StringComparison.Ordinal) ? runeId.Substring(4) : runeId;
            var text = new StringBuilder(body.Length + 8);
            for (var i = 0; i < body.Length; i++)
            {
                var c = body[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(body[i - 1]) && body[i - 1] != ' ')
                    text.Append(' ');
                text.Append(c);
            }
            return text.Length == 0 ? runeId : text.ToString();
        }
    }
}
