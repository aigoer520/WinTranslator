using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using WinTranslator;

// v1.6.3 实拍：①长句展开详情（删/去翻译独立行不压字＋行头无译文）②译圆钮红底 ③悬浮球红底 ④应用图标墨迹居中（放大渲染验）
class FixShot163
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
            bmp.Save("/tmp/boards163/" + name + ".png");
        }
        Console.WriteLine("shot " + name);
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        var main = new MainForm();
        var store = (Store)typeof(MainForm).GetField("_store", BF).GetValue(main);
        var sent = new Entry
        {
            Src = "Longlegs keep them high above the hot desert sands which can be up to 80 degrees, and if they do have to kneel down, they have specialized pads on their contact points with the ground.",
            Dst = "长腿将它们高高置于高达80°C的炎热沙漠沙地上。如果它们不得不跪下，它们在与地面的接触点上有专门的垫子。",
            SrcLang = "en", DstLang = "zh", Provider = "有道", Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        if (!store.Favorites.Exists(f => f.Src == sent.Src)) store.AddFavorite(sent);
        var w1 = new Entry
        {
            Src = "zzv163probe", Dst = "adj. 必然的，不可避免的", SrcLang = "en", DstLang = "zh",
            Provider = "有道", Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ExamplesSource = "有道词典",
            Examples = new List<ExampleItem> { new ExampleItem { En = "Reform is inevitable.", Zh = "改革是不可避免的。", Source = "21世纪大英汉词典" } }
        };
        if (!store.Favorites.Exists(f => f.Src == "zzv163probe")) store.AddFavorite(w1);

        main.Show();
        typeof(MainForm).GetMethod("ShowPage", BF).Invoke(main, new object[] { 1 });
        var pageWord = typeof(MainForm).GetField("_pageWord", BF).GetValue(main);
        pageWord.GetType().GetMethod("SelectTab", BF).Invoke(pageWord, new object[] { false });
        var favS = store.Favorites.Find(f => f.Src == sent.Src);
        pageWord.GetType().GetMethod("ToggleExpand", BF).Invoke(pageWord, new object[] { favS });
        Shot(main, "v163-01-sentence-detail");

        pageWord.GetType().GetMethod("SelectTab", BF).Invoke(pageWord, new object[] { true });
        var favW = store.Favorites.Find(f => f.Src == "zzv163probe");
        pageWord.GetType().GetMethod("ToggleExpand", BF).Invoke(pageWord, new object[] { favW });
        Shot(main, "v163-02-word-yi-red");
        main.Hide();

        var ball = new FloatBall(store);
        ball.Show();
        Shot(ball, "v163-03-ball-red");
        ball.Close();

        // 应用图标：反射调 MakeAppIcon，放大 8 倍存图验居中
        var mi = typeof(MainForm).GetMethod("MakeAppIcon", BindingFlags.NonPublic | BindingFlags.Static);
        var icon = (Icon)mi.Invoke(null, null);
        using (var bmp = icon.ToBitmap())
        using (var big = new Bitmap(256, 256))
        using (var g = Graphics.FromImage(big))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.Clear(Color.FromArgb(200, 210, 220));
            g.DrawImage(bmp, 0, 0, 256, 256);
            big.Save("/tmp/boards163/v163-04-appicon-big.png");
        }
        Console.WriteLine("shot v163-04-appicon-big");
        main.Close();
        Console.WriteLine("DONE");
    }
}
