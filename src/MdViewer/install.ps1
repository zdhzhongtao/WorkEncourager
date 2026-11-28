<#
.SYNOPSIS
    把 Markdown 阅读器（MdViewer）装到当前用户下。

.DESCRIPTION
    全程只写 HKCU（不需要管理员），不动系统里任何别人的关联：
      · 程序本体装到 %LOCALAPPDATA%\Programs\MdViewer
      · 建开始菜单快捷方式（桌面快捷方式可关）
      · 注册 .md / .markdown / .mdown 的默认打开方式
      · 目标扩展名已经被别的程序占着时，只加「用 MdViewer 打开」右键菜单项，不硬抢

.PARAMETER ExePath
    直接用现成的 exe，跳过 publish。不给就现场编译一个单文件。

.PARAMETER Mode
    编译哪种版本：
      auto（默认）  本机装了 .NET 10 桌面运行时就用 local，否则用 portable
      local         2 MB 左右，依赖本机的 .NET 10 桌面运行时
      portable      62 MB 左右，自带运行时，拷到任何 Win10/11 都能跑

.PARAMETER NoDesktop
    不建桌面快捷方式。

.PARAMETER Uninstall
    卸载：删快捷方式、删本程序的注册表项、删安装目录。不碰别的程序。
    安装时如果顶掉过别人的默认关联，这里会照着备份还原回去。

.PARAMETER KeepExisting
    不抢已有的默认打开方式，只在右键菜单里加一项。

.EXAMPLE
    .\install.ps1
    .\install.ps1 -Mode portable
    .\install.ps1 -NoDesktop
    .\install.ps1 -KeepExisting
    .\install.ps1 -ExePath 'C:\temp\MdViewer.exe'
    .\install.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [string]$ExePath,
    [ValidateSet('auto', 'local', 'portable')]
    [string]$Mode = 'auto',
    [switch]$NoDesktop,
    [switch]$Uninstall,
    [switch]$KeepExisting
)

$ErrorActionPreference = 'Stop'

# ───────────────────────── 常量 ─────────────────────────

$AppName      = 'MdViewer'
$AppTitle     = 'Markdown 阅读器'
$ProgId       = 'MdViewer.md'
$Extensions   = @('.md', '.markdown', '.mdown')
$ContentType  = 'text/markdown'
$InstallDir   = Join-Path $env:LOCALAPPDATA "Programs\$AppName"
$TargetExe    = Join-Path $InstallDir "$AppName.exe"
$StartMenuDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$DesktopDir   = [Environment]::GetFolderPath('Desktop')
# 顶掉别人默认关联之前，先把原值记下来，卸载时还原（放用户数据目录，卸载不删）
$BackupPath   = Join-Path $env:APPDATA "$AppName\assoc-backup.json"

function Get-DefaultValue {
    # 注册表项的默认值用 Get-ItemProperty -Name '(default)' 取不稳（脚本里会拿到空），
    # 直接用 .NET 的 GetValue('') 才是可靠写法
    param([string]$KeyPath)
    if (-not (Test-Path $KeyPath)) { return '' }
    $item = Get-Item -Path $KeyPath -ErrorAction SilentlyContinue
    if (-not $item) { return '' }
    $value = $item.GetValue('')
    if ($null -eq $value) { return '' }
    return [string]$value
}

function Write-Step([string]$text) { Write-Host "  $text" -ForegroundColor Gray }
function Write-Ok([string]$text)   { Write-Host "  [OK] $text" -ForegroundColor Green }
function Write-Warn2([string]$text){ Write-Host "  [!] $text" -ForegroundColor Yellow }

# ───────────────────────── 快捷方式 ─────────────────────────

function New-Shortcut {
    param([string]$Path, [string]$Target)

    # 交给程序自己建（PowerShell 的 WScript.Shell 在 UTF-8 全局代码页机器上会把中文名写坏）。
    # 注意：MdViewer 是 GUI 程序，PowerShell 调用它不会阻塞，所以要轮询等文件真的落盘。
    $null = & $Target --make-shortcut $Path

    $deadline = (Get-Date).AddSeconds(25)
    while (-not (Test-Path $Path) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 150 }

    if (-not (Test-Path $Path)) { throw "程序没有生成快捷方式：$Path" }
}

