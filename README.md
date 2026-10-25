# 抖音 PC 版 —— 强制最高画质（增强版）

**工具版本：1.4.1** ｜ [更新日志](CHANGELOG.md) ｜ 版本清单 [versions.json](versions.json) ｜ [MIT License](LICENSE) ｜ [第三方引用声明](THIRD-PARTY-NOTICES.md)

> **本项目基于 [GUAPIXIA/Douyin_Mandatory_maximum_graphics_quality](https://github.com/GUAPIXIA/Douyin_Mandatory_maximum_graphics_quality)（MIT）派生开发**，
> 并集成了 [WhiteSevs/TamperMonkeyScript](https://github.com/WhiteSevs/TamperMonkeyScript)（GPL-3.0）中的「抖音优化」用户脚本。
> 完整引用与许可说明见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

上游原项目：<https://github.com/GUAPIXIA/Douyin_Mandatory_maximum_graphics_quality>
本仓库（增强版）：<https://github.com/yss161/douyin-quality-helper-plus>

让抖音 PC 客户端**每个视频都按该视频可用的最高清晰度播放**（4K → 2K → 1080P），
不再被默认的"智能"档压到 1080P。

在原项目的基础上，本仓库额外做了：

- **结束抖音进程**：一个按钮把主程序 / 托盘 / 守护 / 崩溃上报全部正常关闭
- **抖音优化脚本集成**：用户脚本装进客户端，设置入口长在左侧导航里
- **注入内容三选一**：只装最高画质 / 只装脚本 / 两个都装
- **上传脚本更新**：换脚本不用重新打包，也不用再提权

---

## 提示
### 抖音pc端左下角 设置-> 通用设置 ->关于抖音 可以查看版本号
### 若你的版本低于8.7.0,请尝试更新版本,若可更新版本无法到达8.7.0,请使用抖音十几分钟,其会尝试自动更新,无需过度紧张
### 最简单使用方式,去右侧下载exe进行一键运行

---

## 支持的抖音版本

| 抖音版本 | 状态 | 说明 | 实测日期 |
|---|---|---|---|
| **8.7.0** | ✅ **已实测支持** | buildId 495279540；Electron 36.4.0-rs.33；`md5.json` 1093 项 | 2026-09-23 |
| 其它版本 | ⚠️ 未验证但默认允许 | 结构一致即可用，工具会警告并提示实测确认 | — |
| 已知不兼容版本 | ❌ 拒绝安装 | 目前没有；如发现会记录在 `versions.json` 并在界面标红 | — |

**运行时判定**：工具读取安装目录的 `launcher_config.json` 拿到版本号，并用官方文件哈希做指纹
（`app.asar` 我们从不修改，可随时与安装目录 `md5.json` 对账）。判定结果分四档：

| 状态 | 含义 | 界面表现 |
|---|---|---|
| `verified` | 这个精确版本实测通过 | 绿色「8.7.0（已实测支持）」 |
| `compatible` | 版本已登记但 `app.asar` 哈希不符 | 橙色「结构兼容，未实测」 |
| `unknown` | 清单里没有这个新版本 | 橙色「未验证的新版本」，**允许安装**并提示实测 |
| `unsupported` | 已知不兼容 | 红色「不受支持」，**拒绝安装** |

> 策略取向：注入只依赖"这个 preload 会挂到 `www.douyin.com`"这一结构事实，与版本号无关；
> 所以对新版默认放行 + 警告，由实测决定，而不是一律拒绝。已知不兼容才拦。

**在新版本上验证通过后**，可以把它登记进清单，以后界面就会显示"已实测支持"：


---

## 新克隆怎么开始

```powershell
# 1) 首次初始化：定位抖音、固化官方原始文件快照（只读抖音目录，只写本仓库）
pwsh -File tools\prepare.ps1

# 2) 启用最高画质（图形界面，推荐）
#    双击 启动画质助手.bat
#    选择 douyin.exe → 退出抖音 → 点「① 一键启用最高画质」

# 3) 验证
pwsh -File tools\check-inject.ps1
```

也可以只用命令行：

```powershell
pwsh -File douyin-quality.ps1 -Action install
pwsh -File douyin-quality.ps1 -Action version
pwsh -File douyin-quality.ps1 -Action restore
pwsh -File douyin-quality.ps1 -Action kill      # 结束所有抖音进程

# 安装/还原时自动结束在跑的抖音（而不是报错退出）
pwsh -File douyin-quality.ps1 -Action install -Force

# EXE 版也能直接结束
抖音画质助手.exe --kill --root "D:\douyin"
```

**关于 `backup/`**：它不在仓库里，而是在每台机器上按需生成。`tools\prepare.ps1`、GUI 的「①」、
CLI 的 install、`tools\deploy.ps1` 都会自动固化一份官方原版快照。即使没跑过 `prepare.ps1`
也能直接安装；跑它的好处是**安装前就能核对指纹**，并且让「还原」立即可用。

---


## 原理

抖音 PC 版是 **Electron 外壳 + 内嵌 `www.douyin.com` 网页播放器**，清晰度选择完全发生在网页里。
网页播放器每次构建播放配置时，会读 **`sessionStorage` 的 `MANUAL_SWITCH`**，取其中的 `gearType`
作为 `config.shared.definition.userSelectDefinition`。

而给 `www.douyin.com` 挂 preload 的那个文件是 **asar 外的 loose 文件**：

它在该页面上以 **`isolated=false`**（与页面同一 JS 上下文）运行，所以可以直接改写页面的
`sessionStorage`。本项目就是往这个文件追加一段注入代码，让它在页面加载早期把
`MANUAL_SWITCH` 写成 4K 档，并持续看护（页面会把"智能"重置回去）。

---

## 实测依据

| 事实 | 证据 |
|---|---|
| 画质偏好存在 sessionStorage，键名 `MANUAL_SWITCH` | 实测值落盘于 `%APPDATA%\douyin\Session Storage\leveldb`，内容含 `"gearName":"超清 4K"` |
| 键值格式 | `{"clarityReal":[...],"done":1,"gearClarity":"20","qualityType":1,"gearName":"超清 4K","gearType":-2}` |
| 档位表 | `gearClarity`/`gearType`：`'30'`/`-3`=超清4K HDR，`'20'`/`-2`=超清 4K，`'10'`/`-1`=超清 2K，`'5'`/`1`=高清 1080P，`'4'`/`2`=高清 720P，`'3'`/`3`=标清 540P，`'2'`/`4`=极速，`'0'`/`0`=智能 |
| HDR 档的取舍 | 常量表有新旧两版，旧版没有 `HD4KHDR(-3)`；因此只在 `clarityReal` 明确含 `adapt_lowest_hdr_4_1` 时才写 `-3`，否则用 `-2` |
| `qualityType` 不可推导 | 同一个 `gearClarity:"20"` 实测出现过 `qualityType:1` 与 `:72`，它是服务端每条流自带的 definition code |
| "智能"为什么卡在 1080P | AB 配置 `bitrate_selector.white_list` 的最高档就是 `normal_1080_0` / `adapt_lowest_1080_1` |
| 重启后回到智能 | sessionStorage 随进程结束而丢失，所以必须每次开页强制 |
| 4K 档需要登录 | AB `need_login_qualities: ["uhd","uhd50p"]` |
| 注入点在 loose preload | 实机日志：`[boot] loose-preload 已加载 url=https://www.douyin.com/ isolated=false` |
| 页面自身会优雅降级 | 请求 4K 但视频没有 4K 时，页面把 `gearType` 20 →（有 1440 则 10，否则 1） |

---

## 文件说明

| 文件 | 作用 |
|---|---|
| `启动画质助手.bat` | **双击即用**的图形界面入口（给新手用户） |
| `douyin-quality-gui.ps1` | 图形界面逻辑：选择/探测抖音位置、版本检查、一键启用、还原、检查 |
| `gui.xaml` | 界面（唯一含中文的文件，必须与上面的脚本同目录） |
| `config.json` | 记住用户选定的抖音安装目录（首次系统运行自动生成） |
| `douyin-quality.ps1` | 命令行一键脚本：菜单 + 参数双模式（`-Action kill` 结束抖音进程） |
| **`versions.json`** | **版本兼容性清单（唯一数据源）**：工具版本号、注入契约、各抖音版本的指纹与实测状态 |
| **`CHANGELOG.md`** | **更新日志**：每个版本做了什么、修了什么问题、实测环境 |
| `tools/douyin-version.ps1` | 版本兼容性检查实现（GUI 与 CLI 共用） |
| `tools/record-version.ps1` | 在新版抖音上实测通过后，把该版本登记进 `versions.json` |
| `tools/patch-preload.js` | **权威**注入实现（CLI 与 GUI 都调用它；GUI 内嵌 base64 副本由 `tools/sync-gui-patcher.js` 同步） |
| `exe-src/ProcessKiller.cs` | **结束抖音进程**的唯一实现：枚举 / 正常关闭 / 强制结束 / 复核 |
| `injector/payload.js` | **核心**。画质强制器，被追加进 preload |
| `injector/dy-host.js` | GM 兼容层 + 左侧栏设置入口（让用户脚本能在客户端里跑） |
| `injector/userscript-loader.js` | preload 侧装载器：读 `%APPDATA%` 里的脚本包并执行 |
| `injector/userscript.bundle.js` | 打包产物（自动生成，由 `tools/build-userscript.js` 产出） |
| `tools/build-userscript.js` | 把 GM 层 + 依赖 + 原版用户脚本拼成 bundle（脚本段带可替换标记） |
| `exe-src/UserscriptBundler.cs` | 解析 UserScript 头部、替换 bundle 里的脚本段 |
| `exe-src/PayloadComposer.cs` | 按「注入内容」组合 preload（三种模式） |
| `tools/fetch-vendor.ps1` | 下载用户脚本依赖的 UMD 库 |
| `tools/deploy.ps1` | 分步部署：备份原始文件 → 生成注入版 → 写入安装目录 |
| `tools/restore.ps1` | 分步还原为官方版本（校验 md5） |
| `tools/check-inject.ps1` | 分步检查日志 |
| `tools/test-payload.js` | 本地单测（14 项，覆盖创建/拉高/降级/HDR/容错） |
| `tools/sync-gui-patcher.js` | 改过 `patch-preload.js` 后，用它把 GUI 内嵌副本同步一致 |
| `backup/loose-preload/preload.js.orig` | **固化原始备份**，md5 `bfcb8e31fae5a7370c5bee258ffcdfad`（与安装目录 `md5.json` 一致） |

---

## 用法

### 图形界面（推荐给新手用户）

**双击 `启动画质助手.bat`**

界面里三步搞定：**① 选择抖音程序 + 一键启用 → ② 启动抖音看视频 → ③ 检查生效情况**。

### 内置「抖音优化」脚本

启用补丁后，[抖音优化](https://github.com/WhiteSevs/TamperMonkeyScript) 这个用户脚本会一起装进客户端：
**左侧导航下方多出一个「抖音优化」分组**，点「⚙ 设置」就能像在油猴里那样打开完整设置面板
（视频过滤、屏蔽弹窗、清晰度、快捷键……）。

用户脚本本身一行没改——客户端里没有油猴，所以项目自带一个 GM 兼容层
（`injector/dy-host.js`）：`GM_getValue` 等存储接口落在页面 localStorage，
`GM_registerMenuCommand` 注册的菜单项被渲染成左侧栏按钮。

脚本包放在 `%APPDATA%\douyin\userscript.bundle.js`（**不在安装目录**），
所以以后换脚本版本只要替换这个文件，不用再往 `C:\Program Files` 提权写一次。

### 注入内容三选一

界面上的「注入内容」可以只装一部分：

| 选项 | 装什么 |
|---|---|
| ① 最高画质 + 抖音优化脚本 | 默认，两者都装 |
| ② 只装最高画质 | 原项目的自动高画质，完全不碰用户脚本 |
| ③ 只装抖音优化脚本 | 不强制画质，只装脚本 |

选择记在 `config.json`，并同步写 `%APPDATA%\douyin\inject-mode.json`
（`{"scripts":…,"quality":…}`），装载器和画质强制器各自读它，所以即使
preload 里两段代码都在，也会按你的选择关掉其中一半。

### 上传脚本更新

点「**上传脚本更新…**」，选一个 `.user.js` 即可：

- 只替换 bundle 里的**脚本本体**，GM 兼容层和依赖原样保留
- 会校验 `==UserScript==` 头部与 `@name`，并弹窗确认名称 / 版本 / 作者
- 界面实时显示当前脚本（名称 + 版本 + 路径），与内置版本不同时标注「（已上传）」
- 「**恢复内置脚本**」丢弃上传内容
- 再次点「①」**不会**覆盖你上传的脚本

自己改脚本（开发用）：

```powershell
pwsh -File tools\fetch-vendor.ps1     # 按 @require 的版本拉 UMD 依赖（首次）
node tools\build-userscript.js        # GM 层 + 依赖 + 原版脚本 -> bundle
# 然后把 injector\userscript.bundle.js 复制到 %APPDATA%\douyin\（或重跑一次「①」）
```

改文件前抖音必须完全退出（只关窗口不算）。不想自己去任务管理器里找进程的话，
直接点界面上红色的「**结束抖音进程**」：它会先把当前在跑的进程列出来
（`douyin.exe(1234，主程序)、douyin_guard.exe(5678，守护进程)`），确认后
**先请求正常关闭、再强制结束、最后重新枚举复核**，确认真的没了才继续。

点「①」/「②」时如果抖音还在运行，也会自动问你要不要结束它们，不需要再手动处理。

### 关于选择抖音位置（第一步，必须由用户操作）

工具**不自动选定**路径，必须由你亲自点「浏览…」选中安装目录里的 **`douyin.exe`**：

- 界面顶部会用橙色卡片提示「第一步：请选择抖音程序」，路径框显示「尚未选择」
- 此时 **①/②/③ 三个按钮都是灰的（不可点）**，只有「浏览…」可点 —— 防止误操作
- 选定后路径框显示确认结果（例如 `D:\douyin`），三个按钮才变可用
- 选完会记到 `config.json`，下次启动对话框会直接打开到该目录

工具启动时会**自动探测**抖音位置（运行中的进程、常见安装目录、注册表卸载项、各盘根目录），
但**只作为日志提示**、不会自动填进路径框。选择器很宽容：选 `douyin.exe`、选版本子目录、
甚至选安装根目录都能正确识别；选中无关文件会被拒绝并给出提示。

> 界面文件是 `gui.xaml`（必须和脚本放在同一目录）。`.ps1` 里的字面量刻意保持纯 ASCII：
> 中文写在 `.ps1` 字符串里，在 Windows PowerShell 5.1 下会因编码被重新解释而报错，所以中文只放在 XAML 里。

---

## 行为策略

- **优先 4K**：写 `gearType:-2 / gearClarity:'20' / gearName:'超清 4K'`
- **HDR 4K 更优**：若该视频 `clarityReal` 里确有 `adapt_lowest_hdr_4_1`，改写 `gearType:-3 / gearClarity:'30' / '超清4K HDR'`
- 页面自动降级：视频没有 4K 时自动用 2K 或 1080P，不会播放失败
- 注入器还会读 `clarityReal`（该视频真实可用档位列表）做预判：
  明确只有 1440 → 写 2K；明确只有 1080P → 不干预
- 看护 3 分钟（每秒一次），页面把"智能"重置回去时会拉回
- 沙箱帧（`about:blank` 等）访问 `sessionStorage` 会抛 `SecurityError`，这类文档一次性跳过，不刷日志
- **结束抖音进程**只处理 `versions.json → processes` 里登记的进程名：
  抖音专属名字（`douyin` / `douyin_guard` / `douyin_tray` / `douyin_widget`）按名字认；
  字节系通用组件名（`parfait_crash_handler` 等）必须确认 exe 就在选定的抖音目录下才动手

---

## 已知风险与注意事项

1. **`resources\app.asar.unpacked\preload.js` 在 `md5.json` 里**（1093 项清单之一）。
   该清单主要用于 `app_shell_updater.exe` 的 bin 更新校验；`douyin_guard.exe` 会从服务端
   取指令并做 md5 比对。目前多次启动均未被拦截，但**如果风控升级，这是唯一的暴露面**。
   一键还原即可消除。
2. **抖音自动更新会覆盖**这个文件（`launcher_config.json: need_update=false`，但整体重装会重置）。
   更新后重新跑一次「① 一键启用最高画质」即可；原始备份已固化，不会丢失。
   更新到新版本号后，工具会显示「未验证的新版本」，实测通过可用 `tools/record-version.ps1` 登记。
3. `gearType` 在部分分支用字符串比较（`10===R / 20===R`），因此注入器写入的是**数字** `-2`，
   与网页自身点选菜单时写入的类型一致。
4. 只影响点播（短视频）；直播清晰度是另一套（`webcast_local_quality`，localStorage），
   当前实现会顺带把 `live.douyin.com` 页面的 `MANUAL_SWITCH` 也写成 4K，但直播不受它控制。
5. 4K 档通常**需要登录**抖音账号；未登录时页面会降级到可用档位。
6. 如果抖音是**以管理员身份**启动的，普通权限下结束不了它的进程：工具会提示
   「请以管理员身份运行」，升级后重试即可。

---

## 版本管理约定

- **工具版本**记录在 `versions.json → tool.version`，同时写进 `CHANGELOG.md`。
  界面标题栏与 `-Action version` 都会显示。
- **抖音版本**以 `launcher_config.json` 的 `cur_path`（即安装目录下的版本文件夹名）为版本号，
  用官方文件哈希做指纹：
  - `resources\app.asar` —— 我们**从不修改**，可随时与安装目录 `md5.json` 对账
  - `resources\app.asar.unpacked\preload.js` —— 注入目标，其**原始**哈希与大小记录在册
- 新增受支持版本：在真机启用补丁 → 打开视频确认清晰度 → 运行 `tools/record-version.ps1`。
- 修改注入实现或兼容性策略 → 提升 `tool.version` 并在 `CHANGELOG.md` 增条目。

---

## 许可与引用

| 部分 | 许可 | 说明 |
|---|---|---|
| 本项目原创代码 | **MIT**（见 [LICENSE](LICENSE)） | 派生自 GUAPIXIA 的原项目 |
| 原项目代码 | **MIT** | [GUAPIXIA/Douyin_Mandatory_maximum_graphics_quality](https://github.com/GUAPIXIA/Douyin_Mandatory_maximum_graphics_quality) |
| 「抖音优化」用户脚本 | **GPL-3.0-only** | [WhiteSevs/TamperMonkeyScript](https://github.com/WhiteSevs/TamperMonkeyScript)，原样内嵌 |
| 脚本依赖的 4 个 UMD 库 | **MIT** | @whitesev/utils、@whitesev/domutils、@whitesev/pops、qmsg |

> ⚠️ `injector/userscript.bundle.js` 与打包了它的 `dist/*.exe` 属于
> **GPL-3.0 组合作品**（含 GPL 的用户脚本）。再分发这些产物时请一并提供源码
> 并保留 GPL-3.0 许可。若只需 MIT 部分，界面选「② 只装最高画质」即可。

完整的引用清单、版本号和获取方式见 **[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)**。
