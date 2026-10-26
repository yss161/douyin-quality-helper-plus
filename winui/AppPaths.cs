using System;
using System.IO;
using System.Reflection;

namespace DouyinQualityHelper
{
    /// <summary>
    /// All writable data lives next to the EXE (portable green package).
    /// </summary>
    public static class AppPaths
    {
        /// <summary>
        /// EXE 所在目录（便携绿色包：配置/备份/日志都放这里）。
        ///
        /// 注意：不要用 Assembly.Location —— 单文件发布时它是空字符串，
        /// WinUI/自包含下也可能拿不到。AppContext.BaseDirectory 才是稳的。
        /// </summary>
        public static string AppDir
        {
            get
            {
                string dir = AppContext.BaseDirectory;
                if (string.IsNullOrEmpty(dir))
                {
                    try { dir = Path.GetDirectoryName(Environment.ProcessPath); } catch { }
                }
                if (string.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
                return dir.TrimEnd(Path.DirectorySeparatorChar);
            }
        }

        /// <summary>内嵌资源所在的程序集（用于 ReadEmbeddedText）。</summary>
        static Assembly ResourceAssembly
        {
            get { return typeof(AppPaths).Assembly; }
        }

        public static string ConfigFile { get { return Path.Combine(AppDir, "config.json"); } }
        public static string VersionsFile { get { return Path.Combine(AppDir, "versions.json"); } }
        public static string BackupDir { get { return Path.Combine(AppDir, "backup"); } }
        public static string OrigPreload { get { return Path.Combine(BackupDir, "loose-preload", "preload.js.orig"); } }
        public static string AsarOrig { get { return Path.Combine(BackupDir, "app.asar.orig"); } }
        public static string BuildDir { get { return Path.Combine(AppDir, "work", "build"); } }
        public static string UserData { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "douyin"); } }
        public static string PageLog { get { return Path.Combine(UserData, "dy-force.log"); } }
        public static string UserscriptBundle { get { return Path.Combine(UserData, "userscript.bundle.js"); } }

        /// <summary>装载器的开关文件：决定页面里跑不跑脚本 / 强制不强制画质。</summary>
        public static string InjectModeFile { get { return Path.Combine(UserData, "inject-mode.json"); } }

        /// <summary>用户上传的脚本备份，用于「恢复内置脚本」。</summary>
        public static string BuiltinBundle { get { return Path.Combine(AppDir, "userscript.builtin.js"); } }
        public static string CrashLog { get { return Path.Combine(AppDir, "error.log"); } }

        public static string ReadEmbeddedText(string name)
        {
            string full = "DouyinQualityHelper.Resources." + name;
            using (var s = ResourceAssembly.GetManifestResourceStream(full))
            {
                if (s == null) throw new FileNotFoundException("Embedded resource missing: " + name);
                using (var r = new StreamReader(s, System.Text.Encoding.UTF8))
                    return r.ReadToEnd();
            }
        }

        public static void EnsureVersionsFile()
        {
            if (File.Exists(VersionsFile)) return;
            Directory.CreateDirectory(AppDir);
            File.WriteAllText(VersionsFile, ReadEmbeddedText("versions.json"), new System.Text.UTF8Encoding(false));
        }

        public static void WriteCrashLog(Exception ex)
        {
            try
            {
                string text = DateTime.Now.ToString("o") + "\r\n" + ex + "\r\n\r\n";
                File.AppendAllText(CrashLog, text, new System.Text.UTF8Encoding(false));
            }
            catch { }
        }
    }

    public sealed class InstallPaths
    {
        public string Root { get; set; }
        public string VersionDir { get; set; }
        public string Preload { get; set; }
        public string Asar { get; set; }
        public string Md5Json { get; set; }
        public string LauncherConfig { get; set; }
    }
}