# ───────────────────────── 卸载 ─────────────────────────

if ($Uninstall) {
    Write-Host "`n卸载 $AppTitle`n" -ForegroundColor Cyan

    foreach ($lnk in @(
        (Join-Path $StartMenuDir "$AppTitle.lnk"),
        (Join-Path $DesktopDir   "$AppTitle.lnk")
    )) {
        if (Test-Path $lnk) { Remove-Item $lnk -Force; Write-Step "已删除快捷方式 $lnk" }
    }

    # 只删自己的 ProgId，扩展名键只清自己写进去的默认值
    if (Test-Path "HKCU:\Software\Classes\$ProgId") {
        Remove-Item "HKCU:\Software\Classes\$ProgId" -Recurse -Force
        Write-Step "已删除 ProgId：$ProgId"
    }

    # 先照着备份把我们顶掉过的默认关联还原回去
    $restore = @{}
    if (Test-Path $BackupPath) {
        try { $restore = Get-Content $BackupPath -Raw | ConvertFrom-Json -AsHashtable } catch { $restore = @{} }
    }

    foreach ($ext in $Extensions) {
        $key = "HKCU:\Software\Classes\$ext"
        if (-not (Test-Path $key)) { continue }

        $current = Get-DefaultValue $key
        if ($current -eq $ProgId) {
            $original = if ($restore.ContainsKey($ext)) { $restore[$ext] } else { '' }
            if ([string]::IsNullOrEmpty($original)) {
                # 空值不能写成空串：HKCU 的空默认值会遮蔽 HKLM 的系统关联，
                # 卸载后双击 .md 就变成「选择打开方式」。默认值只能走 .NET API 删
                # （PowerShell 的 Remove-ItemProperty -Name '(default)' 在注册表上不生效）。
                $regKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("Software\Classes\$ext", $true)
                if ($regKey) {
                    try { $regKey.DeleteValue('', $false) } catch { }
                    $regKey.Dispose()
                }
                Write-Step "已解除 $ext 的默认关联"
            }
            else {
                Set-ItemProperty -Path $key -Name '(default)' -Value $original
                Write-Step "已把 $ext 的默认打开方式还原成 $original"
            }
        }

        # 安装时写进去的 Content Type 也要一起清掉，否则卸载后仍挂着一个不存在的程序
        $contentType = (Get-ItemProperty -Path $key -Name 'Content Type' -ErrorAction SilentlyContinue).'Content Type'
        if ($contentType -eq $ContentType) {
            Remove-ItemProperty -Path $key -Name 'Content Type' -ErrorAction SilentlyContinue
        }

        $openWith = Join-Path $key 'OpenWithProgids'
        if (Test-Path $openWith) {
            Remove-ItemProperty -Path $openWith -Name $ProgId -ErrorAction SilentlyContinue
        }

        $menuKey = Join-Path $key 'shell\OpenWithMdViewer'
        if (Test-Path $menuKey) { Remove-Item $menuKey -Recurse -Force; Write-Step "已移除 $ext 的右键菜单项" }
    }

    if (Test-Path $InstallDir) {
        Remove-Item $InstallDir -Recurse -Force
        Write-Step "已删除安装目录 $InstallDir"
    }

    Write-Host "`n卸载完成。用户数据（%APPDATA%\MdViewer）保留着，不需要可以手动删。`n" -ForegroundColor Green
    return
}

# ───────────────────────── 安装 ─────────────────────────

Write-Host "`n安装 $AppTitle`n" -ForegroundColor Cyan

