param(
    [Parameter(Mandatory = $true)]
    [int]$ProcessId,
    [Parameter(Mandatory = $true)]
    [string]$TargetPath,
    [Parameter(Mandatory = $true)]
    [string]$DeltaPath,
    [Parameter(Mandatory = $true)]
    [string]$DeltaDigest,
    [Parameter(Mandatory = $true)]
    [string]$TargetDigest,
    [string]$OutputPath,
    [switch]$VerifyOnly
)

$ErrorActionPreference = 'Stop'
$maximumTargetSize = 250MB
$magic = [System.Text.Encoding]::ASCII.GetBytes('ALDELTA1')

function Get-Sha256Digest([string]$Path) {
    $stream = [System.IO.File]::OpenRead($Path)
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        return 'sha256:' + ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $stream.Dispose()
        $algorithm.Dispose()
    }
}

function ConvertTo-HexString([byte[]]$Bytes) {
    return ([BitConverter]::ToString($Bytes)).Replace('-', '').ToLowerInvariant()
}

function Read-ExactBytes([System.IO.BinaryReader]$Reader, [int]$Count) {
    $bytes = $Reader.ReadBytes($Count)
    if ($bytes.Length -ne $Count) {
        throw 'The update delta is truncated.'
    }
    return ,$bytes
}

$target = [System.IO.Path]::GetFullPath($TargetPath)
$delta = [System.IO.Path]::GetFullPath($DeltaPath)
if (-not (Test-Path -LiteralPath $target -PathType Leaf) -or
    -not (Test-Path -LiteralPath $delta -PathType Leaf) -or
    $DeltaDigest -notmatch '^sha256:[0-9a-fA-F]{64}$' -or
    $TargetDigest -notmatch '^sha256:[0-9a-fA-F]{64}$') {
    throw 'The update paths or SHA-256 digests are invalid.'
}

if ((Get-Sha256Digest $delta) -ne $DeltaDigest.ToLowerInvariant()) {
    throw 'The update delta failed its SHA-256 integrity check.'
}

if (-not $VerifyOnly) {
    try {
        Wait-Process -Id $ProcessId -Timeout 120 -ErrorAction Stop
    }
    catch [Microsoft.PowerShell.Commands.ProcessCommandException] {
    }
}

$stage = if ($VerifyOnly) {
    [System.IO.Path]::GetFullPath($OutputPath)
}
else {
    $target + '.update'
}

$source = [System.IO.File]::OpenRead($target)
$rawDelta = [System.IO.File]::OpenRead($delta)
$gzip = [System.IO.Compression.GZipStream]::new($rawDelta, [System.IO.Compression.CompressionMode]::Decompress)
$reader = [System.IO.BinaryReader]::new($gzip)
$destination = $null
$backup = $null
$replaced = $false
$preserveStage = $false

try {
    $header = Read-ExactBytes $reader 8
    if ((ConvertTo-HexString $header) -ne (ConvertTo-HexString $magic)) {
        throw 'The update delta format is not supported.'
    }

    $expectedBaseHash = 'sha256:' + (ConvertTo-HexString (Read-ExactBytes $reader 32))
    $embeddedTargetHash = 'sha256:' + (ConvertTo-HexString (Read-ExactBytes $reader 32))
    $targetLength = $reader.ReadUInt64()
    $operationCount = $reader.ReadUInt32()
    if ($targetLength -gt $maximumTargetSize -or $operationCount -gt 1000000) {
        throw 'The update delta exceeds its allowed size or operation count.'
    }
    if ((Get-Sha256Digest $target) -ne $expectedBaseHash) {
        throw 'This update delta does not match the installed program version.'
    }
    if ($embeddedTargetHash -ne $TargetDigest.ToLowerInvariant()) {
        throw 'The update target digest does not match the release metadata.'
    }

    $destination = [System.IO.File]::Open($stage, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    $buffer = [byte[]]::new(81920)
    for ([uint32]$index = 0; $index -lt $operationCount; $index++) {
        $operation = $reader.ReadByte()
        if ($operation -eq 0) {
            $offset = $reader.ReadUInt64()
            $length = $reader.ReadUInt32()
            if ($offset + $length -gt [uint64]$source.Length) {
                throw 'An update delta copy operation exceeds the installed file.'
            }
            [void]$source.Seek([long]$offset, [System.IO.SeekOrigin]::Begin)
            [uint32]$remaining = $length
            while ($remaining -gt 0) {
                $read = $source.Read($buffer, 0, [int][Math]::Min($buffer.Length, $remaining))
                if ($read -le 0) {
                    throw 'The installed program ended unexpectedly while applying the update.'
                }
                $destination.Write($buffer, 0, $read)
                $remaining -= [uint32]$read
            }
        }
        elseif ($operation -eq 1) {
            $length = $reader.ReadUInt32()
            if ($length -gt $maximumTargetSize) {
                throw 'An update delta literal exceeds the allowed size.'
            }
            $literal = Read-ExactBytes $reader ([int]$length)
            $destination.Write($literal, 0, $literal.Length)
        }
        else {
            throw 'The update delta contains an unknown operation.'
        }
        if ($destination.Length -gt $targetLength) {
            throw 'The update delta produced an oversized executable.'
        }
    }
    $destination.Flush()
    $destination.Dispose()
    $destination = $null

    if ((Get-Item -LiteralPath $stage).Length -ne $targetLength -or
        (Get-Sha256Digest $stage) -ne $TargetDigest.ToLowerInvariant()) {
        throw 'The reconstructed update failed its size or SHA-256 integrity check.'
    }

    if ($VerifyOnly) {
        $preserveStage = $true
        return
    }

    $backup = $target + '.previous'
    if (Test-Path -LiteralPath $backup) {
        Remove-Item -LiteralPath $backup -Force
    }
    [System.IO.File]::Move($target, $backup)
    [System.IO.File]::Move($stage, $target)
    $replaced = $true

    try {
        Start-Process -FilePath $target -ErrorAction Stop | Out-Null
        Remove-Item -LiteralPath $backup -Force
    }
    catch {
        Remove-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue
        [System.IO.File]::Move($backup, $target)
        throw
    }
}
catch {
    if ($replaced -and $backup -and (Test-Path -LiteralPath $backup)) {
        Remove-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue
        [System.IO.File]::Move($backup, $target)
    }
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show(
        "The update could not be applied. Your existing installation was preserved.`n`n$($_.Exception.Message)",
        'AlphaLedger update failed',
        [System.Windows.MessageBoxButton]::OK,
        [System.Windows.MessageBoxImage]::Error) | Out-Null
    exit 1
}
finally {
    if ($destination) { $destination.Dispose() }
    if ($reader) { $reader.Dispose() }
    if ($gzip) { $gzip.Dispose() }
    if ($rawDelta) { $rawDelta.Dispose() }
    if ($source) { $source.Dispose() }
    if (-not $preserveStage -and (Test-Path -LiteralPath $stage)) {
        Remove-Item -LiteralPath $stage -Force -ErrorAction SilentlyContinue
    }
}
