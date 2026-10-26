# 更新日志

本项目版本号规则：`主版本.次版本.修订号`
- **主版本**：注入方式或兼容性策略发生不兼容变化
- **次版本**：新增功能（GUI、版本检查等）
- **修订号**：修 bug、调整文案

工具版本号记录在 [versions.json](versions.json) 的 `tool.version`，界面与 `-Action version` 都会显示。

---

## [1.6.0] - 2026-10-26

**直播间复制链接优化**：点「分享 → 复制链接」拿到的是干净链接，不再带追踪参数。

### 新增

- **`injector/live-share-clean.js`**：直播间的复制链接优化
  - 拦截分享面板里「复制链接」的点击，自己写入清洗后的链接，并阻止页面原本的复制
  - `https://live.douyin.com/785963955393?enter_from=web_share&share_token=xyz`
    → `https://live.douyin.com/785963955393`
  - 链接来源按可靠性排序：React fiber 的 `shareInfo` / `roomInfo` / `web_rid`，
    退回到地址栏 `pathname`；**两者都拿不到就不干预**，让页面按原样复制
  - 剪贴板优先 `navigator.clipboard.writeText`，失败退回 `execCommand('copy')`
- **`injector/live-share-clean.standalone.js`**：独立注入版，只要这个功能时用

### 说明

上游脚本的「修改复制链接内容」只处理**视频页**
（选择器写死 `div[data-e2e="video-share-container"]`），直播间是另一套 DOM，
所以直播页此前拿到的一直是带参数的原始链接。本模块补上这块。

### 单测（19 项，全部通过）

```
链接清洗 9 项：直播链接去参数 / 带 www / 短链 / 视频链接 / 非抖音域名不动 /
              空值 / null / 非法输入
fiber 提取 5 项：shareInfo.shareUrl / roomInfo.shareUrl / web_rid 拼接 /
                 无信息 / 非抖音域名忽略
按钮识别 5 项：复制链接 / 带图标 / 非目标 / 超大容器 / null
端到端：点击「复制链接」→ 剪贴板得到干净链接，且原逻辑被阻止
```

---

## [1.5.2] - 2026-10-25

**修：「更多」左侧变成直角**，补成和右侧一样的圆角。

### 修复

礼物项原本是"一整条"：第一个带左圆角、最后一个带右圆角、中间都是直角。
把前面的礼物藏掉后，最先露出来的「更多」就只剩右圆角，左边是一刀切的直角。

实测它的计算样式：

```
.NZICWeHy（更多）
  border-radius: 0px 8px 8px 0px     ← 左直角、右 8px
```

修复：隐藏礼物后，在整条礼物栏里找"右侧有圆角、左侧是直角"的最左可见块，
把它的左圆角补成与右圆角相同的值。修完：

```
border-radius: 8px   （四角一致）
```

注意「更多」并不是礼物列表的子节点，而是列表的**兄弟节点**（两者同属一条 row），
所以不能只在 `list.children` 里找，必须在整条栏范围内扫描。

---

## [1.5.1] - 2026-10-25

**礼物栏改成"精简"而不是"全隐"**：按需求只保留「更多」二级菜单入口和钻石。

### 变更

- `injector/live-gift-hide.js` 的行为从"隐藏整条礼物栏"改成"只留两样"：
  - **保留**：「更多 >」按钮（点开仍是完整礼物面板）、钻石数量
  - **隐藏**：每个礼物图标（三生有幸 / 金秋传爱意 / 秋至情圆 / 鲜花 …）
  - 送礼全屏特效与飘屏仍然隐藏
  - 礼物面板 `#GiftMenuLayout` 不再隐藏 —— 否则点「更多」没反应

- **判定改用文本特征，不写死 class 名**：
  抖音的 class 是构建时哈希的（`xAG9cqUC` / `saw7uCUW` / `NMfqnFm5` …），每次发版都会变。
  现在按「带价格的礼物项文本含 `钻`」「更多按钮文本是 `更多`」来判断，
  另加一条结构兜底（除「更多」「钻石」外全部隐藏）。

