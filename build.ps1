param([string]$OutputDirectory = '')

$ErrorActionPreference = 'Stop'
$requiredSdkPattern = '^10\.0\.4(0[2-9]|[1-9]\d) '
$repositoryRoot = $PSScriptRoot
$publishOutput = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $repositoryRoot 'artifacts\publish'
} elseif ([IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory
} else {
    Join-Path $repositoryRoot $OutputDirectory
}

if (-not (dotnet --list-sdks | Select-String -Pattern $requiredSdkPattern)) {
    throw ".NET SDK 10.0.402 or a later 10.0.4xx patch is required. Install that SDK and run the build again."
}

$env:DOTNET_CLI_HOME = Join-Path $repositoryRoot '.dotnet-home'
$env:APPDATA = Join-Path $repositoryRoot '.appdata'
$env:NUGET_PACKAGES = Join-Path $repositoryRoot '.nuget\packages'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

$project = Join-Path $repositoryRoot 'src\Cropwell\Cropwell.csproj'
$config = Join-Path $repositoryRoot 'NuGet.Config'
$offlineSource = Join-Path $repositoryRoot '.nuget\offline'
$restoreArguments = @(
    $project,
    '--runtime', 'win-x64',
    '--configfile', $config,
    '--source', 'https://api.nuget.org/v3/index.json',
    '--property:NuGetAudit=false'
)
if (Test-Path -LiteralPath $offlineSource) {
    $restoreArguments += @('--source', $offlineSource, '--ignore-failed-sources')
}

dotnet restore @restoreArguments
if ($LASTEXITCODE -ne 0) { throw "Cropwell restore failed with exit code $LASTEXITCODE." }

dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --no-restore `
    --output $publishOutput `
    --configfile $config

if ($LASTEXITCODE -ne 0) { throw "Cropwell publish failed with exit code $LASTEXITCODE." }
