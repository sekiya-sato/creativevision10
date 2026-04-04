param(
    [Parameter(Mandatory = $true)]
    [string]$AppSettingsPath,

    [switch]$Increment
)

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$content = [System.IO.File]::ReadAllText($AppSettingsPath, $utf8NoBom)
$match = [regex]::Match($content, '"Application"\s*:\s*\{[\s\S]*?"Version"\s*:\s*"([^"]+)"')

if (-not $match.Success) {
    exit 1
}

$version = $match.Groups[1].Value

if ($Increment) {
    $versionParts = $version.Split('.')

    if ($versionParts.Length -ne 3) {
        exit 1
    }

    $patchNumber = 0
    if (-not [int]::TryParse($versionParts[2], [ref]$patchNumber)) {
        exit 1
    }

    $patchNumber++
    $newVersion = '{0}.{1}.{2}' -f $versionParts[0], $versionParts[1], $patchNumber
    $updatedContent = [regex]::Replace(
        $content,
        '((?:"Application"\s*:\s*\{[\s\S]*?)"Version"\s*:\s*")([^"]+)(")',
        {
            param($matched)
            $matched.Groups[1].Value + $newVersion + $matched.Groups[3].Value
        },
        1
    )

    if ($updatedContent -eq $content) {
        exit 1
    }

    [System.IO.File]::WriteAllText($AppSettingsPath, $updatedContent, $utf8NoBom)
    $version = $newVersion
}

$version
