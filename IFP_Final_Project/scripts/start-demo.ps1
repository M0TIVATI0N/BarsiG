<#
.SYNOPSIS
    Starts the backend of BarsiG with the local demonstration settings.

.DESCRIPTION
    The script starts the existing final demo database. It never creates or stores an
    administrator password.

.PARAMETER Port
    The port of the local server. The default 5099 is almost always free.

.PARAMETER Desktop
    Also publishes and starts the Windows application of BarsiG.

.EXAMPLE
    .\scripts\start-demo.ps1
    .\scripts\start-demo.ps1 -Desktop
#>
[CmdletBinding()]
param(
    [int]$Port = 5099,
    [switch]$Desktop
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$databasePath = Join-Path $root 'local-demo-admin\financeflow-admin-demo.db'

if (-not (Test-Path -LiteralPath $databasePath -PathType Leaf)) {
    throw "The primary demo database does not exist: $databasePath. Initialize it first with scripts\start-isolated-admin-demo.ps1."
}

$previousConnection = [Environment]::GetEnvironmentVariable('ConnectionStrings__FinanceFlow', 'Process')
$previousPhoneVerification = [Environment]::GetEnvironmentVariable('PhoneVerification__EnableLocalDemo', 'Process')

try {
    [Environment]::SetEnvironmentVariable(
        'ConnectionStrings__FinanceFlow',
        "Data Source=$databasePath",
        'Process')
    [Environment]::SetEnvironmentVariable('PhoneVerification__EnableLocalDemo', 'true', 'Process')

    Write-Host 'BarsiG - Digital Banking Simulator' -ForegroundColor Green
    Write-Host "The web application will run on http://localhost:$Port" -ForegroundColor DarkGray
    Write-Host "The primary demo database: $databasePath" -ForegroundColor DarkGray
    Write-Host ''

    if ($Desktop) {
        Write-Host 'Publishing the Windows application...' -ForegroundColor Cyan
        dotnet publish (Join-Path $root 'src\FinanceFlow.Desktop') -c Release -o (Join-Path $root 'publish\desktop')

        if ($LASTEXITCODE -ne 0) {
            throw 'The publish of the desktop application failed.'
        }

        Write-Host 'Starting BarsiG.exe ...' -ForegroundColor Cyan
        Start-Process -FilePath (Join-Path $root 'publish\desktop\BarsiG.exe') -WorkingDirectory (Join-Path $root 'publish\desktop')
    }

    dotnet run --project (Join-Path $root 'src\FinanceFlow.Web') -- --urls "http://localhost:$Port"
}
finally {
    [Environment]::SetEnvironmentVariable(
        'ConnectionStrings__FinanceFlow',
        $previousConnection,
        'Process')
    [Environment]::SetEnvironmentVariable(
        'PhoneVerification__EnableLocalDemo',
        $previousPhoneVerification,
        'Process')
}
