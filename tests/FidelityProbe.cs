using System;
using System.Collections.Generic;
using System.Drawing;
using WinTranslator;

static class FidelityProbe
{
    static string Stub(string src)
    {
        if (src.Contains("Massimo")) return "马西莫 7 小时\n一个小女孩，一只豪猪和一只果子狸。";
        if (src.Contains("really hurts")) return "马里奥·纳福尔 22 小时\n看着真让人难受，但你就是停不下来。然后有人试图\n帮忙... WTF？！";
        if (src.Contains("Mario")) return "马里奥·纳福尔";
        if (src.Contains("little girl")) return "一个小女孩，一只豪猪和一只果子狸。";
        if (src.Contains("Sri Lankan")) return "这个斯里兰卡乡村女孩和她不太可能的宠物之间的纽带，是你今天看到的最健康的东西。";
        if (src.Contains("Writer")) return "作者：杰米";
        if (src.Contains("小时")) return src; // 时间行原样
        return "译文示例";
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        string path = args[0];
        string outPath = args[1];
        int fail = 0;
        using (var bmp = new Bitmap(path))
        {
            List<Rectangle> prot;
            var blocks = OcrService.RecognizeBlocks((Bitmap)bmp.Clone(), out prot);
            Console.WriteLine("blocks=" + blocks.Count + " protected=" + prot.Count);
            foreach (var p in prot) Console.WriteLine("  prot [" + p.X + "," + p.Y + " " + p.Width + "x" + p.Height + "]");
            foreach (var b in blocks) Console.WriteLine("B[" + b.Rect.X + "," + b.Rect.Y + " " + b.Rect.Width + "x" + b.Rect.Height + "] " + b.Text.Replace("\n", " / "));
            var regions = OverlayComposer.MergeParagraphs(blocks);
            Console.WriteLine("regions=" + regions.Count);
            var trs = new List<string>();
            foreach (var r in regions)
            {
                Console.WriteLine("R[" + r.Rect.X + "," + r.Rect.Y + " " + r.Rect.Width + "x" + r.Rect.Height + "] parts=" + (r.Parts == null ? -1 : r.Parts.Count) + " :: " + r.Text.Replace("\n", " / "));
                trs.Add(Stub(r.Text));
            }
            var composed = OverlayComposer.Compose(bmp, regions, trs, prot);
            // 像素护栏：保护框内逐像素必须与原图一致
            int badPx = 0; long totPx = 0;
            foreach (var p in prot)
            {
                for (int y = p.Top; y < p.Bottom && y < bmp.Height; y++)
                    for (int x = p.Left; x < p.Right && x < bmp.Width; x++)
                    {
                        totPx++;
                        if (bmp.GetPixel(x, y) != composed.GetPixel(x, y)) badPx++;
                    }
            }
            foreach (var p in prot)
            {
                int bad = 0;
                for (int y = p.Top; y < p.Bottom && y < bmp.Height; y++)
                    for (int x = p.Left; x < p.Right && x < bmp.Width; x++)
                        if (bmp.GetPixel(x, y) != composed.GetPixel(x, y)) bad++;
                if (bad > 0) Console.WriteLine("  prot CHANGED [" + p.X + "," + p.Y + " " + p.Width + "x" + p.Height + "] bad=" + bad);
            }
            Console.WriteLine("protected pixels: total=" + totPx + " changed=" + badPx);
            if (badPx > 0) { Console.WriteLine("FAIL protected pixels changed"); fail++; }
            composed.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine("saved " + outPath);
        }
        Console.WriteLine(fail == 0 ? "FIDELITY PASS" : "FIDELITY FAIL=" + fail);
        Console.Out.Flush();
        return fail == 0 ? 0 : 1;
    }
}
