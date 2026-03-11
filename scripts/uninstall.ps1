# RevitChatBot - Uninstall Script for Revit 2025

$revitAddinsDir = "$env:APPDATA\Autodesk\Revit\Addins\2025"
$deployDir = Join-Path $revitAddinsDir "RevitChatBot"
$addinManifest = Join-Path $revitAddinsDir "RevitChatBot.addin"

Write-Host "Uninstalling RevitChatBot..." -ForegroundColor Cyan

$removed = $false

if (Test-Path $deployDir) {
    Remove-Item $deployDir -Recurse -Force
    Write-Host "  Removed: $deployDir" -ForegroundColor Green
    $removed = $true
}

if (Test-Path $addinManifest) {
    Remove-Item $addinManifest -Force
    Write-Host "  Removed: $addinManifest" -ForegroundColor Green
    $removed = $true
}

if ($removed) {
    Write-Host ""
    Write-Host "RevitChatBot has been uninstalled." -ForegroundColor Green
    Write-Host "Restart Revit to complete the removal." -ForegroundColor Yellow
}
else {
    Write-Host "RevitChatBot was not found. Nothing to remove." -ForegroundColor Yellow
}
