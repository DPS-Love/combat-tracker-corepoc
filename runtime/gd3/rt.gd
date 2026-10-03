extends Reference

# 运行库（GDScript 3 版），和 core/Rt.cs 一一对应，手写。
# 注意 GDScript 3 的 abs / max 对整数也返回 float，所以整数版本自己写。


static func RoundHalfAway(x: float) -> float:
	return round(x)   # C 的 round：.5 远离 0


static func Floor(x: float) -> float:
	return floor(x)


static func AbsD(x: float) -> float:
	return abs(x)


static func AbsL(x: int) -> int:
	return -x if x < 0 else x


static func MaxD(a: float, b: float) -> float:
	return a if a > b else b


static func MaxL(a: int, b: int) -> int:
	return a if a > b else b


static func IsFinite(x: float) -> bool:
	return not (is_nan(x) or is_inf(x))


static func LToS(v: int) -> String:
	return str(v)


static func Split(s: String, sep: String) -> Array:
	return Array(s.split(sep, true))


const MAX_INT_DIGITS = 18
const MAX_NUMBER_DIGITS = 15


static func _digits(s: String, from: int) -> bool:
	if from >= s.length():
		return false
	for i in range(from, s.length()):
		var c = ord(s[i])
		if c < 48 or c > 57:
			return false
	return true


static func IsAsciiInt(s: String) -> bool:
	var from = 1 if s.begins_with("-") else 0
	return s.length() - from <= MAX_INT_DIGITS and _digits(s, from)


static func IsAsciiNumber(s: String) -> bool:
	var n = s.length() - (1 if s.begins_with("-") else 0) - (1 if s.find(".") >= 0 else 0)
	if n > MAX_NUMBER_DIGITS:
		return false
	var dot = s.find(".")
	if dot < 0:
		return IsAsciiInt(s)
	return _digits(s, dot + 1) and IsAsciiInt(s.substr(0, dot))


static func ParseInt(s: String) -> int:
	return s.to_int()


# 不能用 String.to_float()：Godot 3 的它只有单精度（实测低位全是 0）。
# 数字不超过 15 位时，"整数 / 10^k" 只有一次舍入，结果和 C# 的 double.Parse 一样
static func ParseNumber(s: String) -> float:
	var neg = s.begins_with("-")
	var body = s.substr(1, -1) if neg else s
	var dot = body.find(".")
	var k = 0
	var digits = body
	if dot >= 0:
		digits = body.substr(0, dot) + body.substr(dot + 1, -1)
		k = body.length() - dot - 1
	var p = 1.0
	for _i in range(k):
		p *= 10.0
	var v = float(digits.to_int()) / p
	return -v if neg else v
