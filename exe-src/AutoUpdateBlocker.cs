using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DouyinQualityHelper
{
    /// <summary>禁止自动更新的当前状态。</summary>
    public sealed class AutoUpdateState
    {
        /// <summary>launcher_config.json 是否存在。</summary>
        public bool ConfigFound;
        /// <summary>当前 need_update 的值。</summary>
        public bool NeedUpdate;
        /// <summary>文件是否只读。</summary>
        public bool IsReadOnly;
        /// <summary>是否已处于"禁止自动更新"状态（need_update=false 且只读）。</summary>
        public bool Blocked;
        /// <summary>仍记录着待安装包路径的字段（说明有已下载的更新）。</summary>
        public List<string> PendingFields = new List<string>();
        /// <summary>配置文件路径。</summary>
        public string ConfigPath = "";
        /// <summary>给界面看的一句话说明。</summary>
        public string Detail = "";
        /// <summary>读取或解析失败时的原因。</summary>
        public string Error;
    }

    /// <summary>
    /// 禁止抖音自动更新。
    ///
    /// 原理：抖音桌面版每次启动会读安装根目录的 launcher_config.json，
    /// 依据 need_update 决定要不要检查/安装新版本；同时 installer_path /
    /// mid_path / new_mid_path / new_path 记录了已下载或待安装的更新包。
    ///
    /// 所以"禁止自动更新"要做三件事：
    ///   1. need_update 改成 false
    ///   2. 清空那四个包路径字段（否则已下载的包仍可能被装上）
    ///   3. 把文件设为只读，防止抖音或 guard 进程改回去
    ///
    /// 为什么要禁止更新：抖音每次升级都会换版本号目录（8.7.0 → 8.8.0），
    /// 新版目录里的 preload.js 是官方原版，我们打的画质补丁会一起消失。
    ///
    /// 需要写 C:\Program Files 下的文件，所以调用前必须是管理员。
    /// </summary>
    public static class AutoUpdateBlocker
    {
        /// <summary>记录待安装包路径的字段，禁止更新时要清空。</summary>
        static readonly string[] PackageFields = new string[]
        {
            "installer_path", "mid_path", "new_mid_path", "new_path"
        };

        public static string ConfigPathOf(string installRoot)
        {
            if (string.IsNullOrEmpty(installRoot)) return null;
            return Path.Combine(installRoot, "launcher_config.json");
        }

        /// <summary>读取当前状态，不发任何修改。失败时 result.Error 有原因。</summary>
        public static AutoUpdateState Inspect(string installRoot)
        {
            var st = new AutoUpdateState();

            string path = ConfigPathOf(installRoot);
            if (path == null)
            {
                st.Error = "还没有选择抖音安装位置";
                return st;
            }

            st.ConfigPath = path;
            if (!File.Exists(path))
            {
                st.Error = "找不到 launcher_config.json";
                return st;
            }

            st.ConfigFound = true;

            try
            {
                var attrs = File.GetAttributes(path);
                st.IsReadOnly = (attrs & FileAttributes.ReadOnly) == FileAttributes.ReadOnly;
            }
            catch (Exception ex)
            {
                st.Error = "读取文件属性失败：" + ex.Message;
            }

            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                var dict = JsonUtil.AsDict(JsonUtil.Parse(text));
                if (dict == null)
                {
                    st.Error = "launcher_config.json 内容不是对象";
                    return st;
                }

                st.NeedUpdate = GetBool(dict, "need_update", true);

                for (int i = 0; i < PackageFields.Length; i++)
                {
                    string v = JsonUtil.GetString(dict, PackageFields[i]);
                    if (!string.IsNullOrEmpty(v)) st.PendingFields.Add(PackageFields[i]);
                }
            }
            catch (Exception ex)
            {
                st.Error = "解析失败：" + ex.Message;
                return st;
            }

            st.Blocked = !st.NeedUpdate && st.IsReadOnly;
            st.Detail = DescribeState(st);
            return st;
        }

        static string DescribeState(AutoUpdateState st)
        {
            if (!st.NeedUpdate && st.IsReadOnly)
                return "已禁止自动更新（need_update=false 且文件只读）";

            if (!st.NeedUpdate && !st.IsReadOnly)
                return "need_update 已是 false，但文件没设只读，抖音可能改回去";

            if (st.NeedUpdate && st.IsReadOnly)
                return "need_update=true 且文件只读，抖音改不动，等于仍然允许更新";

            string s = "允许自动更新（need_update=true）";
            if (st.PendingFields.Count > 0)
                s += "，且有 " + st.PendingFields.Count + " 个已下载的更新包待安装";
            return s;
        }

        /// <summary>
        /// 禁止自动更新：need_update=false + 清空包路径 + 设只读。
        /// 成功返回 null，失败返回原因。
        /// </summary>
        public static string Disable(string installRoot)
        {
            string path = ConfigPathOf(installRoot);
            if (path == null) return "还没有选择抖音安装位置";
            if (!File.Exists(path)) return "找不到 launcher_config.json";

            try
            {
                // 备份一份原始配置（只在第一次备份，避免覆盖掉最初的样子）
                try
                {
                    string bak = Path.Combine(AppPaths.BackupDir, "launcher_config.json.orig");
                    if (!File.Exists(bak))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(bak));
                        File.Copy(path, bak, true);
                    }
                }
                catch { }

                var dict = ReadDict(path);
                if (dict == null) return "launcher_config.json 内容无法解析";

                dict["need_update"] = false;
                for (int i = 0; i < PackageFields.Length; i++)
                    dict[PackageFields[i]] = "";

                WriteDict(path, dict);

                // 写完之后再设只读，否则会写不进去
                SetReadOnly(path, true);

                // 复核
                var st = Inspect(installRoot);
                if (!st.Blocked)
                    return "写入后复核未通过：" + (st.Error == null ? st.Detail : st.Error);

                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return "没有权限修改抖音目录，请右键本程序「以管理员身份运行」后重试";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>
        /// 恢复自动更新：去掉只读 + need_update 改回 true。
        /// 成功返回 null，失败返回原因。
        /// </summary>
        public static string Enable(string installRoot)
        {
            string path = ConfigPathOf(installRoot);
            if (path == null) return "还没有选择抖音安装位置";
            if (!File.Exists(path)) return "找不到 launcher_config.json";

            try
            {
                SetReadOnly(path, false);

                var dict = ReadDict(path);
                if (dict == null) return "launcher_config.json 内容无法解析";

                dict["need_update"] = true;
                WriteDict(path, dict);

                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return "没有权限修改抖音目录，请右键本程序「以管理员身份运行」后重试";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        // -----------------------------------------------------------------
        // 内部工具
        // -----------------------------------------------------------------
        static Dictionary<string, object> ReadDict(string path)
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0) return null;
            return JsonUtil.AsDict(JsonUtil.Parse(text));
        }

        static void WriteDict(string path, Dictionary<string, object> dict)
        {
            // 写之前先确保不是只读，否则会抛 UnauthorizedAccessException
            SetReadOnly(path, false);

            string json = JsonUtil.Serialize(dict);
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

        static void SetReadOnly(string path, bool readOnly)
        {
            var attrs = File.GetAttributes(path);
            if (readOnly) attrs = attrs | FileAttributes.ReadOnly;
            else attrs = attrs & ~FileAttributes.ReadOnly;
            File.SetAttributes(path, attrs);
        }

        static bool GetBool(Dictionary<string, object> d, string key, bool fallback)
        {
            object v = JsonUtil.Get(d, key);
            if (v is bool) return (bool)v;
            if (v == null) return fallback;

            string s = Convert.ToString(v).Trim().ToLowerInvariant();
            if (s == "true" || s == "1") return true;
            if (s == "false" || s == "0") return false;
            return fallback;
        }
    }
}
