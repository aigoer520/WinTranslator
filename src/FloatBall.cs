using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinTranslator
{
    /// <summary>桌面悬浮球：双击截图翻译，右键菜单，拖动换位（位置持久化）。</summary>
    public class FloatBall : Form
    {
        private readonly Store _store;
        private bool _dragging, _moved;
        private Point _dragForm, _dragMouse;

        public event Action ScreenshotRequested;
        public event Action<int> ScreenshotDelayedRequested;
        public event Action MiniRequested;
        public event Action MainRequested;

        public FloatBall(Store store)
        {
            _store = store;
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            // v1.1.1 事项二：正圆造型锁定正方（56×56），不再用洋红透明键（真机出现紫边/拉宽），改 Region 圆形裁剪
            ClientSize = new Size(56, 56);
            MinimumSize = new Size(56, 56);
            MaximumSize = new Size(56, 56);
            BackColor = Color.White;

            if (store.Settings.BallX >= 0)
            {
                var wa = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(Math.Min(store.Settings.BallX, wa.Right - 30),
                                     Math.Min(store.Settings.BallY, wa.Bottom - 30));
            }
            else
            {
                var wa = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(wa.Right - 90, wa.Bottom - 160);
            }

            var menu = new ContextMenuStrip();
            menu.Items.Add("截图翻译", null, (s, e) => { if (ScreenshotRequested != null) ScreenshotRequested(); });
            menu.Items.Add("延时 3 秒截图", null, (s, e) => { if (ScreenshotDelayedRequested != null) ScreenshotDelayedRequested(3); });
            menu.Items.Add("延时 5 秒截图", null, (s, e) => { if (ScreenshotDelayedRequested != null) ScreenshotDelayedRequested(5); });
            menu.Items.Add("迷你窗口", null, (s, e) => { if (MiniRequested != null) MiniRequested(); });
            menu.Items.Add("打开主窗口", null, (s, e) => { if (MainRequested != null) MainRequested(); });
            ContextMenuStrip = menu;

            MouseDown += BallMouseDown;
            MouseMove += BallMouseMove;
            MouseUp += BallMouseUp;
            DoubleClick += (s, e) => { if (ScreenshotRequested != null) ScreenshotRequested(); };
        }

        private void BallMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _moved = false;
            _dragForm = Location;
            _dragMouse = Cursor.Position;
        }

        private void BallMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            var now = Cursor.Position;
            int dx = now.X - _dragMouse.X, dy = now.Y - _dragMouse.Y;
            if (Math.Abs(dx) + Math.Abs(dy) > 4) _moved = true;
            Location = new Point(_dragForm.X + dx, _dragForm.Y + dy);
        }

        private void BallMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _dragging = false;
            if (_moved)
            {
                _store.Settings.BallX = Location.X;
                _store.Settings.BallY = Location.Y;
                _store.SaveSettings();
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // 圆形窗口区域：按较小边定直径、居中裁剪，保证任何情况下都是正圆
            int d = Math.Min(Width, Height);
            if (d <= 0) return;
            var path = new GraphicsPath();
            path.AddEllipse((Width - d) / 2, (Height - d) / 2, d, d);
            Region = new Region(path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int d = Math.Min(Width, Height);
            int ox = (Width - d) / 2, oy = (Height - d) / 2;
            // 白圈（外圈）＋红渐变内圆，仿参考图 yd 悬浮图标造型（不含其商标字样）
            using (var w = new SolidBrush(Color.White))
                e.Graphics.FillEllipse(w, ox, oy, d, d);
            const int ring = 4;
            // v1.6.3：悬浮球全皮肤统一红渐变（v1.6.0 的 Lucide 黑球用户明确不要，点名要红底）
            Color gradA = Color.FromArgb(255, 96, 66);
            Color gradB = Color.FromArgb(224, 42, 22);
            using (var brush = new LinearGradientBrush(new Rectangle(ox + ring, oy + ring, d - ring * 2, d - ring * 2),
                gradA, gradB, 45f))
                e.Graphics.FillEllipse(brush, ox + ring, oy + ring, d - ring * 2, d - ring * 2);
            // v1.5.1 事项四：离屏位图墨迹居中——先把字画到临时图、按实际着墨包围盒算中心，再贴到圆心。
            // v1.5.0 的 TextRenderer 居中对齐的是字体行框而非墨迹框，真机仍偏右溢出（用户回图 image-114），故改此确定性方案。
            EnsureGlyph();
            if (_glyph != null)
            {
                int inner = d - ring * 2;
                float targetH = inner * 0.60f;
                float scale = targetH / _ink.Height;
                float dw = _ink.Width * scale, dh = targetH;
                float cx = ox + d / 2f, cy = oy + d / 2f;
                var dest = new RectangleF(cx - dw / 2f, cy - dh / 2f, dw, dh);
                var g2 = e.Graphics;
                var oldMode = g2.InterpolationMode;
                g2.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g2.DrawImage(_glyph, dest, new RectangleF(_ink.X, _ink.Y, _ink.Width, _ink.Height), GraphicsUnit.Pixel);
                g2.InterpolationMode = oldMode;
            }
        }

        private static Bitmap _glyph;
        private static Rectangle _ink;

        /// <summary>离屏渲染"译"并量测着墨包围盒（只做一次）。字画在 160×160 画布上、字号足够大以保证缩放平滑。</summary>
        private static void EnsureGlyph()
        {
            if (_glyph != null) return;
            try
            {
                var bmp = new Bitmap(160, 160);
                using (var g = Graphics.FromImage(bmp))
                using (var f = new Font(FontTheme.Family, 64f, FontStyle.Bold))
                {
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    TextRenderer.DrawText(g, "译", f, new Rectangle(0, 0, 160, 160), Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                int minX = 160, minY = 160, maxX = -1, maxY = -1;
                for (int y = 0; y < 160; y++)
                    for (int x = 0; x < 160; x++)
                    {
                        if (bmp.GetPixel(x, y).A < 40) continue;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                if (maxX < 0) { bmp.Dispose(); return; }
                _ink = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
                _glyph = bmp;
            }
            catch { }
        }
    }
}
