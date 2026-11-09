using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;

// v1.5.3 界面美化设计稿（静态板）：留白加大、层级拉开、红色收敛、卡片改白底细边+例句浅灰内嵌块。
// 只画图给用户检查，不动产品代码。
internal static class DesignV153
{
    private static readonly Color Bg = Color.FromArgb(247, 248, 250);
    private static readonly Color NavBg = Color.FromArgb(245, 243, 239);
    private static readonly Color Card = Color.White;
    private static readonly Color Border = Color.FromArgb(228, 231, 236);
    private static readonly Color Main = Color.FromArgb(35, 39, 46);
    private static readonly Color Sub = Color.FromArgb(144, 149, 158);
    private static readonly Color Accent = Color.FromArgb(233, 61, 46);
    private static readonly Color SelBg = Color.FromArgb(234, 241, 254);
    private static readonly Color PillBg = Color.FromArgb(238, 240, 244);
    private static readonly Color Inset = Color.FromArgb(245, 247, 250);
    private static readonly Color Blue = Color.FromArgb(59, 130, 246);
    private static readonly Color BarBg = Color.FromArgb(50, 54, 62);
    private static readonly Color BarBtn = Color.FromArgb(66, 71, 80);

    private static Font F(float pt, bool bold = false)
    {
        return new Font("Microsoft YaHei UI", pt, bold ? FontStyle.Bold : FontStyle.Regular);
    }

    private static void RR(Graphics g, Rectangle r, int rad, Color fill, Color border = default(Color))
    {
        using (var path = Path(r, rad))
        {
            if (fill != Color.Transparent) using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            if (border != default(Color)) using (var p = new Pen(border, 1)) g.DrawPath(p, path);
        }
    }

