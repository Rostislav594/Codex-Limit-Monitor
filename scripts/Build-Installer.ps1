param(
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$')]
    [string] $Version = "0.1.0",

    [switch] $SkipPortablePublish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactRoot = Join-Path $repositoryRoot "artifacts"
$portablePublishScript = Join-Path $PSScriptRoot "Publish-Portable.ps1"
$distributionAuditScript = Join-Path $PSScriptRoot "Test-Distribution.ps1"
$installerScript = Join-Path $repositoryRoot "installer\CodexLimitMonitor.iss"
$publishDirectory = Join-Path $artifactRoot "publish\win-x64"
$stagingDirectory = Join-Path $artifactRoot "staging\installer"
$installerDirectory = Join-Path $artifactRoot "installer"
$setupFileName = "CodexLimitMonitor-$Version-win-x64-setup.exe"
$setupPath = Join-Path $installerDirectory $setupFileName
$checksumPath = "$setupPath.sha256"

function Remove-InstallerStagingDirectory {
    if (-not (Test-Path -LiteralPath $stagingDirectory)) {
        return
    }

    $fullPath = [System.IO.Path]::GetFullPath($stagingDirectory)
    $artifactPrefix = [System.IO.Path]::GetFullPath($artifactRoot).TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith($artifactPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove installer staging outside the artifact root: $fullPath"
    }

    $items = @(Get-ChildItem -LiteralPath $fullPath -Force -Recurse)
    $reparsePoints = @($items | Where-Object {
        ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0
    })
    $rootItem = Get-Item -LiteralPath $fullPath -Force
    if (($rootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 -or
        $reparsePoints.Count -ne 0) {
        throw "Refusing to remove installer staging containing reparse points: $fullPath"
    }

    Remove-Item -LiteralPath $fullPath -Recurse -Force
}

function Find-InnoCompiler {
    $configuredPath = [Environment]::GetEnvironmentVariable("INNO_SETUP_COMPILER", "Process")
    $candidates = @(
        $configuredPath,
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    $command = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    throw "Inno Setup 6 compiler was not found. Install JRSoftware.InnoSetup or set INNO_SETUP_COMPILER."
}

foreach ($requiredPath in @(
    $portablePublishScript,
    $distributionAuditScript,
    $installerScript
)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required installer input is missing: $requiredPath"
    }
}

if (-not $SkipPortablePublish) {
    & $portablePublishScript -Version $Version
}

& $distributionAuditScript -Path $publishDirectory

$innoCompiler = Find-InnoCompiler
Remove-InstallerStagingDirectory
New-Item -ItemType Directory -Force -Path $stagingDirectory, $installerDirectory | Out-Null

& $innoCompiler `
    "/DAppVersion=$Version" `
    "/O$stagingDirectory" `
    "/F$([System.IO.Path]::GetFileNameWithoutExtension($setupFileName))" `
    $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}

$stagedSetupPath = Join-Path $stagingDirectory $setupFileName
if (-not (Test-Path -LiteralPath $stagedSetupPath -PathType Leaf)) {
    throw "Inno Setup did not create the expected installer: $stagedSetupPath"
}

& $distributionAuditScript -Path $stagingDirectory

Remove-Item -LiteralPath $setupPath, $checksumPath -Force -ErrorAction SilentlyContinue
Move-Item -LiteralPath $stagedSetupPath -Destination $setupPath

$checksum = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content `
    -LiteralPath $checksumPath `
    -Value "$checksum *$setupFileName" `
    -Encoding ascii

Remove-InstallerStagingDirectory

$signature = Get-AuthenticodeSignature -LiteralPath $setupPath
Write-Host "Installer: $setupPath"
Write-Host "SHA-256: $checksum"
Write-Host "Authenticode: $($signature.Status)"
