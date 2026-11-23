#Requires -Version 5.1
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$stamp = Get-Date -Format "yyyyMMdd"
$suiteRoot = Split-Path $PSScriptRoot -Parent
$destRoot = if ($env:WORKLOG_BUILD_DIR) { $env:WORKLOG_BUILD_DIR } else { Join-Path $suiteRoot "Builded\WorkLog" }
$build = Join-Path $destRoot "_build"
$project = Join-Path $PSScriptRoot "MacBridge\MacBridge.csproj"
$plistTemplate = Join-Path $PSScriptRoot "MacBridge\Info.plist"

if (-not (Test-Path $plistTemplate)) { throw "Info.plist missing" }
New-Item -ItemType Directory -Path $destRoot -Force | Out-Null
if (Test-Path $build) { Remove-Item $build -Recurse -Force }
New-Item -ItemType Directory -Path $build | Out-Null

function Write-Utf8NoBom([string]$FilePath, [string]$Content) {
  $utf8 = New-Object System.Text.UTF8Encoding $false
  [System.IO.File]::WriteAllText($FilePath, $Content.Replace("`r`n", "`n"), $utf8)
}

function New-MacPortable {
  param([string]$Rid, [string]$Label)

  $outDir = Join-Path $build "MacBridge-$Rid"
  Write-Host "[$Label] Publishing self-contained $Rid (WorkLog MacBridge)..."
  dotnet publish $project `
    -c Release -r $Rid --self-contained true `
    -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
    -o $outDir
  if ($LASTEXITCODE -ne 0) { throw "Publish failed for $Rid" }

  $bin = Join-Path $outDir "WorkLog"
  if (-not (Test-Path $bin)) { throw "WorkLog binary not found for $Rid" }

  # 형제 앱과 같은 폴더형 포터블 (#61)
  $portable = Join-Path $destRoot "WorkLog-macOS-$Label"
  if (Test-Path $portable) { Remove-Item $portable -Recurse -Force }
  New-Item -ItemType Directory -Path $portable -Force | Out-Null
  Copy-Item $bin (Join-Path $portable "WorkLog") -Force
  Get-ChildItem $outDir -Force | Where-Object { $_.Name -ne "WorkLog" } | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $portable $_.Name) -Recurse -Force
  }
  Write-Utf8NoBom (Join-Path $portable "Launch-WorkLog.command") @"
#!/bin/bash
cd "`$(dirname "`$0")" || exit 1
BIN="./WorkLog"
chmod +x "`$BIN" 2>/dev/null || true
if ! pgrep -xq "WorkLog" >/dev/null 2>&1; then
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
  Write-Utf8NoBom (Join-Path $portable "Start-WorkLog.command") @"
#!/bin/bash
cd "`$(dirname "`$0")" || exit 1
BIN="./WorkLog"
chmod +x "`$BIN" 2>/dev/null || true
if ! pgrep -xq "WorkLog" >/dev/null 2>&1; then
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
  try {
    & chmod +x (Join-Path $portable "Launch-WorkLog.command") (Join-Path $portable "Start-WorkLog.command") (Join-Path $portable "WorkLog") 2>$null
  } catch { }
  Write-Host "  folder: $portable"

  # 전환기: .app 도 유지 (Legacy / Finder 더블클릭)
  $appDir = Join-Path $destRoot "WorkLog-macOS-$Label.app"
  if (Test-Path $appDir) { Remove-Item $appDir -Recurse -Force }
  $macOsDir = Join-Path $appDir "Contents\MacOS"
  New-Item -ItemType Directory -Path $macOsDir -Force | Out-Null
  Copy-Item $bin (Join-Path $macOsDir "WorkLog")
  Copy-Item $plistTemplate (Join-Path $appDir "Contents\Info.plist")
  Write-Host "  app: $appDir"

  $zipPath = Join-Path $destRoot "WorkLog-macOS-$Label-$stamp.zip"
  if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
  Compress-Archive -Path $portable -DestinationPath $zipPath -Force
  Write-Host "Created $zipPath"
}

New-MacPortable -Rid "osx-arm64" -Label "arm64"
New-MacPortable -Rid "osx-x64" -Label "x64"
Remove-Item $build -Recurse -Force

Write-Host ""
Write-Host "Done under $destRoot"
Write-Host "권장: WorkLog-macOS-{arm64|x64}/Launch-WorkLog.command (브리지 켜져 있으면 /worklog 로 유도)"
Write-Host "레거시 .app 도 동일 폴더에 생성됨."
