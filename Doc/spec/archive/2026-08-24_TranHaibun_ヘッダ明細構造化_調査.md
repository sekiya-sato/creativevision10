# TranHaibun のヘッダ・明細構造化 調査（2026-08-24）

対象HEAD: `8694e0f`
対象テーブル: `CvBase/BaseDbHaibun.cs` の `TranHaibun`（同ファイルの `TranHoju` も同型）
比較対象: `CvBase/BaseDb2Trans.cs` の `TranAllHeader` 派生伝票（`Tran00Uriage` / `Tran01Tenuri` / `Tran03Shiire` / `Tran05Ido` 等）＋ `Tran99Meisai`
関連決定: `Doc/spec/archive/2026-08-17_旧cvnet比較_仕様決定判断材料.md` I5（2026-08-17 決定 / REPLACE）

## 1. 現状構造の確認

### 1.1 TranHaibun（フラット1行 = 1 SKU）

`CvBase/BaseDbHaibun.cs:9-14` のとおり、PKは `Id`（AutoIncrement）のみ。索引は
`nk1(DenDay)` と `nk2(Id_Soko, Id_Shohin, Id_Col, Id_Siz)` の2本で、**nk2 は引当数の
再計算が倉庫+SKUで絞り込むために存在する**（同 `:11` のコメント）。

列は役割で3群に分かれる。

| 群 | 列 | 現状の重複 |
|---|---|---|
| ヘッダ相当 | `DenDay` `NouhinDay` `Id_Soko` `Id_Tenpo` `Kubun` `SendFlg` `Id_Shain` `RelateNo1` | 同一配分の全SKU行で同値が繰り返される |
| 明細相当 | `Id_Shohin` `JanCode` `Id_Col` `Id_Siz` `Su` `Tanka` `Kingaku` `Jodai` `Gedai` `Memo` | 行固有 |
| 明細単位の状態 | `KakuteiDay` `JitsuSu` `ShortSu` `EndFlag` `RelateNo2` | 行固有。**SKU単位で遷移する** |

### 1.2 比較対象（TranAllHeader 派生）

`TranAllHeader`（`CvBase/BaseDb2Trans.cs:167-277`）は実テーブル1行がヘッダで、
明細は `Jmeisai`（`List<Tran99Meisai>` / `[SerializedColumn]` / `varchar(4000)`）に
**JSONで内包**する。別テーブルではない。集計は SQLite の `json_each` / `json_extract` で展開する
（`CvDomainLogic/SummaryDb.cs:326-356` の `CreateSummaryStockSql`）。

`Tran99Meisai`（`CvBase/BaseDb2Trans.cs:287-460`）の列は
`No/Kubun/Id_Shohin/Code_Shohin/Mei_Shohin/JanCode/Id_Col(+CD,名)/Id_Siz(+CD,名)/Su/Tanka/Kingaku/Jodai/Gedai/Nebiki00-02/Id_Shain(+CD,名)/Memo` で、
**`KakuteiDay` / `JitsuSu` / `ShortSu` / `EndFlag` / `RelateNo2` に相当する列は無い**。

### 1.3 「仮想ヘッダ」は既に運用されている

出荷処理（`CvDomainLogic/ShippingDb.cs:131`）は
`(DenDay, NouhinDay, Id_Soko, Id_Tenpo, Kubun, RelateNo1)` の6列で `GroupBy` し、
1グループ=1伝票（`Tran00Uriage` または `Tran10IdoOut`）を作る。これが I5 決定の
「明細行単位のままとし、仮想ヘッダのキーで括る」の実装である。

**ユーザー提示の `DenDate + Id_Soko + Id_Tenpo` の3列は、この仮想ヘッダキーより粒度が粗い。**
`NouhinDay` / `Kubun` / `RelateNo1` が異なる配分が1レコードに混ざるため、
そのままでは以下が壊れる。

- `RelateNo1 = 元伝票Id` 規約（受注Id / 発注Id）が1ヘッダに複数入る → 受注・発注の自動完了判定
  （`CvDomainLogic/CompletionDb` 経由、`CvServer/Services/WriteEffectRunner.cs:161-190`）が特定できない
