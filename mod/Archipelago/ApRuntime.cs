using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using BepInEx.Logging;
using UnityEngine;

namespace SoulstoneAP
{
    /// <summary>
    /// Main-thread half of the in-game Archipelago client: consumes server
    /// messages, applies received items to the save and reports location checks.
    /// </summary>
    public sealed class ApRuntime : MonoBehaviour
    {
        public ApRuntime(IntPtr ptr) : base(ptr) { }

        private static ApRuntime _instance;

        private readonly ApSession _session = new ApSession();
        private readonly HashSet<int> _sentChecks = new HashSet<int>();
        private readonly HashSet<int> _checkedLocations = new HashSet<int>();
        private readonly HashSet<string> _receivedItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _mapLocations = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _achievementLocations = new Dictionary<string, int>();
        private readonly List<string> _goalMaps = new List<string>();
        private readonly List<int> _goalLocations = new List<int>();
        private string _goalLabel = "maps";
        private string _serverLabel = "";
        private readonly Dictionary<int, string> _itemCommands = new Dictionary<int, string>();
        // Maps this slot actually received. With map_unlocks on, a `Clear <map>`
        // check is only reported once its map item arrived, so unlocking a map by
        // playing (the game's own achievement route) earns nothing.
        private readonly HashSet<string> _grantedMaps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _mapsAreDefault;

        private sealed class PendingGrant
        {
            public int Index;
            public string Command;
        }

        private readonly List<PendingGrant> _pendingGrants = new List<PendingGrant>();

        // Everything the panel shows, oldest first: own checks, own items and
        // other players' finds. Bounded so a long session cannot grow forever.
        private const int MaxLogLines = 200;
        private sealed class EventLine
        {
            public string Text;
            public string Class;
        }

        private readonly List<EventLine> _events = new List<EventLine>();
        private readonly Dictionary<int, string> _playerNames = new Dictionary<int, string>();
        private int _ourSlot = -1;

        /// <summary>The whole log, one entry per line, oldest first.</summary>
        /// <summary>One recent item or check for the top-left HUD.</summary>
        public sealed class FeedEntry
        {
            public string Text;
            public string Kind;
            public string Key;
            public string Class;
            public float Time;
        }

        private readonly List<FeedEntry> _feed = new List<FeedEntry>();

        /// <summary>Newest last; the HUD reads this directly.</summary>
        public List<FeedEntry> Feed => _feed;

        public string EventLog
        {
            get
            {
                if (_events.Count == 0) return "";
                var text = new StringBuilder();
                for (var i = 0; i < _events.Count; i++)
                {
                    if (i > 0) text.Append((char)10);
                    text.Append("<color=#").Append(ColorOf(_events[i].Class)).Append('>')
                        .Append(_events[i].Text).Append("</color>");
                }
                return text.ToString();
            }
        }

        /// <summary>Bumped for every new line, so the panel knows when to repaint.</summary>
        public int EventLogVersion { get; private set; }

        private bool _joined;
        private bool _autoConnectTried;
        private float _saveAt = -1f;
        private float _nextPoll;
        private string _joinStatus = "";

        // links
        private bool _deathLinkEnabled;
        private bool _knockbackLinkEnabled;
        private string _slotName = "";
        private bool _wasDead;
        private float _lastHealth = -1f;
        private float _nextKnockbackSend;
        private float _nextLinkPoll;
        private string _effectNotice = "";
        private float _effectNoticeUntil;
        private AudioSource _audio;
        private AudioClip _goodTone;
        private AudioClip _badTone;
        private bool _soundWarned;
        private float _nextChime;

        public static ApRuntime Instance => _instance;
        public string Status => _session.RetrySeconds > 0
            ? _session.Status
            : _joined
                ? "In game (v" + Plugin.Version + "): " + _joinStatus +
                  (Time.unscaledTime < _effectNoticeUntil ? " | " + _effectNotice : "")
                : _session.Status;
        public bool IsRunning => _session.IsRunning;
        public bool IsJoined => _joined;
        public string ServerLabel => _serverLabel;
        public int RetrySeconds => _session.RetrySeconds;

        /// <summary>off / connecting / joining / joined / retrying, for the status HUD.</summary>
        public string Phase
        {
            get
            {
                if (!_session.IsRunning) return "off";
                if (_session.RetrySeconds > 0) return "retrying";
                if (!_session.Connected) return "connecting";
                return _joined ? "joined" : "joining";
            }
        }

        public int GoalTotal => _goalLocations.Count;

        public string GoalLabel => _goalLabel;

        public int GoalCleared
        {
            get
            {
                var cleared = 0;
                foreach (var id in _goalLocations)
                    if (IsChecked(id)) cleared++;
                return cleared;
            }
        }

        private void Awake()
        {
            _instance = this;
        }

        public void Connect(string host, int port, string slot, string password)
        {
            _sentChecks.Clear();
            _slotName = slot;
            _serverLabel = host + ":" + port;
            _goalMaps.Clear();
            _goalLocations.Clear();
            _goalLabel = "maps";
            _wasDead = false;
            _lastHealth = -1f;
            _joined = false;
            _itemCommands.Clear();
            _mapLocations.Clear();
            _achievementLocations.Clear();
            _joinStatus = "";
            _session.Start(host, port, slot, password);
        }

