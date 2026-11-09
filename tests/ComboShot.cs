using System;
using System.Drawing;
using System.Windows.Forms;
using WinTranslator;

// 实拍自查：用用户原图的真实识别/翻译/合成结果打开双呈现组合窗并截屏
internal static class ComboShot
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        var store = new Store();
        var engine = new TranslationEngine();
        var orig = new Bitmap("/tmp/demo-crop.png");
        var tran = new Bitmap("/tmp/demo-composed.png");
        var form = new ResultForm(engine, store, "x", (Bitmap)orig.Clone());
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(30, 20);
        form.Show();
        form.SetPrecomputed(
            "DEFEATED BRAZILIAN PRESIDENTIAL CANDIDATE FLAVIO BOLSONARO AND HIS FAMILY LAND IN MIAMI\nOpa-locka",
            "被击败的巴西总统候选人弗拉维奥·博尔索纳罗及其家人在迈阿密着陆\n奥帕洛卡",
            "en", "zh");
        form.SetComboImages(orig, tran);
        var timer = new Timer { Interval = 900 };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            Application.DoEvents();
            var b = form.Bounds;
            using (var bmp = new Bitmap(b.Width, b.Height))
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(b.Left, b.Top, 0, 0, b.Size);
                bmp.Save("/tmp/combo-shot.png");
            }
            Console.WriteLine("SHOT DONE " + b.Width + "x" + b.Height);
            Application.Exit();
        };
        timer.Start();
        Application.Run(form);
    }
}
