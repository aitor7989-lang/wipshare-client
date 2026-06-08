# WipShare - uninstaller. Removes the program files, shortcut, startup entry,
# and the Apps listing. Leaves your settings/credentials untouched.
$ErrorActionPreference = 'SilentlyContinue'
$InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\WipShare'

Write-Host '  Removing WipShare...' -ForegroundColor Cyan

# Stop a running instance.
Get-Process -Name 'WipShare' | Stop-Process -Force
Start-Sleep -Milliseconds 400

# Start-menu shortcut
Remove-Item (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\WipShare.lnk') -Force

# Launch-at-startup + Add/Remove Programs entries
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'WipShare'
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\WipShare' -Recurse -Force

# Program files. This script lives inside $InstallDir, so hand the delete to a
# detached cmd that waits for us to exit first.
Start-Process cmd.exe -WindowStyle Hidden -ArgumentList ('/c timeout /t 1 >nul & rmdir /s /q "' + $InstallDir + '"')

Write-Host '  WipShare uninstalled.' -ForegroundColor Green
