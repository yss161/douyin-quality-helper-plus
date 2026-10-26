using System;
using System.IO;

namespace DouyinQualityHelper
{
    /// <summary>
    /// 自检：不碰系统，只验证内嵌资源与纯逻辑。
    /// 用 --selftest 跑，输出到控制台并写 selftest.txt。
    /// </summary>
    public static class SelfTest
    {
        static int _failed;
        static StreamWriter _log;

        public static int Run(string appDir)
        {
            _failed = 0;
            string logPath = Path.Combine(appDir, "selftest.txt");
            try { _log = new StreamWriter(logPath, false, new System.Text.UTF8Encoding(false)); } catch { }

            try
            {
                AppPaths.EnsureVersionsFile();
                Check("versions.json", File.Exists(AppPaths.VersionsFile), AppPaths.VersionsFile);

                string tool = VersionChecker.GetToolVersion();
                Check("tool version", !string.IsNullOrEmpty(tool), tool);

                string payload = AppPaths.ReadEmbeddedText("payload.js");
                Check("payload embedded", payload.Contains("MANUAL_SWITCH") && payload.Contains("PICK_4K"), "len=" + payload.Length);
                Check("userscript loader embedded", payload.Contains("__DY_US_BOOTED__"), null);
                Check("loader prefers embedded",
                    payload.Contains("__DY_EMBEDDED_BUNDLE__") && payload.Contains("pickBundle"), null);

                string usBundle = AppPaths.ReadEmbeddedText("userscript.bundle.js");
                Check("userscript bundle embedded",
                    usBundle.Contains("__DY_HOST__") && usBundle.Contains("GM_registerMenuCommand"),
                    "len=" + usBundle.Length);

                long builtInVer = UserscriptBundler.ReadBundleVersion(usBundle);
                Check("bundle has version stamp", builtInVer > 20240101000000L, "v" + builtInVer);
                Check("embedded version matches",
                    UserscriptBundler.ReadEmbeddedBundleVersion() == builtInVer, null);

                string older = usBundle.Replace("__DY_BUNDLE_VERSION__ = " + builtInVer,
                                                "__DY_BUNDLE_VERSION__ = " + (builtInVer - 1000));
                Check("older external detected", UserscriptBundler.ReadBundleVersion(older) == builtInVer - 1000, null);
                Check("no-version bundle reads 0", UserscriptBundler.ReadBundleVersion("var x = 1;") == 0, null);

                string orig = "/* head */\nfunction x() { return 1; }\n//# sourceMappingURL=app.js.map\n";
                string patched = Patcher.PatchText(orig, payload);
                Check("patch inserts marks", patched.Contains(Patcher.MARK_BEGIN) && patched.Contains(Patcher.MARK_END), null);
                Check("mapUrl stays last", patched.TrimEnd().EndsWith("//# sourceMappingURL=app.js.map"), null);
                string stripped;
                Check("strip round-trip", Patcher.TryStrip(patched, out stripped) && stripped == orig, null);
                Check("orig md5 constant", Patcher.ORIG_PRELOAD_MD5 == "bfcb8e31fae5a7370c5bee258ffcdfad", null);

                string both = PayloadComposer.Compose(payload, InjectMode.Both);
                Check("mode both", both == payload, "len=" + both.Length);
                PayloadComposer.Validate(both, InjectMode.Both);

                string qOnly = PayloadComposer.Compose(payload, InjectMode.QualityOnly);
                Check("mode quality drops loader",
                    qOnly.IndexOf(PayloadComposer.MARK_LOADER, StringComparison.Ordinal) < 0 &&
                    qOnly.IndexOf("PICK_4K", StringComparison.Ordinal) >= 0, "len=" + qOnly.Length);
                PayloadComposer.Validate(qOnly, InjectMode.QualityOnly);

                string sOnly = PayloadComposer.Compose(payload, InjectMode.ScriptOnly);
                Check("mode script keeps loader only",
                    sOnly.IndexOf(PayloadComposer.MARK_LOADER, StringComparison.Ordinal) >= 0 &&
                    sOnly.IndexOf("PICK_4K", StringComparison.Ordinal) < 0, "len=" + sOnly.Length);
                PayloadComposer.Validate(sOnly, InjectMode.ScriptOnly);

                string sampleJs = "// ==UserScript==\n// @name  t\n// @version 1.0\n// ==/UserScript==\n(function(){})();\n";
                UserscriptBundler.ScriptMeta meta;
                string err;
                Check("userscript header parses", UserscriptBundler.LooksLikeUserscript(sampleJs, out meta, out err) && meta.Version == "1.0", err);
                Check("reject non-userscript", !UserscriptBundler.LooksLikeUserscript("var x=1;", out meta, out err), null);

                var procs = ProcessKiller.Find(null);
                Check("process scan", procs != null, "running=" + (procs == null ? -1 : procs.Count));
                Check("process roles", ProcessKiller.RoleOf("douyin") == "主程序", null);

                // 更新检测：纯逻辑（不发网络请求）
                Check("version compare newer", UpdateChecker.IsNewer("2.0.0", "1.7.0"), null);
                Check("version compare same", !UpdateChecker.IsNewer("2.0.0", "2.0.0"), null);
                Check("version compare older", !UpdateChecker.IsNewer("1.7.0", "2.0.0"), null);
                Check("tag normalize", UpdateChecker.NormalizeTag("v2.1.0") == "2.1.0", null);

                return _failed == 0 ? 0 : 1;
            }
            finally
            {
                if (_log != null) { try { _log.Flush(); _log.Dispose(); } catch { } }
                Console.WriteLine(_failed == 0 ? "SELFTEST-OK" : ("SELFTEST-FAILED " + _failed));
            }
        }

        static void Check(string name, bool ok, string detail)
        {
            string line = (ok ? "PASS " : "FAIL ") + name + (string.IsNullOrEmpty(detail) ? "" : " :: " + detail);
            Console.WriteLine(line);
            try { if (_log != null) _log.WriteLine(line); } catch { }
            if (!ok) _failed++;
        }
    }
}