    private static GraphicsPath Path(Rectangle r, int rad)
    {
        var p = new GraphicsPath();
        int d = rad * 2;
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static void T(Graphics g, string s, Font f, Color c, float x, float y)
    {
        using (var b = new SolidBrush(c)) g.DrawString(s, f, b, x, y);
    }

    private static void TR(Graphics g, string s, Font f, Color c, Rectangle r)
    {
        using (var b = new SolidBrush(c))
        using (var sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
            g.DrawString(s, f, b, r, sf);
    }

    private static void TC(Graphics g, string s, Font f, Color c, Rectangle r)
    {
        using (var b = new SolidBrush(c))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            g.DrawString(s, f, b, r, sf);
    }

    private static float Wrap(Graphics g, string s, Font f, Color c, RectangleF r)
    {
        using (var b = new SolidBrush(c))
        using (var sf = new StringFormat { LineAlignment = StringAlignment.Near })
        {
            g.DrawString(s, f, b, r, sf);
            return g.MeasureString(s, f, (int)r.Width, sf).Height;
        }
    }

    private static void Shadow(Graphics g, Rectangle r, int rad)
    {
        for (int i = 3; i >= 1; i--)
        {
            var rr = new Rectangle(r.Left - i, r.Top - i + 2, r.Width + i * 2, r.Height + i * 2);
            using (var path = Path(rr, rad + i))
            using (var b = new SolidBrush(Color.FromArgb(10, 30, 35, 45)))
                g.FillPath(b, path);
        }
    }

    private static void Pill(Graphics g, Rectangle r, string text, Font f, Color fill, Color fg, bool border = false)
    {
        RR(g, r, r.Height / 2, fill, border ? Border : default(Color));
        TC(g, text, f, fg, r);
    }

    // 左侧导航（各板共用）：active 为当前页
    private static void Nav(Graphics g, string active)
    {
        using (var b = new SolidBrush(NavBg)) g.FillRectangle(b, 0, 0, 172, 800);
        T(g, "WinTranslator", F(13, true), Accent, 22, 20);
        string[][] items = {
            new[] { "首页", "home" }, new[] { "单词本", "book" }, new[] { "我的翻译", "hist" },
            new[] { "截图翻译", "shot" }, new[] { "取词", "pick" }, new[] { "划词", "sel" } };
        int y = 84;
        foreach (var it in items)
        {
            bool on = it[0] == active;
            if (on) RR(g, new Rectangle(12, y - 4, 148, 38), 10, SelBg);
            using (var b = new SolidBrush(on ? Blue : Sub))
                g.FillEllipse(b, 26, y + 7, 12, 12);
            T(g, it[0], F(10.5f, on), on ? Main : Color.FromArgb(90, 95, 105), 50, y + 2);
            y += 52;
        }
        // 取词/划词开关示意（底部两行）
        T(g, "完成 · 就绪", F(8.5f), Sub, 22, 762);
    }

    private static void TopBar(Graphics g, string title)
    {
        T(g, title, F(16, true), Main, 202, 26);
        TR(g, "—    □    ✕", F(10), Sub, new Rectangle(900, 18, 250, 26));
    }

    private static Bitmap NewBoard()
    {
        var bmp = new Bitmap(1180, 800);
        var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Bg);
        return bmp;
    }

    private static void Save(Bitmap bmp, string name)
    {
        Directory.CreateDirectory("/tmp/boards");
        bmp.Save("/tmp/boards/" + name);
        bmp.Dispose();
    }

    [STAThread]
    public static void Entry()
    {
        BoardWordbook();
        BoardHome();
        BoardHistory();
        BoardCombo();
        BoardSmallWindows();
        BoardSettings();
        Console.WriteLine("boards done");
    }

    // ---------- 板 1：单词本（用户点名拥挤的界面） ----------
    private static void BoardWordbook()
    {
        var bmp = NewBoard();
        var g = Graphics.FromImage(bmp);
        Nav(g, "单词本");
        TopBar(g, "单词本");

        // 工具行：分组页签 + 排序/筛选 + 右侧复习与视图切换，全部加高加距
        int x = 202;
        Pill(g, new Rectangle(x, 74, 92, 36), "单词 2", F(10, true), Color.White, Main, true); x += 104;
        Pill(g, new Rectangle(x, 74, 92, 36), "句子 1", F(10), PillBg, Color.FromArgb(90, 95, 105)); x += 128;
        T(g, "排序  新→旧 ▾", F(9.5f), Color.FromArgb(90, 95, 105), x, 82); x += 128;
        T(g, "筛选  全部 ▾", F(9.5f), Color.FromArgb(90, 95, 105), x, 82);
        // 复习按钮（留足右边距，角标不被裁）
        var rev = new Rectangle(952, 72, 108, 38);
        RR(g, rev, 19, Accent);
        TC(g, "✓ 复习", F(10.5f, true), Color.White, rev);
        using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, 1042, 62, 20, 20);
        using (var p = new Pen(Accent, 1.5f)) g.DrawEllipse(p, 1042, 62, 20, 20);
        TC(g, "2", F(8, true), Accent, new Rectangle(1042, 62, 20, 20));
        // 视图切换段
        var seg = new Rectangle(952, 122, 108, 34);
        RR(g, seg, 9, PillBg);
        RR(g, new Rectangle(954, 124, 52, 30), 7, Color.White);
        TC(g, "列表", F(9.5f), Main, new Rectangle(954, 124, 52, 30));
        TC(g, "卡片", F(9.5f, true), Accent, new Rectangle(1006, 124, 52, 30));

        // 搜索框（整行、加高）
        var search = new Rectangle(202, 122, 560, 38);
        RR(g, search, 10, Color.White, Border);
        T(g, "⌕  搜索单词或句子…", F(9.5f), Sub, 218, 130);

        // 日期组标题
        T(g, "2026-10-29", F(11, true), Main, 202, 182);

        // 展开卡（白底细边、内边距 22、例句浅灰内嵌块）
        var card = new Rectangle(202, 214, 858, 372);
        Shadow(g, card, 14);
        RR(g, card, 14, Card, Border);
        int cx = card.Left + 22, cw = card.Width - 44;
        T(g, "01", F(10), Sub, cx, card.Top + 18);
        T(g, "fold", F(15, true), Main, cx + 34, card.Top + 12);
        T(g, "/fəʊld/", F(9.5f), Sub, cx + 108, card.Top + 20);
        // 右侧安静的操作钮
        Pill(g, new Rectangle(card.Right - 196, card.Top + 14, 84, 30), "▶ 朗读", F(9), PillBg, Color.FromArgb(90, 95, 105));
        Pill(g, new Rectangle(card.Right - 104, card.Top + 14, 82, 30), "去翻译", F(9), PillBg, Color.FromArgb(90, 95, 105));
        float yy = card.Top + 58;
        yy += Wrap(g, "v. 折叠，对折；倒闭，垮掉；交叉，交叠；包，裹；拥抱；拌入；认输；抽筋，双腿发软；使（岩石层）形成褶皱；（尤指运动员或运动队）突然不在状态（或发挥失常）；把（牲畜）圈在栏中",
            F(10), Color.FromArgb(60, 65, 75), new RectangleF(cx, yy, cw, 60)) + 14;
        // 例句区
        T(g, "例句", F(10.5f, true), Main, cx, yy);
        TR(g, "有道词典例句 · 已缓存", F(8.5f), Sub, new Rectangle(cx, (int)yy - 2, cw, 20));
        yy += 30;
        string[][] ex = {
            new[] { "You'd better fold it carefully.", "你最好仔细地叠好它。", "收起" },
            new[] { "It has wings that can fold.", "", "显示翻译" },
            new[] { "I fold up the chairs.", "", "显示翻译" } };
        foreach (var e in ex)
        {
            int h = e[1].Length > 0 ? 58 : 38;
            RR(g, new Rectangle(cx, (int)yy, cw, h), 10, Inset);
            T(g, e[0], F(10), Main, cx + 14, yy + 9);
            if (e[1].Length > 0) T(g, e[1], F(9.5f), Sub, cx + 14, yy + 33);
            TR(g, e[2], F(8.5f), Accent, new Rectangle(cx, (int)yy + 4, cw - 14, 18));
            yy += h + 10;
        }
        // 卡底操作行
        T(g, "删除", F(9), Color.FromArgb(200, 90, 80), cx, card.Bottom - 34);
        TR(g, "掌握度  ▓▓▓░░  3/5", F(9), Sub, new Rectangle(cx, card.Bottom - 36, cw, 20));

        // 折叠行（keep）：一行高 54、发丝线分隔，装在同一张组卡里示意
        var row = new Rectangle(202, 606, 858, 56);
        RR(g, row, 12, Card, Border);
        T(g, "02", F(10), Sub, 224, 624);
        T(g, "keep", F(12, true), Main, 258, 618);
        T(g, "v.（使）保持，（使）留在；继续，重复；供养，抚养；耽搁，延误；保有，留下；存放，…", F(9.5f), Sub, 330, 622);
        TR(g, "›", F(13), Sub, new Rectangle(1000, 612, 40, 40));

        // 底部行
        Pill(g, new Rectangle(202, 690, 96, 34), "导出 TXT", F(9.5f), Color.White, Color.FromArgb(90, 95, 105), true);
        Pill(g, new Rectangle(310, 690, 96, 34), "导出 CSV", F(9.5f), Color.White, Color.FromArgb(90, 95, 105), true);
        T(g, "共 2 条", F(9.5f), Sub, 424, 698);
        Save(bmp, "b1-wordbook.png");
    }

