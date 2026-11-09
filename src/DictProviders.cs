using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace WinTranslator
{
    /// <summary>
    /// v1.8.0 多词典来源（有道沿用 DictService.YoudaoDict，其余 9 家在此）。
    /// 口径：查不到/失败明确抛 TranslationException 让小窗显示原因，不静默跳别家（方案 A 块）。
    /// </summary>
    public static class MultiDict
    {
        public class DictInfo
        {
            public string Id;
            public string Name;
            public string Short;   // 来源切换条上的短名
            public bool Chinese;   // 中文词优先落点
        }

        public static readonly DictInfo[] All =
        {
            new DictInfo { Id = "youdao",    Name = "有道词典",   Short = "有道",       Chinese = false },
            new DictInfo { Id = "bingdict",  Name = "必应词典",   Short = "必应",       Chinese = false },
            new DictInfo { Id = "cambridge", Name = "剑桥词典",   Short = "剑桥",       Chinese = false },
            new DictInfo { Id = "webster",   Name = "韦氏词典",   Short = "韦氏",       Chinese = false },
            new DictInfo { Id = "urban",     Name = "Urban",      Short = "Urban",      Chinese = false },
            new DictInfo { Id = "vocabulary", Name = "Vocabulary.com", Short = "Vocabulary", Chinese = false },
            new DictInfo { Id = "caiyun",    Name = "彩云词典",   Short = "彩云",       Chinese = false },
            new DictInfo { Id = "zdic",      Name = "汉典",       Short = "汉典",       Chinese = true },
            new DictInfo { Id = "guoyu",     Name = "国语辞典",   Short = "国语",       Chinese = true },
            new DictInfo { Id = "liangan",   Name = "两岸词典",   Short = "两岸",       Chinese = true },
        };

        public static DictInfo InfoOf(string id)
        {
            foreach (var d in All) if (d.Id == id) return d;
            return null;
        }

        public static bool IsConfigured(string id, AppSettings s)
        {
            if (s == null) return false;
            switch (id)
            {
                case "webster": return !string.IsNullOrWhiteSpace(s.WebsterKey);
                case "caiyun": return !string.IsNullOrWhiteSpace(s.CaiyunToken);
                default: return true;
            }
        }

        /// <summary>查中文词的默认来源、英文词的默认来源（用户没指定时用）。</summary>
        public static string DefaultFor(string word)
        {
            bool zh = word != null && Regex.IsMatch(word, "[\u4e00-\u9fff]");
            return zh ? "zdic" : "youdao";
        }

        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };

        public static async Task<DictResult> Lookup(string id, string word, AppSettings s)
        {
            word = (word ?? "").Trim();
            if (word.Length == 0) throw new TranslationException("查词内容为空");
            if (!IsConfigured(id, s))
                throw new TranslationException((InfoOf(id) != null ? InfoOf(id).Name : id) + " 未配置（请在设置→翻译接口里填写密钥）");
            switch (id)
            {
                case "youdao": return await YoudaoDict.Lookup(word).ConfigureAwait(false);
                case "urban": return await UrbanLookup(word).ConfigureAwait(false);
                case "guoyu": return await MoedictLookup(word, "a", "国语辞典").ConfigureAwait(false);
                case "liangan": return await MoedictLookup(word, "c", "两岸词典").ConfigureAwait(false);
                case "webster": return await WebsterLookup(word, s).ConfigureAwait(false);
                case "bingdict": return await BingDictLookup(word).ConfigureAwait(false);
                case "cambridge": return await CambridgeLookup(word).ConfigureAwait(false);
                case "vocabulary": return await VocabularyLookup(word).ConfigureAwait(false);
                case "zdic": return await ZdicLookup(word).ConfigureAwait(false);
                case "caiyun": return await CaiyunDictLookup(word, s).ConfigureAwait(false);
                default: throw new TranslationException("未知词典来源：" + id);
            }
        }

        // ---------------- 通用 ----------------

        private static string Str(Dictionary<string, object> d, params string[] keys)
        {
            if (d == null) return "";
            foreach (var k in keys)
                if (d.ContainsKey(k) && d[k] is string) return (string)d[k];
            return "";
        }

        private static object[] Arr(Dictionary<string, object> d, params string[] keys)
        {
            if (d == null) return null;
            foreach (var k in keys)
                if (d.ContainsKey(k) && d[k] is object[]) return (object[])d[k];
            return null;
        }

        private static async Task<Dictionary<string, object>> GetJson(string url)
        {
            string raw = await HttpUtil.Client.GetStringAsync(url).ConfigureAwait(false);
            return Json.DeserializeObject(raw) as Dictionary<string, object>;
        }

        private static async Task<string> GetHtml(string url)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            // 带浏览器 UA：汉典等站对无 UA 请求直接 404（2026-11-04 实测：curl 带 UA 得 200、Mono 无 UA 得 404）
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
            req.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
            var resp = await HttpUtil.Client.SendAsync(req).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.Forbidden || resp.StatusCode == HttpStatusCode.NotFound)
                throw new TranslationException("对方站点返回 " + (int)resp.StatusCode + "（可能被反爬拦截）");
            if (!resp.IsSuccessStatusCode)
                throw new TranslationException("对方站点返回 HTTP " + (int)resp.StatusCode);
            return body;
        }

        private static string CleanHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";
            string t = Regex.Replace(html, "(?is)<(script|style)[^>]*>.*?</\\1>", " ");
            t = Regex.Replace(t, "(?i)<br\\s*/?>", "\n");
            t = Regex.Replace(t, "<[^>]+>", " ");
            t = WebUtility.HtmlDecode(t);
            t = Regex.Replace(t, "[ \\t]+", " ");
            t = Regex.Replace(t, "\\n\\s*\\n+", "\n");
            return t.Trim();
        }

        // ---------------- 各家 ----------------

        private static async Task<DictResult> UrbanLookup(string word)
        {
            var root = await GetJson("https://api.urbandictionary.com/v0/define?term=" + Uri.EscapeDataString(word)).ConfigureAwait(false);
            var list = Arr(root, "list");
            if (list == null || list.Length == 0) throw new TranslationException("Urban 未收录该词");
            var r = new DictResult { Word = word };
            int defCount = 0;
            foreach (var item in list)
            {
                var d = item as Dictionary<string, object>;
                if (d == null) continue;
                string def = Regex.Replace(Str(d, "definition"), "[\\[\\]]", "");
                string ex = Regex.Replace(Str(d, "example"), "[\\[\\]]", "");
                if (def.Length > 0 && defCount < 3) { r.Definitions.Add(def); defCount++; }
                if (ex.Length > 0 && r.Examples.Count < 3)
                    r.Examples.Add(new ExampleItem { En = ex, Zh = "", Source = "Urban" });
                if (defCount >= 3 && r.Examples.Count >= 3) break;
            }
            if (r.Definitions.Count == 0) throw new TranslationException("Urban 未收录该词");
            return r;
        }

        private static async Task<DictResult> MoedictLookup(string word, string dictCode, string dictName)
        {
            var root = await GetJson("https://www.moedict.tw/" + dictCode + "/" + Uri.EscapeDataString(word) + ".json").ConfigureAwait(false);
            if (root == null) throw new TranslationException(dictName + "未收录该词");
            var r = new DictResult { Word = word };
            string title = Str(root, "title", "t");
            if (title.Length > 0) r.Word = title;
            var hets = Arr(root, "heteronyms", "h");
            if (hets != null)
                foreach (var h in hets)
                {
                    var hd = h as Dictionary<string, object>;
                    if (hd == null) continue;
                    string pinyin = Str(hd, "pinyin", "p");
                    string bopomofo = Str(hd, "bopomofo", "b");
                    if (r.UsPhone.Length == 0 && pinyin.Length > 0) r.UsPhone = pinyin;
                    if (r.UkPhone.Length == 0 && bopomofo.Length > 0) r.UkPhone = bopomofo;
                    var defs = Arr(hd, "definitions", "d");
                    if (defs != null)
                        foreach (var df in defs)
                        {
                            var dd = df as Dictionary<string, object>;
                            if (dd == null) continue;
                            string type = MoedictClean(Str(dd, "type", "t"), title.Length > 0 ? title : word);
                            string def = MoedictClean(Str(dd, "def", "f"), title.Length > 0 ? title : word);
                            if (def.Length > 0)
                                r.Definitions.Add((type.Length > 0 ? "〈" + type + "〉" : "") + def);
                            var exs = Arr(dd, "example", "e");
                            if (exs != null)
                                foreach (var e in exs)
                                {
                                    string ex = e as string;
                                    if (!string.IsNullOrEmpty(ex) && r.Examples.Count < 3)
                                        r.Examples.Add(new ExampleItem { En = "", Zh = MoedictClean(CleanHtml(ex), title.Length > 0 ? title : word), Source = dictName });
                                }
                            if (r.Definitions.Count >= 8) break;
                        }
                    if (r.Definitions.Count >= 8) break;
                }
            if (r.Definitions.Count == 0) throw new TranslationException(dictName + "未收录该词");
            return r;
        }

        /// <summary>萌典原文记号清理：原文按「`词段~」逐段标记（2026-11-04 实查 萌 的原始 JSON 确认），
        /// 反引号与 ~ 都是分段记号、不是内容，一律去掉（~ 误作词头替换会把词头插进每段、造成叠字）。</summary>
        private static string MoedictClean(string text, string head)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("`", "").Replace("~", "").Trim();
        }

        private static async Task<DictResult> WebsterLookup(string word, AppSettings s)
        {
            string url = "https://www.dictionaryapi.com/api/v3/references/collegiate/json/" +
                Uri.EscapeDataString(word) + "?key=" + Uri.EscapeDataString(s.WebsterKey.Trim());
            string raw = await HttpUtil.Client.GetStringAsync(url).ConfigureAwait(false);
            var arr = Json.DeserializeObject(raw) as object[];
            if (arr == null || arr.Length == 0) throw new TranslationException("韦氏词典未收录该词");
            if (arr[0] is string) throw new TranslationException("韦氏词典未收录该词（返回的是拼写建议）");
            var r = new DictResult { Word = word };
            foreach (var item in arr)
            {
                var d = item as Dictionary<string, object>;
                if (d == null) continue;
                var hwi = d.ContainsKey("hwi") ? d["hwi"] as Dictionary<string, object> : null;
                var prs = hwi != null ? Arr(hwi, "prs") : null;
                if (prs != null && prs.Length > 0)
                {
                    var p0 = prs[0] as Dictionary<string, object>;
                    string mw = p0 != null ? Str(p0, "mw") : "";
                    if (mw.Length > 0 && r.UsPhone.Length == 0) r.UsPhone = mw;
                }
                string fl = Str(d, "fl");
                var shorts = Arr(d, "shortdef");
                if (shorts != null)
                    foreach (var sd in shorts)
                    {
                        string t = sd as string;
                        if (!string.IsNullOrEmpty(t))
                            r.Definitions.Add((fl.Length > 0 ? fl + ". " : "") + t);
                        if (r.Definitions.Count >= 6) break;
                    }
                if (r.Definitions.Count >= 6) break;
            }
            if (r.Definitions.Count == 0) throw new TranslationException("韦氏词典未收录该词");
            return r;
        }

        private static async Task<DictResult> BingDictLookup(string word)
        {
            string html = await GetHtml("https://www.bing.com/dict/search?q=" + Uri.EscapeDataString(word)).ConfigureAwait(false);
            // 必应按出口 IP 有时回「无结果」页（带拼写提示语），别把那页的样式/提示当释义解析（2026-11-04 实测翻车）
            if (html.IndexOf("Ensure words are spelled correctly", StringComparison.Ordinal) >= 0)
                throw new TranslationException("必应词典未查到该词（必应回了无结果页，也可能与当前网络出口有关）");
            var r = new DictResult { Word = word };
            var mUs = Regex.Match(html, "hd_prUS[^>]*>\\s*\\[?([^\\]<{]+)");
            if (mUs.Success) r.UsPhone = mUs.Groups[1].Value.Trim();
            var mUk = Regex.Match(html, "hd_pr[^>]*>\\s*\\[?([^\\]<{]+)");
            if (mUk.Success) r.UkPhone = mUk.Groups[1].Value.Trim();
            var mDef = Regex.Match(html, "(?is)<div class=\"qdef\">(.*?)</div>\\s*<div class=\"hd_div1\"");
            string zone;
            if (mDef.Success) zone = mDef.Groups[1].Value;
            else
            {
                int qi = html.IndexOf("class=\"qdef\"", StringComparison.Ordinal);
                if (qi < 0) throw new TranslationException("必应词典未收录该词或页面结构已变");
                zone = html.Substring(qi, Math.Min(9000, html.Length - qi));
            }
            foreach (Match li in Regex.Matches(zone, "(?is)<li>(.*?)</li>"))
            {
                string t = CleanHtml(li.Groups[1].Value);
                // 样式垃圾兜底：含花括号/以点开头的多半是 CSS，不是释义
                if (t.Length > 0 && t.Length <= 200 && !t.Contains("{") && !t.Contains("}") && !t.StartsWith(".") && !t.StartsWith("网络释义"))
                    r.Definitions.Add(t);
                if (r.Definitions.Count >= 6) break;
            }
            if (r.Definitions.Count == 0) throw new TranslationException("必应词典未收录该词或页面结构已变");
            return r;
        }

        private static async Task<DictResult> CambridgeLookup(string word)
        {
            string html = await GetHtml("https://dictionary.cambridge.org/dictionary/english-chinese-simplified/" +
                Uri.EscapeDataString(word)).ConfigureAwait(false);
            var r = new DictResult { Word = word };
            var mPh = Regex.Match(html, "(?is)<span class=\"ipa[^\"]*\"[^>]*>(.*?)</span>/");
            if (mPh.Success) r.UsPhone = CleanHtml(mPh.Groups[1].Value);
            foreach (Match m in Regex.Matches(html, "(?is)<div class=\"def ddef_d db\">(.*?)</div>"))
            {
                string t = CleanHtml(m.Groups[1].Value);
                if (t.Length > 0) r.Definitions.Add(t);
                if (r.Definitions.Count >= 5) break;
            }
            // 中文译义在 lang="zh-Hans" 的 trans 跨度里（内嵌链接/子跨度，取定长窗口整段清洗）
            foreach (Match m in Regex.Matches(html, "class=\"trans dtrans[^\"]*\" lang=\"zh-Hans\">"))
            {
                int start = m.Index + m.Length;
                int take = Math.Min(400, html.Length - start);
                string rawZone = html.Substring(start, take);
                int scriptAt = rawZone.IndexOf("<script", StringComparison.OrdinalIgnoreCase);
                if (scriptAt > 0) rawZone = rawZone.Substring(0, scriptAt);
                string t = CleanHtml(rawZone);
                int cut = t.IndexOf("Add to word list", StringComparison.Ordinal);
                if (cut > 0) t = t.Substring(0, cut).Trim();
                if (t.Length > 0 && r.Definitions.Count < 8 && !r.Definitions.Contains(t)) r.Definitions.Add(t);
                if (r.Definitions.Count >= 8) break;
            }
            if (r.Definitions.Count == 0)
                throw new TranslationException("剑桥词典未收录该词或被反爬拦截（可在浏览器打开查看）");
            return r;
        }

        private static async Task<DictResult> VocabularyLookup(string word)
        {
            string html = await GetHtml("https://www.vocabulary.com/dictionary/" + Uri.EscapeDataString(word)).ConfigureAwait(false);
            var r = new DictResult { Word = word };
            var mShort = Regex.Match(html, "(?is)<p class=\"short\">(.*?)</p>");
            if (mShort.Success)
            {
                string t = CleanHtml(mShort.Groups[1].Value);
                if (t.Length > 0) r.Definitions.Add(t);
            }
            foreach (Match m in Regex.Matches(html, "(?is)<div class=\"definition\"[^>]*>(.*?)</div>"))
            {
                string t = CleanHtml(m.Groups[1].Value);
                if (t.Length > 0) r.Definitions.Add(t);
                if (r.Definitions.Count >= 6) break;
            }
            if (r.Definitions.Count == 0) throw new TranslationException("Vocabulary.com 未收录该词或页面结构已变");
            return r;
        }

        private static async Task<DictResult> ZdicLookup(string word)
        {
            // 用裸域：www 子域对部分客户端直接 404（2026-11-04 实测 Mono 访问 www 得 404、裸域得 200）
            string html = await GetHtml("https://zdic.net/hans/" + Uri.EscapeDataString(word)).ConfigureAwait(false);
            var r = new DictResult { Word = word };
            // 新版页结构（2026-11-04 实查）：基本解释在 jbjs 区，拼音/注音与逐条释义都有固定 class
            var mPy = Regex.Match(html, "jbjs-reading__py\">([^<]+)");
            if (mPy.Success) r.UsPhone = mPy.Groups[1].Value.Trim();
            var mZy = Regex.Match(html, "jbjs-reading__zy\">([^<]+)");
            if (mZy.Success) r.UkPhone = mZy.Groups[1].Value.Trim();
            foreach (Match m in Regex.Matches(html, "(?is)jbjs-item__def\">(.*?)</span>"))
            {
                string t = CleanHtml(m.Groups[1].Value);
                if (t.Length > 0) r.Definitions.Add(t);
                if (r.Definitions.Count >= 8) break;
            }
            if (r.Definitions.Count == 0)
                foreach (Match m in Regex.Matches(html, "(?is)xxjs-item__def\">(.*?)</div>"))
                {
                    string t = CleanHtml(m.Groups[1].Value);
                    if (t.Length > 0) r.Definitions.Add(t);
                    if (r.Definitions.Count >= 8) break;
                }
            if (r.Definitions.Count == 0) throw new TranslationException("汉典未收录该词或页面结构已变");
            return r;
        }

        private static async Task<DictResult> CaiyunDictLookup(string word, AppSettings s)
        {
            bool zh = Regex.IsMatch(word, "[\u4e00-\u9fff]");
            var payload = new Dictionary<string, object>
            {
                { "source", word },
                { "trans_type", zh ? "zh2en" : "en2zh" },
                { "request_id", Guid.NewGuid().ToString("N") },
                { "user_id", "" }
            };
            var headers = new Dictionary<string, string>
            {
                { "x-authorization", "token " + s.CaiyunToken.Trim() },
                { "X-Authorization", "token " + s.CaiyunToken.Trim() }
            };
            var req = new HttpRequestMessage(HttpMethod.Post, "https://api.interpreter.caiyunai.com/v1/dict");
            req.Content = new StringContent(Json.Serialize(payload), Encoding.UTF8, "application/json");
            foreach (var kv in headers) req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
            var resp = await HttpUtil.Client.SendAsync(req).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var root = Json.DeserializeObject(body) as Dictionary<string, object>;
            var dict = root != null && root.ContainsKey("dictionary") ? root["dictionary"] as Dictionary<string, object> : null;
            var exps = dict != null ? Arr(dict, "explanations") : null;
            var r = new DictResult { Word = word };
            if (exps != null)
                foreach (var e in exps)
                {
                    string t = e as string;
                    if (!string.IsNullOrEmpty(t)) r.Definitions.Add(t);
                    if (r.Definitions.Count >= 6) break;
                }
            if (r.Definitions.Count == 0) throw new TranslationException("彩云词典未收录该词或接口返回异常");
            return r;
        }
    }
}
