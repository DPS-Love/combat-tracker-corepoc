using System;
using System.Collections.Generic;

namespace TrackerCore
{
    /// <summary>读一份日志容器的结果。事件只拆到"时钟、字母、字段"，各游戏的字母表在方言层解释。</summary>
    public sealed class ContainerResult
    {
        /// <summary>空串 = 成功。</summary>
        public string Error = "";
        public long Version = 0;
        public Dictionary<string, string> Meta = new Dictionary<string, string>();
        /// <summary>META 键的出现顺序（Map 的遍历顺序各语言不一，顺序单独记）。</summary>
        public List<string> MetaKeys = new List<string>();
        /// <summary>每个事件：[规整后的时钟, 字母, 字段…]，字段已反转义。</summary>
        public List<List<string>> Events = new List<List<string>>();
        public long Bad = 0;
        public long Comments = 0;
        /// <summary>没读到 #END。</summary>
        public bool Truncated = true;
    }

    /// <summary>
    /// 日志容器：#MAGIC|版本 / #META|k=v|… / 事件行 / #END|events=N，其它 # 行是注释。
    /// 第一行非空行必须是文件头。坏行（时钟不是数字、缺字母）计数后跳过。
    /// </summary>
    public static class LogContainer
    {
        public static string HeaderLine(string magic, long version) => magic + "|" + Rt.LToS(version);

        public static string EndLine(long events) => "#END|events=" + Rt.LToS(events);

        public static string MetaLine(List<string> keys, List<string> values)
        {
            string res = "#META";
            int i = 0;
            while (i < keys.Count)
            {
                res = res + "|" + LogText.Escape(keys[i]) + "=" + LogText.Escape(values[i]);
                i = i + 1;
            }
            return res;
        }

        public static string EventLine(double t, string letter, List<string> fields)
        {
            string res = NumText.Clock(t) + "|" + letter;
            foreach (string f in fields) res = res + "|" + LogText.Escape(f);
            return res;
        }

        public static ContainerResult Parse(List<string> lines, string magic)
        {
            ContainerResult r = new ContainerResult();
            bool sawHeader = false;
            foreach (string raw in lines)
            {
                string line = raw;
                if (line.EndsWith("\r", StringComparison.Ordinal)) line = line.Substring(0, line.Length - 1);
                if (line == "") continue;

                if (!sawHeader)
                {
                    if (!line.StartsWith(magic + "|", StringComparison.Ordinal))
                    {
                        r.Error = "not a combat log";
                        return r;
                    }
                    string ver = line.Substring(magic.Length + 1);
                    if (!Rt.IsAsciiInt(ver))
                    {
                        r.Error = "bad header";
                        return r;
                    }
                    r.Version = Rt.ParseInt(ver);
                    sawHeader = true;
                    continue;
                }

                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    if (line == "#END" || line.StartsWith("#END|", StringComparison.Ordinal)) r.Truncated = false;
                    else if (line.StartsWith("#META|", StringComparison.Ordinal)) ParseMeta(r, line.Substring(6));
                    else r.Comments = r.Comments + 1;
                    continue;
                }

                List<string> f = Rt.Split(line, "|");
                if (f.Count < 2 || f[1] == "" || !Rt.IsAsciiNumber(f[0]))
                {
                    r.Bad = r.Bad + 1;
                    continue;
                }
                List<string> ev = new List<string>();
                ev.Add(NumText.Clock(Rt.ParseNumber(f[0])));
                ev.Add(f[1]);
                int i = 2;
                while (i < f.Count)
                {
                    ev.Add(LogText.Unescape(f[i]));
                    i = i + 1;
                }
                r.Events.Add(ev);
            }
            if (!sawHeader) r.Error = "not a combat log";
            return r;
        }

        private static void ParseMeta(ContainerResult r, string body)
        {
            foreach (string kv in Rt.Split(body, "|"))
            {
                int eq = kv.IndexOf("=", StringComparison.Ordinal);
                if (eq <= 0) continue;
                string key = LogText.Unescape(kv.Substring(0, eq));
                if (!r.Meta.ContainsKey(key)) r.MetaKeys.Add(key);
                r.Meta[key] = LogText.Unescape(kv.Substring(eq + 1));
            }
        }
    }
}
