using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace WinTranslator
{
    public class OcrException : Exception
    {
        public OcrException(string msg) : base(msg) { }
    }

/// <summary>v1.3.0：带坐标的文字块（原位覆盖译图用）。Rect 为原图坐标，Text 为块内多行文本。</summary>
public class OcrBlock
    {
    public Rectangle Rect;
    public string Text;
    /// <summary>v1.4.0（照有道 resRegions 数据模型）：段内行数与平均行高（原图坐标），供回填排版定字号/行距。</summary>
    public int LineCount;
    public double LineHeight;
    /// <summary>v1.7.5：组段后的区域块带上组成它的原始块清单（MergeParagraphs 填），供逐行回填按原行位置绘制。</summary>
    public System.Collections.Generic.List<OcrBlock> Parts;
    /// <summary>v1.7.5：逐行框（原图坐标，与 Text 的行一一对应），识别组装时按行带记录，供逐行回填精确定位每行左界与行框。</summary>
    public System.Collections.Generic.List<Rectangle> LineRects;
    /// <summary>v1.8.2：两遍冲突合并出的块置此标记，重识环节强制整片复核（不看触发分）。</summary>
    public bool ForceRescue;
    }

    /// <summary>本地 Tesseract OCR：exe 同级 tesseract 文件夹（便携版）优先，其次系统 PATH。</summary>
    public static class OcrService
    {
        /// <summary>诊断日志钩子（MainForm 注入写文件；默认空操作）。</summary>
        public static Action<string> Log = delegate { };

        private static void Diag(string msg) { try { Log(msg); } catch { } }
        public static string FindTesseract(out string tessdataDir)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string bundled = Path.Combine(baseDir, "tesseract", "tesseract.exe");
            if (File.Exists(bundled))
            {
                tessdataDir = Path.Combine(baseDir, "tesseract", "tessdata");
                return bundled;
            }
            tessdataDir = null;
            if (File.Exists("/usr/bin/tesseract")) return "/usr/bin/tesseract"; // 开发/测试回退
            return null;
        }

        // 按已装模型拼 -l 参数（缺哪个模型就不带哪个，避免 tesseract 报错）
        internal static string BuildLangArg(string tessdataDir)
        {
            string langs = "chi_sim+eng";
            if (tessdataDir != null)
            {
                if (File.Exists(Path.Combine(tessdataDir, "jpn.traineddata"))) langs += "+jpn";
                if (File.Exists(Path.Combine(tessdataDir, "kor.traineddata"))) langs += "+kor";
            }
            return langs;
        }

        public static string Recognize(Bitmap image)
        {
            string tessdataDir;
            string exe = FindTesseract(out tessdataDir);
            if (exe == null) throw new OcrException("找不到 Tesseract OCR 引擎（tesseract 文件夹缺失）。");

            string tmp = Path.Combine(Path.GetTempPath(), "wintranslator2_ocr.png");
            using (var prepared = Preprocess(image))
                prepared.Save(tmp, ImageFormat.Png);
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "\"" + tmp + "\" stdout -l " + BuildLangArg(tessdataDir) + " --psm 6" +
                                (tessdataDir != null ? " --tessdata-dir \"" + tessdataDir + "\"" : ""),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                using (var proc = Process.Start(psi))
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    if (!proc.WaitForExit(60000))
                    {
                        try { proc.Kill(); } catch { }
                        throw new OcrException("OCR 识别超时。");
                    }
                    if (proc.ExitCode != 0) throw new OcrException("OCR 识别失败（退出码 " + proc.ExitCode + "）。");
                    return CleanText(output);
                }
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
        }


        /// <summary>
        /// v1.3.0 原位覆盖：带坐标识别。走 Tesseract 的 TSV 输出，按（区段 block、段落 par）聚合为块，
        /// 坐标从预处理图换算回原图。识别不到任何块时返回空表，调用方回退普通流程。
        /// </summary>
        public static List<OcrBlock> RecognizeBlocks(Bitmap image)
        {
            List<Rectangle> ignored;
            return RecognizeBlocks(image, out ignored);
        }

        /// <summary>
        /// v1.7.5：protectedRects 输出被过滤掉的非文字元素框（符号/@提及/图标/人像，原图坐标），
        /// 供合成译图时挖空避让、像素原样保留。
        /// </summary>
        public static List<OcrBlock> RecognizeBlocks(Bitmap image, out List<Rectangle> protectedRects)
        {
            protectedRects = new List<Rectangle>();
            // 主遍 psm 6（常规版面）＋补漏遍 psm 11（稀疏文字）：纹理/照片底上的大标题在 psm 6 下会被
            // 单词置信误标 0 分、整行丢弃（2026-11-01 海报实测：TEXTURED/GLASS 高对比大字 conf=0），
            // psm 11 同图给 96 分。两遍合并，与已有块明显重叠的补漏块不收（防重复盖图）。
            // 先用中英双语识别（日韩模型会把假名/谚文碎字混进中英文，是实测揉团主因）；
            // 两遍都空时再用已装的全部语种重试（保住日韩截图）。
            // 注：psm 11 无版面结构，其 TSV 的 block/par/line 编号全是 1（实测），解析层不能依赖编号分组，
            // 只能靠下方按坐标重建行——编号分组仅对 psm 6 成立。
            var blocks = RecognizeBlocksWithLangs(image, "chi_sim+eng", 6, protectedRects);
            Diag("blocks pass1(chi_sim+eng,psm6): " + blocks.Count);
            try
            {
                // 补漏合并口径：按"词"判重而非按块——主块常混着同区域的噪声词（如标题行的 "? 4"、纹理误读），
                // 整块判重会把补漏遍认出的正经词连坐丢弃；只丢与已有块同行且文本相同的词行，剩余部分自成新块。
                var extra = RecognizeBlocksWithLangs(image, "chi_sim+eng", 11, protectedRects);
                int added = 0;
                foreach (var eb in extra)
                {
                    // 判重：eb 被已有块覆盖 ≥85% 且词集大半相同 → 同一处文字，不重复收；
                    // 词序在两遍间常不同（阅读序差异），故比词集不比整串。
                    bool contained = false;
                    var ebWords = WordSet(eb.Text);
                    foreach (var b in blocks)
                    {
                        var inter = Rectangle.Intersect(b.Rect, eb.Rect);
                        if (inter.Width <= 0 || inter.Height <= 0) continue;
                        if (inter.Width * (double)inter.Height < 0.85 * eb.Rect.Width * (double)eb.Rect.Height) continue;
                        var bWords = WordSet(b.Text);
                        int hit = 0;
                        foreach (var w in ebWords) if (bWords.Contains(w)) hit++;
                        // 覆盖率按交集占较小块面积的比例也要过半（eb 可能只是擦到大块一角）；
                        // 且词集命中需 ≥60%（阅读序无关，防海报那种同行不同词的误杀）
                        double coverSmall = inter.Width * (double)inter.Height / Math.Min(b.Rect.Width * (double)b.Rect.Height, eb.Rect.Width * (double)eb.Rect.Height);
                        if (ebWords.Count > 0 && hit * 5 >= ebWords.Count * 3 && coverSmall >= 0.5) { contained = true; break; }
                    }
                    if (contained) continue;
                    blocks.Add(eb);
                    added++;
                }
                if (added > 0)
                {
                    blocks.Sort((a, b) => a.Rect.Top != b.Rect.Top ? a.Rect.Top.CompareTo(b.Rect.Top) : a.Rect.Left.CompareTo(b.Rect.Left));
                    Diag("blocks pass2(psm11) added: " + added);
                }
            }
            catch (Exception ex) { Diag("blocks pass2(psm11) failed: " + ex.Message); }
            // v1.8.3 二值化正式遍（真机 CGTN 帧实测）：发光/描边字幕在灰度直读时字母被光晕
            // 粘连成团（整行糊读），双芯二值化（亮芯>205 与暗芯<70 都成黑字、中间调成白底）
            // 后字母分离、整行读通。仅在已有块为空或含低可信块时启用，候选块按可信度仲裁：
            // 与已有块同区域（覆盖候选 ≥50%）时，可信度高出 0.15 以上才换文本；不重叠且
            // 可信的候选才新增——不让二值化遍的纹理垃圾反向污染（其垃圾由下方纹理门收口）。
            try
            {
                // 触发口径（实测收紧）：已有任一可信块时不启用——主线对可读版面本来就稳，
                // 二值化遍的框不可靠（2 倍图上行分组会把字幕带下方的路面标线并成"行"，
                // 框高翻倍），只在整图全军覆没（无块或全是低可信块）时作兜底安全网。
                bool anyPlausible = false;
                foreach (var b in blocks) if (Plausibility(b.Text) >= 0.45) { anyPlausible = true; break; }
                if (!anyPlausible)
                {
                    var cand = BinarizedCandidateBlocks(image);
                    int added = 0, swapped = 0;
                    foreach (var cb in cand)
                    {
                        if (Plausibility(cb.Text) < 0.45) continue;
                        OcrBlock rival = null; double bestOv = 0;
                        foreach (var b in blocks)
                        {
                            var inter = Rectangle.Intersect(b.Rect, cb.Rect);
                            if (inter.Width <= 0 || inter.Height <= 0) continue;
                            double ov = inter.Width * (double)inter.Height / Math.Max(1, cb.Rect.Width * (double)cb.Rect.Height);
                            if (ov > bestOv) { bestOv = ov; rival = b; }
                        }
                        if (rival != null && bestOv >= 0.5)
                        {
                            // 同区域：只借文本，几何永远用已有块的（候选框不可信，见上）。
                            if (Plausibility(cb.Text) > Plausibility(rival.Text) + 0.15)
                            {
                                rival.Text = cb.Text;
                                swapped++;
                            }
                        }
                        else
                        {
                            // 全新区：收，但清掉逐行框（行分组不可信，合成时按行数均分），
                            // 且块高按行高×行数封顶，防松框把填充铺出文字带。
                            cb.LineRects = null;
                            if (cb.LineHeight > 0 && cb.LineCount > 0
                                && cb.Rect.Height > cb.LineCount * cb.LineHeight * 1.6)
                            {
                                int h = (int)(cb.LineCount * cb.LineHeight * 1.3);
                                cb.Rect = new Rectangle(cb.Rect.Left, cb.Rect.Top, cb.Rect.Width, Math.Max(10, h));
                            }
                            blocks.Add(cb);
                            added++;
                        }
                    }
                    if (added > 0 || swapped > 0)
                    {
                        blocks.Sort((a, b) => a.Rect.Top != b.Rect.Top ? a.Rect.Top.CompareTo(b.Rect.Top) : a.Rect.Left.CompareTo(b.Rect.Left));
                        Diag("blocks binarized pass: added=" + added + " swapped=" + swapped);
                    }
                }
            }
            catch (Exception ex) { Diag("blocks binarized pass failed: " + ex.Message); }
            if (blocks.Count == 0)
            {
                string tessdataDir;
                FindTesseract(out tessdataDir);
                string full = BuildLangArg(tessdataDir);
                if (full != "chi_sim+eng")
                {
                    blocks = RecognizeBlocksWithLangs(image, full, 6, protectedRects);
                    Diag("blocks pass3(" + full + "): " + blocks.Count);
                }
            }
            // v1.8.2 块级收尾（梗图实测教训，2026-11-04）：两遍合并后仍有三类病——
            // ①同区域两遍各出一块、文字不同（描边字两遍读法不一）→ 按词集判重、留可读性高的一块；
            // ②同一行被拆成多块（组段 CanMerge 只并上下行、并不了同行碎块）→ 同行碎块先并回一行；
            // ③照片纹理幻觉块（大行高＋短杂合文字，如西装纹理读成「二三二 ‘a Py,」翻成「爸爸」盖人脸）→ 滤掉；
            // ④拉丁块可读性过低（描边/艺术字粘连）→ 对该行裁图做亮芯提取重识，可读性明显更高才换文本。
            blocks = CleanupBlocks(blocks, protectedRects, image);
            // v1.8.2 第三轮（CGTN 中文字幕帧实测）：整图两遍都读不到的行——半透明带透出深色
            // 背景，字形在整图二值化时与背景团块粘连被吞（黑字首行整图/整带正反两极性均 0 词，
            // 单独裁该行放大后正反都完整读出）。对已识别块的上下邻带做行墨迹扫描，发现未被
            // 任何块认领的文字行就隔离裁图放大重读，读实了才收。
            blocks = RescueAdjacentBands(image, blocks, protectedRects);
            return blocks;
        }

        /// <summary>文本可信度 0..1：成句中文（CJK ≥4）直接高分；拉丁为主看词典命中率
        /// （ScoreLatin）；其余按 CJK 个数给分。纹理垃圾的典型形态是多语种碎字＋词典零命中，
        /// 与真文本（整句中文或整行真词）分离度足够，供二值化遍仲裁与纹理门共用。</summary>
        private static double Plausibility(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            int cjk = 0, letters = 0;
            foreach (char c in text)
            {
                if (IsCjkChar(c)) cjk++;
                else if (char.IsLetter(c)) letters++;
            }
            if (cjk >= 4)
            {
                // 笔画幻觉防误判：不同字 ≤2 且某个字占过半（如车牌横线被读成
                // "了一一一一一"）不是成句中文，按低可信计。
                var freq = new Dictionary<char, int>();
                foreach (char c in text)
                {
                    if (!IsCjkChar(c)) continue;
                    int n; freq.TryGetValue(c, out n); freq[c] = n + 1;
                }
                int maxFreq = 0;
                foreach (var kv in freq) if (kv.Value > maxFreq) maxFreq = kv.Value;
                if (freq.Count <= 2 && maxFreq * 2 > cjk) return 0.25;
                return 0.9;
            }
            if (letters == 0) return cjk > 0 ? 0.5 : 0.1;
            return ScoreLatin(text);
        }

        private static int CountCjk(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int n = 0;
            foreach (char c in text) if (IsCjkChar(c)) n++;
            return n;
        }

        /// <summary>双芯二值化候选遍：整图按亮度重映射（>205 亮芯与 <70 暗芯成黑字、
        /// 中间调一律白底）并 2 倍放大，走一遍带坐标识别后把框换算回原图坐标。
        /// 中间调正是发光字的光晕、半透明带的透底与照片纹理所在——抹掉后字母分离。
        /// 只产候选，收不收由 RecognizeBlocks 的可信度仲裁与 CleanupBlocks 的纹理门决定。</summary>
        private static List<OcrBlock> BinarizedCandidateBlocks(Bitmap image)
        {
            int W = image.Width, H = image.Height;
            var result = new List<OcrBlock>();
            using (var big = new Bitmap(W * 2, H * 2))
            {
                for (int y = 0; y < H; y++)
                {
                    for (int x = 0; x < W; x++)
                    {
                        var p = image.GetPixel(x, y);
                        int g = (p.R * 299 + p.G * 587 + p.B * 114) / 1000;
                        var c = (g > 205 || g < 70) ? Color.Black : Color.White;
                        big.SetPixel(x * 2, y * 2, c);
                        big.SetPixel(x * 2 + 1, y * 2, c);
                        big.SetPixel(x * 2, y * 2 + 1, c);
                        big.SetPixel(x * 2 + 1, y * 2 + 1, c);
                    }
                }
                var scratch = new List<Rectangle>();
                var raw = RecognizeBlocksWithLangs(big, "chi_sim+eng", 6, scratch);
                foreach (var b in raw)
                {
                    b.Rect = new Rectangle(b.Rect.Left / 2, b.Rect.Top / 2,
                        Math.Max(1, b.Rect.Width / 2), Math.Max(1, b.Rect.Height / 2));
                    if (b.LineRects != null)
                    {
                        for (int i = 0; i < b.LineRects.Count; i++)
                        {
                            var r = b.LineRects[i];
                            b.LineRects[i] = new Rectangle(r.Left / 2, r.Top / 2,
                                Math.Max(1, r.Width / 2), Math.Max(1, r.Height / 2));
                        }
                    }
                    if (b.LineHeight > 0) b.LineHeight /= 2;
                    result.Add(b);
                }
            }
            return result;
        }

        private static double OverlapRatio(Rectangle inner, Rectangle outer)
        {
            var inter = Rectangle.Intersect(inner, outer);
            if (inter.Width <= 0 || inter.Height <= 0) return 0;
            double a = inner.Width * (double)inner.Height;
            return a > 0 ? inter.Width * (double)inter.Height / a : 0;
        }

        /// <summary>邻带隔离重读：以已识别块为锚，扫描其本体±1.6 倍行高的整条带（含块内——
        /// 漏网行常被偏大的块框罩住大半，只扫块外会漏检）。带内文字行逐个隔离重读，
        /// 与已有块文本重复的（已认领的行）丢弃，读出新文本的才收。防纹理幻觉靠三道门：
        /// 行带底色稳定＋墨迹极性一致（FindTextBands 内）、与锚块距离 ≤1.2 行高、
        /// 读出文本的行数/字数可读性门（ReadBandBlock/BandTextPlausible）。</summary>
        private static List<OcrBlock> RescueAdjacentBands(Bitmap image, List<OcrBlock> blocks, List<Rectangle> protectedRects)
        {
            if (blocks == null || blocks.Count == 0) return blocks;
            var found = new List<OcrBlock>();
            foreach (var anchor in new List<OcrBlock>(blocks))
            {
                double lh = anchor.LineHeight > 0 ? anchor.LineHeight : anchor.Rect.Height / (double)Math.Max(1, anchor.LineCount);
                if (lh < 8) lh = anchor.Rect.Height;
                int reach = (int)Math.Ceiling(lh * 1.6);
                int zx = Math.Max(0, anchor.Rect.Left - anchor.Rect.Width / 10);
                int zr = Math.Min(image.Width, anchor.Rect.Right + anchor.Rect.Width / 10);
                if (zr - zx < 40) continue;
                int sy0 = Math.Max(0, anchor.Rect.Top - reach);
                int sy1 = Math.Min(image.Height, anchor.Rect.Bottom + reach);
                if (sy1 - sy0 < 12) continue;
                var strip = new Rectangle(zx, sy0, zr - zx, sy1 - sy0);
                // 锚块自身的字形行（块内占主导极性的墨迹行）：候选带若大半行与之重合，
                // 就是已认领行被换个裁法重读（读花了还会绕过文本判重），按行认领直接拒。
                var anchorGlyphRow = new bool[strip.Height];
                {
                    double totD = 0, totL = 0;
                    var perRow = new List<double[]>();
                    for (int y = anchor.Rect.Top; y < anchor.Rect.Bottom; y++)
                    {
                        var lums = new List<int>();
                        for (int x = zx; x < zr; x += 2)
                        {
                            var c = image.GetPixel(x, y);
                            lums.Add((c.R * 299 + c.G * 587 + c.B * 114) / 1000);
                        }
                        if (lums.Count < 8) { perRow.Add(new double[] { 0, 0 }); continue; }
                        var sorted = new List<int>(lums);
                        sorted.Sort();
                        int med = sorted[sorted.Count / 2];
                        int dk = 0, lt = 0;
                        foreach (int l in lums) { if (l < med - 55) dk++; else if (l > med + 55) lt++; }
                        double df = dk / (double)lums.Count, lf = lt / (double)lums.Count;
                        perRow.Add(new double[] { df, lf });
                        totD += df; totL += lf;
                    }
                    bool darkDom = totD >= totL;
                    for (int i = 0; i < perRow.Count; i++)
                    {
                        double main = darkDom ? perRow[i][0] : perRow[i][1];
                        int sy = anchor.Rect.Top + i - strip.Top;
                        if (sy >= 0 && sy < strip.Height && main >= 0.02) anchorGlyphRow[sy] = true;
                    }
                }
                var stripBands = FindTextBands(image, strip);
                foreach (var band in stripBands)
                {
                    if (band.Width < Math.Max(40, anchor.Rect.Width * 0.35)) continue;
                    // 距离门：离锚块太远的带（路牌水印、别处文字）不归本锚管
                    int dyGap = band.Bottom < anchor.Rect.Top ? anchor.Rect.Top - band.Bottom
                              : band.Top > anchor.Rect.Bottom ? band.Top - anchor.Rect.Bottom : 0;
                    if (dyGap > lh * 1.2) continue;
                    bool claimed = false;
                    foreach (var b in found) if (OverlapRatio(band, b.Rect) >= 0.4) { claimed = true; break; }
                    if (!claimed && protectedRects != null) foreach (var p in protectedRects) if (OverlapRatio(band, p) >= 0.5) { claimed = true; break; }
                    if (claimed) continue;
                    // 行认领检查：候选带的行大半是锚块自己的字形行 → 已认领，拒
                    int glyphHit = 0;
                    for (int y = band.Top; y < band.Bottom; y++)
                    {
                        int sy = y - strip.Top;
                        if (sy >= 0 && sy < strip.Height && anchorGlyphRow[sy]) glyphHit++;
                    }
                    if (band.Height > 0 && glyphHit >= band.Height * 0.6) continue;
                    var nb = ReadBandBlock(image, band, stripBands);
                    if (nb == null) continue;
                    string nn = NormalizeLineText((nb.Text ?? "").Replace("\n", ""));
                    bool dupText = false;
                    foreach (var b in blocks)
                    {
                        string bn = NormalizeLineText((b.Text ?? "").Replace("\n", ""));
                        if (nn.Length >= 4 && SimilarText(nn, bn)) { dupText = true; break; }
                    }
                    if (dupText) continue;
                    Diag("band rescue: " + nb.Rect + " [" + nb.Text.Replace("\n", " / ") + "]");
                    found.Add(nb);
                }
            }
            if (found.Count == 0) return blocks;
            var all = new List<OcrBlock>(blocks);
            all.AddRange(found);
            all.Sort((a, b) => a.Rect.Top != b.Rect.Top ? a.Rect.Top.CompareTo(b.Rect.Top) : a.Rect.Left.CompareTo(b.Rect.Left));
            return all;
        }

        /// <summary>在给定区域内按行墨迹找文字行带，分暗墨、亮墨两遍扫：逐行以行中位亮度
        /// 为底，偏离 −55 的算暗墨、+55 的算亮墨；某行暗墨（或亮墨）占比 2%~65% 且至少是
        /// 另一极的两倍才算该极的文字行——同带异极性字幕（上黑下白）必须分开成带，
        /// 合在一起会因极性混杂被误判成纹理（中文字幕帧实测教训）。带内允许 ≤2 行空隙，
        /// 带高 10~110；带内行底色（中位亮度）须稳定（标准差 ≤28，纹理带底色乱跳）；
        /// 最后按列墨迹收紧左右界。</summary>
        private static List<Rectangle> FindTextBands(Bitmap image, Rectangle zone)
        {
            int rows = zone.Height;
            var rowBg = new int[rows];
            var rowDark = new double[rows];
            var rowLight = new double[rows];
            for (int i = 0; i < rows; i++)
            {
                int y = zone.Top + i;
                var lums = new List<int>();
                for (int x = zone.Left; x < zone.Right; x += 2)
                {
                    var c = image.GetPixel(x, y);
                    lums.Add((c.R * 299 + c.G * 587 + c.B * 114) / 1000);
                }
                if (lums.Count < 8) continue;
                var sorted = new List<int>(lums);
                sorted.Sort();
                int med = sorted[sorted.Count / 2];
                rowBg[i] = med;
                int dark = 0, light = 0;
                foreach (int l in lums) { if (l < med - 55) dark++; else if (l > med + 55) light++; }
                rowDark[i] = dark / (double)lums.Count;
                rowLight[i] = light / (double)lums.Count;
            }
            var bands = new List<Rectangle>();
            for (int pass = 0; pass < 2; pass++)
            {
                var texty = new bool[rows];
                for (int i = 0; i < rows; i++)
                {
                    double main = pass == 0 ? rowDark[i] : rowLight[i];
                    double other = pass == 0 ? rowLight[i] : rowDark[i];
                    texty[i] = main >= 0.02 && main <= 0.65 && main >= other * 2;
                }
                int idx = 0;
                while (idx < rows)
                {
                    if (!texty[idx]) { idx++; continue; }
                    int s = idx, last = idx, gap = 0, j = idx;
                    while (j < rows)
                    {
                        if (texty[j]) { last = j; gap = 0; }
                        else { gap++; if (gap > 2) break; }
                        j++;
                    }
                    int h = last - s + 1;
                    bool quality = false;
                    if (h >= 10 && h <= 110)
                    {
                        double mean = 0;
                        for (int i = s; i <= last; i++) mean += rowBg[i];
                        mean /= h;
                        double var = 0;
                        for (int i = s; i <= last; i++) var += (rowBg[i] - mean) * (rowBg[i] - mean);
                        quality = Math.Sqrt(var / h) <= 28;
                    }
                    if (quality)
                    {
                        // 列收紧：只数本带极性的墨行，取首尾墨列
                        int firstCol = -1, lastCol = -1;
                        for (int x = zone.Left; x < zone.Right; x++)
                        {
                            int cnt = 0;
                            for (int i = s; i <= last; i++)
                            {
                                var c = image.GetPixel(x, zone.Top + i);
                                int lum = (c.R * 299 + c.G * 587 + c.B * 114) / 1000;
                                if (pass == 0 ? lum < rowBg[i] - 55 : lum > rowBg[i] + 55) cnt++;
                            }
                            if (cnt >= Math.Max(2, h / 5)) { if (firstCol < 0) firstCol = x; lastCol = x; }
                        }
                        if (firstCol >= 0 && lastCol - firstCol + 1 >= 30)
                        {
                            int bx = Math.Max(zone.Left, firstCol - 2);
                            int bw = Math.Min(zone.Right, lastCol + 3) - bx;
                            bands.Add(new Rectangle(bx, zone.Top + s, bw, h));
                        }
                    }
                    idx = last + 1;
                }
            }
            return bands;
        }

        /// <summary>两段归一化文本是否近似重复：包含关系，或最长公共子序列占较短串 ≥75%
        /// （重读同一行常有个别字误读，如「地带」读成「地市」，纯包含判断会漏掉，
        /// 误收一块就会双块双译）。</summary>
        private static bool SimilarText(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            if (a.Contains(b) || b.Contains(a)) return true;
            int min = Math.Min(a.Length, b.Length);
            if (min < 4) return false;
            var prev = new int[b.Length + 1];
            var cur = new int[b.Length + 1];
            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                    cur[j] = a[i - 1] == b[j - 1] ? prev[j - 1] + 1 : Math.Max(prev[j], cur[j - 1]);
                var tmp = prev; prev = cur; cur = tmp;
            }
            return prev[b.Length] >= min * 0.75;
        }

        /// <summary>把一行带裁出来放大 3~4 倍重读（读不出再反色读一遍）；文本须过可读性门
        /// （中文 ≥3 字，或拉丁 ≥6 字母且词表分 ≥0.5）才收成块，防纹理幻觉。</summary>
        private static OcrBlock ReadBandBlock(Bitmap image, Rectangle band, List<Rectangle> stripBands)
        {
            // 裁图上下留白：成带阈值会切掉笔画稀疏的字形顶/底行（实测切顶约 5px 把
            // 「男孩」误读成「为」），裁图向外扩 35% 带高，但不越过与相邻带的中点，
            // 防把邻行字形带进来。
            int ext = (int)Math.Ceiling(band.Height * 0.35);
            int topLim = band.Top - ext, botLim = band.Bottom + ext;
            if (stripBands != null)
            {
                foreach (var ob in stripBands)
                {
                    if (ob.Equals(band)) continue;
                    if (ob.Bottom <= band.Top) topLim = Math.Max(topLim, (ob.Bottom + band.Top) / 2);
                    if (ob.Top >= band.Bottom) botLim = Math.Min(botLim, (ob.Top + band.Bottom) / 2);
                }
            }
            var crop = new Rectangle(band.Left - 3, topLim, band.Width + 6, botLim - topLim);
            crop.Intersect(new Rectangle(0, 0, image.Width, image.Height));
            if (crop.Width < 30 || crop.Height < 8) return null;
            int scale = band.Height <= 26 ? 4 : 3;
            // 多配方竞读：整带 psm 6 → 单行 psm 7（矮带）→ 反色 psm 6，过可读性门的候选里
            // 取字量最大的一份（小裁图在不同配方下错字位置不同，实测同一行 psm 6 把
            // 「男孩」读成「男了」、隔离实验配方则全对）。
            var attempts = new List<int[]> { new[] { 0, 6 } };
            if (band.Height <= 52) attempts.Add(new[] { 0, 7 });
            attempts.Add(new[] { 1, 6 });
            string text = null;
            int bestScore = -1;
            foreach (var att in attempts)
            {
                int invert = att[0], psm = att[1];
                using (var prep = new Bitmap(crop.Width * scale, crop.Height * scale))
                using (var g = Graphics.FromImage(prep))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    if (invert == 1)
                    {
                        var cm = new System.Drawing.Imaging.ColorMatrix(new[]
                        {
                            new[] { -1f, 0f, 0f, 0f, 0f }, new[] { 0f, -1f, 0f, 0f, 0f },
                            new[] { 0f, 0f, -1f, 0f, 0f }, new[] { 0f, 0f, 0f, 1f, 0f },
                            new[] { 1f, 1f, 1f, 0f, 1f }
                        });
                        var ia = new System.Drawing.Imaging.ImageAttributes();
                        ia.SetColorMatrix(cm);
                        g.DrawImage(image, new Rectangle(0, 0, prep.Width, prep.Height), crop.X, crop.Y, crop.Width, crop.Height, GraphicsUnit.Pixel, ia);
                        ia.Dispose();
                    }
                    else
                    {
                        g.DrawImage(image, new Rectangle(0, 0, prep.Width, prep.Height), crop, GraphicsUnit.Pixel);
                    }
                    string t = RunTesseractPlain(prep, "chi_sim+eng", psm);
                    if (!string.IsNullOrWhiteSpace(t))
                    {
                        t = CleanText(t).Trim();
                        if (BandTextPlausible(t))
                        {
                            int cjk = 0, latin = 0;
                            foreach (char c in t) { if (IsCjkChar(c)) cjk++; else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) latin++; }
                            int score = cjk * 2 + latin;
                            if (score > bestScore) { bestScore = score; text = t; }
                        }
                    }
                }
            }
            if (text == null) return null;
            var lines = new List<string>();
            foreach (var ln in text.Split(new[] { '\n' }))
            {
                // 行首尾的边框类杂符（字幕带边线常被读成 | / \ 等）先剥掉再参与判重与收录
                string s = ln.Trim().TrimStart('|', '/', '\\', ':', ';', '·', '•', ' ').TrimEnd('|', '/', '\\', ' ');
                if (s.Length > 0) lines.Add(s);
            }
            if (lines.Count == 0) return null;
            // 行数合理性门：一行带的合理行高至少十几像素；纹理裁图常被读成许多碎行
            // （实测树冠带 57px 被读出 9 行），行数与带高不匹配的整体丢弃。
            if (lines.Count > Math.Max(1, band.Height / 16)) return null;
            return new OcrBlock
            {
                Rect = band,
                Text = string.Join("\n", lines.ToArray()),
                LineCount = lines.Count,
                LineHeight = band.Height / (double)lines.Count,
                LineRects = null
            };
        }

        private static bool BandTextPlausible(string t)
        {
            int cjk = 0, latin = 0;
            foreach (char c in t)
            {
                if (IsCjkChar(c)) cjk++;
                else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) latin++;
            }
            if (cjk >= 4) return true;
            if (latin >= 6 && ScoreLatin(t) >= 0.5) return true;
            return false;
        }

        /// <summary>行文本归一化（去标点空格、转小写），用于两遍结果判重。</summary>
        private static string NormalizeLineText(string line)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in line ?? "")
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        /// <summary>块文本的词集（归一化后按空格切分），用于两遍结果判重。</summary>
        private static System.Collections.Generic.HashSet<string> WordSet(string text)
        {
            var set = new System.Collections.Generic.HashSet<string>();
            foreach (var w in (text ?? "").Split(new[] { ' ', '\n' }))
            {
                string n = NormalizeLineText(w);
                if (n.Length > 0) set.Add(n);
            }
            return set;
        }

        /// <summary>两块是否纵向同行（中心距小于较小块高的一半）。</summary>
        private static bool SameRow(Rectangle a, Rectangle b)
        {
            double ca = a.Top + a.Height / 2.0, cb = b.Top + b.Height / 2.0;
            return Math.Abs(ca - cb) < Math.Min(a.Height, b.Height) * 0.5;
        }

        // ---------- v1.8.2 块级收尾：判重 / 同行并块 / 幻觉过滤 / 描边字重识 ----------

        private static readonly char[] TokenTrimChars = ".,;:!?\"'“”‘’()[]{}<>|/\\*_-—…·".ToCharArray();

        /// <summary>拉丁文本可读性评分：词元命中常用词表的比例（0~1）。含数字/中日韩的词元不计分母；
        /// 无可计词元时返回 1（不表态）。仅用于两遍判重取舍与重识触发/验收，不单独决定删块。</summary>
        private static double ScoreLatin(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int counted = 0, hit = 0;
            foreach (var raw in text.Replace("\n", " ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string tok = raw.ToLowerInvariant().Trim(TokenTrimChars);
                if (tok.Length == 0) continue;
                bool hasDigit = false, hasCjk = false, hasLatin = false;
                foreach (char c in tok)
                {
                    if (char.IsDigit(c)) hasDigit = true;
                    else if ((c >= 0x2E80 && c <= 0x9FFF) || (c >= 0x3040 && c <= 0x30FF) || (c >= 0xAC00 && c <= 0xD7AF)) hasCjk = true;
                    else if ((c >= 'a' && c <= 'z')) hasLatin = true;
                }
                if (hasDigit || hasCjk || !hasLatin) continue;
                counted++;
                if (CommonWords.Contains(tok) || tok == "a" || tok == "i") hit++;
            }
            if (counted == 0) return 1.0;
            return hit / (double)counted;
        }

        private static double WordJaccard(string a, string b)
        {
            var sa = WordSet(a); var sb = WordSet(b);
            if (sa.Count == 0 || sb.Count == 0) return 0;
            int inter = 0;
            foreach (var w in sa) if (sb.Contains(w)) inter++;
            int union = sa.Count + sb.Count - inter;
            return union > 0 ? inter / (double)union : 0;
        }

        private static double BlockLineHeight(OcrBlock b)
        {
            if (b.LineHeight > 0) return b.LineHeight;
            return b.Rect.Height / (double)Math.Max(1, b.LineCount);
        }

        private static List<OcrBlock> CleanupBlocks(List<OcrBlock> blocks, List<Rectangle> protectedOut, Bitmap image)
        {
            if (blocks == null || blocks.Count == 0) return blocks ?? new List<OcrBlock>();
            var list = new List<OcrBlock>(blocks);
            list.Sort((x, y) => x.Rect.Top != y.Rect.Top ? x.Rect.Top.CompareTo(y.Rect.Top) : x.Rect.Left.CompareTo(y.Rect.Left));

            // ① 判重：交叠 ≥55%（占较小块）且词集 Jaccard ≥0.5 → 同一处文字的两遍版本，
            // 留可读性高的一块（同分留长者），框取并集。描边字两遍读法不同、旧判重（词集命中 60%）
            // 放行的重叠双块在这里收口。
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < list.Count && !changed; i++)
                {
                    for (int j = i + 1; j < list.Count && !changed; j++)
                    {
                        var a = list[i]; var b = list[j];
                        var inter = Rectangle.Intersect(a.Rect, b.Rect);
                        if (inter.Width <= 0 || inter.Height <= 0) continue;
                        double minArea = Math.Min(a.Rect.Width * (double)a.Rect.Height, b.Rect.Width * (double)b.Rect.Height);
                        if (minArea <= 0 || inter.Width * (double)inter.Height < 0.55 * minArea) continue;
                        double interArea = inter.Width * (double)inter.Height;
                        double maxArea = Math.Max(a.Rect.Width * (double)a.Rect.Height, b.Rect.Width * (double)b.Rect.Height);
                        bool nearSame = maxArea > 0 && interArea >= 0.85 * minArea && interArea >= 0.85 * maxArea;
                        // 取舍：一块的词集若几乎包含另一块（≥90%），留全的（实测：推文两行版被一行版
                        // 按可读性误杀、第二行整行丢失）；否则留可读性高的（同分留长者）。
                        // 含关系本身也可触发判重（词集含关系下 Jaccard 天然偏低，如标题整片块含单行块）。
                        var setA = WordSet(a.Text); var setB = WordSet(b.Text);
                        int interW = 0;
                        foreach (var w in setA) if (setB.Contains(w)) interW++;
                        bool aSuper = setB.Count > 0 && interW >= 0.9 * setB.Count;
                        bool bSuper = setA.Count > 0 && interW >= 0.9 * setA.Count;
                        if (WordJaccard(a.Text, b.Text) < 0.5 && !nearSame && !aSuper && !bSuper) continue;
                        OcrBlock keep;
                        if (aSuper && !bSuper) keep = a;
                        else if (bSuper && !aSuper) keep = b;
                        else
                        {
                            double sa = ScoreLatin(a.Text), sb = ScoreLatin(b.Text);
                            keep = (sb > sa || (sb == sa && (b.Text ?? "").Length > (a.Text ?? "").Length)) ? b : a;
                        }
                        var drop = keep == a ? b : a;
                        keep.Rect = Rectangle.Union(a.Rect, b.Rect);
                        if (keep.LineRects != null && drop.LineRects != null && keep.LineRects.Count + drop.LineRects.Count == keep.LineCount + drop.LineCount)
                        {
                            var lr = new List<Rectangle>(keep.LineRects); lr.AddRange(drop.LineRects); keep.LineRects = lr;
                        }
                        else keep.LineRects = null;
                        keep.LineCount = Math.Max(keep.LineCount, drop.LineCount);
                        Diag("cleanup dedupe: drop [" + drop.Text + "] keep [" + keep.Text + "]");
                        list.Remove(drop);
                        changed = true;
                    }
                }
            }

            // ①b 冲突双块合并：几何上是同一处（交叠 ≥55%）但文字互不包含、词集也对不上（Jaccard <0.5）
            // → 两遍对同一片文字的两种错读（如视频字幕被两遍读成两套乱序词）。各画一块只会叠出两坨
            // 乱码贴片；并成一块（框取并集、文本先取可读性高者），交给 ④ 按整片重识裁决。
            changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < list.Count && !changed; i++)
                {
                    for (int j = i + 1; j < list.Count && !changed; j++)
                    {
                        var a = list[i]; var b = list[j];
                        var inter = Rectangle.Intersect(a.Rect, b.Rect);
                        if (inter.Width <= 0 || inter.Height <= 0) continue;
                        double minArea = Math.Min(a.Rect.Width * (double)a.Rect.Height, b.Rect.Width * (double)b.Rect.Height);
                        if (minArea <= 0 || inter.Width * (double)inter.Height < 0.55 * minArea) continue;
                        if (WordJaccard(a.Text, b.Text) >= 0.5) continue;
                        var setA = WordSet(a.Text); var setB = WordSet(b.Text);
                        int interW = 0;
                        foreach (var w in setA) if (setB.Contains(w)) interW++;
                        bool aSuper = setB.Count > 0 && interW >= 0.9 * setB.Count;
                        bool bSuper = setA.Count > 0 && interW >= 0.9 * setA.Count;
                        if (aSuper || bSuper) continue;
                        var keep = ScoreLatin(b.Text) > ScoreLatin(a.Text) ? b : a;
                        var merged = new OcrBlock
                        {
                            Rect = Rectangle.Union(a.Rect, b.Rect),
                            Text = keep.Text,
                            LineCount = Math.Max(a.LineCount, b.LineCount),
                            LineHeight = BlockLineHeight(keep),
                            LineRects = null,
                            ForceRescue = true // 两遍读法互相打架本身就是识别不可靠的证据，④ 强制整片重识裁决
                        };
                        Diag("cleanup conflictmerge: [" + a.Text + "] + [" + b.Text + "] -> [" + merged.Text + "]");
                        list.Remove(a); list.Remove(b); list.Add(merged);
                        list.Sort((x, y) => x.Rect.Top != y.Rect.Top ? x.Rect.Top.CompareTo(y.Rect.Top) : x.Rect.Left.CompareTo(y.Rect.Left));
                        changed = true;
                    }
                }
            }

            // ② 同行碎块合并：纵向交叠 ≥50%（占较小块高）、横向间隙 ≤0.6 倍较小块高、且非判重关系、
            // 且都是单行块 → 并成一行（按左到右拼文本）。梗图字幕一行被拆成三块分开翻就是缺这一步。
            changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < list.Count && !changed; i++)
                {
                    for (int j = i + 1; j < list.Count && !changed; j++)
                    {
                        var a = list[i]; var b = list[j];
                        if (a.LineCount > 1 || b.LineCount > 1) continue;
                        int vOv = Math.Min(a.Rect.Bottom, b.Rect.Bottom) - Math.Max(a.Rect.Top, b.Rect.Top);
                        int minH = Math.Min(a.Rect.Height, b.Rect.Height);
                        if (minH <= 0 || vOv < 0.5 * minH) continue;
                        int hGap = Math.Max(a.Rect.Left, b.Rect.Left) - Math.Min(a.Rect.Right, b.Rect.Right);
                        if (hGap > 0.6 * minH) continue;
                        var inter = Rectangle.Intersect(a.Rect, b.Rect);
                        double minArea = Math.Min(a.Rect.Width * (double)a.Rect.Height, b.Rect.Width * (double)b.Rect.Height);
                        if (minArea > 0 && inter.Width > 0 && inter.Width * (double)inter.Height >= 0.55 * minArea && WordJaccard(a.Text, b.Text) >= 0.5) continue;
                        var first = a.Rect.Left <= b.Rect.Left ? a : b;
                        var second = first == a ? b : a;
                        string t1 = (first.Text ?? "").Trim(), t2 = (second.Text ?? "").Trim();
                        string sep = " ";
                        if (t1.Length > 0 && t2.Length > 0 && (IsCjkChar(t1[t1.Length - 1]) || IsCjkChar(t2[0]))) sep = "";
                        var merged = new OcrBlock
                        {
                            Rect = Rectangle.Union(a.Rect, b.Rect),
                            Text = t1.Length == 0 ? t2 : (t2.Length == 0 ? t1 : t1 + sep + t2),
                            LineCount = 1,
                            LineHeight = Math.Max(BlockLineHeight(a), BlockLineHeight(b)),
                            LineRects = new List<Rectangle> { Rectangle.Union(a.Rect, b.Rect) }
                        };
                        Diag("cleanup rowmerge: [" + a.Text + "] + [" + b.Text + "] -> [" + merged.Text + "]");
                        list.Remove(a); list.Remove(b); list.Add(merged);
                        list.Sort((x, y) => x.Rect.Top != y.Rect.Top ? x.Rect.Top.CompareTo(y.Rect.Top) : x.Rect.Left.CompareTo(y.Rect.Left));
                        changed = true;
                    }
                }
            }

            // ③ 幻觉块过滤：全图拉丁字母占优时，一块若行高 ≥1.6 倍全图行高中位数、字母总数 ≤8、
            // 其中中日韩字母占一半以上、且无数字 → 照片纹理误读（如西装纹理「二三二 ‘a Py,」），
            // 删块并把框记入保护区（不贴片、像素原样）。带数字的「11小时」类真文字不受影响。
            if (list.Count >= 2)
            {
                var hs = new List<double>();
                int totLatin = 0, totCjk = 0;
                foreach (var b in list)
                {
                    hs.Add(BlockLineHeight(b));
                    foreach (char c in b.Text ?? "")
                    {
                        if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) totLatin++;
                        else if (IsCjkChar(c)) totCjk++;
                    }
                }
                hs.Sort();
                double med = hs[(hs.Count - 1) / 2]; // 取低中位数：块数少时高中位数会被大块本身顶满（如全图只剩 2 块时取到幻觉块自己的行高，门槛失效）
                if (med > 0 && totLatin > totCjk)
                {
                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        var b = list[i];
                        int letters = 0, cjk = 0; bool hasDigit = false;
                        foreach (char c in b.Text ?? "")
                        {
                            if (char.IsLetter(c)) { letters++; if (IsCjkChar(c)) cjk++; }
                            if (char.IsDigit(c)) hasDigit = true;
                        }
                        if (letters >= 3 && letters <= 8 && cjk * 2 >= letters && !hasDigit && BlockLineHeight(b) >= med * 1.6)
                        {
                            Diag("cleanup hallucination drop: [" + b.Text + "] rect=" + b.Rect);
                            if (protectedOut != null) protectedOut.Add(b.Rect);
                            list.RemoveAt(i);
                        }
                    }
                }
            }

            // ③b 短乱码块过滤：拉丁可读性为 0（无一词元命中常用词表）、拉丁词元 ≤3、字母总数 ≤10、
            // 中日韩字 ≤1、面积 ≤全图 5% → 纹理/徽标曲线文字的误读（实测：徽章上的「Nome SY」、
            // 路面纹理「ra ear」、队徽碎字）。删块并记保护区。纯数字/纯中文块可读性记 1（不表态）
            // 不受影响；真短词靠词表覆盖保下（如 Jobs、PRESS RELEASE 靠字母数 >10 豁免）。
            {
                double imgArea = image.Width * (double)image.Height;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var b = list[i];
                    if (b.Rect.Width * (double)b.Rect.Height > 0.05 * imgArea) continue;
                    int letters = 0, cjk = 0, latinTokens = 0;
                    foreach (char c in b.Text ?? "")
                    {
                        if (char.IsLetter(c)) { letters++; if (IsCjkChar(c)) cjk++; }
                    }
                    if (letters > 10 || cjk > 1) continue;
                    // 域名类文本（daimg.com、xx.com 站点水印）不是乱码，豁免
                    bool domainLike = false;
                    foreach (var raw in (b.Text ?? "").Replace("\n", " ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string tok = raw.ToLowerInvariant().Trim(TokenTrimChars);
                        int dot = tok.LastIndexOf('.');
                        if (dot >= 2 && tok.Length - dot - 1 >= 2) { domainLike = true; break; }
                    }
                    if (domainLike) continue;
                    foreach (var raw in (b.Text ?? "").Replace("\n", " ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string tok = raw.ToLowerInvariant().Trim(TokenTrimChars);
                        if (tok.Length == 0) continue;
                        bool hasCjkT = false, hasLatinT = false;
                        foreach (char c in tok) { if (IsCjkChar(c)) hasCjkT = true; else if (c >= 'a' && c <= 'z') hasLatinT = true; }
                        if (hasLatinT && !hasCjkT) latinTokens++;
                    }
                    if (latinTokens == 0 || latinTokens > 3) continue;
                    if (ScoreLatin(b.Text) > 0) continue;
                    Diag("cleanup gibberish drop: [" + b.Text + "] rect=" + b.Rect);
                    if (protectedOut != null) protectedOut.Add(b.Rect);
                    list.RemoveAt(i);
                }
            }

            // ④ 描边字重识：单行拉丁块可读性 <0.6 且词元 ≥3、拉丁字母 ≥12 → 裁该行做亮芯提取
            // （高亮像素为字芯、二值化＋膨胀，专克白字黑描边）重识，两档阈值取可读性最高者，
            // 须比原文本高 0.10 以上才替换（防把好文本换坏）。
            foreach (var b in list)
            {
                try { RescueBlock(image, b); }
                catch (Exception ex) { Diag("cleanup rescue failed: " + ex.Message); }
            }

            // ⑤ 保护框冲突消解：保护框与保留块交叠 ≥50%（占保护框面积）且未被块完全包含（探出块界）
            // → 是另一遍对同一处文字的误判（实测：字幕首词被 psm11 遍误滤成图形保护框，合成时把
            // 该词从贴片跨度里挖掉、原文残留在译图上）。保留块已认定此处为文字，保护框作废；
            // 完全落在块内的保护框（行间国旗/徽章）与块外交叠不足的（行侧头像）不受影响。
            if (protectedOut != null && list.Count > 0)
            {
                for (int i = protectedOut.Count - 1; i >= 0; i--)
                {
                    var p = protectedOut[i];
                    double pa = p.Width * (double)p.Height;
                    if (pa <= 0) continue;
                    bool drop = false;
                    foreach (var b in list)
                    {
                        var inter = Rectangle.Intersect(p, b.Rect);
                        if (inter.Width <= 0 || inter.Height <= 0) continue;
                        if (inter.Width * (double)inter.Height < 0.5 * pa) continue;
                        bool contained = p.Left >= b.Rect.Left && p.Top >= b.Rect.Top &&
                                         p.Right <= b.Rect.Right && p.Bottom <= b.Rect.Bottom;
                        if (!contained) { drop = true; break; }
                    }
                    if (drop)
                    {
                        Diag("cleanup protected prune: " + p);
                        protectedOut.RemoveAt(i);
                    }
                }
            }

            // ⑥ 墨迹延伸：把每块的行框按像素墨迹四边追出去——漏识的词没有任何框、被框 clip
            // 掉的字形升部也没有，几何覆盖看不见它们，只能以像素为准（实测司法部标题行尾
            // 整词残留、衬线竖笔碎片戳在贴片外）。无行框的块（重识后）几何已由重识管线负责，跳过。
            foreach (var b in list)
            {
                if (b.LineRects == null || b.LineCount <= 0 || b.LineRects.Count != Math.Max(1, b.LineCount)) continue;
                // 尺度用行距（块高÷行数）：多行块的 LineHeight 字段可能是单行框高，与词间
                // 空隙的比例关系对不上（实测标题行距 47.5、单行框高 36，容忍量因此偏小）。
                double blh = b.LineCount > 1 ? b.Rect.Height / (double)b.LineCount
                             : (b.LineHeight > 0 ? b.LineHeight : b.Rect.Height);
                var imgRect = new Rectangle(0, 0, image.Width, image.Height);
                var nl = new List<Rectangle>();
                var union = b.Rect;
                foreach (var lr in b.LineRects)
                {
                    var ex = ExtendRectByInk(image, lr, blh, imgRect);
                    nl.Add(ex);
                    union = Rectangle.Union(union, ex);
                }
                b.LineRects = nl;
                b.Rect = union;
            }

            // ⑦ 保护框复核（延伸后）：两类误保护要撤销——
            // (a) 行内微型框（宽高 ≤0.35 行距）：字母点/标点碎片（实测标题里 i 的点 7×7 被保护，
            //     填充与绘字都绕开它、原样留在译图上）；
            // (b) 整词型框（与块重叠 ≥50%、高在 0.5~1.15 行距间、宽 ≥2.2 倍高）：漏识的整词
            //     被当成图形保护了（实测标题行尾整词 233×45 被保护、永不被盖）。
            // 徽章/旗帜/印章宽高比远小于 2.2，不受 (b) 影响；微型框阈值也够不到徽章尺寸。
            if (protectedOut != null && protectedOut.Count > 0)
            {
                for (int i = protectedOut.Count - 1; i >= 0; i--)
                {
                    var p = protectedOut[i];
                    bool drop = false;
                    foreach (var b in list)
                    {
                        double pitch = b.Rect.Height / (double)Math.Max(1, b.LineCount);
                        bool inside = p.Left >= b.Rect.Left && p.Top >= b.Rect.Top && p.Right <= b.Rect.Right && p.Bottom <= b.Rect.Bottom;
                        if (inside && p.Width <= 0.35 * pitch && p.Height <= 0.35 * pitch) { drop = true; break; }
                        var inter = Rectangle.Intersect(p, b.Rect);
                        if (inter.Width > 0 && inter.Height > 0)
                        {
                            double interArea = inter.Width * (double)inter.Height;
                            double pArea = Math.Max(1, p.Width * (double)p.Height);
                            if (interArea >= 0.5 * pArea && p.Height >= 0.5 * pitch && p.Height <= 1.15 * pitch && p.Width >= 2.2 * p.Height)
                            { drop = true; break; }
                            // (d) 多行文字带误保护（v1.8.3 真机 CGTN 帧实测）：某个已删垃圾块的框
                            // （或照片区误判）横跨已验真文字块的两行左半（125×54、2 倍行距高），
                            // 合成时把该块的可用跨切掉一半、译文只能挤在右半与原文混排。
                            // 高 ≥1.5 行距、过半面积落进可信块（可信度 ≥0.45）内 → 是文字带不是
                            // 图形，撤销。头像类保护框落在文字块外（交叠不过半），不受影响。
                            if (interArea >= 0.5 * pArea && p.Height >= 1.5 * pitch && Plausibility(b.Text) >= 0.45)
                            { drop = true; break; }
                        }
                        // (c) 行内同墨色碎片：中心落在某行框内、高 0.4~1.2 行距、且主墨色与该行
                        // 文字同色（距 <60）→ 漏识的词/字（如行尾短词），不是图形。徽章等真图形
                        // 墨色与文字色距离远，不受影响。
                        if (b.LineRects != null)
                        {
                            foreach (var lr in b.LineRects)
                            {
                                var band = lr;
                                band.Inflate(0, (int)(0.25 * pitch));
                                var pc = new Point(p.Left + p.Width / 2, p.Top + p.Height / 2);
                                if (!band.Contains(pc)) continue;
                                // 高度窗放宽到 0.2~1.3 行距：被切成上下片的字形碎片（如字幕首词
                                // 只有上半截被当图形）也算；真伪主要靠下面的同墨色判据把关。
                                if (p.Height < 0.2 * pitch || p.Height > 1.3 * pitch) continue;
                                int pr, pg, pb2, br, bg2, bb2;
                                if (TryInkColor(image, p, out pr, out pg, out pb2) && TryInkColor(image, lr, out br, out bg2, out bb2))
                                {
                                    int dr = pr - br, dg = pg - bg2, db = pb2 - bb2;
                                    if (Math.Sqrt(dr * dr + dg * dg + db * db) < 60) { drop = true; }
                                }
                                if (drop) break;
                            }
                            if (drop) break;
                        }
                    }
                    if (drop)
                    {
                        Diag("cleanup protected drop (text-like): " + p);
                        protectedOut.RemoveAt(i);
                    }
                }
            }

            // ⑧ 纹理垃圾门（v1.8.3，婚礼人群帧实测）：照片纹理被读成整幅大块垃圾文本
            // （516×296 的人群带只读出 "a aie bi ae 4"），送译后糊成色块盖住人脸。
            // 大块（面积 ≥ 图面 6% 且高 ≥ 76）＋可信度 <0.45 才拦——字幕整段块面积
            // 虽大但可信度高（整行真词/成句中文），不受影响；小块另拦纯拉丁碎片
            // （≤10 字、无中文、词典命中 <0.30，如 "Atri a a" 这类纹理碎读）。
            {
                double imgArea = Math.Max(1, image.Width * (double)image.Height);
                var kept = new List<OcrBlock>();
                foreach (var b in list)
                {
                    double area = b.Rect.Width * (double)b.Rect.Height;
                    double pl = Plausibility(b.Text);
                    string t = (b.Text ?? "").Trim();
                    // 混编幻觉旁路（合成图实测）：照片被读成中英混编垃圾时，零星几个
                    // 中文幻觉字会把可信度顶到 0.9（CJK≥4 判真），整幅糊到照片上。
                    // 大块＋中文占字母比 <一半＋拉丁可读性 <0.45 → 混编垃圾；真中文段
                    // 中文占比近 1、真英文段拉丁可读性高，都不受影响。
                    bool mixedJunk = false;
                    if (area >= 0.06 * imgArea && b.Rect.Height >= 76)
                    {
                        int lat = 0, cjk = 0;
                        foreach (char c in t)
                        {
                            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) lat++;
                            else if (IsCjkChar(c)) cjk++;
                        }
                        mixedJunk = lat + cjk >= 12 && cjk * 2 < lat + cjk && ScoreLatin(t) < 0.45;
                    }
                    if (area >= 0.06 * imgArea && b.Rect.Height >= 76 && (pl < 0.45 || mixedJunk))
                    {
                        Diag("cleanup texture drop (big): " + b.Rect + " [" + t + "]");
                        continue;
                    }
                    if (pl < 0.30 && CountCjk(t) == 0 && t.Length > 0 && t.Length <= 10)
                    {
                        Diag("cleanup texture drop (frag): " + b.Rect + " [" + t + "]");
                        continue;
                    }
                    // 中号碎片（合成图实测）：158×102 的照片块只读出 "path hae M4 a"
                    // （一个真词带一串碎读，可信度卡在 0.30 门之上），糊出来是一块
                    // 158×102 的补丁。面积 ≥图面 1.5%、高 ≥56（多行体量）、无中文、
                    // 长度 ≤16、可信度 <0.40 → 拦。高门槛是硬教训：单行短标题
                    // （"Hello Block One"，高 28、可信度同为 0.33）曾被首版误杀、
                    // ocr 套件三项全红——可信度分不开真假，身高才分得开。
                    if (pl < 0.40 && CountCjk(t) == 0 && t.Length > 0 && t.Length <= 16
                        && area >= 0.015 * imgArea && b.Rect.Height >= 56)
                    {
                        Diag("cleanup texture drop (frag2): " + b.Rect + " [" + t + "]");
                        continue;
                    }
                    kept.Add(b);
                }
                list = kept;
            }

            list = BandOrderBlocks(list);
            return list;
        }

        /// <summary>按墨迹延伸矩形四边：从每一边向外逐格扫描，墨迹连续（容忍少量空白格）就把边
        /// 推过去。纵向上限 1.2 个行高；横向上限 6 个行高（整词漏识时要走远），走远后若延伸区
        /// 没有主导底色则回滚（防在照片纹理上跑飞）。墨迹判据与 ScanInkBottom 同：采样亮度极差 ≥70。</summary>
        private static Rectangle ExtendRectByInk(Bitmap image, Rectangle r, double lh, Rectangle limit)
        {
            if (r.Width <= 0 || r.Height <= 0) return r;
            int maxExtV = Math.Max(4, (int)Math.Ceiling(lh * 1.2));
            int maxExtH = Math.Max(4, (int)Math.Ceiling(lh * 6));
            // 横向容忍更大的空白：大字体词内/词间空隙可达 0.4 行距（实测标题整词内部 14~16px
            // 空列，按 0.35 容忍会停在词中间）；相邻独立元素通常相距更远，不会被误桥。
            int gapTolH = Math.Max(2, (int)Math.Ceiling(lh * 0.45));
            int left = r.Left, right = r.Right, top = r.Top, bottom = r.Bottom;
            int gap = 0;
            for (int x = r.Left - 1; x >= Math.Max(limit.Left, r.Left - maxExtH); x--)
            {
                if (ColumnInk(image, x, top, bottom)) { left = x; gap = 0; }
                else if (++gap > gapTolH) break;
            }
            gap = 0;
            for (int x = r.Right; x < Math.Min(limit.Right, r.Right + maxExtH); x++)
            {
                if (ColumnInk(image, x, top, bottom)) { right = x + 1; gap = 0; }
                else if (++gap > gapTolH) break;
            }
            // 纹理回滚：横向走远（超 1.2 行高）后，若延伸区没有主导底色（占比 <0.5），
            // 说明走进的是照片/纹理而非文字行，退回到 1.2 行高（实测防字幕块在照片上跑飞）。
            if (r.Left - left > maxExtV)
            {
                var zone = Rectangle.FromLTRB(left, top, r.Left, bottom);
                if (ZoneDominantShare(image, zone) < 0.5) left = r.Left - maxExtV;
            }
            if (right - r.Right > maxExtV)
            {
                var zone = Rectangle.FromLTRB(r.Right, top, right, bottom);
                if (ZoneDominantShare(image, zone) < 0.5) right = r.Right + maxExtV;
            }
            int maxExt = maxExtV;
            // 纵向空白容忍必须小：行间空白就是行界，容忍大了会爬进相邻行、两行贴片互相
            // 压字（实测推文相邻行被桥接成整行重叠）。被 clip 的升部/降部只离框边几行，
            // 0.12 行距足够接住；字形本体是连续墨迹、不靠容忍推进。
            int gapTolV = Math.Max(2, (int)Math.Ceiling(lh * 0.12));
            gap = 0;
            for (int y = r.Top - 1; y >= Math.Max(limit.Top, r.Top - maxExt); y--)
            {
                if (RowInk(image, y, left, right)) { top = y; gap = 0; }
                else if (++gap > gapTolV) break;
            }
            gap = 0;
            for (int y = r.Bottom; y < Math.Min(limit.Bottom, r.Bottom + maxExt); y++)
            {
                if (RowInk(image, y, left, right)) { bottom = y + 1; gap = 0; }
                else if (++gap > gapTolV) break;
            }
            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        /// <summary>取矩形内字形（少数派像素）的主墨色：按亮度中位分两侧，像素少的一侧为字形侧，
        /// 返回其平均色。采样步长 3，样本不足返回 false。</summary>
        private static bool TryInkColor(Bitmap image, Rectangle r, out int ir, out int ig, out int ib)
        {
            ir = ig = ib = 0;
            if (r.Width <= 0 || r.Height <= 0) return false;
            var lums = new List<int>();
            var rs = new List<int>(); var gs = new List<int>(); var bs = new List<int>();
            for (int y = r.Top; y < r.Bottom; y += 3)
            {
                for (int x = r.Left; x < r.Right; x += 3)
                {
                    if (x < 0 || y < 0 || x >= image.Width || y >= image.Height) continue;
                    var c = image.GetPixel(x, y);
                    lums.Add((c.R * 299 + c.G * 587 + c.B * 114) / 1000);
                    rs.Add(c.R); gs.Add(c.G); bs.Add(c.B);
                }
            }
            if (lums.Count < 10) return false;
            var sorted = new List<int>(lums);
            sorted.Sort();
            int med = sorted[sorted.Count / 2];
            long dr = 0, dg = 0, db = 0, lr2 = 0, lg = 0, lb = 0;
            int dn = 0, ln = 0;
            for (int i = 0; i < lums.Count; i++)
            {
                if (lums[i] <= med) { dr += rs[i]; dg += gs[i]; db += bs[i]; dn++; }
                else { lr2 += rs[i]; lg += gs[i]; lb += bs[i]; ln++; }
            }
            if (dn == 0 || ln == 0) return false;
            if (dn <= ln) { ir = (int)(dr / dn); ig = (int)(dg / dn); ib = (int)(db / dn); }
            else { ir = (int)(lr2 / ln); ig = (int)(lg / ln); ib = (int)(lb / ln); }
            return true;
        }

        /// <summary>区域主导色占比（16 级量化、隔点采样）：文字行的延伸区以纸面/底带为主导色，
        /// 照片纹理区则没有主导色——用它区分两者。</summary>
        private static double ZoneDominantShare(Bitmap image, Rectangle zone)
        {
            if (zone.Width <= 0 || zone.Height <= 0) return 1;
            var buckets = new Dictionary<int, int>();
            int total = 0, best = 0;
            for (int y = zone.Top; y < zone.Bottom; y += 2)
            {
                for (int x = zone.Left; x < zone.Right; x += 2)
                {
                    var c = image.GetPixel(x, y);
                    int key = ((c.R >> 4) << 8) | ((c.G >> 4) << 4) | (c.B >> 4);
                    int n;
                    buckets.TryGetValue(key, out n);
                    buckets[key] = n + 1;
                    total++;
                    if (n + 1 > best) best = n + 1;
                }
            }
            return total > 0 ? best / (double)total : 1;
        }

        private static bool ColumnInk(Bitmap image, int x, int y0, int y1)
        {
            int mn = 255, mx = 0, n = 0;
            for (int y = y0; y < y1; y += 2)
            {
                var c = image.GetPixel(x, y);
                int lum = (c.R * 299 + c.G * 587 + c.B * 114) / 1000;
                if (lum < mn) mn = lum;
                if (lum > mx) mx = lum;
                n++;
            }
            return n > 0 && (mx - mn) >= 70;
        }

        private static bool RowInk(Bitmap image, int y, int x0, int x1)
        {
            int mn = 255, mx = 0, n = 0;
            for (int x = x0; x < x1; x += 2)
            {
                var c = image.GetPixel(x, y);
                int lum = (c.R * 299 + c.G * 587 + c.B * 114) / 1000;
                if (lum < mn) mn = lum;
                if (lum > mx) mx = lum;
                n++;
            }
            return n > 0 && (mx - mn) >= 70;
        }

        /// <summary>怪词元率：含数字或 | / \ 等粘连符的词元占比。乱序错读的文本怪词元多；
        /// 词表分对乱序不敏感（词还是那些词），验收重识结果时用它补判。</summary>
        private static double WeirdRate(string text)
        {
            int counted = 0, weird = 0;
            foreach (var raw in (text ?? "").Replace("\n", " ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string tok = raw.Trim(TokenTrimChars);
                if (tok.Length == 0) continue;
                bool hasLetter = false, bad = false;
                foreach (char c in tok)
                {
                    if (char.IsLetter(c)) hasLetter = true;
                    if (char.IsDigit(c) || c == '|' || c == '/' || c == '\\') bad = true;
                }
                if (!hasLetter && !bad) continue;
                counted++;
                if (bad) weird++;
            }
            return counted > 0 ? weird / (double)counted : 0;
        }

        private static void RescueBlock(Bitmap image, OcrBlock b)
        {
            string text = b.Text ?? "";
            // 单行块走 psm 7 整行重识；带状块（多行、或块高 ≥48 的伪单行字幕带）走 psm 6 按版面重识。
            // 触发：单行可读性 <0.6、带状 <0.78（带状混着专名时分数天然偏低）。
            // 验收（防把好文本换坏）：候选可读性 ≥0.5、字母量 ≥原文 6 成（防丢词刷分——实测海报
            // 大标题被换成只剩几个常见词的残句），且（可读性增益＋怪词元率降幅）合计 ≥0.12。
            bool band = b.LineCount > 1 || text.IndexOf('\n') >= 0 || b.Rect.Height >= 48;
            int latin = 0, tokens = 0;
            foreach (char c in text) if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) latin++;
            foreach (var t in text.Replace("\n", " ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) tokens++;
            if (!band) { if (tokens < 2 || latin < 12) return; }
            else { if (latin < 20) return; }
            double score = ScoreLatin(text);
            if (!b.ForceRescue && score >= (band ? 0.78 : 0.6)) return;
            double weird0 = WeirdRate(text);
            var rect = Rectangle.Inflate(b.Rect, 6, 4);
            rect.Intersect(new Rectangle(0, 0, image.Width, image.Height));
            if (rect.Width < 30 || rect.Height < 10) return;
            string best = null; double bestRank = double.MinValue;
            foreach (int th in new[] { 235, 215 })
            {
                using (var prep = BuildCoreImage(image, rect, th))
                {
                    if (prep == null) continue;
                    string t = RunTesseractPlain(prep, "chi_sim+eng", band ? 6 : 7);
                    if (string.IsNullOrWhiteSpace(t)) continue;
                    t = CleanText(t);
                    if (!band) t = t.Replace("\n", " ");
                    t = t.Trim();
                    if (t.Length == 0) continue;
                    int tLatin = 0;
                    foreach (char c in t) if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) tLatin++;
                    double s = ScoreLatin(t);
                    double wr = WeirdRate(t);
                    bool ok; double rank;
                    if (b.ForceRescue)
                    {
                        // 强制复核（两遍读法打架的块）：候选须内容量不减（字母 ≥9 成）、可读性 ≥0.6、
                        // 综合质量（可读性－怪词元率）不比原文差 0.05 以上——乱序文本词表分虚高，
                        // 不能再要求候选分数反超原文（实测视频字幕乱序版 0.86、干净版 0.79）。
                        ok = s >= 0.6 && tLatin >= 0.9 * latin && (s - wr) >= (score - weird0) - 0.05;
                        rank = s - wr;
                    }
                    else
                    {
                        double gain = (s - score) + (weird0 - wr);
                        ok = s >= 0.5 && tLatin >= 0.6 * latin && gain >= 0.12;
                        rank = gain;
                    }
                    Diag("rescue th=" + th + " score=" + s.ToString("0.00") + " rank=" + rank.ToString("0.00") + " letters=" + tLatin + "/" + latin + " ok=" + ok + " text=[" + t + "]");
                    if (ok && rank > bestRank) { best = t; bestRank = rank; }
                }
            }
            if (best != null)
            {
                Diag("rescue replaced: [" + text + "] -> [" + best + "]");
                if (band)
                {
                    // 带状重识偶尔在边缘多出一行两三字母的碎渣（如字幕带下沿噪声读出的 "aa"），剔除
                    var rawLines = best.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    var lines = new List<string>();
                    foreach (var ln in rawLines)
                    {
                        int ll = 0; bool keep = false;
                        foreach (char c in ln) { if (char.IsLetter(c)) ll++; if (char.IsDigit(c) || IsCjkChar(c)) keep = true; }
                        if (ll >= 3 || keep || rawLines.Length == 1) lines.Add(ln);
                    }
                    if (lines.Count == 0) lines.AddRange(rawLines);
                    // 末行尾部碎词修剪：重识偶尔在最后一行末尾挂一两个字母的碎词（如字幕句尾的 "aa"），
                    // 末行词元 ≥5 时，把尾部 ≤2 字母、非词表词、无数字的碎词逐个剥掉。
                    if (lines.Count > 0)
                    {
                        var toks = new List<string>(lines[lines.Count - 1].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
                        while (toks.Count >= 5)
                        {
                            string last = toks[toks.Count - 1].ToLowerInvariant().Trim(TokenTrimChars);
                            int ll = 0; bool dig = false;
                            foreach (char c in last) { if (c >= 'a' && c <= 'z') ll++; if (char.IsDigit(c)) dig = true; }
                            if (ll >= 1 && ll <= 2 && !dig && !CommonWords.Contains(last)) toks.RemoveAt(toks.Count - 1);
                            else break;
                        }
                        lines[lines.Count - 1] = string.Join(" ", toks.ToArray());
                    }
                    best = string.Join("\n", lines.ToArray());
                    // 框高修正：重识行数×原行高超过原框时合成盖不住末行（实测字幕带三行只给
                    // 两行框高，第三行整行留在贴片外）。按行数长框，并向下扫墨迹行兜底
                    // （行数估少时以像素为准），取较大者；只长高、不动位置与宽度。
                    int oldLines = Math.Max(1, b.LineCount);
                    if (lines.Count > oldLines)
                    {
                        // 行数变多：实测根源是原行数记少了（两行并作一行），原框其实已含全部行——
                        // 按新行数摊薄行高即可，不要按旧行高长框（会把行距拉大、贴片下挂空板）。
                        // 仅当墨迹在框底之下还连续延伸（字形真探出框外）才小幅长框，上限 0.6 个新行高。
                        double pitch = b.Rect.Height / (double)lines.Count;
                        int inkH = ScanInkBottom(image, b.Rect, pitch);
                        int capH = b.Rect.Height + (int)Math.Ceiling(pitch * 0.6);
                        int newH = Math.Min(Math.Max(b.Rect.Height, Math.Min(inkH, capH)), image.Height - b.Rect.Top);
                        if (newH > b.Rect.Height)
                        {
                            Diag("rescue grow rect h " + b.Rect.Height + " -> " + newH + " (lines " + oldLines + "->" + lines.Count + ")");
                            b.Rect = new Rectangle(b.Rect.Left, b.Rect.Top, b.Rect.Width, newH);
                        }
                        b.LineCount = lines.Count;
                        b.LineHeight = b.Rect.Height / (double)lines.Count;
                    }
                    else
                    {
                        // 行数未增：行高以块自身几何为准（多行块 LineHeight 字段可能被合并路径
                        // 写成整块高，不可直接信），墨迹扫描小幅兜底长框。
                        double oldLh = oldLines > 1 ? b.Rect.Height / (double)oldLines
                                     : (b.LineHeight > 0 ? b.LineHeight : b.Rect.Height);
                        int wantH = (int)Math.Ceiling(oldLh * lines.Count);
                        int inkH = ScanInkBottom(image, b.Rect, oldLh);
                        int capH = wantH + (int)Math.Ceiling(oldLh * 0.4);
                        int newH = Math.Min(Math.Max(b.Rect.Height, Math.Max(wantH, Math.Min(inkH, capH))), image.Height - b.Rect.Top);
                        if (newH > b.Rect.Height)
                        {
                            Diag("rescue grow rect h " + b.Rect.Height + " -> " + newH + " (lines " + oldLines + "->" + lines.Count + ")");
                            b.Rect = new Rectangle(b.Rect.Left, b.Rect.Top, b.Rect.Width, newH);
                        }
                        b.LineCount = Math.Max(1, lines.Count);
                        b.LineHeight = b.Rect.Height / (double)b.LineCount;
                    }
                    b.LineRects = null;
                }
                b.Text = best;
            }
        }

        /// <summary>从块底向下扫墨迹行（行内亮度极差 ≥70 判为有字形像素），返回墨迹能延伸到的
        /// 应有块高（相对块顶）；空白行容忍 3 行，最多向下扫 1.6 个原行高。给重识长框兜底用。</summary>
        private static int ScanInkBottom(Bitmap image, Rectangle rect, double lineHeight)
        {
            int x0 = Math.Max(0, rect.Left), x1 = Math.Min(image.Width, rect.Right);
            if (x1 - x0 < 8) return rect.Height;
            int maxScan = (int)Math.Ceiling(lineHeight * 1.6);
            int lastInk = rect.Bottom - 1;
            int gap = 0;
            for (int y = rect.Bottom; y < Math.Min(image.Height, rect.Bottom + maxScan); y++)
            {
                int mn = 255, mx = 0, n = 0;
                for (int x = x0; x < x1; x += 2)
                {
                    var c = image.GetPixel(x, y);
                    int lum = (c.R * 299 + c.G * 587 + c.B * 114) / 1000;
                    if (lum < mn) mn = lum;
                    if (lum > mx) mx = lum;
                    n++;
                }
                bool ink = n > 0 && (mx - mn) >= 70;
                if (ink) { lastInk = y; gap = 0; }
                else if (++gap > 3) break;
            }
            return lastInk - rect.Top + 2;
        }

        /// <summary>亮芯提取：裁行放大 3 倍后，亮度超阈值的像素作字芯（黑字白底），再做 3×3 最小值
        /// 膨胀把描边字芯涨成实心字。白字黑描边的梗图字幕在普通灰度遍里字形粘连，此法实测可读出整句。</summary>
        private static Bitmap BuildCoreImage(Bitmap src, Rectangle rect, int threshold)
        {
            Bitmap up;
            using (var crop = src.Clone(rect, PixelFormat.Format32bppArgb))
            {
                up = new Bitmap(rect.Width * 3, rect.Height * 3, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(up))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(crop, 0, 0, up.Width, up.Height);
                }
            }
            int w = up.Width, h = up.Height;
            var data = up.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                var buf = new byte[stride * h];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, buf.Length);
                var lum = new byte[w * h];
                for (int y = 0; y < h; y++)
                {
                    int rowOff = y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        int off = rowOff + x * 4;
                        lum[y * w + x] = (byte)((buf[off + 2] * 299 + buf[off + 1] * 587 + buf[off] * 114) / 1000);
                    }
                }
                // 二值化（亮→字芯黑）后 3×3 膨胀（取最小值）
                var bin = new byte[w * h];
                for (int i = 0; i < lum.Length; i++) bin[i] = lum[i] > threshold ? (byte)0 : (byte)255;
                var dil = new byte[w * h];
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        byte m = 255;
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            int yy = y + dy; if (yy < 0 || yy >= h) continue;
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = x + dx; if (xx < 0 || xx >= w) continue;
                                byte v = bin[yy * w + xx];
                                if (v < m) m = v;
                            }
                        }
                        dil[y * w + x] = m;
                    }
                }
                for (int y = 0; y < h; y++)
                {
                    int rowOff = y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        int off = rowOff + x * 4;
                        byte v = dil[y * w + x];
                        buf[off] = v; buf[off + 1] = v; buf[off + 2] = v; buf[off + 3] = 255;
                    }
                }
                System.Runtime.InteropServices.Marshal.Copy(buf, 0, data.Scan0, buf.Length);
            }
            finally { up.UnlockBits(data); }
            return up;
        }

        /// <summary>对已预处理的图直接跑一遍 Tesseract 取纯文本（重识用，不再过 Preprocess）。</summary>
        private static string RunTesseractPlain(Bitmap prepared, string langArg, int psm)
        {
            string tessdataDir;
            string exe = FindTesseract(out tessdataDir);
            if (exe == null) return null;
            string tmp = Path.Combine(Path.GetTempPath(), "wintranslator2_ocr_rescue.png");
            prepared.Save(tmp, ImageFormat.Png);
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "\"" + tmp + "\" stdout -l " + langArg + " --psm " + psm +
                                (tessdataDir != null ? " --tessdata-dir \"" + tessdataDir + "\"" : ""),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                using (var proc = Process.Start(psi))
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    if (!proc.WaitForExit(60000))
                    {
                        try { proc.Kill(); } catch { }
                        return null;
                    }
                    if (proc.ExitCode != 0) return null;
                    return output;
                }
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
        }

        private static readonly System.Collections.Generic.HashSet<string> CommonWords =
            new System.Collections.Generic.HashSet<string>(CommonWordList.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

        private const string CommonWordList =
            "the of and to in is you that it he was for on are as with his they at be this have from or one had by word but not what all were we when your can said there use an each which she do how their if will up other about out many then them these so some her would make like him into time has look two more write go see number no way could people my than first water been call who oil its now find long down day did get come made may part over new sound take only little work know place year live me back give most very after thing our just name good sentence man think say great where help through much before line right too mean old any same tell boy follow came want show also around form three small set put end does another well large must big even such because turn here why ask went men read need land different home us move try kind hand again change off play spell air away animal house point page letter mother answer found study still learn should world high every near add food between own below country plant last school father keep tree never start city earth eye light thought head under story saw left few while along might close something seem next hard open example begin life always those both paper together got group often run dog horse cat bird fish red blue green black white room door sun moon star sky sea river fire wind rain snow happy sad fast slow hot cold night morning evening today tomorrow yesterday week month clock watch phone book listen speak walk jump eat drink sleep sing dance teach understand remember forget question problem idea game movie music picture color shape size short tall heavy easy difficult wrong true false young angry tired hungry thirsty family friend teacher student doctor farmer driver cook brother sister baby woman girl myself yourself himself herself really i'm don't can't won't it's that's there's what's let's you're we're they're i've we've i'll you'll isn't aren't wasn't weren't doesn't didn't couldn't shouldn't wouldn't know knows knew say says said get gets got make makes made take takes took see sees saw come comes came go goes went give gives gave find finds found tell tells told work works worked ask asks asked seem seems seemed feel feels felt try tries tried leave leaves left call calls called office district attorney press release job jobs safety traffic image effect include included including visit visited driver stopped pulled amid moving middle secretary national security threat chairman congress senate federal government administration president america american october friday monday tuesday wednesday thursday saturday sunday january february march april june july august september november december divisions programs services resources events search menu contact center policy public private general information latest story stories video photo live breaking report reports according officials local county region member members team season court law police money bank account accounts computer protect protected sentenced victims elderly conspiracy defraud wide scale roles traveled homes posed enforcement officers collect sums cash app upload bills bored him her them us one two three four five six seven eight nine ten him back safety first young people hide freedom expression citizens biggest world serves goal spreading cover sure truth taught glass textured news page site mail sign user account settings home login register sports business health travel science technology entertainment opinion weather local station channel program list show part side end top front back left";



        private class WordBox
        {
            public Rectangle Rect;
            public string Text;
            public double Conf;
        }

        private class Segment
        {
            public Rectangle Rect;
            public string Text;
        }

        private static List<OcrBlock> RecognizeBlocksWithLangs(Bitmap image, string langArg, int psm, List<Rectangle> protectedOut)
        {
            var protScaled = new List<Rectangle>();
            string tessdataDir;
            string exe = FindTesseract(out tessdataDir);
            if (exe == null) throw new OcrException("找不到 Tesseract OCR 引擎（tesseract 文件夹缺失）。");

            string tmp = Path.Combine(Path.GetTempPath(), "wintranslator2_ocr_blocks.png");
            double scale;
            using (var prepared = Preprocess(image))
            {
                scale = prepared.Width / (double)image.Width;
                prepared.Save(tmp, ImageFormat.Png);
            }
            string tsv;
            try
            {
                // TSV 输出用 -c 开关而不是 "tsv" 配置文件：精简版 tessdata 可能不带 configs/tsv，
                // 缺了它整条带坐标识别会失败并静默回退（v1.3.3 实测定位的真机根因）
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "\"" + tmp + "\" stdout -l " + langArg + " --psm " + psm + " -c tessedit_create_tsv=1" +
                                (tessdataDir != null ? " --tessdata-dir \"" + tessdataDir + "\"" : ""),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                using (var proc = Process.Start(psi))
                {
                    var errTask = System.Threading.Tasks.Task.Run(() => proc.StandardError.ReadToEnd());
                    tsv = proc.StandardOutput.ReadToEnd();
                    if (!proc.WaitForExit(60000))
                    {
                        try { proc.Kill(); } catch { }
                        throw new OcrException("OCR 识别超时。");
                    }
                    string stderr = "";
                    try { stderr = errTask.Result ?? ""; } catch { }
                    Diag("tsv run exe=" + exe + " langs=" + langArg + " exit=" + proc.ExitCode +
                         " stderr=" + (stderr.Length > 400 ? stderr.Substring(0, 400) : stderr).Replace("\n", " "));
                    if (proc.ExitCode != 0) throw new OcrException("OCR 识别失败（退出码 " + proc.ExitCode + "）。");
                }
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }

            // 解析 TSV 词行：level 5 为词级；置信 <20 的词丢弃（纹理噪声多为低置信碎字）
            var lineWords = new Dictionary<string, List<WordBox>>();
            foreach (var raw in tsv.Replace("\r\n", "\n").Split(new[] { '\n' }))
            {
                var cols = raw.Split(new[] { '\t' });
                if (cols.Length < 12) continue;
                int level, left, top, width, height;
                double conf;
                if (!int.TryParse(cols[0], out level) || level != 5) continue;
                if (!int.TryParse(cols[6], out left) || !int.TryParse(cols[7], out top) ||
                    !int.TryParse(cols[8], out width) || !int.TryParse(cols[9], out height)) continue;
                if (!double.TryParse(cols[10], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out conf) || conf < 20) continue;
                string word = cols[11].Trim();
                if (word.Length == 0 || width <= 0 || height <= 0) continue;
                string key = cols[2] + "/" + cols[3] + "/" + cols[4];
                List<WordBox> ws;
                if (!lineWords.TryGetValue(key, out ws)) { ws = new List<WordBox>(); lineWords[key] = ws; }
                ws.Add(new WordBox { Rect = new Rectangle(left, top, width, height), Text = word, Conf = conf });
            }

            // v1.7.4 词级过滤（用户真机反馈：国旗/认证徽章/@符号/头像被当文字翻译）：在行重建前剔除
            // 非文字词，避免它们混进句子、或用超大框污染行高判定把相邻行揉成一行（推文截图实测：
            // 头像被读成 47×53 的 "ry"，其框高让姓名行与正文行被并成一行、整段语序全乱）。
            var rawWords = new List<WordBox>();
            foreach (var kv in lineWords) rawWords.AddRange(kv.Value);
            if (rawWords.Count > 0)
            {
                var hs = new List<int>();
                foreach (var w in rawWords) hs.Add(w.Rect.Height);
                hs.Sort();
                double medH = hs[hs.Count / 2];
                if (medH < 4) medH = 4;
                var keptWords = new List<WordBox>();
                foreach (var w in rawWords)
                {
                    string t = w.Text;
                    bool hasLetterOrDigit = false, hasCjk = false, hasDigit = false;
                    foreach (char c in t)
                    {
                        if (char.IsLetterOrDigit(c)) hasLetterOrDigit = true;
                        if (char.IsDigit(c)) hasDigit = true;
                        if ((c >= 0x2E80 && c <= 0x9FFF) || (c >= 0x3040 && c <= 0x30FF) || (c >= 0xAC00 && c <= 0xD7AF)) hasCjk = true;
                    }
                    // R1 纯符号词（©、@、…、? 等，无字母数字）不是文字，直接丢
                    if (!hasLetterOrDigit) { protScaled.Add(w.Rect); continue; }
                    // R2 @提及（@MarioNawfal 这类账号名）不进翻译文本
                    if (t.Length > 1 && t[0] == '@') { protScaled.Add(w.Rect); continue; }
                    // R3 大框：高与宽都远超正文字高 → 人像/大图块的误读（如头像 "ry"）。
                    // 豁免：≥4 个字母的全大写词（海报标题 GLASS/TEXTURED 这类展示字）——它们在
                    // psm11 遍的 medH 被满屏碎字拖低时会被误杀（实测 GLASS conf 96 仍被 R3 丢弃）；
                    // 徽标碎字（如 AESK）纵被豁免，后面还有短乱码块过滤兜底。
                    bool capsWord = false;
                    {
                        int letters = 0; bool allCaps = true;
                        foreach (char c in t)
                        {
                            if (char.IsLetter(c)) { letters++; if (!char.IsUpper(c)) allCaps = false; }
                        }
                        capsWord = letters >= 4 && allCaps;
                    }
                    if (!capsWord && w.Rect.Height > medH * 2.5 && w.Rect.Width > medH * 1.6) { protScaled.Add(w.Rect); continue; }
                    // R4 图标方框：近正方形小框＋低置信＋无数字＋非中日韩＋短词 → 徽章/国旗类图标误读（如 "FJ"）
                    double ar = w.Rect.Height > 0 ? w.Rect.Width / (double)w.Rect.Height : 0;
                    if (!hasCjk && !hasDigit && t.Length <= 3 && w.Conf < 80 &&
                        ar >= 0.75 && ar <= 1.35 && w.Rect.Height >= medH * 0.8 && w.Rect.Height <= medH * 1.6) { protScaled.Add(w.Rect); continue; }
                    keptWords.Add(w);
                }
                if (keptWords.Count != rawWords.Count)
                {
                    Diag("word filter: " + rawWords.Count + " -> " + keptWords.Count + " (medH=" + medH + ")");
                    lineWords.Clear();
                    lineWords["all"] = keptWords;
                }
            }

            // 词 → 行：按坐标重建（纵向中心距 < 行高×0.6 归同一行）。不能依赖 TSV 的 block/par/line 编号分组：
            // psm 6 的编号只在同一 block 内有意义（跨 block 会撞号，把相距很远的行并成一行——如标题与页脚行）；
            // psm 11 的编号全是 1，等于没有结构。编号分组是 2026-11-01 海报漏识别与视频日期误并的共同根因。
            var allWords = new List<WordBox>();
            foreach (var kv in lineWords) allWords.AddRange(kv.Value);
            allWords.Sort((a, b) => a.Rect.Top != b.Rect.Top ? a.Rect.Top.CompareTo(b.Rect.Top) : a.Rect.Left.CompareTo(b.Rect.Left));
            var coordLines = new List<List<WordBox>>();
            foreach (var w in allWords)
            {
                double cy = w.Rect.Top + w.Rect.Height / 2.0;
                List<WordBox> hitLine = null;
                foreach (var ln in coordLines)
                {
                    double top = double.MaxValue, bot = double.MinValue;
                    foreach (var x in ln) { top = Math.Min(top, x.Rect.Top); bot = Math.Max(bot, x.Rect.Bottom); }
                    double lcy = (top + bot) / 2.0, lh = Math.Max(1, bot - top);
                    if (Math.Abs(cy - lcy) < Math.Max(lh, w.Rect.Height) * 0.6) { hitLine = ln; break; }
                }
                if (hitLine == null) { hitLine = new List<WordBox>(); coordLines.Add(hitLine); }
                hitLine.Add(w);
            }
            var segments = new List<Segment>();
            foreach (var words in coordLines)
            {
                words.Sort((a, b) => a.Rect.Left.CompareTo(b.Rect.Left));
                Rectangle lineRect = Rectangle.Empty;
                foreach (var w in words) lineRect = lineRect == Rectangle.Empty ? w.Rect : Rectangle.Union(lineRect, w.Rect);
                int gapLimit = Math.Max(28, (int)(lineRect.Height * 1.1));
                var curWords = new List<WordBox>();
                Rectangle curRect = Rectangle.Empty;
                int curRight = 0;
                foreach (var w in words)
                {
                    if (curWords.Count > 0 && w.Rect.Left - curRight > gapLimit)
                    {
                        AddSegment(segments, curWords, curRect, protScaled);
                        curWords = new List<WordBox>();
                        curRect = Rectangle.Empty;
                    }
                    curWords.Add(w);
                    curRect = curRect == Rectangle.Empty ? w.Rect : Rectangle.Union(curRect, w.Rect);
                    curRight = Math.Max(curRight, w.Rect.Right);
                }
                if (curWords.Count > 0) AddSegment(segments, curWords, curRect, protScaled);
            }

            // 片段 → 块：上下相邻（行距小于字高）且横向明显重叠的片段合并（多行字幕合成一块）；
            // 位置离得远的（日期、角标）各自成块，分别翻译、分别盖回。
            segments.Sort((a, b) => a.Rect.Top != b.Rect.Top ? a.Rect.Top.CompareTo(b.Rect.Top) : a.Rect.Left.CompareTo(b.Rect.Left));
            var blocks = new List<List<Segment>>();
            var blockRects = new List<Rectangle>();
            for (int pass = 0; pass < 2; pass++)
            {
                blocks.Clear(); blockRects.Clear();
                foreach (var seg in segments)
                {
                    int hit = -1;
                    for (int i = 0; i < blocks.Count; i++)
                    {
                        var br = blockRects[i];
                        int vGap = Math.Max(0, Math.Max(seg.Rect.Top - br.Bottom, br.Top - seg.Rect.Bottom));
                        int overlap = Math.Min(seg.Rect.Right, br.Right) - Math.Max(seg.Rect.Left, br.Left);
                        int minW = Math.Min(seg.Rect.Width, br.Width);
                        if (vGap < seg.Rect.Height * 0.9 && minW > 0 && overlap >= minW * 0.35) { hit = i; break; }
                    }
                    if (hit >= 0)
                    {
                        blocks[hit].Add(seg);
                        blockRects[hit] = Rectangle.Union(blockRects[hit], seg.Rect);
                    }
                    else
                    {
                        blocks.Add(new List<Segment> { seg });
                        blockRects.Add(seg.Rect);
                    }
                }
                if (pass == 0)
                {
                    // 第二遍用合并后的块序再试一次（块框长大后可能还能并入邻近片段）
                    var flat = new List<Segment>();
                    foreach (var b in blocks) flat.AddRange(b);
                    if (flat.Count == segments.Count) { segments = flat; }
                }
            }

            var result = new List<OcrBlock>();
            foreach (var segs in blocks)
            {
                // v1.7.5 行序与行界修正：同一视觉行的分段先成带、带内按左到右拼成一行（旧口径纯按 Top 排，
                // 「7 小时」这类顶边高 1~2px 的段会跑到姓名前面；且并排段被当成上下两行、逐行回填会画错位）。
                // 块的行数＝行带数，行高按行带高计。
                var bands = BandGroupSegments(segs);
                var texts = new List<string>();
                var lineRects = new List<Rectangle>();
                Rectangle rect = Rectangle.Empty;
                double lhSum = 0;
                foreach (var band in bands)
                {
                    string lineText = "";
                    Rectangle bandRect = Rectangle.Empty;
                    foreach (var seg in band)
                    {
                        string t = (seg.Text ?? "").Trim();
                        if (t.Length > 0)
                        {
                            string sep = (lineText.Length > 0 && (IsCjkChar(lineText[lineText.Length - 1]) || IsCjkChar(t[0]))) ? "" : " ";
                            lineText = lineText.Length == 0 ? t : lineText + sep + t;
                        }
                        bandRect = bandRect == Rectangle.Empty ? seg.Rect : Rectangle.Union(bandRect, seg.Rect);
                        rect = rect == Rectangle.Empty ? seg.Rect : Rectangle.Union(rect, seg.Rect);
                    }
                    if (lineText.Length > 0)
                    {
                        texts.Add(lineText);
                        var lb = new Rectangle(
                            (int)Math.Round(bandRect.Left / scale), (int)Math.Round(bandRect.Top / scale),
                            Math.Max(1, (int)Math.Round(bandRect.Width / scale)), Math.Max(1, (int)Math.Round(bandRect.Height / scale)));
                        lb.Intersect(new Rectangle(0, 0, image.Width, image.Height));
                        lineRects.Add(lb);
                    }
                    lhSum += bandRect.Height;
                }
                var scaled = new Rectangle(
                    (int)Math.Round(rect.Left / scale), (int)Math.Round(rect.Top / scale),
                    Math.Max(1, (int)Math.Round(rect.Width / scale)), Math.Max(1, (int)Math.Round(rect.Height / scale)));
                scaled.Intersect(new Rectangle(0, 0, image.Width, image.Height));
                if (scaled.Width < 8 || scaled.Height < 6) continue;
                result.Add(new OcrBlock
                {
                    Rect = scaled,
                    Text = string.Join("\n", texts.ToArray()),
                    LineCount = Math.Max(1, texts.Count),
                    LineHeight = bands.Count > 0 ? (lhSum / bands.Count) / scale : scaled.Height,
                    LineRects = lineRects.Count == texts.Count ? lineRects : null
                });
            }
            result = BandOrderBlocks(result);
            // v1.7.5：保护框换算回原图坐标交出（合成时避让）
            if (protectedOut != null)
            {
                foreach (var r in protScaled)
                {
                    var o = new Rectangle(
                        (int)Math.Round(r.Left / scale), (int)Math.Round(r.Top / scale),
                        Math.Max(1, (int)Math.Round(r.Width / scale)), Math.Max(1, (int)Math.Round(r.Height / scale)));
                    o.Intersect(new Rectangle(0, 0, image.Width, image.Height));
                    if (o.Width > 0 && o.Height > 0) protectedOut.Add(o);
                }
            }
            return result;
        }

        private static bool IsCjkChar(char c)
        {
            return (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3040 && c <= 0x30FF) || (c >= 0xAC00 && c <= 0xD7AF);
        }

        /// <summary>v1.7.5：分段按视觉行带分组——顶边差小于较小段高一半的视为同一行，行内按左到右，行带按自上而下。</summary>
        private static List<List<Segment>> BandGroupSegments(List<Segment> segs)
        {
            var sorted = new List<Segment>(segs);
            sorted.Sort((a, b) => a.Rect.Top != b.Rect.Top ? a.Rect.Top.CompareTo(b.Rect.Top) : a.Rect.Left.CompareTo(b.Rect.Left));
            var bands = new List<List<Segment>>();
            int i = 0;
            while (i < sorted.Count)
            {
                var band = new List<Segment> { sorted[i] };
                int bandTop = sorted[i].Rect.Top, bandH = sorted[i].Rect.Height;
                int j = i + 1;
                while (j < sorted.Count && sorted[j].Rect.Top - bandTop < Math.Min(sorted[j].Rect.Height, bandH) * 0.5)
                {
                    band.Add(sorted[j]);
                    j++;
                }
                band.Sort((a, b) => a.Rect.Left.CompareTo(b.Rect.Left));
                bands.Add(band);
                i = j;
            }
            return bands;
        }

        /// <summary>v1.7.5：块级同口径行带排序（左右相邻的两块不因 1~2px 顶边差颠倒阅读序）。</summary>
        private static List<OcrBlock> BandOrderBlocks(List<OcrBlock> blocks)
        {
            var sorted = new List<OcrBlock>(blocks);
            sorted.Sort((a, b) => a.Rect.Top != b.Rect.Top ? a.Rect.Top.CompareTo(b.Rect.Top) : a.Rect.Left.CompareTo(b.Rect.Left));
            var ordered = new List<OcrBlock>();
            int i = 0;
            while (i < sorted.Count)
            {
                var band = new List<OcrBlock> { sorted[i] };
                int bandTop = sorted[i].Rect.Top, bandH = sorted[i].Rect.Height;
                int j = i + 1;
                while (j < sorted.Count && sorted[j].Rect.Top - bandTop < Math.Min(sorted[j].Rect.Height, bandH) * 0.5)
                {
                    band.Add(sorted[j]);
                    j++;
                }
                band.Sort((a, b) => a.Rect.Left.CompareTo(b.Rect.Left));
                ordered.AddRange(band);
                i = j;
            }
            return ordered;
        }

        private static void AddSegment(List<Segment> segments, List<WordBox> words, Rectangle rect, List<Rectangle> protScaled)
        {
            var texts = new List<string>();
            double confSum = 0;
            foreach (var w in words) { texts.Add(w.Text); confSum += w.Conf; }
            string text = CleanText(string.Join(" ", texts.ToArray()));
            if (text.Length == 0 || rect.Width < 6 || rect.Height < 5) return;
            // v1.7.4 R5 短垃圾段：字母总数 ≤4 且平均置信 <75 且无数字无中日韩 → 图标误读段
            // （如右上角图标被读成 "(Jur" 自成一段）；真短词（WTF?! 等）置信通常 >90，不受影响
            {
                int letters = 0; bool hasDigit = false, hasCjk = false;
                foreach (char c in text)
                {
                    if (char.IsLetter(c)) letters++;
                    if (char.IsDigit(c)) hasDigit = true;
                    if ((c >= 0x2E80 && c <= 0x9FFF) || (c >= 0x3040 && c <= 0x30FF) || (c >= 0xAC00 && c <= 0xD7AF)) hasCjk = true;
                }
                double avg = words.Count > 0 ? confSum / words.Count : 0;
                if (letters <= 4 && avg < 75 && !hasDigit && !hasCjk) { if (protScaled != null) protScaled.Add(rect); return; }
            }
            if (!LooksLikeText(text))
            {
                // 小字豁免：短块（如 "Oct. 28"）须同时满足 有效字≥3、字母≥2、平均置信≥60 才保留；
                // 门槛过松会把衣纹噪声（如 "4 j"）放进来盖到人身上（v1.3.4 端到端自检实测教训）
                double avgConf = words.Count > 0 ? confSum / words.Count : 0;
                int validChars = 0, letters = 0;
                foreach (char c in text)
                {
                    if (char.IsLetterOrDigit(c)) validChars++;
                    if (char.IsLetter(c)) letters++;
                }
                if (!(validChars >= 3 && letters >= 2 && avgConf >= 60)) { if (protScaled != null) protScaled.Add(rect); return; }
            }
            segments.Add(new Segment { Rect = rect, Text = text });
        }

        /// <summary>是否为像样文字：有效字符占比过半、至少 3 个，且至少含一个 3+ 字母的词（滤掉把人物/纹理读出的碎符号）。</summary>
        private static bool LooksLikeText(string text)
        {
            int valid = 0, total = 0;
            bool hasWord = false;
            int run = 0;
            foreach (char c in text)
            {
                if (c == ' ' || c == '\n') { if (run >= 3) hasWord = true; run = 0; continue; }
                total++;
                bool letter = char.IsLetter(c);
                if (letter) run++; else { if (run >= 3) hasWord = true; run = 0; }
                if (char.IsLetterOrDigit(c) || c == '.' || c == ',' || c == '!' || c == '?' ||
                    c == '·' || c == '—' || c == '-' || c == '\'' || c == '%' || c == ':')
                    valid++;
            }
            if (run >= 3) hasWord = true;
            return valid >= 3 && valid * 2 >= total && hasWord;
        }

                /// <summary>预处理：小图放大 2 倍（双三次）＋灰度化＋300 DPI，提升小字识别率。</summary>
        public static Bitmap Preprocess(Bitmap src)
        {
            int w = src.Width, h = src.Height;
            double scale = w < 900 ? 2.0 : 1.0;
            var bmp = new Bitmap((int)(w * scale), (int)(h * scale), PixelFormat.Format32bppArgb);
            bmp.SetResolution(300, 300);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                var cm = new ColorMatrix(new[]
                {
                    new[] { 0.299f, 0.299f, 0.299f, 0f, 0f },
                    new[] { 0.587f, 0.587f, 0.587f, 0f, 0f },
                    new[] { 0.114f, 0.114f, 0.114f, 0f, 0f },
                    new[] { 0f, 0f, 0f, 1f, 0f },
                    new[] { 0f, 0f, 0f, 0f, 1f }
                });
                using (var attrs = new ImageAttributes())
                {
                    attrs.SetColorMatrix(cm);
                    g.DrawImage(src, new Rectangle(0, 0, bmp.Width, bmp.Height), 0, 0, w, h, GraphicsUnit.Pixel, attrs);
                }
            }
            return bmp;
        }

        private static readonly Regex MultiSpace = new Regex(" +");
        private static readonly Regex CjkSpace = new Regex("(?<=[\u3400-\u9FFF\u3000-\u303F\uFF00-\uFFEF]) (?=[\u3400-\u9FFF\u3000-\u303F\uFF00-\uFFEF])");

        /// <summary>清理：合并多余空格、去掉中文字之间的空格、英文词距保留。</summary>
        public static string CleanText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var lines = text.Replace("\r\n", "\n").Split(new[] { '\n' });
            var sb = new StringBuilder();
            foreach (var raw in lines)
            {
                string line = MultiSpace.Replace(raw.Trim(), " ");
                line = CjkSpace.Replace(line, "");
                if (line.Length > 0)
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(line);
                }
            }
            return sb.ToString();
        }
    }
}
