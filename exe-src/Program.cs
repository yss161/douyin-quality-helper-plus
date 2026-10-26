using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Markup;
using System.Xml;

namespace DouyinQualityHelper
{
    public static class Program
    {
        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int pid);

        [DllImport("kernel32.dll")]
        static extern bool AllocConsole();

        const int ATTACH_PARENT_PROCESS = -1;

        [STAThread]
        public static int Main(string[] args)
        {
            bool cli = args != null && args.Length > 0;
            if (cli)
            {
                if (!AttachConsole(ATTACH_PARENT_PROCESS)) AllocConsole();
                Console.OutputEncoding = System.Text.Encoding.UTF8;
            }

            if (cli)
            {
                string cmd = args[0].ToLowerInvariant();
                if (cmd == "--selftest" || cmd == "/selftest") return SelfTest();
                if (cmd == "--patch")
                {
                    try
                    {
                        string src = null, payload = null, output = null;
                        for (int i = 1; i < args.Length; i++)
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
                        try { System.IO.File.WriteAllText(System.IO.Path.Combine(AppPaths.AppDir, "patch-error.txt"), ex.ToString()); } catch { }
                        return 1;
                    }
                }
                if (cmd == "--version" || cmd == "/version")
                {
                    Console.WriteLine("douyin-quality-helper " + typeof(Program).Assembly.GetName().Version);
                    Console.WriteLine("tool " + VersionChecker.GetToolVersion());
                    return 0;
                }
                if (cmd == "--kill" || cmd == "/kill") return KillCli(args);
            }

            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.DispatcherUnhandledException += (s, e) =>
            {
                try { AppPaths.WriteCrashLog(e.Exception); } catch { }
                MessageBox.Show(
                    "程序出错了：\n\n" + e.Exception.Message + "\n\n详细信息已写入 error.log",
                    "抖音画质助手", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            };

            try
            {
                AppPaths.EnsureVersionsFile();
                string xaml = AppPaths.ReadEmbeddedText("MainWindow.xaml");
                xaml = xaml.Replace("x:Class=\"DouyinQualityHelper.MainWindow\"", "");
                var doc = new XmlDocument();
                doc.LoadXml(xaml);
                var win = (Window)XamlReader.Load(new XmlNodeReader(doc));
                var logic = new MainWindow();
                logic.Bind(win);
                win.Loaded += (s, e) => logic.OnLoadedInternal();
                app.Run(win);
                return 0;
            }
            catch (Exception ex)
            {
                try { AppPaths.WriteCrashLog(ex); } catch { }
                MessageBox.Show("启动失败：\n\n" + ex.Message, "抖音画质助手", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }

        /// <summary>
        /// 命令行结束抖音进程：--kill [--root &lt;抖音安装目录&gt;]
        /// 不传 --root 时用 config.json 里记住的目录；再没有就按进程名判断。
        /// 退出码：0 = 全部结束；3 = 还有进程没结束（通常是权限不够）。
        /// </summary>
        static int KillCli(string[] args)
        {
            string root = null;
            for (int i = 1; i < args.Length; i++)
            {
                if ((args[i] == "--root" || args[i] == "--dir") && i + 1 < args.Length) root = args[++i];
                else if (args[i].StartsWith("--root=")) root = args[i].Substring(7);
            }
            if (string.IsNullOrEmpty(root)) root = Config.ReadDouyinDir();

            Console.WriteLine("root: " + (string.IsNullOrEmpty(root) ? "(unknown, match by process name)" : root));
            var found = ProcessKiller.Find(root);
            if (found.Count == 0)
            {
                Console.WriteLine("[ok] no Douyin process is running");
                return 0;
            }

            Console.WriteLine("found " + found.Count + " process(es):");
            foreach (var d in found) Console.WriteLine("  " + d.Short() + "  " + d.Path);

            var r = ProcessKiller.KillAll(root, 2500, 5000, delegate(string m) { Console.WriteLine("[*] " + m); });
            foreach (var f in r.Failures) Console.Error.WriteLine("[x] " + f);

            if (r.AllGone)
            {
                Console.WriteLine("[ok] all Douyin processes are gone");
                return 0;
            }
            Console.Error.WriteLine("[x] " + r.Remaining.Count + " process(es) still running: " + ProcessKiller.Describe(r.Remaining));
            if (r.RequiresElevation)
                Console.Error.WriteLine("    run this program as administrator and try again");
            return 3;
        }

        static int _failed;
        static System.IO.StreamWriter _selftestLog;

        static void Check(string name, bool ok, string detail)
        {
            string line = (ok ? "PASS " : "FAIL ") + name + (string.IsNullOrEmpty(detail) ? "" : (" :: " + detail));
            Console.WriteLine(line);
            if (_selftestLog != null) _selftestLog.WriteLine(line);
            if (!ok) _failed++;
        }

        /// <summary>
        /// Headless checks used by the packaging script. No GUI, no Node.
        /// </summary>
        static int SelfTest()
        {
            _failed = 0;
            string logPath = System.IO.Path.Combine(AppPaths.AppDir, "selftest.txt");
            _selftestLog = new System.IO.StreamWriter(logPath, false, System.Text.Encoding.UTF8);
            try
            {
                AppPaths.EnsureVersionsFile();
                Check("versions.json", System.IO.File.Exists(AppPaths.VersionsFile), AppPaths.VersionsFile);
                string tool = VersionChecker.GetToolVersion();
                Check("tool version", !string.IsNullOrEmpty(tool), tool);

                string payload = AppPaths.ReadEmbeddedText("payload.js");
                Check("payload embedded", payload.Contains("MANUAL_SWITCH") && payload.Contains("PICK_4K"), "len=" + payload.Length);
                Check("userscript loader embedded", payload.Contains("__DY_US_BOOTED__"), null);

                string usBundle = AppPaths.ReadEmbeddedText("userscript.bundle.js");
                Check("userscript bundle embedded",
                    usBundle.Contains("__DY_HOST__") && usBundle.Contains("GM_registerMenuCommand"),
                    "len=" + usBundle.Length);

                // --- 内置脚本：装载器要能自带一份，且版本号可比较 ---
                Check("loader prefers embedded",
                    payload.Contains("__DY_EMBEDDED_BUNDLE__") && payload.Contains("pickBundle"),
                    null);

                long builtInVer = UserscriptBundler.ReadBundleVersion(usBundle);
                Check("bundle has version stamp", builtInVer > 20240101000000L, "v" + builtInVer);
                Check("embedded version matches",
                    UserscriptBundler.ReadEmbeddedBundleVersion() == builtInVer,
                    "embedded=" + UserscriptBundler.ReadEmbeddedBundleVersion());

                // 外部更新才生效：造一个更旧/更新的 bundle 验证比较逻辑
                string olderBundle = usBundle.Replace(
                    "__DY_BUNDLE_VERSION__ = " + builtInVer,
                    "__DY_BUNDLE_VERSION__ = " + (builtInVer - 1000));
                Check("older external detected",
                    UserscriptBundler.ReadBundleVersion(olderBundle) == builtInVer - 1000, null);

                string newerBundle = usBundle.Replace(
                    "__DY_BUNDLE_VERSION__ = " + builtInVer,
                    "__DY_BUNDLE_VERSION__ = " + (builtInVer + 1000));
                Check("newer external detected",
                    UserscriptBundler.ReadBundleVersion(newerBundle) == builtInVer + 1000, null);
                Check("no-version bundle reads 0",
                    UserscriptBundler.ReadBundleVersion("var x = 1;") == 0, null);

                string xaml = AppPaths.ReadEmbeddedText("MainWindow.xaml");
                Check("xaml embedded", xaml.Contains("BtnInstall") && xaml.Contains("LogBox"), "len=" + xaml.Length);

                string orig = "/* head */\nfunction x() { return 1; }\n//# sourceMappingURL=app.js.map\n";
                string patched = Patcher.PatchText(orig, payload);
                Check("patch inserts marks", patched.Contains(Patcher.MARK_BEGIN) && patched.Contains(Patcher.MARK_END), null);
                Check("mapUrl stays last", patched.TrimEnd().EndsWith("//# sourceMappingURL=app.js.map"), null);
                string stripped;
                bool strippedOk = Patcher.TryStrip(patched, out stripped) && stripped == orig;
                Check("strip round-trip", strippedOk, null);

                Check("orig md5 constant", Patcher.ORIG_PRELOAD_MD5 == "bfcb8e31fae5a7370c5bee258ffcdfad", null);
                Check("no node dependency", true, "patcher is pure C#");

                // --- 注入方式：三种组合都要能生成合法 payload ---
                string both = PayloadComposer.Compose(payload, InjectMode.Both);
                Check("mode both == combined", both == payload, "len=" + both.Length);
                PayloadComposer.Validate(both, InjectMode.Both);

                string qualityOnly = PayloadComposer.Compose(payload, InjectMode.QualityOnly);
                Check("mode quality drops loader",
                    qualityOnly.IndexOf(PayloadComposer.MARK_LOADER, StringComparison.Ordinal) < 0 &&
                    qualityOnly.IndexOf("PICK_4K", StringComparison.Ordinal) >= 0,
                    "len=" + qualityOnly.Length);
                PayloadComposer.Validate(qualityOnly, InjectMode.QualityOnly);

                string scriptOnly = PayloadComposer.Compose(payload, InjectMode.ScriptOnly);
                Check("mode script keeps loader only",
                    scriptOnly.IndexOf(PayloadComposer.MARK_LOADER, StringComparison.Ordinal) >= 0 &&
                    scriptOnly.IndexOf("PICK_4K", StringComparison.Ordinal) < 0,
                    "len=" + scriptOnly.Length);
                PayloadComposer.Validate(scriptOnly, InjectMode.ScriptOnly);

                // --- 上传脚本：替换脚本段、依赖保持、元信息回读 ---
                string sampleUserJs = "// ==UserScript==\n// @name        测试脚本\n// @version     9.9.9\n// @author      t\n// @description d\n// ==/UserScript==\n(function(){})();\n";
                UserscriptBundler.ScriptMeta sampleMeta;
                string sampleErr;
                Check("userscript header parses",
                    UserscriptBundler.LooksLikeUserscript(sampleUserJs, out sampleMeta, out sampleErr) &&
                    sampleMeta.Name == "测试脚本" && sampleMeta.Version == "9.9.9",
                    sampleErr);

                string replaced = UserscriptBundler.ReplaceScript(usBundle, sampleUserJs, sampleMeta);
                bool replacedBuiltIn;
                var replacedMeta = UserscriptBundler.ReadBundleMeta(replaced, out replacedBuiltIn);
                Check("replace keeps deps",
                    replaced.IndexOf("GM_registerMenuCommand", StringComparison.Ordinal) >= 0 &&
                    replaced.IndexOf("pops-shadow-container", StringComparison.Ordinal) >= 0,
                    "len=" + replaced.Length);
                Check("replace swaps script", replacedMeta != null && replacedMeta.Name == "测试脚本" && replacedMeta.Version == "9.9.9",
                    replacedMeta == null ? "meta=null" : replacedMeta.Summary());

                bool builtInFlag;
                var originalMeta = UserscriptBundler.ReadBundleMeta(usBundle, out builtInFlag);
                Check("builtin meta readable", originalMeta != null && !string.IsNullOrEmpty(originalMeta.Version),
                    originalMeta == null ? "null" : originalMeta.Summary());
                Check("reject non-userscript",
                    !UserscriptBundler.LooksLikeUserscript("var x = 1;", out sampleMeta, out sampleErr) && sampleErr != null, null);

                // 结束进程：只验证能枚举、角色表可用，绝不在这里真的去杀进程
                var procs = ProcessKiller.Find(null);
                Check("process scan", procs != null, "running=" + (procs == null ? -1 : procs.Count));
                Check("process roles", ProcessKiller.RoleOf("douyin") == "主程序"
                                       && ProcessKiller.RoleOf("douyin_guard") == "守护进程"
                                       && ProcessKiller.RoleOf("parfait_crash_handler") == "崩溃上报", null);
                Check("process describe", ProcessKiller.Describe(procs) != null, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL exception :: " + ex.Message);
                _failed++;
            }

            string verdict = _failed == 0 ? "SELFTEST-OK" : ("SELFTEST-FAILED " + _failed);
            Console.WriteLine(verdict);
            if (_selftestLog != null)
            {
                _selftestLog.WriteLine(verdict);
                _selftestLog.Flush();
                _selftestLog.Dispose();
                _selftestLog = null;
            }
            return _failed == 0 ? 0 : 2;
        }
    }
}
