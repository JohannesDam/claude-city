using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ClaudeCityBridge
{
    /// <summary>Tiny JSON reader/writer so the mod has no third-party dependencies.</summary>
    public static class Json
    {
        // ---------- parsing ----------
        public static object Parse(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return new Dictionary<string, object>();
            int i = 0;
            var v = ParseValue(s, ref i);
            return v;
        }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object ParseValue(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end of json");
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>();
                i++; Ws(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i);
                    var k = ParseString(s, ref i);
                    Ws(s, ref i);
                    if (s[i] != ':') throw new FormatException("expected : at " + i);
                    i++;
                    d[k] = ParseValue(s, ref i);
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException("expected , or } at " + i);
                }
            }
            if (c == '[')
            {
                var l = new List<object>();
                i++; Ws(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(ParseValue(s, ref i));
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new FormatException("expected , or ] at " + i);
                }
            }
            if (c == '"') return ParseString(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (st == i) throw new FormatException("bad json value at " + i);
            return double.Parse(s.Substring(st, i - st), CultureInfo.InvariantCulture);
        }

        static string ParseString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("expected string at " + i);
            i++;
            var sb = new StringBuilder();
            while (s[i] != '"')
            {
                char c = s[i++];
                if (c == '\\')
                {
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            i++;
            return sb.ToString();
        }

        // ---------- writing ----------
        public static string Write(object o)
        {
            var sb = new StringBuilder();
            WriteValue(sb, o);
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object o)
        {
            switch (o)
            {
                case null: sb.Append("null"); return;
                case string s: WriteString(sb, s); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case float f: WriteNum(sb, f); return;
                case double d: WriteNum(sb, d); return;
                case int n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); return;
                case long n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); return;
                case uint n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); return;
                case ulong n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); return;
                case short n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); return;
                case byte n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); return;
                case Enum e: WriteString(sb, e.ToString()); return;
                case IDictionary dict:
                    {
                        sb.Append('{');
                        bool first = true;
                        foreach (DictionaryEntry kv in dict)
                        {
                            if (!first) sb.Append(',');
                            first = false;
                            WriteString(sb, kv.Key.ToString());
                            sb.Append(':');
                            WriteValue(sb, kv.Value);
                        }
                        sb.Append('}');
                        return;
                    }
                case IEnumerable en:
                    {
                        sb.Append('[');
                        bool first = true;
                        foreach (var x in en)
                        {
                            if (!first) sb.Append(',');
                            first = false;
                            WriteValue(sb, x);
                        }
                        sb.Append(']');
                        return;
                    }
                default: WriteString(sb, o.ToString()); return;
            }
        }

        static void WriteNum(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("null"); return; }
            sb.Append(Math.Round(d, 3).ToString("0.###", CultureInfo.InvariantCulture));
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
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
        }
    }

    /// <summary>Convenience accessors for parsed request bodies.</summary>
    public class Args
    {
        readonly Dictionary<string, object> d;
        public Args(object o) { d = o as Dictionary<string, object> ?? new Dictionary<string, object>(); }
        public Dictionary<string, object> RawDict => d;
        public bool Has(string k) => d.ContainsKey(k) && d[k] != null;
        public object Raw(string k) => d.TryGetValue(k, out var v) ? v : null;
        public string Str(string k, string def = null) => d.TryGetValue(k, out var v) && v != null ? v.ToString() : def;
        public double Num(string k, double def = 0) => d.TryGetValue(k, out var v) && v is double x ? x : (v is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ? y : def);
        public float F(string k, float def = 0) => (float)Num(k, def);
        public int I(string k, int def = 0) => (int)Math.Round(Num(k, def));
        public bool B(string k, bool def = false) => d.TryGetValue(k, out var v) && v is bool b ? b : (v is string s ? s == "true" || s == "1" : def);
        public List<object> List(string k) => d.TryGetValue(k, out var v) ? v as List<object> : null;
    }
}