- 文本读取用 `textContent` 而不是 `innerText`：
  `innerText` 依赖布局，未渲染的节点会返回空串导致判不出来，而且会强制重排。

### 实测

`live.douyin.com/82485729980`，礼物栏真实渲染出 14 个礼物项时：

```
隐藏：1钻小心心 / 99钻Thuglife / 50钻天鹅之梦 / 99钻蝶恋秋韵 /
      1钻人气票 / 1500钻金秋传爱意 / 1钻玫瑰 / 1钻抖音 /
      1888钻秋雨暖阳 / 2钻大啤酒 / 299钻比心兔兔 / 5200钻秋至情圆 /
      520钻热气球 / 9钻棒棒糖
保留：更多（65x72，可见）、8（钻石，可见）
```

---

## [1.5.0] - 2026-10-25

**隐藏直播间礼物**：底部那一排礼物图标（小心心 / 人气票 / 蝶恋秋鹤 / 鲜花 …）不再显示。

### 新增

- **`injector/live-gift-hide.js`**：直播页隐藏礼物相关元素
  - 底部礼物栏（截图红框那一排）、点开的礼物面板、送礼全屏特效与飘屏、礼物充值入口
  - 保留：底部控制条（清晰度 / 全屏 / 音量）、弹幕、聊天区、底部其他按钮
  - 纯 CSS 隐藏，不动 DOM 结构，不会破坏 React 的事件绑定
  - MutationObserver 兜底：SPA 切房间重建 DOM 后自动补回样式
  - 想临时恢复：控制台执行 `window.__DY_LIVE_GIFT_HIDE__ = false;` 后刷新
- **`injector/live-gift-hide.standalone.js`**：独立注入版，只要这个功能时直接注入它
- 该模块已并入 `injector/userscript.bundle.js`，随「① 一键启用最高画质」一起装好

### 修复

**上游「屏蔽底部礼物栏」开关点了没反应**

| 问题 | 原因 | 处理 |
|---|---|---|
| `live-shieldGiftColumn` 里的选择器在当前版本已失效 | 实测两条规则命中数都是 0：`xg-controls > div:has(div[data-e2e="gifts-container"])`、`>div> div:has(>pace-island>.gitBarOptimizeEnabled)` | 新模块改用实测有效的 `.gitBarOptimizeEnabled`（命中 1）并补 `#BottomLayout` 定位 |

> 实测环境：`live.douyin.com/82485729980`，确认存在
> `.gitBarOptimizeEnabled`、`#GiftEffectLayout`、`#GiftTrayLayout`、`#GiftMenuLayout`、`.giftRechargeContaner`。
> 隐藏后 `#BottomLayout` 高度归 0，视频区域仍为 1332x856，未出现留白或压缩。

---

## [1.4.1] - 2026-10-25

**开源发布**：补全第三方引用声明，准备上传 GitHub。

### 新增

- **`THIRD-PARTY-NOTICES.md`**：逐项说明引用了哪些原项目
  - 原项目 GUAPIXIA/Douyin_Mandatory_maximum_graphics_quality（MIT，派生开发）
  - 「抖音优化」用户脚本 WhiteSevs/TamperMonkeyScript（GPL-3.0-only，原样内嵌，版本 2026.10.23）
  - 4 个 UMD 依赖库（MIT，版本与 `@require` 一致）
  - 运行环境依赖、免责声明、引用一览简表
- README 顶部加引用横幅，末尾加「许可与引用」小节
- LICENSE 的第三方说明从一句话扩成完整清单

### 说明

- `injector/userscript.bundle.js` 与打包了它的 EXE 属于 **GPL-3.0 组合作品**，
  再分发需同时提供源码；只要 MIT 部分可在界面选「② 只装最高画质」

---

## [1.4.0] - 2026-10-25

**注入方式可选 + 上传脚本更新**：不再只有一个"全都要"的开关，脚本也能自己换。

