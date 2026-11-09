using System;
using System.Drawing;
using System.Windows.Forms;
using WinTranslator;

static class SegProbe
{
    static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        int fail = 0;
        using (var seg = new SegmentControl(new[] { "自动", "有道", "必应", "AI", "谷歌", "MyMemory" }, 0))
        {
            seg.Size = new Size(456, 34);
            var boxes = seg.ComputeBoxes();
            Console.WriteLine("boxes=" + boxes.Length);
            for (int i = 0; i < boxes.Length; i++)
                Console.WriteLine("  [" + i + "] x=" + boxes[i].X + " w=" + boxes[i].Width);
            if (boxes.Length != 6) { Console.WriteLine("FAIL count"); fail++; }
            if (boxes[0].X != 3) { Console.WriteLine("FAIL first x"); fail++; }
            if (boxes[5].Right != 456 - 3) { Console.WriteLine("FAIL last right=" + boxes[5].Right); fail++; }
            int gap0 = boxes[1].X - boxes[0].Right;
            for (int i = 1; i < 5; i++)
            {
                int gap = boxes[i + 1].X - boxes[i].Right;
                Console.WriteLine("  gap" + i + "=" + gap);
                if (Math.Abs(gap - gap0) > 1) { Console.WriteLine("FAIL gap not equal"); fail++; }
            }
            // 字宽定框：AI 的框应明显窄于 MyMemory 的框（旧等宽格两者同宽）
            if (!(boxes[3].Width < boxes[5].Width - 20)) { Console.WriteLine("FAIL box width not text-based"); fail++; }
            // 换选中项（MyMemory 变粗体）后仍等距、仍铺满
            seg.SelectedIndex = 5;
            var b2 = seg.ComputeBoxes();
            int g0 = b2[1].X - b2[0].Right;
            for (int i = 1; i < 5; i++)
                if (Math.Abs((b2[i + 1].X - b2[i].Right) - g0) > 1) { Console.WriteLine("FAIL gap after reselect"); fail++; }
            if (b2[5].Right != 453) { Console.WriteLine("FAIL last right after reselect"); fail++; }
        }
        Console.WriteLine(fail == 0 ? "SEG ALL PASS" : "SEG FAILURES=" + fail);
        Console.Out.Flush();
        return fail == 0 ? 0 : 1;
    }
}
