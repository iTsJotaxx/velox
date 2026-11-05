using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Velox.Core.Drivers
{
    /// <summary>
    /// Analizador JSON mínimo (sin dependencias). Devuelve Dictionary&lt;string, object&gt;, List&lt;object&gt;, string, double, bool o null.
    /// Lanza FormatException si el texto no es JSON válido o es demasiado profundo.
    /// </summary>
    internal sealed class MiniJson
    {
        const int MaxDepth = 32;
        readonly string _s;
        int _i;

        MiniJson(string s) { _s = s; }

        internal static object Parse(string json)
        {
            if (json == null) throw new FormatException("JSON is null");
            var p = new MiniJson(json);
            p.Skip();
            object v = p.Value(0);
            p.Skip();
            if (p._i != p._s.Length) throw p.Err("Unexpected trailing data");
            return v;
        }

        FormatException Err(string m) => new FormatException(m + " at " + _i);

        void Skip()
        {
            while (_i < _s.Length && (_s[_i] == ' ' || _s[_i] == '\t' || _s[_i] == '\r' || _s[_i] == '\n' || _s[_i] == '﻿')) _i++;
        }

        object Value(int depth)
        {
            if (depth > MaxDepth) throw Err("Too deep");
            if (_i >= _s.Length) throw Err("Unexpected end");
            char c = _s[_i];
            if (c == '{') return Obj(depth);
            if (c == '[') return Arr(depth);
            if (c == '"') return Str();
            if (Lit("true")) return true;
            if (Lit("false")) return false;
            if (Lit("null")) return null;
            return Num();
        }

        bool Lit(string w)
        {
            if (string.CompareOrdinal(_s, _i, w, 0, w.Length) != 0) return false;
            _i += w.Length;
            return true;
        }

        Dictionary<string, object> Obj(int depth)
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal);
            _i++; Skip();
            if (_i < _s.Length && _s[_i] == '}') { _i++; return d; }
            while (true)
            {
                Skip();
                if (_i >= _s.Length || _s[_i] != '"') throw Err("Expected key");
                string k = Str();
                Skip();
                if (_i >= _s.Length || _s[_i] != ':') throw Err("Expected ':'");
                _i++; Skip();
                d[k] = Value(depth + 1);
                Skip();
                if (_i >= _s.Length) throw Err("Unexpected end");
                if (_s[_i] == ',') { _i++; continue; }
                if (_s[_i] == '}') { _i++; return d; }
                throw Err("Expected ',' or '}'");
            }
        }

        List<object> Arr(int depth)
        {
            var l = new List<object>();
            _i++; Skip();
            if (_i < _s.Length && _s[_i] == ']') { _i++; return l; }
            while (true)
            {
                Skip();
                l.Add(Value(depth + 1));
                Skip();
                if (_i >= _s.Length) throw Err("Unexpected end");
                if (_s[_i] == ',') { _i++; continue; }
                if (_s[_i] == ']') { _i++; return l; }
                throw Err("Expected ',' or ']'");
            }
        }

        string Str()
        {
            var sb = new StringBuilder();
            _i++; // comilla inicial
            while (true)
            {
                if (_i >= _s.Length) throw Err("Unterminated string");
                char c = _s[_i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (_i >= _s.Length) throw Err("Bad escape");
                char e = _s[_i++];
                switch (e)
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
                        int code;
                        if (_i + 4 > _s.Length || !int.TryParse(_s.Substring(_i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code)) throw Err("Bad \\u escape");
                        sb.Append((char)code);
                        _i += 4;
                        break;
                    default: throw Err("Bad escape");
                }
            }
        }

        double Num()
        {
            int start = _i;
            while (_i < _s.Length && "+-0123456789.eE".IndexOf(_s[_i]) >= 0) _i++;
            double v;
            if (_i == start || !double.TryParse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) throw Err("Bad value");
            return v;
        }
    }
}
