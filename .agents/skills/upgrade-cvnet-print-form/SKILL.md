---
name: upgrade-cvnet-print-form
description: Replace an existing CV10 PrintStream QFM from a legacy cvnet reference folder and align its existing WPF report ViewModel SQL/CSV output. Use when a folder supplies a replacement .qfm, legacy PDF, d_sql.txt, and data.txt for an existing report.
---

# 旧cvnet QFM帳票の差し替えとSQL列調整

既存の `CvWpfclient` 帳票画面に対し、旧cvnetで出力された帳票一式を根拠として QFM と SQL 出力をそろえるスキル。

対象は新規画面移植ではない。既存の View / ViewModel / `printform` を保ったまま、旧帳票の列順・表示内容へ近づける。

## 適用条件

次がそろう参照フォルダを渡されたときに使う。

- 差し替え用の `.qfm`
- 旧帳票の `data.pdf`
- 旧SQLの列名リスト `d_sql.txt`
- そのPDFに使われた `data.txt`
- 対象となる既存の `CvWpfclient.Views.*` または対応 ViewModel

旧cvnetとCV10のDB・データは別物として扱う。旧 `data.txt` の値を現在DBへ合わせ込まず、列の意味・順序・帳票の見え方を移植根拠にする。

QFMの文法・cp932・PDF描画の詳細は、先に [`../author-printstream-qfm/SKILL.md`](../author-printstream-qfm/SKILL.md) を読む。View / ViewModel を変更する場合は `wpf-project-guide` と `wpf-view-workflow` も読む。

## 調査と詳細設計

編集前に、現在の対象 ViewModel と参照フォルダを比較する。

1. 現行 ViewModel の `FormFileName`、印刷SQL、SQLパラメーター、並び順を確認する。`PrintPdfService` が SQL 結果を `Dictionary<string, object>` としてCSV化することも確認する。
2. 旧 `d_sql.txt` の列名一覧、旧 `data.txt` の**CSVとしての列数**、QFM の `datarecord/item` と `datasrc` を確認する。CSVは単純なカンマ分割をせず、引用符を処理できるパーサーで数える。
3. 旧PDFを視認し、ヘッダ・明細・合計・住所・バーコード・固定文言のどの列が実際に使われるか確認する。
4. 現行の伝票・明細・マスタモデルと近い帳票SQLを調べ、旧列ごとの取得元または空欄理由を整理する。
5. cvsqlite MCPでは、まず `list_tables`、次に `describe_table` を使う。実データ確認は読み取り専用かつ `@p0` などのパラメーターを使う一文の `WITH ... SELECT` / `SELECT` とする。

以下は実装前にユーザーへ提示し、業務的な選択が残る場合は承認を得る。

- QFMをバイト同一で差し替えるか、CV10向け修正を許容するか
- 合計の取得元（例: 明細金額合計か税込合計か）
- 支払先・倉庫・自社情報のフォールバック
- 現行モデルにないFAX・メーカー品番・受注番号などを空欄にするか
- QFMのitem数と旧 `data.txt` 列数が違う場合の扱い

## 実装

### QFM

ユーザーが「そのまま上書き」と指定した場合は、参照 `.qfm` を `printform/` へ内容を変えずにコピーする。

- Shift_JIS(cp932) とCRLFを維持する。
- 元と差し替え先のハッシュを比較し、完全一致を確認する。
- 旧QFM固有のページ位置や末尾タブを、共通validatorの警告だけを理由に変更しない。警告の内容と、元ファイルにも存在することを報告する。

### ViewModel SQL

- SQLのSELECT順は、旧 `data.txt` / `d_sql.txt` とQFMの `itemN` に合わせる。
- ヘッダ値は帳票の既存パターンに従い、明細行へ繰り返す。
- 名称は伝票時点のV列を優先し、住所など現在マスタで補完する項目は理由を明記する。
- 未保有の項目を推測で埋めない。承認済みの空欄、または現行モデルから追跡できる値だけを出力する。

#### SELECT列の別名は必須

`PrintPdfService` は `RawExecCmd` の結果列名を `Dictionary<string, object>` のキーにする。同じ列・式を2回SELECTすると、`KingakuTotal` のような同名キーで帳票出力が失敗する。

そのため、帳票SQLの**全SELECT列**に列順どおりの一意な別名を付ける。QFMの列数に応じて `item1` から連番とし、必要なら `item99` 以降も続ける。

```sql
SELECT
    h.KingakuTotal AS item13,  /* 明細金額合計 */
    h.KingakuTotal AS item16,  /* 総合計 */
    ...
```

- 別名は重複させない。SQL本体で `AS itemN` を全列に明示する。
- 別名を変えても `Dictionary` の挿入順＝SELECT順が保たれるため、QFM側のCSV列順は変わらない。
- 完成時は `AS itemN` の件数、連番、ユニーク数を確認する。

## 検証

1. QFMがcp932で読め、XML構文・`printstream`・CSV `data.txt`・item定義を満たすことを確認する。
2. `d_sql.txt` の列名とSQLのitemコメントを照合し、列数・順序・名称の差異を報告する。
3. cvsqlite MCPで、画面入力と同じ期間・条件をパラメーターで渡した完全SQLを実行する。実データの行数、SELECT列数、列名の一意性を確認する。
4. `CvWpfclient/CvWpfclient.csproj` をビルドする。
5. `git diff --check` と変更ファイルのCRLFを確認する。QFMをバイト同一コピーした場合に既存末尾空白が検出されたら、その由来を区別して報告する。
6. 可能なら旧 `data.txt` でQFMをローカルPDF描画し、旧 `data.pdf` とレイアウトを比較する。ライセンスまたは実行環境が不足する場合は、その制約と未確認範囲を明記する。

画面からの実PDF出力を確認していない場合は、完了報告に明記する。ログ・コミットはリポジトリの `AGENTS.md` とユーザーの明示指示に従い、依頼がない限りコミットしない。