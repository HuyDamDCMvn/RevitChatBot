# RevitChatBot - Uninstall Script
$addinManifest = "C:\ProgramData\Autodesk\Revit\Addins\2025\RevitChatBot.addin"

if (Test-Path $addinManifest) {
    Remove-Item $addinManifest -Force
    Write-Host "RevitChatBot add-in removed." -ForegroundColor Green
} else {
    Write-Host "Add-in manifest not found. Nothing to remove." -ForegroundColor Yellow
}
