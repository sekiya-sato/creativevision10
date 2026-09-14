---
name: upgrade-cvnet-print-form
description: Port a legacy cvnet report into an existing CV10 PrintStream form — replace printform/*.qfm from a reference folder and rewrite the WPF ViewModel print SQL to the legacy CSV column order. Use when given an instruction.md plus legacy cvnet assets (a .crs program, header/detail .qfm, and a real print spool folder with d_sql.txt / data.txt / data.pdf) for a report that already exists in CV10.
---

# 旧cvnet帳票のCV10移植（qfm差し替え＋印刷SQL整合）

既存の CvWpfclient 画面に対し、旧cvnetで実際に出力された帳票一式を根拠として qfm と印刷SQLをそろえるスキル。

新規画面の移植ではない。既存の View / ViewModel / `printform` を保ったまま、旧帳票の列順・見出し・見え方へ寄せる。

## 適用条件

次がそろって渡されたときに使う。

| 与えられるもの | 役割 |
|---|---|
| `instruction.md` | 対象画面、差し替え方針、空欄化の指示 |
| 旧プログラム `*.crs` | 印刷SQLの列順（`col_list` / `col_list2`）の根拠 |
| 旧 `*.qfm`（ヘッダ用・明細用） | 差し替え元のレイアウト |
| 実印刷spoolフォルダ | `d_sql.txt`（列名一覧＝正典）、`data.txt`（実CSV）、`data.pdf`（**目標レイアウト**） |

qfmの文法・cp932・PDF描画の詳細は先に [`../author-printstream-qfm/SKILL.md`](../author-printstream-qfm/SKILL.md) を読む。View / ViewModel を触るなら `wpf-project-guide` と `wpf-view-workflow` も読む。

旧cvnetとCV10のDB・データは別物として扱う。旧 `data.txt` の値を現在DBへ合わせ込まず、**列の意味・順序・帳票の見え方**を移植根拠にする。

## 先に知っておく3つの落とし穴

この3点を知らずに「旧qfmをコピーしてSQLを合わせる」だけをやると、ほぼ確実に壊れた帳票になる。

### 1. 列見出しは qfm の中に無い（`HEAD*` / `rectype="H"`）

旧qfmの見出しセルは `<data calctype="item" datasrc="HEAD3"/>` のように `HEAD1..HEAD91` へ束縛されている。`<datadesc>` 末尾に

```xml
<prefix id="HEAD" rectype="H"/>
<prefix id="item" default="1"/>
```

があり、`HEAD*` は **`data.txt` の先頭に置かれる `H` レコード**（1列目が `H` の行）から供給される。旧cvnetはこの行を書き出していた。

**CV10 の `PrintPdfService` → `WriteDynamicCsv` は `H` レコードを出力しない。** そのまま差し替えると見出しが全部空欄、帳票タイトルも空欄になる。

対応: `HEAD*` 束縛の見出しを `<data calctype="static">見出し文字列</data>` へ変換する。帳票タイトルは、旧SQLの「タイトル用」列（たいてい4列目の `'○○伝票一覧'`）へ `datasrc="item4"` で束縛し直すと動的のまま残せる。

> サーバ側に `H` レコードを出させる改造で解こうとしない。`WriteDynamicCsv` は全帳票共通で、1帳票のために変えるところではない。

### 2. 渡される旧qfmは実運用版より古いことがある

旧CRS側でSQL列が増減しても qfm が追随していない場合がある（例: `#29919 関連伝票NO2 取得位置移動` で列が1本増え、それ以降の `itemN` が全部1ずれる）。

**`d_sql.txt` が正典**で、`data.pdf` がその正典で描かれた目標レイアウト。渡された qfm の `datasrc` はズレている前提で必ず実測検証する（→「プローブ描画」）。

### 3. `<script>` は実行されるが、値代入はたいていコメントアウト

`FormWriter` は qfm 内の JavaScript を実行する。ただし `fdTxtNN.value = dtitemNN;` の一括代入ブロックは `/* ... */` で無効化されていることが多く、実効のバインドは `<data datasrc>` 側である。

一方で条件付き非表示は生きていることがある。

```javascript
if(dtitem26=="19010101") { fdTxt53.visible = 0; fdTxt51.visible = 0; }
```

この種のロジックは残す価値がある。CV10に該当項目が無いなら、**スクリプトが非表示にする値（上例なら `19010101`）をSQLから固定出力**して列を隠す。

## 手順

### Step 0. 現状把握

1. 対象 ViewModel の `FormFileName` / 印刷SQL / パラメーター / 並び順を読む。
2. 旧CRS から `col_list`（伝票列）/ `col_list2`（明細列）と `OnQueryPrint` / `OnQueryDetailPrint` を読み、列順の組み立てを把握する。
3. `d_sql.txt` の列名を数える。`data.txt` は引用符を含むので単純なカンマ分割で数えない。
4. `data.pdf` を Read ツールで開く。**テキスト層に見出しと値が出る**ので、これが移植の受け入れ基準になる。ラスタ画像では日本語グリフが落ちるため ASCII 断片（`No` `CD` `FLG`）しか見えないが、テキスト層は正しい。

### Step 1. プローブ描画でスロット→列の対応を実測する

推測でマッピングしない。旧qfmを**そのまま**、識別可能なダミーデータで描画して対応表を作る。

```bash
# ハーネスをビルド（初回のみ）
C:/gitroot/UT/vscmd.bat dotnet build .agents/skills/author-printstream-qfm/tools/qfmprint/qfmprint.csproj
BIN=.agents/skills/author-printstream-qfm/tools/qfmprint/bin/Debug/net10.0
cp refer/printdll/printstream.license "$BIN/"

