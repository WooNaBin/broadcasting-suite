#Requires -Version 5.1
<#
.SYNOPSIS
  방송실 프로그램 배포본을 일괄 생성합니다. (Windows / macOS, 산출 경로는 설정 파일에 기록)

.DESCRIPTION
  대상: BroadcastNasBridge, CtrlOne, FileChecker, ScheduleDataManager, ScheduleReader, WorkLog
  - 산출 경로: -OutRoot > BROADCAST_BUILD_DIR > broadcast-suite.build.json > <레포>/Builded
  - OS: -Target Host|Windows|Mac|All (설정 파일 targets 와 동일)
  - .NET 앱: self-contained 단일 실행 파일 (호스트에서 해당 RID 게시)
  - ScheduleReader: .NET self-contained 단일 exe (Windows + Mac, 포트 17823)
  - 스위트 버전: broadcast-suite.version.json (빌드마다 build 번호 증가)
  - 개별 portable 폴더 + 개별 zip + 통합 zip:
      <OutRoot>\BroadcastingApp_<version>_<yyyyMMdd>.zip

.EXAMPLE
  .\scripts\Build-BroadcastApps.ps1
  .\scripts\Build-BroadcastApps.ps1 -Configure
  .\scripts\Build-BroadcastApps.ps1 -SetOutRoot E:\Releases
  .\scripts\Build-BroadcastApps.ps1 -Target All
  .\scripts\Build-BroadcastApps.ps1 -Apps CtrlOne,WorkLog
  .\scripts\Build-BroadcastApps.ps1 -SkipZip
  .\scripts\Build-BroadcastApps.ps1 -SkipBundle
  .\scripts\Build-BroadcastApps.ps1 -Bump Minor
  .\scripts\Build-BroadcastApps.ps1 -Version 1.2.0
  .\scripts\Build-BroadcastApps.ps1 -List
#>
[CmdletBinding()]
param(
    [ValidateSet('BroadcastNasBridge', 'CtrlOne', 'FileChecker', 'ScheduleDataManager', 'ScheduleReader', 'WorkLog')]
    [string[]]$Apps = @(
        'BroadcastNasBridge',
        'CtrlOne',
        'FileChecker',
        'ScheduleDataManager',
        'ScheduleReader',
        'WorkLog'
    ),
    [string]$OutRoot = '',
    [string]$SetOutRoot = '',
    [ValidateSet('Host', 'Windows', 'Mac', 'All')]
    [string]$Target = '',
    [string]$Version = '',
    [ValidateSet('None', 'Build', 'Patch', 'Minor', 'Major')]
    [string]$Bump = 'Build',
    [switch]$SkipZip,
    [switch]$SkipBundle,
    [switch]$List,
    [switch]$Configure,
    [switch]$ShowConfig,
    [switch]$Menu,
    # 레거시 SDM/WL/FC 단독 포터블을 통합 zip Windows\Legacy 에 포함 (기본 on). -IncludeLegacy:$false 로 제외.
    [bool]$IncludeLegacy = $true
)

# 한글 콘솔: UTF-8 (dotnet "복원할 프로젝트를 확인하는 중..." 깨짐 완화)
try {
    $utf8 = New-Object System.Text.UTF8Encoding $false
    if (Get-Command chcp -ErrorAction SilentlyContinue) {
        chcp 65001 | Out-Null
    }
    [Console]::InputEncoding = $utf8
    [Console]::OutputEncoding = $utf8
    $OutputEncoding = $utf8
}
catch { }

$ErrorActionPreference = 'Stop'
$ProjectsRoot = Split-Path $PSScriptRoot -Parent
$BuildConfigPath = Join-Path $ProjectsRoot 'broadcast-suite.build.json'
$script:WantWindows = $true
$script:WantMac = $false

$VersionFile = Join-Path $ProjectsRoot 'broadcast-suite.version.json'
$Stamp = Get-Date -Format 'yyyyMMdd'
$StampTime = Get-Date -Format 'yyyyMMdd-HHmm'
$BuildStarted = Get-Date
$AllSuiteApps = @('BroadcastNasBridge', 'CtrlOne', 'FileChecker', 'ScheduleDataManager', 'ScheduleReader', 'WorkLog')
$BundleZipPath = $null
$SuiteLabel = $null
$SuiteVersionInfo = $null

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "=== $Message ===" -ForegroundColor Cyan
}

function Get-GitInfo([string]$RepoPath) {
    $info = [ordered]@{ commit = $null; branch = $null; dirty = $null }
    if (-not (Test-Path (Join-Path $RepoPath '.git'))) { return $info }
    try {
        Push-Location $RepoPath
        $info.commit = (git rev-parse --short HEAD 2>$null)
        $info.branch = (git rev-parse --abbrev-ref HEAD 2>$null)
        $status = git status --porcelain 2>$null
        $info.dirty = -not [string]::IsNullOrWhiteSpace($status)
    }
    catch { }
    finally { Pop-Location }
    return $info
}

