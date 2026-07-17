param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot

$projectPath = Join-Path $PSScriptRoot "Launcher.App\Launcher.App.csproj"
$publishProfile = "LauncherApp-win-x64"

Write-Host "Publishing Launcher.App ($Configuration) using profile '$publishProfile'..." -ForegroundColor Cyan

dotnet publish $projectPath -c $Configuration -p:PublishProfile=$publishProfile
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$publishOutput = Join-Path $PSScriptRoot "publish\win-x64"
Write-Host "Publish complete: $publishOutput" -ForegroundColor Green