    // ---------- 板 2：首页 ----------
    private static void BoardHome()
    {
        var bmp = NewBoard();
        var g = Graphics.FromImage(bmp);
        Nav(g, "首页");
        TopBar(g, "首页");
        int x = 202, w = 858;
        // 语言行：源/目标大胶囊 + 交换
        Pill(g, new Rectangle(x, 76, 180, 40), "检测到英语 ▾", F(10), Color.White, Main, true);
        TC(g, "⇄", F(12), Sub, new Rectangle(x + 188, 76, 40, 40));
        Pill(g, new Rectangle(x + 236, 76, 180, 40), "中文 ▾", F(10), Color.White, Main, true);
        // 引擎段（加宽、不挤）
        string[] engs = { "自动", "有道", "必应", "AI", "谷歌", "MyMemory" };
        int ex = x + 448;
        foreach (var e in engs)
        {
            bool on = e == "自动";
            int ew = e.Length > 2 ? 76 : 56;
            Pill(g, new Rectangle(ex, 78, ew, 36), e, F(9.5f, on), on ? SelBg : PillBg, on ? Blue : Color.FromArgb(90, 95, 105));
            ex += ew + 8;
        }
        T(g, "引擎", F(9), Sub, x + 448, 58);
        // 输入卡
        var inp = new Rectangle(x, 140, w, 210);
        Shadow(g, inp, 14);
        RR(g, inp, 14, Card, Border);
        T(g, "输入或粘贴要翻译的文字…", F(11), Sub, x + 22, 162);
        TR(g, "0 / 5000", F(8.5f), Sub, new Rectangle(x, 312, w - 22, 18));
        // 操作行
        Pill(g, new Rectangle(x, 368, 108, 38), "清空", F(10), Color.White, Color.FromArgb(90, 95, 105), true);
        Pill(g, new Rectangle(x + 120, 368, 108, 38), "粘贴", F(10), Color.White, Color.FromArgb(90, 95, 105), true);
        var go = new Rectangle(x + w - 150, 366, 150, 42);
        RR(g, go, 21, Accent);
        TC(g, "翻 译", F(11, true), Color.White, go);
        // 结果卡
        var outp = new Rectangle(x, 436, w, 230);
        Shadow(g, outp, 14);
        RR(g, outp, 14, Card, Border);
        T(g, "译文", F(9.5f, true), Sub, x + 22, 452);
        T(g, "The quick brown fox jumps over the lazy dog.", F(9.5f), Sub, x + 22, 480);
        Wrap(g, "敏捷的棕色狐狸跳过那只懒狗。", F(12), Main, new RectangleF(x + 22, 508, w - 44, 40));
        // 结果操作行（图标钮拉开间距）
        int bx = x + 22;
        string[] acts = { "复制", "朗读", "收藏", "钉住" };
        foreach (var a in acts)
        {
            Pill(g, new Rectangle(bx, 606, 84, 34), a, F(9.5f), PillBg, Color.FromArgb(90, 95, 105));
            bx += 96;
        }
        TR(g, "有道 · 0.4 秒", F(8.5f), Sub, new Rectangle(x, 612, w - 22, 18));
        T(g, "最近翻译", F(11, true), Main, x, 700);
        T(g, "fold — v. 折叠，对折；倒闭，垮掉…", F(9.5f), Color.FromArgb(90, 95, 105), x, 730);
        T(g, "keep — v.（使）保持，（使）留在；继续…", F(9.5f), Color.FromArgb(90, 95, 105), x, 758);
        Save(bmp, "b2-home.png");
    }

