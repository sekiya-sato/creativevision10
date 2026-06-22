---
name: author-printstream-qfm
description: Documents the repo-proven safe subset for authoring PrintStream qfm files in cv10. Covers Shift_JIS XML structure under printform/, page/region/record/text/image patterns, CSV data binding, script and font/PDF cautions, and validation steps. Use when creating or reviewing qfm report forms in this repository.
---

# PrintStream qfm Authoring Guide

このスキルは、`cv10` リポジトリで実際に運用されている PrintStream 帳票 (`.qfm`) の作り方を、**repo 実績ベース**でまとめたものです。

公開情報は FormEditor GUI 中心で、ベンダーが raw qfm XML の完全仕様を公開していることまでは確認できていません。一方で、この repo には `printform/*.qfm` として Shift_JIS(cp932) の XML 帳票が実在し、`CvPrints/PrintAdapter.cs` から `FormWriter` に渡して動作しています。

したがって、このスキルでは次の方針を採ります。

- **この repo では qfm を Shift_JIS(cp932) の XML テンプレートとして管理する。**
- **既存 qfm を雛形にして最小差分で編集する。**
- **CHM は概念理解と高度機能の補助資料として使う。**
- **既存帳票に実績のある要素・属性の範囲を安全な実用サブセットとして扱う。**

## 参照優先度

1. `printform/*.qfm`
   - 実運用されている帳票本体。最優先で参照する。
2. `CvPrints/PrintAdapter.cs`
   - `FormWriter` に何を渡しているか、実行時に何が必要かを示す。
3. `.agents/skills/add-print-process-master-mente/SKILL.md`
   - この repo における qfm 作成・配置・検証の運用ルール。
4. `.agents/skills/add-print-process-master-mente/scripts/validate_qfm.py`
   - 現在の repo 共通 qfm validator。
5. `refer/printdll/PrintStream.chm`
   - `page / region / record / text / image / script / font / PDF` などの概念・補助仕様。

## まず守るべき基本ルール

- 保存文字コードは **Shift_JIS(cp932)** を使う。
- XML 宣言は `encoding="SHIFT_JIS"` または `encoding="shift_jis"` を使う。
- 現在の repo 標準は **`printform/` 配下・A4縦・CSV `data.txt` 入力**。
- `itemN` の定義順、CSV/SQL の列順、`datasrc="itemN"` の対応を必ず一致させる。
- 既存帳票をコピーして必要箇所だけ変える。ゼロから独自構造を発明しない。
- ユーザーが生成結果を否定して中止や差し戻しを指示した場合は、追加検討を止め、触った qfm と一時出力だけを即時に戻す。

## 最小骨格

この repo で最も安全な最小骨格は次です。

```xml
<?xml version="1.0" encoding="SHIFT_JIS"?>

<printstream version="3.0">
    <datadesc src="file">
        <datarecord>
            <item id="item1"><position offset="0" length="12"/></item>
            <item id="item2"><position offset="0" length="80"/></item>
        </datarecord>
        <file>
            <path datatype="csv" target="data.txt"/>
        </file>
    </datadesc>

    <page orientation="portrait" cpi="10" lpi="6" compatibility="3.0.0" id="Form1" tree="1">
        <position x="8" y="8" width="156" height="272"/>
    </page>
</printstream>
```

### 骨格の意味

- `<printstream version="3.0">`
  - repo 既存帳票の共通ルート。
- `<datadesc src="file">`
  - ファイル入力を使う定義。
- `<datarecord>`
  - 論理項目定義。`item1`, `item2`... を並べる。
- `<position offset="0" length="..."/>`
  - 項目長さ。repo の CSV 前提では `offset="0"` が継続利用されている。
- `<path datatype="csv" target="data.txt"/>`
  - この repo 標準の入力。validator もここを前提にする。
- `<page ...>`
  - 用紙設定。既存帳票はほぼこの A4縦テンプレート。

## 要素ごとの読み方

### page

CHM の `FormEditor/part4_2.html` にある通り、`page` は帳票全体の用紙設定です。

```xml
<page orientation="portrait" cpi="10" lpi="6" compatibility="3.0.0" id="Form1" tree="1">
    <position x="8" y="8" width="156" height="272"/>
</page>
```

