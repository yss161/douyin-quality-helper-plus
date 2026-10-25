using System;
using System.IO;
using System.Text;

namespace DouyinQualityHelper
{
    /// <summary>注入内容：启用补丁时到底装什么。</summary>
    public enum InjectMode
    {
        /// <summary>画质强制 + 抖音优化脚本（默认）。</summary>
        Both = 0,
        /// <summary>只装原项目的自动最高画质，不动脚本。</summary>
        QualityOnly = 1,
        /// <summary>只装抖音优化脚本，不强制画质。</summary>
        ScriptOnly = 2,
    }

    public static class Config
    {
        public static string ReadDouyinDir()
        {
            try
            {
                if (!File.Exists(AppPaths.ConfigFile)) return null;
                var d = JsonUtil.AsDict(JsonUtil.Parse(File.ReadAllText(AppPaths.ConfigFile, Encoding.UTF8)));
                string dir = JsonUtil.GetString(d, "douyinDir");
                return string.IsNullOrEmpty(dir) ? null : dir;
            }
            catch { return null; }
        }

        public static InjectMode ReadInjectMode()
        {
            try
            {
                if (!File.Exists(AppPaths.ConfigFile)) return InjectMode.Both;
                var d = JsonUtil.AsDict(JsonUtil.Parse(File.ReadAllText(AppPaths.ConfigFile, Encoding.UTF8)));
                string raw = JsonUtil.GetString(d, "injectMode");
                if (string.IsNullOrEmpty(raw)) return InjectMode.Both;
                switch (raw)
                {
                    case "quality": return InjectMode.QualityOnly;
                    case "script": return InjectMode.ScriptOnly;
                    default: return InjectMode.Both;
                }
            }
            catch { return InjectMode.Both; }
        }

        public static string InjectModeKey(InjectMode mode)
        {
            switch (mode)
            {
                case InjectMode.QualityOnly: return "quality";
                case InjectMode.ScriptOnly: return "script";
                default: return "both";
            }
        }

        public static string InjectModeLabel(InjectMode mode)
        {
            switch (mode)
            {
                case InjectMode.QualityOnly: return "只装最高画质";
                case InjectMode.ScriptOnly: return "只装抖音优化脚本";
                default: return "最高画质 + 抖音优化脚本";
            }
        }

        /// <summary>
        /// 保存目录与注入方式。两者同在一个 config.json 里，所以每次都要把
        /// 另一项原样带上 —— 只写一半会把它抹掉。
        /// </summary>
        public static bool Save(string douyinDir, InjectMode mode)
        {
            try
            {
                var obj = new System.Collections.Generic.Dictionary<string, object>
                {
                    { "douyinDir", douyinDir ?? "" },
                    { "injectMode", InjectModeKey(mode) },
                    { "savedAt", DateTime.Now.ToString("o") }
                };
                File.WriteAllText(AppPaths.ConfigFile, JsonUtil.Serialize(obj), new UTF8Encoding(false));
                return true;
            }
            catch { return false; }
        }

        public static bool SaveDouyinDir(string dir)
        {
            return Save(dir, ReadInjectMode());
        }

        public static bool SaveInjectMode(InjectMode mode)
        {
            return Save(ReadDouyinDir(), mode);
        }
    }
}
