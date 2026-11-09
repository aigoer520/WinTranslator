using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WinTranslator;

internal static class TestMain
{
    private static int _fail = 0;

    private static void Check(bool ok, string name)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
        if (!ok) _fail++;
    }

    private static int Main(string[] args)
    {
        GlassMotion.Enabled = false; // 动效纯视觉层，断言不跟计时器赛跑
        string mode = args.Length > 0 ? args[0] : "engine";
        try
        {
            if (mode == "engine") RunEngine().Wait();
            else if (mode == "glm-live") RunGlmLive().Wait();
            else if (mode == "layout") RunLayout();
            else if (mode == "overlay") RunOverlay();
            else if (mode == "store") RunStore();
            else if (mode == "dict") RunDict().Wait();
            else if (mode == "ocr") RunOcr();
        }
        catch (Exception ex)
        {
            Console.WriteLine("EXCEPTION: " + ex);
            _fail++;
        }
        Console.WriteLine(_fail == 0 ? "ALL PASS" : ("FAILURES=" + _fail));
        return _fail == 0 ? 0 : 1;
    }

    // ---------- 引擎实测（真实联网） ----------
    private static async Task RunEngine()
    {
        var yd = new YoudaoClient();
        string t1 = await yd.Translate("Hello world, this is a test.", "en", "zh-CHS");
        Console.WriteLine("youdao en->zh: " + t1);
        Check(t1.Contains("你好") || t1.Contains("测试"), "youdao basic");

        var bing = new BingClient();
        string t2 = await bing.Translate("Hello world, this is a test.", "en", "zh-Hans");
        Console.WriteLine("bing en->zh: " + t2);
        Check(t2.Length > 0, "bing basic");

        var engine = new TranslationEngine();
        var r1 = await engine.Translate("我想学习编程。");
        Console.WriteLine("engine zh->en (" + r1.Provider + "): " + r1.Text);
        Check(r1.DstLang == "en" && r1.Text.Length > 0, "engine auto zh->en");

        var r2 = await engine.Translate("Bonjour le monde", "fr", "zh");
        Console.WriteLine("engine fr->zh (" + r2.Provider + "): " + r2.Text);
        Check(r2.Text.Length > 0, "engine fr->zh");

        var r3 = await engine.Translate("こんにちは世界", "ja", "zh", "bing");
        Console.WriteLine("engine ja->zh bing: " + r3.Text);
        Check(r3.Text.Length > 0, "engine ja->zh bing");

        // GLM 无 Key：客户端应明确报错；引擎层应回退有道/必应
        var glm = new GlmClient();
        bool glmThrew = false;
        try { await glm.Translate("Hello", "en", "zh"); }
        catch (TranslationException) { glmThrew = true; }
        Check(glmThrew, "glm no-key throws");
        engine.GlmApiKey = "";
        var r4 = await engine.Translate("Hello world", "en", "zh", "glm");
        Console.WriteLine("engine glm-fallback (" + r4.Provider + "): " + r4.Text);
        Check(r4.Text.Length > 0 && r4.Provider != "智谱 GLM", "engine glm fallback to web engines");

        // v1.5.1 事项六：谷歌 gtx / MyMemory 解析（离线样本）＋引擎分发
        Check(GtxClient.ParseResponse("[[[\"你好，\",\"Hello \",null,null,3],[\"世界\",\"world\",null,null,3]],null,\"en\"]") == "你好，世界", "gtx parse joins segments");
        bool gtxThrew = false;
        try { GtxClient.ParseResponse("<html><head><title>Sorry...</title></html>"); } catch (TranslationException) { gtxThrew = true; }
        Check(gtxThrew, "gtx parse rejects block page");
        Check(MyMemoryClient.ParseResponse("{\"responseData\":{\"translatedText\":\"你好世界\",\"match\":1},\"quotaFinished\":false,\"responseStatus\":200}") == "你好世界", "mymemory parse ok");
        bool mmThrew = false;
        try { MyMemoryClient.ParseResponse("{\"responseData\":{\"translatedText\":\"\"},\"quotaFinished\":true,\"responseStatus\":403}"); } catch (TranslationException) { mmThrew = true; }
        Check(mmThrew, "mymemory quota throws");
        Check(TranslationEngine.ToGoogleCode("zh") == "zh-CN" && TranslationEngine.ToGoogleCode("cht") == "zh-TW", "code map google");
        var r5 = await engine.Translate("The quick brown fox jumps over the lazy dog", "en", "zh", "mymemory");
        Console.WriteLine("engine mymemory (" + r5.Provider + "): " + r5.Text);
        Check(r5.Provider == "MyMemory" && r5.Text.Contains("狐狸"), "engine mymemory live");
        var r6 = await engine.Translate("Hello world", "en", "zh", "gtx");
        Console.WriteLine("engine gtx-prefer (" + r6.Provider + "): " + r6.Text);
        Check(r6.Text.Length > 0, "engine gtx prefer returns (VM 出口被谷歌拦时回退有道/必应亦可)");

        Check(TranslationEngine.DetectSource("你好") == "zh", "detect zh");
        Check(TranslationEngine.DetectSource("ありがとう") == "ja", "detect ja");
        Check(TranslationEngine.ToYoudaoCode("cht") == "zh-CHT", "code map youdao cht");
        Check(TranslationEngine.ToBingCode("zh") == "zh-Hans", "code map bing zh");
    }

    private static void showPageHome(MainForm main)
    {
        typeof(MainForm).GetMethod("ShowPage", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(main, new object[] { 0 });
    }

    // ---------- GLM 真实 Key 联调（Key 从环境变量读，不落盘） ----------
    private static async Task RunGlmLive()
    {
        string key = Environment.GetEnvironmentVariable("GLM_KEY");
        Check(!string.IsNullOrEmpty(key), "glm key provided via env");
        if (string.IsNullOrEmpty(key)) return;
        var direct = new GlmClient { ApiKey = key };
        try
        {
            string d = await direct.Translate("Hello", "en", "zh");
            Console.WriteLine("glm direct: " + d);
        }
        catch (Exception ex) { Console.WriteLine("glm direct error: " + ex.Message); }
        var engine = new TranslationEngine();
        engine.GlmApiKey = key;
        var r1 = await engine.Translate("Hello world, this is a test.", "en", "zh", "glm");
        Console.WriteLine("glm en->zh (" + r1.Provider + "): " + r1.Text);
        Check(r1.Provider == "智谱 GLM" && r1.Text.Length > 0, "glm live en->zh via GLM itself");
        var r2 = await engine.Translate("我想学习编程，贵在坚持。", "zh", "en", "glm");
        Console.WriteLine("glm zh->en (" + r2.Provider + "): " + r2.Text);
        Check(r2.Provider == "智谱 GLM" && r2.Text.Length > 0, "glm live zh->en via GLM itself");
    }

    // ---------- 界面布局截图 ----------
    private static void Shot(Control c, string name)
    {
        Application.DoEvents();
        Thread.Sleep(350);
        var bounds = c is Form f ? f.Bounds : c.RectangleToScreen(c.ClientRectangle);
        using (var bmp = new Bitmap(bounds.Width, bounds.Height))
        {
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
            string path = "/tmp/shots/" + name + ".png";
            bmp.Save(path);
            Console.WriteLine("shot: " + path + " " + bounds.Width + "x" + bounds.Height);
        }
    }

    private static void RunLayout()
    {
        Directory.CreateDirectory("/tmp/shots");
        Application.EnableVisualStyles();
        var store = new Store();
        var engine = new TranslationEngine();

        // 预置示例数据（速览/单词本/历史截图用）
        store.AddFavorite(new Entry { Src = "serendipity", Dst = "意外发现美好事物的运气", SrcLang = "en", DstLang = "zh", Provider = "有道", Time = 1761710000 });
        var pers = new Entry
        {
            Src = "persistence", Dst = "坚持不懈；恒心", SrcLang = "en", DstLang = "zh", Provider = "有道", Time = 1761710050,
            ExamplesSource = "AI 日常例句",
            Examples = new List<ExampleItem>
            {
                new ExampleItem { En = "Don't give up — a little persistence and you'll pass the exam.", Zh = "别放弃，坚持一下你就能通过考试。", Source = "AI 日常例句" },
                new ExampleItem { En = "Learning English takes persistence, but you can do it step by step.", Zh = "学英语需要坚持，但你可以一步一步来。", Source = "AI 日常例句" },
                new ExampleItem { En = "His persistence paid off — he finally got the job he wanted.", Zh = "他的坚持有了回报——他终于得到了想要的工作。", Source = "AI 日常例句" },
                new ExampleItem { En = "With a bit of persistence, I fixed the car myself last weekend.", Zh = "上周末凭着一点坚持，我自己把车修好了。", Source = "AI 日常例句" }
            }
        };
        store.AddFavorite(pers);
        store.AddFavorite(new Entry { Src = "Bonjour le monde", Dst = "你好，世界", SrcLang = "fr", DstLang = "zh", Provider = "有道", Time = 1761710100 });
        store.AddHistory(new Entry { Src = "Hello, nice to meet you.", Dst = "你好，很高兴见到你。", SrcLang = "en", DstLang = "zh", Provider = "有道", Time = 1761710200 });

        var main = new MainForm();
        main.Show();
        Shot(main, "01-main-home");
        // 输入删空 → 译文同步清空（真机反馈回归）
        var txtIn = (TextBox)typeof(MainForm).GetField("_txtInput", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(main);
        var txtOut = (TextBox)typeof(MainForm).GetField("_txtOutput", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(main);
        txtIn.Text = "hate";
        txtOut.Text = "讨厌";
        txtIn.Text = "";
        Check(txtOut.TextLength == 0, "clear input clears output");
        txtIn.Text = "love";
        txtOut.Text = "爱";
        txtIn.Text = "love you";
        Check(txtOut.Text == "爱", "partial edit keeps output");

        // 四页切换（新版导航为 RoundPanel，用 Ui.RaiseClick 触发）
        var t = typeof(MainForm);
        var navWord = (Control)t.GetField("_navWord", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(main);
        Ui.RaiseClick(navWord);
        Shot(main, "02-main-wordbook");
        // v1.2.0：展开 persistence 详情（例句已缓存）＋点词独立弹窗
        var pageWord = (WordbookPanel)t.GetField("_pageWord", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(main);
        var mainStore = (Store)t.GetField("_store", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(main);
        var persFav = mainStore.Favorites.Find(f => f.Src == "persistence");
        Check(persFav != null, "wordbook persistence seeded");
        pageWord.ToggleExpand(persFav);
        Shot(main, "14-wordbook-detail");
        var revealed = (HashSet<int>)typeof(WordbookPanel).GetField("_revealed", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(pageWord);
        revealed.Add(0);
        pageWord.RefreshAll();
        Shot(main, "16-wordbook-revealed");
        t.GetMethod("ShowDictPopup", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(main, new object[] { "persistence", new Point(420, 260) });
        var popField = t.GetField("_dictPop", BindingFlags.NonPublic | BindingFlags.Instance);
        var pop = (DictPopupForm)popField.GetValue(main);
        for (int i = 0; i < 40 && !pop.LoadedOk; i++) { Application.DoEvents(); Thread.Sleep(250); }
        Check(pop.LoadedOk, "dict popup loaded");
        // v1.8.0：来源切换条在场且默认落有道；点「剑桥」胶囊应换源（剑桥在 VM 可能被拦，只验已换源＋窗内有内容）
        var popSrc = (string)typeof(DictPopupForm).GetProperty("ActiveSourceForTest", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(pop, null);
        Check(popSrc == "youdao", "dict popup default source youdao");
        var card1 = (Control)typeof(DictPopupForm).GetField("_card", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(pop);
        Label camChip = null;
        foreach (Control cc in card1.Controls)
            foreach (Control inner in cc.Controls)
                if (inner is Label && inner.Text == "剑桥") camChip = (Label)inner;
        Check(camChip != null, "dict popup source strip has cambridge chip");
        Shot(pop, "15-dict-popup");
        pop.Close();
        // 收藏此词必须真能用：对未收藏的词开弹窗 → 点收藏片 → 收藏落盘且重载可见
        // 测试可重复：上一轮已把 inevitable 收藏落盘，本轮先摘掉，保证从"未收藏"起测
        var preFav = mainStore.Favorites.Find(f => f.Src == "inevitable");
        if (preFav != null) mainStore.RemoveFavorite(preFav);
        t.GetMethod("ShowDictPopup", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(main, new object[] { "inevitable", new Point(420, 260) });
        var pop2 = (DictPopupForm)popField.GetValue(main);
        for (int i = 0; i < 40 && !pop2.LoadedOk; i++) { Application.DoEvents(); Thread.Sleep(250); }
        Check(pop2.LoadedOk, "dict popup2 loaded");
        var card2 = (Control)typeof(DictPopupForm).GetField("_card", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(pop2);
        Label favLbl = null;
        foreach (Control cc in card2.Controls)
            foreach (Control inner in cc.Controls)
                if (inner is Label && inner.Text.Contains("收藏此词")) favLbl = (Label)inner;
        Check(favLbl != null, "favorite chip present for new word");
        if (favLbl != null) Ui.RaiseClick(favLbl);
        Application.DoEvents();
        Thread.Sleep(150);
        Check(mainStore.Favorites.Exists(f => f.Src == "inevitable"), "favorite this word adds to store");
        // v1.3.1：查词弹窗朗读钮存在
        Label spkLbl = null;
        foreach (Control cc in card2.Controls)
            foreach (Control inner in cc.Controls)
                if (inner is Label && inner.Text.Contains("朗读")) spkLbl = (Label)inner;
        Check(spkLbl != null, "dict popup speak chip present");
        Check(new Store().Favorites.Exists(f => f.Src == "inevitable"), "favorite persisted to disk");
        pop2.Close();
        // ---- v1.3.0 单词本改版：日期分组、复习流程、句子页签 ----
        var wbT = typeof(WordbookPanel);
        var bf = BindingFlags.NonPublic | BindingFlags.Instance;
        var listHost = (Panel)wbT.GetField("_listHost", bf).GetValue(pageWord);
        bool hasDate = false, hasFooter = false;
        string dateStr = DateTimeOffset.FromUnixTimeSeconds(1761710000).LocalDateTime.ToString("yyyy-MM-dd");
        foreach (Control c in listHost.Controls)
            if (c is Label)
            {
                if (c.Text == dateStr) hasDate = true;
                if (c.Text.Contains("该分组下共")) hasFooter = true;
            }
        Check(hasDate && hasFooter, "wordbook date groups with footers");
        var reviewBtn = (Control)wbT.GetField("_reviewBtn", bf).GetValue(pageWord);
        Ui.RaiseClick(reviewBtn);
        Application.DoEvents();
        Thread.Sleep(200);
        Check((int)wbT.GetField("_viewMode", bf).GetValue(pageWord) == 1, "review enters card view");
        var cardWord = (Label)wbT.GetField("_cardWord", bf).GetValue(pageWord);
        Check(cardWord.Text.Length > 0 && cardWord.Text != "暂无单词", "review card shows word");
        Shot(main, "19-wordbook-card-hidden");
        var revealBtn = (Control)wbT.GetField("_cardRevealBtn", bf).GetValue(pageWord);
        Ui.RaiseClick(revealBtn);
        var knowBtn = (Control)wbT.GetField("_cardKnowBtn", bf).GetValue(pageWord);
        Check(knowBtn.Visible, "card reveal shows know button");
        Shot(main, "20-wordbook-card-revealed");
        string topWord = cardWord.Text;
        Ui.RaiseClick(knowBtn);
        Application.DoEvents();
        Thread.Sleep(150);
        var topEntry = mainStore.Favorites.Find(f => f.Src == topWord || f.Dst == topWord);
        Check(topEntry != null && topEntry.ReviewCount >= 1 && topEntry.ReviewLevel >= 1, "review know marks entry");
        Check(new Store().Favorites.Exists(f => (f.Src == topWord || f.Dst == topWord) && f.ReviewCount >= 1), "review mark persisted");
        var tabSent = (Control)wbT.GetField("_tabSentBtn", bf).GetValue(pageWord);
        Ui.RaiseClick(tabSent);
        Application.DoEvents();
        Check(!reviewBtn.Visible, "sentence tab hides review button");
        Check(((Panel)wbT.GetField("_listHost", bf).GetValue(pageWord)).Visible, "sentence tab uses list");
        Shot(main, "21-wordbook-sentences");
        var tabWord = (Control)wbT.GetField("_tabWordBtn", bf).GetValue(pageWord);
        Ui.RaiseClick(tabWord);
        Application.DoEvents();
        var navHistory = (Control)t.GetField("_navHist", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(main);
        Ui.RaiseClick(navHistory);
        Shot(main, "03-main-history");
        // 设置独立弹窗（v1.1.1 事项五）
        var openSettings = t.GetMethod("OpenSettings", BindingFlags.NonPublic | BindingFlags.Instance);
        openSettings.Invoke(main, new object[0]);
        var settingsFormField = t.GetField("_settingsForm", BindingFlags.NonPublic | BindingFlags.Instance);
        var settingsForm = (Form)settingsFormField.GetValue(main);
        Shot(settingsForm, "04-settings-general");
        var sft = typeof(SettingsForm);
        sft.GetMethod("ShowTab", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(settingsForm, new object[] { 1 });
        Shot(settingsForm, "05-settings-ocr");
        // v1.8.0 翻译接口页：展开百度行拍一张
        sft.GetMethod("ShowTab", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(settingsForm, new object[] { 2 });
        var apiRows = (System.Collections.IList)sft.GetField("_apiRows", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(settingsForm);
        Check(apiRows != null && apiRows.Count == 10, "settings api rows = 10");
        if (apiRows != null && apiRows.Count > 0)
        {
            var header0 = (Control)apiRows[0].GetType().GetField("Header").GetValue(apiRows[0]);
            Ui.RaiseClick(header0);
            Application.DoEvents();
        }
        Shot(settingsForm, "26-settings-api");
        settingsForm.Close();

        // v1.8.0 引擎胶囊：填假密钥后重建，百度/DeepL 应进目录（6 基础＋3 免费＋2 密钥＝11）
        mainStore.Settings.BaiduAppId = "testid";
        mainStore.Settings.BaiduKey = "testkey";
        mainStore.Settings.DeepLKey = "testkey:fx";
        t.GetMethod("RebuildEngineSeg", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(main, new object[0]);
        var engOpts = (System.Collections.IList)t.GetField("_engineOptions", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(main);
        Check(engOpts != null && engOpts.Count == 11, "engine catalog with 2 key engines = 11, got " + (engOpts == null ? -1 : engOpts.Count));
        showPageHome(main);
        Shot(main, "27-home-many-engines");
        mainStore.Settings.BaiduAppId = "";
        mainStore.Settings.BaiduKey = "";
        mainStore.Settings.DeepLKey = "";
        t.GetMethod("RebuildEngineSeg", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(main, new object[0]);

        // 主界面放大（事项四 A 案铺满）
        main.Location = new Point(10, 10);
        main.Size = new Size(1500, 950);
        showPageHome(main);
        Shot(main, "11-main-large");
        main.Size = new Size(1080, 700);

        var popup = new ResultForm(engine, store, "Hello world", null);
        popup.Location = new Point(60, 60);
        popup.Show();
        Shot(popup, "06-result-compare");
        var chk = (CheckBox)typeof(ResultForm).GetField("_chkCompare", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(popup);
        chk.Checked = false;
        Shot(popup, "07-result-stacked");
        popup.Close();

        // 截图模式新弹窗（v1.1.1 事项三）：短/长内容自适应
        var rf = BindingFlags.NonPublic | BindingFlags.Instance;
        var rt = typeof(ResultForm);
        using (var shotImg = new Bitmap(460, 300))
        {
            using (var g = Graphics.FromImage(shotImg)) { g.Clear(Color.White); }
            var sp = new ResultForm(engine, store, "", (Bitmap)shotImg.Clone());
            sp.Location = new Point(40, 40);
            sp.Show();
            rt.GetField("_srcText", rf).SetValue(sp, "Hello.");
            rt.GetField("_dstText", rf).SetValue(sp, "你好。");
            ((TextBox)rt.GetField("_sTxtSrc", rf).GetValue(sp)).Text = "Hello.";
            ((TextBox)rt.GetField("_sTxtDst", rf).GetValue(sp)).Text = "你好。";
            sp.FitToContent();
            Shot(sp, "12-result-shot-short");
            string longSrc = "Long legs keep them high above the hot desert sands which can be up to 80°C. If they do have to kneel down, they have specialized pads on their contact points with the ground. A camel's hump does not store water as many people believe; it stores fat, which the body can break down for energy when food is scarce. Their thick fur coat reflects sunlight during the day and keeps them warm on cold desert nights.";
            string longDst = "长腿使它们能高高地悬在高达80℃的炎热沙漠沙地上方。如果必须跪下，它们的脚部接触地面的部位还配有专门的垫子。与许多人以为的不同，骆驼的驼峰储存的不是水，而是脂肪——食物匮乏时，身体可以分解脂肪来获取能量。它们厚厚的皮毛白天能反射阳光，寒冷的沙漠夜晚又能保暖。";
            rt.GetField("_srcText", rf).SetValue(sp, longSrc);
            rt.GetField("_dstText", rf).SetValue(sp, longDst);
            ((TextBox)rt.GetField("_sTxtSrc", rf).GetValue(sp)).Text = longSrc;
            ((TextBox)rt.GetField("_sTxtDst", rf).GetValue(sp)).Text = longDst;
            sp.FitToContent();
            Shot(sp, "13-result-shot-long");
            Console.WriteLine("shot popup long size: " + sp.Width + "x" + sp.Height);
            Check(sp.Width >= 480 && sp.Height >= 380, "shot popup long content grows");
            var spSrc = rt.GetField("_srcCard", rf).GetValue(sp) as Control;
            var spDst = rt.GetField("_dstCard", rf).GetValue(sp) as Control;
            var spSrcBox = rt.GetField("_sTxtSrc", rf).GetValue(sp) as TextBox;
            int spNeed = TextRenderer.MeasureText(longSrc, spSrcBox.Font, new Size(Math.Max(120, spSrc.Width - 56), int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height + 48;
            Check(spSrc != null && spSrc.Visible && spSrc.Height + 6 >= spNeed, "v151 long shot src card holds full text (card " + (spSrc != null ? spSrc.Height : -1) + " >= need " + spNeed + ")");
            Check(spDst != null && !spDst.Visible, "v151 long shot dst card hidden");
            // v1.2.1 事项一：英文原文侧切点词控件、中文译文侧保持文本框
            rt.GetField("_lastSrcLang", rf).SetValue(sp, "en");
            rt.GetField("_lastDstLang", rf).SetValue(sp, "zh");
            rt.GetMethod("ApplyWordMode", rf).Invoke(sp, new object[0]);
            var srcScroll = (Panel)rt.GetField("_sSrcScroll", rf).GetValue(sp);
            var dstScroll = (Panel)rt.GetField("_sDstScroll", rf).GetValue(sp);
            var srcBoxCtl = (TextBox)rt.GetField("_sTxtSrc", rf).GetValue(sp);
            Check(srcScroll.Visible && !srcBoxCtl.Visible, "en src side uses wordtext");
            Check(!dstScroll.Visible, "zh dst side stays textbox");
            sp.FitToContent();
            Shot(sp, "17-shot-wordclick");
            // v1.2.1 事项四：弹窗内右键按下即关闭（在底栏上模拟右键按下）
            var barCtl = (Control)rt.GetField("_bar", rf).GetValue(sp);
            typeof(Control).GetMethod("OnMouseDown", rf).Invoke(barCtl, new object[] { new MouseEventArgs(MouseButtons.Right, 1, 5, 5, 0) });
            Application.DoEvents();
            Check(sp.IsDisposed || !sp.Visible, "right click closes shot popup");
            if (!sp.IsDisposed) sp.Close();
        }
        // v1.2.1 事项三：主页文档翻译栏已移除（字段应不存在）
        Check(typeof(MainForm).GetField("_docCard", rf) == null, "doc card removed from home");

        // 弹窗定位（v1.1.2）：选区正下方居中；变宽后保持居中；下方放不下翻到上方
        using (var shotImg2 = new Bitmap(460, 300))
        {
            using (var g = Graphics.FromImage(shotImg2)) { g.Clear(Color.White); }
            var pp = new ResultForm(engine, store, "", (Bitmap)shotImg2.Clone());
            pp.PlaceForSelection(new Rectangle(600, 200, 460, 300));
            pp.Show();
            Console.WriteLine("place below: " + pp.Location + " size " + pp.Width + "x" + pp.Height);
            Check(pp.Location.X == 578 && pp.Location.Y == 512, "popup centered below selection");
            rt.GetField("_srcText", rf).SetValue(pp, "Hello world, this is a fairly long sentence for width growth testing purposes.");
            rt.GetField("_dstText", rf).SetValue(pp, "你好世界，这是一个用于测试宽度增长的相当长的句子，需要足够长才能触发加宽逻辑继续增长。");
            ((TextBox)rt.GetField("_sTxtSrc", rf).GetValue(pp)).Text = "Hello world, this is a fairly long sentence for width growth testing purposes.";
            ((TextBox)rt.GetField("_sTxtDst", rf).GetValue(pp)).Text = "你好世界，这是一个用于测试宽度增长的相当长的句子，需要足够长才能触发加宽逻辑继续增长。";
            pp.FitToContent();
            int expectX = 600 + (460 - pp.Width) / 2;
            Console.WriteLine("place after grow: " + pp.Location + " expectX " + expectX);
            Check(pp.Location.X == expectX && pp.Location.Y == 512, "popup stays centered after growth");
            pp.Close();

            var pq = new ResultForm(engine, store, "", (Bitmap)shotImg2.Clone());
            pq.PlaceForSelection(new Rectangle(600, 800, 460, 300));
            pq.Show();
            Console.WriteLine("place above: " + pq.Location);
            Check(pq.Location.X == 578 && pq.Location.Y == 800 - pq.Height - 12, "popup flips above when no room below");
            pq.Close();
        }

        var mini = new MiniForm(engine, store, () => { });
        mini.Location = new Point(700, 420);
        mini.Show();
        Shot(mini, "08-mini");
        mini.Close();

        var ball = new FloatBall(store);
        ball.Location = new Point(760, 60);
        ball.Show();
        Shot(ball, "09-ball");
        // v1.5.1 事项四断言：抓屏量测"译"字墨迹质心与红圆圆心偏差 ≤3px（离屏位图墨迹居中）
        using (var bbmp = new Bitmap(ball.Width, ball.Height))
        {
            ball.DrawToBitmap(bbmp, new Rectangle(0, 0, ball.Width, ball.Height));
            int rMinX = 999, rMinY = 999, rMaxX = -1, rMaxY = -1;
            for (int y = 0; y < bbmp.Height; y++) for (int x = 0; x < bbmp.Width; x++)
            {
                var px = bbmp.GetPixel(x, y);
                if (px.R > 150 && px.G < 110 && px.B < 110) { if (x < rMinX) rMinX = x; if (x > rMaxX) rMaxX = x; if (y < rMinY) rMinY = y; if (y > rMaxY) rMaxY = y; }
            }
            Check(rMaxX > 0, "ball red circle found");
            if (rMaxX > 0)
            {
                double ccx = (rMinX + rMaxX) / 2.0, ccy = (rMinY + rMaxY) / 2.0, rr = (rMaxX - rMinX) / 2.0;
                double sx = 0, sy = 0; int n = 0;
                for (int y = 0; y < bbmp.Height; y++) for (int x = 0; x < bbmp.Width; x++)
                {
                    double dx = x - ccx, dy = y - ccy;
                    if (dx * dx + dy * dy > rr * rr * 0.56) continue;
                    var px = bbmp.GetPixel(x, y);
                    if (px.R > 235 && px.G > 235 && px.B > 235) { sx += x; sy += y; n++; }
                }
                Check(n > 30, "ball glyph ink found (" + n + "px)");
                if (n > 30)
                {
                    double ix = sx / n, iy = sy / n;
                    Check(Math.Abs(ix - ccx) <= 3 && Math.Abs(iy - ccy) <= 3,
                        "v151 ball glyph centered (ink " + ix.ToString("F1") + "," + iy.ToString("F1") + " vs circle " + ccx.ToString("F1") + "," + ccy.ToString("F1") + ")");
                }
            }
        }
        ball.Close();

        using (var img = new Bitmap(300, 180))
        {
            using (var g = Graphics.FromImage(img)) { g.Clear(Color.LightSteelBlue); }
            var nail = new NailForm(img);
            nail.Location = new Point(420, 420);
            nail.Show();
            Shot(nail, "10-nail");
            nail.Close();
        }
        // ---- v1.3.0 紧凑小窗点词 ----
        var cp = new ResultForm(engine, store, "Hello world", null);
        cp.Location = new Point(80, 80);
        cp.Show();
        rt.GetField("_srcText", rf).SetValue(cp, "Hello world");
        rt.GetField("_dstText", rf).SetValue(cp, "你好世界");
        rt.GetField("_lastSrcLang", rf).SetValue(cp, "en");
        rt.GetField("_lastDstLang", rf).SetValue(cp, "zh");
        rt.GetMethod("ApplyWordMode", rf).Invoke(cp, new object[0]);
        var cSrcScroll = (Panel)rt.GetField("_cSrcScroll", rf).GetValue(cp);
        var cSrcBox = (TextBox)rt.GetField("_txtSrc", rf).GetValue(cp);
        Check(cSrcScroll.Visible && !cSrcBox.Visible, "compact en src uses wordtext");
        Shot(cp, "22-compact-wordclick");
        cp.Close();
        // ---- v1.3.0 主页译文点词 ----
        showPageHome(main);
        var outScroll = (Panel)t.GetField("_outScroll", rf).GetValue(main);
        txtOut.Text = "Hello world";
        t.GetField("_homeDstLang", rf).SetValue(main, "en");
        t.GetMethod("ApplyHomeWordMode", rf).Invoke(main, new object[0]);
        Check(outScroll.Visible && !txtOut.Visible, "home en output uses wordtext");
        Shot(main, "23-home-wordclick");
        t.GetField("_homeDstLang", rf).SetValue(main, "zh");
        t.GetMethod("ApplyHomeWordMode", rf).Invoke(main, new object[0]);
        Check(!outScroll.Visible && txtOut.Visible, "home zh output back to textbox");
        // ---- v1.3.0 皮肤热切换 ----
        t.GetMethod("ApplySkinLive", rf).Invoke(main, new object[] { "backdrop" });
        Check(Palette.Bg == Color.FromArgb(243, 235, 224), "skin backdrop palette");
        Shot(main, "24-skin-backdrop-home");
        t.GetMethod("ApplySkinLive", rf).Invoke(main, new object[] { "acrylic" });
        Application.DoEvents();
        Check(SkinMode.Current == "acrylic", "skin acrylic switch no crash");
        t.GetMethod("ApplySkinLive", rf).Invoke(main, new object[] { "classic" });
        Check(Palette.Bg == Color.FromArgb(247, 248, 250), "skin back to classic");
        // ---- v1.3.0 原位覆盖合成与译图窗 ----
        using (var oimg = new Bitmap(460, 300))
        {
            using (var g = Graphics.FromImage(oimg)) { g.Clear(Color.White); using (var f = new Font("Microsoft YaHei UI", 16f)) g.DrawString("Hello overlay", f, Brushes.Black, 30, 40); }
            var blocks = new List<OcrBlock> { new OcrBlock { Rect = new Rectangle(24, 32, 320, 56), Text = "Hello overlay" } };
            var composed = OverlayComposer.Compose(oimg, blocks, new List<string> { "你好，覆盖翻译测试" });
            Check(composed.Width == 460 && composed.Height == 300, "overlay compose size");
            var px = composed.GetPixel(30, 38);
            Check(px.R >= 240 && px.G >= 240 && px.B >= 240, "overlay patch fills dominant bg color");
            // v1.3.1 四不变：尺寸一致 + 块外像素不变 + 纯色背景块填充后回到背景色
            Check(composed.GetPixel(400, 250).ToArgb() == oimg.GetPixel(400, 250).ToArgb(), "outside block pixels unchanged");
            var inner = composed.GetPixel(330, 85);
            Check(Math.Abs(inner.R - 255) <= 24 && Math.Abs(inner.G - 255) <= 24 && Math.Abs(inner.B - 255) <= 24, "patch fill returns to bg color");
            // v1.3.4 双呈现：组合窗（ResultForm 灌双图）——双图条出现、窗高增加、译图就位
            var comboForm = new ResultForm(engine, mainStore, "DEFEATED BRAZILIAN", (Bitmap)oimg.Clone());
            comboForm.Show();
            Application.DoEvents();
            comboForm.SetPrecomputed("DEFEATED BRAZILIAN", "被击败的巴西", "en", "zh");
            int hBefore = comboForm.ClientSize.Height;
            comboForm.SetComboImages(oimg, composed);
            Application.DoEvents();
            var rfT = typeof(ResultForm);
            var strip = rfT.GetField("_imgStrip", bf).GetValue(comboForm) as Panel;
            var picTran = rfT.GetField("_picTran", bf).GetValue(comboForm) as PictureBox;
            Check(strip != null && strip.Visible, "combo strip visible");
            Check(picTran != null && picTran.Image != null, "combo translated image set");
            Check(comboForm.ClientSize.Height > hBefore, "combo form grows (" + hBefore + "->" + comboForm.ClientSize.Height + ")");
            // v1.5.0 回归断言：关闭徽章整圆避开窗体圆角裁剪带（徽章外接圆不与圆角弧相交）
            var badge = rfT.GetField("_badge", bf).GetValue(comboForm) as Control;
            if (badge != null)
            {
                int W = comboForm.Width, R = 16, bw = badge.Width;
                double cx = badge.Left + bw / 2.0, cy = badge.Top + badge.Height / 2.0;
                // 判定口径：徽章圆周的右上 45° 点、右点、上点都必须落在窗体圆角区域内（即整圆不被圆角切线裁）
                using (var rg = new Region(RoundPanel.PathFor(new Rectangle(0, 0, W, comboForm.Height), R)))
                using (var gg = comboForm.CreateGraphics())
                {
                    bool arcIn = rg.IsVisible((float)(cx + bw / 2.0 * 0.707), (float)(cy - bw / 2.0 * 0.707), gg)
                        && rg.IsVisible((float)(cx + bw / 2.0), (float)cy, gg) && rg.IsVisible((float)cx, (float)(cy - bw / 2.0), gg);
                    Check(arcIn, "badge circle clears rounded corner cut");
                }
            }
            // v1.5.1 断言组：②下部只留原文卡 ③1:1 放大 ①徽章 z 序＋像素完整 ⑤底栏全 IconBtn
            var dstCard151 = rfT.GetField("_dstCard", bf).GetValue(comboForm) as Control;
            var srcCard151 = rfT.GetField("_srcCard", bf).GetValue(comboForm) as Control;
            Check(dstCard151 != null && !dstCard151.Visible, "v151 shot mode hides dst card");
            Check(srcCard151 != null && srcCard151.Visible, "v151 shot mode shows src card only");
            Check(picTran.Height == 300, "v151 strip image at 1:1 height (" + picTran.Height + ")");
            if (badge != null && strip != null)
            {
                int bi = comboForm.Controls.GetChildIndex(badge);
                int si = comboForm.Controls.GetChildIndex(strip);
                Check(bi < si, "v151 badge z-order above strip (" + bi + "<" + si + ")");
                Application.DoEvents();
                System.Threading.Thread.Sleep(300);
                using (var bmp151 = new Bitmap(badge.Width, badge.Height))
                {
                    using (var gg151 = Graphics.FromImage(bmp151))
                        gg151.CopyFromScreen(new Point(comboForm.Left + badge.Left, comboForm.Top + badge.Top), Point.Empty, badge.Size);
                    var edge151 = bmp151.GetPixel(badge.Width - 4, badge.Height / 2);
                    Check(edge151.R > 235 && edge151.G > 235 && edge151.B > 235, "v151 badge right edge pixel white");
                    var ctr151 = bmp151.GetPixel(badge.Width / 2, badge.Height / 2);
                    Check(ctr151.R < 170 && ctr151.G < 170 && ctr151.B < 170, "v151 badge center pixel is cross");
                }
            }
            var icons151 = rfT.GetField("_icons", bf).GetValue(comboForm) as System.Collections.IList;
            bool allIconBtn = icons151 != null && icons151.Count == 6;
            if (icons151 != null) foreach (var ic in icons151) if (!(ic is IconBtn)) allIconBtn = false;
            Check(allIconBtn, "v151 combo bar buttons are IconBtn x6");
            comboForm.Close();
            var oform = new OverlayResultForm((Bitmap)oimg.Clone(), composed, "测试 · 英语 → 中文");
            oform.PlaceForSelection(new Rectangle(200, 100, 460, 300));
            oform.Show();
            Shot(oform, "25-overlay-result");
            Check(oform.Location.Y == 412, "overlay form below selection");
            oform.Close();
        }
        // ---- v1.5.2 A/C1/C2/C3/C4 ----
        {
            // C1 字色聚类：白底深红字 → 背景/字色采样与回填字色
            using (var cimg = new Bitmap(200, 80))
            {
                using (var g = Graphics.FromImage(cimg))
                {
                    g.Clear(Color.White);
                    using (var f = new Font("Microsoft YaHei UI", 20f, FontStyle.Bold))
                        g.DrawString("RED TEXT", f, new SolidBrush(Color.FromArgb(178, 34, 34)), 10, 20);
                }
                Color cbg, cfg; bool chas; double cshare;
                OverlayComposer.SampleRegionColors(cimg, new Rectangle(0, 0, 200, 80), out cbg, out cfg, out chas, out cshare);
                Check(cbg.R > 235 && cbg.G > 235, "v152 C1 bg sampled white");
                Check(chas && cfg.R > 120 && cfg.G < 110 && cfg.B < 110, "v152 C1 fg sampled red");
                var cblocks = new List<OcrBlock> { new OcrBlock { Rect = new Rectangle(4, 12, 190, 52), Text = "RED TEXT", LineCount = 1, LineHeight = 40 } };
                var ccomp = OverlayComposer.Compose(cimg, cblocks, new List<string> { "红色文字" });
                int redPx = 0;
                for (int y = 16; y < 60; y += 2) for (int x = 8; x < 190; x += 2)
                { var p = ccomp.GetPixel(x, y); if (p.R > 110 && p.R > p.G + 50) redPx++; }
                Check(redPx > 10, "v152 C1 composed text uses sampled red (" + redPx + ")");
                ccomp.Dispose();
            }
            // C3 渐变：上黑下白、块跨中部 → 贴片上深下浅
            using (var gimg = new Bitmap(200, 120))
            {
                using (var g = Graphics.FromImage(gimg))
                {
                    using (var br = new SolidBrush(Color.FromArgb(30, 30, 40))) g.FillRectangle(br, 0, 0, 200, 60);
                    using (var br = new SolidBrush(Color.FromArgb(235, 235, 240))) g.FillRectangle(br, 0, 60, 200, 60);
                    using (var f = new Font("Microsoft YaHei UI", 14f)) g.DrawString("mid", f, Brushes.Gray, 20, 48);
                }
                var gblocks = new List<OcrBlock> { new OcrBlock { Rect = new Rectangle(10, 40, 180, 44), Text = "mid", LineCount = 1, LineHeight = 30 } };
                var gcomp = OverlayComposer.Compose(gimg, gblocks, new List<string> { "中间文字" });
                var topPx = gcomp.GetPixel(190, 47);
                var botPx = gcomp.GetPixel(190, 77);
                Check(topPx.R < botPx.R - 60, "v152 C3 gradient top darker (" + topPx.R + " vs " + botPx.R + ")");
                gcomp.Dispose();
            }
            // C2 组段：同栏两行合并＋右栏独立＋下段独立；拉丁空格连接、中文无空格连接
            var mblocks = new List<OcrBlock>
            {
                new OcrBlock { Rect = new Rectangle(10, 10, 200, 24), Text = "Hello brave", LineCount = 1, LineHeight = 24 },
                new OcrBlock { Rect = new Rectangle(10, 40, 200, 24), Text = "new world", LineCount = 1, LineHeight = 24 },
                new OcrBlock { Rect = new Rectangle(400, 12, 150, 24), Text = "side note", LineCount = 1, LineHeight = 24 },
                new OcrBlock { Rect = new Rectangle(10, 200, 200, 24), Text = "第二段开始", LineCount = 1, LineHeight = 24 },
                new OcrBlock { Rect = new Rectangle(10, 230, 200, 24), Text = "继续内容", LineCount = 1, LineHeight = 24 },
            };
            var regs = OverlayComposer.MergeParagraphs(mblocks);
            Check(regs.Count == 3, "v152 C2 merges into 3 regions (" + regs.Count + ")");
            Check(regs.Count >= 1 && regs[0].Text == "Hello brave new world", "v152 C2 latin join with space");
            Check(regs.Count >= 3 && regs[2].Text == "第二段开始继续内容", "v152 C2 cjk join without space");
            // A 单显：900 宽图（1700 屏并排需 1848）→ 单显译图 1:1，切换显原图
            using (var big = new Bitmap(900, 300))
            {
                using (var g = Graphics.FromImage(big)) g.Clear(Color.White);
                var cf = new ResultForm(engine, mainStore, "big", (Bitmap)big.Clone());
                cf.Show();
                Application.DoEvents();
                cf.SetPrecomputed("big src", "大图译文", "en", "zh");
                cf.SetComboImages(big, big);
                Application.DoEvents();
                var rfT2 = typeof(ResultForm);
                var pT = rfT2.GetField("_picTran", bf).GetValue(cf) as PictureBox;
                var pO = rfT2.GetField("_picOrig", bf).GetValue(cf) as PictureBox;
                Check(pT.Visible && !pO.Visible, "v152 A single view shows tran first");
                Check(pT.Width == 900, "v152 A single view 1:1 width (" + pT.Width + ")");
                rfT2.GetMethod("ToggleSingleView", bf).Invoke(cf, null);
                Application.DoEvents();
                Check(pO.Visible && !pT.Visible, "v152 A toggle switches to orig");
                cf.Close();
            }
            // C4 原位窗：边界等于选区、点击触发转组合窗
            {
                bool fired = false;
                var ipf = new InPlaceForm(new Bitmap(300, 200), new Rectangle(120, 130, 300, 200));
                ipf.SwitchToCombo = () => { fired = true; };
                ipf.Show();
                Application.DoEvents();
                Check(ipf.Bounds == new Rectangle(120, 130, 300, 200), "v152 C4 bounds equal selection");
                typeof(InPlaceForm).GetMethod("OnMouseClick", bf).Invoke(ipf, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 10, 10, 0) });
                Check(fired, "v152 C4 click fires switch-to-combo");
            }
        }
        main.Close();
        Check(true, "layout shots done");
    }

    // ---------- 定格框选 ----------
    private static void RunOverlay()
    {
        Application.EnableVisualStyles();
        Bitmap frozen;
        using (var src = new Bitmap(800, 600))
        {
            using (var g = Graphics.FromImage(src))
            {
                g.Clear(Color.White);
                using (var f = new Font("Microsoft YaHei UI", 20f))
                    g.DrawString("定格测试 Frozen Frame", f, Brushes.Black, 90, 90);
            }
            frozen = (Bitmap)src.Clone();
        }
        var overlay = new OverlayForm(frozen);
        overlay.Show();
        Application.DoEvents();
        Thread.Sleep(300);
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var tt = typeof(OverlayForm);
        tt.GetMethod("OnMouseDown", flags).Invoke(overlay, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 100, 100, 0) });
        tt.GetMethod("OnMouseMove", flags).Invoke(overlay, new object[] { new MouseEventArgs(MouseButtons.Left, 0, 320, 230, 0) });
        tt.GetMethod("OnMouseUp", flags).Invoke(overlay, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 320, 230, 0) });
        Check(overlay.CroppedImage != null, "overlay cropped not null");
        if (overlay.CroppedImage != null)
        {
            Console.WriteLine("crop size: " + overlay.CroppedImage.Width + "x" + overlay.CroppedImage.Height);
            Check(overlay.CroppedImage.Width == 220 && overlay.CroppedImage.Height == 130, "overlay crop size 220x130");
        }
        // v1.2.1 事项四：框选时右键按下即取消
        var overlay2 = new OverlayForm((Bitmap)frozen.Clone());
        overlay2.Show();
        Application.DoEvents();
        Thread.Sleep(200);
        tt.GetMethod("OnMouseDown", flags).Invoke(overlay2, new object[] { new MouseEventArgs(MouseButtons.Right, 1, 50, 50, 0) });
        Application.DoEvents();
        Check(overlay2.DialogResult == DialogResult.Cancel && !overlay2.Visible, "overlay right click cancels");
        frozen.Dispose();
    }

    // ---------- 存储 ----------
    private static void RunStore()
    {
        var store = new Store();
        store.ClearHistory();
        var word = new Entry { Src = "apple", Dst = "苹果", SrcLang = "en", DstLang = "zh", Provider = "有道", Time = 1761710000 };
        var sentence = new Entry { Src = "I want to learn programming well", Dst = "我想学好编程", SrcLang = "en", DstLang = "zh", Provider = "必应", Time = 1761710001 };
        Check(word.IsWordLike(), "wordlike: apple");
        Check(!sentence.IsWordLike(), "wordlike: long sentence not word");
        store.AddFavorite(word);
        store.AddFavorite(sentence);
        store.AddHistory(word);
        string txt = Path.Combine(Path.GetTempPath(), "fav_test.txt");
        string csv = Path.Combine(Path.GetTempPath(), "fav_test.csv");
        // 导出条数＝当前收藏总数（测试库可能有历史运行留下的条目，不写死 2）
        Check(store.ExportFavorites(txt) == store.Favorites.Count && File.ReadAllText(txt).Contains("苹果"), "export txt");
        Check(store.ExportFavoritesCsv(csv) == store.Favorites.Count && File.ReadAllText(csv).Contains("苹果"), "export csv");
        var store2 = new Store();
        Check(store2.Favorites.Count >= 2, "reload favorites persisted");
        Check(store2.History.Count >= 1, "reload history persisted");
        Check(store2.Settings.HotkeyKey == "S", "settings default key S");
        Check(store2.Settings.GlmApiKey != null, "glm key default not null");
        store2.Settings.GlmApiKey = "test-key-123";
        store2.SaveSettings();
        var store3 = new Store();
        Check(store3.Settings.GlmApiKey == "test-key-123", "glm key persisted");
        store3.Settings.GlmApiKey = "";
        store3.SaveSettings();
        // v1.2.0 例句缓存往返
        var exEntry = new Entry { Src = "zzword120", Dst = "测试词", SrcLang = "en", DstLang = "zh", Provider = "test", Time = 1761710002 };
        store.AddFavorite(exEntry);
        exEntry.Examples = new List<ExampleItem>
        {
            new ExampleItem { En = "This is a zzword120 test.", Zh = "这是测试。", Source = "AI 日常例句" },
            new ExampleItem { En = "Another zzword120 here.", Zh = "另一个测试。", Source = "AI 日常例句" }
        };
        exEntry.ExamplesSource = "AI 日常例句";
        store.PersistFavorites();
        var store4 = new Store();
        var back = store4.Favorites.Find(f => f.Src == "zzword120");
        Check(back != null && back.Examples != null && back.Examples.Count == 2 && back.ExamplesSource == "AI 日常例句", "examples cache roundtrip");
        store.RemoveFavorite(exEntry);
        // v1.3.0：皮肤设置与复习/音标字段往返
        store3.Settings.Skin = "backdrop";
        store3.SaveSettings();
        Check(new Store().Settings.Skin == "backdrop", "skin setting persisted");
        var store5 = new Store();
        store5.Settings.Skin = "classic";
        store5.SaveSettings();
        var revEntry = new Entry { Src = "zzword130", Dst = "复习词", SrcLang = "en", DstLang = "zh", Provider = "test", Time = 1761710003, PhoneticUk = "/uk/", PhoneticUs = "/us/", ReviewLevel = 2, ReviewCount = 3, LastReview = 1761719999 };
        store.AddFavorite(revEntry);
        var back2 = new Store().Favorites.Find(f => f.Src == "zzword130");
        Check(back2 != null && back2.ReviewLevel == 2 && back2.ReviewCount == 3 && back2.PhoneticUk == "/uk/" && back2.LastReview == 1761719999, "review fields roundtrip");
        store.RemoveFavorite(revEntry);
    }

    // ---------- v1.2.0 词典/例句/点词 ----------
    private static async Task RunDict()
    {
        // 取词边界
        Check(WordText.WordAt("don't stop", 1) == "don't", "wordat apostrophe");
        Check(WordText.WordAt("well-known fact", 6) == "well-known", "wordat hyphen");
        Check(WordText.WordAt("hello, world", 5) == "", "wordat comma empty");
        Check(WordText.WordAt("persistence pays", 3) == "persistence", "wordat plain");
        // GLM 例句解析校验
        string good = "Don't give up — a little persistence and you'll pass the exam. ||| 别放弃，坚持一下你就能通过考试。\n" +
                      "Learning English takes persistence, step by step. ||| 学英语需要坚持，一步一步来。\n" +
                      "His persistence paid off in the end. ||| 他的坚持最终有了回报。\n" +
                      "With persistence, I fixed the car myself. ||| 凭着坚持，我自己修好了车。";
        Check(GlmExamples.Parse(good, "persistence").Count == 4, "glm examples parse 4");
        string bad = "Nothing relevant here. ||| 没关系。\nAnother one. ||| 另一个。";
        Check(GlmExamples.Parse(bad, "persistence").Count == 0, "glm examples reject off-word");
        // 有道词典实查
        var r = await YoudaoDict.Lookup("persistence");
        Console.WriteLine("dict: phone us=" + r.UsPhone + " uk=" + r.UkPhone + " defs=" + r.Definitions.Count + " ex=" + r.Examples.Count);
        if (r.Definitions.Count > 0) Console.WriteLine("def0: " + r.Definitions[0]);
        if (r.Examples.Count > 0) Console.WriteLine("ex0: " + r.Examples[0].En);
        Check(r.Definitions.Count >= 1, "dict definitions present");
        Check(r.Examples.Count >= 2, "dict examples present");
        Check(r.UsPhone.Length > 0 || r.UkPhone.Length > 0, "dict phonetic present");
        // 例句编排（无 Key → 词典兜底）
        var eng = new TranslationEngine();
        var f = await ExampleProvider.Fetch(eng, "persistence", "坚持不懈");
        Check(f.Items.Count >= 2 && f.Source == "有道词典例句", "example provider dict fallback");
    }

    // ---------- OCR（系统 tesseract 回退路径） ----------
    private static void RunOcr()
    {
        using (var bmp = new Bitmap(640, 120))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                using (var f = new Font("DejaVu Sans", 28f))
                    g.DrawString("Hello OCR World 123", f, Brushes.Black, 20, 35);
            }
            string text = OcrService.Recognize(bmp);
            Console.WriteLine("ocr: " + text);
            Check(text.Contains("Hello") && text.Contains("123"), "ocr reads test image");
        }
        // v1.3.0：带坐标识别（两块分离文本）
        using (var bmp2 = new Bitmap(640, 260))
        {
            using (var g = Graphics.FromImage(bmp2))
            {
                g.Clear(Color.White);
                using (var f = new Font("DejaVu Sans", 26f))
                {
                    g.DrawString("Hello Block One", f, Brushes.Black, 20, 20);
                    g.DrawString("Second Block Two", f, Brushes.Black, 20, 170);
                }
            }
            var blocks = OcrService.RecognizeBlocks(bmp2);
            Console.WriteLine("blocks: " + blocks.Count);
            foreach (var b in blocks) Console.WriteLine("block @" + b.Rect + " : " + b.Text.Replace("\n", " / "));
            Check(blocks.Count >= 2, "ocr blocks split");
            Check(blocks.Exists(b => b.Text.Contains("Hello")) && blocks.Exists(b => b.Text.Contains("Second")), "ocr blocks texts");
            if (blocks.Count >= 2) Check(blocks[0].Rect.Top < blocks[1].Rect.Top, "ocr blocks sorted by top");
            // v1.4.0：块带行高/行数元数据（有道 resRegions 口径）
            Check(blocks.Exists(b => b.LineCount >= 1 && b.LineHeight > 5), "ocr block line metadata");
        }
        // v1.4.0 自检（不含翻译步）：引擎/带坐标/整文三项应全绿
        var selfSteps = SelfTest.Run(false, "", 0).Result;
        foreach (var st in selfSteps) Console.WriteLine("selftest: " + st.Name + " ok=" + st.Ok + " " + st.Detail);
        Check(selfSteps.Count >= 3 && selfSteps.TrueForAll(st => st.Ok), "selftest ocr steps all ok");
        string cleaned = OcrService.CleanText("你 好 世 界  test  多 余");
        Console.WriteLine("clean: " + cleaned);
        Check(!cleaned.Contains("你 好"), "clean removes cjk spaces");
    }
}
