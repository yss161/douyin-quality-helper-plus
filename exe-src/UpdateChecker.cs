using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace DouyinQualityHelper
{
    /// <summary>一次更新检测的结果。</summary>
    public sealed class UpdateInfo
    {
        public bool HasUpdate;
        public string CurrentVersion = "";
        public string LatestVersion = "";
        public string Title = "";
        public string Notes = "";
        public string HtmlUrl = "";
        public string Error;
        public DateTime PublishedAt = DateTime.MinValue;
    }

    /// <summary>
    /// 基于 GitHub Releases 的更新检测。
    ///
    /// 跑在后台线程（由调用方用 Thread 或 Task 包装），这里只做同步 HTTP。
    /// 不用 HttpClient（.NET 4.8 的 HttpClient 会拉起一堆依赖），改用 WebClient。
    /// 网络失败不抛异常，只把原因放进 Error —— 更新检测失败不该影响主功能。
    /// </summary>
    public static class UpdateChecker
    {
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

        /// <summary>离线版本比较：latest 是否比 current 新。</summary>
        public static bool IsNewer(string latest, string current)
        {
            List<int> a = ParseVersion(latest);
            List<int> b = ParseVersion(current);
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

        /// <summary>把 "v2.1.0" / "2.1.0-beta" 统一成 "2.1.0"。</summary>
        public static string NormalizeTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return "";
            string s = tag.Trim();
            if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V')) s = s.Substring(1);
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
            string[] parts = s.Split('.');
            List<int> nums = new List<int>();
            for (int i = 0; i < parts.Length; i++)
            {
                int n;
                if (!int.TryParse(parts[i], out n)) return null;
                nums.Add(n);
            }
            return nums.Count > 0 ? nums : null;
        }

        /// <summary>
        /// 同步查询 GitHub 最新 Release。超时 10 秒。
        /// 任何异常都会被吞掉并写进 Error 字段。
        /// </summary>
        public static UpdateInfo Check(string currentVersion)
        {
            UpdateInfo info = new UpdateInfo();
            info.CurrentVersion = NormalizeTag(currentVersion);

            try
            {
                using (WebClient wc = new WebClient())
                {
                    // GitHub API 强制要求 User-Agent，否则 403
                    wc.Headers.Add("User-Agent", "DouyinQualityHelper/" + info.CurrentVersion);
                    wc.Headers.Add("Accept", "application/vnd.github+json");
                    wc.Encoding = Encoding.UTF8;

                    string json = wc.DownloadString(ApiUrl);
                    ParseRelease(json, info);
                    info.HasUpdate = IsNewer(info.LatestVersion, info.CurrentVersion);
                    return info;
                }
            }
            catch (WebException wex)
            {
                HttpWebResponse resp = wex.Response as HttpWebResponse;
                if (resp != null)
                {
                    int code = (int)resp.StatusCode;
                    info.Error = code == 404 ? "还没有发布任何版本" : ("HTTP " + code);
                }
                else
                {
                    info.Error = "无法连接 GitHub";
                }
                return info;
            }
            catch (Exception ex)
            {
                info.Error = ex.Message;
                return info;
            }
        }

        static void ParseRelease(string json, UpdateInfo info)
        {
            try
            {
                string tag = JsonGetString(json, "tag_name");
                info.LatestVersion = NormalizeTag(tag);
                if (info.LatestVersion.Length == 0) info.LatestVersion = tag;

                info.Title = JsonGetString(json, "name");
                if (string.IsNullOrEmpty(info.Title)) info.Title = tag;
                info.Notes = JsonGetString(json, "body");
                info.HtmlUrl = JsonGetString(json, "html_url");

                string published = JsonGetString(json, "published_at");
                if (!string.IsNullOrEmpty(published))
                {
                    DateTime dt;
                    if (DateTime.TryParse(published, out dt)) info.PublishedAt = dt;
                }
            }
            catch (Exception ex)
            {
                info.Error = "解析失败：" + ex.Message;
            }
        }

        /// <summary>
        /// 从 JSON 文本里取一个顶层字符串字段。
        /// Release 的 body 可能包含大量转义和嵌套，用简单扫描比引入 JSON 库更稳。
        /// </summary>
        static string JsonGetString(string json, string name)
        {
            if (string.IsNullOrEmpty(json)) return "";

            // "name": "value"   或   "name": null
            Match m = Regex.Match(json, "\"" + Regex.Escape(name) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!m.Success) return "";

            string raw = m.Groups[1].Value;
            return Unescape(raw);
        }

        static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length)
                {
                    sb.Append(c);
                    continue;
                }

                char n = s[++i];
                switch (n)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'u':
                        if (i + 4 < s.Length)
                        {
                            string hex = s.Substring(i + 1, 4);
                            int code;
                            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out code))
                            {
                                sb.Append((char)code);
                                i += 4;
                            }
                            else sb.Append(n);
                        }
                        else sb.Append(n);
                        break;
                    default: sb.Append(n); break;
                }
            }
            return sb.ToString();
        }
    }
}
