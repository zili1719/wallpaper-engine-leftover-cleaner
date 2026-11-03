// 极简 JSON 解析/序列化 — 不依赖 System.Web.Extensions，C# 5 兼容
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace WeCleaner
{
    public static class Json
    {
        public static object ParseFile(string path)
        {
            string text = File.ReadAllText(path, Kv.DetectEncoding(path));
            return Parse(text);
        }

        public static object Parse(string text)
        {
            int i = 0;
            object v = ParseValue(text, ref i);
            SkipWs(text, ref i);
            return v;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '\uFEFF' || char.IsWhiteSpace(c)) { i++; continue; }
                break;
            }
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i);
            if (c == '[') return ParseArray(s, ref i);
            if (c == '"') return ParseString(s, ref i);
            if (c == 't') { i += 4; return true; }
            if (c == 'f') { i += 5; return false; }
            if (c == 'n') { i += 4; return null; }
            return ParseNumber(s, ref i);
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            Dictionary<string, object> d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            i++; // {
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length) break;
                if (s[i] == '}') { i++; break; }
                if (s[i] == ',') { i++; continue; }
                string k = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ':') i++;
                object v = ParseValue(s, ref i);
                if (k != null) d[k] = v;
            }
            return d;
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            List<object> a = new List<object>();
            i++; // [
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length) break;
                if (s[i] == ']') { i++; break; }
                if (s[i] == ',') { i++; continue; }
                a.Add(ParseValue(s, ref i));
            }
            return a;
        }

        private static string ParseString(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length || s[i] != '"') return null;
            i++;
            StringBuilder sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    i++;
                    char e = s[i];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (i + 4 < s.Length)
                            {
                                int code;
                                if (int.TryParse(s.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                {
                                    sb.Append((char)code);
                                    i += 4;
                                }
                            }
                            break;
                        default: sb.Append(e); break;
                    }
                    i++;
                    continue;
                }
                if (c == '"') { i++; break; }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        private static object ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length)
            {
                char c = s[i];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') i++;
                else break;
            }
            if (i == start)
            {
                i++;
                return null;
            }
            string t = s.Substring(start, i - start);
            double d;
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            long l;
            if (long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) return (double)l;
            return t;
        }

        // ---------- 取值助手 ----------

        public static Dictionary<string, object> Obj(object o)
        {
            return o as Dictionary<string, object>;
        }

        public static List<object> Arr(object o)
        {
            return o as List<object>;
        }

        public static string GetStr(IDictionary<string, object> d, string key, string def)
        {
            if (d == null) return def;
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return def;
            string s = v as string;
            if (s != null) return s;
            if (v is bool) return ((bool)v) ? "true" : "false";
            if (v is double) return ((double)v).ToString(CultureInfo.InvariantCulture);
            return v.ToString();
        }

        public static bool GetBool(IDictionary<string, object> d, string key, bool def)
        {
            if (d == null) return def;
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return def;
            if (v is bool) return (bool)v;
            if (v is double) return ((double)v) != 0;
            string s = v as string;
            if (s != null) return string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);
            return def;
        }

        // ---------- 序列化 ----------

        public static void WriteIndented(StringBuilder sb, object value, int indent)
        {
            if (value == null) { sb.Append("null"); return; }
            string str = value as string;
            if (str != null) { WriteString(sb, str); return; }
            if (value is bool) { sb.Append(((bool)value) ? "true" : "false"); return; }
            if (value is double)
            {
                double d = (double)value;
                if (Math.Abs(d - Math.Round(d)) < 0.0000001 && Math.Abs(d) < 1e15)
                    sb.Append(((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture));
                else
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (value is long) { sb.Append(((long)value).ToString(CultureInfo.InvariantCulture)); return; }
            if (value is int) { sb.Append(((int)value).ToString(CultureInfo.InvariantCulture)); return; }

            IDictionary<string, object> dict = value as IDictionary<string, object>;
            if (dict != null)
            {
                if (dict.Count == 0) { sb.Append("{}"); return; }
                sb.Append('{');
                bool first = true;
                foreach (KeyValuePair<string, object> kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    NewLine(sb, indent + 1);
                    WriteString(sb, kv.Key);
                    sb.Append(": ");
                    WriteIndented(sb, kv.Value, indent + 1);
                }
                NewLine(sb, indent);
                sb.Append('}');
                return;
            }

            IEnumerable list = value as IEnumerable;
            if (list != null)
            {
                List<object> items = new List<object>();
                foreach (object o in list) items.Add(o);
                if (items.Count == 0) { sb.Append("[]"); return; }
                sb.Append('[');
                for (int k = 0; k < items.Count; k++)
                {
                    if (k > 0) sb.Append(',');
                    NewLine(sb, indent + 1);
                    WriteIndented(sb, items[k], indent + 1);
                }
                NewLine(sb, indent);
                sb.Append(']');
                return;
            }

            WriteString(sb, value.ToString());
        }

        private static void NewLine(StringBuilder sb, int indent)
        {
            sb.Append('\n');
            for (int i = 0; i < indent; i++) sb.Append("  ");
        }

        public static void WriteString(StringBuilder sb, string s)
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

        public static string Serialize(object value)
        {
            StringBuilder sb = new StringBuilder();
            WriteIndented(sb, value, 0);
            return sb.ToString();
        }
    }
}
