// v1.7.2 探针：①首页英文译文拖选召唤选取层（单击不召唤、新译文到来自动退回）；
// ②查词小窗释义/音标/例句为可选取 TextBox。鼠标事件用反射调 OnMouseDown/OnMouseUp 模拟（同 FixShot165 口径）。
// 无消息泵环境：断言只针对同步逻辑，收尾用 Environment.Exit（ShotReplaceProbe 教训）。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WinTranslator;

static class SelectCopyProbe
{
    static readonly BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Instance;
    static int _fail = 0;

    static void Check(bool ok, string name)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
        if (!ok) _fail++;
    }

    static void FireMouse(Control c, string method, int x, int y)
    {
        var m = typeof(Control).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
        m.Invoke(c, new object[] { new MouseEventArgs(MouseButtons.Left, 1, x, y, 0) });
    }

    static void DumpTextBoxes(Control root, List<TextBox> acc)
    {
        var tb = root as TextBox;
        if (tb != null) acc.Add(tb);
        foreach (Control c in root.Controls) DumpTextBoxes(c, acc);
    }

    [STAThread]
    static void Main()
    {
        Directory.CreateDirectory("/tmp/boards172");
        Application.EnableVisualStyles();
        GlassMotion.Enabled = false;
        var main = new MainForm();
        main.Show();
        Application.DoEvents();

        // ① 首页：设英文译文 → 点词模式
        var txtOutput = (TextBox)typeof(MainForm).GetField("_txtOutput", BF).GetValue(main);
        var outWt = typeof(MainForm).GetField("_outWt", BF).GetValue(main) as Control;
        var outSelect = typeof(MainForm).GetField("_outSelect", BF).GetValue(main) as TextBox;
        var outScroll = typeof(MainForm).GetField("_outScroll", BF).GetValue(main) as Panel;
        typeof(MainForm).GetField("_homeDstLang", BF).SetValue(main, "en");
        txtOutput.Text = "Long legs keep them high above the hot desert sands.";
        typeof(MainForm).GetMethod("ApplyHomeWordMode", BF).Invoke(main, null);
        Application.DoEvents();
        Check(outScroll.Visible && !txtOutput.Visible, "en output uses word layer");
        Check(outSelect != null && !outSelect.Visible, "select layer hidden initially");

        // 单击（位移 2px）→ 不召唤
        FireMouse(outWt, "OnMouseDown", 30, 10);
        FireMouse(outWt, "OnMouseUp", 32, 11);
        Check(!outSelect.Visible, "single click keeps word mode");

        // 拖动（位移 120px）→ 召唤，文本一致
        FireMouse(outWt, "OnMouseDown", 30, 10);
        FireMouse(outWt, "OnMouseUp", 150, 14);
        Check(outSelect.Visible, "drag summons select layer");
        Check(outSelect.Text == txtOutput.Text, "select layer text matches output");
        Check(outSelect.Bounds == outWt.Bounds, "select layer bounds match word layer");

        // Esc → 退回
        typeof(Control).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(outSelect, new object[] { new KeyEventArgs(Keys.Escape) });
        Check(!outSelect.Visible, "esc hides select layer");

        // 再拖出 → 新译文到来（ApplyHomeWordMode）→ 自动退回
        FireMouse(outWt, "OnMouseDown", 30, 10);
        FireMouse(outWt, "OnMouseUp", 150, 14);
        Check(outSelect.Visible, "drag summons again");
        txtOutput.Text = "A brand new translation result.";
        typeof(MainForm).GetMethod("ApplyHomeWordMode", BF).Invoke(main, null);
        Check(!outSelect.Visible, "new output resets to word mode");
        main.Close();

        // ② 查词小窗：反射调 Fill 造内容，断言文本框集合
        var store = new Store();
        var pop = new DictPopupForm(store, s => { }, () => { }, null);
        var r = new DictResult
        {
            Word = "serendipity",
            UsPhone = "/ˌserənˈdɪpəti/",
            UkPhone = "/ˌserənˈdɪpəti/",
            Definitions = new List<string> { "n. 意外发现美好事物的运气；机缘巧合" },
            Examples = new List<ExampleItem> { new ExampleItem { En = "Finding that little café was pure serendipity.", Zh = "找到那家小咖啡馆纯属意外之喜。" } }
        };
        typeof(DictPopupForm).GetMethod("Fill", BF).Invoke(pop, new object[] { r });
        var boxes = new List<TextBox>();
        DumpTextBoxes(pop, boxes);
        bool hasPhone = false, hasDef = false, hasEn = false, hasZh = false, allReadonly = true;
        foreach (var b in boxes)
        {
            if (!b.ReadOnly) allReadonly = false;
            if (b.Text.Contains("ˌserən")) hasPhone = true;
            if (b.Text.Contains("意外发现美好事物的运气")) hasDef = true;
            if (b.Text.Contains("pure serendipity")) hasEn = true;
            if (b.Text.Contains("意外之喜")) hasZh = true;
        }
        Console.WriteLine("popup textboxes=" + boxes.Count);
        Check(boxes.Count >= 4, "popup has >=4 selectable boxes");
        Check(hasPhone && hasDef && hasEn && hasZh, "popup phone/def/example-en/example-zh all selectable");
        Check(allReadonly, "popup boxes readonly");
        pop.Close();

        Console.WriteLine(_fail == 0 ? "PROBE ALL PASS" : ("PROBE FAILURES=" + _fail));
        Console.Out.Flush();
        Environment.Exit(_fail == 0 ? 0 : 1);
    }
}
