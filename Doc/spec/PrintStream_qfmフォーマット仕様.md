# PrintStream qfm フォーマット仕様

状態: **常設ドキュメント（随時更新）**。qfm新規作成・修正のたびに参照・追記する恒久的なフォーマット仕様であり、完了・未完了の区分を持たない。

PrintStream 帳票ファイル (`.qfm`) の XML フォーマット仕様。**AI が qfm を新規作成・修正できる粒度**でまとめた実務リファレンス。

## この仕様の作り方（根拠）

qfm の XML 文法そのものはベンダーの CHM ヘルプに記載がない（`datadesc` / `calctype` などのタグ名で `PrintStream.chm` を全文検索してもヒットしない）。CHM は FormEditor(GUI) 操作とスクリプト API の解説であって、raw XML の文法書ではない。

そこでこの仕様は 2 系統の一次情報を突き合わせて構成している。

1. **XML 文法（タグ・属性・出現構造・値の分布）= 実 qfm コーパスの機械マイニング**
   - `C:\gitroot\new2022\cv10\printform\*.qfm` … 108 本（cv10 実運用）
   - `C:\gitroot\cv\cvnet_pkg\cvnetpss\*.qfm` … 1967 本（旧 cv.net 資産）
   - 合計 **2075 本**。抽出は `Doc/spec/tools/extract_qfm_grammar.ps1` で再現可能。
2. **属性値の意味（書式トークン / レコード種別 / バーコード種別 / フォント等）= CHM の該当ページ**
   - 展開済み: `refer/printdll/PrintStream_decompiled/FormEditor/*.html`

各項目には出所を明記する。

- `[C]` … CHM に意味が明記されている（確定）
- `[M]` … コーパス実測（出現あり。件数付き）。意味は推定を含む場合がある。
- `[C+M]` … 両方で裏が取れている

> **注意**: コーパスで観測されない属性値・要素を推測で発明しない。新機能が必要なときは、この仕様と CHM と既存 qfm 前例を確認してから足す。

---

## 0. 基本ルール

- 文字コードは **Shift_JIS(cp932)**。XML 宣言は `encoding="SHIFT_JIS"`（または `shift_jis`）。
- ルート要素は `<printstream version="3.0">`（コーパス 2075/2075 が `3.0`）。 `[M]`
- データ入力は **CSV のみ**。コーパスの `<path datatype="...">` は 2072/2072 が `csv`。固定長・XML 入力は実運用で使わないため本仕様では扱わない。 `[M]`
- 長さ・座標の単位は文字グリッド（`cpi`/`lpi` 基準の桁・行）。`position` の `x/y/width/height` はページ／親領域内の相対グリッド座標。
- **既存 qfm をコピーして最小差分で編集する。ゼロから書き起こさない。**

---

## 1. ドキュメント構造（要素ツリー）

コーパス全体の要素出現数（2075 ファイル）: `[M]`

| 要素 | 出現数 | 役割 |
|---|---:|---|
| `printstream` | 2075 | ルート |
| `datadesc` | 2075 | データ定義ブロック |
| `datarecord` | 2072 | 論理項目(item)定義 |
| `item` | 120307 | 論理項目 1 個 |
| `prefix` | 843 | CSV レコード種別プレフィックス（複数レコード様式） |
| `file` | 2072 | 入力ファイル指定 |
| `path` | 2072 | 入力ファイルパス／種別 |
| `page` | 2077 | 用紙・ページ |
| `region` | 2497 | レコード印刷領域 |
| `record` | 7074 | レイアウト単位（明細・見出し・中計など） |
| `group` | 3358 | 集計・改ページグループ |
| `text` | 134912 | テキストフィールド |
| `image` | 248 | イメージフィールド |
| `barcode` | 195 | バーコードフィールド |
| `data` | 133142 | フィールドのデータソース設定 |
| `decode` | 135107 | フィールドの書式（属性＋編集文字列） |
| `font` | 128131 | フォント設定 |
| `color` | 144926 | 色・透過設定 |
| `border` | 79140 | 罫線 |
| `position` | 223549 | 位置・大きさ（ほぼ全フィールド／領域） |
| `cceffect` | 508 | 網掛け・交互色などの効果 |
| `cc` | 98 | カラーコード指定 |
| `script` | 704 | スクリプト |

