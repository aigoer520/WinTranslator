using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace WinTranslator
{
    /// <summary>
    /// 独立设置弹窗（v1.1.1 事项五，仿用户参考的有道设置窗形态）：
    /// 左侧栏（设置/关于）＋顶部页签（常规 / OCR·截图）＋右上角 ✕ 关闭。
    /// 保存后通过回调让 MainForm 应用（热键重注册、悬浮球、置顶、引擎与 GLM Key 同步）。
    /// </summary>
    public class SettingsForm : Form
    {
        private readonly Store _store;
        private readonly Func<string> _hotkeyStatus;
        private readonly Action _saved;

        private RoundPanel _sideSettings, _sideAbout;
        private Label _tabGeneral, _tabOcr, _tabApi;
        private Panel _underline;
        private RoundPanel _btnSave;
        private Label _lblSaved;
        private Panel _pageGeneral, _pageOcr, _pageApi, _pageAbout;

        // v1.8.0 翻译接口页：折叠行（9 家自备 Key 引擎＋DeepLX＋韦氏词典）＋免费地址＋默认词典＋核对开关
        private class ApiDef
        {
            public string Id, Title, ApplyUrl, ApplyText;
            public string[] FieldLabels;
            public bool[] FieldSecret;
            public bool IsDict; // 韦氏：测试走查词而非翻译
        }

        private class ApiRow
        {
            public ApiDef Def;
            public RoundPanel Header;
            public Panel Body;
            public Label StatusLbl, Chevron, ResultLbl;
            public TextBox[] Boxes;
            public bool Expanded;
            public int BodyH;
        }

        private static readonly ApiDef[] ApiDefs =
        {
            new ApiDef { Id = "baidu",    Title = "百度翻译",   ApplyUrl = "https://fanyi-api.baidu.com/",            ApplyText = "百度翻译开放平台",   FieldLabels = new[] { "App ID", "密钥" },            FieldSecret = new[] { false, true } },
            new ApiDef { Id = "deepl",    Title = "DeepL",      ApplyUrl = "https://www.deepl.com/pro-api",          ApplyText = "DeepL API 官网",     FieldLabels = new[] { "Auth Key" },              FieldSecret = new[] { true } },
            new ApiDef { Id = "caiyun",   Title = "彩云小译",   ApplyUrl = "https://dashboard.caiyunapp.com/",       ApplyText = "彩云科技开放平台",   FieldLabels = new[] { "Token" },                 FieldSecret = new[] { true } },
            new ApiDef { Id = "niutrans", Title = "小牛翻译",   ApplyUrl = "https://niutrans.com/",                  ApplyText = "小牛翻译官网",       FieldLabels = new[] { "API Key" },               FieldSecret = new[] { true } },
            new ApiDef { Id = "youdaozy", Title = "有道智云",   ApplyUrl = "https://ai.youdao.com/",                 ApplyText = "有道智云",           FieldLabels = new[] { "应用 ID", "应用密钥" },   FieldSecret = new[] { false, true } },
            new ApiDef { Id = "ali",      Title = "阿里翻译",   ApplyUrl = "https://mt.console.aliyun.com/",         ApplyText = "阿里云机器翻译",     FieldLabels = new[] { "AccessKey ID", "AccessKey Secret" }, FieldSecret = new[] { false, true } },
            new ApiDef { Id = "tencent",  Title = "腾讯翻译君", ApplyUrl = "https://console.cloud.tencent.com/tmt",  ApplyText = "腾讯云机器翻译",     FieldLabels = new[] { "SecretId", "SecretKey" }, FieldSecret = new[] { false, true } },
            new ApiDef { Id = "volc",     Title = "火山翻译",   ApplyUrl = "https://console.volcengine.com/iam/keymanage", ApplyText = "火山引擎密钥管理", FieldLabels = new[] { "AccessKey ID", "SecretAccessKey" }, FieldSecret = new[] { false, true } },
            new ApiDef { Id = "deeplx",   Title = "DeepLX",     ApplyUrl = null,                                     ApplyText = "自部署服务",         FieldLabels = new[] { "接口地址", "访问令牌（可空）" }, FieldSecret = new[] { false, true } },
            new ApiDef { Id = "webster",  Title = "韦氏词典",   ApplyUrl = "https://dictionaryapi.com/register/index", ApplyText = "韦氏开发者中心",   FieldLabels = new[] { "API Key" },               FieldSecret = new[] { true }, IsDict = true },
        };

        private readonly List<ApiRow> _apiRows = new List<ApiRow>();
        private Panel _apiInner;
        private Label _apiTitle2, _apiTitle3, _apiTitle4;
        private Panel _apiFreePanel, _apiDictPanel, _apiCrossPanel;
        private TextBox _txtLibreUrl, _txtLingvaUrl;
        private ComboBox _cmbDictDefault;
        private CheckBox _chkCross, _chkCrossBack;
        private int _engineSelLoaded;

        private Label[] _fontOpts;
        private int _fontSel;
        private Label[] _uiFontOpts, _uiScaleOpts;
        private int _uiFontSel, _uiScaleSel = 1;
        private CheckBox _chkSync;
        private Label[] _syncMethodOpts;
        private int _syncMethodSel;
        private static readonly string[] SyncMethodNames = { "GitHub 私有仓库", "WebDAV 网盘" };
        private Panel _ghGroup, _davGroup;
        private TextBox _txtGhToken, _txtGhRepo, _txtDavUrl, _txtDavUser, _txtDavPass;
        private Label _lblSyncStatus;
        private RoundPanel _btnSyncNow;
        private static readonly string[] UiFontNames = { "微软雅黑 UI", "微软雅黑", "等线", "宋体" };
        private static readonly string[] UiFontKeys = { "Microsoft YaHei UI", "Microsoft YaHei", "DengXian", "SimSun" };
        private static readonly string[] UiScaleNames = { "小", "标准", "大" };
        private Label[] _skinOpts;
        private int _skinSel;
        private static readonly string[] SkinNames = { "经典浅色", "磨砂毛玻璃", "磨砂·自带背景", "深色玻璃", "Lucide 线条", "液态玻璃" };
        private static readonly string[] SkinKeys = { "classic", "acrylic", "backdrop", "frosted", "lucide", "liquid" };
        private CheckBox _chkAutoStart, _chkStartMin, _chkTray, _chkBall, _chkTopMost, _chkClipboard;
        private Label[] _engineOpts;
        private int _engineSel;
        private TextBox _txtGlmKey;
        private Label _lblGlmStatus;
        private CheckBox _chkShotCtrl, _chkShotAlt, _chkShotShift, _chkMiniCtrl, _chkMiniAlt, _chkMiniShift;
        private ComboBox _cmbShotKey, _cmbMiniKey;
        private Label _lblHkStatus;
        private CheckBox _chkHideUi;
        private ComboBox _cmbShotDelay;

        private static readonly int[] FontSizes = { 12, 14, 16, 18, 20 };
        private static readonly string[] EngineNames = { "自动切换", "有道", "必应", "AI（智谱）", "谷歌", "MyMemory" };

        public SettingsForm(Store store, Func<string> hotkeyStatus, Action saved)
        {
            _store = store;
            _hotkeyStatus = hotkeyStatus;
            _saved = saved;

            Text = "设置";
            ClientSize = new Size(940, 660);
            MinimumSize = new Size(860, 540);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            BackColor = SkinMode.IsFrosted ? Palette.Bg : Color.White;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei UI", 9f);
            KeyPreview = true;

            BuildSidebar();
            BuildTabs();
            _pageGeneral = BuildGeneralPage();
            _pageOcr = BuildOcrPage();
            _pageApi = BuildApiPage();
            _pageAbout = BuildAboutPage();
            Controls.AddRange(new Control[] { _pageGeneral, _pageOcr, _pageApi, _pageAbout });

            _btnSave = Ui.PillButton("保存设置", ClientSize.Width - 158, ClientSize.Height - 56, 112, 36, Palette.Accent, Color.White, true);
            _btnSave.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _btnSave.WireClickThrough((s, e) => SaveAll());
            Controls.Add(_btnSave);
            _lblSaved = Ui.Lbl("", 0, ClientSize.Height - 48, 9.5f, Palette.SwitchBlue);
            _lblSaved.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            Controls.Add(_lblSaved);

            // 保存按钮须浮在页面面板之上（页面为后加控件会盖住先加的按钮区）
            _btnSave.BringToFront();
            _lblSaved.BringToFront();
            LoadFromSettings();
            ShowSection(0);
            ShowTab(0);
            FrostifyInputs(this);
            Shown += (s, e) => GlassMotion.FadeIn(this);
            Shown += (s, e) => FontTheme.ApplyTo(this); // v1.5.3 界面字体/字号档
            FormClosing += (s, e) => GlassMotion.TryFadeOut(this, e);
        }

        /// <summary>v1.5.0 深色玻璃：设置内的输入框/下拉框改深卡面＋浅字（其他皮肤不生效）。</summary>
        private static void FrostifyInputs(Control root)
        {
            if (!SkinMode.IsFrosted || root == null) return;
            foreach (Control c in root.Controls)
            {
                if (c is TextBox || c is ComboBox) { c.BackColor = Palette.Card; c.ForeColor = Palette.Main; }
                FrostifyInputs(c);
            }
        }

        private void BuildSidebar()
        {
            var side = new Panel { Dock = DockStyle.Left, Width = 150, BackColor = SkinMode.IsAcrylic ? SkinMode.Key : (SkinMode.IsFrosted ? Palette.NavBg : Color.FromArgb(244, 245, 247)) };
            Controls.Add(side);
            _sideSettings = new RoundPanel { Location = new Point(10, 92), Size = new Size(130, 40), Radius = 8, Fill = Palette.SelBg, NoEdge = true, Cursor = Cursors.Hand };
            _sideSettings.Controls.Add(new Label
            {
                Text = "设置", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold), ForeColor = Palette.Accent, BackColor = Color.Transparent,
                Padding = new Padding(16, 0, 0, 0)
            });
            _sideSettings.WireClickThrough((s, e) => ShowSection(0));
            side.Controls.Add(_sideSettings);
            _sideAbout = new RoundPanel { Location = new Point(10, 142), Size = new Size(130, 40), Radius = 8, Fill = SkinMode.IsFrosted ? Palette.PillBg : Color.FromArgb(244, 245, 247), NoEdge = true, Cursor = Cursors.Hand };
            _sideAbout.Controls.Add(new Label
            {
                Text = "关于", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 11f), ForeColor = Palette.Sub, BackColor = Color.Transparent,
                Padding = new Padding(16, 0, 0, 0)
            });
            _sideAbout.WireClickThrough((s, e) => ShowSection(1));
            side.Controls.Add(_sideAbout);
        }

        private void BuildTabs()
        {
            var close = new IconBtn { Kind = "close", Ghost = true, Size = new Size(30, 30), Location = new Point(ClientSize.Width - 44, 8) };
            close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            close.Click += (s, e) => Close();
            Controls.Add(close);

            _tabGeneral = Ui.Lbl("常规", 196, 38, 12f, Palette.Main, true);
            _tabGeneral.Cursor = Cursors.Hand;
            _tabGeneral.Click += (s, e) => ShowTab(0);
            _tabOcr = Ui.Lbl("OCR/截图", 268, 38, 12f, Palette.Sub);
            _tabOcr.Cursor = Cursors.Hand;
            _tabOcr.Click += (s, e) => ShowTab(1);
            _tabApi = Ui.Lbl("翻译接口", 364, 38, 12f, Palette.Sub);
            _tabApi.Cursor = Cursors.Hand;
            _tabApi.Click += (s, e) => ShowTab(2);
            Controls.AddRange(new Control[] { _tabGeneral, _tabOcr, _tabApi });
            _underline = new Panel { Location = new Point(196, 70), Size = new Size(30, 3), BackColor = Palette.Accent };
            Controls.Add(_underline);
            var line = new Panel { Location = new Point(196, 76), Size = new Size(ClientSize.Width - 196 - 32, 1), BackColor = SkinMode.IsFrosted ? Palette.Border : Color.FromArgb(232, 234, 238), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            Controls.Add(line);
        }

        private Panel NewPage()
        {
            return new Panel
            {
                Location = new Point(150, 84),
                Size = new Size(ClientSize.Width - 150, ClientSize.Height - 84),
                BackColor = SkinMode.IsAcrylic ? SkinMode.Key : (SkinMode.IsFrosted ? Palette.Bg : Color.White), Visible = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
        }

        private void ShowSection(int idx)
        {
            bool settings = idx == 0;
            _tabGeneral.Visible = settings;
            _tabOcr.Visible = settings;
            _tabApi.Visible = settings;
            _underline.Visible = settings;
            _btnSave.Visible = settings;
            _lblSaved.Visible = settings;
            _pageAbout.Visible = !settings;
            if (settings) { _pageGeneral.Visible = true; ShowTab(_pageApi.Visible ? 2 : (_pageOcr.Visible ? 1 : 0)); }
            _sideSettings.Fill = settings ? ((SkinMode.IsFrosted || SkinMode.IsLucide) ? Palette.SelBg : Color.FromArgb(253, 236, 233)) : ((SkinMode.IsFrosted || SkinMode.IsLucide) ? Palette.PillBg : Color.FromArgb(244, 245, 247));
            _sideAbout.Fill = settings ? ((SkinMode.IsFrosted || SkinMode.IsLucide) ? Palette.PillBg : Color.FromArgb(244, 245, 247)) : ((SkinMode.IsFrosted || SkinMode.IsLucide) ? Palette.SelBg : Color.FromArgb(253, 236, 233));
            _sideSettings.Controls[0].ForeColor = settings ? Palette.Accent : Palette.Sub;
            _sideSettings.Controls[0].Font = new Font("Microsoft YaHei UI", 11f, settings ? FontStyle.Bold : FontStyle.Regular);
            _sideAbout.Controls[0].ForeColor = settings ? Palette.Sub : Palette.Accent;
            _sideAbout.Controls[0].Font = new Font("Microsoft YaHei UI", 11f, settings ? FontStyle.Regular : FontStyle.Bold);
            _sideSettings.Invalidate(); _sideAbout.Invalidate();
        }

        private void ShowTab(int idx)
        {
            _pageGeneral.Visible = idx == 0;
            _pageOcr.Visible = idx == 1;
            _pageApi.Visible = idx == 2;
            _tabGeneral.Font = new Font("Microsoft YaHei UI", 12f, idx == 0 ? FontStyle.Bold : FontStyle.Regular);
            _tabGeneral.ForeColor = idx == 0 ? Palette.Main : Palette.Sub;
            _tabOcr.Font = new Font("Microsoft YaHei UI", 12f, idx == 1 ? FontStyle.Bold : FontStyle.Regular);
            _tabOcr.ForeColor = idx == 1 ? Palette.Main : Palette.Sub;
            _tabApi.Font = new Font("Microsoft YaHei UI", 12f, idx == 2 ? FontStyle.Bold : FontStyle.Regular);
            _tabApi.ForeColor = idx == 2 ? Palette.Main : Palette.Sub;
            _underline.Location = new Point(idx == 0 ? 196 : (idx == 1 ? 268 : 364), 70);
            _underline.Size = new Size(idx == 0 ? 30 : (idx == 1 ? 58 : 56), 3);
        }

        // ---------------- 常规页 ----------------

        private Label[] OptionRow(Control parent, string[] texts, int x, int y, int gap, Action<int> onPick)
        {
            var labels = new Label[texts.Length];
            for (int i = 0; i < texts.Length; i++)
            {
                int idx = i;
                var l = Ui.Lbl(texts[i], x + i * gap, y, 11f, Palette.Main);
                l.Cursor = Cursors.Hand;
                l.Click += (s, e) => onPick(idx);
                parent.Controls.Add(l);
                labels[i] = l;
            }
            return labels;
        }

        private static void RefreshRow(Label[] labels, string[] texts, int sel)
        {
            for (int i = 0; i < labels.Length; i++)
            {
                labels[i].Text = (i == sel ? "◉ " : "○ ") + texts[i];
                labels[i].ForeColor = i == sel ? Palette.Accent : Palette.Main;
                labels[i].Font = new Font("Microsoft YaHei UI", 11f, i == sel ? FontStyle.Bold : FontStyle.Regular);
            }
        }

        private static CheckBox Chk(Control parent, string text, int x, int y)
        {
            var c = new CheckBox { Text = text, Location = new Point(x, y), AutoSize = true, Font = new Font("Microsoft YaHei UI", 10.5f), ForeColor = Palette.Main, BackColor = Color.Transparent };
            parent.Controls.Add(c);
            return c;
        }

        private Panel BuildGeneralPage()
        {
            var p = NewPage();
            p.AutoScroll = true;
            var inner = new Panel { Location = new Point(0, 0), Size = new Size(760, 1272), BackColor = SkinMode.IsAcrylic ? SkinMode.Key : (SkinMode.IsFrosted ? Palette.Bg : Color.White) };
            p.Controls.Add(inner);

            inner.Controls.Add(Ui.Lbl("字号选择", 46, 18, 12.5f, Palette.Main, true));
            inner.Controls.Add(Ui.Lbl("影响查词和翻译结果", 140, 22, 9.5f, Palette.Sub));
            var fontTexts = new string[FontSizes.Length];
            for (int i = 0; i < FontSizes.Length; i++) fontTexts[i] = FontSizes[i].ToString();
            _fontOpts = OptionRow(inner, fontTexts, 46, 54, 66, idx =>
            {
                _fontSel = idx;
                RefreshRow(_fontOpts, fontTexts, _fontSel);
            });
            RefreshRow(_fontOpts, fontTexts, 1);

            inner.Controls.Add(Ui.Lbl("启动", 46, 104, 12.5f, Palette.Main, true));
            _chkAutoStart = Chk(inner, "开机时自动启动", 46, 138);
            _chkStartMin = Chk(inner, "启动后最小化到系统托盘", 46, 168);
            _chkTray = Chk(inner, "关闭窗口时最小化到托盘", 46, 198);

            inner.Controls.Add(Ui.Lbl("界面与取词", 46, 240, 12.5f, Palette.Main, true));
            _chkBall = Chk(inner, "显示桌面悬浮球", 46, 274);
            _chkTopMost = Chk(inner, "主窗口置顶", 46, 304);
            _chkClipboard = Chk(inner, "剪贴板取词：复制文字后自动弹翻译小窗", 46, 334);

            inner.Controls.Add(Ui.Lbl("皮肤", 46, 376, 12.5f, Palette.Main, true));
            inner.Controls.Add(Ui.Lbl("切换后主窗口即时生效；深色玻璃＝深蓝黑流光底＋深色玻璃卡＋白字；Lucide 线条＝纯白底＋黑按钮＋深色描边；液态玻璃＝彩色光晕底＋半透明白玻璃卡＋顶边高光", 110, 380, 9.5f, Palette.Sub));
            _skinOpts = OptionRow(inner, SkinNames, 46, 408, 118, idx =>
            {
                _skinSel = idx;
                RefreshRow(_skinOpts, SkinNames, _skinSel);
            });
            RefreshRow(_skinOpts, SkinNames, 0);

            inner.Controls.Add(Ui.Lbl("默认翻译引擎", 46, 448, 12.5f, Palette.Main, true));
            // v1.5.1 引擎增至 6 个：单行放不下（末项会被保存按钮挡），分两行（4＋2）
            var engRowA = OptionRow(inner, new[] { EngineNames[0], EngineNames[1], EngineNames[2], EngineNames[3] }, 46, 480, 118, idx =>
            {
                _engineSel = idx;
                RefreshRow(_engineOpts, EngineNames, _engineSel);
            });
            var engRowB = OptionRow(inner, new[] { EngineNames[4], EngineNames[5] }, 46, 514, 118, idx =>
            {
                _engineSel = idx + 4;
                RefreshRow(_engineOpts, EngineNames, _engineSel);
            });
            _engineOpts = new[] { engRowA[0], engRowA[1], engRowA[2], engRowA[3], engRowB[0], engRowB[1] };
            RefreshRow(_engineOpts, EngineNames, 0);

            inner.Controls.Add(Ui.Lbl("AI 翻译（智谱 GLM）", 46, 558, 12.5f, Palette.Main, true));
            inner.Controls.Add(Ui.Lbl("API Key", 46, 594, 10f, Palette.Sub));
            _txtGlmKey = new TextBox
            {
                Location = new Point(110, 590), Width = 360,
                Font = new Font("Microsoft YaHei UI", 10f), UseSystemPasswordChar = true
            };
            inner.Controls.Add(_txtGlmKey);
            var lnkReg = Ui.Lbl("注册获取免费 Key", 486, 594, 10f, Palette.Accent);
            lnkReg.Cursor = Cursors.Hand;
            lnkReg.Click += (s, e) => { try { Process.Start("https://open.bigmodel.cn"); } catch { } };
            inner.Controls.Add(lnkReg);
            var btnTest = Ui.PillButton("测试连接", 46, 628, 104, 32, Color.White, Palette.Main);
            btnTest.WireClickThrough(async (s, e) =>
            {
                _lblGlmStatus.Text = "测试中…";
                try
                {
                    var client = new GlmClient { ApiKey = _txtGlmKey.Text.Trim() };
                    string r = await client.TestConnection();
                    _lblGlmStatus.Text = "连接成功，试译：Hello → " + r;
                }
                catch (Exception ex) { _lblGlmStatus.Text = ex.Message; }
            });
            inner.Controls.Add(btnTest);
            _lblGlmStatus = Ui.Lbl("GLM-4-Flash 为智谱免费模型；Key 只存本机设置文件。", 166, 636, 9.5f, Palette.Sub);
            inner.Controls.Add(_lblGlmStatus);

            inner.Controls.Add(Ui.Lbl("快捷键", 46, 682, 12.5f, Palette.Main, true));
            inner.Controls.Add(Ui.Lbl("截图翻译", 46, 720, 10.5f, Palette.Main));
            _chkShotCtrl = InlineChk(inner, "Ctrl", 130, 718);
            _chkShotAlt = InlineChk(inner, "Alt", 198, 718);
            _chkShotShift = InlineChk(inner, "Shift", 262, 718);
            _cmbShotKey = KeyCombo(inner, 330, 715);
            inner.Controls.Add(Ui.Lbl("迷你窗口", 46, 756, 10.5f, Palette.Main));
            _chkMiniCtrl = InlineChk(inner, "Ctrl", 130, 754);
            _chkMiniAlt = InlineChk(inner, "Alt", 198, 754);
            _chkMiniShift = InlineChk(inner, "Shift", 262, 754);
            _cmbMiniKey = KeyCombo(inner, 330, 751);
            _lblHkStatus = Ui.Lbl("", 46, 796, 9.5f, Palette.Sub);
            inner.Controls.Add(_lblHkStatus);

            // v1.5.3 界面字体（用户要求）：字体族＋界面字号档；上方"字号选择"是译文字号，两者分工不同
            inner.Controls.Add(Ui.Lbl("界面字体", 46, 830, 12.5f, Palette.Main, true));
            inner.Controls.Add(Ui.Lbl("影响软件界面文字；截图译图上的字按原文行高排版，不受影响", 140, 834, 9.5f, Palette.Sub));
            _uiFontOpts = OptionRow(inner, UiFontNames, 46, 864, 148, idx =>
            {
                _uiFontSel = idx;
                RefreshRow(_uiFontOpts, UiFontNames, _uiFontSel);
            });
            RefreshRow(_uiFontOpts, UiFontNames, 0);
            inner.Controls.Add(Ui.Lbl("界面字号", 46, 902, 11f, Palette.Main, true));
            _uiScaleOpts = OptionRow(inner, UiScaleNames, 140, 900, 76, idx =>
            {
                _uiScaleSel = idx;
                RefreshRow(_uiScaleOpts, UiScaleNames, _uiScaleSel);
            });
            RefreshRow(_uiScaleOpts, UiScaleNames, 1);

            // v1.5.3 云同步（用户要求）：绑定自己的 GitHub 私有仓库或 WebDAV 网盘，资料自动上下行
            inner.Controls.Add(Ui.Lbl("云同步", 46, 948, 12.5f, Palette.Main, true));
            inner.Controls.Add(Ui.Lbl("学习资料存到你自己的云存储，换台电脑打开自动下载；GLM Key 与同步令牌不上传", 128, 952, 9.5f, Palette.Sub));
            _chkSync = Chk(inner, "启用云同步（打开软件自动拉取、资料变动自动上传）", 46, 982);
            inner.Controls.Add(Ui.Lbl("同步方式", 46, 1018, 11f, Palette.Main, true));
            _syncMethodOpts = OptionRow(inner, SyncMethodNames, 140, 1016, 176, idx =>
            {
                _syncMethodSel = idx;
                RefreshRow(_syncMethodOpts, SyncMethodNames, _syncMethodSel);
                RefreshSyncGroups();
            });
            RefreshRow(_syncMethodOpts, SyncMethodNames, 0);

            _ghGroup = new Panel { Location = new Point(46, 1052), Size = new Size(700, 108), BackColor = Color.Transparent };
            _ghGroup.Controls.Add(Ui.Lbl("访问令牌", 0, 4, 10f, Palette.Sub));
            _txtGhToken = new TextBox { Location = new Point(84, 0), Width = 400, Font = FontTheme.Of(10f), UseSystemPasswordChar = true };
            _ghGroup.Controls.Add(_txtGhToken);
            _ghGroup.Controls.Add(Ui.Lbl("仓库名", 0, 42, 10f, Palette.Sub));
            _txtGhRepo = new TextBox { Location = new Point(84, 38), Width = 400, Font = FontTheme.Of(10f) };
            _ghGroup.Controls.Add(_txtGhRepo);
            var ghHint = new Label
            {
                Location = new Point(0, 74), Width = 690, Height = 30, ForeColor = Palette.Sub, Font = FontTheme.Of(9f), BackColor = Color.Transparent,
                Text = "令牌在 GitHub → Settings → Developer settings → Personal access tokens 创建（勾选 repo 权限）；仓库不存在会自动建为私有仓库。令牌只加密存本机。"
            };
            _ghGroup.Controls.Add(ghHint);
            inner.Controls.Add(_ghGroup);

            _davGroup = new Panel { Location = new Point(46, 1052), Size = new Size(700, 146), BackColor = Color.Transparent };
            _davGroup.Controls.Add(Ui.Lbl("服务器地址", 0, 4, 10f, Palette.Sub));
            _txtDavUrl = new TextBox { Location = new Point(96, 0), Width = 424, Font = FontTheme.Of(10f) };
            _davGroup.Controls.Add(_txtDavUrl);
            _davGroup.Controls.Add(Ui.Lbl("账号", 0, 42, 10f, Palette.Sub));
            _txtDavUser = new TextBox { Location = new Point(96, 38), Width = 300, Font = FontTheme.Of(10f) };
            _davGroup.Controls.Add(_txtDavUser);
            _davGroup.Controls.Add(Ui.Lbl("应用密码", 0, 80, 10f, Palette.Sub));
            _txtDavPass = new TextBox { Location = new Point(96, 76), Width = 300, Font = FontTheme.Of(10f), UseSystemPasswordChar = true };
            _davGroup.Controls.Add(_txtDavPass);
            var davHint = new Label
            {
                Location = new Point(0, 112), Width = 690, Height = 30, ForeColor = Palette.Sub, Font = FontTheme.Of(9f), BackColor = Color.Transparent,
                Text = "坚果云：网页端账号设置 → 安全选项 → 第三方应用管理，生成应用密码（不要填登录密码）。应用密码只加密存本机。"
            };
            _davGroup.Controls.Add(davHint);
            inner.Controls.Add(_davGroup);

            _btnSyncNow = Ui.PillButton("立即同步", 46, 1212, 110, 34, Color.White, Palette.Main);
            _btnSyncNow.WireClickThrough(async (s2, e2) =>
            {
                CollectSyncFields();
                _store.SaveSettings();
                if (!_chkSync.Checked) { _lblSyncStatus.Text = "请先勾选启用云同步"; return; }
                _lblSyncStatus.Text = "同步中…";
                var r = await SyncService.SyncNow(_store);
                _lblSyncStatus.Text = r.Status;
                if (r.Applied && _saved != null) _saved();
            });
            inner.Controls.Add(_btnSyncNow);
            _lblSyncStatus = Ui.Lbl("", 172, 1220, 9.5f, Palette.Sub);
            inner.Controls.Add(_lblSyncStatus);
            RefreshSyncGroups();
            return p;
        }

        private void RefreshSyncGroups()
        {
            if (_ghGroup != null) _ghGroup.Visible = _syncMethodSel == 0;
            if (_davGroup != null) _davGroup.Visible = _syncMethodSel == 1;
        }

        private void CollectSyncFields()
        {
            var s = _store.Settings;
            s.SyncEnabled = _chkSync.Checked;
            s.SyncMethod = _syncMethodSel;
            s.GithubToken = SyncService.Protect(_txtGhToken.Text.Trim());
            s.GithubRepo = string.IsNullOrEmpty(_txtGhRepo.Text.Trim()) ? "wintranslator-data" : _txtGhRepo.Text.Trim();
            s.WebdavUrl = _txtDavUrl.Text.Trim();
            s.WebdavUser = _txtDavUser.Text.Trim();
            s.WebdavPass = SyncService.Protect(_txtDavPass.Text.Trim());
        }

        private void LoadSyncFields()
        {
            var s = _store.Settings;
            _chkSync.Checked = s.SyncEnabled;
            _syncMethodSel = Math.Min(1, Math.Max(0, s.SyncMethod));
            RefreshRow(_syncMethodOpts, SyncMethodNames, _syncMethodSel);
            _txtGhToken.Text = SyncService.Unprotect(s.GithubToken);
            _txtGhRepo.Text = s.GithubRepo ?? "wintranslator-data";
            _txtDavUrl.Text = s.WebdavUrl ?? "";
            _txtDavUser.Text = s.WebdavUser ?? "";
            _txtDavPass.Text = SyncService.Unprotect(s.WebdavPass);
            _lblSyncStatus.Text = s.SyncUpdatedAt > 0
                ? "上次同步：" + DateTimeOffset.FromUnixTimeSeconds(s.SyncUpdatedAt).LocalDateTime.ToString("yyyy-MM-dd HH:mm")
                : "尚未同步过";
            RefreshSyncGroups();
        }

        private static CheckBox InlineChk(Control parent, string text, int x, int y)
        {
            var c = new CheckBox { Text = text, Location = new Point(x, y), AutoSize = true, BackColor = Color.Transparent };
            parent.Controls.Add(c);
            return c;
        }

        private static ComboBox KeyCombo(Control parent, int x, int y)
        {
            var cmb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(x, y), Width = 80 };
            for (char c = 'A'; c <= 'Z'; c++) cmb.Items.Add(c.ToString());
            for (int i = 1; i <= 9; i++) cmb.Items.Add("F" + i);
            parent.Controls.Add(cmb);
            return cmb;
        }

        // ---------------- OCR 页 ----------------

        private Panel BuildOcrPage()
        {
            var p = NewPage();
            _chkHideUi = Chk(p, "截图时自动隐藏本软件界面（主窗口/悬浮球/浮窗）", 46, 26);
            p.Controls.Add(Ui.Lbl("默认截图延时（托盘/悬浮球菜单可临时选 3 秒/5 秒）", 46, 70, 10f, Palette.Sub));
            _cmbShotDelay = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(46, 96), Width = 120 };
            _cmbShotDelay.Items.AddRange(new object[] { "立即", "3 秒", "5 秒" });
            p.Controls.Add(_cmbShotDelay);
            var note = new Label
            {
                Location = new Point(46, 150), Width = 620, Height = 110, ForeColor = Palette.Sub,
                Font = new Font("Microsoft YaHei UI", 10f), BackColor = Color.Transparent,
                Text = "文字识别在本机离线完成（Tesseract 引擎，随软件自带），支持中文简体、英文、日语、韩语。\n" +
                       "识别前会自动放大、灰度化小图以提升小字识别率；识别结果会自动去掉中文字符间的多余空格。\n" +
                       "若识别不到文字，请框选更清晰、对比更强的区域重试。"
            };
            p.Controls.Add(note);
            return p;
        }

        // ---------------- 翻译接口页（v1.8.0） ----------------

        private static string GetCredPlain(AppSettings s, string id, int idx)
        {
            switch (id)
            {
                case "baidu": return idx == 0 ? s.BaiduAppId : s.BaiduKey;
                case "deepl": return s.DeepLKey;
                case "caiyun": return s.CaiyunToken;
                case "niutrans": return s.NiutransKey;
                case "youdaozy": return idx == 0 ? s.YoudaoAppKey : s.YoudaoKey;
                case "ali": return idx == 0 ? s.AliKeyId : s.AliKeySecret;
                case "tencent": return idx == 0 ? s.TencentSecretId : s.TencentSecretKey;
                case "volc": return idx == 0 ? s.VolcKeyId : s.VolcKeySecret;
                case "deeplx": return idx == 0 ? s.DeeplxUrl : s.DeeplxToken;
                case "webster": return s.WebsterKey;
                default: return "";
            }
        }

        private static void SetCred(AppSettings s, string id, int idx, string v)
        {
            switch (id)
            {
                case "baidu": if (idx == 0) s.BaiduAppId = v; else s.BaiduKey = v; break;
                case "deepl": s.DeepLKey = v; break;
                case "caiyun": s.CaiyunToken = v; break;
                case "niutrans": s.NiutransKey = v; break;
                case "youdaozy": if (idx == 0) s.YoudaoAppKey = v; else s.YoudaoKey = v; break;
                case "ali": if (idx == 0) s.AliKeyId = v; else s.AliKeySecret = v; break;
                case "tencent": if (idx == 0) s.TencentSecretId = v; else s.TencentSecretKey = v; break;
                case "volc": if (idx == 0) s.VolcKeyId = v; else s.VolcKeySecret = v; break;
                case "deeplx": if (idx == 0) s.DeeplxUrl = v; else s.DeeplxToken = v; break;
                case "webster": s.WebsterKey = v; break;
            }
        }

        private Panel BuildApiPage()
        {
            var p = NewPage();
            p.AutoScroll = true;
            _apiInner = new Panel { Location = new Point(0, 0), Size = new Size(760, 1500), BackColor = SkinMode.IsAcrylic ? SkinMode.Key : (SkinMode.IsFrosted ? Palette.Bg : Color.White) };
            p.Controls.Add(_apiInner);

            _apiInner.Controls.Add(Ui.Lbl("自备密钥引擎", 46, 18, 12.5f, Palette.Main, true));
            _apiInner.Controls.Add(Ui.Lbl("填了哪家、哪家才出现在引擎胶囊里；密钥只加密存本机。点开一行填写，再点测试连接验证。", 170, 22, 9.5f, Palette.Sub));

            foreach (var def in ApiDefs) _apiRows.Add(BuildApiRow(def));

            _apiTitle2 = Ui.Lbl("免费接口地址", 46, 0, 12.5f, Palette.Main, true);
            _apiInner.Controls.Add(_apiTitle2);
            _apiFreePanel = new Panel { Location = new Point(46, 0), Size = new Size(668, 128), BackColor = Color.Transparent };
            _apiFreePanel.Controls.Add(Ui.Lbl("LibreTranslate", 0, 4, 10f, Palette.Sub));
            _txtLibreUrl = new TextBox { Location = new Point(130, 0), Width = 420, Font = FontTheme.Of(10f) };
            _apiFreePanel.Controls.Add(_txtLibreUrl);
            _apiFreePanel.Controls.Add(Ui.Lbl("官方公共实例现需付费 Key；填自建或其他可用实例地址后，它才会进引擎胶囊。", 130, 28, 9f, Palette.Sub));
            _apiFreePanel.Controls.Add(Ui.Lbl("Lingva", 0, 62, 10f, Palette.Sub));
            _txtLingvaUrl = new TextBox { Location = new Point(130, 58), Width = 420, Font = FontTheme.Of(10f) };
            _apiFreePanel.Controls.Add(_txtLingvaUrl);
            _apiFreePanel.Controls.Add(Ui.Lbl("换成可用实例地址后才会进引擎胶囊（默认公共实例目前不稳定）。", 130, 86, 9f, Palette.Sub));
            _apiInner.Controls.Add(_apiFreePanel);

            _apiTitle3 = Ui.Lbl("默认词典", 46, 0, 12.5f, Palette.Main, true);
            _apiInner.Controls.Add(_apiTitle3);
            _apiDictPanel = new Panel { Location = new Point(46, 0), Size = new Size(668, 44), BackColor = Color.Transparent };
            _cmbDictDefault = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(0, 4), Width = 220, Font = FontTheme.Of(10.5f) };
            foreach (var d in MultiDict.All) _cmbDictDefault.Items.Add(d.Name);
            _apiDictPanel.Controls.Add(_cmbDictDefault);
            _apiDictPanel.Controls.Add(Ui.Lbl("查词小窗的默认来源（与词的语言类别相符时生效，点选切换会记住上次用的来源）", 236, 8, 9.5f, Palette.Sub));
            _apiInner.Controls.Add(_apiDictPanel);

            _apiTitle4 = Ui.Lbl("翻译交叉核对", 46, 0, 12.5f, Palette.Main, true);
            _apiInner.Controls.Add(_apiTitle4);
            _apiCrossPanel = new Panel { Location = new Point(46, 0), Size = new Size(668, 108), BackColor = Color.Transparent };
            _chkCross = Chk(_apiCrossPanel, "启用交叉核对（出译文后用第二家免费引擎同翻一遍，数字/否定/译法分歧标在结果里）", 0, 2);
            _chkCrossBack = Chk(_apiCrossPanel, "回译抽查（仅对存疑句：把主译文翻回原文语言比对，更慢）", 0, 34);
            var ccNote = new Label
            {
                Location = new Point(0, 66), Width = 660, Height = 40, ForeColor = Palette.Sub,
                Font = FontTheme.Of(9f), BackColor = Color.Transparent,
                Text = "核对只提示存疑、不自动改译文，也不显示「准确率」：两家一致不等于正确，两家不一致也不等于主译文错。"
            };
            _apiCrossPanel.Controls.Add(ccNote);
            _apiInner.Controls.Add(_apiCrossPanel);

            // 首行默认展开百度（与静态稿 20261104-07 一致的行为：进来能看到一行展开的样子）
            LayoutApiPage();
            return p;
        }

        private ApiRow BuildApiRow(ApiDef def)
        {
            var row = new ApiRow { Def = def };
            row.Header = new RoundPanel
            {
                Location = new Point(46, 0), Size = new Size(668, 40), Radius = 8,
                Fill = (SkinMode.IsFrosted || SkinMode.IsLucide) ? Palette.PillBg : Color.FromArgb(246, 247, 249), NoEdge = true, Cursor = Cursors.Hand
            };
            row.Header.Controls.Add(new Label
            {
                Text = def.Title, Location = new Point(16, 0), Size = new Size(180, 40),
                TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
                ForeColor = Palette.Main, BackColor = Color.Transparent
            });
            row.StatusLbl = new Label
            {
                Text = "未填写", Location = new Point(300, 0), Size = new Size(170, 40),
                TextAlign = ContentAlignment.MiddleRight, Font = FontTheme.Of(9.5f),
                ForeColor = Palette.Sub, BackColor = Color.Transparent
            };
            row.Header.Controls.Add(row.StatusLbl);
            if (def.ApplyUrl != null)
            {
                var apply = new Label
                {
                    Text = "官网申请 ›", Location = new Point(478, 0), Size = new Size(110, 40),
                    TextAlign = ContentAlignment.MiddleRight, Font = FontTheme.Of(9.5f),
                    ForeColor = Palette.Accent, BackColor = Color.Transparent, Cursor = Cursors.Hand
                };
                apply.Click += (s, e) => { try { Process.Start(def.ApplyUrl); } catch { } };
                row.Header.Controls.Add(apply);
            }
            else
            {
                row.Header.Controls.Add(new Label
                {
                    Text = def.ApplyText, Location = new Point(478, 0), Size = new Size(110, 40),
                    TextAlign = ContentAlignment.MiddleRight, Font = FontTheme.Of(9.5f),
                    ForeColor = Palette.Sub, BackColor = Color.Transparent
                });
            }
            row.Chevron = new Label
            {
                Text = "展开 ▾", Location = new Point(596, 0), Size = new Size(60, 40),
                TextAlign = ContentAlignment.MiddleCenter, Font = FontTheme.Of(9.5f),
                ForeColor = Palette.Sub, BackColor = Color.Transparent, Cursor = Cursors.Hand
            };
            row.Header.Controls.Add(row.Chevron);
            EventHandler toggle = (s, e) =>
            {
                row.Expanded = !row.Expanded;
                row.Body.Visible = row.Expanded;
                row.Chevron.Text = row.Expanded ? "收起 ▴" : "展开 ▾";
                LayoutApiPage();
            };
            row.Header.Click += toggle;
            row.Header.Controls[0].Click += toggle;
            row.StatusLbl.Click += toggle;
            row.Chevron.Click += toggle;
            _apiInner.Controls.Add(row.Header);

            int bodyH = def.FieldLabels.Length * 38 + 46 + 36;
            row.BodyH = bodyH;
            row.Body = new Panel { Location = new Point(46, 0), Size = new Size(668, bodyH), BackColor = Color.Transparent, Visible = false };
            row.Boxes = new TextBox[def.FieldLabels.Length];
            for (int i = 0; i < def.FieldLabels.Length; i++)
            {
                row.Body.Controls.Add(Ui.Lbl(def.FieldLabels[i], 16, 12 + i * 38, 10f, Palette.Sub));
                var tb = new TextBox
                {
                    Location = new Point(150, 8 + i * 38), Width = 380,
                    Font = FontTheme.Of(10f), UseSystemPasswordChar = def.FieldSecret[i]
                };
                tb.TextChanged += (s, e) => RefreshApiRowStatus(row);
                row.Body.Controls.Add(tb);
                row.Boxes[i] = tb;
            }
            int btnY = def.FieldLabels.Length * 38 + 8;
            var btnTest = Ui.PillButton("测试连接", 150, btnY, 104, 32, Color.White, Palette.Main);
            btnTest.WireClickThrough(async (s, e) =>
            {
                row.ResultLbl.ForeColor = Palette.Sub;
                row.ResultLbl.Text = "测试中…";
                var eff = SyncService.EffectiveCreds(_store.Settings);
                for (int i = 0; i < row.Boxes.Length; i++) SetCred(eff, def.Id, i, row.Boxes[i].Text.Trim());
                try
                {
                    if (def.IsDict)
                    {
                        var r = await MultiDict.Lookup(def.Id, "serendipity", eff);
                        row.ResultLbl.ForeColor = Color.FromArgb(30, 140, 70);
                        row.ResultLbl.Text = "✓ 连通：serendipity 返回 " + r.Definitions.Count + " 条释义";
                    }
                    else
                    {
                        string t = await MultiEngine.Translate(def.Id, "你好，世界", "zh", "en", eff);
                        row.ResultLbl.ForeColor = Color.FromArgb(30, 140, 70);
                        row.ResultLbl.Text = "✓ 连通：你好，世界 → " + t;
                    }
                }
                catch (Exception ex)
                {
                    row.ResultLbl.ForeColor = Color.FromArgb(200, 50, 40);
                    row.ResultLbl.Text = "✗ " + ex.Message;
                }
                RefreshApiRowStatus(row);
            });
            row.Body.Controls.Add(btnTest);
            row.ResultLbl = new Label
            {
                Location = new Point(150, btnY + 38), Width = 500, Height = 34, ForeColor = Palette.Sub,
                Font = FontTheme.Of(9.5f), BackColor = Color.Transparent
            };
            row.Body.Controls.Add(row.ResultLbl);
            _apiInner.Controls.Add(row.Body);
            return row;
        }

        private void RefreshApiRowStatus(ApiRow row)
        {
            if (row == null || row.Boxes == null) return;
            var eff = SyncService.EffectiveCreds(_store.Settings);
            for (int i = 0; i < row.Boxes.Length; i++) SetCred(eff, row.Def.Id, i, row.Boxes[i].Text.Trim());
            bool ok = row.Def.IsDict ? MultiDict.IsConfigured(row.Def.Id, eff) : MultiEngine.IsConfigured(row.Def.Id, eff);
            row.StatusLbl.Text = ok ? "已填写" : "未填写";
            row.StatusLbl.ForeColor = ok ? Color.FromArgb(30, 140, 70) : Palette.Sub;
        }

        private void LayoutApiPage()
        {
            if (_apiInner == null) return;
            int y = 56;
            foreach (var row in _apiRows)
            {
                row.Header.Location = new Point(46, y);
                y += 40;
                if (row.Expanded)
                {
                    row.Body.Location = new Point(46, y);
                    y += row.BodyH;
                }
                y += 6;
            }
            y += 12;
            _apiTitle2.Location = new Point(46, y); y += 36;
            _apiFreePanel.Location = new Point(46, y); y += 128 + 12;
            _apiTitle3.Location = new Point(46, y); y += 36;
            _apiDictPanel.Location = new Point(46, y); y += 44 + 12;
            _apiTitle4.Location = new Point(46, y); y += 36;
            _apiCrossPanel.Location = new Point(46, y); y += 108 + 16;
            _apiInner.Size = new Size(760, y);
        }

        private void LoadApiFields()
        {
            var s = _store.Settings;
            var eff = SyncService.EffectiveCreds(s);
            foreach (var row in _apiRows)
                for (int i = 0; i < row.Boxes.Length; i++)
                    row.Boxes[i].Text = GetCredPlain(eff, row.Def.Id, i) ?? "";
            foreach (var row in _apiRows) RefreshApiRowStatus(row);
            _txtLibreUrl.Text = s.LibreUrl ?? "";
            _txtLingvaUrl.Text = s.LingvaUrl ?? "";
            int di = 0;
            for (int i = 0; i < MultiDict.All.Length; i++)
                if (MultiDict.All[i].Id == s.DictSource) { di = i; break; }
            _cmbDictDefault.SelectedIndex = di;
            _chkCross.Checked = s.CrossCheckEnabled;
            _chkCrossBack.Checked = s.CrossCheckBack;
        }

        private void CollectApiFields()
        {
            var s = _store.Settings;
            foreach (var row in _apiRows)
                for (int i = 0; i < row.Boxes.Length; i++)
                {
                    string v = row.Boxes[i].Text.Trim();
                    // DeepLX 的接口地址不是秘密、引擎按明文读，不走加密
                    if (row.Def.Id == "deeplx" && i == 0) SetCred(s, row.Def.Id, i, v);
                    else SetCred(s, row.Def.Id, i, SyncService.Protect(v));
                }
            s.LibreUrl = string.IsNullOrWhiteSpace(_txtLibreUrl.Text) ? "https://libretranslate.com" : _txtLibreUrl.Text.Trim();
            s.LingvaUrl = string.IsNullOrWhiteSpace(_txtLingvaUrl.Text) ? "https://lingva.lunar.icu" : _txtLingvaUrl.Text.Trim();
            int di = _cmbDictDefault.SelectedIndex;
            if (di >= 0 && di < MultiDict.All.Length) s.DictSource = MultiDict.All[di].Id;
            s.CrossCheckEnabled = _chkCross.Checked;
            s.CrossCheckBack = _chkCrossBack.Checked;
        }

        // ---------------- 关于页 ----------------

        private Panel BuildAboutPage()
        {
            var p = NewPage();
            p.Controls.Add(Ui.Lbl("WinTranslator", 46, 30, 16f, Palette.Accent, true));
            p.Controls.Add(Ui.Lbl("版本 v1.8.2 · 绿色免安装", 46, 66, 10.5f, Palette.Main));
            var body = new Label
            {
                Location = new Point(46, 106), Width = 640, Height = 120, ForeColor = Palette.Sub,
                Font = new Font("Microsoft YaHei UI", 10f), BackColor = Color.Transparent,
                Text = "翻译引擎：有道 / 必应（网页免费接口，自动切换）＋谷歌 gtx / MyMemory（免费接口）＋智谱 GLM-4-Flash（免费模型，需自备 Key）。\n" +
                    "谷歌接口在国内网络可能直连不通；MyMemory 匿名版有每日额度。选定引擎失败时会自动回退有道/必应并在状态栏注明实际引擎。\n" +
                       "历史与单词本只保存在本机：" + _store.DataDir + "\n" +
                       "截图文字识别在本机离线完成。"
            };
            p.Controls.Add(body);
            var btnTest = Ui.PillButton("截图翻译自检", 46, 196, 140, 34, Color.White, Palette.Main);
            btnTest.WireClickThrough(async (s, e) =>
            {
                btnTest.Enabled = false;
                try
                {
                    var steps = await SelfTest.Run(true, _store.Settings.GlmApiKey, _store.Settings.EngineIndex);
                    MessageBox.Show(SelfTest.Format(steps), "截图翻译自检", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("自检异常：" + ex.Message, "截图翻译自检", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                finally { btnTest.Enabled = true; }
            });
            p.Controls.Add(btnTest);
            var btnDir = Ui.PillButton("打开数据文件夹", 46, 240, 140, 34, Color.White, Palette.Main);
            btnDir.WireClickThrough((s, e) => { try { Process.Start("explorer.exe", _store.DataDir); } catch { } });
            p.Controls.Add(btnDir);
            return p;
        }

        // ---------------- 读写 ----------------

        public void LoadFromSettings()
        {
            var s = _store.Settings;
            _fontSel = Array.IndexOf(FontSizes, s.FontSize);
            if (_fontSel < 0) _fontSel = 1;
            var fontTexts = new string[FontSizes.Length];
            for (int i = 0; i < FontSizes.Length; i++) fontTexts[i] = FontSizes[i].ToString();
            RefreshRow(_fontOpts, fontTexts, _fontSel);
            _chkAutoStart.Checked = s.AutoStart;
            _chkStartMin.Checked = s.StartMinimized;
            _chkTray.Checked = s.MinimizeToTrayOnClose;
            _chkBall.Checked = s.BallEnabled;
            _chkTopMost.Checked = s.MainTopMost;
            _chkClipboard.Checked = s.ClipboardListen;
            _engineSel = Math.Min(5, Math.Max(0, s.EngineIndex));
            RefreshRow(_engineOpts, EngineNames, _engineSel);
            _skinSel = Array.IndexOf(SkinKeys, SkinMode.Normalize(s.Skin));
            if (_skinSel < 0) _skinSel = 0;
            RefreshRow(_skinOpts, SkinNames, _skinSel);
            _txtGlmKey.Text = s.GlmApiKey ?? "";
            _chkHideUi.Checked = s.ScreenshotHideUi != false;
            _cmbShotDelay.SelectedIndex = s.ScreenshotDelaySec >= 5 ? 2 : (s.ScreenshotDelaySec >= 3 ? 1 : 0);
            _chkShotCtrl.Checked = s.HotkeyCtrl; _chkShotAlt.Checked = s.HotkeyAlt; _chkShotShift.Checked = s.HotkeyShift;
            _cmbShotKey.SelectedItem = s.HotkeyKey; if (_cmbShotKey.SelectedIndex < 0) _cmbShotKey.SelectedItem = "S";
            _chkMiniCtrl.Checked = s.MiniHkCtrl; _chkMiniAlt.Checked = s.MiniHkAlt; _chkMiniShift.Checked = s.MiniHkShift;
            _cmbMiniKey.SelectedItem = s.MiniHkKey; if (_cmbMiniKey.SelectedIndex < 0) _cmbMiniKey.SelectedItem = "M";
            _lblHkStatus.Text = _hotkeyStatus != null ? _hotkeyStatus() : "";
            _uiFontSel = Array.IndexOf(UiFontKeys, s.UiFontFamily);
            if (_uiFontSel < 0) _uiFontSel = 0;
            RefreshRow(_uiFontOpts, UiFontNames, _uiFontSel);
            _uiScaleSel = Math.Min(2, Math.Max(0, s.UiFontScale));
            RefreshRow(_uiScaleOpts, UiScaleNames, _uiScaleSel);
            LoadSyncFields();
            LoadApiFields();
            _engineSelLoaded = _engineSel;
            _lblSaved.Text = "";
        }

        private void SaveAll()
        {
            var s = _store.Settings;
            s.FontSize = FontSizes[_fontSel];
            s.UiFontFamily = UiFontKeys[_uiFontSel];
            s.UiFontScale = _uiScaleSel;
            CollectSyncFields();
            s.AutoStart = _chkAutoStart.Checked;
            s.StartMinimized = _chkStartMin.Checked;
            s.MinimizeToTrayOnClose = _chkTray.Checked;
            s.BallEnabled = _chkBall.Checked;
            s.MainTopMost = _chkTopMost.Checked;
            s.ClipboardListen = _chkClipboard.Checked;
            s.EngineIndex = _engineSel;
            // v1.8.0：只有用户真改了默认引擎才同步 EnginePrefer，避免把胶囊里选的新引擎（如 DeepL）冲回旧值
            if (_engineSel != _engineSelLoaded)
                s.EnginePrefer = new[] { "auto", "youdao", "bing", "glm", "gtx", "mymemory" }[_engineSel];
            CollectApiFields();
            s.Skin = SkinKeys[_skinSel];
            s.GlmApiKey = _txtGlmKey.Text.Trim();
            s.ScreenshotHideUi = _chkHideUi.Checked;
            s.ScreenshotDelaySec = _cmbShotDelay.SelectedIndex == 2 ? 5 : (_cmbShotDelay.SelectedIndex == 1 ? 3 : 0);
            s.HotkeyCtrl = _chkShotCtrl.Checked; s.HotkeyAlt = _chkShotAlt.Checked; s.HotkeyShift = _chkShotShift.Checked;
            s.HotkeyKey = (string)_cmbShotKey.SelectedItem;
            s.MiniHkCtrl = _chkMiniCtrl.Checked; s.MiniHkAlt = _chkMiniAlt.Checked; s.MiniHkShift = _chkMiniShift.Checked;
            s.MiniHkKey = (string)_cmbMiniKey.SelectedItem;
            _store.SaveSettings();
            if (_saved != null) _saved();
            _lblHkStatus.Text = _hotkeyStatus != null ? _hotkeyStatus() : "";
            _lblSaved.Text = "已保存";
            _lblSaved.Location = new Point(ClientSize.Width - 158 - _lblSaved.PreferredWidth - 8, ClientSize.Height - 48);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SkinFx.Apply(this);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Color.FromArgb(225, 227, 231), 1))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Close();
        }
    }
}