        /// <summary>Sends a chat command such as `!hint`; replies arrive as Print / PrintJSON.</summary>
        public void SendChat(string command)
        {
            command = (command ?? "").Trim();
            if (command.Length == 0) return;
            if (!_joined || !_session.IsRunning || !_session.Connected)
            {
                NoteEvent("Not connected - chat command not sent.", "chat");
                return;
            }
            var payload = new Json().StartObject()
                .Name("cmd").Value("Say")
                .Name("text").Value(command)
                .EndObject().ToString();
            _session.Send("[" + payload + "]");
            NoteEvent("> " + command, "chat");
        }

        public void Disconnect()
        {
            _session.Stop();
            _joined = false;
            _joinStatus = "";
            _goalMaps.Clear();
            _goalLocations.Clear();
            _goalLabel = "maps";
        }

        private void Update()
        {
            if (!_autoConnectTried)
            {
                _autoConnectTried = true;
                TryAutoConnect();
            }

            DrainIncoming();

            if (Time.unscaledTime >= _nextPoll)
            {
                _nextPoll = Time.unscaledTime + 1f;
                if (_joined)
                {
                    RetryPendingGrants();
                    ReportChecks();
                }
                MaybeSave();
            }

            PollLinks();
        }

        /// <summary>Grants live in memory; flush them so closing the game cannot lose them.</summary>
        private void MaybeSave()
        {
            if (_saveAt < 0f || Time.unscaledTime < _saveAt) return;

            var result = GameState.Save(
                SaveFileKind.UnlockedCharacters,
                SaveFileKind.CharacterProgression,
                SaveFileKind.UnlockedWeapons,
                SaveFileKind.Currencies);
            if (result.StartsWith("OK", StringComparison.Ordinal))
            {
                _saveAt = -1f;
                Log?.LogInfo("AP: profile saved after item grants");
            }
            else if (result.Contains("BUSY"))
            {
                _saveAt = Time.unscaledTime + 1f;
            }
            else
            {
                _saveAt = Time.unscaledTime + 5f;
            }
        }

        private void TryAutoConnect()
        {
            if (!Plugin.AutoConnect.Value) return;
            if (string.IsNullOrWhiteSpace(Plugin.Slot.Value)) return;

            var address = (Plugin.Server.Value ?? "localhost:38281").Trim();
            var parts = address.Split(':');
            var host = parts[0];
            var port = 38281;
            if (parts.Length > 1 && !int.TryParse(parts[1], out port)) port = 38281;

            Log?.LogInfo($"AP: auto-connecting to {host}:{port} as '{Plugin.Slot.Value}'");
            Connect(host, port, Plugin.Slot.Value, Plugin.Password.Value ?? "");
        }

        /// <summary>Items received before a save slot is loaded are retried here.</summary>
        private void RetryPendingGrants()
        {
            if (_pendingGrants.Count == 0) return;

            var changed = false;
            for (var i = 0; i < _pendingGrants.Count; i++)
            {
                var pending = _pendingGrants[i];
                var reply = GameState.Grant(pending.Command);
                if (!reply.StartsWith("OK", StringComparison.Ordinal)) continue;

                Log?.LogInfo($"AP: (retried) {pending.Command} -> {reply}");
                _pendingGrants.RemoveAt(i);
                AdvanceApplied(pending.Index + 1);
                _saveAt = Time.unscaledTime + 1f;
                changed = true;
                i--;
            }

            // The counter has to survive a crash too, or the same item is handed
            // out again on the next launch.
            if (changed) Plugin.Instance?.Config.Save();
        }

        private void AdvanceApplied(int count)
        {
            if (count <= Plugin.AppliedItems.Value) return;
            Plugin.AppliedItems.Value = count;
        }

        // ------------------------------------------------------------ messages

        private static ManualLogSource Log => Plugin.Logger;

        private void DrainIncoming()
        {
            while (_session.Incoming.TryDequeue(out var text))
            {
                List<object> packets;
                try
                {
                    var parsed = JsonParser.Parse(text);
                    packets = parsed as List<object> ?? new List<object> { parsed };
                }
                catch (Exception error)
                {
                    Log?.LogWarning($"AP: could not parse message: {error.Message}");
                    continue;
                }

                foreach (var packet in packets)
                {
                    var message = JsonParser.AsObject(packet);
                    if (message == null) continue;
                    try
                    {
                        HandleMessage(message);
                    }
                    catch (Exception error)
                    {
                        Log?.LogError($"AP: handling '{JsonParser.GetString(message, "cmd")}' failed: {error}");
                    }
                }
            }
        }

        private void HandleMessage(Dictionary<string, object> message)
        {
            var cmd = JsonParser.GetString(message, "cmd", "");
            switch (cmd)
            {
                case "RoomInfo":
                    SendConnect();
                    break;

                case "Connected":
                    OnConnected(message);
                    break;

                case "ReceivedItems":
                    OnReceivedItems(message);
                    break;

                case "Bounce":
                case "Bounced":   // what the server forwards a Bounce as
                    OnBounce(message);
                    break;

                case "PrintJSON":
                    OnPrintJson(message);
                    break;

                case "Print":
                case "ChatPrint":
                    NoteChat(JsonParser.GetString(message, "text", ""));
                    break;

                case "ConnectionRefused":
                    _session.Status = "Refused: " + FirstError(message);
                    Log?.LogWarning("AP: connection refused - " + FirstError(message));
                    _session.Stop();
                    break;
            }
        }

