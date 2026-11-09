// v1.7.2 实拍自查：查词小窗 Fill 后真屏抓图（看例句块高度/文字不被截断）＋首页英文译文点词态抓图。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using WinTranslator;

static class SelectShot172
{
    static readonly BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Instance;

    static void Grab(Form f, string name)
    {
        f.Refresh();
        Thread.Sleep(350);
        Application.DoEvents();
        var b = f.Bounds;
        var bmp = new Bitmap(Math.Max(50, b.Width + 16), Math.Max(50, b.Height + 16));
        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(b.Left - 8, b.Top - 8, 0, 0, bmp.Size);
        bmp.Save("/tmp/boards172/" + name + ".png");
        bmp.Dispose();
        Console.WriteLine("SHOT " + name + " formH=" + f.Height);
    }

    [STAThread]
    static void Main()
    {
        Directory.CreateDirectory("/tmp/boards172");
        Application.EnableVisualStyles();
        GlassMotion.Enabled = false;
        var main = new MainForm();
        main.Show();
        Application.DoEvents();
        var txtOutput = (TextBox)typeof(MainForm).GetField("_txtOutput", BF).GetValue(main);
        typeof(MainForm).GetField("_homeDstLang", BF).SetValue(main, "en");
        txtOutput.Text = "Long legs keep them high above the hot desert sands, and if they do have to kneel down, they have specialized pads on their contact points with the ground.";
        typeof(MainForm).GetMethod("ApplyHomeWordMode", BF).Invoke(main, null);
        Application.DoEvents();
        Grab(main, "v172-01-home-en");

        var store = (Store)typeof(MainForm).GetField("_store", BF).GetValue(main);
        var pop = new DictPopupForm(store, s => { }, () => { }, null);
        var r = new DictResult
        {
            Word = "serendipity",
            UsPhone = "/ˌserənˈdɪpəti/",
            UkPhone = "/ˌserənˈdɪpəti/",
            Definitions = new List<string> { "n. 意外发现美好事物的运气；机缘巧合", "adj. 偶然发现的（serendipitous）" },
            Examples = new List<ExampleItem> { new ExampleItem { En = "Finding that little café was pure serendipity, and we stayed there talking until the rain stopped outside.", Zh = "找到那家小咖啡馆纯属意外之喜，我们在那里一直聊到外面的雨停。" } }
        };
        typeof(DictPopupForm).GetMethod("Fill", BF).Invoke(pop, new object[] { r });
        pop.Location = new Point(1150, 120);
        pop.Show();
        Application.DoEvents();
        Grab(pop, "v172-02-popup");
        Console.Out.Flush();
        Environment.Exit(0);
    }
}
