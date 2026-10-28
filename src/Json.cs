// JSON for the data files, the API and the integrations, the same on Windows (.NET Framework) and Linux (.NET 10,
// item 28). It writes and reads what System.Web.Extensions' JavaScriptSerializer did before it, so nothing outside
// this file changed: objects as Dictionary<string, object>, arrays as object[], whole numbers as int (long when they
// don't fit), numbers with an exponent as double, other numbers as decimal; "<", ">", "&" and "'" escaped; double and
// float in their round-trip form. JsonTests compares both on Windows.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

static class Json
{
    // JavaScriptSerializer's defaults: 2 MB of text was its limit (FanPilot raised it to 8 MB), 100 levels deep.
    const int MaxLength = 8 * 1024 * 1024;
    const int MaxDepth = 100;

    public static string Serialize(object o)
    {
        var sb = new StringBuilder();
        Write(sb, o, 0);
        return sb.ToString();
    }

    public static Dictionary<string, object> ParseObject(string text)
    {
        if (text == null) throw new FormatException("JSON text expected");
        if (text.Length > MaxLength) throw new FormatException("JSON text longer than " + MaxLength + " characters");
        var d = new Reader(text).ReadDocument() as Dictionary<string, object>;
        if (d == null) throw new FormatException("JSON object expected");
        return d;
    }

    public static object Get(Dictionary<string, object> d, string key)
    {
        object v;
        return d != null && d.TryGetValue(key, out v) ? v : null;
    }

    public static int GetInt(Dictionary<string, object> d, string key, int fallback)
    {
        object v = Get(d, key);
        return v == null ? fallback : (int)Math.Round(ToDouble(v));
    }

    public static string GetString(Dictionary<string, object> d, string key, string fallback)
    {
        object v = Get(d, key);
        return v == null ? fallback : Convert.ToString(v, CultureInfo.InvariantCulture);
    }

    public static double ToDouble(object v)
    {
        if (v == null) throw new FormatException("number expected");
        return Convert.ToDouble(v, CultureInfo.InvariantCulture);
    }

    public static List<object> ToList(object v)
    {
        var e = v as IEnumerable;
        if (e == null || v is string) throw new FormatException("array expected");
        var list = new List<object>();
        foreach (object o in e) list.Add(o);
        return list;
    }