        private static string FirstError(Dictionary<string, object> message)
        {
            if (message.TryGetValue("errors", out var errors) && JsonParser.AsArray(errors) is { } list && list.Count > 0)
                return JsonParser.AsString(list[0]) ?? "unknown";
            return "unknown";
        }

        /// <summary>
        /// Server broadcasts. ItemSend fires for every check anyone completes, so
        /// this is how other players' finds reach the panel; our own checks are
        /// logged where they are sent, where the location name is known.
        /// </summary>
        private void OnPrintJson(Dictionary<string, object> message)
        {
            var type = JsonParser.GetString(message, "type", "");

            if (type == "Chat")
            {
                var chat = JoinData(message);
                if (!string.IsNullOrEmpty(chat)) NoteChat(chat);
                return;
            }

            if (type == "Hint")
            {
                var hint = HintText(message);
                if (!string.IsNullOrEmpty(hint)) NoteChat(hint);
                return;
            }

            if (type == "ItemSend")
            {
                var item = message.TryGetValue("item", out var raw) ? JsonParser.AsObject(raw) : null;
                if (item == null) return;

                var sender = JsonParser.GetInt(item, "player", -1);
                var receiving = JsonParser.GetInt(message, "receiving", -1);

                // Our own finds are logged by ReportChecks, and items for us arrive
                // as ReceivedItems. Only other-to-other sends are news here.
                if (sender == _ourSlot || receiving == _ourSlot) return;

                var found = PlayerName(sender) + " found " + JsonParser.GetString(item, "name", "an item") +
                            " for " + PlayerName(receiving);
                var category = ClassOfFlags(item);
                NoteEvent(found, category);
                NoteFeed(found, "Other", "", category, true);
                return;
            }

            if (type == "Goal")
            {
                var slot = JsonParser.GetInt(message, "slot", -1);
                if (slot >= 0 && slot != _ourSlot)
                {
                    var goal = PlayerName(slot) + " completed their goal";
                    NoteEvent(goal, "goal");
                    NoteFeed(goal, "Goal", "", "goal");
                }
            }
        }

        private void SendConnect()
        {
            var payload = new Json()
                .StartObject()
                .Name("cmd").Value("Connect")
                .Name("game").Value(SoulstoneConfig.GameName)
                .Name("name").Value(Plugin.Slot.Value)
                .Name("uuid").Value(Guid.NewGuid().ToString())
                .Name("version").StartObject()
                    .Name("major").Value(0)
                    .Name("minor").Value(6)
                    .Name("build").Value(8)
                    .Name("class").Value("Version")
                .EndObject()
                .Name("items_handling").Value(0b111)
                .Name("tags").StartArray()
                    .Value("DeathLink")
                    .Value("KnockbackLink")
                .EndArray()
                .Name("password").Value(Plugin.Password.Value ?? "")
                .EndObject()
                .ToString();

            _session.Send("[" + payload + "]");
            _session.Status = "Joining slot '" + Plugin.Slot.Value + "'...";
        }

        private void OnConnected(Dictionary<string, object> message)
        {
            _joined = true;
            _sentChecks.Clear();
            _checkedLocations.Clear();
            _receivedItems.Clear();

            if (message.TryGetValue("checked_locations", out var checkedRaw) &&
                JsonParser.AsArray(checkedRaw) is { } checkedList)
            {
                foreach (var value in checkedList)
                {
                    var checkedId = JsonParser.AsInt(value, -1);
                    if (checkedId >= 0) _checkedLocations.Add(checkedId);
                }
            }

            var slot = JsonParser.GetInt(message, "slot", -1);
            _ourSlot = slot;
            _events.Clear();
            EventLogVersion++;
            ReadPlayerNames(message);

            var players = message.TryGetValue("slot_info", out var info)
                ? JsonParser.AsObject(info)?.Count ?? 0
                : 0;

            string seed = "";
            if (message.TryGetValue("slot_data", out var slotData) && JsonParser.AsObject(slotData) is { } data)
                ParseSlotData(data, out seed);

            // The applied-items counter only means anything for the seed it was built
            // on. Joining a different room or slot used to keep the old count, which
            // silently skipped every item below it (a fresh profile then received
            // nothing at all).
            SyncAppliedSeed(JsonParser.GetString(message, "seed_name", ""), slot);

            _joinStatus = $"slot {slot}, {seed}";
            Log?.LogInfo($"AP: joined slot {slot} ({players} slots), {_itemCommands.Count} items, " +
                         $"{_mapLocations.Count} maps, {_achievementLocations.Count} achievements, " +
                         $"{EndgameChecks.Count} endgame checks");
        }

