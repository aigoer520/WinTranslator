using System;
using System.Drawing;
using System.Windows.Forms;

namespace WinTranslator
{
    /// <summary>
    /// 全屏定格框选层：进入时先抓全屏图铺满，框选从同一张图裁剪，
    /// 保证"所见即所得"，杜绝二次抓屏的坐标错位。
    /// </summary>
    public class OverlayForm : Form
    {
        private readonly Bitmap _frozen;
        private Point _start;
        private Rectangle _sel;
        private bool _dragging;

        public Bitmap CroppedImage { get; private set; }
        public Rectangle SelectionScreenRect { get; private set; }

        public OverlayForm(Bitmap frozenScreen)
        {
            _frozen = frozenScreen;
            FormBorderStyle = FormBorderStyle.None;
            Bounds = SystemInformation.VirtualScreen;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
            KeyPreview = true;
            ShowInTaskbar = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.DrawImage(_frozen, 0, 0, _frozen.Width, _frozen.Height);
            using (var shade = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
                e.Graphics.FillRectangle(shade, ClientRectangle);
            if (_dragging && _sel.Width > 0 && _sel.Height > 0)
            {
                e.Graphics.DrawImage(_frozen, _sel, _sel, GraphicsUnit.Pixel);
                using (var pen = new Pen(Color.FromArgb(238, 63, 42), 2))
                    e.Graphics.DrawRectangle(pen, _sel);
                string label = _sel.Width + " × " + _sel.Height;
                using (var f = new Font("Microsoft YaHei UI", 10f))
                using (var bg = new SolidBrush(Color.FromArgb(238, 63, 42)))
                using (var fg = new SolidBrush(Color.White))
                {
                    var size = e.Graphics.MeasureString(label, f);
                    float ly = _sel.Top - size.Height - 8;
                    if (ly < 0) ly = _sel.Top + 4;
                    e.Graphics.FillRectangle(bg, _sel.Left, ly, size.Width + 8, size.Height + 2);
                    e.Graphics.DrawString(label, f, fg, _sel.Left + 4, ly + 1);
                }
            }
            else
            {
                using (var f = new Font("Microsoft YaHei UI", 12f))
                using (var fg = new SolidBrush(Color.White))
                    e.Graphics.DrawString("拖动鼠标框选要翻译的区域，右键或 Esc 取消", f, fg, 24, 20);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right)
            {
                // v1.2.1 事项四：框选时右键按下即取消（与 Esc 等效，拖动中途亦然）
                _dragging = false;
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _start = e.Location;
            _sel = new Rectangle(e.Location, Size.Empty);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;
            int x = Math.Min(_start.X, e.X), y = Math.Min(_start.Y, e.Y);
            int w = Math.Abs(e.X - _start.X), h = Math.Abs(e.Y - _start.Y);
            _sel = new Rectangle(x, y, w, h);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !_dragging) return;
            _dragging = false;
            if (_sel.Width < 8 || _sel.Height < 8)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }
            CroppedImage = _frozen.Clone(_sel, _frozen.PixelFormat);
            var vs = SystemInformation.VirtualScreen;
            SelectionScreenRect = new Rectangle(_sel.X + vs.Left, _sel.Y + vs.Top, _sel.Width, _sel.Height);
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            // _frozen 由调用方负责释放（截图流程统一管理）；此处不释放。
        }
    }
}
