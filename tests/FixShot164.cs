using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using WinTranslator;

// v1.6.4 验证：①海报式图片 OCR→组段（同字号闸）→合成，译文各就原位 ②单词本译/隐成对 ③引擎红底胶囊 ④组合窗原文拖选出选取层
class FixShot164
{
    static readonly BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Instance;

    static void Shot(Control c, string name)
    {
        Application.DoEvents();
        Thread.Sleep(400);
        var bounds = c is Form f ? f.Bounds : c.RectangleToScreen(c.ClientRectangle);
        using (var bmp = new Bitmap(bounds.Width, bounds.Height))
        {
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
            bmp.Save("/tmp/boards164/" + name + ".png");
        }
        Console.WriteLine("shot " + name);
    }

    static Control FindToggle(Control root, string glyph)
    {
        foreach (Control c in root.Controls)
        {
            var rp = c as RoundPanel;
            if (rp != null && rp.Width == 22 && rp.Height == 22)
                foreach (Control ch in rp.Controls)
                    if (ch is Label && ch.Text == glyph) return rp;
            var r = FindToggle(c, glyph);
            if (r != null) return r;
        }
        return null;
    }

    static void ClickCtl(Control c)
    {
        c.GetType().GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(c, new object[] { EventArgs.Empty });
    }

    static void MouseCtl(Control c, string method, int x, int y)
    {
        var m = c.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
        m.Invoke(c, new object[] { new MouseEventArgs(MouseButtons.Left, 1, x, y, 0) });
    }


