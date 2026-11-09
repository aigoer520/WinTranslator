using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace WinTranslator
{
    /// <summary>新版 UI 配色（仿有道新版：浅底、白卡、圆角、红强调；v1.1.1 美化口径：柔和冷灰白底、柔黑字、统一红/蓝）。
    /// v1.3.0：表面色字段改可变，由 Palette.Apply 按皮肤（classic/backdrop/acrylic）整表切换；文字色三档皮肤共用。</summary>
    public static class Palette
    {
        public static Color Bg = Color.FromArgb(247, 248, 250);
        public static Color NavBg = Color.FromArgb(245, 243, 239);
        public static Color Card = Color.White;
        public static Color Border = Color.FromArgb(231, 233, 237);
        public static Color Main = Color.FromArgb(35, 39, 46);
        public static Color Sub = Color.FromArgb(144, 149, 158);
        public static Color Accent = Color.FromArgb(233, 61, 46);
        /// <summary>v1.6.0 卡片描边（CardAt 专用）：多数皮肤与 Border 同值；lucide 用深色描边勾出面板轮廓。</summary>
        public static Color CardEdge = Color.FromArgb(231, 233, 237);
        public static Color SelBg = Color.FromArgb(234, 241, 254);
        public static Color PillBg = Color.FromArgb(238, 240, 244);
        public static Color DashBorder = Color.FromArgb(176, 196, 232);
        public static Color DashFill = Color.FromArgb(249, 251, 255);
        // 截图弹窗（v1.1.1 事项三）
        public static Color Frame = Color.FromArgb(233, 235, 239);
        public static Color BarBg = Color.FromArgb(50, 54, 62);
        public static Color BarBtn = Color.FromArgb(66, 71, 80);
        public static Color BarSub = Color.FromArgb(168, 173, 182);
        public static readonly Color SwitchBlue = Color.FromArgb(59, 130, 246);

        /// <summary>按皮肤切换整套表面色。acrylic（真毛玻璃）时页面底色＝透明键色，窗体该区域透出系统亚克力。</summary>
        public static void Apply(string skin)
        {
            // v1.6.0：强调色随皮肤切换（lucide 黑，其余品牌红）；其余槽位在各分支内赋值
            Accent = skin == "lucide" ? Color.FromArgb(17, 17, 17) : Color.FromArgb(233, 61, 46);
            if (skin == "backdrop")
            {
                Bg = Color.FromArgb(243, 235, 224);
                NavBg = Color.FromArgb(241, 232, 218);
                Card = Color.FromArgb(254, 251, 246);
                Border = Color.FromArgb(233, 223, 205);
                SelBg = Color.FromArgb(253, 236, 224);
                PillBg = Color.FromArgb(244, 236, 224);
                DashBorder = Color.FromArgb(214, 190, 160);
                DashFill = Color.FromArgb(253, 248, 240);
                Frame = Color.FromArgb(241, 234, 222);
                CardEdge = Color.FromArgb(233, 223, 205);
                BarBg = Color.FromArgb(54, 49, 45);
                BarBtn = Color.FromArgb(84, 77, 70);
                BarSub = Color.FromArgb(196, 186, 172);
            }
            else if (skin == "acrylic")
            {
                Bg = SkinMode.Key;
                NavBg = SkinMode.Key;
                Card = Color.FromArgb(253, 250, 246);
                Border = Color.FromArgb(233, 226, 214);
                SelBg = Color.FromArgb(253, 236, 224);
                PillBg = Color.FromArgb(246, 240, 230);
                DashBorder = Color.FromArgb(214, 190, 160);
                DashFill = Color.FromArgb(253, 248, 240);
                Frame = SkinMode.Key;
                CardEdge = Color.FromArgb(233, 226, 214);
                BarBg = Color.FromArgb(50, 46, 43);
                BarBtn = Color.FromArgb(80, 74, 68);
                BarSub = Color.FromArgb(190, 182, 170);
            }
            else if (skin == "frosted")
            {
                // v1.5.0 深色玻璃（用户定版参考：深蓝黑流光底＋深色半透玻璃卡＋白字＋蓝辉光描边）
                Bg = Color.FromArgb(8, 12, 21);
                NavBg = Color.FromArgb(11, 17, 28);
                Card = Color.FromArgb(22, 32, 46);
                Border = Color.FromArgb(56, 74, 96);
                SelBg = Color.FromArgb(23, 42, 64);
                PillBg = Color.FromArgb(29, 41, 57);
                DashBorder = Color.FromArgb(56, 130, 200);
                DashFill = Color.FromArgb(16, 25, 37);
                Frame = Color.FromArgb(7, 11, 19);
                CardEdge = Color.FromArgb(56, 74, 96);
                BarBg = Color.FromArgb(12, 19, 30);
                BarBtn = Color.FromArgb(30, 44, 61);
                BarSub = Color.FromArgb(148, 163, 184);
            }
            else if (skin == "lucide")
            {
                // v1.6.0 Lucide 线条（静态稿定版）：纯白底、锌灰层次、黑主按钮、卡片深描边
                Bg = Color.FromArgb(250, 250, 250);
                NavBg = Color.White;
                Card = Color.White;
                Border = Color.FromArgb(229, 229, 229);
                SelBg = Color.FromArgb(244, 244, 245);
                PillBg = Color.FromArgb(247, 247, 247);
                DashBorder = Color.FromArgb(163, 163, 163);
                DashFill = Color.FromArgb(250, 250, 250);
                Frame = Color.FromArgb(238, 238, 238);
                CardEdge = Color.FromArgb(22, 22, 22);
                BarBg = Color.FromArgb(24, 24, 27);
                BarBtn = Color.FromArgb(39, 39, 42);
                BarSub = Color.FromArgb(161, 161, 170);
            }
            else if (skin == "liquid")
            {
                // v1.7.0 液态玻璃（静态稿定版）：彩色光晕底图（LiquidArt）＋静态混色玻璃。
                // WinForms 的 BackColor 不接受 alpha（TextBox 赋值即抛异常，v1.7.0 探针实测），
                // 所以半透感用「与光晕底平均色预先混好的近白色」静态实现：卡面比底图略亮＝玻璃浮起感。
                // 导航/页面铺 LiquidArt 底图，卡片浮在图上；组合窗同理（窗体铺底图＋浅色卡）。
                Bg = Color.FromArgb(228, 224, 244);
                NavBg = Color.FromArgb(233, 229, 247);
                Card = Color.FromArgb(243, 242, 251);
                Border = Color.FromArgb(214, 211, 236);
                SelBg = Color.FromArgb(233, 231, 248);
                PillBg = Color.FromArgb(226, 223, 243);
                DashBorder = Color.FromArgb(255, 150, 138);
                DashFill = Color.FromArgb(246, 240, 248);
                Frame = Color.FromArgb(220, 216, 240);
                CardEdge = Color.FromArgb(206, 202, 232);
                BarBg = Color.FromArgb(26, 28, 38);
                BarBtn = Color.FromArgb(52, 56, 70);
                BarSub = Color.FromArgb(178, 182, 196);
            }
            else
            {
                Bg = Color.FromArgb(247, 248, 250);
                NavBg = Color.FromArgb(245, 243, 239);
                Card = Color.White;
                Border = Color.FromArgb(231, 233, 237);
                SelBg = Color.FromArgb(234, 241, 254);
                PillBg = Color.FromArgb(238, 240, 244);
                DashBorder = Color.FromArgb(176, 196, 232);
                DashFill = Color.FromArgb(249, 251, 255);
                Frame = Color.FromArgb(233, 235, 239);
                CardEdge = Color.FromArgb(231, 233, 237);
                BarBg = Color.FromArgb(50, 54, 62);
                BarBtn = Color.FromArgb(66, 71, 80);
                BarSub = Color.FromArgb(168, 173, 182);
            }
            if (skin == "frosted")
            {
                Main = Color.FromArgb(238, 243, 249);
                Sub = Color.FromArgb(148, 163, 184);
            }
            else if (skin == "lucide")
            {
                Main = Color.FromArgb(23, 23, 23);
                Sub = Color.FromArgb(138, 138, 138);
            }
            else
            {
                Main = Color.FromArgb(35, 39, 46);
                Sub = Color.FromArgb(144, 149, 158);
            }
        }
    }

    /// <summary>圆角面板：Region 裁剪保证子控件不溢出圆角。</summary>
    public class RoundPanel : Panel
    {
        public int Radius = 12;
        public Color Fill = Color.White;
        public Color Edge = Color.FromArgb(231, 233, 237);
        public bool Dashed;
        public bool NoEdge;

        public RoundPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.Transparent;
        }

        internal static GraphicsPath PathFor(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Max(2, radius * 2);
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width > 0 && Height > 0)
                Region = new Region(PathFor(new Rectangle(0, 0, Width, Height), Radius));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = PathFor(rect, Radius))
            {
                if (SkinMode.IsLiquid)
                {
                    // v1.7.0 液态玻璃竖向渐变：顶部近白→底部微透出粉蓝（静态近似玻璃的通透层次）。
                    // 只作用于卡片类大面积填充（Fill＝Palette.Card 时）；彩色按钮（红/深底）不处理。
                    int fr = Fill.R, fg = Fill.G, fb = Fill.B;
                    bool cardLike = Height >= 40 && Width >= 80 && fr > 225 && fg > 220 && fb > 225;
                    if (cardLike)
                    {
                        var top = Color.FromArgb(Math.Min(255, fr + 9), Math.Min(255, fg + 9), Math.Min(255, fb + 4));
                        var bot = Color.FromArgb(Math.Max(0, fr - 14), Math.Max(0, fg - 16), fb);
                        using (var lg0 = new System.Drawing.Drawing2D.LinearGradientBrush(
                            new Rectangle(0, 0, Math.Max(2, Width), Math.Max(2, Height)), top, bot, 90f))
                            e.Graphics.FillPath(lg0, path);
                    }
                    else
                    {
                        using (var b0 = new SolidBrush(Fill)) e.Graphics.FillPath(b0, path);
                    }
                }
                else
                {
                    using (var b = new SolidBrush(Fill)) e.Graphics.FillPath(b, path);
                }
                if (SkinMode.IsLiquid && Radius >= 8 && Height >= 26)
                {
                    // v1.7.0 液态玻璃镜面高光：顶边内侧一道白色渐变亮线（上浓下淡），模拟玻璃上沿的镜面反射。
                    // 只对有一定高度的卡片类面板生效，小圆钮（译/隐切换等）自动跳过。
                    using (var clip = new Region(path))
                    {
                        var oldClip = e.Graphics.Clip;
                        e.Graphics.Clip = clip;
                        using (var lg = new System.Drawing.Drawing2D.LinearGradientBrush(
                            new Rectangle(0, 0, Math.Max(2, Width), 3), Color.FromArgb(235, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                            e.Graphics.FillRectangle(lg, 1, 1, Math.Max(0, Width - 2), 3);
                        e.Graphics.Clip = oldClip;
                    }
                }
                if (!NoEdge)
                {
                    using (var pen = new Pen(Edge, 1))
                    {
                        if (Dashed) pen.DashStyle = DashStyle.Dash;
                        e.Graphics.DrawPath(pen, path);
                    }
                }
            }
            base.OnPaint(e);
        }

        /// <summary>让点击穿透到本面板（子标签点击也算面板点击）。</summary>
        public void WireClickThrough(EventHandler handler)
        {
            Click += handler;
            foreach (Control c in Controls) c.Click += handler;
        }

        /// <summary>v1.6.4：异色子丸显形兜底——子 RoundPanel 若已建句柄、其表面被父级圆角 Region 裁掉
        /// （Mono 实测：异色子控件在父 Region 边缘带会被裁），给子控件建自己的 Region 并强制重绘。
        /// 用于 SegmentControl 选中丸这类"子控件必须盖住父底色"的场景；父/子底色接近时无需调用。</summary>
        public void EnsureChildShape(Control child)
        {
            if (child == null) return;
            var rp = child as RoundPanel;
            if (rp != null && rp.Width > 0 && rp.Height > 0)
                rp.Region = new Region(RoundPanel.PathFor(new Rectangle(0, 0, rp.Width, rp.Height), rp.Radius));
            child.Visible = true;
            child.Invalidate();
            child.Update();
        }
    }

    /// <summary>开关控件（单控件自绘：药丸＋圆钮）。</summary>
    public class ToggleSwitch : Control
    {
        private bool _on;
        public event EventHandler Changed;

        public bool On
        {
            get { return _on; }
            set { if (_on != value) { _on = value; Invalidate(); } }
        }

        public ToggleSwitch(bool on)
        {
            _on = on;
            Size = new Size(34, 20);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            _on = !_on;
            Invalidate();
            var h = Changed;
            if (h != null) h(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = RoundPanel.PathFor(rect, 10))
            using (var b = new SolidBrush(_on ? Palette.SwitchBlue : Color.FromArgb(200, 203, 208)))
                e.Graphics.FillPath(b, path);
            int kx = _on ? Width - 18 : 2;
            using (var b = new SolidBrush(Color.White))
                e.Graphics.FillEllipse(b, kx, 2, 16, 16);
        }
    }

    /// <summary>胶囊分段控件（如：自动/有道/必应）。
    /// v1.6.5 全自绘：选中丸与全部选项文字都由本控件 OnPaint 一次画出（不再用子 Label）。
    /// 缘起：Label 版 MyMemory 在用户真机上始终比其他项偏上（FontTheme 统一、关兼容渲染都没根治，
    /// 又复现不出，属 Label 排版黑盒）；自绘后每项走完全相同的 TextRenderer 调用与矩形，基线由构造一致。
    /// 文字超宽时自动缩字号塞进本段（防截断）。点击按 X 坐标算段号。</summary>
    public class SegmentControl : RoundPanel
    {
        private string[] _options;
        private int _selected;
        public event EventHandler Changed;

        /// <summary>v1.8.0：运行时替换选项（如引擎目录随密钥配置增减）；选中下标越界时回落 0。</summary>
        public void SetOptions(string[] options, int selected)
        {
            _options = options ?? new string[0];
            _selected = selected >= 0 && selected < _options.Length ? selected : 0;
            Invalidate();
        }

        private bool _accentSelected;
        /// <summary>v1.6.4：选中项改红底白字（用户要求引擎胶囊选中项加红色底色；默认关、仅引擎条开启）。</summary>
        public bool AccentSelected
        {
            get { return _accentSelected; }
            set { if (_accentSelected != value) { _accentSelected = value; Invalidate(); } }
        }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                if (value < 0 || value >= _options.Length) return;
                if (_selected != value) { _selected = value; Invalidate(); }
            }
        }

        public SegmentControl(string[] options, int selected)
        {
            _options = options;
            _selected = selected;
            Radius = 17;
            Fill = Palette.PillBg;
            NoEdge = true;
            Cursor = Cursors.Hand;
        }

        private int SegW { get { return _options.Length > 0 ? (Width - 6) / _options.Length : 0; } }

        /// <summary>
        /// v1.7.4 等距布局（用户定的静态稿）：每项框宽＝字宽＋左右各 11px 内边距，
        /// 项与项之间的空隙全部相等、整排铺满控件内宽；选中丸只包选中项的框。
        /// 全部项天然宽度放不下时回退旧的等宽格布局（保底不溢出）。
        /// </summary>
        internal Rectangle[] ComputeBoxes()
        {
            int n = _options.Length;
            var boxes = new Rectangle[n];
            if (n == 0 || Width <= 0) return boxes;
            int inner = Width - 6;
            const int Pad = 11;
            var textW = new int[n];
            int total = 0;
            for (int i = 0; i < n; i++)
            {
                using (var f = FontTheme.Of(10f, i == _selected ? FontStyle.Bold : FontStyle.Regular))
                    textW[i] = TextRenderer.MeasureText(_options[i], f, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                total += textW[i] + Pad * 2;
            }
            if (total > inner || n == 1)
            {
                if (n == 1)
                {
                    int bw = Math.Min(inner, textW[0] + Pad * 2);
                    boxes[0] = new Rectangle(3 + (inner - bw) / 2, 0, bw, Height);
                    return boxes;
                }
                int segW = SegW;
                for (int i = 0; i < n; i++) boxes[i] = new Rectangle(3 + segW * i, 0, segW, Height);
                return boxes;
            }
            double gap = n > 1 ? (inner - total) / (double)(n - 1) : 0;
            double x = 3;
            for (int i = 0; i < n; i++)
            {
                int bw = textW[i] + Pad * 2;
                if (i == n - 1) bw = Width - 3 - (int)Math.Round(x);
                boxes[i] = new Rectangle((int)Math.Round(x), 0, bw, Height);
                x += bw + gap;
            }
            return boxes;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_options.Length == 0 || Width <= 0) return;
            var boxes = ComputeBoxes();
            if (boxes.Length == 0 || boxes[0].Width <= 0) return;
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            // 选中丸：只包选中项的框（v1.7.4 等距版；旧版占满整格）
            var sel = boxes[_selected];
            var pillRect = new Rectangle(sel.X, 3, sel.Width, Height - 6);
            using (var path = PathFor(new Rectangle(pillRect.X, pillRect.Y, pillRect.Width - 1, pillRect.Height - 1), 14))
            using (var b = new SolidBrush(AccentSelected ? Color.FromArgb(233, 61, 46) : Color.White))
                g.FillPath(b, path);
            // 选项文字：在各自框内居中；超宽缩字号
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            for (int i = 0; i < _options.Length; i++)
            {
                bool isSel = i == _selected;
                Color fore = (AccentSelected && isSel) ? Color.White : Palette.Main;
                float size = 10f;
                Font font = FontTheme.Of(size, isSel ? FontStyle.Bold : FontStyle.Regular);
                while (size > 7.5f && TextRenderer.MeasureText(_options[i], font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width > boxes[i].Width - 8)
                {
                    font.Dispose();
                    size -= 0.5f;
                    font = FontTheme.Of(size, isSel ? FontStyle.Bold : FontStyle.Regular);
                }
                TextRenderer.DrawText(g, _options[i], font, boxes[i], fore, flags);
                font.Dispose();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || _options.Length == 0) return;
            var boxes = ComputeBoxes();
            if (boxes.Length == 0 || boxes[0].Width <= 0) return;
            int idx = boxes.Length - 1;
            for (int i = 0; i < boxes.Length; i++)
            {
                if (e.X < boxes[i].Right) { idx = i; break; }
                // 空隙内点击：按与相邻项中心的中点归属
                if (i + 1 < boxes.Length)
                {
                    int mid = (boxes[i].X + boxes[i].Width / 2 + boxes[i + 1].X + boxes[i + 1].Width / 2) / 2;
                    if (e.X < mid) { idx = i; break; }
                }
            }
            if (e.X < boxes[0].X) idx = 0;
            if (idx != _selected)
            {
                _selected = idx;
                Invalidate();
                var h = Changed;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
        }
    }

    /// <summary>
    /// v1.5.3 字体设置（用户要求设置里加字体设置）：界面字体族＋界面字号档（小 0.9 / 标准 1.0 / 大 1.15）。
    /// Ui.Lbl 等工厂方法直接按主题出字；已建控件用 ApplyTo 整树换字（以首次见到的字体为基准、防反复缩放叠加）。
    /// 截图译图回填字号走原文行高算法，不走这里。
    /// </summary>
    public static class FontTheme
    {
        public static string Family = "Microsoft YaHei UI";
        public static double Scale = 1.0;
        private static readonly ConditionalWeakTable<Control, Font> BaseFonts = new ConditionalWeakTable<Control, Font>();

        public static void Configure(string family, int scaleIdx)
        {
            Family = string.IsNullOrEmpty(family) ? "Microsoft YaHei UI" : family;
            Scale = scaleIdx == 0 ? 0.9 : (scaleIdx == 2 ? 1.15 : 1.0);
        }

        public static Font Of(float baseSize, bool bold = false)
        {
            return new Font(Family, (float)(baseSize * Scale), bold ? FontStyle.Bold : FontStyle.Regular);
        }

        public static Font Of(float baseSize, FontStyle style)
        {
            return new Font(Family, (float)(baseSize * Scale), style);
        }

        public static void ApplyTo(Control root)
        {
            if (root == null) return;
            Walk(root);
        }

        private static void Walk(Control c)
        {
            try
            {
                if (c.Font != null)
                {
                    Font baseF;
                    if (!BaseFonts.TryGetValue(c, out baseF))
                    {
                        baseF = c.Font;
                        BaseFonts.Add(c, baseF);
                    }
                    float wantSize = (float)(baseF.Size * Scale);
                    if (c.Font.Name != Family || Math.Abs(c.Font.Size - wantSize) > 0.05f)
                        c.Font = new Font(Family, wantSize, baseF.Style);
                }
            }
            catch { }
            foreach (Control ch in c.Controls) Walk(ch);
        }
    }

    public static class Ui
    {
        public static Label Lbl(string text, int x, int y, float size, Color color, bool bold = false)
        {
            return new Label
            {
                Text = text, Location = new Point(x, y), AutoSize = true,
                Font = FontTheme.Of(size, bold),
                ForeColor = color, BackColor = Color.Transparent
            };
        }

        public static RoundPanel CardAt(int x, int y, int w, int h, int radius = 12)
        {
            return new RoundPanel { Location = new Point(x, y), Size = new Size(w, h), Radius = radius, Fill = Palette.Card, Edge = Palette.CardEdge, Tag = "cardedge" };
        }

        /// <summary>胶囊按钮（圆角面板＋居中文字）。</summary>
        public static RoundPanel PillButton(string text, int x, int y, int w, int h, Color fill, Color fore, bool bold = false)
        {
            var p = new RoundPanel { Location = new Point(x, y), Size = new Size(w, h), Radius = h / 2, Fill = fill, NoEdge = fill != Color.White, Edge = Palette.Border, Cursor = Cursors.Hand };
            p.Controls.Add(new Label
            {
                Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 10f, bold ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = fore, BackColor = Color.Transparent
            });
            return p;
        }

        /// <summary>触发控件的 Click（测试/代码内导航用）。</summary>
        public static void RaiseClick(Control c)
        {
            var m = typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (m != null) m.Invoke(c, new object[] { EventArgs.Empty });
        }
    }
}
