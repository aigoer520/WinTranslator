using System;
using System.Drawing;
using System.Windows.Forms;

namespace WinTranslator
{
    /// <summary>
    /// 迷你窗口：小窗查词。搜索框＋翻译、历史上下翻、打开主窗口。
    /// 无边框可拖动，位置持久化。
    /// </summary>
    public class MiniForm : Form
    {
        private readonly TranslationEngine _engine;
        private readonly Store _store;
        private readonly Action _openMain;
        private readonly TextBox _input, _output;
        private int _historyIndex = -1;
        private bool _dragging;
        private Point _dragForm, _dragMouse;
        private readonly Label _lblStatus;

        public MiniForm(TranslationEngine engine, Store store, Action openMain)
        {
            _engine = engine;
            _store = store;
            _openMain = openMain;

            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(404, 216);
            BackColor = SkinMode.IsAcrylic ? SkinMode.Key : (SkinMode.IsFrosted ? Palette.Bg : Color.White);
            Font = new Font("Microsoft YaHei UI", 9f);
            KeyPreview = true;

            if (_store.Settings.MiniX >= 0)
            {
                var wa = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(Math.Min(_store.Settings.MiniX, wa.Right - 60),
                                     Math.Min(_store.Settings.MiniY, wa.Bottom - 40));
            }
            else Location = new Point(120, 120);

            var title = new Label
            {
                Text = "迷你翻译  （拖动移动 · Esc 关闭）",
                Location = new Point(14, 10), AutoSize = true,
                ForeColor = SkinMode.IsFrosted ? Palette.Sub : Color.FromArgb(120, 120, 120)
            };
            Controls.Add(title);

            _input = new TextBox { Location = new Point(14, 38), Width = 272, Font = new Font("Microsoft YaHei UI", 11f) };
            if (SkinMode.IsFrosted) { _input.BackColor = Palette.Card; _input.ForeColor = Palette.Main; }
            Controls.Add(_input);
            var btnGo = new Button
            {
                Text = "翻译", Location = new Point(292, 36), Width = 98, Height = 30,
                FlatStyle = FlatStyle.Flat, BackColor = Palette.Accent,
                ForeColor = Color.White, Cursor = Cursors.Hand
            };
            btnGo.FlatAppearance.BorderSize = 0;
            Controls.Add(btnGo);

            var btnPrev = NavBtn("上一条", 14);
            var btnNext = NavBtn("下一条", 98);
            var btnMain = NavBtn("打开主窗口", 182);
            btnMain.Width = 96;
            _lblStatus = new Label
            {
                Location = new Point(292, 76), AutoSize = false, Width = 98, Height = 20,
                ForeColor = SkinMode.IsFrosted ? Palette.Sub : Color.FromArgb(150, 150, 150), TextAlign = ContentAlignment.MiddleRight
            };
            Controls.AddRange(new Control[] { btnPrev, btnNext, btnMain, _lblStatus });

            _output = new TextBox
            {
                Location = new Point(14, 108), Size = new Size(376, 94),
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle, Font = new Font("Microsoft YaHei UI", 11f),
                BackColor = SkinMode.IsFrosted ? Palette.Card : Color.FromArgb(255, 250, 240),
                ForeColor = SkinMode.IsFrosted ? Palette.Main : Color.FromArgb(30, 30, 30)
            };
            Controls.Add(_output);

            btnGo.Click += async (s, e) => await DoTranslate();
            _input.KeyDown += async (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await DoTranslate(); }
            };
            btnPrev.Click += (s, e) => StepHistory(1);
            btnNext.Click += (s, e) => StepHistory(-1);
            btnMain.Click += (s, e) => { if (_openMain != null) _openMain(); };

            MouseDown += DragDown;
            MouseMove += DragMove;
            MouseUp += (s, e) => _dragging = false;
            title.MouseDown += DragDown;
            title.MouseMove += DragMove;
            title.MouseUp += (s, e) => _dragging = false;
            Shown += (s, e) => GlassMotion.FadeIn(this);
            Shown += (s, e) => FontTheme.ApplyTo(this); // v1.5.3 界面字体/字号档
            FormClosing += (s, e) => GlassMotion.TryFadeOut(this, e);
        }

        private static Button NavBtn(string text, int x)
        {
            return new Button
            {
                Text = text, Location = new Point(x, 74), Width = 76, Height = 26,
                FlatStyle = FlatStyle.Flat, BackColor = SkinMode.IsFrosted ? Palette.BarBtn : Color.FromArgb(245, 245, 245),
                ForeColor = SkinMode.IsFrosted ? Palette.Main : Color.FromArgb(30, 30, 30), Cursor = Cursors.Hand
            };
        }

        private void DragDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _dragForm = Location;
            _dragMouse = Cursor.Position;
        }

        private void DragMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            var now = Cursor.Position;
            Location = new Point(_dragForm.X + now.X - _dragMouse.X, _dragForm.Y + now.Y - _dragMouse.Y);
        }

        private async System.Threading.Tasks.Task DoTranslate()
        {
            string text = _input.Text.Trim();
            if (text.Length == 0) return;
            _lblStatus.Text = "翻译中…";
            try
            {
                var r = await _engine.Translate(text, "auto", "auto", ResultForm.PreferOf(_store.Settings.EngineIndex));
                _output.Text = r.Text;
                _lblStatus.Text = r.Provider;
                _historyIndex = -1;
                _store.AddHistory(new Entry
                {
                    Src = text, Dst = r.Text, SrcLang = r.SrcLang, DstLang = r.DstLang,
                    Provider = r.Provider, Time = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds
                });
            }
            catch (Exception ex)
            {
                _output.Text = ex.Message;
                _lblStatus.Text = "失败";
            }
        }

        private void StepHistory(int delta)
        {
            var h = _store.History;
            if (h.Count == 0) { _lblStatus.Text = "暂无历史"; return; }
            int next = _historyIndex + delta;
            if (next < 0) next = 0;
            if (next >= h.Count) next = h.Count - 1;
            _historyIndex = next;
            _input.Text = h[next].Src;
            _output.Text = h[next].Dst;
            _lblStatus.Text = (next + 1) + "/" + h.Count;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SkinFx.Apply(this);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Color.FromArgb(220, 220, 220), 1))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Hide();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _store.Settings.MiniX = Location.X;
            _store.Settings.MiniY = Location.Y;
            _store.SaveSettings();
            base.OnFormClosing(e);
        }
    }
}
