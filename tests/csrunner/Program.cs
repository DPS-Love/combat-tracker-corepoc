// C# 一侧的测试台：读 vectors.json，用核心（原样编译）和 TBH 旧实现各跑一遍，结果写成 JSON。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using TrackerCore;
using Legacy = TbhCombatTracker;

internal static class Program
{
    private static double D(JsonNode b) => BitConverter.Int64BitsToDouble((long)(((ulong)(long)b[0] << 32) | (ulong)(long)b[1]));
    private static string Hex(double v) => BitConverter.DoubleToInt64Bits(v).ToString("x16");

    private static int Main(string[] args)
    {
        var vec = JsonNode.Parse(File.ReadAllText(args[0]));
        var outp = new JsonObject();

        var texts = vec["texts"].AsArray().Select(x => (string)x).ToList();
        outp["text"] = Arr(texts.Select(s => Arr(LogText.Escape(s), LogText.Unescape(LogText.Escape(s)), LogText.Unescape(s))));

        var values = vec["values"].AsArray().Select(D).ToList();
        var digits = vec["fixed_digits"].AsArray().Select(x => (long)x).ToList();
        var fixedOut = new JsonArray();
        foreach (var v in values)
            foreach (var d in digits)
            {
                fixedOut.Add(NumText.Fixed(v, d, false));
                fixedOut.Add(NumText.Fixed(v, d, true));
            }
        outp["fixed"] = fixedOut;
        outp["fmt"] = Arr(values.Select(v => Arr(Fmt.Short(v), Fmt.Dur(v), Fmt.Pct(v / 1000.0))));

        var counts = vec["counts"].AsArray().Select(x => (long)x).ToList();
        outp["count"] = Arr(counts.Select(n => (JsonNode)Fmt.Count(n)));

        var parses = vec["parses"].AsArray().Select(x => (string)x).ToList();
        outp["parse"] = Arr(parses.Select(s => Arr(
            Rt.IsAsciiInt(s) ? "int" : "-", Rt.IsAsciiInt(s) ? Rt.LToS(Rt.ParseInt(s)) : "",
            Rt.IsAsciiNumber(s) ? "num" : "-", Rt.IsAsciiNumber(s) ? Hex(Rt.ParseNumber(s)) : "")));

        var cont = new JsonArray();
        foreach (var c in vec["containers"].AsArray())
        {
            var lines = c["lines"].AsArray().Select(x => (string)x).ToList();
            cont.Add(Container(LogContainer.Parse(lines, (string)c["magic"])));
        }
        outp["container"] = cont;

        var rts = new JsonArray();
        foreach (var r in vec["roundtrips"].AsArray())
        {
            var fields = r["fields"].AsArray().Select(x => (string)x).ToList();
            var line = LogContainer.EventLine(D(r["t"]), (string)r["letter"], fields);
            var parsed = LogContainer.Parse(new List<string> { LogContainer.HeaderLine("#T", 1), line, LogContainer.EndLine(1) }, "#T");
            var ok = parsed.Events.Count == 1 && parsed.Events[0].Skip(2).SequenceEqual(fields);
            rts.Add(Arr(line, ok ? "ok" : "MISMATCH"));
        }
        outp["roundtrip"] = rts;

        // ---- TBH 旧实现（对照用）
        var num = typeof(Legacy.EventLogFormat).GetMethod("Num", BindingFlags.NonPublic | BindingFlags.Static);
        outp["legacy_text"] = Arr(texts.Select(s => Arr(Legacy.EventLogFormat.Escape(s),
            Legacy.EventLogFormat.Unescape(Legacy.EventLogFormat.Escape(s)), Legacy.EventLogFormat.Unescape(s))));
        outp["legacy_amount"] = Arr(values.Select(v => (JsonNode)(string)num.Invoke(null, new object[] { (float)v })));
        outp["legacy_fmt"] = Arr(values.Select(v => Arr(Legacy.Fmt.Short(v), Legacy.Fmt.Dur(v), Legacy.Fmt.Pct(v / 1000.0))));
        outp["legacy_count"] = Arr(counts.Select(n => (JsonNode)Legacy.Fmt.Count(n)));

        File.WriteAllText(args[1], outp.ToJsonString(new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        }));
        return 0;
    }

    private static JsonNode Container(ContainerResult r) => new JsonObject
    {
        ["error"] = r.Error,
        ["version"] = Rt.LToS(r.Version),
        ["meta"] = Arr(r.MetaKeys.Select(k => Arr(k, r.Meta[k]))),
        ["events"] = Arr(r.Events.Select(e => Arr(e.ToArray()))),
        ["bad"] = Rt.LToS(r.Bad),
        ["comments"] = Rt.LToS(r.Comments),
        ["truncated"] = r.Truncated ? "true" : "false",
    };

    private static JsonArray Arr(params string[] xs) => new JsonArray(xs.Select(x => (JsonNode)x).ToArray());
    private static JsonArray Arr(IEnumerable<JsonNode> xs) => new JsonArray(xs.ToArray());
}
