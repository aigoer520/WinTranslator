using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using WinTranslator;

public static class BadgeProbe
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        var store = new Store();
        var engine = new TranslationEngine();
        Bitmap crop;
        using (var full = new Bitmap("/tmp/ref.png"))
        {
            var rect = new Rectangle(36, 41, 418, 585);
            crop = full.Clone(new Rectangle(rect.X + 15, rect.Y + 15, rect.Width - 30, rect.Height - 30), full.PixelFormat);
        }
        var form = new ResultForm(engine, store, "SRC", (Bitmap)crop.Clone());
        form.Location = new Point(40, 30);
        form.Show();
        Application.DoEvents(); Thread.Sleep(600); Application.DoEvents();
        form.SetComboImages(crop, crop);
        Application.DoEvents(); Thread.Sleep(600); Application.DoEvents();

        var t = typeof(ResultForm);
        var badge = (Control)t.GetField("_badge", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
        var strip = (Control)t.GetField("_imgStrip", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
        Console.WriteLine("form client=" + form.ClientSize + " badge.bounds=" + badge.Bounds + " badgeIdx=" + form.Controls.GetChildIndex(badge)
            + " stripIdx=" + form.Controls.GetChildIndex(strip) + " strip.bounds=" + strip.Bounds);
        // 徽章圆心与四角是否落在窗体 Region 内
        var region = form.Region;
        var c = new Point(badge.Bounds.X + 14, badge.Bounds.Y + 14);
        Console.WriteLine("badge center in region=" + region.IsVisible(c) + " badge topright in region=" + region.IsVisible(new Point(badge.Bounds.Right - 2, badge.Bounds.Y + 2)));
        // 抓屏取徽章圆心像素：若被图条盖住，应为图条深色底；徽章本体为白色
        using (var bmp = new Bitmap(30, 30))
        {
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(new Point(form.Bounds.X + c.X - 15, form.Bounds.Y + c.Y - 15), Point.Empty, new Size(30, 30));
            var px = bmp.GetPixel(15, 15);
            Console.WriteLine("badge center screen pixel=" + px);
        }
    }
}
