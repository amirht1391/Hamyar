# Builds the final app into .\publish  (Hamyar.exe). The Vazirmatn fonts (Fonts folder) are EMBEDDED inside the exe.
#   .\publish.ps1                 small, needs the .NET 8 Desktop Runtime on the target PC
#   .\publish.ps1 -SelfContained  ~70 MB, runs on any PC
param([switch]$SelfContained)
$ErrorActionPreference = "Stop"

$font = Join-Path $PSScriptRoot "Fonts\Vazirmatn-Regular.ttf"
if (-not (Test-Path $font)) {
    throw "The Fonts folder (Vazirmatn-*.ttf) was not found next to Hamyar.csproj. Restore it first - the fonts are embedded into the exe at build time."
}

$sc  = if ($SelfContained) { "true" } else { "false" }
$out = Join-Path $PSScriptRoot "publish"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

dotnet publish (Join-Path $PSScriptRoot "Hamyar.csproj") -c Release -r win-x64 --self-contained $sc -p:PublishSingleFile=true -o $out

# sanity check: the exe must be larger than one font file, otherwise the font was not embedded
$exe = Get-Item (Join-Path $out "Hamyar.exe")
if ($exe.Length -lt (Get-Item $font).Length) {
    Write-Host "WARNING: Hamyar.exe is smaller than the font file - the font was probably NOT embedded." -ForegroundColor Yellow
} else {
    Write-Host "`nFont embedded OK." -ForegroundColor Green
}
Write-Host "Done -> $out\Hamyar.exe" -ForegroundColor Green