function Ensure-Dir([string]$Path) {
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function Write-Utf8NoBom([string]$FilePath, [string]$Content) {
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($FilePath, $Content, $utf8)
}

function Get-HostOsTag {
    if ($env:OS -eq 'Windows_NT') { return 'windows' }
    return 'macos'
}

function Convert-ToFullPath([string]$PathValue) {
    if ([string]::IsNullOrWhiteSpace($PathValue)) { return $null }
    $p = $PathValue.Trim().Trim('"')
    if ($p.StartsWith('~')) {
        $home = if ($env:HOME) { $env:HOME } else { $env:USERPROFILE }
        $p = Join-Path $home $p.Substring(1).TrimStart('\', '/')
    }
    if (-not [System.IO.Path]::IsPathRooted($p)) {
        $p = Join-Path $ProjectsRoot $p
    }
    try {
        return [System.IO.Path]::GetFullPath($p)
    }
    catch {
        return $p
    }
}

function Read-BuildConfig {
    if (-not (Test-Path $BuildConfigPath)) {
        return [pscustomobject]@{
            outRoot   = $null
            targets   = 'host'
            updatedAt = $null
        }
    }
    $raw = [System.IO.File]::ReadAllText($BuildConfigPath, [System.Text.UTF8Encoding]::new($false)) | ConvertFrom-Json
    if (-not $raw.targets) {
        $raw | Add-Member -NotePropertyName targets -NotePropertyValue 'host' -Force
    }
    return $raw
}

function Save-BuildConfig {
    param(
        [string]$OutRootPath,
        [string]$TargetsValue
    )
    $cfg = Read-BuildConfig
    $payload = [ordered]@{
        outRoot   = $(if ($OutRootPath) { $OutRootPath } elseif ($cfg.outRoot) { [string]$cfg.outRoot } else { Join-Path $ProjectsRoot 'Builded' })
        targets   = $(if ($TargetsValue) { $TargetsValue.ToLowerInvariant() } elseif ($cfg.targets) { [string]$cfg.targets } else { 'host' })
        updatedAt = (Get-Date).ToString('o')
    }
    Write-Utf8NoBom $BuildConfigPath ($payload | ConvertTo-Json -Depth 4)
    Write-Host "빌드 설정 저장: $BuildConfigPath" -ForegroundColor Green
    Write-Host "  outRoot = $($payload.outRoot)"
    Write-Host "  targets = $($payload.targets)"
}

function Resolve-BuildTarget {
    param([string]$Requested)
    $cfg = Read-BuildConfig
    $raw = $Requested
    if ([string]::IsNullOrWhiteSpace($raw)) { $raw = [string]$cfg.targets }
    if ([string]::IsNullOrWhiteSpace($raw)) { $raw = 'host' }
    switch -Regex ($raw.ToLowerInvariant()) {
        '^(all|both)$' { return 'all' }
        '^(mac|macos|osx)$' { return 'macos' }
        '^(win|windows)$' { return 'windows' }
        default { return 'host' }
    }
}

function Initialize-BuildTargets {
    param([string]$ResolvedTarget)
    $hostOs = Get-HostOsTag
    switch ($ResolvedTarget) {
        'all' {
            $script:WantWindows = $true
            $script:WantMac = $true
        }
        'windows' {
            $script:WantWindows = $true
            $script:WantMac = $false
        }
        'macos' {
            $script:WantWindows = $false
            $script:WantMac = $true
        }
        default {
            $script:WantWindows = ($hostOs -eq 'windows')
            $script:WantMac = ($hostOs -ne 'windows')
        }
    }
}

function Resolve-OutRootPath {
    if ($OutRoot) { return (Convert-ToFullPath $OutRoot) }
    if ($SetOutRoot) { return (Convert-ToFullPath $SetOutRoot) }
    if ($env:BROADCAST_BUILD_DIR) { return (Convert-ToFullPath $env:BROADCAST_BUILD_DIR) }
    $cfg = Read-BuildConfig
    if ($cfg.outRoot) { return (Convert-ToFullPath ([string]$cfg.outRoot)) }
    return (Join-Path $ProjectsRoot 'Builded')
}

function Show-BuildConfigInfo {
    $cfg = Read-BuildConfig
    $resolved = Resolve-OutRootPath
    $resolvedTarget = Resolve-BuildTarget $Target
    Write-Host "설정 파일: $(if (Test-Path $BuildConfigPath) { $BuildConfigPath } else { '(없음 — 기본값 사용)' })"
    Write-Host "기록된 outRoot: $(if ($cfg.outRoot) { $cfg.outRoot } else { '(없음)' })"
    Write-Host "기록된 targets: $(if ($cfg.targets) { $cfg.targets } else { 'host' })"
    Write-Host "이번 실행 산출 경로: $resolved"
    Write-Host "이번 실행 대상: $resolvedTarget  (Windows=$script:WantWindows  Mac=$script:WantMac)"
    Write-Host "환경 변수 BROADCAST_BUILD_DIR: $(if ($env:BROADCAST_BUILD_DIR) { $env:BROADCAST_BUILD_DIR } else { '(없음)' })"
    Write-Host "우선순위: -OutRoot > BROADCAST_BUILD_DIR > broadcast-suite.build.json > <레포>/Builded"
}

function Invoke-ConfigureOutRoot {
    $current = Resolve-OutRootPath
    Write-Host "현재 산출 경로: $current"
    $picked = $null
    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
        $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
        $dlg.Description = '방송실 빌드 산출물 폴더를 선택하세요'
        $dlg.ShowNewFolderButton = $true
        if (Test-Path $current) { $dlg.SelectedPath = $current }
        $result = $dlg.ShowDialog()
        if ($result -eq [System.Windows.Forms.DialogResult]::OK) {
            $picked = $dlg.SelectedPath
        }
    }
    catch {
        $typed = Read-Host "산출 폴더 경로 (Enter = 현재 값 유지)"
        if (-not [string]::IsNullOrWhiteSpace($typed)) { $picked = $typed.Trim().Trim('"') }
    }
    if (-not $picked) {
        Write-Host "변경하지 않았습니다."
        return $current
    }
    $full = Convert-ToFullPath $picked
    $tgt = Resolve-BuildTarget $Target
    Save-BuildConfig -OutRootPath $full -TargetsValue $tgt
    return $full
}

function Show-InteractiveMenu {
    while ($true) {
        $OutRoot = Resolve-OutRootPath
        $resolvedTarget = Resolve-BuildTarget $Target
        Initialize-BuildTargets $resolvedTarget
        Write-Host ""
        Write-Host "방송실 프로그램 빌드" -ForegroundColor Cyan
        Write-Host "산출 경로: $OutRoot"
        Write-Host "대상 OS: $resolvedTarget  (이 PC: $(Get-HostOsTag))"
        Write-Host ""
        Write-Host "  1) 빌드 시작"
        Write-Host "  2) 대상 OS: 이 PC만 (host)"
        Write-Host "  3) 대상 OS: Windows + Mac"
        Write-Host "  4) 산출 경로 변경"
        Write-Host "  5) 마지막 빌드 보기"
        Write-Host "  Q) 종료"
        $choice = Read-Host "선택"
        switch -Regex ($choice) {
            '^1$' { return 'build' }
            '^2$' {
                $script:Target = 'Host'
                Save-BuildConfig -OutRootPath $OutRoot -TargetsValue 'host'
            }
            '^3$' {
                $script:Target = 'All'
                Save-BuildConfig -OutRootPath $OutRoot -TargetsValue 'all'
            }
            '^4$' { $script:OutRoot = Invoke-ConfigureOutRoot }
            '^5$' { Show-Manifest; Show-BuildConfigInfo }
            '^[Qq]$' { return 'quit' }
            default { Write-Host "다시 선택하세요." }
        }
    }
}

function Read-SuiteVersion {
    if (-not (Test-Path $VersionFile)) {
        return [pscustomobject]@{
            name        = 'BroadcastingApp'
            version     = '1.0.0'
            build       = 0
            description = '방송실 프로그램 스위트'
        }
    }
    return ([System.IO.File]::ReadAllText($VersionFile, [System.Text.UTF8Encoding]::new($false)) | ConvertFrom-Json)
}

function Save-SuiteVersion($info) {
    $payload = [ordered]@{
        name        = $info.name
        version     = $info.version
        build       = [int]$info.build
        description = $info.description
        lastBuild   = $BuildStarted.ToString('o')
        lastLabel   = $SuiteLabel
    }
    Write-Utf8NoBom $VersionFile ($payload | ConvertTo-Json -Depth 4)
}

function Step-SemVer([string]$ver, [string]$part) {
    $bits = $ver.Split('.')
    while ($bits.Count -lt 3) { $bits += '0' }
    $major = [int]$bits[0]; $minor = [int]$bits[1]; $patch = [int]$bits[2]
    switch ($part) {
        'Major' { $major++; $minor = 0; $patch = 0 }
        'Minor' { $minor++; $patch = 0 }
        'Patch' { $patch++ }
    }
    return "$major.$minor.$patch"
}

function Resolve-SuiteVersion {
    $info = Read-SuiteVersion
    if (-not $info.name) { $info | Add-Member -NotePropertyName name -NotePropertyValue 'BroadcastingApp' -Force }
    if (-not $info.description) { $info | Add-Member -NotePropertyName description -NotePropertyValue '방송실 프로그램 스위트' -Force }

    if ($Version) {
        $info.version = $Version.TrimStart('v', 'V')
        if ($Bump -eq 'None') {
            # keep build as-is unless explicitly bumping
        }
        else {
            $info.build = [int]$info.build + 1
        }
    }
    else {
        switch ($Bump) {
            'None' { }
            'Build' { $info.build = [int]$info.build + 1 }
            'Patch' {
                $info.version = Step-SemVer $info.version 'Patch'
                $info.build = [int]$info.build + 1
            }
            'Minor' {
                $info.version = Step-SemVer $info.version 'Minor'
                $info.build = [int]$info.build + 1
            }
            'Major' {
                $info.version = Step-SemVer $info.version 'Major'
                $info.build = [int]$info.build + 1
            }
        }
    }

    # 파일명용 라벨: 1.0.0.12_20260908
    $script:SuiteLabel = '{0}.{1}_{2}' -f $info.version, $info.build, $Stamp
    $script:SuiteVersionInfo = $info
    return $info
}

function New-ZipFromFolder([string]$SourceFolder, [string]$ZipPath) {
    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
    Compress-Archive -Path (Join-Path $SourceFolder '*') -DestinationPath $ZipPath -Force
    return Get-Item $ZipPath
}

function New-SuiteBundleZip {
    param(
        [object[]]$OkResults,
        [string]$Label
    )

    Write-Step "통합 패키지 BroadcastingApp_$Label"
    $bundleRootName = "BroadcastingApp_$Label"
    $stageRoot = Join-Path $OutRoot '_bundle_stage'
    $stage = Join-Path $stageRoot $bundleRootName
    if (Test-Path $stageRoot) { Remove-Item $stageRoot -Recurse -Force }
    Ensure-Dir $stage

    $windowsDir = Join-Path $stage 'Windows'
    $macDir = Join-Path $stage 'Mac'
    $macArm = Join-Path $macDir 'arm64'
    $macX64 = Join-Path $macDir 'x64'
    $legacyDir = Join-Path $windowsDir 'Legacy'
    Ensure-Dir $windowsDir
    Ensure-Dir $macArm
    Ensure-Dir $macX64

    $legacyNames = @('FileChecker', 'ScheduleDataManager', 'WorkLog')

    foreach ($r in $OkResults) {
        if (-not $r.portable -or -not (Test-Path $r.portable)) {
            Write-Host "  (Windows portable 없음: $($r.name))" -ForegroundColor Yellow
            continue
        }
        $leaf = Split-Path $r.portable -Leaf
        if ($legacyNames -contains $r.name) {
            if (-not $IncludeLegacy) {
                Write-Host "  (Legacy 생략: $leaf)" -ForegroundColor DarkYellow
                continue
            }
            Ensure-Dir $legacyDir
            Write-Host "  + Windows\Legacy\$leaf"
            Copy-Item $r.portable (Join-Path $legacyDir $leaf) -Recurse -Force
        }
        else {
            Write-Host "  + Windows\$leaf"
            Copy-Item $r.portable (Join-Path $windowsDir $leaf) -Recurse -Force
        }
    }

    # macOS: Mac\arm64\ · Mac\x64\ (#62). WorkLog는 폴더형 우선, .app은 Legacy 표기.
    $macPairs = @(
        @{ Arch = 'arm64'; Dest = $macArm; Paths = @(
            (Join-Path $OutRoot 'BroadcastNasBridge\BroadcastNasBridge-macOS-arm64'),
            (Join-Path $OutRoot 'CtrlOne\CtrlOne-macOS-arm64'),
            (Join-Path $OutRoot 'WorkLog\WorkLog-macOS-arm64'),
            (Join-Path $OutRoot 'ScheduleDataManager\BroadcastingSchedule-macOS-arm64'),
            (Join-Path $OutRoot 'FileChecker\FileChecker-macOS-arm64'),
            (Join-Path $OutRoot 'ScheduleReader\ScheduleReader-macOS-arm64')
        ); LegacyApps = @(
            (Join-Path $OutRoot 'WorkLog\WorkLog-macOS-arm64.app')
        ) },
        @{ Arch = 'x64'; Dest = $macX64; Paths = @(
            (Join-Path $OutRoot 'BroadcastNasBridge\BroadcastNasBridge-macOS-x64'),
            (Join-Path $OutRoot 'CtrlOne\CtrlOne-macOS-x64'),
            (Join-Path $OutRoot 'WorkLog\WorkLog-macOS-x64'),
            (Join-Path $OutRoot 'ScheduleDataManager\BroadcastingSchedule-macOS-x64'),
            (Join-Path $OutRoot 'FileChecker\FileChecker-macOS-x64'),
            (Join-Path $OutRoot 'ScheduleReader\ScheduleReader-macOS-x64')
        ); LegacyApps = @(
            (Join-Path $OutRoot 'WorkLog\WorkLog-macOS-x64.app')
        ) }
    )
    $macCopied = 0
    foreach ($pair in $macPairs) {
        foreach ($src in $pair.Paths) {
            if (Test-Path $src) {
                $leaf = Split-Path $src -Leaf
                Write-Host "  + Mac\$($pair.Arch)\$leaf"
                Copy-Item $src (Join-Path $pair.Dest $leaf) -Recurse -Force
                $macCopied++
            }
        }
        if ($IncludeLegacy) {
            $leg = Join-Path $pair.Dest 'Legacy'
            foreach ($src in $pair.LegacyApps) {
                if (Test-Path $src) {
                    Ensure-Dir $leg
                    $leaf = Split-Path $src -Leaf
                    Write-Host "  + Mac\$($pair.Arch)\Legacy\$leaf"
                    Copy-Item $src (Join-Path $leg $leaf) -Recurse -Force
                    $macCopied++
                }
            }
        }
    }
    $macMarker = Join-Path $macDir 'README.txt'
    if ($macCopied -eq 0) {
        Write-Utf8NoBom $macMarker @"
macOS 배포본이 없습니다.
Mac에서 scripts/Build-BroadcastApps.sh --target macos (또는 -Target Mac) 로 빌드하세요.
Windows에서도 -Target All 이면 가능한 앱은 크로스 게시합니다 (FileChecker 제외).

레이아웃: Mac/arm64/ … Mac/x64/
시작: BroadcastNasBridge-macOS-*/Launch-BroadcastNasBridge.command
"@.TrimEnd()
    }
    else {
        Write-Utf8NoBom $macMarker @"
macOS 배포본
============
레이아웃: Mac/arm64/ 와 Mac/x64/ (#62)

권장 시작
---------
1. 해당 CPU 폴더의 BroadcastNasBridge-macOS-*/Launch-BroadcastNasBridge.command
2. 브라우저 http://127.0.0.1:17820 → 일정·일지·파일체크

WorkLog는 형제와 같이 폴더형(Launch-WorkLog.command). .app 은 Legacy/ 에 있을 수 있음.
FileChecker 는 Windows만. ScheduleReader 는 Windows + Mac.
Gatekeeper: 우클릭 → 열기 또는 xattr -dr com.apple.quarantine <폴더>
"@.TrimEnd()
    }

    $versionTxt = @"
BroadcastingApp suite
version: $($SuiteVersionInfo.version)
build: $($SuiteVersionInfo.build)
label: $Label
builtAt: $($BuildStarted.ToString('yyyy-MM-dd HH:mm:ss'))
stamp: $Stamp
includeLegacy: $IncludeLegacy

Apps:
$($OkResults | ForEach-Object { "- $($_.name)" } | Out-String)
"@
    Write-Utf8NoBom (Join-Path $stage 'VERSION.txt') $versionTxt.TrimEnd()

    $readme = @"
방송실 프로그램 통합 배포 패키지
================================

폴더 구성
---------
- Windows\          … 권장 앱 (Bridge, CtrlOne, ScheduleReader)
- Windows\Legacy\   … 단독 SDM/WL/FC (브리지 없을 때 폴백, IncludeLegacy=$IncludeLegacy)
- Mac\arm64\ · Mac\x64\  … CPU별 macOS 포터블
- Install-BroadcastApps.bat / .ps1
- VERSION.txt

빠른 시작 (#63)
---------------
1. zip 압축 해제
2. Install-BroadcastApps.bat (Windows) 또는
   Windows\BroadcastNasBridge-Windows-x64\Start-BroadcastNasBridge.bat
3. 브라우저 http://127.0.0.1:17820

Mac: Mac\<arch>\BroadcastNasBridge-macOS-*\Launch-BroadcastNasBridge.command

포트: Bridge 17820 (권장) / 레거시 17821·17822·5187 / SR 17823 / CtrlOne 5177
레거시 정리: docs/LEGACY-CLEANUP.md
"@
    Write-Utf8NoBom (Join-Path $stage 'README.txt') $readme.TrimEnd()

    $howTo = @"
방송실 프로그램 — 사용 방법
============================

1) BroadcastNasBridge 실행 (필수 권장)
2) http://127.0.0.1:17820 에서 NAS 연결
3) 일정 / 작업일지 / 파일체크 카드로 진입

단독 exe(Legacy)는 브리지가 꺼져 있을 때만 사용하세요.
브리지가 켜져 있으면 단독 앱도 브리지 URL로만 열립니다.
"@
    Write-Utf8NoBom (Join-Path $stage 'HOW-TO-START.txt') $howTo.TrimEnd()

    $installerPs1 = Join-Path $PSScriptRoot 'Install-BroadcastApps.ps1'
    $installerBat = Join-Path $PSScriptRoot 'Install-BroadcastApps.bat'
    if (-not (Test-Path $installerPs1)) { throw "설치 스크립트 없음: $installerPs1" }
    if (-not (Test-Path $installerBat)) { throw "설치 스크립트 없음: $installerBat" }
    $installerText = [System.IO.File]::ReadAllText($installerPs1, [System.Text.UTF8Encoding]::new($false)).TrimStart([char]0xFEFF)
    $utf8Bom = New-Object System.Text.UTF8Encoding $true
    [System.IO.File]::WriteAllText((Join-Path $stage 'Install-BroadcastApps.ps1'), $installerText, $utf8Bom)
    Copy-Item $installerBat (Join-Path $stage 'Install-BroadcastApps.bat') -Force
    Write-Host "  + Install-BroadcastApps.ps1 / .bat"
    Write-Host "  + HOW-TO-START.txt"

    $zipName = "BroadcastingApp_$Label.zip"
    $zipPath = Join-Path $OutRoot $zipName
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

    # Compress-Archive: 상위 폴더 이름이 zip 안에 남도록 stage 내용을 압축
    Compress-Archive -Path $stage -DestinationPath $zipPath -Force
    Remove-Item $stageRoot -Recurse -Force

    $item = Get-Item $zipPath
    Write-Host ("통합 zip: {0} ({1:N1} MB)" -f $item.FullName, ($item.Length / 1MB))
    return $item.FullName
}

