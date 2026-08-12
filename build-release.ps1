[CmdletBinding()]
param(
    [ValidateSet("win-x64")]
    [string]$Runtime = "win-x64",

    [switch]$SelfContained,

    [switch]$SingleFile,

    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = $PSScriptRoot
$solutionPath = Join-Path $repositoryRoot "PerDeviceMixer.slnx"
$appProjectPath = Join-Path $repositoryRoot "src\PerDeviceMixer.App\PerDeviceMixer.App.csproj"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot "artifacts\PerDeviceMixer-$Runtime"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK 10 is required. Install it from https://dotnet.microsoft.com/download/dotnet/10.0"
}

Push-Location $repositoryRoot
try {
    dotnet restore $solutionPath
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }

    dotnet build $solutionPath -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed." }

    dotnet test $solutionPath -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed." }

    dotnet restore $appProjectPath -r $Runtime
    if ($LASTEXITCODE -ne 0) { throw "runtime-specific dotnet restore failed." }

    $publishArguments = @(
        "publish",
        $appProjectPath,
        "-c", "Release",
        "-r", $Runtime,
        "--no-restore",
        "--self-contained", $SelfContained.IsPresent.ToString().ToLowerInvariant(),
        "-o", $OutputDirectory
    )

    if ($SingleFile.IsPresent) {
        $publishArguments += @(
            "-p:PublishSingleFile=true",
            "-p:IncludeNativeLibrariesForSelfExtract=true",
            "-p:EnableCompressionInSingleFile=true"
        )
    }

    dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

    Write-Host "Published PerDeviceMixer to: $OutputDirectory"
}
finally {
    Pop-Location
}