# 1. 拿到 exe
if ($ExePath) {
    if (-not (Test-Path $ExePath)) { throw "找不到指定的 exe：$ExePath" }
    $sourceExe = (Resolve-Path $ExePath).Path
    Write-Step "使用现成的 exe：$sourceExe"
}
else {
    $repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $project = Join-Path $repoRoot 'src\MdViewer\MdViewer.csproj'
    if (-not (Test-Path $project)) { throw "找不到项目文件：$project（也可以用 -ExePath 指定现成的 exe）" }

    # auto：本机有 .NET 10 桌面运行时就用精简版，没有就自带运行时
    $chosen = $Mode
    if ($chosen -eq 'auto') {
        $hasDesktop = $false
        try {
            $runtimes = & dotnet --list-runtimes 2>$null
            if ($LASTEXITCODE -eq 0 -and $runtimes) {
                $hasDesktop = [bool]($runtimes | Where-Object { $_ -match '^Microsoft\.WindowsDesktop\.App\s+10\.' })
            }
        } catch { $hasDesktop = $false }
        $chosen = if ($hasDesktop) { 'local' } else { 'portable' }
    }

    $stage = Join-Path $env:TEMP "mdviewer-publish-$(Get-Date -Format 'HHmmss')"
    $common = @(
        'publish', $project, '-c', 'Release', '-r', 'win-x64',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:DebugType=none',
        '-p:SatelliteResourceLanguages=zh-Hans',
        '-p:AllowedReferenceRelatedFileExtensions=.pdb',
        '-o', $stage, '--nologo', '-v', 'q'
    )

    if ($chosen -eq 'local') {
        Write-Step '正在编译精简版（2 MB 左右，依赖本机的 .NET 10 桌面运行时）…'
        $publishLog = & dotnet @common '--self-contained' 'false' 2>&1
    }
    else {
        Write-Step '正在编译便携版（60 MB 左右，自带运行时，第一次会久一点）…'
        $publishLog = & dotnet @common '--self-contained' 'true' '-p:EnableCompressionInSingleFile=true' 2>&1
    }

    if ($LASTEXITCODE -ne 0) {
        $publishLog | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
        throw "编译失败（dotnet publish 返回 $LASTEXITCODE）"
    }

    $sourceExe = Join-Path $stage "$AppName.exe"
    if (-not (Test-Path $sourceExe)) { throw "编译完了但没找到 $sourceExe" }
    $sizeMb = [math]::Round((Get-Item $sourceExe).Length / 1MB, 1)
    Write-Ok "编译完成（$chosen，$sizeMb MB）"
}

# 2. 复制到安装目录
if (-not (Test-Path $InstallDir)) { New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null }

# 正在运行的旧版本会锁住 exe，先请它退出
$running = Get-Process -Name $AppName -ErrorAction SilentlyContinue
if ($running) {
    Write-Step '关闭正在运行的旧版本…'
    $running | ForEach-Object { $_.CloseMainWindow() | Out-Null }
    Start-Sleep -Milliseconds 900
    Get-Process -Name $AppName -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 300
}

Copy-Item $sourceExe $TargetExe -Force
Write-Ok "程序已安装到 $TargetExe"

$version = (Get-Item $TargetExe).VersionInfo.FileVersion
if ($version) { Write-Step "版本 $version" }

# 3. 快捷方式
$startLnk = Join-Path $StartMenuDir "$AppTitle.lnk"
New-Shortcut -Path $startLnk -Target $TargetExe
Write-Ok "开始菜单快捷方式：$startLnk"

if (-not $NoDesktop) {
    $deskLnk = Join-Path $DesktopDir "$AppTitle.lnk"
    New-Shortcut -Path $deskLnk -Target $TargetExe
    Write-Ok "桌面快捷方式：$deskLnk"
}

# 4. 注册表：ProgId
$progKey = "HKCU:\Software\Classes\$ProgId"
New-Item -Path $progKey -Force | Out-Null
Set-ItemProperty -Path $progKey -Name '(default)' -Value "$AppTitle 文档"

New-Item -Path "$progKey\DefaultIcon" -Force | Out-Null
Set-ItemProperty -Path "$progKey\DefaultIcon" -Name '(default)' -Value "$TargetExe,0"

New-Item -Path "$progKey\shell\open\command" -Force | Out-Null
Set-ItemProperty -Path "$progKey\shell\open\command" -Name '(default)' -Value "`"$TargetExe`" `"%1`""

# 让资源管理器把这个 ProgId 认成"能打开文本的应用"
New-Item -Path "$progKey\Application" -Force | Out-Null
Set-ItemProperty -Path "$progKey\Application" -Name 'ApplicationName' -Value $AppTitle
Set-ItemProperty -Path "$progKey\Application" -Name 'ApplicationIcon' -Value "$TargetExe,0"
Write-Ok "已登记 ProgId：$ProgId"