function Stop-IfRunning([string[]]$ProcessNames) {
    foreach ($name in $ProcessNames) {
        Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
            Write-Host "  실행 중 프로세스 종료: $($_.ProcessName) (PID $($_.Id))" -ForegroundColor Yellow
            Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
        }
    }
    Start-Sleep -Milliseconds 400
}

function Invoke-DotnetPublish {
    param(
        [string]$Project,
        [string]$OutputDir,
        [string]$Runtime = 'win-x64',
        [hashtable]$ExtraProps = @{}
    )
    Ensure-Dir $OutputDir
    $args = @(
        'publish', $Project,
        '-c', 'Release',
        '-r', $Runtime,
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        '-o', $OutputDir
    )
    if ($Runtime -like 'win-*') {
        $args += '-p:EnableWindowsTargeting=true'
    }
    foreach ($key in $ExtraProps.Keys) {
        $args += "-p:$key=$($ExtraProps[$key])"
    }
    & dotnet @args | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 실패: $Project ($Runtime)" }
}

function New-MacPortableFolder {
    param(
        [string]$Project,
        [string]$DestRoot,
        [string]$FolderPrefix,
        [string]$BinaryName,
        [hashtable]$ExtraProps = @{},
        [string[]]$ExtraFiles = @()
    )
    $created = @()
    foreach ($pair in @(
            @{ Rid = 'osx-arm64'; Label = 'arm64' },
            @{ Rid = 'osx-x64'; Label = 'x64' }
        )) {
        $stage = Join-Path $DestRoot ("_stage_" + $pair.Rid)
        if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
        Invoke-DotnetPublish -Project $Project -OutputDir $stage -Runtime $pair.Rid -ExtraProps $ExtraProps
        $portable = Join-Path $DestRoot ("$FolderPrefix-macOS-" + $pair.Label)
        if (Test-Path $portable) { Remove-Item $portable -Recurse -Force }
        Ensure-Dir $portable
        $binSrc = Join-Path $stage $BinaryName
        if (-not (Test-Path $binSrc)) { throw "$BinaryName 없음 ($($pair.Rid))" }
        Copy-Item $binSrc (Join-Path $portable $BinaryName) -Force
        Get-ChildItem $stage -Force | Where-Object { $_.Name -ne $BinaryName } | ForEach-Object {
            Copy-Item $_.FullName (Join-Path $portable $_.Name) -Recurse -Force
        }
        foreach ($extra in $ExtraFiles) {
            if (Test-Path $extra) {
                Copy-Item $extra (Join-Path $portable (Split-Path $extra -Leaf)) -Force
            }
        }
        Remove-Item $stage -Recurse -Force
        Write-MacCommandLauncher -Dir $portable -BinaryName $BinaryName -FileName ("Launch-{0}.command" -f $BinaryName)
        Write-Host "  Mac $($pair.Label): $portable"
        $created += $portable
    }
    return $created
}

