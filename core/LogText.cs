using System;
using System.Collections.Generic;

namespace TrackerCore
{
    /// <summary>
    /// 日志文本字段的转义：\ → \\，| → \p，换行 → \n，回车 → \r；其它 \x 读作 x，末尾孤立的 \ 原样保留。
    /// 全程不按下标取字符：只用切分、替换和 ASCII 前缀，各语言的字符串编码不同也不受影响。
    /// </summary>
    public static class LogText
    {
        public static string Escape(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("|", "\\p").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        public static string Unescape(string s)
        {
            if (s == null) return "";
            if (s.IndexOf("\\", StringComparison.Ordinal) < 0) return s;

            List<string> parts = Rt.Split(s, "\\");
            string res = parts[0];
            int n = parts.Count;
            int i = 1;
            while (i < n)
            {
                string p = parts[i];
                if (p == "")
                {
                    // 两个反斜杠之间的空段：转义的反斜杠，后面那段原样；在最后则是末尾孤立的反斜杠
                    if (i == n - 1)
                    {
                        res = res + "\\";
                        i = i + 1;
                    }
                    else
                    {
                        res = res + "\\" + parts[i + 1];
                        i = i + 2;
                    }
                    continue;
                }
                if (p.StartsWith("p", StringComparison.Ordinal)) res = res + "|" + p.Substring(1);
                else if (p.StartsWith("n", StringComparison.Ordinal)) res = res + "\n" + p.Substring(1);
                else if (p.StartsWith("r", StringComparison.Ordinal)) res = res + "\r" + p.Substring(1);
                else res = res + p;
                i = i + 1;
            }
            return res;
        }
    }
}
