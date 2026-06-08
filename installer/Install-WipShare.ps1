# WipShare - user-scope installer (no admin required).
# Installs the self-contained WipShare.exe sitting next to this script.
$ErrorActionPreference = 'Stop'

$Version    = '0.1.0'
$Publisher  = 'WipShare'
$ExeName    = 'WipShare.exe'
$InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\WipShare'
$src        = Join-Path $PSScriptRoot $ExeName

Write-Host ''
Write-Host '  Installing WipShare...' -ForegroundColor Cyan

if (-not (Test-Path $src)) {
    Write-Host "  ERROR: $ExeName was not found next to this installer." -ForegroundColor Red
    exit 1
}

# Close a running instance so the exe can be overwritten.
Get-Process -Name 'WipShare' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400

# 1. Program files
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item $src (Join-Path $InstallDir $ExeName) -Force
$uninSrc = Join-Path $PSScriptRoot 'Uninstall-WipShare.ps1'
if (Test-Path $uninSrc) { Copy-Item $uninSrc (Join-Path $InstallDir 'Uninstall-WipShare.ps1') -Force }
$exePath = Join-Path $InstallDir $ExeName

# 2. Start-menu shortcut
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$wsh = New-Object -ComObject WScript.Shell
$sc = $wsh.CreateShortcut((Join-Path $startMenu 'WipShare.lnk'))
$sc.TargetPath = $exePath
$sc.WorkingDirectory = $InstallDir
$sc.IconLocation = "$exePath,0"
$sc.Description = 'Quick screen clips for artists'
$sc.Save()

# 3. Launch at startup - the same HKCU Run value the app's Settings toggle manages,
#    so the toggle correctly reflects this. Disable any time in WipShare > Settings.
New-Item -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Force | Out-Null
Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'WipShare' -Value ('"' + $exePath + '"')

# 4. Add/Remove Programs entry (user scope, so it shows in Settings > Apps)
$unin = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\WipShare'
New-Item -Path $unin -Force | Out-Null
Set-ItemProperty $unin -Name 'DisplayName'     -Value 'WipShare'
Set-ItemProperty $unin -Name 'DisplayVersion'  -Value $Version
Set-ItemProperty $unin -Name 'Publisher'       -Value $Publisher
Set-ItemProperty $unin -Name 'DisplayIcon'     -Value $exePath
Set-ItemProperty $unin -Name 'InstallLocation' -Value $InstallDir
Set-ItemProperty $unin -Name 'UninstallString' -Value ('powershell -NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $InstallDir 'Uninstall-WipShare.ps1') + '"')
Set-ItemProperty $unin -Name 'NoModify'      -Value 1 -Type DWord
Set-ItemProperty $unin -Name 'NoRepair'      -Value 1 -Type DWord
Set-ItemProperty $unin -Name 'EstimatedSize' -Value ([int]((Get-Item $exePath).Length / 1KB)) -Type DWord

Write-Host "  Installed to $InstallDir" -ForegroundColor Green
Write-Host '    - Start-menu shortcut created'
Write-Host '    - Launch at startup enabled (toggle in WipShare > Settings)'
Write-Host '    - Listed in Settings > Apps for a clean uninstall'
Write-Host ''

$ans = Read-Host '  Launch WipShare now? [Y/n]'
if ($ans -eq '' -or $ans -match '^[Yy]') { Start-Process $exePath }
