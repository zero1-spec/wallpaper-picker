param()

$ErrorActionPreference = "Stop"

$root = Resolve-Path "$PSScriptRoot\.."
$outDir = Join-Path $root "dist\WallpaperSuite"

Write-Host "Publishing WallpaperSuite to $outDir"

dotnet publish "$root\src\Walld\Walld.csproj" -c Release -r win-x64 --self-contained -o "$outDir"
dotnet publish "$root\src\WallPicker\WallPicker.csproj" -c Release -r win-x64 --self-contained -o "$outDir"

Write-Host "Publish complete: $outDir"