### 典型ツリー

```
printstream
├─ datadesc (src="file")
│  ├─ datarecord
│  │  ├─ item ×N            ← 論理項目。CSV 列順と一致
│  │  └─ prefix ×N          ← 複数レコード様式のとき（任意）
│  └─ file
│     └─ path (datatype="csv" target="data.txt")
└─ page ×1..N
   ├─ position               ← ページ印字範囲
   ├─ (text/image/barcode)   ← ページ直下フィールド（タイトル・日付・ページ番号など）
   └─ region ×1..N
      └─ record ×1..N        ← 明細・見出し・中計…
         └─ (text/image/barcode) ×N
```

フィールド（`text`/`image`/`barcode`）は `page` 直下にも `record` 内にも置ける。 `[C]`
- `page` 直下 = ページ単位で 1 回（タイトル、出力日時、ページ番号、合計 等）
- `record` 内 = 印刷データ 1 件ごとに繰り返し

---

## 2. データ定義（CSV 前提）

### 2.1 datadesc / file / path

```xml
<datadesc src="file">
    <datarecord>
        <item id="item1"><position offset="0" length="12"/></item>
        <item id="item2"><position offset="0" length="80"/></item>
        ...
    </datarecord>
    <file>
        <path datatype="csv" target="data.txt"/>
    </file>
</datadesc>
```

| 要素/属性 | 値 | 出所・意味 |
|---|---|---|
| `datadesc @src` | `file` | 入力元がファイル。 `[M]` 2072/2072 |
| `path @datatype` | `csv` | CSV 入力。 `[M]` 2072/2072 |
| `path @target` | `data.txt` 等 | 入力ファイル名。cv10 標準は `data.txt`。 `[M]` |

### 2.2 item（論理項目）

```xml
<item id="item5"><position offset="0" length="60"/></item>
```

- `id` … `item1`, `item2` … の連番。フィールドの `data @datasrc` から参照される。 `[M]` `item` の唯一の属性は `id`（120307/120307）。
- `position @offset` … CSV では通常 `0`。
- `position @length` … 想定データ長（表示幅ではない）。目安: コード 10〜12 / 日時文字列 14 / 名称 60〜120 / 電話 20〜30 / メモ 100+。

> **鉄則**: `item` の定義順・CSV の列順・`datasrc="itemN"` の対応を必ず一致させる。

### 2.3 prefix（複数レコード様式 CSV）— 任意 `[M]`

1 つの CSV に種類の異なる行（ヘッダ行・明細行など）が混在する様式で使う。`[M]` 属性: `id`(843) / `rectype`(434) / `default`(409) / `skip`(2)。

- `rectype` … 行の先頭に付くレコード種別コード。この値で `datarecord` 内の項目セットを切り替える。
- `default` … 種別コードが一致しない行に適用する既定。
- 単純な 1 種類 CSV しか使わない帳票では **prefix は不要**。まず prefix なしを優先する。

---

## 3. ページとレイアウト

### 3.1 page `[C+M]`

```xml
<page orientation="portrait" cpi="10" lpi="6" compatibility="3.0.0" id="Form1" tree="1">
    <position x="8" y="8" width="156" height="272"/>
</page>
```