- `Kubun`（`EnumHaibun`）が引当対象判定そのもの（`SummaryDb.ReserveTargetWhere` = `Kubun <> 0`）
  → ヘッダ単位で持てないなら明細へ降ろすしかない
- 納品日違いの出荷が1伝票にまとまる（旧CV.netの配分ヘッダ構成とも不一致）

→ **構造化するなら、キーは3列ではなく I5 の6列（仮想ヘッダ）にする必要がある。** 以降は
6列キーを前提に評価する。

## 2. 関与コードの一覧

### 2.1 CvServer / CvDomainLogic（サーバ側）

| 箇所 | 内容 |
|---|---|
| `CvDomainLogic/SummaryDb.cs:181-320` | 引当数の引き直し。`CalcHaibun2Reserve`（キー単位）/ `CalcReserveQtyAll`（全件）。`TranHaibun` を直接 `GROUP BY Id_Soko, Id_Shohin, Id_Col, Id_Siz` して `SummaryStock` / `SummaryRealStock.ReserveQty` へ `INSERT ... ON CONFLICT` |
| `CvDomainLogic/ShippingDb.cs`（272行・全体） | 出荷指示確定（`KakuteiDay`）・確定取消・出荷処理（`JitsuSu`/`ShortSu` 書込→伝票生成→`RelateNo2`+`EndFlag=1`）。`TranHaibun` へのSQLは5箇所、`Fetch<TranHaibun>` が2箇所 |
| `CvServer/Services/WriteEffectRunner.cs:113-162` | `ITranReserve` を実装する行の追加/修正/削除で `ReserveKey` を集めて引当再計算。**キー抽出は「1行=1キー」前提** |
| `CvServer/Services/WriteEffectRunner.cs:77-80` | `PartialUpdateDeniedColumns`。`Id_Soko/Id_Shohin/Id_Col/Id_Siz/Su/Kubun/DenDay/Jmeisai` 等は部分更新禁止 |
| `CvServer/Services/HandlerClass.cs:482-511` | `InsertBulkParam`。「配分は数百件が一括登録される」ため引当キーを溜めて最後に1回だけ引き直す |
| `CvBase/BaseDb2Trans.cs:41-52` | `ITranReserve`（`Id/DenDay/Id_Soko/Id_Shohin/Id_Col/Id_Siz/Su/EndFlag`）。**1行にSKUが1つある前提のインターフェース** |
| `CvBase/DefineDataTable.cs:76` | テーブル登録 |
| `CvBase/UpdateDb.cs:35,37` | 既存マイグレーション（`EndFlag` / `ShortSu` 追加）。**`ALTER TABLE` ベースの追記のみ** |

### 2.2 View / ViewModel（クライアント側）

実装済み（`TranHaibun` に直接SQLを発行しているもの）:

| 画面 | ViewModel | 行数 | TranHaibun への関与 |
|---|---|---|---|
| 店舗配分入力 | `07Haibun/ShopHaibunInputViewModel.cs` | 855 | 商品×(店舗×SKU)クロス表。洗い替え登録（`:260-270` で**1行ずつ `DeleteByIdParam`**、`:279` で `InsertBulkParam`）。SQL 2箇所 |
| 受注配分入力 | `07Haibun/JuchuHaibunInputViewModel.cs` | 1148 | 受注伝票単位。SQL 4箇所（配分サマリ/受注残/重複チェック等） |
| 発注配分入力 | `03Hatchu/HachuHaibunInputViewModel.cs` | 1124 | 発注伝票単位。SQL 2箇所＋削除ループ |
| 出荷指示確定(商品/得意先) | `Helpers/ViewModels/BaseShippingConfirmViewModel.cs` | 360 | **SKU行単位のチェックボックス選択**で確定/確定取消 |
| 出荷処理入力 | `07Haibun/ShippingInputViewModel.cs` | 284 | SKU行単位に `JitsuSu` を入力 |
| 滞留・欠品例外 | `07Haibun/ShippingConfirmListViewModel.cs` | 417 | 確定済み未出荷のSKU行一覧 |
| 配分問合わせ | `07Haibun/HaibunQueryViewModel.cs` ＋ `BaseHaibunInquiryViewModel.cs` | 38 / 709 | 倉庫×色サイズのマトリクス。`GROUP BY Id_Soko, Id_Shohin, Id_Col, Id_Siz` |
| 引当/有効在庫問合わせ | `HikiateQueryViewModel.cs` / `YukoZaikoQueryViewModel.cs` | 39 / 38 | 参照は `SummaryRealStock`（`TranHaibun` 直参照なし） |

