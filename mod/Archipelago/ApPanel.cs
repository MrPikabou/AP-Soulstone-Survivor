using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SoulstoneAP
{
    /// <summary>
    /// In-game Archipelago connection panel (uGUI + TextMeshPro).
    ///
    /// Two constraints shaped this design:
    ///  * IMGUI is unusable here - GUI.DoTextField throws "Method unstripping failed".
    ///  * TMP_InputField cannot hold focus, because the game's menu logic reassigns
    ///    the EventSystem selection every frame (click selects all, then deselects).
    ///
    /// So the panel only uses uGUI for rendering and click hit-testing, and reads
    /// keystrokes straight from the Input System into its own string buffers.
    /// </summary>
    public sealed class ApPanel : MonoBehaviour
    {
        private const float PanelWidth = 560f;
        private const float PanelHeight = 440f;
        private const int MaxFieldLength = 120;

        private static readonly Color PanelColor = new Color(0.09f, 0.09f, 0.12f, 0.97f);
        private static readonly Color FieldColor = new Color(0.16f, 0.16f, 0.21f, 1f);
        private static readonly Color FieldActiveColor = new Color(0.21f, 0.26f, 0.34f, 1f);
        private static readonly Color AccentColor = new Color(0.29f, 0.52f, 0.78f, 1f);
        private static readonly Color MutedColor = new Color(0.62f, 0.64f, 0.70f, 1f);

        private sealed class Field
        {
            public Image Background;
            public TextMeshProUGUI Text;
            public string Value = "";
            public string Placeholder = "";
            public bool IsPassword;

            public bool Active
            {
                get => _active;
                set
                {
                    _active = value;
                    if (Background != null) Background.color = value ? FieldActiveColor : FieldColor;
                }
            }

            private bool _active;
        }

        private Canvas _canvas;
        private GameObject _panel;

        private readonly List<Field> _fields = new List<Field>();
        private Field _server;
        private Field _slot;
        private Field _password;

        private TextMeshProUGUI _statusText;
        private TextMeshProUGUI _errorText;
        private TextMeshProUGUI _connectLabel;

        private Texture2D _logo;
        private float _bootTime;
        private bool _toggled;
        private bool _dismissed;
        private bool _autoOpened;
        private bool _built;
        private float _nextStatusRefresh;
        private string _error = "";

        private static ManualLogSource Log => Plugin.Logger;

        // ------------------------------------------------------------------ setup

        private void Awake()
        {
            _bootTime = Time.unscaledTime;
            LoadLogo();
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
            {
                _toggled = !_toggled;
                _dismissed = false;
            }

            if (!_built)
            {
                // Build only once the game has an EventSystem; creating our own
                // would steal the save-slot menu's clicks.
                if (EventSystem.current == null && Time.unscaledTime - _bootTime < 10f) return;
                Build();
                if (!_built) return;
            }

            if (_canvas == null)
            {
                _built = false;
                return;
            }

            var loaded = !ProfileMissing();

            // Open once the chosen save has loaded, so the panel stops covering
            // the save-slot screen at boot (both behaviours are configurable).
            if (!loaded)
            {
                _autoOpened = false;
            }
            else if (!_autoOpened)
            {
                _autoOpened = true;
                if (Plugin.OpenPanelAfterSaveLoad.Value)
                {
                    _toggled = true;
                    _dismissed = false;
                }
            }

            var visible = _toggled || (!_dismissed && Plugin.ShowPanelOnSlotScreen.Value && !loaded);
            if (_panel.activeSelf != visible)
            {
                _panel.SetActive(visible);
                if (!visible) SetActiveField(null);
            }

            if (!visible) return;

            HandleClickOutside();
            HandleTyping();

            if (Time.unscaledTime >= _nextStatusRefresh)
            {
                _nextStatusRefresh = Time.unscaledTime + 0.25f;
                RefreshStatus();
                RefreshFieldTexts();
            }
        }

        private static bool ProfileMissing()
        {
            try { return Global.PlayerProfile == null; }
            catch { return true; }
        }

        // ------------------------------------------------------------------ typing

        private static readonly Key[] PrintableKeys = BuildPrintableKeys();

        private static Key[] BuildPrintableKeys()
        {
            var keys = new List<Key>();
            for (var key = Key.A; key <= Key.Z; key++) keys.Add(key);
            // Key.Digit0 sorts after Digit9 in the enum, so it is added separately.
            for (var key = Key.Digit1; key <= Key.Digit9; key++) keys.Add(key);
            keys.Add(Key.Digit0);
            keys.Add(Key.Space);
            keys.Add(Key.Period);
            keys.Add(Key.Comma);
            keys.Add(Key.Slash);
            keys.Add(Key.Semicolon);
            keys.Add(Key.Minus);
            keys.Add(Key.Equals);
            keys.Add(Key.Quote);
            keys.Add(Key.LeftBracket);
            keys.Add(Key.RightBracket);
            keys.Add(Key.Backslash);
            keys.Add(Key.Backquote);
            keys.Add(Key.NumpadPeriod);
            keys.Add(Key.NumpadDivide);
            keys.Add(Key.NumpadMinus);
            for (var key = Key.Numpad0; key <= Key.Numpad9; key++) keys.Add(key);
            return keys.ToArray();
        }

        /// <summary>Clicking anywhere outside the panel drops keyboard focus.</summary>
        private void HandleClickOutside()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            if (_activeField == null || _panel == null) return;

            var panelRect = _panel.GetComponent<RectTransform>();
            if (panelRect == null) return;
            if (RectTransformUtility.RectangleContainsScreenPoint(panelRect, mouse.position.ReadValue(), null)) return;

            SetActiveField(null);
        }

        private void SetActiveField(Field field)
        {
            foreach (var candidate in _fields) candidate.Active = ReferenceEquals(candidate, field);
            _activeField = field;
            if (field != null) RefreshFieldText(field);
        }

        private Field _activeField;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetKeyState(int virtualKey);

        private const int CapsLockVirtualKey = 0x14;

        /// <summary>
        /// OS Caps Lock state. The Input System only reports Caps Lock being pressed,
        /// never whether the lock is on, so typing in capitals relied on Shift alone.
        /// </summary>
        private static bool CapsLockOn()
        {
            try { return (GetKeyState(CapsLockVirtualKey) & 1) != 0; }
            catch { return false; }
        }

        private void HandleTyping()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || _activeField == null) return;

            var changed = false;

            if (keyboard.backspaceKey.wasPressedThisFrame && _activeField.Value.Length > 0)
            {
                _activeField.Value = _activeField.Value.Substring(0, _activeField.Value.Length - 1);
                changed = true;
            }

            var command = keyboard.ctrlKey.isPressed
                || (keyboard.leftCommandKey != null && keyboard.leftCommandKey.isPressed)
                || (keyboard.rightCommandKey != null && keyboard.rightCommandKey.isPressed);

            if (command && keyboard.vKey.wasPressedThisFrame) PasteIntoActiveField();
            else if (command && keyboard.cKey.wasPressedThisFrame) CopyActiveField();

            if (!command)
            {
                var shift = keyboard.shiftKey.isPressed;
                var caps = CapsLockOn();
                foreach (var key in PrintableKeys)
                {
                    if (_activeField.Value.Length >= MaxFieldLength) break;

                    var control = keyboard[key];
                    if (control == null || !control.wasPressedThisFrame) continue;

                    var character = KeyToChar(key, shift, caps);
                    if (!character.HasValue) continue;

                    _activeField.Value += character.Value;
                    changed = true;
                }
            }

            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame ||
                keyboard.tabKey.wasPressedThisFrame)
            {
                SetActiveField(null);
            }

            if (changed && _activeField != null) RefreshFieldText(_activeField);
        }

        private void CopyActiveField()
        {
            try
            {
                GUIUtility.systemCopyBuffer = _activeField.Value ?? "";
                _error = "";
            }
            catch (Exception error)
            {
                _error = "Clipboard unavailable on this build (" + error.GetType().Name + ").";
            }
        }

        private void PasteIntoActiveField()
        {
            var field = _activeField;
            if (field == null) return;

            string clipboard;
            try
            {
                clipboard = GUIUtility.systemCopyBuffer;
            }
            catch (Exception error)
            {
                _error = "Clipboard unavailable on this build (" + error.GetType().Name + ").";
                return;
            }

            if (string.IsNullOrEmpty(clipboard)) return;

            // Multi-line clips lose their breaks: these fields are single line.
            var buffer = new char[clipboard.Length];
            var length = 0;
            foreach (var character in clipboard)
            {
                if (character < ' ' || character == '\u007f') continue;
                buffer[length++] = character;
            }
            if (length == 0) return;

            var room = MaxFieldLength - field.Value.Length;
            if (room <= 0) return;
            if (length > room) length = room;

            field.Value += new string(buffer, 0, length);
            _error = "";
            RefreshFieldText(field);
        }

        /// <summary>
        /// One key to one character. Caps Lock only affects letters; symbols stay on
        /// Shift, exactly as a normal text box behaves.
        /// </summary>
        private static char? KeyToChar(Key key, bool shift, bool caps)
        {
            if (key >= Key.A && key <= Key.Z)
            {
                var letter = (char)('a' + (key - Key.A));
                return (shift ^ caps) ? char.ToUpperInvariant(letter) : letter;
            }

            if (key >= Key.Digit1 && key <= Key.Digit9)
            {
                const string shifted = "!@#$%^&*(";
                var index = key - Key.Digit1;
                return shift ? shifted[index] : (char)('1' + index);
            }

            if (key == Key.Digit0) return shift ? ')' : '0';

            if (key >= Key.Numpad0 && key <= Key.Numpad9) return (char)('0' + (key - Key.Numpad0));

            switch (key)
            {
                case Key.Space: return ' ';
                case Key.Period: return shift ? '>' : '.';
                case Key.Comma: return shift ? '<' : ',';
                case Key.Slash: return shift ? '?' : '/';
                case Key.Semicolon: return shift ? ':' : ';';
                case Key.Minus: return shift ? '_' : '-';
                case Key.Equals: return shift ? '+' : '=';
                case Key.Quote: return shift ? '"' : '\'';
                case Key.LeftBracket: return shift ? '{' : '[';
                case Key.RightBracket: return shift ? '}' : ']';
                case Key.Backslash: return shift ? '|' : '\\';
                case Key.Backquote: return shift ? '~' : '`';
                case Key.NumpadPeriod: return '.';
                case Key.NumpadDivide: return '/';
                case Key.NumpadMinus: return '-';
                default: return null;
            }
        }

        private void RefreshFieldTexts()
        {
            foreach (var field in _fields) RefreshFieldText(field);
        }

        private void RefreshFieldText(Field field)
        {
            if (field?.Text == null) return;

            var caret = field.Active && ((int)(Time.unscaledTime * 2f) % 2 == 0) ? "|" : "";

            if (string.IsNullOrEmpty(field.Value))
            {
                field.Text.text = field.Active ? caret : field.Placeholder;
                field.Text.color = new Color(0.5f, 0.52f, 0.58f, 1f);
                return;
            }

            field.Text.color = Color.white;
            field.Text.text = field.IsPassword
                ? new string('*', field.Value.Length) + caret
                : field.Value + caret;
        }

        // ------------------------------------------------------------------ build

        private void Build()
        {
            try
            {
                var canvasObject = new GameObject("SoulstoneAP_Canvas");
                UnityEngine.Object.DontDestroyOnLoad(canvasObject);

                _canvas = canvasObject.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 25000;

                var scaler = canvasObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;

                canvasObject.AddComponent<GraphicRaycaster>();

                _panel = BuildPanel(canvasObject.transform);
                _built = true;
                Log?.LogInfo("AP: in-game panel built");
            }
            catch (Exception error)
            {
                Log?.LogError("AP: panel build failed: " + error);
                _built = false;
            }
        }

        private GameObject BuildPanel(Transform parent)
        {
            var root = MakeRect("SoulstoneAP_Panel", parent);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = Vector2.zero;
            root.sizeDelta = new Vector2(PanelWidth, PanelHeight);

            var background = root.gameObject.AddComponent<Image>();
            background.color = PanelColor;

            if (_logo != null)
            {
                var logoRect = MakeRect("Logo", root);
                logoRect.anchorMin = logoRect.anchorMax = new Vector2(0.5f, 1f);
                logoRect.pivot = new Vector2(0.5f, 1f);
                logoRect.anchoredPosition = new Vector2(0f, -14f);
                logoRect.sizeDelta = new Vector2(92f, 92f);

                var logoImage = logoRect.gameObject.AddComponent<Image>();
                logoImage.sprite = Sprite.Create(_logo, new Rect(0, 0, _logo.width, _logo.height),
                    new Vector2(0.5f, 0.5f));
                logoImage.preserveAspect = true;
                logoImage.raycastTarget = false;
            }

            MakeText(root, "Title", "Archipelago", 26f, TextAlignmentOptions.Center, Color.white,
                new Vector2(0f, -108f), new Vector2(400f, 34f));
            MakeText(root, "Subtitle", "Soulstone Survivors", 14f, TextAlignmentOptions.Center, MutedColor,
                new Vector2(0f, -136f), new Vector2(400f, 22f));

            _server = MakeField(root, "Server", "localhost:38281", false, -172f);
            _slot = MakeField(root, "Slot", "slot name", false, -220f);
            _password = MakeField(root, "Password", "password (optional)", true, -268f);

            _server.Value = Plugin.Server.Value ?? "";
            _slot.Value = Plugin.Slot.Value ?? "";
            _password.Value = Plugin.Password.Value ?? "";

            MakeButton(root, "ConnectButton", "Connect", AccentColor,
                new Vector2(-92f, -330f), new Vector2(160f, 40f), OnConnectPressed, out _connectLabel);

            MakeButton(root, "CloseButton", "Close", new Color(0.28f, 0.29f, 0.34f, 1f),
                new Vector2(96f, -330f), new Vector2(120f, 40f), OnClosePressed, out _);

            _errorText = MakeText(root, "Error", "", 13f, TextAlignmentOptions.Center,
                new Color(0.95f, 0.55f, 0.55f, 1f), new Vector2(0f, -374f), new Vector2(PanelWidth - 40f, 20f));

            _statusText = MakeText(root, "Status", "", 13f, TextAlignmentOptions.Center, MutedColor,
                new Vector2(0f, -398f), new Vector2(PanelWidth - 40f, 34f));
            _statusText.enableWordWrapping = true;

            RefreshFieldTexts();
            return root.gameObject;
        }

        private Field MakeField(RectTransform parent, string name, string placeholder, bool password, float y)
        {
            var row = MakeRect(name + "Row", parent);
            row.anchorMin = row.anchorMax = new Vector2(0.5f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.anchoredPosition = new Vector2(0f, y);
            row.sizeDelta = new Vector2(PanelWidth - 60f, 40f);

            var label = MakeText(row, name + "Label", name, 14f, TextAlignmentOptions.MidlineLeft,
                MutedColor, Vector2.zero, new Vector2(150f, 40f));
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            label.rectTransform.pivot = new Vector2(0f, 0.5f);
            label.rectTransform.anchoredPosition = Vector2.zero;

            var fieldRect = MakeRect(name + "Field", row);
            fieldRect.anchorMin = fieldRect.anchorMax = new Vector2(1f, 0.5f);
            fieldRect.pivot = new Vector2(1f, 0.5f);
            fieldRect.anchoredPosition = Vector2.zero;
            fieldRect.sizeDelta = new Vector2(PanelWidth - 60f - 160f, 38f);

            var fieldBackground = fieldRect.gameObject.AddComponent<Image>();
            fieldBackground.color = FieldColor;

            var textRect = MakeRect("Text", fieldRect);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10f, 4f);
            textRect.offsetMax = new Vector2(-10f, -4f);
            textRect.gameObject.AddComponent<RectMask2D>();

            var text = textRect.gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = 17f;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.enableWordWrapping = false;
            text.raycastTarget = false;

            var field = new Field
            {
                Background = fieldBackground,
                Text = text,
                Placeholder = placeholder,
                IsPassword = password,
            };
            _fields.Add(field);

            // A Button just for click handling: sets this field as the typing target.
            var button = fieldRect.gameObject.AddComponent<Button>();
            button.targetGraphic = fieldBackground;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener((UnityEngine.Events.UnityAction)(() => SetActiveField(field)));

            return field;
        }

        private Button MakeButton(RectTransform parent, string name, string label, Color color,
            Vector2 position, Vector2 size, Action onClick, out TextMeshProUGUI labelText)
        {
            var rect = MakeRect(name, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener((UnityEngine.Events.UnityAction)(() => onClick()));

            var text = MakeText(rect, name + "Label", label, 17f, TextAlignmentOptions.Center, Color.white,
                Vector2.zero, size);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            text.raycastTarget = false;

            labelText = text;
            return button;
        }

        private static TextMeshProUGUI MakeText(RectTransform parent, string name, string content, float size,
            TextAlignmentOptions alignment, Color color, Vector2 position, Vector2 dimensions)
        {
            var rect = MakeRect(name, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;

            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform MakeRect(string name, Transform parent)
        {
            var go = new GameObject(name);
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        // ------------------------------------------------------------------ actions

        private void OnClosePressed()
        {
            _dismissed = true;
            _toggled = false;
            SetActiveField(null);
        }

        private void OnConnectPressed()
        {
            var runtime = ApRuntime.Instance;
            if (runtime == null)
            {
                _error = "Mod not ready yet.";
                return;
            }

            SetActiveField(null);

            if (runtime.IsRunning)
            {
                runtime.Disconnect();
                _error = "";
                return;
            }

            if (!TryParseAddress(_server.Value, out var host, out var port))
            {
                _error = "Server must look like host:port (e.g. localhost:38281).";
                return;
            }
            if (string.IsNullOrWhiteSpace(_slot.Value))
            {
                _error = "Enter your slot name.";
                return;
            }

            _error = "";
            SaveSettings();
            runtime.Connect(host, port, _slot.Value.Trim(), _password.Value ?? "");
        }

        private static bool TryParseAddress(string address, out string host, out int port)
        {
            host = null;
            port = 38281;
            if (string.IsNullOrWhiteSpace(address)) return false;

            var parts = address.Trim().Split(':');
            host = parts[0];
            if (parts.Length > 1 && !int.TryParse(parts[1], out port)) return false;
            return !string.IsNullOrEmpty(host);
        }

        private void SaveSettings()
        {
            Plugin.Server.Value = _server.Value ?? "";
            Plugin.Slot.Value = _slot.Value ?? "";
            Plugin.Password.Value = _password.Value ?? "";
            Plugin.Instance?.Config.Save();
        }

        private void RefreshStatus()
        {
            var runtime = ApRuntime.Instance;
            var running = runtime != null && runtime.IsRunning;

            if (_statusText != null)
            {
                var status = runtime == null ? "mod not initialised" : runtime.Status;
                if (ProfileMissing() && running)
                    status += "\nWaiting for you to pick save slot " + Plugin.AllowedProfile.Value;
                _statusText.text = status;
            }

            if (_errorText != null) _errorText.text = _error;
            if (_connectLabel != null) _connectLabel.text = running ? "Disconnect" : "Connect";
        }

        // ------------------------------------------------------------------ logo

        private void LoadLogo()
        {
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
                    _logo = texture;
                    break;
                }
            }
            catch (Exception error)
            {
                Log?.LogWarning("AP: could not load logo: " + error.Message);
            }
        }
    }
}
