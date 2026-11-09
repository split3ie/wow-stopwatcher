#!/usr/bin/env bash
# Installs the latest WowStopwatcher release on Linux or macOS.
# Usage: curl -fsSL https://raw.githubusercontent.com/split3ie/wow-stopwatcher/main/scripts/install.sh | bash
set -euo pipefail

# ==========================================
# CONFIGURATION
# ==========================================
Repo="split3ie/wow-stopwatcher"
BinaryName="WowStopwatcher"
# ==========================================

if [ -t 1 ]; then
    CYAN=$'\033[36m'; GREEN=$'\033[32m'; YELLOW=$'\033[33m'; RED=$'\033[31m'; RESET=$'\033[0m'
else
    CYAN=""; GREEN=""; YELLOW=""; RED=""; RESET=""
fi

info()    { printf '%s%s%s\n' "$CYAN" "$1" "$RESET"; }
success() { printf '%s%s%s\n' "$GREEN" "$1" "$RESET"; }
path()    { printf '%s  %s%s\n' "$YELLOW" "$1" "$RESET"; }
fail()    { printf '%sError: %s%s\n' "$RED" "$1" "$RESET" >&2; exit 1; }

command -v curl >/dev/null 2>&1 || fail "curl is required but was not found."

# --- Detect OS and architecture ---
case "$(uname -s)" in
    Linux)  Os="linux" ;;
    Darwin) Os="osx" ;;
    *)      fail "Unsupported operating system: $(uname -s)" ;;
esac

case "$(uname -m)" in
    x86_64|amd64)  Arch="x64" ;;
    arm64|aarch64) Arch="arm64" ;;
    *)             fail "Unsupported architecture: $(uname -m)" ;;
esac

if [ "$Os" = "linux" ] && [ "$Arch" != "x64" ]; then
    fail "No Linux $Arch build is published; only linux-x64 is available."
fi

info "Detected Architecture: $Os $Arch"

if [ "$Os" = "linux" ]; then
    AssetSuffix="-linux-x64.tar.gz"
else
    AssetSuffix="-osx-$Arch.zip"
fi

# --- Temp directory, removed on exit ---
TempDir="$(mktemp -d)"
trap 'rm -rf "$TempDir"' EXIT

# --- Fetch latest release info from GitHub API ---
ApiUrl="https://api.github.com/repos/$Repo/releases/latest"
info "Fetching release information from $Repo..."

ReleaseJson="$(curl -fsSL -H "User-Agent: Bash-Installer" "$ApiUrl")" \
    || fail "Failed to fetch release info. Verify repository name is correct and public."

Urls="$(printf '%s\n' "$ReleaseJson" \
    | grep -o '"browser_download_url"[[:space:]]*:[[:space:]]*"[^"]*"' \
    | sed -e 's/^"browser_download_url"[[:space:]]*:[[:space:]]*"//' -e 's/"$//' || true)"

DownloadUrl="$(printf '%s\n' "$Urls" | grep -F "$AssetSuffix" | grep -v '\.sha256$' | head -n 1 || true)"
[ -n "$DownloadUrl" ] || fail "No asset ending in $AssetSuffix found in the latest release of $Repo."

ChecksumUrl="$(printf '%s\n' "$Urls" | grep -Fx "$DownloadUrl.sha256" | head -n 1 || true)"
[ -n "$ChecksumUrl" ] || fail "No .sha256 file found for $(basename "$DownloadUrl"); aborting."

FileName="$(basename "$DownloadUrl")"
ArchivePath="$TempDir/$FileName"

# --- Download and verify ---
info "Downloading $FileName..."
curl -fsSL -o "$ArchivePath" "$DownloadUrl" || fail "Download failed."
curl -fsSL -o "$ArchivePath.sha256" "$ChecksumUrl" || fail "Checksum download failed."

info "Verifying SHA-256..."
Expected="$(awk '{print $1}' "$ArchivePath.sha256" | head -n 1 | tr 'A-F' 'a-f')"
if [ "$Os" = "linux" ]; then
    Actual="$(sha256sum "$ArchivePath" | awk '{print $1}')"
else
    Actual="$(shasum -a 256 "$ArchivePath" | awk '{print $1}')"
fi
[ -n "$Expected" ] || fail "Checksum file is empty or invalid."
[ "$Expected" = "$Actual" ] || fail "SHA-256 mismatch for $FileName (expected $Expected, got $Actual)."

# --- Extract and install ---
info "Extracting archive..."
ExtractDir="$TempDir/extracted"
mkdir -p "$ExtractDir"

if [ "$Os" = "linux" ]; then
    tar -xzf "$ArchivePath" -C "$ExtractDir"
    [ -f "$ExtractDir/$BinaryName" ] || fail "$BinaryName not found inside the archive."

    InstallDir="$HOME/.local/bin"
    mkdir -p "$InstallDir"
    Destination="$InstallDir/$BinaryName"
    Staged="$InstallDir/.$BinaryName.new.$$"

    # Copy next to the target, then rename: replaces the file atomically even if it is running.
    cp "$ExtractDir/$BinaryName" "$Staged"
    chmod +x "$Staged"
    mv -f "$Staged" "$Destination"
else
    ditto -x -k "$ArchivePath" "$ExtractDir"
    [ -d "$ExtractDir/$BinaryName.app" ] || fail "$BinaryName.app not found inside the archive."

    InstallDir="$HOME/Applications"
    mkdir -p "$InstallDir"
    Destination="$InstallDir/$BinaryName.app"
    rm -rf "$Destination"
    ditto "$ExtractDir/$BinaryName.app" "$Destination"
    xattr -dr com.apple.quarantine "$Destination" 2>/dev/null || true
fi

# --- Display installed path ---
printf '\n'
success "[SUCCESS] Installation complete!"
info "Installed Path:"
path "$Destination"

if [ "$Os" = "linux" ]; then
    case ":$PATH:" in
        *":$InstallDir:"*)
            success "$InstallDir is already in your PATH."
            ;;
        *)
            printf '\n'
            info "$InstallDir is not in your PATH. Add this line to your shell profile (for example ~/.bashrc or ~/.profile):"
            path 'export PATH="$HOME/.local/bin:$PATH"'
            ;;
    esac
else
    printf '\n'
    info "Launch it with:"
    path "open ~/Applications/$BinaryName.app"
fi
