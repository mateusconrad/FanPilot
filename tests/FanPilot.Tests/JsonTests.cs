// Item 28: Json replaced System.Web.Extensions' JavaScriptSerializer, which .NET 10 (Linux) doesn't have. On Windows
// the old serializer is still at hand, so every case here is also checked against it: the data files, the API and the
// integrations must not notice the change.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

public class JsonTests
{
    sealed class Plain
    {
        public string Name = "fan";
        public int Count = 2;
        public double? Missing = null;
        public bool Ok { get { return true; } }
    }

    enum Kind { First, Second = 7 }

    static Dictionary<string, object> Everything()
    {
        return new Dictionary<string, object> {
            { "text", "Ação \"quoted\" \\ back/slash <b>&'x'</b>\n\r\t\b\f\u0001\u001f\u0085\u2028\u2029 é 😀" },
            { "empty", "" }, { "nothing", null }, { "yes", true }, { "no", false }, { "letter", 'x' }, { "nul", '\0' },
            { "int", 42 }, { "negative", -7 }, { "long", 1L << 40 }, { "short", (short)3 }, { "byte", (byte)255 }, { "uint", 4000000000u },
            { "double", 0.1 + 0.2 }, { "whole double", 100.0 }, { "tiny", 1e-7 }, { "huge", 1.5e300 }, { "negzero", -0.0 },
            { "float", 37.375f }, { "float odd", 1.1f }, { "decimal", 12.50m },
            { "enum", Kind.Second }, { "guid", new Guid("0f8fad5b-d9cb-469f-a165-70867728950e") },
            { "date", new DateTime(2026, 10, 28, 12, 34, 56, 789, DateTimeKind.Utc) },
            { "list", new List<object> { 1, "two", null, new object[] { 3.5, new int[0] } } },
            { "points", new List<double[]> { new[] { 30.0, 76.5 }, new[] { 90.0, 255.0 } } },
            { "nested", new Dictionary<string, object> { { "deeper", new Dictionary<string, object>() } } },
            { "plain", new Plain() }
        };
    }

    // Documents FanPilot reads: what it wrote, what the panel sends, and what people type into config.json.
    static readonly string[] Documents = {
        "{}",
        "{\"port\":8787,\"publicHost\":null,\"backend\":\"auto\",\"historyDays\":7}",
        " \r\n\t{ \"a\" : [ 1 , -2 , 3.25 , -0.5e3 , 1E2 , 2147483647 , 2147483648 , -2147483649 , 9223372036854775807 ] } \n",
        "{\"big\":79228162514264337593543950335,\"bigger\":1e40,\"exp\":12e-3,\"zero\":0,\"negzero\":-0}",
        "{\"s\":\"\\u00e9\\u003c\\/\\\"\\\\\\b\\f\\n\\r\\t \\ud83d\\ude00 é\",\"t\":true,\"f\":false,\"n\":null}",
        "{\"points\":[[30,76.5],[90,255]],\"inner\":{\"x\":{\"y\":{}}},\"empty\":[]}",
        "{\"dup\":1,\"dup\":2}"
    };

    [Fact]
    public void InvalidJsonIsAFormatException()
    {
        foreach (string bad in new[] { "", "   ", "{", "{\"a\":}", "{\"a\":1,}", "[1,2]", "null", "\"text\"", "{\"a\":01x}",
                                       "{'a':1}", "{a:1}", "{\"a\":1} x", "{\"a\":\"\\q\"}", "{\"a\":\"\u0001\"}", "{\"a\":tru}",
                                       "{\"a\":-}", "{\"a\":1.}", "{\"a\":\"open}" })
            Assert.Throws<FormatException>(() => Json.ParseObject(bad));
    }

    [Fact]
    public void DeeplyNestedJsonIsRefused()
    {
        string deep = "{\"a\":" + string.Concat(Enumerable.Repeat("[", 200)) + string.Concat(Enumerable.Repeat("]", 200)) + "}";
        Assert.Throws<FormatException>(() => Json.ParseObject(deep));
    }

