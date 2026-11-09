using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using WinTranslator;

// v1.6.0 Lucide 线条皮肤实拍自查：真实窗体在 lucide 下渲染（主页/单词本/历史/组合窗/查词/迷你/设置皮肤行/悬浮球）＋热切换回经典验色
class LucideShot
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
            bmp.Save("/tmp/boards160/" + name + ".png");
        }
        Console.WriteLine("shot " + name);
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        var store = new Store();
        var engine = new TranslationEngine();
        var e1 = new Entry
        {
            Src = "zzv160probe", Dst = "adj. 必然的，不可避免的", SrcLang = "en", DstLang = "zh",
            Provider = "有道", Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ReviewLevel = 2,
            ExamplesSource = "有道词典",
            Examples = new List<ExampleItem>
            {
                new ExampleItem { En = "Reform is inevitable.", Zh = "改革是不可避免的。", Source = "21世纪大英汉词典" },
                new ExampleItem { En = "It was an inevitable consequence of the war.", Zh = "那是战争的必然结果。", Source = "" }
            }
        };

        var main = new MainForm();
        store = (Store)typeof(MainForm).GetField("_store", BF).GetValue(main);
        if (!store.Favorites.Exists(f => f.Src == "zzv160probe")) store.AddFavorite(e1);
        if (!store.Favorites.Exists(f => f.Src == "prosperity")) store.AddFavorite(new Entry { Src = "prosperity", Dst = "n. 繁荣，昌盛", SrcLang = "en", DstLang = "zh", Provider = "有道", Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 86400, ReviewLevel = 1 });
        if (!store.Favorites.Exists(f => f.Src == "resilient")) store.AddFavorite(new Entry { Src = "resilient", Dst = "adj. 有弹性的；能复原的", SrcLang = "en", DstLang = "zh", Provider = "有道", Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 172800 });
        store.AddHistory(new Entry { Src = "Long legs keep them high above the hot desert sands.", Dst = "长腿使它们能高高地悬在炎热的沙地上方。", SrcLang = "en", DstLang = "zh", Provider = "有道", Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600 });
        store.AddHistory(new Entry { Src = "The committee has approved the budget for next year.", Dst = "委员会已批准明年的预算。", SrcLang = "en", DstLang = "zh", Provider = "必应", Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 7200 });

        main.Show();
        typeof(MainForm).GetMethod("ApplySkinLive", BF).Invoke(main, new object[] { "lucide" });
        Console.WriteLine("skin=" + SkinMode.Current + " accent=" + Palette.Accent + " cardedge=" + Palette.CardEdge);
        Shot(main, "v160-01-home-lucide");

        typeof(MainForm).GetMethod("ShowPage", BF).Invoke(main, new object[] { 1 });
        var pageWord = typeof(MainForm).GetField("_pageWord", BF).GetValue(main);
        var fav = store.Favorites.Find(f => f.Src == "zzv160probe");
        pageWord.GetType().GetMethod("ToggleExpand", BF).Invoke(pageWord, new object[] { fav });
        var rev = (HashSet<int>)pageWord.GetType().GetField("_revealed", BF).GetValue(pageWord);
        rev.Add(0);
        pageWord.GetType().GetMethod("RefreshAll").Invoke(pageWord, null);
        Shot(main, "v160-02-wordbook-lucide");

        typeof(MainForm).GetMethod("ShowPage", BF).Invoke(main, new object[] { 2 });
        Shot(main, "v160-03-history-lucide");

        // 热切换回经典：验 Accent/描边回位无串色
        typeof(MainForm).GetMethod("ApplySkinLive", BF).Invoke(main, new object[] { "classic" });
        typeof(MainForm).GetMethod("ShowPage", BF).Invoke(main, new object[] { 0 });
        Shot(main, "v160-04-home-back-to-classic");
        typeof(MainForm).GetMethod("ApplySkinLive", BF).Invoke(main, new object[] { "lucide" });
        main.Hide();

        using (var img = new Bitmap(760, 300))
        {
            using (var g = Graphics.FromImage(img)) { g.Clear(Color.White); using (var f = new Font("Georgia", 16f)) g.DrawString("Long legs keep them high above the hot desert sands.", f, Brushes.Black, 20, 30); }
            var rf = new ResultForm(engine, store, "Long legs keep them high above the hot desert sands.", (Bitmap)img.Clone());
            rf.Show();
            rf.SetPrecomputed("Long legs keep them high above the hot desert sands.", "长腿使它们能高高地悬在炎热的沙地上方。", "en", "zh");
            rf.SetComboImages(img, img);
            Shot(rf, "v160-05-combo-lucide");
            rf.Close();
        }

        var dict = new DictResult
        {
            Word = "inevitable", UsPhone = "[ɪn'evɪtəbl]", UkPhone = "[ɪn'evɪtəbl]",
            Definitions = new List<string> { "adj. 必然的，不可避免的" },
            Examples = new List<ExampleItem> { new ExampleItem { En = "Reform is inevitable.", Zh = "改革是不可避免的。" } }
        };
        var dp = new DictPopupForm(store, s => { }, () => { }, s => { });
        dp.Show();
        typeof(DictPopupForm).GetMethod("Fill", BF).Invoke(dp, new object[] { dict });
        Shot(dp, "v160-06-dictpopup-lucide");
        dp.Close();

        var mini = new MiniForm(engine, store, () => { });
        mini.Show();
        Shot(mini, "v160-07-mini-lucide");
        mini.Close();

        var ball = new FloatBall(store);
        ball.Show();
        Shot(ball, "v160-08-ball-lucide");
        ball.Close();

        // 设置：皮肤行（5 选项）区域
        store.Settings.Skin = "lucide";
        var sf = new SettingsForm(store, () => "", () => { });
        sf.Show();
        sf.LoadFromSettings();
        var page = typeof(SettingsForm).GetField("_pageGeneral", BF).GetValue(sf) as Panel;
        if (page != null) page.AutoScrollPosition = new Point(0, 330);
        Shot(sf, "v160-09-settings-skinrow");
        sf.Close();

        main.Close();
        Console.WriteLine("DONE");
    }
}
