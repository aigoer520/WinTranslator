using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace WinTranslator
{
    /// <summary>
    /// v1.8.0 新增引擎（批次 0 免费无 Key ＋ 批次 1/2 用户自备 Key）。
    /// 口径：手动选定的引擎失败一律抛 TranslationException 明确报错，不静默回退（方案拍板③）；
    /// 「自动」链不纳入这些引擎（在 TranslationEngine 里控制）。密钥从 AppSettings 读（DPAPI 解密由调用方完成）。
    /// </summary>
    public static class MultiEngine
    {
        public class ProviderInfo
        {
            public string Id;
            public string Name;
            public string Capsule;     // 引擎胶囊上的短名
            public bool NeedsConfig;   // 需要用户填地址或密钥才出现
        }

        public static readonly ProviderInfo[] All =
        {
            new ProviderInfo { Id = "edge",     Name = "Edge 翻译",   Capsule = "Edge 翻译", NeedsConfig = false },
            new ProviderInfo { Id = "deeplx",   Name = "DeepLX",      Capsule = "DeepLX",   NeedsConfig = true },
            new ProviderInfo { Id = "libre",    Name = "LibreTranslate", Capsule = "Libre", NeedsConfig = true },
            new ProviderInfo { Id = "lingva",   Name = "Lingva",      Capsule = "Lingva",   NeedsConfig = true },
            new ProviderInfo { Id = "apertium", Name = "Apertium",    Capsule = "Apertium", NeedsConfig = false },
            new ProviderInfo { Id = "reverso",  Name = "Reverso",     Capsule = "Reverso",  NeedsConfig = false },
            new ProviderInfo { Id = "baidu",    Name = "百度翻译",    Capsule = "百度",     NeedsConfig = true },
            new ProviderInfo { Id = "deepl",    Name = "DeepL",       Capsule = "DeepL",    NeedsConfig = true },
            new ProviderInfo { Id = "caiyun",   Name = "彩云小译",    Capsule = "彩云",     NeedsConfig = true },
            new ProviderInfo { Id = "niutrans", Name = "小牛翻译",    Capsule = "小牛",     NeedsConfig = true },
            new ProviderInfo { Id = "youdaozy", Name = "有道智云",    Capsule = "有道智云", NeedsConfig = true },
            new ProviderInfo { Id = "ali",      Name = "阿里翻译",    Capsule = "阿里",     NeedsConfig = true },
            new ProviderInfo { Id = "tencent",  Name = "腾讯翻译君",  Capsule = "腾讯",     NeedsConfig = true },
            new ProviderInfo { Id = "volc",     Name = "火山翻译",    Capsule = "火山",     NeedsConfig = true },
        };

        public static ProviderInfo InfoOf(string id)
        {
            foreach (var p in All) if (p.Id == id) return p;
            return null;
        }

        public static bool IsConfigured(string id, AppSettings s)
        {
            if (s == null) return false;
            switch (id)
            {
                case "edge": case "apertium": case "reverso": return true;
                // Libre/Lingva：官方/默认公共实例现需 Key 或上游不稳（2026-11-04 实测），
                // 用户把地址改成自建/可用实例后才算配置（默认值与 Store.Defaults 对齐）
                case "libre": return !string.IsNullOrWhiteSpace(s.LibreUrl) &&
                    !string.Equals(s.LibreUrl.Trim().TrimEnd(new[] { '/' }), "https://libretranslate.com", StringComparison.OrdinalIgnoreCase);
                case "lingva": return !string.IsNullOrWhiteSpace(s.LingvaUrl) &&
                    !string.Equals(s.LingvaUrl.Trim().TrimEnd(new[] { '/' }), "https://lingva.lunar.icu", StringComparison.OrdinalIgnoreCase);
                case "deeplx": return !string.IsNullOrWhiteSpace(s.DeeplxUrl);
                case "baidu": return !string.IsNullOrWhiteSpace(s.BaiduAppId) && !string.IsNullOrWhiteSpace(s.BaiduKey);
                case "deepl": return !string.IsNullOrWhiteSpace(s.DeepLKey);
                case "caiyun": return !string.IsNullOrWhiteSpace(s.CaiyunToken);
                case "niutrans": return !string.IsNullOrWhiteSpace(s.NiutransKey);
                case "youdaozy": return !string.IsNullOrWhiteSpace(s.YoudaoAppKey) && !string.IsNullOrWhiteSpace(s.YoudaoKey);
                case "ali": return !string.IsNullOrWhiteSpace(s.AliKeyId) && !string.IsNullOrWhiteSpace(s.AliKeySecret);
                case "tencent": return !string.IsNullOrWhiteSpace(s.TencentSecretId) && !string.IsNullOrWhiteSpace(s.TencentSecretKey);
                case "volc": return !string.IsNullOrWhiteSpace(s.VolcKeyId) && !string.IsNullOrWhiteSpace(s.VolcKeySecret);
                default: return false;
            }
        }

        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };

        public static async Task<string> Translate(string id, string text, string src, string dst, AppSettings s)
        {
            if (!IsConfigured(id, s))
                throw new TranslationException((InfoOf(id) != null ? InfoOf(id).Name : id) + " 未配置（请在设置→翻译接口里填写）");
            var parts = new List<string>();
            foreach (var chunk in ChunkText(text, 900))
                parts.Add(await TranslateChunk(id, chunk, src, dst, s).ConfigureAwait(false));
            return string.Join("\n", parts);
        }

        private static List<string> ChunkText(string text, int maxLen)
        {
            var chunks = new List<string>();
            var current = new StringBuilder();
            foreach (var para in text.Replace("\r\n", "\n").Split(new[] { '\n' }))
            {
                string p = para;
                while (p.Length > maxLen)
                {
                    int cut = p.LastIndexOfAny(new[] { '。', '！', '？', '.', '!', '?', ' ' }, maxLen - 1);
                    if (cut < maxLen / 3) cut = maxLen; else cut = cut + 1;
                    if (current.Length > 0) { chunks.Add(current.ToString()); current.Clear(); }
                    chunks.Add(p.Substring(0, cut));
                    p = p.Substring(cut);
                }
                if (current.Length + p.Length + 1 > maxLen && current.Length > 0)
                {
                    chunks.Add(current.ToString());
                    current.Clear();
                }
                if (current.Length > 0) current.Append('\n');
                current.Append(p);
            }
            if (current.Length > 0) chunks.Add(current.ToString());
            return chunks;
        }

        private static async Task<string> TranslateChunk(string id, string text, string src, string dst, AppSettings s)
        {
            switch (id)
            {
                case "edge": return await EdgeTranslate(text, src, dst).ConfigureAwait(false);
                case "apertium": return await ApertiumTranslate(text, src, dst).ConfigureAwait(false);
                case "lingva": return await LingvaTranslate(text, src, dst, s).ConfigureAwait(false);
                case "libre": return await LibreTranslate(text, src, dst, s).ConfigureAwait(false);
                case "deeplx": return await DeepLxTranslate(text, src, dst, s).ConfigureAwait(false);
                case "reverso": return await ReversoTranslate(text, src, dst).ConfigureAwait(false);
                case "baidu": return await BaiduTranslate(text, src, dst, s).ConfigureAwait(false);
                case "deepl": return await DeepLTranslate(text, src, dst, s).ConfigureAwait(false);
                case "caiyun": return await CaiyunTranslate(text, src, dst, s).ConfigureAwait(false);
                case "niutrans": return await NiutransTranslate(text, src, dst, s).ConfigureAwait(false);
                case "youdaozy": return await YoudaoZyTranslate(text, src, dst, s).ConfigureAwait(false);
                case "tencent": return await TencentTranslate(text, src, dst, s).ConfigureAwait(false);
                case "ali": return await AliTranslate(text, src, dst, s).ConfigureAwait(false);
                case "volc": return await VolcTranslate(text, src, dst, s).ConfigureAwait(false);
                default: throw new TranslationException("未知引擎：" + id);
            }
        }

        // ---------------- 通用小工具 ----------------

        private static string Esc(string s) { return Uri.EscapeDataString(s ?? ""); }

        private static async Task<Dictionary<string, object>> GetJson(string url)
        {
            string body = await HttpUtil.Client.GetStringAsync(url).ConfigureAwait(false);
            return Json.DeserializeObject(body) as Dictionary<string, object>;
        }

        private static async Task<Dictionary<string, object>> PostJson(string url, object payload, IDictionary<string, string> headers = null)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Content = new StringContent(Json.Serialize(payload), Encoding.UTF8, "application/json");
            if (headers != null)
                foreach (var kv in headers) req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
            var resp = await HttpUtil.Client.SendAsync(req).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new TranslationException("接口返回 HTTP " + (int)resp.StatusCode + "：" + Trunc(body, 120));
            return Json.DeserializeObject(body) as Dictionary<string, object>;
        }

        private static string Trunc(string s, int n)
        {
            s = (s ?? "").Replace("\r", " ").Replace("\n", " ");
            return s.Length <= n ? s : s.Substring(0, n) + "…";
        }

        private static string Str(object o) { return o == null ? "" : o.ToString(); }

        private static string Iso3Apertium(string code)
        {
            switch (code)
            {
                case "en": return "eng"; case "es": return "spa"; case "fr": return "fra";
                case "de": return "deu"; case "ru": return "rus"; case "pt": return "por";
                case "zh": case "cht": return "zho"; case "ja": return "jpn"; case "ko": return "kor";
                default: return code;
            }
        }

        private static string Iso3Reverso(string code)
        {
            switch (code)
            {
                case "en": return "eng"; case "zh": case "cht": return "chi"; case "fr": return "fra";
                case "de": return "ger"; case "es": return "spa"; case "ru": return "rus";
                case "ja": return "jpn"; case "ko": return "kor"; case "pt": return "por";
                default: return code;
            }
        }

        // ---------------- 批次 0：免费无 Key ----------------

        private static string _edgeAuth;
        private static DateTime _edgeAuthAt = DateTime.MinValue;

        private static async Task<string> EdgeTranslate(string text, string src, string dst)
        {
            if (_edgeAuth == null || (DateTime.Now - _edgeAuthAt).TotalMinutes > 8)
            {
                _edgeAuth = await HttpUtil.Client.GetStringAsync("https://edge.microsoft.com/translate/auth").ConfigureAwait(false);
                _edgeAuth = (_edgeAuth ?? "").Trim();
                _edgeAuthAt = DateTime.Now;
                if (_edgeAuth.Length < 10) throw new TranslationException("Edge 翻译令牌获取失败");
            }
            string from = src == "zh" ? "zh-Hans" : src == "cht" ? "zh-Hant" : src;
            string to = dst == "zh" ? "zh-Hans" : dst == "cht" ? "zh-Hant" : dst;
            string url = "https://api-edge.cognitive.microsofttranslator.com/translate?api-version=3.0&from=" + Esc(from) + "&to=" + Esc(to);
            var headers = new Dictionary<string, string> { { "Authorization", "Bearer " + _edgeAuth } };
            var arr = await PostJsonArray(url, new[] { new Dictionary<string, object> { { "Text", text } } }, headers).ConfigureAwait(false);
            if (arr == null || arr.Length == 0) throw new TranslationException("Edge 翻译无结果");
            var first = arr[0] as Dictionary<string, object>;
            var trs = first != null ? first["translations"] as object[] : null;
            if (trs == null || trs.Length == 0) throw new TranslationException("Edge 翻译无结果");
            var t0 = trs[0] as Dictionary<string, object>;
            string outText = t0 != null ? Str(t0["text"]) : "";
            if (outText.Length == 0) throw new TranslationException("Edge 翻译无结果");
            return outText;
        }

        private static async Task<object[]> PostJsonArray(string url, object payload, IDictionary<string, string> headers)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Content = new StringContent(Json.Serialize(payload), Encoding.UTF8, "application/json");
            if (headers != null)
                foreach (var kv in headers) req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
            var resp = await HttpUtil.Client.SendAsync(req).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new TranslationException("接口返回 HTTP " + (int)resp.StatusCode + "：" + Trunc(body, 120));
            return Json.DeserializeObject(body) as object[];
        }

        private static async Task<string> ApertiumTranslate(string text, string src, string dst)
        {
            string url = "https://www.apertium.org/apy/translate?q=" + Esc(text) +
                "&langpair=" + Iso3Apertium(src) + "%7C" + Iso3Apertium(dst);
            var root = await GetJson(url).ConfigureAwait(false);
            var data = root != null ? root["responseData"] as Dictionary<string, object> : null;
            string outText = data != null ? Str(data["translatedText"]) : "";
            if (outText.Length == 0 || outText.Contains("Not an Apertium"))
                throw new TranslationException("Apertium 不支持该语种对或无结果（它以欧洲语言为主）");
            return outText;
        }

        private static async Task<string> LingvaTranslate(string text, string src, string dst, AppSettings s)
        {
            string baseUrl = (string.IsNullOrWhiteSpace(s.LingvaUrl) ? "https://lingva.lunar.icu" : s.LingvaUrl).TrimEnd(new[] { '/' });
            string from = src == "cht" ? "zh-TW" : src;
            string to = dst == "cht" ? "zh-TW" : dst;
            string url = baseUrl + "/api/v1/" + Esc(from) + "/" + Esc(to) + "/" + Esc(text);
            var root = await GetJson(url).ConfigureAwait(false);
            string outText = root != null ? Str(root["translation"]) : "";
            if (outText.Length == 0) throw new TranslationException("Lingva 无结果（实例可能不可用，可在设置里换实例地址）");
            return outText;
        }

        private static async Task<string> LibreTranslate(string text, string src, string dst, AppSettings s)
        {
            if (dst == "cht" || src == "cht")
                throw new TranslationException("LibreTranslate 不支持繁体中文");
            string baseUrl = (string.IsNullOrWhiteSpace(s.LibreUrl) ? "https://libretranslate.com" : s.LibreUrl).TrimEnd(new[] { '/' });
            var payload = new Dictionary<string, object>
            {
                { "q", text }, { "source", src }, { "target", dst }, { "format", "text" }
            };
            var root = await PostJson(baseUrl + "/translate", payload).ConfigureAwait(false);
            string outText = root != null ? Str(root["translatedText"]) : "";
            if (outText.Length == 0)
                throw new TranslationException("LibreTranslate 无结果（官方实例现需 Key，可在设置里换自建/公开实例地址）");
            return outText;
        }

        private static async Task<string> DeepLxTranslate(string text, string src, string dst, AppSettings s)
        {
            string url = s.DeeplxUrl.Trim();
            var payload = new Dictionary<string, object>
            {
                { "text", text },
                { "source_lang", src.ToUpperInvariant() == "CHT" ? "ZH" : src.ToUpperInvariant() },
                { "target_lang", dst.ToUpperInvariant() == "CHT" ? "ZH" : dst.ToUpperInvariant() }
            };
            var headers = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(s.DeeplxToken))
                headers["Authorization"] = "Bearer " + s.DeeplxToken.Trim();
            var root = await PostJson(url, payload, headers).ConfigureAwait(false);
            string outText = root != null ? Str(root["data"]) : "";
            if (outText.Length == 0 && root != null && root.ContainsKey("alternatives")) outText = "";
            if (outText.Length == 0) throw new TranslationException("DeepLX 无结果（检查实例地址与令牌）");
            return outText;
        }

        private static async Task<string> ReversoTranslate(string text, string src, string dst)
        {
            var payload = new Dictionary<string, object>
            {
                { "format", "text" },
                { "from", Iso3Reverso(src) },
                { "to", Iso3Reverso(dst) },
                { "input", text },
                { "options", new Dictionary<string, object>
                    {
                        { "sentenceSplitter", true }, { "origin", "translation.web" },
                        { "contextResults", true }, { "languageDetection", true }
                    } }
            };
            var root = await PostJson("https://api.reverso.net/translate/v1/translation", payload).ConfigureAwait(false);
            var arr = root != null ? root["translation"] as object[] : null;
            if (arr == null || arr.Length == 0) throw new TranslationException("Reverso 无结果");
            var sb = new StringBuilder();
            foreach (var a in arr) sb.Append(Str(a));
            return sb.ToString();
        }

        // ---------------- 批次 1：自备 Key（签名简单） ----------------

        private static string BaiduCode(string code)
        {
            switch (code)
            {
                case "ja": return "jp"; case "ko": return "kor"; case "fr": return "fra";
                case "es": return "spa"; default: return code;
            }
        }

        private static async Task<string> BaiduTranslate(string text, string src, string dst, AppSettings s)
        {
            string salt = new Random().Next(10000, 99999).ToString();
            string sign = HttpUtil.Md5Hex(s.BaiduAppId.Trim() + text + salt + s.BaiduKey.Trim());
            string url = "https://fanyi-api.baidu.com/api/trans/vip/translate?q=" + Esc(text) +
                "&from=" + BaiduCode(src) + "&to=" + BaiduCode(dst) +
                "&appid=" + Esc(s.BaiduAppId.Trim()) + "&salt=" + salt + "&sign=" + sign;
            var root = await GetJson(url).ConfigureAwait(false);
            if (root != null && root.ContainsKey("error_code"))
                throw new TranslationException("百度翻译报错 " + Str(root["error_code"]) + "：" + Str(root["error_msg"]));
            var arr = root != null ? root["trans_result"] as object[] : null;
            if (arr == null || arr.Length == 0) throw new TranslationException("百度翻译无结果");
            var sb = new StringBuilder();
            foreach (var item in arr)
            {
                var d = item as Dictionary<string, object>;
                if (d != null) { if (sb.Length > 0) sb.Append("\n"); sb.Append(Str(d["dst"])); }
            }
            return sb.ToString();
        }

        private static async Task<string> DeepLTranslate(string text, string src, string dst, AppSettings s)
        {
            string key = s.DeepLKey.Trim();
            string host = key.EndsWith(":fx") ? "https://api-free.deepl.com" : "https://api.deepl.com";
            string target = dst == "zh" ? "ZH-HANS" : dst == "cht" ? "ZH-HANT" : dst.ToUpperInvariant();
            string source = src == "cht" ? "ZH" : src.ToUpperInvariant();
            var form = new Dictionary<string, string>
            {
                { "text", text }, { "target_lang", target }, { "source_lang", source }
            };
            var req = new HttpRequestMessage(HttpMethod.Post, host + "/v2/translate");
            req.Headers.TryAddWithoutValidation("Authorization", "DeepL-Auth-Key " + key);
            req.Content = new FormUrlEncodedContent(form);
            var resp = await HttpUtil.Client.SendAsync(req).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new TranslationException("DeepL 返回 HTTP " + (int)resp.StatusCode + "：" + Trunc(body, 120));
            var root = Json.DeserializeObject(body) as Dictionary<string, object>;
            var arr = root != null ? root["translations"] as object[] : null;
            if (arr == null || arr.Length == 0) throw new TranslationException("DeepL 无结果");
            var t0 = arr[0] as Dictionary<string, object>;
            return t0 != null ? Str(t0["text"]) : "";
        }

        private static async Task<string> CaiyunTranslate(string text, string src, string dst, AppSettings s)
        {
            var ok = new HashSet<string> { "en", "zh", "ja" };
            if (!ok.Contains(src) || !ok.Contains(dst))
                throw new TranslationException("彩云小译仅支持中/英/日互译");
            var payload = new Dictionary<string, object>
            {
                { "source", new[] { text } },
                { "trans_type", src + "2" + dst },
                { "request_id", Guid.NewGuid().ToString("N") },
                { "detect", true }
            };
            var headers = new Dictionary<string, string>
            {
                { "x-authorization", "token " + s.CaiyunToken.Trim() },
                { "X-Authorization", "token " + s.CaiyunToken.Trim() }
            };
            var root = await PostJson("https://api.interpreter.caiyunai.com/v1/translator", payload, headers).ConfigureAwait(false);
            var arr = root != null ? root["target"] as object[] : null;
            if (arr == null || arr.Length == 0)
                throw new TranslationException("彩云小译无结果（" + Trunc(root != null ? Str(root["message"]) : "", 80) + "）");
            return Str(arr[0]);
        }

        private static async Task<string> NiutransTranslate(string text, string src, string dst, AppSettings s)
        {
            var payload = new Dictionary<string, object>
            {
                { "from", src == "cht" ? "zh" : src },
                { "to", dst == "cht" ? "zh" : dst },
                { "apikey", s.NiutransKey.Trim() },
                { "src_text", text }
            };
            var root = await PostJson("https://api.niutrans.com/NiuTransServer/translation", payload).ConfigureAwait(false);
            string outText = root != null ? Str(root["tgt_text"]) : "";
            if (outText.Length == 0)
                throw new TranslationException("小牛翻译无结果（" + Trunc(root != null ? Str(root["error_msg"]) : "", 80) + "）");
            return outText;
        }

        private static string Sha256Hex(string s)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                var sb = new StringBuilder();
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static async Task<string> YoudaoZyTranslate(string text, string src, string dst, AppSettings s)
        {
            string appKey = s.YoudaoAppKey.Trim();
            string salt = Guid.NewGuid().ToString("N");
            string curtime = ((long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds).ToString();
            string input = text.Length <= 20 ? text : text.Substring(0, 10) + text.Length + text.Substring(text.Length - 10);
            string sign = Sha256Hex(appKey + input + salt + curtime + s.YoudaoKey.Trim());
            var form = new Dictionary<string, string>
            {
                { "q", text },
                { "from", TranslationEngine.ToYoudaoCode(src) },
                { "to", TranslationEngine.ToYoudaoCode(dst) },
                { "appKey", appKey }, { "salt", salt },
                { "sign", sign }, { "signType", "v3" }, { "curtime", curtime }
            };
            var req = new HttpRequestMessage(HttpMethod.Post, "https://openapi.youdao.com/api");
            req.Content = new FormUrlEncodedContent(form);
            var resp = await HttpUtil.Client.SendAsync(req).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var root = Json.DeserializeObject(body) as Dictionary<string, object>;
            string code = root != null ? Str(root["errorCode"]) : "?";
            if (code != "0") throw new TranslationException("有道智云报错 errorCode=" + code);
            var arr = root != null ? root["translation"] as object[] : null;
            if (arr == null || arr.Length == 0) throw new TranslationException("有道智云无结果");
            return Str(arr[0]);
        }

        // ---------------- 批次 2：云签名链 ----------------

        private static byte[] HmacSha256(byte[] key, string msg)
        {
            using (var h = new HMACSHA256(key)) return h.ComputeHash(Encoding.UTF8.GetBytes(msg));
        }

        private static string HexLower(byte[] bytes)
        {
            var sb = new StringBuilder();
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static async Task<string> TencentTranslate(string text, string src, string dst, AppSettings s)
        {
            string secretId = s.TencentSecretId.Trim(), secretKey = s.TencentSecretKey.Trim();
            string host = "tmt.tencentcloudapi.com";
            string service = "tmt", action = "TextTranslate", version = "2018-03-21", region = "ap-guangzhou";
            string from = src == "cht" ? "zh-TW" : src;
            string to = dst == "cht" ? "zh-TW" : dst;
            var payloadObj = new Dictionary<string, object>
            {
                { "SourceText", text }, { "Source", from }, { "Target", to }, { "ProjectId", 0 }
            };
            string payload = Json.Serialize(payloadObj);
            long ts = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            string date = DateTime.UtcNow.ToString("yyyy-MM-dd");
            string hashedPayload = Sha256Hex(payload);
            string canonical = "POST\n/\n\ncontent-type:application/json; charset=utf-8\nhost:" + host + "\nx-tc-action:" + action.ToLowerInvariant() + "\n\ncontent-type;host;x-tc-action\n" + hashedPayload;
            string scope = date + "/" + service + "/tc3_request";
            string stringToSign = "TC3-HMAC-SHA256\n" + ts + "\n" + scope + "\n" + Sha256Hex(canonical);
            byte[] kDate = HmacSha256(Encoding.UTF8.GetBytes("TC3" + secretKey), date);
            byte[] kService = HmacSha256(kDate, service);
            byte[] kSigning = HmacSha256(kService, "tc3_request");
            string signature = HexLower(HmacSha256(kSigning, stringToSign));
            string auth = "TC3-HMAC-SHA256 Credential=" + secretId + "/" + scope +
                ", SignedHeaders=content-type;host;x-tc-action, Signature=" + signature;
            var req = new HttpRequestMessage(HttpMethod.Post, "https://" + host);
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            req.Headers.TryAddWithoutValidation("Authorization", auth);
            req.Headers.TryAddWithoutValidation("X-TC-Action", action);
            req.Headers.TryAddWithoutValidation("X-TC-Version", version);
            req.Headers.TryAddWithoutValidation("X-TC-Timestamp", ts.ToString());
            req.Headers.TryAddWithoutValidation("X-TC-Region", region);
            var resp = await HttpUtil.Client.SendAsync(req).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var root = Json.DeserializeObject(body) as Dictionary<string, object>;
            var response = root != null ? root["Response"] as Dictionary<string, object> : null;
            if (response != null && response.ContainsKey("Error"))
            {
                var err = response["Error"] as Dictionary<string, object>;
                throw new TranslationException("腾讯翻译君报错 " + (err != null ? Str(err["Code"]) + "：" + Str(err["Message"]) : ""));
            }
            string outText = response != null ? Str(response["TargetText"]) : "";
            if (outText.Length == 0) throw new TranslationException("腾讯翻译君无结果");
            return outText;
        }

        private static string UrlEncodeRfc3986(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            foreach (var b in Encoding.UTF8.GetBytes(s))
            {
                char c = (char)b;
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') ||
                    c == '-' || c == '_' || c == '.' || c == '~')
                    sb.Append(c);
                else
                    sb.Append('%').Append(b.ToString("X2"));
            }
            return sb.ToString();
        }

        private static async Task<string> AliTranslate(string text, string src, string dst, AppSettings s)
        {
            var pars = new SortedDictionary<string, string>
            {
                { "Format", "JSON" }, { "Version", "2018-10-12" },
                { "AccessKeyId", s.AliKeyId.Trim() },
                { "SignatureMethod", "HMAC-SHA1" },
                { "Timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") },
                { "SignatureVersion", "1.0" },
                { "SignatureNonce", Guid.NewGuid().ToString("N") },
                { "Action", "TranslateGeneral" },
                { "SourceLanguage", src == "cht" ? "zh-tw" : src },
                { "TargetLanguage", dst == "cht" ? "zh-tw" : dst },
                { "SourceText", text }, { "FormatType", "text" }, { "Scene", "general" }
            };
            var sb = new StringBuilder();
            foreach (var kv in pars)
            {
                if (sb.Length > 0) sb.Append('&');
                sb.Append(UrlEncodeRfc3986(kv.Key)).Append('=').Append(UrlEncodeRfc3986(kv.Value));
            }
            string stringToSign = "GET&%2F&" + UrlEncodeRfc3986(sb.ToString());
            string signature;
            using (var h = new HMACSHA1(Encoding.UTF8.GetBytes(s.AliKeySecret.Trim() + "&")))
                signature = Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
            string url = "https://mt.cn-hangzhou.aliyuncs.com/?Signature=" + UrlEncodeRfc3986(signature) + "&" + sb;
            var root = await GetJson(url).ConfigureAwait(false);
            var data = root != null ? root["Data"] as Dictionary<string, object> : null;
            string outText = data != null ? Str(data["Translated"]) : "";
            if (outText.Length == 0)
                throw new TranslationException("阿里翻译无结果（" + Trunc(root != null ? Str(root["Message"]) : "", 80) + "）");
            return outText;
        }

        private static async Task<string> VolcTranslate(string text, string src, string dst, AppSettings s)
        {
            string ak = s.VolcKeyId.Trim(), sk = s.VolcKeySecret.Trim();
            string host = "translate.volcengineapi.com";
            string to = dst == "cht" ? "zh-Hant" : dst;
            var payloadObj = new Dictionary<string, object>
            {
                { "TargetLanguage", to }, { "TextList", new[] { text } }
            };
            string payload = Json.Serialize(payloadObj);
            string xDate = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
            string shortDate = xDate.Substring(0, 8);
            string hashedPayload = Sha256Hex(payload);
            string query = "Action=TranslateText&Version=2020-06-01";
            string canonical = "POST\n/\n" + query + "\ncontent-type:application/json\nhost:" + host + "\nx-content-sha256:" + hashedPayload + "\nx-date:" + xDate + "\n\ncontent-type;host;x-content-sha256;x-date\n" + hashedPayload;
            string scope = shortDate + "/cn-north-1/translate/request";
            string stringToSign = "HMAC-SHA256\n" + xDate + "\n" + scope + "\n" + Sha256Hex(canonical);
            byte[] kDate = HmacSha256(Encoding.UTF8.GetBytes(sk), shortDate);
            byte[] kRegion = HmacSha256(kDate, "cn-north-1");
            byte[] kService = HmacSha256(kRegion, "translate");
            byte[] kSigning = HmacSha256(kService, "request");
            string signature = HexLower(HmacSha256(kSigning, stringToSign));
            string auth = "HMAC-SHA256 Credential=" + ak + "/" + scope +
                ", SignedHeaders=content-type;host;x-content-sha256;x-date, Signature=" + signature;
            var req = new HttpRequestMessage(HttpMethod.Post, "https://" + host + "/?" + query);
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            req.Headers.TryAddWithoutValidation("Authorization", auth);
            req.Headers.TryAddWithoutValidation("X-Date", xDate);
            req.Headers.TryAddWithoutValidation("X-Content-Sha256", hashedPayload);
            var resp = await HttpUtil.Client.SendAsync(req).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var root = Json.DeserializeObject(body) as Dictionary<string, object>;
            if (root != null && root.ContainsKey("ResponseMetadata"))
            {
                var meta = root["ResponseMetadata"] as Dictionary<string, object>;
                if (meta != null && meta.ContainsKey("Error"))
                {
                    var err = meta["Error"] as Dictionary<string, object>;
                    throw new TranslationException("火山翻译报错 " + (err != null ? Str(err["Code"]) + "：" + Str(err["Message"]) : ""));
                }
            }
            var arr = root != null ? root["TranslationList"] as object[] : null;
            if (arr == null || arr.Length == 0) throw new TranslationException("火山翻译无结果");
            var t0 = arr[0] as Dictionary<string, object>;
            return t0 != null ? Str(t0["Translation"]) : "";
        }
    }

    /// <summary>
    /// v1.8.0 引擎目录：胶囊条的选项构成与当前选中引擎的统一解析（MainForm/ResultForm 共用）。
    /// 基础 6 个恒在；其余按 MultiEngine.IsConfigured 决定（免费的默认在，填了密钥/改了实例地址的才出现）。
    /// 选中值存 AppSettings.EnginePrefer（prefer id）；旧 EngineIndex 仅作老配置的回退映射。
    /// </summary>
    public static class EngineCatalog
    {
        public class Option
        {
            public string Prefer;
            public string Label;
        }

        private static readonly Option[] BaseOptions =
        {
            new Option { Prefer = "auto", Label = "自动" },
            new Option { Prefer = "youdao", Label = "有道" },
            new Option { Prefer = "bing", Label = "必应" },
            new Option { Prefer = "glm", Label = "AI" },
            new Option { Prefer = "gtx", Label = "谷歌" },
            new Option { Prefer = "mymemory", Label = "MyMemory" },
        };

        public static List<Option> BuildOptions(AppSettings s)
        {
            var list = new List<Option>(BaseOptions);
            if (s != null)
                foreach (var p in MultiEngine.All)
                    if (MultiEngine.IsConfigured(p.Id, s))
                        list.Add(new Option { Prefer = p.Id, Label = p.Capsule });
            return list;
        }

        public static bool IsKnownPrefer(string prefer)
        {
            if (string.IsNullOrEmpty(prefer)) return false;
            foreach (var o in BaseOptions) if (o.Prefer == prefer) return true;
            return MultiEngine.InfoOf(prefer) != null;
        }

        /// <summary>当前生效的 prefer：EnginePrefer 有效则用之，否则按旧 EngineIndex 映射。</summary>
        public static string ResolvePrefer(AppSettings s)
        {
            if (s == null) return "auto";
            if (IsKnownPrefer(s.EnginePrefer))
            {
                // 密钥被删掉后该引擎已不在目录里：回落自动，不让失效选择卡住翻译
                var info = MultiEngine.InfoOf(s.EnginePrefer);
                if (info == null || MultiEngine.IsConfigured(s.EnginePrefer, s)) return s.EnginePrefer;
                return "auto";
            }
            switch (s.EngineIndex)
            {
                case 1: return "youdao";
                case 2: return "bing";
                case 3: return "glm";
                case 4: return "gtx";
                case 5: return "mymemory";
                default: return "auto";
            }
        }

        /// <summary>prefer 在给定选项表中的下标（找不到回 0=自动）。</summary>
        public static int IndexOfPrefer(List<Option> options, string prefer)
        {
            for (int i = 0; i < options.Count; i++)
                if (options[i].Prefer == prefer) return i;
            return 0;
        }

        /// <summary>交叉核对的第二引擎：与主引擎不同家、免密钥优先（不消耗用户付费额度）。
        /// 顺序：有道→必应→谷歌→MyMemory→Edge→Reverso→Apertium（主引擎是谁就跳过谁）。找不到回 null。</summary>
        public static string PickSecondEngine(string primaryPrefer, AppSettings s)
        {
            var candidates = new[] { "youdao", "bing", "gtx", "mymemory", "edge", "reverso", "apertium" };
            foreach (var c in candidates)
            {
                if (c == primaryPrefer) continue;
                var info = MultiEngine.InfoOf(c);
                if (info != null && !MultiEngine.IsConfigured(c, s)) continue;
                return c;
            }
            return null;
        }
    }
}

