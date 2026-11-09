using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using WinTranslator;

// v1.5.3 美化实拍自查：真实产品控件渲染（单词本展开+例句内嵌块/首页/历史/组合窗/查词弹窗/迷你窗/设置字体与云同步区）
class V153Shot
{
    static readonly BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Instance;
    static int n = 0;

    static void Shot(Control c, string name)
    {
        Application.DoEvents();
        Thread.Sleep(400);
        var bounds = c is Form f ? f.Bounds : c.RectangleToScreen(c.ClientRectangle);
        using (var bmp = new Bitmap(bounds.Width, bounds.Height))
        {
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
            bmp.Save("/tmp/boards153/" + name + ".png");
        }
        Console.WriteLine("shot " + name);
    }

    [STAThread]
    static void Main()
    {
        System.IO.Directory.CreateDirectory("/tmp/boards153");
        Application.EnableVisualStyles();
        var store = new Store();
        var engine = new TranslationEngine();

        // 种子：带例句缓存的词条（展开即显示内嵌块，不走网络）
        var e1 = new Entry
        {
            Src = "zzv153probe", Dst = "adj. 必然的，不可避免的", SrcLang = "en", DstLang = "zh",
            Provider = "有道", Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ReviewLevel = 2,
            ExamplesSource = "有道词典",
            Examples = new List<ExampleItem>
            {
                new ExampleItem { En = "Reform is inevitable.", Zh = "改革是不可避免的。", Source = "21世纪大英汉词典" },
                new ExampleItem { En = "It was an inevitable consequence of the war.", Zh = "那是战争的必然结果。", Source = "" }
            }
        };
        if (!store.Favorites.Exists(f => f.Src == "zzv153probe")) store.AddFavorite(e1);
        if (!store.Favorites.Exists(f => f.Src == "prosperity")) store.AddFavorite(new Entry { Src = "prosperity", Dst = "n. 繁荣，昌盛", SrcLang = "en", DstLang = "zh", Provider = "有道", Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 86400, ReviewLevel = 1 });
        if (!store.Favorites.Exists(f => f.Src == "resilient")) store.AddFavorite(new Entry { Src = "resilient", Dst = "adj. 有弹性的；能复原的", SrcLang = "en", DstLang = "zh", Provider = "有道", Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 172800 });
        store.AddHistory(new Entry { Src = "Long legs keep them high above the hot desert sands.", Dst = "长腿使它们能高高地悬在炎热的沙地上方。", SrcLang = "en", DstLang = "zh", Provider = "有道", Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600 });
        store.AddHistory(new Entry { Src = "The committee has approved the budget for next year.", Dst = "委员会已批准明年的预算。", SrcLang = "en", DstLang = "zh", Provider = "必应", Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 7200 });

        var main = new MainForm();
        store = (Store)typeof(MainForm).GetField("_store", BF).GetValue(main);
        if (!store.Favorites.Exists(f => f.Src == "zzv153probe")) store.AddFavorite(e1);
        if (!store.Favorites.Exists(f => f.Src == "prosperity")) store.AddFavorite(new Entry { Src = "prosperity", Dst = "n. 繁荣，昌盛", SrcLang = "en", DstLang = "zh", Provider = "有道", Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 86400, ReviewLevel = 1 });
        if (!store.Favorites.Exists(f => f.Src == "resilient")) store.AddFavorite(new Entry { Src = "resilient", Dst = "adj. 有弹性的；能复原的", SrcLang = "en", DstLang = "zh", Provider = "有道", Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 172800 });
        main.Show();
        Shot(main, "v153-01-home");

        // 单词本：展开 inevitable（含例句块，并点开第一条译文）
        Console.WriteLine("step showpage method=" + (typeof(MainForm).GetMethod("ShowPage", BF) != null));
        typeof(MainForm).GetMethod("ShowPage", BF).Invoke(main, new object[] { 1 });
        var pageWord = typeof(MainForm).GetField("_pageWord", BF).GetValue(main);
        Console.WriteLine("step pageWord=" + (pageWord != null));
        var fav = store.Favorites.Find(f => f.Src == "zzv153probe");
        Console.WriteLine("step fav=" + (fav != null) + " toggle=" + (pageWord.GetType().GetMethod("ToggleExpand", BF) != null));
        pageWord.GetType().GetMethod("ToggleExpand", BF).Invoke(pageWord, new object[] { fav });
        Application.DoEvents();
        Thread.Sleep(300);
        // 点开第一条例句的"显示翻译"（通过 RefreshAll 前的内部集合：直接操作 _revealed）
        Console.WriteLine("step toggled");
        var revField = pageWord.GetType().GetField("_revealed", BF);
        Console.WriteLine("step revField=" + (revField != null));
        var rev = (System.Collections.Generic.HashSet<int>)revField.GetValue(pageWord);
        rev.Add(0);
        Console.WriteLine("step expandedNow=" + (pageWord.GetType().GetField("_expanded", BF).GetValue(pageWord) != null));
        pageWord.GetType().GetMethod("RefreshAll").Invoke(pageWord, null);
        Shot(main, "v153-02-wordbook-expanded");

        // 历史
        typeof(MainForm).GetMethod("ShowPage", BF).Invoke(main, new object[] { 2 });
        Shot(main, "v153-03-history");
        main.Hide();

        // 组合窗
        using (var img = new Bitmap(760, 300))
        {
            using (var g = Graphics.FromImage(img)) { g.Clear(Color.White); using (var f = new Font("Georgia", 16f)) g.DrawString("Long legs keep them high above the hot desert sands.", f, Brushes.Black, 20, 30); }
            var rf = new ResultForm(engine, store, "Long legs keep them high above the hot desert sands.", (Bitmap)img.Clone());
            rf.Show();
            rf.SetPrecomputed("Long legs keep them high above the hot desert sands.", "长腿使它们能高高地悬在炎热的沙地上方。", "en", "zh");
            rf.SetComboImages(img, img);
            Shot(rf, "v153-04-combo");
            rf.Close();
        }

        // 查词弹窗（带例句内嵌块）
        var dict = new DictResult
        {
            Word = "inevitable", UsPhone = "[ɪn'evɪtəbl]", UkPhone = "[ɪn'evɪtəbl]",
            Definitions = new List<string> { "adj. 必然的，不可避免的" },
            Examples = new List<ExampleItem> { new ExampleItem { En = "Reform is inevitable.", Zh = "改革是不可避免的。" } }
        };
        var dp = new DictPopupForm(store, s => { }, () => { }, s => { });
        dp.Show();
        typeof(DictPopupForm).GetMethod("Fill", BF).Invoke(dp, new object[] { dict });
        Shot(dp, "v153-05-dictpopup");
        dp.Close();

        // 迷你窗
        var mini = new MiniForm(engine, store, () => { });
        mini.Show();
        Shot(mini, "v153-06-mini");
        mini.Close();

        // 设置：字体区与云同步区
        var sf = new SettingsForm(store, () => "", () => { });
        sf.Show();
        sf.LoadFromSettings();
        var page = typeof(SettingsForm).GetField("_pageGeneral", BF).GetValue(sf) as Panel;
        if (page != null) page.AutoScrollPosition = new Point(0, 700);
        Shot(sf, "v153-07-settings-font");
        if (page != null) page.AutoScrollPosition = new Point(0, 1100);
        Shot(sf, "v153-08-settings-sync");
        sf.Close();

        main.Close();
        Console.WriteLine("V153 SHOTS DONE");
    }
}
