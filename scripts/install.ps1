# RevitChatBot - Install Script for Revit 2025
# Usage: .\install.ps1 [-SourceDir <path>]
#   - No arguments: installs from pre-built Release output in repo
#   - With -SourceDir: installs from extracted release zip

param(
    [string]$SourceDir = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

if (-not $SourceDir) {
    $SourceDir = Join-Path $repoRoot "src\RevitChatBot.Addin\bin\Release\net8.0-windows"
}

if (-not (Test-Path (Join-Path $SourceDir "RevitChatBot.Addin.dll"))) {
    Write-Host "ERROR: RevitChatBot.Addin.dll not found in $SourceDir" -ForegroundColor Red
    Write-Host ""
    Write-Host "If installing from source, build first:" -ForegroundColor Yellow
    Write-Host "  cd ui/revitchatbot-ui; npm install; npm run build" -ForegroundColor Yellow
    Write-Host "  dotnet build RevitChatBot.sln -c Release" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "If installing from release zip, specify the extracted folder:" -ForegroundColor Yellow
    Write-Host "  .\install.ps1 -SourceDir C:\path\to\extracted\RevitChatBot" -ForegroundColor Yellow
    exit 1
}

$revitAddinsDir = "$env:APPDATA\Autodesk\Revit\Addins\2025"
$deployDir = Join-Path $revitAddinsDir "RevitChatBot"

if (-not (Test-Path $revitAddinsDir)) {
    Write-Host "WARNING: Revit 2025 Addins folder not found at $revitAddinsDir" -ForegroundColor Yellow
    Write-Host "Creating folder..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $revitAddinsDir -Force | Out-Null
}

Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  RevitChatBot Installer - Revit 2025" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Source:      $SourceDir" -ForegroundColor White
Write-Host "  Destination: $deployDir" -ForegroundColor White
Write-Host ""

# Copy all files
Write-Host "Copying files..." -ForegroundColor Yellow
if (Test-Path $deployDir) {
    Remove-Item $deployDir -Recurse -Force
}
Copy-Item $SourceDir $deployDir -Recurse -Force

# Copy .addin manifest to Addins root
$addinManifest = Join-Path $SourceDir "RevitChatBot.addin"
if (Test-Path $addinManifest) {
    Copy-Item $addinManifest $revitAddinsDir -Force
}
else {
    $manifestContent = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>RevitChatBot</Name>
    <Assembly>RevitChatBot\RevitChatBot.Addin.dll</Assembly>
    <FullClassName>RevitChatBot.Addin.App</FullClassName>
    <AddInId>A1B2C3D4-E5F6-7890-ABCD-EF1234567890</AddInId>
    <VendorId>DCMvn</VendorId>
    <VendorDescription>DCMvn - Revit MEP ChatBot</VendorDescription>
  </AddIn>
</RevitAddIns>
"@
    $manifestContent | Out-File -FilePath (Join-Path $revitAddinsDir "RevitChatBot.addin") -Encoding utf8
}

Write-Host ""
Write-Host "Installation complete!" -ForegroundColor Green
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  Next Steps:" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  1. Install Ollama:  https://ollama.ai" -ForegroundColor White
Write-Host "  2. Start Ollama:    ollama serve" -ForegroundColor White
Write-Host "  3. Pull models:" -ForegroundColor White
Write-Host "       ollama pull qwen2.5:7b" -ForegroundColor White
Write-Host "       ollama pull nomic-embed-text" -ForegroundColor White
Write-Host "  4. Launch Revit 2025" -ForegroundColor White
Write-Host '  5. Click "MEP ChatBot" in the AI ribbon tab' -ForegroundColor White
Write-Host ""
