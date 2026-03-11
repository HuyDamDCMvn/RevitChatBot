# RevitChatBot - Package Release
# Creates a zip file ready for GitHub release

param(
    [string]$Version = "1.0.0",
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$buildOutput = Join-Path $repoRoot "src\RevitChatBot.Addin\bin\Release\net8.0-windows"

if (-not $OutputDir) {
    $OutputDir = Join-Path $repoRoot "release"
}

if (-not (Test-Path (Join-Path $buildOutput "RevitChatBot.Addin.dll"))) {
    Write-Host "ERROR: Release build not found. Run these commands first:" -ForegroundColor Red
    Write-Host "  cd ui/revitchatbot-ui; npm install; npm run build" -ForegroundColor Yellow
    Write-Host "  dotnet build RevitChatBot.sln -c Release" -ForegroundColor Yellow
    exit 1
}

Write-Host "Packaging RevitChatBot v$Version..." -ForegroundColor Cyan

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$stagingDir = Join-Path $OutputDir "RevitChatBot-v$Version"
if (Test-Path $stagingDir) {
    Remove-Item $stagingDir -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

$addinDir = Join-Path $stagingDir "RevitChatBot"
Copy-Item $buildOutput $addinDir -Recurse -Force

# Remove unnecessary files from package
$removePatterns = @("*.pdb", "*.xml", "*.deps.json")
foreach ($pattern in $removePatterns) {
    Get-ChildItem $addinDir -Filter $pattern -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue
}

# Remove localization satellite DLLs (keep only English)
$localeDirs = @("cs", "de", "es", "fr", "it", "ja", "ko", "pl", "pt-BR", "ru", "tr", "zh-Hans", "zh-Hant")
foreach ($locale in $localeDirs) {
    $localeDir = Join-Path $addinDir $locale
    if (Test-Path $localeDir) {
        Remove-Item $localeDir -Recurse -Force
    }
}

# Remove duplicate old UI assets (keep only latest build)
$uiAssetsDir = Join-Path $addinDir "ui\assets"
if (Test-Path $uiAssetsDir) {
    $jsFiles = Get-ChildItem $uiAssetsDir -Filter "*.js" | Group-Object { ($_.Name -split '-')[0..($_.Name.Split('-').Length - 2)] -join '-' }
    foreach ($group in $jsFiles) {
        if ($group.Count -gt 1) {
            $oldest = $group.Group | Sort-Object LastWriteTime | Select-Object -First ($group.Count - 1)
            $oldest | Remove-Item -Force -ErrorAction SilentlyContinue
        }
    }

    $cssFiles = Get-ChildItem $uiAssetsDir -Filter "*.css" | Group-Object { ($_.Name -split '-')[0..($_.Name.Split('-').Length - 2)] -join '-' }
    foreach ($group in $cssFiles) {
        if ($group.Count -gt 1) {
            $oldest = $group.Group | Sort-Object LastWriteTime | Select-Object -First ($group.Count - 1)
            $oldest | Remove-Item -Force -ErrorAction SilentlyContinue
        }
    }
}

# Copy install script into package
Copy-Item (Join-Path $repoRoot "scripts\install.ps1") $stagingDir -Force
Copy-Item (Join-Path $repoRoot "scripts\uninstall.ps1") $stagingDir -Force

# Create quick-start README in the package
$quickStart = @"
# RevitChatBot v$Version - Quick Install

## One-Click Install

1. Run PowerShell as your normal user (not Admin required)
2. Execute:
   ``````
   .\install.ps1 -SourceDir .\RevitChatBot
   ``````

## Prerequisites

- Revit 2025
- Ollama (https://ollama.ai)
- Models: qwen2.5:7b + nomic-embed-text

## Setup Ollama

``````bash
ollama serve
ollama pull qwen2.5:7b
ollama pull nomic-embed-text
``````

## Launch

1. Start Revit 2025
2. Click "MEP ChatBot" in the AI ribbon tab
3. Wait for Ollama connection (green status)
4. Start chatting!

## Uninstall

``````
.\uninstall.ps1
``````
"@
$quickStart | Out-File -FilePath (Join-Path $stagingDir "README.txt") -Encoding utf8

# Create the zip
$zipPath = Join-Path $OutputDir "RevitChatBot-v$Version.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path "$stagingDir\*" -DestinationPath $zipPath -CompressionLevel Optimal

$zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host ""
Write-Host "Package created: $zipPath ($zipSize MB)" -ForegroundColor Green
Write-Host ""

# Cleanup staging
Remove-Item $stagingDir -Recurse -Force

Write-Host "Ready for GitHub release!" -ForegroundColor Cyan
