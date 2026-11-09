using System;
using System.Drawing;
using WinTranslator;
using System.Windows.Forms;
public static class ArtProbe {
    [STAThread]
    private static void Main() {
        Application.EnableVisualStyles();
        var main = new MainForm();
        main.ApplySkinLive("frosted");
        Console.WriteLine("Current=" + SkinMode.Current);
        var home = (Panel)typeof(MainForm).GetField("_pageHome", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(main);
        Console.WriteLine("home px=" + ((Bitmap)home.BackgroundImage).GetPixel(300, 200));
        // 再单独测：新尺寸请求是否换图
        var t1 = FrostArt.For(1180, 780);
        Console.WriteLine("frost(1180,780) px=" + t1.GetPixel(300, 200));
        var t2 = BackdropArt.For(1180, 780);
        Console.WriteLine("backdrop(1180,780) px=" + t2.GetPixel(300, 200) + " sameObj=" + object.ReferenceEquals(t1, t2));
    }
}
