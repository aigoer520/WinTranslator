using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WinTranslator;

internal static class DbgProbe
{
    private static int Main(string[] args)
    {
        var t = Run().Result;
        Console.WriteLine(t);
        return 0;
    }

    private static async Task<string> Run()
    {
        var store = new Store();
        var eff = SyncService.EffectiveCreds(store.Settings);
        var r = await MultiDict.Lookup("youdao", "persistence", eff);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("DEFS=" + r.Definitions.Count);
        foreach (var d in r.Definitions) sb.AppendLine("DEF[" + d.Replace("\n", "\\n") + "]");
        foreach (var e in r.Examples) { sb.AppendLine("EN[" + e.En + "]"); sb.AppendLine("ZH[" + e.Zh + "]"); }
        sb.AppendLine("UK=" + r.UkPhone + " US=" + r.UsPhone);
        return sb.ToString();
    }
}
