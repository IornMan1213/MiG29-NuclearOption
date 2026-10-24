using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MiG29Tools
{
    /// <summary>
    /// A small JSON reader shared by the builder and the instruments plugin: objects -> Dictionary&lt;string, object&gt;, arrays ->
    /// List&lt;object&gt;, numbers -> double, strings, true / false / null. JsonUtility cannot read nested arrays (the cockpit groups'
    /// frames) and came back empty for the plugin's own types in game. Throws FormatException on malformed input.
    /// </summary>
    public sealed class MiG29Json
    {
        readonly string s; int i;
        MiG29Json(string text) { s = text ?? ""; }

        public static object Parse(string text)
        {
            var j = new MiG29Json(text);
            var v = j.Value();
            j.Ws();
            if (j.i < j.s.Length) throw new FormatException($"JSON: unexpected '{j.s[j.i]}' at {j.i}");
            return v;
        }

        void Ws() { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
        char Peek() { Ws(); if (i >= s.Length) throw new FormatException("JSON: unexpected end"); return s[i]; }
        void Expect(char c) { if (Peek() != c) throw new FormatException($"JSON: expected '{c}' at {i}"); i++; }

        object Value()
        {
            char c = Peek();
            if (c == '{')
            {
                i++; var d = new Dictionary<string, object>();
                if (Peek() == '}') { i++; return d; }
                while (true)
                {
                    if (Peek() != '"') throw new FormatException($"JSON: expected a key at {i}");
                    string k = Str(); Expect(':'); d[k] = Value();
                    char n = Peek(); i++;
                    if (n == ',') continue;
                    if (n == '}') return d;
                    throw new FormatException($"JSON: expected ',' or '}}' at {i - 1}");
                }
            }
            if (c == '[')
            {
                i++; var l = new List<object>();
                if (Peek() == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value());
                    char n = Peek(); i++;
                    if (n == ',') continue;
                    if (n == ']') return l;
                    throw new FormatException($"JSON: expected ',' or ']' at {i - 1}");
                }
            }
            if (c == '"') return Str();
            if (Match("true")) return true;
            if (Match("false")) return false;
            if (Match("null")) return null;
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (i == st) throw new FormatException($"JSON: unexpected '{c}' at {i}");
            return double.Parse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        bool Match(string w) { if (string.CompareOrdinal(s, i, w, 0, w.Length) == 0) { i += w.Length; return true; } return false; }

        string Str()
        {
            i++;   // opening quote
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new FormatException("JSON: unterminated string");
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
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
        }

        // ---- typed access (missing or wrong-typed values give the default)
        public static Dictionary<string, object> Obj(object o, string k) => o is Dictionary<string, object> d && d.TryGetValue(k, out var v) ? v as Dictionary<string, object> : null;
        public static List<object> Arr(object o, string k) => o is Dictionary<string, object> d && d.TryGetValue(k, out var v) && v is List<object> l ? l : new List<object>();
        public static string Str(object o, string k, string def = "") => o is Dictionary<string, object> d && d.TryGetValue(k, out var v) && v is string x ? x : def;
        public static float Num(object o, string k, float def = 0f) => o is Dictionary<string, object> d && d.TryGetValue(k, out var v) && v is double n ? (float)n : def;
        public static bool Bool(object o, string k, bool def = false) => o is Dictionary<string, object> d && d.TryGetValue(k, out var v) && v is bool b ? b : def;
        public static float[] Floats(object o, string k)
        {
            var l = Arr(o, k); var r = new float[l.Count];
            for (int n = 0; n < l.Count; n++) r[n] = l[n] is double x ? (float)x : 0f;
            return r;
        }
        public static float[] Floats(object list)
        {
            if (!(list is List<object> l)) return new float[0];
            var r = new float[l.Count];
            for (int n = 0; n < l.Count; n++) r[n] = l[n] is double x ? (float)x : 0f;
            return r;
        }
        public static int[] Ints(object o, string k)
        {
            var l = Arr(o, k); var r = new int[l.Count];
            for (int n = 0; n < l.Count; n++) r[n] = l[n] is double x ? (int)x : 0;
            return r;
        }
    }
}
