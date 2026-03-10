# RevitChatBot - Install Script for Revit 2025
# Run as Administrator if needed

param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not (Test-Path "$repoRoot\RevitChatBot.slnx")) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
}

$buildOutput = Join-Path $repoRoot "src\RevitChatBot.Addin\bin\$Configuration\net8.0-windows"
$revitAddinsDir = "C:\ProgramData\Autodesk\Revit\Addins\2025"
$addinManifest = Join-Path $revitAddinsDir "RevitChatBot.addin"
$dllPath = Join-Path $buildOutput "RevitChatBot.Addin.dll"

if (-not (Test-Path $dllPath)) {
    Write-Host "ERROR: Build output not found at $dllPath" -ForegroundColor Red
    Write-Host "Please build the solution first: dotnet build RevitChatBot.slnx" -ForegroundColor Yellow
    exit 1
}

$addinContent = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>RevitChatBot</Name>
    <Assembly>$dllPath</Assembly>
    <FullClassName>RevitChatBot.Addin.App</FullClassName>
    <AddInId>A1B2C3D4-E5F6-7890-ABCD-EF1234567890</AddInId>
    <VendorId>DCMvn</VendorId>
    <VendorDescription>DCMvn - Revit MEP ChatBot</VendorDescription>
  </AddIn>
</RevitAddIns>
"@

Write-Host "Installing RevitChatBot add-in for Revit 2025..." -ForegroundColor Cyan
Write-Host "  DLL: $dllPath"
Write-Host "  Manifest: $addinManifest"

$addinContent | Out-File -FilePath $addinManifest -Encoding utf8
Write-Host "Add-in manifest installed successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Make sure Ollama is running: ollama serve"
Write-Host "  2. Pull the model: ollama pull qwen2.5:7b"
Write-Host "  3. Start Revit 2025"
Write-Host "  4. Click 'MEP ChatBot' button in the ribbon"
