#Requires -Version 7.0
<#
.SYNOPSIS
    本地开发用：编译单文件 exe → 关掉正在跑的 → 覆盖已安装的那一份 → 重新启动。

.DESCRIPTION
    只替换 %LOCALAPPDATA%\Programs\MdViewer\MdViewer.exe 这一枚文件。
    注册表、开始菜单快捷方式、.md 文件关联都是 install.ps1 一次性装好的，这里一律不碰。

    覆盖前把旧的改名成 MdViewer.exe.old。新版起不来时把它改回 MdViewer.exe 就退回去了。

.PARAMETER Portable
    编 portable（自带运行时，约 61 MB，慢）。默认编 local（约 2 MB，依赖本机 .NET 10 桌面运行时）。

.PARAMETER File
    启动时打开哪个文件。不给就沿用被关掉的那个实例正在看的文档。

.PARAMETER NoStart
    只覆盖，不启动。

.EXAMPLE
    .\devrun.ps1
    .\devrun.ps1 -File .\samples\功能验收.md
    .\devrun.ps1 -Portable
    .\devrun.ps1 -NoStart
#>
[CmdletBinding()]
param(
    [switch]$Portable,
    [string]$File,
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'

$project    = Join-Path $PSScriptRoot 'MdViewer.csproj'
$InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\MdViewer'
$TargetExe  = Join-Path $InstallDir 'MdViewer.exe'
$BackupExe  = Join-Path $InstallDir 'MdViewer.exe.old'

function Step([string]$t) { Write-Host "  $t" -ForegroundColor Gray }
function Ok([string]$t)   { Write-Host "  [OK] $t" -ForegroundColor Green }
function Warn([string]$t) { Write-Host "  [!] $t" -ForegroundColor Yellow }

if (-not (Test-Path $project)) { throw "找不到项目文件：$project" }
if (-not (Test-Path $TargetExe)) {
    throw "本机还没装过：$TargetExe 不存在。先跑一次 .\install.ps1，之后就可以一直用这个脚本。"
}

# 版本号从 csproj 读，别手写两处
$version = '0.0.0'
$match = Select-String -Path $project -Pattern '<Version>([^<]+)</Version>'
if ($match) { $version = $match.Matches[0].Groups[1].Value }

$kind = if ($Portable) { 'portable · 自带运行时' } else { 'local · 依赖本机运行时' }
Write-Host "`nMdViewer 本地部署  v$version  ($kind)`n" -ForegroundColor Cyan

$oldVersion = (Get-Item $TargetExe).VersionInfo.FileVersion

# ── 1. 关掉正在跑的实例，顺手记下它在看哪个文件 ───────────────────────────
$doc = $File
$running = @(Get-CimInstance Win32_Process -Filter "Name='MdViewer.exe'" -ErrorAction SilentlyContinue)

if (-not $doc) {
    foreach ($proc in $running) {
        $cmd = $proc.CommandLine
        if ([string]::IsNullOrWhiteSpace($cmd)) { continue }
        # 命令行里找第一个 .md / .markdown / .mdown / .epub 路径
        $hit = [regex]::Match($cmd, '"([^"]+\.(?:md|markdown|mdown|epub))"', 'IgnoreCase')
        if (-not $hit.Success) {
            $hit = [regex]::Match($cmd, '([A-Za-z]:\\[^"]*?\.(?:md|markdown|mdown|epub))', 'IgnoreCase')
        }
        if ($hit.Success) { $doc = $hit.Groups[1].Value; break }
    }
}

if ($running.Count -gt 0) {
    Step "关掉 $($running.Count) 个正在运行的实例…"
    foreach ($proc in $running) {
        Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue
    }
    for ($i = 0; $i -lt 50; $i++) {
        if (-not (Get-Process -Name 'MdViewer' -ErrorAction SilentlyContinue)) { break }
        Start-Sleep -Milliseconds 100
    }
    if (Get-Process -Name 'MdViewer' -ErrorAction SilentlyContinue) {
        Warn "还有实例没退干净，等它自己结束…"
        Start-Sleep -Milliseconds 800
    }
}

# ── 2. 编译 ────────────────────────────────────────────────────────────
$stage = Join-Path $env:TEMP ("mdviewer-dev-" + (Get-Date -Format 'HHmmss'))
$pubArgs = @(
    'publish', $project, '-c', 'Release', '-r', 'win-x64',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=none',
    '-p:AllowedReferenceRelatedFileExtensions=.pdb',
    '-p:SatelliteResourceLanguages=zh-Hans',
    '--nologo', '-v', 'q',
    '-o', $stage
)
if ($Portable) {
    $pubArgs += '--self-contained', 'true'
    $pubArgs += '-p:EnableCompressionInSingleFile=true'
}
else {
    $pubArgs += '--self-contained', 'false'
}

Step "编译（$(if ($Portable) { 'portable，这一步慢一些' } else { 'local，十几秒' })）…"
$log = & dotnet @pubArgs 2>&1
if ($LASTEXITCODE -ne 0) {
    $log | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
    throw "编译失败（dotnet 退出码 $LASTEXITCODE）"
}
$built = Join-Path $stage 'MdViewer.exe'
if (-not (Test-Path $built)) { throw "编译完了但没找到 $built" }

# ── 3. 覆盖 ────────────────────────────────────────────────────────────
$free = $false
for ($i = 0; $i -lt 40; $i++) {
    try {
        $fs = [IO.File]::Open($TargetExe, 'Open', 'ReadWrite', 'None')
        $fs.Dispose()
        $free = $true
        break
    }
    catch { Start-Sleep -Milliseconds 200 }
}
if (-not $free) {
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
    throw "覆盖不了：$TargetExe 仍被占用。看看是不是还有 MdViewer 进程或杀毒软件正拿着它。"
}

Remove-Item $BackupExe -Force -ErrorAction SilentlyContinue
Move-Item $TargetExe $BackupExe -Force
Copy-Item $built $TargetExe -Force
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue

$newVersion = (Get-Item $TargetExe).VersionInfo.FileVersion
$sizeMb = [math]::Round((Get-Item $TargetExe).Length / 1MB, 1)
Ok "$TargetExe  ($sizeMb MB)  $oldVersion → $newVersion"
Step "旧版留在 $BackupExe，新版起不来就把它改回 MdViewer.exe"

# ── 4. 启动 ────────────────────────────────────────────────────────────
if (-not $NoStart) {
    if ($doc -and (Test-Path $doc)) {
        Step "打开 $doc"
        Start-Process -FilePath $TargetExe -ArgumentList "`"$doc`""
    }
    else {
        Start-Process -FilePath $TargetExe
    }
}

Write-Host "`n完成。`n" -ForegroundColor Cyan
