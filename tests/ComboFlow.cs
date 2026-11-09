using System;
using System.Drawing;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using WinTranslator;

// 端到端自检：走 MainForm.StartOverlayShot 真实流程（识别→逐块翻译→合成→组合窗），逐条核对用户要求
internal static class ComboFlow
{
    private const BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Instance;

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        var main = new MainForm();
        main.StartPosition = FormStartPosition.Manual;
        main.Location = new Point(10, 10);
        main.Show();
        Application.DoEvents();

        var crop = new Bitmap("/tmp/demo-crop.png");
        var m = typeof(MainForm).GetMethod("StartOverlayShot", BF);
        m.Invoke(main, new object[] { crop, new Rectangle(60, 60, crop.Width, crop.Height) });

        // 轮询等组合窗出现（识别+翻译是异步的）
        ResultForm combo = null;
        for (int i = 0; i < 60 && combo == null; i++)
        {
            System.Threading.Thread.Sleep(500);
            Application.DoEvents();
            foreach (Form f in Application.OpenForms)
            {
                var rf = f as ResultForm;
                if (rf == null || !rf.Visible) continue;
                var strip = typeof(ResultForm).GetField("_imgStrip", BF).GetValue(rf) as Panel;
                if (strip != null && strip.Visible) combo = rf;
            }
        }
        Console.WriteLine(combo != null ? "FLOW: combo form shown" : "FLOW: NO combo form (fallback?)");
        if (combo != null)
        {
            var picT = typeof(ResultForm).GetField("_picTran", BF).GetValue(combo) as PictureBox;
            var picO = typeof(ResultForm).GetField("_picOrig", BF).GetValue(combo) as PictureBox;
            Console.WriteLine("picOrig size=" + picO.Bounds.Size + " picTran size=" + picT.Bounds.Size);
            Console.WriteLine("same display height: " + (picO.Height == picT.Height));
            var tranImg = picT.Image as Bitmap;
            Console.WriteLine("composed size=" + tranImg.Width + "x" + tranImg.Height + " crop=" + crop.Width + "x" + crop.Height);
            // 盖字检验：原字幕字母笔画处像素应为纯贴片色（240,240,240），不是与原图混合的半透明色
            var px = tranImg.GetPixel(45, 118);
            Console.WriteLine("patch pixel @(45,118): " + px.R + "," + px.G + "," + px.B + " (opaque 240 expected near)");
            // 块外像素：右下人物区应与原图逐像素一致
            var po = crop.GetPixel(330, 500); var pt = tranImg.GetPixel(330, 500);
            Console.WriteLine("outside block identical: " + (po.ToArgb() == pt.ToArgb()));
            combo.Location = new Point(420, 10);
            Application.DoEvents();
            System.Threading.Thread.Sleep(600);
            Application.DoEvents();
            var b = combo.Bounds;
            using (var bmp = new Bitmap(b.Width, b.Height))
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(b.Left, b.Top, 0, 0, b.Size);
                bmp.Save("/tmp/combo-flow.png");
            }
            Console.WriteLine("SHOT " + b.Width + "x" + b.Height);
        }
        Console.WriteLine("FLOW DONE");
        Application.Exit();
    }
}