| 属性 | 観測値（件数） | 意味 |
|---|---|---|
| `orientation` | `portrait`(1077) / `landscape`(998) | 縦／横。**横帳票も普通に使う**。 `[C+M]` |
| `cpi` | `10`(2005) / `15`(65) / `12`(5) | 1 インチあたり文字数（横グリッド密度）。 `[M]` |
| `lpi` | `6`(1999) / `18`(59) / `8`(11) / `10`(6) | 1 インチあたり行数（縦グリッド密度）。 `[M]` |
| `compatibility` | `3.0.0`(全件) | 互換バージョン。 `[M]` |
| `id` | `Form1` 等 | ページ識別子。 `[M]` |
| `tree` | `1`(全件) | エディタ用ツリー表示フラグ。 `[M]` |
| `title` | 任意 | ページタイトル（1310 件で使用）。 `[M]` |
| `size`/`width`/`length` | 任意 | 用紙サイズ指定（89 件、任意用紙のとき）。 `[M]` |
| `splitOrder`/`splitCount`/`region`/`startpage`/`breaklevel` | 稀(各2) | ページ分割・多面付け系。高度機能。 `[M]` |

- A4 縦の cv10 標準印字範囲: `<position x="8" y="8" width="156" height="272"/>`。
- 複数 `page` を並べると多ページ様式（例: 明細ページ＋集計ページ）。 `[M]` `page` は 2077 出現 = 一部ファイルが複数ページ。

### 3.2 region（レコード印刷領域）`[C+M]`

```xml
<region id="Rgn01" direction="0" tree="1">
    <position x="0" y="10" width="150" height="248"/>
    <color transparent="1"/>
</region>
```

| 属性 | 観測値（件数） | 意味 `[C]` |
|---|---|---|
| `direction` | `0`(2098) / `1`(399) | レコードの敷き詰め方向。`0`=縦方向（上から下、一覧帳票の既定）、`1`=横方向（タックシール等）。 |
| `link` | 任意(359) | リージョン接続（1 領域に収まらないとき次領域へ継続）。矩形で表せない領域・タックシールで使用。 |
| `id`/`tree` | — | 識別子／エディタ表示。 |

- `region` はページ上のみ定義可。領域内に `record` を敷き詰める。レコードは領域をはみ出さない。 `[C]`
- まずは単一 `region` + `direction="0"` を優先。

### 3.3 record（レイアウト単位）`[C+M]`

```xml
<record id="Rec01" tree="1" recordtype="1" breaktype="0" grouplevel="0" adjusttype="0">
    ...fields...
</record>
```

CHM のレコード種別 `[C]`:

| 種別 | 説明 |
|---|---|
| データレコード | 明細行・中計行。印刷データ 1 件ごとに出力。最も一般的。 |
| ヘッダーレコード | リージョン先頭に必ず出力（見出し）。継続領域には出ない。 |
| フッターレコード | リージョン末尾に必ず出力。継続領域には出ない。 |
| フィルレコード | データが途中までのとき、残り領域を埋める（罫線接続などに使用）。 |

| 属性 | 観測値（件数） | 意味 |
|---|---|---|
| `recordtype` | `1`(2023) / `3`(310) / `2`(49) | レコード種別。`1`=データレコードが圧倒的多数。`2`/`3`=ヘッダ／フッタ／フィル等。**意味の完全対応表は CHM に数値表記がないため、既存 qfm の値をそのまま踏襲する。** `[M]` |
| `breaktype` | 任意(2425) | グループ前／後どちらに印字するか（中計＝後、サブヘッダ＝前）。 `[C]` |
| `grouplevel` | 任意(2425) | 対象とする集計グループのレベル。キーブレイク時に出力。 `[C]` |
| `adjusttype` | 任意(825) | 印字位置調整。`0`=調整しない/先頭行にしない/最終行にしない/前で改ページ/後で改ページ（中計・サブヘッダの改ページ制御）。 `[C]` |
| `id`/`tree` | — | 識別子／表示。 |

> `recordtype` / `breaktype` / `adjusttype` の**数値と挙動の対応は CHM に数値表がない**。前例のない値を推測で入れない。既存の中計・サブヘッダ帳票（下記パターン参照）からコピーする。

### 3.4 group（集計・改ページグループ）`[C+M]`

```xml
<group level="1" pagechange="1"/>
```