function Update-BuildVerifyDoc {
    param(
        [object[]]$BuildResults,
        [string]$Label,
        [object]$SuiteInfo,
        [string]$ZipPath,
        [datetime]$BuiltAt
    )
    $docPath = Join-Path $ProjectsRoot 'docs\BUILD-VERIFY.md'
    $markerStart = '<!-- BUILD-VERIFY:AUTO-START -->'
    $markerEnd = '<!-- BUILD-VERIFY:AUTO-END -->'
    $manualStart = '<!-- BUILD-VERIFY:MANUAL-START -->'
    $manualEnd = '<!-- BUILD-VERIFY:MANUAL-END -->'

    $appRows = @()
    foreach ($r in $BuildResults) {
        $appRows += "| $($r.name) | $($r.status)$(if ($r.error) { " — $($r.error)" }) |"
    }
    if ($appRows.Count -eq 0) { $appRows += '| (없음) | — |' }

    $zipOk = $ZipPath -and (Test-Path -LiteralPath $ZipPath)
    $zipDisplay = if ($ZipPath) { $ZipPath } else { $null }
    $latestPath = Join-Path $OutRoot 'LATEST.txt'
    $latestZipNote = $null
    if (-not $zipDisplay -and (Test-Path -LiteralPath $latestPath)) {
        $latestMap = @{}
        Get-Content -LiteralPath $latestPath | ForEach-Object {
            if ($_ -match '^\s*([^=]+)=(.*)$') { $latestMap[$Matches[1].Trim()] = $Matches[2].Trim() }
        }
        if ($latestMap['bundleZip']) {
            $zipDisplay = $latestMap['bundleZip']
            $zipOk = Test-Path -LiteralPath $zipDisplay
            $latestZipNote = '- 참고: 이번 실행은 통합 zip 미생성(`-SkipBundle` 등). 위 zip은 `LATEST.txt` 기준'
        }
    }
    $legacyHint = if ($IncludeLegacy) { '- [x] `Windows\Legacy\` (SDM/WL/FC) — IncludeLegacy=True' } else { '- [ ] `Windows\Legacy\` — IncludeLegacy=False (의도적 제외)' }
    $macHint = if ($WantMac) { '- [x] `Mac\arm64\` · `Mac\x64\` — Mac 빌드 포함' } else { '- [ ] `Mac\arm64\` · `Mac\x64\` — 이번 빌드는 Windows만 (Host/Windows)' }

    $autoBlock = @"
$markerStart
## 현재 빌드 (자동)

| 항목 | 값 |
|------|-----|
| 라벨 | ``$Label`` |
| 버전 | ``$($SuiteInfo.version)`` (build $($SuiteInfo.build)) |
| 빌드 시각 | $($BuiltAt.ToString('yyyy-MM-dd HH:mm:ss')) |
| 출력 | ``$OutRoot`` |
| 통합 zip | $(if ($zipDisplay) { "``$zipDisplay``" } else { '없음' }) |
| Windows | $WantWindows |
| Mac | $WantMac |
| IncludeLegacy | $IncludeLegacy |

### 이번 실행 앱 결과

| 앱 | 상태 |
|----|------|
$($appRows -join "`n")

### 산출물 빠른 확인 (자동 힌트)

- $(if ($zipOk) { '[x]' } else { '[ ]' }) 통합 zip 경로가 ``LATEST.txt`` / 위 표와 일치
$(if ($latestZipNote) { $latestZipNote })
- $(if ($WantWindows) { '[x]' } else { '[ ]' }) ``Windows\BroadcastNasBridge-Windows-x64`` 존재 예상
$legacyHint
$macHint
$markerEnd
"@.TrimEnd()

    $manualDefault = @"
$manualStart
## 이번 릴리스 실기 (수동)

새 Minor/기능 빌드 후 항목을 추가·정리하세요. 빌드 스크립트는 **이 구역을 지우지 않습니다.**

### Windows

- [ ] Bridge 시작 → http://127.0.0.1:17820 NAS 연결
- [ ] `/schedule` · `/worklog` · `/files` 카드 진입
- [ ] Install 바로가기가 브리지로 열림 · Legacy는 ``Windows\Legacy\``

### macOS

- [ ] ``Mac\<arch>\BroadcastNasBridge-macOS-*\Launch-*.command``
- [ ] 브리지 유도 시 중복 마운트 없음

### 상시 스모크

- [ ] Bridge 탭 종료 후 프로세스 종료
- [ ] CtrlOne · ScheduleReader 기동
$manualEnd
"@.TrimEnd()

    $header = @"
# 빌드 확인 체크리스트

최신 스위트 빌드 후 **직접 확인해야 할 항목**을 모은 문서입니다.  
``Build-BroadcastApps.ps1`` / ``.sh`` 실행 시 **「현재 빌드(자동)」** 구역이 갱신되고, 동일 내용이 ``Builded\BUILD-VERIFY.md``에도 복사됩니다.

- 배포 설치 절차: [DEPLOY-CHECKLIST.md](DEPLOY-CHECKLIST.md)
- 변경 기록: [CHANGES.md](../CHANGES.md) · 할 일: [TODO.md](../TODO.md)

체크(``- [x]``)는 **실기한 사람이 수동으로** 표시합니다. 자동 구역의 메타·산출물 표만 빌드가 덮어씁니다.

---

"@

    $manualBlock = $manualDefault
    if (Test-Path -LiteralPath $docPath) {
        $existing = [System.IO.File]::ReadAllText($docPath, [System.Text.UTF8Encoding]::new($false))
        $m0 = $existing.IndexOf($manualStart)
        $m1 = $existing.IndexOf($manualEnd)
        if ($m0 -ge 0 -and $m1 -gt $m0) {
            $manualBlock = $existing.Substring($m0, $m1 + $manualEnd.Length - $m0).TrimEnd()
        }
    }

    $full = ($header.TrimEnd() + "`r`n`r`n" + $autoBlock + "`r`n`r`n---`r`n`r`n" + $manualBlock + "`r`n").Replace("`n", "`r`n")
    # Normalize accidental double CR
    while ($full.Contains("`r`r`n")) { $full = $full.Replace("`r`r`n", "`r`n") }

    Ensure-Dir (Split-Path $docPath -Parent)
    $docText = $full.TrimEnd() + "`r`n"
    Write-Utf8NoBom $docPath $docText

    $outCopy = Join-Path $OutRoot 'BUILD-VERIFY.md'
    Write-Utf8NoBom $outCopy $docText
    Write-Host "빌드 확인 문서: $docPath"
    Write-Host "             → $outCopy"
}