# プローブ用 data.txt（H レコード + A01..ANN のデータ行）
WD="$TMP/probe"; mkdir -p "$WD"
perl .agents/skills/upgrade-cvnet-print-form/scripts/make_probe_data.pl 68 > "$WD/data.txt"

"$BIN/qfmprint.exe" "C:\path\to\legacy.qfm" "$(cygpath -w "$WD")"
```

生成された `outfile.pdf` を Read すると、見出しセルに `H3 H4 H5 ...`、値セルに `A01 A06 A07 ...` が並ぶ。これを `data.pdf` の同じ位置の見出し・値と突き合わせれば、

- `HEADn` → 実際の見出し文字列
- `itemN` → そのスロットが本来出すべき `d_sql.txt` の列

が確定する。ズレている `datasrc` はここで全部洗い出す。

> 非表示スクリプトがあるスロットはプローブでも出ない。`data.pdf` の見出し数とスロット数が合わないときは、まず非表示列と「旧qfmに枠が無い新設列」を疑う。

### Step 2. qfm を差し替えて調整する

```bash
cp refer/.../legacy_header.qfm printform/Xxx_header.qfm
T="$TMP/x.utf8"; iconv -f CP932 -t UTF-8 printform/Xxx_header.qfm > "$T"
# ここで $T を編集
iconv -f UTF-8 -t CP932 "$T" | perl -pe 's/\r?\n$/\r\n/' > printform/Xxx_header.qfm
```

編集内容:

- `datasrc="HEADn"` → `<data calctype="static">見出し</data>`（タイトルだけ `datasrc="item4"` 等へ）
- ズレている `datasrc="itemN"` の修正。**必ず `<text id="TxtNN">` でスコープして置換する。** 素の `datasrc="item29"` を置換すると別要素を巻き込む。

```perl
perl -0777 -i -pe 's{(<text id="Txt46">.*?datasrc=")item29(")}{$1item30$2}s;' "$T"
```

- `HEAD*` の `<item>` 定義や `<prefix id="HEAD" rectype="H"/>` は**残してよい**（Hレコードが来ないだけで無害）。minimal diff を優先する。

注意点:

- **`sed` / `perl` は CR を落とす。** cp932 へ戻したあと必ず `perl -pe 's/\r?\n$/\r\n/'` で CRLF を復元し、`file -b` で `with CRLF line terminators` を確認する（`.gitattributes` は `*.qfm text eol=crlf`）。
- 静的文字列が**日本語1文字だけ**だと PrintStream が描画しないことがある。2文字以上にする（例: `色` → `色CD`）。
- 置換後に `grep -c 'datasrc="HEAD'` が 0 であることと、`<data calctype="static">` の件数が想定どおりであることを確認する。

### Step 3. 実データ形状のプローブで再描画する

CV10 のSQLが出す予定の値（日本語・負値・空欄・改ページ対象行数）を手書きした CSV で描画し、`data.pdf` と突き合わせる。

```bash
iconv -f UTF-8 -t CP932 probe.utf8.txt | perl -pe 's/\r?\n$/\r\n/' > "$WD/data.txt"
"$BIN/qfmprint.exe" "C:\gitroot\new2022\cv10\printform\Xxx_header.qfm" "$(cygpath -w "$WD")"
```

### Step 4. ViewModel の印刷SQLを書き換える

- SELECT 列順 ＝ `d_sql.txt` の列順 ＝ qfm の `item1..itemN`。
- **全SELECT列に `as itemN` を付ける。** `PrintPdfService` は結果列名を `Dictionary<string,object>` のキーにするため、同名列があるとCSV化で壊れる。列順（＝挿入順）は別名を変えても保たれる。
- ヘッダ値は明細行へ繰り返す（旧cvnetのCSVがヘッダ＋明細の1行フラット構造のため）。
- `V*` 列（CodeNameView JSON）は `json_extract(col,'$.Cd')` / `'$.Mei'` でコードと名称を**別列**に出す。画面表示用の `CodeNameDisplay.SqlFromVColumn`（`(Id) コード 名称`）は旧帳票の形と違うので使わない。
- 明細は `cross join json_each(h.Jmeisai) m` で展開し、`json_extract(m.value,'$.XXX')` で取る。マスタ補完が要る列は `left join` する。
- 旧にありCV10に無い列は `'' as itemN` で空欄。**推測で埋めない。**
- 条件付き非表示に使われる値（掛計上日の `19010101` など）は固定出力する。

### Step 5. 検証

1. `git diff --check`。qfm の trailing whitespace は差し替え元由来なら残し、由来を報告で区別する。
2. 改行コード: 触った全ファイルを `file -b` で確認。**`.cs` は作業ツリーが LF のリポジトリがある**ので、挿入したブロックを周囲へ合わせる（混在させない）。`git diff --stat` が想定行数かで検算する。
3. cv-sqlite MCP で帳票SQLを実データへ実行し、**列数・列名の一意性・値**を確認する。`data.pdf` と同じ伝票が取れるなら値を直接突合する。
4. 対象プロジェクトをビルドする。
5. Step 3 の要領で最終 qfm を実PDF描画し、`data.pdf` と突合する。
6. 画面（F6→gRPC→サーバ）からの実出力を試していないなら、完了報告に明記する。

## 実装前にユーザーへ確認すること

業務判断が残るものだけ絞って聞く。既定値を推奨として提示すると早い。

- CV10に対応物が無い旧項目を、近い概念（例: 旧「得意先＝入庫先」→ CV10 の倉庫）で埋めるか空欄にするか
- 合計の取得元（明細金額合計か税込合計か、税率別列の合算可否）
- 明細単位だった項目が CV10 では伝票単位のとき、伝票値を明細行へ繰り返してよいか
- 旧PDFにあって供給qfmに枠が無い列を、枠ごと追加するか見送るか
- コード体系が変わった項目（仕入区分など）の扱い

## 記録

- `Doc/aicoding_log.md` の先頭へ追記する。qfmのどの `datasrc` をどう直したか、どの列を空欄にしたか、対応付けの根拠を残す。
- commit / push はユーザーの明示指示があるときだけ行う。

## 関連

- `author-printstream-qfm` — qfm文法、cp932、validator、`tools/qfmprint` ハーネス本体
- `add-print-process-master-mente` — ViewModel 側の印刷配線を新設するとき
