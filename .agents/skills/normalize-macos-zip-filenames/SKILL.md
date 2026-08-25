---
name: normalize-macos-zip-filenames
description: macOSで作成したZIP内の日本語ファイル名をWindows 11で扱いやすいUnicode NFC形式へ変換し、エントリ内容を保持したZIPを作成するときに使う。
---

# macOS ZIP内日本語ファイル名の正規化

macOSで圧縮されたZIPでは、日本語の濁点・半濁点が分解形式（例: `カ` + 結合濁点）で保存されることがある。Windows 11で展開したときの表示・参照不整合を避けるため、ZIP内エントリ名だけをUnicode NFC（Form C）へ変換する。

## 適用条件

- 対象はZIP内のファイル名・ディレクトリ名の文字化けまたはUnicode分解であり、ファイル内容の文字コード変換ではない。
- 入力ZIPの圧縮データを展開して再圧縮するため、ZIP全体のバイナリハッシュは変わる。各エントリの展開後バイト列は変えてはならない。
- `__MACOSX` のAppleDoubleエントリを含む場合も、対象ZIP内の全エントリ名を同じ規則で処理する。不要なエントリ削除はこのスキルの範囲外。

## 標準手順

1. 入力パスは `-LiteralPath` 相当で扱い、空白・日本語を含むパスをワイルドカード展開しない。
2. まず出力先を別ZIPにする。出力先を省略すると、ヘルパーは入力名に `.nfc.zip` を付ける。
3. ZIPを読み込み、各エントリ名を `System.Text.NormalizationForm.FormC` で正規化する。
4. 正規化後のエントリ名が重複する場合は変換を中止する。Windowsで衝突しやすい大文字小文字違いも同時に検出する。
5. 一時ZIPへエントリをコピーし、エントリ数とSHA-256（展開後バイト列）を入力ZIPと比較する。
6. 検証成功後にだけ出力先へ移動する。元ZIPを置き換える場合は、ユーザーが明示したときだけ `-ReplaceInput` を使う。

## 推奨ヘルパー

[`scripts/normalize-zip-filenames.ps1`](scripts/normalize-zip-filenames.ps1) を使う。

別ZIPを作成する:

```powershell
pwsh -File .agents/skills/normalize-macos-zip-filenames/scripts/normalize-zip-filenames.ps1 `
  -InputPath 'refer/Pie POS API Spec.v1.4.zip' `
  -OutputPath 'refer/Pie POS API Spec.v1.4.nfc.zip'
```

ユーザーが元ZIPの置換を明示した場合:

```powershell
pwsh -File .agents/skills/normalize-macos-zip-filenames/scripts/normalize-zip-filenames.ps1 `
  -InputPath 'refer/Pie POS API Spec.v1.4.zip' -ReplaceInput
```

`-ReplaceInput` は一時ファイルの作成、内容検証、元パスへの移動を一連で行う。実行前に、対象が依頼された単独ZIPであることを確認する。

## 完了条件

- ZIPを再度読み込める。
- 入力と出力のエントリ数が一致する。
- 出力の全エントリ名がNFC形式で、正規化後の重複がない。
- 入力と出力で対応するエントリの展開後SHA-256が一致する。
- 一時ファイルが残っていない。

## 扱わないこと

- PDF、YAML、画像などエントリ内容の編集・文字コード変換
- `__MACOSX` や `.DS_Store` の削除
- ZIP外側のファイル名変更
- ユーザーの明示なしの元ファイル上書き
