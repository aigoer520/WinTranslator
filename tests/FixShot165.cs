using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using WinTranslator;

// v1.6.5 验证：①引擎自绘胶囊 ②单词本头部（无列表/卡片、页签红底纹无数字）③句子详情（译图标+大行距）
// ④词义显示＝可选文本框 ⑤复习入口仍进大卡片（_viewMode==1）⑥胶囊点击切换（自绘鼠标命中）
class FixShot165
{
    static readonly BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Instance;

    static void Shot(Control c, string name)
    {
        Application.DoEvents();
        System.Threading.Thread.Sleep(400);
        var bounds = c is Form f ? f.Bounds : c.RectangleToScreen(c.ClientRectangle);
        using (var bmp = new Bitmap(bounds.Width, bounds.Height))
        {
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
            bmp.Save("/tmp/boards165/" + name + ".png");
        }
        Console.WriteLine("shot " + name);
    }

    static Control FindToggle(Control root, string glyph)
    {
        foreach (Control c in root.Controls)
        {
            var rp = c as RoundPanel;
            if (rp != null && rp.Width == 22 && rp.Height == 22)
                foreach (Control ch in rp.Controls)
                    if (ch is Label && ch.Text == glyph) return rp;
            var r = FindToggle(c, glyph);
            if (r != null) return r;
        }
        return null;
    }

    static bool HasText(Control root, string t)
    {
        foreach (Control c in root.Controls)
        {
            if (c is Label && c.Text == t) return true;
            if (HasText(c, t)) return true;
        }
        return false;
    }

    static int CountTextBoxes(Control root)
    {
        int n = 0;
        foreach (Control c in root.Controls) { if (c is TextBox) n++; n += CountTextBoxes(c); }
        return n;
    }

    static void ClickCtl(Control c)
    {
        c.GetType().GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(c, new object[] { EventArgs.Empty });
    }


    static void DumpTb(Control root, int depth)
    {
        var tb = root as TextBox;
        if (tb != null && tb.Multiline)
            Console.WriteLine("TB text=[" + tb.Text + "] bounds=" + tb.Bounds + " font=" + tb.Font.Name + " " + tb.Font.Size + " fore=" + tb.ForeColor);
        foreach (Control c in root.Controls) DumpTb(c, depth + 1);
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        var main = new MainForm();
        var store = (Store)typeof(MainForm).GetField("_store", BF).GetValue(main);
        var w1 = new Entry
        {
            Src = "zzv165probe", Dst = "adj. 必然的，不可避免的", SrcLang = "en", DstLang = "zh",
            Provider = "有道", Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ExamplesSource = "有道词典",
            Examples = new List<ExampleItem> { new ExampleItem { En = "Reform is inevitable.", Zh = "改革是不可避免的。", Source = "21世纪大英汉词典" } }
        };
        if (!store.Favorites.Exists(f => f.Src == "zzv165probe")) store.AddFavorite(w1);
        var sent = new Entry
        {
            Src = "Longlegs keep them high above the hot desert sands which can be up to 80 degrees, and if they do have to kneel down, they have specialized pads on their contact points with the ground.",
            Dst = "长腿将它们高高置于炎热的沙漠沙地上。", SrcLang = "en", DstLang = "zh",
            Provider = "有道", Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        if (!store.Favorites.Exists(f => f.Src == sent.Src)) store.AddFavorite(sent);

        var w2 = new Entry
        {
            Src = "zzv165en", Dst = "adj. bound to happen, unavoidable", SrcLang = "en", DstLang = "zh",
            Provider = "有道", Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        if (!store.Favorites.Exists(f => f.Src == "zzv165en")) store.AddFavorite(w2);
        main.Show();
        Shot(main, "v165-01-home-capsule");

        // ⑥ 胶囊点击：点第 6 段中心（MyMemory）→ SelectedIndex 变 5、Changed 触发
        var seg = typeof(MainForm).GetField("_segEngine", BF).GetValue(main) as SegmentControl;
        var md = typeof(Control).GetMethod("OnMouseDown", BindingFlags.NonPublic | BindingFlags.Instance);
        int segW = (seg.Width - 6) / 6;
        md.Invoke(seg, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 3 + segW * 5 + segW / 2, 17, 0) });
        Application.DoEvents();
        Console.WriteLine("capsule click -> SelectedIndex=" + seg.SelectedIndex + " (expect 5)");
        Shot(main, "v165-02-capsule-mymemory-selected");
        md.Invoke(seg, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 3 + segW / 2, 17, 0) });

        // ②③ 单词本头部与句子详情
        typeof(MainForm).GetMethod("ShowPage", BF).Invoke(main, new object[] { 1 });
        var pageWord = typeof(MainForm).GetField("_pageWord", BF).GetValue(main);
        Shot(main, "v165-03-wordbook-header");
        Console.WriteLine("has 列表 label=" + HasText((Control)pageWord, "列表") + " has 卡片 label=" + HasText((Control)pageWord, "卡片"));
        pageWord.GetType().GetMethod("SelectTab", BF).Invoke(pageWord, new object[] { false });
        var favS = store.Favorites.Find(f => f.Src == sent.Src);
        pageWord.GetType().GetMethod("ToggleExpand", BF).Invoke(pageWord, new object[] { favS });
        Shot(main, "v165-04-sentence-detail");
        Console.WriteLine("has 去翻译 text=" + HasText((Control)pageWord, "去翻译"));

        // ④ 单词词义显示＝可选文本框
        pageWord.GetType().GetMethod("SelectTab", BF).Invoke(pageWord, new object[] { true });
        var favW = store.Favorites.Find(f => f.Src == "zzv165probe");
        pageWord.GetType().GetMethod("ToggleExpand", BF).Invoke(pageWord, new object[] { favW });
        int before = CountTextBoxes((Control)pageWord);
        var tog = FindToggle((Control)pageWord, "译");
        if (tog != null) ClickCtl(tog);
        Application.DoEvents(); System.Threading.Thread.Sleep(300);
        int after = CountTextBoxes((Control)pageWord);
        Console.WriteLine("meaning textboxes before=" + before + " after=" + after + " (search box counts 1; expect after=before+1)");
        Shot(main, "v165-05-meaning-selectable");
        foreach (Control c0 in ((Control)pageWord).Controls) DumpTb(c0, 0);

        // 对照：英文标点词义的渲染
        var favE = store.Favorites.Find(f => f.Src == "zzv165en");
        pageWord.GetType().GetMethod("ToggleExpand", BF).Invoke(pageWord, new object[] { favE });
        var togE = FindToggle((Control)pageWord, "译");
        if (togE != null) ClickCtl(togE);
        Application.DoEvents(); System.Threading.Thread.Sleep(300);
        Shot(main, "v165-05b-meaning-en-punct");

        // ⑤ 复习入口仍进大卡片
        pageWord.GetType().GetMethod("StartReview", BF).Invoke(pageWord, null);
        Application.DoEvents(); System.Threading.Thread.Sleep(300);
        int vm = (int)pageWord.GetType().GetField("_viewMode", BF).GetValue(pageWord);
        Console.WriteLine("after StartReview _viewMode=" + vm + " (expect 1)");
        Shot(main, "v165-06-review-card");
        // 点当前页签退出回列表
        pageWord.GetType().GetMethod("SelectTab", BF).Invoke(pageWord, new object[] { true });
        vm = (int)pageWord.GetType().GetField("_viewMode", BF).GetValue(pageWord);
        Console.WriteLine("after SelectTab _viewMode=" + vm + " (expect 0)");
        main.Close();
        Console.WriteLine("DONE");
    }
}
