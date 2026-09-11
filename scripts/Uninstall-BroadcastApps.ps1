#Requires -Version 5.1
<#
.SYNOPSIS
  방송실 프로그램 제거 (Windows): 프로세스 종료 → 설치 폴더·바탕화면 바로가기 삭제.
.DESCRIPTION
  통합 zip 루트 또는 설치 폴더에 둡니다. 기본 설치 위치: %LOCALAPPDATA%\BroadcastingApp
#>
[CmdletBinding()]
param(
    [string]$InstallDir = ''
)

try {
    $utf8 = New-Object System.Text.UTF8Encoding $false
    if (Get-Command chcp -ErrorAction SilentlyContinue) { chcp 65001 | Out-Null }
    [Console]::InputEncoding = $utf8
    [Console]::OutputEncoding = $utf8
}
catch { }

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$stopPs1 = Join-Path $here 'Stop-BroadcastApps.ps1'
if (Test-Path $stopPs1) {
    & $stopPs1
}
else {
    Write-Host "Stop-BroadcastApps.ps1 없음 — 프로세스 종료를 건너뜁니다." -ForegroundColor Yellow
}

function Select-UninstallFolder([string]$ScriptDir, [string]$DefaultPath) {
    Write-Host ""
    Write-Host "방송실 프로그램 제거"
    Write-Host "===================="
    Write-Host "  [1] 이 삭제 프로그램이 있는 위치 (설치 폴더): $ScriptDir"
    Write-Host "  [2] 기본 설치 위치: $DefaultPath"
    Write-Host "  [3] 다른 폴더 선택"
    $choice = Read-Host "번호 (Enter=1)"
    if ([string]::IsNullOrWhiteSpace($choice) -or $choice -eq '1') {
        return $ScriptDir
    }
    if ($choice -eq '2') {
        return $DefaultPath
    }
    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
        [System.Windows.Forms.Application]::EnableVisualStyles()
        $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
        $dlg.Description = '삭제할 설치 폴더를 선택하세요'
        $dlg.ShowNewFolderButton = $false
        $start = $ScriptDir
        if (-not (Test-Path $start)) { $start = $DefaultPath }
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
        $typed = Read-Host "설치 폴더 경로"
        if (-not [string]::IsNullOrWhiteSpace($typed)) {
            return $typed.Trim().Trim('"')
        }
    }
    Write-Host "폴더 선택을 취소했습니다. 이 삭제 프로그램 위치를 사용합니다."
    return $ScriptDir
}

$defaultDir = Join-Path $env:LOCALAPPDATA 'BroadcastingApp'
if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    $InstallDir = Select-UninstallFolder $here $defaultDir
}
else {
    $InstallDir = $InstallDir.Trim().Trim('"')
}

Write-Host ""
Write-Host "삭제 대상 설치 폴더: $InstallDir"
$confirm = Read-Host "정말 삭제할까요? (Y/N)"
if ($confirm -notmatch '^[Yy]') {
    Write-Host "취소했습니다."
    exit 0
}

if (Test-Path -LiteralPath $InstallDir) {
    # 자기 자신이 설치 폴더 안에 있으면 먼저 다른 곳으로 안내할 수 없으므로, 가능한 파일만 삭제
    try {
        Remove-Item -LiteralPath $InstallDir -Recurse -Force -ErrorAction Stop
        Write-Host "설치 폴더 삭제: $InstallDir" -ForegroundColor Green
    }
    catch {
        Write-Host "설치 폴더 일부 삭제 실패: $($_.Exception.Message)" -ForegroundColor Yellow
        Write-Host "실행 중인 파일이나 이 창이 설치 폴더에 있으면 닫은 뒤 수동 삭제하세요."
    }
}
else {
    Write-Host "설치 폴더 없음: $InstallDir" -ForegroundColor Yellow
}

$desktop = [Environment]::GetFolderPath('Desktop')
$shortcutDir = Join-Path $desktop '방송실 프로그램'
if (Test-Path -LiteralPath $shortcutDir) {
    try {
        Remove-Item -LiteralPath $shortcutDir -Recurse -Force
        Write-Host "바로가기 폴더 삭제: $shortcutDir" -ForegroundColor Green
    }
    catch {
        Write-Host "바로가기 삭제 실패: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}
else {
    Write-Host "바탕화면 바로가기 폴더 없음."
}

Write-Host ""
Write-Host "제거 작업이 끝났습니다."
Read-Host "Enter 키를 누르면 종료"
