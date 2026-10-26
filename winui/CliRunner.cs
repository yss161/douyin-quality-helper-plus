using System;
using System.Runtime.InteropServices;

namespace DouyinQualityHelper
{
    /// <summary>
    /// 命令行入口。WinUI 程序默认是 WinExe（没有控制台），
    /// 所以先用 AttachConsole 接管调用者的控制台，没有就自建一个。
    ///
    /// 支持：
    ///   --selftest              自检
    ///   --patch --src --payload --out   单文件打补丁（与 Node 版对拍用）
    ///   --version               版本
    ///   --kill [--root DIR]     结束抖音进程
    /// </summary>
    public static class CliRunner
    {
        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int pid);

        [DllImport("kernel32.dll")]
        static extern bool AllocConsole();

        const int ATTACH_PARENT_PROCESS = -1;

        public static int Run(string[] args)
        {
            if (args == null || args.Length <= 1) return 0;

            if (!AttachConsole(ATTACH_PARENT_PROCESS)) AllocConsole();
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }

            string cmd = (args[1] ?? "").ToLowerInvariant();
            switch (cmd)
            {
                case "--selftest":
                case "/selftest":
                    return SelfTest.Run(AppPaths.AppDir);

                case "--version":
                case "/version":
                    Console.WriteLine("douyin-quality-helper (WinUI) " + typeof(CliRunner).Assembly.GetName().Version);
                    Console.WriteLine("tool " + VersionChecker.GetToolVersion());
                    return 0;

                case "--patch":
                case "/patch":
                    return Patch(args);

                case "--kill":
                case "/kill":
                    return Kill(args);

                default:
                    Console.Error.WriteLine("unknown command: " + cmd);
                    Console.Error.WriteLine("usage: --selftest | --version | --patch --src <f> --payload <f> --out <f> | --kill [--root <dir>]");
                    return 2;
            }
        }

        static int Patch(string[] args)
        {
            try
            {
                string src = null, payload = null, output = null;
                for (int i = 2; i < args.Length; i++)
                {
                    if (args[i] == "--src" && i + 1 < args.Length) src = args[++i];
                    else if (args[i] == "--payload" && i + 1 < args.Length) payload = args[++i];
                    else if (args[i] == "--out" && i + 1 < args.Length) output = args[++i];
                }
                if (src == null || payload == null || output == null)
                {
                    Console.Error.WriteLine("usage: --patch --src <preload.js> --payload <payload.js> --out <out.js>");
                    return 2;
                }
                Patcher.PatchFile(src, payload, output);
                Console.WriteLine("[ok] wrote " + output);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[x] " + ex.Message);
                return 1;
            }
        }

        static int Kill(string[] args)
        {
            string root = null;
            for (int i = 2; i < args.Length; i++)
            {
                if ((args[i] == "--root" || args[i] == "--dir") && i + 1 < args.Length) root = args[++i];
                else if (args[i].StartsWith("--root=", StringComparison.Ordinal)) root = args[i].Substring(7);
            }
            if (string.IsNullOrEmpty(root)) root = Config.ReadDouyinDir();

            Console.WriteLine("root: " + (string.IsNullOrEmpty(root) ? "(unknown)" : root));
            var found = ProcessKiller.Find(root);
            if (found.Count == 0)
            {
                Console.WriteLine("[ok] no Douyin process is running");
                return 0;
            }

            var r = ProcessKiller.KillAll(root, 2500, 5000, delegate (string m) { Console.WriteLine("[*] " + m); });
            foreach (var f in r.Failures) Console.Error.WriteLine("[x] " + f);

            if (r.AllGone) { Console.WriteLine("[ok] all gone"); return 0; }
            Console.Error.WriteLine("[x] " + r.Remaining.Count + " still running");
            return 3;
        }
    }
}
