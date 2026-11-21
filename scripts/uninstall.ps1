# Stops WinPulse and removes the per-user installation created by install.ps1.
$ErrorActionPreference = 'SilentlyContinue'

Get-Process WinPulse | Stop-Process -Force
Start-Sleep -Milliseconds 800

$appDir = Join-Path $env:LOCALAPPDATA 'Programs\WinPulse'
$shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\WinPulse.lnk'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\WinPulse'

Remove-Item $shortcut -Force
Remove-Item $uninstallKey -Recurse -Force
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name WinPulse

# The running script can be inside $appDir, so let cmd remove it shortly after exit.
Start-Process cmd.exe -ArgumentList "/c timeout /t 2 >nul & rmdir /s /q `"$appDir`"" -WindowStyle Hidden

Write-Host 'WinPulse removed. Your local index was stored in %LOCALAPPDATA%\WinPulse and has been kept unless you deleted it manually.' -ForegroundColor Green
