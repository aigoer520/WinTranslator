using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using WinTranslator;

// v1.5.3 云同步本地模拟测试：配合 /tmp/tt/mock_cloud.py（GitHub contents API 子集＋WebDAV）
// 阶段：①GitHub 首传 ②无变化不重复同步 ③云端更新→整库覆盖+本地备份 ④WebDAV 上传落盘
class SyncTest
{
    static int fails = 0;
    static void Check(bool ok, string name)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
        if (!ok) fails++;
    }

    static bool HasFav(Store st, string src)
    {
        return st.Favorites.Any(f => f.Src == src);
    }

    static void Main()
    {
        Run().Wait();
        Console.WriteLine(fails == 0 ? "SYNC ALL PASS" : ("SYNC FAILS=" + fails));
        Environment.Exit(fails == 0 ? 0 : 1);
    }

    static async Task Run()
    {
        SyncService.GithubApiBase = "http://127.0.0.1:8899";
        var st = new Store();
        st.Settings.SyncEnabled = true;
        st.Settings.SyncMethod = 0;
        st.Settings.GithubToken = SyncService.Protect("fake-token");
        st.Settings.GithubRepo = "wintranslator-data";
        st.Settings.SyncHash = "";
        st.Settings.SyncUpdatedAt = 0;
        st.AddFavorite(new Entry { Src = "syncmark-one", Dst = "标记一", Time = 1 });

        // ① GitHub 首传
        var r1 = await SyncService.SyncNow(st);
        Check(r1.Error == null && r1.Uploaded, "github first upload: " + r1.Status);
        Check(st.Settings.SyncHash.Length == 64 && st.Settings.SyncUpdatedAt > 0, "github sync state saved");

        // ② 无变化 → 不重复同步
        var r2 = await SyncService.SyncNow(st);
        Check(!r2.Uploaded && !r2.Applied && r2.Error == null, "github noop when unchanged: " + r2.Status);

        // ③ 云端更新（另一台电脑加了新词）→ 本地整库被覆盖，旧的本地分叉词消失，且有备份
        using (var c = new HttpClient())
        {
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "fake-token");
            c.DefaultRequestHeaders.UserAgent.ParseAdd("WinTranslator");
            string cur = await c.GetStringAsync("http://127.0.0.1:8899/repos/tester/wintranslator-data/contents/wintranslator-data.json");
            var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
            var d = ser.Deserialize<System.Collections.Generic.Dictionary<string, object>>(cur);
            string json = Encoding.UTF8.GetString(Convert.FromBase64String(((string)d["content"]).Replace("\n", "")));
            var bundle = SyncService.ParseBundle(json);
            bundle.Favorites.Insert(0, new Entry { Src = "syncmark-two", Dst = "标记二", Time = 2 });
            bundle.UpdatedAt = st.Settings.SyncUpdatedAt + 500;
            bundle.Hash = "remotehash-remotehash-remotehash-remotehash-remotehash-0000abcd";
            var payload = ser.Serialize(new System.Collections.Generic.Dictionary<string, object>
            {
                { "message", "other pc" },
                { "content", Convert.ToBase64String(Encoding.UTF8.GetBytes(ser.Serialize(bundle))) },
                { "sha", (string)d["sha"] }
            });
            var req = new HttpRequestMessage(HttpMethod.Put, "http://127.0.0.1:8899/repos/tester/wintranslator-data/contents/wintranslator-data.json")
            { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
            var resp = await c.SendAsync(req);
            resp.EnsureSuccessStatusCode();
        }
        st.AddFavorite(new Entry { Src = "syncmark-localfork", Dst = "本地分叉", Time = 3 });
        int backupsBefore = Directory.GetDirectories(st.DataDir, "backup-*").Length;
        var r3 = await SyncService.SyncNow(st);
        Check(r3.Applied, "github remote-newer applied: " + r3.Status);
        Check(HasFav(st, "syncmark-two") && HasFav(st, "syncmark-one"), "downloaded favorites present");
        Check(!HasFav(st, "syncmark-localfork"), "local fork overwritten by newer store");
        Check(Directory.GetDirectories(st.DataDir, "backup-*").Length == backupsBefore + 1, "backup created before overwrite");
        Check(st.Settings.SyncHash.StartsWith("remotehash"), "sync hash follows remote");

        // ④ WebDAV：切方式后上传，文件落到 DAV 路径且内容含最新词
        st.Settings.SyncMethod = 1;
        st.Settings.WebdavUrl = "http://127.0.0.1:8899/dav/";
        st.Settings.WebdavUser = "u@example.com";
        st.Settings.WebdavPass = SyncService.Protect("app-pass");
        st.Settings.SyncHash = "dirty";
        var r4 = await SyncService.SyncNow(st);
        Check(r4.Error == null && r4.Uploaded, "webdav upload: " + r4.Status);
        using (var c = new HttpClient())
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:8899/dav/wintranslator-data.json");
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("u@example.com:app-pass")));
            string body = await (await c.SendAsync(req)).Content.ReadAsStringAsync();
            Check(body.Contains("syncmark-two"), "webdav file contains latest favorite");
            var rb = SyncService.ParseBundle(body);
            Check(rb != null && rb.Favorites.Any(f => f.Src == "syncmark-two"), "webdav bundle parses");
            Check(rb.Settings.GithubToken == "" && rb.Settings.WebdavPass == "" && rb.Settings.GlmApiKey == "",
                "bundle excludes credentials and GLM key");
        }

        // ⑤ 凭据保护往返
        string enc = SyncService.Protect("secret-123");
        Check(enc != "secret-123" && SyncService.Unprotect(enc) == "secret-123", "credential protect roundtrip");
    }
}
