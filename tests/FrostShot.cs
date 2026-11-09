using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using WinTranslator;

/// <summary>v1.5.0 磨砂皮肤静态稿：主窗口（frosted）＋组合窗（frosted，用真实流程产物图）＋悬浮球，真实窗体抓屏。</summary>
public static class FrostShot
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        var store = new Store();
        store.Settings.Skin = "frosted"; // MainForm 构造会按 Settings.Skin 归一化设 SkinMode 并套调色板
        var engine = new TranslationEngine();

        var main = new MainForm();
        main.ApplySkinLive("frosted"); // MainForm 无参构造自建 Store 读盘（经典），须构造后热切换到磨砂
        main.Show();
        Application.DoEvents();
        Thread.Sleep(900);
        Grab(main.Bounds, "/tmp/frost-main.png");

        // 组合窗用图：演示裁剪图与其合成译图
        Bitmap crop, composed;
        using (var full = new Bitmap("/tmp/ref.png"))
        {
            // 与历次演示同一裁剪：红框约 (36,41,418,585)，内缩 15 得照片区 388x555
            var rect = new Rectangle(36, 41, 418, 585);
            crop = full.Clone(new Rectangle(rect.X + 15, rect.Y + 15, rect.Width - 30, rect.Height - 30), full.PixelFormat);
        }
        var blocks = OcrService.RecognizeBlocks((Bitmap)crop.Clone());
        var trs = new System.Collections.Generic.List<string>();
        foreach (var b in blocks) trs.Add("被击败的巴西总统候选人及其家人抵达迈阿密");
        composed = OverlayComposer.Compose(crop, blocks, trs);

        var form = new ResultForm(engine, store, "DEFEATED BRAZILIAN", (Bitmap)crop.Clone());
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(60, 40);
        form.Show();
        Application.DoEvents();
        form.SetPrecomputed("DEFEATED BRAZILIAN PRESIDENTIAL CANDIDATE FLAVIO BOLSONARO AND HIS FAMILY LAND IN MIAMI",
            "被击败的巴西总统候选人弗拉维奥·博尔索纳罗及其家人抵达迈阿密", "en", "zh");
        form.SetComboImages(crop, composed);
        Application.DoEvents();
        Thread.Sleep(600);
        Grab(form.Bounds, "/tmp/frost-combo.png");
        form.Close();

        var ball = new FloatBall(store);
        ball.Location = new Point(1500, 60);
        ball.Show();
        Application.DoEvents();
        Thread.Sleep(300);
        Grab(new Rectangle(ball.Left - 6, ball.Top - 6, ball.Width + 12, ball.Height + 12), "/tmp/frost-ball.png");
        Console.WriteLine("FROST SHOTS DONE");
    }

    private static void Grab(Rectangle r, string path)
    {
        using (var bmp = new Bitmap(r.Width, r.Height))
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(r.Location, Point.Empty, r.Size);
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