    static void Write(StringBuilder sb, object o, int depth)
    {
        if (depth > MaxDepth) throw new ArgumentException("JSON nested deeper than " + MaxDepth + " levels");
        if (o == null || o is DBNull) { sb.Append("null"); return; }
        if (o is string s) { Quote(sb, s); return; }
        if (o is char c) { if (c == '\0') sb.Append("null"); else Quote(sb, c.ToString()); return; }
        if (o is bool b) { sb.Append(b ? "true" : "false"); return; }
        if (o is double dbl) { sb.Append(dbl.ToString("r", CultureInfo.InvariantCulture)); return; }
        if (o is float f) { sb.Append(f.ToString("r", CultureInfo.InvariantCulture)); return; }
        if (o is Enum) { sb.Append(Convert.ToInt64(o, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)); return; }
        if (o is DateTime dt) { sb.Append("\"\\/Date(").Append(UnixMs(dt).ToString(CultureInfo.InvariantCulture)).Append(")\\/\""); return; }
        if (o is Guid || o is Uri) { Quote(sb, o.ToString()); return; }
        if (o.GetType().IsPrimitive || o is decimal) { sb.Append(Convert.ToString(o, CultureInfo.InvariantCulture)); return; }
        if (o is IDictionary dict)
        {
            sb.Append('{');
            bool first = true;
            foreach (DictionaryEntry e in dict)
            {
                if (!(e.Key is string key)) throw new ArgumentException("JSON object keys must be strings");
                if (!first) sb.Append(',');
                first = false;
                Quote(sb, key);
                sb.Append(':');
                Write(sb, e.Value, depth + 1);
            }
            sb.Append('}');
            return;
        }
        if (o is IEnumerable list)
        {
            sb.Append('[');
            bool first = true;
            foreach (object item in list)
            {
                if (!first) sb.Append(',');
                first = false;
                Write(sb, item, depth + 1);
            }
            sb.Append(']');
            return;
        }
        // Any other object: its public fields, then its public readable properties, as JavaScriptSerializer did.
        sb.Append('{');
        bool firstMember = true;
        foreach (var field in o.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!firstMember) sb.Append(',');
            firstMember = false;
            Quote(sb, field.Name);
            sb.Append(':');
            Write(sb, field.GetValue(o), depth + 1);
        }
        foreach (var prop in o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var getter = prop.GetGetMethod();
            if (getter == null || getter.GetParameters().Length > 0) continue;
            if (!firstMember) sb.Append(',');
            firstMember = false;
            Quote(sb, prop.Name);
            sb.Append(':');
            Write(sb, getter.Invoke(o, null), depth + 1);
        }
        sb.Append('}');
    }

    static long UnixMs(DateTime t)
    {
        return (t.ToUniversalTime().Ticks - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks) / 10000;
    }

    static void Quote(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    // Safe inside a <script> and an HTML attribute too, as JavaScriptSerializer wrote them.
                    if (c < ' ' || c == '<' || c == '>' || c == '&' || c == '\'' || c == '\u0085' || c == (char)0x2028 || c == (char)0x2029)
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    // Strict JSON (RFC 8259): what FanPilot writes, the panel sends and the integrations get.
    sealed class Reader
    {
        readonly string _s;
        int _i;

        public Reader(string s) { _s = s; }

        public object ReadDocument()
        {
            object v = ReadValue(0);
            SkipSpace();
            if (_i < _s.Length) throw Error("unexpected text after the value");
            return v;
        }

        object ReadValue(int depth)
        {
            if (depth > MaxDepth) throw Error("nested deeper than " + MaxDepth + " levels");
            SkipSpace();
            if (_i >= _s.Length) throw Error("value expected");
            char c = _s[_i];
            switch (c)
            {
                case '{': return ReadObject(depth);
                case '[': return ReadArray(depth);
                case '"': return ReadString();
                case 't': Expect("true"); return true;
                case 'f': Expect("false"); return false;
                case 'n': Expect("null"); return null;
                default:
                    if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                    throw Error("unexpected '" + c + "'");
            }
        }

        Dictionary<string, object> ReadObject(int depth)
        {
            var d = new Dictionary<string, object>();
            _i++; // {
            SkipSpace();
            if (Peek() == '}') { _i++; return d; }
            while (true)
            {
                SkipSpace();
                if (Peek() != '"') throw Error("property name expected");
                string key = ReadString();
                SkipSpace();
                if (Peek() != ':') throw Error("':' expected");
                _i++;
                d[key] = ReadValue(depth + 1); // a repeated name: the last one wins, as before
                SkipSpace();
                char c = Peek();
                _i++;
                if (c == '}') return d;
                if (c != ',') throw Error("',' or '}' expected");
            }
        }

        object[] ReadArray(int depth)
        {
            var list = new List<object>();
            _i++; // [
            SkipSpace();
            if (Peek() == ']') { _i++; return list.ToArray(); }
            while (true)
            {
                list.Add(ReadValue(depth + 1));
                SkipSpace();
                char c = Peek();
                _i++;
                if (c == ']') return list.ToArray();
                if (c != ',') throw Error("',' or ']' expected");
            }
        }

        string ReadString()
        {
            _i++; // "
            var sb = new StringBuilder();
            while (true)
            {
                if (_i >= _s.Length) throw Error("unterminated string");
                char c = _s[_i++];
                if (c == '"') return sb.ToString();
                if (c < ' ') throw Error("control character in a string");
                if (c != '\\') { sb.Append(c); continue; }
                if (_i >= _s.Length) throw Error("unterminated string");
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
                        if (_i + 4 > _s.Length || !int.TryParse(_s.Substring(_i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code))
                            throw Error("bad \\u escape");
                        sb.Append((char)code);
                        _i += 4;
                        break;
                    default: throw Error("bad escape '\\" + e + "'");
                }
            }
        }

        // Whole numbers as int, or long when they don't fit; with an exponent, double; anything else as decimal (double past
        // its range).
        object ReadNumber()
        {
            int start = _i;
            if (Peek() == '-') _i++;
            if (!Digits()) throw Error("digit expected");
            bool whole = true, exponent = false;
            if (Peek() == '.') { _i++; whole = false; if (!Digits()) throw Error("digit expected after '.'"); }
            if (Peek() == 'e' || Peek() == 'E')
            {
                _i++;
                whole = false;
                exponent = true;
                if (Peek() == '+' || Peek() == '-') _i++;
                if (!Digits()) throw Error("digit expected in the exponent");
            }
            string text = _s.Substring(start, _i - start);
            if (whole)
            {
                int i;
                if (int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out i)) return i;
                long l;
                if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out l)) return l;
            }
            decimal m;
            if (!exponent && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out m)) return m;
            double d;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            throw Error("bad number '" + text + "'");
        }

        bool Digits()
        {
            int start = _i;
            while (_i < _s.Length && _s[_i] >= '0' && _s[_i] <= '9') _i++;
            return _i > start;
        }

        void Expect(string word)
        {
            if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) throw Error("unexpected '" + _s[_i] + "'");
            _i += word.Length;
        }

        char Peek() { return _i < _s.Length ? _s[_i] : '\0'; }

        void SkipSpace()
        {
            while (_i < _s.Length && (_s[_i] == ' ' || _s[_i] == '\t' || _s[_i] == '\n' || _s[_i] == '\r' || _s[_i] == (char)0xFEFF)) _i++;
        }

        FormatException Error(string what) { return new FormatException("Invalid JSON at position " + _i + ": " + what + "."); }
    }
}
