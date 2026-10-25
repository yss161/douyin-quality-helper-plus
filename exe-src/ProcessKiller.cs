using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace DouyinQualityHelper
{
    /// <summary>
    /// 一个正在运行的抖音相关进程。
    /// </summary>
    public sealed class DouyinProcess
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string Role { get; set; }

        public DouyinProcess()
        {
            Name = "";
            Path = "";
            Role = "";
        }

        /// <summary>例如 douyin.exe(1234，主程序)</summary>
        public string Short()
        {
            return Name + ".exe(" + Id + "，" + Role + ")";
        }
    }

    /// <summary>
    /// 一次"结束抖音进程"的结果，供界面/CLI 原样展示。
    /// </summary>
    public sealed class KillResult
    {
        public List<DouyinProcess> Found = new List<DouyinProcess>();
        public List<DouyinProcess> ClosedGently = new List<DouyinProcess>();
        public List<DouyinProcess> Terminated = new List<DouyinProcess>();
        public List<DouyinProcess> Remaining = new List<DouyinProcess>();
        public List<string> Failures = new List<string>();
        /// <summary>有进程因为权限不足（需要管理员）没能结束。</summary>
        public bool RequiresElevation;

        public bool AllGone { get { return Remaining.Count == 0; } }
    }

    /// <summary>
    /// 抖音运行时会一直占着安装目录里的文件：改 preload.js 之前，主程序、托盘、
    /// 守护进程、崩溃上报这些都必须先退出，否则写文件会失败，或者刚写的补丁被
    /// 运行中的旧进程重新覆盖。
    ///
    /// 这里把"找进程 → 先请求正常关闭 → 再强制结束 → 重新枚举复核"收在一个地方，
    /// 图形界面、命令行（--kill）和脚本都调用它，行为完全一致。
    ///
    /// 安全边界：只动白名单里的进程名，并且能用路径确认时还要求 exe 就在用户选定的
    /// 抖音安装目录下 —— 绝不会误杀同名的其它程序。
    /// </summary>
    public static class ProcessKiller
    {
        // 名字本身就只属于抖音：即使读不到 exe 路径（例如提权进程）也能认定。
        static readonly string[] SpecificNames = { "douyin", "douyin_guard", "douyin_tray", "douyin_widget" };

        // 字节系共用的通用组件名：必须确认 exe 路径就在抖音安装目录里才动手。
        static readonly string[] HelperNames =
        {
            "parfait_crash_handler", "tt_crash_reporter", "push_detect", "douyin_doctor", "dump_reporter"
        };

        /// <summary>进程名的中文角色，只用于日志和提示。</summary>
        public static string RoleOf(string name)
        {
            switch ((name ?? "").ToLowerInvariant())
            {
                case "douyin": return "主程序";
                case "douyin_guard": return "守护进程";
                case "douyin_tray": return "托盘";
                case "douyin_widget": return "桌面组件";
                case "parfait_crash_handler": return "崩溃上报";
                case "tt_crash_reporter": return "崩溃上报";
                case "push_detect": return "推送检测";
                case "douyin_doctor": return "自检工具";
                case "dump_reporter": return "崩溃转储";
                default: return "抖音进程";
            }
        }

        /// <summary>拼成一行人类可读的进程列表。</summary>
        public static string Describe(IEnumerable<DouyinProcess> list)
        {
            var parts = new List<string>();
            if (list != null)
                foreach (var d in list)
                    if (d != null) parts.Add(d.Short());
            return string.Join("、", parts.ToArray());
        }

        /// <summary>抖音（或它的附属进程）是否还在运行。</summary>
        public static bool IsAnyRunning(string installRoot)
        {
            return Find(installRoot).Count > 0;
        }

        /// <summary>
        /// 找出所有"需要结束"的抖音进程。installRoot 已知时会用它来确认路径，
        /// 未知（null/空）时退化为按进程名判断。
        /// </summary>
        public static List<DouyinProcess> Find(string installRoot)
        {
            var found = new List<DouyinProcess>();
            string root = NormalizeRoot(installRoot);
            bool hasRoot = root.Length > 0;

            int selfId = 0;
            try { selfId = Process.GetCurrentProcess().Id; }
            catch { }

            Process[] all;
            try { all = Process.GetProcesses(); }
            catch { return found; }

            foreach (var p in all)
            {
                try
                {
                    if (p == null || p.Id == selfId) continue;

                    string name = "";
                    try { name = p.ProcessName ?? ""; }
                    catch { continue; }
                    if (name.Length == 0) continue;

                    bool specific = Matches(SpecificNames, name);
                    bool helper = Matches(HelperNames, name);
                    if (!specific && !helper) continue;

                    string exe = null;
                    try { exe = p.MainModule == null ? null : p.MainModule.FileName; }
                    catch { exe = null; }

                    // 能读到 exe 路径、并且用户选定了安装目录时，以路径为准：
                    // 不在这个安装目录里的同名单进程一律不动，避免误杀。
                    if (!string.IsNullOrEmpty(exe) && hasRoot && !IsUnder(exe, root)) continue;
                    // 路径读不到（进程以更高权限运行）时，只有抖音专属名字才敢认：
                    // parfait_crash_handler / tt_crash_reporter 这类通用组件名必须看到路径。
                    if (string.IsNullOrEmpty(exe) && !specific) continue;

                    found.Add(new DouyinProcess
                    {
                        Id = p.Id,
                        Name = name,
                        Path = exe ?? "",
                        Role = RoleOf(name)
                    });
                }
                catch { }
                finally
                {
                    try { p.Dispose(); } catch { }
                }
            }

            found.Sort(delegate(DouyinProcess a, DouyinProcess b)
            {
                int pa = Priority(a.Name), pb = Priority(b.Name);
                if (pa != pb) return pa.CompareTo(pb);
                int n = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                return n != 0 ? n : a.Id.CompareTo(b.Id);
            });
            return found;
        }

        /// <summary>
        /// 结束所有需要结束的抖音进程。
        /// 先请求正常关闭（抖音有机会自己退出、保住登录态），超时后强制结束，最后重新
        /// 枚举一遍确认真的没了 —— 返回的 Remaining 才是可信结论。
        /// </summary>
        /// <param name="installRoot">抖音安装目录，可为空（此时按进程名判断）</param>
        /// <param name="gentleMs">等待正常关闭的时间（毫秒）</param>
        /// <param name="forceMs">强制结束后等待退出的时间（毫秒）</param>
        /// <param name="log">进度回调，可为空</param>
        public static KillResult KillAll(string installRoot, int gentleMs, int forceMs, Action<string> log)
        {
            var result = new KillResult();
            result.Found = Find(installRoot);
            if (result.Found.Count == 0) return result;
            if (log != null) log("找到 " + result.Found.Count + " 个抖音进程：" + Describe(result.Found));

            // 1) 先请求正常关闭
            foreach (var d in result.Found)
            {
                Process p = TryOpen(d.Id);
                if (p == null) continue;
                try
                {
                    if (p.MainWindowHandle != IntPtr.Zero && p.CloseMainWindow())
                        result.ClosedGently.Add(d);
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }
            if (result.ClosedGently.Count > 0 && log != null)
                log("已请求正常关闭：" + Describe(result.ClosedGently));
            WaitGone(result.Found, gentleMs);

            // 2) 还活着的强制结束
            foreach (var d in result.Found)
            {
                Process p = TryOpen(d.Id);
                if (p == null) continue;
                try
                {
                    p.Kill();
                    result.Terminated.Add(d);
                }
                catch (Exception ex)
                {
                    if (IsAccessDenied(ex)) result.RequiresElevation = true;
                    result.Failures.Add("无法结束 " + d.Short() + "：" + Friendly(ex));
                }
                finally { try { p.Dispose(); } catch { } }
            }
            if (result.Terminated.Count > 0 && log != null)
                log("已强制结束：" + Describe(result.Terminated));
            WaitGone(result.Found, forceMs);

            // 3) 复核：以"重新枚举"为准，而不是相信 Kill() 没抛异常
            result.Remaining = Find(installRoot);
            foreach (var d in result.Remaining)
            {
                bool reported = false;
                foreach (var f in result.Failures)
                    if (f.IndexOf("(" + d.Id + "，", StringComparison.Ordinal) >= 0) { reported = true; break; }
                if (!reported) result.Failures.Add("仍在运行：" + d.Short());
            }
            if (result.Remaining.Count > 0)
            {
                foreach (var d in result.Remaining)
                {
                    if (IsRunningElevated(d.Id)) result.RequiresElevation = true;
                }
            }
            return result;
        }

        // ------------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------------
        static int Priority(string name)
        {
            switch ((name ?? "").ToLowerInvariant())
            {
                case "douyin": return 0;          // 主程序先走
                case "douyin_tray": return 1;
                case "douyin_widget": return 2;
                case "douyin_guard": return 3;    // 守护最后
                default: return 5;
            }
        }

        static bool Matches(string[] names, string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < names.Length; i++)
                if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static Process TryOpen(int pid)
        {
            try
            {
                var p = Process.GetProcessById(pid);
                return p;
            }
            catch { return null; }
        }

        static void WaitGone(List<DouyinProcess> targets, int timeoutMs)
        {
            if (timeoutMs <= 0) return;
            var deadline = Environment.TickCount + timeoutMs;
            while (Environment.TickCount < deadline)
            {
                bool any = false;
                foreach (var d in targets)
                {
                    Process p = TryOpen(d.Id);
                    if (p != null)
                    {
                        any = true;
                        try { p.Dispose(); } catch { }
                    }
                }
                if (!any) return;
                Thread.Sleep(120);
            }
        }

        static bool IsRunningElevated(int pid)
        {
            Process p = TryOpen(pid);
            if (p == null) return false;
            try
            {
                if (p.MainModule == null) return true;
                return false;   // 能读到模块信息 → 和我们同权限，不是提权进程
            }
            catch (Win32Exception ex) { return IsAccessDenied(ex); }
            catch { return true; }
            finally { try { p.Dispose(); } catch { } }
        }

        static bool IsAccessDenied(Exception ex)
        {
            var we = ex as Win32Exception;
            if (we != null) return we.NativeErrorCode == 5;
            return ex != null && ex.Message.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static string Friendly(Exception ex)
        {
            if (IsAccessDenied(ex)) return "拒绝访问（需要管理员权限）";
            var we = ex as Win32Exception;
            if (we != null) return we.Message + "（代码 " + we.NativeErrorCode + "）";
            return ex == null ? "未知错误" : ex.Message;
        }

        static string NormalizeRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return "";
            string s = root.Trim().Trim('"');
            try { s = Path.GetFullPath(s); }
            catch { }
            return s.TrimEnd('\\', '/');
        }

        static bool IsUnder(string filePath, string root)
        {
            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(root)) return false;
            string f = filePath;
            try { f = Path.GetFullPath(f); }
            catch { }
            f = f.TrimEnd('\\', '/');
            if (f.Length <= root.Length) return false;
            if (!f.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;
            char sep = f[root.Length];
            return sep == '\\' || sep == '/';
        }
    }
}