### 新增

- **「注入内容」三选一**（界面上的单选，选择会记住）
  - **① 最高画质 + 抖音优化脚本**（默认，和以前一样）
  - **② 只装最高画质** —— 原项目的自动高画质，完全不碰用户脚本
  - **③ 只装抖音优化脚本** —— 不强制画质，只装脚本
  - 实现：`PayloadComposer` 按选择拼 preload；同时往
    `%APPDATA%\douyin\inject-mode.json` 写 `{"scripts":…,"quality":…}`，
    装载器与画质强制器各自读它，双保险
- **上传脚本更新**：选一个 `.user.js`，只替换 bundle 里的脚本段，
  **GM 兼容层和 UMD 依赖原样保留**
  - 校验：必须有 `==UserScript==` 头部和 `@name`，否则拒绝并说明原因
  - 上传前弹窗确认名称 / 版本 / 作者 / 描述，写入后回读校验
  - 界面上实时显示当前装的是哪个脚本（名称 + 版本 + 路径），
    与内置版本不同时标注「（已上传）」
  - **恢复内置脚本**按钮：丢弃上传内容
  - 「① 一键启用最高画质」不会覆盖掉已上传的脚本
- **bundle 结构可替换**：脚本本体用 `SCRIPT-BEGIN` / `SCRIPT-END` 包起来
  （`tools/build-userscript.js` 与 `exe-src/UserscriptBundler.cs` 两侧标记一致），
  所以换脚本不需要重新打包依赖
- **自检**新增 8 项：三种注入模式的组合与互斥校验、脚本段替换后依赖是否保留、
  元信息回读、非用户脚本拒绝

### 修复

**注入的脚本在客户端里没生效**（显式报错：`Cannot read properties of null (reading 'childNodes')`）

| 问题 | 原因 | 处理 |
|---|---|---|
| 脚本一跑到 `DOMUtils.addStyle` 就抛异常 | Electron preload 比油猴的 `document-start` 更早执行，那时 `document.documentElement` 还是 `null`；domutils 在 `document.head` 不存在时会读 `documentElement.childNodes` | 宿主提供 `__DY_DEFER_SCRIPT__`，脚本体等 `<html>` 出现再执行；装载器也做同样等待，两处都兜住 |
| 脚本段没有可替换的边界 | bundle 只有整个文件 | 加 `SCRIPT-BEGIN` / `SCRIPT-END` 标记，上传时只换中间那段 |

- 同时把脚本执行失败的原因写进 `dy-force.log`（带完整调用栈），以后再出问题不用猜

---

## [1.3.0] - 2026-10-25

**把「抖音优化」用户脚本搬进客户端**：打开抖音就能用，设置入口直接长在左侧导航里。

### 新增

- **GM 兼容层**（`injector/dy-host.js`）：实现 `GM_getValue/setValue/deleteValue/listValues/setValues`、
  `GM_registerMenuCommand`、`GM_xmlhttpRequest`、`GM_download`、`GM_info` 等
  - 存储落在页面 `localStorage`（`dy-userscript::` 前缀），跨重启保留
  - 原版用户脚本**一行不改**直接跑
- **左侧栏设置入口**：油猴菜单在这类客户端里没有入口，所以把 `GM_registerMenuCommand`
  注册的菜单项渲染成左侧导航里的按钮（「抖音优化」分组）
  - 用 `a[href*="from_nav"]` 的公共祖先定位侧栏，**不依赖构建时哈希的 class 名**
  - MutationObserver 自动补回：切路由 / 重新渲染后被冲掉会自己长回来
  - 浅色 / 深色主题自适应
- **轻量装载器**（`injector/userscript-loader.js`）：preload 侧从
  `%APPDATA%\douyin\userscript.bundle.js` 读取脚本包再交给页面执行
  - **脚本包放在用户目录**：以后换脚本版本不用再往 `C:\Program Files` 提权写入
  - 文件不存在时静默跳过，老安装行为完全不变
