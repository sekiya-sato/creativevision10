param(
    # Application.Version を読み書きする appsettings.json のパス。
    [Parameter(Mandatory = $true)]
    [string]$AppSettingsPath,

    # 指定時は major.minor.patch の patch だけを +1 して保存する。
    [switch]$Increment
)

# Windows PowerShell 5.x でも日本語コメントを壊さないよう UTF-8(BOMなし) を明示する。
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)

# JSON コメントを含む appsettings.json をそのまま扱うため、JSON パーサーではなくテキストとして読む。
$content = [System.IO.File]::ReadAllText($AppSettingsPath, $utf8NoBom)

# Application セクション内の Version だけを配布バージョンとして抽出する。
$match = [regex]::Match($content, '"Application"\s*:\s*\{[\s\S]*?"Version"\s*:\s*"([^"]+)"')

# Version が見つからない場合は呼び出し元の bat で検知できるよう終了コードだけ返す。
if (-not $match.Success) {
    exit 1
}

$version = $match.Groups[1].Value

if ($Increment) {
    # publish 時の自動採番は x.y.z 形式だけを許可し、patch 以外は変更しない。
    $versionParts = $version.Split('.')

    if ($versionParts.Length -ne 3) {
        exit 1
    }

    # patch が数値でない場合は不正な配布バージョンとして停止する。
    $patchNumber = 0
    if (-not [int]::TryParse($versionParts[2], [ref]$patchNumber)) {
        exit 1
    }

    # patch を +1 した値を、新しい Application.Version として作る。
    $patchNumber++
    $newVersion = '{0}.{1}.{2}' -f $versionParts[0], $versionParts[1], $patchNumber

    # Application セクション内の Version だけを置換し、他の設定やコメントは維持する。
    $updatedContent = [regex]::Replace(
        $content,
        '((?:"Application"\s*:\s*\{[\s\S]*?)"Version"\s*:\s*")([^"]+)(")',
        {
            # 置換文字列の $1 誤解釈を避けるため、MatchEvaluator で安全に連結する。
            param($matched)
            $matched.Groups[1].Value + $newVersion + $matched.Groups[3].Value
        },
        1
    )

    # 置換対象が変わらない場合は、意図した Version 更新ができていないため停止する。
    if ($updatedContent -eq $content) {
        exit 1
    }

    # UTF-8(BOMなし) のまま appsettings.json へ更新内容を書き戻す。
    [System.IO.File]::WriteAllText($AppSettingsPath, $updatedContent, $utf8NoBom)
    $version = $newVersion
}

# bat の for /f で受け取れるよう、最終的な Version だけを標準出力する。
$version
