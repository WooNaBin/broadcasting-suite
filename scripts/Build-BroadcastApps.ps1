#Requires -Version 5.1
<#
.SYNOPSIS
  방송실 프로그램 최신 Windows 배포본을 D:\Projects\Builded 에 일괄 생성합니다.

.DESCRIPTION
  대상: CtrlOne, FileChecker, ScheduleDataManager, ScheduleReader, WorkLog
  - .NET 앱: self-contained win-x64 단일 실행 파일
  - ScheduleReader: Python 휴대 패키지(venv는 대상 PC에서 Setup)
  - 스위트 버전: broadcast-suite.version.json (빌드마다 build 번호 증가)
  - 개별 portable 폴더 + 개별 zip + 통합 zip:
      Builded\BroadcastingApp_<version>_<yyyyMMdd>.zip

.EXAMPLE
  .\scripts\Build-BroadcastApps.ps1
  .\scripts\Build-BroadcastApps.ps1 -Apps CtrlOne,WorkLog
  .\scripts\Build-BroadcastApps.ps1 -SkipZip
  .\scripts\Build-BroadcastApps.ps1 -SkipBundle
  .\scripts\Build-BroadcastApps.ps1 -Bump Minor
  .\scripts\Build-BroadcastApps.ps1 -Version 1.2.0
  .\scripts\Build-BroadcastApps.ps1 -List
#>
[CmdletBinding()]
param(
    [ValidateSet('CtrlOne', 'FileChecker', 'ScheduleDataManager', 'ScheduleReader', 'WorkLog')]
    [string[]]$Apps = @(
        'CtrlOne',
        'FileChecker',
        'ScheduleDataManager',
        'ScheduleReader',
        'WorkLog'
    ),
    [string]$OutRoot = '',
    [string]$Version = '',
    [ValidateSet('None', 'Build', 'Patch', 'Minor', 'Major')]
    [string]$Bump = 'Build',
    [switch]$SkipZip,
    [switch]$SkipBundle,
    [switch]$List
)

$ErrorActionPreference = 'Stop'
$ProjectsRoot = Split-Path $PSScriptRoot -Parent
if (-not $OutRoot) {
    $OutRoot = if ($env:BROADCAST_BUILD_DIR) { $env:BROADCAST_BUILD_DIR } else { Join-Path $ProjectsRoot 'Builded' }
}

$VersionFile = Join-Path $ProjectsRoot 'broadcast-suite.version.json'
$Stamp = Get-Date -Format 'yyyyMMdd'
$StampTime = Get-Date -Format 'yyyyMMdd-HHmm'
$BuildStarted = Get-Date
$AllSuiteApps = @('CtrlOne', 'FileChecker', 'ScheduleDataManager', 'ScheduleReader', 'WorkLog')
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

