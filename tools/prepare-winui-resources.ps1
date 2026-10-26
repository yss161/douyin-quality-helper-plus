# 为 WinUI 项目准备内嵌资源（payload.js 需要现场拼装）
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$winui = Join-Path $root 'winui'
$res = Join-Path $winui 'Resources'
New-Item -ItemType Directory -Force -Path $res | Out-Null

$embed  = Join-Path $root 'injector\userscript.bundle.embedded.js'
$loader = Join-Path $root 'injector\userscript-loader.js'
$qual   = Join-Path $root 'injector\payload.js'
$dst    = Join-Path $res 'payload.js'

if (-not (Test-Path $embed))  { throw '缺少 injector/userscript.bundle.embedded.js，先运行 node tools/build-userscript.js' }
if (-not (Test-Path $loader)) { throw '缺少 injector/userscript-loader.js' }

$nl = [char]10
$pieces = @()
$pieces += (Get-Content -Raw -Encoding UTF8 $embed).TrimEnd()
$pieces += (Get-Content -Raw -Encoding UTF8 $loader).TrimEnd()
$pieces += (Get-Content -Raw -Encoding UTF8 $qual)
$text = ($pieces -join $nl) + $nl

[System.IO.File]::WriteAllText($dst, $text, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ('payload.js: ' + (Get-Item $dst).Length + ' bytes')

Copy-Item (Join-Path $root 'versions.json') (Join-Path $res 'versions.json') -Force
Copy-Item (Join-Path $root 'injector\userscript.bundle.js') (Join-Path $res 'userscript.bundle.js') -Force
Write-Host ('userscript.bundle.js: ' + (Get-Item (Join-Path $res 'userscript.bundle.js')).Length + ' bytes')
Write-Host ''
Write-Host 'WinUI 资源已就绪，可以编译了。' -ForegroundColor Green
