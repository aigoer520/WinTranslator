using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WinTranslator
{
    /// <summary>v1.8.0 交叉核对的一条存疑标记（挂在对照卡的句子上）。</summary>
    public class CrossFlag
    {
        public int Index;          // 句子序号（0 起）
        public string Reason;      // 存疑原因（数字不一致/否定词不一致/两家分歧大）
        public string Primary;     // 主译文该句
        public string Secondary;   // 第二家该句（无则空）
    }

    public class CrossCheckReport
    {
        public List<CrossFlag> Flags = new List<CrossFlag>();
        public int SentenceCount;
        public bool Aligned;       // 三方句数一致才逐句比对
        public string Note = "";   // 未对齐等情况说明
    }

    /// <summary>
    /// v1.8.0 翻译交叉核对（方案第十节）：量一致性与风险点，不产出「准确率」。
    /// 硬核对：数字必须对上、否定词必须同向；软核对：两家译文相似度过低标分歧。
    /// </summary>
    public static class CrossChecker
    {
        private static readonly Regex NumRe = new Regex("\\d+(?:[.,]\\d+)*%?");

        public static List<string> SplitSentences(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(text)) return result;
            foreach (var para in text.Replace("\r\n", "\n").Split(new[] { '\n' }))
            {
                string buf = "";
                foreach (char c in para)
                {
                    buf += c;
                    if (c == '。' || c == '！' || c == '？' || c == '!' || c == '?' || c == '；' || c == ';' || c == '.')
                    {
                        if (buf.Trim().Length > 0) result.Add(buf.Trim());
                        buf = "";
                    }
                }
                if (buf.Trim().Length > 0) result.Add(buf.Trim());
            }
            return result;
        }

        public static List<string> ExtractNumbers(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;
            foreach (Match m in NumRe.Matches(text))
            {
                string n = m.Value.Replace(",", "");
                if (n.Length > 0 && !list.Contains(n)) list.Add(n);
            }
            return list;
        }

        private static readonly string[] EnNeg = { "not", "no", "never", "none", "nothing", "without", "n't" };
        private static readonly char[] ZhNeg = { '不', '没', '无', '未', '别', '非' };

        private static bool HasNegation(string sentence, bool isZh)
        {
            if (string.IsNullOrEmpty(sentence)) return false;
            if (isZh)
            {
                foreach (var c in ZhNeg) if (sentence.IndexOf(c) >= 0) return true;
                return false;
            }
            string low = " " + sentence.ToLowerInvariant() + " ";
            foreach (var w in EnNeg)
            {
                if (w == "n't") { if (low.Contains("n't")) return true; }
                else if (low.Contains(" " + w + " ") || low.Contains(" " + w + ",") || low.Contains(" " + w + ".")) return true;
            }
            return false;
        }

        private static bool IsZhText(string s)
        {
            return s != null && Regex.IsMatch(s, "[\u4e00-\u9fff]");
        }

        /// <summary>字符二元组（CJK）或词集（拉丁）的 Jaccard 相似度，0~1。</summary>
        public static double Similarity(string a, string b)
        {
            var sa = TokenSet(a);
            var sb = TokenSet(b);
            if (sa.Count == 0 || sb.Count == 0) return 0;
            int inter = 0;
            foreach (var t in sa) if (sb.Contains(t)) inter++;
            int union = sa.Count + sb.Count - inter;
            return union > 0 ? inter / (double)union : 0;
        }

        private static HashSet<string> TokenSet(string s)
        {
            var set = new HashSet<string>();
            if (string.IsNullOrEmpty(s)) return set;
            s = s.ToLowerInvariant();
            if (IsZhText(s))
            {
                string chars = Regex.Replace(s, "[^\\u4e00-\u9fff0-9a-z]", "");
                for (int i = 0; i + 1 < chars.Length; i++) set.Add(chars.Substring(i, 2));
                if (chars.Length == 1) set.Add(chars);
            }
            else
            {
                foreach (var w in Regex.Split(s, "[^a-z0-9]+"))
                    if (w.Length > 0) set.Add(w);
            }
            return set;
        }

        public static CrossCheckReport Check(string srcText, string primary, string secondary)
        {
            var report = new CrossCheckReport();
            var srcSents = SplitSentences(srcText);
            var priSents = SplitSentences(primary);
            var secSents = secondary != null ? SplitSentences(secondary) : new List<string>();
            report.SentenceCount = srcSents.Count;
            report.Aligned = srcSents.Count > 0 && priSents.Count == srcSents.Count &&
                (secondary == null || secSents.Count == srcSents.Count);
            bool priZh = IsZhText(primary);
            bool srcZh = IsZhText(srcText);

            if (!report.Aligned)
            {
                report.Note = "句数未对齐（原文 " + srcSents.Count + " 句 / 主译 " + priSents.Count +
                    (secondary != null ? " / 第二家 " + secSents.Count : "") + "），仅做全文数字核对";
                var srcNums = ExtractNumbers(srcText);
                var priNums = ExtractNumbers(primary);
                foreach (var n in srcNums)
                    if (!priNums.Contains(n))
                        report.Flags.Add(new CrossFlag { Index = -1, Reason = "数字不一致：原文有 " + n + "，主译文未见", Primary = "", Secondary = "" });
                return report;
            }

            for (int i = 0; i < srcSents.Count; i++)
            {
                string s = srcSents[i], p = priSents[i];
                string q = secondary != null ? secSents[i] : null;
                // 数字硬核对
                foreach (var n in ExtractNumbers(s))
                {
                    if (!ExtractNumbers(p).Contains(n))
                        report.Flags.Add(new CrossFlag { Index = i, Reason = "数字不一致：原文有 " + n + "，主译文未见", Primary = p, Secondary = q ?? "" });
                    else if (q != null && !ExtractNumbers(q).Contains(n))
                        report.Flags.Add(new CrossFlag { Index = i, Reason = "数字不一致：第二家译文缺 " + n, Primary = p, Secondary = q });
                }
                // 否定词同向核对（主译文一侧）
                bool srcNeg = HasNegation(s, srcZh);
                bool priNeg = HasNegation(p, priZh);
                if (srcNeg != priNeg)
                    report.Flags.Add(new CrossFlag
                    {
                        Index = i,
                        Reason = srcNeg ? "否定词不一致：原文有否定，主译文未见" : "否定词不一致：原文无否定，主译文多了否定",
                        Primary = p,
                        Secondary = q ?? ""
                    });
                // 两家分歧（软核对）
                if (q != null && Similarity(p, q) < 0.30)
                    report.Flags.Add(new CrossFlag { Index = i, Reason = "两家译法分歧大，建议人工复核", Primary = p, Secondary = q });
            }
            return report;
        }
    }

    /// <summary>v1.8.0 交叉核对的运行结果：Note 给对照卡多行展示，Summary 给状态栏一行摘要。</summary>
    public class CrossCheckOutcome
    {
        public bool Available;      // 第二引擎成功跑完并完成比对
        public string SecondName = "";
        public CrossCheckReport Report;
        public string Note;         // 存疑时非空
        public string Summary = ""; // 如「核对：2 处存疑」「核对通过」「核对不可用：…」
    }

    /// <summary>
    /// v1.8.0 交叉核对编排：选第二引擎→幕后同翻→CrossChecker 比对→（可选）对存疑句回译抽查。
    /// 全程不改主译文；任何失败都收敛成 Summary 说明，不抛给调用方。
    /// </summary>
    public static class CrossCheckRunner
    {
        public static string DisplayNameOf(string prefer)
        {
            switch (prefer)
            {
                case "youdao": return "有道";
                case "bing": return "必应";
                case "gtx": return "谷歌";
                case "mymemory": return "MyMemory";
                case "glm": return "AI";
                default:
                    var info = MultiEngine.InfoOf(prefer);
                    return info != null ? info.Name : prefer;
            }
        }

        public static async Task<CrossCheckOutcome> Run(TranslationEngine engine, AppSettings effective,
            string srcText, string primaryText, string srcLang, string dstLang, string primaryPrefer, bool backEnabled)
        {
            var o = new CrossCheckOutcome();
            try
            {
                if (engine == null || string.IsNullOrWhiteSpace(srcText) || string.IsNullOrWhiteSpace(primaryText))
                { o.Summary = "核对不可用：缺少原文或译文"; return o; }
                string primaryUsed = primaryPrefer == "auto" ? engine.ActiveProvider : primaryPrefer;
                string second = EngineCatalog.PickSecondEngine(primaryUsed, effective);
                if (second == null) { o.Summary = "核对不可用：没有可用的第二引擎"; return o; }
                o.SecondName = DisplayNameOf(second);
                string secondText;
                try
                {
                    var r = await engine.Translate(srcText, srcLang, dstLang, second).ConfigureAwait(false);
                    secondText = r.Text;
                }
                catch (Exception ex)
                {
                    o.Summary = "核对不可用（第二引擎" + o.SecondName + "：" + ex.Message + "）";
                    return o;
                }
                var report = CrossChecker.Check(srcText, primaryText, secondText);
                o.Report = report;
                o.Available = true;

                if (backEnabled && report.Aligned && report.Flags.Count > 0)
                {
                    var srcSents = CrossChecker.SplitSentences(srcText);
                    var priSents = CrossChecker.SplitSentences(primaryText);
                    int done = 0;
                    foreach (var f in report.Flags)
                    {
                        if (done >= 3) break;
                        if (f.Index < 0 || f.Index >= srcSents.Count || f.Index >= priSents.Count) continue;
                        try
                        {
                            var bt = await engine.Translate(priSents[f.Index], dstLang, srcLang, second).ConfigureAwait(false);
                            double sim = CrossChecker.Similarity(srcSents[f.Index], bt.Text);
                            f.Reason += sim >= 0.30 ? "；回译抽查基本一致" : "；回译抽查差异大";
                            done++;
                        }
                        catch { /* 回译失败就不给该句下回译结论 */ }
                    }
                }

                if (report.Flags.Count > 0)
                {
                    o.Summary = "核对：" + report.Flags.Count + " 处存疑（第二引擎：" + o.SecondName + "）";
                    var sb = new StringBuilder();
                    sb.Append("⚠ 交叉核对发现 ").Append(report.Flags.Count).Append(" 处存疑（第二引擎：").Append(o.SecondName).Append("，仅供复核，不代表判错）：");
                    int shown = 0;
                    foreach (var f in report.Flags)
                    {
                        if (shown >= 5) { sb.Append("\n· …其余 ").Append(report.Flags.Count - shown).Append(" 处略"); break; }
                        sb.Append("\n· ");
                        if (f.Index >= 0) sb.Append("第 ").Append(f.Index + 1).Append(" 句 ");
                        sb.Append(f.Reason);
                        if (!string.IsNullOrEmpty(f.Secondary))
                        {
                            string sec = f.Secondary.Length > 60 ? f.Secondary.Substring(0, 60) + "…" : f.Secondary;
                            sb.Append("（第二家：").Append(sec).Append("）");
                        }
                        shown++;
                    }
                    if (!report.Aligned && report.Note.Length > 0) sb.Append("\n· ").Append(report.Note);
                    o.Note = sb.ToString();
                }
                else
                {
                    o.Summary = "核对通过（第二引擎：" + o.SecondName + "）";
                    if (!report.Aligned && report.Note.Length > 0)
                        o.Note = "交叉核对（第二引擎：" + o.SecondName + "）：" + report.Note + "，未发现数字出入。";
                }
                return o;
            }
            catch (Exception ex)
            {
                o.Available = false;
                o.Note = null;
                o.Summary = "核对不可用：" + ex.Message;
                return o;
            }
        }
    }
}
