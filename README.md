# 抖音 PC 版 —— 强制最高画质（增强版）

**v1.7.0** ｜ [更新日志](CHANGELOG.md) ｜ [版本清单](versions.json) ｜ [MIT](LICENSE) ｜ [第三方引用](THIRD-PARTY-NOTICES.md)

> 本项目基于 [GUAPIXIA/Douyin_Mandatory_maximum_graphics_quality](https://github.com/GUAPIXIA/Douyin_Mandatory_maximum_graphics_quality)（MIT）派生开发，
> 并集成了 [WhiteSevs/TamperMonkeyScript](https://github.com/WhiteSevs/TamperMonkeyScript)（GPL-3.0）中的「抖音优化」用户脚本。
> 详见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

让抖音 PC 客户端**每个视频都按该视频可用的最高清晰度播放**（4K → 2K → 1080P），
不再被默认的"智能"档压到 1080P。

在原项目基础上新增：

- **结束抖音进程** —— 一个按钮关掉主程序 / 托盘 / 守护 / 崩溃上报（只关窗口不算退出）
- **抖音优化脚本集成** —— 用户脚本装进客户端，设置入口长在左侧导航里
- **注入内容三选一** —— 只装最高画质 / 只装脚本 / 两个都装
- **上传脚本更新** —— 换脚本不用重新打包，也不用再提权

---

## 快速开始

**新手：** 下载 [dist/抖音画质助手.exe](dist/抖音画质助手.exe)，双击运行。

1. 点「浏览…」选中抖音安装目录里的 `douyin.exe`
2. 抖音在跑就点红色的「结束抖音进程」
3. 点「① 一键启用最高画质」
4. 正常启动抖音看视频

> 抖音装在 `C:\Program Files` 下时需要**右键 EXE → 以管理员身份运行**才能写入补丁。

**开发者：**

```powershell
pwsh -File tools\prepare.ps1              # 首次：定位抖音、固化原始文件快照
pwsh -File douyin-quality.ps1 -Action install
pwsh -File douyin-quality.ps1 -Action restore
pwsh -File douyin-quality.ps1 -Action kill        # 结束所有抖音进程
```

---

## 注入内容三选一

界面上的「注入内容」可以只装一部分，选择记在 `config.json`：

| 选项 | 装什么 |
|---|---|
| ① 最高画质 + 抖音优化脚本 | 默认，两者都装 |
| ② 只装最高画质 | 原项目功能，完全不碰用户脚本 |
| ③ 只装抖音优化脚本 | 不强制画质，只装脚本 |

同时写 `%APPDATA%\douyin\inject-mode.json`，装载器和画质强制器各自读它，
所以不会出现"选了一个却跑另一个"。

## 上传脚本更新

点「**上传脚本更新…**」选一个 `.user.js`：

- 只替换 bundle 里的**脚本本体**，GM 兼容层和依赖原样保留
- 校验 `==UserScript==` 头部与 `@name`，弹窗确认名称 / 版本 / 作者
- 「**恢复内置脚本**」丢弃上传内容；再点「①」也不会覆盖已上传的脚本

自己构建：

```powershell
pwsh -File tools\fetch-vendor.ps1     # 首次：拉 UMD 依赖
node tools\build-userscript.js        # GM 层 + 依赖 + 原版脚本 -> bundle
```

---

## 支持的抖音版本

| 版本 | 状态 | 说明 |
|---|---|---|
| **8.7.0** | ✅ 已实测 | Electron 36.4.0-rs.33，`md5.json` 1093 项 |
| 其它版本 | ⚠️ 未验证但允许安装 | 结构一致即可用，界面会提示实测 |
| 已知不兼容 | ❌ 拒绝安装 | 目前没有 |

判定用 `launcher_config.json` 的版本号 + 官方文件哈希指纹（`app.asar` 从不修改，
可随时与 `md5.json` 对账）。新版默认放行 + 警告，实测通过后用
`tools/record-version.ps1` 登记。

---

## 原理

抖音 PC 版是 **Electron 外壳 + 内嵌 `www.douyin.com` 网页播放器**。网页播放器构建播放配置时
读 **sessionStorage 的 `MANUAL_SWITCH`**，取其中 `gearType` 作为清晰度。

给 `www.douyin.com` 挂 preload 的是 **asar 外的 loose 文件**
（`resources\app.asar.unpacked\preload.js`），它以 `isolated=false` 运行、与页面同一 JS 上下文，
所以能直接改写 `sessionStorage`。本项目往该文件追加一段代码，在页面加载早期写入 4K 档并持续看护
（页面会把"智能"重置回去）。`app.asar` 从不修改。

档位：`'30'`/`-3`=4K HDR，`'20'`/`-2`=4K，`'10'`/`-1`=2K，`'5'`/`1`=1080P，`'0'`/`0`=智能。
视频没有 4K 时页面会自动降级，不会播放失败。

---

## 精简直播间礼物栏

进直播间后，底部礼物栏只保留两样：

| 保留 | 隐藏 |
|---|---|
| 「更多 >」按钮（点开仍是完整礼物面板） | 每个礼物图标（三生有幸 / 金秋传爱意 / 秋至情圆 / 鲜花 …） |
| 钻石数量 | 送礼全屏特效、飘屏 |

**底部控制条（清晰度 / 全屏 / 音量）、弹幕、聊天区都保留**，视频画面不会压缩。

- 随「① 一键启用最高画质」一起装好（已并入 `injector/userscript.bundle.js`）
- 只想用这一个功能：把 `injector/live-gift-hide.standalone.js` 单独注入客户端即可
- 临时恢复：在控制台执行 `window.__DY_LIVE_GIFT_HIDE__ = false;` 后刷新

> 判定**不写死 class 名** —— 抖音的 class 是构建时哈希的，每次发版都会变。
> 改用文本特征：带价格的礼物项文本含「钻」，「更多」按钮文本是「更多」，
> 另加一条结构兜底。所以版本更新后依然有效。
>
> 上游脚本自带的「【屏蔽】底部的礼物栏」开关在当前版本**点了不起作用**，
> 它的选择器已失效（实测命中 0）。

---

## 直播间复制链接优化

直播间点「分享 → 复制链接」，拿到的是干净链接：

```
https://live.douyin.com/785963955393?enter_from=web_share&share_token=xyz
                              ↓
https://live.douyin.com/785963955393
```

- 随「① 一键启用最高画质」一起装好（已并入 `injector/userscript.bundle.js`）
- 只想用这一个功能：把 `injector/live-share-clean.standalone.js` 单独注入客户端即可
- 临时关闭：在控制台执行 `window.__DY_LIVE_SHARE_CLEAN__ = false;` 后刷新

> 上游脚本的「修改复制链接内容」只处理**视频页**（选择器写死
> `div[data-e2e="video-share-container"]`），直播间是另一套 DOM，所以直播页
> 此前拿到的仍是带参数的原始链接。本模块是补上这块。
>
> 链接来源按可靠性排序：React fiber 的 `shareInfo` / `roomInfo` / `web_rid`，
> 退回到地址栏。**两者都拿不到就不干预**，让页面按原样复制，不会让你复制失败。

---

## 脚本内置与外部升级

装载器**自带一份最新的脚本**，所以装完就能用，不需要额外放文件：

| 情况 | 实际用哪份 |
|---|---|
| 外部文件不存在 / 损坏 | **内置**（EXE / preload 里那份） |
| 外部版本 **比内置新** | **外部**（这就是外部升级） |
| 外部版本 **比内置旧或相同** | **内置** |

- 内置副本：`injector/userscript.bundle.embedded.js`，gzip + base64，约 409 KB
- 版本号：构建时间戳 `yyyyMMddHHmmss`，单调递增，无需手工维护
- 界面上「脚本」一栏会直接显示实际来源：`内置` / `外部，更新` / `外部，比内置旧 → 实际用内置`
- 运行日志里也会写明：`using external bundle (v… > embedded v…)` 或 `using embedded v…`

**怎么发布一个新脚本：**

```powershell
node tools\build-userscript.js          # 重新打包，同时刷新内置副本与版本号
# 然后把 injector\userscript.bundle.js 覆盖到 %APPDATA%\douyin\ 即可热更新
```

如果只想热更新、不动 EXE，直接替换 `%APPDATA%\douyin\userscript.bundle.js` 就行 ——
只要它的版本号比内置新，装载器就会自动采用它。

---

## 文件说明

| 文件 | 作用 |
|---|---|
| `dist/抖音画质助手.exe` | 单文件 GUI（推荐） |
| `启动画质助手.bat` + `douyin-quality-gui.ps1` + `gui.xaml` | PowerShell 版界面 |
| `douyin-quality.ps1` | 命令行工具 |
| `versions.json` / `CHANGELOG.md` | 版本清单 / 更新日志 |
| `injector/payload.js` | 画质强制器（注入 preload） |
| `injector/dy-host.js` | GM 兼容层 + 左侧栏设置入口 |
| `injector/userscript-loader.js` | preload 侧脚本装载器 |
| `injector/userscript.bundle.js` | 打包产物（自动生成） |
| `injector/userscript.bundle.embedded.js` | 内置副本（gzip+base64，构建时生成） |
| `injector/live-gift-hide.js` | 直播页隐藏礼物栏（已并入 bundle） |
| `injector/live-gift-hide.standalone.js` | 同上，独立注入版（只要这个功能时用） |
| `injector/live-share-clean.js` | 直播间复制链接优化（已并入 bundle） |
| `injector/live-share-clean.standalone.js` | 同上，独立注入版 |
| `injector/vendor/` | 用户脚本原文 + 4 个 UMD 依赖 |
| `exe-src/ProcessKiller.cs` | 结束抖音进程的实现 |
| `exe-src/PayloadComposer.cs` | 按注入方式组合 preload |
| `exe-src/UserscriptBundler.cs` | 解析并替换用户脚本 |
| `tools/` | 构建、部署、还原、检查、测试脚本 |

`backup/`、`work/` 不在仓库里，首次运行时在本机自动生成。

---

## 注意事项

1. **唯一暴露面**：`preload.js` 在官方 `md5.json` 清单内（1093 项之一），`douyin_guard.exe`
   会做哈希比对。目前多次启动未被拦截，若风控升级可用「② 还原」消除。
2. **抖音自动更新会覆盖**该文件，更新后重跑一次「①」即可；原始备份已固化不会丢。
3. 只影响**点播**（短视频）；直播清晰度是另一套机制（`webcast_local_quality`）。
4. 4K 档通常**需要登录**抖音账号。
5. 若抖音以管理员身份启动，需以管理员身份运行本工具才能结束其进程。

---

## 许可与引用

| 部分 | 许可 | 说明 |
|---|---|---|
| 本项目原创代码 | **MIT** | 派生自 GUAPIXIA 原项目 |
| 原项目 | **MIT** | [GUAPIXIA/Douyin_Mandatory_maximum_graphics_quality](https://github.com/GUAPIXIA/Douyin_Mandatory_maximum_graphics_quality) |
| 「抖音优化」用户脚本 | **GPL-3.0-only** | [WhiteSevs/TamperMonkeyScript](https://github.com/WhiteSevs/TamperMonkeyScript)，原样内嵌 |
| 4 个 UMD 依赖 | **MIT** | @whitesev/utils、@whitesev/domutils、@whitesev/pops、qmsg |

> ⚠️ `injector/userscript.bundle.js` 与打包了它的 EXE 属于 **GPL-3.0 组合作品**，
> 再分发需一并提供源码。只需 MIT 部分可在界面选「② 只装最高画质」。

完整引用清单见 **[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)**。
