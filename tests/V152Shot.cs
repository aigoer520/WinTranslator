using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;
using WinTranslator;

// v1.5.2 实拍自查：单显组合窗 / 原位窗 / 真实照片合成（C1 字色＋C3 渐变羽化＋C2 组段）
internal static class V152Shot
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        SkinMode.Current = "glass";
        Palette.Apply("glass");

        // 1) 真实照片：OCR→组段→合成（伪译文只为看排版/配色）
        var src = new Bitmap("/tmp/ref.png");
        var crop = src.Clone(new Rectangle(36 + 15, 41 + 15, 418 - 30, 585 - 30), src.PixelFormat);
        var blocks = OcrService.RecognizeBlocks(crop);
        var regions = OverlayComposer.MergeParagraphs(blocks);
        Console.WriteLine("blocks=" + blocks.Count + " regions=" + regions.Count);
        var trs = new List<string>();
        foreach (var r in regions) trs.Add("译文示例段落文字");
        var composed = OverlayComposer.Compose(crop, regions, trs);
        composed.Save("/tmp/v152-demo-composed.png");
        Console.WriteLine("demo composed saved");

        // 2) 单显组合窗：1200 宽图（1700 屏并排需 2448 → 单显）
        var store = new Store();
        var engine = new TranslationEngine();
        var wide = new Bitmap(1200, 420);
        using (var g = Graphics.FromImage(wide))
        {
            using (var br = new LinearGradientBrush(new Rectangle(0, 0, 1200, 420), Color.FromArgb(30, 40, 70), Color.FromArgb(200, 210, 225), LinearGradientMode.Vertical))
                g.FillRectangle(br, 0, 0, 1200, 420);
            using (var f = new Font("Microsoft YaHei UI", 28f, FontStyle.Bold))
                g.DrawString("WIDE SCREENSHOT TEXT", f, Brushes.White, 60, 60);
        }
        var wideTr = OverlayComposer.Compose(wide,
            new List<OcrBlock> { new OcrBlock { Rect = new Rectangle(50, 50, 700, 60), Text = "WIDE SCREENSHOT TEXT", LineCount = 1, LineHeight = 54 } },
            new List<string> { "宽图截图文字" });
        var form = new ResultForm(engine, store, "WIDE SCREENSHOT TEXT", (Bitmap)wide.Clone());
        form.Show();
        Application.DoEvents();
        form.SetPrecomputed("WIDE SCREENSHOT TEXT", "宽图截图文字", "en", "zh");
        form.SetComboImages(wide, wideTr);
        form.Location = new Point(60, 60);
        Application.DoEvents();
        Thread.Sleep(500);
        Grab(new Rectangle(form.Left, form.Top, form.Width, form.Height), "/tmp/v152-combo-single.png");
        Console.WriteLine("combo single shot saved " + form.Width + "x" + form.Height);
        form.Close();

        // 3) 原位窗：放在一张"桌面"底板上
        var desk = new Form { FormBorderStyle = FormBorderStyle.None, Bounds = new Rectangle(0, 0, 1200, 800), BackColor = Color.FromArgb(70, 90, 120) };
        desk.Show();
        Application.DoEvents();
        var sel = new Rectangle(300, 200, 418, 300);
        var ip = new InPlaceForm((Bitmap)composed.Clone(), sel);
        ip.Show();
        Application.DoEvents();
        Thread.Sleep(500);
        Grab(new Rectangle(280, 180, 460, 340), "/tmp/v152-inplace.png");
        Console.WriteLine("inplace shot saved");
        ip.Close();
        desk.Close();
    }

    private static void Grab(Rectangle r, string path)
    {
        using (var bmp = new Bitmap(r.Width, r.Height))
        {
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(r.Location, Point.Empty, r.Size);
            bmp.Save(path);
        }
    }
}
