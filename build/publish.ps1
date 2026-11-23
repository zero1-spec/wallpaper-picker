param()

$ErrorActionPreference = "Stop"

$root = Resolve-Path "$PSScriptRoot\.."
$outDir = Join-Path $root "dist\WallpaperSuite"

Write-Host "Publishing WallpaperSuite to $outDir"

# Start clean so files removed from the project never linger in the installer.
if (Test-Path $outDir) {
    Remove-Item -Recurse -Force $outDir
}

# All three apps publish self-contained into one folder (the runtime files are shared).
# WallVideo must be published too: walld only copies wallvideo.exe as a build-order
# reference, without wallvideo.dll, so the live wallpaper player could not start.
$projects = @(
    "src\Walld\Walld.csproj",
    "src\WallPicker\WallPicker.csproj",
    "src\WallVideo\WallVideo.csproj"
)
foreach ($project in $projects) {
    dotnet publish (Join-Path $root $project) -c Release -r win-x64 --self-contained -o "$outDir"
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $project" }
}

foreach ($required in @("walld.exe", "wallpicker.exe", "wallpicker.dll", "wallvideo.exe", "wallvideo.dll")) {
    if (-not (Test-Path (Join-Path $outDir $required))) { throw "Publish output is missing $required" }
}

$wallpapers = @(Get-ChildItem (Join-Path $outDir "wallpapers") -Recurse -File -Include *.jpg, *.jpeg, *.png -ErrorAction SilentlyContinue).Count
Write-Host "Publish complete: $outDir ($wallpapers built-in wallpapers)"
