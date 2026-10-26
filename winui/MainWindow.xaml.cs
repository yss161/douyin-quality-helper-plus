using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DouyinQualityHelper
{
    /// <summary>
    /// 主窗口。界面为 WinUI 3（Fluent），业务逻辑复用共享的 *Helper 类。
    ///
    /// 与旧 WPF 版的差异：
    ///   - 用 x:Name 生成的字段（不再 FindName）
    ///   - 文件选择用 StoragePicker + 窗口句柄（WinUI 非打包应用必需）
    ///   - 定时刷新用 DispatcherQueueTimer
    ///   - 弹窗用 ContentDialog（WinUI 没有 MessageBox）
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        string _installRoot;
        string _hintDir;
        VersionInfo _versionInfo;
        bool _busy;
        DispatcherTimer _watch;
        UpdateInfo _updateInfo;
        bool _updateChecking;

        public MainWindow()
        {
            InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(null);

            Title = "抖音画质助手";
            try { AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 1000, Height = 860 }); } catch { }

            BtnBrowse.Click += (s, e) => InvokeBrowse();
            BtnInstall.Click += async (s, e) => await InvokeInstallAsync();
            BtnRestore.Click += async (s, e) => await InvokeRestoreAsync();
            BtnCheck.Click += (s, e) => InvokeCheck();
            BtnRefresh.Click += (s, e) =>
            {
                LogBox.Text = "";
                WriteLog("状态已刷新。");
                UpdateVersionInfo(_installRoot);
                UpdateView();
            };
            BtnKill.Click += (s, e) => KillDouyinProcesses(true, true);
            BtnUploadScript.Click += async (s, e) => await PickAndUploadScriptAsync();
            BtnResetScript.Click += async (s, e) => await ResetScriptAsync();
            BtnCheckUpdate.Click += async (s, e) => await CheckUpdateAsync(true);
            BtnOpenRelease.Click += (s, e) => OpenReleasePage();

            ModeBoth.Checked += (s, e) => { Config.SaveInjectMode(InjectMode.Both); WriteLog("注入方式：最高画质 + 抖音优化脚本"); };
            ModeQuality.Checked += (s, e) => { Config.SaveInjectMode(InjectMode.QualityOnly); WriteLog("注入方式：只装最高画质"); };
            ModeScript.Checked += (s, e) => { Config.SaveInjectMode(InjectMode.ScriptOnly); WriteLog("注入方式：只装抖音优化脚本"); };

            RootGrid.Loaded += async (s, e) =>
            {
                OnLoadedInternal();
                // 启动后自动查一次更新（静默，失败不打扰）
                await CheckUpdateAsync(false);
            };
        }

        void OnLoadedInternal()
        {
            try
            {
                AppPaths.EnsureVersionsFile();
                string toolVer = "v" + VersionChecker.GetToolVersion();
                ToolVersionText.Text = toolVer;
                FootText.Text = "数据目录：" + AppPaths.AppDir + "  ｜  " + toolVer + "  ｜  WinUI 3";

                WriteLog("工具已就绪。请先点「浏览」选择 douyin.exe。");
                string hint = DouyinLocator.FindHintDir();
                if (hint != null)
                {
                    _hintDir = hint;
                    WriteLog("检测到抖音可能在：" + hint);
                    WriteLog("请点「浏览」选中 douyin.exe 以确认。");
                }
                else
                {
                    WriteLog("未能自动找到抖音，请用「浏览」手动定位。", "warn");
                }

                SetInstallRoot(null, false);
                SetStatus("?", "请先选择抖音程序", "点下面的「浏览」，选中抖音安装目录里的 douyin.exe", "#CA5010");

                ApplyModeToUi(Config.ReadInjectMode());

                UpdateView();
                StartWatcher();
            }
            catch (Exception ex)
            {
                WriteLog("初始化失败：" + ex.Message, "err");
            }
        }

        // =====================================================================
        // 日志与状态
        // =====================================================================
        void WriteLog(string msg, string level = "info")
        {
            string tag = level == "err" ? "[ ERR  ]" : level == "warn" ? "[ WARN ]" : level == "ok" ? "[  OK  ]" : "[ INFO ]";
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + tag + " " + msg + Environment.NewLine;
            LogBox.Text += line;

            // 自动滚到底（TextBox 没有 ScrollToEnd，用 Select + 偏移模拟）
            try
            {
                LogBox.SelectionStart = LogBox.Text.Length;
                LogBox.SelectionLength = 0;
            }
            catch { }
        }

        void SetStatus(string icon, string text, string detail, string colorHex)
        {
            StatusIcon.Glyph = GlyphFor(icon);
            StatusIcon.Foreground = BrushFromHex(colorHex);
            StatusText.Text = text;
            StatusDetail.Text = detail;
        }

        static string GlyphFor(string icon)
        {
            switch (icon)
            {
                case "?": return "\uE9CE";   // Help
                case "o": return "\uE946";   // Info
                case "*": return "\uE930";   // Completed
                case "!": return "\uE7BA";   // Warning
                case "x": return "\uEA39";   // Error
                default: return "\uE9CE";
            }
        }

        static SolidColorBrush BrushFromHex(string hex)
        {
            try
            {
                hex = (hex ?? "").TrimStart('#');
                if (hex.Length == 6)
                {
                    byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                    byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                    byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                    return new SolidColorBrush(ColorHelper.FromArgb(255, r, g, b));
                }
            }
            catch { }
            return new SolidColorBrush(ColorHelper.FromArgb(255, 0x5D, 0x5D, 0x5D));
        }

        void SetBusy(bool busy)
        {
            _busy = busy;
            BtnInstall.IsEnabled = !busy;
            BtnRestore.IsEnabled = !busy;
            BtnCheck.IsEnabled = !busy;
            BtnRefresh.IsEnabled = !busy;
            BtnBrowse.IsEnabled = !busy;
            BtnKill.IsEnabled = !busy;
        }

        void StartWatcher()
        {
            if (_watch != null) return;
            _watch = DispatcherQueue.CreateTimer();
            _watch.Interval = TimeSpan.FromSeconds(2.5);
            _watch.Tick += (s, e) =>
            {
                if (_busy) return;
                try { UpdateRunState(); } catch { }
            };
            _watch.Start();
        }

        // =====================================================================
        // 运行状态
        // =====================================================================
        void UpdateRunState()
        {
            var found = ProcessKiller.Find(_installRoot);
            bool running = found.Count > 0;

            RunBadge.Text = running ? ("抖音运行中 · " + found.Count + " 个进程") : "抖音未运行";
            RunBadge.Foreground = BrushFromHex(running ? "#C42B1C" : "#0F7B0F");

            if (found.Count == 0)
            {
                KillCard.Visibility = Visibility.Collapsed;
            }
            else
            {
                KillCard.Visibility = Visibility.Visible;
                KillTitle.Text = "抖音正在运行：" + found.Count + " 个进程需要结束";
                KillDetail.Text = ProcessKiller.Describe(found) + "。只关窗口不算退出，点右侧按钮一键结束。";
            }
        }

        void UpdateScriptInfo()
        {
            try
            {
                string builtInBundle = AppPaths.ReadEmbeddedText("userscript.bundle.js");
                bool dummy;
                var builtInMeta = UserscriptBundler.ReadBundleMeta(builtInBundle, out dummy);

                if (!File.Exists(AppPaths.UserscriptBundle))
                {
                    ScriptValue.Text = builtInMeta == null ? "内置脚本（未检出）" : builtInMeta.Summary() + "（内置）";
                    ScriptPathValue.Text = "使用内置副本（无外部文件）";
                    BtnResetScript.IsEnabled = false;
                    return;
                }

                string live = File.ReadAllText(AppPaths.UserscriptBundle, Encoding.UTF8);
                bool builtIn;
                var meta = UserscriptBundler.ReadBundleMeta(live, out builtIn);

                long liveVer = UserscriptBundler.ReadBundleVersion(live);
                long builtInVer = UserscriptBundler.ReadEmbeddedBundleVersion();

                string suffix;
                if (liveVer > builtInVer) suffix = "（外部，更新）";
                else if (liveVer > 0 && liveVer == builtInVer) suffix = "（外部，与内置相同）";
                else suffix = "（外部比内置旧，实际用内置）";

                if (builtInMeta != null && meta != null &&
                    (!string.Equals(meta.Version, builtInMeta.Version, StringComparison.Ordinal) ||
                     !string.Equals(meta.Name, builtInMeta.Name, StringComparison.Ordinal)))
                    suffix += "·已上传";

                ScriptValue.Text = (meta == null ? "无法识别" : meta.Summary()) + suffix;
                ScriptPathValue.Text = AppPaths.UserscriptBundle;
                BtnResetScript.IsEnabled = true;
            }
            catch (Exception ex)
            {
                ScriptValue.Text = "无法读取：" + ex.Message;
                ScriptPathValue.Text = "-";
                BtnResetScript.IsEnabled = false;
            }
        }

        // =====================================================================
        // 视图刷新
        // =====================================================================
        void UpdateView()
        {
            UpdateScriptInfo();

            if (string.IsNullOrEmpty(_installRoot))
            {
                SetStatus("?", "请先选择抖音程序", "点下面的「浏览」，选中抖音安装目录里的 douyin.exe", "#CA5010");
                BtnInstall.IsEnabled = false;
                BtnRestore.IsEnabled = false;
                BtnCheck.IsEnabled = false;
                BtnBrowse.IsEnabled = true;
                if (string.IsNullOrEmpty(PathBox.Text)) PathBox.Text = "尚未选择";
                VersionValue.Text = "待选择（选好后自动识别）";
                PreloadValue.Text = "-";
                AsarValue.Text = "-";
                BackupValue.Text = "-";
                UpdateRunState();
                return;
            }

            InstallPaths p;
            try { p = DouyinLocator.GetInstallPaths(_installRoot); }
            catch (Exception ex)
            {
                SetStatus("!", "抖音目录有问题", ex.Message, "#C42B1C");
                BtnInstall.IsEnabled = false;
                BtnRestore.IsEnabled = false;
                BtnCheck.IsEnabled = false;
                UpdateRunState();
                return;
            }

            bool patched = false;
            if (File.Exists(p.Preload))
            {
                try { patched = Patcher.HasMark(File.ReadAllText(p.Preload, Encoding.UTF8)); }
                catch { }
            }

            if (_versionInfo != null)
            {
                VersionValue.Text = _versionInfo.Label;
                VersionValue.Foreground = BrushFromHex(StatusColorHex(_versionInfo.Status));
            }
            else
            {
                VersionValue.Text = "未检查";
                VersionValue.Foreground = BrushFromHex("#5D5D5D");
            }

            if (!File.Exists(p.Preload))
            {
                SetStatus("x", "找不到 preload.js", p.Preload, "#C42B1C");
                PreloadValue.Text = "缺失";
                BtnInstall.IsEnabled = false;
                BtnRestore.IsEnabled = false;
            }
            else if (patched)
            {
                SetStatus("*", "最高画质已启用", "打开抖音，视频会用可用的最高清晰度", "#0F7B0F");
                PreloadValue.Text = "已打补丁";
                BtnInstall.IsEnabled = !DouyinLocator.IsDouyinRunning(_installRoot);
                BtnRestore.IsEnabled = !DouyinLocator.IsDouyinRunning(_installRoot);
            }
            else
            {
                SetStatus("o", "尚未启用", "点上面的「一键启用最高画质」", "#CA5010");
                PreloadValue.Text = "官方原版";
                BtnInstall.IsEnabled = !DouyinLocator.IsDouyinRunning(_installRoot);
                BtnRestore.IsEnabled = false;
            }

            // app.asar 与 md5.json 对账
            string asarState = "未知";
            try
            {
                if (File.Exists(p.Md5Json))
                {
                    var manifest = JsonUtil.AsDict(JsonUtil.Parse(File.ReadAllText(p.Md5Json, Encoding.UTF8)));
                    var files = JsonUtil.AsDict(JsonUtil.Get(manifest, "files"));
                    string expected = files != null && files.ContainsKey("resources\\app.asar")
                        ? Convert.ToString(files["resources\\app.asar"]) : null;
                    if (!string.IsNullOrEmpty(expected))
                        asarState = HashUtil.GetMd5(p.Asar) == expected ? "官方原版（未改动）" : "与 md5.json 不符";
                }
            }
            catch { }
            AsarValue.Text = asarState;

            bool hasBackup = File.Exists(AppPaths.OrigPreload) &&
                             HashUtil.GetMd5(AppPaths.OrigPreload) == Patcher.ORIG_PRELOAD_MD5;
            BackupValue.Text = hasBackup ? "已固化（可一键还原）" : "尚未固化（首次启用时自动备份）";

            bool canInstall = _versionInfo == null || _versionInfo.IsSupported;
            BtnInstall.IsEnabled = canInstall && !DouyinLocator.IsDouyinRunning(_installRoot);
            BtnCheck.IsEnabled = File.Exists(AppPaths.PageLog);
            BtnBrowse.IsEnabled = true;

            UpdateRunState();

            if (_versionInfo != null && !_versionInfo.IsSupported)
            {
                SetStatus("x", "此抖音版本不受支持", _versionInfo.Detail, "#C42B1C");
                BtnInstall.IsEnabled = false;
            }
        }

        static string StatusColorHex(string status)
        {
            switch (status)
            {
                case "verified": return "#0F7B0F";
                case "unsupported": return "#C42B1C";
                default: return "#CA5010";
            }
        }

        // 后续方法（浏览/启用/还原/检查/结束进程/脚本管理/更新）在 MainWindow.Actions.cs
    }
}
