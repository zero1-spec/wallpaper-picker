param()

$ErrorActionPreference = "Stop"

$root = Resolve-Path "$PSScriptRoot\.."

Write-Host "Step 1: Publishing..."
& "$root\build\publish.ps1"

$isccPaths = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)
$iscc = $null
foreach ($p in $isccPaths) {
    if (Test-Path $p) { $iscc = $p; break }
}
if (-not $iscc) {
    Write-Error "ISCC.exe not found. Checked: $($isccPaths -join ', '). Install Inno Setup 6 first."
    exit 1
}

Write-Host "Step 2: Building installer..."
& $iscc "$root\installer\WallpaperSetup.iss"

Write-Host "Installer build complete."
