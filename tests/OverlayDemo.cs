using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using WinTranslator;

// 用用户原图实跑 v1.3.1 的原位覆盖管线：红框检测→裁左联→带坐标识别→逐块翻译→合成→并排对照图
internal static class OverlayDemo
{
    private static int Main()
    {
        try { Run().Wait(); Console.WriteLine("DEMO OK"); return 0; }
        catch (Exception ex) { Console.WriteLine("DEMO FAIL: " + ex); return 1; }
    }

    private static async Task Run()
    {
        using (var full = new Bitmap("/tmp/ref.png"))
        {
            // 找左半幅的红色选框（高 R、低 G/B）
            int minX = int.MaxValue, minY = int.MaxValue, maxX = 0, maxY = 0;
            for (int y = 0; y < full.Height; y++)
                for (int x = 0; x < full.Width / 2; x++)
                {
                    var c = full.GetPixel(x, y);
                    if (c.R > 170 && c.G < 130 && c.B < 130)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            Console.WriteLine("frame: " + minX + "," + minY + " - " + maxX + "," + maxY);
            var inner = new Rectangle(minX + 15, minY + 15, maxX - minX - 30, maxY - minY - 30);
            using (var crop = full.Clone(inner, full.PixelFormat))
            {
                crop.Save("/tmp/demo-crop.png");
                var blocks = OcrService.RecognizeBlocks(crop);
                Console.WriteLine("blocks: " + blocks.Count);
                var engine = new TranslationEngine();
                var translations = new List<string>();
                foreach (var b in blocks)
                {
                    Console.WriteLine("block @" + b.Rect + " : " + b.Text.Replace("\n", " / "));
                    var r = await engine.Translate(b.Text.Replace("\n", " "), "en", "zh", "auto");
                    translations.Add(r.Text);
                    Console.WriteLine("  -> " + r.Text + " (" + r.Provider + ")");
                }
                var composed = OverlayComposer.Compose(crop, blocks, translations);
                composed.Save("/tmp/demo-composed.png");
                using (var side = new Bitmap(crop.Width * 2 + 16, crop.Height))
                using (var g = Graphics.FromImage(side))
                {
                    g.Clear(Color.FromArgb(233, 235, 239));
                    g.DrawImage(crop, 0, 0);
                    g.DrawImage(composed, crop.Width + 16, 0);
                    side.Save("/home/hatch/workspace/goals/windows/files/20261030-14-原图实跑覆盖对照.png");
                }
                composed.Dispose();
            }
        }
    }
}
