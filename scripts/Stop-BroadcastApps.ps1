#Requires -Version 5.1
<#
.SYNOPSIS
  방송실 관련 프로세스 종료 (Windows).
#>
[CmdletBinding()]
param()

try {
    $utf8 = New-Object System.Text.UTF8Encoding $false
    if (Get-Command chcp -ErrorAction SilentlyContinue) { chcp 65001 | Out-Null }
    [Console]::OutputEncoding = $utf8
}
catch { }

$names = @(
    'BroadcastNasBridge',
    'CtrlOne',
    'WorkLog',
    'BroadcastingSchedule',
    'ScheduleReader',
    'FileCheckerFinder'
)

Write-Host "방송실 프로그램 프로세스 종료"
Write-Host "=========================="
$killed = 0
foreach ($name in $names) {
    Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host ("  종료: {0} (PID {1})" -f $_.ProcessName, $_.Id)
        Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
        $killed++
    }
}
if ($killed -eq 0) {
    Write-Host "  실행 중인 방송실 프로세스가 없습니다."
}
else {
    Write-Host ("  {0}개 프로세스 종료." -f $killed) -ForegroundColor Green
}
Start-Sleep -Milliseconds 400
