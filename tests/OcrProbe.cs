using System;
using System.Drawing;
using WinTranslator;

/// <summary>OCR 探针：对指定图片跑带坐标识别，打印块清单（定位漏识别发生在哪一步）。</summary>
public static class OcrProbe
{
    private static void Main(string[] args)
    {
        using (var bmp = new Bitmap(args[0]))
        {
            var blocks = OcrService.RecognizeBlocks(bmp);
            Console.WriteLine("blocks: " + blocks.Count);
            foreach (var b in blocks)
                Console.WriteLine("block @" + b.Rect + " lines=" + b.LineCount + " lh=" + b.LineHeight.ToString("F0") + " : " + b.Text.Replace("\n", " / "));
            string whole = OcrService.Recognize(bmp);
            Console.WriteLine("whole: " + whole.Replace("\n", " | "));
        }
    }
}
