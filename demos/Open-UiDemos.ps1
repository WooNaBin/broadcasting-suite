#Requires -Version 5.1
<#
.SYNOPSIS
  방송실 UI 데모 정적 서버 (NAS/빌드 불필요). Mac: Open-UiDemos.sh
  Python이 없어도 PowerShell HttpListener로 동작합니다.
#>
$ErrorActionPreference = 'Stop'
$Port = 17990
$Root = (Resolve-Path (Split-Path $PSScriptRoot -Parent)).Path
$Url = "http://127.0.0.1:$Port/demos/"

$Mime = @{
  '.html' = 'text/html; charset=utf-8'
  '.htm'  = 'text/html; charset=utf-8'
  '.js'   = 'text/javascript; charset=utf-8'
  '.mjs'  = 'text/javascript; charset=utf-8'
  '.css'  = 'text/css; charset=utf-8'
  '.json' = 'application/json; charset=utf-8'
  '.svg'  = 'image/svg+xml'
  '.png'  = 'image/png'
  '.jpg'  = 'image/jpeg'
  '.jpeg' = 'image/jpeg'
  '.gif'  = 'image/gif'
  '.ico'  = 'image/x-icon'
  '.woff' = 'font/woff'
  '.woff2'= 'font/woff2'
  '.map'  = 'application/json'
  '.txt'  = 'text/plain; charset=utf-8'
  '.md'   = 'text/markdown; charset=utf-8'
}

function Get-DemoContentType([string]$Path) {
  $ext = [IO.Path]::GetExtension($Path).ToLowerInvariant()
  if ($Mime.ContainsKey($ext)) { return $Mime[$ext] }
  return 'application/octet-stream'
}

Write-Host "UI demos root: $Root"
Write-Host "Open: $Url"
Write-Host "Stop: Ctrl+C"
Write-Host ""

$listener = [System.Net.HttpListener]::new()
$prefix = "http://127.0.0.1:$Port/"
try {
  $listener.Prefixes.Add($prefix)
  $listener.Start()
} catch {
  Write-Host "Port $Port busy — opening existing URL."
  Start-Process $Url | Out-Null
  exit 0
}

Start-Process $Url | Out-Null

try {
  while ($listener.IsListening) {
    $ctx = $listener.GetContext()
    $req = $ctx.Request
    $res = $ctx.Response
    try {
      $rel = [Uri]::UnescapeDataString($req.Url.AbsolutePath.TrimStart('/'))
      if ([string]::IsNullOrWhiteSpace($rel)) { $rel = 'demos/index.html' }
      $rel = $rel -replace '/', [IO.Path]::DirectorySeparatorChar
      $full = [IO.Path]::GetFullPath((Join-Path $Root $rel))
      if (-not $full.StartsWith($Root, [StringComparison]::OrdinalIgnoreCase)) {
        $res.StatusCode = 403
        $bytes = [Text.Encoding]::UTF8.GetBytes('Forbidden')
        $res.OutputStream.Write($bytes, 0, $bytes.Length)
      } elseif ((Test-Path $full -PathType Container)) {
        $index = Join-Path $full 'index.html'
        if (Test-Path $index) {
          $bytes = [IO.File]::ReadAllBytes($index)
          $res.ContentType = 'text/html; charset=utf-8'
          $res.ContentLength64 = $bytes.Length
          $res.Headers['Cache-Control'] = 'no-cache'
          $res.OutputStream.Write($bytes, 0, $bytes.Length)
        } else {
          $res.StatusCode = 404
        }
      } elseif (Test-Path $full -PathType Leaf) {
        $bytes = [IO.File]::ReadAllBytes($full)
        $res.ContentType = Get-DemoContentType $full
        $res.ContentLength64 = $bytes.Length
        $res.Headers['Cache-Control'] = 'no-cache'
        $res.OutputStream.Write($bytes, 0, $bytes.Length)
      } else {
        $res.StatusCode = 404
        $bytes = [Text.Encoding]::UTF8.GetBytes('Not Found')
        $res.OutputStream.Write($bytes, 0, $bytes.Length)
      }
    } catch {
      try { $res.StatusCode = 500 } catch { }
    } finally {
      try { $res.OutputStream.Close() } catch { }
    }
  }
} finally {
  if ($listener.IsListening) { $listener.Stop() }
  $listener.Close()
}