    // ---------- 板 3：我的翻译 ----------
    private static void BoardHistory()
    {
        var bmp = NewBoard();
        var g = Graphics.FromImage(bmp);
        Nav(g, "我的翻译");
        TopBar(g, "我的翻译");
        int x = 202, w = 858;
        var search = new Rectangle(x, 74, 560, 38);
        RR(g, search, 10, Color.White, Border);
        T(g, "⌕  搜索历史记录…", F(9.5f), Sub, x + 16, 82);
        Pill(g, new Rectangle(x + w - 120, 74, 120, 38), "清空历史", F(9.5f), Color.White, Color.FromArgb(200, 90, 80), true);
        // 一张大卡装列表行：行高 72、发丝线分隔
        var list = new Rectangle(x, 136, w, 480);
        Shadow(g, list, 14);
        RR(g, list, 14, Card, Border);
        string[][] rows = {
            new[] { "DEFEATED BRAZILIAN", "被击败的巴西人", "英语 → 中文 · 截图翻译", "17:02" },
            new[] { "The quick brown fox jumps over the lazy dog.", "敏捷的棕色狐狸跳过那只懒狗。", "英语 → 中文 · 有道", "16:48" },
            new[] { "fold", "v. 折叠，对折；倒闭，垮掉；交叉，交叠…", "英语 → 中文 · 查词", "16:31" },
            new[] { "It has wings that can fold.", "它有可以折叠的翅膀。", "英语 → 中文 · 必应", "昨天" },
            new[] { "学而不思则罔，思而不学则殆。", "Learning without thought is labor lost.", "中文 → 英语 · 有道", "昨天" },
            new[] { "keep", "v.（使）保持，（使）留在；继续，重复…", "英语 → 中文 · 查词", "10-29" } };
        int ry = list.Top;
        for (int i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            T(g, r[0], F(10.5f, true), Main, x + 22, ry + 14);
            T(g, r[1], F(10), Color.FromArgb(90, 95, 105), x + 22, ry + 40);
            TR(g, r[3], F(8.5f), Sub, new Rectangle(x, ry + 12, w - 22, 16));
            TR(g, r[2], F(8.5f), Sub, new Rectangle(x, ry + 38, w - 22, 16));
            if (i < rows.Length - 1)
                using (var p = new Pen(Border)) g.DrawLine(p, x + 22, ry + 79, x + w - 22, ry + 79);
            ry += 80;
        }
        T(g, "共 42 条 · 按时间倒序", F(9.5f), Sub, x, 640);
        Save(bmp, "b3-history.png");
    }

