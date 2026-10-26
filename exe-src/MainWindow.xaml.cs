using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace DouyinQualityHelper
{
    /// <summary>
    /// UI controller. The visual tree is loaded from embedded XAML at runtime,
    /// so this class does not depend on MSBuild-generated InitializeComponent.
    /// </summary>
    public sealed class MainWindow
    {
        Window _win;
        TextBlock StatusIcon, StatusText, StatusDetail, RunBadge, ToolVersionText;
        TextBlock VersionValue, PreloadValue, AsarValue, BackupValue;
        TextBox PathBox, LogBox;
        TextBlock FootText;
        TextBlock KillTitle, KillDetail;
        TextBlock ScriptValue, ScriptPathValue;
        RadioButton ModeBoth, ModeQuality, ModeScript;
        Button BtnBrowse, BtnInstall, BtnRestore, BtnCheck, BtnRefresh, BtnKill;
        Button BtnUploadScript, BtnResetScript;

        string _installRoot;
        string _hintDir;
        VersionInfo _versionInfo;
        bool _busy;
        ScrollViewer _rootScroll;
        System.Windows.Threading.DispatcherTimer _watch;

        public void Bind(Window win)
        {
            _win = win;
            StatusIcon = win.FindName("StatusIcon") as TextBlock;
            StatusText = win.FindName("StatusText") as TextBlock;
            StatusDetail = win.FindName("StatusDetail") as TextBlock;
            RunBadge = win.FindName("RunBadge") as TextBlock;
            ToolVersionText = win.FindName("ToolVersionText") as TextBlock;
            VersionValue = win.FindName("VersionValue") as TextBlock;
            PreloadValue = win.FindName("PreloadValue") as TextBlock;
            AsarValue = win.FindName("AsarValue") as TextBlock;
            BackupValue = win.FindName("BackupValue") as TextBlock;
            PathBox = win.FindName("PathBox") as TextBox;
            LogBox = win.FindName("LogBox") as TextBox;
            FootText = win.FindName("FootText") as TextBlock;
            BtnBrowse = win.FindName("BtnBrowse") as Button;
            BtnInstall = win.FindName("BtnInstall") as Button;
            BtnRestore = win.FindName("BtnRestore") as Button;
            BtnCheck = win.FindName("BtnCheck") as Button;
            BtnRefresh = win.FindName("BtnRefresh") as Button;
            BtnKill = win.FindName("BtnKill") as Button;
            _rootScroll = win.FindName("RootScroll") as ScrollViewer;
            ScriptValue = win.FindName("ScriptValue") as TextBlock;
            ScriptPathValue = win.FindName("ScriptPathValue") as TextBlock;
            ModeBoth = win.FindName("ModeBoth") as RadioButton;
            ModeQuality = win.FindName("ModeQuality") as RadioButton;
            ModeScript = win.FindName("ModeScript") as RadioButton;
            BtnUploadScript = win.FindName("BtnUploadScript") as Button;
            BtnResetScript = win.FindName("BtnResetScript") as Button;
            KillTitle = win.FindName("KillTitle") as TextBlock;
            KillDetail = win.FindName("KillDetail") as TextBlock;

            if (StatusIcon == null || BtnInstall == null || LogBox == null)
                throw new InvalidOperationException("gui xaml missing named controls");

            BtnBrowse.Click += (s, e) => InvokeBrowse();
            BtnInstall.Click += (s, e) => InvokeInstall();
            BtnRestore.Click += (s, e) => InvokeRestore();
            BtnCheck.Click += (s, e) => InvokeCheck();
            BtnKill.Click += (s, e) => { KillDouyinProcesses(true, true); };
            BtnUploadScript.Click += (s, e) => UploadScript();
            BtnResetScript.Click += (s, e) => ResetScript();
            ModeBoth.Checked += (s, e) => { Config.SaveInjectMode(InjectMode.Both); WriteLog("注入方式：最高画质 + 抖音优化脚本"); };
            ModeQuality.Checked += (s, e) => { Config.SaveInjectMode(InjectMode.QualityOnly); WriteLog("注入方式：只装最高画质"); };
            ModeScript.Checked += (s, e) => { Config.SaveInjectMode(InjectMode.ScriptOnly); WriteLog("注入方式：只装抖音优化脚本"); };
            BtnRefresh.Click += (s, e) =>
            {
                LogBox.Clear();
                WriteLog("状态已刷新。");
                UpdateVersionInfo(_installRoot);
                UpdateView();
            };
        }

        public void OnLoadedInternal()
        {
            try
            {
                AppPaths.EnsureVersionsFile();
                string toolVer = "v" + VersionChecker.GetToolVersion();
                if (ToolVersionText != null) ToolVersionText.Text = toolVer;
                if (FootText != null) FootText.Text = "数据目录：" + AppPaths.AppDir + "  ｜  " + toolVer + "  ｜  无需 Node.js";

                WriteLog("工具已就绪。请先点「浏览…」选择 douyin.exe。");
                string hint = DouyinLocator.FindHintDir();
                if (hint != null)
                {
                    _hintDir = hint;
                    WriteLog("检测到抖音可能在：" + hint);
                    WriteLog("请点「浏览…」选中 douyin.exe 以确认。");
                }
                else
                {
                    WriteLog("未能自动找到抖音，请用「浏览…」手动定位。", "warn");
                }

                SetInstallRoot(null, false);
                SetStatus("?", "请先选择抖音程序", "点下面的「浏览…」，选中抖音安装目录里的 douyin.exe", "#D25F00");

                // 记住上次选的注入方式
                ApplyModeToUi(Config.ReadInjectMode());

                UpdateView();
                StartWatcher();
                WriteLog("提示：douyin.exe 通常在 <抖音安装目录>\\douyin.exe");
                // 日志框的 ScrollToEnd 会把外层 ScrollViewer 一起带下去，这里拉回顶部
                if (_rootScroll != null) _rootScroll.ScrollToTop();
            }
            catch (Exception ex)
            {
                WriteLog("启动出错：" + ex.Message, "err");
            }
        }

        void WriteLog(string msg, string level = "info")
        {
            string ts = DateTime.Now.ToString("HH:mm:ss");
            string prefix = level == "ok" ? "[ OK ]"
                : level == "warn" ? "[WARN]"
                : level == "err" ? "[FAIL]"
                : "[INFO]";
            LogBox.AppendText("[" + ts + "] " + prefix + " " + msg + Environment.NewLine);
            LogBox.ScrollToEnd();
        }

        void SetStatus(string icon, string text, string detail, string color)
        {
            StatusIcon.Text = icon;
            StatusText.Text = text;
            try { StatusText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString(color); }
            catch { StatusText.Foreground = Brushes.Black; }
            StatusDetail.Text = detail ?? "";
        }

        void SetBusy(bool busy)
        {
            _busy = busy;
            _win.Cursor = busy ? System.Windows.Input.Cursors.Wait : System.Windows.Input.Cursors.Arrow;
            BtnInstall.IsEnabled = !busy;
            BtnRestore.IsEnabled = !busy;
            BtnCheck.IsEnabled = !busy;
            BtnRefresh.IsEnabled = !busy;
            BtnBrowse.IsEnabled = !busy;
            if (BtnKill != null) BtnKill.IsEnabled = !busy;
        }

        /// <summary>
        /// 定时看抖音还在不在，让徽标和"结束抖音进程"这条横幅自动跟上
        /// —— 用户在托盘里退出了抖音，界面不用再点刷新。
        /// </summary>
        void StartWatcher()
        {
            if (_watch != null) return;
            _watch = new System.Windows.Threading.DispatcherTimer();
            _watch.Interval = TimeSpan.FromSeconds(2.5);
            _watch.Tick += (s, e) =>
            {
                if (_busy) return;
                try { UpdateRunState(); } catch { }
            };
            _watch.Start();
        }

        /// <summary>刷新"抖音运行中/未运行"和结束进程横幅。</summary>
        void UpdateRunState()
        {
            var found = ProcessKiller.Find(_installRoot);
            bool running = found.Count > 0;
            if (RunBadge != null)
            {
                RunBadge.Text = running ? ("抖音运行中 · " + found.Count + " 个进程") : "抖音未运行";
                RunBadge.Foreground = running
                    ? new SolidColorBrush(Color.FromRgb(0xF5, 0x3F, 0x3F))
                    : new SolidColorBrush(Color.FromRgb(0x00, 0xB4, 0x2A));
            }
            UpdateKillBar(found);
        }

        /// <summary>
        /// "结束抖音进程"横幅：抖音没在运行时给出提示并禁掉按钮，
        /// 在运行时列出到底是哪几个进程占着文件。
        /// </summary>
        void UpdateKillBar(List<DouyinProcess> found)
        {
            if (KillTitle == null || KillDetail == null || BtnKill == null) return;
            if (found == null) found = ProcessKiller.Find(_installRoot);
            if (found.Count == 0)
            {
                KillTitle.Text = "抖音未运行";
                KillDetail.Text = "没有检测到抖音进程，可以直接启用或还原补丁。";
                BtnKill.IsEnabled = false;
                return;
            }
            KillTitle.Text = "抖音正在运行：" + found.Count + " 个进程需要结束";
            KillDetail.Text = ProcessKiller.Describe(found) + "。只关窗口不算退出，点右侧按钮一键结束。";
            BtnKill.IsEnabled = !_busy;
        }

        void SetInstallRoot(string root, bool persist)
        {
            _installRoot = root;
            if (persist && !string.IsNullOrEmpty(root)) Config.SaveDouyinDir(root);
            UpdateVersionInfo(root);
            PathBox.Text = string.IsNullOrEmpty(root) ? "尚未选择" : root;
        }

        void UpdateVersionInfo(string root)
        {
            _versionInfo = null;
            if (string.IsNullOrEmpty(root)) return;
            try { _versionInfo = VersionChecker.GetInfo(root); }
            catch (Exception ex) { WriteLog("版本检查失败：" + ex.Message, "warn"); }
        }

        void UpdateView()
        {
            if (string.IsNullOrEmpty(_installRoot))
            {
                SetStatus("?", "请先选择抖音程序", "点下面的「浏览…」，选中抖音安装目录里的 douyin.exe", "#D25F00");
                BtnInstall.IsEnabled = false;
                BtnRestore.IsEnabled = false;
                BtnCheck.IsEnabled = false;
                BtnBrowse.IsEnabled = true;
                if (string.IsNullOrEmpty(PathBox.Text)) PathBox.Text = "尚未选择";
                VersionValue.Text = "待选择（选好后自动识别）";
                VersionValue.Foreground = new SolidColorBrush(Color.FromRgb(0x86, 0x90, 0x9C));
                PreloadValue.Text = "-";
                AsarValue.Text = "-";
                BackupValue.Text = "-";
                UpdateRunState();
                // 未选目录时也要显示脚本状态：脚本包与抖音目录无关
                UpdateScriptInfo();
                return;
            }

            InstallPaths p;
            try { p = DouyinLocator.GetInstallPaths(_installRoot); }
            catch (Exception ex)
            {
                SetStatus("!", "抖音目录有问题", ex.Message, "#C0392B");
                BtnInstall.IsEnabled = false;
                BtnRestore.IsEnabled = false;
                BtnCheck.IsEnabled = false;
                UpdateScriptInfo();
                return;
            }

            string curMd5 = HashUtil.GetMd5(p.Preload);
            bool patched = false;
            if (File.Exists(p.Preload))
            {
                try { patched = Patcher.HasMark(File.ReadAllText(p.Preload, Encoding.UTF8)); }
                catch { }
            }

            if (_versionInfo != null)
            {
                VersionValue.Text = _versionInfo.Label;
                VersionValue.Foreground = StatusColor(_versionInfo.Status);
                if (_versionInfo.IsPatched || patched)
                {
                    PreloadValue.Text = "已启用最高画质补丁";
                    PreloadValue.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xB4, 0x2A));
                    SetStatus("✓", "最高画质已启用", "启动抖音看视频即可；没有 4K 的会自动降档。", "#00B42A");
                }
                else if (curMd5 == Patcher.ORIG_PRELOAD_MD5 || curMd5 == _versionInfo.PristineMd5)
                {
                    PreloadValue.Text = "官方原版（未启用补丁）";
                    PreloadValue.Foreground = new SolidColorBrush(Color.FromRgb(0x1D, 0x21, 0x29));
                    SetStatus("○", "尚未启用最高画质", "退出抖音后点「① 一键启用最高画质」。", "#165DFF");
                }
                else
                {
                    PreloadValue.Text = "文件与原版不一致（非本工具标记）";
                    PreloadValue.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x7D, 0x00));
                    SetStatus("!", "检测到未知修改", "建议先还原官方原版，再启用补丁。", "#D25F00");
                }
            }
            else
            {
                VersionValue.Text = Path.GetFileName(_installRoot.TrimEnd('\\'));
                PreloadValue.Text = patched ? "已启用补丁" : "未启用";
            }

            if (File.Exists(p.Asar))
            {
                string sha = HashUtil.GetSha256(p.Asar);
                AsarValue.Text = "sha256 " + (sha.Length >= 12 ? sha.Substring(0, 12) : sha) + "…（未改动）";
            }
            else AsarValue.Text = "未找到 app.asar";

            bool hasBackup = File.Exists(AppPaths.OrigPreload) &&
                             HashUtil.GetMd5(AppPaths.OrigPreload) == Patcher.ORIG_PRELOAD_MD5;
            BackupValue.Text = hasBackup ? "已固化（可一键还原）" : "尚未固化（首次启用时自动备份）";
            BackupValue.Foreground = hasBackup
                ? new SolidColorBrush(Color.FromRgb(0x00, 0xB4, 0x2A))
                : new SolidColorBrush(Color.FromRgb(0x86, 0x90, 0x9C));

            bool canInstall = _versionInfo == null || _versionInfo.IsSupported;
            BtnInstall.IsEnabled = canInstall;
            BtnRestore.IsEnabled = true;
            BtnCheck.IsEnabled = File.Exists(AppPaths.PageLog);
            BtnBrowse.IsEnabled = true;

            UpdateRunState();
            UpdateScriptInfo();

            if (_versionInfo != null && !_versionInfo.IsSupported)
            {
                SetStatus("×", "此抖音版本不受支持", _versionInfo.Detail, "#C0392B");
                BtnInstall.IsEnabled = false;
            }
        }

        static SolidColorBrush StatusColor(string status)
        {
            switch (status)
            {
                case "verified": return new SolidColorBrush(Color.FromRgb(0x00, 0xB4, 0x2A));
                case "unsupported": return new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));
                default: return new SolidColorBrush(Color.FromRgb(0xFF, 0x7D, 0x00));
            }
        }

        void InvokeBrowse()
        {
            var dlg = new OpenFileDialog
            {
                Title = "请选择抖音程序 douyin.exe",
                Filter = "douyin.exe|douyin.exe|可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            string hint = _installRoot ?? _hintDir;
            if (!string.IsNullOrEmpty(hint) && Directory.Exists(hint)) dlg.InitialDirectory = hint;

            if (dlg.ShowDialog(_win) != true)
            {
                WriteLog("已取消选择。", "warn");
                return;
            }

            string picked = dlg.FileName;
            WriteLog("已选择：" + picked);
            if (!Regex.IsMatch(Path.GetFileName(picked), @"^douyin.*\.exe$", RegexOptions.IgnoreCase))
                WriteLog("注意：文件名不叫 douyin.exe，继续尝试识别所在目录。", "warn");

            string root = DouyinLocator.ResolveInstallRoot(picked);
            if (root == null)
            {
                WriteLog("该文件不在抖音安装目录里，无法识别。", "err");
                MessageBox.Show(_win,
                    "该文件不在抖音安装目录中，无法识别。\n\n请选择抖音安装目录里的 douyin.exe，例如：\n  D:\\douyin\\douyin.exe\n  C:\\Program Files\\douyin\\douyin.exe",
                    "位置不正确", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SetInstallRoot(root, true);
            WriteLog("已确认抖音位置：" + root, "ok");
            WriteLog("接下来：退出抖音 → 点「① 一键启用最高画质」");
            UpdateView();
        }

        void InvokeInstall()
        {
            LogBox.Clear();
            WriteLog("开始启用最高画质…");
            if (string.IsNullOrEmpty(_installRoot))
            {
                WriteLog("尚未选择抖音位置，请先点「浏览…」。", "err");
                return;
            }

            InstallPaths p;
            try { p = DouyinLocator.GetInstallPaths(_installRoot); }
            catch (Exception ex) { WriteLog(ex.Message, "err"); return; }

            if (!EnsureDouyinClosed())
            {
                WriteLog("抖音仍在运行，已中止；补丁没有做任何改动。", "err");
                return;
            }

            SetBusy(true);
            try
            {
                WriteLog("使用安装目录：" + _installRoot);
                WriteLog("检查原始文件备份…");
                Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.OrigPreload));
                bool haveOrig = HashUtil.GetMd5(AppPaths.OrigPreload) == Patcher.ORIG_PRELOAD_MD5;

                if (haveOrig)
                {
                    WriteLog("原始备份已固化并通过校验。", "ok");
                }
                else if (HashUtil.GetMd5(p.Preload) == Patcher.ORIG_PRELOAD_MD5)
                {
                    File.Copy(p.Preload, AppPaths.OrigPreload, true);
                    haveOrig = true;
                    WriteLog("已创建原始文件备份。", "ok");
                }
                else
                {
                    WriteLog("文件已被修改，尝试剥离本工具的标记以还原原始内容…", "warn");
                    string txt = File.ReadAllText(p.Preload, Encoding.UTF8);
                    string stripped;
                    if (Patcher.TryStrip(txt, out stripped))
                    {
                        Directory.CreateDirectory(AppPaths.BuildDir);
                        string tmp = Path.Combine(AppPaths.BuildDir, "recovered-orig.js");
                        File.WriteAllText(tmp, stripped, new UTF8Encoding(false));
                        if (HashUtil.GetMd5(tmp) == Patcher.ORIG_PRELOAD_MD5)
                        {
                            File.Copy(tmp, AppPaths.OrigPreload, true);
                            haveOrig = true;
                            WriteLog("原始文件已成功恢复。", "ok");
                        }
                    }
                }

                if (!haveOrig)
                {
                    WriteLog("无法取得官方原始文件，已中止。", "err");
                    MessageBox.Show(_win,
                        "无法取得官方原始文件，因此没有做任何修改。\n\n请重新安装抖音客户端后再试。",
                        "启用失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                WriteLog("生成注入文件…");
                InjectMode mode = CurrentMode();
                WriteLog("注入内容：" + Config.InjectModeLabel(mode));

                string embedded = AppPaths.ReadEmbeddedText("payload.js");
                string payload = PayloadComposer.Compose(embedded, mode);
                PayloadComposer.Validate(payload, mode);

                string built = Path.Combine(AppPaths.BuildDir, "gui-patched.js");
                Directory.CreateDirectory(AppPaths.BuildDir);
                string original = File.ReadAllText(AppPaths.OrigPreload, Encoding.UTF8);
                string patched = Patcher.PatchText(original, payload);
                File.WriteAllText(built, patched, new UTF8Encoding(false));
                WriteLog(string.Format("生成成功（{0:N0} 字节）", new FileInfo(built).Length), "ok");

                WriteLog("写入抖音目录…");
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string snap = Path.Combine(AppPaths.BackupDir, stamp);
                Directory.CreateDirectory(snap);
                File.Copy(p.Preload, Path.Combine(snap, "preload.js.before"), true);
                try
                {
                    File.Copy(built, p.Preload, true);
                }
                catch (UnauthorizedAccessException)
                {
                    // 抖音装在 Program Files 时，改它的文件需要管理员权限。
                    // 直接抛出来的英文异常对普通用户毫无意义，这里换成可执行的提示。
                    throw new InvalidOperationException(
                        "没有权限写入抖音安装目录：" + p.Preload + "\n\n" +
                        "抖音安装在系统目录（例如 C:\\Program Files）下，修改其中的文件需要管理员权限。\n\n" +
                        "请右键本程序 → 「以管理员身份运行」，然后再点一次「① 一键启用最高画质」。",
                        new UnauthorizedAccessException(p.Preload));
                }
                WriteLog("已写入（md5 " + HashUtil.GetMd5(p.Preload).Substring(0, 8) + "）", "ok");

                try
                {
                    if (File.Exists(AppPaths.PageLog)) File.Delete(AppPaths.PageLog);
                    string mainLog = Path.Combine(AppPaths.UserData, "dy-force-main.log");
                    if (File.Exists(mainLog)) File.Delete(mainLog);
                }
                catch { }

                if (File.Exists(AppPaths.AsarOrig) &&
                    HashUtil.GetSha256(p.Asar) != HashUtil.GetSha256(AppPaths.AsarOrig))
                {
                    File.Copy(AppPaths.AsarOrig, p.Asar, true);
                    WriteLog("app.asar 已还原为官方版本。", "ok");
                }

                try
                {
                    Directory.CreateDirectory(AppPaths.BackupDir);
                    var meta = new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "timestamp", DateTime.Now.ToString("o") },
                        { "versionDir", p.VersionDir },
                        { "targetFile", p.Preload },
                        { "origMd5", Patcher.ORIG_PRELOAD_MD5 },
                        { "newMd5", HashUtil.GetMd5(p.Preload) },
                        { "pristinePath", AppPaths.OrigPreload },
                        { "payload", "embedded" },
                        { "installedBy", "exe" },
                        { "douyinVersion", _versionInfo != null ? _versionInfo.Version : "" },
                        { "toolVersion", _versionInfo != null ? _versionInfo.ToolVersion : VersionChecker.GetToolVersion() }
                    };
                    File.WriteAllText(Path.Combine(AppPaths.BackupDir, "deploy-meta.json"),
                        JsonUtil.Serialize(meta), new UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(AppPaths.BackupDir, "LATEST"), stamp, new UTF8Encoding(false));
                    WriteLog("还原元数据已写入 backup\\deploy-meta.json。", "ok");
                }
                catch (Exception ex)
                {
                    WriteLog("无法写入还原元数据：" + ex.Message, "warn");
                }

                // 脚本包 + 开关：两者都放 %APPDATA%\douyin\，换脚本版本不用再提权
                try { ApplyBundleAndFlags(mode); }
                catch (Exception ex) { WriteLog("抖音优化脚本部署失败：" + ex.Message, "warn"); }

                WriteLog("完成。现在可以启动抖音了。", "ok");
                MessageBox.Show(_win,
                    "最高画质已启用。\n\n接下来：\n  1. 正常启动抖音\n  2. 打开任意支持 4K 的视频\n  3. 清晰度会直接是 4K\n\n没有 4K 的视频会自动降为 2K 或 1080P。",
                    "已启用", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                WriteLog("启用出错：" + ex.Message, "err");
                MessageBox.Show(_win, "启用出错：\n\n" + ex.Message, "出错", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
                UpdateView();
            }
        }

        void InvokeRestore()
        {
            LogBox.Clear();
            WriteLog("开始还原…");
            if (string.IsNullOrEmpty(_installRoot))
            {
                WriteLog("尚未选择抖音位置。", "err");
                return;
            }

            InstallPaths p;
            try { p = DouyinLocator.GetInstallPaths(_installRoot); }
            catch (Exception ex) { WriteLog(ex.Message, "err"); return; }

            if (!EnsureDouyinClosed())
            {
                WriteLog("抖音仍在运行，已取消还原。", "err");
                return;
            }

            var ans = MessageBox.Show(_win,
                "要还原成抖音官方原版吗？\n\n最高画质强制将停止（以后可以再启用）。",
                "确认还原", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (ans != MessageBoxResult.Yes)
            {
                WriteLog("已取消还原。");
                return;
            }

            SetBusy(true);
            try
            {
                if (HashUtil.GetMd5(AppPaths.OrigPreload) != Patcher.ORIG_PRELOAD_MD5)
                    throw new InvalidOperationException("原始备份缺失或校验失败，拒绝还原。");

                File.Copy(AppPaths.OrigPreload, p.Preload, true);
                if (HashUtil.GetMd5(p.Preload) != Patcher.ORIG_PRELOAD_MD5)
                    throw new InvalidOperationException("还原后校验失败");
                WriteLog("preload.js 已还原为官方原版。", "ok");

                if (File.Exists(AppPaths.AsarOrig) &&
                    HashUtil.GetSha256(p.Asar) != HashUtil.GetSha256(AppPaths.AsarOrig))
                {
                    File.Copy(AppPaths.AsarOrig, p.Asar, true);
                    WriteLog("app.asar 已还原为官方原版。", "ok");
                }

                foreach (var n in new[] { "dy-force.log", "dy-force-main.log" })
                {
                    string f = Path.Combine(AppPaths.UserData, n);
                    if (File.Exists(f)) File.Delete(f);
                }

                foreach (var path in new[] { AppPaths.UserscriptBundle, AppPaths.InjectModeFile })
                {
                    if (!File.Exists(path)) continue;
                    try { File.Delete(path); }
                    catch (Exception ex) { WriteLog("无法删除 " + Path.GetFileName(path) + "：" + ex.Message, "warn"); }
                }
                WriteLog("抖音优化脚本已移除。", "ok");

                WriteLog("还原完成，抖音已回到官方状态。", "ok");
                MessageBox.Show(_win, "已还原为官方原版。", "还原完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                WriteLog("还原出错：" + ex.Message, "err");
                MessageBox.Show(_win, "还原出错：\n\n" + ex.Message, "出错", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
                UpdateView();
            }
        }

        /// <summary>
        /// 启用/还原之前统一走这里：抖音还有进程在跑就给出提示并结束它们。
        /// 返回 true 表示现在可以安全地改文件了。
        /// </summary>
        bool EnsureDouyinClosed()
        {
            var found = ProcessKiller.Find(_installRoot);
            if (found.Count == 0) return true;

            WriteLog("抖音正在运行：" + ProcessKiller.Describe(found), "warn");
            var ans = MessageBox.Show(_win,
                "抖音正在运行，补丁没法写入（改文件时会被占用，而且运行中的旧进程会把补丁覆盖掉）。\n\n"
                + "检测到 " + found.Count + " 个进程：" + ProcessKiller.Describe(found) + "\n\n"
                + "现在就结束它们吗？（只关窗口不算退出，这里会连托盘、守护进程一起结束）",
                "结束抖音进程", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (ans != MessageBoxResult.Yes)
            {
                WriteLog("已取消结束抖音进程。", "warn");
                return false;
            }
            return KillDouyinProcesses(false, true);
        }

        /// <summary>
        /// 结束抖音的所有进程（主程序 / 托盘 / 守护 / 崩溃上报等）。
        /// askOnly=false 时不再二次确认（调用方刚刚问过）。
        /// </summary>
        bool KillDouyinProcesses(bool askOnly, bool quietSuccess)
        {
            var found = ProcessKiller.Find(_installRoot);
            if (found.Count == 0)
            {
                WriteLog("没有检测到正在运行的抖音进程。", "ok");
                UpdateRunState();
                if (!quietSuccess)
                    MessageBox.Show(_win, "没有检测到正在运行的抖音进程。", "无需操作", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }

            if (askOnly)
            {
                var confirm = MessageBox.Show(_win,
                    "要结束以下抖音进程吗？\n\n  " + ProcessKiller.Describe(found) + "\n\n"
                    + "抖音里未保存的操作会丢失；守护进程和崩溃上报也会一起结束，避免它们重新拉起或占着文件。",
                    "结束抖音进程", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.Yes)
                {
                    WriteLog("已取消结束抖音进程。", "warn");
                    return ProcessKiller.Find(_installRoot).Count == 0;
                }
            }

            SetBusy(true);
            try
            {
                WriteLog("正在结束抖音进程…");
                KillResult r;
                try { r = ProcessKiller.KillAll(_installRoot, 2500, 5000, delegate(string m) { WriteLog(m); }); }
                catch (Exception ex)
                {
                    WriteLog("结束进程出错：" + ex.Message, "err");
                    MessageBox.Show(_win, "结束抖音进程时出错：\n\n" + ex.Message, "出错", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                if (r.AllGone)
                {
                    WriteLog("抖音已完全退出。", "ok");
                    if (!quietSuccess)
                        MessageBox.Show(_win, "抖音已完全退出，现在可以启用或还原补丁了。", "已完成",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    return true;
                }

                foreach (var f in r.Failures) WriteLog(f, "err");
                string detail = "还有 " + r.Remaining.Count + " 个进程没能结束：\n  "
                    + ProcessKiller.Describe(r.Remaining) + "\n\n";
                detail += r.RequiresElevation
                    ? "其中有的进程权限比本工具高（例如抖音以管理员身份启动过）。\n请右键点击本程序 →「以管理员身份运行」后再试一次。"
                    : "请稍后重试；仍不行就在任务管理器里手动结束这些进程。";
                MessageBox.Show(_win, detail, "未能完全结束", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            finally
            {
                SetBusy(false);
                UpdateView();
            }
        }

        // ---------------------------------------------------------------------
        // 注入方式 / 脚本管理
        // ---------------------------------------------------------------------
        InjectMode CurrentMode()
        {
            if (ModeQuality != null && ModeQuality.IsChecked == true) return InjectMode.QualityOnly;
            if (ModeScript != null && ModeScript.IsChecked == true) return InjectMode.ScriptOnly;
            return InjectMode.Both;
        }

        void ApplyModeToUi(InjectMode mode)
        {
            if (ModeBoth == null) return;
            ModeBoth.IsChecked = mode == InjectMode.Both;
            ModeQuality.IsChecked = mode == InjectMode.QualityOnly;
            ModeScript.IsChecked = mode == InjectMode.ScriptOnly;
        }

        /// <summary>
        /// 把脚本包与开关写进 %APPDATA%\douyin\。
        /// 「只装最高画质」时只写开关、不落地脚本，页面里就完全不会跑用户脚本。
        /// </summary>
        void ApplyBundleAndFlags(InjectMode mode)
        {
            Directory.CreateDirectory(AppPaths.UserData);

            bool wantScripts = mode != InjectMode.QualityOnly;
            bool wantQuality = mode != InjectMode.ScriptOnly;

            if (wantScripts)
            {
                string bundle;
                if (File.Exists(AppPaths.UserscriptBundle))
                {
                    // 用户上传过脚本：保留它，不要被内置版本覆盖
                    bundle = File.ReadAllText(AppPaths.UserscriptBundle, Encoding.UTF8);
                    WriteLog("保留已上传的脚本，未用内置版本覆盖。", "ok");
                }
                else
                {
                    bundle = AppPaths.ReadEmbeddedText("userscript.bundle.js");
                }
                File.WriteAllText(AppPaths.UserscriptBundle, bundle, new UTF8Encoding(false));
                WriteLog("抖音优化脚本已部署（" + (bundle.Length / 1024) + " KB）。", "ok");
            }

            var flags = new System.Collections.Generic.Dictionary<string, object>
            {
                { "scripts", wantScripts },
                { "quality", wantQuality },
                { "mode", Config.InjectModeKey(mode) },
                { "savedAt", DateTime.Now.ToString("o") }
            };
            File.WriteAllText(AppPaths.InjectModeFile, JsonUtil.Serialize(flags), new UTF8Encoding(false));

            if (!wantScripts) WriteLog("已关闭用户脚本（只装最高画质）。", "ok");
            if (!wantQuality) WriteLog("已关闭画质强制（只装抖音优化脚本）。", "ok");
        }

        /// <summary>当前 bundle 里装的是哪个脚本 + 是否上传过。</summary>
        void UpdateScriptInfo()
        {
            if (ScriptValue == null) return;
            try
            {
                string builtInBundle = AppPaths.ReadEmbeddedText("userscript.bundle.js");
                bool builtInMetaDummy;
                var builtInMeta = UserscriptBundler.ReadBundleMeta(builtInBundle, out builtInMetaDummy);

                if (!File.Exists(AppPaths.UserscriptBundle))
                {
                    // 没有外部文件也没关系：装载器会用 EXE 里内置的那份
                    ScriptValue.Text = builtInMeta == null
                        ? "内置脚本（未检出）"
                        : builtInMeta.Summary() + "（内置）";
                    ScriptPathValue.Text = "使用 EXE 内置副本（无外部文件）";
                    BtnResetScript.IsEnabled = false;
                    return;
                }

                string live = File.ReadAllText(AppPaths.UserscriptBundle, Encoding.UTF8);
                bool builtIn;
                var meta = UserscriptBundler.ReadBundleMeta(live, out builtIn);

                // 装载器按 __DY_BUNDLE_VERSION__ 取新者：外部更新才生效，
                // 外部更旧时仍跑内置。这里把判断结果如实显示出来。
                long liveVer = UserscriptBundler.ReadBundleVersion(live);
                long builtInVer = UserscriptBundler.ReadEmbeddedBundleVersion();

                string suffix;
                if (liveVer > builtInVer) suffix = "（外部，更新）";
                else if (liveVer > 0 && liveVer == builtInVer) suffix = "（外部，与内置相同）";
                else suffix = "（外部，比内置旧 → 实际用内置）";

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
                if (BtnResetScript != null) BtnResetScript.IsEnabled = false;
            }
        }

        /// <summary>上传一个 .user.js，替换 bundle 里的脚本段（依赖保持不动）。</summary>
        void UploadScript()
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择要安装的用户脚本（.user.js）",
                Filter = "用户脚本 (*.user.js;*.js)|*.user.js;*.js|所有文件 (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (!string.IsNullOrEmpty(_installRoot))
            {
                string hint = Path.Combine(AppPaths.AppDir, "userscripts");
                if (Directory.Exists(hint)) dlg.InitialDirectory = hint;
            }

            if (dlg.ShowDialog(_win) != true) return;

            string picked = dlg.FileName;
            string src;
            try { src = File.ReadAllText(picked, Encoding.UTF8); }
            catch (Exception ex)
            {
                MessageBox.Show(_win, "读取文件失败：\n\n" + ex.Message, "上传失败", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            UserscriptBundler.ScriptMeta meta;
            string error;
            if (!UserscriptBundler.LooksLikeUserscript(src, out meta, out error))
            {
                WriteLog("上传失败：" + error.Replace("\n", " "), "err");
                MessageBox.Show(_win, error, "不是用户脚本", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(_win,
                "要安装这个脚本吗？\n\n"
                + "名称：" + meta.Name + "\n"
                + "版本：" + (string.IsNullOrEmpty(meta.Version) ? "未知" : meta.Version) + "\n"
                + "作者：" + (string.IsNullOrEmpty(meta.Author) ? "未知" : meta.Author) + "\n"
                + (string.IsNullOrEmpty(meta.Description) ? "" : "\n" + meta.Description + "\n")
                + "\n只替换脚本本体，GM 兼容层和依赖保持不变；重启抖音后生效。",
                "确认上传", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                WriteLog("已取消上传。");
                return;
            }

            try
            {
                Directory.CreateDirectory(AppPaths.UserData);

                // 没有现成 bundle 时用内置那份做底板
                string bundle = File.Exists(AppPaths.UserscriptBundle)
                    ? File.ReadAllText(AppPaths.UserscriptBundle, Encoding.UTF8)
                    : AppPaths.ReadEmbeddedText("userscript.bundle.js");

                string updated = UserscriptBundler.ReplaceScript(bundle, src, meta);
                File.WriteAllText(AppPaths.UserscriptBundle, updated, new UTF8Encoding(false));

                bool dummy;
                var check = UserscriptBundler.ReadBundleMeta(updated, out dummy);
                if (check == null || check.Name != meta.Name)
                    throw new InvalidOperationException("写入后回读校验失败，脚本包可能已损坏。");

                WriteLog("已上传脚本：" + meta.Summary() + "（" + picked + "）", "ok");
                UpdateScriptInfo();
                UpdateView();

                MessageBox.Show(_win,
                    "脚本已更新：" + meta.Summary() + "\n\n重启抖音后生效（点「结束抖音进程」再重新打开抖音即可）。",
                    "上传成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                WriteLog("上传脚本失败：" + ex.Message, "err");
                MessageBox.Show(_win, "上传脚本失败：\n\n" + ex.Message, "出错", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>丢掉上传的脚本，恢复成 EXE 内置那份。</summary>
        void ResetScript()
        {
            try
            {
                if (File.Exists(AppPaths.UserscriptBundle))
                {
                    string live = File.ReadAllText(AppPaths.UserscriptBundle, Encoding.UTF8);
                    string builtInBundle = AppPaths.ReadEmbeddedText("userscript.bundle.js");
                    if (string.Equals(live, builtInBundle, StringComparison.Ordinal))
                    {
                        WriteLog("当前已经是内置脚本，无需恢复。", "ok");
                        return;
                    }
                }

                var ans = MessageBox.Show(_win,
                    "要恢复成内置的「抖音优化」脚本吗？\n\n上传的脚本会被丢弃（重启抖音后生效）。",
                    "恢复内置脚本", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (ans != MessageBoxResult.Yes) { WriteLog("已取消恢复。"); return; }

                Directory.CreateDirectory(AppPaths.UserData);
                File.WriteAllText(AppPaths.UserscriptBundle, AppPaths.ReadEmbeddedText("userscript.bundle.js"), new UTF8Encoding(false));
                WriteLog("已恢复内置脚本。", "ok");
                UpdateScriptInfo();
                UpdateView();
            }
            catch (Exception ex)
            {
                WriteLog("恢复内置脚本失败：" + ex.Message, "err");
                MessageBox.Show(_win, "恢复失败：\n\n" + ex.Message, "出错", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void InvokeCheck()
        {
            LogBox.Clear();
            WriteLog("读取运行日志…");

            if (!File.Exists(AppPaths.PageLog))
            {
                WriteLog("还没有运行日志。请先启用，再打开一些视频。", "warn");
                MessageBox.Show(_win,
                    "还没有找到运行日志。\n\n请先这样做：\n  1. 退出抖音\n  2. 点「① 一键启用最高画质」\n  3. 启动抖音并打开几个视频\n  4. 回来点「③ 检查生效情况」",
                    "暂无数据", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var lines = File.ReadAllLines(AppPaths.PageLog);
            int onPage = lines.Count(l => Regex.IsMatch(l, "loose-preload .*url=https://www\\.douyin\\.com"));
            int created = lines.Count(l => l.IndexOf("[created]", StringComparison.Ordinal) >= 0);
            int forced = lines.Count(l => l.IndexOf("[forced]", StringComparison.Ordinal) >= 0);
            int skipped = lines.Count(l => l.IndexOf("[skip]", StringComparison.Ordinal) >= 0);
            int errs = lines.Count(l => Regex.IsMatch(l, "SecurityError|ERROR|error"));

            WriteLog("日志行数：" + lines.Length);
            if (onPage > 0) WriteLog("已确认注入到抖音页面。", "ok");
            else WriteLog("没有找到抖音页面注入记录。", "err");
            WriteLog("清晰度写入：created=" + created + " forced=" + forced + " kept-default=" + skipped);
            if (errs > 0) WriteLog("错误行数：" + errs, "warn");

            if (onPage > 0 && (created > 0 || forced > 0))
            {
                WriteLog("结论：工具工作正常。", "ok");
                MessageBox.Show(_win,
                    "工具工作正常。\n\n如果画面仍不像 4K，请检查：\n  - 该视频本身是否有 4K 选项\n  - 是否已登录（4K 通常需要登录）\n  - 打开清晰度菜单看当前档位",
                    "检查结果：正常", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                WriteLog("结论：暂未看到生效记录，请先打开一些视频。", "warn");
                MessageBox.Show(_win,
                    "暂未看到生效记录。\n\n请启动抖音、打开几个视频，然后再点「检查」。",
                    "检查结果：暂无数据", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