- **打包工具** `tools/build-userscript.js`（GM 层 + 依赖 + 原版脚本 → 单个 bundle）
  与 `tools/fetch-vendor.ps1`（按 `@require` 的版本下载 UMD 依赖）
- **画质强制器与优化脚本共存**：用户在优化脚本里选了别的清晰度后，
  画质强制器**让位**、不再拉回 4K；选「无」时仍由强制器兜底

### 说明

- 客户端启动器不认 `--remote-debugging-port`，所以调试走 preload 注入这条现成通道
- 原版脚本依赖 CDN 上的 4 个 UMD 库，已按 `@require` 中标注的版本
  （utils 2.15.2 / domutils 2.0.12 / pops 4.2.10 / qmsg 1.7.3）落到 `injector/vendor/`

---

## [1.2.0] - 2026-10-25

**新增「结束抖音进程」**：启用/还原补丁前不必再自己去任务管理器里找进程，一个按钮搞定。

### 新增

- **结束抖音进程（图形界面）**：新增红色的「结束抖音进程」横幅与按钮
  - 实时显示到底有几个进程在跑、分别是谁，例如
    `douyin.exe(1234，主程序)、douyin_guard.exe(5678，守护进程)`
  - 点「① 一键启用最高画质」/「② 还原官方原版」时，如果抖音还在运行，
    会弹窗问「现在结束它们吗？」，确认后自动结束并继续，不再直接把你挡回去
  - 每 2.5 秒自动刷新运行状态，在托盘里退出抖音后界面会自己跟上，不用点刷新
- **结束进程的两段式处理**：先请求正常关闭（抖音有机会自己退出、保住登录态），
  等 2.5 秒还在就强制结束；最后**重新枚举复核**，只以"进程确实没了"为结论
- **覆盖抖音的全部附属进程**：主程序 `douyin.exe`、托盘 `douyin_tray.exe`、
  桌面组件 `douyin_widget.exe`、守护 `douyin_guard.exe`，
  以及会占着安装目录的崩溃上报/推送检测等组件
- **命令行**：`抖音画质助手.exe --kill [--root <抖音安装目录>]`
  （退出码 0 = 全部结束，3 = 仍有进程没结束）；脚本侧新增
  `pwsh douyin-quality.ps1 -Action kill`、`-Force`（安装/还原时自动结束而非报错）
- **权限提示**：如果抖音是以管理员身份启动的，本工具结束不了，会明确提示
  「以管理员身份运行」后再试

### 安全边界

- 只处理名单内的进程名；名字专属抖音的（`douyin` / `douyin_guard` /
  `douyin_tray` / `douyin_widget`）按名字识别
- 字节系通用组件名（`parfait_crash_handler` / `tt_crash_reporter` /
  `push_detect` / `douyin_doctor` / `dump_reporter`）**必须确认 exe 就在
  你选定的抖音安装目录下**才动手，不会误杀同名的其它程序
- 进程名单登记在 `versions.json → processes`

### 修复

