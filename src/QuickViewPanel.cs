using System;
using System.Drawing;
using System.Windows.Forms;

namespace WinTranslator
{
    /// <summary>右栏速览（B 案）：最近历史 4 条＋单词本 4 条，点击填回主页。</summary>
    public class QuickViewPanel : RoundPanel
    {
        private readonly Store _store;
        private readonly Action<Entry> _fillHome;

        public QuickViewPanel(Store store, Action<Entry> fillHome)
        {
            _store = store;
            _fillHome = fillHome;
            Radius = 14;
            Fill = Palette.Card;
            Edge = Palette.Border;
        }

        public void RefreshAll()
        {
            Controls.Clear();
            Controls.Add(Ui.Lbl("速览", 16, 14, 12.5f, Palette.Main, true));
            Controls.Add(Ui.Lbl("最近历史", 16, 50, 10f, Palette.Sub, true));

            int y = 78;
            if (_store.History.Count == 0)
                Controls.Add(Ui.Lbl("暂无历史", 16, y, 9.5f, Palette.Sub));
            for (int i = 0; i < Math.Min(4, _store.History.Count); i++)
            {
                AddRow(_store.History[i], y, true);
                y += 56;
            }

            int wordTitleY = 78 + 4 * 56 + 8;
            Controls.Add(Ui.Lbl("单词本", 16, wordTitleY, 10f, Palette.Sub, true));
            y = wordTitleY + 28;
            if (_store.Favorites.Count == 0)
                Controls.Add(Ui.Lbl("暂无收藏", 16, y, 9.5f, Palette.Sub));
            for (int i = 0; i < Math.Min(4, _store.Favorites.Count); i++)
            {
                AddRow(_store.Favorites[i], y, false);
                y += 52;
            }
            var tip = Ui.Lbl("点击任意条目填回主页", 16, Height - 30, 8.5f, Palette.Sub);
            Controls.Add(tip);
        }

        private void AddRow(Entry entry, int y, bool withTime)
        {
            var src = Ui.Lbl(Trim(entry.Src, 22), 16, y, 10f, Palette.Main, entry.Src != null && !withTime);
            var dst = Ui.Lbl(Trim(entry.Dst, 24), 16, y + 20, 9.5f, Palette.Sub);
            // v1.1.1 事项四：限宽＋省略号，长句不再溢出卡片
            foreach (var l in new[] { src, dst })
            {
                l.AutoSize = false;
                l.Width = Math.Max(80, Width - 32 - (withTime && l == src ? 44 : 0));
                l.AutoEllipsis = true;
            }
            src.Cursor = Cursors.Hand;
            dst.Cursor = Cursors.Hand;
            EventHandler click = (s, e) => { if (_fillHome != null) _fillHome(entry); };
            src.Click += click;
            dst.Click += click;
            Controls.Add(src);
            Controls.Add(dst);
            if (withTime && entry.Time > 0)
            {
                var t = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(entry.Time).ToLocalTime();
                var lbl = Ui.Lbl(t.ToString("HH:mm"), 0, y, 8.5f, Palette.Sub);
                lbl.Location = new Point(Width - 16 - lbl.PreferredWidth, y);
                Controls.Add(lbl);
            }
        }

        private static string Trim(string s, int max)
        {
            s = (s ?? "").Replace("\r", " ").Replace("\n", " ");
            return s.Length > max ? s.Substring(0, max) + "…" : s;
        }
    }
}
