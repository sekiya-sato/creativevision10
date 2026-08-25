[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InputPath,

    [string]$OutputPath,

    [switch]$ReplaceInput,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-EntryHash {
    param(
        [Parameter(Mandatory = $true)]
        [IO.Compression.ZipArchiveEntry]$Entry
    )

    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        $stream = $Entry.Open()
        try {
            return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '')
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $algorithm.Dispose()
    }
}

$inputFullPath = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $InputPath).Path)
if (-not $ReplaceInput -and [string]::IsNullOrWhiteSpace($OutputPath)) {
    $baseName = [IO.Path]::GetFileNameWithoutExtension($inputFullPath)
    $OutputPath = Join-Path ([IO.Path]::GetDirectoryName($inputFullPath)) "$baseName.nfc.zip"
}

if ($ReplaceInput) {
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        throw '-ReplaceInput と -OutputPath は同時に指定できません。'
    }
    $outputFullPath = $inputFullPath
}
else {
    $outputFullPath = [IO.Path]::GetFullPath($OutputPath)
    if ([string]::Equals($inputFullPath, $outputFullPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw '入力と出力が同じです。元ZIPを置き換える場合は -ReplaceInput を指定してください。'
    }
    if ((Test-Path -LiteralPath $outputFullPath) -and -not $Force) {
        throw "出力先が既に存在します。上書きする場合は -Force を指定してください: $outputFullPath"
    }
}

$tempPath = "$outputFullPath.$([Guid]::NewGuid().ToString('N')).tmp"
$source = $null
$archive = $null
$verificationSource = $null
$verificationOutput = $null
$success = $false

try {
    $source = [IO.Compression.ZipFile]::OpenRead($inputFullPath)
    $normalizedNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $changedCount = 0

    foreach ($entry in $source.Entries) {
        $normalizedName = $entry.FullName.Normalize([System.Text.NormalizationForm]::FormC)
        if (-not $normalizedNames.Add($normalizedName)) {
            throw "正規化後にWindows上で衝突するエントリ名があります: $normalizedName"
        }
        if (-not [string]::Equals($entry.FullName, $normalizedName, [StringComparison]::Ordinal)) {
            $changedCount++
        }
    }

    $archive = [IO.Compression.ZipFile]::Open(
        $tempPath,
        [IO.Compression.ZipArchiveMode]::Create
    )
    foreach ($entry in $source.Entries) {
        $normalizedName = $entry.FullName.Normalize([System.Text.NormalizationForm]::FormC)
        $newEntry = $archive.CreateEntry(
            $normalizedName,
            [IO.Compression.CompressionLevel]::Optimal
        )
        $newEntry.LastWriteTime = $entry.LastWriteTime

        if (-not $entry.FullName.EndsWith('/')) {
            $inputStream = $entry.Open()
            $outputStream = $newEntry.Open()
            try {
                $inputStream.CopyTo($outputStream)
            }
            finally {
                $outputStream.Dispose()
                $inputStream.Dispose()
            }
        }
    }
    $archive.Dispose()
    $archive = $null
    $source.Dispose()
    $source = $null

    $verificationSource = [IO.Compression.ZipFile]::OpenRead($inputFullPath)
    $verificationOutput = [IO.Compression.ZipFile]::OpenRead($tempPath)
    if ($verificationSource.Entries.Count -ne $verificationOutput.Entries.Count) {
        throw '入力と出力のエントリ数が一致しません。'
    }

    $sourceHashes = @{}
    foreach ($entry in $verificationSource.Entries) {
        $normalizedName = $entry.FullName.Normalize([System.Text.NormalizationForm]::FormC)
        $sourceHashes[$normalizedName] = Get-EntryHash -Entry $entry
    }
    foreach ($entry in $verificationOutput.Entries) {
        $outputHash = Get-EntryHash -Entry $entry
        if (-not $sourceHashes.ContainsKey($entry.FullName) -or
            -not [string]::Equals($sourceHashes[$entry.FullName], $outputHash, [StringComparison]::Ordinal)) {
            throw "エントリ内容のSHA-256が一致しません: $($entry.FullName)"
        }
        if (-not [string]::Equals(
                $entry.FullName,
                $entry.FullName.Normalize([System.Text.NormalizationForm]::FormC),
                [StringComparison]::Ordinal)) {
            throw "出力エントリ名がNFC形式ではありません: $($entry.FullName)"
        }
    }

    $verificationOutput.Dispose()
    $verificationOutput = $null
    $verificationSource.Dispose()
    $verificationSource = $null

    Move-Item -LiteralPath $tempPath -Destination $outputFullPath -Force
    $success = $true
    [pscustomobject]@{
        InputPath = $inputFullPath
        OutputPath = $outputFullPath
        EntryCount = $normalizedNames.Count
        RenamedEntryCount = $changedCount
        ContentVerification = 'SHA-256 OK'
    }
}
finally {
    if ($null -ne $archive) { $archive.Dispose() }
    if ($null -ne $source) { $source.Dispose() }
    if ($null -ne $verificationOutput) { $verificationOutput.Dispose() }
    if ($null -ne $verificationSource) { $verificationSource.Dispose() }
    if (-not $success -and (Test-Path -LiteralPath $tempPath)) {
        Remove-Item -LiteralPath $tempPath -Force
    }
}