- `orientation="portrait"`: 縦向き
- `cpi="10" lpi="6"`: 既存帳票の標準値
- `position x="8" y="8" width="156" height="272"`: 現行 validator が期待する A4縦基本位置

### region

CHM の `FormEditor/part4_3.html` にある通り、`region` はレコードの印刷領域です。

```xml
<region id="Rgn01" direction="0" tree="1">
    <position x="0" y="10" width="150" height="248"/>
    <color transparent="1"/>
</region>
```

- 一覧帳票は通常 `region` の中に `record` を置く。
- CHM 上はリージョン接続も可能だが、この repo ではまず単一 `region` を優先する。

### record

CHM の `FormEditor/part4_4.html` では、`record` はレイアウト単位です。

この repo では主に次の使い方があります。

- **データレコード**: 明細 1 件ごとに繰り返す本体行
- **ヘッダ相当レコード**: 見出し行
- **単票レコード**: 1 枚の中にラベル + 値を並べるレコード

CHM にはヘッダー/フッター/フィルレコードの概念がありますが、この repo で `recordtype` 数値の完全対応表までは確認できていません。**既存帳票で使われている値だけを踏襲**してください。

### text

CHM の `FormEditor/part4_5.html` にある通り、テキストフィールドは主に次のデータタイプを持ちます。

- 固定データ
- 日付と時刻
- ページ番号
- 連番
- 合計
- 件数
- データ項目

repo の qfm では主に以下を使っています。

#### 固定文字

```xml
<data calctype="static">社員マスタリスト</data>
<decode datatype="string" format=""/>
```

#### 印刷データ項目

```xml
<data calctype="item" datasrc="item5"/>
<decode datatype="string" format=""/>
```

`datasrc` は `datarecord/item` に定義した `itemN` と一致させる。

#### 実行日時

```xml
<data calctype="date"/>
<decode datatype="date" format="&quot;出力日付: &quot;YYYY/MM/DD HH24:MI:SS"/>
```

#### ページ番号

```xml
<data calctype="page"/>
<decode datatype="number" format="&quot;P.&quot;0999"/>
```

### decode / format

repo 実例で確認できる定番書式は次です。

```xml
<decode datatype="string" format=""/>
<decode datatype="date" format="&quot;出力日付: &quot;YYYY/MM/DD HH24:MI:SS"/>
<decode datatype="number" format="&quot;P.&quot;0999"/>
```

日時文字列の切り出し表示には、既存 qfm で次のような書式が使われています。

```xml
<decode datatype="string" format="S0.4&quot;/&quot;S4.2&quot;/&quot;S6.2&quot; &quot;S8.2&quot;:&quot;S10.2&quot;:&quot;S12.2"/>
```

新しい書式文字列を発明する前に、**既存 qfm の format 例を流用**してください。

### image

CHM の `FormEditor/part4_6.html` では、イメージフィールドは BMP/JPEG/PNG を扱えます。

repo 側では `image` 要素を使う帳票パターンがあり、次のように `itemN` から画像ファイルパスを受け取れます。

```xml
<image id="Img01" relative="0">
    <position x="120" y="1" width="32" height="40"/>
    <color transparent="1"/>
    <data calctype="item" datasrc="item26"/>
</image>
```

注意点:

- 画像は固定データでもデータ項目でも指定可能。
- フォームファイルと同じフォルダから読む相対パス運用ができる。
- 画像埋め込みも可能。
- サーバ実行時は**サーバから参照できるファイル配置**が必要。

### barcode

CHM の `FormEditor/part4_7.html` では CODE39 / JAN / NW-7 / ITF / 郵便 / CODE128 / EAN128 / QR などが説明されています。

ただし、この repo の qfm 実例ではバーコード帳票の蓄積が少ないため、**このスキルではバーコードを主題にしません**。必要になった場合は CHM と既存前例を確認してから追加してください。

cv10 の商品バーコード帳票を触る場合は、まず同系統の既存 qfm を比較してください。`MasterPrintBarcode002.qfm` は `group level="1" pagechange="1"` により 1 商品 1 ページの挙動になり、複数商品を 1 ページへ詰める系統では `MasterPrintBarcodeSho.qfm`、`MasterPrintBarcodeCode39.qfm`、`MasterPrintBarcodeNw7.qfm` の `pagechange="0"` 構造が近い前例です。

## 帳票パターンの選び方

### 一覧帳票

