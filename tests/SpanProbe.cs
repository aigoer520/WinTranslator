using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using WinTranslator;

static class SpanProbe
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        using (var bmp = new Bitmap(args[0]))
        {
            List<Rectangle> prot;
            var blocks = OcrService.RecognizeBlocks((Bitmap)bmp.Clone(), out prot);
            var t = typeof(OverlayComposer);
            var mFree = t.GetMethod("FreeSpans", BindingFlags.NonPublic | BindingFlags.Static);
            var mSplit = t.GetMethod("SplitBySpans", BindingFlags.NonPublic | BindingFlags.Static);
            var mFlat = t.GetMethod("FlattenLines", BindingFlags.NonPublic | BindingFlags.Static);
            var regions = OverlayComposer.MergeParagraphs(blocks);
            foreach (var r in regions)
            {
                var slots = (System.Collections.IList)mFlat.Invoke(null, new object[] { r });
                foreach (var slot in slots)
                {
                    var st = slot.GetType();
                    var srect = (Rectangle)st.GetField("Rect").GetValue(slot);
                    var inflated = srect; inflated.Inflate(3, 2);
                    var spans = (List<Rectangle>)mFree.Invoke(null, new object[] { inflated, srect, prot });
                    Console.WriteLine("slot " + srect + " text=" + st.GetField("Text").GetValue(slot));
                    foreach (var sp in spans) Console.WriteLine("   span " + sp);
                    var pieces = (List<string>)mSplit.Invoke(null, new object[] { "马里奥·纳福尔 22 小时", spans });
                    foreach (var p in pieces) Console.WriteLine("   piece <" + p + ">");
                    Color bg, fg; bool hasFg; double share;
                    OverlayComposer.SampleRegionColors(bmp, inflated, out bg, out fg, out hasFg, out share);
                    Console.WriteLine("   colors bg=" + bg + " fg=" + fg + " hasFg=" + hasFg + " share=" + share.ToString("F2"));
                }
            }
        }
        Console.Out.Flush();
        return 0;
    }
}
