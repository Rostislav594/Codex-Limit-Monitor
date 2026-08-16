param(
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$')]
    [string] $Version = "0.1.0"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactRoot = Join-Path $repositoryRoot "artifacts"
$projectPath = Join-Path $repositoryRoot "src\CodexLimitMonitor.App\CodexLimitMonitor.App.csproj"
$stagingDirectory = Join-Path $artifactRoot "staging\portable-win-x64"
$publishDirectory = Join-Path $artifactRoot "publish\win-x64"
$verificationDirectory = Join-Path $artifactRoot "verification\portable-win-x64"
$releaseDirectory = Join-Path $artifactRoot "release"
$archivePath = Join-Path $releaseDirectory "CodexLimitMonitor-$Version-win-x64.zip"
$temporaryArchivePath = "$archivePath.tmp.zip"
$checksumPath = "$archivePath.sha256"
$temporaryChecksumPath = "$checksumPath.tmp"
$distributionAuditScript = Join-Path $PSScriptRoot "Test-Distribution.ps1"

function Remove-ArtifactDirectory {
    param([Parameter(Mandatory)][string] $Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $artifactPrefix = [System.IO.Path]::GetFullPath($artifactRoot).TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith($artifactPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a directory outside the artifact root: $fullPath"
    }

    if (Test-Path -LiteralPath $fullPath) {
        $reparsePoints = @(Get-ChildItem -LiteralPath $fullPath -Force -Recurse -ErrorAction Stop |
            Where-Object { ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 })
        $rootItem = Get-Item -LiteralPath $fullPath -Force
        if (($rootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 -or
            $reparsePoints.Count -ne 0) {
            throw "Refusing to recursively remove an artifact directory containing reparse points: $fullPath"
        }

        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
}

function Get-DistributionManifest {
    param([Parameter(Mandatory)][string] $Path)

    $root = [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
    Get-ChildItem -LiteralPath $root -Recurse -File |
        Sort-Object FullName |
        ForEach-Object {
            $relativePath = $_.FullName.Substring($root.Length + 1)
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            "$relativePath|$($_.Length)|$hash"
        }
}

foreach ($requiredPath in @(
    $projectPath,
    (Join-Path $repositoryRoot "README.md"),
    (Join-Path $repositoryRoot "docs\troubleshooting.md"),
    $distributionAuditScript
)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required release input is missing: $requiredPath"
    }
}

Remove-ArtifactDirectory $stagingDirectory
Remove-ArtifactDirectory $verificationDirectory
New-Item -ItemType Directory -Force -Path $stagingDirectory, $releaseDirectory | Out-Null

dotnet publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishProfile=win-x64 `
    "-p:PublishDir=$stagingDirectory" `
    -p:Version=$Version
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Get-ChildItem -LiteralPath $stagingDirectory -Filter "*.pdb" -File -Recurse |
    Remove-Item -Force

$publishedDocsDirectory = Join-Path $stagingDirectory "docs"
New-Item -ItemType Directory -Force -Path $publishedDocsDirectory | Out-Null
Copy-Item (Join-Path $repositoryRoot "README.md") $stagingDirectory -Force
Copy-Item (Join-Path $repositoryRoot "docs\troubleshooting.md") $publishedDocsDirectory -Force

& $distributionAuditScript -Path $stagingDirectory

Remove-ArtifactDirectory $publishDirectory
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $publishDirectory) | Out-Null
Move-Item -LiteralPath $stagingDirectory -Destination $publishDirectory

Remove-Item -LiteralPath $temporaryArchivePath, $temporaryChecksumPath -Force -ErrorAction SilentlyContinue
$archiveItems = @(Get-ChildItem -LiteralPath $publishDirectory | Sort-Object Name)
if ($archiveItems.Count -eq 0) {
    throw "Portable publish directory is empty."
}

Compress-Archive `
    -LiteralPath $archiveItems.FullName `
    -DestinationPath $temporaryArchivePath `
    -CompressionLevel Optimal

New-Item -ItemType Directory -Force -Path $verificationDirectory | Out-Null
Expand-Archive -LiteralPath $temporaryArchivePath -DestinationPath $verificationDirectory
& $distributionAuditScript -Path $verificationDirectory

$publishedManifest = @(Get-DistributionManifest $publishDirectory)
$archiveManifest = @(Get-DistributionManifest $verificationDirectory)
$manifestDifference = @(Compare-Object $publishedManifest $archiveManifest)
if ($manifestDifference.Count -ne 0) {
    throw "Archive verification failed: extracted files differ from the publish directory."
}

$checksum = (Get-FileHash -LiteralPath $temporaryArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content `
    -LiteralPath $temporaryChecksumPath `
    -Value "$checksum *$(Split-Path -Leaf $archivePath)" `
    -Encoding ascii

Move-Item -LiteralPath $temporaryArchivePath -Destination $archivePath -Force
Move-Item -LiteralPath $temporaryChecksumPath -Destination $checksumPath -Force

$verifiedChecksum = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($verifiedChecksum -ne $checksum) {
    throw "Archive checksum verification failed."
}

Remove-ArtifactDirectory $verificationDirectory

Write-Host "Portable release: $archivePath"
Write-Host "SHA-256: $checksum"