    [Fact]
    public void ValuesComeBackAsTheTypesFanPilotExpects()
    {
        var d = Json.ParseObject(Documents[2]);
        var a = (object[])d["a"];
        Assert.IsType<int>(a[0]);
        Assert.IsType<decimal>(a[2]);
        Assert.IsType<double>(a[3]);
        Assert.Equal(-500.0, a[3]);
        Assert.IsType<long>(a[6]);
        Assert.IsType<long>(a[7]);
        Assert.Equal(long.MaxValue, a[8]);
        Assert.Equal("é<", Json.GetString(Json.ParseObject(Documents[4]), "s", null).Substring(0, 2));
        Assert.Equal(2, Json.GetInt(Json.ParseObject(Documents[6]), "dup", 0));
    }

    [Fact]
    public void WhatIsWrittenIsReadBack()
    {
        var d = Json.ParseObject(Json.Serialize(Everything()));
        Assert.Equal(Everything()["text"], d["text"]);
        Assert.Equal(1L << 40, d["long"]);
        Assert.Equal(0.30000000000000004, Json.ToDouble(d["double"]));
        Assert.Equal(7, d["enum"]);
        Assert.Equal(new[] { 30.0, 76.5 }, Json.ToList(Json.ToList(d["points"])[0]).Select(Json.ToDouble));
        Assert.Equal("fan", Json.GetString((Dictionary<string, object>)d["plain"], "Name", null));
    }

#if NETFRAMEWORK
    static readonly System.Web.Script.Serialization.JavaScriptSerializer Old =
        new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };

    [Fact]
    public void WritesWhatJavaScriptSerializerWrote()
    {
        foreach (var kv in Everything()) Assert.Equal(Old.Serialize(kv.Value), Json.Serialize(kv.Value));
        // NaN and the infinities: the old serializer wrote them as bare words (not valid JSON, so not read back);
        // so does this one.
        foreach (object odd in new object[] { double.NaN, double.PositiveInfinity, float.NegativeInfinity })
            Assert.Equal(Old.Serialize(odd), Json.Serialize(odd));
        Assert.Equal(Old.Serialize(Everything()), Json.Serialize(Everything()));
    }

    [Fact]
    public void ReadsWhatJavaScriptSerializerRead()
    {
        foreach (string doc in Documents) AssertSame(Old.DeserializeObject(doc), Json.ParseObject(doc), doc);
    }

    [Fact]
    public void TheShippedJsonReadsAndWritesTheSame()
    {
        var files = Directory.GetFiles(Path.Combine(TestEnv.OutputDir, "fixtures"), "*.json", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(TestEnv.OutputDir, "wwwroot", "i18n"), "*.json")).ToList();
        Assert.NotEmpty(files);
        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            object old = Old.DeserializeObject(text);
            if (!(old is Dictionary<string, object>)) continue;
            var parsed = Json.ParseObject(text);
            AssertSame(old, parsed, file);
            Assert.Equal(Old.Serialize(old), Json.Serialize(parsed));
        }
    }

    // Same shape, same values and the same .NET types (int, long, decimal, object[], Dictionary).
    static void AssertSame(object expected, object actual, string where)
    {
        if (expected == null) { Assert.Null(actual); return; }
        Assert.NotNull(actual);
        Assert.True(expected.GetType() == actual.GetType(), where + ": " + expected.GetType() + " vs " + actual.GetType() + " for " + expected);
        if (expected is IDictionary<string, object> ed)
        {
            var ad = (IDictionary<string, object>)actual;
            Assert.Equal(ed.Keys.OrderBy(k => k, StringComparer.Ordinal), ad.Keys.OrderBy(k => k, StringComparer.Ordinal));
            foreach (var key in ed.Keys) AssertSame(ed[key], ad[key], where + "." + key);
        }
        else if (expected is IList el)
        {
            var al = (IList)actual;
            Assert.Equal(el.Count, al.Count);
            for (int i = 0; i < el.Count; i++) AssertSame(el[i], al[i], where + "[" + i + "]");
        }
        else Assert.Equal(expected, actual);
    }
#endif
}
