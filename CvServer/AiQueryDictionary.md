# CV10 業務辞書（AI照会用）

この辞書はAI照会のSQL生成に渡す業務規約である。表・列の意味は describe_table の説明を正とし、ここには表をまたぐ規約だけを書く。
要求ごとに読み込むため、追記はサーバ再起動なしで反映される。

## SQLの書き方

- SQLite方言の SELECT または WITH ... SELECT を1文だけ書く。更新系SQL・PRAGMA・Sys で始まる表は使わない。
- 値はSQLへ埋め込まず @0, @1, ... のパラメータにする。日付は 'yyyyMMdd' の8桁文字列のまま比較する。
- 数値のパラメータは CAST(@n AS INTEGER) または CAST(@n AS REAL) で使う。日付（Date型）のパラメータと日付列は CAST せず、h.DenDay BETWEEN @0 AND @1 のように文字列のまま比較する。
- 出力列には分かりやすい別名（日本語可）を付ける。

## 伝票（Tran*）

- 主な伝票表: Tran01Tenuri=店舗売上、Tran00Uriage=本部売上（得意先への卸売上）、Tran03Shiire=仕入、Tran12Jyuchu=受注、Tran13Hachu=発注、Tran05Ido/Tran10IdoOut/Tran11IdoIn=移動、Tran06Nyukin=入金、Tran07Shiharai=支払。
- 「売上」とだけ言われたら、店舗売上（Tran01Tenuri）か本部売上（Tran00Uriage）かを質問する。
- DenDay は計上日（yyyyMMdd）。他の日付の意味は各表の列説明を確認する。
- 金額: KingakuTotal は明細金額合計（税抜）、Tax1+Tax2+Tax3 が消費税、Total は税込総合計。値引は Nebiki00Total（合計からの値引）。
- 店舗・倉庫・社員・取引先などのコードと名称は、ヘッダの V* 列（JSON）から取り出す: コード ifnull(json_extract(h.VTenpo,'$.Cd'),'')、名称 ifnull(json_extract(h.VTenpo,'$.Mei'),'')。
- Tran* の V* 列は伝票作成時点の名称（監査値）。現在のマスタ名称が必要なときだけ Master* 表と Id_* で結合する。

## 返品・値引の符号（CalcFlag）

- 取引区分 Kubun: 10〜19 売上（または仕入）、20〜29 返品、30〜39 値引。返品・値引も数量・金額・税は正の値で保存されている。
- 符号付きの合計（純売上・純数量・税の合計）は必ずヘッダの CalcFlag を掛ける: SUM(h.CalcFlag * h.KingakuTotal)。
- 返品額だけを出すときは正の値のまま: SUM(CASE WHEN h.Kubun BETWEEN 20 AND 29 THEN h.KingakuTotal ELSE 0 END)。
- 伝票数は CalcFlag = 1 の伝票だけを数える。

## 明細（Jmeisai）

- 明細は伝票ヘッダの Jmeisai 列に JSON 配列で入っている。展開は FROM Tran01Tenuri h, json_each(h.Jmeisai) m とし、WHERE に json_valid(h.Jmeisai) を必ず入れる。
- 明細項目: 文字列 ifnull(json_extract(m.value,'$.Code_Shohin'),'')、数値 CAST(ifnull(json_extract(m.value,'$.Su'),0) AS INTEGER)。0 の項目はJSONから省略されるため ifnull/COALESCE が必須。
- 明細を展開した集計では、ヘッダの金額（KingakuTotal 等）を合計しない（明細行の数だけ重複する）。明細の金額を合計する。
- 明細の数量・金額にも符号が必要なら h.CalcFlag を掛ける。

## 在庫

- 現在在庫は SummaryRealStock（倉庫×商品×色×サイズ）。月別在庫は SummaryStock。
- 在庫の数量は既に符号付きなので CalcFlag を掛けない。
