namespace TrackerCore
{
    /// <summary>界面上的数字：大数缩写、时长、百分比、千分位。</summary>
    public static class Fmt
    {
        public static string Short(double v)
        {
            double a = Rt.AbsD(v);
            if (a >= 1e12) return NumText.Fixed(v / 1e12, 2, true) + "T";
            if (a >= 1e9) return NumText.Fixed(v / 1e9, 2, true) + "B";
            if (a >= 1e6) return NumText.Fixed(v / 1e6, 2, true) + "M";
            if (a >= 1e3) return NumText.Fixed(v / 1e3, 2, true) + "K";
            return NumText.Fixed(v, 1, true);
        }

        /// <summary>2:31、1:02:03。秒数 .5 远离 0 取整。</summary>
        public static string Dur(double seconds)
        {
            long s = (long)Rt.RoundHalfAway(Rt.MaxD(0.0, seconds));
            if (s >= 3600) return Rt.LToS(s / 3600) + ":" + Pad2(s / 60 % 60) + ":" + Pad2(s % 60);
            return Rt.LToS(s / 60) + ":" + Pad2(s % 60);
        }

        private static string Pad2(long n) => n < 10 ? "0" + Rt.LToS(n) : Rt.LToS(n);

        public static string Pct(double v) => NumText.Fixed(v * 100.0, 1, false) + "%";

        public static string Count(long n)
        {
            string digits = Rt.LToS(Rt.AbsL(n));
            string res = "";
            long i = digits.Length;
            while (i > 3)
            {
                res = "," + digits.Substring((int)(i - 3), 3) + res;
                i = i - 3;
            }
            res = digits.Substring(0, (int)i) + res;
            return n < 0 ? "-" + res : res;
        }
    }
}
