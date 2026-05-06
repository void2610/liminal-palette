using System;
using System.Globalization;
using System.Text;

namespace Void2610.LiminalPalette.Ipc.Json
{
    public enum JsonToken
    {
        None,
        BeginObject,
        EndObject,
        BeginArray,
        EndArray,
        PropertyName,
        String,
        Number,
        True,
        False,
        Null,
        EndOfStream,
    }

    /// <summary>
    /// Phase 4 で必要な範囲だけを自前実装した JSON パーサ。
    /// Read() でストリーム的にトークンを返す。Deserialize&lt;T&gt; のような汎用化はしない (各 endpoint が手動で組み立てる)。
    /// 仕様: object / array / string / number / bool / null。コメント・トレーリングカンマはサポート外。
    /// 数値表現は IEEE 754 double として読む。整数も double に変換 (53 bit までは安全)。
    /// </summary>
    public sealed class JsonReader
    {
        private readonly string _src;
        private int _pos;
        // object 内で次に来るのが key かを区別する。array 内では常に value。
        // スタックは現在の集合が object なら true。
        private readonly System.Collections.Generic.Stack<bool> _isObject = new System.Collections.Generic.Stack<bool>();
        // object 内で次は key を期待しているか (true なら key、false なら value)。
        private bool _expectKey;

        public JsonToken TokenType { get; private set; } = JsonToken.None;
        public string StringValue { get; private set; }
        public double NumberValue { get; private set; }

        public JsonReader(string source)
        {
            _src = source ?? throw new ArgumentNullException(nameof(source));
            _pos = 0;
        }

        /// <summary>次のトークンを読む。EOS なら EndOfStream を返す。</summary>
        public JsonToken Read()
        {
            SkipWhitespace();
            if (_pos >= _src.Length)
            {
                TokenType = JsonToken.EndOfStream;
                return TokenType;
            }

            // object 内では各 entry の前にカンマ、key の後にコロンが要る。
            // ここで同時に「次は何を期待しているか」も整合させる。
            if (_isObject.Count > 0 && _isObject.Peek())
            {
                // object 内
                if (_expectKey && _src[_pos] != '}')
                {
                    return ReadKey();
                }
            }
            // array 内 / object の value 位置 / ルート は通常の value 読み取り。
            return ReadValueOrStructural();
        }

        // ---- internals ----

        private JsonToken ReadKey()
        {
            if (_src[_pos] != '"')
                throw new FormatException($"Expected '\"' for property name at {_pos}, got '{_src[_pos]}'.");
            var s = ReadString();
            SkipWhitespace();
            if (_pos >= _src.Length || _src[_pos] != ':')
                throw new FormatException($"Expected ':' after property name at {_pos}.");
            _pos++; // skip ':'
            StringValue = s;
            TokenType = JsonToken.PropertyName;
            _expectKey = false;
            return TokenType;
        }

        private JsonToken ReadValueOrStructural()
        {
            var c = _src[_pos];
            switch (c)
            {
                case '{':
                    _pos++;
                    _isObject.Push(true);
                    _expectKey = true;
                    TokenType = JsonToken.BeginObject;
                    return TokenType;
                case '}':
                    _pos++;
                    if (_isObject.Count == 0 || !_isObject.Pop())
                        throw new FormatException($"Unexpected '}}' at {_pos - 1}.");
                    AfterValue();
                    TokenType = JsonToken.EndObject;
                    return TokenType;
                case '[':
                    _pos++;
                    _isObject.Push(false);
                    _expectKey = false;
                    TokenType = JsonToken.BeginArray;
                    return TokenType;
                case ']':
                    _pos++;
                    if (_isObject.Count == 0 || _isObject.Pop())
                        throw new FormatException($"Unexpected ']' at {_pos - 1}.");
                    AfterValue();
                    TokenType = JsonToken.EndArray;
                    return TokenType;
                case '"':
                    var s = ReadString();
                    StringValue = s;
                    TokenType = JsonToken.String;
                    AfterValue();
                    return TokenType;
                case 't':
                case 'f':
                case 'n':
                    return ReadLiteral();
                default:
                    if (c == '-' || (c >= '0' && c <= '9'))
                        return ReadNumber();
                    throw new FormatException($"Unexpected char '{c}' at {_pos}.");
            }
        }

        // value 読み取り後に次のセパレータ (, または閉じ括弧) を吸収して、必要なら次の expectKey を立てる。
        private void AfterValue()
        {
            SkipWhitespace();
            if (_pos >= _src.Length) return;
            var c = _src[_pos];
            if (c == ',')
            {
                _pos++;
                if (_isObject.Count > 0 && _isObject.Peek())
                    _expectKey = true; // object なら次は key を要求
                else
                    _expectKey = false;
            }
            // 閉じ括弧の場合は次の Read() で End* が返るのでここでは何もしない。
        }

        private string ReadString()
        {
            // 入口は '"'。
            _pos++;
            var sb = new StringBuilder();
            while (_pos < _src.Length)
            {
                var c = _src[_pos++];
                if (c == '"') return sb.ToString();
                if (c == '\\')
                {
                    if (_pos >= _src.Length) throw new FormatException("Unterminated escape.");
                    var esc = _src[_pos++];
                    switch (esc)
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
                            if (_pos + 4 > _src.Length) throw new FormatException("Truncated \\u escape.");
                            var hex = _src.Substring(_pos, 4);
                            _pos += 4;
                            sb.Append((char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            break;
                        default: throw new FormatException($"Invalid escape \\{esc} at {_pos - 1}.");
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            throw new FormatException("Unterminated string.");
        }

        private JsonToken ReadNumber()
        {
            var start = _pos;
            if (_src[_pos] == '-') _pos++;
            while (_pos < _src.Length && IsNumberChar(_src[_pos])) _pos++;
            var s = _src.Substring(start, _pos - start);
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                throw new FormatException($"Invalid number '{s}' at {start}.");
            NumberValue = v;
            TokenType = JsonToken.Number;
            AfterValue();
            return TokenType;
        }

        private static bool IsNumberChar(char c)
            => (c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-';

        private JsonToken ReadLiteral()
        {
            if (Match("true")) { TokenType = JsonToken.True; AfterValue(); return TokenType; }
            if (Match("false")) { TokenType = JsonToken.False; AfterValue(); return TokenType; }
            if (Match("null")) { TokenType = JsonToken.Null; AfterValue(); return TokenType; }
            throw new FormatException($"Invalid literal at {_pos}.");
        }

        private bool Match(string keyword)
        {
            if (_pos + keyword.Length > _src.Length) return false;
            for (var i = 0; i < keyword.Length; i++)
                if (_src[_pos + i] != keyword[i]) return false;
            _pos += keyword.Length;
            return true;
        }

        private void SkipWhitespace()
        {
            while (_pos < _src.Length)
            {
                var c = _src[_pos];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') _pos++;
                else break;
            }
        }
    }
}
