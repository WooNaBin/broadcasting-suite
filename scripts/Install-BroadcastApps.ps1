#Requires -Version 5.1
<#
.SYNOPSIS
  BroadcastingApp 통합 패키지 설치 (Windows 폴더만 복사).
.DESCRIPTION
  이 스크립트는 통합 zip 루트(Windows / Mac / VERSION.txt 옆)에 둡니다.
  Windows\* (권장 + Windows\Legacy)를 설치 폴더로 복사하고, 선택 시 바탕화면\방송실 프로그램 폴더에 바로가기를 만듭니다.
  스케줄·일지·파일체크는 BroadcastNasBridge(17820)로 엽니다.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = ''
)

# Windows PowerShell 5.1: 파일은 UTF-8 BOM. 콘솔 UTF-8로 고정해 한글 깨짐 완화.
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
$BundleRoot = $PSScriptRoot
$WindowsSrc = Join-Path $BundleRoot 'Windows'

if (-not (Test-Path $WindowsSrc)) {
    Write-Host "Windows 폴더를 찾을 수 없습니다: $WindowsSrc" -ForegroundColor Red
    Write-Host "이 스크립트는 통합 패키지 루트(BroadcastingApp_...)에서 실행해야 합니다."
    exit 1
}

function Select-InstallFolder([string]$DefaultPath) {
    Write-Host ""
    Write-Host "방송실 프로그램 설치"
    Write-Host "===================="
    Write-Host "기본 설치 위치: $DefaultPath"
    Write-Host "폴더 선택 창이 열립니다. 취소하면 기본 위치에 설치합니다."
    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
        [System.Windows.Forms.Application]::EnableVisualStyles()
        $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
        $dlg.Description = '방송실 프로그램을 설치할 폴더를 선택하세요'
        $dlg.ShowNewFolderButton = $true
        $start = $DefaultPath
        if (-not (Test-Path $start)) {
            $parent = Split-Path $start -Parent
            if (Test-Path $parent) { $start = $parent }
        }
        if (Test-Path $start) { $dlg.SelectedPath = $start }

        $owner = New-Object System.Windows.Forms.Form
        $owner.TopMost = $true
        $owner.ShowInTaskbar = $false
        $owner.WindowState = 'Minimized'
        $result = $dlg.ShowDialog($owner)
        $owner.Dispose()

        if ($result -eq [System.Windows.Forms.DialogResult]::OK -and -not [string]::IsNullOrWhiteSpace($dlg.SelectedPath)) {
            return $dlg.SelectedPath
        }
    }
    catch {
        Write-Host "폴더 창을 열 수 없습니다: $($_.Exception.Message)" -ForegroundColor Yellow
        Write-Host "기본 위치에 설치합니다."
    }
    Write-Host "기본 위치로 설치합니다."
    return $DefaultPath
}

$defaultDir = Join-Path $env:LOCALAPPDATA 'BroadcastingApp'
if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    $InstallDir = Select-InstallFolder $defaultDir
}
else {
    $InstallDir = $InstallDir.Trim().Trim('"')
}

Write-Host ""
Write-Host "설치 위치: $InstallDir"
Write-Host "원본:      $WindowsSrc"

$stopPs1 = Join-Path $BundleRoot 'Stop-BroadcastApps.ps1'
Write-Host ""
Write-Host "실행 중인 방송실 프로그램을 종료합니다…"
if (Test-Path -LiteralPath $stopPs1) {
    & $stopPs1
}
else {
    $names = @(
        'BroadcastNasBridge', 'CtrlOne', 'WorkLog',
        'BroadcastingSchedule', 'ScheduleReader', 'FileCheckerFinder'
    )
    foreach ($name in $names) {
        Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
            Write-Host ("  종료: {0} (PID {1})" -f $_.ProcessName, $_.Id)
            Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
        }
    }
}
Start-Sleep -Milliseconds 700

if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

Write-Host "복사 중..."
Copy-Item -Path (Join-Path $WindowsSrc '*') -Destination $InstallDir -Recurse -Force

$versionSrc = Join-Path $BundleRoot 'VERSION.txt'
if (Test-Path $versionSrc) {
    Copy-Item $versionSrc (Join-Path $InstallDir 'VERSION.txt') -Force
}

foreach ($helper in @(
        'Stop-BroadcastApps.ps1', 'Stop-BroadcastApps.bat',
        'Uninstall-BroadcastApps.ps1', 'Uninstall-BroadcastApps.bat'
    )) {
    $hs = Join-Path $BundleRoot $helper
    if (Test-Path $hs) {
        Copy-Item $hs (Join-Path $InstallDir $helper) -Force
    }
}

Write-Host "복사 완료." -ForegroundColor Green