function Show-Manifest {
    $manifestPath = Join-Path $OutRoot 'manifest.json'
    if (-not (Test-Path $manifestPath)) {
        Write-Host "manifest.json 없음: $manifestPath"
        return
    }
    $raw = [System.IO.File]::ReadAllText($manifestPath, [System.Text.UTF8Encoding]::new($false))
    $m = $raw | ConvertFrom-Json
    Write-Host "마지막 빌드: $($m.builtAt)"
    Write-Host "출력 루트: $($m.outRoot)"
    if ($m.suite) {
        Write-Host ("스위트 버전: {0} (build {1})  label={2}" -f $m.suite.version, $m.suite.build, $m.suite.label)
    }
    if ($m.bundleZip) {
        Write-Host "통합 zip: $($m.bundleZip)"
    }
    Write-Host ""
    foreach ($app in $m.apps) {
        $dirty = if ($app.git.dirty) { ' (dirty)' } else { '' }
        Write-Host ("- {0}: {1}{2}" -f $app.name, $app.status, $(if ($app.error) { " — $($app.error)" } else { '' }))
        if ($app.git.commit) {
            Write-Host ("    git {0} @ {1}{2}" -f $app.git.branch, $app.git.commit, $dirty)
        }
        if ($app.portable) { Write-Host "    portable: $($app.portable)" }
        if ($app.zip) { Write-Host "    zip: $($app.zip)" }
    }
}

if ($SetOutRoot) {
    $OutRoot = Convert-ToFullPath $SetOutRoot
    Save-BuildConfig -OutRootPath $OutRoot -TargetsValue (Resolve-BuildTarget $Target)
}
elseif ($Configure) {
    $null = Invoke-ConfigureOutRoot
    return
}
else {
    $OutRoot = Resolve-OutRootPath
}

Initialize-BuildTargets (Resolve-BuildTarget $Target)

if ($ShowConfig) {
    Show-BuildConfigInfo
    return
}

if ($List) {
    Show-BuildConfigInfo
    Write-Host ""
    if (Test-Path $VersionFile) {
        $v = Read-SuiteVersion
        Write-Host ("현재 스위트 버전 파일: {0}.{1}" -f $v.version, $v.build)
        if ($v.lastLabel) { Write-Host "마지막 라벨: $($v.lastLabel)" }
        Write-Host ""
    }
    Show-Manifest
    return
}

if ($Menu) {
    $menuAct = Show-InteractiveMenu
    if ($menuAct -eq 'quit') { return }
    $OutRoot = Resolve-OutRootPath
    Initialize-BuildTargets (Resolve-BuildTarget $Target)
}

if (-not (Test-Path $BuildConfigPath)) {
    Save-BuildConfig -OutRootPath $OutRoot -TargetsValue (Resolve-BuildTarget $Target)
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK(dotnet)가 PATH에 없습니다.'
}

Ensure-Dir $OutRoot
$results = @()

function Add-Result {
    param(
        [string]$Name,
        [string]$Status,
        [string]$Portable = $null,
        [string]$Zip = $null,
        [string]$ErrorMessage = $null,
        [object]$Git = $null,
        [hashtable]$Extra = @{}
    )
    $item = [ordered]@{
        name     = $Name
        status   = $Status
        portable = $Portable
        zip      = $Zip
        error    = $ErrorMessage
        git      = $Git
    }
    foreach ($k in $Extra.Keys) { $item[$k] = $Extra[$k] }
    $script:results += [pscustomobject]$item
}

function Sync-BridgeUi {
    $ps1 = Join-Path $ProjectsRoot 'BroadcastNasBridge\scripts\sync-ui.ps1'
    $sh = Join-Path $ProjectsRoot 'BroadcastNasBridge\scripts\sync-ui.sh'
    if (Test-Path $ps1) {
        & $ps1
        return
    }
    if (Test-Path $sh) {
        bash $sh
    }
}

function Write-MacCommandLauncher([string]$Dir, [string]$BinaryName, [string]$FileName = 'Launch.command') {
    $path = Join-Path $Dir $FileName
    # Finder .command는 Terminal을 연다. 앱은 분리 실행하고 창을 닫아 쌓임을 막는다.
    $body = @"
#!/bin/bash
cd "`$(dirname "`$0")" || exit 1
BIN="./$BinaryName"
chmod +x "`$BIN" 2>/dev/null || true
if ! pgrep -xq "$BinaryName" >/dev/null 2>&1; then
  nohup "`$BIN" >/dev/null 2>&1 &
  disown 2>/dev/null || true
fi
osascript >/dev/null 2>&1 <<'OSA' &
delay 0.2
tell application "Terminal"
  try
    close front window saving no
  end try
end tell
OSA
exit 0
"@
    Write-Utf8NoBom $path $body.Replace("`r`n", "`n")
    try { & chmod +x $path 2>$null } catch { }
}

