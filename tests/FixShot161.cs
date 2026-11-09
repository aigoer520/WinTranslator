using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using WinTranslator;

// v1.6.1 三处真机反馈修复实拍：①查词弹窗朗读/关闭间距（经典皮肤）②单词本列表/卡片图标间距 ③查词点在屏幕底部时弹窗不探出
class FixShot161
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
            bmp.Save("/tmp/boards161/" + name + ".png");
        }
        Console.WriteLine("shot " + name);
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        var store = new Store();
        var dict = new DictResult
        {
            Word = "carefully", UsPhone = "'kerfəli", UkPhone = "'keəfəli",
            Definitions = new List<string> { "adv. 小心地，仔细地" },
            Examples = new List<ExampleItem> { new ExampleItem { En = "I counted the money carefully.", Zh = "我仔细点了这笔钱。" } }
        };
        // ①+③：弹窗定位到屏幕底部附近再 Fill（带例句变高），验不探出＋朗读/关闭不重叠
        var dp = new DictPopupForm(store, s => { }, () => { }, s => { });
        var wa = Screen.PrimaryScreen.WorkingArea;
        dp.Location = new Point(wa.Left + 200, wa.Bottom - 170);
        dp.Show();
        typeof(DictPopupForm).GetMethod("Fill", BF).Invoke(dp, new object[] { dict });
        Console.WriteLine("popup bottom=" + dp.Bounds.Bottom + " waBottom=" + wa.Bottom + " right=" + dp.Bounds.Right + " waRight=" + wa.Right);
        Shot(dp, "v161-01-dictpopup-bottom");
        dp.Close();

        // ②：单词本头部（列表/卡片切换）
        var main = new MainForm();
        main.Show();
        typeof(MainForm).GetMethod("ShowPage", BF).Invoke(main, new object[] { 1 });
        Shot(main, "v161-02-wordbook-head");
        main.Close();
        Console.WriteLine("DONE");
    }
}
