# WinUI 3 版本

这是新版界面（Fluent / WinUI 3），与 `dist/抖音画质助手.exe`（WPF 版）并存。

## 为什么单独一个目录

- 旧版是 `csc.exe` 直接编译的 .NET Framework 4.8 WPF 单文件，**不需要 SDK**
- WinUI 3 必须用 .NET SDK + Windows App SDK 编译，而且体积大得多（自包含约 90MB）

两者共用同一份业务逻辑（`AppPaths` / `Patcher` / `ProcessKiller` / `UserscriptBundler` …），
用 `cp` 从 `exe-src/` 复制过来，**不重复维护**。

## 本地编译（需要 .NET 8 SDK）

```powershell
# 1) 先把 payload.js 准备好（内置脚本 + 装载器 + 画质强制器）
node tools\build-userscript.js
pwsh -File tools\prepare-winui-resources.ps1

# 2) 编译
dotnet publish winui\DouyinQualityHelper.WinUI.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -o publish\x64
```

## GitHub Actions 编译（推荐）

推送 tag 即可：

```bash
git tag v2.0.0
git push origin v2.0.0
```

`.github/workflows/build-winui.yml` 会在 `windows-latest` 上编译、跑 `--selftest`、
打包 zip 并发布 Release。也可以到 Actions 页面手动 `Run workflow`。

## 与 WPF 版的差异

| 项 | WPF 版 | WinUI 3 版 |
|---|---|---|
| 编译 | `csc.exe`，无 SDK | .NET 8 SDK + WindowsAppSDK |
| 体积 | ~2.9 MB 单文件 | ~90 MB 目录（自包含） |
| 界面 | 手写 Fluent 风格 | 原生 WinUI 3 控件 + Mica/亚克力 |
| 文件选择 | `OpenFileDialog` | `FileOpenPicker` + 窗口句柄 |
| 弹窗 | `MessageBox` | `ContentDialog` |
| 更新检测 | 无 | **GitHub Releases 自动检查** |

## 自动更新检测

`Services/UpdateChecker.cs`：

- 请求 `https://api.github.com/repos/yss161/douyin-quality-helper-plus/releases/latest`（匿名，无需 Token）
- 版本比较是离线的 `IsNewer()`，支持 `v2.1.0` / `2.1.0-beta` 这类写法
- **网络失败不影响使用**：只把"检查失败"写到界面上，不弹错误框
- 发现新版本时：

  1. 顶部弹出 `InfoBar`「发现新版本 vX.Y.Z」
  2. 「软件更新」卡片右侧出现「前往下载」按钮
  3. 点击用默认浏览器打开该 Release 页

改仓库地址只需要动 `UpdateChecker.Owner` / `Repo` 两个常量。