未実装（11行の空クラス。`MenuData.cs` で「準備中」）:
在庫品配分・得意先別配分入力・配分データメンテ・店舗出荷依頼・出荷指示明細書印刷・納入一覧表・
取置入力・移動指示(SKU)・移動指示(商品)、および1.1以降の自動補充系3画面。

`TranHaibun` に対するSQL発行箇所は **アプリ側で18箇所**（テスト2ファイルの33参照は別）。

## 3. ヘッダ・明細構造にした場合のメリット

### M1. 出荷系ロジックからグルーピングが消える
`ShippingDb.CreateShippingSlips` の `GroupBy`（`:131`）と、グループキーをタプルで持ち回す
`CreateUriage` / `CreateIdoOut` の引数（`:218,244`）が不要になる。
`TranHaibun` 1行 → 伝票1行の素直な変換になり、`RelateNo2` もヘッダ1列の更新で済む
（現状は `UPDATE ... WHERE Id IN (...)` でグループ内全行を更新している `:161-164`）。

### M2. 洗い替え登録が1往復で済む
`ShopHaibunInputViewModel:260-270` は既存行を**1件ずつ** `DeleteByIdParam` で消しており、
100行なら100往復＋100回の楽観排他判定になる。ヘッダ化すれば店舗ごと1行の削除になり
（1商品×20店舗なら20往復）、さらに `UpdateParam` 1発の置換に寄せられる。
楽観排他も「ヘッダの `Vdu` 1つ」で表現でき、部分成功（一部だけ消えた状態）が原理的に発生しない。

### M3. ヘッダ項目の重複が消え、行数が1〜2桁減る
`DenDay/NouhinDay/Id_Soko/Id_Tenpo/Kubun/SendFlg/Id_Shain/RelateNo1` の8列が全SKU行に
複製されている。数百件/回の登録（`HandlerClass.cs:491` のコメント）が数件〜数十件になる。
`SendFlg`（物流連携、`41Logistics/連携データ手動送信`）を**送信単位=ヘッダ単位**で持てる。

### M4. 既存の伝票基盤を再利用できる
`TranAllHeader` 派生にすれば以下がそのまま使える。
- `CvWpfclient/Helpers/ViewModels/BaseTranInputViewModel.cs`（`TDen : TranAllHeader` 制約。
  発注/受注/仕入/売上/店舗売上の5画面が利用中）＝明細操作・合計集計・行採番の共通実装
- `SuTotal/KingakuTotal/JodaiTotal/GedaiTotal` のヘッダ合計列（現状の配分には無く、
  一覧に合計を出すたび集計SQLを書いている）
- `VShain/VSoko` の時点名称スナップショット（現状の配分は `Id_Shain` だけで名称を都度JOIN）
- `TranMeisaiSql` / `Jdetail` などの共通部品

### M5. 未実装画面が旧CV.net構造に素直に対応する
旧CV.netの「配分データメンテ（追加／修正）」は**ヘッダ項目と明細項目が明確に分かれている**
（`2026-08-17_旧cvnet比較_仕様決定判断材料.md` 5.2 の「ヘッダの修正可能項目/明細の修正可能項目」）。
出荷指示明細書印刷（ピッキングリスト）・納入一覧表も「1出庫元 ⇒ 1出荷先」=1ヘッダが単位。
これらは**まだ未実装**なので、いま構造化するなら再作業が発生しない。

