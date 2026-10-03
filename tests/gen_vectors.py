"""生成一致性测试向量 vectors.json。

浮点输入一律以 IEEE 754 位模式传（[高 32 位, 低 32 位]），各语言的 JSON 解析器读小数的精度不同，
用位模式才能保证两边拿到的是同一个 double。
"""
import json
import random
import struct
import sys

rnd = random.Random(20261003)


def bits(v):
    u = struct.unpack("<Q", struct.pack("<d", v))[0]
    return [u >> 32, u & 0xFFFFFFFF]


# ---------------------------------------------------------------- 文本
texts = [
    "", "a", "plain text", "a|b", "|", "||", "\\", "\\\\", "\\p", "\\\\p", "a\\", "a\\\\", "\\x", "\\nn",
    "\n", "\r", "\r\n", "line1\nline2", "中文|测试", "游侠\\快速射击", "🙂|x", "x🙂\\p🙂", "=", "k=v|k2=v2",
    "#END", "w:weapon_smg:2", "Monster_30043", "Stage {0}-{1}", "\\r\\n\\p\\\\",
]
alphabet = ["a", "b", "|", "\\", "\n", "\r", "p", "n", "r", "中", "🙂", "=", "#", " ", "x"]
for _ in range(500):
    texts.append("".join(rnd.choice(alphabet) for _ in range(rnd.randint(0, 14))))

# ---------------------------------------------------------------- 数字
values = [0.0, -0.0, 1.0, -1.0, 0.5, 1.5, 2.5, -2.5, 0.125, 0.375, 1.005, 1.115, 2.675, 0.285, 8123.46,
          999.95, 999.96, 999999.5, 1e3, 1e6, 1e9, 1e12, 999999999999.995, 123456789012.34, 1e-9, 0.0049,
          0.005, 0.015, 0.025, 0.045, 59.5, 3599.5, 3600.0, 86399.5, 7322.5, 0.9995, 1234.5678]
for _ in range(600):
    values.append(round(rnd.uniform(-1000, 1000), rnd.randint(0, 6)))
for _ in range(400):
    values.append(rnd.uniform(0, 10) ** rnd.randint(1, 13))
for _ in range(300):
    # x.xx5 这类恰在两位小数中间的值：最容易暴露舍入规则的差异
    values.append(rnd.randint(0, 99999) / 100 + 0.005)
for _ in range(200):
    values.append(rnd.randint(0, 100000) + 0.5)
for _ in range(200):
    values.append(rnd.random())
values = [v for v in values if abs(v) < 1e14]

counts = [0, 1, -1, 12, 123, 1234, -1234, 12345, 123456, 1234567, 999999, 1000000, -9876543210,
          9007199254740991, -9007199254740991]
counts += [rnd.randint(-10 ** 12, 10 ** 12) for _ in range(300)]

parses = ["0", "-0", "1", "-1", "12", "1.5", "-1.5", "0.512", "1.", ".5", "+1", "1e5", "1E5", " 1", "1 ", "",
          "-", "abc", "1.2.3", "0x10", "NaN", "Infinity", "１２", "9007199254740993", "123456789012345678",
          "0.1000000000000000055511151231257827", "0.30000000000000004", "2.675", "1234.5678",
          "99999999999999.999", "0.000001", "3.14159265358979323846"]
for _ in range(300):
    parses.append(repr(round(rnd.uniform(0, 100000), rnd.randint(0, 3))))
for _ in range(200):
    parses.append("%.17g" % rnd.uniform(0, 1000))

# ---------------------------------------------------------------- 容器
containers = [
    {"magic": "#BCTLOG", "lines": [
        "#BCTLOG|1", "#META|mod=0.1.0|game=1.1.15.4|start=2026-09-28T20:29:31|start_unix=1790598571",
        "0.000|W|start|9|0|1|1790598576", "0.000|C|0|character_well_rounded",
        "0.000|L|0|w:weapon_torch:1,w:weapon_plank:1", "2.283|D|0|w:weapon_shredder:0|e:chaser|5|5|0|x|stat_ranged_damage",
        "29.867|W|end|0", "#END|events=5"]},
    {"magic": "#TBHLOG", "lines": [
        "#TBHLOG|1", "#META|mod=0.3.0|game=1.2.8|start=2026-09-23T11:22:08.1234567+08:00",
        "0.512|U|-51234|H|2|Hero_201|游侠", "0.512|A|3|SkillName_20101|快速射击", "0.512|D|-51234|-60012|8123.46|10234|1|2|1|3",
        "# 注释", "1.5|S|name|关卡 3\\p2", "2|R", "3.25|X|future|fields"]},
    {"magic": "#TBHLOG", "lines": ["0.5|D|1|2|3", "#TBHLOG|1"]},
    {"magic": "#TBHLOG", "lines": ["", "#TBHLOG|x"]},
    {"magic": "#TBHLOG", "lines": ["#TBHLOG|2\r", "1.0|D|a\r", "bad|D", "1.0||x", "1.0", "1e3|D", "-0.0005|D|x", "#END"]},
    {"magic": "#TBHLOG", "lines": ["#TBHLOG|1", "#META|k\\p=v\\n|中文键=值|noeq|=novalue|k\\p=dup", "1.0005|T|\\\\|\\p|\\r"]},
    {"magic": "#TBHLOG", "lines": []},
]

roundtrips = []
for _ in range(300):
    t = rnd.choice([rnd.uniform(0, 5000), rnd.randint(0, 5000) + 0.0005, float(rnd.randint(0, 100))])
    letter = rnd.choice(["D", "T", "H", "W", "X"])
    fields = ["".join(rnd.choice(alphabet) for _ in range(rnd.randint(0, 8))) for _ in range(rnd.randint(0, 9))]
    roundtrips.append({"t": bits(t), "letter": letter, "fields": fields})

doc = {
    "texts": texts,
    "values": [bits(v) for v in values],
    "fixed_digits": [0, 1, 2, 3],
    "counts": counts,
    "parses": parses,
    "containers": containers,
    "roundtrips": roundtrips,
}
with open(sys.argv[1], "w", encoding="utf-8") as f:
    json.dump(doc, f, ensure_ascii=False, indent=1)
print(f"texts={len(texts)} values={len(values)} counts={len(counts)} parses={len(parses)} "
      f"containers={len(containers)} roundtrips={len(roundtrips)}")
