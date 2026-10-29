using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SoulstoneAP
{
    /// <summary>
    /// Tiny JSON writer. The plugin has no JSON dependency (the game ships its
    /// own IL2CPP-side serializer which is not usable from managed code).
    /// </summary>
    internal sealed class Json
    {
        private readonly StringBuilder _sb = new StringBuilder();
        private readonly Stack<bool> _first = new Stack<bool>();
        private bool _needComma;

        public static string Str(string value)
        {
            if (value == null) return "null";
            var sb = new StringBuilder(value.Length + 8);
            sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static string Num(float value) =>
            float.IsNaN(value) || float.IsInfinity(value)
                ? "0"
                : value.ToString("0.####", CultureInfo.InvariantCulture);

        private void Comma()
        {
            if (_needComma) _sb.Append(',');
            _needComma = true;
        }

        public Json StartObject() { Comma(); _sb.Append('{'); _needComma = false; return this; }
        public Json EndObject() { _sb.Append('}'); _needComma = true; return this; }
        public Json StartArray() { Comma(); _sb.Append('['); _needComma = false; return this; }
        public Json EndArray() { _sb.Append(']'); _needComma = true; return this; }

        public Json Name(string name)
        {
            Comma();
            _sb.Append(Str(name)).Append(':');
            _needComma = false;
            return this;
        }

        public Json Raw(string json)
        {
            Comma();
            _sb.Append(json);
            _needComma = true;
            return this;
        }

        public Json Value(string value) => Raw(Str(value));
        public Json Value(int value) => Raw(value.ToString(CultureInfo.InvariantCulture));
        public Json Value(float value) => Raw(Num(value));
        public Json Value(bool value) => Raw(value ? "true" : "false");

        public override string ToString() => _sb.ToString();
    }
}
