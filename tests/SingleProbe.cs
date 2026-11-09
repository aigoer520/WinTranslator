using System;
using System.Threading.Tasks;
using WinTranslator;

static class SingleProbe
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        return Run(args).GetAwaiter().GetResult();
    }

    static async Task<int> Run(string[] args)
    {
        var s = AppSettings.Defaults();
        string kind = args[0];
        try
        {
            if (kind == "apertium")
                Console.WriteLine("apertium: " + await MultiEngine.Translate("apertium", "Hello world", "en", "es", s));
            else if (kind == "zdic")
            {
                var r = await MultiDict.Lookup("zdic", "萌", s);
                Console.WriteLine("zdic phone=" + r.UsPhone + " defs=" + r.Definitions.Count + " first=" + (r.Definitions.Count > 0 ? r.Definitions[0] : ""));
            }
            else if (kind == "vocabulary")
            {
                var r = await MultiDict.Lookup("vocabulary", "serendipity", s);
                Console.WriteLine("vocabulary defs=" + r.Definitions.Count + " first=" + (r.Definitions.Count > 0 ? r.Definitions[0].Substring(0, Math.Min(80, r.Definitions[0].Length)) : ""));
            }
            else if (kind == "cambridge")
            {
                var r = await MultiDict.Lookup("cambridge", "serendipity", s);
                Console.WriteLine("cambridge phone=" + r.UsPhone + " defs=" + r.Definitions.Count);
                foreach (var d in r.Definitions) Console.WriteLine("  def: " + d);
            }
            else if (kind == "reverso")
                Console.WriteLine("reverso: " + await MultiEngine.Translate("reverso", "Hello world", "en", "zh", s));
        }
        catch (Exception ex)
        {
            Console.WriteLine(kind + " EX: " + ex.GetType().Name + ": " + ex.Message);
        }
        Console.Out.Flush();
        return 0;
    }
}