### M6. 未実装画面の数が多いうちなら移行コストが小さい
07Haibun 21エントリのうち9画面が空クラス。実データも UpdateDb `26_08_15_01` のコメント時点で
`TranHaibun` は0件で、開発用DB（`server-user163.db`）にも配分の実データはほぼ無い。
**データ移行の実質コストはほぼゼロ**である。

## 4. デメリット

### D1. 明細単位の状態遷移がJSON書き換えになる（最大の問題）
`KakuteiDay` `JitsuSu` `ShortSu` `EndFlag` `RelateNo2` はいずれも**SKU単位で遷移する**。
現状はすべて `UPDATE TranHaibun SET ... WHERE Id IN (...)` の単文で済んでいる
（`ShippingDb.cs:76,90,162,209`）。JSON内包にすると、

- 1SKUの確定でもヘッダ行のJSON全体を read-modify-write する
- 楽観排他がヘッダ単位になる。**同じ配分の別SKUを2人が同時に確定すると競合になる**
  （現状は行単位なので競合しない）。出荷指示確定・出荷処理はSKU行をチェックして一括処理する
  画面なので、この競合は実運用で起きる
- `ProcessShipping`（`ShippingDb.cs:186-213`）の「全行の `Vdu` を先に検証して1件でも
  食い違えば何も書かない」fail-fast も、粒度がヘッダに粗くなる

### D2. 引当再計算の索引が使えなくなる
`nk2(Id_Soko, Id_Shohin, Id_Col, Id_Siz)` は**引当再計算のために張った索引**
（`BaseDbHaibun.cs:11`）。JSON内包にすると SKU で索引が張れず、
`CalcHaibun2Reserve`（キー単位、1キーごとに2文）が毎回 **全件スキャン＋`json_each` 展開**になる。
`WriteEffectRunner` は1回の書込みごとにこれを呼ぶため、劣化が直接効く。
`SummaryStock` 側の集計SQL（`SummaryDb.cs:326`）は `WHERE t.Id=@0` でPK1行に絞ってから
`json_each` するので同じ問題が無い＝**既存伝票の書き方はそのまま流用できない**。

回避策は「配分明細を実テーブル（`TranHaibunMeisai`）に分ける」だが、それは
`Jmeisai` JSON方式という**この製品の伝票設計から外れる**（2テーブル構成の伝票は他に無い）。

### D3. `Tran99Meisai` を共用できない
`Tran99Meisai` に `KakuteiDay/JitsuSu/ShortSu/EndFlag/RelateNo2` は無い。選択肢は2つ。
- (a) 共通の `Tran99Meisai` に5列追加 → **全伝票のJSONペイロードに未使用列が増える**。
  `varchar(4000)` の名目上限（SQLiteは非強制だが他DB移行時に効く）にも近づく
- (b) 専用 `TranHaibunMeisai` を新設 → `BaseTranInputViewModel<TDen>` は明細が
  `ObservableCollection<Tran99Meisai>` 固定なので **M4の再利用メリットが大きく削れる**。
  基底のジェネリック化が別途必要

### D4. `ITranReserve` / `WriteEffectRunner` の前提を変える必要がある
`ITranReserve`（`BaseDb2Trans.cs:41-52`）は1行=1SKUのインターフェースで、
`WriteEffectRunner.After`（`:150-158`）は `item`/`org` から**1キーずつ**取り出す。
ヘッダ化すると「1行 → N個の `ReserveKey`」になり、インターフェースと抽出処理の作り直しになる。
`ReserveKey.From` の呼び出し（`ShippingDb.cs:167` ほか）も同様。

### D5. 入力画面の粒度と一致しない画面がある
`ShopHaibunInputViewModel` は **1商品を選び (店舗×SKU) クロス表に入力する**画面。
1回の登録が「店舗ごとに1ヘッダ」へ分かれるため、クライアント側では
「クロス表 → N個のヘッダ＋各Jmeisai」の組み立て/復元が必要になり、
現在の「行の平坦なリストを作って `InsertBulkParam` 1発」（`:652-690`, `:279`）より複雑になる。
洗い替え時の突き合わせ（既存ヘッダの再利用か削除か）も新規ロジックになる。

