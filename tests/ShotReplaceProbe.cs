// v1.7.1 探针：新截图开始前，上一个原位译图窗必须先消失（根因：原先替换点在新结果生成之后，
// 框选期间旧窗一直浮着且会被框进新截图）。做法：反射调 ShowInPlaceFirst 造一个旧原位窗，
// 再反射调 StartScreenshot(-1)——它开头同步关窗，随后才走框选（框选层在无头环境 600ms 后 Esc 取消）。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using WinTranslator;

static class ShotReplaceProbe
{
    static readonly BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Instance;

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        GlassMotion.Enabled = false;
        var main = new MainForm();
        main.Show();

        // 造旧原位窗：ShowInPlaceFirst(cropped, composed, blocks, fullSrc, fullDst, srcLang, dstLang, prefer, selRect)
        var img = new Bitmap(300, 120);
        using (var g = Graphics.FromImage(img)) { g.Clear(Color.White); using (var f = new Font("Arial", 14)) g.DrawString("Hello world", f, Brushes.Black, 10, 10); }
        var showInPlace = typeof(MainForm).GetMethod("ShowInPlaceFirst", BF);
        showInPlace.Invoke(main, new object[] { img, (Bitmap)img.Clone(), new List<OcrBlock>(), "Hello world", "你好世界", "en", "zh", "auto", new Rectangle(120, 120, 300, 120) });
        Application.DoEvents();
        Thread.Sleep(300);
        var inPlace = typeof(MainForm).GetField("_inPlace", BF).GetValue(main) as Form;
        Console.WriteLine("old inPlace visible=" + (inPlace != null && inPlace.Visible) + " disposed=" + (inPlace == null || inPlace.IsDisposed));

        // 触发新截图。Mono 下 Application.Run 泵不存在时 async void 的后续段（Task.Delay 之后）
        // 不会被推进——历次探针都卡在这里（stdout 连开头行都没落盘，缓冲未刷）。
        // 所以断言只针对 v1.7.1 修复的本质：StartScreenshot 同步开头段执行完，旧窗必须已关、字段已清；
        // 后半程（抓屏→框选→识别→新结果替换旧结果）由既有六套测试与历版实拍覆盖。
        var store = (Store)typeof(MainForm).GetField("_store", BF).GetValue(main);
        store.Settings.ScreenshotDelaySec = 0; // 避免开头段后还要等延时（反正泵推不动后续段）
        typeof(MainForm).GetMethod("StartScreenshot").Invoke(main, new object[] { 0 });
        Console.Out.Flush();
        bool closedAtStart = inPlace == null || inPlace.IsDisposed;
        var fieldAfter = typeof(MainForm).GetField("_inPlace", BF).GetValue(main);
        bool popupGone = true;
        var popField = typeof(MainForm).GetField("_popup", BF).GetValue(main) as Form;
        if (popField != null && !popField.IsDisposed) popupGone = false;
        Console.WriteLine("old inPlace disposedRightAfterStartCall=" + closedAtStart);
        Console.WriteLine("field _inPlace cleared=" + (fieldAfter == null));
        Console.WriteLine("field _popup nullOrDisposed=" + popupGone);
        Console.WriteLine(closedAtStart && fieldAfter == null ? "PROBE PASS" : "PROBE FAIL");
        Console.Out.Flush();
        Environment.Exit(0);
    }
}
