#Requires -Version 5.1
<#
.SYNOPSIS
  BroadcastingApp 통합 패키지 설치 (Windows 폴더만 복사).
.DESCRIPTION
  이 스크립트는 통합 zip 루트(Windows / Mac / VERSION.txt 옆)에 둡니다.
  Windows\* 내용을 선택한 설치 폴더로 복사하고, 선택 시 바탕화면 바로가기를 만듭니다.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = ''
)

$ErrorActionPreference = 'Stop'
$BundleRoot = $PSScriptRoot
$WindowsSrc = Join-Path $BundleRoot 'Windows'

if (-not (Test-Path $WindowsSrc)) {
    Write-Host "Windows 폴더를 찾을 수 없습니다: $WindowsSrc" -ForegroundColor Red
    Write-Host "이 스크립트는 통합 패키지 루트(BroadcastingApp_...)에서 실행해야 합니다."
    exit 1
}

$defaultDir = Join-Path $env:LOCALAPPDATA 'BroadcastingApp'
if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    Write-Host ""
    Write-Host "방송실 프로그램 설치"
    Write-Host "===================="
    Write-Host "기본 설치 위치: $defaultDir"
    $inputDir = Read-Host "설치 폴더 (Enter = 기본값)"
    if ([string]::IsNullOrWhiteSpace($inputDir)) {
        $InstallDir = $defaultDir
    }
    else {
        $InstallDir = $inputDir.Trim().Trim('"')
    }
}

Write-Host ""
Write-Host "설치 위치: $InstallDir"
Write-Host "원본:      $WindowsSrc"

if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

Write-Host "복사 중..."
Copy-Item -Path (Join-Path $WindowsSrc '*') -Destination $InstallDir -Recurse -Force

$versionSrc = Join-Path $BundleRoot 'VERSION.txt'
if (Test-Path $versionSrc) {
    Copy-Item $versionSrc (Join-Path $InstallDir 'VERSION.txt') -Force
}

Write-Host "복사 완료." -ForegroundColor Green

$shortcutAnswer = Read-Host "바탕화면에 바로가기를 만들까요? (Y/N)"
if ($shortcutAnswer -match '^[Yy]') {
    $desktop = [Environment]::GetFolderPath('Desktop')
    $wsh = New-Object -ComObject WScript.Shell

    $targets = @(
        @{ Name = 'CtrlOne'; Rel = 'CtrlOne-Windows-x64\CtrlOne.exe' },
        @{ Name = 'FileChecker'; Rel = 'FileChecker-Windows-x64\FileCheckerFinder.exe' },
        @{ Name = 'BroadcastingSchedule'; Rel = 'BroadcastingSchedule-Windows-x64\BroadcastingSchedule.exe' },
        @{ Name = 'WorkLog'; Rel = 'WorkLog-Windows-x64\WorkLog.exe' },
        @{ Name = 'ScheduleReader'; Rel = 'ScheduleReader-portable\Setup-And-Run.bat' }
    )

    foreach ($t in $targets) {
        $exePath = Join-Path $InstallDir $t.Rel
        if (-not (Test-Path $exePath)) {
            Write-Host "  건너뜀 (없음): $($t.Rel)" -ForegroundColor Yellow
            continue
        }
        $lnkPath = Join-Path $desktop ("BroadcastingApp - $($t.Name).lnk")
        $sc = $wsh.CreateShortcut($lnkPath)
        $sc.TargetPath = $exePath
        $sc.WorkingDirectory = Split-Path $exePath -Parent
        $sc.Description = "BroadcastingApp - $($t.Name)"
        $sc.Save()
        Write-Host "  바로가기: $lnkPath"
    }
}
else {
    Write-Host "바로가기 생략."
}

Write-Host ""
Write-Host "설치가 끝났습니다. 각 앱 폴더의 실행 파일을 사용하세요." -ForegroundColor Green
Write-Host "  $InstallDir"
Write-Host ""
Read-Host "Enter 키를 누르면 종료"
