extends SceneTree

# GDScript 3 一侧的测试台：用转译出来的核心（res://gen/）和 Brotato 旧实现（res://legacy/，原样拷贝）
# 各跑一遍 vectors.json，结果写成 JSON。路径由环境变量 VEC / OUT 给。

const LogText = preload("res://gen/log_text.gd")
const NumText = preload("res://gen/num_text.gd")
const Fmt = preload("res://gen/fmt.gd")
const LogContainer = preload("res://gen/log_container.gd")
const Rt = preload("res://gen/rt.gd")
const BroLog = preload("res://legacy/log_format.gd")
const BroFmt = preload("res://legacy/fmt.gd")


func _init() -> void:
	var f = File.new()
	f.open(OS.get_environment("VEC"), File.READ)
	var vec = JSON.parse(f.get_as_text()).result
	f.close()
	var out = {}

	var text = []
	var legacy_text = []
	for s in vec.texts:
		text.append([LogText.Escape(s), LogText.Unescape(LogText.Escape(s)), LogText.Unescape(s)])
		legacy_text.append([BroLog.escape(s), BroLog.unescape(BroLog.escape(s)), BroLog.unescape(s)])
	out.text = text
	out.legacy_text = legacy_text

	var values = []
	for b in vec.values:
		values.append(_dbl(b))
	var fixed = []
	for v in values:
		for d in vec.fixed_digits:
			fixed.append(NumText.Fixed(v, int(d), false))
			fixed.append(NumText.Fixed(v, int(d), true))
	out.fixed = fixed
	var fmt = []
	var legacy_fmt = []
	var legacy_amount = []
	for v in values:
		fmt.append([Fmt.Short(v), Fmt.Dur(v), Fmt.Pct(v / 1000.0)])
		legacy_fmt.append([BroFmt.short(v), BroFmt.dur(v), BroFmt.pct(v / 1000.0)])
		legacy_amount.append(BroLog.num(v))
	out.fmt = fmt
	out.legacy_fmt = legacy_fmt
	out.legacy_amount = legacy_amount

	var count = []
	var legacy_count = []
	for n in vec.counts:
		count.append(Fmt.Count(int(n)))
		legacy_count.append(BroFmt.count(int(n)))
	out.count = count
	out.legacy_count = legacy_count

	var parse = []
	for s in vec.parses:
		var isint = Rt.IsAsciiInt(s)
		var isnum = Rt.IsAsciiNumber(s)
		parse.append(["int" if isint else "-", Rt.LToS(Rt.ParseInt(s)) if isint else "",
			"num" if isnum else "-", _hex(Rt.ParseNumber(s)) if isnum else ""])
	out.parse = parse

	var cont = []
	for c in vec.containers:
		var r = LogContainer.Parse(c.lines, c.magic)
		var meta = []
		for k in r.MetaKeys:
			meta.append([k, r.Meta[k]])
		cont.append({"error": r.Error_, "version": str(r.Version), "meta": meta, "events": r.Events,
			"bad": str(r.Bad), "comments": str(r.Comments), "truncated": "true" if r.Truncated else "false"})
	out.container = cont

	var rts = []
	for rt in vec.roundtrips:
		var line = LogContainer.EventLine(_dbl(rt.t), rt.letter, rt.fields)
		var parsed = LogContainer.Parse([LogContainer.HeaderLine("#T", 1), line, LogContainer.EndLine(1)], "#T")
		var ok = parsed.Events.size() == 1 and parsed.Events[0].size() == rt.fields.size() + 2
		if ok:
			for i in range(rt.fields.size()):
				if parsed.Events[0][i + 2] != rt.fields[i]:
					ok = false
		rts.append([line, "ok" if ok else "MISMATCH"])
	out.roundtrip = rts

	f.open(OS.get_environment("OUT"), File.WRITE)
	f.store_string(to_json(out))
	f.close()
	quit()


static func _dbl(b) -> float:
	var buf = StreamPeerBuffer.new()
	buf.put_u32(int(b[1]))
	buf.put_u32(int(b[0]))
	buf.seek(0)
	return buf.get_double()


static func _hex(v: float) -> String:
	var buf = StreamPeerBuffer.new()
	buf.put_double(v)
	buf.seek(0)
	var lo = buf.get_u32()
	var hi = buf.get_u32()
	return "%08x%08x" % [hi, lo]
