[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version = '1.0.4'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = Join-Path $repoRoot 'artifacts'
$payloadPath = Join-Path $artifactsRoot 'app'
$installerPath = Join-Path $artifactsRoot 'installer'
$appProject = Join-Path $repoRoot 'Eario.csproj'
$installerProject = Join-Path $PSScriptRoot 'Eario.Installer.wixproj'

New-Item -ItemType Directory -Path $payloadPath -Force | Out-Null
New-Item -ItemType Directory -Path $installerPath -Force | Out-Null

Write-Host "Publishing Eario $Version as a self-contained Windows application..."
dotnet publish $appProject `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:Version=$Version `
    --output $payloadPath

if ($LASTEXITCODE -ne 0) { throw 'Eario publish failed.' }

Write-Host 'Building the MSI package...'
dotnet build $installerProject `
    --configuration Release `
    -p:ProductVersion=$Version `
    -p:EarioPayload=$payloadPath `
    -p:OutputPath=$installerPath

if ($LASTEXITCODE -ne 0) { throw 'MSI build failed.' }

$msi = Get-ChildItem -Path $installerPath -Filter '*.msi' | Select-Object -First 1
if ($null -eq $msi) { throw 'The WiX build completed without producing an MSI.' }

Write-Host "Installer created: $($msi.FullName)"
