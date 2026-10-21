# デモ用画像をマスタCD順に割り当てる。DBはcv-sqliteから出力したJSONを参照する。
[CmdletBinding(DefaultParameterSetName = 'Rename')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Rename')]
    [string]$CodesPath,
    [Parameter(Mandatory, ParameterSetName = 'Restore')]
    [string]$RestoreManifest,
    [string]$ServerRoot = (Join-Path $PSScriptRoot '../../../../CvServer'),
    [switch]$Apply,
    [string]$ManifestPath
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$root = (Resolve-Path -LiteralPath $ServerRoot).Path
$items = [Collections.Generic.List[object]]::new()
$beforeCounts = @{}

function Get-ImageDirectory([string]$folder) {
    if ($folder -notin @('img', 'imgshain')) { throw "対象外フォルダ: $folder" }
    $directory = Get-Item -LiteralPath (Join-Path $root $folder)
    if (!$directory.PSIsContainer -or ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "通常フォルダを指定してください: $($directory.FullName)"
    }
    return $directory.FullName
}

function Get-ImagePath([string]$folder, [string]$name) {
    if ([string]::IsNullOrWhiteSpace($name) -or $name -ne [IO.Path]::GetFileName($name) -or
        $name.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or
        [IO.Path]::GetExtension($name) -ine '.jpg') { throw "不正な画像名: $name" }
    return Join-Path (Get-ImageDirectory $folder) $name
}

foreach ($folder in @('img', 'imgshain')) {
    $beforeCounts[$folder] = @(Get-ChildItem -LiteralPath (Get-ImageDirectory $folder) -File).Count
}

if ($PSCmdlet.ParameterSetName -eq 'Restore') {
    $manifest = Get-Content -LiteralPath $RestoreManifest -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.Version -ne 1 -or !$manifest.Items) { throw '対応していないマニフェストです。' }
    foreach ($entry in $manifest.Items) {
        $items.Add([pscustomobject]@{Folder=$entry.Folder; Source=$entry.Target; Target=$entry.Source; SHA256=$entry.SHA256})
    }
} else {
    $codes = Get-Content -LiteralPath $CodesPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($pair in @(@('img', 'MasterShohin'), @('imgshain', 'MasterShain'))) {
        $folder, $table = $pair
        if ($null -eq $codes.$table -or $codes.$table -isnot [array]) { throw "CD配列が必要です: $table" }
        $orderedCodes = [Collections.Generic.List[string]]::new()
        $uniqueCodes = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($code in $codes.$table) {
            if ($code -isnot [string] -or [string]::IsNullOrWhiteSpace($code) -or
                $code -match '[<>:"/\\|?*\x00-\x1f]|[. ]$|^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])([.]|$)' -or
                !$uniqueCodes.Add($code)) { throw "空・不正・重複CD: $table / $code" }
            $orderedCodes.Add($code)
        }
        $orderedCodes.Sort([StringComparer]::Ordinal)
        $files = @(Get-ChildItem -LiteralPath (Get-ImageDirectory $folder) -File | Where-Object Extension -ieq '.jpg')
        $byName = @{}
        foreach ($file in $files) { $byName[$file.Name] = $file }
        $sourceNames = [Collections.Generic.List[string]]::new()
        foreach ($file in $files) { $sourceNames.Add($file.Name) }
        $sourceNames.Sort([StringComparer]::Ordinal)
        $available = @($orderedCodes | Where-Object { !$byName.ContainsKey($_ + '.jpg') })
        $index = 0
        foreach ($name in $sourceNames) {
            $file = $byName[$name]
            $target = $name
            if (!$uniqueCodes.Contains($file.BaseName)) {
                if ($index -ge $available.Count) { throw "画像数に対してCDが不足しています: $table" }
                $target = $available[$index++] + '.jpg'
            }
            $items.Add([pscustomobject]@{Folder=$folder; Source=$name; Target=$target; SHA256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash})
        }
    }
}

# 変更前に全件を検査し、保持対象も含めて内容を記録・検証する。
$sources = @{}
$targets = @{}
foreach ($entry in $items) {
    $source = Get-ImagePath $entry.Folder $entry.Source
    $target = Get-ImagePath $entry.Folder $entry.Target
    $file = Get-Item -LiteralPath $source
    if ($file.PSIsContainer -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
        $sources.ContainsKey($source) -or $targets.ContainsKey($target) -or
        ($source -ine $target -and (Test-Path -LiteralPath $target)) -or
        (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $entry.SHA256) {
        throw "画像の競合・内容不一致: $source -> $target"
    }
    $sources[$source] = $true
    $targets[$target] = $true
}
$changes = @($items | Where-Object { $_.Source -cne $_.Target })
$changes | Format-Table Folder, Source, Target -AutoSize | Out-Host
Write-Host "変更: $($changes.Count)件 / 維持: $($items.Count - $changes.Count)件"
if (!$Apply -or $changes.Count -eq 0) { return }
if (!$ManifestPath) { throw '-Apply には新規の -ManifestPath が必要です。' }

$record = [ordered]@{Version=1; CreatedAt=(Get-Date).ToString('o'); ServerRoot=$root; Items=@($items.ToArray())}
$bytes = [Text.UTF8Encoding]::new($false).GetBytes(($record | ConvertTo-Json -Depth 6))
$stream = [IO.File]::Open([IO.Path]::GetFullPath($ManifestPath), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
$completed = [Collections.Generic.List[object]]::new()
try {
    foreach ($entry in $changes) {
        $source = Get-ImagePath $entry.Folder $entry.Source
        if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $entry.SHA256) { throw "実行直前に画像が変更されました: $source" }
        Rename-Item -LiteralPath $source -NewName $entry.Target -ErrorAction Stop
        $completed.Add($entry)
    }
    foreach ($entry in $items) {
        if ((Get-FileHash -LiteralPath (Get-ImagePath $entry.Folder $entry.Target) -Algorithm SHA256).Hash -ne $entry.SHA256) {
            throw "変更後の内容不一致: $($entry.Target)"
        }
    }
    foreach ($folder in @('img', 'imgshain')) {
        if (@(Get-ChildItem -LiteralPath (Get-ImageDirectory $folder) -File).Count -ne $beforeCounts[$folder]) { throw "件数不一致: $folder" }
    }
} catch {
    $failure = $_
    for ($i = $completed.Count - 1; $i -ge 0; $i--) {
        $entry = $completed[$i]
        Rename-Item -LiteralPath (Get-ImagePath $entry.Folder $entry.Target) -NewName $entry.Source -ErrorAction Stop
    }
    throw $failure
}
Write-Host "完了: $($changes.Count)件。件数・SHA256を検証しました。対応表: $ManifestPath"