function Read-SuiteVersion {
    if (-not (Test-Path $VersionFile)) {
        return [pscustomobject]@{
            name        = 'BroadcastingApp'
            version     = '1.0.0'
            build       = 0
            description = '방송실 프로그램 스위트'
        }
    }
    return ([System.IO.File]::ReadAllText($VersionFile) | ConvertFrom-Json)
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
    Ensure-Dir $windowsDir
    Ensure-Dir $macDir

    foreach ($r in $OkResults) {
        if (-not $r.portable -or -not (Test-Path $r.portable)) {
            throw "portable 폴더 없음: $($r.name) → $($r.portable)"
        }
        $leaf = Split-Path $r.portable -Leaf
        Write-Host "  + Windows\$leaf"
        Copy-Item $r.portable (Join-Path $windowsDir $leaf) -Recurse -Force
    }

    # 향후 macOS 산출물이 있으면 Mac\ 로 복사 (현재는 빈 폴더 유지)
    $macMarker = Join-Path $macDir 'README.txt'
    Write-Utf8NoBom $macMarker @"
macOS 배포본은 아직 포함되지 않았습니다.
추후 Mac 빌드가 추가되면 이 폴더에 앱 폴더가 들어갑니다.
"@.TrimEnd()

    $versionTxt = @"
BroadcastingApp suite
version: $($SuiteVersionInfo.version)
build: $($SuiteVersionInfo.build)
label: $Label
builtAt: $($BuildStarted.ToString('yyyy-MM-dd HH:mm:ss'))
stamp: $Stamp

Apps:
$($OkResults | ForEach-Object { "- $($_.name)" } | Out-String)
"@
    Write-Utf8NoBom (Join-Path $stage 'VERSION.txt') $versionTxt.TrimEnd()

    $readme = @"
방송실 프로그램 통합 배포 패키지
================================

폴더 구성
---------
- Windows\   … Windows x64 포터블 앱
- Mac\       … macOS용 (추후)
- Install-BroadcastApps.bat / .ps1  … Windows 설치 도우미
- VERSION.txt

설치 (권장)
-----------
1. zip 압축 해제
2. Install-BroadcastApps.bat 실행
3. 설치 폴더 선택 (기본: %LOCALAPPDATA%\BroadcastingApp)
4. Windows\ 내용만 복사됨. 바탕화면 바로가기는 선택

수동 실행
---------
- Windows\CtrlOne-Windows-x64\CtrlOne.exe
- Windows\FileChecker-Windows-x64\Start-FileCheckerFinder.bat
- Windows\BroadcastingSchedule-Windows-x64\BroadcastingSchedule.exe
- Windows\WorkLog-Windows-x64\WorkLog.exe
- Windows\ScheduleReader-portable\Setup-And-Run.bat (최초) / serve.bat

포트: Schedule 17821 / WorkLog 17822 / ScheduleReader 17823 / CtrlOne 5177 / FileChecker 5187
버전 정보: VERSION.txt
"@
    Write-Utf8NoBom (Join-Path $stage 'README.txt') $readme.TrimEnd()

    $installerPs1 = Join-Path $PSScriptRoot 'Install-BroadcastApps.ps1'
    $installerBat = Join-Path $PSScriptRoot 'Install-BroadcastApps.bat'
    if (-not (Test-Path $installerPs1)) { throw "설치 스크립트 없음: $installerPs1" }
    if (-not (Test-Path $installerBat)) { throw "설치 스크립트 없음: $installerBat" }
    Copy-Item $installerPs1 (Join-Path $stage 'Install-BroadcastApps.ps1') -Force
    Copy-Item $installerBat (Join-Path $stage 'Install-BroadcastApps.bat') -Force
    Write-Host "  + Install-BroadcastApps.ps1 / .bat"

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
        [hashtable]$ExtraProps = @{}
    )
    Ensure-Dir $OutputDir
    $args = @(
        'publish', $Project,
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        '-o', $OutputDir
    )
    foreach ($key in $ExtraProps.Keys) {
        $args += "-p:$key=$($ExtraProps[$key])"
    }
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 실패: $Project" }
}

