# Build the single-file WPF EXE with the system C# compiler (no NuGet).
# Output: dist\抖音画质助手.exe
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$src  = Join-Path $root 'exe-src'
$dist = Join-Path $root 'dist'
# ASCII output name avoids encoding issues with csc.exe; we rename after build.
$out  = Join-Path $dist 'douyin-quality-helper.exe'

New-Item -ItemType Directory -Force -Path $dist | Out-Null

# Refresh embedded resources from the authoritative sources.
#
# payload.js = userscript-loader + quality payload, concatenated in that order.
# The loader only reads %APPDATA%\douyin\userscript.bundle.js and stays silent
# when that file is missing, so an install without the bundle behaves as before.
$loaderFile = Join-Path $root 'injector\userscript-loader.js'
$payloadFile = Join-Path $root 'injector\payload.js'
$combinedFile = Join-Path $src 'Resources\payload.js'

$loaderText = if (Test-Path $loaderFile) { [System.IO.File]::ReadAllText($loaderFile) } else { '' }
$payloadText = [System.IO.File]::ReadAllText($payloadFile)
$combinedText = if ($loaderText) { $loaderText.TrimEnd() + "`n" + $payloadText } else { $payloadText }
[System.IO.File]::WriteAllText($combinedFile, $combinedText, (New-Object System.Text.UTF8Encoding($false)))

Copy-Item (Join-Path $root 'versions.json') (Join-Path $src 'Resources\versions.json') -Force

$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe not found: $csc" }

# Reference assemblies are installed by the .NET Framework targeting pack (any
# SDK/VS install). Plain Windows machines usually do not have them, but the same
# WPF assemblies ship inside the framework folder, which csc can reference just
# as well - fall back to that instead of failing the build.
$refCandidates = @(
  'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8',
  'C:\Program Files\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8',
  'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF',
  'C:\Windows\Microsoft.NET\Framework\v4.0.30319\WPF'
)
$refAsm = $null
foreach ($c in $refCandidates) {
  if ((Test-Path (Join-Path $c 'PresentationFramework.dll')) -and (Test-Path (Join-Path $c 'WindowsBase.dll'))) {
    $refAsm = $c
    break
  }
}
if (-not $refAsm) {
  throw ('WPF reference assemblies not found. Tried:' + [Environment]::NewLine + ($refCandidates -join [Environment]::NewLine))
}
Write-Host "References: $refAsm"

$refs = @(
  'System',
  'System.Core',
  'System.Xaml',
  'System.Xml',
  'System.Web.Extensions',
  'System.Windows.Forms',
  'PresentationCore',
  'PresentationFramework',
  'WindowsBase',
  'Microsoft.CSharp'
)

$files = Get-ChildItem -Path $src -Filter '*.cs' | ForEach-Object { $_.FullName }

$refArgs = @("/lib:`"$refAsm`"")
foreach ($r in $refs) { $refArgs += "/reference:$r.dll" }

$resourceArgs = @(
  ('/resource:{0},DouyinQualityHelper.Resources.payload.js' -f (Join-Path $src 'Resources\payload.js')),
  ('/resource:{0},DouyinQualityHelper.Resources.versions.json' -f (Join-Path $src 'Resources\versions.json')),
  ('/resource:{0},DouyinQualityHelper.Resources.MainWindow.xaml' -f (Join-Path $src 'MainWindow.xaml')),
  ('/resource:{0},DouyinQualityHelper.Resources.userscript.bundle.js' -f (Join-Path $root 'injector\userscript.bundle.js'))
)

$argList = @(
  '/nologo',
  '/target:winexe',
  '/platform:anycpu',
  '/optimize+',
  '/debug-',
  '/utf8output',
  ('/out:{0}' -f $out)
) + $refArgs + $resourceArgs + $files

Write-Host "Compiling $($files.Count) source files..."
& $csc @argList
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit code $LASTEXITCODE" }

$fi = Get-Item $out
Write-Host ("OK  {0}  ({1:N0} bytes)" -f $fi.FullName, $fi.Length)
