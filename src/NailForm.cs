using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WinTranslator
{
    /// <summary>钉图窗：截图钉在桌面置顶，可拖动、复制图片、另存、关闭。</summary>
    public class NailForm : Form
    {
        private readonly Bitmap _image;
        private readonly PictureBox _pic;
        private bool _dragging;
        private Point _dragStartForm, _dragStartMouse;

        public NailForm(Bitmap image)
        {
            _image = image;
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(Math.Max(80, image.Width), Math.Max(60, image.Height));
            BackColor = SkinMode.IsFrosted ? Palette.Frame : Color.White;

            _pic = new PictureBox
            {
                Dock = DockStyle.Fill,
                Image = image,
                SizeMode = PictureBoxSizeMode.Zoom,
                Cursor = Cursors.SizeAll
            };
            Controls.Add(_pic);

            var menu = new ContextMenuStrip();
            menu.Items.Add("复制图片", null, (s, e) => { try { Clipboard.SetImage(_image); } catch { } });
            menu.Items.Add("另存为…", null, (s, e) => SaveAs());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("关闭", null, (s, e) => Close());
            _pic.ContextMenuStrip = menu;
            ContextMenuStrip = menu;

            _pic.MouseDown += PicMouseDown;
            _pic.MouseMove += PicMouseMove;
            _pic.MouseUp += (s, e) => _dragging = false;
            _pic.MouseDoubleClick += (s, e) => Close();
            KeyPreview = true;
            Shown += (s, e) => GlassMotion.FadeIn(this);
            Shown += (s, e) => FontTheme.ApplyTo(this); // v1.5.3 界面字体/字号档
            FormClosing += (s, e) => GlassMotion.TryFadeOut(this, e);
        }

        private void PicMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _dragStartForm = Location;
            _dragStartMouse = Cursor.Position;
        }

        private void PicMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            var now = Cursor.Position;
            Location = new Point(_dragStartForm.X + now.X - _dragStartMouse.X,
                                 _dragStartForm.Y + now.Y - _dragStartMouse.Y);
        }

        private void SaveAs()
        {
            using (var dlg = new SaveFileDialog
            {
                Filter = "PNG 图片|*.png|JPEG 图片|*.jpg",
                FileName = "钉图-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png"
            })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    var fmt = Path.GetExtension(dlg.FileName).ToLowerInvariant() == ".jpg"
                        ? System.Drawing.Imaging.ImageFormat.Jpeg
                        : System.Drawing.Imaging.ImageFormat.Png;
                    _image.Save(dlg.FileName, fmt);
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Color.FromArgb(238, 63, 42), 2))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Close();
        }
    }
}
