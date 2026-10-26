using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DouyinQualityHelper
{
    /// <summary>
    /// bundle 里"脚本本体"那一段的可替换操作。
    ///
    /// bundle 结构固定为：
    ///   GM 兼容层 + UMD 依赖 + [SCRIPT-BEGIN] 元信息 + 用户脚本 [SCRIPT-END]
    ///
    /// 上传新脚本时只替换两个标记之间的内容，依赖部分原样保留 ——
    /// 这样用户拿到的官方 .user.js 可以直接换进去，不用重新打包。
    /// 标记必须与 tools/build-userscript.js 里的常量完全一致。
    /// </summary>
    public static class UserscriptBundler
    {
        public const string SCRIPT_BEGIN = "/* ====== SCRIPT-BEGIN ====== */";
        public const string SCRIPT_END = "/* ====== SCRIPT-END ====== */";

        /// <summary>脚本元信息，来自 ==UserScript== 头部。</summary>
        public sealed class ScriptMeta
        {
            public string Name = "";
            public string Version = "";
            public string Description = "";
            public string Author = "";
            public List<string> Match = new List<string>();

            public string Summary()
            {
                if (string.IsNullOrEmpty(Name) && string.IsNullOrEmpty(Version)) return "未知脚本";
                return Name + (string.IsNullOrEmpty(Version) ? "" : " v" + Version);
            }
        }

        /// <summary>
        /// 解析 UserScript 头部。不是用户脚本（没有 ==UserScript== 头或没有 @name）返回 null。
        /// </summary>
        public static ScriptMeta ParseHeader(string source)
        {
            if (string.IsNullOrEmpty(source)) return null;
            int open = source.IndexOf("==UserScript==", StringComparison.Ordinal);
            if (open < 0) return null;
            int close = source.IndexOf("==/UserScript==", open, StringComparison.Ordinal);
            if (close < 0) return null;

            var meta = new ScriptMeta();
            string block = source.Substring(open, close - open);
            foreach (var rawLine in block.Split('\n'))
            {
                string line = rawLine.Trim().TrimEnd('\r').Trim();
                if (!line.StartsWith("//", StringComparison.Ordinal)) continue;
                line = line.Substring(2).Trim();
                if (line.Length == 0 || line[0] != '@') continue;

                int sp = line.IndexOf(' ');
                string key = sp < 0 ? line.Substring(1) : line.Substring(1, sp - 1);
                string val = sp < 0 ? "" : line.Substring(sp + 1).Trim();

                switch (key)
                {
                    case "name": if (meta.Name.Length == 0) meta.Name = val; break;
                    case "version": if (meta.Version.Length == 0) meta.Version = val; break;
                    case "description": if (meta.Description.Length == 0) meta.Description = val; break;
                    case "author": if (meta.Author.Length == 0) meta.Author = val; break;
                    case "match": meta.Match.Add(val); break;
                }
            }
            if (meta.Name.Length == 0) return null;
            return meta;
        }

        /// <summary>bundle 里当前装的是哪个脚本（解析 __DY_SCRIPT_META__）。</summary>
        public static ScriptMeta ReadBundleMeta(string bundle, out bool builtIn)
        {
            builtIn = true;
            if (string.IsNullOrEmpty(bundle)) return null;

            const string marker = "window.__DY_SCRIPT_META__ = ";
            int i = bundle.IndexOf(marker, StringComparison.Ordinal);
            if (i >= 0)
            {
                int start = i + marker.Length;
                int end = bundle.IndexOf(';', start);
                if (end > start)
                {
                    try
                    {
                        var d = JsonUtil.AsDict(JsonUtil.Parse(bundle.Substring(start, end - start)));
                        var meta = new ScriptMeta
                        {
                            Name = JsonUtil.GetString(d, "name"),
                            Version = JsonUtil.GetString(d, "version"),
                            Description = JsonUtil.GetString(d, "description"),
                            Author = JsonUtil.GetString(d, "author"),
                        };
                        if (!string.IsNullOrEmpty(meta.Name)) return meta;
                    }
                    catch { }
                }
            }

            // 老 bundle 没有 __DY_SCRIPT_META__：退回解析 SCRIPT 段里的 @name
            string section = ExtractScriptSection(bundle);
            var parsed = ParseHeader(section);
            if (parsed != null) return parsed;

            // 连头部都没有：报个占位
            builtIn = false;
            var fallback = new ScriptMeta();
            fallback.Name = "抖音优化";
            return fallback;
        }

        /// <summary>取出两个标记之间的脚本段（含元信息行）。</summary>
        public static string ExtractScriptSection(string bundle)
        {
            if (string.IsNullOrEmpty(bundle)) return "";
            int i = bundle.IndexOf(SCRIPT_BEGIN, StringComparison.Ordinal);
            if (i < 0) return "";
            int start = i + SCRIPT_BEGIN.Length;
            int j = bundle.IndexOf(SCRIPT_END, start, StringComparison.Ordinal);
            if (j < 0) return "";
            return bundle.Substring(start, j - start);
        }

        /// <summary>
        /// 把 bundle 里的脚本段换成 newScript，返回新 bundle。
        /// 依赖部分（GM 层 + UMD 库）原样保留。
        /// </summary>
        public static string ReplaceScript(string bundle, string newScript, ScriptMeta meta)
        {
            if (string.IsNullOrEmpty(bundle))
                throw new InvalidOperationException("当前脚本包为空，请先「① 一键启用最高画质」安装一次。");
            if (string.IsNullOrEmpty(newScript))
                throw new InvalidOperationException("脚本内容为空。");

            int i = bundle.IndexOf(SCRIPT_BEGIN, StringComparison.Ordinal);
            if (i < 0)
                throw new InvalidOperationException("脚本包里找不到 SCRIPT-BEGIN 标记，请重新安装一次补丁。");
            int start = i + SCRIPT_BEGIN.Length;
            int j = bundle.IndexOf(SCRIPT_END, start, StringComparison.Ordinal);
            if (j < 0)
                throw new InvalidOperationException("脚本包里找不到 SCRIPT-END 标记，请重新安装一次补丁。");

            var sb = new StringBuilder();
            sb.Append(bundle, 0, start);
            sb.Append('\n');
            if (meta != null)
                sb.Append("window.__DY_SCRIPT_META__ = ").Append(BuildMetaJson(meta)).Append(';').Append('\n');
            sb.Append(newScript);
            if (!newScript.EndsWith("\n", StringComparison.Ordinal)) sb.Append('\n');
            sb.Append(bundle, j, bundle.Length - j);
            return sb.ToString();
        }

        /// <summary>
        /// 读 bundle 里的 __DY_BUNDLE_VERSION__（构建时间戳，秒级单调递增）。
        /// 装载器用同一个值比较"外部 vs 内置"哪份更新。没有标记的老 bundle 返回 0。
        /// </summary>
        public static long ReadBundleVersion(string bundle)
        {
            if (string.IsNullOrEmpty(bundle)) return 0;
            const string marker = "__DY_BUNDLE_VERSION__";
            int i = bundle.IndexOf(marker, StringComparison.Ordinal);
            if (i < 0) return 0;

            int eq = bundle.IndexOf('=', i + marker.Length);
            if (eq < 0) return 0;

            int p = eq + 1;
            while (p < bundle.Length && (bundle[p] == ' ' || bundle[p] == '\t')) p++;

            int start = p;
            while (p < bundle.Length && bundle[p] >= '0' && bundle[p] <= '9') p++;
            if (p <= start) return 0;

            long v;
            if (!long.TryParse(bundle.Substring(start, p - start), out v)) return 0;
            return v;
        }

        /// <summary>
        /// EXE 内置副本的版本号（从内嵌的 userscript.bundle.js 读）。
        /// 读不到时返回 0，界面会因此显示"外部更新"。
        /// </summary>
        public static long ReadEmbeddedBundleVersion()
        {
            try
            {
                return ReadBundleVersion(AppPaths.ReadEmbeddedText("userscript.bundle.js"));
            }
            catch { return 0; }
        }

        static string BuildMetaJson(ScriptMeta meta)
        {
            var d = new Dictionary<string, object>
            {
                { "name", meta.Name },
                { "version", meta.Version },
                { "description", meta.Description },
                { "author", meta.Author },
            };
            return JsonUtil.Serialize(d);
        }

        /// <summary>
        /// 快速判断一段文本是不是"看起来像用户脚本"：要能解析出 @name。
        /// </summary>
        public static bool LooksLikeUserscript(string source, out ScriptMeta meta, out string error)
        {
            meta = null;
            error = null;
            if (string.IsNullOrWhiteSpace(source))
            {
                error = "文件是空的。";
                return false;
            }
            if (source.IndexOf("==UserScript==", StringComparison.Ordinal) < 0)
            {
                error = "文件里没有 ==UserScript== 头部，看起来不是油猴脚本。\n\n请选中从脚本仓库下载的 .user.js 文件。";
                return false;
            }
            meta = ParseHeader(source);
            if (meta == null)
            {
                error = "脚本头部里没有 @name，无法识别。";
                return false;
            }
            return true;
        }
    }
}
