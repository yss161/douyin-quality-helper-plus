using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DouyinQualityHelper
{
    public sealed partial class MainWindow
    {
        // =====================================================================
        // 选择抖音位置
        // =====================================================================
        void SetInstallRoot(string root, bool save)
        {
            _installRoot = root;

            if (string.IsNullOrEmpty(root))
            {
                PathBox.Text = "尚未选择";
                VersionValue.Text = "待选择（选好后自动识别）";
            }
            else
            {
                PathBox.Text = root;
                if (save) Config.SaveDouyinDir(root);
                UpdateVersionInfo(root);
            }
        }

        void UpdateVersionInfo(string root)
        {
            _versionInfo = null;
            if (string.IsNullOrEmpty(root)) return;
            try { _versionInfo = VersionChecker.GetInfo(root); }
            catch (Exception ex) { WriteLog("版本检测失败：" + ex.Message, "warn"); }
        }

        async Task InvokeBrowseAsync()
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".exe");
            picker.FileTypeFilter.Add("*");

            // WinUI 3 非打包应用：必须把窗口句柄交给 picker，否则会崩
            var hwnd = WindowNative.GetWindowHandle(this);
            InitializeWithWindow.Initialize(picker, hwnd);

            if (!string.IsNullOrEmpty(_hintDir) && Directory.Exists(_hintDir))
            {
                try { picker.SuggestedStartLocation = PickerLocationId.ComputerFolder; } catch { }
            }

            StorageFile file = await picker.PickSingleFileAsync();
            if (file == null) return;

            try
            {
                string root = DouyinLocator.ResolveInstallRoot(file.Path);
                if (string.IsNullOrEmpty(root))
                {
                    WriteLog("选中的不是抖音程序：" + file.Path, "err");
                    await ShowDialogSimple("无法识别", "选中的不是抖音安装目录里的 douyin.exe。\n\n请重新选择，或直接选抖音安装根目录。");
                    return;
                }

                SetInstallRoot(root, true);
                WriteLog("已选择抖音位置：" + root, "ok");
                UpdateView();
            }
            catch (Exception ex)
            {
                WriteLog("选择失败：" + ex.Message, "err");
            }
        }

        void InvokeBrowse()
        {
            // 事件处理器里不阻塞 UI 线程：fire-and-forget，异常在方法内处理
            _ = InvokeBrowseAsync();
        }

        // =====================================================================
        // 结束抖音进程
        // =====================================================================
        bool EnsureDouyinClosed()
        {
            var found = ProcessKiller.Find(_installRoot);
            if (found.Count == 0) return true;

            WriteLog("抖音正在运行：" + ProcessKiller.Describe(found), "warn");
            var result = ShowDialogBlocking(
                "结束抖音进程",
                "抖音正在运行，补丁没法写入。\n\n检测到 " + found.Count + " 个进程：\n" +
                ProcessKiller.Describe(found) + "\n\n现在就结束它们吗？",
                "结束", "取消");
            if (!result) { WriteLog("已取消。", "warn"); return false; }
            return KillDouyinProcesses(false, false);
        }

        bool KillDouyinProcesses(bool askFirst, bool quietSuccess)
        {
            var found = ProcessKiller.Find(_installRoot);
            if (found.Count == 0)
            {
                WriteLog("没有正在运行的抖音进程。", "ok");
                UpdateRunState();
                return true;
            }

            if (askFirst)
            {
                bool ok = ShowDialogBlocking("结束抖音进程",
                    "要结束以下抖音进程吗？\n\n  " + ProcessKiller.Describe(found) +
                    "\n\n抖音里未保存的操作会丢失；守护进程和崩溃上报也会一起结束。",
                    "结束", "取消");
                if (!ok) { WriteLog("已取消。", "warn"); return false; }
            }

            SetBusy(true);
            try
            {
                WriteLog("正在结束抖音进程…");
                var r = ProcessKiller.KillAll(_installRoot, 2500, 5000, m => WriteLog(m));

                if (r.AllGone)
                {
                    WriteLog("抖音已完全退出。", "ok");
                    if (!quietSuccess) { }
                    return true;
                }

                foreach (var f in r.Failures) WriteLog(f, "err");
                WriteLog("仍有 " + r.Remaining.Count + " 个进程未结束：" + ProcessKiller.Describe(r.Remaining), "err");
                if (r.RequiresElevation)
                    WriteLog("有进程权限更高，请右键本程序「以管理员身份运行」后重试。", "warn");
                return false;
            }
            finally
            {
                SetBusy(false);
                UpdateView();
            }
        }

        // =====================================================================
        // 启用 / 还原
        // =====================================================================
        async Task InvokeInstallAsync()
        {
            LogBox.Text = "";

            if (string.IsNullOrEmpty(_installRoot))
            {
                WriteLog("还没有选择抖音位置。", "warn");
                await InvokeBrowseAsync();
                if (string.IsNullOrEmpty(_installRoot)) return;
            }

            InstallPaths p;
            try { p = DouyinLocator.GetInstallPaths(_installRoot); }
            catch (Exception ex) { WriteLog(ex.Message, "err"); return; }

            if (!EnsureDouyinClosed()) return;

            SetBusy(true);
            try
            {
                if (!File.Exists(AppPaths.OrigPreload) ||
                    HashUtil.GetMd5(AppPaths.OrigPreload) != Patcher.ORIG_PRELOAD_MD5)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.OrigPreload));
                    string curMd5 = HashUtil.GetMd5(p.Preload);
                    if (curMd5 == Patcher.ORIG_PRELOAD_MD5)
                    {
                        File.Copy(p.Preload, AppPaths.OrigPreload, true);
                        WriteLog("已固化原始文件备份。", "ok");
                    }
                    else
                    {
                        WriteLog("当前 preload.js 不是官方原版，且没有可用备份。", "err");
                        await ShowDialogSimple("无法继续", "找不到官方原始文件，也没有可用的备份。\n\n请重新安装抖音客户端后再试。");
                        return;
                    }
                }

                var mode = CurrentMode();
                WriteLog("注入内容：" + Config.InjectModeLabel(mode));

                string embedded = AppPaths.ReadEmbeddedText("payload.js");
                string payload = PayloadComposer.Compose(embedded, mode);
                PayloadComposer.Validate(payload, mode);

                Directory.CreateDirectory(AppPaths.BuildDir);
                string built = Path.Combine(AppPaths.BuildDir, "gui-patched.js");
                string original = File.ReadAllText(AppPaths.OrigPreload, Encoding.UTF8);
                string patched = Patcher.PatchText(original, payload);
                File.WriteAllText(built, patched, new UTF8Encoding(false));
                WriteLog("生成成功（" + new FileInfo(built).Length.ToString("N0") + " 字节）", "ok");

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
                    WriteLog("没有权限写入抖音安装目录。", "err");
                    await ShowDialogSimple("需要管理员权限",
                        "抖音安装在系统目录下，修改其中的文件需要管理员权限。\n\n" +
                        "请右键本程序 →「以管理员身份运行」，然后再点一次「一键启用最高画质」。");
                    return;
                }

                WriteLog("已写入（md5 " + HashUtil.GetMd5(p.Preload).Substring(0, 8) + "）", "ok");

                try
                {
                    if (File.Exists(AppPaths.PageLog)) File.Delete(AppPaths.PageLog);
                }
                catch { }

                ApplyBundleAndFlags(mode);
                WriteLog("完成。现在可以启动抖音了。", "ok");

                await ShowDialogSimple("已启用最高画质",
                    "接下来：\n  1. 正常启动抖音\n  2. 打开任意支持 4K 的视频\n  3. 清晰度会直接是 4K\n\n没有 4K 的视频会自动降为 2K 或 1080P。");
            }
            catch (Exception ex)
            {
                WriteLog("启用出错：" + ex.Message, "err");
                await ShowDialogSimple("启用失败", ex.Message);
            }
            finally
            {
                SetBusy(false);
                UpdateVersionInfo(_installRoot);
                UpdateView();
            }
        }

        async Task InvokeRestoreAsync()
        {
            LogBox.Text = "";

            if (string.IsNullOrEmpty(_installRoot)) { WriteLog("还没有选择抖音位置。", "warn"); return; }

            InstallPaths p;
            try { p = DouyinLocator.GetInstallPaths(_installRoot); }
            catch (Exception ex) { WriteLog(ex.Message, "err"); return; }

            if (!EnsureDouyinClosed()) return;

            bool go = ShowDialogBlocking("还原官方原版",
                "要还原成抖音官方原版吗？\n\n最高画质强制将停止（以后可以再启用）。", "还原", "取消");
            if (!go) { WriteLog("已取消还原。"); return; }

            SetBusy(true);
            try
            {
                if (File.Exists(AppPaths.OrigPreload))
                {
                    File.Copy(AppPaths.OrigPreload, p.Preload, true);
                    if (HashUtil.GetMd5(p.Preload) != Patcher.ORIG_PRELOAD_MD5)
                    {
                        WriteLog("还原校验失败！", "err");
                        return;
                    }
                    WriteLog("preload.js 已还原为官方原版。", "ok");
                }
                else
                {
                    WriteLog("没有原始备份，无法还原。", "err");
                    await ShowDialogSimple("无法还原", "找不到原始备份文件。\n\n请重新安装抖音客户端，或从别的机器复制一份官方 preload.js。");
                    return;
                }

                foreach (var name in new[] { AppPaths.UserscriptBundle, AppPaths.InjectModeFile })
                {
                    try { if (File.Exists(name)) File.Delete(name); } catch { }
                }
                try { if (File.Exists(AppPaths.PageLog)) File.Delete(AppPaths.PageLog); } catch { }

                WriteLog("还原完成，抖音已回到官方状态。", "ok");
                await ShowDialogSimple("还原完成", "已还原为官方原版。");
            }
            catch (Exception ex)
            {
                WriteLog("还原出错：" + ex.Message, "err");
                await ShowDialogSimple("还原失败", ex.Message);
            }
            finally
            {
                SetBusy(false);
                UpdateVersionInfo(_installRoot);
                UpdateView();
            }
        }

        // =====================================================================
        // 检查生效情况
        // =====================================================================
        void InvokeCheck()
        {
            LogBox.Text = "";
            WriteLog("读取运行日志…");

            if (!File.Exists(AppPaths.PageLog))
            {
                WriteLog("还没有运行日志。请先启用，再打开一些视频。", "warn");
                _ = ShowDialogSimple("暂无数据",
                    "还没有找到运行日志。\n\n请先：\n  1. 退出抖音\n  2. 点「一键启用最高画质」\n  3. 启动抖音并打开几个视频\n  4. 回来点「检查生效情况」");
                return;
            }

            var lines = File.ReadAllLines(AppPaths.PageLog);
            int onPage = lines.Count(l => Regex.IsMatch(l, "loose-preload .*url=https://www\\.douyin\\.com"));
            int created = lines.Count(l => l.IndexOf("[created]", StringComparison.Ordinal) >= 0);
            int forced = lines.Count(l => l.IndexOf("[forced]", StringComparison.Ordinal) >= 0);
            int errs = lines.Count(l => Regex.IsMatch(l, "SecurityError|ERROR|error"));

            WriteLog("日志行数：" + lines.Length);
            if (onPage > 0) WriteLog("已确认注入到抖音页面。", "ok");
            else WriteLog("没有找到抖音页面注入记录。", "err");
            WriteLog("清晰度写入：created=" + created + " forced=" + forced);
            if (errs > 0) WriteLog("错误行数：" + errs, "warn");

            bool good = onPage > 0 && (created > 0 || forced > 0);
            WriteLog(good ? "结论：工具工作正常。" : "结论：暂未看到生效记录。", good ? "ok" : "warn");

            _ = ShowDialogSimple(good ? "检查结果：正常" : "检查结果：暂无数据",
                good
                    ? "工具工作正常。\n\n如果画面仍不像 4K，请检查：\n  - 该视频本身是否有 4K 选项\n  - 是否已登录（4K 通常需要登录）"
                    : "暂未看到生效记录。\n\n请启动抖音、打开几个视频，然后再点检查。");
        }

        // =====================================================================
        // 注入方式 / 脚本包
        // =====================================================================
        InjectMode CurrentMode()
        {
            if (ModeQuality.IsChecked == true) return InjectMode.QualityOnly;
            if (ModeScript.IsChecked == true) return InjectMode.ScriptOnly;
            return InjectMode.Both;
        }

        void ApplyModeToUi(InjectMode mode)
        {
            ModeBoth.IsChecked = mode == InjectMode.Both;
            ModeQuality.IsChecked = mode == InjectMode.QualityOnly;
            ModeScript.IsChecked = mode == InjectMode.ScriptOnly;
        }

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

            var flags = new Dictionary<string, object>
            {
                { "scripts", wantScripts },
                { "quality", wantQuality },
                { "mode", Config.InjectModeKey(mode) },
                { "savedAt", DateTime.Now.ToString("o") }
            };
            File.WriteAllText(AppPaths.InjectModeFile, JsonUtil.Serialize(flags), new UTF8Encoding(false));
        }

        async Task PickAndUploadScriptAsync()
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".js");
            picker.FileTypeFilter.Add("*");
            var hwnd = WindowNative.GetWindowHandle(this);
            InitializeWithWindow.Initialize(picker, hwnd);

            StorageFile file = await picker.PickSingleFileAsync();
            if (file == null) return;

            string src;
            try { src = await FileIO.ReadTextAsync(file); }
            catch (Exception ex)
            {
                await ShowDialogSimple("读取失败", ex.Message);
                return;
            }

            UserscriptBundler.ScriptMeta meta;
            string error;
            if (!UserscriptBundler.LooksLikeUserscript(src, out meta, out error))
            {
                WriteLog("上传失败：" + error.Replace("\n", " "), "err");
                await ShowDialogSimple("不是用户脚本", error);
                return;
            }

            bool go = ShowDialogBlocking("确认上传",
                "要安装这个脚本吗？\n\n名称：" + meta.Name +
                "\n版本：" + (string.IsNullOrEmpty(meta.Version) ? "未知" : meta.Version) +
                "\n作者：" + (string.IsNullOrEmpty(meta.Author) ? "未知" : meta.Author) +
                "\n\n只替换脚本本体，GM 兼容层和依赖保持不变；重启抖音后生效。",
                "安装", "取消");
            if (!go) { WriteLog("已取消上传。"); return; }

            try
            {
                Directory.CreateDirectory(AppPaths.UserData);
                string bundle = File.Exists(AppPaths.UserscriptBundle)
                    ? File.ReadAllText(AppPaths.UserscriptBundle, Encoding.UTF8)
                    : AppPaths.ReadEmbeddedText("userscript.bundle.js");

                string updated = UserscriptBundler.ReplaceScript(bundle, src, meta);
                File.WriteAllText(AppPaths.UserscriptBundle, updated, new UTF8Encoding(false));

                bool dummy;
                var check = UserscriptBundler.ReadBundleMeta(updated, out dummy);
                if (check == null || check.Name != meta.Name)
                    throw new InvalidOperationException("写入后回读校验失败，脚本包可能已损坏。");

                WriteLog("已上传脚本：" + meta.Summary(), "ok");
                UpdateScriptInfo();
                await ShowDialogSimple("上传成功", "脚本已更新：" + meta.Summary() + "\n\n重启抖音后生效。");
            }
            catch (Exception ex)
            {
                WriteLog("上传脚本失败：" + ex.Message, "err");
                await ShowDialogSimple("上传失败", ex.Message);
            }
        }

        async Task ResetScriptAsync()
        {
            try
            {
                if (!File.Exists(AppPaths.UserscriptBundle))
                {
                    WriteLog("当前没有外部脚本，本来就使用内置副本。", "ok");
                    return;
                }

                string live = File.ReadAllText(AppPaths.UserscriptBundle, Encoding.UTF8);
                string builtInBundle = AppPaths.ReadEmbeddedText("userscript.bundle.js");
                if (string.Equals(live, builtInBundle, StringComparison.Ordinal))
                {
                    WriteLog("当前已经是内置脚本，无需恢复。", "ok");
                    return;
                }

                bool go = ShowDialogBlocking("恢复内置脚本",
                    "要恢复成内置的「抖音优化」脚本吗？\n\n上传的脚本会被丢弃（重启抖音后生效）。", "恢复", "取消");
                if (!go) { WriteLog("已取消恢复。"); return; }

                Directory.CreateDirectory(AppPaths.UserData);
                File.WriteAllText(AppPaths.UserscriptBundle, builtInBundle, new UTF8Encoding(false));
                WriteLog("已恢复内置脚本。", "ok");
                UpdateScriptInfo();
            }
            catch (Exception ex)
            {
                WriteLog("恢复失败：" + ex.Message, "err");
                await ShowDialogSimple("恢复失败", ex.Message);
            }
        }

        // =====================================================================
        // 更新检测
        // =====================================================================
        async Task CheckUpdateAsync(bool manual)
        {
            if (_updateChecking) return;
            _updateChecking = true;

            BtnCheckUpdate.IsEnabled = false;
            if (manual) UpdateDetail.Text = "正在检查…";
            UpdateBar.IsOpen = false;

            try
            {
                string current = VersionChecker.GetToolVersion();
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                {
                    _updateInfo = await UpdateChecker.CheckAsync(current, cts.Token);
                }

                var info = _updateInfo;

                if (!string.IsNullOrEmpty(info.Error))
                {
                    UpdateDetail.Text = "检查失败：" + info.Error;
                    BtnOpenRelease.Visibility = Visibility.Collapsed;
                    if (manual) WriteLog("更新检查失败：" + info.Error, "warn");
                    else WriteLog("自动检查更新失败（不影响使用）：" + info.Error, "warn");
                    return;
                }

                string latest = string.IsNullOrEmpty(info.LatestVersion) ? "未知" : info.LatestVersion;
                string published = info.PublishedAt.HasValue
                    ? "（" + info.PublishedAt.Value.ToLocalTime().ToString("yyyy-MM-dd") + "）"
                    : "";

                if (info.HasUpdate)
                {
                    UpdateDetail.Text = "有新版本 " + latest + published + "，当前 " + info.CurrentVersion;
                    BtnOpenReleaseText.Text = "前往下载 v" + latest;
                    BtnOpenRelease.Visibility = Visibility.Visible;

                    UpdateBar.Title = "发现新版本 v" + latest;
                    UpdateBar.Message = string.IsNullOrEmpty(info.Title) ? "" : info.Title;
                    UpdateBar.Severity = InfoBarSeverity.Success;
                    UpdateBar.IsOpen = true;

                    WriteLog("发现新版本：" + latest + "（当前 " + info.CurrentVersion + "）", "ok");
                }
                else
                {
                    UpdateDetail.Text = "已是最新版本 " + info.CurrentVersion + published;
                    BtnOpenRelease.Visibility = Visibility.Collapsed;
                    WriteLog("已是最新版本 " + info.CurrentVersion, "ok");
                }
            }
            catch (Exception ex)
            {
                UpdateDetail.Text = "检查失败：" + ex.Message;
                if (manual) WriteLog("更新检查异常：" + ex.Message, "warn");
            }
            finally
            {
                _updateChecking = false;
                BtnCheckUpdate.IsEnabled = true;
            }
        }

        /// <summary>打开 Release 页面（有更新时跳转，否则打开 releases 列表）。</summary>
        void OpenReleasePage()
        {
            string url = _updateInfo != null && !string.IsNullOrEmpty(_updateInfo.HtmlUrl)
                ? _updateInfo.HtmlUrl
                : UpdateChecker.ReleasesPageUrl;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };
                Process.Start(psi);
                WriteLog("已在浏览器中打开：" + url);
            }
            catch (Exception ex)
            {
                WriteLog("打开链接失败：" + ex.Message, "err");
            }
        }

        // =====================================================================
        // 对话框（WinUI 没有 MessageBox，用 ContentDialog 代替）
        // =====================================================================
        async Task ShowDialogSimple(string title, string message)
        {
            try
            {
                var dlg = new ContentDialog
                {
                    Title = title,
                    Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    CloseButtonText = "知道了",
                    XamlRoot = RootGrid.XamlRoot
                };
                await dlg.ShowAsync();
            }
            catch { }
        }

        /// <summary>
        /// 同步的是/否确认。
        /// ContentDialog 只能异步，所以在调用点用"先显示、后取结果"的方式会漏掉用户点击；
        /// 这里用 TaskCompletionSource + 自旋等待完成，保证返回值可信。
        /// 仅用于"必须先确认才能继续"的流程，调用点已在后台线程。
        /// </summary>
        bool ShowDialogBlocking(string title, string message, string yesText, string noText)
        {
            bool result = false;
            var tcs = new TaskCompletionSource<bool>();
            var dispatcher = DispatcherQueue;

            dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    var dlg = new ContentDialog
                    {
                        Title = title,
                        Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                        PrimaryButtonText = yesText,
                        SecondaryButtonText = noText,
                        DefaultButton = ContentDialogButton.Primary,
                        XamlRoot = RootGrid.XamlRoot
                    };
                    var r = await dlg.ShowAsync();
                    tcs.TrySetResult(r == ContentDialogResult.Primary);
                }
                catch
                {
                    tcs.TrySetResult(false);
                }
            });

            // 阻断等待（最多 2 分钟，避免异常情况卡死）
            if (tcs.Task.Wait(TimeSpan.FromMinutes(2)))
                result = tcs.Task.Result;

            return result;
        }
    }
}
