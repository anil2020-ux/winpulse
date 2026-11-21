# WinPulse installer for Windows (per-user, no admin required).
#
#   .\install.ps1                 install the latest GitHub release
#   .\install.ps1 -Token $env:GITHUB_TOKEN   install from a private repository
#   .\install.ps1 -NoLaunch       install without starting WinPulse
#
# The app installs to %LOCALAPPDATA%\Programs\WinPulse so the in-app updater
# can replace its own files without elevation.

[CmdletBinding()]
param(
    [string]$Version = 'latest',
    [string]$Token = $env:GITHUB_TOKEN,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
$repo = 'anil2020-ux/winpulse'
$appDir = Join-Path $env:LOCALAPPDATA 'Programs\WinPulse'
$apiHeaders = @{ 'User-Agent' = 'WinPulse-Installer' }
if ($Token) { $apiHeaders['Authorization'] = "Bearer $Token" }

Write-Host "Resolving WinPulse release ($Version)…" -ForegroundColor Cyan
if ($Version -eq 'latest') {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/latest" -Headers $apiHeaders
} else {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/tags/$Version" -Headers $apiHeaders
}

$asset = $release.assets | Where-Object { $_.name -like '*.zip' } | Select-Object -First 1
if (-not $asset) {
    throw "Release $($release.tag_name) has no Windows zip asset."
}

$tag = $release.tag_name
Write-Host "Downloading $($asset.name) ($([math]::Round($asset.size / 1MB, 1)) MB), version $tag…" -ForegroundColor Cyan
$tempZip = Join-Path $env:TEMP ("winpulse-" + [Guid]::NewGuid().ToString('N') + '.zip')
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $tempZip -Headers $apiHeaders

$running = Get-Process WinPulse -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Closing the running WinPulse…" -ForegroundColor Yellow
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 800
}

New-Item -ItemType Directory -Path $appDir -Force | Out-Null
Write-Host "Installing to $appDir…" -ForegroundColor Cyan
Expand-Archive -Path $tempZip -DestinationPath $appDir -Force
Remove-Item $tempZip -Force

$exe = Join-Path $appDir 'WinPulse.exe'
if (-not (Test-Path $exe)) {
    throw "Install failed: WinPulse.exe was not extracted."
}

# Uninstaller lives next to the app so the registry entry can point at it.
$uninstallScript = Join-Path $appDir 'uninstall.ps1'
@'
$ErrorActionPreference = "SilentlyContinue"
Get-Process WinPulse | Stop-Process -Force
Start-Sleep -Milliseconds 800
$appDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$shortcut = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\WinPulse.lnk"
Remove-Item $shortcut -Force
Remove-Item "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\WinPulse" -Recurse -Force
Remove-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name WinPulse
Start-Process cmd.exe -ArgumentList "/c timeout /t 2 >nul & rmdir /s /q `"$appDir`"" -WindowStyle Hidden
'@ | Set-Content -Path $uninstallScript -Encoding UTF8

# Start Menu shortcut
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
New-Item -ItemType Directory -Path $startMenu -Force | Out-Null
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path $startMenu 'WinPulse.lnk'))
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $appDir
$shortcut.IconLocation = "$exe,0"
$shortcut.Description = 'WinPulse — Windows activity and file tracker'
$shortcut.Save()

# Per-user uninstall entry
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\WinPulse'
New-Item -Path $uninstallKey -Force | Out-Null
Set-ItemProperty $uninstallKey -Name DisplayName -Value 'WinPulse'
Set-ItemProperty $uninstallKey -Name DisplayVersion -Value $tag.TrimStart('v')
Set-ItemProperty $uninstallKey -Name Publisher -Value 'Anil Acharya'
Set-ItemProperty $uninstallKey -Name InstallLocation -Value $appDir
Set-ItemProperty $uninstallKey -Name DisplayIcon -Value $exe
Set-ItemProperty $uninstallKey -Name EstimatedSize -Value ([int]((Get-ChildItem $appDir -Recurse | Measure-Object Length -Sum).Sum / 1KB))
Set-ItemProperty $uninstallKey -Name UninstallString -Value "powershell -NoProfile -ExecutionPolicy Bypass -File `"$uninstallScript`""
Set-ItemProperty $uninstallKey -Name NoModify -Value 1 -Type DWord
Set-ItemProperty $uninstallKey -Name NoRepair -Value 1 -Type DWord

# Remove any stale startup entry before recording a fresh install.
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name WinPulse -ErrorAction SilentlyContinue

Write-Host "WinPulse $($tag.TrimStart('v')) installed." -ForegroundColor Green
Write-Host "  App: $exe"
Write-Host "  Start Menu: WinPulse"

if (-not $NoLaunch) {
    Start-Process -FilePath $exe
}
