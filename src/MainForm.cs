using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WinTranslator
{
    /// <summary>
    /// 主窗口（v1.1 新版 UI）：无边框＋自绘顶栏（☰ 菜单/最小化/最大化/关闭）、
    /// 左导航（首页/单词本/我的翻译/截图翻译＋取词·划词开关）、右栏速览、
    /// 中央四页。功能逻辑与 v1.0 一致：双引擎翻译、截图全链路、热键、剪贴板取词、托盘、悬浮球、迷你窗。
    /// </summary>
    public class MainForm : Form
    {
        private const int WM_HOTKEY = 0x0312;
        private const int WM_CLIPBOARDUPDATE = 0x031D;
        private const int WM_NCHITTEST = 0x0084;
        private const int HK_SHOT = 1;
        private const int HK_MINI = 2;
        private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004;

        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private readonly TranslationEngine _engine = new TranslationEngine();
        private readonly Store _store = new Store();

        private static DateTime _suppressUntil = DateTime.MinValue;
        internal static void SuppressClipboardWatch(int ms) { _suppressUntil = DateTime.Now.AddMilliseconds(ms); }
        private string _lastClipText = "";
        private DateTime _lastClipAt = DateTime.MinValue;

        // 外壳
        private Panel _pageHome, _pageHist;
        private WordbookPanel _pageWord;
        private Panel _navPanel;
        private RoundPanel _navHome, _navWord, _navHist, _navShot, _navQu, _navHua;
        private SettingsForm _settingsForm;
        private bool _mainHiddenForShot, _popupReplacing;
        private OverlayResultForm _overlayForm;
        private InPlaceForm _inPlace;
        private List<Rectangle> _shotProtected;   // v1.7.5：当前截图的符号/头像保护框（合成避让）
        private bool _inPlaceReplacing;
        private bool _overlayReplacing;
        private ToggleSwitch _tglQu, _tglHua;
        private QuickViewPanel _quick;
        private Label _lblStatus;

        // 主页
        private ComboBox _cmbSrc, _cmbDst;
        private SegmentControl _segEngine;
        private List<EngineCatalog.Option> _engineOptions; // v1.8.0：胶囊选项（prefer＋标签），随密钥配置重建
        private string _shotCcNote;                        // v1.8.0：本次截图翻译的交叉核对说明（给组合窗对照卡）
        private Panel _customRow;
        private Label _lblPresetName, _engineLbl;
        private TextBox _txtInput, _txtOutput;
        // v1.3.0 点词全域：主页译文为英文时切换为点词层
        private Panel _outScroll;
        private WordText _outWt;
        private TextBox _outSelect; // v1.7.2：首页英文译文的选取层（拖选召唤、可复制，口径同截图原文）
        private bool _outSelArmed;
        private Point _outSelStart;
        private string _homeDstLang = "";
        private Label _lblCount, _lblProvider;
        private RoundPanel _inputCard, _outputCard, _toolsCard;
        private Label _lnkHist, _lnkClear, _lnkCopy, _lnkSpeak, _lnkFav;
        private RoundPanel _btnGo;

        private readonly System.Collections.Generic.List<Label> _toolZones = new System.Collections.Generic.List<Label>();

        // 我的翻译页
        private TextBox _txtHistSearch;
        private Panel _histHost;
        private RoundPanel _btnClearHist;
        private Label _histFootnote;

        private NotifyIcon _tray;
        private FloatBall _ball;
        private MiniForm _mini;
        private ResultForm _popup;
        private bool _reallyExit;
        private Timer _syncTimer;
        private bool _syncing;
        private bool _shotRegistered, _miniRegistered;
        private string _shotLabel = "", _miniLabel = "";

        private static readonly string[] SrcCodes = { "auto", "zh", "cht", "en", "fr", "ja", "de", "ko", "pt", "es", "ru" };
        private static readonly string[] DstCodes = { "auto", "zh", "cht", "en", "fr", "ja", "de", "ko", "pt", "es", "ru" };

        private string _appliedSkin = "classic";

        // ---------------- 诊断日志（v1.3.6：定位截图翻译真机回退原因） ----------------
        private static readonly object DiagLock = new object();

        internal static void Diag(string msg)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinTranslator");
                Directory.CreateDirectory(dir);
                lock (DiagLock)
                    File.AppendAllText(Path.Combine(dir, "diag.log"),
                        DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        public MainForm()
        {
            OcrService.Log = Diag;
            string td;
            string tess = OcrService.FindTesseract(out td);
            Diag("=== app start v1.3.6 base=" + AppDomain.CurrentDomain.BaseDirectory +
                 " tesseract=" + (tess ?? "NOT FOUND") + " engineIdx=" + _store.Settings.EngineIndex +
                 " glmKey=" + (!string.IsNullOrEmpty(_store.Settings.GlmApiKey)));
            // v1.5.3 字体：先定界面字体/字号档再建任何控件（Ui 工厂方法按主题出字）
            FontTheme.Configure(_store.Settings.UiFontFamily, _store.Settings.UiFontScale);
            // v1.3.0 皮肤：先定皮肤再建任何控件（Palette 全表切换）
            SkinMode.Current = SkinMode.Normalize(_store.Settings.Skin);
            Palette.Apply(SkinMode.Current);
            _appliedSkin = SkinMode.Current;

            Text = "WinTranslator 翻译";
            ClientSize = new Size(1080, 700);
            MinimumSize = new Size(980, 640);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Palette.Bg;
            Font = new Font("Microsoft YaHei UI", 9f);
            Icon = MakeAppIcon();

            BuildTopBar();
            BuildNav();
            BuildQuickView();
            SyncEngineCreds();
            _pageHome = BuildHomePage();
            _pageWord = BuildWordbookPage();
            _pageHist = BuildHistoryPage();
            BuildStatusBar();
            if (_quick != null) _quick.BringToFront();
            ShowPage(0);
            BuildTray();
            ApplySettingsToHome();
            UpdateTopMost();

            LayoutAll();
            if (SkinMode.IsBackdrop || SkinMode.IsFrosted || SkinMode.IsLiquid) ApplyBackdropImages();
            Resize += (s, e) => LayoutAll();
            Shown += OnFirstShown;
            FormClosing += OnFormClosingMain;
            Shown += (s, e) => StartSyncLoops(); // v1.5.3 云同步：启动拉取＋每分钟巡检变动上传
            Shown += (s, e) => GlassMotion.FadeIn(this); // v1.5.0 深色玻璃开窗动效（关闭走托盘逻辑、不淡出）
            HandleCreated += (s, e) => { try { AddClipboardFormatListener(Handle); } catch { } SkinFx.Apply(this); };
        }

        /// <summary>v1.3.0 自带背景/v1.5.0 磨砂皮肤：给导航与各页铺底图（拉伸填充）。frosted 用 FrostArt 暖色磨砂纹理。</summary>
        private void ApplyBackdropImages()
        {
            var img = SkinMode.IsLiquid
                ? LiquidArt.For(Math.Max(400, ClientSize.Width), Math.Max(300, ClientSize.Height))
                : SkinMode.IsFrosted
                ? FrostArt.For(Math.Max(400, ClientSize.Width), Math.Max(300, ClientSize.Height))
                : BackdropArt.For(Math.Max(400, ClientSize.Width), Math.Max(300, ClientSize.Height));
            foreach (var p in new Panel[] { _navPanel, _pageHome, _pageHist })
            {
                if (p == null) continue;
                p.BackgroundImage = img;
                p.BackgroundImageLayout = ImageLayout.Stretch;
            }
            if (_pageWord != null)
            {
                _pageWord.BackgroundImage = img;
                _pageWord.BackgroundImageLayout = ImageLayout.Stretch;
            }
        }

        private void ClearBackdropImages()
        {
            foreach (var p in new Panel[] { _navPanel, _pageHome, _pageHist })
                if (p != null) p.BackgroundImage = null;
            if (_pageWord != null) _pageWord.BackgroundImage = null;
        }

        /// <summary>v1.3.0 皮肤热切换：重映射已建控件的表面色（Palette 槽位旧→新），并处理亚克力/底图进出。</summary>
        internal void ApplySkinLive(string want)
        {
            var slots = new Func<Color[]>[]
            {
                () => new[] { Palette.Bg, Palette.NavBg, Palette.Card, Palette.Border, Palette.SelBg, Palette.PillBg, Palette.Frame, Palette.BarBg, Palette.BarBtn, Palette.BarSub, Palette.Main, Palette.Sub, Palette.Accent, Palette.CardEdge }
            };
            Color[] oldCols = slots[0]();
            string old = _appliedSkin;
            Palette.Apply(want);
            Color[] newCols = slots[0]();
            var map = new System.Collections.Generic.Dictionary<int, Color>();
            for (int i = 0; i < oldCols.Length; i++) map[oldCols[i].ToArgb()] = newCols[i];
            // 前景色专用映射：只跟正文/次要文字槽位。不能用全量 map——经典皮肤 Card=纯白，
            // 与品牌红按钮上的白字同值，全量映射会把白字误改成深色卡面色（v1.5.1 自查实拍发现的既有缺陷）
            var foreMap = new System.Collections.Generic.Dictionary<int, Color>();
            foreMap[oldCols[10].ToArgb()] = newCols[10]; // Main
            foreMap[oldCols[11].ToArgb()] = newCols[11]; // Sub
            foreMap[oldCols[9].ToArgb()] = newCols[9];   // BarSub
            foreMap[oldCols[12].ToArgb()] = newCols[12]; // v1.6.0 Accent（lucide 黑↔其余红，链接/词性等强调字同步）

            Action<Control> walk = null;
            walk = (Control c) =>
            {
                var rp = c as RoundPanel;
                if (rp != null) { Color fill = rp.Fill; if (map.ContainsKey(fill.ToArgb())) { rp.Fill = map[fill.ToArgb()]; rp.Invalidate(); } if ((rp.Tag as string) == "cardedge") rp.Edge = newCols[13]; }
                else if (!(c is TextBox) && !(c is ComboBox) && map.ContainsKey(c.BackColor.ToArgb())) c.BackColor = map[c.BackColor.ToArgb()];
                // 深色玻璃等皮肤的文字色切换：前景色命中旧正文/次要色时同步重映射（文本框除外，其底色另行处理）
                if (!(c is TextBox) && !(c is ComboBox) && foreMap.ContainsKey(c.ForeColor.ToArgb())) c.ForeColor = foreMap[c.ForeColor.ToArgb()];
                // 文本框/点词面：底色与前景成对重映射（底命中色板槽位时才动，避免白底黑字被拆散成白底白字）
                if ((c is TextBox || c is WordText) && map.ContainsKey(c.BackColor.ToArgb()))
                {
                    c.BackColor = map[c.BackColor.ToArgb()];
                    if (foreMap.ContainsKey(c.ForeColor.ToArgb())) c.ForeColor = foreMap[c.ForeColor.ToArgb()];
                }
                foreach (Control child in c.Controls) walk(child);
            };
            walk(this);
            BackColor = Palette.Bg;

            if (want == "acrylic") SkinFx.Apply(this);
            else if (old == "acrylic") { SkinFx.Remove(this); BackColor = Palette.Bg; }
            // 先落 SkinMode 再铺底图：ApplyBackdropImages 按 SkinMode.IsFrosted 选图，
            // 顺序反了会把旧皮肤的底图铺上（深色玻璃初版静态稿串成暖色底图的实测教训）
            SkinMode.Current = want;
            _appliedSkin = want;
            if (want == "backdrop" || want == "frosted" || want == "liquid") ApplyBackdropImages();
            else if (old == "backdrop" || old == "frosted" || old == "liquid") ClearBackdropImages();

            if (_pageWord != null) _pageWord.RefreshAll();
            if (_quick != null) _quick.RefreshAll();
            Invalidate(true);
        }

        // ---------------- 外壳 ----------------

        private static Icon MakeAppIcon()
        {
            // v1.6.3：底色固定品牌红（不再随皮肤变黑）；「译」字按实际墨迹包围盒居中——
            // 原先固定坐标 (5,4) 画字、按字体行框定位，墨迹偏左上，最小化到托盘/任务栏后看着不在圆心（用户反馈②）。
            // 底图保留不释放：Icon.FromHandle 不复制位图数据，释放底图会让图标失效（托盘/任务栏共用此图标）。
            var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.FillEllipse(new SolidBrush(Color.White), 0, 0, 31, 31);
                g.FillEllipse(new SolidBrush(Color.FromArgb(233, 61, 46)), 2, 2, 27, 27);
            }
            using (var tmp = new Bitmap(128, 128))
            using (var g = Graphics.FromImage(tmp))
            using (var f = new Font("Microsoft YaHei UI", 56f, FontStyle.Bold))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                TextRenderer.DrawText(g, "译", f, new Rectangle(0, 0, 128, 128), Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                int minX = 128, minY = 128, maxX = -1, maxY = -1;
                for (int yy = 0; yy < 128; yy++)
                    for (int xx = 0; xx < 128; xx++)
                    {
                        if (tmp.GetPixel(xx, yy).A < 40) continue;
                        if (xx < minX) minX = xx;
                        if (xx > maxX) maxX = xx;
                        if (yy < minY) minY = yy;
                        if (yy > maxY) maxY = yy;
                    }
                if (maxX > minX && maxY > minY)
                {
                    using (var g2 = Graphics.FromImage(bmp))
                    {
                        g2.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        float inkW = maxX - minX + 1, inkH = maxY - minY + 1;
                        float targetH = 27 * 0.62f, scale = targetH / inkH;
                        float dw = inkW * scale, dh = targetH;
                        var dest = new RectangleF(16f - dw / 2f, 16f - dh / 2f, dw, dh);
                        g2.DrawImage(tmp, dest, new RectangleF(minX, minY, inkW, inkH), GraphicsUnit.Pixel);
                    }
                }
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        private void BuildTopBar()
        {
            var bar = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = Palette.Bg };
            Controls.Add(bar);
            bar.MouseDown += DragWindow;
            var name = Ui.Lbl("WinTranslator", 16, 11, 11f, Palette.Accent, true);
            name.MouseDown += DragWindow;
            bar.Controls.Add(name);

            var btnMenu = new IconBtn { Kind = "menu", Ghost = true, Size = new Size(36, 30) };
            btnMenu.Location = new Point(ClientSize.Width - 172, 7);
            btnMenu.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            var menu = new ContextMenuStrip();
            menu.Items.Add("设置", null, (s, e) => OpenSettings());
            menu.Items.Add("迷你窗口", null, (s, e) => ToggleMini());
            menu.Items.Add("截图翻译", null, (s, e) => StartScreenshot(-1));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出程序", null, (s, e) => { _reallyExit = true; Close(); });
            btnMenu.Click += (s, e) => menu.Show(btnMenu, new Point(0, btnMenu.Height));
            bar.Controls.Add(btnMenu);

            var btnMin = WinBtn("min", -128);
            btnMin.Click += (s, e) => WindowState = FormWindowState.Minimized;
            var btnMax = WinBtn("max", -88);
            btnMax.Click += (s, e) => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
            var btnClose = WinBtn("close", -48);
            btnClose.Click += (s, e) => Close();
            bar.Controls.AddRange(new Control[] { btnMin, btnMax, btnClose });
        }

        private static IconBtn WinBtn(string kind, int rightOffset)
        {
            var b = new IconBtn
            {
                Kind = kind, Ghost = true, Size = new Size(36, 30),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            b.Location = new Point(1080 + rightOffset, 7);
            return b;
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, 0x2, 0); // WM_NCLBUTTONDOWN + HTCAPTION
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HK_SHOT) StartScreenshot(-1);
                else if (id == HK_MINI) ToggleMini();
            }
            else if (m.Msg == WM_CLIPBOARDUPDATE)
            {
                OnClipboardChanged();
            }
            else if (m.Msg == WM_NCHITTEST && WindowState == FormWindowState.Normal)
            {
                base.WndProc(ref m);
                var pt = PointToClient(new Point(m.LParam.ToInt32() & 0xFFFF, m.LParam.ToInt32() >> 16));
                const int grip = 6;
                bool l = pt.X <= grip, r = pt.X >= ClientSize.Width - grip;
                bool t = pt.Y <= grip, b = pt.Y >= ClientSize.Height - grip;
                if (t && l) m.Result = (IntPtr)13;       // HTTOPLEFT
                else if (t && r) m.Result = (IntPtr)14;  // HTTOPRIGHT
                else if (b && l) m.Result = (IntPtr)16;  // HTBOTTOMLEFT
                else if (b && r) m.Result = (IntPtr)17;  // HTBOTTOMRIGHT
                else if (l) m.Result = (IntPtr)10;       // HTLEFT
                else if (r) m.Result = (IntPtr)11;       // HTRIGHT
                else if (t) m.Result = (IntPtr)12;       // HTTOP
                else if (b) m.Result = (IntPtr)15;       // HTBOTTOM
                return;
            }
            base.WndProc(ref m);
        }

        private RoundPanel NavItem(int y, string kind, Color glyphBg, string text, bool withSwitch, bool switchOn)
        {
            var item = new RoundPanel
            {
                Location = new Point(8, y), Size = new Size(134, 40), Radius = 10,
                Fill = Palette.NavBg, NoEdge = true, Cursor = Cursors.Hand
            };
            var icon = new RoundPanel { Location = new Point(8, 9), Size = new Size(22, 22), Radius = 6, Fill = glyphBg, NoEdge = true };
            icon.Controls.Add(new IconGlyph { Kind = kind, IconColor = Color.White, Location = new Point(4, 4), Size = new Size(14, 14) });
            item.Controls.Add(icon);
            item.Controls.Add(Ui.Lbl(text, 40, 10, 10.5f, Palette.Main));
            if (withSwitch)
            {
                var sw = new ToggleSwitch(switchOn) { Location = new Point(96, 10) };
                item.Controls.Add(sw);
            }
            return item;
        }

        private void BuildNav()
        {
            var nav = new Panel { Dock = DockStyle.Left, Width = 150, BackColor = Palette.NavBg };
            _navPanel = nav;
            Controls.Add(nav);
            _navHome = NavItem(14, "home", Palette.Accent, "首页", false, false);
            _navWord = NavItem(62, "book", Color.FromArgb(90, 120, 200), "单词本", false, false);
            _navHist = NavItem(110, "history", Color.FromArgb(70, 160, 110), "我的翻译", false, false);
            _navHome.WireClickThrough((s, e) => ShowPage(0));
            _navWord.WireClickThrough((s, e) => ShowPage(1));
            _navHist.WireClickThrough((s, e) => ShowPage(2));
            nav.Controls.AddRange(new Control[] { _navHome, _navWord, _navHist });

            var shot = NavItem(452, "shot", Color.FromArgb(120, 120, 130), "截图翻译", false, false);
            shot.WireClickThrough((s, e) => StartScreenshot(-1));
            nav.Controls.Add(shot);
            _navShot = shot;

            var quRow = NavItem(500, "search", Color.FromArgb(120, 120, 130), "取词", true, _store.Settings.ClipboardListen);
            _tglQu = (ToggleSwitch)quRow.Controls[quRow.Controls.Count - 1];
            _tglQu.Changed += (s, e) => SetClipboardListen(_tglQu.On, true);
            nav.Controls.Add(quRow);
            _navQu = quRow;
            var huaRow = NavItem(548, "extract", Palette.Accent, "划词", true, _store.Settings.ClipboardListen);
            _tglHua = (ToggleSwitch)huaRow.Controls[huaRow.Controls.Count - 1];
            _tglHua.Changed += (s, e) => SetClipboardListen(_tglHua.On, true);
            nav.Controls.Add(huaRow);
            _navHua = huaRow;
        }

        private void SetClipboardListen(bool on, bool fromNav)
        {
            _store.Settings.ClipboardListen = on;
            _store.SaveSettings();
            if (_tglQu != null) _tglQu.On = on;
            if (_tglHua != null) _tglHua.On = on;
            if (fromNav) SetStatus(on ? "取词/划词已开启（复制文字自动弹翻译小窗）" : "取词/划词已关闭");
        }

        private void BuildQuickView()
        {
            // v1.3.4：速览面板按用户要求去掉，主区改整宽（_quick 保持 null，各刷新点均已判空）
            _quick = null;
        }

        private void BuildStatusBar()
        {
            // 状态栏改为独立底栏：页面区高度随之让出 26px，避免页面底部控件被状态栏盖住（Mono/真机一致）。
            var bar = new Panel { Dock = DockStyle.Bottom, Height = 26, BackColor = Palette.Bg };
            Controls.Add(bar);
            bar.SendToBack();
            _lblStatus = new Label
            {
                Text = "就绪", AutoSize = false, Height = 22,
                ForeColor = Palette.Sub, TextAlign = ContentAlignment.MiddleLeft,
                Location = new Point(166, 2), Width = 620, BackColor = Color.Transparent
            };
            bar.Controls.Add(_lblStatus);
        }

        private Panel NewPage()
        {
            var p = new Panel
            {
                Location = new Point(150, 42), Size = new Size(646, 632),
                BackColor = Palette.Bg, Visible = false
            };
            Controls.Add(p);
            p.BringToFront();
            if (_lblStatus != null) _lblStatus.BringToFront();
            return p;
        }

        private void ShowPage(int idx)
        {
            _pageHome.Visible = idx == 0;
            _pageWord.Holder.Visible = idx == 1;
            // 单词本页（内嵌 UserControl）切换可见后强制整页重绘一次，避免 Mono 旧帧残留
            if (idx == 1) _pageWord.Holder.Refresh();
            _pageHist.Visible = idx == 2;
            _navHome.Fill = idx == 0 ? Palette.SelBg : Palette.NavBg;
            _navWord.Fill = idx == 1 ? Palette.SelBg : Palette.NavBg;
            _navHist.Fill = idx == 2 ? Palette.SelBg : Palette.NavBg;
            _navHome.Invalidate(); _navWord.Invalidate(); _navHist.Invalidate();
            if (idx == 1) _pageWord.RefreshAll();
            if (idx == 2) RefreshHistory();
            if (_quick != null) _quick.RefreshAll();
        }

        private void FillHome(Entry entry)
        {
            if (entry == null) return;
            ShowPage(0);
            _txtInput.Text = entry.Src;
            _txtOutput.Text = entry.Dst;
            _homeDstLang = entry.DstLang ?? "";
            ApplyHomeWordMode();
            SetStatus("已填入主页");
        }

        private void ApplyHomeWordMode()
        {
            if (_txtOutput == null || _outScroll == null || _outWt == null) return;
            bool en = _homeDstLang == "en" && !string.IsNullOrEmpty(_txtOutput.Text);
            _outWt.Text = _txtOutput.Text;
            if (_outSelect != null) _outSelect.Visible = false; // v1.7.2：新译文到来先回点词层
            _outScroll.Visible = en;
            _txtOutput.Visible = !en;
            if (en) _outWt.LayoutInScrollPanel(_outScroll);
        }

        // ---------------- 主页 ----------------

        private Panel BuildHomePage()
        {
            var p = NewPage();

            var capsule = new RoundPanel { Location = new Point(16, 12), Size = new Size(200, 36), Radius = 18, Fill = Color.White, Edge = Palette.Border, Cursor = Cursors.Hand };
            _lblPresetName = Ui.Lbl("英汉互译", 18, 8, 10.5f, Palette.Main);
            capsule.Controls.Add(_lblPresetName);
            capsule.Controls.Add(new IconGlyph { Kind = "chevron", IconColor = Palette.Sub, Location = new Point(170, 10), Size = new Size(16, 16) });
            capsule.WireClickThrough((s, e) => ShowPresetMenu(capsule));
            p.Controls.Add(capsule);

            _engineLbl = Ui.Lbl("引擎", 386, 19, 10f, Palette.Sub);
            p.Controls.Add(_engineLbl);
            _engineOptions = EngineCatalog.BuildOptions(_store.Settings);
            var segLabels = new string[_engineOptions.Count];
            for (int i = 0; i < segLabels.Length; i++) segLabels[i] = _engineOptions[i].Label;
            int segSel = EngineCatalog.IndexOfPrefer(_engineOptions, EngineCatalog.ResolvePrefer(_store.Settings));
            _segEngine = new SegmentControl(segLabels, segSel) { Location = new Point(300, 13), Size = new Size(580, 34) };
            _segEngine.AccentSelected = true; // v1.6.5 胶囊全自绘，红底选中丸与文字同出（v1.6.4）
            _segEngine.Changed += (s, e) =>
            {
                int idx = _segEngine.SelectedIndex;
                if (_engineOptions != null && idx >= 0 && idx < _engineOptions.Count)
                {
                    string prefer = _engineOptions[idx].Prefer;
                    _store.Settings.EnginePrefer = prefer;
                    // 旧 EngineIndex 只对基础 6 个有意义，是基础引擎时同步过去（老版本/其他入口的回退映射）
                    int legacy = Array.IndexOf(new[] { "auto", "youdao", "bing", "glm", "gtx", "mymemory" }, prefer);
                    if (legacy >= 0) _store.Settings.EngineIndex = legacy;
                }
                _store.SaveSettings();
            };
            p.Controls.Add(_segEngine);

            _customRow = new Panel { Location = new Point(16, 52), Size = new Size(614, 32), BackColor = Palette.Bg, Visible = false };
            _cmbSrc = LangCombo(SrcCodes, true); _cmbSrc.Location = new Point(0, 3); _cmbSrc.Width = 130;
            var btnSwap = Ui.PillButton("", 140, 2, 40, 28, Color.White, Palette.Main);
            btnSwap.Controls.Add(new IconGlyph { Kind = "swap", IconColor = Palette.Main, Location = new Point(10, 4), Size = new Size(20, 20) });
            btnSwap.WireClickThrough((s, e) =>
            {
                string sc = SrcCodes[_cmbSrc.SelectedIndex], dc = DstCodes[_cmbDst.SelectedIndex];
                SelectComboCode(_cmbSrc, SrcCodes, dc == "auto" ? "en" : dc);
                SelectComboCode(_cmbDst, DstCodes, sc == "auto" ? "zh" : sc);
                _lblPresetName.Text = "自定义";
            });
            _cmbDst = LangCombo(DstCodes, false); _cmbDst.Location = new Point(192, 3); _cmbDst.Width = 130;
            _customRow.Controls.AddRange(new Control[] { _cmbSrc, btnSwap, _cmbDst });
            p.Controls.Add(_customRow);

            _inputCard = Ui.CardAt(16, 96, 614, 150, 16);
            _txtInput = new TextBox
            {
                BorderStyle = BorderStyle.None, Multiline = true, ScrollBars = ScrollBars.Vertical,
                Location = new Point(20, 16), Size = new Size(574, 90), BackColor = Palette.Card,
                Font = new Font("Microsoft YaHei UI", 14f)
            };
            _txtInput.TextChanged += (s, e) => UpdateCount();
            _inputCard.Controls.Add(_txtInput);
            _lblCount = Ui.Lbl("0 字", 20, 116, 9f, Palette.Sub);
            _inputCard.Controls.Add(_lblCount);
            _lnkHist = LinkLbl("历史", 452, 116);
            _lnkHist.Click += (s, e) => ShowPage(2);
            _lnkClear = LinkLbl("清空", 496, 116);
            _lnkClear.Click += (s, e) => { _txtInput.Clear(); _txtOutput.Clear(); _homeDstLang = ""; ApplyHomeWordMode(); };
            _btnGo = Ui.PillButton("翻 译", 540, 108, 62, 28, Palette.Accent, Color.White, true);
            _btnGo.WireClickThrough(async (s, e) => await TranslateHome());
            _inputCard.Controls.AddRange(new Control[] { _lnkHist, _lnkClear, _btnGo });
            p.Controls.Add(_inputCard);

            _outputCard = Ui.CardAt(16, 260, 614, 140, 16);
            _txtOutput = new TextBox
            {
                BorderStyle = BorderStyle.None, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Location = new Point(20, 16), Size = new Size(574, 84), BackColor = Palette.Card,
                Font = new Font("Microsoft YaHei UI", 14f)
            };
            _outputCard.Controls.Add(_txtOutput);
            _outWt = new WordText { BackColor = Palette.Card, ForeColor = Palette.Main, Font = new Font("Microsoft YaHei UI", 14f) };
            _outWt.WordClicked += (word, pt) => ShowDictPopup(word, _outWt.PointToScreen(pt));
            _outScroll = new Panel { BackColor = Palette.Card, AutoScroll = true, Visible = false };
            _outScroll.Controls.Add(_outWt);
            // v1.7.2：点词层上加选取层——拖动超 6px 召唤出可选文本框（Esc/无选区失焦退回），单击仍是点词查词。
            // 与截图原文（ResultForm._sSrcSelect）、单词本译文同一口径，用户三处一个用法。
            _outSelect = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None, BackColor = Palette.Card,
                ForeColor = Palette.Main, Font = new Font("Microsoft YaHei UI", 14f),
                Visible = false
            };
            _outScroll.Controls.Add(_outSelect);
            _outWt.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                _outSelArmed = true;
                _outSelStart = e.Location;
            };
            _outWt.MouseUp += (s, e) =>
            {
                if (!_outSelArmed) return;
                _outSelArmed = false;
                int dx = Math.Abs(e.Location.X - _outSelStart.X), dy = Math.Abs(e.Location.Y - _outSelStart.Y);
                if (dx + dy < 6) return; // 纯单击＝点词查词，不切选取层
                _outSelect.Text = _txtOutput.Text ?? "";
                _outSelect.Bounds = _outWt.Bounds;
                _outSelect.Visible = true;
                _outSelect.BringToFront();
                _outSelect.Focus();
            };
            _outSelect.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { _outSelect.Visible = false; _outWt.Focus(); }
            };
            _outSelect.Leave += (s, e) =>
            {
                if (_outSelect.SelectionLength == 0) _outSelect.Visible = false;
            };
            _outputCard.Controls.Add(_outScroll);
            _lnkCopy = LinkLbl("复制", 20, 108);
            _lnkCopy.Click += (s, e) => { if (_txtOutput.TextLength > 0) { SuppressClipboardWatch(1200); Clipboard.SetText(_txtOutput.Text); SetStatus("译文已复制"); } };
            _lnkSpeak = LinkLbl("朗读", 64, 108);
            _lnkSpeak.Click += (s, e) => Speak(_txtOutput.Text);
            _lnkFav = LinkLbl("收藏", 108, 108);
            _lnkFav.Click += (s, e) => FavoriteHome();
            _lblProvider = Ui.Lbl("", 0, 108, 9f, Palette.Sub);
            _outputCard.Controls.AddRange(new Control[] { _lnkCopy, _lnkSpeak, _lnkFav, _lblProvider });
            p.Controls.Add(_outputCard);

            // v1.2.1 事项三：文档翻译栏已按用户要求整栏取消（原虚线占位框移除，高度让给输入/译文卡）

            _toolsCard = Ui.CardAt(16, 534, 614, 56, 16);
            var t1 = ToolZone(_toolsCard, 0, "截图翻译");
            t1.Click += (s, e) => StartScreenshot(-1);
            var t2 = ToolZone(_toolsCard, 205, "迷你窗口");
            t2.Click += (s, e) => ToggleMini();
            var t3 = ToolZone(_toolsCard, 410, "延时截图");
            t3.Controls.Add(new IconGlyph { Kind = "chevron", IconColor = Palette.Sub, Location = new Point(168, 20), Size = new Size(16, 16) });
            t3.Click += (s, e) =>
            {
                var m = new ContextMenuStrip();
                m.Items.Add("延时 3 秒", null, (s2, e2) => StartScreenshot(3));
                m.Items.Add("延时 5 秒", null, (s2, e2) => StartScreenshot(5));
                m.Show(t3, new Point(0, t3.Height));
            };
            _toolZones.AddRange(new[] { t1, t2, t3 });
            p.Controls.Add(_toolsCard);
            return p;
        }

        private static Label LinkLbl(string text, int x, int y)
        {
            var l = Ui.Lbl(text, x, y, 9.5f, Palette.Sub);
            l.Cursor = Cursors.Hand;
            return l;
        }

        private static Label ToolZone(Control parent, int x, string text)
        {
            var l = new Label
            {
                Text = text, Location = new Point(x, 0), Size = new Size(204, 56),
                TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Microsoft YaHei UI", 11f),
                ForeColor = Palette.Main, BackColor = Color.Transparent, Cursor = Cursors.Hand
            };
            parent.Controls.Add(l);
            return l;
        }

        private void ShowPresetMenu(Control anchor)
        {
            var menu = new ContextMenuStrip();
            var defs = new[]
            {
                new[] { "英汉互译（自动）", "auto", "auto" }, new[] { "英 → 中", "en", "zh" }, new[] { "中 → 英", "zh", "en" },
                new[] { "法 → 中", "fr", "zh" }, new[] { "中 → 法", "zh", "fr" }, new[] { "日 → 中", "ja", "zh" },
                new[] { "中 → 日", "zh", "ja" }, new[] { "德 → 中", "de", "zh" }, new[] { "中 → 德", "zh", "de" },
                new[] { "韩 → 中", "ko", "zh" }, new[] { "中 → 韩", "zh", "ko" }, new[] { "俄 → 中", "ru", "zh" },
                new[] { "中 → 俄", "zh", "ru" }
            };
            foreach (var def in defs)
            {
                string name = def[0], src = def[1], dst = def[2];
                var item = new ToolStripMenuItem(name);
                item.Click += (s, e) =>
                {
                    SelectComboCode(_cmbSrc, SrcCodes, src);
                    SelectComboCode(_cmbDst, DstCodes, dst);
                    _customRow.Visible = false;
                    _lblPresetName.Text = name;
                };
                menu.Items.Add(item);
            }
            menu.Items.Add(new ToolStripSeparator());
            var custom = new ToolStripMenuItem("自定义语言对…");
            custom.Click += (s, e) => { _customRow.Visible = true; _lblPresetName.Text = "自定义"; };
            menu.Items.Add(custom);
            menu.Show(anchor, new Point(0, anchor.Height));
        }

        private static ComboBox LangCombo(string[] codes, bool isSrc)
        {
            var cmb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Microsoft YaHei UI", 9.5f) };
            foreach (var c in codes)
                cmb.Items.Add(c == "auto" ? (isSrc ? "自动检测" : "自动") : TranslationEngine.LangNameOf(c));
            cmb.SelectedIndex = 0;
            return cmb;
        }

        private static void SelectComboCode(ComboBox cmb, string[] codes, string code)
        {
            int i = Array.IndexOf(codes, code);
            if (i >= 0) cmb.SelectedIndex = i;
        }

        private void UpdateCount()
        {
            _lblCount.Text = _txtInput.TextLength + " 字";
            // 输入删空时，下方译文与引擎标注同步消失（2026-10-29 真机反馈：删了原文、译文还留着）
            if (_txtInput.Text.Trim().Length == 0 && _txtOutput != null && _txtOutput.TextLength > 0)
            {
                _txtOutput.Clear();
                _homeDstLang = "";
                ApplyHomeWordMode();
                if (_lblProvider != null) _lblProvider.Text = "";
                SetStatus("就绪");
            }
        }

        private async Task TranslateHome()
        {
            string text = _txtInput.Text.Trim();
            if (text.Length == 0) { SetStatus("先输入要翻译的文字"); return; }
            SetStatus("翻译中…");
            try
            {
                string src = SrcCodes[_cmbSrc.SelectedIndex];
                string dst = DstCodes[_cmbDst.SelectedIndex];
                string prefer = (_engineOptions != null && _segEngine.SelectedIndex >= 0 && _segEngine.SelectedIndex < _engineOptions.Count)
                    ? _engineOptions[_segEngine.SelectedIndex].Prefer
                    : EngineCatalog.ResolvePrefer(_store.Settings);
                bool aiNoKey = false;
                if (prefer == "glm" && string.IsNullOrEmpty(_store.Settings.GlmApiKey)) { prefer = "auto"; aiNoKey = true; }
                var r = await _engine.Translate(text, src, dst, prefer);
                _txtOutput.Text = r.Text;
                _homeDstLang = r.DstLang;
                ApplyHomeWordMode();
                _lblProvider.Text = r.Provider + " · " + TranslationEngine.LangNameOf(r.SrcLang) + " → " + TranslationEngine.LangNameOf(r.DstLang);
                if (_outputCard != null)
                    _lblProvider.Location = new Point(_outputCard.Width - 20 - _lblProvider.PreferredWidth, _outputCard.Height - 34);
                string status = aiNoKey ? "未填 GLM Key，已用" + r.Provider + "翻译" : "完成 · " + r.Provider;
                if (_store.Settings.CrossCheckEnabled)
                {
                    SetStatus("翻译完成，交叉核对中…");
                    var cc = await CrossCheckRunner.Run(_engine, _engine.ExtraSettings, text, r.Text, r.SrcLang, r.DstLang, prefer, _store.Settings.CrossCheckBack);
                    if (cc.Available || cc.Summary.Length > 0) status += " · " + cc.Summary;
                }
                SetStatus(status);
                _store.AddHistory(new Entry
                {
                    Src = text, Dst = r.Text, SrcLang = r.SrcLang, DstLang = r.DstLang,
                    Provider = r.Provider, Time = NowSec()
                });
                if (_quick != null) _quick.RefreshAll();
            }
            catch (Exception ex) { SetStatus(ex.Message); }
        }

        private void FavoriteHome()
        {
            if (_txtInput.Text.Trim().Length == 0 || _txtOutput.Text.Trim().Length == 0)
            { SetStatus("先完成一次翻译再收藏"); return; }
            _store.AddFavorite(new Entry
            {
                Src = _txtInput.Text.Trim(), Dst = _txtOutput.Text.Trim(),
                SrcLang = SrcCodes[_cmbSrc.SelectedIndex] == "auto" ? TranslationEngine.DetectSource(_txtInput.Text) : SrcCodes[_cmbSrc.SelectedIndex],
                DstLang = DstCodes[_cmbDst.SelectedIndex] == "auto" ? TranslationEngine.DetectTarget(_txtInput.Text) : DstCodes[_cmbDst.SelectedIndex],
                Provider = _engine.ActiveProvider == "bing" ? "必应" : (_engine.ActiveProvider == "glm" ? "智谱 GLM" : "有道"),
                Time = NowSec()
            });
            SetStatus("已收藏到单词本");
            if (_quick != null) _quick.RefreshAll();
        }

        private static long NowSec() { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds; }

        // ---------------- 单词本页 ----------------

        private WordbookPanel BuildWordbookPage()
        {
            var holder = NewPage();
            var panel = new WordbookPanel(_store, FillHome, Speak, SetStatus);
            panel.Location = new Point(0, 0);
            panel.Size = new Size(646, 632);
            panel.Holder = holder;
            panel.Engine = _engine;
            panel.WordLookup = (word, pt) => ShowDictPopup(word, pt);
            holder.Controls.Add(panel);
            return panel;
        }

        // v1.2.0 点词查词独立弹窗（单例复用）
        private DictPopupForm _dictPop;

        private void ShowDictPopup(string word, Point pt)
        {
            if (_dictPop == null || _dictPop.IsDisposed)
            {
                _dictPop = new DictPopupForm(_store, SetStatus, () =>
                {
                    if (_pageWord != null) _pageWord.RefreshAll();
                    if (_quick != null) _quick.RefreshAll();
                }, Speak);
            }
            _dictPop.ShowFor(word, pt);
        }

        // ---------------- 我的翻译页 ----------------

        private Panel BuildHistoryPage()
        {
            var p = NewPage();
            p.Controls.Add(Ui.Lbl("我的翻译", 16, 14, 15f, Palette.Main, true));
            var searchCard = new RoundPanel { Location = new Point(16, 52), Size = new Size(300, 34), Radius = 17, Fill = Color.White, Edge = Palette.Border };
            _txtHistSearch = new TextBox
            {
                BorderStyle = BorderStyle.None, Location = new Point(14, 8), Width = 272,
                Font = new Font("Microsoft YaHei UI", 10f), BackColor = Palette.Card
            };
            _txtHistSearch.TextChanged += (s, e) => RefreshHistory();
            searchCard.Controls.Add(_txtHistSearch);
            p.Controls.Add(searchCard);
            _btnClearHist = Ui.PillButton("清空历史", 530, 52, 100, 34, Color.White, Palette.Accent);
            _btnClearHist.WireClickThrough((s, e) =>
            {
                if (MessageBox.Show("确定清空全部历史记录？", "确认", MessageBoxButtons.YesNo) == DialogResult.Yes)
                { _store.ClearHistory(); RefreshHistory(); if (_quick != null) _quick.RefreshAll(); }
            });
            p.Controls.Add(_btnClearHist);
            _histHost = new Panel { Location = new Point(16, 100), Size = new Size(630, 466), BackColor = Palette.Bg, AutoScroll = true };
            p.Controls.Add(_histHost);
            _histFootnote = Ui.Lbl("点击任意一条填回主页 · 历史上限 500 条", 16, 578, 9f, Palette.Sub);
            p.Controls.Add(_histFootnote);
            return p;
        }

        private void RefreshHistory()
        {
            if (_histHost == null) return;
            string q = _txtHistSearch != null ? _txtHistSearch.Text.Trim() : "";
            var rows = new List<Entry>();
            foreach (var h in _store.History)
            {
                if (q.Length > 0 && (h.Src ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0
                    && (h.Dst ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                rows.Add(h);
                if (rows.Count >= 200) break;
            }
            _histHost.Controls.Clear();
            int histCardW = Math.Max(320, _histHost.Width - 18);
            var card = Ui.CardAt(0, 0, histCardW, Math.Max(120, rows.Count * 80 + 20));
            if (rows.Count == 0)
                card.Controls.Add(Ui.Lbl("暂无历史记录。", 22, 22, 10.5f, Palette.Sub));
            for (int i = 0; i < rows.Count; i++)
            {
                var entry = rows[i];
                int y = 14 + i * 80;
                var src = Ui.Lbl(OneLine(entry.Src, 36), 22, y, 11.5f, Palette.Main);
                var dst = Ui.Lbl(OneLine(entry.Dst, 36), 22, y + 28, 11.5f, Palette.Sub);
                var time = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(entry.Time).ToLocalTime();
                string meta = TranslationEngine.LangNameOf(entry.SrcLang) + "→" + TranslationEngine.LangNameOf(entry.DstLang) +
                              " · " + entry.Provider + " · " + time.ToString("MM-dd HH:mm");
                var metaLbl = Ui.Lbl(meta, 0, y + 4, 9f, Palette.Sub);
                metaLbl.Location = new Point(histCardW - 22 - metaLbl.PreferredWidth, y + 4);
                EventHandler click = (s, e) => FillHome(entry);
                src.Click += click; dst.Click += click;
                src.Cursor = Cursors.Hand; dst.Cursor = Cursors.Hand;
                card.Controls.AddRange(new Control[] { src, dst, metaLbl });
            }
            _histHost.Controls.Add(card);
        }

        private static string OneLine(string s, int max)
        {
            s = (s ?? "").Replace("\r", " ").Replace("\n", " ");
            return s.Length > max ? s.Substring(0, max) + "…" : s;
        }

        // ---------------- 设置（独立弹窗，v1.1.1 事项五） ----------------

        private void OpenSettings()
        {
            if (_settingsForm == null || _settingsForm.IsDisposed)
            {
                _settingsForm = new SettingsForm(_store, GetHotkeyStatusText, OnSettingsSaved);
                var wa = Screen.FromControl(this).WorkingArea;
                int x = Visible ? Location.X + (Width - _settingsForm.Width) / 2 : wa.Left + (wa.Width - _settingsForm.Width) / 2;
                int y = Visible ? Location.Y + (Height - _settingsForm.Height) / 2 : wa.Top + (wa.Height - _settingsForm.Height) / 2;
                _settingsForm.Location = new Point(
                    Math.Max(wa.Left, Math.Min(x, wa.Right - _settingsForm.Width)),
                    Math.Max(wa.Top, Math.Min(y, wa.Bottom - _settingsForm.Height)));
            }
            _settingsForm.LoadFromSettings();
            _settingsForm.Show();
            _settingsForm.Activate();
        }

        /// <summary>v1.5.3 字体热生效：主窗整树换字，悬浮球/迷你窗若开着一并换（其余小窗下次打开时按主题出字）。</summary>
        internal void ApplyFontLive()
        {
            var st = _store.Settings;
            FontTheme.Configure(st.UiFontFamily, st.UiFontScale);
            FontTheme.ApplyTo(this);
            if (_ball != null && !_ball.IsDisposed) FontTheme.ApplyTo(_ball);
            if (_mini != null && !_mini.IsDisposed) FontTheme.ApplyTo(_mini);
        }

        private void OnSettingsSaved()
        {
            var st = _store.Settings;
            string wantSkin = SkinMode.Normalize(st.Skin);
            if (wantSkin != _appliedSkin) ApplySkinLive(wantSkin);
            ApplyFontLive();
            SyncEngineCreds();
            ApplyAutoStart(st.AutoStart);
            ApplySettingsToHome();
            RegisterAllHotkeys();
            UpdateTopMost();
            SetClipboardListen(st.ClipboardListen, false);
            SetStatus("设置已保存");
        }

        public string GetHotkeyStatusText()
        {
            return "当前生效：截图 " + (_shotRegistered ? _shotLabel : "（注册失败）") +
                   " · 迷你窗 " + (_miniRegistered ? _miniLabel : "（注册失败）");
        }

        // ---------------- 响应式布局（v1.1.1 事项四：A 案铺满拉伸） ----------------

        private void LayoutAll()
        {
            if (_pageHome == null || _navPanel == null) return;
            int pageW = ClientSize.Width - 150 - 24 - 24;
            int pageH = ClientSize.Height - 42 - 26;
            if (pageW < 420 || pageH < 320) return;
            var pb = new Rectangle(150, 42, pageW, pageH);
            _pageHome.Bounds = pb;
            if (_pageWord != null && _pageWord.Holder != null) _pageWord.Holder.Bounds = pb;
            if (_pageHist != null) _pageHist.Bounds = pb;
            int navH = _navPanel.Height;
            if (_navShot != null) _navShot.Location = new Point(8, navH - 192);
            if (_navQu != null) _navQu.Location = new Point(8, navH - 144);
            if (_navHua != null) _navHua.Location = new Point(8, navH - 96);
            LayoutHome(pageW, pageH);
            if (_pageWord != null) _pageWord.Relayout(pageW, pageH);
            LayoutHist(pageW, pageH);
            if (_quick != null) _quick.RefreshAll();
        }

        private void LayoutHome(int pageW, int pageH)
        {
            if (_inputCard == null) return;
            int cardW = pageW - 48;
            // v1.8.0 单行多引擎（静态稿 20261104-09 定版）：轨道向左拉宽借用顶部空位，
            // 左端固定 300（语种胶囊 16..216＋「引擎」标签约 250..292），右端仍贴右缘
            int segLeft = 300;
            _segEngine.Location = new Point(segLeft, 13);
            _segEngine.Width = Math.Max(280, pageW - 24 - segLeft);
            _engineLbl.Location = new Point(segLeft - 42, 19);
            _customRow.Size = new Size(cardW, 32);
            int toolsY = pageH - 16 - 56;
            int avail = toolsY - 14 - 96;
            int inH = Math.Max(96, avail * 46 / 100);
            int outH = Math.Max(84, avail - inH - 14);
            _inputCard.Bounds = new Rectangle(24, 96, cardW, inH);
            _txtInput.Bounds = new Rectangle(24, 18, cardW - 48, inH - 64);
            _lblCount.Location = new Point(24, inH - 34);
            _lnkHist.Location = new Point(cardW - 182, inH - 34);
            _lnkClear.Location = new Point(cardW - 138, inH - 34);
            _btnGo.Location = new Point(cardW - 82, inH - 44);
            _outputCard.Bounds = new Rectangle(24, 96 + inH + 16, cardW, outH);
            _txtOutput.Bounds = new Rectangle(24, 18, cardW - 48, outH - 64);
            if (_outScroll != null)
            {
                _outScroll.Bounds = _txtOutput.Bounds;
                if (_outScroll.Visible) _outWt.LayoutInScrollPanel(_outScroll);
            }
            _lnkCopy.Location = new Point(24, outH - 34);
            _lnkSpeak.Location = new Point(72, outH - 34);
            _lnkFav.Location = new Point(120, outH - 34);
            _lblProvider.Location = new Point(cardW - 20 - _lblProvider.PreferredWidth, outH - 34);
            _toolsCard.Bounds = new Rectangle(24, toolsY, cardW, 56);
            for (int i = 0; i < _toolZones.Count; i++)
                _toolZones[i].Bounds = new Rectangle(i * cardW / 3, 0, cardW / 3, 56);
        }

        private void LayoutHist(int pageW, int pageH)
        {
            if (_histHost == null) return;
            _histHost.Bounds = new Rectangle(24, 100, pageW - 48, pageH - 148);
            if (_btnClearHist != null) _btnClearHist.Location = new Point(pageW - 24 - 100, 52);
            if (_histFootnote != null) _histFootnote.Location = new Point(24, pageH - 24);
        }

        /// <summary>v1.8.0：把凭据同步进引擎（GLM 明文沿旧例；多引擎凭据用解密后的有效设置副本）。</summary>
        private void SyncEngineCreds()
        {
            _engine.GlmApiKey = _store.Settings.GlmApiKey;
            _engine.ExtraSettings = SyncService.EffectiveCreds(_store.Settings);
        }

        /// <summary>v1.8.0：按当前配置重建引擎胶囊（填了密钥的引擎才出现），选中项按 EnginePrefer 解析。</summary>
        private void RebuildEngineSeg()
        {
            if (_segEngine == null) return;
            _engineOptions = EngineCatalog.BuildOptions(_store.Settings);
            var labels = new string[_engineOptions.Count];
            for (int i = 0; i < labels.Length; i++) labels[i] = _engineOptions[i].Label;
            _segEngine.SetOptions(labels, EngineCatalog.IndexOfPrefer(_engineOptions, EngineCatalog.ResolvePrefer(_store.Settings)));
        }

        private void ApplySettingsToHome()
        {
            var f = new Font("Microsoft YaHei UI", _store.Settings.FontSize);
            _txtInput.Font = f;
            _txtOutput.Font = f;
            if (_outWt != null) _outWt.Font = f;
            SyncEngineCreds();
            RebuildEngineSeg();
            int legacy = _store.Settings.DefaultSrcIndex;
            if (legacy == 1) SelectComboCode(_cmbSrc, SrcCodes, "en");
            else if (legacy == 2) SelectComboCode(_cmbSrc, SrcCodes, "zh");
            if (_store.Settings.BallEnabled) ShowBall(); else if (_ball != null) _ball.Hide();
        }

        private void UpdateTopMost() { TopMost = _store.Settings.MainTopMost; }

        private void ApplyAutoStart(bool on)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key == null) return;
                    if (on) key.SetValue("WinTranslator", "\"" + Application.ExecutablePath + "\"");
                    else key.DeleteValue("WinTranslator", false);
                }
            }
            catch { }
        }

        // ---------------- 托盘/悬浮球/迷你/浮窗 ----------------

        private void BuildTray()
        {
            _tray = new NotifyIcon { Icon = MakeAppIcon(), Text = "WinTranslator", Visible = true };
            var menu = new ContextMenuStrip();
            menu.Items.Add("截图翻译", null, (s, e) => StartScreenshot(-1));
            menu.Items.Add("延时 3 秒截图", null, (s, e) => StartScreenshot(3));
            menu.Items.Add("延时 5 秒截图", null, (s, e) => StartScreenshot(5));
            menu.Items.Add("迷你窗口", null, (s, e) => ToggleMini());
            menu.Items.Add("打开主窗口", null, (s, e) => ShowMain());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, (s, e) => { _reallyExit = true; Close(); });
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (s, e) => ShowMain();
        }

        private void ShowMain()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void ShowBall()
        {
            if (_ball == null)
            {
                _ball = new FloatBall(_store);
                _ball.ScreenshotRequested += () => StartScreenshot(-1);
                _ball.ScreenshotDelayedRequested += d => StartScreenshot(d);
                _ball.MiniRequested += () => ToggleMini();
                _ball.MainRequested += () => ShowMain();
            }
            if (!_ball.Visible) _ball.Show();
        }

        private void ToggleMini()
        {
            if (_mini == null || _mini.IsDisposed)
                _mini = new MiniForm(_engine, _store, ShowMain);
            if (_mini.Visible) _mini.Hide();
            else { _mini.Show(); _mini.Activate(); }
        }

        private void ClosePopup()
        {
            if (_popup != null && !_popup.IsDisposed) { try { _popup.Close(); } catch { } }
            _popup = null;
        }

        private void ShowPopup(string srcText, Bitmap image, Point anchor)
        {
            ShowPopup(srcText, image, anchor, null);
        }

        private void ShowPopup(string srcText, Bitmap image, Point anchor, Rectangle? selRect)
        {
            _popupReplacing = true;
            ClosePopup();
            _popupReplacing = false;
            _popup = new ResultForm(_engine, _store, srcText, image);
            _popup.WordLookup = (word, pt) => ShowDictPopup(word, pt); // v1.2.1 事项一：截图弹窗点词
            _popup.FormClosed += (s, e) =>
            {
                // v1.2.1 事项一：结果弹窗关闭时，查词弹窗一并关（不留悬空小窗）
                if (_dictPop != null && !_dictPop.IsDisposed && _dictPop.Visible) _dictPop.Close();
            };
            if (image != null)
            {
                _popup.FormClosed += (s, e) =>
                {
                    if (_mainHiddenForShot && !_popupReplacing)
                    {
                        _mainHiddenForShot = false; // v1.5.3：截图结果关闭后主界面不再自动弹出（保持最小化，用户从悬浮球/托盘手动调出）
                    }
                };
            }
            if (image != null && selRect.HasValue) _popup.PlaceForSelection(selRect.Value);
            else _popup.PlaceNear(anchor);
            _popup.Show();
            _popup.StartWork();
        }

        // ---------------- 截图流程 ----------------

        public async void StartScreenshot(int delayOverride)
        {
            // v1.7.1：新截图开始前先撤掉上一个截图结果（原位译图窗＋组合窗）——原先只在新结果
            // 生成后才替换，框选期间旧译图窗一直浮在屏幕上、还会被框进新截图里（用户真机反馈配图实证）。
            // 关闭走 replacing 标志，避免触发“结果关闭后”的善后逻辑。
            if (_inPlace != null && !_inPlace.IsDisposed)
            {
                _inPlaceReplacing = true;
                try { _inPlace.Close(); } catch { }
                _inPlaceReplacing = false;
                _inPlace = null;
            }
            _popupReplacing = true;
            ClosePopup();
            _popupReplacing = false;
            int delay = delayOverride >= 0 ? delayOverride : _store.Settings.ScreenshotDelaySec;
            if (delay > 0)
            {
                SetStatus("将在 " + delay + " 秒后截图…");
                await Task.Delay(delay * 1000);
            }
            bool hideUi = _store.Settings.ScreenshotHideUi != false;
            bool mainWas = Visible, ballWas = _ball != null && _ball.Visible;
            bool miniWas = _mini != null && _mini.Visible;
            // 组合窗已在开头关闭，不再有 popupWas 需要隐藏/取消后恢复
            if (hideUi)
            {
                if (mainWas) Hide();
                if (ballWas) _ball.Hide();
                if (miniWas) _mini.Hide();
                await Task.Delay(350);
            }

            var vs = SystemInformation.VirtualScreen;
            Bitmap cropped = null;
            Rectangle selRect = Rectangle.Empty;
            using (var frozen = new Bitmap(vs.Width, vs.Height))
            {
                using (var g = Graphics.FromImage(frozen))
                    g.CopyFromScreen(vs.Left, vs.Top, 0, 0, new Size(vs.Width, vs.Height));
                using (var overlay = new OverlayForm((Bitmap)frozen.Clone()))
                {
                    if (overlay.ShowDialog() == DialogResult.OK)
                    {
                        cropped = overlay.CroppedImage;
                        selRect = overlay.SelectionScreenRect;
                    }
                }
            }

            if (hideUi && ballWas) ShowBall();
            if (cropped == null)
            {
                if (hideUi && mainWas) Show();
                SetStatus("截图已取消");
                return;
            }
            // 事项一：结果出来后主界面保持隐藏，弹窗关闭时再恢复（仅限截图前主窗本来开着）
            if (hideUi && mainWas) _mainHiddenForShot = true;
            // v1.3.0：截图翻译默认改原位覆盖译图（识别带坐标→逐块翻译→盖回原位置）；识别不到块时回退对照弹窗
            StartOverlayShot(cropped, selRect);
        }


        /// <summary>v1.3.0 截图翻译新流程：带坐标识别→定全图语种→逐块翻译→合成译图窗。</summary>
        /// <summary>卡片流式拼接：块内换行与块间分隔按语种处理（拉丁语系补空格、中日韩不补），
        /// 避免对照卡里英文词被换行吞掉粘连（v1.4.0 端到端自检发现）。</summary>
        private static string FlowJoin(IList<string> parts, string lang)
        {
            bool cjk = lang == "zh" || lang == "ja" || lang == "ko";
            var flat = new List<string>();
            foreach (var part in parts)
            {
                string t = (part ?? "").Replace("\r\n", "\n").Replace("\n", cjk ? "" : " ").Trim();
                if (t.Length > 0) flat.Add(t);
            }
            return string.Join(cjk ? "" : " ", flat.ToArray());
        }

        /// <summary>v1.4.0：逐块翻译（长段先拆句再翻、翻完合并——微信做法，防长文超时与错误累积）。</summary>
        private async Task<TranslationResult> TranslateBlockText(string text, string sl, string dl, string prefer)
        {
            string flat = (text ?? "").Replace("\n", " ");
            if (flat.Length <= 110)
                return await _engine.Translate(flat, sl, dl, prefer);
            var parts = SplitSentences(flat);
            if (parts.Count <= 1)
                return await _engine.Translate(flat, sl, dl, prefer);
            var outs = new List<string>();
            TranslationResult last = null;
            foreach (var part in parts)
            {
                if (part.Trim().Length == 0) continue;
                last = await _engine.Translate(part, sl, dl, prefer);
                outs.Add(last.Text);
            }
            bool cjk = dl == "zh" || dl == "ja" || dl == "ko";
            return new TranslationResult
            {
                Text = string.Join(cjk ? "" : " ", outs.ToArray()),
                Provider = last != null ? last.Provider : "",
                SrcLang = sl,
                DstLang = dl
            };
        }

        private static List<string> SplitSentences(string text)
        {
            var parts = new List<string>();
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '。' || c == '！' || c == '？' || c == '；' || c == '.' || c == '!' || c == '?' || c == ';')
                {
                    if (i + 1 - start >= 8)
                    {
                        parts.Add(text.Substring(start, i + 1 - start));
                        start = i + 1;
                    }
                }
            }
            if (start < text.Length) parts.Add(text.Substring(start));
            if (start > 0 && parts.Count > 0 && parts[parts.Count - 1].Trim().Length < 8 && parts.Count > 1)
            {
                string tail = parts[parts.Count - 1];
                parts.RemoveAt(parts.Count - 1);
                parts[parts.Count - 1] += tail;
            }
            return parts;
        }

        /// <summary>
        /// v1.4.0 五步编排：识别→定语种→逐块翻译→合成→组窗。
        /// 容错口径：单块翻译失败只丢那一块；识别/翻译整步失败时组合窗照常打开，
        /// 失败步与原因在窗内明示，不再静默跳回整文弹窗。
        /// </summary>
        private async void StartOverlayShot(Bitmap cropped, Rectangle selRect)
        {
            if (_inPlace != null && !_inPlace.IsDisposed)
            {
                _inPlaceReplacing = true;
                try { _inPlace.Close(); } catch { }
                _inPlaceReplacing = false;
            }
            SetStatus("正在识别文字…");
            Diag("overlay start " + cropped.Width + "x" + cropped.Height);
            // 第 1 步：带坐标识别
            List<OcrBlock> blocks = null;
            string ocrError = null;
            _shotProtected = null;
            _shotCcNote = null;
            try
            {
                blocks = await Task.Run(() =>
                {
                    List<Rectangle> pr;
                    var bs = OcrService.RecognizeBlocks(cropped, out pr);
                    _shotProtected = pr;
                    return bs;
                });
            }
            catch (Exception ex) { blocks = null; ocrError = ex.Message; }
            Diag("overlay blocks=" + (blocks == null ? "null" : blocks.Count.ToString()) + " err=" + (ocrError ?? ""));
            string prefer = EngineCatalog.ResolvePrefer(_store.Settings);
            if (prefer == "glm" && string.IsNullOrEmpty(_store.Settings.GlmApiKey)) prefer = "auto";

            if (blocks == null || blocks.Count == 0)
            {
                string reason = ocrError != null
                    ? "第 1 步识别失败：" + ocrError
                    : "第 1 步识别未取到带坐标的文字块";
                Diag("overlay FAIL-OPEN: " + reason);
                string whole = "";
                try { whole = await Task.Run(() => OcrService.Recognize(cropped)); } catch { }
                ShowComboPopup(cropped, (Bitmap)cropped.Clone(), new List<OcrBlock>(),
                    whole ?? "", "（" + reason + "。下方是整图识别的文本，可复制使用；换个更清晰的区域重试通常能出译图。）",
                    "auto", "auto", prefer, selRect);
                if (_popup != null) _popup.StatusOverride = reason + "（已打开组合窗明示）";
                SetStatus(reason);
                return;
            }

            // 第 2 步：定语种
            string rawSrc = string.Join("\n", blocks.ConvertAll(b => b.Text).ToArray());
            string srcLang = TranslationEngine.DetectSource(rawSrc);
            string dstLang = TranslationEngine.DetectTarget(rawSrc);
            string fullSrc = FlowJoin(blocks.ConvertAll(b => b.Text), srcLang);

            // 第 3 步：v1.5.2 C2 先组段再整段翻译（单段失败只丢一段）
            var regions = OverlayComposer.MergeParagraphs(blocks);
            Diag("overlay regions=" + regions.Count + " (blocks=" + blocks.Count + ")");
            var translations = new List<string>();
            string provider = "";
            int failCount = 0;
            string firstError = null;
            foreach (var block in regions)
            {
                try
                {
                    var r = await TranslateBlockText(block.Text, srcLang, dstLang, prefer);
                    // v1.8.3：空译文计入失败（原先空字符串被当成功，合成不出贴片却走
                    // 成功分支，真机上表现为"识别有字、译图空白"还无任何提示）。
                    if (string.IsNullOrWhiteSpace(r.Text))
                    {
                        translations.Add("");
                        failCount++;
                        if (firstError == null) firstError = "引擎返回空译文";
                        Diag("overlay region empty translation: [" + block.Text + "]");
                    }
                    else
                    {
                        translations.Add(r.Text);
                        provider = r.Provider;
                    }
                }
                catch (Exception ex)
                {
                    translations.Add("");
                    failCount++;
                    if (firstError == null) firstError = ex.Message;
                    Diag("overlay region translate fail: " + ex.Message);
                }
            }

            if (failCount == regions.Count)
            {
                string reason = "第 3 步翻译失败：" + firstError;
                Diag("overlay FAIL-OPEN: " + reason);
                ShowComboPopup(cropped, (Bitmap)cropped.Clone(), blocks,
                    fullSrc, "（" + reason + "。右侧图未翻译；检查网络或在设置里换翻译引擎后点\"重翻\"再试。）",
                    srcLang, dstLang, prefer, selRect);
                if (_popup != null) _popup.StatusOverride = reason + "（已打开组合窗明示）";
                SetStatus(reason);
                return;
            }

            // 第 4 步：合成；第 5 步：组窗（双呈现）
            string fullDst = FlowJoin(translations, dstLang);
            _store.AddHistory(new Entry
            {
                Src = fullSrc, Dst = fullDst, SrcLang = srcLang, DstLang = dstLang,
                Provider = provider, Time = NowSec()
            });
            if (_quick != null) _quick.RefreshAll();
            Diag("overlay translated ok, provider=" + provider + " fails=" + failCount);
            // v1.8.0 交叉核对（开关开时）：第二引擎整段同翻比对，存疑说明随组合窗对照卡展示；核对失败不影响出图
            string ccSummary = null;
            if (_store.Settings.CrossCheckEnabled)
            {
                var cc = await CrossCheckRunner.Run(_engine, _engine.ExtraSettings, fullSrc, fullDst, srcLang, dstLang, prefer, _store.Settings.CrossCheckBack);
                _shotCcNote = cc.Note;
                if (cc.Summary.Length > 0) ccSummary = cc.Summary;
                Diag("crosscheck: " + cc.Summary);
            }
            var composed = OverlayComposer.Compose(cropped, regions, translations, _shotProtected);
            ShowInPlaceFirst(cropped, composed, blocks, fullSrc, fullDst, srcLang, dstLang, prefer, selRect);
            SetStatus((failCount > 0
                ? "完成 · " + regions.Count + " 段中 " + failCount + " 段翻译失败（" + firstError + "），其余已盖图"
                : "完成 · 双呈现（译图＋对照）") + (ccSummary != null ? " · " + ccSummary : ""));
        }

        private void ShowOverlayForm(Bitmap image, Bitmap composed, string fullSrc, string fullDst,
            string srcLang, string dstLang, string statusText, Rectangle selRect)
        {
            if (_overlayForm != null && !_overlayForm.IsDisposed)
            {
                _overlayReplacing = true;
                try { _overlayForm.Close(); } catch { }
                _overlayReplacing = false;
            }
            var form = new OverlayResultForm((Bitmap)image.Clone(), composed, statusText);
            form.OpenCompare = () =>
            {
                _overlayReplacing = true;
                ShowPrecomputedPopup((Bitmap)image.Clone(), fullSrc, fullDst, srcLang, dstLang, selRect);
                form.Close();
                _overlayReplacing = false;
            };
            form.FormClosed += (s, e) =>
            {
                if (_dictPop != null && !_dictPop.IsDisposed && _dictPop.Visible) _dictPop.Close();
                if (_mainHiddenForShot && !_overlayReplacing)
                {
                    _mainHiddenForShot = false; // v1.5.3：截图结果关闭后主界面不再自动弹出（保持最小化，用户从悬浮球/托盘手动调出）
                }
            };
            _overlayForm = form;
            form.PlaceForSelection(selRect);
            form.Show();
        }

        /// <summary>
        /// v1.5.2 C4 原位呈现：译图先按原选区坐标贴回屏幕（学有道），左键点击转组合窗细看；
        /// 直接关掉则恢复主窗。打开组合窗后的生命周期与此前直开组合窗一致。
        /// </summary>
        private void ShowInPlaceFirst(Bitmap cropped, Bitmap composed, List<OcrBlock> blocks, string fullSrc, string fullDst,
            string srcLang, string dstLang, string prefer, Rectangle selRect)
        {
            if (_inPlace != null && !_inPlace.IsDisposed)
            {
                _inPlaceReplacing = true;
                try { _inPlace.Close(); } catch { }
                _inPlaceReplacing = false;
            }
            var form = new InPlaceForm((Bitmap)composed.Clone(), selRect);
            bool switched = false;
            form.SwitchToCombo = () =>
            {
                switched = true;
                ShowComboPopup(cropped, composed, blocks, fullSrc, fullDst, srcLang, dstLang, prefer, selRect);
            };
            form.FormClosed += (s, e) =>
            {
                if (!switched && !_inPlaceReplacing && _mainHiddenForShot)
                {
                    _mainHiddenForShot = false; // v1.5.3：截图结果关闭后主界面不再自动弹出（保持最小化，用户从悬浮球/托盘手动调出）
                }
            };
            _inPlace = form;
            form.Show();
        }

        /// <summary>v1.3.4 双呈现：组合窗（双图＋同源对照卡）；重翻/改语言时逐块重译并重新合成译图。</summary>
        private void ShowComboPopup(Bitmap image, Bitmap composed, List<OcrBlock> blocks, string src, string dst,
            string srcLang, string dstLang, string prefer, Rectangle selRect)
        {
            _popupReplacing = true;
            ClosePopup();
            _popupReplacing = false;
            _popup = new ResultForm(_engine, _store, src, (Bitmap)image.Clone());
            _popup.WordLookup = (word, pt) => ShowDictPopup(word, pt);
            var shotCopy = (Bitmap)image.Clone();
            var popRef = _popup;
            _popup.ComboRetranslate = async (sl, dl) =>
            {
                // v1.5.2 C2：重翻同样先组段再整段译，与首翻口径一致
                var regs = OverlayComposer.MergeParagraphs(blocks);
                var trs = new List<string>();
                foreach (var b in regs)
                {
                    var r = await TranslateBlockText(b.Text, sl, dl, prefer);
                    trs.Add(r.Text);
                }
                var comp2 = OverlayComposer.Compose(shotCopy, regs, trs, _shotProtected);
                string newDst = FlowJoin(trs, dl);
                popRef.SetPrecomputed(src, newDst, sl, dl);
                popRef.SetComboTranslation(comp2);
                comp2.Dispose();
                // v1.8.0：重翻后核对说明按新译文重算（旧说明对着旧译文，会误导）
                if (_store.Settings.CrossCheckEnabled)
                {
                    var cc = await CrossCheckRunner.Run(_engine, _engine.ExtraSettings, src, newDst, sl, dl, prefer, _store.Settings.CrossCheckBack);
                    _shotCcNote = cc.Note;
                    popRef.SetCrossCheckNote(cc.Note);
                }
                else popRef.SetCrossCheckNote(null);
            };
            _popup.FormClosed += (s, e) =>
            {
                if (_dictPop != null && !_dictPop.IsDisposed && _dictPop.Visible) _dictPop.Close();
            };
            _popup.FormClosed += (s, e) =>
            {
                if (_mainHiddenForShot && !_popupReplacing)
                {
                    _mainHiddenForShot = false; // v1.5.3：截图结果关闭后主界面不再自动弹出（保持最小化，用户从悬浮球/托盘手动调出）
                }
            };
            _popup.PlaceForSelection(selRect);
            _popup.Show();
            _popup.SetPrecomputed(src, dst, srcLang, dstLang);
            _popup.SetComboImages(image, composed);
            _popup.SetCrossCheckNote(_shotCcNote);
        }

        /// <summary>v1.3.0：对照弹窗的预计算版（译图窗"对照"入口），跳过 StartWork 的重复识别翻译。</summary>
        private void ShowPrecomputedPopup(Bitmap image, string src, string dst, string srcLang, string dstLang, Rectangle selRect)
        {
            _popupReplacing = true;
            ClosePopup();
            _popupReplacing = false;
            _popup = new ResultForm(_engine, _store, src, image);
            _popup.WordLookup = (word, pt) => ShowDictPopup(word, pt);
            _popup.FormClosed += (s, e) =>
            {
                if (_dictPop != null && !_dictPop.IsDisposed && _dictPop.Visible) _dictPop.Close();
            };
            _popup.FormClosed += (s, e) =>
            {
                if (_mainHiddenForShot && !_popupReplacing)
                {
                    _mainHiddenForShot = false; // v1.5.3：截图结果关闭后主界面不再自动弹出（保持最小化，用户从悬浮球/托盘手动调出）
                }
            };
            _popup.PlaceForSelection(selRect);
            _popup.Show();
            _popup.SetPrecomputed(src, dst, srcLang, dstLang);
            _popup.SetCrossCheckNote(_shotCcNote);
        }

        // ---------------- 全局快捷键 ----------------

        private void OnFirstShown(object sender, EventArgs e)
        {
            RegisterAllHotkeys();
            if (_store.Settings.StartMinimized) Hide();
        }

        private static uint Mods(bool ctrl, bool alt, bool shift)
        {
            uint m = 0;
            if (ctrl) m |= MOD_CONTROL;
            if (alt) m |= MOD_ALT;
            if (shift) m |= MOD_SHIFT;
            return m;
        }

        private static uint VkOf(string key)
        {
            if (string.IsNullOrEmpty(key)) return 0;
            if (key.Length > 1 && key[0] == 'F') return (uint)(Keys.F1 + (key[1] - '1'));
            return (uint)key[0];
        }

        private static string LabelOf(bool ctrl, bool alt, bool shift, string key)
        {
            var parts = new List<string>();
            if (ctrl) parts.Add("Ctrl");
            if (alt) parts.Add("Alt");
            if (shift) parts.Add("Shift");
            parts.Add(key);
            return string.Join("+", parts);
        }

        private void RegisterAllHotkeys()
        {
            try { UnregisterHotKey(Handle, HK_SHOT); } catch { }
            try { UnregisterHotKey(Handle, HK_MINI); } catch { }
            var s = _store.Settings;

            _shotRegistered = false;
            var tries = new List<uint[]>
            {
                new[] { Mods(s.HotkeyCtrl, s.HotkeyAlt, s.HotkeyShift), VkOf(s.HotkeyKey) },
                new[] { MOD_CONTROL | MOD_ALT, (uint)'T' },
                new[] { MOD_CONTROL | MOD_ALT, (uint)'A' },
                new[] { MOD_CONTROL | MOD_SHIFT, (uint)'S' },
                new[] { 0u, (uint)Keys.F9 }
            };
            var labels = new List<string>
            {
                LabelOf(s.HotkeyCtrl, s.HotkeyAlt, s.HotkeyShift, s.HotkeyKey),
                "Ctrl+Alt+T", "Ctrl+Alt+A", "Ctrl+Shift+S", "F9"
            };
            for (int i = 0; i < tries.Count && !_shotRegistered; i++)
            {
                try
                {
                    if (RegisterHotKey(Handle, HK_SHOT, tries[i][0], tries[i][1]))
                    { _shotRegistered = true; _shotLabel = labels[i]; }
                }
                catch { }
            }

            _miniRegistered = false;
            try
            {
                if (RegisterHotKey(Handle, HK_MINI, Mods(s.MiniHkCtrl, s.MiniHkAlt, s.MiniHkShift), VkOf(s.MiniHkKey)))
                { _miniRegistered = true; _miniLabel = LabelOf(s.MiniHkCtrl, s.MiniHkAlt, s.MiniHkShift, s.MiniHkKey); }
                else if (RegisterHotKey(Handle, HK_MINI, MOD_CONTROL | MOD_ALT, (uint)'M'))
                { _miniRegistered = true; _miniLabel = "Ctrl+Alt+M"; }
            }
            catch { }

            if (_tray != null)
                _tray.Text = "WinTranslator（截图 " + (_shotRegistered ? _shotLabel : "?") + "）";
        }

        // ---------------- 剪贴板取词 ----------------

        private void OnClipboardChanged()
        {
            if (!_store.Settings.ClipboardListen) return;
            if (DateTime.Now < _suppressUntil) return;
            string text = null;
            try { if (Clipboard.ContainsText()) text = Clipboard.GetText(); } catch { }
            if (string.IsNullOrWhiteSpace(text)) return;
            text = text.Trim();
            if (text.Length > 2000) return;
            var now = DateTime.Now;
            if (text == _lastClipText && (now - _lastClipAt).TotalMilliseconds < 800) return;
            _lastClipText = text;
            _lastClipAt = now;
            ShowPopup(text, null, Cursor.Position);
        }

        // ---------------- 发音 ----------------

        private void Speak(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return;
            try
            {
                var voiceType = Type.GetTypeFromProgID("SAPI.SpVoice");
                if (voiceType == null) { SetStatus("系统语音不可用"); return; }
                dynamic voice = Activator.CreateInstance(voiceType);
                voice.Rate = 0;
                voice.Speak(text, 1);
            }
            catch { SetStatus("朗读失败"); }
        }

        // ---------------- 生命周期 ----------------

        private void SetStatus(string text) { if (_lblStatus != null) _lblStatus.Text = text; }

        // ---------------- v1.5.3 云同步 ----------------

        private void StartSyncLoops()
        {
            if (_syncTimer != null) return;
            _syncTimer = new Timer { Interval = 60000 };
            _syncTimer.Tick += (s, e) => RunSyncAsync();
            _syncTimer.Start();
            RunSyncAsync();
        }

        private async void RunSyncAsync()
        {
            if (_syncing || !SyncService.IsConfigured(_store.Settings)) return;
            _syncing = true;
            try
            {
                var r = await SyncService.SyncNow(_store);
                if (r.Applied)
                {
                    ApplyFontLive();
                    ApplySettingsToHome();
                    if (_pageWord != null) _pageWord.RefreshAll();
                    RefreshHistory();
                    if (_quick != null) _quick.RefreshAll();
                    SetStatus("云同步：" + r.Status);
                }
            }
            catch { }
            finally { _syncing = false; }
        }

        private void OnFormClosingMain(object sender, FormClosingEventArgs e)
        {
            if (!_reallyExit && _store.Settings.MinimizeToTrayOnClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            // v1.5.3 退出前尽力同步一次（本地有变动才上传，最多等 4 秒，不阻塞退出太久）
            try
            {
                if (SyncService.IsConfigured(_store.Settings) && SyncService.ComputeHash(_store) != _store.Settings.SyncHash)
                    SyncService.SyncNow(_store).Wait(4000);
            }
            catch { }
            try { RemoveClipboardFormatListener(Handle); } catch { }
            try { UnregisterHotKey(Handle, HK_SHOT); UnregisterHotKey(Handle, HK_MINI); } catch { }
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            if (_ball != null && !_ball.IsDisposed) _ball.Close();
            if (_mini != null && !_mini.IsDisposed) _mini.Close();
            ClosePopup();
        }
    }
}
