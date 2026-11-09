using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace WinTranslator
{
    /// <summary>
    /// v1.5.3 云同步（用户要求"云账户"）：无自有服务器，落为绑定用户自己的云存储——
    /// GitHub 私有仓库或 WebDAV 网盘（二选一启用）。同步单词本/收藏、历史与软件设置
    /// （GLM Key 与同步凭据本身不上传）。口径：启动拉取、变更自动上传、整库按时间新者胜，
    /// 覆盖前本地自动备份。凭据用 DPAPI（Data Protection API，数据保护接口）加密存本机。
    /// </summary>
    public class SyncBundle
    {
        public string App { get; set; }
        public int Version { get; set; }
        public long UpdatedAt { get; set; }
        public string Hash { get; set; }
        public AppSettings Settings { get; set; }
        public List<Entry> Favorites { get; set; }
        public List<Entry> History { get; set; }
    }

    public class SyncResult
    {
        public string Status = "";
        public bool Applied;
        public bool Uploaded;
        public string Error;
    }

    public static class SyncService
    {
        public static string GithubApiBase = "https://api.github.com";
        private const string FileName = "wintranslator-data.json";
        private static readonly JavaScriptSerializer Ser = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };

        // ---------------- 凭据保护 ----------------

        public static string Protect(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            try
            {
                var b = ProtectedData.Protect(Encoding.UTF8.GetBytes(s), null, DataProtectionScope.CurrentUser);
                return "dpapi:" + Convert.ToBase64String(b);
            }
            catch { return "raw:" + s; }
        }

        public static string Unprotect(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            try
            {
                if (s.StartsWith("dpapi:", StringComparison.Ordinal))
                    return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s.Substring(6)), null, DataProtectionScope.CurrentUser));
                if (s.StartsWith("raw:", StringComparison.Ordinal)) return s.Substring(4);
            }
            catch { }
            return "";
        }

        /// <summary>v1.8.0：给引擎/词典用的有效设置副本——凭据字段从 DPAPI 密文解回明文（原设置对象不动）。</summary>
        public static AppSettings EffectiveCreds(AppSettings s)
        {
            if (s == null) return null;
            var copy = Ser.Deserialize<AppSettings>(Ser.Serialize(s));
            copy.BaiduAppId = Unprotect(copy.BaiduAppId);
            copy.BaiduKey = Unprotect(copy.BaiduKey);
            copy.DeepLKey = Unprotect(copy.DeepLKey);
            copy.DeeplxToken = Unprotect(copy.DeeplxToken);
            copy.CaiyunToken = Unprotect(copy.CaiyunToken);
            copy.NiutransKey = Unprotect(copy.NiutransKey);
            copy.YoudaoAppKey = Unprotect(copy.YoudaoAppKey);
            copy.YoudaoKey = Unprotect(copy.YoudaoKey);
            copy.AliKeyId = Unprotect(copy.AliKeyId);
            copy.AliKeySecret = Unprotect(copy.AliKeySecret);
            copy.TencentSecretId = Unprotect(copy.TencentSecretId);
            copy.TencentSecretKey = Unprotect(copy.TencentSecretKey);
            copy.VolcKeyId = Unprotect(copy.VolcKeyId);
            copy.VolcKeySecret = Unprotect(copy.VolcKeySecret);
            copy.WebsterKey = Unprotect(copy.WebsterKey);
            return copy;
        }

        // ---------------- 数据包 ----------------

        private static readonly string[] SettingsExclude =
        {
            "GlmApiKey", "GithubToken", "WebdavPass",
            "BaiduAppId", "BaiduKey", "DeepLKey", "DeeplxUrl", "DeeplxToken", "CaiyunToken", "NiutransKey",
            "YoudaoAppKey", "YoudaoKey", "AliKeyId", "AliKeySecret", "TencentSecretId", "TencentSecretKey",
            "VolcKeyId", "VolcKeySecret", "WebsterKey",
            "SyncEnabled", "SyncMethod", "GithubRepo", "WebdavUrl", "WebdavUser", "SyncUpdatedAt", "SyncHash",
            "BallX", "BallY", "MiniX", "MiniY", "DeskBarX", "DeskBarY"
        };

        public static AppSettings SanitizeSettings(AppSettings s)
        {
            var copy = Ser.Deserialize<AppSettings>(Ser.Serialize(s));
            foreach (var name in SettingsExclude)
            {
                var p = typeof(AppSettings).GetProperty(name);
                if (p != null && p.CanWrite) p.SetValue(copy, p.PropertyType == typeof(string) ? (object)"" : Activator.CreateInstance(p.PropertyType), null);
            }
            return copy;
        }

        public static string ComputeHash(Store store)
        {
            string raw = Ser.Serialize(store.Favorites) + "|" + Ser.Serialize(store.History) + "|" + Ser.Serialize(SanitizeSettings(store.Settings));
            using (var sha = SHA256.Create())
            {
                var b = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                var sb = new StringBuilder();
                foreach (var x in b) sb.Append(x.ToString("x2"));
                return sb.ToString();
            }
        }

        public static SyncBundle BuildBundle(Store store, long updatedAt)
        {
            return new SyncBundle
            {
                App = "WinTranslator",
                Version = 1,
                UpdatedAt = updatedAt,
                Hash = ComputeHash(store),
                Settings = SanitizeSettings(store.Settings),
                Favorites = store.Favorites,
                History = store.History
            };
        }

        public static SyncBundle ParseBundle(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var b = Ser.Deserialize<SyncBundle>(json);
            if (b == null || b.App != "WinTranslator") return null;
            if (b.Favorites == null) b.Favorites = new List<Entry>();
            if (b.History == null) b.History = new List<Entry>();
            return b;
        }

        private static void BackupLocal(Store store)
        {
            try
            {
                string dir = Path.Combine(store.DataDir, "backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(dir);
                foreach (var f in new[] { "settings.json", "favorites.json", "history.json" })
                {
                    string src = Path.Combine(store.DataDir, f);
                    if (File.Exists(src)) File.Copy(src, Path.Combine(dir, f), true);
                }
            }
            catch { }
        }

        // ---------------- 同步主流程 ----------------

        public static bool IsConfigured(AppSettings s)
        {
            if (s == null || !s.SyncEnabled) return false;
            if (s.SyncMethod == 0) return !string.IsNullOrEmpty(Unprotect(s.GithubToken));
            return !string.IsNullOrEmpty(s.WebdavUrl) && !string.IsNullOrEmpty(s.WebdavUser) && !string.IsNullOrEmpty(Unprotect(s.WebdavPass));
        }

        public static async Task<SyncResult> SyncNow(Store store)
        {
            var r = new SyncResult();
            var s = store.Settings;
            if (!s.SyncEnabled) { r.Status = "未启用云同步"; return r; }
            try
            {
                ISyncTransport t = s.SyncMethod == 0
                    ? (ISyncTransport)new GithubTransport(Unprotect(s.GithubToken), string.IsNullOrEmpty(s.GithubRepo) ? "wintranslator-data" : s.GithubRepo)
                    : new WebDavTransport(s.WebdavUrl, s.WebdavUser, Unprotect(s.WebdavPass));
                string remoteJson = await t.Download();
                string localHash = ComputeHash(store);
                if (!string.IsNullOrEmpty(remoteJson))
                {
                    var rb = ParseBundle(remoteJson);
                    if (rb != null && rb.Hash != localHash && rb.UpdatedAt > s.SyncUpdatedAt)
                    {
                        BackupLocal(store);
                        store.ReplaceAllData(rb.Favorites, rb.History, rb.Settings);
                        s.SyncUpdatedAt = rb.UpdatedAt;
                        s.SyncHash = rb.Hash;
                        store.SaveSettings();
                        r.Applied = true;
                        r.Status = "已下载云端资料（单词本 " + rb.Favorites.Count + " 条、历史 " + rb.History.Count + " 条）";
                        return r;
                    }
                }
                if (localHash != s.SyncHash)
                {
                    long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    var bundle = BuildBundle(store, now);
                    await t.Upload(Ser.Serialize(bundle));
                    s.SyncUpdatedAt = now;
                    s.SyncHash = localHash;
                    store.SaveSettings();
                    r.Uploaded = true;
                    r.Status = "已上传最新资料到云端";
                    return r;
                }
                r.Status = "已是最新，无需同步";
                return r;
            }
            catch (Exception ex)
            {
                r.Error = ex.Message;
                r.Status = "同步失败：" + ex.Message;
                return r;
            }
        }

        // ---------------- 传输层 ----------------

        private interface ISyncTransport
        {
            Task<string> Download();   // 不存在返回 null
            Task Upload(string content);
        }

        private static HttpClient NewClient()
        {
            var c = new HttpClient();
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("WinTranslator");
            return c;
        }

        private class GithubTransport : ISyncTransport
        {
            private readonly string _token, _repo;
            private string _login, _sha;

            public GithubTransport(string token, string repo) { _token = token; _repo = repo; }

            private HttpClient Client()
            {
                var c = NewClient();
                c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                return c;
            }

            private static Dictionary<string, object> Json(string s)
            {
                return Ser.Deserialize<Dictionary<string, object>>(s);
            }

            private async Task EnsureRepo()
            {
                if (_login != null) return;
                using (var c = Client())
                {
                    var me = await c.GetStringAsync(GithubApiBase + "/user");
                    _login = (string)Json(me)["login"];
                    var resp = await c.GetAsync(GithubApiBase + "/repos/" + _login + "/" + _repo);
                    if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        var body = Ser.Serialize(new Dictionary<string, object> { { "name", _repo }, { "private", true } });
                        var create = await c.PostAsync(GithubApiBase + "/user/repos", new StringContent(body, Encoding.UTF8, "application/json"));
                        create.EnsureSuccessStatusCode();
                    }
                    else resp.EnsureSuccessStatusCode();
                }
            }

            public async Task<string> Download()
            {
                await EnsureRepo();
                using (var c = Client())
                {
                    var resp = await c.GetAsync(GithubApiBase + "/repos/" + _login + "/" + _repo + "/contents/" + FileName);
                    if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
                    resp.EnsureSuccessStatusCode();
                    var d = Json(await resp.Content.ReadAsStringAsync());
                    _sha = d.ContainsKey("sha") ? (string)d["sha"] : null;
                    string b64 = ((string)d["content"]).Replace("\n", "").Replace("\r", "");
                    return Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                }
            }

            public async Task Upload(string content)
            {
                await EnsureRepo();
                if (_sha == null)
                {
                    // 未下载过时先探一次 sha，避免重复创建冲突
                    try { await Download(); } catch { }
                }
                using (var c = Client())
                {
                    var payload = new Dictionary<string, object>
                    {
                        { "message", "WinTranslator sync " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                        { "content", Convert.ToBase64String(Encoding.UTF8.GetBytes(content)) }
                    };
                    if (_sha != null) payload["sha"] = _sha;
                    var body = Ser.Serialize(payload);
                    var req = new HttpRequestMessage(HttpMethod.Put, GithubApiBase + "/repos/" + _login + "/" + _repo + "/contents/" + FileName)
                    {
                        Content = new StringContent(body, Encoding.UTF8, "application/json")
                    };
                    var resp = await c.SendAsync(req);
                    resp.EnsureSuccessStatusCode();
                    var d = Json(await resp.Content.ReadAsStringAsync());
                    if (d.ContainsKey("content") && d["content"] is Dictionary<string, object>)
                        _sha = (string)((Dictionary<string, object>)d["content"])["sha"];
                }
            }
        }

        private class WebDavTransport : ISyncTransport
        {
            private readonly string _fileUrl, _user, _pass;

            public WebDavTransport(string url, string user, string pass)
            {
                _fileUrl = (url ?? "").TrimEnd(new[] { '/' }) + "/" + FileName;
                _user = user ?? "";
                _pass = pass ?? "";
            }

            private HttpClient Client()
            {
                var c = NewClient();
                c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(_user + ":" + _pass)));
                return c;
            }

            public async Task<string> Download()
            {
                using (var c = Client())
                {
                    var resp = await c.GetAsync(_fileUrl);
                    if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
                    resp.EnsureSuccessStatusCode();
                    return await resp.Content.ReadAsStringAsync();
                }
            }

            public async Task Upload(string content)
            {
                using (var c = Client())
                {
                    var req = new HttpRequestMessage(HttpMethod.Put, _fileUrl)
                    {
                        Content = new StringContent(content, Encoding.UTF8, "application/json")
                    };
                    var resp = await c.SendAsync(req);
                    resp.EnsureSuccessStatusCode();
                }
            }
        }
    }
}
