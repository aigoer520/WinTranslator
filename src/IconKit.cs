using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinTranslator
{
    /// <summary>
    /// v1.5.1 矢量图标库：全部图标程序绘制（24×24 视图框、2px 圆头线条），不再用字体字形拼图标——
    /// 根除真机字体回退导致的图标大小不一/裁字/缺字形（v1.1.1 底栏曾因此翻车）。
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
    /// <summary>幽灵款：无底色、线色跟 Palette.Main（顶栏窗口控制钮用，各皮肤都合衬）；悬停才浮出 PillBg 底。</summary>
    public bool Ghost;
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
        int st = _state;
        if (Ghost)
        {
            if (st == 1) IconKit.RR(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), 8, Palette.PillBg);
            else if (st == 2) IconKit.RR(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), 8, Palette.SelBg);
            int gsz = Math.Min(Width, Height) - 14;
            IconKit.Draw(e.Graphics, Kind, new Rectangle((Width - gsz) / 2, (Height - gsz) / 2, gsz, gsz), Palette.Main);
            return;
        }
        var fill = st == 1 ? IconKit.BtnFillHover : st == 2 ? IconKit.BtnFillDown : IconKit.BtnFill;
        var ink = st == 0 ? IconKit.Ink : IconKit.InkHover;
        IconKit.RR(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), 10, fill, st == 1 ? IconKit.GlowEdge : Color.FromArgb(56, 74, 96));
        int sz = Math.Min(Width, Height) - 16;
        IconKit.Draw(e.Graphics, Kind, new Rectangle((Width - sz) / 2, (Height - sz) / 2, sz, sz), ink, fill);
    }
}

/// <summary>只画一个图标的透明小控件（嵌进片/胶囊里配文字用）。</summary>
public class IconGlyph : Control
{
    public string Kind = "chevron";
    public Color IconColor = IconKit.Ink;
    public IconGlyph()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Enabled = false; // 纯装饰，点击由父控件承接
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        IconKit.Draw(e.Graphics, Kind, new Rectangle(0, 0, Width, Height), IconColor);
    }
}

/// <summary>v1.5.1 关闭徽章：白圆＋矢量 ✕（代字体字形），自绘保证任何机器上都是完整正圆。</summary>
public class BadgeBtn : Control
{
    public BadgeBtn()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Size = new Size(28, 28);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(Color.White)) e.Graphics.FillEllipse(b, 0, 0, Width - 1, Height - 1);
        using (var pen = new Pen(Color.FromArgb(213, 216, 222), 1.2f)) e.Graphics.DrawEllipse(pen, 0, 0, Width - 1, Height - 1);
        int m = Math.Max(5, Width / 4);
        IconKit.Draw(e.Graphics, "close", new Rectangle(m, m, Width - m * 2, Height - m * 2), Color.FromArgb(90, 96, 104));
    }
}

}
