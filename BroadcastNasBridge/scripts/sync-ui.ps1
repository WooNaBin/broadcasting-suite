#Requires -Version 5.1
<#
.SYNOPSIS
  형제 앱 UI를 wwwroot/{schedule,worklog,files} 로 복사·패치 (sync-ui.sh 대응)
#>
$ErrorActionPreference = 'Stop'
$Root = Split-Path $PSScriptRoot -Parent
$Suite = Split-Path $Root -Parent

function Write-Utf8NoBom([string]$FilePath, [string]$Content) {
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($FilePath, $Content, $utf8)
}

function Copy-IfExists([string]$Src, [string]$Dst) {
    if (Test-Path $Src) {
        Copy-Item $Src $Dst -Force
    }
}

$scheduleSrc = Join-Path $Suite 'ScheduleDataManager'
$scheduleDest = Join-Path $Root 'wwwroot\schedule'
New-Item -ItemType Directory -Path (Join-Path $scheduleDest 'assets\icons') -Force | Out-Null
if (Test-Path $scheduleSrc) {
    foreach ($f in @('index.html', 'styles.css', 'sw.js')) {
        Copy-IfExists (Join-Path $scheduleSrc $f) (Join-Path $scheduleDest $f)
    }
    $appJs = Join-Path $scheduleSrc 'app.js'
    if (Test-Path $appJs) {
        $txt = [System.IO.File]::ReadAllText($appJs)
        $txt = $txt.Replace('const API_BASE = "";', 'const API_BASE = "/schedule";')
        $txt = $txt.Replace("const API_BASE = '';", "const API_BASE = '/schedule';")
        Write-Utf8NoBom (Join-Path $scheduleDest 'app.js') $txt
    }
    $icons = Join-Path $scheduleSrc 'assets\icons'
    if (Test-Path $icons) {
        Get-ChildItem $icons -Filter '*.png' -ErrorAction SilentlyContinue |
            ForEach-Object { Copy-Item $_.FullName (Join-Path $scheduleDest 'assets\icons') -Force }
    }
}

$worklogSrc = Join-Path $Suite 'WorkLog\www'
$worklogDest = Join-Path $Root 'wwwroot\worklog'
New-Item -ItemType Directory -Path $worklogDest -Force | Out-Null
if (Test-Path $worklogSrc) {
    foreach ($f in @('index.html', 'styles.css', 'templates.js')) {
        Copy-IfExists (Join-Path $worklogSrc $f) (Join-Path $worklogDest $f)
    }
    $indexHtml = Join-Path $worklogDest 'index.html'
    if (Test-Path $indexHtml) {
        $html = [System.IO.File]::ReadAllText($indexHtml)
        $html = $html.Replace('href="/styles.css"', 'href="/worklog/styles.css"')
        $html = $html.Replace('href="styles.css"', 'href="/worklog/styles.css"')
        $html = $html.Replace('src="/app.js"', 'src="/worklog/app.js"')
        $html = $html.Replace('src="app.js"', 'src="/worklog/app.js"')
        Write-Utf8NoBom $indexHtml $html
    }
    $appJs = Join-Path $worklogSrc 'app.js'
    if (Test-Path $appJs) {
        $txt = [System.IO.File]::ReadAllText($appJs)
        $txt = $txt.Replace('let apiBase = "http://127.0.0.1:17822";', 'let apiBase = "/worklog";')
        $txt = $txt.Replace("let apiBase = 'http://127.0.0.1:17822';", "let apiBase = '/worklog';")
        $txt = $txt.Replace('return isBridgeHosted() ? "/worklog" : "http://127.0.0.1:17822";', 'return "/worklog";')
        $txt = $txt.Replace("return isBridgeHosted() ? '/worklog' : 'http://127.0.0.1:17822';", "return '/worklog';")
        $txt = $txt.Replace('return "http://127.0.0.1:17822";', 'return "/worklog";')
        $txt = $txt.Replace("return 'http://127.0.0.1:17822';", "return '/worklog';")
        $txt = $txt.Replace('from "/templates.js"', 'from "/worklog/templates.js"')
        $txt = $txt.Replace("from '/templates.js'", "from '/worklog/templates.js'")
        $txt = $txt.Replace('from "./templates.js"', 'from "/worklog/templates.js"')
        $txt = $txt.Replace("from './templates.js'", "from '/worklog/templates.js'")
        Write-Utf8NoBom (Join-Path $worklogDest 'app.js') $txt
    }
}

$filesSrc = Join-Path $Suite 'FileChecker\wwwroot'
$filesDest = Join-Path $Root 'wwwroot\files'
New-Item -ItemType Directory -Path $filesDest -Force | Out-Null
if (Test-Path $filesSrc) {
    Get-ChildItem $filesSrc -File | ForEach-Object {
        if ($_.Name -eq 'app.js') {
            $txt = [System.IO.File]::ReadAllText($_.FullName)
            $txt = $txt.Replace('fetch(url, options)', "fetch(url.startsWith('/api/') ? '/files' + url : url, options)")
            $txt = $txt.Replace("window.location.href = '/api/", "window.location.href = '/files/api/")
            $txt = $txt.Replace(
                "if(action === 'schedule') openModal('schedule');",
                "if(action === 'schedule') { toast('작업 목록은 ScheduleDataManager의 Recording 일정에서 자동으로 가져옵니다.'); return; }")
            $txt = $txt.Replace(
                "if(action === 'import-schedules') `$('#schedule-import').click();",
                "if(action === 'import-schedules') { toast('브리지에서는 SDM 공유 스케줄만 사용합니다.'); return; }")
            $txt = $txt.Replace('등록된 스케줄이 없습니다', 'Recording 일정이 없습니다')
            Write-Utf8NoBom (Join-Path $filesDest 'app.js') $txt
        }
        elseif ($_.Name -eq 'index.html') {
            $utf8 = New-Object System.Text.UTF8Encoding $false
            $html = [System.IO.File]::ReadAllText($_.FullName, $utf8)
            $html = [regex]::Replace(
                $html,
                '<button class="nav-button active" data-action="schedule">[^<]*</button>',
                '<button class="nav-button" data-action="refresh" title="SDM Recording sync">SDM sync</button>')
            $html = [regex]::Replace(
                $html,
                '<h3>[^<]*</h3><p>-</p><button class="primary" data-action="schedule">[^<]*</button>',
                '<h3>No Recording schedules</h3><p>SDM preparation includes Recording</p><button class="primary" data-action="refresh">Refresh</button>')
            $html = $html.Replace('href="styles.css"', 'href="/files/styles.css"')
            $html = $html.Replace('src="app.js"', 'src="/files/app.js"')
            Write-Utf8NoBom (Join-Path $filesDest 'index.html') $html
        }
        else {
            Copy-Item $_.FullName (Join-Path $filesDest $_.Name) -Force
        }
    }
}

Write-Host "UI synced -> $Root\wwwroot\{schedule,worklog,files}"
