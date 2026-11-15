<#
.SYNOPSIS
    Publishes the Windows application of BarsiG.

.DESCRIPTION
    The result is the file BarsiG.exe with the approved icon of BarsiG. The application
    starts the server of the simulation inside its own process, so the user never has to
    type an address.

.PARAMETER SelfContained
    Publishes a self contained build, so the application also runs on a computer without
    the .NET runtime.

.EXAMPLE
    .\scripts\publish-desktop.ps1
    .\scripts\publish-desktop.ps1 -SelfContained
#>
[CmdletBinding()]
param(
    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root 'publish\desktop'

$arguments = @(
    'publish'
    (Join-Path $root 'src\FinanceFlow.Desktop')
    '-c', 'Release'
    '-o', $output
)

if ($SelfContained) {
    $arguments += @('-r', 'win-x64', '--self-contained', 'true')
}

dotnet @arguments

if ($LASTEXITCODE -ne 0) {
    throw 'The publish of the desktop application failed.'
}

Write-Host ''
Write-Host "BarsiG.exe was published in $output" -ForegroundColor Green
Write-Host 'Start it with:' -ForegroundColor DarkGray
Write-Host "  $output\BarsiG.exe" -ForegroundColor DarkGray
