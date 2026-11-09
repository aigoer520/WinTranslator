using System;
using System.Drawing;
using WinTranslator;

static class ColorProbe
{
    static int Main(string[] args)
    {
        using (var bmp = new Bitmap(args[0]))
        {
            for (int i = 1; i + 3 < args.Length; i += 4)
            {
                var r = new Rectangle(int.Parse(args[i]), int.Parse(args[i + 1]), int.Parse(args[i + 2]), int.Parse(args[i + 3]));
                Color bg, fg; bool hasFg; double share;
                OverlayComposer.SampleRegionColors(bmp, r, out bg, out fg, out hasFg, out share);
                Console.WriteLine("rect " + r + " bg=" + bg + " fg=" + fg + " hasFg=" + hasFg + " share=" + share.ToString("F2"));
            }
        }
        Console.Out.Flush();
        return 0;
    }
}