最初の雛形は次を優先します。

- `printform/MasterShainMente.qfm`
- `printform/MasterMeishoMente.qfm`

特徴:

- `region` + 複数 `record` 構成
- 1 つのデータレコードを繰り返し出力
- 見出し行を別 `record` に切り出し
- 上部にタイトル、出力日時、ページ番号

### 単票帳票

最初の雛形は次を使います。

- `printform/MasterSysKanriMente.qfm`

特徴:

- ラベル (`static`) と値 (`item`) を対にした単票レイアウト
- 1 画面の情報をそのまま紙面へ展開しやすい

## データ定義の実務ルール

### itemN の設計

- `item1`, `item2`... の順序は、CSV または SQL の列順と一致させる。
- `length` は表示幅ではなく、データ項目の想定長さ。
- repo 既存帳票でよく使う目安:
  - コード: 10〜12
  - 日時文字列: 14
  - 名称: 60〜120
  - 電話番号: 20〜30
  - メモ: 100 以上

### text の幅設計

- `text/position width` は紙面レイアウト幅
- `item/position length` はデータ長
- この 2 つは別物として考える
- 一覧領域では原則 `x + width <= 150` を守る

## データ入力

CHM では次の入力形式が説明されています。

- CSV (`FormEditor/part5_12.html`)
- 固定長テキスト (`FormEditor/part5_13.html`)
- XML (`FormEditor/part5_14.html`)

ただし、この repo で現在の標準運用として確認できるのは **CSV + `data.txt`** です。

```xml
<file>
    <path datatype="csv" target="data.txt"/>
</file>
```

CHM (`part5_12.html`) では複数レコード様式を持つ CSV も説明されていますが、この repo の既存帳票では単純 CSV を主に使っています。**複数レコード区分 CSV は高度機能**として扱ってください。

## PrintStream スクリプト

CHM の `FormEditor/part7_*` にある通り、PrintStream には JavaScript 互換のスクリプト機能があります。

### 基本姿勢

- まずは **スクリプトなしで表現できる帳票に寄せる**。
- データ整形、ページ制御、パラメータ分岐が必要なときだけ使う。

### CHM で確認できる代表 API

- `GetQfm()`
- `ScriptVersion("3.1")`
- `qfm.GetParameter(symbol)`
- `qfm.GetPage()`
- `qfm.GetCursor()`
- `page.GetField(name)`
- `page.AssignData()`
- `cursor.GetValue(itemName)` / `cursor.SetValue(itemName, value)`
- `qfm.End()`

### 注意点

- `GetRecordCount()` は先読みを伴うため、大量データでは遅くなる。
- `PrepareKeyBreak()` と `CalculateTotal()` は、グループや集計を伴う手動カーソル制御で必要になる。
- 実行パラメータは `GetParameter()` で取得する。
- マルチバイト文字を扱う場合は `substr` 系と `substrB` 系を混同しない。

## フォント / PDF の注意

CHM の `part9_*`, `part13_*` に基づく実務ルールです。

### まず優先すること

- 可能なら `QFM` 系プリセットフォントを使う。
- PDF で運用する帳票は、PDF 差異を前提に確認する。

### プリセットフォント

- `QFM Gothic`
- `QFM Mincho`
- `QFM TimesRoman`
- `QFM Courier`
- `QFM Gothic JP/CN/TW/KR`
- `QFM Mincho JP/CN/TW/KR`

### 非プリセットフォント

- 標準印刷系では `.far` のフォント情報ファイルが必要。
- PDF では `.far` ではなく**フォント埋め込み**が必要。
- PDF は標準印刷と完全一致しない。太字、長文、自動改行、網掛けで差が出やすい。

`CvPrints/PrintAdapter.cs` では現在 `FormWriter.PDF` を使っているため、**最終確認は PDF 実出力ベース**で考えること。

## 推奨ワークフロー

1. 最も近い既存 qfm を選ぶ
   - 一覧: `MasterShainMente.qfm`, `MasterMeishoMente.qfm`
   - 単票: `MasterSysKanriMente.qfm`
2. `printform/` にコピーしてファイル名を決める
3. XML 宣言と `<printstream version="3.0">` を維持する
4. `datarecord/item` を新しい列順に合わせる
5. `datasrc="itemN"` を列順に合わせて修正する
6. `decode` は既存パターンから流用する
7. 画像やスクリプトは最後に追加する
8. Shift_JIS(cp932) で保存する
9. validator を実行する
10. 実際の印刷経路で PDF を確認する

