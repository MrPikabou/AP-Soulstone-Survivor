using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SoulstoneAP
{
    /// <summary>
    /// Minimal JSON reader for Archipelago server messages. Produces
    /// Dictionary&lt;string, object&gt;, List&lt;object&gt;, string, double, bool and null.
    /// </summary>
    internal static class JsonParser
    {
        public static object Parse(string text)
        {
            var index = 0;
            var value = ParseValue(text, ref index);
            SkipWhitespace(text, ref index);
            return value;
        }

        public static Dictionary<string, object> ParseObject(string text)
        {
            return Parse(text) as Dictionary<string, object>;
        }

        private static void SkipWhitespace(string text, ref int index)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
        }

        private static object ParseValue(string text, ref int index)
        {
            SkipWhitespace(text, ref index);
            if (index >= text.Length) throw new FormatException("unexpected end of json");

            var c = text[index];
            switch (c)
            {
                case '{': return ParseObjectValue(text, ref index);
                case '[': return ParseArray(text, ref index);
                case '"': return ParseString(text, ref index);
                case 't': Expect(text, ref index, "true"); return true;
                case 'f': Expect(text, ref index, "false"); return false;
                case 'n': Expect(text, ref index, "null"); return null;
                default: return ParseNumber(text, ref index);
            }
        }

        private static void Expect(string text, ref int index, string literal)
        {
            if (string.CompareOrdinal(text, index, literal, 0, literal.Length) != 0)
                throw new FormatException("expected " + literal);
            index += literal.Length;
        }

        private static Dictionary<string, object> ParseObjectValue(string text, ref int index)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            index++; // '{'
            SkipWhitespace(text, ref index);
            if (index < text.Length && text[index] == '}') { index++; return result; }

            while (true)
            {
                SkipWhitespace(text, ref index);
                var key = ParseString(text, ref index);
                SkipWhitespace(text, ref index);
                if (index >= text.Length || text[index] != ':') throw new FormatException("expected ':'");
                index++;
                result[key] = ParseValue(text, ref index);
                SkipWhitespace(text, ref index);
                if (index >= text.Length) throw new FormatException("unterminated object");
                if (text[index] == ',') { index++; continue; }
                if (text[index] == '}') { index++; return result; }
                throw new FormatException("expected ',' or '}'");
            }
        }

        private static List<object> ParseArray(string text, ref int index)
        {
            var result = new List<object>();
            index++; // '['
            SkipWhitespace(text, ref index);
            if (index < text.Length && text[index] == ']') { index++; return result; }

            while (true)
            {
                result.Add(ParseValue(text, ref index));
                SkipWhitespace(text, ref index);
                if (index >= text.Length) throw new FormatException("unterminated array");
                if (text[index] == ',') { index++; continue; }
                if (text[index] == ']') { index++; return result; }
                throw new FormatException("expected ',' or ']'");
            }
        }

        private static string ParseString(string text, ref int index)
        {
            if (text[index] != '"') throw new FormatException("expected string");
            index++;
            var sb = new StringBuilder();
            while (index < text.Length)
            {
                var c = text[index++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }

                if (index >= text.Length) break;
                var escape = text[index++];
                switch (escape)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (index + 4 <= text.Length)
                        {
                            sb.Append((char)int.Parse(text.Substring(index, 4), NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture));
                            index += 4;
                        }
                        break;
                    default: sb.Append(escape); break;
                }
            }
            throw new FormatException("unterminated string");
        }

        private static object ParseNumber(string text, ref int index)
        {
            var start = index;
            while (index < text.Length && (char.IsDigit(text[index]) || "+-.eE".IndexOf(text[index]) >= 0)) index++;
            var slice = text.Substring(start, index - start);
            if (double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
            throw new FormatException("bad number '" + slice + "'");
        }

        // ---- helpers for reading values out of the parsed graph

        public static Dictionary<string, object> AsObject(object value) => value as Dictionary<string, object>;
        public static List<object> AsArray(object value) => value as List<object>;

        public static string AsString(object value) => value as string;

        public static int AsInt(object value, int fallback = 0)
        {
            if (value is double d) return (int)d;
            if (value is string s && int.TryParse(s, out var parsed)) return parsed;
            return fallback;
        }

        public static string GetString(Dictionary<string, object> obj, string key, string fallback = null)
        {
            return obj != null && obj.TryGetValue(key, out var value) ? AsString(value) ?? fallback : fallback;
        }

        public static int GetInt(Dictionary<string, object> obj, string key, int fallback = 0)
        {
            return obj != null && obj.TryGetValue(key, out var value) ? AsInt(value, fallback) : fallback;
        }

        public static bool GetBool(Dictionary<string, object> obj, string key, bool fallback = false)
        {
            if (obj == null || !obj.TryGetValue(key, out var value)) return fallback;
            if (value is bool flag) return flag;
            if (value is string text && bool.TryParse(text, out var parsed)) return parsed;
            if (value is double number) return number != 0;
            return fallback;
        }
    }
}