| 属性 | 観測値（件数） | 意味 |
|---|---|---|
| `level` | `1`(1744)/`2`(1029)/`3`(430)/`4`(155) | 集計階層。1 が最上位。最大 4 階層まで観測。 `[M]` |
| `pagechange` | `1`(1843)/`0`(1515) | `1`=グループ切替で改ページ。`0`=改ページしない。 `[M]` |

- 例: `MasterPrintBarcode002.qfm` は `group level="1" pagechange="1"` で 1 グループ 1 ページ。詰めて出す帳票は `pagechange="0"`。

---

## 4. フィールド共通

`text` / `image` / `barcode` は概ね以下の子要素を持つ。

```xml
<text id="..." halign="left" valign="middle">
    <position x="10" y="2" width="40" height="1"/>
    <data calctype="item" datasrc="item5"/>
    <decode datatype="string" format=""/>
    <font size="90"/>
    <color transparent="1"/>
    <border .../>            <!-- 任意 -->
</text>
```

### 4.1 position `[M]`

- フィールド: `x` / `y` / `width` / `height`（147001 件）… 親（page/record）内のグリッド座標。
- item: `offset` / `length`（76548 件）… §2.2 参照。
- 一覧領域では原則 `x + width <= 150`（A4 縦・cpi=10 の目安）。

### 4.2 data（データソース）`[C+M]`

```xml
<data calctype="item" datasrc="item5"/>
```

| `calctype` | 件数 | 意味 `[C]` | `datasrc` |
|---|---:|---|---|
| `item` | 68670 | 印刷データ項目をそのまま印字 | 必要（`itemN`） |
| `static` | 49320 | 固定文字（要素本文に文字列を書く） | 不要 |
| `sum` | 11319 | 合計（グループ／ページ内） | 対象 item |
| `page` | 1991 | ページ番号 | 不要 |
| `date` | 1809 | 実行日時 | 不要 |
| `seq` | 27 | 連番 | 不要 |
| `count` | 6 | 件数 | 対象 item |

固定文字は本文にテキストを入れる:

```xml
<data calctype="static">社員マスタリスト</data>
```

グループ制御属性 `[C+M]`:

| 属性 | 観測値 | 意味 |
|---|---|---|
| `grouplevel` | 任意(1419) | 対象グループレベル。 `[C]` |
| `groupcontrol` | `5`(642)/`1`(487)/`0`(262)/`4`(22)/`3`(3)/`2`(3) | グループ境界・先頭ページのみ印字、継続時別文字など（CHM「対象とするグループ」節）。数値対応表なし→前例踏襲。 `[M]` |
| `suppress` | `1`(556) | 連続する同一データを印字しない。 `[C]` |
| `groupaltstring` | 任意(9) | 次ページ継続時に印字する別文字列。 `[C]` |

### 4.3 decode（属性＋編集文字列）`[C+M]`

```xml
<decode datatype="string" format=""/>
```

| `datatype` | 件数 | 標準（format 空）の挙動 `[C]` |
|---|---:|---|
| `string` | 90558 | データをそのまま印字 |
| `number` | 41915 | 電卓形式（マイナスのみ左端、カンマなし、必要な小数のみ） |
| `date` | 2634 | `YYYY/MM/DD HH:MM:DD`（時分秒が全 0 なら時刻略、秒 0 なら秒略） |

`format` は編集文字列。XML 内ではリテラル文字を `&quot;...&quot;` で囲む。詳細は §5。

### 4.4 font `[C+M]`

```xml
<font size="90"/>
<font size="80" face="ＭＳ 明朝" style="1"/>
```

| 属性 | 観測値（件数） | 意味 |
|---|---|---|
| `size` | `90`(54985)/`80`(38841)/`100`/`70`/`50`… | 文字サイズ（1/10 pt 単位。90=9pt）。 `[M]` |
| `face` | `ＭＳ 明朝`(7552)/`QFM Mincyo`(1859)/`QFM TimesRoman`/`ＭＳ Ｐゴシック`/`Arial`/`OCRB`… | 書体。省略時は既定。 `[M]` |
| `style` | `1`/`64`/`65`/`3`/`4`/`5`/`32`/`33`… | スタイルのビット合成（太字/斜体/下線等）。数値表は CHM になし→前例踏襲。 `[M]` |
| `pitch` | 任意(2982) | 文字ピッチ。 `[M]` |
| `widechar`/`hsize`/`vsize` | 稀 | 倍角・縦横倍率。 `[M]` |

