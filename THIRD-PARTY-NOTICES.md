# 第三方组件与引用声明 / Third-Party Notices

本项目在原项目的基础上扩展而来，并使用了若干第三方开源组件。
下面逐项列出**引用了什么、来自哪里、以什么方式使用、遵循什么许可**。

---

## 1. 原项目（本项目的基础）

**抖音 PC 版 —— 强制最高画质**

| 项 | 内容 |
|---|---|
| 仓库 | <https://github.com/GUAPIXIA/Douyin_Mandatory_maximum_graphics_quality> |
| 作者 | GUAPIXIA |
| 许可 | MIT（见其仓库 LICENSE） |
| 引用方式 | **派生开发**：画质强制方案、注入点定位、版本指纹机制、GUI 与 CLI 骨架均来自该项目，在其基础上继续开发 |

继承自该项目的部分：

- `injector/payload.js` —— 画质强制器（sessionStorage `MANUAL_SWITCH` 的写入与看护逻辑）
- `tools/patch-preload.js` —— preload 注入实现
- `douyin-quality.ps1`、`douyin-quality-gui.ps1`、`gui.xaml` —— 命令行与 PowerShell 图形界面
- `versions.json` —— 抖音版本兼容性清单的结构与 8.7.0 指纹
- `exe-src/` 的 WPF 单文件 EXE 骨架
- README / CHANGELOG 的项目结构与说明风格

在这些基础上**新增**的功能（见 [CHANGELOG.md](CHANGELOG.md)）：
结束抖音进程、抖音优化脚本集成、GM 兼容层、左侧栏设置入口、
注入内容三选一、上传脚本更新、preload 延迟注入修复。

按 MIT 许可要求，原项目的版权与许可声明予以保留。

---

## 2. 「抖音优化」用户脚本

**WhiteSevs/TamperMonkeyScript —— 抖音优化**

| 项 | 内容 |
|---|---|
| 仓库 | <https://github.com/WhiteSevs/TamperMonkeyScript> |
| 作者 | WhiteSevs |
| 脚本名 | 抖音优化（`@name`） |
| 使用的版本 | `2026.10.23` |
| 许可 | **GPL-3.0-only**（脚本头部 `@license`） |
| 引用方式 | **原样内嵌**：脚本主体未做任何修改，通过 GM 兼容层运行 |

文件位置：

- `injector/vendor/douyin-optimize.js` —— 从上游仓库取得、未经改动的脚本原文
- `injector/userscript.bundle.js` —— 由 `tools/build-userscript.js` 生成的打包产物
  （GM 兼容层 + 依赖库 + 上面那份脚本）

**重要：GPL-3.0**

该脚本以 GPL-3.0-only 授权。当它与本项目一起打包进 `userscript.bundle.js`
或内嵌进 `抖音画质助手.exe` 时，**该打包产物属于 GPL-3.0 的组合作品**，
分发时必须同时提供完整对应源码并保留 GPL-3.0 许可。

本项目仓库本身就是公开的，包含该组合所需的全部源码，满足这一要求。
如果你要再分发 `dist/` 下的可执行文件，请一并保留本文件与 LICENSE。

**不想承担 GPL 义务？** 界面「注入内容」选 **② 只装最高画质**，
或用 `tools/build-userscript.js --src none` 的方式构建不含脚本的版本，
此时产物只包含 MIT 许可的原创代码。

---

## 3. 用户脚本依赖的 UMD 库

「抖音优化」脚本原本通过 `@require` 从 CDN 加载下列库。客户端里没有油猴，
所以本项目把它们落到 `injector/vendor/` 并一起打包。**版本与脚本头部
`@require` 中标注的一致，代码未做修改。**

| 库 | 版本 | 许可 | 上游 |
|---|---|---|---|
| `@whitesev/utils` | 2.15.2 | MIT | <https://www.npmjs.com/package/@whitesev/utils> |
| `@whitesev/domutils` | 2.0.12 | MIT | <https://www.npmjs.com/package/@whitesev/domutils> |
| `@whitesev/pops` | 4.2.10 | MIT | <https://www.npmjs.com/package/@whitesev/pops> |
| `qmsg` | 1.7.3 | MIT | <https://www.npmjs.com/package/qmsg> |

对应文件：`injector/vendor/utils.umd.js`、`domutils.umd.js`、`pops.umd.js`、`qmsg.umd.js`

重新获取：`pwsh -File tools/fetch-vendor.ps1`

---

## 4. 运行环境依赖（不随项目分发）

| 组件 | 用途 | 说明 |
|---|---|---|
| 抖音 PC 客户端 | 目标程序 | 本项目**不包含**其任何文件；用户自行安装 |
| .NET Framework 4.8（WPF） | EXE 运行环境 | Windows 自带 / 系统更新提供 |
| Node.js | 构建脚本与注入文件生成 | 仅开发时需要，最终用户无需安装 |

---

## 5. 免责声明

- 本项目仅出于**互操作性**目的，修改用户自己电脑上安装的客户端中的一个文件
  （`resources\app.asar.unpacked\preload.js`），不涉及绕过付费、破解或内容盗取。
- 不包含、不转发任何来自抖音 / 字节跳动的版权文件或数据。
- 使用风险自负；随时可用界面「② 还原官方原版」恢复。

---

## 引用一览（简表）

| 项目 | 作者 | 许可 | 关系 |
|---|---|---|---|
| [Douyin_Mandatory_maximum_graphics_quality](https://github.com/GUAPIXIA/Douyin_Mandatory_maximum_graphics_quality) | GUAPIXIA | MIT | 原项目，本项目在其基础上派生开发 |
| [TamperMonkeyScript](https://github.com/WhiteSevs/TamperMonkeyScript)（抖音优化） | WhiteSevs | GPL-3.0-only | 原样内嵌并集成到客户端 |
| [@whitesev/utils](https://www.npmjs.com/package/@whitesev/utils) | WhiteSevs | MIT | 用户脚本依赖，原样打包 |
| [@whitesev/domutils](https://www.npmjs.com/package/@whitesev/domutils) | WhiteSevs | MIT | 用户脚本依赖，原样打包 |
| [@whitesev/pops](https://www.npmjs.com/package/@whitesev/pops) | WhiteSevs | MIT | 用户脚本依赖，原样打包 |
| [qmsg](https://www.npmjs.com/package/qmsg) | WhiteSevs | MIT | 用户脚本依赖，原样打包 |
