using System;

namespace TrackerCore
{
    /// <summary>
    /// 数字写成文本。不用任何语言自带的格式化（舍入规则各不相同）：先按 .5 远离 0 取整成整数，
    /// 再用整数拼出小数——整数运算在各语言里结果一致。
    /// </summary>
    public static class NumText
    {
        public static double Pow10(long d)
        {
            double r = 1.0;
            long i = 0;
            while (i < d)
            {
                r = r * 10.0;
                i = i + 1;
            }
            return r;
        }

        /// <summary>保留 d 位小数；trim 时去掉末尾的 0（和多余的小数点）。非有限值写成 "0"。</summary>
        public static string Fixed(double v, long d, bool trim)
        {
            if (!Rt.IsFinite(v)) return "0";
            double scale = Pow10(d);
            long units = (long)Rt.RoundHalfAway(Rt.AbsD(v) * scale);
            long whole = units / (long)scale;
            long frac = units - whole * (long)scale;
            string res = (v < 0.0 && units != 0 ? "-" : "") + Rt.LToS(whole);
            if (d == 0) return res;

            string fs = Rt.LToS(frac);
            while (fs.Length < d) fs = "0" + fs;
            if (trim)
            {
                while (fs.Length > 0 && fs.EndsWith("0", StringComparison.Ordinal)) fs = fs.Substring(0, fs.Length - 1);
                if (fs.Length == 0) return res;
            }
            return res + "." + fs;
        }

        /// <summary>日志里的时钟：3 位小数。</summary>
        public static string Clock(double t) => Fixed(t, 3, false);

        /// <summary>日志里的数值：最多 2 位小数。</summary>
        public static string Amount(double v) => Fixed(v, 2, true);
    }
}
