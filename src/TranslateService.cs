using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace WinTranslator
{
    public class TranslationException : Exception
    {
        public TranslationException(string msg) : base(msg) { }
    }

    public class TranslationResult
    {
        public string Text;
        public string Provider;
        public string SrcLang;
        public string DstLang;
    }

    internal static class HttpUtil
    {
        internal static readonly HttpClient Client = Create();

        private static HttpClient Create()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                CookieContainer = new CookieContainer()
            };
            var c = new HttpClient(handler);
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            return c;
        }

        internal static string Md5Hex(string s)
        {
            using (var md5 = MD5.Create())
            {
                var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(s));
                var sb = new StringBuilder();
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        internal static byte[] Md5Raw(string s)
        {
            using (var md5 = MD5.Create())
                return md5.ComputeHash(Encoding.UTF8.GetBytes(s));
        }

        internal static string UnixMs()
        {
            return ((long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds).ToString();
        }
    }

    /// <summary>
    /// 有道网页翻译 V2（免费，无需 Key）：/webtranslate/key 取 secretKey 与 AES 密钥对，
    /// MD5 签名后 POST /webtranslate，响应为 URL-safe Base64 的 AES-128-CBC 密文。
    /// 常量 keyid 与预置 key 取自 fanyi.youdao.com 当前线上 app.js（2026-10-29 实测全链路通）。
    /// 风控要点（2026-11-01 v1.5.1 测试期实测定位）：请求须携带 fanyi.youdao.com 首页下发的
    /// OUTFOX_SEARCH_USER_ID cookie，否则 /webtranslate 对多数文本回 code 50（少数高频短语偶能通过，易误判为"时好时坏"）；
    /// 故首次取 key 前先访问一次首页把 cookie 种进共享 CookieContainer（与浏览器流程一致）。
    /// </summary>
    public class YoudaoClient
    {
        private const string KeyId = "webfanyi-key-getter-2025";
        private const string PresetKey = "yU5nT5dK3eZ1pI4j";
        private string _secretKey, _aesKey, _aesIv;
        private DateTime _fetchedAt = DateTime.MinValue;
        private bool _cookieWarmed;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        /// <summary>访问一次翻译首页，让服务端下发 OUTFOX_SEARCH_USER_ID cookie（缺它翻译接口回 code 50）。</summary>
        private async Task EnsureCookie()
        {
            if (_cookieWarmed) return;
            _cookieWarmed = true;
            try { await HttpUtil.Client.GetStringAsync("https://fanyi.youdao.com/").ConfigureAwait(false); }
            catch { /* 种 cookie 失败不阻断后续流程，接口层还有必应回退 */ }
        }

        private async Task EnsureKeys()
        {
            if (_secretKey != null && (DateTime.Now - _fetchedAt).TotalMinutes < 20) return;
            await EnsureCookie().ConfigureAwait(false);
            string ts = HttpUtil.UnixMs();
            string sign = HttpUtil.Md5Hex("client=fanyideskweb&mysticTime=" + ts + "&product=webfanyi&key=" + PresetKey);
            string url = "https://dict.youdao.com/webtranslate/key?keyid=" + KeyId +
                "&client=fanyideskweb&mysticTime=" + ts + "&product=webfanyi&sign=" + sign +
                "&pointParam=client,mysticTime,product";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Referer", "https://fanyi.youdao.com/");
            var resp = await HttpUtil.Client.SendAsync(req).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var root = _json.DeserializeObject(body) as Dictionary<string, object>;
            if (root == null || Convert.ToInt32(root["code"]) != 0)
                throw new TranslationException("有道密钥获取失败");
            var data = root["data"] as Dictionary<string, object>;
            _secretKey = (string)data["secretKey"];
            _aesKey = (string)data["aesKey"];
            _aesIv = (string)data["aesIv"];
            _fetchedAt = DateTime.Now;
        }

        /// <summary>from/to 为有道代码（auto/zh-CHS/zh-CHT/en/fr/ja/de/ko/pt/es/ru）。</summary>
        public async Task<string> Translate(string text, string from, string to)
        {
            await EnsureKeys().ConfigureAwait(false);
            string ts = HttpUtil.UnixMs();
            string sign = HttpUtil.Md5Hex("client=fanyideskweb&mysticTime=" + ts + "&product=webfanyi&key=" + _secretKey);
            var form = new Dictionary<string, string>
            {
                { "i", text }, { "from", from }, { "to", to }, { "dictResult", "true" },
                { "keyid", "webfanyi" }, { "sign", sign }, { "client", "fanyideskweb" },
                { "product", "webfanyi" }, { "appVersion", "1.0.0" }, { "vendor", "web" },
                { "pointParam", "client,mysticTime,product" }, { "mysticTime", ts }, { "keyfrom", "fanyi.web" }
            };
            var req = new HttpRequestMessage(HttpMethod.Post, "https://dict.youdao.com/webtranslate");
            req.Headers.TryAddWithoutValidation("Referer", "https://fanyi.youdao.com/");
            req.Content = new FormUrlEncodedContent(form);
            string raw = await (await HttpUtil.Client.SendAsync(req).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);
            string plain = Decrypt(raw.Trim());
            var root = _json.DeserializeObject(plain) as Dictionary<string, object>;
            if (root == null || Convert.ToInt32(root["code"]) != 0)
                throw new TranslationException("有道翻译失败（code " + (root == null ? "?" : root["code"].ToString()) + "）");
            var sb = new StringBuilder();
            foreach (object rowObj in (object[])root["translateResult"])
            {
                if (sb.Length > 0) sb.Append('\n');
                foreach (object segObj in (object[])rowObj)
                {
                    var seg = segObj as Dictionary<string, object>;
                    if (seg != null && seg.ContainsKey("tgt")) sb.Append((string)seg["tgt"]);
                }
            }
            return sb.ToString();
        }

        private string Decrypt(string text)
        {
            string b64 = text.Replace('-', '+').Replace('_', '/');
            switch (b64.Length % 4) { case 2: b64 += "=="; break; case 3: b64 += "="; break; }
            byte[] cipher = Convert.FromBase64String(b64);
            using (var aes = Aes.Create())
            {
                aes.Key = HttpUtil.Md5Raw(_aesKey);
                aes.IV = HttpUtil.Md5Raw(_aesIv);
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using (var dec = aes.CreateDecryptor())
                {
                    byte[] plain = dec.TransformFinalBlock(cipher, 0, cipher.Length);
                    return Encoding.UTF8.GetString(plain);
                }
            }
        }
    }

    /// <summary>
    /// 必应网页翻译（免费，无需 Key）：bing.com/Translator 页取 IG 与 AbusePreventionHelper 的 key/token，
    /// POST ttranslatev3。IID 用 translator.5028.1 构造式（2026-10-29 实测页内已无 IID 字段）。
    /// </summary>
    public class BingClient
    {
        private string _ig, _key, _token;
        private DateTime _fetchedAt = DateTime.MinValue;

        private async Task EnsureParams()
        {
            if (_ig != null && (DateTime.Now - _fetchedAt).TotalMinutes < 8) return;
            string html = await HttpUtil.Client.GetStringAsync("https://www.bing.com/Translator").ConfigureAwait(false);
            var mIg = Regex.Match(html, "IG:\"([^\"]+)\"");
            var mAb = Regex.Match(html, @"params_AbusePreventionHelper = \[(\d+),""([^""]+)""");
            if (!mIg.Success || !mAb.Success) throw new TranslationException("必应参数获取失败");
            _ig = mIg.Groups[1].Value;
            _key = mAb.Groups[1].Value;
            _token = mAb.Groups[2].Value;
            _fetchedAt = DateTime.Now;
        }

        /// <summary>from/to 为必应代码（auto-detect/zh-Hans/zh-Hant/en/fr/ja/de/ko/pt/es/ru）。</summary>
        public async Task<string> Translate(string text, string from, string to)
        {
            await EnsureParams().ConfigureAwait(false);
            string url = "https://www.bing.com/ttranslatev3?isVertical=1&IG=" + _ig + "&IID=translator.5028.1";
            var form = new Dictionary<string, string>
            {
                { "fromLang", from }, { "to", to }, { "text", text },
                { "token", _token }, { "key", _key }
            };
            var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.TryAddWithoutValidation("Referer", "https://www.bing.com/Translator");
            req.Content = new FormUrlEncodedContent(form);
            string body = await (await HttpUtil.Client.SendAsync(req).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);
            var ser = new JavaScriptSerializer();
            var arr = ser.DeserializeObject(body) as object[];
            if (arr == null || arr.Length == 0) throw new TranslationException("必应翻译失败（空响应）");
            var first = arr[0] as Dictionary<string, object>;
            var translations = first != null && first.ContainsKey("translations") ? first["translations"] as object[] : null;
            if (translations == null || translations.Length == 0) throw new TranslationException("必应翻译失败（无译文）");
            var t0 = translations[0] as Dictionary<string, object>;
            return (string)t0["text"];
        }
    }

    /// <summary>
    /// 智谱 GLM 免费大模型翻译（v1.1.1 事项七，用户点选）：GLM-4-Flash，官方免费档，需用户自备 API Key。
    /// 接口 POST open.bigmodel.cn/api/paas/v4/chat/completions（Bearer 鉴权，OpenAI 兼容格式），
    /// 系统提示约束为专业翻译、只输出译文。
    /// </summary>
    public class GlmClient
    {
        public string ApiKey = "";
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        public async Task<string> Translate(string text, string srcCode, string dstCode)
        {
            string sys = "You are a professional translator. Translate the user's text from " +
                         TranslationEngine.LangNameOf(srcCode) + " to " + TranslationEngine.LangNameOf(dstCode) +
                         ". Output only the translation itself, without any explanation, quotes or extra text.";
            return await ChatRaw(sys, text).ConfigureAwait(false);
        }

        /// <summary>v1.2.0：通用对话调用（翻译与例句生成共用）。</summary>
        public async Task<string> ChatRaw(string sys, string user)
        {
            if (string.IsNullOrWhiteSpace(ApiKey))
                throw new TranslationException("未填写智谱 GLM 的 API Key（设置中填写）");
            string text = user;
            var body = new Dictionary<string, object>
            {
                { "model", "glm-4-flash" },
                { "temperature", 0.2 },
                { "messages", new object[]
                    {
                        new Dictionary<string, string> { { "role", "system" }, { "content", sys } },
                        new Dictionary<string, string> { { "role", "user" }, { "content", text } }
                    }
                }
            };
            var req = new HttpRequestMessage(HttpMethod.Post, "https://open.bigmodel.cn/api/paas/v4/chat/completions");
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + ApiKey.Trim());
            req.Content = new StringContent(_json.Serialize(body), Encoding.UTF8, "application/json");
            string raw = await (await HttpUtil.Client.SendAsync(req).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);
            var root = _json.DeserializeObject(raw) as Dictionary<string, object>;
            if (root == null) throw new TranslationException("智谱 GLM 响应异常");
            if (root.ContainsKey("error"))
            {
                var err = root["error"] as Dictionary<string, object>;
                string msg = err != null && err.ContainsKey("message") ? (string)err["message"] : "未知错误";
                throw new TranslationException("智谱 GLM 调用失败：" + msg);
            }
            var choices = root.ContainsKey("choices") ? root["choices"] as object[] : null;
            if (choices == null || choices.Length == 0) throw new TranslationException("智谱 GLM 无译文");
            var c0 = choices[0] as Dictionary<string, object>;
            var message = c0 != null && c0.ContainsKey("message") ? c0["message"] as Dictionary<string, object> : null;
            string content = message != null && message.ContainsKey("content") ? (string)message["content"] : null;
            if (string.IsNullOrWhiteSpace(content)) throw new TranslationException("智谱 GLM 译文为空");
            return content.Trim();
        }

        /// <summary>设置页"测试连接"用：翻一句短文本验证 Key。</summary>
        public Task<string> TestConnection() { return Translate("Hello", "en", "zh"); }
    }

    /// <summary>多引擎调度：语言探测、代码映射、分段、失败切换（有道/必应/智谱 GLM）。</summary>

    /// <summary>
    /// 谷歌 gtx 免费网页接口（v1.5.1 事项六，用户点选加入）：translate.googleapis.com/translate_a/single?client=gtx，
    /// 免 Key、多语种；非官方接口，谷歌可随时调整，且谷歌域名在国内网络通常直连不通——真机可用性以用户网络为准。
    /// 响应为嵌套数组：[0] 是分段数组，每段 [0] 为译文片段，拼接即全文。
    /// </summary>
    public class GtxClient
    {
        public async Task<string> Translate(string text, string from, string to)
        {
            string url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=" + Uri.EscapeDataString(from)
                + "&tl=" + Uri.EscapeDataString(to) + "&dt=t&q=" + Uri.EscapeDataString(text);
            string body = await HttpUtil.Client.GetStringAsync(url).ConfigureAwait(false);
            return ParseResponse(body);
        }

        internal static string ParseResponse(string body)
        {
            if (string.IsNullOrEmpty(body) || !body.TrimStart().StartsWith("["))
                throw new TranslationException("谷歌接口无有效响应（网络不通或被拦截）");
            var ser = new JavaScriptSerializer();
            var root = ser.DeserializeObject(body) as object[];
            var segs = root != null && root.Length > 0 ? root[0] as object[] : null;
            if (segs == null) throw new TranslationException("谷歌翻译失败（空响应）");
            var sb = new StringBuilder();
            foreach (var segObj in segs)
            {
                var seg = segObj as object[];
                if (seg != null && seg.Length > 0 && seg[0] is string) sb.Append((string)seg[0]);
            }
            if (sb.Length == 0) throw new TranslationException("谷歌翻译失败（无译文）");
            return sb.ToString();
        }
    }

    /// <summary>
    /// MyMemory 免费翻译接口（v1.5.1 事项六，用户点选加入）：api.mymemory.translated.net/get，免 Key、匿名可用，
    /// 单次 q 上限 500 字符（引擎层按 450 分段），匿名每日有额度（官方口径约 1000 词/天）。
    /// 响应 JSON：responseData.translatedText 为译文，quotaFinished=true 表示当日额度用完。
    /// </summary>
    public class MyMemoryClient
    {
        public async Task<string> Translate(string text, string from, string to)
        {
            string url = "https://api.mymemory.translated.net/get?q=" + Uri.EscapeDataString(text)
                + "&langpair=" + Uri.EscapeDataString(from) + "%7C" + Uri.EscapeDataString(to);
            string body = await HttpUtil.Client.GetStringAsync(url).ConfigureAwait(false);
            return ParseResponse(body);
        }

        internal static string ParseResponse(string body)
        {
            var ser = new JavaScriptSerializer();
            var root = ser.DeserializeObject(body) as Dictionary<string, object>;
            if (root == null) throw new TranslationException("MyMemory 翻译失败（空响应）");
            if (root.ContainsKey("quotaFinished") && root["quotaFinished"] is bool && (bool)root["quotaFinished"])
                throw new TranslationException("MyMemory 今日额度已用完");
            var data = root.ContainsKey("responseData") ? root["responseData"] as Dictionary<string, object> : null;
            var text = data != null && data.ContainsKey("translatedText") ? data["translatedText"] as string : null;
            if (string.IsNullOrEmpty(text)) throw new TranslationException("MyMemory 翻译失败（无译文）");
            if (text.StartsWith("QUERY LENGTH LIMIT") || text.StartsWith("INVALID"))
                throw new TranslationException("MyMemory 拒绝请求：" + text);
            return text;
        }
    }

    public class TranslationEngine
    {
        private readonly YoudaoClient _youdao = new YoudaoClient();
        private readonly BingClient _bing = new BingClient();
        private readonly GlmClient _glm = new GlmClient();
        private readonly GtxClient _gtx = new GtxClient();
        private readonly MyMemoryClient _mymemory = new MyMemoryClient();
        public string ActiveProvider { get; private set; }

        /// <summary>智谱 GLM 的 Key（由 MainForm 从设置同步进来）。</summary>
        public string GlmApiKey { get { return _glm.ApiKey; } set { _glm.ApiKey = value ?? ""; } }
        public Task<string> TestGlmConnection() { return _glm.TestConnection(); }

        /// <summary>v1.8.0：多引擎（MultiEngine）所需的完整设置（密钥等），由 MainForm 同步进来。</summary>
        public AppSettings ExtraSettings { get; set; }

        // 应用层语言代码（10 个，已实测双引擎全通）
        public static readonly string[] LangCodes = { "zh", "cht", "en", "fr", "ja", "de", "ko", "pt", "es", "ru" };

        public static string LangNameOf(string code)
        {
            switch (code)
            {
                case "zh": return "中文简体";
                case "cht": return "中文繁体";
                case "en": return "英语";
                case "fr": return "法语";
                case "ja": return "日语";
                case "de": return "德语";
                case "ko": return "韩语";
                case "pt": return "葡萄牙语";
                case "es": return "西班牙语";
                case "ru": return "俄语";
                default: return code ?? "";
            }
        }

        public static string ToYoudaoCode(string code)
        {
            if (code == "zh") return "zh-CHS";
            if (code == "cht") return "zh-CHT";
            return code;
        }

        public static string ToBingCode(string code)
        {
            if (code == "zh") return "zh-Hans";
            if (code == "cht") return "zh-Hant";
            return code;
        }

        /// <summary>gtx/MyMemory 共用：zh→zh-CN、cht→zh-TW，其余与应用层代码一致。</summary>
        public static string ToGoogleCode(string code)
        {
            if (code == "zh") return "zh-CN";
            if (code == "cht") return "zh-TW";
            return code;
        }

        private static readonly Regex Cjk = new Regex("[一-鿿]");
        // v1.7.3：Kana/Hangul/Cyrillic 三个正则已删——DetectSource 改为逐字计数占比判定，不再用它们；
        // Cjk 保留给 ContainsChinese 用。

        public static bool ContainsChinese(string text) { return text != null && Cjk.IsMatch(text); }

        public static string DetectSource(string text)
        {
            if (string.IsNullOrEmpty(text)) return "en";
            // v1.7.3 按字数占比判定（旧实现「含 1 个汉字即判中文」是真机事故根源：英文推文截图里
            // 界面自带的「11小时」几个汉字，就把整图判成中文→目标又自动翻回英文→等于没翻）。
            // 汉字一字一义、信息密度高，按 3 倍计分；韩文音节按 2 倍；假名是日语决定性标记（汉字日中共用）优先判。
            int kana = 0, hangul = 0, cyrillic = 0, cjk = 0, latin = 0;
            foreach (char c in text)
            {
                if ((c >= '぀' && c <= 'ヿ')) kana++;
                else if ((c >= '가' && c <= '힯')) hangul++;
                else if ((c >= 'Ѐ' && c <= 'ӿ')) cyrillic++;
                else if ((c >= '一' && c <= '鿿')) cjk++;
                else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= 'À' && c <= 'ÿ')) latin++;
            }
            if (kana > 0) return "ja";
            int zhScore = cjk * 3, koScore = hangul * 2;
            int best = Math.Max(Math.Max(koScore, cyrillic), Math.Max(zhScore, latin));
            if (best == 0) return "en";
            if (best == koScore) return "ko";
            if (best == cyrillic) return "ru";
            if (best == zhScore) return "zh";
            return "en";
        }

        public static string DetectTarget(string text)
        {
            return DetectSource(text) == "zh" ? "en" : "zh";
        }

        private static List<string> Chunk(string text, int maxLen)
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

        /// <summary>src/dst：auto 或 LangCodes 之一；prefer：auto/youdao/bing/glm/gtx/mymemory（指定引擎失败自动回退有道/必应）。</summary>
        public async Task<TranslationResult> Translate(string text, string src = "auto", string dst = "auto", string prefer = "auto")
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) throw new TranslationException("翻译文本为空");
            string realSrc = src == "auto" ? DetectSource(text) : src;
            string realDst = dst == "auto" ? (realSrc == "zh" || realSrc == "cht" ? "en" : "zh") : dst;
            if (realDst == realSrc) realDst = realSrc == "en" ? "zh" : "en";

            // v1.8.0 新增引擎：手动指定时直连 MultiEngine，失败明确报错、不回退（方案拍板③）
            var extraInfo = MultiEngine.InfoOf(prefer);
            if (extraInfo != null)
            {
                if (ExtraSettings == null)
                    throw new TranslationException(extraInfo.Name + " 未就绪（设置未同步）");
                string extraOut = await MultiEngine.Translate(prefer, text, realSrc, realDst, ExtraSettings).ConfigureAwait(false);
                ActiveProvider = prefer;
                return new TranslationResult { Text = extraOut, Provider = extraInfo.Name, SrcLang = realSrc, DstLang = realDst };
            }

            var providers = new List<string>();
            if (prefer == "glm") { providers.Add("glm"); providers.Add("youdao"); providers.Add("bing"); }
            else if (prefer == "gtx") { providers.Add("gtx"); providers.Add("youdao"); providers.Add("bing"); }
            else if (prefer == "mymemory") { providers.Add("mymemory"); providers.Add("youdao"); providers.Add("bing"); }
            else
            {
                providers.Add("youdao"); providers.Add("bing");
                if (prefer == "bing" || (prefer == "auto" && ActiveProvider == "bing")) providers.Reverse();
            }

            Exception last = null;
            foreach (var provider in providers)
            {
                try
                {
                    string output;
                    if (provider == "glm")
                    {
                        var parts = new List<string>();
                        foreach (var chunk in Chunk(text, 1500))
                            parts.Add(await _glm.Translate(chunk, realSrc, realDst).ConfigureAwait(false));
                        output = string.Join("\n", parts);
                    }
                    else if (provider == "gtx")
                    {
                        string from = src == "auto" ? "auto" : ToGoogleCode(realSrc);
                        string to = ToGoogleCode(realDst);
                        var parts = new List<string>();
                        foreach (var chunk in Chunk(text, 1500))
                            parts.Add(await _gtx.Translate(chunk, from, to).ConfigureAwait(false));
                        output = string.Join("\n", parts);
                    }
                    else if (provider == "mymemory")
                    {
                        // MyMemory 不支持 auto 源语言，用已检测出的 realSrc；单次上限 500 字符，按 450 分段
                        string from = ToGoogleCode(realSrc);
                        string to = ToGoogleCode(realDst);
                        var parts = new List<string>();
                        foreach (var chunk in Chunk(text, 450))
                            parts.Add(await _mymemory.Translate(chunk, from, to).ConfigureAwait(false));
                        output = string.Join("\n", parts);
                    }
                    else if (provider == "youdao")
                    {
                        string from = src == "auto" ? "auto" : ToYoudaoCode(realSrc);
                        string to = ToYoudaoCode(realDst);
                        var parts = new List<string>();
                        foreach (var chunk in Chunk(text, 1800))
                            parts.Add(await _youdao.Translate(chunk, from, to).ConfigureAwait(false));
                        output = string.Join("\n", parts);
                    }
                    else
                    {
                        string from = src == "auto" ? "auto-detect" : ToBingCode(realSrc);
                        string to = ToBingCode(realDst);
                        var parts = new List<string>();
                        foreach (var chunk in Chunk(text, 900))
                            parts.Add(await _bing.Translate(chunk, from, to).ConfigureAwait(false));
                        output = string.Join("\n", parts);
                    }
                    ActiveProvider = provider;
                    return new TranslationResult
                    {
                        Text = output,
                        Provider = provider == "youdao" ? "有道" : (provider == "bing" ? "必应" : (provider == "gtx" ? "谷歌" : (provider == "mymemory" ? "MyMemory" : "智谱 GLM"))),
                        SrcLang = realSrc,
                        DstLang = realDst
                    };
                }
                catch (Exception ex) { last = ex; }
            }
            throw new TranslationException("翻译失败：" + (last != null ? last.Message : "未知错误"));
        }
    }
}
