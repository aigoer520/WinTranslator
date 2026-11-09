using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinTranslator
{
    /// <summary>
    /// v1.3.0 原位覆盖合成：把每块译文盖回原文块的位置——先按该块背景亮度铺半透明贴片遮住原文，
    /// 再在原框内排译文（字号自适应，实在塞不下时贴片小幅向下加高）。"位置一样"指块的位置与范围一致。
    /// </summary>
    public static class OverlayComposer
    {
        public static Bitmap Compose(Bitmap src, IList<OcrBlock> blocks, IList<string> translations)
        {
            return Compose(src, blocks, translations, null);
        }

        // ---------------- v1.7.5 逐行回填 ----------------

        private class LineSlot
        {
            public Rectangle Rect;
            public string Text = "";
            public double LineHeight;
        }

        /// <summary>槽位按墨迹扩宽（v1.8.3，真机 CGTN 帧实测）：OCR 词框比可见字迹窄
        /// （发光字外缘光晕不进词框，块宽 306 而字迹跨 ~330），译文按词框排版会右溢、
        /// 与原文残迹混排。以行带底色为基准（亮度偏离 >55 为墨），在槽位左右按列找
        /// 墨迹尽头（容忍 3 列空隙），每侧至多扩 35% 槽宽；找不到墨迹就不动。</summary>
        private static void ExtendSlotSpans(Bitmap src, List<LineSlot> slots)
        {
            foreach (var slot in slots)
            {
                var r = slot.Rect;
                if (r.Width <= 0 || r.Height <= 0) continue;
                int top = Math.Max(0, r.Top), bottom = Math.Min(src.Height, r.Bottom);
                if (bottom - top < 3) continue;
                // 行带底色：带内亮度中位数（采样步长 3）
                var lums = new List<int>();
                for (int y = top; y < bottom; y += 2)
                    for (int x = Math.Max(0, r.Left); x < Math.Min(src.Width, r.Right); x += 3)
                    {
                        var p = src.GetPixel(x, y);
                        lums.Add((p.R * 299 + p.G * 587 + p.B * 114) / 1000);
                    }
                if (lums.Count == 0) continue;
                lums.Sort();
                int bg = lums[lums.Count / 2];
                Func<int, bool> colInk = x =>
                {
                    int hit = 0;
                    for (int y = top; y < bottom; y += 1)
                    {
                        var p = src.GetPixel(x, y);
                        int g = (p.R * 299 + p.G * 587 + p.B * 114) / 1000;
                        if (Math.Abs(g - bg) > 55) { hit++; if (hit >= 2) return true; }
                    }
                    return false;
                };
                int cap = Math.Max(12, (int)(r.Width * 0.35));
                int newRight = r.Right, gap = 0;
                for (int x = r.Right; x < Math.Min(src.Width, r.Right + cap); x++)
                {
                    if (colInk(x)) { newRight = x + 1; gap = 0; }
                    else if (++gap > 3) break;
                }
                int newLeft = r.Left; gap = 0;
                for (int x = r.Left - 1; x >= Math.Max(0, r.Left - cap); x--)
                {
                    if (colInk(x)) { newLeft = x; gap = 0; }
                    else if (++gap > 3) break;
                }
                if (newLeft != r.Left || newRight != r.Right)
                    slot.Rect = new Rectangle(newLeft, r.Top, newRight - newLeft, r.Height);
            }
        }

        /// <summary>把区域块展开成原始行槽：优先用组段保留的 Parts；每个原始块再按其行数/行高切成行。</summary>
        private static List<LineSlot> FlattenLines(OcrBlock block)
        {
            var slots = new List<LineSlot>();
            var parts = (block.Parts != null && block.Parts.Count > 0) ? block.Parts : new List<OcrBlock> { block };
            foreach (var part in parts)
            {
                int lines = Math.Max(1, part.LineCount);
                double lh = part.LineHeight > 0 ? part.LineHeight : part.Rect.Height / (double)lines;
                var texts = (part.Text ?? "").Replace("\r", "").Split(new[] { '\n' });
                bool hasLineRects = part.LineRects != null && part.LineRects.Count == lines;
                for (int k = 0; k < lines; k++)
                {
                    Rectangle lineRect;
                    if (hasLineRects)
                    {
                        lineRect = part.LineRects[k];
                    }
                    else
                    {
                        int top = part.Rect.Top + (int)Math.Round(k * lh);
                        int bottom = (k == lines - 1) ? part.Rect.Bottom : part.Rect.Top + (int)Math.Round((k + 1) * lh);
                        lineRect = new Rectangle(part.Rect.Left, top, part.Rect.Width, Math.Max(1, bottom - top));
                    }
                    slots.Add(new LineSlot
                    {
                        Rect = lineRect,
                        Text = k < texts.Length ? texts[k] : "",
                        LineHeight = lh
                    });
                }
            }
            // 槽位去重：同一行被两个来源（双遍识别/多 part）各给一份框时，两份译文片会
            // 叠画成重影（实测海报标题行双框叠字）。与已收槽重叠 ≥60%（按较小面积）的后到槽丢弃。
            var dedup = new List<LineSlot>();
            foreach (var s in slots)
            {
                bool dup = false;
                foreach (var k in dedup)
                {
                    var inter = Rectangle.Intersect(k.Rect, s.Rect);
                    if (inter.Width <= 0 || inter.Height <= 0) continue;
                    double ia = inter.Width * (double)inter.Height;
                    double minA = Math.Min(k.Rect.Width * (double)k.Rect.Height, s.Rect.Width * (double)s.Rect.Height);
                    if (minA > 0 && ia >= 0.6 * minA) { dup = true; break; }
                }
                if (!dup) dedup.Add(s);
            }
            return dedup;
        }

        /// <summary>
        /// 译文分配到行：译文本身带换行且行数与原行数一致时直接逐行对位（引擎对分行输入通常分行返回）；
        /// 否则按各原行文本长度比例分配，切点就近落在空格/标点边界，最后一行收余量。
        /// </summary>
        private static List<string> DistributeText(List<LineSlot> slots, string rawText)
        {
            string text = (rawText ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            if (slots.Count > 1)
            {
                var rawLines = new List<string>();
                foreach (var ln in (rawText ?? "").Replace("\r", "").Split(new[] { '\n' }))
                {
                    string t = ln.Trim();
                    if (t.Length > 0) rawLines.Add(t);
                }
                if (rawLines.Count == slots.Count) return rawLines;
            }
            return DistributeByProportion(slots, text);
        }

        private static List<string> DistributeByProportion(List<LineSlot> slots, string text)
        {
            var pieces = new List<string>();
            int n = slots.Count;
            if (n == 0) return pieces;
            if (n == 1) { pieces.Add(text); return pieces; }
            var weights = new int[n];
            int total = 0;
            for (int i = 0; i < n; i++)
            {
                weights[i] = Math.Max(1, (slots[i].Text ?? "").Trim().Length);
                total += weights[i];
            }
            int pos = 0, cum = 0;
            for (int i = 0; i < n - 1; i++)
            {
                cum += weights[i];
                int target = (int)Math.Round(text.Length * (cum / (double)total));
                int cut = FindCut(text, pos, target);
                pieces.Add(text.Substring(pos, cut - pos).Trim());
                pos = cut;
            }
            pieces.Add(text.Substring(pos).Trim());
            // 防标点孤儿：任何一片只剩标点/边界符时并回上一片（末片是旧实测；中间片同样
            // 会发生——实测英译中三行分片把一个逗号单独分进中间行，画成一个孤零零的逗号）。
            // 首片无上一片时并入下一片。
            for (int i = 0; i < pieces.Count; i++)
            {
                string pc = pieces[i];
                if (pc.Length == 0) continue;
                bool onlyPunct = true;
                foreach (char c in pc)
                    if (!char.IsPunctuation(c) && !IsBoundary(c)) { onlyPunct = false; break; }
                if (!onlyPunct) continue;
                if (i > 0) { pieces[i - 1] += pc; pieces[i] = ""; }
                else if (pieces.Count > 1) { pieces[1] = pc + pieces[1]; pieces[0] = ""; }
            }
            return pieces;
        }

        private static int FindCut(string text, int from, int target)
        {
            if (target <= from) return from;
            if (target >= text.Length) return text.Length;
            for (int d = 0; d <= 8; d++)
            {
                int fwd = target + d;
                if (fwd < text.Length && IsBoundary(text[fwd])) return fwd + 1;
                int back = target - d;
                if (back > from && IsBoundary(text[back])) return back + 1;
            }
            return target;
        }

        private static bool IsBoundary(char c)
        {
            return c == ' ' || c == '，' || c == '。' || c == '、' || c == '；' || c == '：'
                || c == ',' || c == '.' || c == ';' || c == ':' || c == '!' || c == '?' || c == '！' || c == '？';
        }

        /// <summary>
        /// v1.7.5 译图保真合成（用户定版）：整段译文按原行逐行回填——每行画在原行框内、
        /// 字号由原行高一次定死（不再缩字循环）、常规字重；某行放不下先向下扩高（不超区域底＋一行），
        /// 再小幅缩字（下限 85%）。贴片与文字都挖空保护框（符号/徽章/头像/@账号像素原样保留）。
        /// </summary>
        public static Bitmap Compose(Bitmap src, IList<OcrBlock> blocks, IList<string> translations, IList<Rectangle> protectedRects)
        {
            var composed = (Bitmap)src.Clone();
            using (var g = Graphics.FromImage(composed))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                int n = Math.Min(blocks.Count, translations.Count);
                // v1.8.2 两遍合成（借 LacyanTranslator 公开做法：先画全部底、再画全部字）：
                // 先为每行算好版面，再统一填充，最后统一绘字——填充不会再擦掉已画好的相邻行文字，
                // 行框得以放心膨胀覆盖原文（见 PlanLine）。
                var plans = new List<LinePlan>();
                for (int i = 0; i < n; i++)
                {
                    var block = blocks[i];
                    string text = (translations[i] ?? "").Trim();
                    if (text.Length == 0) continue;
                    var slots = FlattenLines(block);
                    if (slots.Count == 0) continue;
                    ExtendSlotSpans(src, slots);
                    var pieces = DistributeText(slots, text);
                    var blockPlans = new List<LinePlan>();
                    for (int s = 0; s < slots.Count; s++)
                    {
                        var plan = PlanLine(g, src, slots[s], pieces[s], block.Rect, protectedRects);
                        if (plan == null && pieces[s].Length == 0)
                        {
                            // 分到空片的行：其原文已被译进其它行，仍须铺底盖住，否则原样
                            // 留在图上成"重影"（实测推文末行英文与译文并存）。
                            plan = PlanFillOnly(src, slots[s], block.Rect);
                        }
                        if (plan != null) blockPlans.Add(plan);
                    }
                    plans.AddRange(blockPlans);
                }
                RestackPlans(plans, src.Height);
                var bands = UnifyBandFills(plans);
                foreach (var b in bands) FillBandFloor(composed, g, b, protectedRects);
                foreach (var p in plans) FillLine(composed, g, src, p, protectedRects);
                foreach (var b in bands) FeatherBandEdges(composed, src, b, protectedRects);
                foreach (var p in plans) DrawLineText(composed, g, p, protectedRects);
            }
            return composed;
        }

        /// <summary>全局纵向重排：行槽若几何重叠（漏识别的行被救回后与原块框只差几像素，
        /// 实测中文字幕两行框顶只差 3px、且分属两个块），或某行译文折行后高过行槽，
        /// 后一行仍按原锚点画就会与前一行叠字。自上而下走一遍：每行的实际绘字顶须
        /// 让开所有与之横向重叠（≥较窄者 40%）的已放置文字区，位移记为 ShiftY
        /// （填充同步处理，见 FillLine/DrawLineText）。横向不重叠的并列栏互不干扰；
        /// 无重叠时 ShiftY 恒为 0，旧版面不受影响；只铺底不绘字的行不参与重排
        /// （它的活是擦掉本行原文，位置不能动）。</summary>
        private static void RestackPlans(List<LinePlan> plans, int imageHeight)
        {
            var textPlans = plans.FindAll(p => p.SpanHeights != null && p.SpanHeights.Exists(h => h > 0));
            if (textPlans.Count < 2) return;
            for (int i = 1; i < textPlans.Count; i++)
            {
                var key = textPlans[i];
                int j = i - 1;
                while (j >= 0 && textPlans[j].SlotRect.Top > key.SlotRect.Top) { textPlans[j + 1] = textPlans[j]; j--; }
                textPlans[j + 1] = key;
            }
            var placed = new List<Rectangle>();
            foreach (var p in textPlans)
            {
                int textH = 0;
                foreach (var h in p.SpanHeights) textH = Math.Max(textH, h);
                int drawTop = p.SlotRect.Top;
                foreach (var zone in placed)
                {
                    int xOv = Math.Min(zone.Right, p.SlotRect.Right) - Math.Max(zone.Left, p.SlotRect.Left);
                    int minW = Math.Min(zone.Width, p.SlotRect.Width);
                    if (minW > 0 && xOv >= 0.4 * minW && zone.Bottom + 2 > drawTop)
                        drawTop = Math.Max(drawTop, zone.Bottom + 2);
                }
                int shift = drawTop - p.SlotRect.Top;
                if (drawTop + textH > imageHeight)
                    shift = Math.Max(0, imageHeight - textH - p.SlotRect.Top);
                p.ShiftY = shift;
                placed.Add(new Rectangle(p.SlotRect.Left, p.SlotRect.Top + shift, p.SlotRect.Width, textH));
            }
        }

        /// <summary>一行的完整版面计划：膨胀行框、采样色、自由跨度、实测分片、最终字号与折行结果。</summary>
        private class LinePlan
        {
            public Rectangle Rect;        // 膨胀后的行框（采样/填充基准）
            public Rectangle SlotRect;    // 原始行框（绘字锚点：文字仍落在原行位置，不随膨胀漂移）
            public int ShiftY;            // 块内重排位移（行槽重叠/译文溢出时后行整体下移，0=不移）
            public Rectangle RegionRect;  // 所属区域框（平底整带填充的横向范围）
            public bool InBand;           // 属于多计划同带：填充走带底整铺＋带外缘羽化，贴片自身不羽化
            public Color Bg, TopC, BotC, TextColor;
            public bool FlatBg;
            public float Size;
            public List<Rectangle> Spans;
            public List<List<string>> SpanLines;
            public List<int> SpanHeights;
            public List<int> PatchHeights;
            public Rectangle ExtraLeft, ExtraRight;  // 平底行向区域左右的整带延伸填充（Empty=不延伸）
        }

        /// <summary>延伸区与行底是否一致：底色占比 ≥0.80 且色距 <30 才算同一片底。</summary>
        private static bool StripMatchesBg(Bitmap src, Rectangle strip, Color bg)
        {
            Color sbg, sfg; bool sHas; double sShare;
            SampleRegionColors(src, strip, out sbg, out sfg, out sHas, out sShare);
            if (sShare < 0.80) return false;
            int dr = sbg.R - bg.R, dg = sbg.G - bg.G, db = sbg.B - bg.B;
            return Math.Sqrt(dr * dr + dg * dg + db * db) < 30;
        }

        /// <summary>未认领墨迹护栏：填充框内若存在既非底色、也非本行字色的成带墨迹（多半是
        /// 漏识别的相邻行，如异极性字幕行），把填充框上下边收回到该墨迹带之外——宁可少盖
        /// 一点，也不能把没被翻译的文字当背景糊掉（实测中文字幕黑字首行被盖掉约九成，
        /// 病灶是行框偏高加膨胀、填充前不查带内还有没有别人的字）。本行墨迹按采样字色
        /// 判定；一行里两种墨迹都没有时不收（回退旧行为）。</summary>
        private static Rectangle ClampFillToOwnInk(Bitmap src, Rectangle rect, Color bg, Color own)
        {
            if (rect.Width < 16 || rect.Height < 12) return rect;
            int step = 2, n = 0;
            for (int x = rect.Left; x < rect.Right; x += step) n++;
            if (n < 8) return rect;
            int need = Math.Max(3, (int)Math.Ceiling(n * 0.02));
            var ownRow = new bool[rect.Height];
            var foreignRow = new bool[rect.Height];
            for (int i = 0; i < rect.Height; i++)
            {
                int y = rect.Top + i, ownCnt = 0, frnCnt = 0;
                for (int x = rect.Left; x < rect.Right; x += step)
                {
                    var c = src.GetPixel(x, y);
                    int dr = c.R - own.R, dg = c.G - own.G, db = c.B - own.B;
                    if (dr * dr + dg * dg + db * db < 3600) { ownCnt++; continue; }
                    int br = c.R - bg.R, bg2 = c.G - bg.G, bb = c.B - bg.B;
                    if (br * br + bg2 * bg2 + bb * bb > 3600) frnCnt++;
                }
                ownRow[i] = ownCnt >= need;
                foreignRow[i] = frnCnt >= need;
            }
            int ownTop = -1, ownBottom = -1;
            for (int i = 0; i < rect.Height; i++) if (ownRow[i]) { if (ownTop < 0) ownTop = i; ownBottom = i; }
            if (ownTop < 0) return rect;
            int top = rect.Top, bottom = rect.Bottom;
            for (int i = ownTop - 1; i >= 0; i--)
                if (foreignRow[i]) { top = rect.Top + Math.Min(ownTop, i + 2); break; }
            for (int i = ownBottom + 1; i < rect.Height; i++)
                if (foreignRow[i]) { bottom = rect.Top + Math.Max(ownBottom + 1, i); break; }
            if (bottom - top < 6) return rect;
            return new Rectangle(rect.Left, top, rect.Width, bottom - top);
        }

        /// <summary>墨迹判据（扩盖探针用）：芯色直接命中（与字色距离 <60），或像素相对
        /// 底色的偏离方向与"底色→字色"方向一致（余弦 >0.8）且偏离量 >22——发光字的
        /// 辉光晕是字色与底色的中间调 blend，芯色口径看不见（实测晕色离字芯距离
        /// 240+、离底色仅 30），方向判据专抓它；随机纹理偏离方向杂乱，过不了余弦门。</summary>
        private static bool IsOwnInk(Color c, Color bg, Color own)
        {
            int dr = c.R - own.R, dg = c.G - own.G, db = c.B - own.B;
            if (dr * dr + dg * dg + db * db < 3600) return true;
            int vr = c.R - bg.R, vg = c.G - bg.G, vb = c.B - bg.B;
            int d2 = vr * vr + vg * vg + vb * vb;
            if (d2 < 484) return false;
            int wr = own.R - bg.R, wg = own.G - bg.G, wb = own.B - bg.B;
            double dot = vr * (double)wr + vg * wg + vb * wb;
            double denom = Math.Sqrt(d2) * Math.Sqrt(wr * wr + wg * wg + wb * wb);
            return denom > 0 && dot / denom > 0.8;
        }

        /// <summary>填充按自有墨迹扩盖（v1.8.3 调整，真机帧实测）：识别行框是收缩核，
        /// 发光字/描边字的笔画尖常戳出框外几像素（字幕首行顶部露一排英文笔尖、行尾
        /// 拖一道横划），固定膨胀量盖不住。自填充框四边向外逐行/逐列探同色墨迹
        /// （与字色距离 <60，口径同 ClampFillToOwnInk），容忍 ≤2 行空隙（辉光晕），
        /// 探到墨迹尽头再垫 2px；纵向探距 0.5 倍行高、横向 0.4 倍行高，不跨带乱爬。</summary>
        private static Rectangle ExpandFillToOwnInk(Bitmap src, Rectangle rect, Color bg, Color own, double lh)
        {
            if (rect.Width < 8 || rect.Height < 6) return rect;
            int vMax = Math.Max(2, (int)Math.Round(lh * 0.5));
            int hMax = Math.Max(2, (int)Math.Round(lh * 0.4));
            int step = 2;
            int left = rect.Left, right = rect.Right, top = rect.Top, bottom = rect.Bottom;
            // 上
            {
                int n = 0; for (int x = rect.Left; x < rect.Right; x += step) n++;
                int need = Math.Max(2, n / 50), empty = 0, found = -1;
                for (int y = rect.Top - 1; y >= Math.Max(0, rect.Top - vMax); y--)
                {
                    int cnt = 0;
                    for (int x = rect.Left; x < rect.Right; x += step) if (IsOwnInk(src.GetPixel(x, y), bg, own)) cnt++;
                    if (cnt >= need) { found = y; empty = 0; }
                    else if (++empty >= 3) break;
                }
                if (found >= 0) top = Math.Max(0, found - 2);
            }
            // 下
            {
                int n = 0; for (int x = rect.Left; x < rect.Right; x += step) n++;
                int need = Math.Max(2, n / 50), empty = 0, found = -1;
                for (int y = rect.Bottom; y < Math.Min(src.Height, rect.Bottom + vMax); y++)
                {
                    int cnt = 0;
                    for (int x = rect.Left; x < rect.Right; x += step) if (IsOwnInk(src.GetPixel(x, y), bg, own)) cnt++;
                    if (cnt >= need) { found = y; empty = 0; }
                    else if (++empty >= 3) break;
                }
                if (found >= 0) bottom = Math.Min(src.Height, found + 3);
            }
            // 左
            {
                int n = 0; for (int y = rect.Top; y < rect.Bottom; y += step) n++;
                int need = Math.Max(2, n / 50), empty = 0, found = -1;
                for (int x = rect.Left - 1; x >= Math.Max(0, rect.Left - hMax); x--)
                {
                    int cnt = 0;
                    for (int y = rect.Top; y < rect.Bottom; y += step) if (IsOwnInk(src.GetPixel(x, y), bg, own)) cnt++;
                    if (cnt >= need) { found = x; empty = 0; }
                    else if (++empty >= 3) break;
                }
                if (found >= 0) left = Math.Max(0, found - 2);
            }
            // 右
            {
                int n = 0; for (int y = rect.Top; y < rect.Bottom; y += step) n++;
                int need = Math.Max(2, n / 50), empty = 0, found = -1;
                for (int x = rect.Right; x < Math.Min(src.Width, rect.Right + hMax); x++)
                {
                    int cnt = 0;
                    for (int y = rect.Top; y < rect.Bottom; y += step) if (IsOwnInk(src.GetPixel(x, y), bg, own)) cnt++;
                    if (cnt >= need) { found = x; empty = 0; }
                    else if (++empty >= 3) break;
                }
                if (found >= 0) right = Math.Min(src.Width, found + 3);
            }
            var r = new Rectangle(left, top, right - left, bottom - top);
            return (r.Width >= 8 && r.Height >= 6) ? r : rect;
        }

        private static double ColorDist(Color a, Color b)
        {
            int dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
            return Math.Sqrt(dr * dr + dg * dg + db * db);
        }

        /// <summary>同带统一填充（v1.8.3 调整，真机人群全景帧实测）：同一字幕带被识别
        /// 拆成两块时，各行各自采样底色——两块一深一浅成双色补丁、块间空隙无人填充、
        /// 原文残迹夹在缝里。把竖向相邻（缝 ≤0.55 倍行高）、横向对齐（重叠 ≥45%）、
        /// 底色相近（距离 <45）、字色同族（距离 <80）的计划归为一带：底色与渐变
        /// 端色统一为面积加权平均；带内相邻计划间缝 ≤0.8 倍行高时两边对半延伸
        /// 填充弥合。底色差大的上下卡片（白卡压红卡）会被距离门拦下，不受影响。</summary>
        private class BandInfo
        {
            public List<LinePlan> Members;
            public bool AllFlat;
            public Color Bg, TopC, BotC;
            public int TopY, BotY;
        }

        private static List<BandInfo> UnifyBandFills(List<LinePlan> plans)
        {
            var result = new List<BandInfo>();
            if (plans.Count < 2) return result;
            var sorted = new List<LinePlan>(plans);
            sorted.Sort((a, b) => a.Rect.Top != b.Rect.Top ? a.Rect.Top.CompareTo(b.Rect.Top) : a.Rect.Left.CompareTo(b.Rect.Left));
            var bands = new List<List<LinePlan>>();
            var cur = new List<LinePlan> { sorted[0] };
            bands.Add(cur);
            for (int i = 1; i < sorted.Count; i++)
            {
                var m = cur[cur.Count - 1];
                var c = sorted[i];
                double lhM = m.SlotRect.Height > 0 ? m.SlotRect.Height : m.Rect.Height;
                double lhC = c.SlotRect.Height > 0 ? c.SlotRect.Height : c.Rect.Height;
                int gap = c.Rect.Top - m.Rect.Bottom;
                int xOv = Math.Min(m.Rect.Right, c.Rect.Right) - Math.Max(m.Rect.Left, c.Rect.Left);
                int minW = Math.Min(m.Rect.Width, c.Rect.Width);
                // 纵向不设下限：识别行框臃肿重叠时相邻计划可重叠几十像素（实测 -77），
                // 那正是同带最需要整铺的情况；只卡上限（缝太宽不是一带）与横向/颜色门。
                bool same = gap <= Math.Max(10, 0.55 * Math.Min(lhM, lhC))
                    && minW > 0 && xOv >= 0.45 * minW
                    && ColorDist(m.Bg, c.Bg) < 45 && ColorDist(m.TextColor, c.TextColor) < 80;
                if (same) cur.Add(c);
                else { cur = new List<LinePlan> { c }; bands.Add(cur); }
            }
            foreach (var band in bands)
            {
                if (band.Count < 2) continue;
                double wsum = 0, br = 0, bg2 = 0, bb = 0, tr = 0, tg = 0, tb = 0, orr = 0, og = 0, ob = 0;
                bool allFlat = true;
                foreach (var p in band)
                {
                    double w = Math.Max(1, p.Rect.Width * (double)p.Rect.Height);
                    wsum += w;
                    br += p.Bg.R * w; bg2 += p.Bg.G * w; bb += p.Bg.B * w;
                    var tc = p.FlatBg ? p.Bg : p.TopC;
                    var bc = p.FlatBg ? p.Bg : p.BotC;
                    tr += tc.R * w; tg += tc.G * w; tb += tc.B * w;
                    orr += bc.R * w; og += bc.G * w; ob += bc.B * w;
                    if (!p.FlatBg) allFlat = false;
                }
                var bandBg = Color.FromArgb((int)(br / wsum), (int)(bg2 / wsum), (int)(bb / wsum));
                var bandTop = Color.FromArgb((int)(tr / wsum), (int)(tg / wsum), (int)(tb / wsum));
                var bandBot = Color.FromArgb((int)(orr / wsum), (int)(og / wsum), (int)(ob / wsum));
                // 渐变切片：整带是一条连续渐变，各计划只取自己纵向位置的那一截，
                // 相邻贴片的端色才能严丝合缝——人人用同一对端色会在带内形成横条纹。
                int bandTopY = int.MaxValue, bandBotY = int.MinValue;
                foreach (var p in band) { bandTopY = Math.Min(bandTopY, p.Rect.Top); bandBotY = Math.Max(bandBotY, p.Rect.Bottom); }
                foreach (var p in band)
                {
                    p.Bg = bandBg;
                    p.FlatBg = allFlat;
                    if (allFlat) { p.TopC = bandBg; p.BotC = bandBg; continue; }
                    double span = Math.Max(1, bandBotY - bandTopY);
                    double t0 = (p.Rect.Top - bandTopY) / span;
                    double t1 = (p.Rect.Bottom - bandTopY) / span;
                    p.TopC = Color.FromArgb(
                        (int)(bandTop.R + (bandBot.R - bandTop.R) * t0),
                        (int)(bandTop.G + (bandBot.G - bandTop.G) * t0),
                        (int)(bandTop.B + (bandBot.B - bandTop.B) * t0));
                    p.BotC = Color.FromArgb(
                        (int)(bandTop.R + (bandBot.R - bandTop.R) * t1),
                        (int)(bandTop.G + (bandBot.G - bandTop.G) * t1),
                        (int)(bandTop.B + (bandBot.B - bandTop.B) * t1));
                }
                for (int i = 1; i < band.Count; i++)
                {
                    var up = band[i - 1];
                    var dn = band[i];
                    int gap = dn.Rect.Top - up.Rect.Bottom;
                    double lhU = up.SlotRect.Height > 0 ? up.SlotRect.Height : up.Rect.Height;
                    double lhD = dn.SlotRect.Height > 0 ? dn.SlotRect.Height : dn.Rect.Height;
                    double lh = Math.Min(lhU, lhD);
                    if (gap <= 0 || gap > Math.Max(12, 0.8 * lh)) continue;
                    int dnShare = gap / 2, upShare = gap - dnShare;
                    if (upShare > 0)
                    {
                        up.Rect = new Rectangle(up.Rect.Left, up.Rect.Top, up.Rect.Width, up.Rect.Height + upShare);
                        for (int s = 0; s < up.PatchHeights.Count; s++) up.PatchHeights[s] += upShare;
                    }
                    if (dnShare > 0)
                    {
                        dn.Rect = new Rectangle(dn.Rect.Left, dn.Rect.Top - dnShare, dn.Rect.Width, dn.Rect.Height + dnShare);
                        for (int s = 0; s < dn.PatchHeights.Count; s++) dn.PatchHeights[s] += dnShare;
                    }
                }
                var info = new BandInfo { Members = band, AllFlat = allFlat, Bg = bandBg, TopC = bandTop, BotC = bandBot, TopY = int.MaxValue, BotY = int.MinValue };
                foreach (var p in band)
                {
                    p.InBand = true;
                    info.TopY = Math.Min(info.TopY, p.Rect.Top);
                    info.BotY = Math.Max(info.BotY, p.Rect.Bottom);
                }
                result.Add(info);
            }
            return result;
        }

        /// <summary>带底整铺：带内各贴片的覆盖并集先整铺一遍统一底（渐变按整带
        /// 纵向一气呵成），贴片间的错位细缝（识别行框臃肿重叠时各贴片 x 范围不一、
        /// 总有几像素宽的竖缝/横缝露原文）由这层底兜住；之后各贴片照旧覆画
        /// （同色系、看不出二次覆盖），文字最后统一绘。保护框照旧挖空。</summary>
        private static void FillBandFloor(Bitmap composed, Graphics g, BandInfo band, IList<Rectangle> protectedRects)
        {
            if (band.Members.Count < 2) return;
            var region = new Region(Rectangle.Empty);
            Rectangle bbox = Rectangle.Empty;
            foreach (var p in band.Members)
            {
                region.Union(p.Rect);
                bbox = bbox.IsEmpty ? p.Rect : Rectangle.Union(bbox, p.Rect);
            }
            if (protectedRects != null)
            {
                foreach (var pr in protectedRects)
                {
                    var box = pr;
                    box.Inflate(1, 1);
                    if (box.IntersectsWith(bbox)) region.Exclude(box);
                }
            }
            var oldClip = g.Clip;
            g.Clip = region;
            if (band.AllFlat)
            {
                using (var brush = new SolidBrush(band.Bg)) g.FillRectangle(brush, bbox);
            }
            else
            {
                var grect = new Rectangle(bbox.Left, band.TopY, Math.Max(1, bbox.Width), Math.Max(1, band.BotY - band.TopY));
                using (var brush = new LinearGradientBrush(grect, band.TopC, band.BotC, LinearGradientMode.Vertical))
                    g.FillRectangle(brush, bbox);
            }
            g.Clip = oldClip;
            region.Dispose();
        }

        private static bool CoveredByBand(BandInfo band, int x, int y)
        {
            foreach (var p in band.Members) if (p.Rect.Contains(x, y)) return true;
            return false;
        }

        /// <summary>带外缘羽化：只柔化整带对外的边界；带内贴片间的内边不羽化
        /// （向原图混合会把缝里的原文笔尖重新透出来，实测整行鬼影）。</summary>
        private static void FeatherBandEdges(Bitmap composed, Bitmap src, BandInfo band, IList<Rectangle> protectedRects)
        {
            if (band.Members.Count < 2) return;
            for (int ring = 0; ring < 2; ring++)
            {
                double a = ring == 0 ? 0.45 : 0.22;
                foreach (var p in band.Members)
                {
                    var r = p.Rect;
                    int t = r.Top + ring, b = r.Bottom - 1 - ring, l = r.Left + ring, rr = r.Right - 1 - ring;
                    if (l > rr || t > b) continue;
                    for (int x = l; x <= rr; x++)
                    {
                        if (!CoveredByBand(band, x, r.Top - 1)) BlendPx(composed, src, x, t, a, protectedRects);
                        if (b != t && !CoveredByBand(band, x, r.Bottom)) BlendPx(composed, src, x, b, a, protectedRects);
                    }
                    for (int y = t + 1; y <= b - 1; y++)
                    {
                        if (!CoveredByBand(band, r.Left - 1, y)) BlendPx(composed, src, l, y, a, protectedRects);
                        if (rr != l && !CoveredByBand(band, r.Right, y)) BlendPx(composed, src, rr, y, a, protectedRects);
                    }
                }
            }
        }

        /// <summary>按实测宽度把一行译文分到各跨：每跨取当前字号下能容纳的最长前缀（就近落边界），
        /// 保证每片不宽于其跨——旧版按宽度比例分片，片宽可超出跨宽，字形压到保护区被裁掉（实测推文丢字）。</summary>
        private static List<string> SplitBySpansMeasured(Graphics g, Font font, string text, List<Rectangle> spans)
        {
            var pieces = new List<string>();
            if (spans.Count <= 1) { pieces.Add(text); return pieces; }
            int pos = 0;
            for (int i = 0; i < spans.Count - 1; i++)
            {
                int remain = text.Length - pos;
                if (remain <= 0) { pieces.Add(""); continue; }
                float avail = Math.Max(8, spans[i].Width - 6);
                int lo = 0, hi = remain;
                while (lo < hi)
                {
                    int mid = (lo + hi + 1) / 2;
                    if (g.MeasureString(text.Substring(pos, mid), font).Width <= avail) lo = mid;
                    else hi = mid - 1;
                }
                int take = Math.Max(1, lo);
                if (take < remain)
                {
                    int cut = FindCut(text, pos, pos + take);
                    if (cut > pos && cut - pos <= take) take = cut - pos;
                }
                pieces.Add(text.Substring(pos, take).Trim());
                pos += take;
                while (pos < text.Length && text[pos] == ' ') pos++;
            }
            pieces.Add(pos < text.Length ? text.Substring(pos).Trim() : "");
            return pieces;
        }

        /// <summary>只铺底不绘字的计划（空片行用）：采样与膨胀口径与 PlanLine 一致。</summary>
        private static LinePlan PlanFillOnly(Bitmap src, LineSlot slot, Rectangle regionRect)
        {
            double lh = slot.LineHeight > 0 ? slot.LineHeight : slot.Rect.Height;
            var rect = slot.Rect;
            int dx = Math.Max(3, Math.Min(10, (int)Math.Round(lh * 0.12)));
            int dy = Math.Max(2, Math.Min(12, (int)Math.Round(lh * 0.25)));
            rect.Inflate(dx, dy);
            rect.Intersect(new Rectangle(0, 0, src.Width, src.Height));
            if (rect.Width < 8 || rect.Height < 6) return null;
            Color bg, fg; bool hasFg; double bgShare;
            SampleRegionColors(src, rect, out bg, out fg, out hasFg, out bgShare);
            bool flatBg = bgShare >= 0.72;
            double lum2 = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255.0;
            var ownEst = hasFg ? fg : (lum2 > 0.55 ? Color.FromArgb(24, 24, 24) : Color.FromArgb(245, 245, 245));
            rect = ClampFillToOwnInk(src, rect, bg, ownEst);
            if (rect.Width < 8 || rect.Height < 6) return null;
            rect = ExpandFillToOwnInk(src, rect, bg, ownEst, lh);
            if (rect.Width < 8 || rect.Height < 6) return null;
            Color topC = bg, botC = bg;
            if (!flatBg) SampleEdgeColors(src, rect, out topC, out botC);
            return new LinePlan
            {
                Rect = rect,
                SlotRect = slot.Rect,
                RegionRect = regionRect,
                Bg = bg,
                TopC = topC,
                BotC = botC,
                TextColor = Color.Black,
                FlatBg = flatBg,
                Size = 9,
                Spans = new List<Rectangle> { rect },
                SpanLines = new List<List<string>> { new List<string>() },
                SpanHeights = new List<int> { 0 },
                PatchHeights = new List<int> { rect.Height },
                ExtraLeft = Rectangle.Empty,
                ExtraRight = Rectangle.Empty
            };
        }

        private static LinePlan PlanLine(Graphics g, Bitmap src, LineSlot slot, string text, Rectangle regionRect, IList<Rectangle> protectedRects)
        {
            if (string.IsNullOrEmpty(text)) return null;
            double lh = slot.LineHeight > 0 ? slot.LineHeight : slot.Rect.Height;
            var rect = slot.Rect;
            // 覆盖膨胀（借 PaddleOCR unclip 与漫画翻译器掩膜膨胀的口径）：识别行框是收缩核，
            // 字形笔画（衬线竖笔、描边）常戳出框外；旧版只 Inflate(3,2)，残片留在贴片外（实测
            // 司法部标题残留整词、字幕第三行整行漏盖）。按行高比例膨胀：横 12%、纵 25%。
            int dx = Math.Max(3, Math.Min(10, (int)Math.Round(lh * 0.12)));
            int dy = Math.Max(2, Math.Min(12, (int)Math.Round(lh * 0.25)));
            rect.Inflate(dx, dy);
            rect.Intersect(new Rectangle(0, 0, src.Width, src.Height));
            if (rect.Width < 8 || rect.Height < 6) return null;

            Color bg, fg; bool hasFg; double bgShare;
            SampleRegionColors(src, rect, out bg, out fg, out hasFg, out bgShare);
            double lum = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255.0;
            bool lightBg = lum > 0.55;
            var textColor = hasFg ? fg : (lightBg ? Color.FromArgb(24, 24, 24) : Color.FromArgb(245, 245, 245));
            bool flatBg = bgShare >= 0.72;
            // 未认领墨迹护栏：带内有异色文字行时先收填充框，再按收后的框取渐变端色/算延伸带。
            rect = ClampFillToOwnInk(src, rect, bg, textColor);
            if (rect.Width < 8 || rect.Height < 6) return null;
            // 自有墨迹扩盖：行框是收缩核，发光/描边字的笔画尖常戳出框外（实测字幕首行
            // 顶部露一排英文笔尖、行尾拖横划），按像素把填充追到墨迹尽头再盖。
            rect = ExpandFillToOwnInk(src, rect, bg, textColor, lh);
            if (rect.Width < 8 || rect.Height < 6) return null;
            Color topC = bg, botC = bg;
            if (!flatBg) SampleEdgeColors(src, rect, out topC, out botC);

            // 整带延伸准入（只查行框之外的延伸区）：延伸区也须高度平整且底色与行底一致才许
            // 整带填充，否则会把渐变/纹理抹成平板（实测玻璃海报被糊成整块纯色）；不达标不延伸。
            Rectangle extraLeft = Rectangle.Empty, extraRight = Rectangle.Empty;
            if (flatBg)
            {
                if (rect.Left - regionRect.Left >= 8)
                {
                    var strip = new Rectangle(regionRect.Left, rect.Top, rect.Left - regionRect.Left, rect.Height);
                    if (StripMatchesBg(src, strip, bg)) extraLeft = strip;
                }
                if (regionRect.Right - rect.Right >= 8)
                {
                    var strip = new Rectangle(rect.Right, rect.Top, regionRect.Right - rect.Right, rect.Height);
                    if (StripMatchesBg(src, strip, bg)) extraRight = strip;
                }
            }

            float baseSize = (float)Math.Max(9.0, Math.Min(44.0, lh * 0.62));
            var spans = FreeSpans(rect, slot.Rect, protectedRects);
            if (spans.Count > 1)
            {
                var kept = spans.FindAll(s => s.Width >= Math.Max(14, baseSize * 1.2));
                if (kept.Count > 0) spans = kept;
            }
            int maxH = Math.Max(rect.Height, Math.Min(regionRect.Bottom + (int)Math.Ceiling(lh) - rect.Top, src.Height - rect.Top));
            float floor = Math.Max(7.0f, baseSize * 0.62f);
            float size = baseSize;
            List<List<string>> spanLines = null;
            List<int> spanHeights = null;
            // 字号拟合（借有道/微信公开口径：行高定字号→折行→逐级缩字兜底）：旧版下限 85% 太早收手，
            // 放不下的行在绘字时被静默丢弃；本版下限放宽到 62%（绝对下限 7px），且每级字号下按实测重分片。
            while (true)
            {
                spanLines = new List<List<string>>();
                spanHeights = new List<int>();
                bool fits = true;
                using (var font = new Font("Microsoft YaHei UI", size, FontStyle.Regular))
                {
                    float flh = font.GetHeight(g);
                    var pieces = SplitBySpansMeasured(g, font, text, spans);
                    for (int s = 0; s < spans.Count; s++)
                    {
                        var ls = pieces[s].Length == 0 ? new List<string>() : WrapText(g, pieces[s], font, spans[s].Width - 6);
                        spanLines.Add(ls);
                        int h = (int)Math.Ceiling(ls.Count * flh);
                        spanHeights.Add(h);
                        if (h > maxH) fits = false;
                    }
                }
                if (fits || size <= floor) break;
                size = Math.Max(floor, size - 0.5f);
            }
            var plan = new LinePlan
            {
                Rect = rect,
                SlotRect = slot.Rect,
                RegionRect = regionRect,
                Bg = bg,
                TopC = topC,
                BotC = botC,
                TextColor = textColor,
                FlatBg = flatBg,
                Size = size,
                Spans = spans,
                SpanLines = spanLines,
                SpanHeights = spanHeights,
                PatchHeights = new List<int>(),
                ExtraLeft = extraLeft,
                ExtraRight = extraRight
            };
            // 贴片高须按绘字的锚定坐标系算：文字从原行框顶（SlotRect.Top）起画，而贴片从
            // 膨胀框顶（rect.Top）起铺，两者差一个纵向膨胀量 anchorOff；旧账只按膨胀系算高，
            // 多行溢出时最后一行正好差这几像素被整行丢弃（实测 "safe area" 差 5px 被裁）。
            int anchorOff = Math.Max(0, slot.Rect.Top - rect.Top);
            foreach (var h in spanHeights)
                plan.PatchHeights.Add(Math.Min(src.Height - rect.Top, Math.Max(rect.Height, anchorOff + h + 4)));
            return plan;
        }

        /// <summary>行内自由跨度：把与该行垂直重叠的保护框从行宽里挖掉，剩下的横向区间（姓名段/时间段这类被符号隔开的段各成一跨）。</summary>
        private static List<Rectangle> FreeSpans(Rectangle rect, Rectangle slotRect, IList<Rectangle> protectedRects)
        {
            var spans = new List<Rectangle>();
            var intervals = new List<int[]> { new[] { rect.Left, rect.Right } };
            if (protectedRects != null)
            {
                foreach (var pr in protectedRects)
                {
                    int vOverlap = Math.Min(pr.Bottom, slotRect.Bottom) - Math.Max(pr.Top, slotRect.Top);
                    if (vOverlap < Math.Min(pr.Height, slotRect.Height) * 0.5) continue;
                    var next = new List<int[]>();
                    foreach (var iv in intervals)
                    {
                        int cutL = pr.Left - 1, cutR = pr.Right + 1;
                        if (cutR <= iv[0] || cutL >= iv[1]) { next.Add(iv); continue; }
                        if (cutL > iv[0]) next.Add(new[] { iv[0], cutL });
                        if (cutR < iv[1]) next.Add(new[] { cutR, iv[1] });
                    }
                    intervals = next;
                }
            }
            foreach (var iv in intervals)
                if (iv[1] - iv[0] >= 8) spans.Add(new Rectangle(iv[0], rect.Top, iv[1] - iv[0], rect.Height));
            if (spans.Count == 0) spans.Add(rect);
            return spans;
        }

        /// <summary>按跨度宽度比例把一行译文分到各跨，切点就近落边界（与 DistributeByProportion 同法）。</summary>
        private static List<string> SplitBySpans(string text, List<Rectangle> spans)
        {
            var pieces = new List<string>();
            if (spans.Count <= 1) { pieces.Add(text); return pieces; }
            int total = 0;
            foreach (var s in spans) total += s.Width;
            int pos = 0, cum = 0;
            for (int i = 0; i < spans.Count - 1; i++)
            {
                cum += spans[i].Width;
                int target = (int)Math.Round(text.Length * (cum / (double)total));
                int cut = FindCut(text, pos, target);
                pieces.Add(text.Substring(pos, cut - pos).Trim());
                pos = cut;
            }
            pieces.Add(text.Substring(pos).Trim());
            return pieces;
        }

        /// <summary>第一遍：只铺底。平底行先补左右延伸带（盖行框之外、同行漏识的词），
        /// 再按跨填充；非平底按跨填充＋羽化。</summary>
        private static void FillLine(Bitmap composed, Graphics g, Bitmap src, LinePlan p, IList<Rectangle> protectedRects)
        {
            var rect = p.Rect;
            if (p.FlatBg && (!p.ExtraLeft.IsEmpty || !p.ExtraRight.IsEmpty))
            {
                int h = 0;
                foreach (var ph in p.PatchHeights) h = Math.Max(h, ph);
                if (!p.ExtraLeft.IsEmpty)
                    FillPatch(composed, g, src, p, new Rectangle(p.ExtraLeft.Left, rect.Top, p.ExtraLeft.Width, h), protectedRects, false);
                if (!p.ExtraRight.IsEmpty)
                    FillPatch(composed, g, src, p, new Rectangle(p.ExtraRight.Left, rect.Top, p.ExtraRight.Width, h), protectedRects, false);
            }
            for (int s = 0; s < p.Spans.Count; s++)
            {
                var patchRect = new Rectangle(p.Spans[s].Left, rect.Top, p.Spans[s].Width, p.PatchHeights[s]);
                FillPatch(composed, g, src, p, patchRect, protectedRects, !p.InBand);
                if (p.ShiftY > 0)
                {
                    // 重排下移：原位一份擦原文，下移一份给译文垫底，两份取并集
                    var shifted = new Rectangle(p.Spans[s].Left, rect.Top + p.ShiftY, p.Spans[s].Width, p.PatchHeights[s]);
                    shifted.Intersect(new Rectangle(0, 0, src.Width, src.Height));
                    if (shifted.Height >= 4) FillPatch(composed, g, src, p, shifted, protectedRects, !p.InBand);
                }
            }
        }

        private static void FillPatch(Bitmap composed, Graphics g, Bitmap src, LinePlan p, Rectangle patchRect, IList<Rectangle> protectedRects, bool feather)
        {
            Region clip = BuildProtectedClip(patchRect, protectedRects);
            var oldClip = g.Clip;
            if (clip != null) g.Clip = clip;
            if (p.FlatBg)
            {
                using (var brush = new SolidBrush(p.Bg))
                    g.FillRectangle(brush, patchRect);
            }
            else
            {
                using (var brush = new LinearGradientBrush(patchRect, p.TopC, p.BotC, LinearGradientMode.Vertical))
                    g.FillRectangle(brush, patchRect);
            }
            g.Clip = oldClip;
            if (clip != null) clip.Dispose();
            if (feather && !p.FlatBg) FeatherEdges(composed, src, patchRect, protectedRects);
        }

        private static Region BuildProtectedClip(Rectangle patchRect, IList<Rectangle> protectedRects)
        {
            if (protectedRects == null || protectedRects.Count == 0) return null;
            var clip = new Region(patchRect);
            bool any = false;
            foreach (var pr in protectedRects)
            {
                var box = pr;
                box.Inflate(1, 1);
                if (box.IntersectsWith(patchRect)) { clip.Exclude(box); any = true; }
            }
            if (!any) { clip.Dispose(); return null; }
            return clip;
        }

        /// <summary>第二遍：只绘字（底已在第一遍统一铺好）。裁剪同样挖空保护框，字形不压符号/头像。</summary>
        private static void DrawLineText(Bitmap composed, Graphics g, LinePlan p, IList<Rectangle> protectedRects)
        {
            var rect = p.Rect;
            using (var font = new Font("Microsoft YaHei UI", p.Size, FontStyle.Regular))
            using (var brush = new SolidBrush(p.TextColor))
            {
                float flh = font.GetHeight(g);
                for (int s = 0; s < p.Spans.Count; s++)
                {
                    if (p.SpanLines[s].Count == 0) continue;
                    var patchRect = new Rectangle(p.Spans[s].Left, rect.Top + p.ShiftY, p.Spans[s].Width, p.PatchHeights[s]);
                    Region clip = BuildProtectedClip(patchRect, protectedRects);
                    var oldClip = g.Clip;
                    if (clip != null) g.Clip = clip;
                    // 绘字锚回原行框：膨胀只服务填充覆盖，文字位置/行距仍按原行几何，
                    // 否则整行文字会随膨胀量漂移（实测合成测试采样点被字形压住）。
                    // ShiftY 为块内重排位移（行槽重叠时后行整体下移，见 RestackBlockPlans）。
                    int anchorDx = p.SlotRect.Left - rect.Left;
                    float ty = p.SlotRect.Top + p.ShiftY + Math.Max(1, (p.SlotRect.Height - p.SpanHeights[s]) / 2);
                    foreach (var line in p.SpanLines[s])
                    {
                        if (ty + flh > patchRect.Bottom + 1) break;
                        g.DrawString(line, font, brush, patchRect.Left + 3 + anchorDx, ty);
                        ty += flh;
                    }
                    g.Clip = oldClip;
                    if (clip != null) clip.Dispose();
                }
            }
        }

        /// <summary>
        /// v1.5.2 C1：在块内按 16 级量化做颜色聚类——占比最大的簇＝背景色（bgShare 为其样本占比）；
        /// 与背景距离最远且占比 ≥2% 的簇＝原文字色（照微信公开管线：聚类字色/背景色）；
        /// 距离不足 55 视为采样不到（hasFg=false），调用方回退亮度二选。
        /// </summary>
        public static void SampleRegionColors(Bitmap img, Rectangle r, out Color bg, out Color fg, out bool hasFg, out double bgShare)
        {
            var buckets = new Dictionary<int, long[]>();
            long total = 0;
            int step = Math.Max(1, Math.Min(r.Width, r.Height) / 90);
            for (int y = r.Top; y < r.Bottom; y += step)
            {
                for (int x = r.Left; x < r.Right; x += step)
                {
                    var c = img.GetPixel(x, y);
                    int key = ((c.R >> 4) << 8) | ((c.G >> 4) << 4) | (c.B >> 4);
                    long[] acc;
                    if (!buckets.TryGetValue(key, out acc))
                    {
                        acc = new long[4];
                        buckets[key] = acc;
                    }
                    acc[0] += c.R; acc[1] += c.G; acc[2] += c.B; acc[3]++;
                    total++;
                }
            }
            bg = Color.FromArgb(240, 240, 240);
            fg = bg;
            hasFg = false;
            bgShare = 0;
            if (total == 0) return;
            long bestCount = -1;
            foreach (var kv in buckets)
            {
                var acc = kv.Value;
                if (acc[3] <= bestCount) continue;
                bestCount = acc[3];
                bg = Color.FromArgb((int)(acc[0] / acc[3]), (int)(acc[1] / acc[3]), (int)(acc[2] / acc[3]));
            }
            bgShare = bestCount / (double)total;
            // v1.7.5 字色口径：候选簇须占比 ≥1%、与底色距离 ≥55、亮度对比 ≥0.22；
            // 得分＝距离×(1−0.85×饱和度²)：真墨色（近黑/近白、低饱和）得分高；抗锯齿灰晕距离不够、
            // 徽章类高饱和色块被饱和惩罚压下去（实测教训：取占比最大采到徽章蓝 (29,155,240)、
            // 取最暗/最远又采到灰晕 (197,198,199)，两种单维口径都翻过车）
            long minFg = Math.Max(3, (long)(total * 0.01));
            double bgLum = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255.0;
            double bestScore = -1;
            foreach (var kv in buckets)
            {
                var acc = kv.Value;
                if (acc[3] < minFg) continue;
                var avg = Color.FromArgb((int)(acc[0] / acc[3]), (int)(acc[1] / acc[3]), (int)(acc[2] / acc[3]));
                double dr = avg.R - bg.R, dg = avg.G - bg.G, db = avg.B - bg.B;
                double d = Math.Sqrt(dr * dr + dg * dg + db * db);
                if (d < 55) continue;
                double candLum = (0.299 * avg.R + 0.587 * avg.G + 0.114 * avg.B) / 255.0;
                double contrast = bgLum > 0.55 ? (bgLum - candLum) : (candLum - bgLum);
                if (contrast < 0.22) continue;
                int mx = Math.Max(avg.R, Math.Max(avg.G, avg.B));
                int mn = Math.Min(avg.R, Math.Min(avg.G, avg.B));
                double sat = mx > 0 ? (mx - mn) / (double)mx : 0;
                double score = d * (1 - 0.85 * sat * sat);
                if (score > bestScore) { bestScore = score; fg = avg; }
            }
            hasFg = bestScore >= 0;
            if (!hasFg) fg = bg;
        }

        /// <summary>v1.5.2 C3：复杂背景时取块上下边缘行的平均色作纵向渐变的起止色。</summary>
        private static void SampleEdgeColors(Bitmap img, Rectangle r, out Color top, out Color bottom)
        {
            top = RowAverage(img, r.Left, Math.Max(0, Math.Min(img.Height - 1, r.Top + 1)), r.Width);
            bottom = RowAverage(img, r.Left, Math.Max(0, Math.Min(img.Height - 1, r.Bottom - 2)), r.Width);
        }

        private static Color RowAverage(Bitmap img, int x0, int y, int w)
        {
            long sr = 0, sg = 0, sb = 0;
            int n = 0;
            int step = Math.Max(1, w / 90);
            for (int x = Math.Max(0, x0); x < x0 + w && x < img.Width; x += step)
            {
                var c = img.GetPixel(x, y);
                sr += c.R; sg += c.G; sb += c.B; n++;
            }
            if (n == 0) return Color.FromArgb(240, 240, 240);
            return Color.FromArgb((int)(sr / n), (int)(sg / n), (int)(sb / n));
        }

        /// <summary>v1.5.2 C3：贴片外两圈与原图做渐变混合（外圈 45%、内圈 22%），边缘融进原背景。
        /// v1.7.5：落在保护框内的像素不混合（符号/头像原样）。</summary>
        private static void FeatherEdges(Bitmap composed, Bitmap src, Rectangle p, IList<Rectangle> protectedRects)
        {
            for (int ring = 0; ring < 2; ring++)
            {
                double a = ring == 0 ? 0.45 : 0.22;
                int l = p.Left + ring, t = p.Top + ring, rgt = p.Right - 1 - ring, btm = p.Bottom - 1 - ring;
                if (l > rgt || t > btm) break;
                for (int x = l; x <= rgt; x++)
                {
                    BlendPx(composed, src, x, t, a, protectedRects);
                    if (btm != t) BlendPx(composed, src, x, btm, a, protectedRects);
                }
                for (int y = t + 1; y <= btm - 1; y++)
                {
                    BlendPx(composed, src, l, y, a, protectedRects);
                    if (rgt != l) BlendPx(composed, src, rgt, y, a, protectedRects);
                }
            }
        }

        private static void BlendPx(Bitmap composed, Bitmap src, int x, int y, double a, IList<Rectangle> protectedRects)
        {
            if (x < 0 || y < 0 || x >= composed.Width || y >= composed.Height) return;
            if (protectedRects != null)
                foreach (var pr in protectedRects)
                    if (x >= pr.Left && x < pr.Right && y >= pr.Top && y < pr.Bottom) return;
            var o = src.GetPixel(x, y);
            var c = composed.GetPixel(x, y);
            composed.SetPixel(x, y, Color.FromArgb(
                (int)(o.R * a + c.R * (1 - a)),
                (int)(o.G * a + c.G * (1 - a)),
                (int)(o.B * a + c.B * (1 - a))));
        }

        // ---------------- v1.5.2 C2 组段 ----------------

        private class RegionAcc
        {
            public Rectangle Union;
            public int FirstLeft;
            public string Text = "";
            public int LineCount;
            public double LhSum;
            public int LhWeight;
            public double LastLh;    // v1.6.4：最近并入块的行高（组段同字号闸用）
            public int LastBottom;   // v1.6.4：最近并入块的底缘（间隙改按相邻行算）
            public List<OcrBlock> Parts = new List<OcrBlock>();   // v1.7.5：原始块清单（逐行回填用）

            public void Add(OcrBlock b)
            {
                Parts.Add(b);
                int lines = Math.Max(1, b.LineCount);
                double lh = b.LineHeight > 0 ? b.LineHeight : b.Rect.Height / (double)lines;
                if (Text.Length == 0)
                {
                    Union = b.Rect;
                    FirstLeft = b.Rect.Left;
                    Text = (b.Text ?? "").Trim();
                }
                else
                {
                    string t = (b.Text ?? "").Trim();
                    string sep = (Text.Length > 0 && t.Length > 0 &&
                        (IsCjk(Text[Text.Length - 1]) || IsCjk(t[0]))) ? "" : " ";
                    Text += sep + t;
                    Union = Rectangle.Union(Union, b.Rect);
                }
                LineCount += lines;
                LhSum += lh * lines;
                LhWeight += lines;
                LastLh = lh;
                LastBottom = b.Rect.Bottom;
            }

            public OcrBlock ToBlock()
            {
                return new OcrBlock
                {
                    Rect = Union,
                    Text = Text,
                    LineCount = LineCount,
                    LineHeight = LhWeight > 0 ? LhSum / LhWeight : 0,
                    Parts = Parts
                };
            }
        }

        private static bool IsCjk(char c)
        {
            return (c >= 0x2E80 && c <= 0x9FFF) || (c >= 0xF900 && c <= 0xFAFF)
                || (c >= 0x3040 && c <= 0x30FF) || (c >= 0xAC00 && c <= 0xD7AF);
        }

        /// <summary>
        /// v1.5.2 C2 组段（照有道"组行→组段→整段翻译→按原排版渲染"管线）：
        /// 纵向紧邻（间隙 ≤ 行高×1.4）且横向对齐（左缘接近或 X 向重叠 ≥40%）的块合并为段，
        /// 整段送译避免逐行断句；合并后由 Compose 在段框内自动换行回填。
        /// </summary>
        public static List<OcrBlock> MergeParagraphs(IList<OcrBlock> blocks)
        {
            var sorted = new List<OcrBlock>();
            foreach (var b in blocks)
                if (b != null && !string.IsNullOrWhiteSpace(b.Text)) sorted.Add(b);
            sorted.Sort((a, b) => a.Rect.Top != b.Rect.Top ? a.Rect.Top - b.Rect.Top : a.Rect.Left - b.Rect.Left);
            var regions = new List<RegionAcc>();
            foreach (var b in sorted)
            {
                RegionAcc target = null;
                for (int i = regions.Count - 1; i >= 0 && i >= regions.Count - 3; i--)
                {
                    if (CanMerge(regions[i], b)) { target = regions[i]; break; }
                }
                if (target == null)
                {
                    target = new RegionAcc();
                    regions.Add(target);
                }
                target.Add(b);
            }
            return regions.ConvertAll(r => r.ToBlock());
        }

        private static bool CanMerge(RegionAcc r, OcrBlock b)
        {
            int bLines = Math.Max(1, b.LineCount);
            double lh = b.LineHeight > 0 ? b.LineHeight : Math.Max(10, b.Rect.Height / (double)bLines);
            // v1.6.4 位置定版（用户反馈：译文移位）：加两道闸——
            // ①同字号闸：相邻行行高比超限不合并（海报大标题/小字幕混排时，合并段的译文从段首
            //   重排、把下面行的译文顶到上面去，真机图里「(Computer Intrusion)」的译文跑位即此）；
            // ②间隙改按相邻行（上一行的底缘）算，不再按合并段总底缘（总底缘会被多行块拉大、掩盖真实行距）。
            // v1.7.5 闸门收紧（方案丙）：行高比 1.5→1.25、间隙 1.4→0.9 倍行高——推文的姓名行与正文行
            // 之间隔着账号行与空隙，按此应各自成段，不再并成一段互相覆盖。
            if (r.LastLh > 0)
            {
                double ratio = Math.Max(lh, r.LastLh) / Math.Min(lh, r.LastLh);
                if (ratio > 1.25) return false;
            }
            int gap = b.Rect.Top - r.LastBottom;
            if (gap < -lh * 0.5 || gap > lh * 0.9) return false;
            int overlap = Math.Min(r.Union.Right, b.Rect.Right) - Math.Max(r.Union.Left, b.Rect.Left);
            int minW = Math.Min(r.Union.Width, b.Rect.Width);
            if (minW > 0 && overlap >= 0.4 * minW) return true;
            return Math.Abs(b.Rect.Left - r.FirstLeft) <= lh * 1.2;
        }

        /// <summary>按宽度折行：含空格的文本按词折（超长词再按字切），中文按字折。</summary>
        private static List<string> WrapText(Graphics g, string text, Font font, int maxW)
        {
            var lines = new List<string>();
            if (maxW < 10) maxW = 10;
            if (text.IndexOf(' ') >= 0)
            {
                string cur = "";
                foreach (var word in text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string trial = cur.Length == 0 ? word : cur + " " + word;
                    if (g.MeasureString(trial, font).Width <= maxW) { cur = trial; continue; }
                    if (cur.Length > 0) { lines.Add(cur); cur = ""; }
                    if (g.MeasureString(word, font).Width <= maxW) { cur = word; continue; }
                    foreach (var piece in WrapChars(g, word, font, maxW))
                    {
                        if (cur.Length > 0) { lines.Add(cur); cur = ""; }
                        cur = piece;
                    }
                }
                if (cur.Length > 0) lines.Add(cur);
            }
            else lines.AddRange(WrapChars(g, text, font, maxW));
            if (lines.Count == 0) lines.Add(text);
            // 行首禁则：收尾标点不许落到行首——某行以收尾标点开头时，把标点提回上一行
            // 末尾（实测译文首行末尾的逗号被折成第二行行首的一个孤逗号，悬在两行之间）。
            for (int i = 1; i < lines.Count; i++)
            {
                string ln = lines[i];
                int k = 0;
                while (k < ln.Length && IsClosingPunct(ln[k])) k++;
                if (k == 0) continue;
                lines[i - 1] += ln.Substring(0, k);
                lines[i] = ln.Substring(k);
            }
            lines.RemoveAll(s => s.Length == 0);
            if (lines.Count == 0) lines.Add(text);
            return lines;
        }

        private static bool IsClosingPunct(char c)
        {
            return "，。、；：？！）】」』”’…·".IndexOf(c) >= 0;
        }

        private static List<string> WrapChars(Graphics g, string text, Font font, int maxW)
        {
            var lines = new List<string>();
            string cur = "";
            foreach (char ch in text)
            {
                if (cur.Length > 0 && g.MeasureString(cur + ch, font).Width > maxW)
                {
                    lines.Add(cur);
                    cur = ch.ToString();
                }
                else cur += ch;
            }
            if (cur.Length > 0) lines.Add(cur);
            return lines;
        }
    }

    /// <summary>
    /// v1.3.0 译图窗：显示原位覆盖合成图。底栏：保存译图 / 复制译图 / 看原图（按住）/ 对照 / 关闭；
    /// Esc 或任意位置右键关闭；底栏可拖动。位置沿用选区正下方居中规则。
    /// </summary>
    public class OverlayResultForm : Form
    {
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private readonly Bitmap _original;
        private readonly Bitmap _composed;
        private readonly Panel _imgPanel;
        private bool _showOriginal;

        /// <summary>点"对照"时由 MainForm 接管（打开对照弹窗并关闭本窗）。</summary>
        public Action OpenCompare;

        public OverlayResultForm(Bitmap original, Bitmap composed, string statusText)
        {
            _original = original;
            _composed = composed;
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            Shown += (s, e) => GlassMotion.FadeIn(this);
            Shown += (s, e) => FontTheme.ApplyTo(this); // v1.5.3 界面字体/字号档
            FormClosing += (s, e) => GlassMotion.TryFadeOut(this, e);
            BackColor = Palette.BarBg;
            Font = new Font("Microsoft YaHei UI", 9f);

            // 图像区按原图比例缩放进工作区上限
            var wa = Screen.PrimaryScreen.WorkingArea;
            double scale = Math.Min(1.0, Math.Min((wa.Width - 32) / (double)original.Width, (wa.Height - 120) / (double)original.Height));
            int iw = Math.Max(120, (int)(original.Width * scale));
            int ih = Math.Max(80, (int)(original.Height * scale));
            ClientSize = new Size(iw, ih + 48);

            _imgPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
            _imgPanel.Paint += (s, e) =>
            {
                var bmp = _showOriginal ? _original : _composed;
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                e.Graphics.DrawImage(bmp, 0, 0, _imgPanel.Width, _imgPanel.Height);
            };
            _imgPanel.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Right) Close();
            };
            Controls.Add(_imgPanel);

            var bar = new Panel { Dock = DockStyle.Bottom, Height = 48, BackColor = Palette.BarBg };
            Controls.Add(bar);
            bar.MouseDown += BarDrag;
            int x = 10;
            x = AddBarBtn(bar, "保存译图", x, (s, e) => SaveImage());
            x = AddBarBtn(bar, "复制译图", x, (s, e) => CopyImage());
            var btnOrig = AddBarBtnObj(bar, "看原图", x);
            btnOrig.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { _showOriginal = true; _imgPanel.Invalidate(); } };
            btnOrig.MouseUp += (s, e) => { _showOriginal = false; _imgPanel.Invalidate(); };
            btnOrig.MouseLeave += (s, e) => { if (_showOriginal) { _showOriginal = false; _imgPanel.Invalidate(); } };
            x += 76;
            x = AddBarBtn(bar, "对照", x, (s, e) => { if (OpenCompare != null) OpenCompare(); });
            x = AddBarBtn(bar, "关闭", x, (s, e) => Close());
            var status = new Label
            {
                Text = (statusText ?? "") + " · 原图 " + original.Width + "×" + original.Height, AutoSize = false, TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(Math.Max(x + 8, ClientSize.Width - 260), 0), Size = new Size(ClientSize.Width - Math.Max(x + 8, ClientSize.Width - 260) - 10, 48),
                ForeColor = Palette.BarSub, BackColor = Color.Transparent
            };
            status.MouseDown += BarDrag;
            bar.Controls.Add(status);
            bar.BringToFront();
            WireRightClose(this);
        }

        private RoundPanel AddBarBtnObj(Panel bar, string text, int x)
        {
            var b = new RoundPanel { Location = new Point(x, 9), Size = new Size(68, 30), Radius = 8, Fill = Palette.BarBtn, NoEdge = true, Cursor = Cursors.Hand };
            var l = new Label
            {
                Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 9.5f), ForeColor = Color.White, BackColor = Color.Transparent
            };
            b.Controls.Add(l);
            bar.Controls.Add(b);
            return b;
        }

        private int AddBarBtn(Panel bar, string text, int x, EventHandler click)
        {
            var b = AddBarBtnObj(bar, text, x);
            b.WireClickThrough(click);
            return x + 76;
        }

        private void BarDrag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, 2, 0);
        }

        private void WireRightClose(Control c)
        {
            c.MouseDown += (s, e) => { if (e.Button == MouseButtons.Right && !IsDisposed) Close(); };
            foreach (Control child in c.Controls) WireRightClose(child);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            try { _original.Dispose(); } catch { }
            try { _composed.Dispose(); } catch { }
        }

        public void PlaceForSelection(Rectangle sel)
        {
            var center = new Point(sel.Left + sel.Width / 2, sel.Top + sel.Height / 2);
            var wa = Screen.FromPoint(center).WorkingArea;
            int x = sel.Left + (sel.Width - Width) / 2;
            x = Math.Max(wa.Left, Math.Min(x, wa.Right - Width));
            int y = sel.Bottom + 12;
            if (y + Height > wa.Bottom)
            {
                y = sel.Top - Height - 12;
                if (y < wa.Top) y = Math.Max(wa.Top, wa.Bottom - Height);
            }
            Location = new Point(x, y);
        }

        private void SaveImage()
        {
            using (var dlg = new SaveFileDialog
            {
                Filter = "PNG 图片|*.png|JPEG 图片|*.jpg",
                FileName = "译图-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png"
            })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    var fmt = Path.GetExtension(dlg.FileName).ToLowerInvariant() == ".jpg"
                        ? System.Drawing.Imaging.ImageFormat.Jpeg
                        : System.Drawing.Imaging.ImageFormat.Png;
                    _composed.Save(dlg.FileName, fmt);
                }
            }
        }

        private void CopyImage()
        {
            try { Clipboard.SetImage((Bitmap)_composed.Clone()); } catch { }
        }
    }

    /// <summary>
    /// v1.5.2 C4 原位呈现（学有道招牌做法）：把合成译图按原选区坐标 1:1 贴回屏幕，
    /// 视觉上就像原地把原文换成了译文。左键点击→回调转组合窗细看；Esc/右键→直接关闭。
    /// </summary>
    public class InPlaceForm : Form
    {
        private readonly Bitmap _img;

        /// <summary>左键点击时触发（MainForm 接管打开组合窗），触发后本窗自动关闭。</summary>
        public Action SwitchToCombo;

        public InPlaceForm(Bitmap composed, Rectangle selRect)
        {
            _img = composed;
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Bounds = selRect;
            KeyPreview = true;
            BackColor = Color.Black;
            Cursor = Cursors.Hand;
            Paint += (s, e) =>
            {
                e.Graphics.DrawImage(_img, 0, 0, Width, Height);
                using (var pen = new Pen(Color.FromArgb(96, 150, 230), 1))
                    e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            };
            MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    var cb = SwitchToCombo;
                    SwitchToCombo = null;
                    if (cb != null) cb();
                    Close();
                }
                else if (e.Button == MouseButtons.Right) Close();
            };
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            try { _img.Dispose(); } catch { }
        }
    }
}