プリセットフォント（フォント情報ファイル不要、サーバ登録不要）`[C]`:
`QFM Gothic`(MS ゴシック) / `QFM Mincho`(MS 明朝) / `QFM TimesRoman` / `QFM Courier` / `QFM Gothic JP|CN|TW|KR` / `QFM Mincho JP|CN|TW|KR`。

> 非プリセットフォントは標準印刷で `.far`（フォント情報ファイ）が要る。PDF ではフォント埋め込みが必要。**PDF は標準印刷と完全一致しない**（太字・自動改行・網掛けで差が出やすい）。cv10 は最終的に PDF 出力なので実 PDF で確認する。 `[C]`

### 4.5 color `[C+M]`

```xml
<color transparent="1"/>
<color transparent="0" base="s..." border="s..." font="s..." pattern="s..."/>
```

| 属性 | 観測値 | 意味 |
|---|---|---|
| `transparent` | `1`(127247)/`0`(17679) | `1`=背景透過。 `[M]` |
| `base`/`border`/`font`/`pattern` | 色コード | 背景/罫線/文字/網掛け色。 `[M]` |

### 4.6 border（罫線）`[M]`

`<border top="..." bottom="..." left="..." right="..."/>` に加え四隅 `topleft`/`topright`/`bottomleft`/`bottomright`、対角 `leftdowndiagonal`/`rightdowndiagonal`。各辺に線種値。既存帳票からコピーする。

### 4.7 text 固有 `[C+M]`

| 属性 | 観測値 | 意味 |
|---|---|---|
| `halign` | `right`(56590)/`center`(23011)/(既定 left) | 水平寄せ。 `[M]` |
| `valign` | `middle`(73647)/`bottom`(3089)/(既定 top) | 垂直寄せ。 `[M]` |
| `wrap` | `1`(2585) | 折り返し。 `[M]` |
| `stick`/`rowspace`/`crbreak`/`kinsoku` | 任意 | 詰め／行間／改行／禁則。 `[M]` |

### 4.8 image `[C+M]`

```xml
<image id="Img01" relative="0">
    <position x="120" y="1" width="32" height="40"/>
    <color transparent="1"/>
    <data calctype="item" datasrc="item26"/>
</image>
```

- 対応形式 BMP/JPEG/PNG。固定パスでも item でパスを受けてもよい。 `[C]`
- 属性: `relative`(相対パス) / `stick` / `valign` / `halign` / `emb`(埋め込み)。 `[M]`
- サーバ実行時はサーバから参照できる配置が必要。 `[C]`

### 4.9 barcode `[C+M]`

```xml
<barcode id="Bar01" type="0" string="0" check="0" sschar="0">
    <position .../>
    <data calctype="item" datasrc="item3"/>
</barcode>
```

CHM 対応種別 `[C]`: CODE39 / JAN / JAN短縮 / NW-7 / ITF / 郵便カスタマ / CODE128 / UCC-EAN128 / 標準料金代理収納 / QR。

| 属性 | 観測値 | 意味 |
|---|---|---|
| `type` | `0`(101)/`2`(47)/`1`(47) | バーコード種別番号。**数値と種別の対応表が CHM にないため既存 qfm から確認して使う。** cv10 には `MasterPrintBarcodeCode39.qfm`/`...Nw7.qfm`/`...Sho.qfm` 等の前例あり。 `[M]` |
| `check` | `0`(113)/`1`(82) | チェックデジット付加。CODE39=モジュラス43、JAN=モジュラス10ウェイト3 等。 `[C]` |
| `sschar` | `1`(132)/`0`(63) | スタート/ストップコード付加（CODE39 の `*` 等）。 `[C]` |
| `string` | — | 併記文字列設定。 `[M]` |

