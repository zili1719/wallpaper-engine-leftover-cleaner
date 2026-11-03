# Build WeCleaner.exe using only the csc.exe that ships with .NET Framework 4.x.
# No external dependencies, no NuGet, no .NET SDK required.
# NOTE: this file is intentionally ASCII-only; PowerShell 5.1 misreads BOM-less UTF-8 scripts.
[CmdletBinding()]
param(
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$src  = Join-Path $root 'src'
$dist = Join-Path $root 'dist'

$csc64 = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$csc32 = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$csc = if (Test-Path $csc64) { $csc64 } elseif (Test-Path $csc32) { $csc32 } else { $null }
if (-not $csc) { throw 'csc.exe not found (.NET Framework 4.x missing)' }

if ($Clean -and (Test-Path $dist)) { Remove-Item "$dist\*" -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Force -Path $dist | Out-Null

$sources = Get-ChildItem -Path $src -Filter '*.cs' | Sort-Object Name | ForEach-Object { $_.FullName }
if (-not $sources) { throw 'no .cs files under src' }

$out = Join-Path $dist 'WeCleaner.exe'
$manifest = Join-Path $src 'app.manifest'
$icon = Join-Path $root 'assets\icon.ico'

$cscArgs = @(
    '/nologo'
    '/target:winexe'
    '/platform:anycpu'
    '/optimize+'
    '/warn:4'
    '/codepage:65001'
    ('/out:' + $out)
    ('/win32manifest:' + $manifest)
    '/r:System.dll'
    '/r:System.Core.dll'
    '/r:System.Drawing.dll'
    '/r:System.Windows.Forms.dll'
    '/r:Microsoft.VisualBasic.dll'
)
if (Test-Path $icon) { $cscArgs += ('/win32icon:' + $icon) }
$cscArgs += $sources

Write-Host ("compile -> " + $out) -ForegroundColor Cyan
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw ("compile failed, exit code " + $LASTEXITCODE) }

$fi = Get-Item $out
Write-Host ("done: {0}  ({1:N0} KB)" -f $fi.Name, ($fi.Length / 1KB)) -ForegroundColor Green