$shortcutAnswer = Read-Host "바탕화면에 바로가기를 만들까요? (Y/N)"
if ($shortcutAnswer -match '^[Yy]') {
    $desktop = [Environment]::GetFolderPath('Desktop')
    $shortcutDir = Join-Path $desktop '방송실 프로그램'
    New-Item -ItemType Directory -Path $shortcutDir -Force | Out-Null
    $wsh = New-Object -ComObject WScript.Shell

    $bridgeExe = Join-Path $InstallDir 'BroadcastNasBridge-Windows-x64\BroadcastNasBridge.exe'
    $targets = @(
        @{
            Name = '방송실 프로그램 시작'
            Target = $bridgeExe
            Args = ''
            WorkDir = (Join-Path $InstallDir 'BroadcastNasBridge-Windows-x64')
            Icon = $bridgeExe
        },
        @{
            Name = '레코더 컨트롤러'
            Target = (Join-Path $InstallDir 'CtrlOne-Windows-x64\CtrlOne.exe')
            Args = ''
            WorkDir = (Join-Path $InstallDir 'CtrlOne-Windows-x64')
            Icon = (Join-Path $InstallDir 'CtrlOne-Windows-x64\CtrlOne.exe')
        },
        @{
            Name = '렌더링 파일 확인'
            Target = $bridgeExe
            Args = '/files/'
            WorkDir = (Join-Path $InstallDir 'BroadcastNasBridge-Windows-x64')
            Icon = $(
                $fc = Join-Path $InstallDir 'Legacy\FileChecker-Windows-x64\FileCheckerFinder.exe'
                if (-not (Test-Path $fc)) { $fc = Join-Path $InstallDir 'FileChecker-Windows-x64\FileCheckerFinder.exe' }
                if (Test-Path $fc) { $fc } else { $bridgeExe }
            )
        },
        @{
            Name = '방송실 일정'
            Target = $bridgeExe
            Args = '/schedule/'
            WorkDir = (Join-Path $InstallDir 'BroadcastNasBridge-Windows-x64')
            Icon = $(
                $sdm = Join-Path $InstallDir 'Legacy\BroadcastingSchedule-Windows-x64\BroadcastingSchedule.exe'
                if (-not (Test-Path $sdm)) { $sdm = Join-Path $InstallDir 'BroadcastingSchedule-Windows-x64\BroadcastingSchedule.exe' }
                if (Test-Path $sdm) { $sdm } else { $bridgeExe }
            )
        },
        @{
            Name = '방송실 작업일지'
            Target = $bridgeExe
            Args = '/worklog/'
            WorkDir = (Join-Path $InstallDir 'BroadcastNasBridge-Windows-x64')
            Icon = $(
                $wl = Join-Path $InstallDir 'Legacy\WorkLog-Windows-x64\WorkLog.exe'
                if (-not (Test-Path $wl)) { $wl = Join-Path $InstallDir 'WorkLog-Windows-x64\WorkLog.exe' }
                if (Test-Path $wl) { $wl } else { $bridgeExe }
            )
        },
        @{
            Name = '스케쥴 생성 유틸'
            Target = (Join-Path $InstallDir 'ScheduleReader-Windows-x64\ScheduleReader.exe')
            Args = ''
            WorkDir = (Join-Path $InstallDir 'ScheduleReader-Windows-x64')
            Icon = $(
                $exe = Join-Path $InstallDir 'ScheduleReader-Windows-x64\ScheduleReader.exe'
                if (Test-Path $exe) { $exe }
                else {
                    $legacy = Join-Path $InstallDir 'ScheduleReader-portable\Setup-And-Run.bat'
                    if (Test-Path $legacy) { $legacy } else { $null }
                }
            )
        }
    )

    foreach ($t in $targets) {
        if (-not $t.Target -or -not (Test-Path $t.Target)) {
            Write-Host "  건너뜀 (없음): $($t.Name)" -ForegroundColor Yellow
            continue
        }
        $lnkPath = Join-Path $shortcutDir ("$($t.Name).lnk")
        $sc = $wsh.CreateShortcut($lnkPath)
        $sc.TargetPath = $t.Target
        $sc.Arguments = $t.Args
        $sc.WorkingDirectory = $t.WorkDir
        $sc.Description = $t.Name
        if ($t.Icon -and (Test-Path $t.Icon)) {
            $sc.IconLocation = "$($t.Icon),0"
        }
        $sc.Save()
        Write-Host "  바로가기: $lnkPath"
    }
}
else {
    Write-Host "바로가기 생략."
}

Write-Host ""
Write-Host "설치가 끝났습니다." -ForegroundColor Green
Write-Host "  $InstallDir"
Write-Host "스케줄·일지·파일체크는 「방송실 프로그램 시작」 또는 각 바로가기(브리지)로 실행하세요."
Write-Host ""
Read-Host "Enter 키를 누르면 종료"
