using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;
using WinTranslator;

/// <summary>
/// v1.5.1 图标与按钮重新设计静态稿：不用字体字形拼图标，全部 GDI+ 矢量线条绘制（真机不再缺字形/大小不一）。
/// 三张：01 设计板（图标库＋按钮规范）、02 组合窗应用稿、03 主窗口应用稿。均为设计稿，未进 src。
/// </summary>
public static class IconKit
{
    public static readonly Color Ink = Color.FromArgb(201, 214, 228);   // 常态图标线色（浅灰蓝）
    public static readonly Color InkHover = Color.White;
    public static readonly Color BtnFill = Color.FromArgb(30, 44, 61);  // = Palette.BarBtn
    public static readonly Color BtnFillHover = Color.FromArgb(43, 62, 86);
    public static readonly Color BtnFillDown = Color.FromArgb(23, 42, 64);
    public static readonly Color GlowEdge = Color.FromArgb(86, 178, 246);

    /// <summary>在 rect 内按 24×24 视图框绘制线条图标（2px 圆头线条、圆角转折）。</summary>
    public static void Draw(Graphics g, string kind, Rectangle rect, Color color, Color fillForMask = default(Color))
    {
        float s = rect.Width / 24f;
        var gstate = g.Save();
        g.TranslateTransform(rect.X, rect.Y);
        g.ScaleTransform(s, rect.Height / 24f);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var pen = new Pen(color, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        {
            switch (kind)
            {
                case "copy":
                    g.DrawRectangle(pen, 5, 4, 11, 12);
                    if (fillForMask != default(Color)) using (var b = new SolidBrush(fillForMask)) g.FillRectangle(b, 9, 9, 11, 12);
                    g.DrawRectangle(pen, 9, 9, 11, 12);
                    break;
                case "star":
                    g.DrawPolygon(pen, Star(12, 12, 8.6f, 3.6f));
                    break;
                case "saveimg":
                    g.DrawRectangle(pen, 3.5f, 5, 17, 14);
                    using (var b = new SolidBrush(color)) g.FillEllipse(b, 7.4f, 8.4f, 3, 3);
                    g.DrawLines(pen, new[] { new PointF(4, 17.5f), new PointF(10, 12), new PointF(14, 15), new PointF(17, 12.8f), new PointF(20, 17.5f) });
                    break;
                case "pin":
                    g.DrawEllipse(pen, 7.5f, 3.5f, 9, 9);
                    g.DrawLines(pen, new[] { new PointF(9.2f, 11.4f), new PointF(12, 20.5f), new PointF(14.8f, 11.4f) });
                    break;
                case "refresh":
                    g.DrawArc(pen, 4, 4, 16, 16, -35, 300);
                    g.DrawLines(pen, new[] { new PointF(20.6f, 3.2f), new PointF(20.4f, 7.6f), new PointF(16.4f, 6.6f) });
                    break;
                case "close":
                    g.DrawLine(pen, 6.5f, 6.5f, 17.5f, 17.5f);
                    g.DrawLine(pen, 17.5f, 6.5f, 6.5f, 17.5f);
                    break;
                case "swap":
                    g.DrawLine(pen, 4, 8, 19, 8);
                    g.DrawLines(pen, new[] { new PointF(15.8f, 4.8f), new PointF(19.4f, 8), new PointF(15.8f, 11.2f) });
                    g.DrawLine(pen, 20, 16, 5, 16);
                    g.DrawLines(pen, new[] { new PointF(8.2f, 12.8f), new PointF(4.6f, 16), new PointF(8.2f, 19.2f) });
                    break;
                case "chevron":
                    g.DrawLines(pen, new[] { new PointF(6.5f, 9.5f), new PointF(12, 15), new PointF(17.5f, 9.5f) });
                    break;
                case "extract":
                    g.DrawRectangle(pen, 6.5f, 5, 12, 16);
                    g.DrawRectangle(pen, 9.8f, 3, 5.4f, 4);
                    g.DrawLine(pen, 9.8f, 11, 15.2f, 11);
                    g.DrawLine(pen, 9.8f, 15, 13.4f, 15);
                    break;
                case "home":
                    g.DrawLines(pen, new[] { new PointF(4, 11.5f), new PointF(12, 4.5f), new PointF(20, 11.5f) });
                    g.DrawLines(pen, new[] { new PointF(6.2f, 10), new PointF(6.2f, 19.5f), new PointF(17.8f, 19.5f), new PointF(17.8f, 10) });
                    g.DrawLines(pen, new[] { new PointF(10, 19.5f), new PointF(10, 14.5f), new PointF(14, 14.5f), new PointF(14, 19.5f) });
                    break;
                case "book":
                    g.DrawPath(pen, BookPath());
                    g.DrawLine(pen, 12, 6.4f, 12, 18.6f);
                    break;
                case "history":
                    g.DrawArc(pen, 3.6f, 3.6f, 16.8f, 16.8f, 300, 285);
                    g.DrawLines(pen, new[] { new PointF(4.6f, 3.4f), new PointF(4.2f, 7.8f), new PointF(8.4f, 7.2f) });
                    g.DrawLine(pen, 12, 8, 12, 12.4f);
                    g.DrawLine(pen, 12, 12.4f, 15.2f, 13.6f);
                    break;
                case "gear":
                    // 实心齿轮廓（线条拼齿形只会像星芒）：外轮廓锯齿填充＋中心挖孔
                    using (var gp = new GraphicsPath())
                    {
                        var pts = new System.Collections.Generic.List<PointF>();
                        for (int i = 0; i < 8; i++)
                        {
                            double c = i * Math.PI / 4;
                            foreach (var t in new[] { new KeyValuePair<double, float>(-0.24, 7.3f), new KeyValuePair<double, float>(-0.11, 9.4f), new KeyValuePair<double, float>(0.11, 9.4f), new KeyValuePair<double, float>(0.24, 7.3f) })
                            {
                                double a = c + t.Key;
                                pts.Add(new PointF(12 + (float)(t.Value * Math.Cos(a)), 12 + (float)(t.Value * Math.Sin(a))));
                            }
                        }
                        gp.AddPolygon(pts.ToArray());
                        using (var b = new SolidBrush(color)) g.FillPath(b, gp);
                    }
                    if (fillForMask != default(Color))
                        using (var b = new SolidBrush(fillForMask)) g.FillEllipse(b, 9.1f, 9.1f, 5.8f, 5.8f);
                    break;
                case "speaker":
                    g.DrawPolygon(pen, new[] { new PointF(4, 9.8f), new PointF(7.8f, 9.8f), new PointF(13, 5.6f), new PointF(13, 18.4f), new PointF(7.8f, 14.2f), new PointF(4, 14.2f) });
                    g.DrawArc(pen, 14.5f, 8.6f, 5.4f, 6.8f, -55, 110);
                    g.DrawArc(pen, 16.4f, 6.4f, 8f, 11.2f, -55, 110);
                    break;
                case "search":
                    g.DrawEllipse(pen, 5, 5, 11.5f, 11.5f);
                    g.DrawLine(pen, 14.8f, 14.8f, 20, 20);
                    break;
                case "min":
                    g.DrawLine(pen, 6, 12, 18, 12);
                    break;
                case "max":
                    g.DrawRectangle(pen, 6, 6, 12, 12);
                    break;
                case "restore":
                    g.DrawRectangle(pen, 8.5f, 8.5f, 11, 11);
                    g.DrawLines(pen, new[] { new PointF(5, 15.5f), new PointF(5, 5), new PointF(15.5f, 5) });
                    break;
                case "check":
                    g.DrawLines(pen, new[] { new PointF(5, 13.2f), new PointF(9.8f, 18), new PointF(19, 6.8f) });
                    break;
                case "shot":
                    g.DrawLines(pen, new[] { new PointF(4, 9.5f), new PointF(4, 4), new PointF(9.5f, 4) });
                    g.DrawLines(pen, new[] { new PointF(14.5f, 4), new PointF(20, 4), new PointF(20, 9.5f) });
                    g.DrawLines(pen, new[] { new PointF(20, 14.5f), new PointF(20, 20), new PointF(14.5f, 20) });
                    g.DrawLines(pen, new[] { new PointF(9.5f, 20), new PointF(4, 20), new PointF(4, 14.5f) });
                    break;
                case "menu":
                    g.DrawLine(pen, 5, 7, 19, 7);
                    g.DrawLine(pen, 5, 12, 19, 12);
                    g.DrawLine(pen, 5, 17, 19, 17);
                    break;
                case "globe":
                    g.DrawEllipse(pen, 4, 4, 16, 16);
                    g.DrawEllipse(pen, 8.6f, 4, 6.8f, 16);
                    g.DrawLine(pen, 4.4f, 12, 19.6f, 12);
                    break;
            }
        }
        g.Restore(gstate);
    }

    private static PointF[] Star(float cx, float cy, float rO, float rI)
    {
        var pts = new PointF[10];
        for (int i = 0; i < 10; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / 5;
            float r = i % 2 == 0 ? rO : rI;
            pts[i] = new PointF(cx + (float)(r * Math.Cos(a)), cy + (float)(r * Math.Sin(a)));
        }
        return pts;
    }

    private static GraphicsPath BookPath()
    {
        var p = new GraphicsPath();
        p.AddBezier(4, 6.5f, 8, 4.6f, 11, 5.4f, 12, 7.6f);
        p.AddBezier(12, 7.6f, 13, 5.4f, 16, 4.6f, 20, 6.5f);
        p.AddLine(20, 6.5f, 20, 18.6f);
        p.AddBezier(20, 18.6f, 16, 16.8f, 13, 17.6f, 12, 19.6f);
        p.AddBezier(12, 19.6f, 11, 17.6f, 8, 16.8f, 4, 18.6f);
        p.CloseFigure();
        return p;
    }

    /// <summary>圆角矩形填充（含可选描边）。</summary>
    public static void RR(Graphics g, Rectangle r, int radius, Color fill, Color edge = default(Color))
    {
        using (var path = RoundPath(r, radius))
        {
            using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            if (edge != default(Color)) using (var p = new Pen(edge, 1.2f)) g.DrawPath(p, path);
        }
    }

    public static GraphicsPath RoundPath(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

/// <summary>设计稿用图标按钮：三态（常态/悬停/按下）＋可选下方小字。State 可强制指定用于设计板展示。</summary>
public class IconBtn : Control
{
    public string Kind = "copy";
    public int ForceState = -1; // -1 跟随鼠标；0 常态 1 悬停 2 按下 3 禁用
    private int _state;
    public IconBtn()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent; // 圆角外的四角透出父级底色（初版没设，四角露出控件默认底色发白，实拍已修）
        Cursor = Cursors.Hand;
    }
    protected override void OnMouseEnter(EventArgs e) { _state = 1; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { _state = 0; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { _state = 2; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { _state = 1; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        int st = ForceState >= 0 ? ForceState : _state;
        var fill = st == 1 ? IconKit.BtnFillHover : st == 2 ? IconKit.BtnFillDown : IconKit.BtnFill;
        var ink = st == 3 ? Color.FromArgb(90, 104, 122) : st == 0 ? IconKit.Ink : IconKit.InkHover;
        IconKit.RR(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), 10, fill, st == 1 ? IconKit.GlowEdge : Color.FromArgb(56, 74, 96));
        int sz = Math.Min(Width, Height) - 16;
        IconKit.Draw(e.Graphics, Kind, new Rectangle((Width - sz) / 2, (Height - sz) / 2, sz, sz), ink, fill);
    }
}

public static class IconDesignMock
{
    private static Font F(float sz, bool bold = false) { return new Font("Microsoft YaHei UI", sz, bold ? FontStyle.Bold : FontStyle.Regular); }

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        SkinMode.Current = "frosted";
        Palette.Apply("frosted");

        Board();
        Combo();
        MainWin();
        Console.WriteLine("ICON DESIGN DONE");
    }

    // ---------- 01 设计板 ----------
    private static void Board()
    {
        var form = new Form { FormBorderStyle = FormBorderStyle.None, ClientSize = new Size(1180, 800), StartPosition = FormStartPosition.Manual, Location = new Point(20, 20), BackColor = Palette.Bg };
        form.BackgroundImage = FrostArt.For(1180, 800);
        form.BackgroundImageLayout = ImageLayout.Stretch;
        var cv = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        form.Controls.Add(cv);
        cv.Paint += (s, e) =>
        {
            var g = e.Graphics;
            Text(g, "图标与按钮设计 · 深色玻璃", 36, 26, 17, true, Palette.Main);
            Text(g, "全部程序矢量绘制（2px 圆头线条），不用字体字形——真机不再缺字、不再大小不一", 36, 58, 10.5f, false, Palette.Sub);
            Text(g, "图标库（24×24 视图框，线色随状态变）", 36, 100, 12, true, Palette.Main);
            string[,] icons = {
                { "copy", "复制" }, { "star", "收藏" }, { "saveimg", "存图" }, { "pin", "钉图" },
                { "refresh", "重翻" }, { "close", "关闭" }, { "swap", "交换" }, { "chevron", "展开" },
                { "extract", "提取文本" }, { "shot", "截图翻译" }, { "search", "搜索" }, { "speaker", "朗读" },
                { "home", "首页" }, { "book", "单词本" }, { "history", "我的翻译" }, { "gear", "设置" },
                { "globe", "语言" }, { "check", "确认" }, { "menu", "菜单" }, { "min", "最小化" },
                { "max", "最大化" }, { "restore", "还原" },
            };
            int cols = 8, cw = 136, ch = 108, x0 = 36, y0 = 132;
            for (int i = 0; i < icons.GetLength(0); i++)
            {
                int x = x0 + (i % cols) * cw, y = y0 + (i / cols) * ch;
                IconKit.RR(g, new Rectangle(x, y, 72, 72), 14, Color.FromArgb(22, 32, 46), Color.FromArgb(56, 74, 96));
                IconKit.Draw(g, icons[i, 0], new Rectangle(x + 18, y + 18, 36, 36), IconKit.Ink, Color.FromArgb(22, 32, 46));
                var sf = new StringFormat { Alignment = StringAlignment.Center };
                g.DrawString(icons[i, 1], F(9.5f), new SolidBrush(Palette.Sub), new RectangleF(x - 12, y + 80, 96, 20), sf);
            }
            int by = y0 + 3 * ch + 26;
            Text(g, "按钮规范", 36, by, 12, true, Palette.Main);
            Text(g, "图标钮 38×38 · 圆角 10 · 常态玻璃底，悬停提亮＋蓝辉光边，按下沉一档", 36, by + 28, 10f, false, Palette.Sub);
            Text(g, "主按钮：品牌红胶囊（图标＋文字）；关闭徽章：白圆＋矢量 ✕（不再用字体符号）", 36, by + 52, 10f, false, Palette.Sub);
        };
        // 三态样例（真实控件）
        string[] kinds = { "copy", "star", "pin", "refresh" };
        string[] stateNames = { "常态", "悬停", "按下", "禁用" };
        for (int r = 0; r < kinds.Length; r++)
            for (int c = 0; c < 4; c++)
            {
                var b = new IconBtn { Kind = kinds[r], ForceState = c, Size = new Size(38, 38), Location = new Point(620 + c * 56, 606 + r * 0) };
                if (r > 0) b.Visible = false;
                cv.Controls.Add(b);
            }
        var stateLbl = new Label { Text = "复制钮四态 →", Location = new Point(470, 612), AutoSize = true, Font = F(10f), ForeColor = Palette.Sub, BackColor = Color.Transparent };
        cv.Controls.Add(stateLbl);
        // 主按钮样例：红胶囊（提取文本）
        var pill = new Panel { Location = new Point(36, 596), Size = new Size(150, 40), BackColor = Color.Transparent };
        pill.Paint += (s, e) =>
        {
            using (var lg = new LinearGradientBrush(new Rectangle(0, 0, 150, 40), Color.FromArgb(246, 84, 62), Color.FromArgb(224, 42, 22), 90f))
            using (var path = IconKit.RoundPath(new Rectangle(0, 0, 149, 39), 20))
                e.Graphics.FillPath(lg, path);
            IconKit.Draw(e.Graphics, "extract", new Rectangle(16, 9, 22, 22), Color.White);
            e.Graphics.DrawString("提取文本", F(10.5f, true), Brushes.White, 44, 10);
        };
        cv.Controls.Add(pill);
        // 玻璃片样例（语言片）
        var chip = new Panel { Location = new Point(36, 652), Size = new Size(170, 38), BackColor = Color.Transparent };
        chip.Paint += (s, e) =>
        {
            IconKit.RR(e.Graphics, new Rectangle(0, 0, 169, 37), 10, IconKit.BtnFill, Color.FromArgb(56, 74, 96));
            e.Graphics.DrawString("检测到英语", F(10f), new SolidBrush(Palette.Main), 12, 9);
            IconKit.Draw(e.Graphics, "chevron", new Rectangle(138, 9, 20, 20), IconKit.Ink);
        };
        cv.Controls.Add(chip);
        // 关闭徽章样例
        var badge = new Panel { Location = new Point(230, 652), Size = new Size(38, 38), BackColor = Color.Transparent };
        badge.Paint += (s, e) =>
        {
            using (var b = new SolidBrush(Color.White)) e.Graphics.FillEllipse(b, 4, 4, 30, 30);
            using (var p = new Pen(Color.FromArgb(213, 216, 222), 1.2f)) e.Graphics.DrawEllipse(p, 4, 4, 30, 30);
            IconKit.Draw(e.Graphics, "close", new Rectangle(11, 11, 16, 16), Color.FromArgb(90, 96, 104));
        };
        cv.Controls.Add(badge);
        // 开关样例（开/关）
        var tgl = new Panel { Location = new Point(300, 652), Size = new Size(120, 38), BackColor = Color.Transparent };
        tgl.Paint += (s, e) =>
        {
            DrawToggle(e.Graphics, 0, 7, true);
            DrawToggle(e.Graphics, 64, 7, false);
        };
        cv.Controls.Add(tgl);

        form.Show();
        Application.DoEvents();
        Thread.Sleep(700);
        Grab(form.Bounds, "/tmp/icon-board.png");
        form.Close();
    }

    private static void DrawToggle(Graphics g, int x, int y, bool on)
    {
        IconKit.RR(g, new Rectangle(x, y, 46, 26), 13, on ? Color.FromArgb(46, 124, 246) : Color.FromArgb(52, 64, 80));
        using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, x + (on ? 23 : 3), y + 3, 20, 20);
    }

    private static void Text(Graphics g, string t, int x, int y, float sz, bool bold, Color c)
    {
        g.DrawString(t, F(sz, bold), new SolidBrush(c), x, y);
    }

    // ---------- 02 组合窗应用稿 ----------
    private static void Combo()
    {
        Bitmap crop, composed;
        using (var full = new Bitmap("/tmp/ref.png"))
        {
            var rect = new Rectangle(36, 41, 418, 585);
            crop = full.Clone(new Rectangle(rect.X + 15, rect.Y + 15, rect.Width - 30, rect.Height - 30), full.PixelFormat);
        }
        var blocks = OcrService.RecognizeBlocks((Bitmap)crop.Clone());
        var trs = new System.Collections.Generic.List<string>();
        foreach (var b in blocks) trs.Add("被击败的巴西总统候选人及其家人抵达迈阿密");
        composed = OverlayComposer.Compose(crop, blocks, trs);

        var form = new Form { FormBorderStyle = FormBorderStyle.None, ClientSize = new Size(1080, 880), StartPosition = FormStartPosition.Manual, Location = new Point(30, 30), BackColor = Palette.Frame };
        form.BackgroundImage = FrostArt.For(1080, 880);
        form.BackgroundImageLayout = ImageLayout.Stretch;
        form.Region = new Region(IconKit.RoundPath(new Rectangle(0, 0, 1080, 880), 16));

        // 双图（按 v1.5.1 修订：尽量放大——本稿 415 宽原图按近原尺寸显示）
        int iw = 415, ih = 556, x0 = (1080 - (iw * 2 + 12)) / 2;
        AddCap(form, "原截图", x0, 12);
        AddCap(form, "译图", x0 + iw + 12, 12);
        AddPic(form, crop, x0, 34, iw, ih);
        AddPic(form, composed, x0 + iw + 12, 34, iw, ih);
        // 保存/复制译图：图标＋文字玻璃钮
        AddTextBtn(form, "saveimg", "保存译图", x0 + iw + 12, 34 + ih + 8, 118);
        AddTextBtn(form, "copy", "复制译图", x0 + iw + 12 + 128, 34 + ih + 8, 118);
        var note = new Label { Text = "原图 415×556 · 存图为原尺寸", Location = new Point(1080 - 330, 34 + ih + 14), Size = new Size(300, 20), TextAlign = ContentAlignment.MiddleRight, Font = F(9f), ForeColor = Palette.Sub, BackColor = Color.Transparent };
        form.Controls.Add(note);

        // 下部：只留原文卡（v1.5.1 修订二）
        int cardY = 34 + ih + 52;
        var card = new Panel { Location = new Point(18, cardY), Size = new Size(1080 - 36, 118), BackColor = Color.Transparent };
        card.Paint += (s, e) => IconKit.RR(e.Graphics, new Rectangle(0, 0, card.Width - 1, card.Height - 1), 14, Palette.Card, Palette.Border);
        form.Controls.Add(card);
        var srcTxt = new Label
        {
            Text = "DEFEATED BRAZILIAN PRESIDENTIAL CANDIDATE FLAVIO BOLSONARO AND HIS FAMILY LAND IN MIAMI  Oct. 28  Opa-locka, Fla.",
            Location = new Point(26, 20), Size = new Size(card.Width - 52, 80), Font = F(13f), ForeColor = Palette.Main, BackColor = Color.Transparent
        };
        card.Controls.Add(srcTxt);

        // 底栏：语言片（文字＋矢量 chevron）、交换图标钮、对照开关、图标钮组、红胶囊
        int barY = 880 - 18 - 54;
        var bar = new Panel { Location = new Point(18, barY), Size = new Size(1080 - 36, 54), BackColor = Color.Transparent };
        bar.Paint += (s, e) => IconKit.RR(e.Graphics, new Rectangle(0, 0, bar.Width - 1, bar.Height - 1), 14, Palette.BarBg, Color.FromArgb(40, 54, 72));
        form.Controls.Add(bar);
        AddChip(bar, "检测到英语", 14, 12, 138, true);
        var swap = new IconBtn { Kind = "swap", Size = new Size(38, 34), Location = new Point(158, 10) };
        bar.Controls.Add(swap);
        AddChip(bar, "自动", 204, 12, 96, true);
        var cmpLbl = new Label { Text = "原文", Location = new Point(316, 17), AutoSize = true, Font = F(10f), ForeColor = Color.White, BackColor = Color.Transparent };
        bar.Controls.Add(cmpLbl);
        var tglP = new Panel { Location = new Point(352, 14), Size = new Size(46, 26), BackColor = Color.Transparent };
        tglP.Paint += (s, e) => DrawToggle(e.Graphics, 0, 0, true);
        bar.Controls.Add(tglP);
        string[] kinds = { "copy", "star", "saveimg", "pin", "refresh", "close" };
        for (int i = 0; i < kinds.Length; i++)
        {
            var b = new IconBtn { Kind = kinds[i], Size = new Size(38, 38), Location = new Point(420 + i * 46, 8) };
            bar.Controls.Add(b);
        }
        var status = new Label { Text = "识别原文（来自原位覆盖译图）", Location = new Point(706, 17), AutoSize = true, Font = F(9.5f), ForeColor = Palette.BarSub, BackColor = Color.Transparent };
        bar.Controls.Add(status);
        var pill = new Panel { Location = new Point(bar.Width - 132, 11), Size = new Size(118, 32), BackColor = Color.Transparent };
        pill.Paint += (s, e) =>
        {
            using (var lg = new LinearGradientBrush(new Rectangle(0, 0, 118, 32), Color.FromArgb(246, 84, 62), Color.FromArgb(224, 42, 22), 90f))
            using (var path = IconKit.RoundPath(new Rectangle(0, 0, 117, 31), 16))
                e.Graphics.FillPath(lg, path);
            IconKit.Draw(e.Graphics, "extract", new Rectangle(12, 6, 20, 20), Color.White);
            e.Graphics.DrawString("提取文本", F(10f, true), Brushes.White, 36, 7);
        };
        bar.Controls.Add(pill);

        // 关闭徽章（矢量 ✕，浮于最上层）
        var badge = new Panel { Location = new Point(1080 - 40, 8), Size = new Size(28, 28), BackColor = Color.Transparent };
        badge.Paint += (s, e) =>
        {
            using (var b = new SolidBrush(Color.White)) e.Graphics.FillEllipse(b, 0, 0, 27, 27);
            using (var p = new Pen(Color.FromArgb(213, 216, 222), 1.2f)) e.Graphics.DrawEllipse(p, 0, 0, 27, 27);
            IconKit.Draw(e.Graphics, "close", new Rectangle(6, 6, 15, 15), Color.FromArgb(90, 96, 104));
        };
        form.Controls.Add(badge);
        badge.BringToFront();

        form.Show();
        Application.DoEvents();
        Thread.Sleep(800);
        Grab(form.Bounds, "/tmp/icon-combo.png");
        form.Close();
    }

    private static void AddCap(Form form, string text, int x, int y)
    {
        form.Controls.Add(new Label { Text = text, Location = new Point(x, y), AutoSize = true, Font = F(9.5f), ForeColor = Palette.Sub, BackColor = Color.Transparent });
    }

    private static void AddPic(Form form, Bitmap img, int x, int y, int w, int h)
    {
        form.Controls.Add(new PictureBox { Image = img, SizeMode = PictureBoxSizeMode.StretchImage, Location = new Point(x, y), Size = new Size(w, h), BorderStyle = BorderStyle.FixedSingle });
    }

    private static void AddTextBtn(Control parent, string kind, string text, int x, int y, int w)
    {
        var p = new Panel { Location = new Point(x, y), Size = new Size(w, 32), BackColor = Color.Transparent, Cursor = Cursors.Hand };
        p.Paint += (s, e) =>
        {
            IconKit.RR(e.Graphics, new Rectangle(0, 0, w - 1, 31), 8, IconKit.BtnFill, Color.FromArgb(56, 74, 96));
            IconKit.Draw(e.Graphics, kind, new Rectangle(10, 6, 20, 20), IconKit.Ink);
            e.Graphics.DrawString(text, F(10f), new SolidBrush(Palette.Main), 36, 7);
        };
        parent.Controls.Add(p);
    }

    private static void AddChip(Control parent, string text, int x, int y, int w, bool chevron)
    {
        var p = new Panel { Location = new Point(x, y), Size = new Size(w, 30), BackColor = Color.Transparent, Cursor = Cursors.Hand };
        p.Paint += (s, e) =>
        {
            IconKit.RR(e.Graphics, new Rectangle(0, 0, w - 1, 29), 8, IconKit.BtnFill);
            e.Graphics.DrawString(text, F(9.5f), new SolidBrush(Color.White), 10, 6);
            if (chevron) IconKit.Draw(e.Graphics, "chevron", new Rectangle(w - 26, 6, 18, 18), IconKit.Ink);
        };
        parent.Controls.Add(p);
    }

    // ---------- 03 主窗口应用稿 ----------
    private static void MainWin()
    {
        var form = new Form { FormBorderStyle = FormBorderStyle.None, ClientSize = new Size(1080, 700), StartPosition = FormStartPosition.Manual, Location = new Point(40, 40), BackColor = Palette.Bg };
        form.BackgroundImage = FrostArt.For(1080, 700);
        form.BackgroundImageLayout = ImageLayout.Stretch;
        form.Region = new Region(IconKit.RoundPath(new Rectangle(0, 0, 1080, 700), 16));

        // 顶栏：左标题点＋名称；右窗口控制（矢量线条图标）
        var dot = new Panel { Location = new Point(18, 14), Size = new Size(12, 12), BackColor = Color.Transparent };
        dot.Paint += (s, e) => { using (var b = new SolidBrush(Palette.Accent)) e.Graphics.FillEllipse(b, 0, 0, 11, 11); };
        form.Controls.Add(dot);
        form.Controls.Add(new Label { Text = "WinTranslator", Location = new Point(38, 9), AutoSize = true, Font = F(10.5f, true), ForeColor = Palette.Main, BackColor = Color.Transparent });
        var wc = new[] { "min", "max", "close" };
        for (int i = 0; i < 3; i++)
        {
            var b = new IconBtn { Kind = wc[i], Size = new Size(40, 32), Location = new Point(1080 - 136 + i * 44, 8) };
            form.Controls.Add(b);
        }

        // 左导航：图标＋文字（选中态亮底＋蓝辉光边）
        string[,] nav = { { "home", "首页" }, { "book", "单词本" }, { "history", "我的翻译" } };
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            var item = new Panel { Location = new Point(14, 64 + i * 52), Size = new Size(138, 44), BackColor = Color.Transparent, Cursor = Cursors.Hand };
            item.Paint += (s, e) =>
            {
                if (idx == 0) IconKit.RR(e.Graphics, new Rectangle(0, 0, 137, 43), 10, Palette.SelBg, IconKit.GlowEdge);
                IconKit.Draw(e.Graphics, nav[idx, 0], new Rectangle(12, 10, 24, 24), idx == 0 ? Color.White : IconKit.Ink);
                e.Graphics.DrawString(nav[idx, 1], F(10.5f, idx == 0), new SolidBrush(idx == 0 ? Palette.Main : Palette.Sub), 46, 12);
            };
            form.Controls.Add(item);
        }
        var setItem = new Panel { Location = new Point(14, 700 - 64), Size = new Size(138, 44), BackColor = Color.Transparent, Cursor = Cursors.Hand };
        setItem.Paint += (s, e) =>
        {
            IconKit.Draw(e.Graphics, "gear", new Rectangle(12, 10, 24, 24), IconKit.Ink);
            e.Graphics.DrawString("设置", F(10.5f), new SolidBrush(Palette.Sub), 46, 12);
        };
        form.Controls.Add(setItem);

        // 输入卡＋操作行（截图图标钮、翻译主按钮）
        var inCard = new Panel { Location = new Point(178, 64), Size = new Size(876, 190), BackColor = Color.Transparent };
        inCard.Paint += (s, e) => IconKit.RR(e.Graphics, new Rectangle(0, 0, inCard.Width - 1, inCard.Height - 1), 16, Palette.Card, Palette.Border);
        form.Controls.Add(inCard);
        inCard.Controls.Add(new Label { Text = "DEFEATED BRAZILIAN PRESIDENTIAL CANDIDATE AND HIS FAMILY LAND IN MIAMI", Location = new Point(20, 18), Size = new Size(820, 60), Font = F(13f), ForeColor = Palette.Main, BackColor = Color.Transparent });
        inCard.Controls.Add(new Label { Text = "47 / 5000", Location = new Point(20, 150), AutoSize = true, Font = F(9f), ForeColor = Palette.Sub, BackColor = Color.Transparent });
        var shotBtn = new IconBtn { Kind = "shot", Size = new Size(40, 36), Location = new Point(700, 142) };
        inCard.Controls.Add(shotBtn);
        var trBtn = new Panel { Location = new Point(752, 142), Size = new Size(104, 36), BackColor = Color.Transparent, Cursor = Cursors.Hand };
        trBtn.Paint += (s, e) =>
        {
            using (var lg = new LinearGradientBrush(new Rectangle(0, 0, 104, 36), Color.FromArgb(246, 84, 62), Color.FromArgb(224, 42, 22), 90f))
            using (var path = IconKit.RoundPath(new Rectangle(0, 0, 103, 35), 18))
                e.Graphics.FillPath(lg, path);
            e.Graphics.DrawString("翻 译", F(11f, true), Brushes.White, 30, 8);
        };
        inCard.Controls.Add(trBtn);

        // 引擎片行
        string[] engines = { "有道", "必应", "智谱 GLM" };
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            var chip = new Panel { Location = new Point(178 + i * 118, 270), Size = new Size(106, 34), BackColor = Color.Transparent, Cursor = Cursors.Hand };
            chip.Paint += (s, e) =>
            {
                IconKit.RR(e.Graphics, new Rectangle(0, 0, 105, 33), 9, idx == 0 ? Palette.SelBg : Palette.PillBg, idx == 0 ? IconKit.GlowEdge : Palette.Border);
                if (idx == 0) IconKit.Draw(e.Graphics, "check", new Rectangle(10, 7, 20, 20), IconKit.GlowEdge);
                e.Graphics.DrawString(engines[idx], F(10f), new SolidBrush(Palette.Main), idx == 0 ? 36 : 14, 8);
            };
            form.Controls.Add(chip);
        }
        var langLbl = new Label { Text = "英语 → 中文", Location = new Point(580, 276), AutoSize = true, Font = F(10f), ForeColor = Palette.Sub, BackColor = Color.Transparent };
        form.Controls.Add(langLbl);
        var swap2 = new IconBtn { Kind = "swap", Size = new Size(38, 34), Location = new Point(530, 270) };
        form.Controls.Add(swap2);

        // 输出卡
        var outCard = new Panel { Location = new Point(178, 322), Size = new Size(876, 300), BackColor = Color.Transparent };
        outCard.Paint += (s, e) => IconKit.RR(e.Graphics, new Rectangle(0, 0, outCard.Width - 1, outCard.Height - 1), 16, Palette.Card, Palette.Border);
        form.Controls.Add(outCard);
        outCard.Controls.Add(new Label { Text = "被击败的巴西总统候选人及其家人抵达迈阿密", Location = new Point(20, 18), Size = new Size(820, 40), Font = F(13f), ForeColor = Palette.Main, BackColor = Color.Transparent });
        var copyBtn = new IconBtn { Kind = "copy", Size = new Size(38, 38), Location = new Point(20, 240) };
        outCard.Controls.Add(copyBtn);
        var starBtn = new IconBtn { Kind = "star", Size = new Size(38, 38), Location = new Point(66, 240) };
        outCard.Controls.Add(starBtn);
        var spkBtn = new IconBtn { Kind = "speaker", Size = new Size(38, 38), Location = new Point(112, 240) };
        outCard.Controls.Add(spkBtn);

        form.Show();
        Application.DoEvents();
        Thread.Sleep(800);
        Grab(form.Bounds, "/tmp/icon-main.png");
        form.Close();
    }

    private static void Grab(Rectangle r, string path)
    {
        using (var bmp = new Bitmap(r.Width, r.Height))
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(r.Location, Point.Empty, r.Size);
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