### D6. 照会系マトリクスSQLが `json_each` になる
配分問合わせ（`HaibunQueryViewModel.cs:19-36`）と商品別配分数
（`BaseHaibunInquiryViewModel.cs:317-343`）は `TranHaibun` を直接 SKU で `GROUP BY` している。
`json_each` 展開＋`json_extract` 比較になり、D2 と同じく索引が効かない。

### D7. I5 決定（2026-08-17 / REPLACE）の覆し
I5 は「`TranHaibun` は明細行単位のままとし、仮想ヘッダのキーで括る」と**明示的に決定済み**で、
`ShippingDb` / 出荷指示確定2画面 / 出荷処理入力 / 滞留・欠品例外 / 詳細設計3本
（`2026-08-18_I2I3_...` `2026-08-18_I7_...` `2026-08-18_I9_...`）がこれを前提に書かれている。
再決定にはこれらの改訂も含まれる。

### D8. `TranHoju`（補充）との非対称
`TranHoju`（`BaseDbHaibun.cs:274-`）は `TranHaibun` とほぼ同じフラット構造。
片方だけ構造化すると、自動補充（1.1以降）の実装時に2方式が並立する。

## 5. 影響範囲の規模感

| 対象 | 規模 |
|---|---|
| スキーマ・マイグレーション | 新テーブル作成＋`json_group_array`/`json_object` での移行SQL 1本（`UpdateDb` は任意SQL可）。実データほぼ0件のため低リスク |
| `CvBase` | `TranHaibun` 再定義、`ITranReserve` 改訂、明細クラス新設または `Tran99Meisai` 拡張 |
| `CvDomainLogic` | `SummaryDb` 引当SQL2本の書き換え（`json_each`化・索引戦略の再設計）、`ShippingDb` 全面（272行のほぼ全部） |
| `CvServer` | `WriteEffectRunner` のキー抽出（1→N）、`PartialUpdateDeniedColumns`、`HandleBulkInsert` の見直し |
| クライアント | 実装済み8画面すべて（`ShopHaibun` 855行 / `JuchuHaibun` 1148行 / `HachuHaibun` 1124行 の登録・読込・洗い替え部分、確定/出荷/滞留の3画面はSKU行選択UIの再設計、照会2画面のSQL） |
| テスト | `SummaryDbTests`（18参照）・`WriteEffectRunnerTests`（15参照）ほぼ全面 |
| ドキュメント | I5 決定の再決定＋詳細設計3本の改訂 |

## 6. 結論

**現状維持（フラット行＋仮想ヘッダ）を推奨する。** 理由は次の3点。

1. **D1（明細単位の状態遷移）が業務要件そのもの**である。配分は「SKU単位で確定し、SKU単位で
   欠品が返り、SKU単位で引当が解ける」データで、同一ヘッダ内のSKUが別々のタイミングで
   状態遷移する。ヘッダ内包JSONはこの粒度の同時更新に構造的に向かない。
   売上・仕入のような「伝票単位で一括確定して以後変わらない」データとは性質が違う。
2. **D2（引当索引）が性能上のクリティカルパス**にある。引当再計算は書込みごとに走る。
   JSON内包にすると索引が張れず、行数が減るメリット（M3）を打ち消す。
3. メリットの本体である M1（グルーピング消滅）・M2（洗い替えの往復削減）は、
   構造変更なしでも回収できる。

### 6.1 構造を変えずに得られる改善（推奨する代替案）

- **一括削除パラメータの追加**: `DeleteBulkParam`（条件指定削除）を1本足せば、M2の
  「N往復＋部分成功リスク」は解消する。`ShopHaibunInputViewModel:260-270` /
  `HachuHaibunInputViewModel:745-` の削除ループが消える。影響はサーバ1ハンドラ＋画面3本