        /// <summary>Slot number to player name, so a received item can say who sent it.</summary>
        private void ReadPlayerNames(Dictionary<string, object> message)
        {
            _playerNames.Clear();
            if (!message.TryGetValue("players", out var raw) || JsonParser.AsArray(raw) is not { } players) return;

            foreach (var entry in players)
            {
                var player = JsonParser.AsObject(entry);
                if (player == null) continue;
                var slot = JsonParser.GetInt(player, "slot", -1);
                if (slot < 0) continue;
                var name = JsonParser.GetString(player, "alias", "");
                if (string.IsNullOrEmpty(name)) name = JsonParser.GetString(player, "name", "");
                _playerNames[slot] = name;
            }
        }

        /// <summary>Adds one line to the panel log, dropping the oldest past the cap.</summary>
        private void NoteEvent(string line, string cls = "")
        {
            if (string.IsNullOrEmpty(line)) return;
            _events.Add(new EventLine { Text = line, Class = cls ?? "" });
            while (_events.Count > MaxLogLines) _events.RemoveAt(0);
            EventLogVersion++;
        }

        /// <summary>Adds one line to the top-left feed, keeping only the newest entries.</summary>
        private void NoteFeed(string text, string kind, string key, string cls = "", bool silent = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            var category = string.IsNullOrEmpty(cls) ? ClassOf(kind) : cls;
            _feed.Add(new FeedEntry { Text = text, Kind = kind ?? "", Key = key ?? "", Class = category, Time = Time.unscaledTime });
            while (_feed.Count > 24) _feed.RemoveAt(0);
            if (!silent && (category == "progression" || category == "trap")) Chime(category == "trap");
        }

        /// <summary>Chat and command replies; also mirrored to the BepInEx console.</summary>
        private void NoteChat(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (var raw in text.Split((char)10))
            {
                var line = raw.TrimEnd();
                if (line.Length == 0) continue;
                NoteEvent(line, "chat");
                Plugin.Logger?.LogInfo("AP chat: " + line);
            }
        }

        private static string JoinData(Dictionary<string, object> message)
        {
            var text = JsonParser.GetString(message, "text", "");
            if (!string.IsNullOrEmpty(text)) return text;
            if (!message.TryGetValue("data", out var raw) || JsonParser.AsArray(raw) is not { } data) return "";
            var joined = new StringBuilder();
            foreach (var part in data)
            {
                var entry = JsonParser.AsObject(part);
                var line = entry != null ? JsonParser.GetString(entry, "text", "") : JsonParser.AsString(part) ?? "";
                if (string.IsNullOrEmpty(line)) continue;
                if (joined.Length > 0) joined.Append((char)10);
                joined.Append(line);
            }
            return joined.ToString();
        }

        private string HintText(Dictionary<string, object> message)
        {
            var item = message.TryGetValue("item", out var itemRaw) ? JsonParser.AsObject(itemRaw) : null;
            if (item == null) return JoinData(message);
            var location = message.TryGetValue("location", out var locationRaw) ? JsonParser.AsObject(locationRaw) : null;
            var itemName = JsonParser.GetString(item, "name", "an item");
            var itemPlayer = JsonParser.GetInt(item, "player", -1);
            var locationName = location != null ? JsonParser.GetString(location, "name", "a location") : "a location";
            var locationPlayer = location != null ? JsonParser.GetInt(location, "player", -1) : -1;
            var text = "Hint: " + itemName + " for " + PlayerName(itemPlayer) + " at " + locationName;
            if (locationPlayer >= 0 && locationPlayer != itemPlayer)
                text += " (" + PlayerName(locationPlayer) + "'s world)";
            return text + (JsonParser.GetBool(message, "found") ? " - already found" : "");
        }

        private static string ClassOfFlags(Dictionary<string, object> item)
        {
            if (item == null) return "";
            var flags = JsonParser.GetInt(item, "flags", 0);
            if ((flags & 4) != 0) return "trap";
            if ((flags & 1) != 0) return "progression";
            if ((flags & 2) != 0) return "useful";
            return flags != 0 ? "filler" : "";
        }

        private static string ClassOf(string kind)
        {
            switch ((kind ?? "").ToLowerInvariant())
            {
                case "char":
                case "map":
                case "craft":
                case "curse":
                case "cursetier":
                case "weaponcap":
                case "mode":
                    return "progression";
                case "rune":
                case "weapon":
                case "resource":
                    return "useful";
                case "souls":
                    return "filler";
                case "trap":
                    return "trap";
                case "check":
                    return "check";
                case "goal":
                    return "goal";
                default:
                    return "";
            }
        }

        private static string ColorOf(string category)
        {
            switch (category ?? "")
            {
                case "progression": return "FFC857";
                case "useful": return "6FB3FF";
                case "filler": return "9AA3B2";
                case "trap": return "FF6B6B";
                case "check": return "7BE0A3";
                case "goal": return "FFD166";
                case "chat": return "C9B6FF";
                default: return "E8EDF5";
            }
        }

        private void Chime(bool trap)
        {
            if (Plugin.FeedSounds == null || !Plugin.FeedSounds.Value) return;
            if (Time.unscaledTime < _nextChime) return;
            _nextChime = Time.unscaledTime + 0.4f;
            try
            {
                if (_audio == null)
                {
                    _audio = gameObject.AddComponent<AudioSource>();
                    _audio.playOnAwake = false;
                    _audio.spatialBlend = 0f;
                    _audio.volume = 0.35f;
                }
                if (trap)
                {
                    if (_badTone == null) _badTone = Tone(196f, 0.22f);
                    _audio.PlayOneShot(_badTone, 0.7f);
                }
                else
                {
                    if (_goodTone == null) _goodTone = Tone(659f, 0.14f);
                    _audio.PlayOneShot(_goodTone, 0.55f);
                }
            }
            catch (Exception error)
            {
                if (_soundWarned) return;
                _soundWarned = true;
                Log?.LogWarning("AP: feed sound unavailable: " + error.Message);
            }
        }

