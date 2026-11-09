using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace WinTranslator
{
    public class ExampleItem
    {
        public string En { get; set; }
        public string Zh { get; set; }
        public string Source { get; set; }
    }

    public class DictResult
    {
        public string Word;
        public string UsPhone = "";
        public string UkPhone = "";
        public List<string> Definitions = new List<string>();
        public List<ExampleItem> Examples = new List<ExampleItem>();
    }

    public class ExampleFetch
    {
        public List<ExampleItem> Items = new List<ExampleItem>();
        public string Source = "";
    }

    /// <summary>
    /// 有道词典 jsonapi（免费无 Key，2026-10-29 实测）：一次拿到音标、词典释义与双语例句。
    /// GET dict.youdao.com/jsonapi?jsonversion=2&amp;client=mobile&amp;q=词
    /// </summary>
    public static class YoudaoDict
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        public static async Task<DictResult> Lookup(string word)
        {
            string url = "https://dict.youdao.com/jsonapi?jsonversion=2&client=mobile&q=" + Uri.EscapeDataString(word);
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            string raw = await (await HttpUtil.Client.SendAsync(req).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);
            var root = Json.DeserializeObject(raw) as Dictionary<string, object>;
            if (root == null) throw new TranslationException("有道词典响应异常");
            var r = new DictResult { Word = word };
            // 音标：simple.word[0] 的 usphone/ukphone（ec.word[0] 亦有，互为兜底）
            var simple = Dig(root, "simple", "word");
            if (simple != null) { r.UsPhone = Str(simple, "usphone"); r.UkPhone = Str(simple, "ukphone"); }
            var ecw = Dig(root, "ec", "word");
            if (ecw != null)
            {
                if (r.UsPhone.Length == 0) r.UsPhone = Str(ecw, "usphone");
                if (r.UkPhone.Length == 0) r.UkPhone = Str(ecw, "ukphone");
                var trs = ecw.ContainsKey("trs") ? ecw["trs"] as object[] : null;
                if (trs != null)
                    foreach (var t in trs)
                    {
                        var tr = (t as Dictionary<string, object>);
                        var arr = tr != null && tr.ContainsKey("tr") ? tr["tr"] as object[] : null;
                        if (arr == null || arr.Length == 0) continue;
                        var l = (arr[0] as Dictionary<string, object>);
                        var ld = l != null && l.ContainsKey("l") ? l["l"] as Dictionary<string, object> : null;
                        var ii = ld != null && ld.ContainsKey("i") ? ld["i"] as object[] : null;
                        if (ii != null)
                        {
                            var sb = new StringBuilder();
                            foreach (var x in ii) sb.Append(x as string);
                            if (sb.Length > 0) r.Definitions.Add(sb.ToString());
                        }
                        if (r.Definitions.Count >= 3) break;
                    }
            }
            // 双语例句：blng_sents_part.sentence-pair[]
            var bl = root.ContainsKey("blng_sents_part") ? root["blng_sents_part"] as Dictionary<string, object> : null;
            var pairs = bl != null && bl.ContainsKey("sentence-pair") ? bl["sentence-pair"] as object[] : null;
            if (pairs != null)
                foreach (var p in pairs)
                {
                    var pd = p as Dictionary<string, object>;
                    if (pd == null) continue;
                    string en = StripTags(Str(pd, "sentence"));
                    string zh = Str(pd, "sentence-translation");
                    string src = Str(pd, "source").Replace("《", "").Replace("》", "");
                    if (en.Length > 0 && zh.Length > 0)
                        r.Examples.Add(new ExampleItem { En = en, Zh = zh, Source = src });
                    if (r.Examples.Count >= 6) break;
                }
            return r;
        }

        private static Dictionary<string, object> Dig(Dictionary<string, object> root, string k1, string k2)
        {
            var d = root.ContainsKey(k1) ? root[k1] as Dictionary<string, object> : null;
            if (d == null || !d.ContainsKey(k2)) return null;
            var arr = d[k2] as object[];
            return arr != null && arr.Length > 0 ? arr[0] as Dictionary<string, object> : null;
        }

        private static string Str(Dictionary<string, object> d, string key)
        {
            return d != null && d.ContainsKey(key) ? (d[key] as string) ?? "" : "";
        }

        private static string StripTags(string s)
        {
            return Regex.Replace(s ?? "", "<[^>]+>", "").Trim();
        }
    }

    /// <summary>v1.2.0 例句主路线：智谱 GLM 按"日常口语场景"生成高频句型例句（修订三）。</summary>
    public static class GlmExamples
    {
        public static async Task<List<ExampleItem>> Generate(string apiKey, string word, string meaning)
        {
            var client = new GlmClient { ApiKey = apiKey };
            string sys = "You are an English teacher who helps Chinese learners speak natural daily English.";
            string user = "为英文单词/短语 \"" + word + "\"（中文意思：" + (meaning ?? "") + "）造 4 个例句。要求：" +
                          "1) 全部是日常生活交流场景（家庭、学校、工作、购物、出行、聊天、吃饭等）；" +
                          "2) 句子简单、实用、使用高频句型，每句不超过 18 个英文单词；" +
                          "3) 每句必须自然地包含该单词或短语（可以适当变形）；" +
                          "4) 每句附上自然的中文翻译。" +
                          "输出格式严格如下：每行一句，英文和中文之间用 ||| 分隔；不要编号、不要任何解释。";
            string raw = await client.ChatRaw(sys, user).ConfigureAwait(false);
            return Parse(raw, word);
        }

        /// <summary>解析并校验 GLM 输出：每行 EN ||| ZH；必须含目标词（或其词干）；不足 3 条视为失败。</summary>
        public static List<ExampleItem> Parse(string raw, string word)
        {
            var list = new List<ExampleItem>();
            if (string.IsNullOrWhiteSpace(raw)) return list;
            string stem = (word ?? "").Trim().ToLowerInvariant();
            if (stem.Length > 5) stem = stem.Substring(0, 5);
            foreach (var line in raw.Replace("\r", "").Split(new[] { '\n' }))
            {
                string s = line.Trim().TrimStart('-', '*', '·', ' ', '\t');
                int k = s.IndexOf("|||", StringComparison.Ordinal);
                if (k < 0) continue;
                string en = s.Substring(0, k).Trim().Trim(new[] { '"' });
                string zh = s.Substring(k + 3).Trim().Trim(new[] { '"' });
                if (en.Length == 0 || zh.Length == 0) continue;
                if (stem.Length > 0 && en.ToLowerInvariant().IndexOf(stem, StringComparison.Ordinal) < 0) continue;
                list.Add(new ExampleItem { En = en, Zh = zh, Source = "AI 日常例句" });
                if (list.Count >= 4) break;
            }
            if (list.Count < 3) return new List<ExampleItem>();
            return list;
        }
    }

    /// <summary>例句编排：GLM 日常生成优先 → 有道词典真实例句兜底。</summary>
    public static class ExampleProvider
    {
        public static async Task<ExampleFetch> Fetch(TranslationEngine engine, string word, string meaning)
        {
            if (engine != null && !string.IsNullOrWhiteSpace(engine.GlmApiKey))
            {
                try
                {
                    var items = await GlmExamples.Generate(engine.GlmApiKey, word, meaning).ConfigureAwait(false);
                    if (items.Count > 0) return new ExampleFetch { Items = items, Source = "AI 日常例句" };
                }
                catch { /* 生成失败走兜底 */ }
            }
            var dict = await YoudaoDict.Lookup(word).ConfigureAwait(false);
            var take = new List<ExampleItem>();
            foreach (var ex in dict.Examples) { take.Add(ex); if (take.Count >= 4) break; }
            return new ExampleFetch { Items = take, Source = take.Count > 0 ? "有道词典例句" : "" };
        }
    }
}
