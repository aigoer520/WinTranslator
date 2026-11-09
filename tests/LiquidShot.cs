// v1.7.0 液态玻璃皮肤实拍自查（VM 字体环境限制：中文显形为准，真机以用户验为准）
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WinTranslator;

static class LiquidShot
{
    static readonly BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Instance;

    static void Shot(Form f, string name)
    {
        // 真屏抓图（同 LucideShot/FrostShot 口径）：DrawToBitmap 在 Mono 下抓不到 BackgroundImage
        f.Refresh();
        System.Threading.Thread.Sleep(300);
        Application.DoEvents();
        var bounds = f.Bounds;
        var bmp = new Bitmap(Math.Max(50, bounds.Width), Math.Max(50, bounds.Height));
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
        bmp.Save("/tmp/boards170/" + name + ".png");
        bmp.Dispose();
        Console.WriteLine("SHOT " + name + " " + bounds.Width + "x" + bounds.Height);
    }

    [STAThread]
    static void Main()
    {
        Directory.CreateDirectory("/tmp/boards170");
        Application.EnableVisualStyles();
        GlassMotion.Enabled = false;

        var main = new MainForm();
        // MainForm 无参构造按盘上 settings 归一化皮肤（真实用户盘里此时还是旧皮肤），
        // 构造后热切换到 liquid，与 FrostShot/LucideShot 的实拍口径一致
        typeof(MainForm).GetMethod("ApplySkinLive", BF).Invoke(main, new object[] { "liquid" });
        var store = (Store)typeof(MainForm).GetField("_store", BF).GetValue(main);
        var w1 = new Entry
        {
            Src = "serendipity", Dst = "n. 意外发现美好事物的运气；机缘巧合", SrcLang = "en", DstLang = "zh",
            Provider = "有道", Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ExamplesSource = "GLM 生成",
            Examples = new List<ExampleItem> { new ExampleItem { En = "Finding that little café was pure serendipity.", Zh = "找到那家小咖啡馆纯属意外之喜。", Source = "GLM" } }
        };
        if (!store.Favorites.Exists(f => f.Src == "serendipity")) store.AddFavorite(w1);
        var w2 = new Entry { Src = "resilient", Dst = "adj. 有韧性的；能迅速恢复的", SrcLang = "en", DstLang = "zh", Provider = "有道", Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
        if (!store.Favorites.Exists(f => f.Src == "resilient")) store.AddFavorite(w2);

        main.Show();
        Application.DoEvents();
        Shot(main, "v170-01-home");
        typeof(MainForm).GetMethod("ShowPage", BF).Invoke(main, new object[] { 1 });
        Application.DoEvents();
        Shot(main, "v170-02-wordbook");
        var pageWord = typeof(MainForm).GetField("_pageWord", BF).GetValue(main);
        pageWord.GetType().GetMethod("ToggleExpand", BF).Invoke(pageWord, new object[] { w1 });
        Application.DoEvents();
        Shot(main, "v170-03-wordbook-detail");

        var settings = new SettingsForm(store, () => "就绪", () => { });
        settings.Show();
        Application.DoEvents();
        int sel = (int)typeof(SettingsForm).GetField("_skinSel", BF).GetValue(settings);
        Console.WriteLine("skinSel=" + sel + " (期望 5=液态玻璃)");
        Shot(settings, "v170-04-settings");
        settings.Close();

        // 热切换往返：liquid → classic → liquid
        typeof(MainForm).GetMethod("ApplySkinLive", BF).Invoke(main, new object[] { "classic" });
        Application.DoEvents();
        Console.WriteLine("after classic: skin=" + SkinMode.Current + " card=" + Palette.Card);
        typeof(MainForm).GetMethod("ApplySkinLive", BF).Invoke(main, new object[] { "liquid" });
        Application.DoEvents();
        Console.WriteLine("back to liquid: skin=" + SkinMode.Current + " card=" + Palette.Card);
        var nav = typeof(MainForm).GetField("_navPanel", BF).GetValue(main) as Panel;
        Console.WriteLine("navBgImg=" + (nav.BackgroundImage != null ? "set" : "NULL"));
        Shot(main, "v170-05-home-back-to-liquid");
        main.Close();

        var engine = new TranslationEngine();
        var mini = new MiniForm(engine, store, () => { });
        mini.Show();
        Application.DoEvents();
        Shot(mini, "v170-06-mini");
        mini.Close();

        var bmp = new Bitmap(640, 200);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            using (var f = new Font("Arial", 16)) g.DrawString("Screenshot translation makes reading foreign manuals much easier.", f, Brushes.Black, 10, 20);
        }
        var rf = new ResultForm(engine, store, "Screenshot translation makes reading foreign manuals much easier.", (Bitmap)bmp.Clone());
        rf.Show();
        Application.DoEvents();
        Console.WriteLine("combo bgImg=" + (rf.BackgroundImage != null ? "set" : "NULL"));
        Shot(rf, "v170-07-combo");
        rf.Close();
    }
}
