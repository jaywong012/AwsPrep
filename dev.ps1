<#
    Starts the API and the Vite dev server in separate windows.
    Usage:  .\dev.ps1
#>
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

Write-Host 'Starting AwsCertPrep.Api on http://localhost:5176 ...' -ForegroundColor Cyan
Start-Process powershell -ArgumentList @(
    '-NoExit', '-Command',
    "Set-Location '$root\server\AwsCertPrep.Api'; dotnet run --launch-profile http"
)

Write-Host 'Starting Vite dev server on http://localhost:5173 ...' -ForegroundColor Cyan
Start-Process powershell -ArgumentList @(
    '-NoExit', '-Command',
    "Set-Location '$root\client'; npm run dev"
)

Write-Host ''
Write-Host 'API      http://localhost:5176/api/health'
Write-Host 'OpenAPI  http://localhost:5176/openapi/v1.json'
Write-Host 'Web app  http://localhost:5173'