function Show-Manifest {
    $manifestPath = Join-Path $OutRoot 'manifest.json'
    if (-not (Test-Path $manifestPath)) {
        Write-Host "manifest.json 없음: $manifestPath"
        return
    }
    $raw = [System.IO.File]::ReadAllText($manifestPath)
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

if ($List) {
    if (Test-Path $VersionFile) {
        $v = Read-SuiteVersion
        Write-Host ("현재 스위트 버전 파일: {0}.{1}" -f $v.version, $v.build)
        if ($v.lastLabel) { Write-Host "마지막 라벨: $($v.lastLabel)" }
        Write-Host ""
    }
    Show-Manifest
    return
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

# --- CtrlOne ---
function Build-CtrlOne {
    Write-Step 'CtrlOne'
    $repo = Join-Path $ProjectsRoot 'CtrlOne'
    $git = Get-GitInfo $repo
    Stop-IfRunning @('CtrlOne')

    $dest = Join-Path $OutRoot 'CtrlOne'
    $stage = Join-Path $dest '_stage'
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    Ensure-Dir $stage

    Invoke-DotnetPublish -Project (Join-Path $repo 'CtrlOne.csproj') -OutputDir $stage

    $exe = Join-Path $stage 'CtrlOne.exe'
    if (-not (Test-Path $exe)) { throw 'CtrlOne.exe 없음' }

    # 최신 실행 폴더 (설정 json은 유지)
    $keep = @('devices.json', 'presets.json')
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

    $zipPath = $null
    if (-not $SkipZip) {
        $zipPath = Join-Path $dest "CtrlOne-Windows-x64-$Stamp.zip"
        New-ZipFromFolder $portable $zipPath | Out-Null
    }

    Write-Host "완료: $dest\CtrlOne.exe"
    Add-Result -Name 'CtrlOne' -Status 'ok' -Portable $portable -Zip $zipPath -Git $git -Extra @{ exe = (Join-Path $dest 'CtrlOne.exe') }
}

# --- FileChecker ---
function Build-FileChecker {
    Write-Step 'FileChecker'
    $repo = Join-Path $ProjectsRoot 'FileChecker'
    $git = Get-GitInfo $repo
    Stop-IfRunning @('FileCheckerFinder')

    $dest = Join-Path $OutRoot 'FileChecker'
    $stage = Join-Path $dest '_stage'
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    Ensure-Dir $stage

    Invoke-DotnetPublish -Project (Join-Path $repo 'FileCheckerFinder.csproj') -OutputDir $stage -ExtraProps @{
        IncludeNativeLibrariesForSelfExtract = 'true'
    }

    $exeName = 'FileCheckerFinder.exe'
    $exe = Join-Path $stage $exeName
    if (-not (Test-Path $exe)) { throw "$exeName 없음" }

    # data/ 는 배포 패키지에 넣지 않음 (로컬 설정·비밀번호 보호)
    $portable = Join-Path $dest 'FileChecker-Windows-x64'
    if (Test-Path $portable) { Remove-Item $portable -Recurse -Force }
    Ensure-Dir $portable
    Copy-Item $exe (Join-Path $portable $exeName)
    $batSrc = Join-Path $stage 'Start-FileCheckerFinder.bat'
    if (-not (Test-Path $batSrc)) { $batSrc = Join-Path $repo 'Start-FileCheckerFinder.bat' }
    if (Test-Path $batSrc) { Copy-Item $batSrc (Join-Path $portable 'Start-FileCheckerFinder.bat') }

    # 실행용 최신 폴더: exe/bat만 갱신, data/ 유지
    Ensure-Dir $dest
    Copy-Item $exe (Join-Path $dest $exeName) -Force
    if (Test-Path $batSrc) { Copy-Item $batSrc (Join-Path $dest 'Start-FileCheckerFinder.bat') -Force }
    Remove-Item $stage -Recurse -Force

    $zipPath = $null
    if (-not $SkipZip) {
        $zipPath = Join-Path $dest "FileChecker-Windows-x64-$Stamp.zip"
        New-ZipFromFolder $portable $zipPath | Out-Null
    }

    Write-Host "완료: $dest\$exeName (배포 zip에는 data 제외)"
    Add-Result -Name 'FileChecker' -Status 'ok' -Portable $portable -Zip $zipPath -Git $git -Extra @{ exe = (Join-Path $dest $exeName) }
}

# --- ScheduleDataManager ---
function Build-ScheduleDataManager {
    Write-Step 'ScheduleDataManager'
    $repo = Join-Path $ProjectsRoot 'ScheduleDataManager'
    $git = Get-GitInfo $repo
    Stop-IfRunning @('BroadcastingSchedule')

    $dest = Join-Path $OutRoot 'ScheduleDataManager'
    Ensure-Dir $dest
    $env:SCHEDULE_BUILD_DIR = $dest
    & (Join-Path $repo 'package-portable.ps1')
    if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) { throw 'ScheduleDataManager package-portable 실패' }

    $portable = Join-Path $dest 'BroadcastingSchedule-Windows-x64'
    $zipPath = Join-Path $dest "BroadcastingSchedule-Windows-x64-$Stamp.zip"
    if (-not (Test-Path $portable)) { throw "portable 폴더 없음: $portable" }
    if ($SkipZip -and (Test-Path $zipPath)) {
        # package script may have created zip; leave it
    }
    elseif ($SkipZip) { $zipPath = $null }
    elseif (-not (Test-Path $zipPath)) {
        New-ZipFromFolder $portable $zipPath | Out-Null
    }

    Add-Result -Name 'ScheduleDataManager' -Status 'ok' -Portable $portable -Zip $(if (Test-Path $zipPath) { $zipPath } else { $null }) -Git $git
}

# --- WorkLog ---
function Build-WorkLog {
    Write-Step 'WorkLog'
    $repo = Join-Path $ProjectsRoot 'WorkLog'
    $git = Get-GitInfo $repo
    Stop-IfRunning @('WorkLog')

    $dest = Join-Path $OutRoot 'WorkLog'
    Ensure-Dir $dest
    $env:WORKLOG_BUILD_DIR = $dest
    & (Join-Path $repo 'package-portable.ps1')
    if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) { throw 'WorkLog package-portable 실패' }

    $portable = Join-Path $dest 'WorkLog-Windows-x64'
    $zipPath = Join-Path $dest "WorkLog-Windows-x64-$Stamp.zip"
    if (-not (Test-Path $portable)) { throw "portable 폴더 없음: $portable" }

    Add-Result -Name 'WorkLog' -Status 'ok' -Portable $portable -Zip $(if (Test-Path $zipPath) { $zipPath } else { $null }) -Git $git
}