# 5. 扩展名关联
$claimed = @()
$takenOver = @()
$keptByOthers = @()
$backup = @{}

foreach ($ext in $Extensions) {
    $extKey = "HKCU:\Software\Classes\$ext"

    # 顺序要紧：先把原来的默认值读出来，再 New-Item。
    # PowerShell 的 New-Item -Force 对已存在的注册表键会重建，默认值会被清掉。
    $existing = Get-DefaultValue $extKey

    New-Item -Path $extKey -Force | Out-Null

    # 空着、本来就是自己、或者用户没要求保留 → 接管
    $canTakeOver = [string]::IsNullOrEmpty($existing) -or $existing -eq $ProgId -or (-not $KeepExisting)

    if ($canTakeOver) {
        if (-not [string]::IsNullOrEmpty($existing) -and $existing -ne $ProgId) {
            $backup[$ext] = $existing          # 顶掉别人的之前先记账，卸载时还原
            $takenOver += "$ext（原来是 $existing）"
        }
        Set-ItemProperty -Path $extKey -Name '(default)' -Value $ProgId
        $claimed += $ext
    }
    else {
        $keptByOthers += "$ext → $existing"
    }

    Set-ItemProperty -Path $extKey -Name 'Content Type' -Value $ContentType

    $openWith = Join-Path $extKey 'OpenWithProgids'
    New-Item -Path $openWith -Force | Out-Null
    New-ItemProperty -Path $openWith -Name $ProgId -Value '' -PropertyType String -Force | Out-Null

    # 没接管默认值时，补一个右键菜单项，右键也能"用 MdViewer 打开"
    $menuKey = Join-Path $extKey 'shell\OpenWithMdViewer'
    if ($existing -ne $ProgId -and -not $canTakeOver) {
        New-Item -Path $menuKey -Force | Out-Null
        Set-ItemProperty -Path $menuKey -Name '(default)' -Value "用$AppTitle 打开"
        Set-ItemProperty -Path $menuKey -Name 'Icon' -Value "$TargetExe,0"
        New-Item -Path "$menuKey\command" -Force | Out-Null
        Set-ItemProperty -Path "$menuKey\command" -Name '(default)' -Value "`"$TargetExe`" `"%1`""
    }
    elseif (Test-Path $menuKey) {
        Remove-Item $menuKey -Recurse -Force
    }
}

if ($backup.Count -gt 0) {
    $backupDir = Split-Path $BackupPath -Parent
    if (-not (Test-Path $backupDir)) { New-Item -ItemType Directory -Path $backupDir -Force | Out-Null }
    $backup | ConvertTo-Json | Set-Content -Path $BackupPath -Encoding UTF8
}

if ($claimed.Count -gt 0) { Write-Ok ("已设为默认打开方式：" + ($claimed -join ' ')) }
foreach ($item in $takenOver) { Write-Warn2 "顶掉了原来的默认程序：$item（原值已备份，卸载会还原）" }
foreach ($item in $keptByOthers) { Write-Warn2 "按 -KeepExisting 保留原默认程序，只加了右键菜单项：$item" }

# 6. 通知资源管理器刷新图标与关联
$sig = @'
[DllImport("shell32.dll")] public static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);
'@
try {
    $shell32 = Add-Type -MemberDefinition $sig -Name 'ShellNotify' -Namespace 'MdViewerInstaller' -PassThru
    $shell32::SHChangeNotify(0x08000000, 0x0000, [IntPtr]::Zero, [IntPtr]::Zero)
}
catch { Write-Step '资源管理器通知失败，重启资源管理器即可生效（不影响功能）' }

# 7. 收尾说明
Write-Host ''
Write-Host "装好了：$TargetExe" -ForegroundColor Green
Write-Host ''
Write-Host '下一步：' -ForegroundColor Cyan
Write-Host '  1) 双击任意 .md 文件就能用本程序打开'
Write-Host '  2) 如果双击还是弹"选择应用"，说明系统里还有旧的 UserChoice 记录：'
Write-Host '     右键 .md → 打开方式 → 选择其他应用 → 选 Markdown 阅读器 → 勾"始终使用"'
Write-Host '  3) 卸载：.\install.ps1 -Uninstall'
Write-Host ''
