using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WinTranslator
{
    /// <summary>
    /// 单词本页面（v1.3.0 改版）：单词/句子页签、排序、复习筛选、按日期分组列表、
    /// 单击展开详情（例句与点词沿用 v1.2.0），以及照有道复习流程做的大卡片视图。
    /// 句子不进入卡片复习；复习状态保存在 Entry 的 ReviewLevel/ReviewCount/LastReview。
    /// </summary>
    public class WordbookPanel : UserControl
    {
        private readonly Store _store;
        private readonly Action<Entry> _fillHome;
        private readonly Action<string> _speak;
        private readonly Action<string> _status;

        /// <summary>所在的页面容器（MainForm 用它控制整页显隐）。</summary>
        public Panel Holder;

        // 例句与点词（v1.2.0 沿用）
        public TranslationEngine Engine;
        public Action<string, Point> WordLookup;
        private Entry _expanded, _loadingFor, _failedFor, _pendingToggle;
        private readonly HashSet<int> _revealed = new HashSet<int>();
        private Entry _meaningRevealedFor; // v1.6.2：词义默认隐藏，仅当前展开条目点「译」圆钮后显示
        private readonly Timer _clickTimer;
        private readonly HashSet<Entry> _phoneLoading = new HashSet<Entry>();

        // v1.3.0 视图状态
        private bool _tabWords = true;
        private bool _sortDesc = true;       // true=新→旧，false=旧→新
        private int _filterMode;             // 0=全部，1=未复习，2=已复习（仅单词页签）
        private int _viewMode;             // 0=列表，1=卡片（v1.3.1 图标钮切换）
        private int _cardIndex;
        private List<Entry> _cardList = new List<Entry>();
        private Entry _cardShownEntry;
        private bool _cardRevealed;

        private readonly RoundPanel _tabWordBtn, _tabSentBtn;
        private readonly Label _tabWordLbl, _tabSentLbl, _sortLbl, _filterLbl;
        private readonly RoundPanel _reviewBtn;
        private readonly Label _reviewLbl;
        private readonly RoundPanel _reviewBadge;
        private readonly Label _reviewBadgeLbl;
        private readonly TextBox _txtSearch;
        private readonly Panel _listHost;
        private readonly Panel _cardHost;
        private readonly Label _lblCount;
        private RoundPanel _btnTxt, _btnCsv;
        private ToolTip _tips; // v1.6.5：详情头「译」图标的悬停提示

        // 大卡片控件
        private readonly RoundPanel _flashCard;
        private readonly Label _cardCounter, _cardMore, _cardWord, _cardPhones, _cardDef, _cardExZh, _cardState, _cardDelete;
        private readonly RoundPanel _cardSpeakBtn, _cardRevealBtn, _cardKnowBtn, _cardDontBtn;
        private readonly Label _cardSpeakLbl, _cardRevealLbl, _cardKnowLbl, _cardDontLbl;
        private readonly WordText _cardExWt;
        private RoundPanel _btnPrev, _btnNext;

        public WordbookPanel(Store store, Action<Entry> fillHome, Action<string> speak, Action<string> status)
        {
            _store = store;
            _fillHome = fillHome;
            _speak = speak;
            _status = status;
            _clickTimer = new Timer { Interval = 260 };
            _clickTimer.Tick += (s, e) =>
            {
                _clickTimer.Stop();
                if (_pendingToggle != null) ToggleExpand(_pendingToggle);
            };

            BackColor = Palette.Bg;
            Dock = DockStyle.None;
            Size = new Size(646, 632);

            Controls.Add(Ui.Lbl("单词本", 22, 16, 15f, Palette.Main, true));

            // 页签：单词 / 句子
            _tabWordBtn = new RoundPanel { Location = new Point(22, 48), Size = new Size(88, 36), Radius = 10, Fill = Palette.SelBg, Edge = Palette.Border, Cursor = Cursors.Hand };
            _tabWordLbl = CenterLbl("单词", _tabWordBtn.Size);
            _tabWordBtn.Controls.Add(_tabWordLbl);
            _tabWordBtn.WireClickThrough((s, e) => SelectTab(true));
            _tabSentBtn = new RoundPanel { Location = new Point(114, 48), Size = new Size(88, 36), Radius = 10, Fill = Palette.Card, Edge = Palette.Border, Cursor = Cursors.Hand };
            _tabSentLbl = CenterLbl("句子", _tabSentBtn.Size);
            _tabSentBtn.Controls.Add(_tabSentLbl);
            _tabSentBtn.WireClickThrough((s, e) => SelectTab(false));
            Controls.AddRange(new Control[] { _tabWordBtn, _tabSentBtn });

            _sortLbl = Ui.Lbl("排序  新→旧 ▾", 220, 56, 10f, Palette.Main);
            _sortLbl.Cursor = Cursors.Hand;
            _sortLbl.Click += (s, e) => { _sortDesc = !_sortDesc; _cardIndex = 0; RefreshAll(); };
            Controls.Add(_sortLbl);
            _filterLbl = Ui.Lbl("筛选  全部 ▾", 354, 56, 10f, Palette.Main);
            _filterLbl.Cursor = Cursors.Hand;
            _filterLbl.Click += (s, e) => { _filterMode = (_filterMode + 1) % 3; _cardIndex = 0; RefreshAll(); };
            Controls.Add(_filterLbl);

            _reviewBtn = new RoundPanel { Location = new Point(526, 48), Size = new Size(104, 36), Radius = 10, Fill = Palette.Card, Edge = Palette.Border, Cursor = Cursors.Hand };
            _reviewLbl = new Label
            {
                Text = "✓ 复习", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold), ForeColor = Palette.Accent, BackColor = Color.Transparent
            };
            _reviewBtn.Controls.Add(_reviewLbl);
            _reviewBtn.WireClickThrough((s, e) => StartReview());
            Controls.Add(_reviewBtn);
            _reviewBadge = new RoundPanel { Location = new Point(606, 40), Size = new Size(30, 18), Radius = 9, Fill = Palette.Accent, NoEdge = true };
            _reviewBadgeLbl = new Label
            {
                Text = "", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ForeColor = Color.White, BackColor = Color.Transparent
            };
            _reviewBadge.Controls.Add(_reviewBadgeLbl);
            Controls.Add(_reviewBadge);
            _reviewBadge.BringToFront();

            // 搜索（保留常显）
            var searchCard = new RoundPanel { Location = new Point(22, 94), Size = new Size(300, 38), Radius = 17, Fill = Palette.Card, Edge = Palette.Border };
            _txtSearch = new TextBox
            {
                BorderStyle = BorderStyle.None, Location = new Point(14, 10), Width = 272,
                Font = new Font("Microsoft YaHei UI", 10f), BackColor = Palette.Card
            };
            _txtSearch.TextChanged += (s, e) => { _cardIndex = 0; RefreshAll(); };
            searchCard.Controls.Add(_txtSearch);
            Controls.Add(searchCard);
            var hint = Ui.Lbl("搜索单词或句子…", 36, 105, 9.5f, Palette.Sub);
            hint.Click += (s, e) => _txtSearch.Focus();
            Controls.Add(hint);
            hint.BringToFront();

            // v1.6.5：列表/卡片切换钮按用户要求整组去除（单词本固定列表；复习从「✓ 复习」进大卡片、点页签回列表）

            Color hostBg = (SkinMode.IsBackdrop || SkinMode.IsAcrylic) ? Color.Transparent : Palette.Bg;
            _listHost = new Panel { Location = new Point(22, 146), Size = new Size(618, 438), BackColor = hostBg, AutoScroll = true };
            Controls.Add(_listHost);
            _cardHost = new Panel { Location = new Point(22, 146), Size = new Size(618, 438), BackColor = hostBg, Visible = false };
            Controls.Add(_cardHost);

            // 大卡片
            _flashCard = Ui.CardAt(65, 8, 500, 374, 16);
            _cardCounter = Ui.Lbl("", 20, 14, 9.5f, Palette.Sub);
            _cardMore = Ui.Lbl("更多释义 >", 0, 14, 9.5f, Palette.Sub);
            _cardMore.Cursor = Cursors.Hand;
            _cardMore.Click += (s, e) => OpenCurrentLookup();
            _cardWord = new Label
            {
                Location = new Point(24, 44), Size = new Size(452, 62), TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 26f, FontStyle.Bold), ForeColor = Palette.Main, BackColor = Color.Transparent
            };
            _cardPhones = new Label
            {
                Location = new Point(24, 110), Size = new Size(452, 26), TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 10.5f), ForeColor = Palette.Sub, BackColor = Color.Transparent
            };
            _cardSpeakBtn = new RoundPanel { Location = new Point(200, 142), Size = new Size(100, 32), Radius = 8, Fill = Palette.PillBg, NoEdge = true, Cursor = Cursors.Hand };
            _cardSpeakLbl = CenterLbl("▶ 朗读", _cardSpeakBtn.Size);
            _cardSpeakBtn.Controls.Add(_cardSpeakLbl);
            _cardSpeakBtn.WireClickThrough((s, e) => { var it = CurrentCard(); if (it != null && _speak != null) _speak(DisplayWord(it)); });
            _cardRevealBtn = new RoundPanel { Location = new Point(175, 196), Size = new Size(150, 40), Radius = 10, Fill = Palette.Accent, NoEdge = true, Cursor = Cursors.Hand };
            _cardRevealLbl = new Label
            {
                Text = "查看释义", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold), ForeColor = Color.White, BackColor = Color.Transparent
            };
            _cardRevealBtn.Controls.Add(_cardRevealLbl);
            _cardRevealBtn.WireClickThrough((s, e) => { _cardRevealed = true; ShowCard(_cardList); });
            _cardDef = new Label
            {
                Location = new Point(36, 184), Size = new Size(428, 50),
                Font = new Font("Microsoft YaHei UI", 12f), ForeColor = Palette.Main, BackColor = Color.Transparent, Visible = false
            };
            _cardExWt = new WordText { Location = new Point(36, 240), BackColor = Palette.Card, ForeColor = Palette.Main, Font = FontTheme.Of(11f), Visible = false };
            _cardExWt.WordClicked += (word, pt) => RaiseWordLookup(_cardExWt, word, pt);
            _cardExZh = new Label
            {
                Location = new Point(36, 286), Size = new Size(428, 24),
                Font = new Font("Microsoft YaHei UI", 10f), ForeColor = Color.FromArgb(90, 96, 106), BackColor = Color.Transparent, Visible = false
            };
            _cardKnowBtn = new RoundPanel { Location = new Point(104, 316), Size = new Size(132, 36), Radius = 9, Fill = Color.FromArgb(232, 246, 236), NoEdge = true, Cursor = Cursors.Hand, Visible = false };
            _cardKnowLbl = CenterLbl("认识", _cardKnowBtn.Size);
            _cardKnowBtn.Controls.Add(_cardKnowLbl);
            _cardKnowBtn.WireClickThrough((s, e) => ReviewAnswer(true));
            _cardDontBtn = new RoundPanel { Location = new Point(264, 316), Size = new Size(132, 36), Radius = 9, Fill = Color.FromArgb(253, 236, 233), NoEdge = true, Cursor = Cursors.Hand, Visible = false };
            _cardDontLbl = CenterLbl("不认识", _cardDontBtn.Size);
            _cardDontBtn.Controls.Add(_cardDontLbl);
            _cardDontBtn.WireClickThrough((s, e) => ReviewAnswer(false));
            _cardState = Ui.Lbl("", 20, 344, 9f, Palette.Sub);
            _cardDelete = Ui.Lbl("删除", 438, 344, 9.5f, Palette.Accent);
            _cardDelete.Cursor = Cursors.Hand;
            _cardDelete.Click += (s, e) => DeleteCurrent();
            _flashCard.Controls.AddRange(new Control[]
            {
                _cardCounter, _cardMore, _cardWord, _cardPhones, _cardSpeakBtn, _cardRevealBtn,
                _cardDef, _cardExWt, _cardExZh, _cardKnowBtn, _cardDontBtn, _cardState, _cardDelete
            });
            _cardHost.Controls.Add(_flashCard);
            _btnPrev = Ui.PillButton("← 上一个", 140, 394, 110, 36, Palette.Card, Palette.Main);
            _btnPrev.WireClickThrough((s, e) => StepCard(-1));
            _btnNext = Ui.PillButton("下一个 →", 380, 394, 110, 36, Palette.Card, Palette.Main);
            _btnNext.WireClickThrough((s, e) => StepCard(1));
            _cardHost.Controls.AddRange(new Control[] { _btnPrev, _btnNext });

            // 底部导出（最后加并置顶，沿用既有防遮挡处理）
            _btnTxt = Ui.PillButton("导出 TXT", 22, 592, 104, 32, Palette.Card, Palette.Main);
            _btnTxt.WireClickThrough((s, e) => Export(false));
            _btnCsv = Ui.PillButton("导出 CSV", 136, 592, 104, 32, Palette.Card, Palette.Main);
            _btnCsv.WireClickThrough((s, e) => Export(true));
            Controls.AddRange(new Control[] { _btnTxt, _btnCsv });
            _lblCount = Ui.Lbl("", 256, 600, 9f, Palette.Sub);
            Controls.Add(_lblCount);
            _btnTxt.BringToFront(); _btnCsv.BringToFront(); _lblCount.BringToFront();
        }

        private static Label CenterLbl(string text, Size size)
        {
            return new Label
            {
                Text = text, Location = new Point(0, 0), Size = size, TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 10.5f), ForeColor = Palette.Main, BackColor = Color.Transparent
            };
        }

        private void SelectTab(bool words)
        {
            // v1.6.5：列表/卡片切换钮去掉后，点页签（含当前页签）即退出复习大卡片回列表——这是复习的出口
            _viewMode = 0;
            if (_tabWords == words) { RefreshAll(); return; }
            _tabWords = words;
            _cardIndex = 0;
            _expanded = null;
            _meaningRevealedFor = null;
            RefreshAll();
        }

        private List<Entry> Filtered()
        {
            string q = _txtSearch.Text.Trim();
            var indexed = new List<KeyValuePair<int, Entry>>();
            for (int i = 0; i < _store.Favorites.Count; i++)
            {
                var f = _store.Favorites[i];
                if (f.IsWordLike() != _tabWords) continue;
                if (_tabWords && _filterMode == 1 && f.ReviewCount > 0) continue;
                if (_tabWords && _filterMode == 2 && f.ReviewCount == 0) continue;
                if (q.Length > 0 && (f.Src ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0
                    && (f.Dst ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                indexed.Add(new KeyValuePair<int, Entry>(i, f));
            }
            indexed.Sort((a, b) =>
            {
                int c = a.Value.Time.CompareTo(b.Value.Time);
                if (c == 0) c = a.Key.CompareTo(b.Key);
                return _sortDesc ? -c : c;
            });
            return indexed.Select(kv => kv.Value).ToList();
        }

        private int UnreviewedWordCount()
        {
            int n = 0;
            foreach (var f in _store.Favorites)
                if (f.IsWordLike() && f.ReviewCount == 0) n++;
            return n;
        }

        public void RefreshAll()
        {
            if (_tabWordBtn == null) return;
            // 页签与工具行状态（v1.6.5：页签去数字＋红底纹——选中全红白字、未选浅红底纹）
            var brandRed = Color.FromArgb(233, 61, 46);
            var washRed = Color.FromArgb(253, 232, 229);
            var washEdge = Color.FromArgb(243, 193, 187);
            _tabWordBtn.Fill = _tabWords ? brandRed : washRed;
            _tabWordBtn.Edge = _tabWords ? brandRed : washEdge;
            _tabSentBtn.Fill = !_tabWords ? brandRed : washRed;
            _tabSentBtn.Edge = !_tabWords ? brandRed : washEdge;
            _tabWordBtn.Invalidate(); _tabSentBtn.Invalidate();
            _tabWordLbl.Text = "单词";
            _tabSentLbl.Text = "句子";
            _tabWordLbl.ForeColor = _tabWords ? Color.White : Palette.Main;
            _tabSentLbl.ForeColor = !_tabWords ? Color.White : Palette.Main;
            _tabWordLbl.Font = new Font("Microsoft YaHei UI", 10.5f, _tabWords ? FontStyle.Bold : FontStyle.Regular);
            _tabSentLbl.Font = new Font("Microsoft YaHei UI", 10.5f, !_tabWords ? FontStyle.Bold : FontStyle.Regular);
            _sortLbl.Text = _sortDesc ? "排序  新→旧 ▾" : "排序  旧→新 ▾";
            _filterLbl.Text = "筛选  " + (_filterMode == 1 ? "未复习" : _filterMode == 2 ? "已复习" : "全部") + " ▾";
            _filterLbl.ForeColor = _tabWords ? Palette.Main : Palette.Sub;
            int un = UnreviewedWordCount();
            _reviewBtn.Visible = _tabWords;
            _reviewBadge.Visible = _tabWords && un > 0;
            _reviewBadgeLbl.Text = un > 99 ? "99+" : un.ToString();
            _reviewBadge.Size = new Size(_reviewBadgeLbl.Text.Length > 2 ? 36 : 30, 18);
            if (!_tabWords) _viewMode = 0;

            var list = Filtered();
            _lblCount.Text = "共 " + list.Count + " 条";
            if (_viewMode == 0)
            {
                _listHost.Visible = true;
                _cardHost.Visible = false;
                BuildList(list);
            }
            else
            {
                _listHost.Visible = false;
                _cardHost.Visible = true;
                _cardList = list;
                ShowCard(list);
            }
        }

        /// <summary>页面尺寸变化时重排（列表区拉伸、导出按钮锚底、视图切换锚右、卡片居中）。</summary>
        public void Relayout(int w, int h)
        {
            if (w <= 0 || h <= 0) return;
            Size = new Size(w, h);
            _reviewBtn.Location = new Point(w - 22 - 104, 48);
            _reviewBadge.Location = new Point(w - 22 - 24, 40);
            _listHost.Size = new Size(w - 44, h - 190);
            _cardHost.Size = new Size(w - 44, h - 190);
            _btnTxt.Location = new Point(22, h - 40);
            _btnCsv.Location = new Point(136, h - 40);
            _lblCount.Location = new Point(256, h - 32);
            if (_flashCard != null)
            {
                _flashCard.Location = new Point(Math.Max(0, (_cardHost.Width - 500) / 2), 8);
                _btnPrev.Location = new Point(_flashCard.Location.X + 75, 394);
                _btnNext.Location = new Point(_flashCard.Location.X + 315, 394);
            }
            RefreshAll();
        }

        // ---------------- 列表视图（按日期分组） ----------------

        private void BuildList(List<Entry> list)
        {
            _listHost.Controls.Clear();
            int rowW = Math.Max(320, _listHost.Width - 18);
            if (list.Count == 0)
            {
                var empty = Ui.CardAt(0, 0, rowW, 120);
                empty.Controls.Add(Ui.Lbl(_tabWords ? "暂无符合条件的单词。点“复习”或调整筛选试试。" : "暂无符合条件的句子。", 18, 20, 10.5f, Palette.Sub));
                _listHost.Controls.Add(empty);
                _listHost.AutoScrollMinSize = new Size(0, 120);
                return;
            }
            int y = 4;
            int seq = 0;
            int i = 0;
            while (i < list.Count)
            {
                DateTime day = LocalDay(list[i].Time);
                var group = new List<Entry>();
                while (i < list.Count && LocalDay(list[i].Time) == day) { group.Add(list[i]); i++; }
                _listHost.Controls.Add(Ui.Lbl(day.ToString("yyyy-MM-dd"), 2, y + 4, 10.5f, Palette.Main, true));
                y += 30;
                // v1.3.1：同组条目合成一块白底扁平块（照有道），行间浅分隔线
                var block = new RoundPanel { Location = new Point(0, y), Size = new Size(rowW, 10), Radius = 10, Fill = Palette.Card, Edge = Palette.Border };
                _listHost.Controls.Add(block);
                int ly = 0;
                for (int gi = 0; gi < group.Count; gi++)
                {
                    seq++;
                    ly += BuildRow(block, group[gi], seq, ly, rowW, gi < group.Count - 1);
                }
                block.Size = new Size(rowW, ly);
                y += ly;
                var foot = Ui.Lbl("该分组下共 " + group.Count + " 个" + (_tabWords ? "单词" : "句子"), 0, y + 6, 9f, Palette.Sub);
                foot.Location = new Point(rowW - foot.PreferredWidth, y + 6);
                _listHost.Controls.Add(foot);
                y += 34;
            }
            _listHost.AutoScrollMinSize = new Size(0, y + 4);
        }

        private static DateTime LocalDay(long unixSec)
        {
            if (unixSec <= 0) return new DateTime(1970, 1, 1);
            return DateTimeOffset.FromUnixTimeSeconds(unixSec).LocalDateTime.Date;
        }

        private static string TrimToWidth(string text, int maxW, float size, bool bold = false)
        {
            string t = (text ?? "").Replace("\r", " ").Replace("\n", " ");
            if (maxW < 30) return t;
            using (var f = FontTheme.Of(size, bold))
            {
                if (TextRenderer.MeasureText(t, f).Width <= maxW) return t;
                while (t.Length > 1 && TextRenderer.MeasureText(t + "…", f).Width > maxW)
                    t = t.Substring(0, t.Length - 1);
                return t + "…";
            }
        }

        private int BuildRow(Control host, Entry entry, int seq, int y, int rowW, bool hasSeparator)
        {
            var captured = entry;
            bool expanded = entry == _expanded;
            int baseH = _tabWords ? 54 : 72;
            var row = new Panel
            {
                Location = new Point(0, y), Size = new Size(rowW, baseH),
                BackColor = Palette.Card, Cursor = Cursors.Hand
            };
            if (expanded)
            {
                // v1.5.3 美化：展开行改白底＋细边框卡（取消整片浅蓝满铺），与折叠行靠边框区分
                row.Paint += (s, e) =>
                {
                    using (var p = new Pen(Palette.Border))
                        e.Graphics.DrawRectangle(p, 0, 0, row.Width - 1, row.Height - 1);
                };
            }
            row.Controls.Add(Ui.Lbl(seq.ToString("D2"), 14, _tabWords ? 19 : 14, 10f, Palette.Sub));

            EventHandler rowClick = (s, e) => { _pendingToggle = captured; _clickTimer.Stop(); _clickTimer.Start(); };
            EventHandler rowDbl = (s, e) => { _clickTimer.Stop(); if (_fillHome != null) _fillHome(captured); };
            row.Click += rowClick;
            row.DoubleClick += rowDbl;

            if (_tabWords)
            {
                var src = Ui.Lbl(TrimToWidth(entry.Src, rowW - 220, 12f, true), 52, 16, 12f, Palette.Main, true);
                src.Cursor = Cursors.Hand;
                src.Click += rowClick; src.DoubleClick += rowDbl;
                row.Controls.Add(src);
                if (!expanded)
                {
                    int defX = 52 + src.PreferredWidth + 26;
                    int defMaxW = rowW - 64 - defX;
                    if (defMaxW > 60)
                    {
                        // v1.6.2：词义默认隐藏（用户要求自测回忆，点开条目再点「译」才看）
                        var dst = Ui.Lbl("词义已隐藏 · 点开查看", defX, 18, 10.5f, Palette.Sub);
                        dst.Cursor = Cursors.Hand;
                        dst.Click += rowClick; dst.DoubleClick += rowDbl;
                        row.Controls.Add(dst);
                    }
                    // 掌握度小条（36px，照有道右端短横线的位置）
                    var track = new RoundPanel { Location = new Point(rowW - 52, 25), Size = new Size(36, 4), Radius = 2, Fill = Palette.PillBg, NoEdge = true };
                    int lvl = Math.Max(0, Math.Min(3, entry.ReviewLevel));
                    if (lvl > 0)
                        track.Controls.Add(new RoundPanel { Location = new Point(0, 0), Size = new Size(36 * lvl / 3, 4), Radius = 2, Fill = Palette.SwitchBlue, NoEdge = true });
                    row.Controls.Add(track);
                }
                else
                {
                    var speak = Ui.Lbl("▶ 朗读", rowW - 76, 18, 9.5f, Palette.Accent);
                    speak.Cursor = Cursors.Hand;
                    speak.Click += (s, e) => { if (_speak != null) _speak(SpeakTextOf(captured)); };
                    row.Controls.Add(speak);
                }
            }
            else
            {
                var src = Ui.Lbl(TrimToWidth(entry.Src, rowW - 76, 11f), 52, 11, 11f, Palette.Main);
                src.Cursor = Cursors.Hand;
                src.Click += rowClick; src.DoubleClick += rowDbl;
                // v1.6.2：句子译文未展开时同样默认隐藏；v1.6.3：展开时也不再在行头重复显示（详情里已隐藏，正文行与之保持一致）
                var dstText = expanded ? "" : "译文已隐藏 · 点开查看";
                var dst = Ui.Lbl(dstText, 52, 42, 10f, Palette.Sub);
                dst.Cursor = Cursors.Hand;
                dst.Click += rowClick; dst.DoubleClick += rowDbl;
                row.Controls.AddRange(new Control[] { src, dst });
            }

            host.Controls.Add(row);
            int h = baseH;
            if (expanded)
            {
                int used = BuildDetail(row, entry, baseH + 2, rowW);
                h = baseH + 2 + used + 6;
                row.Size = new Size(rowW, h);
            }
            if (hasSeparator)
                row.Controls.Add(new Panel { Location = new Point(0, h - 1), Size = new Size(rowW, 1), BackColor = Palette.Border });
            return h;
        }

        internal void ToggleExpand(Entry entry)
        {
            _expanded = _expanded == entry ? null : entry;
            _revealed.Clear();
            _meaningRevealedFor = null; // v1.6.2：换条目展开时词义回到默认隐藏
            RefreshAll();
        }

        private static bool IsEnglishText(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            int letters = 0, ascii = 0;
            foreach (char c in s)
            {
                if (char.IsLetter(c)) { letters++; if (c < 128) ascii++; }
            }
            return letters > 0 && ascii * 10 >= letters * 7;
        }

        private static string TargetWordOf(Entry entry, bool srcEn, bool dstEn)
        {
            if (srcEn && entry.IsWordLike()) return entry.Src.Trim();
            if (dstEn && new Entry { Src = entry.Dst }.IsWordLike()) return (entry.Dst ?? "").Trim();
            return null;
        }

        private static string DisplayWord(Entry entry)
        {
            if (IsEnglishText(entry.Src)) return (entry.Src ?? "").Trim();
            if (IsEnglishText(entry.Dst)) return (entry.Dst ?? "").Trim();
            return (entry.Src ?? "").Trim();
        }

        private static string DisplayMeaning(Entry entry)
        {
            string w = DisplayWord(entry);
            return string.Equals(w, (entry.Src ?? "").Trim(), StringComparison.Ordinal) ? entry.Dst : entry.Src;
        }

        private static string SpeakTextOf(Entry entry)
        {
            return IsEnglishText(entry.Src) ? entry.Src : (IsEnglishText(entry.Dst) ? entry.Dst : entry.Src);
        }

        private static Label WrapLabel(string text, int x, int y, int w, float size, Color color)
        {
            var f = FontTheme.Of(size);
            int h = TextRenderer.MeasureText(text ?? "", f, new Size(w, int.MaxValue), TextFormatFlags.WordBreak).Height;
            return new Label
            {
                Text = text ?? "", Location = new Point(x, y), Size = new Size(w, Math.Max(h, (int)size + 8)),
                Font = f, ForeColor = color, BackColor = Color.Transparent
            };
        }

        private void RaiseWordLookup(Control c, string word, Point pt)
        {
            if (WordLookup != null && !string.IsNullOrEmpty(word))
                WordLookup(word, c.PointToScreen(pt));
        }

        /// <summary>v1.6.2 「译」字圆钮（用户要求：显示翻译改极简图标——圆圈里一个译字）。
        /// v1.6.3：全皮肤统一品牌红底白字（用户点名）。
        /// v1.6.4：成对图标——未显示时是红底「译」（点它显示），已显示时换深灰底「隐」（点它收起），
        /// 用户反馈展开后没有隐藏图标、光靠圆钮描边区分看不出来。</summary>
        private Control MakeYiToggle(bool revealed, EventHandler onClick)
        {
            var brandRed = Color.FromArgb(233, 61, 46);
            var ink = Color.FromArgb(63, 66, 72);
            var p = new RoundPanel
            {
                Size = new Size(22, 22), Radius = 11, Cursor = Cursors.Hand,
                Fill = revealed ? ink : brandRed,
                Edge = revealed ? ink : brandRed, NoEdge = false
            };
            var l = new Label
            {
                Text = revealed ? "隐" : "译", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
                ForeColor = Color.White, BackColor = Color.Transparent, Cursor = Cursors.Hand
            };
            l.Click += onClick;
            p.Click += onClick;
            p.Controls.Add(l);
            return p;
        }

        private int AddTextBlock(Control card, string text, bool english, int x, int y, int w)
        {
            if (string.IsNullOrWhiteSpace(text)) return y;
            if (english)
            {
                var wt = new WordText { Location = new Point(x, y), BackColor = Palette.Card, ForeColor = Palette.Main, Font = FontTheme.Of(11f) };
                wt.Text = text.Replace("\r", " ").Replace("\n", " ");
                int hh = wt.FitHeight(w);
                wt.Height = hh;
                wt.WordClicked += (word, pt) => RaiseWordLookup(wt, word, pt);
                card.Controls.Add(wt);
                return y + hh + 6;
            }
            var lbl = WrapLabel(text, x, y, w, 10.5f, Color.FromArgb(90, 96, 106));
            card.Controls.Add(lbl);
            return y + lbl.Height + 6;
        }

        /// <summary>v1.6.5 译文可选文本框：无边框只读 TextBox（可拖选、Ctrl+C）。
        /// 量高沿用 WrapLabel 口径（TextRenderer WordBreak 测高），字体走 FontTheme 与全窗一致；
        /// TextBox 自带内边距约 3px，定位左移补齐、宽度加回，文字与标签版同位。</summary>
        private Control MakeSelectableText(string text, int x, int y, int w, float size, Color fore, out int height)
        {
            var font = FontTheme.Of(size);
            string clean = (text ?? "").Replace("\r", " ").Replace("\n", " ");
            int h = TextRenderer.MeasureText(clean, font, new Size(w, int.MaxValue), TextFormatFlags.WordBreak).Height;
            var tb = new TextBox
            {
                Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, WordWrap = true,
                BackColor = Palette.Card, ForeColor = fore, Font = font, Text = clean,
                Location = new Point(x - 3, y - 2), Width = w + 6
            };
            tb.Height = Math.Max(h + 4, (int)size + 12);
            height = Math.Max(h, (int)size + 8);
            return tb;
        }

        /// <summary>v1.6.2 词义/译文块：默认隐藏（占位行＋译圆钮），点圆钮才显示真内容（与卡片复习同一自测口径）。
        /// v1.6.4：显示后在首行右端补「隐」圆钮，可再收起（用户反馈展开后没有隐图标）。
        /// v1.6.5：显示出的译文可选取复制——中文侧直接用只读文本框渲染；英文侧保留点词，
        /// 在其上加透明选取层（拖动超 6px 召唤出可选文本框、Esc/无选区失焦自动退回，同截图原文口径）。</summary>
        private int AddMeaningBlock(Control card, Entry entry, string text, bool english, int x, int y, int w)
        {
            if (string.IsNullOrWhiteSpace(text)) return y;
            if (_meaningRevealedFor == entry)
            {
                int startY = y;
                int blockW = w - 34; // 右侧预留 34px 给「隐」钮防压字
                int next;
                if (!english)
                {
                    int th;
                    var tb = MakeSelectableText(text, x, y, blockW, 10.5f, Color.FromArgb(90, 96, 106), out th);
                    card.Controls.Add(tb);
                    next = y + th + 6;
                }
                else
                {
                    next = AddTextBlock(card, text, true, x, y, blockW);
                    WordText wt = null;
                    foreach (Control c in card.Controls) { var cand = c as WordText; if (cand != null && cand.Location.Y == startY) wt = cand; }
                    if (wt != null)
                    {
                        var selBox = MakeSelectableText(text, x, y, blockW, 11f, Palette.Main, out _);
                        selBox.Visible = false;
                        card.Controls.Add(selBox);
                        bool armed = false;
                        Point start = Point.Empty;
                        wt.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { armed = true; start = e.Location; } };
                        wt.MouseUp += (s, e) =>
                        {
                            if (!armed) return;
                            armed = false;
                            if (Math.Abs(e.Location.X - start.X) + Math.Abs(e.Location.Y - start.Y) < 6) return;
                            selBox.Visible = true;
                            selBox.BringToFront();
                            selBox.Focus();
                        };
                        var box = selBox as TextBox;
                        box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) box.Visible = false; };
                        box.Leave += (s, e) => { if (box.SelectionLength == 0) box.Visible = false; };
                    }
                }
                var tog = MakeYiToggle(true, (s, e) => { _meaningRevealedFor = null; RefreshAll(); });
                tog.Location = new Point(x + w - 22, startY);
                card.Controls.Add(tog);
                tog.BringToFront();
                return next;
            }
            var hint = Ui.Lbl(english ? "Translation hidden" : "翻译已隐藏", x, y + 2, 10f, Palette.Sub);
            card.Controls.Add(hint);
            var tog2 = MakeYiToggle(false, (s, e) => { _meaningRevealedFor = entry; RefreshAll(); });
            tog2.Location = new Point(x + hint.PreferredWidth + 8, y);
            card.Controls.Add(tog2);
            return y + 28;
        }

        private int BuildDetail(Control card, Entry entry, int y0, int cardW)
        {
            int x = 22, w = cardW - 44;
            int y = y0;
            card.Controls.Add(new Panel { Location = new Point(x, y + 2), Size = new Size(w, 1), BackColor = Palette.Border });
            var captured = entry;
            // v1.5.3 美化：删除用柔和红文字；位置在下方随译图标一起算（v1.6.5）
            var del = Ui.Lbl("删除", 0, y + 8, 9.5f, Color.FromArgb(203, 102, 94));
            del.Cursor = Cursors.Hand;
            del.Click += (s, e) =>
            {
                if (_expanded == captured) _expanded = null;
                _store.RemoveFavorite(captured);
                if (_status != null) _status("已从单词本删除");
                RefreshAll();
            };
            card.Controls.Add(del);
            // v1.6.5：「去翻译」文字改图标——26px 红圆圈内「译」字（与单词本其他译图标同款），悬停提示「去翻译」
            var goIcon = new RoundPanel
            {
                Location = new Point(cardW - 22 - 26, y + 3), Size = new Size(26, 26), Radius = 13,
                Fill = Color.FromArgb(233, 61, 46), Edge = Color.FromArgb(233, 61, 46), NoEdge = false, Cursor = Cursors.Hand
            };
            var goIconLbl = new Label
            {
                Text = "译", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold), ForeColor = Color.White,
                BackColor = Color.Transparent, Cursor = Cursors.Hand
            };
            goIcon.Controls.Add(goIconLbl);
            EventHandler goClick = (s, e) => { if (_fillHome != null) _fillHome(captured); };
            goIcon.Click += goClick;
            goIconLbl.Click += goClick;
            card.Controls.Add(goIcon);
            if (_tips == null) _tips = new ToolTip();
            _tips.SetToolTip(goIcon, "去翻译");
            _tips.SetToolTip(goIconLbl, "去翻译");
            del.Location = new Point(cardW - 22 - 26 - 12 - del.PreferredWidth, y + 8);
            y += 40; // v1.6.3 独立一行防压字；v1.6.5 应用户反馈再加大与正文的行距（30→40）
            bool srcEn = IsEnglishText(entry.Src), dstEn = IsEnglishText(entry.Dst);
            // v1.6.2：原文（外语侧）照常显示；译文（中文侧）默认隐藏。英中方向按内容判定：
            // Src 是中文时译文在 Src 侧、原文（英文句/词）在 Dst 侧，隐藏的永远是中文那一侧。
            string meaningText = srcEn ? entry.Dst : entry.Src;
            bool meaningEn = srcEn ? dstEn : srcEn;
            if (srcEn)
            {
                y = AddTextBlock(card, entry.Src, true, x, y, w);
                y = AddMeaningBlock(card, entry, meaningText, meaningEn, x, y, w);
            }
            else
            {
                y = AddMeaningBlock(card, entry, meaningText, meaningEn, x, y, w);
                y = AddTextBlock(card, entry.Dst, dstEn, x, y, w);
            }
            string target = TargetWordOf(entry, srcEn, dstEn);
            if (target != null)
            {
                y += 4;
                card.Controls.Add(Ui.Lbl("例句", x, y, 10f, Palette.Main, true));
                if (entry.Examples != null && entry.Examples.Count > 0 && !string.IsNullOrEmpty(entry.ExamplesSource))
                {
                    var n = Ui.Lbl(entry.ExamplesSource + " · 已缓存", 0, y + 2, 8.5f, Palette.Sub);
                    n.Location = new Point(cardW - 22 - n.PreferredWidth, y + 2);
                    card.Controls.Add(n);
                }
                y += 30;
                if (entry.Examples != null && entry.Examples.Count > 0)
                {
                    for (int i = 0; i < entry.Examples.Count; i++)
                    {
                        var ex = entry.Examples[i];
                        int idx = i;
                        bool revealed = _revealed.Contains(idx);
                        // v1.5.3 美化：例句改浅灰内嵌块（圆角 10、内边距 12/8），英文/中文/来源同块成组，不再白条压字
                        var wt = new WordText { ForeColor = Palette.Main, Font = FontTheme.Of(11f), BackColor = Palette.PillBg };
                        wt.Text = ex.En;
                        int hh = wt.FitHeight(w - 24 - 40);
                        wt.Height = hh;
                        int zhH = 0;
                        if (revealed && !string.IsNullOrEmpty(ex.Zh))
                        {
                            using (var f = FontTheme.Of(10f))
                                zhH = TextRenderer.MeasureText(ex.Zh, f, new Size(w - 24 - 150, int.MaxValue), TextFormatFlags.WordBreak).Height;
                        }
                        int blockH = 8 + hh + (zhH > 0 ? 3 + zhH : 0) + 9;
                        var block = new RoundPanel
                        {
                            Location = new Point(x, y), Size = new Size(w, blockH),
                            Radius = 10, Fill = Palette.PillBg, NoEdge = true
                        };
                        wt.Location = new Point(12, 8);
                        wt.WordClicked += (word, pt) => RaiseWordLookup(wt, word, pt);
                        block.Controls.Add(wt);
                        // v1.6.2：例句「显示翻译」文字链改「译」字圆钮（用户要求极简）
                        var toggle = MakeYiToggle(revealed, (s, e) =>
                        {
                            if (_revealed.Contains(idx)) _revealed.Remove(idx); else _revealed.Add(idx);
                            RefreshAll();
                        });
                        toggle.Location = new Point(w - 12 - 22, 8);
                        block.Controls.Add(toggle);
                        if (zhH > 0)
                        {
                            var zh = WrapLabel(ex.Zh, 12, 8 + hh + 3, w - 24 - 150, 10f, Color.FromArgb(90, 96, 106));
                            block.Controls.Add(zh);
                            if (!string.IsNullOrEmpty(ex.Source))
                            {
                                var sl = Ui.Lbl(ex.Source, 0, 8 + hh + 5, 8.5f, Palette.Sub);
                                sl.Location = new Point(w - 12 - sl.PreferredWidth, 8 + hh + 5);
                                block.Controls.Add(sl);
                            }
                        }
                        card.Controls.Add(block);
                        y += blockH + 10;
                    }
                }
                else if (_failedFor == entry)
                {
                    var retry = Ui.Lbl("例句没取到，点此重试", x, y, 9.5f, Palette.Accent);
                    retry.Cursor = Cursors.Hand;
                    retry.Click += (s, e) => { _failedFor = null; LoadExamples(captured); RefreshAll(); };
                    card.Controls.Add(retry);
                    y += 24;
                }
                else
                {
                    if (_loadingFor != entry) LoadExamples(entry);
                    card.Controls.Add(Ui.Lbl("正在取例句…", x, y, 9.5f, Palette.Sub));
                    y += 24;
                }
            }
            return y - y0;
        }

        private async void LoadExamples(Entry entry)
        {
            if (_loadingFor == entry) return;
            _loadingFor = entry;
            string target = TargetWordOf(entry, IsEnglishText(entry.Src), IsEnglishText(entry.Dst));
            string meaning = IsEnglishText(entry.Src) ? entry.Dst : entry.Src;
            try
            {
                var f = await ExampleProvider.Fetch(Engine, target, meaning);
                if (f.Items.Count > 0)
                {
                    entry.Examples = f.Items;
                    entry.ExamplesSource = f.Source;
                    _store.PersistFavorites();
                }
                else _failedFor = entry;
            }
            catch { _failedFor = entry; }
            finally
            {
                _loadingFor = null;
                if (_expanded == entry || _cardShownEntry == entry) RefreshAll();
            }
        }

        // ---------------- 卡片复习 ----------------

        private void StartReview()
        {
            _tabWords = true;
            _filterMode = 0;
            if (_txtSearch.Text.Length > 0) _txtSearch.Text = "";
            _viewMode = 1;
            var list = Filtered();
            int idx = list.FindIndex(e => e.ReviewCount == 0);
            if (idx < 0)
            {
                if (_status != null) _status(list.Count == 0 ? "单词本里还没有单词" : "单词都复习过了，可以在卡片里翻看");
                _cardIndex = 0;
            }
            else _cardIndex = idx;
            RefreshAll();
        }

        private Entry CurrentCard()
        {
            if (_cardList == null || _cardList.Count == 0) return null;
            if (_cardIndex < 0) _cardIndex = 0;
            if (_cardIndex >= _cardList.Count) _cardIndex = _cardList.Count - 1;
            return _cardList[_cardIndex];
        }

        private void ShowCard(List<Entry> list)
        {
            _cardList = list;
            if (list.Count == 0)
            {
                _cardCounter.Text = "";
                _cardWord.Text = "暂无单词";
                _cardPhones.Text = "";
                _cardDef.Text = "";
                _cardState.Text = "";
                SetCardReveal(false);
                return;
            }
            var entry = CurrentCard();
            if (_cardShownEntry != entry)
            {
                _cardShownEntry = entry;
                _cardRevealed = false;
            }
            string word = DisplayWord(entry);
            _cardCounter.Text = (_cardIndex + 1) + " / " + list.Count;
            _cardMore.Location = new Point(500 - 20 - _cardMore.PreferredWidth, 14);
            _cardWord.Text = word;
            _cardWord.Font = new Font("Microsoft YaHei UI", word.Length > 26 ? 16f : word.Length > 16 ? 21f : 26f, FontStyle.Bold);
            EnsurePhonetics(entry);
            string phones = "";
            if (!string.IsNullOrEmpty(entry.PhoneticUk)) phones += "英 " + entry.PhoneticUk + "   ";
            if (!string.IsNullOrEmpty(entry.PhoneticUs)) phones += "美 " + entry.PhoneticUs;
            _cardPhones.Text = phones.Trim();
            _cardState.Text = entry.ReviewCount == 0 ? "未复习过" : "已复习 " + entry.ReviewCount + " 次 · 等级 " + Math.Max(0, Math.Min(3, entry.ReviewLevel));
            SetCardReveal(_cardRevealed);
            if (_cardRevealed)
            {
                _cardDef.Text = Trim(DisplayMeaning(entry), 120);
                var ex = entry.Examples != null && entry.Examples.Count > 0 ? entry.Examples[0] : null;
                if (ex != null)
                {
                    _cardExWt.Text = ex.En;
                    _cardExWt.Height = _cardExWt.FitHeight(428);
                    _cardExZh.Text = ex.Zh;
                    _cardExZh.Location = new Point(36, 240 + _cardExWt.Height + 4);
                }
                else
                {
                    _cardExWt.Text = "";
                    _cardExZh.Text = "例句加载中…";
                    _cardExZh.Location = new Point(36, 244);
                    if (_loadingFor != entry && _failedFor != entry) LoadExamples(entry);
                    else if (_failedFor == entry) _cardExZh.Text = "例句没取到，收起再打开可重试";
                }
            }
        }

        private void SetCardReveal(bool revealed)
        {
            _cardRevealBtn.Visible = !revealed;
            _cardDef.Visible = revealed;
            _cardExWt.Visible = revealed;
            _cardExZh.Visible = revealed;
            _cardKnowBtn.Visible = revealed;
            _cardDontBtn.Visible = revealed;
        }

        private async void EnsurePhonetics(Entry entry)
        {
            string target = DisplayWord(entry);
            if (!IsEnglishText(target)) return;
            if (!string.IsNullOrEmpty(entry.PhoneticUk) || !string.IsNullOrEmpty(entry.PhoneticUs)) return;
            if (_phoneLoading.Contains(entry)) return;
            _phoneLoading.Add(entry);
            try
            {
                // v1.8.0：与查词小窗同一套来源解析（设置记住的来源同语言类别优先，否则按语言默认）
                var eff = Engine != null && Engine.ExtraSettings != null ? Engine.ExtraSettings : SyncService.EffectiveCreds(_store.Settings);
                string srcId = MultiDict.DefaultFor(target);
                string saved = _store.Settings != null ? _store.Settings.DictSource : null;
                var savedInfo = !string.IsNullOrEmpty(saved) ? MultiDict.InfoOf(saved) : null;
                if (savedInfo != null && savedInfo.Chinese == (srcId == "zdic")) srcId = saved;
                var r = await MultiDict.Lookup(srcId, target, eff);
                entry.PhoneticUk = r.UkPhone;
                entry.PhoneticUs = r.UsPhone;
                _store.PersistFavorites();
            }
            catch { }
            finally
            {
                _phoneLoading.Remove(entry);
                if (_cardShownEntry == entry && Visible) RefreshAll();
            }
        }

        private void OpenCurrentLookup()
        {
            var entry = CurrentCard();
            if (entry == null) return;
            string word = DisplayWord(entry);
            if (WordLookup != null && IsEnglishText(word))
                WordLookup(word, PointToScreen(new Point(Width - 180, 130)));
            else if (_fillHome != null) _fillHome(entry);
        }

        private void ReviewAnswer(bool known)
        {
            var entry = CurrentCard();
            if (entry == null) return;
            entry.ReviewCount++;
            entry.LastReview = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            entry.ReviewLevel = known ? Math.Min(3, Math.Max(0, entry.ReviewLevel) + 1) : 0;
            _store.PersistFavorites();
            _cardRevealed = false;
            if (_cardIndex < _cardList.Count - 1)
            {
                _cardIndex++;
                if (_status != null) _status(known ? "已记为认识，下一张" : "已记为不认识，下一张");
            }
            else if (_status != null) _status("本轮复习完成");
            RefreshAll();
        }

        private void StepCard(int delta)
        {
            if (_cardList.Count == 0) return;
            _cardIndex = (_cardIndex + delta + _cardList.Count) % _cardList.Count;
            _cardRevealed = false;
            ShowCard(_cardList);
        }

        private void DeleteCurrent()
        {
            var entry = CurrentCard();
            if (entry == null) return;
            _store.RemoveFavorite(entry);
            _cardShownEntry = null;
            if (_status != null) _status("已从单词本删除");
            RefreshAll();
        }

        private void Export(bool csv)
        {
            using (var dlg = new SaveFileDialog
            {
                Filter = csv ? "CSV 表格|*.csv" : "文本文件|*.txt",
                FileName = csv ? "单词本.csv" : "单词本.txt"
            })
            {
                if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    int n = csv ? _store.ExportFavoritesCsv(dlg.FileName) : _store.ExportFavorites(dlg.FileName);
                    if (_status != null) _status("已导出 " + n + " 条到 " + dlg.FileName);
                }
            }
        }

        private static string Trim(string s, int max)
        {
            s = (s ?? "").Replace("\r", " ").Replace("\n", " ");
            return s.Length > max ? s.Substring(0, max) + "…" : s;
        }
    }
}