| 问题 | 原因 | 处理 |
|---|---|---|
| 抖音在运行时只能干瞪眼 | 启用/还原前只做「进程检查 + 报错退出」 | 改为提示后一键结束，返回码来自重新枚举而不是"Kill() 没报错" |
| 只按 `douyin` 判断是否在运行 | 守护/托盘进程同样占着文件 | 运行状态改为统一枚举全部相关进程 |
| `build.ps1` 在没有 .NET 目标包的机器上直接失败 | 硬编码 `Reference Assemblies\...\v4.8` | 找不到时回退到 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF` |

---

## [1.1.0] - 2026-09-28

**面向小白的单文件 EXE 发布包**（`dist\抖音画质助手.exe`）。

### 新增

- **C# WPF 单文件 EXE**（`.NET Framework 4.8`，`csc` 直接编译，约 60KB）
  - 双击即用：不需要 Node.js，不需要 PowerShell，无执行策略问题
  - 界面与功能对齐 `gui.xaml`：选路径 / 一键启用 / 还原 / 检查 / 版本状态
  - 注入实现内嵌于 EXE，与 `tools/patch-preload.js` **字节级一致**（已用原始 preload 对拍）
  - 配置、备份、`versions.json` 都放在 **EXE 同目录**（便携绿色包）
- **无头自检**：`抖音画质助手.exe --selftest`
- **兼容 CLI 的补丁入口**：`--patch --src --payload --out`（便于与 Node 对拍）
- 构建脚本：`exe-src/build.ps1`、`exe-src/pack.ps1`

### 说明

- 旧入口（`启动画质助手.bat` + PowerShell）仍保留，便于开发调试
- 发布包内容：`抖音画质助手.exe` + `使用说明.txt` + `versions.json`

---

## [1.0.0] - 2026-09-23

首个可用版本。核心能力：**让抖音 PC 版每个视频都按该视频可用的最高清晰度播放（4K → 2K → 1080P）**。

### 逆向结论（方案的基础，全部实测）

| 结论 | 依据 |
|---|---|
| 抖音 PC 版 = Electron 外壳 + 内嵌 `www.douyin.com` 网页播放器 | 内嵌网页 + 本地预载包 |
| 清晰度偏好存在网页 **sessionStorage 的 `MANUAL_SWITCH`**，不在 localStorage/cookie/IndexedDB | 实测落盘值 + 网页 bundle 里的读写函数 |
| 该偏好**重启即丢**，所以必须每次开页强制 | 用户实测：重启后回到智能 |
| 真正挂到 `www.douyin.com` 的 preload 是 **asar 之外**的 `resources\app.asar.unpacked\preload.js`，且 `isolated=false` | 注入磁盘日志：`[boot] loose-preload loaded url=https://www.douyin.com/ isolated=false` |
| 档位表 | `gearClarity` `'30'`/`'20'`/`'10'`/`'5'`/`'4'`/`'3'`/`'2'`/`'0'`，对应 `gearType` `-3`/`-2`/`-1`/`1`/`2`/`3`/`4`/`0` |
| "智能"卡在 1080P 的原因 | AB 配置 `bitrate_selector.white_list` 最高档即 `normal_1080_0` / `adapt_lowest_1080_1` |
| 页面自身会按视频实际档位优雅降级 | 请求 4K 但视频无 4K 时，页面把 `gearType` 20 →（有 1440 则 10，否则 1） |

### 新增

- **图形界面** `douyin-quality-gui.ps1` + `gui.xaml`，双击 `启动画质助手.bat` 即可用
  - 三步流程：选抖音程序 → 一键启用 → 启动抖音看视频 → 检查生效
  - **必须由用户亲自选择 `douyin.exe`**：未选择时路径框显示「尚未选择」、三个功能按钮全部禁用
  - 选择器容错：选 `douyin.exe`、版本子目录、安装根目录都能识别；无关文件会被拒绝
  - 实时状态：抖音版本/补丁状态/程序主体哈希/原始备份/抖音是否在运行
- **命令行工具** `douyin-quality.ps1`，菜单 + 参数双模式
  - `-Action status | install | restore | check | test | version`
- **版本兼容性管理** `versions.json` + `tools/douyin-version.ps1`
  - 用官方文件哈希做版本指纹；`app.asar` 从不修改，可随时与安装目录 `md5.json` 对账
  - 三种判定：`verified`（实测通过）/ `compatible`（结构兼容）/ `unknown`（未验证的新版，警告但允许）/ `unsupported`（拒绝）
  - `-Action version` 查看结论；`tools/record-version.ps1` 实测通过后一键登记新版本
- **一键还原** `tools/restore.ps1`、GUI 的「② 还原官方原版」
  - 以固化备份 `backup/loose-preload/preload.js.orig` 为准（md5 与安装目录 `md5.json` 一致，可自证是原版）
  - 附带清理早期版本可能在 `app.asar` 留下的改动
