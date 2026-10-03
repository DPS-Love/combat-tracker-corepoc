"""比对 C# 与 GDScript 3 的结果。

第一部分是硬性要求：同一份核心，C#（原样编译）和 GDScript 3（转译）必须逐项相同。
第二部分只做对照：新核心、TBH 旧实现、Brotato 旧实现三者之间哪里不一样。
"""
import json
import struct
import sys

vec = json.load(open(sys.argv[1], encoding="utf-8"))
cs = json.load(open(sys.argv[2], encoding="utf-8"))
gd = json.load(open(sys.argv[3], encoding="utf-8"))


def dbl(b):
    return struct.unpack("<d", struct.pack("<Q", (b[0] << 32) | b[1]))[0]


values = [dbl(b) for b in vec["values"]]
nd = len(vec["fixed_digits"])


def inputs(key):
    if key in ("text",):
        return vec["texts"]
    if key == "fixed":
        return [f"v={v!r} d={d} trim={t}" for v in values for d in vec["fixed_digits"] for t in (False, True)]
    if key == "fmt":
        return [f"v={v!r}" for v in values]
    if key == "count":
        return vec["counts"]
    if key == "parse":
        return vec["parses"]
    if key == "container":
        return [c["lines"] for c in vec["containers"]]
    if key == "roundtrip":
        return [r["fields"] for r in vec["roundtrips"]]


print("=" * 72)
print("一、同一份核心：C#（原样编译） vs GDScript 3（转译）")
print("=" * 72)
failed = False
for key in ["text", "fixed", "fmt", "count", "parse", "container", "roundtrip"]:
    a, b = cs[key], gd[key]
    ins = inputs(key)
    diffs = [i for i in range(max(len(a), len(b))) if i >= len(a) or i >= len(b) or a[i] != b[i]]
    total = max(len(a), len(b))
    print(f"  {key:<10} {total - len(diffs):>6} / {total:<6} 一致" + ("" if not diffs else f"   ✗ {len(diffs)} 处不同"))
    for i in diffs[:5]:
        failed = True
        print(f"      输入 {ins[i]!r}\n        C#   {a[i] if i < len(a) else None!r}\n        GD3  {b[i] if i < len(b) else None!r}")
for name, res in (("C#", cs), ("GD3", gd)):
    bad = [r for r in res["roundtrip"] if r[1] != "ok"]
    if bad:
        failed = True
        print(f"  ✗ {name} 写出再读回不一致 {len(bad)} 处，例：{bad[0]!r}")
print("  结论：" + ("✗ 有差异" if failed else "全部一致"))

print()
print("=" * 72)
print("二、对照：新核心 / TBH 旧实现（C#）/ Brotato 旧实现（GDScript 3）")
print("=" * 72)


def col(rows, j):
    return [r[j] for r in rows]


core_amount = [cs["fixed"][(vi * nd + vec["fixed_digits"].index(2)) * 2 + 1] for vi in range(len(values))]
checks = [
    ("转义 escape", vec["texts"], col(cs["text"], 0), col(cs["legacy_text"], 0), col(gd["legacy_text"], 0)),
    ("转义往返 unescape(escape(s))", vec["texts"], col(cs["text"], 1), col(cs["legacy_text"], 1), col(gd["legacy_text"], 1)),
    ("反转义任意文本 unescape(s)", vec["texts"], col(cs["text"], 2), col(cs["legacy_text"], 2), col(gd["legacy_text"], 2)),
    ("日志数值（2 位小数）", values, core_amount, cs["legacy_amount"], gd["legacy_amount"]),
    ("Fmt.short", values, col(cs["fmt"], 0), col(cs["legacy_fmt"], 0), col(gd["legacy_fmt"], 0)),
    ("Fmt.dur", values, col(cs["fmt"], 1), col(cs["legacy_fmt"], 1), col(gd["legacy_fmt"], 1)),
    ("Fmt.pct（输入 v/1000）", values, col(cs["fmt"], 2), col(cs["legacy_fmt"], 2), col(gd["legacy_fmt"], 2)),
    ("Fmt.count", vec["counts"], cs["count"], cs["legacy_count"], gd["legacy_count"]),
]
print(f"  {'项目':<28}{'核心=TBH':>10}{'核心=Brotato':>14}{'TBH=Brotato':>13}   （共 N 条）")
examples = []
for name, ins, core, tbh, bro in checks:
    n = len(ins)
    ct = sum(core[i] == tbh[i] for i in range(n))
    cb = sum(core[i] == bro[i] for i in range(n))
    tb = sum(tbh[i] == bro[i] for i in range(n))
    print(f"  {name:<28}{ct:>10}{cb:>14}{tb:>13}   （{n}）")
    shown = 0
    for i in range(n):
        if not (core[i] == tbh[i] == bro[i]) and shown < 3:
            examples.append((name, ins[i], core[i], tbh[i], bro[i]))
            shown += 1
print()
print("  不一致的例子（每项最多 3 条）：")
for name, x, c, t, b in examples:
    print(f"    [{name}] 输入 {x!r}\n        核心 {c!r}   TBH {t!r}   Brotato {b!r}")

sys.exit(1 if failed else 0)
