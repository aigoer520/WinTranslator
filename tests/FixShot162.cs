using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using WinTranslator;

// v1.6.2 实拍：①单词本词义默认隐藏→点译圆钮显示 ②句子页签译文隐藏 ③例句译圆钮 ④首页引擎胶囊对齐
class FixShot162
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
            bmp.Save("/tmp/boards162/" + name + ".png");
        }
        Console.WriteLine("shot " + name);
    }

    static Control FindYiToggle(Control root)
    {
        foreach (Control c in root.Controls)
        {
            var rp = c as RoundPanel;
            if (rp != null && rp.Width == 22 && rp.Height == 22)
            {
                foreach (Control ch in rp.Controls)
                    if (ch is Label && ch.Text == "译") return rp;
            }
            var r = FindYiToggle(c);
            if (r != null) return r;
        }
        return null;
    }

    static void ClickCtl(Control c)
    {
        c.GetType().GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(c, new object[] { EventArgs.Empty });
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        var main = new MainForm();
        var store = (Store)typeof(MainForm).GetField("_store", BF).GetValue(main);
        var e1 = new Entry
        {
            Src = "zzv162probe", Dst = "adj. 必然的，不可避免的", SrcLang = "en", DstLang = "zh",
            Provider = "有道", Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ReviewLevel = 2,
            ExamplesSource = "有道词典",
            Examples = new List<ExampleItem> { new ExampleItem { En = "Reform is inevitable.", Zh = "改革是不可避免的。", Source = "21世纪大英汉词典" } }
        };
        if (!store.Favorites.Exists(f => f.Src == "zzv162probe")) store.AddFavorite(e1);
        var sent = new Entry { Src = "Knowledge is a treasure, but practice is the key to it.", Dst = "知识是宝藏，而实践是开启它的钥匙。", SrcLang = "en", DstLang = "zh", Provider = "有道", Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600 };
        if (!store.Favorites.Exists(f => f.Src == sent.Src)) store.AddFavorite(sent);

        main.Show();
        Shot(main, "v162-01-home-engine");

        typeof(MainForm).GetMethod("ShowPage", BF).Invoke(main, new object[] { 1 });
        var pageWord = typeof(MainForm).GetField("_pageWord", BF).GetValue(main);
        var fav = store.Favorites.Find(f => f.Src == "zzv162probe");
        pageWord.GetType().GetMethod("ToggleExpand", BF).Invoke(pageWord, new object[] { fav });
        Shot(main, "v162-02-word-hidden");

        var tog = FindYiToggle((Control)pageWord);
        Console.WriteLine("yi toggle found=" + (tog != null));
        if (tog != null) { ClickCtl(tog); Application.DoEvents(); Thread.Sleep(300); }
        Shot(main, "v162-03-word-revealed");

        // 句子页签
        pageWord.GetType().GetMethod("SelectTab", BF).Invoke(pageWord, new object[] { false });
        var favS = store.Favorites.Find(f => f.Src == sent.Src);
        pageWord.GetType().GetMethod("ToggleExpand", BF).Invoke(pageWord, new object[] { favS });
        Shot(main, "v162-04-sentence-hidden");
        main.Close();
        Console.WriteLine("DONE");
    }
}
