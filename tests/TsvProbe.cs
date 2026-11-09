using System;
using System.Reflection;
using WinTranslator;
using System.Drawing;
public static class TsvProbe {
    private static void Main(string[] args) {
        OcrService.Log = m => Console.WriteLine("[diag] " + m);
        var m2 = typeof(OcrService).GetMethod("RecognizeBlocksWithLangs", BindingFlags.NonPublic | BindingFlags.Static);
        using (var bmp = new Bitmap(args[0])) {
            foreach (int psm in new[]{6, 11}) {
                var blocks = (System.Collections.Generic.List<OcrBlock>)m2.Invoke(null, new object[]{ bmp, "chi_sim+eng", psm });
                Console.WriteLine("psm" + psm + " blocks: " + blocks.Count);
                foreach (var b in blocks) Console.WriteLine("  @" + b.Rect + " : " + b.Text.Replace("\n"," / "));
            }
            var all = OcrService.RecognizeBlocks(bmp);
            Console.WriteLine("MAIN blocks: " + all.Count);
            foreach (var b in all) Console.WriteLine("  @" + b.Rect + " : " + b.Text.Replace("\n"," / "));
        }
    }
}