- **仮想ヘッダを型として明示する**: `record HaibunHeaderKey(DenDay, NouhinDay, Id_Soko, Id_Tenpo, Kubun, RelateNo1)`
  を `CvBase` に置き、`ShippingDb.CreateShippingSlips` のタプル持ち回り（`:131,218,244`）と
  未実装画面（配分データメンテ・出荷指示明細書印刷・納入一覧表）のヘッダ表示で共有する。
  ヘッダ・明細の**見え方**は仮想ヘッダで満たせる
- **ヘッダ合計の表示**は照会用ビュー（`GROUP BY` した SELECT）で足りる。実列は不要

### 6.1.1 実装記録（2026-08-24）

**`TranHaibun` の現状構造を維持することを決定**し、6.1 の代替案を実装した。I5 決定は据え置き。

| # | 内容 | 変更ファイル |
|---|---|---|
| 1 | `DeleteBulkParam` / `DeleteBulkRow` / `DeleteBulkResult` を追加。行ごとに `ExpectedVdu` を持ち、1件でも競合すれば何も削除しない | `CvBase/Parameters.cs` |
| 2 | `HandleBulkDelete` を `HandleBulkInsert` と対称に追加。1トランザクションで全行を先に検証→削除し、引当キーを溜めて最後に1回 `FlushReserve` | `CvServer/Services/HandlerClass.cs` |
| 3 | `CoreServiceClient.DeleteBulkAsync` を追加し、配分3画面の削除ループを1往復へ置換 | `CvWpfclient/Helpers/Communication/CoreServiceClient.cs`、`ShopHaibunInputViewModel`、`JuchuHaibunInputViewModel`、`HachuHaibunInputViewModel` |
| 4 | 仮想ヘッダキーを `HaibunHeaderKey`（6列）として型で明示。`KeyColumns` / `KeyColumnsSql(alias)` でSQLの `GROUP BY` / `ORDER BY` と定義を一致させる | `CvBase/BaseDbHaibun.cs` |
| 5 | `CreateShippingSlips` のタプル持ち回りを `HaibunHeaderKey` へ置換（`GroupBy` / `CreateUriage` / `CreateIdoOut` / `order by`） | `CvDomainLogic/ShippingDb.cs` |
| 6 | 出荷処理の仮想ヘッダ単位化を固定するテストを追加（キー6列それぞれで伝票が分かれる／同一キーは1伝票／店種区分での伝票種別／全量欠品／キー定義とSQL展開の一致） | `Tests/TestServer/ShippingDbTests.cs` |

「ヘッダ合計列」は実装しない（6.1 のとおり照会SQLの `GROUP BY` で足りる）。

確認: ソリューションビルド 0 warning / 0 error、`TestServer.exe` 173/173 成功（既存168＋新規5）。
`dotnet test` は .NET 10 の Microsoft.Testing.Platform 移行により使えないため、
従来どおりテスト実行ファイルを直接起動した。

追加対応（同日）: `02Yosan` の予算マスタ2画面（`SalesStaffBudgetMasterViewModel` /
`ShopBrandBudgetMasterViewModel`）の `DeleteExistingBudgets` も同じ1件ずつ削除のループで、
かつ削除応答の `Code` を見ていなかった（失敗が黙って無視される）。
`CoreServiceClient.QueryListAsync` + `DeleteBulkAsync` へ寄せ、日数ぶん（最大31往復）の削除が
1往復になり、失敗は例外として既存の catch がダイアログ表示するようになった。

### 6.2 それでも構造化するなら

- キーは **3列ではなく I5 の6列**（`DenDay/NouhinDay/Id_Soko/Id_Tenpo/Kubun/RelateNo1`）
- 明細は **JSON内包ではなく実テーブル `TranHaibunMeisai`**（D1・D2・D6を同時に解消できる唯一の形）。
  ただし製品内で唯一の2テーブル伝票になり、`BaseTranInputViewModel` の再利用（M4）は失われる
- 着手時期は**未実装9画面の実装前**（M6）。実装後に回すとコストが跳ね上がる
- `TranHoju` も同時に（D8）
