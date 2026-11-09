using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using WinTranslator;

/// <summary>v1.5.0 全量自查：设置/查词/迷你/钉图四窗在 frosted 下的实拍＋淡出关闭验证。</summary>
public static class FrostShot2
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        var store = new Store();
        store.Settings.Skin = "frosted";
        SkinMode.Current = "frosted";
        Palette.Apply("frosted");
        var engine = new TranslationEngine();

        // 设置窗
        var settings = new SettingsForm(store, () => "Ctrl+Alt+S", () => { });
        settings.Location = new Point(40, 30);
        settings.Show();
        Pump(900);
        Grab(settings.Bounds, "/tmp/frost-settings.png");
        settings.Close();
        Pump(800);
        Console.WriteLine("settings closed=" + settings.IsDisposed);

        // 查词弹窗
        var dict = new DictPopupForm(store, s => { }, () => { }, w => { });
        dict.ShowFor("persistence", new Point(500, 200));
        Pump(1500);
        Grab(new Rectangle(dict.Location, new Size(dict.Width + 4, dict.Height + 4)), "/tmp/frost-dict.png");
        dict.Close();
        Pump(800);
        Console.WriteLine("dict closed=" + dict.IsDisposed);

        // 迷你窗
        var mini = new MiniForm(engine, store, () => { });
        mini.Location = new Point(700, 500);
        mini.Show();
        Pump(900);
        Grab(mini.Bounds, "/tmp/frost-mini.png");
        mini.Close();
        Pump(800);
        Console.WriteLine("mini closed=" + mini.IsDisposed);

        // 钉图窗
        var bmp = new Bitmap(220, 130);
        using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.SteelBlue); g.DrawString("nail test", new Font("Arial", 14), Brushes.White, 20, 50); }
        var nail = new NailForm(bmp);
        nail.Location = new Point(900, 200);
        nail.Show();
        Pump(900);
        Grab(nail.Bounds, "/tmp/frost-nail.png");
        nail.Close();
        Pump(800);
        Console.WriteLine("nail closed=" + nail.IsDisposed);
        Console.WriteLine("FROST SHOTS 2 DONE");
    }

    private static void Pump(int ms)
    {
        var until = DateTime.Now.AddMilliseconds(ms);
        while (DateTime.Now < until) { Application.DoEvents(); Thread.Sleep(30); }
    }

    private static void Grab(Rectangle r, string path)
    {
        using (var bmp = new Bitmap(Math.Max(10, r.Width), Math.Max(10, r.Height)))
        {
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
            bmp.Save(path);
        }
    }
}