# --- ScheduleReader (Python portable) ---
function Build-ScheduleReader {
    Write-Step 'ScheduleReader'
    $repo = Join-Path $ProjectsRoot 'ScheduleReader'
    $git = Get-GitInfo $repo

    $dest = Join-Path $OutRoot 'ScheduleReader'
    $portable = Join-Path $dest 'ScheduleReader-portable'
    if (Test-Path $portable) { Remove-Item $portable -Recurse -Force }
    Ensure-Dir $portable

    $copyDirs = @('schedule_reader', 'config')
    foreach ($d in $copyDirs) {
        $src = Join-Path $repo $d
        if (-not (Test-Path $src)) { throw "필수 폴더 없음: $src" }
        Copy-Item $src (Join-Path $portable $d) -Recurse -Force
    }

    Copy-Item (Join-Path $repo 'requirements.txt') (Join-Path $portable 'requirements.txt') -Force
    Copy-Item (Join-Path $repo 'README.md') (Join-Path $portable 'README.md') -Force -ErrorAction SilentlyContinue

    $modelsSrc = Join-Path $repo 'models'
    if (Test-Path $modelsSrc) {
        Write-Host '  models/ 포함 (OCR 모델)'
        Copy-Item $modelsSrc (Join-Path $portable 'models') -Recurse -Force
    }

    Ensure-Dir (Join-Path $portable 'input')
    Ensure-Dir (Join-Path $portable 'output')

    function Write-Utf8File {
        param(
            [Parameter(Mandatory = $true)][string]$FilePath,
            [Parameter(Mandatory = $true)][string]$Content
        )
        $utf8 = New-Object System.Text.UTF8Encoding $false
        [System.IO.File]::WriteAllText($FilePath, $Content, $utf8)
    }

    $serveBat = @'
@echo off
chcp 65001 >nul
cd /d "%~dp0"
if not exist ".venv\Scripts\python.exe" (
  echo [.venv missing] Run Setup-And-Run.bat first.
  pause
  exit /b 1
)
echo Starting ScheduleReader...
echo Open http://127.0.0.1:17823 in your browser.
echo Press Ctrl+C in this window to stop.
".venv\Scripts\python.exe" -m schedule_reader serve
if errorlevel 1 (
  echo.
  echo Failed to start.
  pause
)
'@
    Write-Utf8File -FilePath (Join-Path $portable 'serve.bat') -Content $serveBat

    $setupBat = @'
@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo [ScheduleReader] Preparing venv...

set "PYEXE="
where py >nul 2>&1
if not errorlevel 1 (
  for %%V in (3.14 3.13 3.12 3.11 3) do (
    if not defined PYEXE (
      py -%%V -c "import sys" >nul 2>&1
      if not errorlevel 1 set "PYEXE=py -%%V"
    )
  )
)
if not defined PYEXE (
  where python >nul 2>&1
  if not errorlevel 1 (
    python -c "import sys; raise SystemExit(0 if sys.version_info >= (3,11) else 1)" >nul 2>&1
    if not errorlevel 1 set "PYEXE=python"
  )
)
if not defined PYEXE (
  echo Python 3.11+ 를 찾을 수 없습니다.
  echo Microsoft Store 앱 실행 별칭이 켜져 있으면 끄고, python.org 에서 설치하세요.
  echo 설치 후 "Add python.exe to PATH" 를 체크하거나 py launcher 를 사용하세요.
  pause
  exit /b 1
)

echo Using: %PYEXE%
if not exist ".venv\Scripts\python.exe" (
  %PYEXE% -m venv .venv
  if errorlevel 1 (
    echo Failed to create venv.
    echo Store stub python 이 원인일 수 있습니다. py -3.11 로 다시 시도하세요.
    pause
    exit /b 1
  )
)
echo Installing packages...
".venv\Scripts\python.exe" -m pip install --upgrade pip
".venv\Scripts\python.exe" -m pip install -r requirements.txt
if errorlevel 1 (
  echo pip install failed.
  pause
  exit /b 1
)
echo.
echo Ready. Launching serve.bat...
call "%~dp0serve.bat"
'@
    Write-Utf8File -FilePath (Join-Path $portable 'Setup-And-Run.bat') -Content $setupBat

    $readmeDeploy = @'
ScheduleReader 배포 패키지
==========================

1. 이 폴더를 대상 PC에 복사
2. Python 3.11+ 설치 (python.org 권장, PATH 또는 py launcher)
   - Windows "앱 실행 별칭"의 python.exe 는 끄세요 (Store stub 방지)
3. Setup-And-Run.bat 실행 (최초 1회: venv + pip)
4. 이후: serve.bat
5. 브라우저: http://127.0.0.1:17823

models/ 폴더가 있으면 OCR 모델이 포함됩니다 (용량 큼).
없으면 첫 OCR 실행 시 한글 모델이 다운로드됩니다.
'@
    Write-Utf8File -FilePath (Join-Path $portable 'README-DEPLOY.txt') -Content $readmeDeploy

    $zipPath = $null
    if (-not $SkipZip) {
        $zipPath = Join-Path $dest "ScheduleReader-portable-$Stamp.zip"
        New-ZipFromFolder $portable $zipPath | Out-Null
    }

    Write-Host "완료: $portable"
    Add-Result -Name 'ScheduleReader' -Status 'ok' -Portable $portable -Zip $zipPath -Git $git
}

# --- Run ---
$suite = Resolve-SuiteVersion
Write-Host "방송실 프로그램 일괄 빌드" -ForegroundColor Green
Write-Host "출력: $OutRoot"
Write-Host "앱: $($Apps -join ', ')"
Write-Host ("스위트 버전: {0}  build={1}  label={2}" -f $suite.version, $suite.build, $SuiteLabel)
Write-Host "날짜 스탬프: $Stamp"

foreach ($app in $Apps) {
    try {
        switch ($app) {
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
$ok = @($results | Where-Object { $_.status -eq 'ok' -and $_.portable })

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
exit 0
