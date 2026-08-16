param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$distributionRoot = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
if (-not (Test-Path -LiteralPath $distributionRoot -PathType Container)) {
    throw "Distribution path is not a directory: $distributionRoot"
}

$files = @(Get-ChildItem -LiteralPath $distributionRoot -Recurse -File)
if ($files.Count -eq 0) {
    throw "Distribution directory is empty: $distributionRoot"
}

$forbiddenFileNames = @(
    "auth.json",
    "credentials.json",
    "cookie.json",
    "cookies.json",
    ".env",
    ".env.local",
    "appsettings.development.json",
    "settings.json",
    "monitor.jsonl",
    "monitor.jsonl.previous"
)
$forbiddenExtensions = @(
    ".dmp",
    ".jsonl",
    ".key",
    ".log",
    ".p12",
    ".pdb",
    ".pem",
    ".pfx",
    ".suo",
    ".tmp",
    ".trace",
    ".user"
)
$fileViolations = [System.Collections.Generic.List[string]]::new()

foreach ($file in $files) {
    $relativePath = $file.FullName.Substring($distributionRoot.TrimEnd('\').Length + 1)
    if ($forbiddenFileNames -contains $file.Name.ToLowerInvariant()) {
        $fileViolations.Add("forbidden file name: $relativePath")
    }

    if ($forbiddenExtensions -contains $file.Extension.ToLowerInvariant()) {
        $fileViolations.Add("forbidden file extension: $relativePath")
    }
}

$contentPatterns = @(
    [pscustomobject]@{
        Name = "Windows user profile path"
        Regex = [regex]::new('[A-Za-z]:\\Users\\[^\\/:*?"<>|\r\n\x00]{1,80}\\', 'IgnoreCase,CultureInvariant')
    },
    [pscustomobject]@{
        Name = "Unix user profile path"
        Regex = [regex]::new('/(?:home|Users)/[A-Za-z0-9._-]+/', 'CultureInvariant')
    },
    [pscustomobject]@{
        Name = "private key"
        Regex = [regex]::new('-----BEGIN(?: [A-Z0-9]+)? PRIVATE KEY-----', 'CultureInvariant')
    },
    [pscustomobject]@{
        Name = "OpenAI-style secret"
        Regex = [regex]::new('\bsk-(?:proj-|svcacct-)?[A-Za-z0-9_-]{20,}\b', 'CultureInvariant')
    },
    [pscustomobject]@{
        Name = "bearer credential"
        Regex = [regex]::new('\bBearer\s+[A-Za-z0-9._~+/=-]{20,}\b', 'IgnoreCase,CultureInvariant')
    },
    [pscustomobject]@{
        Name = "JWT credential"
        Regex = [regex]::new('\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b', 'CultureInvariant')
    },
    [pscustomobject]@{
        Name = "AWS access key"
        Regex = [regex]::new('\bAKIA[0-9A-Z]{16}\b', 'CultureInvariant')
    },
    [pscustomobject]@{
        Name = "GitHub credential"
        Regex = [regex]::new('\bgh[pousr]_[A-Za-z0-9]{20,}\b', 'CultureInvariant')
    }
)

$contentViolations = [System.Collections.Generic.List[string]]::new()
$bufferSize = 1024 * 1024
$overlapSize = 4096

foreach ($file in $files) {
    $relativePath = $file.FullName.Substring($distributionRoot.TrimEnd('\').Length + 1)
    $stream = [System.IO.File]::OpenRead($file.FullName)
    try {
        $buffer = [byte[]]::new($bufferSize + $overlapSize)
        $overlapCount = 0
        while (($bytesRead = $stream.Read($buffer, $overlapCount, $bufferSize)) -gt 0) {
            $totalBytes = $overlapCount + $bytesRead
            $utf8Content = [System.Text.Encoding]::UTF8.GetString($buffer, 0, $totalBytes)
            $utf16Content = [System.Text.Encoding]::Unicode.GetString($buffer, 0, $totalBytes)

            foreach ($pattern in $contentPatterns) {
                if ($pattern.Regex.IsMatch($utf8Content) -or $pattern.Regex.IsMatch($utf16Content)) {
                    $violation = "$($pattern.Name): $relativePath"
                    if (-not $contentViolations.Contains($violation)) {
                        $contentViolations.Add($violation)
                    }
                }
            }

            $overlapCount = [Math]::Min($overlapSize, $totalBytes)
            [System.Array]::Copy(
                $buffer,
                $totalBytes - $overlapCount,
                $buffer,
                0,
                $overlapCount)
        }
    }
    finally {
        $stream.Dispose()
    }
}

$violations = @($fileViolations) + @($contentViolations)
if ($violations.Count -ne 0) {
    $summary = $violations | Sort-Object -Unique | ForEach-Object { " - $_" }
    throw "Distribution audit failed:`n$($summary -join [Environment]::NewLine)"
}

Write-Host "Distribution audit passed: $($files.Count) files checked."
