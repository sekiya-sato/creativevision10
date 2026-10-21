# デモ環境用データ・画像調整ツール

DBのマスタと画像の対応をデモ用に調整するための手順・ツールをまとめる。
現時点の対象は商品・社員画像のリネームで、DBは読み取りのみ。画像の内容とマスタの実際の人物・商品が一致することは保証しない。

## 前提・割当規則

- Windows PowerShell 5.1以降、またはWindows上のPowerShell 7。追加ライブラリ不要。
- デモ用の `CvServer/img` と `CvServer/imgshain` を対象とし、作業中は画像の追加・変更を止める。
- 直下の `.jpg` のみ対象。サブフォルダ、その他の拡張子は変更しない。
- `MasterShohin.Code` / `MasterShain.Code` と同じベース名の画像は維持する（Windowsの大文字小文字を区別しない）。
- それ以外の画像を元ファイル名のOrdinal昇順に並べ、未配置のCDのOrdinal昇順で先頭から `<Code>.jpg` に割り当てる。先頭ゼロを保持する。
- ファイル数分だけ割り当てる。CD不足、空CD、Windowsで使えないCD、大小文字を無視した重複CD、出力先競合は全件変更前に停止する。
- 既定は予定表示のみ。`-Apply` 時は新規マニフェストへの保存を必須とし、上書きなしでリネームする。
- 維持対象を含むSHA256とフォルダのファイル件数を検証する。通常の途中エラーでは完了済みのリネームを逆順に戻す。

## 1. cv-sqliteでCD一覧を取得

cv-sqliteの `list_tables` → `describe_table` で両テーブルと `Code` を確認してから、次のSELECTを実行する。
接続先が対象デモ環境のDBであることを確認する（このMCPの既定接続先は `server-user52.db`）。

```sql
SELECT Code FROM MasterShohin ORDER BY Code COLLATE BINARY LIMIT @p0 OFFSET @p1;
-- 社員は別のquery呼出しで取得する。
SELECT Code FROM MasterShain ORDER BY Code COLLATE BINARY LIMIT @p0 OFFSET @p1;
```

各queryで `parameters: [1000, 0]`、`maxRows: 1000` を指定し、OFFSETを1000ずつ増やして全件取得する。
最後が1000行なら次のページも確認する。`truncated=true` は取得漏れとして扱う。
各結果の `rows` の第1列を連結し、以下のオブジェクト形式でUTF-8の `master-codes.json` に保存する。
MCP応答全体やエスケープされた文字列のまま保存しない。取得日時・接続先も作業記録に残す。
巨大な `json_group_array` の1セル出力はMCPのセル長制限で切れるため使用しない。
ツールからMCPへ接続する処理は含まず、取得済みの一覧を入力する。

```json
{"MasterShohin":["100101001","100101002"],"MasterShain":["000001","000003"]}
```

これは形式例。実行時は全件の一覧を使用し、JSON・対応表はリポジトリ外の作業フォルダに置く。

## 2. 予定確認・実行

リポジトリルートで実行する。`C:\demo-work` は事前に作成した作業フォルダ例。
`-ServerRoot` を省略すると、このツールを配置したリポジトリの `CvServer` を対象とする。

```powershell
$tool = '.\Doc\spec\tools\demo-environment\Rename-DemoImages.ps1'
& $tool -CodesPath C:\demo-work\master-codes.json -ServerRoot C:\demo\CvServer
& $tool -CodesPath C:\demo-work\master-codes.json -ServerRoot C:\demo\CvServer `
    -Apply -ManifestPath C:\demo-work\rename-20261021.json
```

予定を確認してから実行する。同じCD一覧で再実行すると全画像が維持対象になり、変更は0件。
DBの更新、画像内容の変換、画像の追加・削除は行わない。

## 3. 復元

マニフェストには維持画像を含む相対ファイル名・SHA256を保存する。
復元時も予定表示が既定で、現在の内容とマニフェストが一致すること、復元先に競合がないことを確認する。

```powershell
& $tool -RestoreManifest C:\demo-work\rename-20261021.json -ServerRoot C:\demo\CvServer
& $tool -RestoreManifest C:\demo-work\rename-20261021.json -ServerRoot C:\demo\CvServer `
    -Apply -ManifestPath C:\demo-work\restore-20261021.json
```

プロセス強制終了や電源断では自動復元できない場合があるため、事前にフォルダをバックアップする。
一部だけ変更された場合はマニフェストと現状を照合し、全件復元コマンドを無理に再実行しない。

## 初回実施記録（2026-10-21）

- cv-sqliteの `MasterShohin` / `MasterShain` を参照し、商品58枚・社員2枚をリネーム。
- 商品: `00211161001.jpg` → `100101001.jpg` から、`00211664009.jpg` → `100101233.jpg` まで。
- 社員: `000017.jpg` → `000001.jpg`、`900016.jpg` → `000004.jpg`。既存 `000003.jpg` は維持。
- 全61枚のSHA256と件数を確認済み。
- 初回は対話操作で実施。変更時の対応表を相対パスのVersion 1形式に変換した [history/2026-10-21-image-rename.json](history/2026-10-21-image-rename.json) を保存。
- この履歴を `-RestoreManifest` に指定して初回変更を復元できる（既定は予定表示）。履歴に画像そのものやマスタ全件は含めない。