> バーコードは規格上のバー間隔・クワイエットゾーンが要る。他フィールドと密着させない。運用前に実機確認。 `[C]`

### 4.10 cceffect / cc（網掛け・交互色）`[M]`

`cceffect @type`: `alt`(285 交互色) / `noback`(9) / `nogrid`(1)。`cc` は色コード（`transparent`/`s808040` 等）。一覧の縞模様などに使用。既存帳票からコピー。

---

## 5. 編集文字列（decode/format）チートシート `[C]`

XML 内ではリテラル文字列を `&quot;...&quot;`（= `"..."`）で囲む。

### 5.1 文字列 (datatype="string")

| 記号 | 例 | 意味 |
|---|---|---|
| `@` | `"最大"@` | データをそのまま指定位置に印字 |
| `Sn.m` | `S2.4` | 左端 0 から数えて n 文字目から m 文字を切り出し |
| `"TEXT"` | `9990"円"` | リテラル文字を印字 |
| `N` | `@"様"N` | 空文字なら何も印字しない／あれば書式適用 |

実例（コーパス）:
- 日時文字列 14 桁 → 年月日時分秒: `S0.4"/"S4.2"/"S6.2" "S8.2":"S10.2":"S12.2"`
- 日付だけ: `S0.4"/"S4.2"/"S6.2"` / 和風: `S0.4"年"S4.2"月"S6.2"日"`
- 前置ラベル: `"TEL. "@` / `"〒"@`

### 5.2 数値 (datatype="number")

| 記号 | 例 | 意味 |
|---|---|---|
| `9` | `999` | 桁数指定。先行 0 は空白（ゼロサプレス） |
| `0` | `999,990` | 桁数指定。先行 0 は 0 表示 |
| `,` | `999,990` | 3 桁ごとにカンマ |
| `.` | `9990.99` | 小数点。以下桁数指定 |
| `$` | `$9,990` | 先頭に `$` |
| `L` | `L9990` | ローカル通貨記号 |
| `MI` | `MI999` | 負数のとき指定位置に `-` |
| `MI2` | `MI2999` | 負数のとき `▲` |
| `MI3` | `MI3999` | 負数のとき `△` |
| `S` | `S9990` | 指定位置に符号（+/-） |
| `S2` | `S29990` | 符号を `+`/`△` で |
| `*` | `99,990.*` | 小数桁を入力データに合わせる |
| `N` | `99,990N` | 0 と空を区別（空は非表示） |
| `!` | `999,990!` | 桁あふれ記号 `!` を出さない |
| `@` | `"最大"@` | 既定書式で印字 |

実例（コーパス）:
- 金額（右詰・負号）: `MI999,999,999,990" "` / `MI999,999,990"`
- 小数付き金額: `MI999,999,990.99" "` / `MI999,999,990.00"`
- 率(%): `MI2N990.90"%"`
- ページ番号: `"P."0999`

### 5.3 日付時刻 (datatype="date")

主なトークン: `YYYY`/`YY`(年) `MM`(月) `DD`(日) `HH`(12h) `HH24`(24h) `MI`(分) `SS`(秒) `MONTH`/`MON`(月名) `DAY`/`DY`/`DJ`(曜日) `AM`/`WAM`(午前午後) `WR`/`WY`(和暦年号/年)。

実例: `"出力日付: "YYYY/MM/DD HH24:MI:SS`

---

## 6. スクリプト（任意）`[C]`

`<script language="...">`（704 出現）。JavaScript 互換。**まずスクリプトなしで表現できる帳票に寄せる。** 整形・ページ制御・パラメータ分岐が必要なときだけ。