# --- BroadcastNasBridge ---
function Build-BroadcastNasBridge {
    Write-Step 'BroadcastNasBridge'
    $repo = Join-Path $ProjectsRoot 'BroadcastNasBridge'
    $csproj = Join-Path $repo 'BroadcastNasBridge.csproj'
    if (-not (Test-Path $csproj)) { throw "BroadcastNasBridge 소스 없음: $repo" }
    $git = Get-GitInfo $ProjectsRoot
    Stop-IfRunning @('BroadcastNasBridge')
    Sync-BridgeUi

    $dest = Join-Path $OutRoot 'BroadcastNasBridge'
    Ensure-Dir $dest
    $portable = $null
    $zipPath = $null

    if ($WantWindows) {
        $stage = Join-Path $dest '_stage'
        if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
        Invoke-DotnetPublish -Project $csproj -OutputDir $stage

        $portable = Join-Path $dest 'BroadcastNasBridge-Windows-x64'
        if (Test-Path $portable) { Remove-Item $portable -Recurse -Force }
        Ensure-Dir $portable
        Copy-Item (Join-Path $stage '*') $portable -Recurse -Force
        $launchBat = Join-Path $repo 'scripts\Launch-BroadcastNasBridge.bat'
        if (Test-Path $launchBat) { Copy-Item $launchBat $portable -Force }
        Write-Utf8NoBom (Join-Path $portable 'Start-BroadcastNasBridge.bat') "@echo off`r`ncd /d `"%~dp0`"`r`nstart `"`" BroadcastNasBridge.exe`r`n"
        Remove-Item $stage -Recurse -Force

        if (-not $SkipZip) {
            $zipPath = Join-Path $dest "BroadcastNasBridge-Windows-x64-$Stamp.zip"
            New-ZipFromFolder $portable $zipPath | Out-Null
        }
        Write-Host "완료: $portable"
    }

    if ($WantMac) {
        $macDirs = @(New-MacPortableFolder -Project $csproj -DestRoot $dest -FolderPrefix 'BroadcastNasBridge' -BinaryName 'BroadcastNasBridge' |
            Where-Object { $_ -is [string] -and (Test-Path -LiteralPath $_ -PathType Container) })
        foreach ($d in $macDirs) {
            Write-MacCommandLauncher -Dir ([string]$d) -BinaryName 'BroadcastNasBridge' -FileName 'Launch-BroadcastNasBridge.command'
        }
    }

    Add-Result -Name 'BroadcastNasBridge' -Status 'ok' -Portable $portable -Zip $zipPath -Git $git
}

# --- CtrlOne ---
function Build-CtrlOne {
    Write-Step 'CtrlOne'
    $repo = Join-Path $ProjectsRoot 'CtrlOne'
    $csproj = Join-Path $repo 'CtrlOne.csproj'
    if (-not (Test-Path $csproj)) { throw "CtrlOne 소스 없음: $repo" }
    $git = Get-GitInfo $repo
    $dest = Join-Path $OutRoot 'CtrlOne'
    Ensure-Dir $dest
    $portable = $null
    $zipPath = $null

    if ($WantWindows) {
        Stop-IfRunning @('CtrlOne')
        $stage = Join-Path $dest '_stage'
        if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
        Ensure-Dir $stage

        Invoke-DotnetPublish -Project $csproj -OutputDir $stage

        $exe = Join-Path $stage 'CtrlOne.exe'
        if (-not (Test-Path $exe)) { throw 'CtrlOne.exe 없음' }

        $keep = @('devices.json', 'presets.json', 'CtrlOne-macOS-arm64', 'CtrlOne-macOS-x64')
        Get-ChildItem $dest -Force -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -notin ($keep + '_stage') } |
            Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

        Copy-Item $exe (Join-Path $dest 'CtrlOne.exe') -Force
        $demoSrc = Join-Path $stage 'demo-preview'
        if (Test-Path $demoSrc) {
            $demoDst = Join-Path $dest 'demo-preview'
            if (Test-Path $demoDst) { Remove-Item $demoDst -Recurse -Force }
            Copy-Item $demoSrc $demoDst -Recurse -Force
        }
        Remove-Item $stage -Recurse -Force

        $portable = Join-Path $dest 'CtrlOne-Windows-x64'
        if (Test-Path $portable) { Remove-Item $portable -Recurse -Force }
        Ensure-Dir $portable
        Copy-Item (Join-Path $dest 'CtrlOne.exe') (Join-Path $portable 'CtrlOne.exe')
        if (Test-Path (Join-Path $dest 'demo-preview')) {
            Copy-Item (Join-Path $dest 'demo-preview') (Join-Path $portable 'demo-preview') -Recurse
        }

        if (-not $SkipZip) {
            $zipPath = Join-Path $dest "CtrlOne-Windows-x64-$Stamp.zip"
            New-ZipFromFolder $portable $zipPath | Out-Null
        }
        Write-Host "완료: $dest\CtrlOne.exe"
    }

    if ($WantMac) {
        try {
            New-MacPortableFolder -Project $csproj -DestRoot $dest -FolderPrefix 'CtrlOne' -BinaryName 'CtrlOne'
        }
        catch {
            Write-Host "  Mac 건너뜀 (CtrlOne): $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }

    Add-Result -Name 'CtrlOne' -Status 'ok' -Portable $portable -Zip $zipPath -Git $git -Extra @{ exe = $(if ($portable) { Join-Path $dest 'CtrlOne.exe' } else { $null }) }
}

# --- FileChecker ---
function Build-FileChecker {
    Write-Step 'FileChecker'
    $repo = Join-Path $ProjectsRoot 'FileChecker'
    $csproj = Join-Path $repo 'FileCheckerFinder.csproj'
    if (-not (Test-Path $csproj)) { throw "FileChecker 소스 없음: $repo" }
    $git = Get-GitInfo $repo
    $dest = Join-Path $OutRoot 'FileChecker'
    Ensure-Dir $dest
    $portable = $null
    $zipPath = $null

    if ($WantWindows) {
        Stop-IfRunning @('FileCheckerFinder')
        $stage = Join-Path $dest '_stage'
        if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
        Ensure-Dir $stage

        Invoke-DotnetPublish -Project $csproj -OutputDir $stage -ExtraProps @{
            IncludeNativeLibrariesForSelfExtract = 'true'
        }

        $exeName = 'FileCheckerFinder.exe'
        $exe = Join-Path $stage $exeName
        if (-not (Test-Path $exe)) { throw "$exeName 없음" }

        $portable = Join-Path $dest 'FileChecker-Windows-x64'
        if (Test-Path $portable) { Remove-Item $portable -Recurse -Force }
        Ensure-Dir $portable
        Copy-Item $exe (Join-Path $portable $exeName)
        $batSrc = Join-Path $stage 'Start-FileCheckerFinder.bat'
        if (-not (Test-Path $batSrc)) { $batSrc = Join-Path $repo 'Start-FileCheckerFinder.bat' }
        if (Test-Path $batSrc) { Copy-Item $batSrc (Join-Path $portable 'Start-FileCheckerFinder.bat') }

        Copy-Item $exe (Join-Path $dest $exeName) -Force
        if (Test-Path $batSrc) { Copy-Item $batSrc (Join-Path $dest 'Start-FileCheckerFinder.bat') -Force }
        Remove-Item $stage -Recurse -Force

        if (-not $SkipZip) {
            $zipPath = Join-Path $dest "FileChecker-Windows-x64-$Stamp.zip"
            New-ZipFromFolder $portable $zipPath | Out-Null
        }
        Write-Host "완료: $dest\$exeName (배포 zip에는 data 제외)"
    }

    if ($WantMac) {
        Write-Host '  Mac 생략: FileChecker는 net-windows + WinForms (Windows 전용)' -ForegroundColor Yellow
    }

    Add-Result -Name 'FileChecker' -Status 'ok' -Portable $portable -Zip $zipPath -Git $git -Extra @{ exe = $(if ($portable) { Join-Path $dest 'FileCheckerFinder.exe' } else { $null }) }
}

# --- ScheduleDataManager ---
function Build-ScheduleDataManager {
    Write-Step 'ScheduleDataManager'
    $repo = Join-Path $ProjectsRoot 'ScheduleDataManager'
    $csproj = Join-Path $repo 'LocalBridge\LocalBridge.csproj'
    if (-not (Test-Path $csproj)) { throw "ScheduleDataManager 소스 없음: $repo" }
    $git = Get-GitInfo $repo
    $dest = Join-Path $OutRoot 'ScheduleDataManager'
    Ensure-Dir $dest
    $env:SCHEDULE_BUILD_DIR = $dest
    $portable = $null
    $zipPath = $null

    if ($WantWindows) {
        Stop-IfRunning @('BroadcastingSchedule')
        $pkg = Join-Path $repo 'package-portable.ps1'
        if (Test-Path $pkg) {
            & $pkg
            if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) { throw 'ScheduleDataManager package-portable 실패' }
        }
        else {
            Invoke-DotnetPublish -Project $csproj -OutputDir (Join-Path $dest '_build\win-x64')
            $portableTmp = Join-Path $dest 'BroadcastingSchedule-Windows-x64'
            Ensure-Dir $portableTmp
            Copy-Item (Join-Path $dest '_build\win-x64\BroadcastingSchedule.exe') (Join-Path $portableTmp 'BroadcastingSchedule.exe')
        }

        $portable = Join-Path $dest 'BroadcastingSchedule-Windows-x64'
        $zipPath = Join-Path $dest "BroadcastingSchedule-Windows-x64-$Stamp.zip"
        if (-not (Test-Path $portable)) { throw "portable 폴더 없음: $portable" }
        if ($SkipZip) { $zipPath = $(if (Test-Path $zipPath) { $zipPath } else { $null }) }
        elseif (-not (Test-Path $zipPath)) {
            New-ZipFromFolder $portable $zipPath | Out-Null
        }
    }

    if ($WantMac) {
        New-MacPortableFolder -Project $csproj -DestRoot $dest -FolderPrefix 'BroadcastingSchedule' -BinaryName 'BroadcastingSchedule'
    }

    Add-Result -Name 'ScheduleDataManager' -Status 'ok' -Portable $portable -Zip $(if ($zipPath -and (Test-Path $zipPath)) { $zipPath } else { $null }) -Git $git
}

