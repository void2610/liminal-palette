using System;
using System.Globalization;
using System.Text;

namespace Void2610.LiminalPalette.Ipc.Json
{
    /// <summary>
    /// 必要十分な範囲だけを自前実装した JSON ライタ。
    /// サードパーティ依存を避けるため (Newtonsoft / Utf8Json 等を引き込まない)。
    /// 仕様: object / array / string / number / bool / null。コメント・無効エスケープ等はサポート外。
    ///
    /// 使い方:
    ///   var w = new JsonWriter();
    ///   w.BeginObject();
    ///   w.WriteString("key", "value");
    ///   w.EndObject();
    ///   var json = w.ToString();
    ///
    /// 使用側責任:
    ///   - Begin/End の対応はコンシューマが担保する (validation はしない)。
    ///   - object 内の連続 key/value 間は自動でカンマを挿入する。
    /// </summary>
    public sealed class JsonWriter
    {
        private readonly StringBuilder _sb = new StringBuilder(256);
        // 各ネストごとに「最初の要素を書いたか」を保持。true なら次の要素の前にカンマを打つ。
        private readonly System.Collections.Generic.Stack<bool> _hasItem = new System.Collections.Generic.Stack<bool>();
        // 各ネストが object か array かのフラグ。object は key を要求する。
        private readonly System.Collections.Generic.Stack<bool> _isObject = new System.Collections.Generic.Stack<bool>();

        public void BeginObject() => Begin('{', isObject: true);
        public void EndObject() => End('}');
        public void BeginArray() => Begin('[', isObject: false);
        public void EndArray() => End(']');

        /// <summary>object 内で key 付きの object を開始する (例: "outer": {)。</summary>
        public void BeginObject(string key)
        {
            WriteKey(key);
            _sb.Append('{');
            _hasItem.Push(false);
            _isObject.Push(true);
        }

        /// <summary>object 内で key 付きの array を開始する (例: "list": [)。</summary>
        public void BeginArray(string key)
        {
            WriteKey(key);
            _sb.Append('[');
            _hasItem.Push(false);
            _isObject.Push(false);
        }

        // object 内の "key": value 形式を書く。
        public void WriteString(string key, string value)
        {
            WriteKey(key);
            AppendString(value);
        }

        public void WriteNumber(string key, long value)
        {
            WriteKey(key);
            _sb.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        public void WriteNumber(string key, double value)
        {
            WriteKey(key);
            // double.ToString("R") はロケール依存しない full precision。
            _sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        public void WriteBool(string key, bool value)
        {
            WriteKey(key);
            _sb.Append(value ? "true" : "false");
        }

        public void WriteNull(string key)
        {
            WriteKey(key);
            _sb.Append("null");
        }

        // array 要素 (key 無し)。
        public void WriteString(string value)
        {
            WriteSeparator();
            AppendString(value);
        }

        public void WriteNumber(long value)
        {
            WriteSeparator();
            _sb.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        public void WriteNumber(double value)
        {
            WriteSeparator();
            _sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        public void WriteBool(bool value)
        {
            WriteSeparator();
            _sb.Append(value ? "true" : "false");
        }

        public void WriteNull()
        {
            WriteSeparator();
            _sb.Append("null");
        }

        public override string ToString() => _sb.ToString();

        // ---- internals ----

        private void Begin(char ch, bool isObject)
        {
            WriteSeparator();
            _sb.Append(ch);
            _hasItem.Push(false);
            _isObject.Push(isObject);
        }

        private void End(char ch)
        {
            _hasItem.Pop();
            _isObject.Pop();
            _sb.Append(ch);
            // 親側の hasItem を true にする (今閉じた集合は親集合の 1 要素として既に書かれた)。
            if (_hasItem.Count > 0)
            {
                _hasItem.Pop();
                _hasItem.Push(true);
            }
        }

        private void WriteKey(string key)
        {
            WriteSeparator();
            AppendString(key);
            _sb.Append(':');
        }

        // 直前の要素との間にカンマを打つ。最初の要素なら何もしない。
        private void WriteSeparator()
        {
            if (_hasItem.Count == 0) return; // ルート要素 (1 つだけ書く想定)。
            var hasItem = _hasItem.Pop();
            if (hasItem) _sb.Append(',');
            _hasItem.Push(true);
        }

        // 文字列を JSON エスケープしつつ書く。
        private void AppendString(string s)
        {
            if (s == null) { _sb.Append("null"); return; }
            _sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': _sb.Append("\\\""); break;
                    case '\\': _sb.Append("\\\\"); break;
                    case '\b': _sb.Append("\\b"); break;
                    case '\f': _sb.Append("\\f"); break;
                    case '\n': _sb.Append("\\n"); break;
                    case '\r': _sb.Append("\\r"); break;
                    case '\t': _sb.Append("\\t"); break;
                    default:
                        // 制御文字 (U+0000 〜 U+001F) は \u00XX で出す。それ以外はそのまま (UTF-8 でエンコードされる)。
                        if (c < 0x20)
                        {
                            _sb.Append("\\u");
                            _sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            _sb.Append(c);
                        }
                        break;
                }
            }
            _sb.Append('"');
        }
    }
}