    // ---------- 板 4：截图组合窗 ----------
    private static void BoardCombo()
    {
        var bmp = NewBoard();
        var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(210, 216, 224));
        // 窗体
        var win = new Rectangle(70, 60, 1040, 660);
        Shadow(g, win, 16);
        RR(g, win, 16, Bg, Border);
        int x = win.Left + 26, w = win.Width - 52;
        // 图条：双图 + 标题（间距 16）
        T(g, "原截图", F(9.5f), Sub, x, win.Top + 20);
        T(g, "译图", F(9.5f), Sub, x + 492, win.Top + 20);
        using (var dg = Graphics.FromImage(bmp)) { }
        var im1 = new Rectangle(x, win.Top + 46, 476, 300);
        var im2 = new Rectangle(x + 492, win.Top + 46, 476, 300);
        using (var br = new LinearGradientBrush(im1, Color.FromArgb(35, 45, 75), Color.FromArgb(190, 200, 215), 90f))
            g.FillRectangle(br, im1);
        using (var br = new LinearGradientBrush(im2, Color.FromArgb(35, 45, 75), Color.FromArgb(190, 200, 215), 90f))
            g.FillRectangle(br, im2);
        T(g, "DEFEATED BRAZILIAN", F(13, true), Color.White, im1.Left + 24, im1.Top + 24);
        T(g, "被击败的巴西人", F(13, true), Color.White, im2.Left + 24, im2.Top + 24);
        using (var p = new Pen(Border)) { g.DrawRectangle(p, im1); g.DrawRectangle(p, im2); }
        Pill(g, new Rectangle(im2.Left, im2.Bottom + 12, 118, 34), "保存译图", F(9.5f), Color.FromArgb(31, 41, 55), Color.White);
        Pill(g, new Rectangle(im2.Left + 130, im2.Bottom + 12, 118, 34), "复制译图", F(9.5f), Color.FromArgb(31, 41, 55), Color.White);
        TR(g, "原图 476×300 · 存图为原尺寸", F(8.5f), Sub, new Rectangle(x, im2.Bottom + 18, w, 18));
        // 原文卡
        var card = new Rectangle(x, im1.Bottom + 62, w, 108);
        RR(g, card, 14, Card, Border);
        T(g, "识别原文", F(9, true), Sub, x + 18, card.Top + 12);
        Wrap(g, "DEFEATED BRAZILIAN  （来自原位覆盖译图，点任意词可查词）", F(10.5f), Main, new RectangleF(x + 18, card.Top + 38, w - 36, 30));
        // 底部深色栏（钮间距拉开）
        var bar = new Rectangle(win.Left + 12, win.Bottom - 62, win.Width - 24, 50);
        RR(g, bar, 12, BarBg);
        Pill(g, new Rectangle(bar.Left + 14, bar.Top + 8, 150, 34), "检测到英语 ▾", F(9.5f), BarBtn, Color.White);
        TC(g, "⇄", F(11), Color.White, new Rectangle(bar.Left + 170, bar.Top + 8, 36, 34));
        Pill(g, new Rectangle(bar.Left + 212, bar.Top + 8, 110, 34), "自动 ▾", F(9.5f), BarBtn, Color.White);
        T(g, "原文", F(9.5f), Color.White, bar.Left + 344, bar.Top + 16);
        RR(g, new Rectangle(bar.Left + 382, bar.Top + 11, 40, 22), 11, Blue);
        using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, bar.Left + 400, bar.Top + 14, 16, 16);
        int bx = bar.Left + 448;
        string[] icons = { "复制", "收藏", "图片", "钉图", "重翻", "关" };
        foreach (var ic in icons)
        {
            RR(g, new Rectangle(bx, bar.Top + 8, 40, 34), 8, BarBtn);
            TC(g, ic.Length == 1 ? ic : ic.Substring(0, 1), F(10), Color.White, new Rectangle(bx, bar.Top + 8, 40, 34));
            bx += 52;
        }
        TR(g, "原位覆盖译图", F(8.5f), Color.FromArgb(168, 173, 182), new Rectangle(bar.Left + 766, bar.Top + 16, 112, 18));
        var ext = new Rectangle(bar.Right - 128, bar.Top + 7, 114, 36);
        RR(g, ext, 18, Accent);
        TC(g, "提取文本", F(10, true), Color.White, ext);
        // 关闭徽章
        using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, win.Right - 44, win.Top + 12, 26, 26);
        TC(g, "✕", F(10, true), Main, new Rectangle(win.Right - 44, win.Top + 12, 26, 26));
        Save(bmp, "b4-combo.png");
    }

    // ---------- 板 5：小窗三件（查词弹窗 / 文字弹窗 / 迷你窗） ----------
    private static void BoardSmallWindows()
    {
        var bmp = NewBoard();
        var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(210, 216, 224));
        // 查词弹窗
        var d = new Rectangle(60, 90, 470, 470);
        Shadow(g, d, 14);
        RR(g, d, 14, Card, Border);
        T(g, "fold", F(16, true), Main, d.Left + 24, d.Top + 20);
        T(g, "/fəʊld/  英  美", F(9.5f), Sub, d.Left + 118, d.Top + 30);
        Pill(g, new Rectangle(d.Right - 110, d.Top + 20, 86, 30), "🔊 朗读", F(9), PillBg, Color.FromArgb(90, 95, 105));
        float yy = d.Top + 66;
        yy += Wrap(g, "v. 折叠，对折；倒闭，垮掉；交叉，交叠；包，裹；拥抱；拌入；认输", F(10), Color.FromArgb(60, 65, 75), new RectangleF(d.Left + 24, yy, d.Width - 48, 44)) + 12;
        T(g, "例句", F(10, true), Main, d.Left + 24, yy); yy += 26;
        RR(g, new Rectangle(d.Left + 24, (int)yy, d.Width - 48, 58), 10, Inset);
        T(g, "You'd better fold it carefully.", F(9.5f), Main, d.Left + 38, yy + 8);
        T(g, "你最好仔细地叠好它。", F(9), Sub, d.Left + 38, yy + 32);
        yy += 74;
        T(g, "It has wings that can fold.", F(9.5f), Color.FromArgb(90, 95, 105), d.Left + 24, yy); yy += 30;
        T(g, "I fold up the chairs.", F(9.5f), Color.FromArgb(90, 95, 105), d.Left + 24, yy);
        Pill(g, new Rectangle(d.Left + 24, d.Bottom - 52, 96, 34), "☆ 收藏", F(9.5f), PillBg, Color.FromArgb(90, 95, 105));
        Pill(g, new Rectangle(d.Left + 132, d.Bottom - 52, 96, 34), "复制", F(9.5f), PillBg, Color.FromArgb(90, 95, 105));
        TR(g, "有道词典", F(8.5f), Sub, new Rectangle(d.Left, d.Bottom - 44, d.Width - 24, 16));
        T(g, "查词弹窗", F(11, true), Main, 60, 584);

        // 文字弹窗（紧凑窗保持小，但内边距加大）
        var t = new Rectangle(600, 90, 500, 300);
        Shadow(g, t, 14);
        RR(g, t, 14, Card, Border);
        T(g, "DEFEATED BRAZILIAN", F(10.5f), Main, t.Left + 20, t.Top + 18);
        using (var p = new Pen(Border)) g.DrawLine(p, t.Left + 20, t.Top + 52, t.Right - 20, t.Top + 52);
        T(g, "被击败的巴西人", F(12, true), Main, t.Left + 20, t.Top + 66);
        int bx = t.Left + 20;
        string[] acts = { "复制", "朗读", "收藏", "钉住", "关闭" };
        foreach (var a in acts)
        {
            Pill(g, new Rectangle(bx, t.Bottom - 54, 76, 32), a, F(9), PillBg, Color.FromArgb(90, 95, 105));
            bx += 86;
        }
        TR(g, "英语 → 中文 · 有道", F(8.5f), Sub, new Rectangle(t.Left, t.Top + 104, t.Width - 20, 16));
        T(g, "文字弹窗", F(11, true), Main, 600, 414);

        // 迷你窗
        var m = new Rectangle(600, 470, 500, 190);
        Shadow(g, m, 14);
        RR(g, m, 14, Card, Border);
        var mi = new Rectangle(m.Left + 18, m.Top + 18, m.Width - 36, 62);
        RR(g, mi, 10, Color.White, Border);
        T(g, "输入要翻译的短句…", F(10), Sub, mi.Left + 14, mi.Top + 10);
        Pill(g, new Rectangle(m.Right - 118, m.Bottom - 52, 100, 36), "翻译", F(10, true), Accent, Color.White);
        Pill(g, new Rectangle(m.Left + 18, m.Bottom - 52, 84, 36), "清空", F(9.5f), PillBg, Color.FromArgb(90, 95, 105));
        T(g, "迷你窗", F(11, true), Main, 600, 684);
        Save(bmp, "b5-small.png");
    }

    // ---------- 板 6：设置 ----------
    private static void BoardSettings()
    {
        var bmp = NewBoard();
        var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(210, 216, 224));
        var win = new Rectangle(90, 70, 1000, 640);
        Shadow(g, win, 16);
        RR(g, win, 16, Bg, Border);
        // 左侧分节
        using (var b = new SolidBrush(NavBg)) g.FillRectangle(b, win.Left, win.Top, 190, win.Height);
        T(g, "设置", F(14, true), Main, win.Left + 24, win.Top + 22);
        string[] secs = { "常规", "翻译", "关于" };
        int sy = win.Top + 76;
        foreach (var s in secs)
        {
            bool on = s == "翻译";
            if (on) RR(g, new Rectangle(win.Left + 12, sy - 4, 166, 38), 10, SelBg);
            T(g, s, F(10.5f, on), on ? Main : Color.FromArgb(90, 95, 105), win.Left + 28, sy + 2);
            sy += 52;
        }
        int x = win.Left + 226, w = win.Width - 262;
        // 设置行：行高 64，左标签右控件
        string[] labels = { "默认翻译引擎", "", "默认目标语言", "截图翻译快捷键", "皮肤" };
        int ry = win.Top + 34;
        T(g, "默认翻译引擎", F(10.5f, true), Main, x, ry + 8);
        string[] e1 = { "自动", "有道", "必应", "AI" };
        int ex = x;
        foreach (var e in e1)
        {
            bool on = e == "有道";
            Pill(g, new Rectangle(ex, ry + 34, 96, 36), e, F(9.5f, on), on ? SelBg : Color.White, on ? Blue : Color.FromArgb(90, 95, 105), !on);
            ex += 108;
        }
        string[] e2 = { "谷歌", "MyMemory" };
        ex = x;
        foreach (var e in e2)
        {
            Pill(g, new Rectangle(ex, ry + 80, 96, 36), e, F(9.5f), Color.White, Color.FromArgb(90, 95, 105), true);
            ex += 108;
        }
        T(g, "选定引擎失败时自动回退有道/必应", F(8.5f), Sub, x + 240, ry + 88);
        ry += 150;
        using (var p = new Pen(Border)) g.DrawLine(p, x, ry, x + w, ry);
        T(g, "默认目标语言", F(10.5f, true), Main, x, ry + 18);
        Pill(g, new Rectangle(x + w - 190, ry + 12, 190, 38), "自动（中英互译） ▾", F(9.5f), Color.White, Main, true);
        ry += 74;
        using (var p = new Pen(Border)) g.DrawLine(p, x, ry, x + w, ry);
        T(g, "截图翻译快捷键", F(10.5f, true), Main, x, ry + 18);
        Pill(g, new Rectangle(x + w - 190, ry + 12, 190, 38), "Ctrl + Alt + Z", F(9.5f), Color.White, Main, true);
        ry += 74;
        using (var p = new Pen(Border)) g.DrawLine(p, x, ry, x + w, ry);
        T(g, "皮肤", F(10.5f, true), Main, x, ry + 18);
        string[] skins = { "经典浅色", "磨砂毛玻璃", "磨砂·自带背景", "深色玻璃" };
        int sx = x;
        foreach (var s in skins)
        {
            bool on = s == "深色玻璃";
            Pill(g, new Rectangle(sx, ry + 48, 118, 36), s, F(9, on), on ? SelBg : Color.White, on ? Blue : Color.FromArgb(90, 95, 105), !on);
            sx += 130;
        }
        var save = new Rectangle(win.Right - 150, win.Bottom - 58, 120, 40);
        RR(g, save, 20, Accent);
        TC(g, "保 存", F(10.5f, true), Color.White, save);
        Save(bmp, "b6-settings.png");
    }
}

internal static class DesignV153Launcher
{
    private static void Main() { DesignV153.Entry(); }
}