# --- WorkLog ---
function Build-WorkLog {
    Write-Step 'WorkLog'
    $repo = Join-Path $ProjectsRoot 'WorkLog'
    if (-not (Test-Path $repo)) { throw "WorkLog 소스 없음: $repo" }
    $git = Get-GitInfo $repo
    $dest = Join-Path $OutRoot 'WorkLog'
    Ensure-Dir $dest
    $env:WORKLOG_BUILD_DIR = $dest
    $portable = $null
    $zipPath = $null

    if ($WantWindows) {
        Stop-IfRunning @('WorkLog')
        $pkg = Join-Path $repo 'package-portable.ps1'
        if (-not (Test-Path $pkg)) { throw "package-portable.ps1 없음: $pkg" }
        & $pkg
        if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) { throw 'WorkLog package-portable 실패' }

        $portable = Join-Path $dest 'WorkLog-Windows-x64'
        $zipPath = Join-Path $dest "WorkLog-Windows-x64-$Stamp.zip"
        if (-not (Test-Path $portable)) { throw "portable 폴더 없음: $portable" }
        if ($SkipZip) { $zipPath = $(if (Test-Path $zipPath) { $zipPath } else { $null }) }
        elseif (-not (Test-Path $zipPath)) {
            New-ZipFromFolder $portable $zipPath | Out-Null
        }
    }

    if ($WantMac) {
        $macPs1 = Join-Path $repo 'package-mac.ps1'
        $macSh = Join-Path $repo 'package-mac.sh'
        if (Test-Path $macPs1) {
            & $macPs1
            if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) { throw 'WorkLog package-mac 실패' }
        }
        elseif (Test-Path $macSh) {
            bash $macSh
        }
        else {
            Write-Host '  Mac 건너뜀: WorkLog package-mac 스크립트 없음' -ForegroundColor Yellow
        }
    }

    Add-Result -Name 'WorkLog' -Status 'ok' -Portable $portable -Zip $(if ($zipPath -and (Test-Path $zipPath)) { $zipPath } else { $null }) -Git $git
}

