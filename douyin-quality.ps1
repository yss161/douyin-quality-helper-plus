<#
=============================================================================
 Douyin PC - Force Highest Video Quality  (one-stop tool)
=============================================================================

 ENGLISH-FIRST OUTPUT ON PURPOSE:
   Chinese text in a .ps1 is garbled by Windows PowerShell 5.1 (it reads .ps1 as
   ANSI unless the file has a UTF-8 BOM) and can cause a parse error. This
   script therefore prints ASCII only, so it runs on both 5.1 and 7+.
   The Chinese documentation lives in D:\Code\dy\README.md.

 WHAT IT DOES
   Douyin PC is an Electron shell wrapping the www.douyin.com web player.
   The clarity choice is kept in sessionStorage key "MANUAL_SWITCH" and is lost
   on every restart, so it must be re-forced on each page load.

   The ONLY file this tool touches is:
     <versionDir>\resources\app.asar.unpacked\preload.js
   (the preload that actually attaches to www.douyin.com, running with
   isolated=false, i.e. in the page's own JS context).

   resources\app.asar is verified against md5.json and left untouched.

 USAGE
   Interactive menu:      pwsh -File D:\Code\dy\douyin-quality.ps1
   Non-interactive:       pwsh -File D:\Code\dy\douyin-quality.ps1 -Action install
                          pwsh -File D:\Code\dy\douyin-quality.ps1 -Action status
   Actions: status | install | restore | check | test | version | kill

   -Force   (install / restore / kill) end a running Douyin automatically
            instead of refusing; plain install / restore offer the same thing.
=============================================================================
#>
[CmdletBinding()]
param(
  [ValidateSet('status', 'install', 'restore', 'check', 'test', 'version', 'kill')]
  [string]$Action,

  [string]$DouyinDir = 'D:\douyin',
  [string]$ProjectDir = 'D:\Code\dy',
  [switch]$Force
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Hashing via .NET only: Windows PowerShell 5.1 (which a .bat launcher starts)
# does not ship Get-FileHash, while PowerShell 7 does. Do not depend on it.
# ---------------------------------------------------------------------------
function Get-HashHex([string]$path, [string]$algo) {
  $fs = $null
  try {
    $fs = [System.IO.File]::OpenRead($path)
    $h = [System.Security.Cryptography.HashAlgorithm]::Create($algo)
    return [System.BitConverter]::ToString($h.ComputeHash($fs)).Replace('-', '').ToLower()
  } finally {
    if ($fs) { $fs.Dispose() }
  }
}
function Get-Md5([string]$p) {
  if (-not (Test-Path $p)) { return '' }
  return (Get-HashHex $p 'MD5')
}
function Get-Sha([string]$p) {
  if (-not (Test-Path $p)) { return '' }
  return (Get-HashHex $p 'SHA256').ToUpper()
}

# ---------------------------------------------------------------------------
# Official hash of the pristine preload, taken from <install>\md5.json
# ---------------------------------------------------------------------------
$ORIG_PRELOAD_MD5 = 'bfcb8e31fae5a7370c5bee258ffcdfad'
$MARK_BEGIN = '/* ==== DY_FORCE_INJECTOR_BEGIN ==== */'
$MARK_END = '/* ==== DY_FORCE_INJECTOR_END ==== */'

function Say    ($m) { Write-Host $m }
function Title  ($m) { Write-Host ''; Write-Host ('=' * 66) -ForegroundColor DarkCyan; Write-Host "  $m" -ForegroundColor Cyan; Write-Host ('=' * 66) -ForegroundColor DarkCyan }
function Step   ($m) { Write-Host "[*] $m" -ForegroundColor Cyan }
function Ok     ($m) { Write-Host "[+] $m" -ForegroundColor Green }
function Warn   ($m) { Write-Host "[!] $m" -ForegroundColor Yellow }
function Bad    ($m) { Write-Host "[x] $m" -ForegroundColor Red }
function Dim    ($m) { Write-Host "    $m" -ForegroundColor DarkGray }

function Md5([string]$p) {
  if (-not (Test-Path $p)) { return '' }
  return (Get-HashHex $p 'MD5')
}
function Sha256([string]$p) {
  if (-not (Test-Path $p)) { return '' }
  return (Get-HashHex $p 'SHA256').ToUpper()
}

# ---------------------------------------------------------------------------
# Locate install paths
# ---------------------------------------------------------------------------
function Get-Paths {
  $cfgFile = Join-Path $DouyinDir 'launcher_config.json'
  if (-not (Test-Path $cfgFile)) { throw "launcher_config.json not found: $cfgFile" }
  $cfg = Get-Content $cfgFile -Raw | ConvertFrom-Json
  $ver = $cfg.cur_path
  if (-not $ver) { throw "cur_path missing in $cfgFile" }

  $versionDir = Join-Path $DouyinDir $ver
  [pscustomobject]@{
    DouyinDir   = $DouyinDir
    VersionDir  = $versionDir
    Asar        = Join-Path $versionDir 'resources\app.asar'
    Preload     = Join-Path $versionDir 'resources\app.asar.unpacked\preload.js'
    Md5Json     = Join-Path $versionDir 'md5.json'
    LauncherCfg = $cfgFile
  }
}

function Get-ProjectPaths {
  [pscustomobject]@{
    Payload     = Join-Path $ProjectDir 'injector\payload.js'
    Patcher     = Join-Path $ProjectDir 'tools\patch-preload.js'
    BackupDir   = Join-Path $ProjectDir 'backup\loose-preload'
    OrigPreload = Join-Path $ProjectDir 'backup\loose-preload\preload.js.orig'
    AsarOrig    = Join-Path $ProjectDir 'backup\app.asar.orig'
    BuildDir    = Join-Path $ProjectDir 'work\build'
    Tester      = Join-Path $ProjectDir 'tools\test-payload.js'
    UserData    = Join-Path $env:APPDATA 'douyin'
    PageLog     = Join-Path $env:APPDATA 'douyin\dy-force.log'
  }
}

# ---------------------------------------------------------------------------
# Version compatibility (shared implementation, same file the GUI uses)
# ---------------------------------------------------------------------------
$script:VersionInfo = $null
$versionModule = Join-Path $ProjectDir 'tools\douyin-version.ps1'
if (Test-Path $versionModule) {
  try { . $versionModule } catch { Write-Warn2 ('version module failed: ' + $_.Exception.Message) }
} else {
  Write-Warn2 'tools\douyin-version.ps1 not found; version checks disabled.'
}

function Update-VersionInfo([string]$root) {
  $script:VersionInfo = $null
  if (-not $root) { return }
  if (-not (Get-Command Get-DouyinVersionInfo -ErrorAction SilentlyContinue)) { return }
  try { $script:VersionInfo = Get-DouyinVersionInfo -InstallRoot $root -ProjectDir $ProjectDir } catch { }
}

function Show-Kill {
  Title 'END DOUYIN PROCESSES'
  $procs = @(Get-DouyinProcesses $DouyinDir)
  if ($procs.Count -eq 0) {
    Ok 'no Douyin process is running'
    return $true
  }
  Step 'Running Douyin processes'
  $procs | ForEach-Object { Dim ($_.Name + '.exe   pid=' + $_.Id + '   ' + $_.Path) }
  Write-Host ''
  return (Stop-DouyinProcesses $DouyinDir)
}

function Show-Version {
  Title 'DOUYIN VERSION COMPATIBILITY'
  $p = Get-Paths
  Update-VersionInfo $p.DouyinDir
  $v = $script:VersionInfo

  if (-not $v) { Bad 'Could not read version information.'; return }
  if (-not (Get-Command Get-DouyinVersionInfo -ErrorAction SilentlyContinue)) {
    Bad 'tools\douyin-version.ps1 is missing.'
    return
  }

  Dim ('tool version    : ' + $v.ToolVersion)
  Dim ('douyin version  : ' + $v.Version)
  Dim ('status          : ' + $v.Status)
  Dim ('patch applied   : ' + $v.IsPatched)
  Dim ('pristine md5    : ' + $v.PristineMd5)
  if ($v.Official) { Dim ('md5.json entries: ' + $v.Official.entries) }
  Write-Host ''

  switch ($v.Status) {
    'verified'    { Ok  ($v.Label); Dim $v.Detail }
    'compatible'  { Warn ($v.Label); Dim $v.Detail }
    'unknown'     { Warn ($v.Label); Dim $v.Detail }
    'unsupported' { Bad ($v.Label); Dim $v.Detail }
    default       { Bad ($v.Label); Dim $v.Detail }
  }
  foreach ($w in $v.Warnings) { Warn $w }

  Write-Host ''
  Step 'Tested versions recorded in versions.json'
  $mf = Get-VersionManifest $ProjectDir
  if ($mf) {
    $mf.versions | ForEach-Object {
      Write-Host ('  ' + $_.version.PadRight(10) + $_.status.PadRight(14) + 'tested ' + $_.testedAt) -ForegroundColor Gray
    }
  }
  Write-Host ''
  if ($v.Status -eq 'unknown') {
    Dim 'If the patch works on this version, record it with:'
    Dim ('  pwsh -File ' + $ProjectDir + '\tools\record-version.ps1')
  }
}

# ---------------------------------------------------------------------------
# Douyin processes
#
# Douyin keeps its install files open while it runs: the patch cannot be written
# and an already-running process would overwrite it, so the main process, tray,
# guard and crash-reporter processes all have to go first.
#
# Process names unique to Douyin (douyin / douyin_guard / douyin_tray /
# douyin_widget) are matched by name. Generic ByteDance helper names
# (parfait_crash_handler, tt_crash_reporter, ...) are matched only when the exe
# really lives under the install folder, so an unrelated program with the same
# name is never touched.
# ---------------------------------------------------------------------------
$script:SpecificProcNames = @('douyin', 'douyin_guard', 'douyin_tray', 'douyin_widget')
$script:HelperProcNames   = @('parfait_crash_handler', 'tt_crash_reporter', 'push_detect', 'douyin_doctor', 'dump_reporter')

function ConvertTo-StdPath([string]$p) {
  if (-not $p) { return '' }
  return (($p -replace '/', '\').TrimEnd('\'))
}

function Test-ProcUnderRoot([string]$exePath, [string]$root) {
  if (-not $exePath -or -not $root) { return $false }
  $r = (ConvertTo-StdPath $root)
  $f = (ConvertTo-StdPath $exePath)
  if ($f.Length -le $r.Length + 1) { return $false }
  if (-not $f.StartsWith($r, [System.StringComparison]::OrdinalIgnoreCase)) { return $false }
  return ($f[$r.Length] -eq '\')
}

function Get-DouyinProcesses([string]$root) {
  $out = New-Object System.Collections.Generic.List[object]
  $hasRoot = -not [string]::IsNullOrWhiteSpace($root)
  $rootNorm = if ($hasRoot) { ConvertTo-StdPath $root } else { '' }

  foreach ($p in (Get-Process -ErrorAction SilentlyContinue)) {
    $name = $null
    try { $name = $p.ProcessName } catch { continue }
    if (-not $name) { continue }
    $ln = $name.ToLowerInvariant()
    $specific = ($script:SpecificProcNames -contains $ln)
    $helper = ($script:HelperProcNames -contains $ln)
    if (-not $specific -and -not $helper) { continue }

    $exe = $null
    try { $exe = $p.MainModule.FileName } catch { $exe = $null }
    if ($exe -and $hasRoot -and -not (Test-ProcUnderRoot $exe $rootNorm)) { continue }
    if (-not $exe -and -not $specific) { continue }

    $out.Add([pscustomobject]@{ Id = $p.Id; Name = $p.ProcessName; Path = $exe })
  }
  return @($out | Sort-Object Name, Id)
}

function Get-DouyinProcSummary($list) {
  return (@($list | ForEach-Object { $_.Name + '.exe(' + $_.Id + ')' }) -join ', ')
}

# Ends every Douyin process: ask it to close nicely first, force-kill what is
# left, then re-enumerate - the re-enumeration is the real answer, not Kill().
function Stop-DouyinProcesses([string]$root) {
  $found = @(Get-DouyinProcesses $root)
  if ($found.Count -eq 0) { Ok 'no Douyin process is running'; return $true }

  Step 'Ending Douyin processes'
  Dim ('found: ' + (Get-DouyinProcSummary $found))

  foreach ($d in $found) {
    try {
      $p = Get-Process -Id $d.Id -ErrorAction Stop
      if ($p.MainWindowHandle -ne 0) { [void]$p.CloseMainWindow() }
    } catch { }
  }
  $deadline = (Get-Date).AddMilliseconds(2500)
  while ((Get-Date) -lt $deadline) {
    if (@(Get-DouyinProcesses $root).Count -eq 0) { break }
    Start-Sleep -Milliseconds 120
  }

  foreach ($d in @(Get-DouyinProcesses $root)) {
    try { (Get-Process -Id $d.Id -ErrorAction Stop).Kill() }
    catch { Warn ('cannot end ' + $d.Name + '.exe(' + $d.Id + '): ' + $_.Exception.Message) }
  }
  $deadline = (Get-Date).AddMilliseconds(5000)
  while ((Get-Date) -lt $deadline) {
    if (@(Get-DouyinProcesses $root).Count -eq 0) { break }
    Start-Sleep -Milliseconds 150
  }

  $left = @(Get-DouyinProcesses $root)
  if ($left.Count -eq 0) { Ok 'all Douyin processes are gone'; return $true }

  Bad ('still running: ' + (Get-DouyinProcSummary $left))
  Dim 'If Douyin was started as administrator, run this tool as administrator too.'
  return $false
}

# -Force now means "end them for me" instead of "ignore them".
function Assert-DouyinStopped([switch]$ForceIt) {
  $procs = @(Get-DouyinProcesses $DouyinDir)
  if ($procs.Count -eq 0) { return $true }

  Warn ('Douyin is still running: ' + (Get-DouyinProcSummary $procs))
  if (-not $ForceIt) {
    Dim 'Quit it from the tray icon first, or run this with -Force to end it automatically:'
    Dim '  (or: -Action kill)'
    return $false
  }
  Dim '(-Force) ending them now...'
  return (Stop-DouyinProcesses $DouyinDir)
}

# ---------------------------------------------------------------------------
# status
# ---------------------------------------------------------------------------
function Show-Status {
  Title 'STATUS'

  $p = Get-Paths
  $pp = Get-ProjectPaths

  Step 'Paths'
  Dim "version dir : $($p.VersionDir)"
  Dim "preload     : $($p.Preload)"
  Dim "app.asar    : $($p.Asar)"

  Step 'File integrity'
  $preMd5 = Md5 $p.Preload
  $installed = ($preMd5 -ne $ORIG_PRELOAD_MD5) -and ($preMd5 -ne '')

  Dim "preload md5 : $preMd5"
  Dim "official md5: $ORIG_PRELOAD_MD5"
  if ($preMd5 -eq $ORIG_PRELOAD_MD5) { Ok  'preload is the ORIGINAL (patch not installed)' }
  elseif ($installed)                 { Ok  'preload is PATCHED (tool installed)' }
  else                                { Bad 'preload not found' }

  # app.asar must match md5.json
  if (Test-Path $p.Md5Json) {
    $manifest = Get-Content $p.Md5Json -Raw | ConvertFrom-Json
    $exp = $manifest.files.'resources\app.asar'
    $act = Md5 $p.Asar
    if ($exp -and $act -eq $exp) { Ok  'app.asar matches md5.json (untouched)' }
    elseif ($exp)                { Warn "app.asar differs from md5.json (expected $exp, actual $act)" }
  }

  Step 'Backups'
  $origMd5 = Md5 $pp.OrigPreload
  if ($origMd5 -eq $ORIG_PRELOAD_MD5) { Ok  "pristine preload backup OK: $($pp.OrigPreload)" }
  elseif ($origMd5 -ne '')            { Warn "pristine preload backup has unexpected md5: $origMd5" }
  else                                { Warn 'no pristine preload backup yet (install will create it)' }

  if (Test-Path $pp.AsarOrig) { Ok "app.asar backup: $($pp.AsarOrig)" }

  Step 'Runtime log'
  if (Test-Path $pp.PageLog) {
    $lines = Get-Content $pp.PageLog
    Dim "log: $($pp.PageLog)"
    Dim ("lines: {0}  last write: {1}" -f $lines.Count, (Get-Item $pp.PageLog).LastWriteTime)
    $hit = $lines | Where-Object { $_ -match 'loose-preload .*url=https://www\.douyin\.com' }
    if ($hit) { Ok 'injection reached www.douyin.com' } else { Dim 'no www.douyin.com injection record yet' }
    $fc = ($lines | Where-Object { $_ -match '\[forced\]' }).Count
    $cr = ($lines | Where-Object { $_ -match '\[created\]' }).Count
    Dim ("created: {0}   forced: {1}" -f $cr, $fc)
  } else {
    Dim 'no runtime log yet (start Douyin and open some videos)'
  }
  Write-Host ''
}

# ---------------------------------------------------------------------------
# test (payload unit tests)
# ---------------------------------------------------------------------------
function Invoke-Test {
  Title 'PAYLOAD UNIT TEST'
  $pp = Get-ProjectPaths
  if (-not (Test-Path $pp.Tester)) { Bad "tester not found: $($pp.Tester)"; return $false }
  & node $pp.Tester
  $ok = ($LASTEXITCODE -eq 0)
  if ($ok) { Ok 'all unit tests passed' } else { Bad 'unit tests failed' }
  return $ok
}

# ---------------------------------------------------------------------------
# install
# ---------------------------------------------------------------------------
function Invoke-Install {
  Title 'INSTALL'
  $p = Get-Paths
  $pp = Get-ProjectPaths

  if (-not (Assert-DouyinStopped -ForceIt:$Force)) { return $false }

  if (-not (Test-Path $pp.Payload)) { Bad "payload not found: $($pp.Payload)"; return $false }
  if (-not (Test-Path $pp.Patcher)) { Bad "patcher not found: $($pp.Patcher)"; return $false }

  # userscript bundle (optional): install next to the runtime log so it can be
  # replaced later without touching the protected install folder again
  $bundle = Join-Path $ProjectDir 'injector\userscript.bundle.js'
  if (Test-Path $bundle) {
    Step 'Installing the Douyin optimization script'
    try {
      $userData = Join-Path $env:APPDATA 'douyin'
      New-Item -ItemType Directory -Force -Path $userData | Out-Null
      $dest = Join-Path $userData 'userscript.bundle.js'
      Copy-Item $bundle $dest -Force
      Ok ('bundle installed: ' + $dest)
    } catch { Warn ('bundle install failed: ' + $_.Exception.Message) }
  } else {
    Dim 'no userscript bundle built (run: node tools\build-userscript.js), skipping'
  }

  # --- 1) make sure we have a pristine original to inject into ---
  Step 'Locating pristine original preload'
  New-Item -ItemType Directory -Force -Path $pp.BackupDir | Out-Null
  $pristine = $pp.OrigPreload
  $havePristine = ((Md5 $pristine) -eq $ORIG_PRELOAD_MD5)

  if ($havePristine) {
    Ok "pristine backup OK: $pristine"
  } else {
    $cur = Md5 $p.Preload
    if ($cur -eq $ORIG_PRELOAD_MD5) {
      Copy-Item $p.Preload $pristine -Force
      $havePristine = $true
      Ok "froze pristine backup: $pristine"
    } else {
      # Currently patched but no backup: strip our own injection region to recover it
      Warn 'preload looks patched and no pristine backup exists; trying to strip the injected block'
      $txt = Get-Content $p.Preload -Raw
      $i = $txt.IndexOf($MARK_BEGIN)
      $j = $txt.IndexOf($MARK_END)
      if ($i -ge 0 -and $j -gt $i) {
        # Symmetric strip: the injected block owns the newlines around it,
        # so they must be removed too for a byte-exact restore.
        $end = $j + $MARK_END.Length
        if ($i -gt 0 -and $txt[$i - 1] -eq [char]10) { $i = $i - 1 }
        if ($end -lt $txt.Length -and $txt[$end] -eq [char]13) { $end++ }
        if ($end -lt $txt.Length -and $txt[$end] -eq [char]10) { $end++ }
        $stripped = $txt.Substring(0, $i) + $txt.Substring($end)
        New-Item -ItemType Directory -Force -Path $pp.BuildDir | Out-Null
        $tmp = Join-Path $pp.BuildDir 'recovered-orig.js'
        # BOM-less UTF-8 is mandatory here: a BOM would change the md5
        [System.IO.File]::WriteAllText($tmp, $stripped, (New-Object System.Text.UTF8Encoding($false)))
        if ((Md5 $tmp) -eq $ORIG_PRELOAD_MD5) {
          Copy-Item $tmp $pristine -Force
          $havePristine = $true
          Ok 'recovered pristine original by stripping the injected block'
        } else {
          Bad 'could not reconstruct the original file exactly'
        }
      }
    }
  }

  if (-not $havePristine) {
    Bad 'No pristine original available - refusing to install.'
    Dim 'Reinstall the Douyin client (or restore the original preload.js) and run this again.'
    return $false
  }

  # --- 2) build the patched preload ---
  Step 'Building patched preload'
  New-Item -ItemType Directory -Force -Path $pp.BuildDir | Out-Null
  $built = Join-Path $pp.BuildDir 'loose-preload.patched.js'

  & node --check $pp.Payload
  if ($LASTEXITCODE -ne 0) { Bad 'payload.js has a syntax error'; return $false }
  Ok 'payload.js syntax OK'

  & node $pp.Patcher --src $pristine --payload $pp.Payload --out $built
  if ($LASTEXITCODE -ne 0) { Bad 'patching failed'; return $false }

  & node --check $built
  if ($LASTEXITCODE -ne 0) { Bad 'patched file has a syntax error'; return $false }
  Ok ("patched file built: {0:N0} bytes (syntax OK)" -f (Get-Item $built).Length)

  # --- 3) deploy ---
  Step 'Deploying'
  $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
  $snap = Join-Path $ProjectDir "backup\$stamp"
  New-Item -ItemType Directory -Force -Path $snap | Out-Null
  Copy-Item $p.Preload (Join-Path $snap 'preload.js.before') -Force

  Copy-Item $built $p.Preload -Force
  $newMd5 = Md5 $p.Preload
  Ok "written: $($p.Preload)"
  Dim "new md5: $newMd5"

  # clear old logs so the next run is easy to read
  foreach ($f in @($pp.PageLog, (Join-Path $pp.UserData 'dy-force-main.log'))) {
    if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
  }
  Ok 'old logs cleared'

  # --- 4) make sure app.asar is the official one ---
  Step 'Checking app.asar'
  if (Test-Path $pp.AsarOrig) {
    if ((Sha256 $p.Asar) -ne (Sha256 $pp.AsarOrig)) {
      Copy-Item $pp.AsarOrig $p.Asar -Force
      Ok 'app.asar restored to the official version'
    } else { Ok 'app.asar already official' }
  } else { Dim 'no app.asar backup, skipping' }

  Write-Host ''
  Ok 'INSTALL DONE'
  Dim 'Start Douyin, open a few videos, then run:  -Action check'
  return $true
}

# ---------------------------------------------------------------------------
# restore
# ---------------------------------------------------------------------------
function Invoke-Restore {
  Title 'RESTORE'
  $p = Get-Paths
  $pp = Get-ProjectPaths

  if (-not (Assert-DouyinStopped -ForceIt:$Force)) { return $false }

  if (-not (Test-Path $pp.OrigPreload)) {
    Bad "pristine backup not found: $($pp.OrigPreload)"
    Dim 'Cannot restore without it. Reinstall the Douyin client to get the official file.'
    return $false
  }
  if ((Md5 $pp.OrigPreload) -ne $ORIG_PRELOAD_MD5) {
    Bad 'pristine backup hash mismatch - refusing to use it'
    return $false
  }
  Ok 'pristine backup verified'

  Step 'Restoring preload.js'
  if ((Md5 $p.Preload) -eq $ORIG_PRELOAD_MD5) {
    Ok 'already the original, nothing to do'
  } else {
    Copy-Item $pp.OrigPreload $p.Preload -Force
    if ((Md5 $p.Preload) -ne $ORIG_PRELOAD_MD5) { Bad 'restore verification failed'; return $false }
    Ok 'preload.js restored to the official version'
  }

  Step 'Restoring app.asar (in case an older version patched it)'
  if (Test-Path $pp.AsarOrig) {
    if ((Sha256 $p.Asar) -eq (Sha256 $pp.AsarOrig)) { Ok 'app.asar already official' }
    else {
      Copy-Item $pp.AsarOrig $p.Asar -Force
      Ok 'app.asar restored to the official version'
    }
  } else { Dim 'no app.asar backup, skipping' }

  Step 'Cleaning up logs'
  foreach ($n in @('dy-force.log', 'dy-force-main.log', 'userscript.bundle.js')) {
    $f = Join-Path $pp.UserData $n
    if (Test-Path $f) { Remove-Item $f -Force; Ok "removed $n" }
  }

  Write-Host ''
  Ok 'RESTORE DONE - Douyin is back to its official state.'
  return $true
}

# ---------------------------------------------------------------------------
# check
# ---------------------------------------------------------------------------
function Invoke-Check {
  Title 'CHECK (is it working?)'
  $pp = Get-ProjectPaths

  if (-not (Test-Path $pp.PageLog)) {
    Bad "no runtime log: $($pp.PageLog)"
    Dim 'Either the patch is not installed, or Douyin has not been started yet.'
    return $false
  }

  $lines = Get-Content $pp.PageLog
  Dim "log: $($pp.PageLog)   lines: $($lines.Count)"

  Step '1. preload attached to the page?'
  $onPage = $lines | Where-Object { $_ -match 'loose-preload .*url=https://www\.douyin\.com' }
  if ($onPage) {
    $onPage | Select-Object -Last 1 | ForEach-Object { Ok $_ }
  } else {
    Bad 'no record of the preload loading on www.douyin.com'
    return $false
  }

  Step '2. was MANUAL_SWITCH forced?'
  $cr = ($lines | Where-Object { $_ -match '\[created\]' }).Count
  $fc = ($lines | Where-Object { $_ -match '\[forced\]' }).Count
  $sk = ($lines | Where-Object { $_ -match '\[skip\]' }).Count
  Dim "created: $cr   forced: $fc   skipped(no 4K): $sk"
  if ($cr -gt 0 -or $fc -gt 0) { Ok 'MANUAL_SWITCH is being created/raised' }
  else { Warn 'no create/force record yet - open a video in Douyin' }

  Step '3. errors'
  $errs = $lines | Where-Object { $_ -match 'SecurityError|exception|failed|Error' }
  if ($errs) { $errs | Select-Object -Last 5 | ForEach-Object { Warn $_ } }
  else { Ok 'none' }

  Step '4. tail'
  $lines | Select-Object -Last 12 | ForEach-Object { Dim $_ }

  Write-Host ''
  if ($onPage -and ($cr -gt 0 -or $fc -gt 0)) { Ok 'VERDICT: injection is working.'; return $true }
  Ok 'VERDICT: partial - see details above.'
  return $false
}

# ---------------------------------------------------------------------------
# menu
# ---------------------------------------------------------------------------
function Show-Menu {
  Title 'Douyin PC - Force Highest Video Quality'
  Say '  1) Install / update patch'
  Say '  2) Restore to official state'
  Say '  3) Check whether it works (read log)'
  Say '  4) Show status'
  Say '  5) Run payload unit tests'
  Say '  6) End Douyin processes (needed before install / restore)'
  Say '  0) Exit'
  Write-Host ''
}

function Invoke-Action([string]$name) {
  switch ($name) {
    'status'  { Show-Status | Out-Null }
    'install' { Invoke-Install | Out-Null }
    'restore' { Invoke-Restore | Out-Null }
    'check'   { Invoke-Check   | Out-Null }
    'test'    { Invoke-Test    | Out-Null }
    'version' { Show-Version   | Out-Null }
    'kill'    { Show-Kill      | Out-Null }
    default   { Bad "unknown action: $name" }
  }
}

# ---------------------------------------------------------------------------
# entry
# ---------------------------------------------------------------------------
try {
  if ($Action) {
    Invoke-Action $Action
    exit 0
  }

  while ($true) {
    Show-Menu
    $choice = Read-Host '  Select'
    switch ($choice.Trim()) {
      '1' { Invoke-Install | Out-Null }
      '2' { Invoke-Restore | Out-Null }
      '3' { Invoke-Check   | Out-Null }
      '4' { Show-Status    | Out-Null }
      '5' { Invoke-Test    | Out-Null }
      '6' { Show-Kill      | Out-Null }
      '0' { Say ''; Say '  Bye.'; exit 0 }
      ''  { }
      default { Warn "invalid choice: $choice" }
    }
    Write-Host ''
    Read-Host '  Press Enter to return to the menu' | Out-Null
  }
} catch {
  Bad $_.Exception.Message
  exit 1
}