- **诊断日志** 写到 `%APPDATA%\douyin\dy-force.log`，`-Action check` 与 GUI「③」可解读

### 修复（开发过程中踩到并解决的真实问题）

| 问题 | 原因 | 处理 |
|---|---|---|
| 注入到 `app.asar` 里的 preload 完全无效 | 那个 preload 不挂到 `www.douyin.com` | 改注入 asar 之外的 loose preload |
| `NODE_OPTIONS=--require` 主进程注入失败 | 该 Electron fuse 虽标注开启，但实机无任何反应 | 放弃该路线，相关代码已移除 |
| `NODE_OPTIONS` 方案会触发 md5 校验风险 | `md5.json` 收录 1093 项且 guard 会做 md5 比对 | 改走"只动 1 个文件、且可一键还原"的低暴露方案 |
| `.bat` 报 `'NoProfile' 不是内部或外部命令` | ① 换行是 LF（cmd 会解析错乱）② `"%~dp0x.ps1"` 中 `%~dp0` 以反斜杠结尾，cmd 里 `\"` 不是转义 | 改 CRLF + `cd /d "%~dp0"` + 相对文件名 |
| 双击 bat 崩溃：`Get-FileHash` 找不到 | `cmd → powershell` 起的是 **Windows PowerShell 5.1**，它没有 `Get-FileHash`（PowerShell 7 有） | 全部哈希计算改为纯 .NET，5.1/7 都能跑且结果一致 |
| `.ps1` 里中文导致解析错误 | 5.1 在编码被重新解释时会把字符串引号搞乱 | `.ps1` 内字面量保持纯 ASCII，中文只放 `gui.xaml` |
| GUI 与 CLI 装出的文件字节不同 | 两个入口各有一份注入实现 | 统一为 `tools/patch-preload.js` 唯一权威；GUI 用 base64 内嵌副本并由 `tools/sync-gui-patcher.js` 同步，实测两边 sha256 一致 |
| 注入器对自己的钩子读值，判断失效 | `forceManualSwitch` 走 `sessionStorage.getItem` 读到自己伪造的"假 4K" | 强制逻辑改用保存的原始 `getItem/setItem` |
| 对 1080P-only 视频硬写 4K | 无脑覆盖档位 | 改为读 `clarityReal`（该视频真实可用档位）后决定，并信任页面自身的降级 |
| 沙箱帧刷屏 `SecurityError` | `about:blank` 等文档不允许访问 `sessionStorage`，每秒重试 | 一次性永久跳过该类文档 |
| 日志爆炸（155 行/分钟） | 正常状态也写日志 | 只在"创建/拉高/降级"时记录 |
| 二次部署把已打补丁的文件当成原始备份 | 备份逻辑未校验内容 | 增加指纹校验 + 固化 `backup/app.asar.orig`，并在还原时自动清理 |

### 已知限制

- 只影响**点播（短视频）**清晰度；直播是另一套机制（`webcast_local_quality`，localStorage）
- 4K 档通常**需要登录**抖音账号
- `resources\app.asar.unpacked\preload.js` 在安装目录 `md5.json` 的清单内，属于唯一暴露面：
  多次启动未被拦截，但若风控升级可能被检测；随时可用「② 还原」消除
- 抖音整体更新会覆盖该文件，更新后需重新运行一次「① 一键启用最高画质」

### 实测环境

- Windows 11
- 抖音 PC 版 **8.7.0**（buildId 495279540，Electron 36.4.0-rs.33.release.pgo.0，md5.json 1093 项）→ 状态 `verified`
- 工具同时兼容 Windows PowerShell 5.1 与 PowerShell 7.6

---

## 后续版本规划

- [ ] 直播清晰度（`webcast_local_quality`）
- [ ] 可选策略：只锁 1080P 高码率（`normal_1080_0`）而不吃 4K 带宽
- [ ] 界面内直接显示/切换"当前实际生效档位"
- [ ] 抖音更新后自动提示并引导重新启用
