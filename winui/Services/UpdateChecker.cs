using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DouyinQualityHelper
{
    /// <summary>一次更新检测的结果。</summary>
    public sealed class UpdateInfo
    {
        /// <summary>是否有更新。</summary>
        public bool HasUpdate;
        /// <summary>当前版本（例如 2.0.0）。</summary>
        public string CurrentVersion = "";
        /// <summary>GitHub 上最新版本（已去掉前缀 v）。</summary>
        public string LatestVersion = "";
        /// <summary>Release 标题。</summary>
        public string Title = "";
        /// <summary>Release 说明（Markdown 原文）。</summary>
        public string Notes = "";
        /// <summary>点"查看"跳转的地址。</summary>
        public string HtmlUrl = "";
        /// <summary>可下载的资产直链（可能有多个）。</summary>
        public List<string> AssetUrls = new List<string>();
        /// <summary>发布时间。</summary>
        public DateTimeOffset? PublishedAt;
        /// <summary>失败原因；成功时为 null。</summary>
        public string Error;
    }

    /// <summary>
    /// 基于 GitHub Releases 的更新检测。
    ///
    /// 用匿名请求读公开仓库的 /releases/latest，不消耗配额也不需要 Token。
    /// 网络失败（断网、被墙、限流）都不会弹错误框 —— 只在界面上安静地显示"检查失败"，
    /// 因为更新检测失败不该影响主功能。
    /// </summary>
    public static class UpdateChecker
    {
        /// <summary>仓库坐标，改这里就能指向别的 fork。</summary>
        public const string Owner = "yss161";
        public const string Repo = "douyin-quality-helper-plus";

        public static string ApiUrl
        {
            get { return "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest"; }
        }

        public static string ReleasesPageUrl
        {
            get { return "https://github.com/" + Owner + "/" + Repo + "/releases"; }
        }

        public static string TagsPageUrl
        {
            get { return "https://github.com/" + Owner + "/" + Repo + "/tags"; }
        }

        /// <summary>离线的版本比较：latest 是否比 current 新。</summary>
        public static bool IsNewer(string latest, string current)
        {
            var a = ParseVersion(latest);
            var b = ParseVersion(current);
            if (a == null || b == null) return false;

            int len = Math.Max(a.Count, b.Count);
            for (int i = 0; i < len; i++)
            {
                int x = i < a.Count ? a[i] : 0;
                int y = i < b.Count ? b[i] : 0;
                if (x > y) return true;
                if (x < y) return false;
            }
            return false;
        }

        /// <summary>把 "v2.1.0" / "2.1.0-beta" 之类统一成 "2.1.0"。</summary>
        public static string NormalizeTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return "";
            string s = tag.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
            int dash = s.IndexOf('-');
            if (dash > 0) s = s.Substring(0, dash);
            int plus = s.IndexOf('+');
            if (plus > 0) s = s.Substring(0, plus);
            return s.Trim();
        }

        static List<int> ParseVersion(string v)
        {
            string s = NormalizeTag(v);
            if (s.Length == 0) return null;
            var parts = s.Split('.');
            var nums = new List<int>();
            foreach (var p in parts)
            {
                int n;
                if (!int.TryParse(p, out n)) return null;
                nums.Add(n);
            }
            return nums.Count > 0 ? nums : null;
        }

        /// <summary>
        /// 查询 GitHub 上最新 Release。
        /// 超时 10 秒，失败返回带 Error 的结果（不抛异常）。
        /// </summary>
        public static async Task<UpdateInfo> CheckAsync(string currentVersion, CancellationToken ct)
        {
            var info = new UpdateInfo { CurrentVersion = NormalizeTag(currentVersion) };

            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                {
                    // GitHub API 强制要求 User-Agent
                    http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "DouyinQualityHelper/" + info.CurrentVersion);
                    http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");

                    using (var resp = await http.GetAsync(ApiUrl, ct).ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode)
                        {
                            info.Error = resp.StatusCode == System.Net.HttpStatusCode.NotFound
                                ? "还没有发布任何版本"
                                : ("HTTP " + (int)resp.StatusCode);
                            return info;
                        }

                        string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        ParseRelease(json, info);
                        info.HasUpdate = IsNewer(info.LatestVersion, info.CurrentVersion);
                        return info;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                info.Error = "已取消";
                return info;
            }
            catch (Exception ex)
            {
                // 断网 / DNS / TLS / 限流都在这里
                info.Error = ex.GetType().Name == "HttpRequestException"
                    ? "无法连接 GitHub"
                    : ex.Message;
                return info;
            }
        }

        static void ParseRelease(string json, UpdateInfo info)
        {
            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;

                    string tag = GetString(root, "tag_name");
                    info.LatestVersion = NormalizeTag(tag);
                    if (info.LatestVersion.Length == 0) info.LatestVersion = tag ?? "";

                    info.Title = GetString(root, "name");
                    if (string.IsNullOrEmpty(info.Title)) info.Title = tag ?? "";
                    info.Notes = GetString(root, "body");
                    info.HtmlUrl = GetString(root, "html_url");

                    string published = GetString(root, "published_at");
                    DateTimeOffset dt;
                    if (!string.IsNullOrEmpty(published) &&
                        DateTimeOffset.TryParse(published, out dt))
                        info.PublishedAt = dt;

                    JsonElement assets;
                    if (root.TryGetProperty("assets", out assets) && assets.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var a in assets.EnumerateArray())
                        {
                            string url = GetString(a, "browser_download_url");
                            if (!string.IsNullOrEmpty(url)) info.AssetUrls.Add(url);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                info.Error = "解析失败：" + ex.Message;
            }
        }

        static string GetString(JsonElement el, string name)
        {
            JsonElement v;
            if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out v) &&
                v.ValueKind == JsonValueKind.String)
                return v.GetString() ?? "";
            return "";
        }
    }
}
