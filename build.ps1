# Run from the project folder:  .\build.ps1
$ErrorActionPreference = "Stop"
dotnet --version
if (-not (Test-Path "$PSScriptRoot\Fonts\Vazirmatn-Regular.ttf")) {
    Write-Host "NOTE: the Fonts folder (Vazirmatn) was not found next to Hamyar.csproj - the app will use Segoe UI." -ForegroundColor Yellow
    Write-Host "      Restore the Fonts folder and build again to embed the fonts inside the app." -ForegroundColor Yellow
}
dotnet restore
dotnet build -c Release
Write-Host "`nBuilt OK. Run with:  dotnet run -c Release" -ForegroundColor Green
