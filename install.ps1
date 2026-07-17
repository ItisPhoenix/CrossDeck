# CrossDeck Installer for Windows
# Copies the standalone executable to AppData and creates Start Menu and Desktop shortcuts.

$ErrorActionPreference = "Stop"

# Define target paths
$appDataFolder = Join-Path $env:APPDATA "CrossDeck"
$startMenuFolder = Join-Path $env:USERPROFILE "AppData\Roaming\Microsoft\Windows\Start Menu\Programs"
$shortcutPath = Join-Path $startMenuFolder "CrossDeck.lnk"
$desktopShortcutPath = Join-Path $env:USERPROFILE "Desktop\CrossDeck.lnk"

# Source executable location
$sourceExe = "D:\CrossDeck\windows-host\CrossDeckHost\bin\Release\net8.0-windows\win-x64\publish\CrossDeckHost.exe"
$sourceAssets = "D:\CrossDeck\windows-host\CrossDeckHost\bin\Release\net8.0-windows\win-x64\publish\Assets"

# Ensure the executable exists before copying
if (-not (Test-Path $sourceExe)) {
    Write-Error "Could not find CrossDeckHost.exe at the source path. Please ensure the project is built in Release mode."
}

# 1. Create target AppData directory and copy application files
Write-Host "Creating installation directory at $appDataFolder..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $appDataFolder | Out-Null
Copy-Item -Path $sourceExe -Destination $appDataFolder -Force

if (Test-Path $sourceAssets) {
    Copy-Item -Path $sourceAssets -Destination $appDataFolder -Recurse -Force
}

$targetExePath = Join-Path $appDataFolder "CrossDeckHost.exe"

# 2. Create Start Menu and Desktop Shortcuts
Write-Host "Creating Start Menu and Desktop shortcuts..." -ForegroundColor Cyan
$wshell = New-Object -ComObject WScript.Shell

# Start Menu Shortcut
$shortcut = $wshell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $targetExePath
$shortcut.WorkingDirectory = $appDataFolder
$shortcut.Description = "CrossDeck PC Host Editor"
$shortcut.IconLocation = "$targetExePath,0"
$shortcut.Save()

# Desktop Shortcut
$desktopShortcut = $wshell.CreateShortcut($desktopShortcutPath)
$desktopShortcut.TargetPath = $targetExePath
$desktopShortcut.WorkingDirectory = $appDataFolder
$desktopShortcut.Description = "CrossDeck PC Host Editor"
$desktopShortcut.IconLocation = "$targetExePath,0"
$desktopShortcut.Save()

Write-Host "CrossDeck installation complete!" -ForegroundColor Green
Write-Host "You can now search for 'CrossDeck' in your Windows Start Menu or launch it from your Desktop." -ForegroundColor Green
