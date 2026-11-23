param()

$ErrorActionPreference = "Stop"

$root = Resolve-Path "$PSScriptRoot\.."

# Read version from the csproj (single source of truth).
$csproj = Get-Content (Join-Path $root "src\Walld\Walld.csproj") -Raw
if ($csproj -match '<Version>([^<]+)</Version>') {
    $version = $Matches[1]
} else {
    Write-Error "Could not find Version in src\Walld\Walld.csproj"
    exit 1
}

Write-Host "Building release $version..."

# Step 1: Build the installer.
& "$root\build\build-installer.ps1"
if ($LASTEXITCODE -ne 0) { throw "build-installer.ps1 failed" }

# Step 2: Create the SHA-256 checksum file next to the installer.
$installer = Join-Path $root "installer\Output\WallpaperSetup-$version.exe"
if (-not (Test-Path $installer)) {
    Write-Error "Installer not found: $installer"
    exit 1
}

$hash = Get-FileHash -Path $installer -Algorithm SHA256
$sha256File = Join-Path $root "installer\Output\WallpaperSetup-$version.exe.sha256"
Set-Content -Path $sha256File -Value $hash.Hash -NoNewline

Write-Host ""
Write-Host "Release files to attach to GitHub release v${version}:"
Write-Host "  $installer"
Write-Host "  $sha256File"

