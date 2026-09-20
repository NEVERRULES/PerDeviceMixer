[CmdletBinding()]
param(
    [string]$Version,

    [string]$Target,

    [string]$Title,

    [string]$NotesFile,

    [switch]$Stable,

    [switch]$SkipBuild,

    [switch]$KeepOlderLocalVersions
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = $PSScriptRoot
$versionPropertiesPath = Join-Path $repositoryRoot "Directory.Build.props"
$versionProperties = [xml](Get-Content -LiteralPath $versionPropertiesPath -Raw)
$sourceVersion = [string]$versionProperties.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $sourceVersion
}
elseif (-not [string]::Equals($Version, $sourceVersion, [StringComparison]::Ordinal)) {
    throw "Requested release version '$Version' does not match source version '$sourceVersion'."
}

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI is required. Install it and run 'gh auth login' first."
}

Push-Location $repositoryRoot
try {
    gh auth status | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "GitHub CLI is not authenticated." }

    $workingTreeChanges = @(git status --porcelain)
    if ($LASTEXITCODE -ne 0) { throw "Unable to inspect the Git working tree." }
    if ($workingTreeChanges.Count -ne 0) {
        throw "The working tree must be clean before publishing a GitHub Release."
    }

    if ([string]::IsNullOrWhiteSpace($Target)) {
        $Target = [string](git rev-parse HEAD)
        if ($LASTEXITCODE -ne 0) { throw "Unable to resolve HEAD." }
    }
    if ([string]::IsNullOrWhiteSpace($Title)) {
        $Title = "PerDeviceMixer $Version"
    }

    $tag = "v$Version"
    gh release view $tag *> $null
    if ($LASTEXITCODE -eq 0) {
        throw "GitHub Release '$tag' already exists; refusing to overwrite it."
    }

    if (-not $SkipBuild.IsPresent) {
        & (Join-Path $repositoryRoot "build-distribution.ps1") -Version $Version
        if ($LASTEXITCODE -ne 0) { throw "Distribution build failed." }
    }

    $releaseRoot = Join-Path $repositoryRoot "artifacts\release"
    $versionRoot = Join-Path $releaseRoot $Version
    $assetRoot = Join-Path $versionRoot "assets"
    $checksumPath = Join-Path $assetRoot "SHA256SUMS.txt"
    if (-not (Test-Path -LiteralPath $checksumPath)) {
        throw "Release checksum file was not found: $checksumPath"
    }

    $assets = @(
        Join-Path $assetRoot "PerDeviceMixer-$Version-win-x64-Portable.zip"
        Join-Path $assetRoot "PerDeviceMixer-$Version-win-x64-Setup.exe"
        $checksumPath
    )
    foreach ($asset in $assets) {
        if (-not (Test-Path -LiteralPath $asset -PathType Leaf)) {
            throw "Release asset was not found: $asset"
        }
    }

    foreach ($line in Get-Content -LiteralPath $checksumPath) {
        if ($line -notmatch '^([0-9a-fA-F]{64})\s+(.+)$') {
            throw "Invalid checksum line: $line"
        }
        $expectedHash = $matches[1]
        $assetPath = Join-Path $assetRoot $matches[2]
        $actualHash = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash
        if (-not [string]::Equals($expectedHash, $actualHash, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Local checksum verification failed for '$assetPath'."
        }
    }

    $arguments = @(
        "release", "create", $tag,
        "--target", $Target,
        "--title", $Title
    )
    if ($Stable.IsPresent) {
        $arguments += "--latest"
    }
    else {
        $arguments += "--prerelease"
    }
    if ([string]::IsNullOrWhiteSpace($NotesFile)) {
        $arguments += "--generate-notes"
    }
    else {
        $resolvedNotesFile = [IO.Path]::GetFullPath($NotesFile)
        if (-not (Test-Path -LiteralPath $resolvedNotesFile -PathType Leaf)) {
            throw "Release notes file was not found: $resolvedNotesFile"
        }
        $arguments += @("--notes-file", $resolvedNotesFile)
    }
    $arguments += $assets

    gh @arguments
    if ($LASTEXITCODE -ne 0) { throw "GitHub Release upload failed." }

    $releaseJson = gh release view $tag --json assets
    if ($LASTEXITCODE -ne 0) { throw "Unable to verify the uploaded GitHub Release." }
    $remoteAssets = ($releaseJson | ConvertFrom-Json).assets
    foreach ($asset in $assets) {
        $name = [IO.Path]::GetFileName($asset)
        $remote = @($remoteAssets | Where-Object name -EQ $name)
        if ($remote.Count -ne 1) {
            throw "GitHub Release asset verification failed for '$name'."
        }
        $localHash = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
        $remoteDigest = [string]$remote[0].digest
        if (-not [string]::Equals(
                $remoteDigest,
                "sha256:$localHash",
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "GitHub digest did not match the local file for '$name'."
        }
    }

    if (-not $KeepOlderLocalVersions.IsPresent) {
        $resolvedReleaseRoot = [IO.Path]::GetFullPath($releaseRoot).TrimEnd('\') + '\'
        $resolvedCurrentVersion = [IO.Path]::GetFullPath($versionRoot)
        $removedBytes = 0L
        foreach ($directory in Get-ChildItem -LiteralPath $releaseRoot -Directory) {
            $resolvedDirectory = [IO.Path]::GetFullPath($directory.FullName)
            if (-not $resolvedDirectory.StartsWith(
                    $resolvedReleaseRoot,
                    [StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing to remove a directory outside the release artifact root."
            }
            if ([string]::Equals(
                    $resolvedDirectory,
                    $resolvedCurrentVersion,
                    [StringComparison]::OrdinalIgnoreCase)) {
                continue
            }

            $removedBytes += [long]((Get-ChildItem -LiteralPath $resolvedDirectory -File -Recurse |
                    Measure-Object Length -Sum).Sum)
            Remove-Item -LiteralPath $resolvedDirectory -Recurse -Force
        }
        Write-Host ("Removed older local releases: {0:N2} MiB" -f ($removedBytes / 1MB))
    }

    Write-Host "GitHub Release verified: $tag"
    Write-Host "Local release retained: $versionRoot"
}
finally {
    Pop-Location
}
