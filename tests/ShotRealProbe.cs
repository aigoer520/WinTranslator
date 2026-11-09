using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using WinTranslator;

// 真实管线探针：复刻 MainForm.StartOverlayShot 五步（识别→定语种→组段→逐段真翻译→原位合成），
// 与 FidelityProbe 的区别是翻译走真实引擎（默认设置/自动引擎），不是桩译文。
static class ShotRealProbe
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        return Run(args).GetAwaiter().GetResult();
    }

    static async Task<int> Run(string[] args)
    {
        if (Environment.GetEnvironmentVariable("WT_DIAG") == "1")
            OcrService.Log = s => Console.Error.WriteLine("[ocr] " + s);
        string path = args[0];
        string outPath = args[1];
        var settings = AppSettings.Defaults();
        var engine = new TranslationEngine();
        engine.ExtraSettings = settings;
        engine.GlmApiKey = settings.GlmApiKey;
        string prefer = EngineCatalog.ResolvePrefer(settings);
        Console.WriteLine("prefer=" + prefer);
        int fail = 0;
        using (var bmp = new Bitmap(path))
        {
            List<Rectangle> prot;
            var blocks = OcrService.RecognizeBlocks((Bitmap)bmp.Clone(), out prot);
            Console.WriteLine("blocks=" + blocks.Count + " protected=" + prot.Count);
            foreach (var p in prot) Console.WriteLine("  prot [" + p.X + "," + p.Y + " " + p.Width + "x" + p.Height + "]");
            foreach (var b in blocks) Console.WriteLine("B[" + b.Rect.X + "," + b.Rect.Y + " " + b.Rect.Width + "x" + b.Rect.Height + "] " + b.Text.Replace("\n", " / "));
            if (blocks.Count == 0) { Console.WriteLine("NO BLOCKS"); return 2; }
            string rawSrc = string.Join("\n", blocks.ConvertAll(b => b.Text).ToArray());
            string srcLang = TranslationEngine.DetectSource(rawSrc);
            string dstLang = TranslationEngine.DetectTarget(rawSrc);
            Console.WriteLine("langs: " + srcLang + " -> " + dstLang);
            var regions = OverlayComposer.MergeParagraphs(blocks);
            Console.WriteLine("regions=" + regions.Count);
            var trs = new List<string>();
            foreach (var r in regions)
            {
                string flat = (r.Text ?? "").Replace("\n", " ");
                string t = "";
                string prov = "";
                try
                {
                    var res = await engine.Translate(flat, srcLang, dstLang, prefer);
                    t = res.Text; prov = res.Provider;
                }
                catch (Exception ex) { Console.WriteLine("  translate FAIL: " + ex.Message); fail++; }
                Console.WriteLine("R[" + r.Rect.X + "," + r.Rect.Y + " " + r.Rect.Width + "x" + r.Rect.Height + "] " + flat + "  ==>  [" + prov + "] " + t);
                trs.Add(t);
            }
            var composed = OverlayComposer.Compose(bmp, regions, trs, prot);
            int badPx = 0; long totPx = 0;
            foreach (var p in prot)
            {
                int bad = 0;
                for (int y = p.Top; y < p.Bottom && y < bmp.Height; y++)
                    for (int x = p.Left; x < p.Right && x < bmp.Width; x++)
                    {
                        totPx++;
                        if (bmp.GetPixel(x, y) != composed.GetPixel(x, y)) { badPx++; bad++; }
                    }
                if (bad > 0) Console.WriteLine("  prot CHANGED [" + p.X + "," + p.Y + " " + p.Width + "x" + p.Height + "] bad=" + bad);
            }
            Console.WriteLine("protected pixels: total=" + totPx + " changed=" + badPx);
            if (badPx > 0) { Console.WriteLine("FAIL protected pixels changed"); fail++; }
            composed.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine("saved " + outPath);
        }
        Console.Out.Flush();
        return fail == 0 ? 0 : 1;
    }
}
