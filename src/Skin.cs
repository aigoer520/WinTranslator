using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinTranslator
{
    /// <summary>v1.3.0 皮肤状态：classic（经典浅色）/ acrylic（真毛玻璃，系统亚克力）/ backdrop（自带背景磨砂）。</summary>
    public static class SkinMode
    {
        public static string Current = "classic";
        public static bool IsAcrylic { get { return Current == "acrylic"; } }
        public static bool IsBackdrop { get { return Current == "backdrop"; } }
        /// <summary>v1.5.0 仿磨砂：应用内磨砂纹理＋奶白半透卡质感（参考音乐播放器卡片）。</summary>
        public static bool IsFrosted { get { return Current == "frosted"; } }
        /// <summary>v1.6.0 Lucide 线条皮肤：纯白底＋黑按钮＋深色描边（用户从 18 款设计稿中选定）。</summary>
        public static bool IsLucide { get { return Current == "lucide"; } }
        /// <summary>v1.7.0 液态玻璃皮肤（用户点名 GitHub 项目 naughtyduk/liquidGL 的视觉语言）：
        /// 彩色光晕底图＋半透明白玻璃卡＋顶边镜面高光。原库是网页 WebGPU/WebGL 技术，
        /// WinForms 落地为近似实现（自绘底图＋半透填充＋高光描边，无实时折射）。</summary>
        public static bool IsLiquid { get { return Current == "liquid"; } }
        /// <summary>亚克力窗体的透明键色：窗体上刷此色的区域在真机上透出亚克力模糊（取极冷僻色避免撞色）。</summary>
        public static readonly Color Key = Color.FromArgb(255, 1, 2, 3);

        public static string Normalize(string s)
        {
            return s == "acrylic" || s == "backdrop" || s == "frosted" || s == "lucide" || s == "liquid" ? s : "classic";
        }
    }

    /// <summary>Windows 亚克力（Acrylic）合成：SetWindowCompositionAttribute 的 ACCENT 方式，Win10 1803+ 可用。
    /// 非 Windows 或调用失败一律静默回退（窗体仍以键色/普通色显示，不崩）。</summary>
    public static class Glass
    {
        private enum AccentState
        {
            Disabled = 0,
            EnableGradient = 1,
            EnableTransparentGradient = 2,
            EnableBlurBehind = 3,
            EnableAcrylicBlurBehind = 4,
            Invalid = 5
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public AccentState AccentState;
            public uint AccentFlags;
            public uint GradientColor;
            public uint AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CompositionData
        {
            public uint Attribute;
            public IntPtr Data;
            public int DataSize;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref CompositionData data);

        private static bool IsWindows
        {
            get { return Environment.OSVersion.Platform == PlatformID.Win32NT && Environment.OSVersion.Version >= new Version(10, 0, 17134); }
        }

        private static void SetAccent(Form f, AccentState state, uint tintAbgr)
        {
            if (!IsWindows || f == null || f.IsDisposed) return;
            var accent = new AccentPolicy { AccentState = state, AccentFlags = 2, GradientColor = tintAbgr, AnimationId = 0 };
            int size = Marshal.SizeOf(accent);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new CompositionData { Attribute = 19, Data = ptr, DataSize = size }; // WCA_ACCENT_POLICY=19
                SetWindowCompositionAttribute(f.Handle, ref data);
            }
            catch { }
            finally { Marshal.FreeHGlobal(ptr); }
        }

        /// <summary>开启亚克力：染色为浅暖白（ABGR：A=D8 半透、B=E6、G=F0、R=F8 近白暖调）。</summary>
        public static void EnableAcrylic(Form f) { SetAccent(f, AccentState.EnableAcrylicBlurBehind, 0xD8E6F0F8); }

        public static void Disable(Form f) { SetAccent(f, AccentState.Disabled, 0); }
    }

    /// <summary>把亚克力外观套到窗体：键色背景＋透明键＋开启合成。仅 acrylic 皮肤时动作。</summary>
    public static class SkinFx
    {
        public static void Apply(Form f)
        {
            if (!SkinMode.IsAcrylic || f == null) return;
            try
            {
                f.BackColor = SkinMode.Key;
                f.TransparencyKey = SkinMode.Key;
                var h = f.Handle; // 强制建句柄后开启合成
                Glass.EnableAcrylic(f);
            }
            catch { }
        }

        public static void Remove(Form f)
        {
            if (f == null) return;
            try
            {
                Glass.Disable(f);
                f.TransparencyKey = Color.Empty;
            }
            catch { }
        }
    }

    /// <summary>自带背景皮肤的暖色柔焦底图（与静态稿同做法：渐变＋柔光，生成后两遍模糊）。</summary>
    public static class BackdropArt
    {
        private static Bitmap _cached;
        private static int _cw, _ch;

        public static Bitmap For(int w, int h)
        {
            if (w < 50) w = 50;
            if (h < 50) h = 50;
            if (_cached != null && Math.Abs(_cw - w) < 60 && Math.Abs(_ch - h) < 60) return (Bitmap)_cached.Clone();
            var raw = new Bitmap(w, h);
            using (var g = Graphics.FromImage(raw))
            {
                using (var lg = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(240, 222, 198), Color.FromArgb(206, 172, 140), 35f))
                    g.FillRectangle(lg, 0, 0, w, h);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Soft(g, w * 0.72f, h * 0.10f, 420, Color.FromArgb(95, 255, 240, 214));
                Soft(g, w * 0.12f, h * 0.85f, 380, Color.FromArgb(60, 255, 252, 244));
                Soft(g, w * 0.45f, h * 0.55f, 520, Color.FromArgb(70, 232, 188, 148));
            }
            var blurred = Blur(Blur(raw, 9), 7);
            raw.Dispose();
            if (_cached != null) _cached.Dispose();
            _cached = blurred;
            _cw = w; _ch = h;
            // 返回克隆而非缓存原图：原图被后续不同尺寸的调用 Dispose 时，
            // 仍挂着旧图的窗体一重绘就崩（v1.5.0 布局测试实测抓到的真 bug，主窗与组合窗尺寸不同必触发）
            return (Bitmap)_cached.Clone();
        }

        private static void Soft(Graphics g, float cx, float cy, int size, Color color)
        {
            using (var b = new SolidBrush(color))
                g.FillEllipse(b, cx - size / 2, cy - size / 2, size, size);
        }

        private static Bitmap Blur(Bitmap src, int factor)
        {
            int w = Math.Max(2, src.Width / factor), h = Math.Max(2, src.Height / factor);
            var small = new Bitmap(w, h);
            using (var g = Graphics.FromImage(small))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(src, 0, 0, w, h);
            }
            var big = new Bitmap(src.Width, src.Height);
            using (var g = Graphics.FromImage(big))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(small, 0, 0, big.Width, big.Height);
            }
            small.Dispose();
            return big;
        }
    }

    /// <summary>
    /// v1.5.0 深色玻璃纹理（用户定版参考图：Glassy 深蓝黑流光）：深蓝黑底＋几条宽幅蓝色流光带＋
    /// 细亮蓝光丝＋柔光晕，多遍缩放模糊出玻璃后的流动感；细噪点压到很弱（深色玻璃表面是光滑的）。
    /// 注意：与 BackdropArt 同尺寸共享缓存图的教训——两类底图缓存必须独立，本类自有 _cached（勿与 BackdropArt 混用）。
    /// </summary>
    public static class FrostArt
    {
        private static Bitmap _cached;
        private static int _cw, _ch;

        public static Bitmap For(int w, int h)
        {
            if (w < 50) w = 50;
            if (h < 50) h = 50;
            if (_cached != null && Math.Abs(_cw - w) < 60 && Math.Abs(_ch - h) < 60) return (Bitmap)_cached.Clone();
            var rnd = new Random(20261101);
            var raw = new Bitmap(w, h);
            using (var g = Graphics.FromImage(raw))
            {
                using (var lg = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(7, 11, 20), Color.FromArgb(10, 18, 32), 55f))
                    g.FillRectangle(lg, 0, 0, w, h);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                // 宽幅流光带（贝塞尔缎带，深蓝多层叠出流动感）
                Ribbon(g, w, h, 0.30f, 0.52f, Color.FromArgb(120, 16, 44, 84));
                Ribbon(g, w, h, 0.46f, 0.66f, Color.FromArgb(95, 20, 64, 120));
                Ribbon(g, w, h, 0.62f, 0.80f, Color.FromArgb(70, 14, 36, 74));
                // 柔光晕：右上青蓝、左下深青
                Soft(g, w * 0.86f, h * 0.02f, 470, Color.FromArgb(80, 38, 110, 190));
                Soft(g, w * 0.06f, h * 0.98f, 430, Color.FromArgb(56, 18, 84, 120));
                Soft(g, w * 0.55f, h * 0.55f, 520, Color.FromArgb(36, 30, 90, 160));
            }
            var blurred = Blur(Blur(raw, 7), 5);
            raw.Dispose();
            using (var g = Graphics.FromImage(blurred))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                // 细亮光丝（模糊后再画，保持锐利——参考图里流光边缘的亮线）
                using (var pen = new Pen(Color.FromArgb(105, 86, 178, 246), 2.2f))
                    g.DrawBezier(pen, -20, h * 0.42f, w * 0.35f, h * 0.20f, w * 0.62f, h * 0.66f, w + 20, h * 0.52f);
                using (var pen = new Pen(Color.FromArgb(64, 56, 150, 235), 1.4f))
                    g.DrawBezier(pen, -20, h * 0.60f, w * 0.40f, h * 0.40f, w * 0.58f, h * 0.84f, w + 20, h * 0.70f);
                // 极弱噪点
                int step = Math.Max(3, (w * h) / 9000);
                for (int y = 0; y < h; y += 3)
                    for (int x = (y % 6 == 0 ? 0 : 2); x < w; x += 3)
                    {
                        if (rnd.Next(step) != 0) continue;
                        using (var b = new SolidBrush(Color.FromArgb(6, 190, 215, 255)))
                            g.FillRectangle(b, x, y, 1, 1);
                    }
            }
            if (_cached != null) _cached.Dispose();
            _cached = blurred;
            _cw = w; _ch = h;
            // 返回克隆而非缓存原图：原图被后续不同尺寸的调用 Dispose 时，
            // 仍挂着旧图的窗体一重绘就崩（v1.5.0 布局测试实测抓到的真 bug，主窗与组合窗尺寸不同必触发）
            return (Bitmap)_cached.Clone();
        }

        /// <summary>缎带：一条自左向右的正弦状宽带（上下两条曲线闭合填充）。</summary>
        private static void Ribbon(Graphics g, int w, int h, float cyRatio, float thickRatio, Color color)
        {
            float cy = h * cyRatio, th = h * thickRatio;
            using (var path = new GraphicsPath())
            {
                path.AddBezier(-30, cy - th * 0.4f, w * 0.30f, cy - th * 1.1f, w * 0.62f, cy + th * 0.5f, w + 30, cy - th * 0.2f);
                path.AddBezier(w + 30, cy - th * 0.2f + th, w * 0.62f, cy + th * 0.5f + th, w * 0.30f, cy - th * 1.1f + th, -30, cy - th * 0.4f + th);
                path.CloseFigure();
                using (var b = new SolidBrush(color)) g.FillPath(b, path);
            }
        }

        private static void Soft(Graphics g, float cx, float cy, int size, Color color)
        {
            using (var b = new SolidBrush(color))
                g.FillEllipse(b, cx - size / 2, cy - size / 2, size, size);
        }

        private static Bitmap Blur(Bitmap src, int factor)
        {
            int w = Math.Max(2, src.Width / factor), h = Math.Max(2, src.Height / factor);
            var small = new Bitmap(w, h);
            using (var g = Graphics.FromImage(small))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(src, 0, 0, w, h);
            }
            var big = new Bitmap(src.Width, src.Height);
            using (var g = Graphics.FromImage(big))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(small, 0, 0, big.Width, big.Height);
            }
            small.Dispose();
            return big;
        }
    }

    /// <summary>
    /// v1.7.0 液态玻璃底图（静态稿定版）：深靛蓝斜向打底＋四团彩色光晕（左上橙、右侧粉红、
    /// 底部宝蓝、中部蓝紫），多遍缩放模糊成柔焦光场——玻璃卡片透出的就是这层颜色。
    /// 缓存独立（勿与 BackdropArt/FrostArt 混用，理由同 FrostArt 注释）；返回克隆防跨窗 Dispose 崩溃。
    /// </summary>
    public static class LiquidArt
    {
        private static Bitmap _cached;
        private static int _cw, _ch;

        public static Bitmap For(int w, int h)
        {
            if (w < 50) w = 50;
            if (h < 50) h = 50;
            if (_cached != null && Math.Abs(_cw - w) < 60 && Math.Abs(_ch - h) < 60) return (Bitmap)_cached.Clone();
            var raw = new Bitmap(w, h);
            using (var g = Graphics.FromImage(raw))
            {
                using (var lg = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(43, 16, 85), Color.FromArgb(117, 151, 222), 140f))
                    g.FillRectangle(lg, 0, 0, w, h);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Soft(g, w * 0.10f, h * 0.06f, (int)(w * 0.62f), Color.FromArgb(215, 255, 179, 92));
                Soft(g, w * 0.92f, h * 0.20f, (int)(w * 0.58f), Color.FromArgb(205, 255, 94, 122));
                Soft(g, w * 0.52f, h * 1.05f, (int)(w * 0.72f), Color.FromArgb(190, 79, 124, 255));
                Soft(g, w * 0.58f, h * 0.48f, (int)(w * 0.46f), Color.FromArgb(110, 140, 120, 255));
                Soft(g, w * 0.30f, h * 0.72f, (int)(w * 0.34f), Color.FromArgb(90, 255, 143, 163));
            }
            var blurred = Blur(Blur(raw, 8), 6);
            raw.Dispose();
            if (_cached != null) _cached.Dispose();
            _cached = blurred;
            _cw = w; _ch = h;
            return (Bitmap)_cached.Clone();
        }

        private static void Soft(Graphics g, float cx, float cy, int size, Color color)
        {
            if (size < 10) size = 10;
            using (var b = new SolidBrush(color))
                g.FillEllipse(b, cx - size / 2, cy - size / 2, size, size);
        }

        private static Bitmap Blur(Bitmap src, int factor)
        {
            int w = Math.Max(2, src.Width / factor), h = Math.Max(2, src.Height / factor);
            var small = new Bitmap(w, h);
            using (var g = Graphics.FromImage(small))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(src, 0, 0, w, h);
            }
            var big = new Bitmap(src.Width, src.Height);
            using (var g = Graphics.FromImage(big))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(small, 0, 0, big.Width, big.Height);
            }
            small.Dispose();
            return big;
        }
    }

    /// <summary>
    /// v1.5.0 深色玻璃动效（用户定版：打开和关闭也要视频里那种顺滑感）：仅 frosted 皮肤启用。
    /// 打开＝淡入（不透明度 0.25→1）＋轻微上浮（+10px 回落到位），约 180ms；
    /// 关闭＝淡出约 150ms 后真正关闭。其他皮肤与截图框选窗不受影响；异常一律静默回退为无动效。
    /// </summary>
    public static class GlassMotion
    {
        /// <summary>总开关：自动化测试置 false（动效是纯视觉层，位置/关闭断言不该跟计时器赛跑）。</summary>
        public static bool Enabled = true;
        private static readonly HashSet<Form> _fadingOut = new HashSet<Form>();

        public static void FadeIn(Form f)
        {
            if (!Enabled || !(SkinMode.IsFrosted || SkinMode.IsLiquid) || f == null) return;
            try
            {
                double from = 0.25;
                f.Opacity = from;
                f.Location = new Point(f.Location.X, f.Location.Y + 10);
                int lastOffset = 10;
                var t = new Timer { Interval = 15 };
                int step = 0;
                t.Tick += (snd, ev) =>
                {
                    step++;
                    double k = step / 12.0; // 12×15ms ≈ 180ms
                    try
                    {
                        if (f.IsDisposed) { t.Stop(); t.Dispose(); return; }
                        double ease = k >= 1 ? 1 : 1 - Math.Pow(1 - k, 3);
                        // 相对偏移回落（不钉绝对坐标）：动画期间窗体被业务逻辑挪位（如组合窗按内容重新居中）不冲突
                        int newOffset = (int)Math.Round(10 * (1 - ease));
                        int dy = lastOffset - newOffset;
                        if (dy != 0) f.Location = new Point(f.Location.X, f.Location.Y - dy);
                        lastOffset = newOffset;
                        f.Opacity = from + (1 - from) * ease;
                        if (k >= 1) { t.Stop(); t.Dispose(); f.Opacity = 1.0; }
                    }
                    catch { t.Stop(); t.Dispose(); }
                };
                t.Start();
            }
            catch { try { f.Opacity = 1.0; } catch { } }
        }

        /// <summary>挂到各窗 FormClosing：首次关闭先取消并淡出，淡完再真关（第二次进入直接放行）。</summary>
        public static void TryFadeOut(Form f, FormClosingEventArgs e)
        {
            if (!Enabled || !(SkinMode.IsFrosted || SkinMode.IsLiquid) || f == null || e == null) return;
            if (_fadingOut.Contains(f)) { _fadingOut.Remove(f); return; }
            _fadingOut.Add(f);
            e.Cancel = true;
            try
            {
                double from = f.Opacity;
                var t = new Timer { Interval = 15 };
                int step = 0;
                t.Tick += (snd, ev) =>
                {
                    step++;
                    double k = step / 10.0; // 10×15ms ≈ 150ms
                    try
                    {
                        if (f.IsDisposed) { t.Stop(); t.Dispose(); return; }
                        if (k >= 1) { t.Stop(); t.Dispose(); f.Close(); return; }
                        f.Opacity = from * (1 - k);
                    }
                    catch { t.Stop(); t.Dispose(); }
                };
                t.Start();
            }
            catch { _fadingOut.Remove(f); try { f.Close(); } catch { } }
        }
    }
}
