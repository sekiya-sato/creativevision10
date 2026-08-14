# すべての .cs と .xaml を走査して CRLF に変換
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)

Get-ChildItem -Recurse -Include *.cs, *.xaml | ForEach-Object {
    # ファイルの内容を文字列配列として読み込み
    $content = Get-Content -LiteralPath $_.FullName -Encoding utf8
    # 配列を CRLF (`r`n) で結合した単一の文字列にする
    $contentJson = $content -join "`r`n"
    # 末尾にも改行を追加して保存
    [System.IO.File]::WriteAllText($_.FullName, $contentJson + "`r`n", $utf8NoBom)
}
