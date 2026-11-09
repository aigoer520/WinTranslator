using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace WinTranslator
{
    /// <summary>
    /// v1.2.0 点词文本控件：纯文字渲染，英文单词可悬停（下划线＋手型）与点击。
    /// 平时没有任何标记（隐藏式），只有鼠标移到单词上才出现下划线。
    /// </summary>
    public class WordText : Control
    {
        public event Action<string, Point> WordClicked;


        private string _text = "";
        private List<Token> _tokens = new List<Token>();
        private int _contentHeight;
        private Token _hover;
        private bool _layoutDirty = true;

        private class Token
        {
            public string Text;
            public bool IsWord;
            public Rectangle Rect;
        }

        public WordText()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Color.White;
            Font = FontTheme.Of(11f);
        }

        public override string Text
        {
            get { return _text; }
            set { _text = value ?? ""; _layoutDirty = true; Invalidate(); }
        }

        /// <summary>按给定宽度排版，返回需要的高度（宿主据此设 Height）。</summary>
        public int FitHeight(int width)
        {
            Width = width;
            LayoutTokens();
            return _contentHeight;
        }

        /// <summary>在滚动面板内排版：先估算是否需要纵向滚动条（内容高＞可视高），据此决定词的换行宽度。</summary>
        public void LayoutInScrollPanel(Panel host)
        {
            int trim = SystemInformation.VerticalScrollBarWidth + 4;
            int innerW = host.ClientSize.Width;
            int estH = FitHeight(Math.Max(60, innerW));
            if (estH > host.ClientSize.Height)
                estH = FitHeight(Math.Max(60, innerW - trim));
            Height = estH;
            Location = new Point(0, 0);
            host.AutoScrollMinSize = new Size(0, estH);
        }

        private static bool IsWordChar(char c) { return char.IsLetter(c); }

        /// <summary>从字符下标提取所在英文单词（撇号/连字符夹在字母中间时算词内字符）；不在单词上返回 ""。</summary>
        public static string WordAt(string text, int index)
        {
            if (string.IsNullOrEmpty(text) || index < 0 || index >= text.Length) return "";
            if (!IsWordChar(text[index]) && !IsMidJoin(text, index)) return "";
            int start = index;
            while (start > 0 && (IsWordChar(text[start - 1]) || IsMidJoin(text, start - 1))) start--;
            int end = index;
            while (end < text.Length - 1 && (IsWordChar(text[end + 1]) || IsMidJoin(text, end + 1))) end++;
            return text.Substring(start, end - start + 1);
        }

        // text[i] 是 ' 或 -，且两侧都是字母 → 词内连接符
        private static bool IsMidJoin(string text, int i)
        {
            return i > 0 && i < text.Length - 1
                   && (text[i] == '\'' || text[i] == '’' || text[i] == '-')
                   && IsWordChar(text[i - 1]) && IsWordChar(text[i + 1]);
        }

        private void LayoutTokens()
        {
            _tokens = new List<Token>();
            if (Width <= 0) { _contentHeight = Font.Height + 4; _layoutDirty = false; return; }
            // 切分：单词（含内部 ' -）与分隔串交替
            var parts = new List<Token>();
            int i = 0;
            while (i < _text.Length)
            {
                bool word = IsWordChar(_text[i]);
                int j = i;
                if (word)
                {
                    j = i + 1;
                    while (j < _text.Length && (IsWordChar(_text[j]) ||
                           (j + 1 < _text.Length && (_text[j] == '\'' || _text[j] == '’' || _text[j] == '-') && IsWordChar(_text[j - 1]) && IsWordChar(_text[j + 1])))) j++;
                }
                else
                {
                    while (j < _text.Length && !IsWordChar(_text[j])) j++;
                }
                parts.Add(new Token { Text = _text.Substring(i, j - i), IsWord = word });
                i = j;
            }
            int x = 0, y = 0, lineH = Font.Height + 4;
            int xWidth = TextRenderer.MeasureText("X", Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            foreach (var t in parts)
            {
                // 尾随空格会被 MeasureText 吃掉，用占位字法保留其宽度（否则逗号后空格消失、词贴标点）
                int w = TextRenderer.MeasureText(t.Text + "X", Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width - xWidth;
                bool isSpaceRun = t.Text.Trim().Length == 0;
                if (x > 0 && x + w > Width && !isSpaceRun)
                {
                    x = 0; y += lineH;
                }
                if (x == 0 && isSpaceRun) { t.Rect = Rectangle.Empty; continue; } // 行首空格不画
                t.Rect = new Rectangle(x, y, w, lineH);
                x += w;
            }
            _contentHeight = y + lineH;
            _tokens = parts;
            _layoutDirty = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_layoutDirty || (_tokens.Count == 0 && _text.Length > 0)) LayoutTokens();
            foreach (var t in _tokens)
            {
                if (t.Rect == Rectangle.Empty) continue;
                var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
                if (t == _hover && t.IsWord)
                {
                    using (var uf = new Font(Font, FontStyle.Underline))
                        TextRenderer.DrawText(e.Graphics, t.Text, uf, t.Rect.Location, ForeColor, flags);
                }
                else
                {
                    TextRenderer.DrawText(e.Graphics, t.Text, Font, t.Rect.Location, ForeColor, flags);
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Token hit = null;
            foreach (var t in _tokens)
            {
                if (t.IsWord && t.Rect.Contains(e.Location)) { hit = t; break; }
            }
            if (hit != _hover)
            {
                _hover = hit;
                Cursor = hit != null ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != null) { _hover = null; Cursor = Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (_hover != null && WordClicked != null)
                WordClicked(_hover.Text, e.Location);
        }
    }
}
