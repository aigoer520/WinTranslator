using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace WinTranslator
{
    public class Entry
    {
        public string Src { get; set; }
        public string Dst { get; set; }
        public string SrcLang { get; set; }
        public string DstLang { get; set; }
        public string Provider { get; set; }
        public long Time { get; set; }
        // v1.2.0：单词本例句缓存（展开详情时取到后存回，下次秒开；旧数据无此字段为 null）
        public List<ExampleItem> Examples { get; set; }
        public string ExamplesSource { get; set; }
        // v1.3.0：音标缓存（卡片复习用，词典取到后存回）与复习状态（旧数据默认未复习）
        public string PhoneticUk { get; set; }
        public string PhoneticUs { get; set; }
        public int ReviewLevel { get; set; }   // 0~3 掌握等级
        public int ReviewCount { get; set; }   // 复习次数（0=未复习）
        public long LastReview { get; set; }   // 上次复习时间（unix 秒）

        /// <summary>单词/短语判定（单词本分组）：单行、≤40 字、无句末标点、空格≤3。</summary>
        public bool IsWordLike()
        {
            string s = (Src ?? "").Trim();
            if (s.Length == 0 || s.Length > 40) return false;
            if (s.IndexOfAny(new[] { '\n', '\r', '。', '！', '？', '!', '?' }) >= 0) return false;
            if (s.EndsWith(".") || s.EndsWith("．")) return false;
            int spaces = 0;
            foreach (char c in s) if (c == ' ') spaces++;
            return spaces <= 3;
        }
    }

    /// <summary>软件设置，存 %APPDATA%\WinTranslator\settings.json。</summary>
    public class AppSettings
    {
        public bool HotkeyCtrl { get; set; }
        public bool HotkeyAlt { get; set; }
        public bool HotkeyShift { get; set; }
        public string HotkeyKey { get; set; }
        public bool MiniHkCtrl { get; set; }
        public bool MiniHkAlt { get; set; }
        public bool MiniHkShift { get; set; }
        public string MiniHkKey { get; set; }
        public int DefaultSrcIndex { get; set; }   // 旧语义：0 自动 / 1 英 / 2 中（决定首页语对初值）
        public int DefaultDstIndex { get; set; }
        public bool MinimizeToTrayOnClose { get; set; }
        public bool StartMinimized { get; set; }
        public bool AutoStart { get; set; }
        public bool ClipboardListen { get; set; }
        public int EngineIndex { get; set; }       // 0 自动 / 1 有道 / 2 必应 / 3 AI(GLM) / 4 谷歌 gtx / 5 MyMemory
        public bool BallEnabled { get; set; }
        public int BallX { get; set; }
        public int BallY { get; set; }
        public int MiniX { get; set; }
        public int MiniY { get; set; }
        public int FontSize { get; set; }          // 12/14/16/18/20
        public bool MainTopMost { get; set; }
        public bool? ScreenshotHideUi { get; set; } // null=旧配置，按默认开
        public int ScreenshotDelaySec { get; set; } // 0/3/5
        public bool DeskBarEnabled { get; set; }
        public int DeskBarX { get; set; }
        public int DeskBarY { get; set; }
        public string GlmApiKey { get; set; }      // 智谱 GLM 的 Key（用户自备，存本地）
        public string Skin { get; set; }           // v1.3.0 皮肤：classic / acrylic（真毛玻璃）/ backdrop（自带背景磨砂）
        public string UiFontFamily { get; set; }   // v1.5.3 界面字体族（默认 Microsoft YaHei UI）
        public int UiFontScale { get; set; }       // v1.5.3 界面字号档：0 小 / 1 标准 / 2 大
        public bool SyncEnabled { get; set; }      // v1.5.3 云同步开关
        public int SyncMethod { get; set; }        // v1.5.3 云同步方式：0 GitHub 私有仓库 / 1 WebDAV 网盘
        public string GithubToken { get; set; }    // v1.5.3 GitHub 令牌（DPAPI 加密存本机，不同步）
        public string GithubRepo { get; set; }     // v1.5.3 GitHub 同步仓库名（默认 wintranslator-data）
        public string WebdavUrl { get; set; }      // v1.5.3 WebDAV 地址（默认坚果云）
        public string WebdavUser { get; set; }     // v1.5.3 WebDAV 账号
        public string WebdavPass { get; set; }     // v1.5.3 WebDAV 应用密码（DPAPI 加密存本机，不同步）
        public long SyncUpdatedAt { get; set; }    // v1.5.3 已同步数据包时间戳（秒）
        public string SyncHash { get; set; }       // v1.5.3 已同步数据包内容哈希
        // v1.8.0 多引擎密钥（用户自备，DPAPI 加密存本机，不进云同步包——字段名已加入两处排除清单）
        public string BaiduAppId { get; set; }
        public string BaiduKey { get; set; }
        public string DeepLKey { get; set; }
        public string DeeplxUrl { get; set; }
        public string DeeplxToken { get; set; }
        public string CaiyunToken { get; set; }
        public string NiutransKey { get; set; }
        public string YoudaoAppKey { get; set; }
        public string YoudaoKey { get; set; }
        public string AliKeyId { get; set; }
        public string AliKeySecret { get; set; }
        public string TencentSecretId { get; set; }
        public string TencentSecretKey { get; set; }
        public string VolcKeyId { get; set; }
        public string VolcKeySecret { get; set; }
        public string WebsterKey { get; set; }
        public string LibreUrl { get; set; }       // v1.8.0 LibreTranslate 实例地址（可自建）
        public string LingvaUrl { get; set; }      // v1.8.0 Lingva 实例地址
        public string DictSource { get; set; }     // v1.8.0 上次使用的查词来源 id
        public string EnginePrefer { get; set; }   // v1.8.0 当前引擎 prefer id（空＝按旧 EngineIndex 映射）
        public bool CrossCheckEnabled { get; set; } // v1.8.0 翻译交叉核对开关（默认关）
        public bool CrossCheckBack { get; set; }    // v1.8.0 回译抽查开关（默认关）

        public static AppSettings Defaults()
        {
            return new AppSettings
            {
                HotkeyCtrl = true, HotkeyAlt = true, HotkeyShift = false, HotkeyKey = "S",
                MiniHkCtrl = true, MiniHkAlt = true, MiniHkShift = false, MiniHkKey = "M",
                DefaultSrcIndex = 0, DefaultDstIndex = 0,
                MinimizeToTrayOnClose = true, StartMinimized = false, AutoStart = false,
                ClipboardListen = true, EngineIndex = 0,
                BallEnabled = true, BallX = -1, BallY = -1, MiniX = -1, MiniY = -1,
                FontSize = 14, MainTopMost = false, ScreenshotHideUi = true, ScreenshotDelaySec = 0,
                DeskBarEnabled = false, DeskBarX = -1, DeskBarY = -1, GlmApiKey = "",
                UiFontFamily = "Microsoft YaHei UI", UiFontScale = 1,
                SyncEnabled = false, SyncMethod = 0, GithubToken = "", GithubRepo = "wintranslator-data",
                WebdavUrl = "https://dav.jianguoyun.com/dav/", WebdavUser = "", WebdavPass = "",
                SyncUpdatedAt = 0, SyncHash = "",
                BaiduAppId = "", BaiduKey = "", DeepLKey = "", DeeplxUrl = "", DeeplxToken = "",
                CaiyunToken = "", NiutransKey = "", YoudaoAppKey = "", YoudaoKey = "",
                AliKeyId = "", AliKeySecret = "", TencentSecretId = "", TencentSecretKey = "",
                VolcKeyId = "", VolcKeySecret = "", WebsterKey = "",
                LibreUrl = "https://libretranslate.com", LingvaUrl = "https://lingva.lunar.icu",
                DictSource = "", EnginePrefer = "", CrossCheckEnabled = false, CrossCheckBack = false
            };
        }
    }

    /// <summary>本地数据存储：settings.json / favorites.json / history.json（上限 500 条）。</summary>
    public class Store
    {
        private readonly JavaScriptSerializer _ser = new JavaScriptSerializer();
        public string DataDir { get; private set; }
        public AppSettings Settings { get; private set; }
        public List<Entry> Favorites { get; private set; }
        public List<Entry> History { get; private set; }

        public Store()
        {
            DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinTranslator");
            Directory.CreateDirectory(DataDir);
            Settings = LoadSettings();
            Favorites = LoadList<Entry>("favorites.json");
            History = LoadList<Entry>("history.json");
        }

        private string PathOf(string name) { return Path.Combine(DataDir, name); }

        private AppSettings LoadSettings()
        {
            try
            {
                string path = PathOf("settings.json");
                if (File.Exists(path))
                {
                    string rawText = File.ReadAllText(path, Encoding.UTF8);
                    var s = _ser.Deserialize<AppSettings>(rawText);
                    if (s != null && !string.IsNullOrEmpty(s.HotkeyKey))
                    {
                        if (s.FontSize != 12 && s.FontSize != 14 && s.FontSize != 16 && s.FontSize != 18 && s.FontSize != 20)
                            s.FontSize = 14;
                        if (string.IsNullOrEmpty(s.MiniHkKey)) { s.MiniHkKey = "M"; s.MiniHkCtrl = true; s.MiniHkAlt = true; }
                        if (s.GlmApiKey == null) s.GlmApiKey = "";
                        if (s.EngineIndex < 0 || s.EngineIndex > 5) s.EngineIndex = 0;
                        if (string.IsNullOrEmpty(s.UiFontFamily)) s.UiFontFamily = "Microsoft YaHei UI";
                        // v1.5.3 字段缺失（旧版配置）时反序列化给 0＝"小"，须按缺失判回默认"标准"，不能让升级用户被悄悄改小
                        if (s.UiFontScale < 0 || s.UiFontScale > 2 || !rawText.Contains("\"UiFontScale\"")) s.UiFontScale = 1;
                        // v1.8.0 新字段在旧配置里缺失时补默认（字符串给 ""、实例地址给默认）
                        if (s.BaiduAppId == null) s.BaiduAppId = "";
                        if (s.BaiduKey == null) s.BaiduKey = "";
                        if (s.DeepLKey == null) s.DeepLKey = "";
                        if (s.DeeplxUrl == null) s.DeeplxUrl = "";
                        if (s.DeeplxToken == null) s.DeeplxToken = "";
                        if (s.CaiyunToken == null) s.CaiyunToken = "";
                        if (s.NiutransKey == null) s.NiutransKey = "";
                        if (s.YoudaoAppKey == null) s.YoudaoAppKey = "";
                        if (s.YoudaoKey == null) s.YoudaoKey = "";
                        if (s.AliKeyId == null) s.AliKeyId = "";
                        if (s.AliKeySecret == null) s.AliKeySecret = "";
                        if (s.TencentSecretId == null) s.TencentSecretId = "";
                        if (s.TencentSecretKey == null) s.TencentSecretKey = "";
                        if (s.VolcKeyId == null) s.VolcKeyId = "";
                        if (s.VolcKeySecret == null) s.VolcKeySecret = "";
                        if (s.WebsterKey == null) s.WebsterKey = "";
                        if (string.IsNullOrEmpty(s.LibreUrl)) s.LibreUrl = "https://libretranslate.com";
                        if (string.IsNullOrEmpty(s.LingvaUrl)) s.LingvaUrl = "https://lingva.lunar.icu";
                        if (s.DictSource == null) s.DictSource = "";
                        if (s.EnginePrefer == null) s.EnginePrefer = "";
                        if (s.GithubRepo == null) s.GithubRepo = "wintranslator-data";
                        if (s.WebdavUrl == null) s.WebdavUrl = "https://dav.jianguoyun.com/dav/";
                        if (s.GithubToken == null) s.GithubToken = "";
                        if (s.WebdavUser == null) s.WebdavUser = "";
                        if (s.WebdavPass == null) s.WebdavPass = "";
                        if (s.SyncHash == null) s.SyncHash = "";
                        return s;
                    }
                }
            }
            catch { }
            return AppSettings.Defaults();
        }

        public void SaveSettings()
        {
            try { File.WriteAllText(PathOf("settings.json"), _ser.Serialize(Settings), Encoding.UTF8); }
            catch { }
        }

        private List<T> LoadList<T>(string name)
        {
            try
            {
                string path = PathOf(name);
                if (File.Exists(path))
                {
                    var list = _ser.Deserialize<List<T>>(File.ReadAllText(path, Encoding.UTF8));
                    if (list != null) return list;
                }
            }
            catch { }
            return new List<T>();
        }

        private void SaveList<T>(string name, List<T> list)
        {
            try { File.WriteAllText(PathOf(name), _ser.Serialize(list), Encoding.UTF8); }
            catch { }
        }

        public void AddFavorite(Entry e)
        {
            Favorites.RemoveAll(f => f.Src == e.Src && f.Dst == e.Dst);
            Favorites.Insert(0, e);
            SaveList("favorites.json", Favorites);
        }

        public void RemoveFavorite(Entry e)
        {
            Favorites.Remove(e);
            SaveList("favorites.json", Favorites);
        }

        /// <summary>v1.2.0：例句缓存等条目字段变更后落盘。</summary>
        public void PersistFavorites()
        {
            SaveList("favorites.json", Favorites);
        }

        /// <summary>
        /// v1.5.3 云同步：整库替换为云端资料。设置只合并可同步项——凭据、同步配置、
        /// 窗口位置等本机项一律保留本机值（排除清单与 SyncService.SanitizeSettings 对应）。
        /// </summary>
        public void ReplaceAllData(List<Entry> favorites, List<Entry> history, AppSettings cloudSettings)
        {
            Favorites.Clear();
            if (favorites != null) Favorites.AddRange(favorites);
            History.Clear();
            if (history != null) History.AddRange(history);
            if (cloudSettings != null)
            {
                var keep = new HashSet<string>
                {
                    "GlmApiKey", "GithubToken", "WebdavPass",
                    "BaiduAppId", "BaiduKey", "DeepLKey", "DeeplxUrl", "DeeplxToken", "CaiyunToken", "NiutransKey",
                    "YoudaoAppKey", "YoudaoKey", "AliKeyId", "AliKeySecret", "TencentSecretId", "TencentSecretKey",
                    "VolcKeyId", "VolcKeySecret", "WebsterKey",
                    "SyncEnabled", "SyncMethod", "GithubRepo", "WebdavUrl", "WebdavUser", "SyncUpdatedAt", "SyncHash",
                    "BallX", "BallY", "MiniX", "MiniY", "DeskBarX", "DeskBarY"
                };
                foreach (var p in typeof(AppSettings).GetProperties())
                {
                    if (!p.CanRead || !p.CanWrite || keep.Contains(p.Name)) continue;
                    try { p.SetValue(Settings, p.GetValue(cloudSettings, null), null); } catch { }
                }
            }
            SaveList("favorites.json", Favorites);
            SaveList("history.json", History);
            SaveSettings();
        }

        public void AddHistory(Entry e)
        {
            History.Insert(0, e);
            if (History.Count > 500) History.RemoveRange(500, History.Count - 500);
            SaveList("history.json", History);
        }

        public void ClearHistory()
        {
            History.Clear();
            SaveList("history.json", History);
        }

        public int ExportFavorites(string path)
        {
            var sb = new StringBuilder();
            foreach (var f in Favorites)
            {
                var t = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(f.Time).ToLocalTime();
                sb.AppendLine("[" + t.ToString("yyyy-MM-dd HH:mm") + "] " + f.SrcLang + " -> " + f.DstLang + " (via " + f.Provider + ")");
                sb.AppendLine("原文：" + f.Src);
                sb.AppendLine("译文：" + f.Dst);
                sb.AppendLine(new string('-', 40));
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            return Favorites.Count;
        }

        public int ExportFavoritesCsv(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("时间,原文,译文,源语言,目标语言,引擎");
            foreach (var f in Favorites)
            {
                var t = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(f.Time).ToLocalTime();
                sb.AppendLine(string.Join(",", new[]
                {
                    CsvCell(t.ToString("yyyy-MM-dd HH:mm")), CsvCell(f.Src), CsvCell(f.Dst),
                    CsvCell(f.SrcLang), CsvCell(f.DstLang), CsvCell(f.Provider)
                }));
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            return Favorites.Count;
        }

        private static string CsvCell(string s)
        {
            s = s ?? "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
