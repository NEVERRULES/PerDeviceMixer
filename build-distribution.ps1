[CmdletBinding()]
param(
    [string]$Version = "0.4.0-preview.1",

    [ValidateSet("win-x64")]
    [string]$Runtime = "win-x64"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = $PSScriptRoot
$artifactRoot = Join-Path $repositoryRoot "artifacts\release\$Version"
$publishDirectory = Join-Path $artifactRoot "publish"
$outputDirectory = Join-Path $artifactRoot "assets"
$portableBaseName = "PerDeviceMixer-$Version-$Runtime-Portable"
$setupBaseName = "PerDeviceMixer-$Version-$Runtime-Setup"
$portableArchive = Join-Path $outputDirectory "$portableBaseName.zip"
$checksumFile = Join-Path $outputDirectory "SHA256SUMS.txt"
$installerScript = Join-Path $repositoryRoot "installer\PerDeviceMixer.iss"
$releaseBuilder = Join-Path $repositoryRoot "build-release.ps1"
$languageDirectory = Join-Path $artifactRoot "installer-language"
$chineseMessagesFile = Join-Path $languageDirectory "ChineseSimplified.isl"
$chineseMessagesUrl = "https://raw.githubusercontent.com/jrsoftware/issrc/791ae13f404dd74012fe7ad6f660521dcfb815b7/Files/Languages/ChineseSimplified.isl"
$chineseMessagesSha256 = "e0b0b350e2245f3c5e65586dfe43d574f6e7f06f2261149aba284954b3fc9a8d"

$resolvedRepositoryRoot = [IO.Path]::GetFullPath($repositoryRoot).TrimEnd('\') + '\'
$resolvedArtifactRoot = [IO.Path]::GetFullPath($artifactRoot)
if (-not $resolvedArtifactRoot.StartsWith($resolvedRepositoryRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Artifact directory must stay inside the repository."
}

if (Test-Path -LiteralPath $resolvedArtifactRoot) {
    [IO.Directory]::Delete($resolvedArtifactRoot, $true)
}
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $languageDirectory -Force | Out-Null

Invoke-WebRequest -Uri $chineseMessagesUrl -OutFile $chineseMessagesFile
$actualMessagesHash = (Get-FileHash -LiteralPath $chineseMessagesFile -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualMessagesHash -ne $chineseMessagesSha256) {
    throw "The downloaded Inno Setup Chinese translation did not match the pinned SHA-256 hash."
}

& $releaseBuilder `
    -Runtime $Runtime `
    -SelfContained `
    -SingleFile `
    -OutputDirectory $publishDirectory
if ($LASTEXITCODE -ne 0) { throw "Self-contained application publish failed." }

Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $portableArchive -CompressionLevel Optimal

$innoCandidates = @(
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)
$innoCompiler = $innoCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $innoCompiler) {
    throw "Inno Setup 6 was not found. Install it with: winget install --id JRSoftware.InnoSetup -e"
}

$numericVersionParts = [regex]::Matches($Version, '\d+') | ForEach-Object Value
if ($numericVersionParts.Count -lt 3) {
    throw "Version must contain at least three numeric components."
}
$numericVersion = @($numericVersionParts + @("0", "0", "0", "0"))[0..3] -join "."

& $innoCompiler `
    "/DAppVersion=$Version" `
    "/DVersionInfoVersion=$numericVersion" `
    "/DSourceDir=$publishDirectory" `
    "/DOutputDir=$outputDirectory" `
    "/DOutputBaseFilename=$setupBaseName" `
    "/DChineseMessagesFile=$chineseMessagesFile" `
    $installerScript
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed." }

$releaseAssets = Get-ChildItem -LiteralPath $outputDirectory -File |
    Where-Object Extension -in ".exe", ".zip" |
    Sort-Object Name
$checksumLines = foreach ($asset in $releaseAssets) {
    $hash = Get-FileHash -LiteralPath $asset.FullName -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $($asset.Name)"
}
$checksumLines | Set-Content -LiteralPath $checksumFile -Encoding ASCII

Write-Host "Release assets created in: $outputDirectory"
$releaseAssets + (Get-Item -LiteralPath $checksumFile) |
    Select-Object Name, Length |
    Format-Table -AutoSize
