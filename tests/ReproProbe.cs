using System;
using System.Drawing;
using WinTranslator;

static class ReproProbe
{
    static int Main(string[] args)
    {
        string path = args.Length > 0 ? args[0] : "/tmp/tweet.png";
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        OcrService.Log = m => Console.WriteLine("[diag] " + m);
        using (var bmp = new Bitmap(path))
        {
            var blocks = OcrService.RecognizeBlocks(bmp);
            Console.WriteLine("blocks=" + blocks.Count);
            foreach (var b in blocks)
                Console.WriteLine("[" + b.Rect.X + "," + b.Rect.Y + " " + b.Rect.Width + "x" + b.Rect.Height + "] " + b.Text.Replace("\n", " / "));
            string raw = string.Join("\n", blocks.ConvertAll(b => b.Text).ToArray());
            Console.WriteLine("DetectSource=" + TranslationEngine.DetectSource(raw) + " target=" + TranslationEngine.DetectTarget(raw));
            var regions = OverlayComposer.MergeParagraphs(blocks);
            Console.WriteLine("regions=" + regions.Count);
            foreach (var r in regions)
                Console.WriteLine("R[" + r.Rect.X + "," + r.Rect.Y + " " + r.Rect.Width + "x" + r.Rect.Height + "] " + r.Text);
        }
        Console.Out.Flush();
        return 0;
    }
}
