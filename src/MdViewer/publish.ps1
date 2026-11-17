<#
.SYNOPSIS
    编译 Markdown 阅读器（MdViewer）的两个发布版本，产物放到 dist\。

.DESCRIPTION
    两个版本功能完全一样，区别只在「要不要自带 .NET 运行时」：

      local      约 2 MB   依赖目标机器装有 .NET 10 桌面运行时（自己电脑用这个）
      portable   约 62 MB  自带运行时，拷到任何 Win10/11 都能双击就跑（发给别人用这个）

    两个都是「单文件」：只有一枚 exe，没有 dll、没有 runtimes 目录。

.PARAMETER Mode
    local / portable / both（默认 both）。

.PARAMETER Output
    产物目录，默认仓库根目录下的 dist\。

.EXAMPLE
    .\publish.ps1
    .\publish.ps1 -Mode local
    .\publish.ps1 -Mode portable -Output D:\发布
#>
[CmdletBinding()]
param(
    [ValidateSet('local', 'portable', 'both')]
    [string]$Mode = 'both',
    [string]$Output
)

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'MdViewer.csproj'
if (-not (Test-Path $project)) { throw "找不到项目文件：$project" }

if (-not $Output) {
    $repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $Output = Join-Path $repoRoot 'dist'
}
if (-not (Test-Path $Output)) { New-Item -ItemType Directory -Path $Output -Force | Out-Null }
$Output = (Resolve-Path $Output).Path

# 版本号从 csproj 里读，避免手写两处
$version = '0.0.0'
$match = Select-String -Path $project -Pattern '<Version>([^<]+)</Version>'
if ($match) { $version = $match.Matches[0].Groups[1].Value }

$common = @(
    'publish', $project, '-c', 'Release', '-r', 'win-x64',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=none',
    '-p:SatelliteResourceLanguages=zh-Hans',
    '-p:AllowedReferenceRelatedFileExtensions=.pdb',
    '--nologo', '-v', 'q'
)

function Build-Variant {
    param([string]$Kind)

    $stage = Join-Path $env:TEMP "mdviewer-pub-$Kind-$(Get-Date -Format 'HHmmss')"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }

    if ($Kind -eq 'local') {
        Write-Host "  编译 local（依赖 .NET 10 桌面运行时）…" -ForegroundColor Gray
        $log = & dotnet @common '-o' $stage '--self-contained' 'false' 2>&1
    }
    else {
        Write-Host "  编译 portable（自带运行时，这一步慢一些）…" -ForegroundColor Gray
        $log = & dotnet @common '-o' $stage '--self-contained' 'true' '-p:EnableCompressionInSingleFile=true' 2>&1
    }

    if ($LASTEXITCODE -ne 0) {
        $log | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
        throw "dotnet publish 失败（$Kind，返回 $LASTEXITCODE）"
    }

    $exe = Join-Path $stage 'MdViewer.exe'
    if (-not (Test-Path $exe)) { throw "编译完了但没找到 $exe" }

    $target = Join-Path $Output "MdViewer-$version-$Kind.exe"
    Copy-Item $exe $target -Force
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue

    $sizeMb = [math]::Round((Get-Item $target).Length / 1MB, 1)
    Write-Host "  [OK] $target  ($sizeMb MB)" -ForegroundColor Green
    return $target
}

Write-Host "`nMarkdown 阅读器 v$version`n" -ForegroundColor Cyan

$built = @()
if ($Mode -eq 'local' -or $Mode -eq 'both') { $built += Build-Variant 'local' }
if ($Mode -eq 'portable' -or $Mode -eq 'both') { $built += Build-Variant 'portable' }

Write-Host "`n完成。产物在 $Output" -ForegroundColor Cyan
Write-Host "  发 Release 时把 portable 那份传上去，local 那份留给自己用。`n" -ForegroundColor DarkGray
