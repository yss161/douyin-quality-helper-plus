# =============================================================================
# 下载"抖音优化"用户脚本依赖的 UMD 库
#
# 用户脚本原本通过 @require 从 CDN 加载这些库；客户端里没有油猴，
# 所以要先把它们落到 injector\vendor\ 里，再由 tools\build-userscript.js 打包。
#
# 版本必须与用户脚本头部 @require 里写的一致。
# =============================================================================
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$vendor = Join-Path $root 'injector\vendor'
New-Item -ItemType Directory -Force -Path $vendor | Out-Null

$base = 'https://fastly.jsdelivr.net'

$items = @(
  @{ name = 'utils.umd.js';     url = "$base/npm/@whitesev/utils@2.15.2/dist/index.umd.js" },
  @{ name = 'domutils.umd.js';  url = "$base/npm/@whitesev/domutils@2.0.12/dist/index.umd.js" },
  @{ name = 'pops.umd.js';      url = "$base/npm/@whitesev/pops@4.2.10/dist/index.umd.js" },
  @{ name = 'qmsg.umd.js';      url = "$base/npm/qmsg@1.7.3/dist/index.umd.js" }
)

foreach ($it in $items) {
  $dest = Join-Path $vendor $it.name
  Write-Host ('[*] ' + $it.name)
  try {
    Invoke-WebRequest -Uri $it.url -OutFile $dest -UseBasicParsing -TimeoutSec 120
    $len = (Get-Item $dest).Length
    if ($len -lt 1000) { throw "downloaded file too small: $len bytes" }
    Write-Host ('    ok ' + $len + ' bytes') -ForegroundColor Green
  } catch {
    Write-Host ('    [x] ' + $_.Exception.Message) -ForegroundColor Red
    throw
  }
}

Write-Host ''
Write-Host 'Vendor libraries are ready.' -ForegroundColor Green
Write-Host 'Now run: node tools\build-userscript.js'
