param(
    [Parameter(Mandatory = $true)]
    [string]$AppSettingsPath
)

$content = Get-Content -LiteralPath $AppSettingsPath -Raw
$match = [regex]::Match($content, '"Application"\s*:\s*\{[\s\S]*?"Version"\s*:\s*"([^"]+)"')

if (-not $match.Success) {
    exit 1
}

$match.Groups[1].Value
