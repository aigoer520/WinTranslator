using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WinTranslator
{
    /// <summary>
    /// v1.2.0 点词查词独立弹窗（修订一：不用贴词气泡，用单独小窗口）。
    /// v1.8.0：顶部加来源切换条（10 家词典，一次看一家，切换才请求、结果按词缓存）；
    /// 某家查不到/失败就在窗内明示原因，不静默跳别家。内容：音标、词典释义、一条例句；可收藏进单词本。
    /// </summary>
    public class DictPopupForm : Form
    {
        private readonly Store _store;
        private readonly Action<string> _status;
        private readonly Action _onFavoriteAdded;
        private readonly Action<string> _speak; // v1.3.1：朗读（MainForm 注入，可空）
        private readonly RoundPanel _card;
        private string _currentWord = "";
        private string _activeSource = "youdao";  // v1.8.0 当前词典来源 id
        private string _manualSource;             // v1.8.0 本次会话内用户点选的来源（优先于自动规则）
        private readonly Dictionary<string, DictResult> _cache = new Dictionary<string, DictResult>();
        internal bool LoadedOk; // 测试/调试：最近一次查词是否成功填充
        internal string ActiveSourceForTest { get { return _activeSource; } }

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public DictPopupForm(Store store, Action<string> status, Action onFavoriteAdded, Action<string> speak = null)
        {
            _store = store;
            _status = status;
            _onFavoriteAdded = onFavoriteAdded;
            _speak = speak;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            KeyPreview = true;
            BackColor = SkinMode.IsAcrylic ? SkinMode.Key : (SkinMode.IsFrosted ? Palette.Frame : Color.FromArgb(226, 228, 232));
            ClientSize = new Size(410, 140);
            Shown += (s, e) => GlassMotion.FadeIn(this);
            Shown += (s, e) => FontTheme.ApplyTo(this); // v1.5.3 界面字体/字号档
            FormClosing += (s, e) => GlassMotion.TryFadeOut(this, e);
            _card = new RoundPanel { Location = new Point(3, 3), Size = new Size(404, 134), Radius = 12, Fill = Palette.Card, Edge = SkinMode.IsFrosted ? Palette.Border : Color.FromArgb(198, 202, 208) };
            Controls.Add(_card);
            MouseDown += DragHandler;
            _card.MouseDown += DragHandler;
        }

        private void DragHandler(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SkinFx.Apply(this);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Close();
            base.OnKeyDown(e);
        }

        public async void ShowFor(string word, Point screenPt)
        {
            _currentWord = word;
            _activeSource = ResolveSource(word);
            LoadedOk = false;
            BuildShell(word, "查询中…");
            var wa = Screen.FromPoint(screenPt).WorkingArea;
            int x = Math.Min(screenPt.X + 10, wa.Right - Width - 6);
            int y = Math.Min(screenPt.Y + 16, wa.Bottom - Height - 6);
            Location = new Point(Math.Max(wa.Left + 4, x), Math.Max(wa.Top + 4, y));
            if (!Visible) Show();
            BringToFront();
            await LoadCurrent();
        }

        /// <summary>v1.8.0 来源解析：会话内点选优先；其次设置里记住的来源（须与词的语言类别相符）；再按语言规则（中文词→汉典、英文词→有道）。</summary>
        private string ResolveSource(string word)
        {
            if (!string.IsNullOrEmpty(_manualSource)) return _manualSource;
            bool zh = word != null && System.Text.RegularExpressions.Regex.IsMatch(word, "[一-鿿]");
            string saved = _store != null && _store.Settings != null ? _store.Settings.DictSource : null;
            var info = !string.IsNullOrEmpty(saved) ? MultiDict.InfoOf(saved) : null;
            if (info != null && info.Chinese == zh) return saved;
            return zh ? "zdic" : "youdao";
        }

        private async Task LoadCurrent()
        {
            string word = _currentWord;
            string source = _activeSource;
            string key = source + "\n" + (word ?? "").ToLowerInvariant();
            DictResult cached;
            if (_cache.TryGetValue(key, out cached))
            {
                Fill(cached);
                LoadedOk = true;
                return;
            }
            try
            {
                var eff = _store != null ? SyncService.EffectiveCreds(_store.Settings) : null;
                var r = await MultiDict.Lookup(source, word, eff);
                if (IsDisposed || _currentWord != word || _activeSource != source) return;
                _cache[key] = r;
                Fill(r);
                LoadedOk = true;
            }
            catch (Exception ex)
            {
                if (IsDisposed || _currentWord != word || _activeSource != source) return;
                var info = MultiDict.InfoOf(source);
                BuildShell(word, (info != null ? info.Name : source) + "：" + ex.Message);
            }
        }

        /// <summary>v1.8.0 来源切换条（静态稿 20261104-06）：灰底条内两行白胶囊，选中红底白字；点选即换源重查并记住。</summary>
        private void AddSourceStrip()
        {
            var strip = new RoundPanel
            {
                Location = new Point(20, 46), Size = new Size(364, 62), Radius = 10,
                Fill = Palette.PillBg, NoEdge = true
            };
            strip.MouseDown += DragHandler;
            _card.Controls.Add(strip);
            var rows = new[]
            {
                new[] { "youdao", "bingdict", "cambridge", "webster", "urban" },
                new[] { "vocabulary", "caiyun", "zdic", "guoyu", "liangan" }
            };
            int y = 46 + 6;
            foreach (var row in rows)
            {
                int x = 20 + 8;
                foreach (var id in row)
                {
                    var info = MultiDict.InfoOf(id);
                    bool active = id == _activeSource;
                    var f = FontTheme.Of(9.5f, active ? FontStyle.Bold : FontStyle.Regular);
                    int tw = TextRenderer.MeasureText(info.Short, f, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                    var chip = new RoundPanel
                    {
                        Location = new Point(x, y), Size = new Size(tw + 18, 24), Radius = 12,
                        Fill = active ? Palette.Accent : Color.White, NoEdge = !active, Edge = Palette.Border,
                        Cursor = Cursors.Hand
                    };
                    var lbl = new Label
                    {
                        Text = info.Short, Location = new Point(0, 0), Size = chip.Size,
                        TextAlign = ContentAlignment.MiddleCenter, Font = f,
                        ForeColor = active ? Color.White : Palette.Main, BackColor = Color.Transparent
                    };
                    chip.Controls.Add(lbl);
                    string captured = id;
                    EventHandler onClick = (s, e) =>
                    {
                        if (_activeSource == captured) return;
                        _manualSource = captured;
                        _activeSource = captured;
                        if (_store != null) { _store.Settings.DictSource = captured; _store.SaveSettings(); }
                        BuildShell(_currentWord, "查询中…");
                        LoadCurrent();
                    };
                    chip.Click += onClick;
                    lbl.Click += onClick;
                    _card.Controls.Add(chip);
                    chip.BringToFront();
                    x += chip.Width + 6;
                }
                y += 28;
            }
        }

        private Label Wrap(string text, int x, int y, int w, float size, Color color, bool bold = false)
        {
            var f = FontTheme.Of(size, bold);
            int h = TextRenderer.MeasureText(text, f, new Size(w, int.MaxValue), TextFormatFlags.WordBreak).Height;
            var lbl = new Label
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, Math.Max(h, (int)size + 8)),
                Font = f, ForeColor = color, BackColor = Color.Transparent
            };
            lbl.MouseDown += DragHandler;
            return lbl;
        }

        /// <summary>v1.7.2 可选取文本：只读无边 TextBox（拖选＋Ctrl+C），量高与定位补偿沿单词本
        /// MakeSelectableText 口径（TextRenderer WordBreak 测高、左移 3 补 TextBox 内边距）。
        /// 与 Wrap 的区别：不挂 DragHandler——文字区让给选取，拖窗口改用标题/卡片空白处（静态稿已向用户说明）。</summary>
        private TextBox SelText(string text, int x, int y, int w, float size, Color color, Color back, out int height)
        {
            var f = FontTheme.Of(size);
            int h = TextRenderer.MeasureText(text ?? "", f, new Size(w, int.MaxValue), TextFormatFlags.WordBreak).Height;
            var tb = new TextBox
            {
                Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, WordWrap = true,
                BackColor = back, ForeColor = color, Font = f, Text = text ?? "",
                Location = new Point(x - 3, y - 2), Width = w + 6
            };
            tb.Height = Math.Max(h + 4, (int)size + 12);
            height = Math.Max(h, (int)size + 8);
            return tb;
        }

        private void BuildShell(string word, string note)
        {
            _card.Controls.Clear();
            var title = new Label
            {
                Text = word, AutoSize = true, Location = new Point(20, 12),
                Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold), ForeColor = Palette.Main, BackColor = Color.Transparent
            };
            title.MouseDown += DragHandler;
            _card.Controls.Add(title);
            var close = new IconBtn { Kind = "close", Ghost = true, Size = new Size(26, 26), Location = new Point(342, 8) };
            close.Click += (s, e) => Close();
            _card.Controls.Add(close);
            AddSourceStrip();
            var noteLbl = Wrap(note, 20, 118, 364, 10f, Palette.Sub);
            _card.Controls.Add(noteLbl);
            int bottom = 118 + noteLbl.Height + 6;
            // v1.8.0：剑桥被拦/查不到时，给一条浏览器打开的降级路（方案口径）
            if (_activeSource == "cambridge" && note != "查询中…")
            {
                var link = new RoundPanel
                {
                    Location = new Point(20, bottom), Size = new Size(190, 26), Radius = 8,
                    Fill = Palette.PillBg, NoEdge = true, Cursor = Cursors.Hand
                };
                var linkLbl = new Label
                {
                    Text = "在浏览器打开剑桥词典", Location = new Point(0, 0), Size = link.Size,
                    TextAlign = ContentAlignment.MiddleCenter, Font = FontTheme.Of(9.5f),
                    ForeColor = Palette.Accent, BackColor = Color.Transparent
                };
                link.Controls.Add(linkLbl);
                string w = word;
                EventHandler open = (s, e) =>
                {
                    try
                    {
                        System.Diagnostics.Process.Start("https://dictionary.cambridge.org/dictionary/english-chinese-simplified/" + Uri.EscapeDataString(w ?? ""));
                    }
                    catch { }
                };
                link.Click += open;
                linkLbl.Click += open;
                _card.Controls.Add(link);
                bottom += 32;
            }
            SetHeight(bottom + 10);
        }

        private void Fill(DictResult r)
        {
            _card.Controls.Clear();
            var title = new Label
            {
                Text = r.Word, AutoSize = true, Location = new Point(20, 12),
                Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold), ForeColor = Palette.Main, BackColor = Color.Transparent
            };
            title.MouseDown += DragHandler;
            _card.Controls.Add(title);
            var close = new IconBtn { Kind = "close", Ghost = true, Size = new Size(26, 26), Location = new Point(342, 8) };
            close.Click += (s, e) => Close();
            _card.Controls.Add(close);
            // v1.3.1：朗读此词（查到结果才出现；标签不挂 DragHandler 防吞 Click）
            if (_speak != null)
            {
                var spk = new RoundPanel
                {
                    Location = new Point(262, 10), Size = new Size(84, 28), Radius = 8,
                    Fill = (SkinMode.IsFrosted || SkinMode.IsLucide) ? Palette.PillBg : Color.FromArgb(253, 236, 233), NoEdge = true, Cursor = Cursors.Hand
                };
                spk.Controls.Add(new IconGlyph { Kind = "speaker", IconColor = Palette.Accent, Location = new Point(10, 5), Size = new Size(18, 18) });
                var spkLbl = new Label
                {
                    Text = "朗读", Location = new Point(32, 0), Size = new Size(48, 28), TextAlign = ContentAlignment.MiddleLeft,
                    Font = new Font("Microsoft YaHei UI", 9.5f), ForeColor = Palette.Accent, BackColor = Color.Transparent
                };
                spk.Controls.Add(spkLbl);
                EventHandler read = (s2, e2) => { try { _speak(_currentWord); } catch { } };
                spk.Click += read;
                spkLbl.Click += read;
                _card.Controls.Add(spk);
            }
            AddSourceStrip();
            int y = 118;
            // 中文词典来源的两个读音槽是拼音/注音，英文词典才是美/英音标
            var srcInfo0 = MultiDict.InfoOf(_activeSource);
            bool zhSrc = srcInfo0 != null && srcInfo0.Chinese;
            string phone = "";
            if (r.UsPhone.Length > 0) phone += (zhSrc ? "拼音 " : "美 ") + r.UsPhone + "  ";
            if (r.UkPhone.Length > 0) phone += (zhSrc ? "注音 " : "英 ") + r.UkPhone;
            if (phone.Length > 0)
            {
                int phH;
                _card.Controls.Add(SelText(phone.Trim(), 20, y, 364, 9.5f, Palette.Sub, Palette.Card, out phH));
                y += 24;
            }
            if (r.Definitions.Count > 0)
            {
                int defH;
                var def = SelText(string.Join("\n", r.Definitions.ToArray()), 20, y, 364, 11f, Palette.Main, Palette.Card, out defH);
                _card.Controls.Add(def);
                y += defH + 8;
            }
            else
            {
                int ndH;
                _card.Controls.Add(SelText("（未查到词典释义）", 20, y, 364, 10f, Palette.Sub, Palette.Card, out ndH));
                y += 26;
            }
            if (r.Examples.Count > 0)
            {
                var div = new Panel { Location = new Point(20, y), Size = new Size(364, 1), BackColor = Palette.Border };
                _card.Controls.Add(div);
                y += 10;
                _card.Controls.Add(Wrap("例句", 20, y, 364, 9.5f, Palette.Sub, true));
                y += 22;
                // v1.5.3 美化：例句改浅灰内嵌块（与单词本同语言）
                // v1.7.2：例句中英都换可选取文本框，底色直接给内嵌块填充色（同原标签口径）
                int enH, zhH;
                var en = SelText(r.Examples[0].En, 32, y + 8, 340, 10.5f, Palette.Main, Palette.PillBg, out enH);
                var zh = SelText(r.Examples[0].Zh, 32, y + 8 + enH + 2, 340, 9.5f, Color.FromArgb(90, 96, 106), Palette.PillBg, out zhH);
                var inset = new RoundPanel
                {
                    Location = new Point(20, y), Size = new Size(364, 8 + enH + 2 + zhH + 8),
                    Radius = 10, Fill = Palette.PillBg, NoEdge = true
                };
                // 添加顺序即 z 序：先加文字、最后加内嵌块垫底，否则不透明块面会盖住文字（实拍自查发现）
                _card.Controls.Add(en);
                _card.Controls.Add(zh);
                _card.Controls.Add(inset);
                y += inset.Height + 10;
            }
            // 底部：收藏此词 + 来源
            bool already = false;
            foreach (var f in _store.Favorites)
                if (string.Equals(f.Src, r.Word, StringComparison.OrdinalIgnoreCase)) { already = true; break; }
            var chip = new RoundPanel
            {
                Location = new Point(20, y), Size = new Size(112, 28), Radius = 8,
                Fill = already ? Palette.PillBg : ((SkinMode.IsFrosted || SkinMode.IsLucide) ? Palette.PillBg : Color.FromArgb(253, 236, 233)), NoEdge = true,
                Cursor = already ? Cursors.Default : Cursors.Hand
            };
            if (!already) chip.Controls.Add(new IconGlyph { Kind = "star", IconColor = Palette.Accent, Location = new Point(9, 5), Size = new Size(18, 18) });
            var chipLbl = new Label
            {
                Text = already ? "已在单词本" : "收藏此词", Location = new Point(already ? 0 : 30, 0), Size = new Size(already ? 112 : 80, 28), TextAlign = already ? ContentAlignment.MiddleCenter : ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 9.5f), ForeColor = already ? Palette.Sub : Palette.Accent, BackColor = Color.Transparent
            };
            // 注意：此标签绝不能挂 DragHandler——Windows 下 MouseDown 一进标题栏拖动循环，Click 会被吞掉（真机点不动的元凶）
            chip.Controls.Add(chipLbl);
            if (!already)
            {
                var srcInfo = MultiDict.InfoOf(_activeSource);
                string srcName = srcInfo != null ? srcInfo.Name : "有道词典";
                EventHandler add = (s, e) =>
                {
                    string meaning = r.Definitions.Count > 0 ? r.Definitions[0] : "";
                    _store.AddFavorite(new Entry
                    {
                        Src = r.Word, Dst = meaning, SrcLang = "en", DstLang = "zh", Provider = srcName,
                        PhoneticUk = r.UkPhone, PhoneticUs = r.UsPhone,
                        Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    });
                    if (_status != null) _status("已收藏 " + r.Word);
                    if (_onFavoriteAdded != null) _onFavoriteAdded();
                    Fill(r); // 刷新为"已在单词本"
                };
                chip.Click += add;
                chipLbl.Click += add;
            }
            _card.Controls.Add(chip);
            var srcInfo2 = MultiDict.InfoOf(_activeSource);
            var srcLbl = Ui.Lbl(srcInfo2 != null ? srcInfo2.Name : "有道词典", 0, y + 4, 8.5f, Palette.Sub);
            srcLbl.Location = new Point(404 - 20 - srcLbl.PreferredWidth, y + 4);
            _card.Controls.Add(srcLbl);
            SetHeight(y + 28 + 14);
        }

        private void SetHeight(int cardH)
        {
            _card.Size = new Size(404, cardH);
            ClientSize = new Size(410, cardH + 6);
            // v1.6.1：内容增高后把窗体拉回工作区内（查词点靠屏幕下边、带例句变高时原先会探出屏幕外；用户真机反馈③）
            try
            {
                var scr = Screen.FromControl(this);
                if (scr != null)
                {
                    var wa = scr.WorkingArea;
                    int nx = Location.X, ny = Location.Y;
                    if (nx + Width > wa.Right - 4) nx = wa.Right - Width - 4;
                    if (ny + Height > wa.Bottom - 4) ny = wa.Bottom - Height - 4;
                    if (nx < wa.Left + 4) nx = wa.Left + 4;
                    if (ny < wa.Top + 4) ny = wa.Top + 4;
                    if (nx != Location.X || ny != Location.Y) Location = new Point(nx, ny);
                }
            }
            catch { }
        }
    }
}
