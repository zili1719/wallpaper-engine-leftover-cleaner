// Valve KeyValues (ACF / VDF) 解析器 — C# 5 兼容
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WeCleaner
{
    /// <summary>Valve KeyValues 节点。Value 为 null 表示该节点是块（花括号体）。</summary>
    public sealed class KvNode
    {
        public string Key;
        public string Value;
        public List<KvNode> Children;

        public KvNode()
        {
            Children = new List<KvNode>();
        }

        public KvNode(string key)
        {
            Key = key;
            Children = new List<KvNode>();
        }

        public bool IsBlock()
        {
            return Value == null;
        }

        public KvNode First(string key)
        {
            for (int i = 0; i < Children.Count; i++)
            {
                if (string.Equals(Children[i].Key, key, StringComparison.OrdinalIgnoreCase))
                    return Children[i];
            }
            return null;
        }

        public List<KvNode> All(string key)
        {
            List<KvNode> r = new List<KvNode>();
            for (int i = 0; i < Children.Count; i++)
            {
                if (string.Equals(Children[i].Key, key, StringComparison.OrdinalIgnoreCase))
                    r.Add(Children[i]);
            }
            return r;
        }

        public string Str(string key, string def)
        {
            KvNode n = First(key);
            if (n == null || n.Value == null) return def;
            return n.Value;
        }

        public string Str(string key)
        {
            return Str(key, null);
        }

        public long Long(string key, long def)
        {
            string v = Str(key);
            long x;
            if (v != null && long.TryParse(v.Trim(), out x)) return x;
            return def;
        }
    }

    internal sealed class KvLexer
    {
        private readonly string _s;
        private int _i;

        public KvLexer(string text)
        {
            _s = text ?? string.Empty;
            _i = 0;
        }

        private void SkipWs()
        {
            while (_i < _s.Length)
            {
                char c = _s[_i];
                if (char.IsWhiteSpace(c) || c == '\uFEFF')
                {
                    _i++;
                    continue;
                }
                if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '/')
                {
                    while (_i < _s.Length && _s[_i] != '\n') _i++;
                    continue;
                }
                if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '*')
                {
                    _i += 2;
                    while (_i + 1 < _s.Length && !(_s[_i] == '*' && _s[_i + 1] == '/')) _i++;
                    _i += 2;
                    continue;
                }
                break;
            }
        }

        /// <summary>读下一个 token。返回 null 表示流结束。isBrace 为 true 时返回值为 "{" 或 "}"。</summary>
        public string Next(out bool isBrace)
        {
            isBrace = false;
            SkipWs();
            if (_i >= _s.Length) return null;

            char c = _s[_i];
            if (c == '{' || c == '}')
            {
                isBrace = true;
                _i++;
                return c.ToString();
            }

            if (c == '"')
            {
                _i++;
                StringBuilder sb = new StringBuilder();
                while (_i < _s.Length)
                {
                    char ch = _s[_i];
                    if (ch == '\\' && _i + 1 < _s.Length)
                    {
                        _i++;
                        char e = _s[_i];
                        if (e == 'n') sb.Append('\n');
                        else if (e == 't') sb.Append('\t');
                        else if (e == 'r') sb.Append('\r');
                        else sb.Append(e);
                        _i++;
                        continue;
                    }
                    if (ch == '"')
                    {
                        _i++;
                        break;
                    }
                    sb.Append(ch);
                    _i++;
                }
                return sb.ToString();
            }

            StringBuilder w = new StringBuilder();
            while (_i < _s.Length)
            {
                char ch = _s[_i];
                if (char.IsWhiteSpace(ch) || ch == '{' || ch == '}' || ch == '"') break;
                w.Append(ch);
                _i++;
            }
            if (w.Length == 0)
            {
                _i++;
                return Next(out isBrace);
            }
            return w.ToString();
        }
    }

    public static class Kv
    {
        public static KvNode ParseFile(string path)
        {
            string text = File.ReadAllText(path, DetectEncoding(path));
            return Parse(text);
        }

        public static Encoding DetectEncoding(string path)
        {
            byte[] head = new byte[4];
            int n;
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                n = fs.Read(head, 0, 4);
            }
            if (n >= 2 && head[0] == 0xFF && head[1] == 0xFE) return Encoding.Unicode;
            if (n >= 2 && head[0] == 0xFE && head[1] == 0xFF) return Encoding.BigEndianUnicode;
            if (n >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF) return Encoding.UTF8;
            return new UTF8Encoding(false);
        }

        public static KvNode Parse(string text)
        {
            KvLexer lx = new KvLexer(text);
            KvNode root = new KvNode("__root__");
            ParseList(lx, root);
            return root;
        }

        private static void ParseList(KvLexer lx, KvNode parent)
        {
            while (true)
            {
                bool b1;
                string key = lx.Next(out b1);
                if (key == null) return;

                if (b1)
                {
                    if (key == "}") return;
                    continue; // 多余的 '{'，跳过
                }

                // 条件键：[$WIN32] 等平台标记，直接丢弃
                if (key.Length > 0 && key[0] == '[')
                {
                    bool skipBrace;
                    string peek = lx.Next(out skipBrace);
                    if (peek == null) return;
                    if (skipBrace && peek == "{")
                    {
                        KvNode junk = new KvNode(key);
                        ParseList(lx, junk);
                    }
                    else
                    {
                        bool b2x;
                        string ignored = lx.Next(out b2x);
                        if (b2x && ignored == "{")
                        {
                            KvNode junk2 = new KvNode(key);
                            ParseList(lx, junk2);
                        }
                    }
                    continue;
                }

                bool b2;
                string val = lx.Next(out b2);
                if (val == null) return;

                if (b2 && val == "{")
                {
                    KvNode node = new KvNode(key);
                    ParseList(lx, node);
                    parent.Children.Add(node);
                }
                else if (b2 && val == "}")
                {
                    KvNode node2 = new KvNode(key);
                    parent.Children.Add(node2);
                    return;
                }
                else
                {
                    KvNode leaf = new KvNode(key);
                    leaf.Value = val;
                    parent.Children.Add(leaf);
                }
            }
        }
    }
}