# --- ScheduleReader (.NET self-contained) ---
function Build-ScheduleReader {
    Write-Step 'ScheduleReader'
    $repo = Join-Path $ProjectsRoot 'ScheduleReader'
    $csproj = Join-Path $repo 'ScheduleReader.csproj'
    if (-not (Test-Path $csproj)) { throw "ScheduleReader 소스 없음: $csproj" }
    $git = Get-GitInfo $repo
    $dest = Join-Path $OutRoot 'ScheduleReader'
    Ensure-Dir $dest
    $portable = $null
    $zipPath = $null

    if ($WantWindows) {
        Stop-IfRunning @('ScheduleReader')
        $stage = Join-Path $dest '_stage'
        if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
        Ensure-Dir $stage

        Invoke-DotnetPublish -Project $csproj -OutputDir $stage -ExtraProps @{
            IncludeNativeLibrariesForSelfExtract = 'true'
        }

        $exe = Join-Path $stage 'ScheduleReader.exe'
        if (-not (Test-Path $exe)) { throw 'ScheduleReader.exe 없음' }

        $portable = Join-Path $dest 'ScheduleReader-Windows-x64'
        if (Test-Path $portable) { Remove-Item $portable -Recurse -Force }
        Ensure-Dir $portable
        Copy-Item $exe (Join-Path $portable 'ScheduleReader.exe') -Force
        Copy-Item $exe (Join-Path $dest 'ScheduleReader.exe') -Force

        $serveBat = @'
@echo off
chcp 65001 >nul
cd /d "%~dp0"
start "" "%~dp0ScheduleReader.exe"
'@
        $utf8 = New-Object System.Text.UTF8Encoding $false
        [System.IO.File]::WriteAllText((Join-Path $portable 'serve.bat'), $serveBat, $utf8)
        [System.IO.File]::WriteAllText((Join-Path $dest 'serve.bat'), $serveBat, $utf8)

        $readmeDeploy = @'
ScheduleReader 배포 패키지
==========================

1. 이 폴더를 대상 PC에 복사
2. ScheduleReader.exe (또는 serve.bat) 실행
3. 브라우저: http://127.0.0.1:17823

Python 설치가 필요 없습니다.
교회 월간 일정 엑셀(.xlsx) → schedule-data.json
'@
        [System.IO.File]::WriteAllText((Join-Path $portable 'README-DEPLOY.txt'), $readmeDeploy, $utf8)

        Remove-Item $stage -Recurse -Force

        if (-not $SkipZip) {
            $zipPath = Join-Path $dest "ScheduleReader-Windows-x64-$Stamp.zip"
            New-ZipFromFolder $portable $zipPath | Out-Null
        }
        Write-Host "완료: $portable\ScheduleReader.exe"
    }

    if ($WantMac) {
        $macFolders = New-MacPortableFolder -Project $csproj -DestRoot $dest `
            -FolderPrefix 'ScheduleReader' -BinaryName 'ScheduleReader'
        if (-not $SkipZip) {
            foreach ($folder in $macFolders) {
                $leaf = Split-Path $folder -Leaf
                New-ZipFromFolder $folder (Join-Path $dest "$leaf-$Stamp.zip") | Out-Null
            }
        }
    }

    Add-Result -Name 'ScheduleReader' -Status 'ok' -Portable $portable -Zip $zipPath -Git $git -Extra @{
        exe = $(if ($portable) { Join-Path $portable 'ScheduleReader.exe' } else { $null })
    }
}

# --- Run ---
$suite = Resolve-SuiteVersion
Write-Host "방송실 프로그램 일괄 빌드" -ForegroundColor Green
Write-Host "출력: $OutRoot"
Write-Host "설정: $(if (Test-Path $BuildConfigPath) { $BuildConfigPath } else { '(이번 빌드에서 기록)' })"
Write-Host "앱: $($Apps -join ', ')"
Write-Host "대상: Windows=$WantWindows  Mac=$WantMac"
Write-Host ("스위트 버전: {0}  build={1}  label={2}" -f $suite.version, $suite.build, $SuiteLabel)
Write-Host "날짜 스탬프: $Stamp"

foreach ($app in $Apps) {
    try {
        switch ($app) {
            'BroadcastNasBridge' { Build-BroadcastNasBridge }
            'CtrlOne' { Build-CtrlOne }
            'FileChecker' { Build-FileChecker }
            'ScheduleDataManager' { Build-ScheduleDataManager }
            'ScheduleReader' { Build-ScheduleReader }
            'WorkLog' { Build-WorkLog }
        }
    }
    catch {
        Write-Host "실패: $app — $($_.Exception.Message)" -ForegroundColor Red
        Add-Result -Name $app -Status 'failed' -ErrorMessage $_.Exception.Message -Git (Get-GitInfo (Join-Path $ProjectsRoot $app))
    }
}

$failed = @($results | Where-Object { $_.status -eq 'failed' })
$ok = @($results | Where-Object { $_.status -eq 'ok' })

if (-not $SkipBundle -and $failed.Count -eq 0 -and $ok.Count -gt 0) {
    # 기본 5개 전체 요청이면 통합 zip 필수 생성; 일부만 빌드해도 성공분으로 묶음
    try {
        $BundleZipPath = New-SuiteBundleZip -OkResults $ok -Label $SuiteLabel
        Save-SuiteVersion $SuiteVersionInfo

        # 버전 히스토리
        $histDir = Join-Path $OutRoot 'versions'
        Ensure-Dir $histDir
        $hist = [ordered]@{
            label     = $SuiteLabel
            version   = $SuiteVersionInfo.version
            build     = $SuiteVersionInfo.build
            builtAt   = $BuildStarted.ToString('o')
            bundleZip = $BundleZipPath
            apps      = @($ok | ForEach-Object { $_.name })
        }
        Write-Utf8NoBom (Join-Path $histDir "$SuiteLabel.json") ($hist | ConvertTo-Json -Depth 4)
    }
    catch {
        Write-Host "통합 zip 실패: $($_.Exception.Message)" -ForegroundColor Red
        Add-Result -Name 'BroadcastingApp-Bundle' -Status 'failed' -ErrorMessage $_.Exception.Message
        $failed = @($results | Where-Object { $_.status -eq 'failed' })
    }
}
elseif ($SkipBundle) {
    Write-Host "통합 zip 생략 (-SkipBundle)"
    Save-SuiteVersion $SuiteVersionInfo
}
elseif ($failed.Count -gt 0) {
    Write-Host "개별 빌드 실패가 있어 통합 zip을 만들지 않습니다." -ForegroundColor Yellow
}

$manifest = [ordered]@{
    builtAt    = $BuildStarted.ToString('o')
    finishedAt = (Get-Date).ToString('o')
    stamp      = $Stamp
    stampTime  = $StampTime
    outRoot    = $OutRoot
    configFile = $(if (Test-Path $BuildConfigPath) { $BuildConfigPath } else { $null })
    targets    = (Resolve-BuildTarget $Target)
    wantWindows = $WantWindows
    wantMac    = $WantMac
    machine    = $env:COMPUTERNAME
    suite      = [ordered]@{
        name    = $SuiteVersionInfo.name
        version = $SuiteVersionInfo.version
        build   = $SuiteVersionInfo.build
        label   = $SuiteLabel
    }
    bundleZip  = $BundleZipPath
    apps       = $results
}
$manifestPath = Join-Path $OutRoot 'manifest.json'
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 6), $utf8NoBom)

$infoLines = @(
    "# 방송실 프로그램 빌드 정보",
    "",
    "- 빌드 시각: $($BuildStarted.ToString('yyyy-MM-dd HH:mm:ss'))",
    "- 스위트 버전: $($SuiteVersionInfo.version) (build $($SuiteVersionInfo.build))",
    "- 라벨: $SuiteLabel",
    "- 스탬프: $Stamp",
    "- 출력: $OutRoot",
    "- 통합 zip: $(if ($BundleZipPath) { $BundleZipPath } else { '(없음)' })",
    "",
    "| 앱 | 상태 | portable | zip |",
    "|----|------|----------|-----|"
)
foreach ($r in $results) {
    $infoLines += "| $($r.name) | $($r.status) | $($r.portable) | $($r.zip) |"
}
$infoLines += ""
$infoLines += "상세: manifest.json"
[System.IO.File]::WriteAllText((Join-Path $OutRoot 'BUILD-INFO.md'), ($infoLines -join "`r`n"), $utf8NoBom)

# latest 포인터
if ($BundleZipPath) {
    Write-Utf8NoBom (Join-Path $OutRoot 'LATEST.txt') @"
label=$SuiteLabel
version=$($SuiteVersionInfo.version)
build=$($SuiteVersionInfo.build)
bundleZip=$BundleZipPath
builtAt=$($BuildStarted.ToString('o'))
"@.Trim()
}

Write-Step '요약'
Show-Manifest

try {
    Update-BuildVerifyDoc -BuildResults $results -Label $SuiteLabel -SuiteInfo $SuiteVersionInfo -ZipPath $BundleZipPath -BuiltAt $BuildStarted
}
catch {
    Write-Host "BUILD-VERIFY.md 갱신 실패: $($_.Exception.Message)" -ForegroundColor Yellow
}

$failed = @($results | Where-Object { $_.status -eq 'failed' })
if ($failed.Count -gt 0) {
    Write-Host ""
    Write-Host "$($failed.Count)개 항목 실패." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "모든 요청 앱 빌드 완료." -ForegroundColor Green
Write-Host "manifest: $manifestPath"
if ($BundleZipPath) {
    Write-Host "통합 배포: $BundleZipPath" -ForegroundColor Green
}

Write-Host ""
Write-Host "사용법 (#63)" -ForegroundColor Cyan
Write-Host "  1) zip 압축 해제 후 Install-BroadcastApps.bat (Windows)"
Write-Host "  2) 또는 Windows\BroadcastNasBridge-Windows-x64\Start-BroadcastNasBridge.bat"
Write-Host "  3) 브라우저 http://127.0.0.1:17820"
Write-Host "  Mac: Mac\<arm64|x64>\BroadcastNasBridge-macOS-*\Launch-BroadcastNasBridge.command"
Write-Host "  레거시 SDM/WL/FC: Windows\Legacy\ (IncludeLegacy=$IncludeLegacy)"
Write-Host "  출력 폴더: $OutRoot"
Write-Host "  확인 체크리스트: docs\BUILD-VERIFY.md (및 Builded\BUILD-VERIFY.md)"

# #111 / #63: 산출 폴더를 탐색기에서 열기
try {
    if (Test-Path -LiteralPath $OutRoot) {
        Start-Process explorer.exe -ArgumentList $OutRoot
        Write-Host "Explorer: $OutRoot"
    }
}
catch {
    Write-Host "Explorer open failed: $($_.Exception.Message)" -ForegroundColor Yellow
}

exit 0
