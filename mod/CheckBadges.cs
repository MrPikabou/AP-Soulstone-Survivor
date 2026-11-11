using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SoulstoneAP
{
    /// <summary>Overlay (!) badges on map cards and character tiles.</summary>
    public sealed class CheckBadges : MonoBehaviour
    {
        private const float ScanInterval = 0.5f;
        private static readonly Color Fill = new Color(0.11f, 0.12f, 0.16f, 0.92f);
        private static readonly Color Ink = new Color(1f, 0.83f, 0.40f, 1f);
        private readonly Dictionary<int, GameObject> _maps = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> _chars = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> _modes = new Dictionary<int, GameObject>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly List<int> _stale = new List<int>();
        private readonly Dictionary<string, string> _summaries = new Dictionary<string, string>();
        private RectTransform _layer;
        private float _next, _diag;
        private bool _warned, _logged, _runActive;
        private int _summaryVersion = int.MinValue;
        private int _summaryCount = -1;

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + ScanInterval;
            try
            {
                // Map and character screens are not reachable during a run, so the
                // four scene-wide scans only cost frames there. Drop the badges once
                // and stay idle until the run ends.
                if (PlayerRefs.TryGet(out _))
                {
                    if (!_runActive)
                    {
                        _runActive = true;
                        ClearBadges();
                    }
                    return;
                }

                _runActive = false;
                if (_layer == null) Build();
                ScanMaps();
                ScanChars();
                ScanModes();
            }
            catch (Exception e)
            {
                if (_warned) return;
                _warned = true;
                Plugin.Logger?.LogWarning("CheckBadges: " + e);
            }
        }

        private void ScanMaps()
        {
            _seen.Clear();
            var found = 0;
            var first = "";
            var ok = false;
            foreach (var item in Resources.FindObjectsOfTypeAll<MapItem>())
            {
                if (item == null || !item.gameObject.activeInHierarchy) continue;
                found++;
                var map = item.MapId ?? "";
                var rect = RectOf(item.transform);
                if (found == 1) { first = map; ok = rect != null; }
                if (rect == null || map.Length == 0) continue;
                _seen.Add(item.GetInstanceID());
                var summary = Summary(map, "");
                var badge = Badge(_maps, item.GetInstanceID(), "Map_" + map, () => CheckListPanel.Show(map, ""));
                Place(badge, rect, summary.Length == 0 ? "!" : "! " + summary);
            }
            Prune(_maps);
            Report("map", _maps.Count, found, first, ok);
        }

        private void ScanChars()
        {
            _seen.Clear();
            var found = 0;
            var first = "";
            var ok = false;
            foreach (var item in Resources.FindObjectsOfTypeAll<CharacterScreenListItem>())
            {
                if (item == null || !item.gameObject.activeInHierarchy) continue;
                found++;
                if (item.CharacterId == CharactersType.None) continue;
                var name = item.CharacterId.ToString();
                var rect = RectOf(item.transform);
                if (first.Length == 0) { first = name; ok = rect != null; }
                if (rect == null) continue;
                _seen.Add(item.GetInstanceID());
                var summary = Summary("", name);
                var badge = Badge(_chars, item.GetInstanceID(), "Char_" + name, () => CheckListPanel.Show("", name));
                Place(badge, rect, summary.Length == 0 ? "!" : "! " + summary);
            }
            Prune(_chars);
            Report("character", _chars.Count, found, first, ok);
        }

        private void ScanModes()
        {
            _seen.Clear();
            var found = 0;
            foreach (var panel in Resources.FindObjectsOfTypeAll<MapSelectionMasteryScreenPanel>())
            {
                if (panel == null || !panel.gameObject.activeInHierarchy) continue;
                found++;
                AddModeBadge(panel.transform, panel.GetInstanceID(), "Cathedral", "Unholy Cathedral");
            }
            foreach (var panel in Resources.FindObjectsOfTypeAll<MapSelectionCrusadeScreenPanel>())
            {
                if (panel == null || !panel.gameObject.activeInHierarchy) continue;
                found++;
                AddModeBadge(panel.transform, panel.GetInstanceID(), "Titan", "Titan Hunt");
            }
            Prune(_modes);
            Report("mode", _modes.Count, found, "", true);
        }

        private void AddModeBadge(Transform source, int id, string name, string map)
        {
            var rect = RectOf(source);
            if (rect == null) return;
            _seen.Add(id);
            var summary = Summary(map, "");
            var badge = Badge(_modes, id, name, () => CheckListPanel.Show(map, ""));
            Place(badge, rect, summary.Length == 0 ? "!" : "! " + summary);
        }

        /// <summary>One summary per map/character until a check or item changes.</summary>
        private string Summary(string map, string character)
        {
            var runtime = ApRuntime.Instance;
            var version = runtime == null ? 0 : runtime.ProgressVersion;
            var count = LocationBook.Count;
            if (version != _summaryVersion || count != _summaryCount)
            {
                _summaries.Clear();
                _summaryVersion = version;
                _summaryCount = count;
            }

            var key = map + "\n" + character;
            if (!_summaries.TryGetValue(key, out var summary))
            {
                summary = LocationBook.SummaryFor(map, character);
                _summaries[key] = summary;
            }
            return summary;
        }

        private void ClearBadges()
        {
            ClearTable(_maps);
            ClearTable(_chars);
            ClearTable(_modes);
            _summaries.Clear();
            _summaryVersion = int.MinValue;
            _summaryCount = -1;
        }

        private void ClearTable(Dictionary<int, GameObject> table)
        {
            foreach (var pair in table)
                if (pair.Value != null) Destroy(pair.Value);
            table.Clear();
        }

        private void Report(string kind, int badges, int found, string first, bool ok)
        {
            if (badges > 0 && LocationBook.Count > 0)
            {
                if (_logged) return;
                _logged = true;
                Plugin.Logger?.LogInfo("CheckBadges: " + badges + " " + kind + " badges, book=" + LocationBook.Count);
                return;
            }
            if (Time.unscaledTime < _diag) return;
            _diag = Time.unscaledTime + 10f;
            Plugin.Logger?.LogInfo("CheckBadges: " + kind + " badges=" + badges + " found=" + found +
                " first='" + first + "' rect=" + ok + " book=" + LocationBook.Count);
        }

        private void Build()
        {
            var go = new GameObject("SoulstoneAP_Badges");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 26000;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            _layer = go.GetComponent<RectTransform>();
            if (_layer == null) _layer = go.AddComponent<RectTransform>();
            Plugin.Logger?.LogInfo("CheckBadges: overlay canvas ready");
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

        private GameObject Badge(Dictionary<int, GameObject> table, int id, string name, Action onClick)
        {
            if (table.TryGetValue(id, out var existing) && existing != null) return existing;
            var go = new GameObject("AP_Badge_" + name);
            var image = go.AddComponent<Image>();
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(_layer, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(78f, 22f);

            image.color = Fill;
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener((UnityEngine.Events.UnityAction)(() => onClick()));

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(rect, false);
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            text.fontSize = 12f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Ink;
            text.raycastTarget = false;

            table[id] = go;
            return go;
        }

        private void Place(GameObject badge, RectTransform target, string content)
        {
            if (badge == null || target == null) return;
            var corner = target.TransformPoint(new Vector3(target.rect.xMin, target.rect.yMax, 0f));
            var screen = RectTransformUtility.WorldToScreenPoint(null, corner);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_layer, screen, null, out var local)) return;

            var rect = badge.GetComponent<RectTransform>();
            if (rect == null) return;
            var width = Mathf.Clamp(16f + content.Length * 7f, 26f, 100f);
            var size = new Vector2(width, 22f);
            if (rect.sizeDelta != size) rect.sizeDelta = size;
            var position = local + new Vector2(width / 2f - 2f, -14f);
            if (rect.anchoredPosition != position) rect.anchoredPosition = position;

            var text = badge.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null && text.text != content) text.text = content;
        }

        private void Prune(Dictionary<int, GameObject> table)
        {
            _stale.Clear();
            foreach (var pair in table)
                if (!_seen.Contains(pair.Key)) _stale.Add(pair.Key);
            foreach (var id in _stale)
            {
                if (table.TryGetValue(id, out var go) && go != null) Destroy(go);
                table.Remove(id);
            }
        }
    }
}
