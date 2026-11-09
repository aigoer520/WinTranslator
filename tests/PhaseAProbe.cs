using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WinTranslator;

static class PhaseAProbe
{
    static async Task T(string label, Func<Task> fn)
    {
        try { await fn(); }
        catch (Exception ex) { Console.WriteLine(label + " EX: " + ex.GetType().Name + ": " + ex.Message); }
    }

    static int Main()
    {
        return Run().GetAwaiter().GetResult();
    }

    static async Task<int> Run()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var s = AppSettings.Defaults();
        Console.WriteLine("== engines batch0 ==");
        await T("edge en->zh", async () => Console.WriteLine("edge: " + await MultiEngine.Translate("edge", "Hello world, this is a test.", "en", "zh", s)));
        await T("apertium en->es", async () => Console.WriteLine("apertium: " + await MultiEngine.Translate("apertium", "Hello world", "en", "es", s)));
        await T("lingva en->zh", async () => Console.WriteLine("lingva: " + await MultiEngine.Translate("lingva", "Hello world", "en", "zh", s)));
        await T("libre en->zh", async () => Console.WriteLine("libre: " + await MultiEngine.Translate("libre", "Hello world", "en", "zh", s)));
        await T("reverso en->zh", async () => Console.WriteLine("reverso: " + await MultiEngine.Translate("reverso", "Hello world", "en", "zh", s)));
        await T("deeplx(unconfigured)", async () => Console.WriteLine("deeplx: " + await MultiEngine.Translate("deeplx", "Hello", "en", "zh", s)));
        Console.WriteLine("== engines key (expect 未配置） ==");
        foreach (var id in new[] { "baidu", "deepl", "caiyun", "niutrans", "youdaozy", "ali", "tencent", "volc" })
        {
            string pid = id;
            await T(pid, async () => Console.WriteLine(pid + ": " + await MultiEngine.Translate(pid, "Hello", "en", "zh", s)));
        }
        Console.WriteLine("== dicts ==");
        await T("urban", async () => { var r = await MultiDict.Lookup("urban", "serendipity", s); Console.WriteLine("urban defs=" + r.Definitions.Count + " ex=" + r.Examples.Count + " first=" + (r.Definitions.Count > 0 ? r.Definitions[0].Substring(0, Math.Min(60, r.Definitions[0].Length)) : "")); });
        await T("guoyu 萌", async () => { var r = await MultiDict.Lookup("guoyu", "萌", s); Console.WriteLine("guoyu phone=" + r.UsPhone + "/" + r.UkPhone + " defs=" + r.Definitions.Count + " first=" + (r.Definitions.Count > 0 ? r.Definitions[0] : "")); });
        await T("liangan 萌", async () => { var r = await MultiDict.Lookup("liangan", "萌", s); Console.WriteLine("liangan defs=" + r.Definitions.Count + " first=" + (r.Definitions.Count > 0 ? r.Definitions[0] : "")); });
        await T("bingdict", async () => { var r = await MultiDict.Lookup("bingdict", "serendipity", s); Console.WriteLine("bingdict phone=" + r.UsPhone + " defs=" + r.Definitions.Count + " first=" + (r.Definitions.Count > 0 ? r.Definitions[0] : "")); });
        await T("cambridge", async () => { var r = await MultiDict.Lookup("cambridge", "serendipity", s); Console.WriteLine("cambridge phone=" + r.UsPhone + " defs=" + r.Definitions.Count + " first=" + (r.Definitions.Count > 0 ? r.Definitions[0] : "")); });
        await T("vocabulary", async () => { var r = await MultiDict.Lookup("vocabulary", "serendipity", s); Console.WriteLine("vocabulary defs=" + r.Definitions.Count + " first=" + (r.Definitions.Count > 0 ? r.Definitions[0].Substring(0, Math.Min(60, r.Definitions[0].Length)) : "")); });
        await T("zdic 萌", async () => { var r = await MultiDict.Lookup("zdic", "萌", s); Console.WriteLine("zdic phone=" + r.UsPhone + " defs=" + r.Definitions.Count + " first=" + (r.Definitions.Count > 0 ? r.Definitions[0] : "")); });
        await T("webster(unconfigured)", async () => { var r = await MultiDict.Lookup("webster", "hello", s); Console.WriteLine("webster defs=" + r.Definitions.Count); });
        Console.WriteLine("== crosscheck ==");
        var rep1 = CrossChecker.Check("He has 25 apples and does not eat them.", "他有25个苹果并且不吃它们。", "他有25个苹果，他不吃这些苹果。");
        Console.WriteLine("consistent: flags=" + rep1.Flags.Count + " aligned=" + rep1.Aligned);
        var rep2 = CrossChecker.Check("He has 25 apples and does not eat them.", "他有52个苹果并且吃它们。", "他有25个苹果，他不吃。");
        Console.WriteLine("planted errors: flags=" + rep2.Flags.Count);
        foreach (var f in rep2.Flags) Console.WriteLine("  flag: " + f.Reason);
        var rep3 = CrossChecker.Check("The committee approved the budget.", "委员会批准了预算。", "委员会否决了这项财政计划。");
        Console.WriteLine("divergent: flags=" + rep3.Flags.Count);
        foreach (var f in rep3.Flags) Console.WriteLine("  flag: " + f.Reason);
        Console.Out.Flush();
        return 0;
    }
}