        private static AudioClip Tone(float hz, float seconds)
        {
            const int rate = 44100;
            var count = Mathf.Max(1, Mathf.CeilToInt(rate * seconds));
            var samples = new float[count];
            for (var i = 0; i < count; i++)
            {
                var t = i / (float)rate;
                var fade = Mathf.Min(1f, t * 60f) * Mathf.Min(1f, (seconds - t) * 30f);
                samples[i] = Mathf.Sin(2f * Mathf.PI * hz * t) * 0.5f * fade;
            }
            var clip = AudioClip.Create("ap_tone", count, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>The server's checked set plus whatever this session has sent.</summary>
        public bool IsChecked(int id) => _sentChecks.Contains(id) || _checkedLocations.Contains(id);

        /// <summary>Has an item with this exact name arrived this session?</summary>
        public bool HasReceivedItem(string name) => !string.IsNullOrEmpty(name) && _receivedItems.Contains(name);

        /// <summary>Bumped whenever a check is sent, so the browser can repaint.</summary>
        public int ProgressVersion { get; private set; }

        /// <summary>Slot number to something worth printing.</summary>
        private string PlayerName(int slot)
        {
            if (slot < 0) return "another world";
            if (slot == _ourSlot) return "you";
            return _playerNames.TryGetValue(slot, out var name) && !string.IsNullOrEmpty(name)
                ? name
                : "player " + slot;
        }

        /// <summary>Remembers one received item for the panel, with who sent it.</summary>
        private void NoteReceived(string command, Dictionary<string, object> item, bool silent = false)
        {
            var name = JsonParser.GetString(item, "name", "");
            if (!string.IsNullOrEmpty(name)) _receivedItems.Add(name);
            ProgressVersion++;
            var from = PlayerName(JsonParser.GetInt(item, "player", -1));
            var line = "You received " + Describe(command) + (from == "you" ? "" : " (from " + from + ")");
            var colon = command.IndexOf(':');
            var kind = colon > 0 ? command.Substring(0, colon) : command;
            var category = ClassOfFlags(item);
            if (string.IsNullOrEmpty(category)) category = ClassOf(kind);
            NoteEvent(line, category);
            NoteFeed(line, kind, colon > 0 ? command.Substring(colon + 1) : "", category, silent);
        }

        /// <summary>Item command (`Weapon:WeaponBarbarian-02`) to something a player recognises.</summary>
        private static string Describe(string command)
        {
            var colon = command.IndexOf(':');
            var kind = colon > 0 ? command.Substring(0, colon) : command;
            var value = colon > 0 ? command.Substring(colon + 1) : "";

            switch (kind)
            {
                case "Char":
                    return Spaced(value) + " (character)";
                case "Weapon":
                    return Spaced(Strip(value, "Weapon")) + " (weapon)";
                case "Rune":
                    return Spaced(Strip(value, "Rune")) + " (rune)";
                case "Map":
                    return Spaced(Strip(value, "Survivors")) + " (map)";
                case "Mode":
                    if (value == "Mastery") return "Unholy Cathedral (mode)";
                    if (value == "Crusade") return "Titan Hunt (mode)";
                    return value + " (mode)";
                case "Curse":
                    return "Curse tier " + value;
                case "WeaponCap":
                    return value == "-1" ? "Weapon upgrades: unlimited" : "Weapon upgrades +" + value;
                case "Souls":
                {
                    var parts = value.Split(':');
                    var amount = parts[parts.Length - 1];
                    var name = parts.Length > 1 ? parts[0] : "minor";
                    return amount + " " + Spaced(name) + " soulstones";
                }
                case "Resource":
                    return "Resource Cache";
                case "Craft":
                    return "Crafting licence: " + Spaced(value);
                case "Trap":
                    return "Trap: " + value;
                default:
                    return command;
            }
        }

        private static string Strip(string value, string prefix)
        {
            return value != null && value.StartsWith(prefix, StringComparison.Ordinal) ? value.Substring(prefix.Length) : value;
        }

        /// <summary>Camel-case ids to words, digits split off: `Killed10000` -> `Killed 10000`.</summary>
        internal static string Spaced(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            var builder = new StringBuilder(text.Length + 8);
            for (var i = 0; i < text.Length; i++)
            {
                var character = text[i];
                if (i > 0)
                {
                    var previous = text[i - 1];
                    if (char.IsUpper(character) && !char.IsUpper(previous) && previous != ' ' && !char.IsDigit(previous))
                        builder.Append(' ');
                    else if (char.IsDigit(character) && !char.IsDigit(previous) && previous != ' ')
                        builder.Append(' ');
                }
                builder.Append(character);
            }
            return builder.ToString();
        }

        /// <summary>Starts the applied-items counter over when the seed or slot changed.</summary>
        private void SyncAppliedSeed(string seedName, int slot)
        {
            var identity = (string.IsNullOrEmpty(seedName) ? "?" : seedName) + "|" + Plugin.Slot.Value + "|" + slot;
            if (string.Equals(Plugin.AppliedSeed.Value, identity, StringComparison.Ordinal)) return;

            var previous = Plugin.AppliedItems.Value;
            Plugin.AppliedSeed.Value = identity;
            Plugin.AppliedItems.Value = 0;
            GameState.ResetLicences();
            Plugin.Instance?.Config.Save();
            Log?.LogInfo($"AP: new seed '{identity}' - replaying all {previous} previously counted item(s)");
        }

        private void ParseSlotData(Dictionary<string, object> slotData, out string summary)
        {
            _itemCommands.Clear();
            _mapLocations.Clear();
            _achievementLocations.Clear();
            _grantedMaps.Clear();
            GameState.GrantedMaps.Clear();
            _mapsAreDefault = false;
            _goalMaps.Clear();
            _goalLocations.Clear();
            _goalLabel = "maps";
            _deathLinkEnabled = false;
            GameState.CraftLicences = false;
            _knockbackLinkEnabled = false;

            var sawWeaponItem = false;
            var sawMapItem = false;
            if (slotData.TryGetValue("items", out var items) && JsonParser.AsObject(items) is { } itemMap)
            {
                foreach (var entry in itemMap)
                {
                    if (!int.TryParse(entry.Key, out var id)) continue;
                    var spec = JsonParser.AsObject(entry.Value);
                    if (spec == null) continue;
                    var kind = JsonParser.GetString(spec, "kind", "");
                    var value = JsonParser.GetString(spec, "value", "");
                    switch (kind)
                    {
                        case "char": _itemCommands[id] = "Char:" + value; break;
                        case "weapon":
                            _itemCommands[id] = "Weapon:" + value;
                            sawWeaponItem = true;
                            break;
                        case "rune": _itemCommands[id] = "Rune:" + value; break;
                        case "map":
                            _itemCommands[id] = "Map:" + value;
                            sawMapItem = true;
                            break;
                        case "souls": _itemCommands[id] = "Souls:" + value; break;
                        case "craft": _itemCommands[id] = "Craft:" + value; break;
                        case "trap": _itemCommands[id] = "Trap:" + value; break;
                        case "resource": _itemCommands[id] = "Resource:" + value; break;
                    }
                }
            }

            if (slotData.TryGetValue("maps", out var maps) && JsonParser.AsObject(maps) is { } mapMap)
            {
                var mapIds = new List<string>();
                foreach (var entry in mapMap)
                {
                    _mapLocations[entry.Key] = JsonParser.AsInt(entry.Value);
                    mapIds.Add(entry.Key);
                }

                _mapsAreDefault = JsonParser.GetBool(slotData, "maps_default");

                if (slotData.TryGetValue("goal_maps", out var goalRaw) && JsonParser.AsArray(goalRaw) is { } goalList)
                {
                    foreach (var entry in goalList)
                    {
                        var name = JsonParser.AsString(entry);
                        if (string.IsNullOrEmpty(name)) continue;
                        if (name.StartsWith("Clear ", StringComparison.Ordinal)) name = name.Substring(6);
                        _goalMaps.Add(name);
                    }
                }

                // New seeds name the goal locations by id, which covers both map
                // and portal goals. Older seeds fall back to their goal_maps list.
                if (slotData.TryGetValue("goal_locations", out var goalLocationsRaw) &&
                    JsonParser.AsArray(goalLocationsRaw) is { } goalLocations)
                {
                    foreach (var entry in goalLocations)
                    {
                        var id = JsonParser.AsInt(entry);
                        if (id != 0) _goalLocations.Add(id);
                    }
                }
                else
                {
                    foreach (var map in _goalMaps)
                        if (_mapLocations.TryGetValue(map, out var id)) _goalLocations.Add(id);
                }

                _goalLabel = JsonParser.GetString(slotData, "goal_label", "maps");

                // map_unlocks off: the seed expects every map to be playable from
                // the start, so write the missing entries once.
                if (_mapsAreDefault)
                {
                    var reply = GameState.UnlockMaps(mapIds);
                    Plugin.Logger?.LogInfo("maps unlocked by default: " + reply);
                }
            }

            // The first map must be startable in every seed, even when its item
            // never arrives. The map screen repeats this in case the profile was
            // not loaded yet when the slot data landed.
            GameState.EnsureStartingMap();

            if (slotData.TryGetValue("achievements", out var achievements) &&
                JsonParser.AsObject(achievements) is { } achMap)
                foreach (var entry in achMap)
                    _achievementLocations[entry.Key] = JsonParser.AsInt(entry.Value);

            // Curse ladder / rituals / sigils: the world ships the specs, the
            // plugin only reports which ones the save satisfies.
            EndgameChecks.Load(slotData);

            // Weapons are item-only when the seed actually ships weapon items. That
            // holds for seeds older than the `weapons_default` flag too, which is why
            // the item table is the primary signal; the flag can only turn it off.
            // The gates start on (Plugin.Load). A seed can only turn them off, and
            // only when it genuinely ships nothing to wait for - the flag, or an
            // item table with no entries of that kind on an older seed.
            if (slotData.ContainsKey("weapons_default"))
            {
                if (JsonParser.GetBool(slotData, "weapons_default")) GameState.WeaponsFromItems = false;
            }
            else if (!sawWeaponItem)
            {
                GameState.WeaponsFromItems = false;
            }

            if (slotData.ContainsKey("maps_default"))
            {
                if (JsonParser.GetBool(slotData, "maps_default")) GameState.MapsFromItems = false;
            }
            else if (!sawMapItem)
            {
                GameState.MapsFromItems = false;
            }

            GameState.CraftLicences = JsonParser.GetBool(slotData, "craft_licences");
            LocationBook.Load(slotData);
            _deathLinkEnabled = JsonParser.GetBool(slotData, "death_link");
            _knockbackLinkEnabled = JsonParser.GetBool(slotData, "knockback_link");

            summary = _itemCommands.Count + " items, " + EndgameChecks.Count + " endgame runs" +
                      (_deathLinkEnabled ? ", death link on" : "") +
                      (_knockbackLinkEnabled ? ", knockback link on" : "");
        }

        private void OnReceivedItems(Dictionary<string, object> message)
        {
            var index = JsonParser.GetInt(message, "index", 0);
            if (!message.TryGetValue("items", out var raw) || JsonParser.AsArray(raw) is not { } items) return;

            var alreadyApplied = Plugin.AppliedItems.Value;
            if (index == 0 && _pendingGrants.Count == 0)
                Log?.LogInfo($"AP: {items.Count} item(s) replayed, {alreadyApplied} already applied");

            for (var i = 0; i < items.Count; i++)
            {
                var globalIndex = index + i;
                if (globalIndex < alreadyApplied)
                {
                    // Replayed on reconnect: nothing to apply, but a map item still
                    // counts as received so its Clear check stays reportable.
                    var replayedItem = JsonParser.AsObject(items[i]);
                    if (replayedItem != null &&
                        _itemCommands.TryGetValue(JsonParser.GetInt(replayedItem, "item", -1), out var replayedCommand))
                    {
                        RememberItem(replayedCommand);
                        NoteReceived(replayedCommand, replayedItem, true);
                    }
                    continue;
                }

                var item = JsonParser.AsObject(items[i]);
                if (item == null) continue;

                var id = JsonParser.GetInt(item, "item", -1);
                if (!_itemCommands.TryGetValue(id, out var command))
                {
                    Log?.LogWarning($"AP: no mapping for item id {id}");
                    AdvanceApplied(globalIndex + 1);
                    continue;
                }

                NoteReceived(command, item);

                // Traps act on the live run, not the save, so they never queue.
                if (command.StartsWith("Trap:", StringComparison.Ordinal))
                {
                    var trapName = command.Substring("Trap:".Length);
                    if (PlayerRefs.TryGet(out var player))
                    {
                        var trapReply = Traps.Apply(trapName, player);
                        Log?.LogInfo($"AP: trap {globalIndex} '{trapName}' -> {trapReply}");
                        if (trapReply.StartsWith("OK", StringComparison.Ordinal)) Notice("Trap: " + trapName);
                    }
                    else
                    {
                        Log?.LogInfo($"AP: trap '{trapName}' arrived with no run active - ignored");
                    }

                    AdvanceApplied(globalIndex + 1);
                    continue;
                }

                var reply = GameState.Grant(command);
                if (reply.StartsWith("OK", StringComparison.Ordinal))
                {
                    RememberItem(command);
                    Log?.LogInfo($"AP: item {globalIndex} -> {command} ({reply})");
                    AdvanceApplied(globalIndex + 1);
                    _saveAt = Time.unscaledTime + 2f;
                }
                else
                {
                    // Most often the save slot is not picked yet; retried every second.
                    Log?.LogInfo($"AP: holding {command} ({reply})");
                    if (!_pendingGrants.Exists(p => p.Index == globalIndex))
                        _pendingGrants.Add(new PendingGrant { Index = globalIndex, Command = command });
                }
            }

            Plugin.Instance?.Config.Save();
        }

        // --------------------------------------------------------------- links

        private void Notice(string text) => ShowNotice(text);

        /// <summary>Surfaces a short message in the panel's status line.</summary>
        public static void ShowNotice(string text)
        {
            if (_instance == null) return;
            _instance._effectNotice = text;
            _instance._effectNoticeUntil = Time.unscaledTime + 6f;
        }

        /// <summary>Death and damage are read from the player's health component.</summary>
        private void PollLinks()
        {
            if (!_joined) return;
            if (!_deathLinkEnabled && !_knockbackLinkEnabled) return;
            if (Time.unscaledTime < _nextLinkPoll) return;
            _nextLinkPoll = Time.unscaledTime + 0.25f;

            if (!PlayerRefs.TryGet(out var entity))
            {
                _lastHealth = -1f;
                _wasDead = false;
                return;
            }

            var health = entity._health;
            if (health == null) return;

            var current = health.CurrentHealth;
            var dead = health.HasBeenKilled || current <= 0f;

            if (dead)
            {
                if (!_wasDead)
                {
                    _wasDead = true;
                    if (_deathLinkEnabled) SendBounce("DeathLink", "fell in Soulstone Survivors");
                }
            }
            else
            {
                _wasDead = false;
                if (_knockbackLinkEnabled && _lastHealth >= 0f && current < _lastHealth - 0.01f &&
                    Time.unscaledTime >= _nextKnockbackSend)
                {
                    _nextKnockbackSend = Time.unscaledTime + 5f;
                    SendBounce("KnockbackLink", "took a hit");
                }
            }

            _lastHealth = current;
        }

        private void OnBounce(Dictionary<string, object> message)
        {
            if (!message.TryGetValue("tags", out var raw) || JsonParser.AsArray(raw) is not { } tags) return;

            var death = false;
            var knockback = false;
            foreach (var tag in tags)
            {
                var text = JsonParser.AsString(tag);
                if (text == "DeathLink") death = true;
                else if (text == "KnockbackLink") knockback = true;
            }

            var source = "";
            if (message.TryGetValue("data", out var dataRaw) && JsonParser.AsObject(dataRaw) is { } data)
                source = JsonParser.GetString(data, "source", "");
            if (!string.IsNullOrEmpty(source) && source == _slotName) return;   // our own bounce

            if (death && _deathLinkEnabled) ApplyLink("death");
            if (knockback && _knockbackLinkEnabled) ApplyLink("knockback");
        }

        private void ApplyLink(string kind)
        {
            if (!PlayerRefs.TryGet(out var player))
            {
                Log?.LogInfo($"AP: {kind} link received with no run active - ignored");
                return;
            }

            string reply;
            if (kind == "death")
            {
                CursedChoice.Show(player);
                reply = "OK awaiting the player's choice";
            }
            else
            {
                reply = Knockback.Push(player, 22f);
            }

            Log?.LogInfo($"AP: {kind} link -> {reply}");
            if (reply.StartsWith("OK", StringComparison.Ordinal))
                Notice(kind == "death" ? "Death link: " + reply.Substring(3) : "Knockback link");
        }

        private void SendBounce(string tag, string cause)
        {
            var payload = new Json()
                .StartObject()
                .Name("cmd").Value("Bounce")
                .Name("tags").StartArray().Value(tag).EndArray()
                .Name("data").StartObject()
                    .Name("time").Value((int)DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                    .Name("source").Value(_slotName)
                    .Name("cause").Value(cause)
                .EndObject()
                .EndObject()
                .ToString();

            _session.Send("[" + payload + "]");
            Log?.LogInfo($"AP: sent {tag} bounce");
        }

        /// <summary>Records an item whose effect the save has to remember (maps).</summary>
        private void RememberItem(string command)
        {
            const string prefix = "Map:";
            if (command == null || !command.StartsWith(prefix, StringComparison.Ordinal)) return;
            var mapId = command.Substring(prefix.Length);
            _grantedMaps.Add(mapId);
            GameState.GrantedMaps.Add(mapId);
        }

        // -------------------------------------------------------------- checks

        private void ReportChecks()
        {
            var snapshot = GameState.Snapshot();
            if (snapshot == null) return;

            var ids = new List<int>();
            var labels = new List<string>();

            foreach (var map in snapshot.Maps)
                {
                    if (map.Completeness < 0.999f) continue;
                    if (!_mapsAreDefault && !_grantedMaps.Contains(map.Id))
                    {
                        // Cleared a map nobody sent. Staying quiet here is what
                        // keeps a self-unlocked map from paying out.
                        if (_mapLocations.TryGetValue(map.Id, out var selfUnlocked))
                            Log?.LogInfo($"AP: cleared {map.Id} without its map item - check {selfUnlocked} not sent");
                        continue;
                    }
                    if (_mapLocations.TryGetValue(map.Id, out var id) && _sentChecks.Add(id))
                    {
                        ids.Add(id);
                        labels.Add("You cleared " + Spaced(map.Id));
                    }
                }

            foreach (var achievement in snapshot.Achievements)
            {
                if (!achievement.Done) continue;
                if (_achievementLocations.TryGetValue(achievement.Id, out var id) && _sentChecks.Add(id))
                {
                    ids.Add(id);
                    labels.Add("You completed " +
                               (LocationBook.NameForId(id) ?? Spaced(achievement.Id)));
                }
            }

            var endgame = 0;
            foreach (var id in EndgameChecks.Evaluate(snapshot, _sentChecks))
            {
                if (!_sentChecks.Add(id)) continue;
                ids.Add(id);
                endgame++;
            }
            if (endgame > 0) labels.Add("You completed " + endgame + " endgame check(s)");

            if (ids.Count == 0) return;

            var json = new StringBuilder("[{\"cmd\":\"LocationChecks\",\"locations\":[");
            for (var i = 0; i < ids.Count; i++)
            {
                if (i > 0) json.Append(',');
                json.Append(ids[i]);
            }
            json.Append("]}]");

            _session.Send(json.ToString());
            foreach (var label in labels)
            {
                NoteEvent(label, "check");
                NoteFeed(label, "Check", "", "check");
            }
            ProgressVersion++;
            Log?.LogInfo($"AP: sent {ids.Count} check(s)");
        }
    }

    internal static class SoulstoneConfig
    {
        public const string GameName = "Soulstone Survivors";
    }
}