    static void ProbeCapsule(Control root)
    {
        var sc = root as SegmentControl;
        if (sc != null)
        {
            Console.WriteLine("CAPSULE AccentSelected=" + sc.AccentSelected + " children=" + sc.Controls.Count);
            foreach (Control ch in sc.Controls)
            {
                var rp = ch as RoundPanel;
                if (rp != null) Console.WriteLine("  pill Fill=" + rp.Fill + " loc=" + rp.Location + " size=" + rp.Size);
                if (ch is Label) Console.WriteLine("  lbl " + ch.Text + " fore=" + ch.ForeColor + " useCompat=" + ((Label)ch).UseCompatibleTextRendering);
            }
        }
        foreach (Control c in root.Controls) ProbeCapsule(c);
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();

        // ① 海报式图：红横幅大标题＋深红大名＋小字两行＋小标注
        var poster = new Bitmap(613, 402);
        using (var g = Graphics.FromImage(poster))
        {
            g.Clear(Color.White);
            using (var br = new SolidBrush(Color.FromArgb(190, 10, 50))) g.FillRectangle(br, 0, 0, 613, 108);
            using (var f = new Font("Arial", 34f, FontStyle.Bold)) g.DrawString("WANTED BY THE FBI", f, Brushes.White, 120, 28);
            using (var f = new Font("Arial", 26f, FontStyle.Bold)) g.DrawString("PARK JIN HYOK", f, new SolidBrush(Color.FromArgb(150, 10, 40)), 175, 122);
            using (var f = new Font("Arial", 10.5f, FontStyle.Bold)) g.DrawString("Conspiracy to Commit Wire Fraud and Bank Fraud; Conspiracy to", f, Brushes.Black, 62, 168);
            using (var f = new Font("Arial", 10.5f, FontStyle.Bold)) g.DrawString("Commit Computer-Related Fraud (Computer Intrusion)", f, Brushes.Black, 128, 190);
            using (var br = new SolidBrush(Color.FromArgb(40, 110, 190))) g.FillRectangle(br, 240, 225, 130, 130);
            using (var f = new Font("Arial", 10f, FontStyle.Bold)) g.DrawString("DESCRIPTION", f, Brushes.Black, 258, 362);
        }
        poster.Save("/tmp/boards164/poster-src.png");
        var blocks = OcrService.RecognizeBlocks(poster);
        var regions = OverlayComposer.MergeParagraphs(blocks);
        Console.WriteLine("POSTER blocks=" + blocks.Count + " regions=" + regions.Count);
        foreach (var r in regions) Console.WriteLine("  region [" + r.Rect.X + "," + r.Rect.Y + " " + r.Rect.Width + "x" + r.Rect.Height + "] " + r.Text);
        var trs = new List<string>();
        foreach (var r in regions)
        {
            if (r.Text.Contains("WANTED")) trs.Add("联邦调查局通缉");
            else if (r.Text.Contains("PARK")) trs.Add("朴振赫");
            else if (r.Text.Contains("Conspiracy")) trs.Add("共谋实施电信欺诈和银行欺诈；共谋进行与电脑有关的诈骗（电脑入侵）");
            else if (r.Text.Contains("DESCRIPTION")) trs.Add("描述");
            else trs.Add("译文");
        }
        var composed = OverlayComposer.Compose(poster, regions, trs);
        composed.Save("/tmp/boards164/v164-01-poster-composed.png");
        Console.WriteLine("shot v164-01-poster-composed");

        // ②③ 主窗与单词本
        var main = new MainForm();
        var store = (Store)typeof(MainForm).GetField("_store", BF).GetValue(main);
        var engine = new TranslationEngine();
        var w1 = new Entry
        {
            Src = "zzv164probe", Dst = "adj. 必然的，不可避免的", SrcLang = "en", DstLang = "zh",
            Provider = "有道", Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ExamplesSource = "有道词典",
            Examples = new List<ExampleItem> { new ExampleItem { En = "Reform is inevitable.", Zh = "改革是不可避免的。", Source = "21世纪大英汉词典" } }
        };
        if (!store.Favorites.Exists(f => f.Src == "zzv164probe")) store.AddFavorite(w1);
        main.Show();
        Shot(main, "v164-02-main-full");

        typeof(MainForm).GetMethod("ShowPage", BF).Invoke(main, new object[] { 1 });
        var pageWord = typeof(MainForm).GetField("_pageWord", BF).GetValue(main);
        foreach (Control c0 in main.Controls) ProbeCapsule(c0);
        var fav = store.Favorites.Find(f => f.Src == "zzv164probe");
        pageWord.GetType().GetMethod("ToggleExpand", BF).Invoke(pageWord, new object[] { fav });
        var togYi = FindToggle((Control)pageWord, "译");
        Console.WriteLine("yi found=" + (togYi != null));
        if (togYi != null) { ClickCtl(togYi); Application.DoEvents(); Thread.Sleep(300); }
        Shot(main, "v164-03-meaning-revealed-yin");
        // 例句：再点第一个译钮（此时词义已显，第一个未显的是例句的）
        var togEx = FindToggle((Control)pageWord, "译");
        Console.WriteLine("example yi found=" + (togEx != null));
        if (togEx != null) { ClickCtl(togEx); Application.DoEvents(); Thread.Sleep(300); }
        Shot(main, "v164-04-example-revealed-yin");
        main.Hide();

        // ④ 组合窗：英文原文→点词层；拖动>6px→选取层出现
        var rf = new ResultForm(engine, store, "Long legs keep them high above the hot desert sands.", (Bitmap)poster.Clone());
        rf.Show();
        rf.SetPrecomputed("Long legs keep them high above the hot desert sands.", "长腿使它们能高高地悬在炎热的沙地上方。", "en", "zh");
        rf.SetComboImages(poster, composed);
        Application.DoEvents(); Thread.Sleep(400);
        var wt = typeof(ResultForm).GetField("_sWtSrc", BF).GetValue(rf) as Control;
        var selBox = typeof(ResultForm).GetField("_sSrcSelect", BF).GetValue(rf) as Control;
        Console.WriteLine("wt visible chain: wt=" + (wt != null) + " selVisible(before)=" + (selBox != null && selBox.Visible));
        if (wt != null)
        {
            MouseCtl(wt, "OnMouseDown", 5, 5);
            MouseCtl(wt, "OnMouseUp", 120, 18);
            Application.DoEvents(); Thread.Sleep(200);
        }
        Console.WriteLine("selVisible(after drag)=" + (selBox != null && selBox.Visible));
        Shot(rf, "v164-05-combo-select-layer");
        rf.Close();
        main.Close();
        Console.WriteLine("DONE");
    }
}
