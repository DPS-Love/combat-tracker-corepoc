using System;
using System.Collections.Generic;
using System.Globalization;

// ============================================================================
//  运行库（C# 版）。不经转译：每种目标语言各有一份手写的同名实现（runtime/gd3/rt.gd …），
//  由一致性测试保证逐项相同。核心代码里凡是各语言语义不一的操作（取整、数字和字符串互转、
//  切分），一律经过这里，不直接用 BCL。
// ============================================================================

namespace TrackerCore
{
    public static class Rt
    {
        /// <summary>四舍五入，.5 远离 0（等同 C 的 round）。</summary>
        public static double RoundHalfAway(double x) => Math.Round(x, MidpointRounding.AwayFromZero);

        public static double Floor(double x) => Math.Floor(x);

        public static double AbsD(double x) => Math.Abs(x);

        public static long AbsL(long x) => x < 0 ? -x : x;

        public static double MaxD(double a, double b) => a > b ? a : b;

        public static long MaxL(long a, long b) => a > b ? a : b;

        public static bool IsFinite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);

        public static string LToS(long v) => v.ToString(CultureInfo.InvariantCulture);

        /// <summary>按分隔符切分，保留空段（"a||b" → a、空、b）。</summary>
        public static List<string> Split(string s, string sep) => new List<string>(s.Split(sep));

        /// <summary>日志里一个数字最多这么多位（不算符号和小数点）：整数不溢出 64 位，小数能精确解析。</summary>
        public const int MaxIntDigits = 18;
        public const int MaxNumberDigits = 15;

        /// <summary>-?[0-9]{1,18}</summary>
        public static bool IsAsciiInt(string s)
        {
            var i = s.StartsWith("-", StringComparison.Ordinal) ? 1 : 0;
            if (i >= s.Length || s.Length - i > MaxIntDigits) return false;
            for (; i < s.Length; i++)
                if (s[i] < '0' || s[i] > '9') return false;
            return true;
        }

        /// <summary>
        /// -?[0-9]+(\.[0-9]+)?，数字总共不超过 15 位 —— 不认指数、空白、正号、无穷：日志里的数字只有这一种写法。
        /// 15 位以内时"整数 / 10^k"只有一次舍入，各语言自己实现的解析都能得到同一个 double。
        /// </summary>
        public static bool IsAsciiNumber(string s)
        {
            var digitCount = s.Length - (s.StartsWith("-", StringComparison.Ordinal) ? 1 : 0) - (s.IndexOf('.') >= 0 ? 1 : 0);
            if (digitCount > MaxNumberDigits) return false;
            var dot = s.IndexOf('.');
            if (dot < 0) return IsAsciiInt(s);
            var frac = s.Substring(dot + 1);
            if (frac.Length == 0) return false;
            foreach (var c in frac)
                if (c < '0' || c > '9') return false;
            return IsAsciiInt(s.Substring(0, dot));
        }

        public static long ParseInt(string s) => long.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

        public static double ParseNumber(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