## 検証

repo 共通 validator は次です。

```powershell
python .agents\skills\add-print-process-master-mente\scripts\validate_qfm.py printform\MasterXxxMente.qfm
```

```bash
python3 .agents/skills/add-print-process-master-mente/scripts/validate_qfm.py printform/MasterXxxMente.qfm
```

このスクリプトは主に次を見ています。

- Shift_JIS(cp932) で読めるか
- XML 宣言の encoding が Shift_JIS 系か
- ルート要素が `printstream` か
- `datadesc/file/path` が存在し、`datatype="csv"` `target="data.txt"` か
- `page orientation="portrait"` か
- A4縦の基本 `position x="8" y="8" width="156" height="272"` か
- `datarecord/item` が存在するか

この validator は**現在の repo 運用ルール**を確認するもので、ベンダー仕様の完全検証ではありません。

legacy qfm の position warning は、それだけで失敗扱いにしない。Shift_JIS(cp932) で読めること、XML と `itemN` / `datasrc` 対応が壊れていないこと、PDF 出力結果が要求形状に合うことを分けて確認する。

## ロールバック

- ユーザーが「中止」「想定と違う」「すぐに戻す」と指示したら、追加編集や再提案を挟まず rollback を先に行う。
- Git 操作が `.git/index.lock` などで失敗する場合は、`git cat-file blob HEAD:<path>` や `git checkout-index -f -- <path>` で対象 qfm を HEAD から復元する。
- `tmp/pdfs/`、`printform/data.txt`、preview PDF など、検証で作った一時成果物も合わせて片付ける。

## このスキルがカバーしないこと

- ベンダーが raw qfm XML の完全仕様を保証している、という主張
- 任意の XML を手書きすれば必ず動く、という主張
- FormEditor が不要である、という断定
- repo 例だけで PrintStream の全機能を網羅している、という主張
- `recordtype` 数値や未使用要素の意味を、前例なしに推測で決めること

## 関連スキル

- `add-print-process-master-mente`
  - ViewModel 側の `FormFile`, `PrintBySqlParam`, `PrintByCsvParam`, F6 印刷配線まで含めて進めるときに使う。
- `wpf-project-guide`
  - WPF 側の画面修正も同時に行うときに先に読む。

## CHM の当たり先

`refer/printdll/PrintStream.chm` のうち、今回の主題に近いページは次です。

- `FormEditor/part4_2.html` ページ
- `FormEditor/part4_3.html` リージョン
- `FormEditor/part4_4.html` レコード
- `FormEditor/part4_5.html` テキストフィールド
- `FormEditor/part4_6.html` イメージフィールド
- `FormEditor/part4_7.html` バーコードフィールド
- `FormEditor/part5_3.html` 印刷データとフィールドの対応
- `FormEditor/part5_6.html` テキストファイル入力
- `FormEditor/part5_12.html` CSV
- `FormEditor/part5_13.html` 固定長テキスト
- `FormEditor/part5_14.html` XML
- `FormEditor/part7_12.htm` グローバル関数
- `FormEditor/part7_13.htm` String オブジェクト
- `FormEditor/part7_14.htm` フォームオブジェクト
- `FormEditor/part7_15.htm` カーソルオブジェクト
- `FormEditor/part7_16.htm` ページオブジェクト
- `FormEditor/part11_2.html` 実行パラメータ
- `FormEditor/part11_3.html` 実行パラメータの定義
- `FormEditor/part11_6.html` 実行パラメータをスクリプトで使う例
- `FormEditor/part9_1.html` フォントとテキスト描画
- `FormEditor/part9_2.html` フォント情報ファイル
- `FormEditor/part13_1.html` PDF
- `FormEditor/part13_2.html` フォント埋め込み
- `FormEditor/part13_4.html` 標準印刷との差異

## 最後に

この repo で qfm を安全に増やすコツは、**新規作成ではなく既存帳票のコピーから始めること**です。FormEditor の概念を理解しつつ、repo で実績のある XML パターンを維持し、Shift_JIS・`data.txt`・`itemN` 対応・validator・実 PDF 確認の 5 点を外さなければ、大きく踏み外しにくくなります。
