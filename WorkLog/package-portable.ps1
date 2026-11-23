#Requires -Version 5.1
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$stamp = Get-Date -Format "yyyyMMdd"
$suiteRoot = Split-Path $PSScriptRoot -Parent
$destRoot = if ($env:WORKLOG_BUILD_DIR) { $env:WORKLOG_BUILD_DIR } else { Join-Path $suiteRoot "Builded\WorkLog" }
$build = Join-Path $destRoot "_build"
$project = Join-Path $PSScriptRoot "LocalBridge\LocalBridge.csproj"

Write-Host "Output: $destRoot"
New-Item -ItemType Directory -Path $destRoot -Force | Out-Null
if (Test-Path $build) { Remove-Item $build -Recurse -Force }
New-Item -ItemType Directory -Path $build | Out-Null

Write-Host "[1/3] Publishing self-contained Windows x64 (WorkLog only)..."
$winOut = Join-Path $build "win-x64"
$publishArgs = @(
    'publish', $project,
    '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
    '-p:PublishSingleFile=true', '-p:DebugType=None', '-p:DebugSymbols=false',
    '-o', $winOut
)
for ($attempt = 1; $attempt -le 2; $attempt++) {
    $output = & dotnet @publishArgs 2>&1
    $output | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -eq 0) { break }
    $text = $output | Out-String
    if ($attempt -eq 2 -or $text -notmatch 'MSB3713|being used by another process') { break }
    Write-Host "  publish retry (AssemblyInfo locked)..." -ForegroundColor Yellow
    $null = & dotnet build-server shutdown 2>&1
    Start-Sleep -Milliseconds 800
}
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

$exe = Join-Path $winOut "WorkLog.exe"
if (-not (Test-Path $exe)) { throw "WorkLog.exe not found." }
$vi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
if ($vi.ProductName -ne "WorkLog") {
  throw "Wrong product embedded: ProductName=$($vi.ProductName) (expected WorkLog). Abort."
}

Write-Host "[2/3] Staging portable folder..."
$portable = Join-Path $destRoot "WorkLog-Windows-x64"
if (Test-Path $portable) { Remove-Item $portable -Recurse -Force }
New-Item -ItemType Directory -Path $portable | Out-Null
Copy-Item $exe (Join-Path $portable "WorkLog.exe")

Write-Host "[3/3] Creating zip..."
$zipPath = Join-Path $destRoot "WorkLog-Windows-x64-$stamp.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $portable "*") -DestinationPath $zipPath -Force

Remove-Item $build -Recurse -Force

Get-Item $zipPath | Format-List FullName, Length, LastWriteTime
Write-Host "Done. Portable: $portable"
Write-Host "Verify: ProductName=WorkLog, URL=http://127.0.0.1:17822"
