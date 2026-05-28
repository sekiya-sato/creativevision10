---
name: create-printform-qfm
description: cv10 の printform 配下に Shift_JIS の PrintStream qfm 帳票ファイルを作成・更新する。CSV item mapping、帳票レイアウト確認、ViewModel の FormFile / PrintBySqlParam / PrintByCsvParam 配線、WPF PDF 出力検証が必要なときに使う。
---

# Create PrintForm QFM

`printform/` 配下に新しい `.qfm` を追加するとき、または WPF 画面を `BaseMenteViewModel.DoOutputPdfCommand` の PDF 出力へ接続するときに使う。

## Workflow

1. 対象画面と印刷データソースを確認する。
   - 最初に対象 ViewModel を読む。印刷が動く画面には `FormFile` と、`PrintBySqlParam` または `PrintByCsvParam` のどちらか一方の override が必要。
   - View に `DoOutputPdfCommand` だけを追加しても、ViewModel が帳票ファイル名と印刷データを返さない場合は `BaseMenteViewModel` の警告表示だけで終わる。
2. 現在の印刷フローの起点を読む。
   - `CvServer/Services/PrintPdf.cs`
   - `CvServer/appsettings.json`
   - `printform/MasterMeishoMente.qfm`
   - `CvWpfclient/ViewModels/01Master/MasterMeishoMenteViewModel.cs`
3. qfm を編集する前に CSV 契約を決める。
   - `PrintPdf.printPre` は Shift_JIS で `data.txt` を書き出す。
   - qfm は `<path datatype="csv" target="data.txt"/>` を読む。
   - `item1`, `item2`, ... は CSV の位置項目。SQL の `select` 列順と qfm の `datarecord/item` 順を合わせる。
   - レイアウトで使う `<data calctype="item" datasrc="itemN"/>` には、必ず対応する `<item id="itemN">` を置く。
4. 近い既存 qfm から帳票を作る。
   - 一覧型の帳票なら `printform/MasterMeishoMente.qfm` を出発点にする。
   - 静的な日本語ラベルを保持するため、XML 宣言は `encoding="SHIFT_JIS"` のまま、ファイルも Shift_JIS で保存する。
   - 印刷アダプタ側の要件が変わっていなければ、`printstream version="3.0"`、`page compatibility="3.0.0"`、`target="data.txt"` を維持する。
5. レイアウトとラベルは保守的に更新する。
   - 現行 qfm では `Rec02 recordtype="1"` がヘッダー行のパターン。
   - `Rec01` が繰り返し明細行のパターン。
   - ラベルは `calctype="static"`、CSV データは `calctype="item"`、出力日時は `calctype="date"`、ページ番号は `calctype="page"` を使う。
   - `MasterMeishoMente.qfm` の固定幅フィールドと日付書式を優先して流用する。
6. ViewModel を配線する。
   - `protected override string? FormFile => "NewFormName.qfm";` を設定する。
   - qfm の item 順と同じ列順の SQL で `PrintBySqlParam` を返す。
   - 必須フィルターが未選択の場合は `null` を返し、基底クラスの警告がそのまま使えるようにする。
7. 実行時配置を確認する。
   - リポジトリ上の帳票ソースは `printform/`。
   - サーバー実行時は `PrintBaseDir` + `PrintFormDir` から帳票を読む。既定値は `./form`。
   - packaging やローカル実行で `printform/` から `form/` へのコピーが必要なら、作業内で実装または手順化する。

## Validation

変更した qfm には同梱バリデータを実行する。

```powershell
python .agents\skills\create-printform-qfm\scripts\validate_qfm.py printform\NewFormName.qfm
```

コードも変更した場合は通常の確認を続けて実行する。

```powershell
git diff --check
C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"
```

qfm だけの変更で C# / XAML を触っていない場合は、通常はバリデータと `git diff --check` で足りる。

## Reference

印刷パイプライン、qfm XML チェックリスト、qfm 関連 commit のレビュー観点が必要なときは `references/qfm-print-flow.md` を読む。
