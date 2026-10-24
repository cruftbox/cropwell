$ErrorActionPreference = 'Stop'
$requiredSdk = '10.0.402'
$repositoryRoot = $PSScriptRoot

if (-not (dotnet --list-sdks | Select-String -SimpleMatch "$requiredSdk [")) {
    throw ".NET SDK $requiredSdk is required. Install that SDK and run the tests again."
}

$env:DOTNET_CLI_HOME = Join-Path $repositoryRoot '.dotnet-home'
$env:APPDATA = Join-Path $repositoryRoot '.appdata'
$env:NUGET_PACKAGES = Join-Path $repositoryRoot '.nuget\packages'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

dotnet build (Join-Path $repositoryRoot 'tests\Cropwell.Tests\Cropwell.Tests.csproj') `
    --configuration Release `
    --ignore-failed-sources `
    --property:BuildingTests=true `
    --property:NuGetAudit=false `
    --configfile (Join-Path $repositoryRoot 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { throw "Cropwell test build failed with exit code $LASTEXITCODE." }

& (Join-Path $repositoryRoot 'tests\Cropwell.Tests\bin\Release\net10.0-windows\Cropwell.Tests.exe')
if ($LASTEXITCODE -ne 0) { throw "Cropwell tests failed with exit code $LASTEXITCODE." }
