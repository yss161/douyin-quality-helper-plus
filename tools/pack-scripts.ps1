# =============================================================================
# 打包「纯 JS 脚本」发布物
#
# 这些脚本可以脱离 EXE 单独使用：直接在抖音页面的 DevTools 控制台粘贴，
# 或者配合别的注入方式使用。每个脚本都不依赖项目里的其它文件。
#
# 输出到 dist/scripts/ 并打成一个 zip，供 GitHub Release 附件使用。
# =============================================================================
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$inj  = Join-Path $root 'injector'
$out  = Join-Path $root 'dist\scripts'

New-Item -ItemType Directory -Force -Path $out | Out-Null
Get-ChildItem $out -File | Remove-Item -Force -ErrorAction SilentlyContinue

$ver = '2.0.0'
$verFile = Join-Path $root 'versions.json'
if (Test-Path $verFile) {
  try {
    $j = Get-Content $verFile -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($j.tool.version) { $ver = $j.tool.version }
  } catch { }
}

# 脚本清单：源文件 -> 发布名 -> 说明
#
# 注意：直接取主文件（live-gift-hide.js / live-share-clean.js），
# 不再用 *.standalone.js —— 那是手工维护的副本，很容易和主文件不同步
# （2.0.1 发布时就踩过这个坑：版本号更新了，脚本内容还是旧的）。
$items = @(
  @{ src = 'live-gift-hide.js';   name = '1-gift-hide.js';        title = '直播间：精简礼物栏';   desc = '隐藏礼物图标，只留「更多」与钻石' },
  @{ src = 'live-share-clean.js'; name = '2-share-link-clean.js'; title = '直播间：优化复制链接'; desc = '复制的链接去掉追踪参数' },
  @{ src = 'payload.js';          name = '3-quality-force.js';    title = '视频：强制最高画质';   desc = '写入 sessionStorage 的 MANUAL_SWITCH' },
  @{ src = 'dy-host.js';          name = '4-gm-host.js';          title = 'GM 兼容层';            desc = '让油猴脚本能在客户端里跑（进阶）' }
)

$copied = @()
foreach ($it in $items) {
  $src = Join-Path $inj $it.src
  if (-not (Test-Path $src)) { Write-Host ('[skip] ' + $it.src); continue }

  # 每个脚本前面加一段头部注释，说明用途与版本
  $body = [System.IO.File]::ReadAllText($src)
  $header = @"
/* ============================================================================
 * 抖音画质助手 · $($it.title)
 * 版本 $ver ｜ https://github.com/yss161/douyin-quality-helper-plus
 *
 * $($it.desc)
 *
 * 用法：抖音客户端里打开对应页面（直播页 / 视频页），在 DevTools 控制台
 *       粘贴本文件全部内容后回车即可。刷新页面后需要重新执行。
 * ==========================================================================*/

"@
  $dst = Join-Path $out $it.name
  [System.IO.File]::WriteAllText($dst, ($header + $body), (New-Object System.Text.UTF8Encoding($false)))
  $copied += $it
  Write-Host ('[ok] ' + $it.name + '  (' + (Get-Item $dst).Length + ' bytes)')
}

# 说明文件
$readme = @"
抖音画质助手 · 纯 JS 脚本
版本 $ver
项目：https://github.com/yss161/douyin-quality-helper-plus

这些脚本可以脱离 EXE 单独使用。

【怎么用】
  1. 先用 EXE 或别的办法让抖音页面能执行自定义 JS
     （或者用 DevTools 直接粘贴）
  2. 打开对应的页面：
       直播间  -> live.douyin.com/<房间号>
       视频页  -> www.douyin.com
  3. 按 F12 打开 DevTools，切到 Console
  4. 把对应 .js 文件的全部内容粘贴进去，回车
  5. 刷新页面后需要重新执行

【脚本清单】

  1-gift-hide.js          直播间：精简礼物栏
                          隐藏礼物图标，只留「更多」入口与钻石
                          临时恢复：window.__DY_LIVE_GIFT_HIDE__ = false;

  2-share-link-clean.js   直播间：优化复制链接
                          点「分享-复制链接」拿到的是干净链接
                          临时关闭：window.__DY_LIVE_SHARE_CLEAN__ = false;

  3-quality-force.js      视频：强制最高画质
                          写入 sessionStorage 的 MANUAL_SWITCH
                          这是 EXE 版「一键启用」在页面侧做的那部分

  4-gm-host.js            GM 兼容层（进阶）
                          提供 GM_* API + 左侧栏设置入口，
                          用来跑油猴脚本（如「抖音优化」）

【说明】
  - 这些脚本都不依赖项目里的其它文件，可单独使用
  - 全部用 try/catch 包裹，出错不会破坏页面
  - 4-gm-host.js 只是兼容层，还需要脚本本体才能用

【完整功能请用 EXE】
  双击「抖音画质助手.exe」，一键完成注入、还原、进程管理，
  并支持自动更新检测。
"@
[System.IO.File]::WriteAllText((Join-Path $out 'README.txt'), $readme, (New-Object System.Text.UTF8Encoding($false)))

# 打 zip
$zip = Join-Path $root ('dist\douyin-quality-helper-v' + $ver + '-scripts.zip')
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -Force

Write-Host ''
Write-Host ('打包完成：' + $zip)
Write-Host ('  脚本数：' + $copied.Count)
Write-Host ('  压缩包：' + (Get-Item $zip).Length + ' bytes')