代表 API（CHM `part7_*`）: `GetQfm()` / `ScriptVersion("3.1")` / `qfm.GetParameter(sym)` / `qfm.GetPage()` / `qfm.GetCursor()` / `page.GetField(name)` / `page.AssignData()` / `cursor.GetValue(item)` / `cursor.SetValue(item,v)` / `qfm.End()`。

注意: `GetRecordCount()` は先読みで遅い。マルチバイトは `substr`/`substrB` を混同しない。

---

## 7. 最小骨格テンプレート

### 7.1 空の最小骨格

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

### 7.2 一覧帳票（region + record 繰り返し）

雛形にする既存 qfm: `printform/MasterShainMente.qfm`, `printform/MasterMeishoMente.qfm`。
特徴: 見出し行を別 record に分離、上部にタイトル・出力日時・ページ番号、`region` に明細 record を繰り返し。

### 7.3 単票帳票（ラベル＋値）

雛形: `printform/MasterSysKanriMente.qfm`。ラベル(`static`)と値(`item`)を対にして 1 枚に展開。

---

## 8. 作成ワークフロー

1. 最も近い既存 qfm を選ぶ（一覧: MasterShainMente / MasterMeishoMente、単票: MasterSysKanriMente）。
2. `printform/` にコピーしファイル名を決める。
3. XML 宣言と `<printstream version="3.0">` を維持。
4. `datarecord/item` を新しい CSV 列順に合わせる。
5. 各フィールドの `data @datasrc="itemN"` を列順に合わせる。
6. `decode/format` は §5 と既存例から流用（発明しない）。
7. `record`/`region`/`group` の種別・数値属性は前例からコピー（recordtype 等を推測しない）。
8. 画像・バーコード・スクリプトは最後に追加。
9. **Shift_JIS(cp932)** で保存。
10. validator を実行。
11. 実際の印刷経路で PDF を確認。

---

## 9. 検証

repo 共通 validator:

```powershell
python .agents\skills\add-print-process-master-mente\scripts\validate_qfm.py printform\MasterXxxMente.qfm
```

主なチェック: Shift_JIS で読めるか / XML 宣言 encoding / ルート `printstream` / `path datatype="csv"` `target="data.txt"` / `page orientation` / A4 縦基本 position / `datarecord/item` 存在。

これは repo 運用ルールの確認であり、ベンダー仕様の完全検証ではない。legacy の position warning は単独で失敗扱いしない。Shift_JIS で読めること・`itemN`/`datasrc` 対応が壊れていないこと・PDF 出力が要求形状に合うことを分けて確認する。

文法を再抽出するには:

```powershell
powershell -File Doc\spec\tools\extract_qfm_grammar.ps1 printform C:\gitroot\cv\cvnet_pkg\cvnetpss
```

---

## 10. 出所（CHM 該当ページ）

`refer/printdll/PrintStream_decompiled/FormEditor/` 配下:

| ページ | 内容 |
|---|---|
| `part4_2.html` | ページ |
| `part4_3.html` | リージョン |
| `part4_4.html` | レコード（種別・調整） |
| `part4_5.html` | テキストフィールド・**編集文字列（書式）** |
| `part4_6.html` | イメージフィールド |
| `part4_7.html` | バーコードフィールド |
| `part5_3.html` | 印刷データとフィールドの対応 |
| `part5_12.html` | CSV |
| `part7_12〜16.htm` | スクリプト（global/String/form/cursor/page） |
| `part9_1.html` | フォントとテキスト描画・プリセットフォント |
| `part9_2.html` | フォント情報ファイル |
| `part13_1〜4.html` | PDF・フォント埋め込み・標準印刷との差異 |

---

## 11. この仕様がカバーしないこと

- ベンダーが raw qfm XML の完全仕様を保証している、という主張（CHM に XML 文法書はない）。
- `recordtype`/`breaktype`/`groupcontrol`/`barcode type`/`font style` の数値と挙動の**完全対応表**（CHM に数値表がない。前例のない値を推測で入れない）。
- CSV 以外の入力（固定長・XML）。実運用で使わないため対象外。
- FormEditor が不要である、という断定。
