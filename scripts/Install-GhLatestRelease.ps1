$ErrorActionPreference = "Stop"

# ==========================================
# CONFIGURATION
# ==========================================
$Repo = "split3ie/wow-stopwatcher"        # Example: "cli/cli"
$BinaryName = "WowStopwatcher"    # Expected base name of the tool (without .exe)
# ==========================================

# --- Directories (Windows standard user bin) ---
$InstallDir = Join-Path $HOME "bin"
if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
}

# --- Detect Architecture ---
$Arch = switch ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture) {
    'X64'   { 'x64' }
    'Arm64' { 'arm64' }
    Default { 
        Write-Error "Unsupported architecture: $_"
        exit 1
    }
}

Write-Host "Detected Architecture: Windows $Arch" -ForegroundColor Cyan

# --- Fetch Latest Release URL from GitHub API ---
$ApiUrl = "https://api.github.com/repos/$Repo/releases/latest"
Write-Host "Fetching release information from $Repo..." -ForegroundColor Cyan

try {
    $ReleaseInfo = Invoke-RestMethod -Uri $ApiUrl -Headers @{ "User-Agent" = "PowerShell-Installer" }
} catch {
    Write-Error "Failed to fetch release info. Verify repository name is correct and public."
    exit 1
}

# --- Strict Windows Asset Matching ---
# Look specifically for assets containing both Windows identifiers ('win' or 'windows') AND your architecture ('x64'), ending in .zip
$Asset = $ReleaseInfo.assets | Where-Object { 
    ($_.name -like "*win*" -or $_.name -like "*windows*") -and 
    ($_.name -match $Arch) -and 
    ($_.name -like "*.zip") 
} | Select-Object -First 1

if (-not $Asset) {
    # Fallback: look for any asset containing 'win' or 'windows' with a .zip extension
    $Asset = $ReleaseInfo.assets | Where-Object { 
        ($_.name -like "*win*" -or $_.name -like "*windows*") -and 
        ($_.name -like "*.zip") 
    } | Select-Object -First 1
}

if (-not $Asset) {
    Write-Error "Error: No Windows .zip assets found in the latest release of $Repo."
    exit 1
}

$DownloadUrl = $Asset.browser_download_url
$FileName = $Asset.name
$TempDir = Join-Path([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString())
New-Item -ItemType Directory -Force -Path $TempDir | Out-Null

$TempZipPath = Join-Path $TempDir $FileName

Write-Host "Downloading $FileName..." -ForegroundColor Cyan
Invoke-WebRequest -Uri $DownloadUrl -OutFile $TempZipPath

# --- Extract and Install ---
Write-Host "Extracting archive..." -ForegroundColor Cyan
$ExtractDir = Join-Path $TempDir "extracted"
Expand-Archive -Path $TempZipPath -DestinationPath $ExtractDir -Force

# Search recursively for the executable inside the extracted folders
$ExtractedFile = Get-ChildItem -Path $ExtractDir -Recurse -File | Where-Object { 
    $_.Extension -eq ".exe" -and ($_.Name -eq "$BinaryName.exe" -or $_.Name -like "*$BinaryName*") 
} | Select-Object -First 1

if (-not $ExtractedFile) {
    # Fallback: take any .exe file inside the zip package if naming differs
    $ExtractedFile = Get-ChildItem -Path $ExtractDir -Recurse -File | Where-Object { 
        $_.Extension -eq ".exe" 
    } | Select-Object -First 1
}

if (-not $ExtractedFile) {
    Write-Error "Could not find any executable (.exe) file inside the downloaded zip archive."
    exit 1
}

# Install/rename it cleanly to your desired command name in C:\Users\<User>\bin
$DestinationBinary = Join-Path $InstallDir "$BinaryName.exe"
Copy-Item $ExtractedFile.FullName -Destination $DestinationBinary -Force

# Create/overwrite a Start Menu shortcut so the app can be found via Start search
$ShortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) "$BinaryName.lnk"
$Shell = New-Object -ComObject WScript.Shell
$Shortcut = $Shell.CreateShortcut($ShortcutPath)
$Shortcut.TargetPath = $DestinationBinary
$Shortcut.WorkingDirectory = $InstallDir
$Shortcut.IconLocation = "$DestinationBinary,0"
$Shortcut.Description = "WowStopwatcher time tracker"
$Shortcut.Save()

# Cleanup temp files
Remove-Item -Path $TempDir -Recurse -Force

# --- Display Installed Path ---
Write-Host "`n[SUCCESS] Installation complete!" -ForegroundColor Green
Write-Host "Installed Executable Path:" -ForegroundColor Cyan
Write-Host "  $DestinationBinary" -ForegroundColor Yellow
Write-Host "Start Menu Shortcut:" -ForegroundColor Cyan
Write-Host "  $ShortcutPath" -ForegroundColor Yellow

# --- Ensure PATH is configured persistently ---
$UserPath = [Environment]::GetEnvironmentVariable("PATH", "User")
if ($UserPath -notlike "*$InstallDir*") {
    Write-Host "`nAdding $InstallDir to your User PATH environment variable..." -ForegroundColor Cyan
    $NewPath = "$UserPath;$InstallDir"
    [Environment]::SetEnvironmentVariable("PATH", $NewPath, "User")
    
    # Update current session PATH immediately
    $env:PATH = "$env:PATH;$InstallDir"
    Write-Host "PATH updated successfully!" -ForegroundColor Green
} else {
    Write-Host "$InstallDir is already in your PATH." -ForegroundColor Green
}
