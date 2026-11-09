using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WinTranslator
{
    /// <summary>
    /// 翻译结果浮窗（取词划词/截图翻译共用）：
    /// 文字模式＝紧凑双行工具条＋双卡（原形态不变）；
    /// 截图模式（v1.1.1 事项三）＝灰色圆角外框＋上译文/下原文双卡＋底部单条深色栏
    /// （语言/对照开关/图标组/状态/红色"提取文本"胶囊）＋右上角白色圆形关闭徽标，
    /// 窗口尺寸随截取区与文字量自适应，窄窗底栏自动两行。
    /// </summary>
    public class ResultForm : Form
    {
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private readonly TranslationEngine _engine;
        private readonly Store _store;
        private readonly Bitmap _image;          // 截图模式才有
        private readonly bool _shot;
        private string _srcText = "";
        private string _dstText = "";
        private string _srcSel = "auto";         // auto 或 LangCodes
        private string _dstSel = "auto";
        private string _lastSrcLang = "", _lastDstLang = "";
        private bool _busy;
        // v1.3.4 双呈现：顶部双图条（左原截图、右译图）＋图文同源重翻回调
        private Panel _imgStrip;
        private PictureBox _picOrig, _picTran;
        private Label _lblOrigCap, _lblTranCap, _lblImgNote;
        private Bitmap _comboOrig, _comboTran;
        private bool _showTran = true;   // v1.5.2 事项 A：单显模式当前显示译图（true）还是原图（false）
        public Func<string, string, Task> ComboRetranslate;

        // 文字模式控件
        private Button _btnSrc, _btnSwap, _btnDst, _btnCopy, _btnFav;
        private CheckBox _chkCompare;
        private Button _btnSaveImg, _btnNail, _btnRetry, _btnClose;
        private Label _lblStatus;
        private TextBox _txtSrc, _txtDst;
        private Panel _cardPanel;
        // v1.3.0 点词全域：紧凑小窗英文侧点词（与对应文本框同位置二选一）
        private Panel _cSrcScroll, _cDstScroll;
        private WordText _cWtSrc, _cWtDst;
        private bool _cSrcEn, _cDstEn;

        // 截图模式控件
        private RoundPanel _dstCard, _srcCard, _bar, _chipSrc, _chipDst, _pill;
        private BadgeBtn _badge;
        private TextBox _sTxtSrc, _sTxtDst;
        private TextBox _sSrcSelect; // v1.6.4：原文点词层之上的选取层（拖选→可复制）
        private bool _srcSelArmed;
        private Point _srcSelStart;
        // v1.2.1 事项一：英文侧点词（WordText 宿主在滚动面板内，与文本框二选一显示）
        private Panel _sDstScroll, _sSrcScroll;
        private WordText _sWtDst, _sWtSrc;
        /// <summary>v1.2.1：点词回调（MainForm 注入，打开查词弹窗）。</summary>
        public Action<string, Point> WordLookup;
        private Label _chipSrcLbl, _chipDstLbl, _barStatus;
        private Label _ccNote; // v1.8.0：交叉核对存疑说明（截图对照模式下贴在底栏上方）
        private ToggleSwitch _tglCompare;
        private readonly List<IconBtn> _icons = new List<IconBtn>();
        private bool _shotCompare = true;
        private int _srcNeedFit; // FitToContent 量得的原文卡拟合高，LayoutShot 据此保底（避免与图条固定留底双算）
        private ToolTip _tips;
        private Rectangle _selRect;              // 截图选区（屏幕坐标），弹窗按其正下方居中
        private bool _hasSel;

        public ResultForm(TranslationEngine engine, Store store, string srcText, Bitmap image)
        {
            _engine = engine;
            _store = store;
            _image = image;
            _shot = image != null;
            _srcText = srcText ?? "";

            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Font = new Font("Microsoft YaHei UI", 9f);
            KeyPreview = true;

            if (_shot) BuildShotUi();
            else BuildCompactUi();
            Shown += (s, e) => GlassMotion.FadeIn(this);
            Shown += (s, e) => FontTheme.ApplyTo(this); // v1.5.3 界面字体/字号档
            FormClosing += (s, e) => GlassMotion.TryFadeOut(this, e);
            WireRightClose(this); // v1.2.1 事项四：弹窗内任意位置右键按下即关闭
        }

        /// <summary>v1.2.1 事项四：递归给全部子控件挂右键关闭（按钮/文本区一视同仁）。</summary>
        private void WireRightClose(Control c)
        {
            c.MouseDown += (s, e) => { if (e.Button == MouseButtons.Right && !IsDisposed) Close(); };
            foreach (Control child in c.Controls) WireRightClose(child);
        }

        // ---------------- 文字模式（紧凑，原形态） ----------------

        private void BuildCompactUi()
        {
            BackColor = SkinMode.IsAcrylic ? SkinMode.Key : (SkinMode.IsFrosted ? Palette.Bg : Color.White);
            ClientSize = new Size(640, 356);

            var accent = Color.FromArgb(238, 63, 42);
            var bar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = SkinMode.IsFrosted ? Palette.NavBg : Color.FromArgb(245, 245, 245) };
            Controls.Add(bar);

            _btnSrc = ToolBtn("自动检测 ▾", 8);
            _btnSwap = ToolBtn("⇄", 108); _btnSwap.Width = 34;
            _btnDst = ToolBtn("目标语言 ▾", 148);
            _btnDst.Text = "自动 ▾";
            _chkCompare = new CheckBox
            {
                Text = "对照", Checked = true, AutoSize = true,
                Location = new Point(252, 11), ForeColor = SkinMode.IsFrosted ? Palette.Main : Color.FromArgb(30, 30, 30)
            };
            _btnCopy = ToolBtn("复制", 316);
            _btnFav = ToolBtn("☆ 收藏", 382);
            var btnExtractSrc = ToolBtn("提取原文", 468);
            var btnExtractDst = ToolBtn("提取译文", 552);
            btnExtractDst.Width = 80;
            bar.Controls.AddRange(new Control[] { _btnSrc, _btnSwap, _btnDst, _chkCompare, _btnCopy, _btnFav, btnExtractSrc, btnExtractDst });

            var bar2 = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = SkinMode.IsFrosted ? Palette.NavBg : Color.FromArgb(245, 245, 245) };
            Controls.Add(bar2);
            bar.SendToBack(); // Dock=Top 按 z 序逆序停靠：置底者先停靠、在最上方（语言行在上、操作行在下）
            _btnSaveImg = ToolBtn("保存图片", 8); _btnSaveImg.Width = 76;
            _btnNail = ToolBtn("📌 钉图", 92); _btnNail.Width = 76;
            _btnRetry = ToolBtn("↻ 重翻", 176); _btnRetry.Width = 76;
            _lblStatus = new Label
            {
                AutoSize = false, Width = 280, Height = 22,
                Location = new Point(262, 7), ForeColor = SkinMode.IsFrosted ? Palette.Sub : Color.FromArgb(120, 120, 120),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _btnClose = ToolBtn("✕ 关闭", 556); _btnClose.Width = 76;
            _btnClose.ForeColor = accent;
            bar2.Controls.AddRange(new Control[] { _btnSaveImg, _btnNail, _btnRetry, _lblStatus, _btnClose });

            _cardPanel = new Panel { Dock = DockStyle.Fill, BackColor = SkinMode.IsAcrylic ? SkinMode.Key : (SkinMode.IsFrosted ? Palette.Bg : Color.White) };
            Controls.Add(_cardPanel);
            _cardPanel.BringToFront();

            float fs = _store != null ? _store.Settings.FontSize : 14;
            _txtSrc = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle, BackColor = SkinMode.IsFrosted ? Palette.Card : Color.White,
                ForeColor = SkinMode.IsFrosted ? Palette.Main : Color.FromArgb(26, 26, 26), Font = new Font("Microsoft YaHei UI", fs),
                Text = _srcText
            };
            _txtDst = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle, BackColor = SkinMode.IsFrosted ? Palette.Card : Color.FromArgb(255, 250, 240),
                ForeColor = SkinMode.IsFrosted ? Palette.Main : Color.FromArgb(26, 26, 26), Font = new Font("Microsoft YaHei UI", fs)
            };
            _cardPanel.Controls.AddRange(new Control[] { _txtSrc, _txtDst });
            _cWtSrc = MakeWordText(new Font("Microsoft YaHei UI", fs));
            _cWtDst = MakeWordText(new Font("Microsoft YaHei UI", fs));
            _cWtDst.BackColor = SkinMode.IsFrosted ? Palette.Card : Color.FromArgb(255, 250, 240);
            _cSrcScroll = new Panel { BackColor = SkinMode.IsFrosted ? Palette.Card : Color.White, AutoScroll = true, Visible = false };
            _cSrcScroll.Controls.Add(_cWtSrc);
            _cDstScroll = new Panel { BackColor = SkinMode.IsFrosted ? Palette.Card : Color.FromArgb(255, 250, 240), AutoScroll = true, Visible = false };
            _cDstScroll.Controls.Add(_cWtDst);
            _cardPanel.Controls.AddRange(new Control[] { _cSrcScroll, _cDstScroll });

            _btnSrc.Click += (s, e) => ShowLangMenu(_btnSrc, true);
            _btnDst.Click += (s, e) => ShowLangMenu(_btnDst, false);
            _btnSwap.Click += (s, e) => DoSwap();
            _chkCompare.CheckedChanged += (s, e) => RelayoutCards();
            _btnCopy.Click += (s, e) => CopyText(_dstText, "译文");
            _btnFav.Click += (s, e) => Favorite();
            btnExtractSrc.Click += (s, e) => CopyText(_srcText, "原文");
            btnExtractDst.Click += (s, e) => CopyText(_dstText, "译文");
            _btnSaveImg.Click += (s, e) => SaveImage();
            _btnNail.Click += (s, e) => NailImage();
            _btnRetry.Click += (s, e) => Retranslate();
            _btnClose.Click += (s, e) => Close();

            RelayoutCards();
            UpdateLangLabels();
        }

        private static Button ToolBtn(string text, int x)
        {
            return new Button
            {
                Text = text, Location = new Point(x, 6), Width = 62, Height = 27,
                FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false,
                BackColor = SkinMode.IsFrosted ? Palette.BarBtn : Color.White, ForeColor = SkinMode.IsFrosted ? Palette.Main : Color.FromArgb(30, 30, 30),
                Font = new Font("Microsoft YaHei UI", 9f), Cursor = Cursors.Hand
            };
        }

        private void RelayoutCards()
        {
            if (_shot || _cardPanel == null || _txtSrc == null || _txtDst == null || _chkCompare == null) return;
            int w = _cardPanel.ClientSize.Width, h = _cardPanel.ClientSize.Height;
            if (w <= 0 || h <= 0) return;
            Rectangle srcRect, dstRect;
            if (_chkCompare.Checked)
            {
                srcRect = new Rectangle(8, 8, (w - 24) / 2, h - 16);
                dstRect = new Rectangle(16 + (w - 24) / 2, 8, (w - 24) / 2, h - 16);
            }
            else
            {
                srcRect = new Rectangle(8, 8, w - 16, (h - 24) / 2);
                dstRect = new Rectangle(8, 16 + (h - 24) / 2, w - 16, (h - 24) / 2);
            }
            _txtSrc.Bounds = srcRect;
            _txtDst.Bounds = dstRect;
            _txtSrc.Visible = !_cSrcEn;
            _txtDst.Visible = !_cDstEn;
            if (_cSrcScroll != null)
            {
                _cSrcScroll.Bounds = srcRect;
                _cSrcScroll.Visible = _cSrcEn;
                if (_cSrcEn) _cWtSrc.LayoutInScrollPanel(_cSrcScroll);
            }
            if (_cDstScroll != null)
            {
                _cDstScroll.Bounds = dstRect;
                _cDstScroll.Visible = _cDstEn;
                if (_cDstEn) _cWtDst.LayoutInScrollPanel(_cDstScroll);
            }
        }

        // ---------------- 截图模式（v1.1.1 新样式） ----------------

        private WordText MakeWordText(Font f)
        {
            var wt = new WordText { BackColor = Palette.Card, ForeColor = Palette.Main, Font = f, Visible = true };
            wt.WordClicked += (word, pt) => { if (WordLookup != null) WordLookup(word, wt.PointToScreen(pt)); };
            return wt;
        }

        private static Panel MakeScrollHost(RoundPanel card, WordText wt)
        {
            var p = new Panel { BackColor = Palette.Card, AutoScroll = true, Visible = false };
            p.Controls.Add(wt);
            card.Controls.Add(p);
            return p;
        }

        /// <summary>v1.2.1 事项一：翻译结果出来后，按实际语种决定每侧显示文本框还是点词控件。
        /// 每次调用都会把（重翻后变化的）文本同步进点词控件。布局在 LayoutShot 里做。</summary>
        private void ApplyWordMode()
        {
            bool srcEn = _lastSrcLang == "en", dstEn = _lastDstLang == "en";
            if (!_shot)
            {
                _cSrcEn = srcEn;
                _cDstEn = dstEn;
                if (_cWtSrc != null) _cWtSrc.Text = _srcText ?? "";
                if (_cWtDst != null) _cWtDst.Text = _dstText ?? "";
                RelayoutCards();
                return;
            }
            _sWtSrc.Text = _srcText ?? "";
            _sWtDst.Text = _dstText ?? "";
            if (_sSrcSelect != null) { _sSrcSelect.Visible = false; _sSrcSelect.Text = _srcText ?? ""; }
            _sSrcScroll.Visible = srcEn;
            _sTxtSrc.Visible = !srcEn;
            _sDstScroll.Visible = dstEn;
            _sTxtDst.Visible = !dstEn;
        }

        private void BuildShotUi()
        {
            BackColor = Palette.Frame;
            if (SkinMode.IsFrosted)
            {
                // v1.5.0 仿磨砂：窗体铺磨砂纹理（卡片奶白半透感由 Palette.Card 近似实现）
                BackgroundImage = FrostArt.For(1100, 800);
                BackgroundImageLayout = ImageLayout.Stretch;
            }
            if (SkinMode.IsLiquid)
            {
                // v1.7.0 液态玻璃：窗体铺彩色光晕底图，半透明卡片（Palette.Card 带 alpha）透出底色
                BackgroundImage = LiquidArt.For(1100, 800);
                BackgroundImageLayout = ImageLayout.Stretch;
            }
            MinimumSize = new Size(380, 250);
            int initW = Math.Max(420, Math.Min(920, (_image != null ? _image.Width : 640) + 44));
            ClientSize = new Size(initW, 380);
            _tips = new ToolTip();

            float fs = _store != null ? _store.Settings.FontSize : 14;
            _dstCard = new RoundPanel { Radius = 14, Fill = Palette.Card, NoEdge = true };
            _sTxtDst = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None, BackColor = Palette.Card,
                ForeColor = Palette.Main, Font = new Font("Microsoft YaHei UI", fs + 2, FontStyle.Bold)
            };
            _dstCard.Controls.Add(_sTxtDst);
            _sWtDst = MakeWordText(new Font("Microsoft YaHei UI", fs + 2, FontStyle.Bold));
            _sDstScroll = MakeScrollHost(_dstCard, _sWtDst);
            _srcCard = new RoundPanel { Radius = 14, Fill = Palette.Card, NoEdge = true };
            _sTxtSrc = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None, BackColor = Palette.Card,
                ForeColor = Palette.Main, Font = new Font("Microsoft YaHei UI", Math.Max(10f, fs - 1))
            };
            _srcCard.Controls.Add(_sTxtSrc);
            _sWtSrc = MakeWordText(new Font("Microsoft YaHei UI", Math.Max(10f, fs - 1)));
            _sSrcScroll = MakeScrollHost(_srcCard, _sWtSrc);
            // v1.6.4：点词层上加透明选取层——拖选即把选区交回文本框显示以便复制（用户要求原文可选取复制；
            // 点词层本身是自绘控件没有选区能力）
            _sSrcSelect = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None, BackColor = Palette.Card,
                ForeColor = Palette.Main, Font = new Font("Microsoft YaHei UI", Math.Max(10f, fs - 1)),
                Visible = false
            };
            _sSrcScroll.Controls.Add(_sSrcSelect);
            _sWtSrc.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                _srcSelArmed = true;
                _srcSelStart = e.Location;
            };
            _sWtSrc.MouseUp += (s, e) =>
            {
                if (!_srcSelArmed) return;
                _srcSelArmed = false;
                int dx = Math.Abs(e.Location.X - _srcSelStart.X), dy = Math.Abs(e.Location.Y - _srcSelStart.Y);
                if (dx + dy < 6) return; // 纯单击＝点词查词，不切选取层
                _sSrcSelect.Text = _srcText ?? "";
                _sSrcSelect.Visible = true;
                _sSrcSelect.BringToFront();
                _sSrcSelect.Focus();
            };
            _sSrcSelect.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { _sSrcSelect.Visible = false; }
            };
            _sSrcSelect.Leave += (s, e) =>
            {
                // 选区为空就自动回点词层；有选区留着供 Ctrl+C（用户点回图/底栏时再由新一轮操作覆盖）
                if (_sSrcSelect.SelectionLength == 0) _sSrcSelect.Visible = false;
            };
            Controls.AddRange(new Control[] { _dstCard, _srcCard });

            _bar = new RoundPanel { Radius = 16, Fill = Palette.BarBg, NoEdge = true };
            Controls.Add(_bar);

            _chipSrc = BarChip(out _chipSrcLbl);
            _chipSrc.WireClickThrough((s, e) => ShowLangMenu(_chipSrc, true));
            _chipDst = BarChip(out _chipDstLbl);
            _chipDst.WireClickThrough((s, e) => ShowLangMenu(_chipDst, false));
            var swap = new IconBtn { Kind = "swap", Size = new Size(34, 30) };
            swap.Click += (s, e) => DoSwap();
            swap.Name = "swapLbl";
            var sep = Ui.Lbl("|", 0, 0, 11f, Palette.BarSub);
            sep.Name = "sepLbl";
            var cmpLbl = Ui.Lbl("原文", 0, 0, 10.5f, Color.White);
            cmpLbl.Name = "cmpLbl";
            _tglCompare = new ToggleSwitch(true);
            _tglCompare.Changed += (s, e) => { _shotCompare = _tglCompare.On; LayoutShot(); };
            _bar.Controls.AddRange(new Control[] { _chipSrc, swap, _chipDst, sep, cmpLbl, _tglCompare });

            // v1.5.1 事项五：底栏改程序矢量图标钮（v1.1.3 的纯文字是字体字形时代的权宜；矢量自绘后不再缺字/裁字，悬停提示保留文字说明）
            AddIcon("copy", "复制译文", (s, e) => CopyText(_dstText, "译文"));
            AddIcon("star", "收藏到单词本", (s, e) => Favorite());
            AddIcon("saveimg", "保存截图图片", (s, e) => SaveImage());
            AddIcon("pin", "钉图（把截图钉在桌面）", (s, e) => NailImage());
            AddIcon("refresh", "重新翻译", (s, e) => Retranslate());
            AddIcon("close", "关闭", (s, e) => Close());

            _barStatus = new Label
            {
                AutoSize = false, Height = 22, ForeColor = Palette.BarSub, AutoEllipsis = true,
                Font = new Font("Microsoft YaHei UI", 9f), BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleRight
            };
            _bar.Controls.Add(_barStatus);

            _pill = new RoundPanel { Size = new Size(116, 32), Radius = 16, Fill = Palette.Accent, NoEdge = true, Cursor = Cursors.Hand };
            _pill.Controls.Add(new IconGlyph { Kind = "extract", IconColor = Color.White, Size = new Size(20, 20), Location = new Point(11, 6) });
            _pill.Controls.Add(new Label
            {
                Text = "提取文本", Location = new Point(35, 0), Size = new Size(78, 32), TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold), ForeColor = Color.White, BackColor = Color.Transparent
            });
            _pill.WireClickThrough((s, e) =>
            {
                var m = new ContextMenuStrip();
                m.Items.Add("提取原文", null, (s2, e2) => CopyText(_srcText, "原文"));
                m.Items.Add("提取译文", null, (s2, e2) => CopyText(_dstText, "译文"));
                m.Show(_pill, new Point(0, -64));
            });
            _bar.Controls.Add(_pill);

            _badge = new BadgeBtn();
            _badge.Click += (s, e) => Close();
            Controls.Add(_badge);
            _badge.BringToFront();

            _ccNote = new Label
            {
                Visible = false, AutoSize = false, BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(194, 108, 0), Font = new Font("Microsoft YaHei UI", 9.5f)
            };
            Controls.Add(_ccNote);

            MouseDown += DragForm;
            _dstCard.MouseDown += DragForm;
            _srcCard.MouseDown += DragForm;
            _bar.MouseDown += DragForm;

            UpdateLangLabels();
            LayoutShot();
        }

        private RoundPanel BarChip(out Label lbl)
        {
            var chip = new RoundPanel { Size = new Size(120, 30), Radius = 8, Fill = Palette.BarBtn, NoEdge = true, Cursor = Cursors.Hand };
            lbl = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 9.5f), ForeColor = Color.White, BackColor = Color.Transparent
            };
            chip.Controls.Add(lbl);
            var chev = new IconGlyph { Kind = "chevron", Size = new Size(16, 16), Location = new Point(120 - 22, 7), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            chip.Controls.Add(chev);
            chev.BringToFront();
            return chip;
        }

        private void AddIcon(string kind, string tip, EventHandler handler)
        {
            var p = new IconBtn { Kind = kind, Size = new Size(38, 38) };
            p.Click += handler;
            _tips.SetToolTip(p, tip);
            _icons.Add(p);
            _bar.Controls.Add(p);
        }

        private void DragForm(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, 0x2, 0);
        }

        private Control BarChild(string name)
        {
            foreach (Control c in _bar.Controls) if (c.Name == name) return c;
            return null;
        }

        private void LayoutShot()
        {
            if (!_shot || _bar == null || _dstCard == null) return;
            int W = ClientSize.Width, H = ClientSize.Height;
            if (W <= 0 || H <= 0) return;
            bool narrow = W < 1080;
            int barH = narrow ? 92 : 54;
            // v1.5.1 事项二/三：下部只留原文卡；图条高度按"尽量放大"政策算（给原文卡留底 110，关掉原文卡则全给图）
            int reserve = _shotCompare ? (_srcNeedFit > 0 ? Math.Max(64, Math.Min(160, _srcNeedFit)) : 110) : 0;
            int maxStripH = Math.Max(84, H - 20 - reserve - 14 - barH - 20);
            int stripH = StripHeightFor(W, maxStripH);
            if (_imgStrip != null && _comboTran != null) LayoutStrip(W, maxStripH);
            int top = 20 + stripH;
            // v1.8.0：交叉核对说明占位（底栏上方，原文卡相应让高）
            int noteH = 0;
            if (_ccNote != null && _ccNote.Visible && !string.IsNullOrEmpty(_ccNote.Text))
            {
                int nw = Math.Max(60, W - 44);
                noteH = TextRenderer.MeasureText(_ccNote.Text, _ccNote.Font, new Size(nw, int.MaxValue), TextFormatFlags.WordBreak).Height + 4;
                _ccNote.Bounds = new Rectangle(22, H - 20 - barH - 8 - noteH, nw, noteH);
                _ccNote.BringToFront();
            }
            int contentH = Math.Max(0, H - top - barH - 14 - 20 - (noteH > 0 ? noteH + 8 : 0));
            _dstCard.Visible = false; // 事项二：截图模式不再显示译文卡（译文在译图上，文本仍可经"提取文本"复制）
            if (_shotCompare)
            {
                _srcCard.Visible = true;
                _srcCard.Bounds = new Rectangle(22, top, W - 44, Math.Max(40, contentH));
                var srcInner = new Rectangle(28, 24, Math.Max(60, _srcCard.Width - 56), Math.Max(30, _srcCard.Height - 48));
                _sTxtSrc.Bounds = srcInner;
                _sSrcScroll.Bounds = srcInner;
                if (_sSrcSelect != null) _sSrcSelect.Bounds = new Rectangle(0, 0, _sSrcScroll.Width, _sSrcScroll.Height);
                _sWtSrc.LayoutInScrollPanel(_sSrcScroll);
            }
            else _srcCard.Visible = false;
            _bar.Bounds = new Rectangle(22, H - 20 - barH, W - 44, barH);
            LayoutBar(narrow);
            _badge.Location = new Point(W - 40, 8);
            // v1.5.1 事项一：图条是后建的、曾把徽章压到下层只露一截弧——每次布局后把徽章重提最上层（根因是 z 序，不是位置）
            _badge.BringToFront();
            Region = new Region(RoundPanel.PathFor(new Rectangle(0, 0, W, H), 16));
        }

        private int LayoutIconRow(int x, int y)
        {
            foreach (var btn in _icons)
            {
                btn.Location = new Point(x, y);
                x += btn.Width + 6;
            }
            return x - 6; // 最后一个按钮的右缘
        }

        private void LayoutBar(bool narrow)
        {
            int barW = _bar.Width;
            var swap = BarChild("swapLbl");
            var sep = BarChild("sepLbl");
            var cmp = BarChild("cmpLbl");
            if (narrow)
            {
                _chipSrc.Bounds = new Rectangle(16, 8, 138, 30);
                if (swap != null) swap.Location = new Point(162, 12);
                _chipDst.Bounds = new Rectangle(190, 8, 118, 30);
                if (sep != null) sep.Location = new Point(320, 13);
                if (cmp != null) cmp.Location = new Point(338, 13);
                _tglCompare.Location = new Point(374, 13);
                int endX = LayoutIconRow(16, 52);
                int sx = endX + 8;
                if (barW < 640)
                {
                    // 很窄时胶囊下到第二行右侧，避免压住对照开关；状态文字过长自动省略
                    _pill.Location = new Point(barW - 16 - 116, 50);
                    _barStatus.Bounds = new Rectangle(sx, 56, Math.Max(30, barW - 16 - 116 - 8 - sx), 22);
                }
                else
                {
                    _pill.Location = new Point(barW - 16 - 116, 6);
                    _barStatus.Bounds = new Rectangle(sx, 56, Math.Max(40, barW - 16 - sx), 22);
                }
            }
            else
            {
                _chipSrc.Bounds = new Rectangle(16, 12, 138, 30);
                if (swap != null) swap.Location = new Point(162, 16);
                _chipDst.Bounds = new Rectangle(190, 12, 118, 30);
                if (sep != null) sep.Location = new Point(320, 17);
                if (cmp != null) cmp.Location = new Point(338, 17);
                _tglCompare.Location = new Point(374, 17);
                int endX = LayoutIconRow(420, 12);
                _pill.Location = new Point(barW - 16 - 116, 11);
                int sx = endX + 8;
                _barStatus.Bounds = new Rectangle(sx, 16, Math.Max(40, barW - 16 - 116 - 8 - sx), 22);
            }
        }

        /// <summary>
        /// v1.5.1 尺寸政策：截图翻译尽量放大——窗口宽用到屏幕工作区为界、图优先原尺寸 1:1 显示；
        /// 高度 = 图条（自然高）＋原文卡（文字量，仅开启时）＋底栏，整体不超过屏幕高-16。
        /// </summary>
        public void FitToContent()
        {
            if (!_shot) return;
            try
            {
                var screen = Screen.FromControl(this).WorkingArea;
                int maxW = screen.Width - 16, maxH = screen.Height - 16;
                int wantW = ClientSize.Width;
                if (_image != null) wantW = Math.Max(wantW, _image.Width + 44);
                if (_comboTran != null) wantW = Math.Max(wantW, IsSingleView() ? _comboTran.Width + 44 : _comboTran.Width * 2 + 56);
                wantW = Math.Min(wantW, maxW);

                Func<string, Font, int, int> measure = (t, f, w2) =>
                {
                    if (string.IsNullOrEmpty(t)) return f.Height + 10;
                    var sz = TextRenderer.MeasureText(t, f, new Size(Math.Max(120, w2), int.MaxValue),
                        TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                    return sz.Height + 10;
                };

                var candidates = new List<int>();
                foreach (int c in new[] { wantW, 760, 940, 1120, 1320, maxW })
                {
                    int cc = Math.Max(420, Math.Min(c, maxW));
                    if (!candidates.Contains(cc)) candidates.Add(cc);
                }
                // 顺序：先试意向宽（图 1:1 所需宽），再逐档加宽；判据＝总高不超屏且不显瘦高（舒适比），都不满足才退回"只求放得下"
                var ordered = new List<int> { wantW };
                candidates.Sort();
                foreach (int c in candidates) if (c != wantW) ordered.Add(c);
                int chosenW = candidates[candidates.Count - 1], chosenH = maxH;
                bool fitted = false;
                foreach (int w in ordered)
                {
                    int bH = w < 1080 ? 92 : 54;
                    int srcNeed = _shotCompare ? measure(_srcText, _sTxtSrc.Font, w - 44 - 56) + 48 : 0;
                    int totalH = 20 + StripHeightFor(w, int.MaxValue) + srcNeed + 14 + bH + 20;
                    if (totalH <= maxH && totalH <= w * 0.9) { chosenW = w; chosenH = totalH; fitted = true; break; }
                }
                if (!fitted)
                {
                    foreach (int w in ordered)
                    {
                        int bH = w < 1080 ? 92 : 54;
                        int srcNeed = _shotCompare ? measure(_srcText, _sTxtSrc.Font, w - 44 - 56) + 48 : 0;
                        int totalH = 20 + StripHeightFor(w, int.MaxValue) + srcNeed + 14 + bH + 20;
                        if (totalH <= maxH) { chosenW = w; chosenH = totalH; fitted = true; break; }
                    }
                }
                if (!fitted)
                {
                    // 最宽档仍放不下：用最宽＋满高，布局时图条按剩余高度等比缩（原文卡保底、内部滚动）
                    chosenW = candidates[candidates.Count - 1];
                    chosenH = maxH;
                }
                _srcNeedFit = _shotCompare ? measure(_srcText, _sTxtSrc.Font, chosenW - 44 - 56) + 48 : 0;
                ClientSize = new Size(chosenW, Math.Max(250, chosenH));
                if (_hasSel) RepositionForSelection();
                else
                {
                    int x = Math.Max(screen.Left, Math.Min(Location.X, screen.Right - Width));
                    int y = Math.Max(screen.Top, Math.Min(Location.Y, screen.Bottom - Height));
                    Location = new Point(x, y);
                }
                LayoutShot();
            }
            catch { }
        }

        // ---------------- 共用逻辑 ----------------

        private void DoSwap()
        {
            var t = _srcSel;
            _srcSel = _dstSel == "auto" ? "auto" : _dstSel;
            _dstSel = t == "auto" ? "auto" : t;
            UpdateLangLabels();
            Retranslate();
        }

        private void UpdateLangLabels()
        {
            string srcTxt = _srcSel == "auto"
                ? (string.IsNullOrEmpty(_lastSrcLang) ? "自动检测" : "检测到" + TranslationEngine.LangNameOf(_lastSrcLang))
                : TranslationEngine.LangNameOf(_srcSel);
            string dstTxt = _dstSel == "auto" ? "自动" : TranslationEngine.LangNameOf(_dstSel);
            if (_shot)
            {
                if (_chipSrcLbl != null) _chipSrcLbl.Text = srcTxt;
                if (_chipDstLbl != null) _chipDstLbl.Text = dstTxt;
            }
            else
            {
                if (_btnSrc != null) _btnSrc.Text = srcTxt + " ▾";
                if (_btnDst != null) _btnDst.Text = dstTxt + " ▾";
            }
        }

        private void ShowLangMenu(Control anchor, bool isSrc)
        {
            var menu = new ContextMenuStrip();
            if (isSrc)
            {
                var auto = new ToolStripMenuItem("自动检测") { Checked = _srcSel == "auto" };
                auto.Click += (s, e) => { _srcSel = "auto"; _lastSrcLang = ""; UpdateLangLabels(); Retranslate(); };
                menu.Items.Add(auto);
            }
            else
            {
                var auto = new ToolStripMenuItem("自动（中英互译）") { Checked = _dstSel == "auto" };
                auto.Click += (s, e) => { _dstSel = "auto"; UpdateLangLabels(); Retranslate(); };
                menu.Items.Add(auto);
            }
            foreach (var code in TranslationEngine.LangCodes)
            {
                string c = code;
                var item = new ToolStripMenuItem(TranslationEngine.LangNameOf(c))
                {
                    Checked = isSrc ? _srcSel == c : _dstSel == c
                };
                item.Click += (s, e) =>
                {
                    if (isSrc) _srcSel = c; else _dstSel = c;
                    UpdateLangLabels();
                    Retranslate();
                };
                menu.Items.Add(item);
            }
            menu.Show(anchor, new Point(0, anchor.Height));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SkinFx.Apply(this);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_shot) LayoutShot();
            else RelayoutCards();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!_shot)
            {
                using (var pen = new Pen(Color.FromArgb(220, 220, 220), 1))
                    e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Close();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            // 文字小窗点外部自动关；截图结果窗不自动关（事项三：可对照阅读）
            if (!_shot) Close();
        }

        private TextBox DstBox() { return _shot ? _sTxtDst : _txtDst; }
        private TextBox SrcBox() { return _shot ? _sTxtSrc : _txtSrc; }

        /// <summary>诊断用：设置后状态栏固定显示此文本（如回退原因），优先于流程内的状态刷新。</summary>
        public string StatusOverride;

        private void SetStatus(string text)
        {
            if (!string.IsNullOrEmpty(StatusOverride)) text = StatusOverride;
            if (_shot) { if (_barStatus != null) _barStatus.Text = text; }
            else if (_lblStatus != null) _lblStatus.Text = text;
        }

        // ---------------- v1.3.4 双呈现：双图条 ----------------

        /// <summary>v1.5.2 事项 A：并排 1:1 所需窗口宽超过选区所在屏幕工作区宽 → 单显（一张图独占整行）。</summary>
        private bool IsSingleView()
        {
            if (_comboTran == null) return false;
            var wa = _hasSel ? Screen.FromRectangle(_selRect).WorkingArea : Screen.PrimaryScreen.WorkingArea;
            return _comboTran.Width * 2 + 56 > wa.Width;
        }

        /// <summary>v1.5.1 事项三：双图显示尺寸——宽由窗口宽与原图宽共同决定（不超原尺寸 1:1），高超可用高时等比缩。
        /// v1.5.2 事项 A：单显时一张图用满整行宽，多数情况可恢复 1:1。</summary>
        private void StripSizeFor(int clientW, int maxH, out int iw, out int ih)
        {
            int availW = clientW - 44;
            iw = IsSingleView()
                ? Math.Min(_comboTran.Width, availW)
                : Math.Min(_comboTran.Width, (availW - 12) / 2);
            if (iw < 40) iw = 40;
            ih = (int)(iw * (double)_comboTran.Height / _comboTran.Width);
            int maxIh = maxH - 64;
            if (maxIh >= 40 && ih > maxIh)
            {
                ih = maxIh;
                iw = (int)(ih * (double)_comboTran.Width / _comboTran.Height);
            }
        }

        private int StripHeightFor(int clientW, int maxH)
        {
            if (_comboTran == null) return 0;
            int iw, ih;
            StripSizeFor(clientW, maxH, out iw, out ih);
            return 20 + ih + 44;
        }

        private void BuildStripIfNeeded()
        {
            if (_imgStrip != null) return;
            _imgStrip = new Panel { BackColor = BackColor, Visible = false };
            _lblOrigCap = Ui.Lbl("原截图", 0, 0, 9.5f, Palette.Sub);
            _lblTranCap = Ui.Lbl("译图", 0, 0, 9.5f, Palette.Sub);
            _picOrig = new PictureBox { SizeMode = PictureBoxSizeMode.StretchImage, BorderStyle = BorderStyle.FixedSingle };
            _picTran = new PictureBox { SizeMode = PictureBoxSizeMode.StretchImage, BorderStyle = BorderStyle.FixedSingle };
            var saveBtn = new RoundPanel { Size = new Size(118, 32), Radius = 8, Fill = IconKit.BtnFill, NoEdge = true, Cursor = Cursors.Hand };
            saveBtn.Controls.Add(new IconGlyph { Kind = "saveimg", Size = new Size(20, 20), Location = new Point(10, 6) });
            var saveLbl = new Label
            {
                Text = "保存译图", Location = new Point(36, 0), Size = new Size(80, 32), TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 10f), ForeColor = Color.White, BackColor = Color.Transparent
            };
            saveBtn.Controls.Add(saveLbl);
            saveBtn.WireClickThrough((s, e) => SaveTranslatedImage());
            saveBtn.Name = "comboSave";
            var copyBtn = new RoundPanel { Size = new Size(118, 32), Radius = 8, Fill = IconKit.BtnFill, NoEdge = true, Cursor = Cursors.Hand };
            copyBtn.Controls.Add(new IconGlyph { Kind = "copy", Size = new Size(20, 20), Location = new Point(10, 6) });
            var copyLbl = new Label
            {
                Text = "复制译图", Location = new Point(36, 0), Size = new Size(80, 32), TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 10f), ForeColor = Color.White, BackColor = Color.Transparent
            };
            copyBtn.Controls.Add(copyLbl);
            copyBtn.WireClickThrough((s, e) => { if (_comboTran != null) { try { Clipboard.SetImage(_comboTran); SetStatus("译图已复制"); } catch { } } });
            copyBtn.Name = "comboCopy";
            _lblImgNote = new Label
            {
                AutoSize = false, Size = new Size(300, 20), TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Microsoft YaHei UI", 9f), ForeColor = Palette.Sub, BackColor = Color.Transparent
            };
            _imgStrip.Controls.AddRange(new Control[] { _lblOrigCap, _lblTranCap, _picOrig, _picTran, saveBtn, copyBtn, _lblImgNote });
            Controls.Add(_imgStrip);
            _imgStrip.BringToFront();
            // v1.5.2 事项 A：单显模式下左键点图互换原图/译图
            _picOrig.Click += (s, e) => ToggleSingleView();
            _picTran.Click += (s, e) => ToggleSingleView();
        }

        private void ToggleSingleView()
        {
            if (!IsSingleView()) return;
            _showTran = !_showTran;
            LayoutShot();
        }

        private void LayoutStrip(int W, int maxH)
        {
            int availW = W - 44;
            int iw, ih;
            StripSizeFor(W, maxH, out iw, out ih);
            _imgStrip.Bounds = new Rectangle(22, 12, availW, 20 + ih + 44);
            if (IsSingleView())
            {
                // v1.5.2 事项 A：只显一张（默认译图），点图互换；存图/复制仍作用于译图原尺寸
                int x0 = (availW - iw) / 2;
                var vis = _showTran ? _picTran : _picOrig;
                var hid = _showTran ? _picOrig : _picTran;
                vis.Visible = true;
                vis.Bounds = new Rectangle(x0, 20, iw, ih);
                vis.Cursor = Cursors.Hand;
                hid.Visible = false;
                _lblOrigCap.Visible = false;
                _lblTranCap.Visible = true;
                _lblTranCap.Text = _showTran ? "译图 · 点击图片切换原图" : "原截图 · 点击图片切换译图";
                _lblTranCap.Location = new Point(x0, 0);
                foreach (Control c in _imgStrip.Controls)
                {
                    if (c.Name == "comboSave") c.Location = new Point(x0, 26 + ih);
                    if (c.Name == "comboCopy") c.Location = new Point(x0 + 128, 26 + ih);
                }
                _lblImgNote.Text = "点击图片可切换原图/译图 · 原图 " + _comboTran.Width + "×" + _comboTran.Height + " · 存图为原尺寸";
            }
            else
            {
                int pairW = iw * 2 + 12;
                int x0 = (availW - pairW) / 2;
                _picOrig.Visible = true;
                _picTran.Visible = true;
                _picOrig.Cursor = Cursors.Default;
                _picTran.Cursor = Cursors.Default;
                _lblOrigCap.Visible = true;
                _lblTranCap.Visible = true;
                _lblOrigCap.Text = "原截图";
                _lblTranCap.Text = "译图";
                _lblOrigCap.Location = new Point(x0, 0);
                _lblTranCap.Location = new Point(x0 + iw + 12, 0);
                _picOrig.Bounds = new Rectangle(x0, 20, iw, ih);
                _picTran.Bounds = new Rectangle(x0 + iw + 12, 20, iw, ih);
                foreach (Control c in _imgStrip.Controls)
                {
                    if (c.Name == "comboSave") c.Location = new Point(x0 + iw + 12, 26 + ih);
                    if (c.Name == "comboCopy") c.Location = new Point(x0 + iw + 12 + 128, 26 + ih);
                }
                _lblImgNote.Text = "原图 " + _comboTran.Width + "×" + _comboTran.Height + " · 存图为原尺寸";
            }
            _lblImgNote.Location = new Point(availW - 300, 32 + ih);
        }

        private void SaveTranslatedImage()
        {
            if (_comboTran == null) return;
            using (var dlg = new SaveFileDialog
            {
                Filter = "PNG 图片|*.png",
                FileName = "译图-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png"
            })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _comboTran.Save(dlg.FileName, System.Drawing.Imaging.ImageFormat.Png);
                    SetStatus("译图已保存（原尺寸）");
                }
            }
        }

        /// <summary>v1.3.4：灌入双图（原截图＋译图），与文字卡同窗双呈现。</summary>
        public void SetComboImages(Bitmap orig, Bitmap tran)
        {
            _comboOrig = (Bitmap)orig.Clone();
            var old = _comboTran;
            _comboTran = (Bitmap)tran.Clone();
            if (old != null) old.Dispose();
            BuildStripIfNeeded();
            _picOrig.Image = _comboOrig;
            _picTran.Image = _comboTran;
            _showTran = true; // v1.5.2 事项 A：单显默认先显译图
            _lblImgNote.Text = "原图 " + tran.Width + "×" + tran.Height + " · 存图为原尺寸";
            _imgStrip.Visible = true;
            FitToContent();
        }

        /// <summary>v1.3.4：重翻后只换译图（文字由 SetPrecomputed 同步灌入）。</summary>
        public void SetComboTranslation(Bitmap tran)
        {
            var old = _comboTran;
            _comboTran = (Bitmap)tran.Clone();
            if (_picTran != null) _picTran.Image = _comboTran;
            if (old != null) old.Dispose();
            FitToContent();
        }

        /// <summary>v1.3.0：原位覆盖译图窗点"对照"时直接灌入已算好的原文/译文，不再重复识别翻译。</summary>
        public void SetPrecomputed(string src, string dst, string srcLang, string dstLang)
        {
            _srcText = src ?? "";
            _dstText = dst ?? "";
            _lastSrcLang = srcLang ?? "";
            _lastDstLang = dstLang ?? "";
            SrcBox().Text = _srcText;
            DstBox().Text = _dstText;
            ApplyWordMode();
            UpdateLangLabels();
            SetStatus("识别原文（来自原位覆盖译图）");
            FitToContent();
        }

        /// <summary>窗体显示后调用：图片模式先 OCR 再翻译；文字模式直接翻译。</summary>
        public async void StartWork()
        {
            if (_image != null)
            {
                SetStatus("正在识别文字…");
                try
                {
                    _srcText = await Task.Run(() => OcrService.Recognize(_image));
                    SrcBox().Text = _srcText;
                    // 先按识别文本的实际语种预判原文侧点词（译文出来后 ApplyWordMode 会用权威语种复核）
                    _lastSrcLang = TranslationEngine.DetectSource(_srcText);
                    ApplyWordMode();
                }
                catch (Exception ex)
                {
                    DstBox().Text = "识别失败：" + ex.Message;
                    SetStatus("OCR 失败");
                    FitToContent();
                    return;
                }
                if (string.IsNullOrWhiteSpace(_srcText))
                {
                    DstBox().Text = "没有识别到文字，请框选清晰一些的区域重试。";
                    SetStatus("未识别到文字");
                    FitToContent();
                    return;
                }
            }
            await TranslateCurrent();
        }

        private async void Retranslate()
        {
            if (string.IsNullOrWhiteSpace(_srcText) || _busy) return;
            if (_comboTran != null && ComboRetranslate != null)
            {
                _busy = true;
                try
                {
                    string sl = _srcSel == "auto" ? _lastSrcLang : _srcSel;
                    string dl = _dstSel == "auto" ? _lastDstLang : _dstSel;
                    await ComboRetranslate(sl, dl);
                }
                catch (Exception ex) { SetStatus("重翻失败：" + ex.Message); }
                finally { _busy = false; }
                return;
            }
            await TranslateCurrent();
        }

        private string EffectivePrefer(out bool aiNoKey)
        {
            aiNoKey = false;
            string p = _store != null ? EngineCatalog.ResolvePrefer(_store.Settings) : "auto";
            if (p == "glm" && (_store == null || string.IsNullOrEmpty(_store.Settings.GlmApiKey)))
            {
                aiNoKey = true;
                return "auto";
            }
            return p;
        }

        /// <summary>v1.8.0：交叉核对存疑说明（截图对照模式贴在底栏上方；文字模式不占位、走状态栏摘要）。</summary>
        public void SetCrossCheckNote(string note)
        {
            if (_ccNote == null) return;
            _ccNote.Text = note ?? "";
            _ccNote.Visible = _shot && !string.IsNullOrEmpty(note);
            if (_shot) LayoutShot();
        }

        private async Task TranslateCurrent()
        {
            if (_busy) return;
            _busy = true;
            SetStatus("翻译中…");
            try
            {
                bool aiNoKey;
                string prefer = EffectivePrefer(out aiNoKey);
                var r = await _engine.Translate(_srcText, _srcSel, _dstSel, prefer);
                _dstText = r.Text;
                DstBox().Text = r.Text;
                _lastSrcLang = r.SrcLang;
                _lastDstLang = r.DstLang;
                ApplyWordMode();
                UpdateLangLabels();
                string status = aiNoKey
                    ? "未填 GLM Key，已用" + r.Provider + "翻译"
                    : r.Provider + " · " + TranslationEngine.LangNameOf(r.SrcLang) + " → " + TranslationEngine.LangNameOf(r.DstLang);
                SetStatus(status);
                if (_store != null)
                {
                    _store.AddHistory(new Entry
                    {
                        Src = _srcText, Dst = r.Text, SrcLang = r.SrcLang, DstLang = r.DstLang,
                        Provider = r.Provider, Time = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds
                    });
                }
                FitToContent();
                // v1.8.0 交叉核对（文字模式：状态栏给摘要，明细口径与截图模式一致）
                if (_store != null && _store.Settings.CrossCheckEnabled)
                {
                    var eff = _engine.ExtraSettings ?? SyncService.EffectiveCreds(_store.Settings);
                    var cc = await CrossCheckRunner.Run(_engine, eff, _srcText, r.Text, r.SrcLang, r.DstLang, prefer, _store.Settings.CrossCheckBack);
                    if (!IsDisposed) SetStatus(status + " · " + cc.Summary);
                }
            }
            catch (Exception ex)
            {
                DstBox().Text = ex.Message;
                SetStatus("翻译失败");
            }
            finally { _busy = false; }
        }

        internal static string PreferOf(int engineIndex)
        {
            if (engineIndex == 1) return "youdao";
            if (engineIndex == 2) return "bing";
            if (engineIndex == 3) return "glm";
            if (engineIndex == 4) return "gtx";
            if (engineIndex == 5) return "mymemory";
            return "auto";
        }

        private void CopyText(string text, string what)
        {
            if (string.IsNullOrEmpty(text)) return;
            MainForm.SuppressClipboardWatch(1200);
            try { Clipboard.SetText(text); SetStatus("已复制" + what); } catch { }
        }

        private void Favorite()
        {
            if (_store == null || string.IsNullOrWhiteSpace(_dstText)) return;
            _store.AddFavorite(new Entry
            {
                Src = _srcText, Dst = _dstText,
                SrcLang = string.IsNullOrEmpty(_lastSrcLang) ? TranslationEngine.DetectSource(_srcText) : _lastSrcLang,
                DstLang = _dstSel == "auto" ? (string.IsNullOrEmpty(_lastDstLang) ? TranslationEngine.DetectTarget(_srcText) : _lastDstLang) : _dstSel,
                Provider = "", Time = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds
            });
            if (!_shot && _btnFav != null) _btnFav.Text = "★ 已收藏";
            SetStatus("已加入单词本");
        }

        private void SaveImage()
        {
            if (_image == null) return;
            using (var dlg = new SaveFileDialog
            {
                Filter = "PNG 图片|*.png|JPEG 图片|*.jpg",
                FileName = "截图-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png"
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

        private void NailImage()
        {
            if (_image == null) return;
            var nail = new NailForm((Bitmap)_image.Clone());
            var loc = Location;
            var screen = Screen.FromPoint(loc).WorkingArea;
            nail.Location = new Point(
                Math.Max(screen.Left, Math.Min(loc.X + 24, screen.Right - nail.Width)),
                Math.Max(screen.Top, Math.Min(loc.Y + 24, screen.Bottom - nail.Height)));
            nail.Show();
        }

        /// <summary>
        /// 截图弹窗定位（v1.1.2 用户定版）：选区正下方居中；
        /// 下方放不下时翻到选区正上方居中；横向超出屏幕时钳回屏内。
        /// </summary>
        public void PlaceForSelection(Rectangle sel)
        {
            _selRect = sel;
            _hasSel = true;
            RepositionForSelection();
        }

        private void RepositionForSelection()
        {
            if (!_hasSel) return;
            var center = new Point(_selRect.Left + _selRect.Width / 2, _selRect.Top + _selRect.Height / 2);
            var wa = Screen.FromPoint(center).WorkingArea;
            int x = _selRect.Left + (_selRect.Width - Width) / 2;
            x = Math.Max(wa.Left, Math.Min(x, wa.Right - Width));
            int y = _selRect.Bottom + 12;
            if (y + Height > wa.Bottom)
            {
                y = _selRect.Top - Height - 12;
                if (y < wa.Top) y = Math.Max(wa.Top, wa.Bottom - Height);
            }
            Location = new Point(x, y);
        }

        /// <summary>把浮窗摆到锚点附近并钳制在屏幕工作区内（文字小窗用）。</summary>
        public void PlaceNear(Point anchor)
        {
            var screen = Screen.FromPoint(anchor).WorkingArea;
            int x = anchor.X + 12, y = anchor.Y + 12;
            if (x + Width > screen.Right) x = screen.Right - Width;
            if (y + Height > screen.Bottom) y = anchor.Y - Height - 12;
            if (x < screen.Left) x = screen.Left;
            if (y < screen.Top) y = screen.Top;
            Location = new Point(x, y);
        }
    }
}
