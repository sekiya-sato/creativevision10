## [2026-10-05] HHT：出荷指示明細書・移動明細書・即時移動明細書の印刷

### 実施内容
- 旧CV `SubDlg_08prn_hht02` / `hhtlist05` / `hhtlist06`（即時移動は旧メニュー上 hhtlist06）を調査し、`Doc/spec/2026-10-05_HHT明細書印刷3画面_仕様.md` を作成。利用者決定: 出荷指示は `TranVulcanHht`、印刷済管理は列追加で再現、帳票はA4縦のみ。
- `Tran05Ido` / `Tran10IdoOut` に `IsPrint` を追加（UpdateDb 26_10_05_01）。旧印刷FLGはビット値のため `OldTableCommentAttr` は付けない。
- 3画面の View/VM を実装（移動2画面は共通基底 `BaseIdoDetailBookPrintViewModel`、通常発行で PartialUpdate、再発行は更新なし）。帳票は旧 `cvnet60prn02.qfm` を `printform/IdoDetailBook.qfm` として3画面共用（34列）。メニューの準備中表示を更新。
- UatVm シナリオ `hhtprint` を追加。UAT で見つかった、出荷指示の JAN 引当（Jan1のみ照合）を HhtProcessUpdateMap と同じ照合に修正。

### 検証
- CvWpfclient / CvServer build エラー0。UatVm `hhtprint` 24/24 PASS（開発DB複製）: 3画面JPGレイアウト判定問題なし、PDF生成・目視、通常発行→IsPrint=1、再発行で不変、再通常発行は0件。

### 残余リスク・未実施
- PostgreSQL/MariaDB 未実施。コード範囲・区分「すべて」・複数ページ伝票の印刷は未確認。印刷成否を判定できないため、PDF表示後に発行済みにする（失敗時は再発行で対応）。

## [2026-10-05] WMS連携（物流）：旧AMS連携調査・仕様・L01〜L04 実装

### 実施内容
- 旧CV AMS連携（`refer/cvnet_pkg` #32093/#35039 ほか）を調査し、`Doc/spec/2026-10-05_WMS連携_旧AMS連携調査と仮実装仕様.md` を作成。9章の仮置き（J-01〜J-10）を利用者決定として採用。手動送受信のみ実装し、自動実行は未登録（処理本体 `LogisticsLinkDb` を画面から分離）。
- 履歴2表 `TranLogisticsBatch/Line`、`MasterConfig` 物流連携5項目、ファイル形式 cv10-v1。L01 マスタ（PD/BSY）、L02 送信（ORDER: 配分 SendFlg 0→1→2、STOCK: 発注残・未受入移動、ZAIKO）、L03 取込・検査・反映（ORDERFIX/LACK は `ShippingDb.Commit`、STOCKFIX は仕入・移動受、INVENTORY は棚卸データ）、L04 照会・再出力・取消・除外・訂正版。
- 独立レビュー指摘（重複受信の二重計上、並行反映、成功経路の例外、作成中バッチ、売掛集計、例外分類ほか）を反映。

### 検証
- TestServer 1009/1009（物流35件）、TestSqlDialect 153/153、ソリューション警告0。UatVm で4画面のJPG表示確認（開発DB複製・一時設定）。

- UatVm 通しシナリオ `logisticsflow`（開発DB複製・UatVmSeed/LogisticsSeeder）を追加。送信（ORDER/STOCK/ZAIKO）→応答ファイル→取込・検査・反映（出荷確定・欠品・入荷確定・棚卸）→訂正版・除外・配置失敗の再出力・送信取消を実画面VMで確認し 127/127 PASS。表示崩れ（固定幅列の縮小・右端列のはみ出し）、入荷確定後に「送信後変更あり」と出る誤検出、反映済みで再検査が押せる点、種別切替で前回結果が残る点を修正。

### 残余リスク・未実施
- PCのファイル選択・取込失敗・在庫割れE40の実画面操作は未実施（サーバはテストで確認）。PostgreSQL/MariaDB 未実施。反映は同期実行（大量件数は未確認）。HHT 移動受は RelateNo1 が無く WMS の受入済み検査で検出できない。

## [2026-10-04] 積送中クリア：設計・実装・隔離DB検証

### 実施内容
- 詳細設計 `Doc/spec/2026-10-04_積送中クリア_詳細設計.md` を作成。利用者判断により既存I/Fを維持し、移動・HHT等の更新停止中に、現在の倉庫×商品×色×サイズの積送残を正負ともクリアする。
- `InTransitClearView/ViewModel` に倉庫別合計・正負数・SKU数、対象選択・停止確認、SKU残の再照合、補正移動受の一括登録を実装。備考は指定文言＋実行日時、源泉伝票・集計直接削除なし。メニューの準備中表示を更新。
- 生成伝票は同一倉庫の移動受・元出庫紐付けなし。全選択倉庫を1回の既存一括登録にまとめ、応答不明時は再実行をロックする。

### 検証
- CvWpfclient build警告0・エラー0。隔離SQLiteの実画面VM→gRPC UATは25判定PASS（正負残、未選択保持、再集計一致、再実行、SKU競合停止、数量上限超過停止）。実画面JPG2枚・レイアウト判定に問題なし。
- 初回UATのWITH SQLエラーをSELECT始まりの派生表に修正し再検証。独立Reviewer指摘なし。XAML/XML・CRLF・git diff --checkを確認。

### 残余リスク・未実施
- 停止確認はサーバ排他ではない。出庫番号別未受は残るため、クリア済み分を通常受入しない。源泉と集計が不整合なら再集計で残が復活し得る。
- 他DB・大量件数・通信障害注入は未実施。更新は新規隔離DBのみ。共有開発DB更新なし。

## [2026-10-04] 配分再設計 Step 7：通し検証（SQLite）

### 実施内容
- `Doc/spec/2026-10-04_配分再設計_Step7_通し検証_詳細設計.md` を作成し承認を得た（SQLiteのみ、通しシナリオ10手順、全件再集計一致を合格条件、回帰11本、移行手順書、方言の件は記録のみ）。
- 通しUAT `haibunflow`（`Doc/test/UatVm/Scenarios/HaibunFlowScenario.cs`）を追加。仕入・在庫・受注・取置の配分を同じDB・同じ商品で続けて動かし、配分確定・欠品実績・取置の売上変換と期限切れ・全件再集計との一致まで確認する。
- シーダーが migration 前の複製DBで引当を再計算して失敗する問題（transfer / stocktake）に対し、共通の `SeedSchema.ApplyMigrations` を追加して受注出荷・移動・棚卸のシーダーで使うようにした（受注出荷の列追加の回避策を置換）。
- 移行手順書 `Doc/spec/2026-10-04_配分再設計_移行手順.md` を作成。設計判断記録 2.12（SQLiteのみ・方言変換を通らない配分SQLは10.2）を追記。

### 検証
- `haibunflow` 27判定PASS（全件再集計の前後で対象商品の月次在庫・現在庫・引当が一致、JPG6枚の表示崩れなし）。
- 回帰：配分6本・transfer・stocktake・uat01screen・shiire20260926 はPASS。hachu20260926 は20判定中2件FAIL（既定条件の件数が開発DBの発注追加と当日の変化で変わったため。配分は読まない画面）。
- 開発DB複製（11.3GB）で migration 26_10_03_01〜03 は141ms・エラーなし、引当の全件再集計270ms、権限明細の付け替えと自動実行ジョブの設定行追加を確認。複製DBは削除。

### 残余リスク・未実施
- PostgreSQL / MariaDB は未実施（利用者判断）。開発DBに未完了の配分が無いため、本番相当データでの引当突き合わせと再集計時間は未確認。hachu20260926 の件数依存の判定は未修正（範囲外）。

## [2026-10-03] 配分再設計 Step 6：メニュー整理・旧画面削除・区分名の統一

### 実施内容
- `Doc/spec/2026-10-03_配分再設計_Step6_メニュー整理・旧画面削除_詳細設計.md` を作成し承認を得た（配分確定の統合、権限の付け替え、出荷帳票から取置を除外、区分名、発注メニューのショートカット名、店舗配分入力の削除）。
- 配分確定(商品)/(得意先) を並び順切替つきの `HaibunCommitView/ViewModel`（配分確定）1画面に統合（Shohin を git mv、Tokui を削除）。店舗配分入力 `ShopHaibunInput*` と検索条件ダイアログを削除。
- メニュー「■ 配分・出荷」を D4 の形（配分入力／確定／帳票／照会／補充(1.1以降)）に並べ直し、倉庫業務の配分確定を差し替え、発注メニューのショートカットを「仕入配分入力(伝票別)」に改名。
- 権限明細の付け替え migration（UpdateDb 26_10_03_03。食い違いは許可優先、後継が既にあれば残す）と初期データの機能IDを更新。
- 区分名を `HaibunKubunNames`（CvBase）に集約し、出荷指示明細書・配分出荷リスト・納入一覧表の旧名・英語名を置換、3帳票とも取置を除外。`EnumHaibun` 3/4/5/7 に `[Obsolete]`。古いコメント（配分問合わせ・引当問合わせ・RelateNo1・CostUpdateDbReval）を更新。設計判断記録 2.11 を追記。

### 検証
- ソリューションbuild成功（警告0）。TestServer 974件成功（権限付け替えmigration・区分名を追加）、TestSqlDialect 153件成功。
- 開発DB複製で UatVm 6本PASS（haibunscreen 14・haibunorder 12・haibunstock 11・haibunreceipt 14・haibunreservation 20・juchushipping 37判定）。配分確定は商品順・出荷先順の両方でJPGと表示崩れ判定を確認。複製DBは削除。

### 残余リスク・未実施
- migration 26_10_03_03 は PostgreSQL/MariaDB で未実行（Step 7 の3DB確認で行う）。納入一覧表・配分出荷リストの取置除外は単体・UATの判定なし（SQL条件の追加のみ）。

## [2026-10-03] 配分再設計 Step 5：取置配分入力

### 実施内容
- `Doc/spec/2026-10-03_配分再設計_Step5_取置配分入力_詳細設計.md` を作成し承認を得た（3列追加、在庫不足は警告のみ、売上変換の単価は取置日の上代、期限翌日0:50の自動取消を既定有効、在庫拠点は店舗自身、欠品実績から区分6を除外）。
- `TranHaibun` に Id_Customer / LimitDay / EndReason と `EnumHaibunEndReason`、migration（UpdateDb 26_10_03_02）。区分6の入力検査（顧客・期限日・出庫元=店舗・元伝票なし）を `AllocationRules` に追加。
- `CvDomainLogic/ReservationDb.cs`：売上変換（店舗×顧客ごとに Tran01Tenuri P売上、税は POS と同じ伝票単位・店舗端数処理、在庫計上、引当解除）、取消、期限切れ自動取消。API `ReservationConvertParam` / `ReservationCancelParam`、日次タスク「取置期限切れ自動取消」（MasterConfig 既定・SchedulerService・Program）。
- 新画面 `CustomerReservationAllocationInput`（一覧・売上変換・取消・期限変更・数量変更・取置登録、POS二重計上の注意、在庫超過の警告）。メニューの配分グループへ追加し、雛形 `ReservationInput*` を削除。滞留・欠品例外の欠品実績から区分6を除外。
- 取消コマンドを `DoCancelCommand` にすると BaseWindow が閉じるときに実行してしまうため `CancelReservationCommand` にした（UATで画面終了が止まって判明）。設計判断記録 2.10 を追記。
- 作業ログが800行を超えたため、2026-09-09〜10 の UAT 検証記録（判断を含まない検証結果のみ）を削除した。

### 検証
- ソリューションbuild成功（警告0）。TestServer 969件成功（ReservationTests 15件を追加。ジョブ数の増加に合わせて既存3件を更新）。
- 開発DB複製で `UatVm haibunreservation` 20判定PASS（登録・期限初期値・在庫超過警告・期限3日以内の色・期限変更・数量変更・売上変換で店舗売上・取消・期限切れ自動取消・状態表示・欠品実績に出ない、JPG2枚の表示崩れなし）。JPGで列の押し縮めと入力欄の切れを見つけ修正。複製DBは削除。

### 残余リスク・未実施
- 独立レビュー指摘を別commitで反映：店舗を選び直したら再検索まで操作不可（前の店舗へ登録する不具合）、登録確認に店舗名、サーバで売上日・取消日の形式と出庫元=店舗を検査、過去の期限日は確認、在庫の全件再集計一致テストを追加（TestServer 972件成功、UAT 20判定PASS）。売上変換の店舗売上は金種別売上に出ない点を仕様に運用注意として明記。
- POS で同じ商品を会計すると二重計上になる（連携は1.1以降、運用で防ぐ）。PostgreSQL/MariaDB では未実行。

## [2026-10-03] 配分再設計 Step 4：仕入配分の入荷割当・仕入配分入力

### 実施内容
- `Doc/spec/2026-10-03_配分再設計_Step4_仕入配分入力_詳細設計.md` を作成し承認を得た（ArrivedSu列方式、部分入荷は店舗コード順、紐付かない既存は入荷済み扱い、商品別新設＋伝票別、同一倉庫の仕入のみ、入荷済み超過の確定はエラー）。
- `TranHaibun.ArrivedSu` と migration（UpdateDb 26_10_03_01）を追加。`CvDomainLogic/ArrivalDb.cs` で発注×SKUの「仕入数−確定済み」を未完了の仕入配分へ店舗コード順に割り当て、引当を引き直す。呼び出しは仕入の書き込み（WriteEffectRunner）・HHT取込・配分保存・配分確定の前後・全件再集計。
- 引当の算式を「EndFlag=0の全区分、区分0はArrivedSu」に変更（ReserveTargetWhere/ReserveQtySumExpr/AllocationRules.ReservedQty）。区分0の新規は発注Id必須、確定数はArrivedSu以下（NotArrived）。
- 新画面 `PurchaseReceiptAllocationInput`（仕入配分入力(商品別)）。発注入荷予定を配分先×SKUへ按分し、納品予定日順に発注へ割り付け（既存の紐付けを優先）、発注数超過はブロック。発注配分入力を「仕入配分入力(伝票別)」としてメニューに置き、SKU見出しに入荷数を追加。配分確定画面に入荷済列、確定数の初期値・一括設定を入荷済みまでに。店舗配分入力(初回)をメニューから外した（ファイル削除はStep 6）。
- 独立レビュー指摘を修正：仕入配分の出荷売上にRelateNo1(発注Id)を入れない、保存し直しで入荷済みの紐付けを維持、確定ダイアログで未入荷の仕入配分を警告、ArrivedSuを部分更新の禁止列へ、古いコメントの更新。設計判断記録 2.9 を追記。

### 検証
- ソリューションbuild成功（警告0）。TestServer 956件成功（入荷割当・卸先出荷の紐付け・指示取消・区分0の保存拒否などを追加）。
- 開発DB複製で `UatVm haibunreceipt` 14判定PASS（按分6/4→登録、仕入5でTK入荷5、確定画面の入荷済列と初期値、TK確定・欠品1、追加仕入でTS4、仕入返品でTS3、引当3、伝票別の入荷8、JPG3枚の表示崩れなし）。`haibunscreen` 14判定・`juchushipping` 37判定もPASS。複製DBは削除。JPGで「入荷済」「有効在庫」の見出し切れを見つけ列幅を修正。

### 残余リスク・未実施
- **migration適用後に全件再集計の実行が必要**（実行まで引当がずれて見える）。本番相当データで RelateNo1=0 の区分0件数は未確認。
- PostgreSQL/MariaDB では未実行。割当順の店舗コードは文字列比較（桁数不揃いだと順序が直感と異なる）。全件再集計は全発注の仕入配分の Vdu を更新するため、入力中の配分画面が競合になることがある。

## [2026-10-03] 配分再設計 Step 3：在庫配分入力

### 実施内容
- `Doc/spec/2026-10-03_配分再設計_Step3_在庫配分入力_詳細設計.md` を作成し承認を得た（在庫品配分・店舗出荷依頼・移動指示を統合、卸先単価＝上代×掛率の1円未満切捨、比率基準は売上実績・前回配分・手入力、パターン登録は1.1以降、店舗配分入力は初回専用）。
- `InventoryAllocationInputView/ViewModel` を新設。タブ1は商品一覧（在庫ありのみ・滞留日数）、タブ2は配分先×SKUマトリクスと按分パネル（同数／比率、端数、総数、対象SKU）、配分先の追加・削除、前回の配分先読込、洗い替え登録。配分先は倉庫・卸先・売仕店・直営店。
- 按分の純粋関数 `CvBase/AllocationCalculator.cs`（同数・比率・切捨／四捨五入で総数を超えない）と単体テストを追加。
- 店舗配分入力の区分から在庫配分を外して初回専用にし、メニューに在庫配分入力を追加（配分・倉庫業務）。雛形の在庫品配分・店舗出荷依頼・移動指示(SKU/商品)を削除。
- 一覧の最終売上日は直近1年に限定（全履歴のJSON展開で約15秒→0.3秒未満）。上代の系統は配分先の店種で選ぶ。

### 検証
- ソリューションbuild成功（警告0）。TestServer 951件成功（新規4件）。
- 開発DB複製で `UatVm haibunstock --manage-server` 11判定PASS（前回配分先読込、同数5→5/3、前回配分比率25/75→2/6、登録、卸先単価＝上代×60%、配分確定で卸先＝出荷売上・直営店＝移動、在庫0・引当0、JPG2枚の表示崩れなし）。複製DBは削除。

### 残余リスク・未実施
- 売上実績を比率の基準にする経路はUATで通していない（単体の按分と前回配分経路のみ）。名前付きパターン・消化率等の参考表示は未実装（1.1以降）。

## [2026-10-03] 配分再設計 Step 2：受注配分入力(商品別)

### 実施内容
- `Doc/spec/2026-10-03_配分再設計_Step2_受注配分(商品別)_詳細設計.md` を作成し承認を得た（得意先軸＝商品別マトリクス画面、受注日の古い順に割り付け、在庫内読込を含む、得意先展開は在庫配分へ）。
- `SalesOrderAllocationInputView/ViewModel` を新設。倉庫＋商品で行＝得意先・列＝SKUのマトリクスを作り、受注残読込／在庫内で受注日順に読込／0クリア／登録（`SaveHaibunAsync` で洗い替え1往復）を実装。SKU見出しに在庫・受注残・配分・配分後在庫、受注残超過セルは橙、受注の無いセルは灰色で入力不可。
- 割り付け規則を `CvBase/HaibunOrderDistributor.cs`（純粋関数）に置き、単体テストを追加。受注残超過分は受注に紐付かない配分（RelateNo1=0）とし、最も新しい受注の単価を使う。
- メニュー：「受注配分入力」を「受注配分入力(伝票別)」に改名し、「得意先別配分入力」を「受注配分入力(商品別)」に置換。`TokuiHaibunInput*`（雛形）を削除。
- UatVmに `haibunorder` シナリオを追加。表示崩れ判定は要素と同じ文字描画方式で測るよう修正（Display/Idealの差による誤検知）。

### 検証
- ソリューションbuild成功（警告0）。`dotnet test --project Tests/TestServer/TestServer.csproj` 947件成功（新規4件）。
- 開発DB複製で `UatVm haibunorder --manage-server` 12判定PASS（受注日順の割り付けA4・B3・C1、引当8、受注残超過→紐付かない2、洗い替えで重複なし、JPG2枚の表示崩れなし）。`haibunscreen` も14判定PASSを再確認。複製DBは削除。

### 残余リスク・未実施
- 受注のない得意先への配分（旧「得意先展開」）は Step 3（在庫配分）で扱う。SKU数が多い商品は横スクロールになる。

## [2026-10-03] 配分再設計 Step 1：保存の原子化・確定で即伝票作成

### 実施内容
- 配分再設計の基本設計と Step 1 詳細設計を `Doc/spec/2026-10-03_配分再設計_*.md` に作成し、利用者承認（D1〜D11）を反映した。
- `HaibunSaveParam` を追加し、配分入力3画面（受注・発注・店舗）の洗い替えを1トランザクション化。修正可能条件をサーバで強制し、店舗配分入力が確定済み・未送信の指示を消し得た不具合を是正した。
- `HaibunCommitParam` / `ShippingDb.Commit` を追加し、確定数反映・在庫検査・伝票作成・引当解除を1段階にした。旧 `ShippingConfirm/Cancel/CreateParam`、`ConfirmShipping/CancelConfirm/ProcessShipping`、出荷処理入力画面を削除した。在庫検査と制約は `CvDomainLogic/AllocationRules.cs` に集約した。
- 出荷指示確定を「配分確定(商品/得意先)」に変更（確定数・欠品列、一括設定、取消削除、取置除外）。滞留・欠品例外は未確定滞留＋指示取消（確定数0）、出荷指示明細書は未完了配分を既定対象に変更した。`ShippingStagnationList.qfm` の見出し「確定日」を「基準日」にした。
- UAT-02（`JuchuShippingScenario`）を新しい流れに書き換え、設計判断記録 2.8 を追記した。
- 独立レビューの指摘で、旧状態（確定済み・未出荷）行の確定時に保存済み引当が古く偽の在庫割れになる問題を修正（検査前に対象キーの引当を引き直す）。確定日空の防御、受注配分の修正対象条件の定数化も実施。
- 画面UATシナリオ `haibunscreen`（`Doc/test/UatVm/Scenarios/HaibunScreenScenario.cs`）と、JPG保存・表示崩れ自動判定 `Doc/test/UatVm/ScreenLayoutCheck.cs` を追加。検出した崩れを修正した：配分確定は一括ボタンを一覧見出し行へ移し（「条件クリア」が右端で切れていた）列幅を窓幅に収めた（確定数・欠品が横スクロール外だった）。滞留・欠品例外は条件行をWrapPanel化（「検索実行」が窓外）し、列見出し・日付・種別・色サイズが切れない列幅にした。

### 検証
- `CvServer`・`CvWpfclient`・`UatVm` build成功。`dotnet test --project Tests/TestServer/TestServer.csproj` 941件成功（新規 AllocationRules 11件・HaibunSaveHandler 6件・Commit 8件を含む）。
- 開発DBの複製（隔離SQLite）で `UatVm.exe juchushipping --manage-server --hide-views` 37判定すべてPASS（在庫割れ拒否、欠品確定、指示取消、洗い替え競合、直営店向け移動伝票）。複製DBは検証後に削除。
- 同じく `UatVm.exe haibunscreen --manage-server`（View表示あり）で14判定すべてPASS。配分確定(商品/得意先)・確定後・滞留・欠品実績・出荷指示明細書の6画面をJPG保存（`Doc/test/uat20261003/haibun/screens/`、git管理外）し目視確認。

### 残余リスク・未実施
- 倉庫・出荷先・商品などコード＋名称の長い列は列幅で切れる（判定上は記録のみ）。物流連携（SendFlg）は未実装のまま。PostgreSQL/MariaDB での実行は未確認（新規SQLはSQLite方言の単純なUPDATE/SELECTのみ）。

## [2026-09-29] 仕入帳票(05Shiire)の返品符号(CalcFlag)適用

### 実施内容
- 仕入メニュー UAT で、品番別仕入チェックリスト、ブランド別仕入金額表、仕入先別仕入推移表の「返品･値引も含める」が返品(Kubun=20、プラス保存)を加算していることを確認した。
- 3帳票の集計 SUM（明細JSONの Su/Kingaku、ヘッダの SuTotal/KingakuTotal/Tax/金額計）に `h.CalcFlag` を掛けた。「仕入のみ」の帯 `BETWEEN 10 AND 19` は変更なし。品番別の冒頭コメントを実態に合わせた。

### 検証
- `CvWpfclient` build成功。UAT ReportRunner(2022/05)で19成功/4SKIP。返品込み金額計 98,971,206円（仕入99,948,814円－返品977,608円）とブランド別・仕入先別・品番別の代表値が cv-sqlite の独立集計と一致。仕入のみの結果は修正前と不変。

### 残余リスク・未実施
- 仕入伝票印刷の「お得意様コード」表記は未対応（別途確認）。
## [2026-09-28] 作業ログアーカイブの整理と退避ルール変更

### 実施内容
- `Doc/aicoding_log_009〜020.md` から重要判断33件を抽出し `Doc/spec/2026-09-28_設計判断記録.md` を新設した。001〜008（2026-06以前）は抽出せず、001〜020 の20ファイルを削除した。
- `AGENTS.md` §9 と `.github/copilot-instructions.md` の退避ルールを「重要判断だけ設計判断記録/該当仕様書へ移し残りは削除、番号付きアーカイブは作らない」へ変更した。
- `ConvertDbTran.cs` のコメントと R2 適格返還請求書詳細設計の旧ログ参照を差し替えた。

### 検証
- `rg "aicoding_log_[0-9]"` で追跡対象内の参照残りなし、`git diff --check` 実施。

## [2026-09-26] 卸分析帳票(21OroshiAnalysis)の返品符号(CalcFlag)適用

### 実施内容
- 得意先別売上日報/月報、担当者別売上半期、個人別売上ランキング、販売員予算実績、担当得意先予算実績、半期報告、卸店売上実績の8帳票で、Tran00Uriage/Tran01Tenuriを直接合算するSUM（SuTotal/KingakuTotal/JodaiTotal/GedaiTotal/Tax/Nebiki00Total、明細JSONのSu/Kingaku、得意先別売上日報のHAVING）に `CalcFlag` を掛け、返品(Kubun=20/21)が売上に加算されないようにした。件数(COUNT)、予算(UriYosan)、符号付きCTE列の外側再集計・累計は変更なし。全社受払表(SummaryStock)は対象外。

### 検証
- `CvWpfclient` build成功（警告0、エラー0）。`git diff --check`異常なし。
- cv-sqlite: 開発DB全期間で、売上は数量88,781→88,613・金額234,166,163→233,631,003、店売は数量6,578,969→6,509,877・金額19,440,770,442→19,172,332,640（返品分の2倍が減少）。店売返品の明細JSON Suも正値保存でCalcFlag適用後に負となることを確認。

### 残余リスク・未実施
- 帳票出力（PDF/CSV）での画面確認は未実施。
## [2026-09-26] 発注帳票の返品符号(CalcFlag)適用

### 実施内容
- 受注UATで判明した返品符号未適用(受注側74868e18)と同型の不具合を発注メニューで修正。`SupplierHachuTableViewModel`（ヘッダSuTotal/KingakuTotal/Tax/Total/JodaiTotal/原価率）、`ShohinHachuTableViewModel`/`ShohinHachuSummaryTableViewModel`（明細JSONのSu/Kingaku）に `h.CalcFlag` を掛け、「返品等を含める」時に返品(Kubun=20)が加算されないようにした。
- 入荷済数の突合: `DeliveryScheduleTableViewModel`/`HachuZanKanriTableViewModel`（Tran03Shiire.SuTotal）、`PendingShiireListViewModel`（仕入明細Su）に仕入側CalcFlagを追加し、`RelateNo1`で紐づく仕入返品が入荷数に加算されないようにした（発注残完了設定=BaseZanCompletionViewModelの `SUM(Su * a.CalcFlag)` と整合）。

### 検証
- `CvWpfclient` build成功（警告0、エラー0）。`git diff --check`異常なし。
- cv-sqlite: 開発DBのTran13HachuはKubun=10/CalcFlag=1のみのため発注側集計は修正前後で一致（明細Su計4,148,630で同値）。入荷突合は発注Id=6693（仕入44 + 仕入返品6）が修正前50→修正後38に是正されることを確認。

### 残余リスク・未実施
- 発注返品伝票が開発DBにないため、発注表3本の返品減算は実データ・帳票出力で未確認。
- 21OroshiAnalysis（得意先別売上日報/月報、担当者別半期、個人別売上ランキング、販売員予算実績、担当得意先予算実績、半期報告、卸店売上実績）はTran00Uriage/Tran01TenuriをKubun絞込もCalcFlagもなく合算しており、返品が売上に加算される（開発DB: 売上返品35件/84点/267,580円、店売返品18,629件/34,546点/134,218,901円）。依頼範囲外のため未修正。`CorporateInOutReportViewModel`はSummaryStock集計で対象外。

## [2026-09-26] 受注UAT結果を受けた集計符号・残突合の修正

### 実施内容
- 返品符号(CalcFlag)未適用: `ShouhinJuchuTableViewModel`/`ShouhinJuchuSummaryTableViewModel`（明細JSON集計のSu/Kingaku）、`TokuiSakiJuchuTableViewModel`（ヘッダSuTotal/KingakuTotal/Tax/JodaiTotal/かけ率）で、返品(Kubun=20)を「返品等を含める」表示にすると数量・金額が減算されず加算されていた不具合を、`h.CalcFlag` を掛けて修正した。`JuchuZanKanriTableViewModel`/`TokuiSakiUriageYoteiTableViewModel`のTran00Uriage合算にも売上側CalcFlagを追加した。`JuchuZanCompletionSettingViewModel`(BaseZanCompletionViewModel)は元々CalcFlag適用済みのため対象外。`TantoTenjiJuchuGoukeiTableViewModel`/`JuchuBestTableViewModel`は常にKubun 10-19に絞るため実害なし（変更なし）。`NouhinYoteiTableViewModel`と`JuchuInputViewModel`の一覧・明細印刷SQL（旧cvnet形式のCSV踏襲）は集計を伴わない生データ表示のため、符号変更の要否は判断保留として変更していない。
- RelateNo1突合の誤一致: `Tran00Uriage.RelateNo1=受注Id`だけの突合では、無関係な旧データ（別得意先・受注日より前の売上）が同Idの受注へ誤って合算される事例（受注Id=8）を確認。`BaseZanCompletionViewModel`に既定空文字の`ActualExtraMatchCondition`（仮想プロパティ）を追加し、`JuchuZanCompletionSettingViewModel`だけで`a.Id_Tokui = h.Id_Tokui AND a.DenDay >= h.DenDay`を追加する形にした（発注側`HachuZanCompletionSettingViewModel`は既定のまま変更なし）。`JuchuZanKanriTableViewModel`/`TokuiSakiUriageYoteiTableViewModel`のTran00Uriage突合も同条件の相関サブクエリへ変更した。

### 検証
- `CvWpfclient` build成功（警告0、エラー0）。`git diff --check`異常なし。
- cv-sqlite MCPで2026/07 UATデータ（`UATJ-`、受注9件、返品Id=9 Kubun=20）を独立集計し、修正前後の差分を確認（UATJ-P02: 返品を含む集計が18→12(受注のみと同じ符号方向)に是正、得意先別受注表のUATJ-TK2も18/22,000円→12/16,000円に是正）。受注Id=8への旧売上混入も、修正後は該当なし(旧: uriageSu=7→新: 0、残数-1→6)に是正されたことを確認。
- 既存`Doc/test/uat20260926/jyuchu/ReportRunner`をCvServer(Development, https://127.0.0.1:5012)へ再実行し、28帳票すべて生成成功（新規タイムスタンプフォルダに出力、既存run.jsonは上書きしていない）。生成CSVで上記の是正結果（UATJ-TK2の得意先別受注表、UATJ-P02の商品別受注表・集計表、受注No.8の受注残管理表・得意先別売上予定表）を実データで確認した。検証用CvServerはPDF確認後にプロセス終了（通常終了経路が使えずtaskkill /Fで強制終了。DB更新なし、リモートUAT時と同じ既知の制約）。

### 残余リスク・未実施
- `NouhinYoteiTableViewModel`と`JuchuInputViewModel`の一覧・明細印刷SQLは、符号を付けるべきか（Uriage側`NouhinBookPrintR2ViewModel`は同種の生データ表示でもCalcFlagを掛けている）判断保留。
- 受注残完了設定の完了実行(`ExecuteCompletionCommand`、DB更新)は本修正の検証範囲外（読み取り確認のみ）。

## [2026-09-26] 予算・発注UAT結果を受けた画面修正

### 実施内容
- 予算(667b50ef): 販売員予算マスタメンテ一覧の販売員列を `VShain` のコード・名称表示に変更。両予算マスタメンテの日付欄で文字数カウンタが下段ラベルに重なる問題を非表示で解消。店ブランド予算マスタ(月)のブランド名表示幅を拡張。項目名を「土日係数」「休業日」に統一した。
- 発注(d823f123): 発注配分入力で未配分時の納品日初期値を、発注の納品予定日優先・空なら発注日に変更。発注残完了設定の発注日を `yyyy/MM/dd` 表示にし、関連伝票No列の見出し切れを解消。商品別発注表の「色ｻｲｽﾞ別」を「色サイズ別」に統一した。
- 受注(3aca4007): 受注残完了設定の受注日表示と関連伝票No列幅を発注側と同様に修正した。

### 検証
- 各修正後に `CvWpfclient` build成功（警告0、エラー0）。`git diff --check` 異常なし。修正後の実画面確認は未実施。納品予定日ありの発注が開発DBにないため、配分入力の納品予定日優先は実データで未確認（残余リスク）。

## [2026-09-26] 発注メニューの画面・帳票UAT

### 実施内容
- 開発DBの既存2025/01発注2件を読み取り専用で使用。発注メニュー11画面を実View/ViewModelで開くUatVmシナリオを追加し、配分入力の発注検索と明細読込、納品予定照会、発注残完了設定の検索を確認した。
- 発注帳票7種と発注入力の一覧・明細印刷を実ViewModelのSQL生成とCvServer印刷経路で確認する専用runnerを追加。結果・PDF・画像を`Doc/test/uat20260926/hachu`へ記録した。

### 検証
- CvServer、UatVm、帳票runnerのbuild成功。UatVmは19判定PASS・12画面画像を目視確認。帳票は24PDF・27ページを生成し、CSV行数、PDF構造、ページ数、代表/最終ページの描画を確認。納品予定日が全発注で空欄のため納品予定表4条件はSKIP。DB更新操作は未実施。

## [2026-09-26] 予算メニューの帳票・WPF画面UAT

### 実施内容
- 開発DBの2026/08に店舗ブランド予算10行・販売員予算9行を追加。既存実績1伝票を利用し、予算の有無、ゼロ予算、店舗・ブランド・販売員の集計を確認した。
- 実ViewModelのSQLとCvServer印刷経路を使う専用runnerで基本分岐14件のPDFを `Doc/test/uat20260926/yosan` に保存。投入スクリプトと結果文書を同フォルダに記録した。
- UatVmで予算メニュー8画面を開き、月次予算読込・店別売上検索と画面描画を確認。8PNGとJSONLを同フォルダに保存し、PDFと合わせて結果報告を集約した。大メニューUATのAI向け作業手順を別文書に記録した。

### 検証
- CvServerとrunnerのbuild成功（警告0）。14PDF・21ページの生成、CSV行数、PDF構造、代表ページの描画、主要金額を確認。UatVm build成功（既存nullable警告3件）、18判定PASS・8画像を目視確認。画面のF6操作、マスタ一覧取得ダイアログ、前年実績を伴う前年比計算は未実施。

## [2026-09-25] 販売員別予算マスタの操作と日別一覧を店ブランド予算に合わせて整理

### 実施内容
- 店ブランド予算画面(7ac900af/612cebe1)の整理内容を、Tableの異なる販売員別予算画面に適用した。「予算作成」と重複する自動配分・累計再計算の各コマンドから `[RelayCommand]` を外しボタンを削除（メソッド自体は `CreateBudget`/内部処理から継続利用）。自動配分完了メッセージを「日別予算を作成しました。」に統一した。
- 日別予算を1～31日の単一DataGridへ変更し、数値列と列見出しを右寄せにした。月計の売上・粗利合計と配分残は一覧上部の固定行へ移し、月計ラベルを太字、明細ヘッダは `MenteDataGridColumnHeader` ベースのスタイルにした。
- 前半・後半の表示用コレクション(`FirstHalfDailyBudgets`/`SecondHalfDailyBudgets`)と `RefreshDailyBudgetViews` を削除。保存・配分・累計の内部処理、既存の列構成（日付/曜日/売上予算/粗利予算/売上累計/粗利累計、イベント関連列なし）は維持した。予算登録ボタンを予算保存に改称し、作成/読込/保存/削除のTooltipに操作結果と未保存編集への影響を明記した。

### 検証
- `CvWpfclient` build成功（警告0、エラー0）。`git diff --check` 異常なし。実画面でのUatVm動作確認は未実施（残余リスク）。

## [2026-09-24] 店ブランド予算マスタの操作と日別一覧を整理

### 実施内容
- 「予算作成」と重複する自動配分ボタン・コマンド、および編集時に自動更新される累計再計算ボタン・コマンドを削除。予算決定を予算保存に改称し、作成・読込・保存・削除のTooltipに操作結果と未保存編集への影響を明記した。
- 日別予算を1～31日の単一DataGridへ変更し、数値列と列見出しを右寄せにした。月計の売上・粗利合計と配分残は一覧上部の固定行へ移し、縦スクロール中も確認できるようにした。
- 前半・後半の表示用コレクションと振り分け処理を削除。保存・配分・累計の内部処理は維持した。

### 検証
- `CvWpfclient` build成功（警告0、エラー0）。XAML構文と `git diff --check` を確認。`verify-wpf-screen-runtime` の UatVm で実Viewを開き、単一DataGrid・31行・月計の固定表示を4項目PASSで確認し、描画画像で列の収まりと見出しを目視確認した。

## [2026-09-24] 店舗・得意先イベントの重要度を enum 化

### 実施内容
- `EnumPromotionRank`（0=低 / 1=中 / 2=高）を追加し、`TranShopPromotion.Rank` / `TranTokuiPromotion.Rank` に `[ForeignKey(nameof(EnumPromotionRank))]` と `EnRank` ラッパーを付けた。DB 列は int のまま。
- 両メンテ画面の固定 `RankOptions` と SQL `CASE` による `RankName` を廃止し、選択肢・一覧表示とも `Enum.GetValues` + `EnumCommentDisplayConverter` で enum 由来にした。`RankName`（ResultColumn）は参照が無くなったため削除。
- 他マスタ画面の enum・入力欄チェックでは他に不一致なし。社員マスタの権限プロファイルは権限仕様未決のため非表示のまま。

### 検証
- ソリューション build 成功、`git diff --check` 問題なし。画面の実行確認は未実施。

## [2026-09-24] 得意先マスタ画面へ税・伝票関連の4項目を追加し、顧客マスタの性別表示を修正

### 実施内容
- `MasterEndCustomerMenteViewModel` の一覧 SQL で性別名称が `EnumGender` と逆（1=男性,2=女性）だったため、定義どおり 1=女性,2=男性 に修正した（commit 済）。
- 得意先マスタ画面に入力欄が無かった `TaxCalcUnit`（税計算単位）/ `TaxRounding`（消費税端数処理）/ `TaxPriceType`（外税内税区分）/ `SlipFormType`（伝票印字タイプ）の ComboBox を「税・請求」タブへ追加した。
  - 選択肢は `Enum.GetValues<T>()`、表示は `EnumCommentDisplayConverter`（`[Comment]`）で enum から生成する。
  - `MasterTorihiki` / `MasterTokui` に `EnShime1` と同形の `[Ignore][JsonIgnore]` enum ラッパー `EnTaxCalcUnit` / `EnTaxRounding` / `EnTaxPriceType` / `EnSlipFormType` を追加した。DB 列・保存経路は変更なし。

### 検証
- `CvWpfclient` build 成功、`git diff --check` 問題なし。画面の実行確認は未実施。

## [2026-09-22] 受注に追加受注を追加し受注・発注の帳票フィルタを帯へ揃える

### 実施内容
`EnumJuchu` に `FollowUpJuchu=11`（追加受注）を追加した。これに伴い、受注と発注の画面・帳票を実態へ揃えた。

**受注（EnumJuchu: 10 受注 / 11 追加受注 / 20 受注返品 / 30 値引 / 99 その他）:**
- `JuchuInputViewModel` の区分選択肢と `KubunNameSql` に追加受注を追加した。
- `JuchuHaibunInputViewModel` の選択肢・ラベル辞書・`FormatJuchuKubun` の3箇所に追加受注を追加した。
- 受注帳票7本（`TokuiSakiUriageYoteiTable` / `TokuiSakiJuchuTable` / `TantoTenjiJuchuGoukeiTable` / `ShouhinJuchuTable` / `ShouhinJuchuSummaryTable` / `JuchuZanKanriTable` / `JuchuBestTable`）の `h.Kubun = 10` を `h.Kubun BETWEEN 10 AND 19` へ変更した。あわせて3本の doc comment「受注(Kubun=10)のみ」を受注帯の記述へ直した。

**発注（EnumHachu: 10 発注 / 11 追加発注 / 15 自動発注 / 20 発注返品 / 30 値引 / 99 その他）:**
- 発注帳票6本（`HachuForm` / `SupplierHachuTable` / `HachuZanKanriTable` / `ShohinHachuTable` / `ShohinHachuSummaryTable` / `PendingShiireList`）の `h.Kubun = 10` を `h.Kubun BETWEEN 10 AND 19` へ変更した。
- `DeliveryScheduleTable` の `h.Kubun IN (10,11,15)` という3値列挙も他と書き方を揃えて帯へ変更した。
- `HachuHaibunInputViewModel` の選択肢・ラベル辞書・`FormatHachuKubun` に追加発注(11)・自動発注(15)を追加した。`HachuInputViewModel` は元から両方を持っていた。

区分を離散指定していると、帯の中に区分が追加されたときその区分が静かに抽出条件から漏れる。売上・仕入で採った方針（commit 87c4b13e / b01fa961）と同じく帯で書く形に揃えた。

### 影響
- これまで追加受注(11)・追加発注(11)・自動発注(15)は、上記13本の帳票の集計から漏れていた。帯化により計上されるようになる。特に `PendingShiireList`（入荷予定）では自動発注が入荷対象から外れていた。「返品・値引は入荷対象ではない」という元の意図は帯(10-19)で維持している。
- 更新処理は変更していない。受注残 `CompletionDb` と `WriteEffectRunner` は `Kubun` で絞っておらず、数量・金額の符号はヘッダの `CalcFlag`（`20-39` が -1）で決まるため、11 は +1 で受注として正しく積まれる。
- `CalcFlag` の算出式 `TranCalcBase.GetKubunCalcFlag` は変更していない。

### 未実施・残余リスク
- 実画面での動作確認は未実施（ビルドと TestServer 922件のみ）。
- `DeliveryScheduleTable` は帯化により、将来 16-19 の区分が増えた場合も自動で納品予定の対象になる。除外したい区分が出たときは個別に条件を足す必要がある。

## [2026-09-21] 仕入区分に消化仕入を追加し生地・付属仕入の区分enumを分離する

### 実施内容
マイグレーション `26_09_21_01` を追加し、既存の自動生成消化仕入伝票の区分を 10/20 から **15/25** へ一括変換した。これに伴い、以下の実装を修正した。

**enum拡張:**
- `EnumShiire`（商品仕入）に消化仕入区分を追加: `SoldOnShiire=15`（消化仕入）、`SoldOnHenpin=25`（消化仕入返品）。
- **新規** `EnumMaterialShiire`（生地・付属仕入専用）を定義: 消化仕入なし。`Tran02Material.EnKubun` の型を `EnumShiire` から `EnumMaterialShiire` へ変更。

**コード修正（すべて未コミット、ビルド成功）:**
- 消化仕入生成 `CostUpdateDbConsumption.cs:569` で生成区分を 10/20 から **15/25** へ変更。
- 仕入入力画面 `ShiireInputViewModel.cs` に区分15/25を追加、`NormalizeIsStockForKubun` メソッドで15/25選択時に `IsStock=0` へ揃える。
- 生地・付属仕入入力 `MaterialInputViewModel.cs` の `KubunOption` 型を `EnumMaterialShiire` へ変更。
- 仕入元帳・支払消込・支払残明細・仕入伝票印刷に 15/25 の表示ラベルを追加。
- 帳票フィルタを離散列挙から帯域へ変更（`ShiireTrendReport` / `BrandShiireKingakuTable` / `HinbanShiireCheckList`）: `Kubun = 10` を `Kubun BETWEEN 10 AND 19` へ。消化仕入15が仕入帯に入るため結果は従来と同じだが、離散列挙だと区分追加で静かに漏れるため。
- 消化仕入の対象売上を抽出する `CostUpdateDbConsumption.FetchConsumptionTargetKeys` の SQL は変更していない。ここで列挙している `Kubun` は売上側（`EnumUri00` / `EnumUri01`）であって仕入区分ではなく、15/25 は売上区分に存在しないため。

**DB変更:**
- マイグレーション `26_09_21_01`：`GeneratedKind=1` の既存行を `Kubun` 10→15 / 20→25 へ UPDATE。同時にユーザーが手入力した区分15/25（`GeneratedKind=0`）と区別可能な設計。
- `CalcFlag` は 15=+1 / 25=-1 で従来の 10/20 と同符号のため再計算不要。

### 影響
- 既存の自動生成消化仕入（マイグレーション対象）は区分 15/25 へ変換。イレギュラーな後付け起票分（手入力・`GeneratedKind=0`）は並存可能。
- 買掛集計は帯域判定（10-19 / 20-29）で消化仕入15/25を自動吸収。`SummaryDb.cs` は帯域判定により自動対応。再計算なし。

### 未実施・残余リスク
- `Tran02Material`（生地・付属仕入）は `EnumMaterialShiire` （消化仕入なし）への型統一完了。既存データは互換。
- 既存の消化仕入を区分15/25へ変換する `26_09_21_01` マイグレーションは確認済み。本番環境への適用は別途実行。
- コード側の離散列挙（`Kubun IN (10, 20)` 形）は `IsStock=1` を併記しているため消化仕入（`IsStock=0`）に到達しない。SQL コメント更新のみで SQL 自体は変更せず。
- 区分99の集計列 `Sonota` は改名しない（将来の区分追加時の受け皿として残す）。既存データの再集計も不要。

## [2026-09-21] 売上区分に社販を追加し区分99を消費税へ改名する

### 実施内容
`EnumUri01`（店舗売上）に社販区分 `UriShahan=14`（社販売上）と `HenShahan=24`（社販返品）を追加した。これに伴い、以下の実装を修正した。

**コード修正（すべて未コミット、ビルド成功）:**
- 店舗売上入力画面 `ShopUriageInputViewModel.cs` に社販売上・社販返品の選択肢を追加、`NormalizeHeaderKubun` で社販を 10 に潰していた処理を修正。
- HHT取込 `HhtProcessUpdateMap.cs` で店舗売上の社販14/24に対応。卸売上(`EnumUri00`)には社販なく引き続き E015 エラー。
- POS `PointOfSaleService.cs` で社販14/24を許可。社販売上14の取消伝票は社販返品24。
- 消化仕入生成 `CostUpdateDbConsumption.cs` で消化仕入対象に社販14/24を追加（14は正符号）。
- 区分99「その他」を「消費税」へ改名。参照箇所 8ファイル、テスト 5ケース を修正。
- `SummaryDb.SignExpr` はすでに 20-39 で反転するため、社販14/24は正しく集計される。
- 帳票の区分フィルタを離散列挙から帯へ変更した。`HinbanUriageCheckListViewModel.cs`（卸・店舗の2箇所）と `TokuiTrendReportViewModel.cs` の `Kubun IN (10,11)` を `Kubun BETWEEN 10 AND 19` へ、`ShopSalesDailySummaryViewModel.cs` の売上・返品判定を `BETWEEN 10 AND 19` / `BETWEEN 20 AND 29` へ変更。離散列挙のままだと帯に区分が追加されたとき静かに集計から漏れるため。
- `CostUpdateDbConsumption.FetchConsumptionTargetKeys` の SQL（`Tran01Tenuri` 側）の `Kubun IN (10, 11, 20, 21)` に 14・24 を追加し、C#側 `isTargetKubun` と同じ集合に揃えた。`Tran00Uriage` 側は社販が無いため据え置き。

**文書更新（この作業）:**
- `2026-09-16_CV10次世代帳票_Phase0帳票台帳・再評価.md` の新規候補テーブル（223行）と T02（234行）を、社販実装済みの状態に更新。
- `2026-09-05_原価4項目_詳細設計.md` の§4.3（消化仕入対象区分）に社販14/24を追加、区分99「その他」を「消費税」に統一（788, 970, 1028, 1495行）。
- `Doc/spec/tools/make_hht_testdata.ps1:52` のコメント「社販は未対応」を「卸売上では未対応」に修正。

### 影響
- 店舗売上の社販売上・社販返品は、正負判定帯10-19/20-29に収まるため、既存の `CalcFlag` と集計式は変更不要。
- 消化仕入の対象範囲が拡大（10/11/14/20/21/24）。在庫・売掛・買掛集計は既存の帯域判定（10-19は加算、20-29は減算）で社販を自動吸収。
- 卸売上(EnumUri00)は社販区分を持たず、HHT取込で社販販売区分2を受け取った場合は引き続き E015 エラー。

### 未実施・残余リスク
- `SummaryDb.cs` は社販対応による変更なし。集計は帯域判定で社販を自動吸収。
- 既存の`Sonota`列（区分99）は引き続き存在し、改名しない。消費税は `Tax1`〜`Tax3` が集計しており、`Sonota` は将来どの区分帯にも入らない区分が現れたときの受け皿として残す方針（2026-09-21 ユーザー判断）。区分99は引き続き `Sonota` へ集計される。
- 集計 `SummaryDb.cs` は今回いっさい変更していない（2026-09-21 ユーザー判断）。区分99を税側へ寄せる案は見送り、既存データの再集計も不要。
- 実運用での社販利用頻度、社員・店舗帰属のルール、予算・ランキング帳票での扱いは、業務判断待ち。

## [2026-09-19] 値引(Kubun 30-39)の消費税額符号を返品と同じくCalcFlagで反転する

### 実施内容
`SummaryDb.SignExpr`（税列 SlipTaxN/BillingRawN/TaxableAmountN 専用の符号式）が返品(`Kubun` 20-29)だけを反転し、値引(30-39)は反転しない実装になっていた。本体金額側は区分別バケット(Uriage/Henpin/Nebiki)で値引もちゃんと減算されるのに対し、税額側だけ値引ぶんが反転されず加算のままという非対称があり、当初仕様のミスと判断して修正した。C#側 `TranCalcBase.GetKubunCalcFlag`（20-39で`CalcFlag=-1`）とSQL側の反転範囲を一致させた。

- `CvDomainLogic/SummaryDb.cs` の `SignExpr` を `Kubun BETWEEN 20 AND 29` から `BETWEEN 20 AND 39` へ修正し、XML docコメントを実装に合わせて書き直した。
- `Doc/spec/archive/2026-09-01_消費税計算単位・端数処理_全体設計.md` を改訂し、「値引30-39は反転しない」としていた記述を「返品・値引とも20-39で反転する」へ修正。Total式(§3.8)の説明にも値引ぶんの税額が符号反転される旨を補足した。
- `Tests/TestServer/SummaryKakeDbTests.cs` の5件が旧挙動(値引の税額非反転)を前提にした期待値だったため、新挙動に合わせて修正した（`CalcSummaryUriKake_SeparatesSonotaAndUsesPositiveBalanceForUnrecovered` / `CalcSummaryUriSei_CalculatesPeriodBreakdownBalanceAndDueDay` / `CalcSummaryUriSei_SeparatesKubun99AsSonotaWithoutFoldingIntoUriage` / `CalcSummaryKaiKake_SeparatesSonotaAndUsesPositiveBalanceForUnpaid` / `CalcSummaryKaiShi_CalculatesPeriodBreakdownBalanceAndDueDay`）。

### 影響
旧実装で誤った値が焼き付いている集計行は現時点で存在しない。`SignExpr` の対象となる `Kubun`=30-39 の伝票は `Tran02Material` の41件のみで、うち税額が非0なのは Id=1732（`KakeDay`=2026-07-15、`Id_Shiire`=502、課税対象額1000/税100）の1件だけ。この伝票は Id=1730-1733 の区分10/20/30/99を1件ずつ揃えた買掛集計の検証用データであり、実業務データではない。さらに `SummaryKaiKake` に `Id_Shiire`=502 の行は全期間で存在しない。

仮にこの伝票が集計された場合の旧新差は `Tax1` 4700→4500、`TaxableAmount1` 27000→25000、`TotalShiire` 29700→29500（符号が+から-へ振れるため差は寄与額の2倍になる）。

### 再集計の判断
**再集計は実施しない**（2026-09-19 ユーザー判断）。誤った集計行が存在しないため修正効果が無い一方、`CalcSummaryKaiKake` は対象年月を `DELETE` してから再作成する方式のため、2026/06-07 を再集計すると既存の `SummaryKaiKake` 202607 の5行（`Id_Shiire` 1/2/5/6/7、これもシード済みテストデータ）が消えて `Id_Shiire`=502 の1行に置き換わり、テストデータの破壊だけが起きる。次回の通常再集計から新仕様で計算される。

### 未実施・残余リスク
- 実データへの再集計は上記の判断により未実施。
- `Doc/spec/archive/2026-08-18_請求計算・支払計算_詳細設計.md` にも同種の「税額は返品20-29のみ反転」という記述があったため、あわせて2026-09-19改訂として注記を追記した。旧記述は歴史的記録として残している。

## [2026-09-18] 自動実行から呼ぶ再集計の履歴を「自動実行」として記録する

### 実施内容
自動実行タスクから呼ばれた「在庫・掛再集計」等が、自動実行履歴画面で実行種別「手動実行」と表示されていた問題を修正した。`ManualLockDb.Complete` が `SysHistType` を `EmSysHistType.ManualExec` 固定で書いていたのが原因。自動実行はサーバ内で完結するため gRPC 契約（`CvFlag`）は変更せず、内部メソッドの引数として実行種別を伝播させた。

- `ManualLockDb.Complete(..., int isAutoExec = (int)EmSysHistType.ManualExec)` を追加し、`SysHistAutoexec.SysHistType` へそのまま書く。値域は `EmSysHistType` と統一（0=自動実行 / 1=手動実行）、既定 1＝手動実行。
- `StreamStepProgressRunner.Run(..., int isAutoExec = ...)` を追加して `Complete` へ委譲。
- `SummaryDb.SummaryAllAsyncStream` / `SummaryUriKakeAsyncStream` / `SummaryKaiKakeAsyncStream` に同引数を追加。
- `SchedulerService` の `ExecuteRunSummaryAsync` と `ExecuteMonthlyResummaryCoreAsync` から `AutoExecHistType`(=0) を渡す。後者は `ResummaryGroup` へメソッドグループを渡していたため、既定値が効かないようラムダへ変更した。

### 自動実行から呼ばれる処理の棚卸し
`SysHistAutoexec` を内部で書く（＝マニュアル排他制御を取る）自動実行経路は再集計3ストリームのみだった。適用上代削除・商品名称マスタ再構築・V*列再同期・伝票税額再更新・WALチェックポイント・ワークファイル削除は排他も履歴も持たず、`ExecuteWithAutoexecHistoryAsync` が書くタスク単位の1行だけなので変更不要。監視タスクは元から `AutoExec` で記録している。棚卸・HHT・原価4処理は自動実行経路が無いため既定値のまま。

### 検証
- `dotnet build creativevision10.slnx` 成功（0 警告 / 0 エラー）。
- TestServer: `SummaryKakeDbTests` 56件、`ManualLock*` 66件、`SummaryDbTests` 56件すべて成功。`SummaryUriKakeAsyncStream_AutoExec_AddsAutoExecHistory`（isAutoExec=0 で `SysHistType=0` が書かれる）を追加。

### 未実施・残余リスク
- 実サーバでの自動実行タスク実行による履歴画面の目視確認は未実施。
- 既存の履歴行（手動実行として記録済みの自動実行分）は遡及修正していない。
## [2026-09-17] 配分出荷リスト qfm の実PDF確認とレイアウト修正

### 実施内容
Step 4 で新規作成した配分出荷リスト4形式の qfm を、`.agents/skills/author-printstream-qfm` のローカルPDF描画ハーネス(qfmprint)で実PDF出力して検証し、見つかった不具合を修正した。変更は `position` の x/width のみで、74行すべてが `position` 要素（`recordtype`/`breaktype`/`grouplevel` 等の列挙値、日本語文言、cp932、改行コードは無変更）。

### 修正した不具合
- **portrait 3形式（Hin / Sku / HinTok）で右端の数量列が描画されない**。region内のローカルX座標が概ね151mm以降にある要素（右詰めの予定数量計・確定数量計、ヘッダの作成日時・ページ番号）がPDFに出力されなかった。受注数計/予定数量計/確定数量計の3列をローカルX≦123mmへ再配置し、作成日時・ページ番号も移動した。
  - **既存 portrait qfm には X≧151mm の要素が1つも無い**ことを確認しており、CV10 の corpus の慣行に揃える修正でもある。本番の CvServer 側レンダラで同じクリップが起きるかは未検証（ハーネス固有の可能性も残る）だが、いずれにせよ安全側。
- **Den で7桁の数量が桁あふれし隣接列と重なる**。受注数/予定数/確定数の3列を各10mm→13/14/14mm に拡幅した。
- **HinTok のタイトルがタイトル枠からはみ出し先頭1文字が欠ける**。枠幅を47mm→58mmに拡大した。

### 検証（実PDF・多段小計の検算）
- Hin / Sku / HinTok: 2ブランド×各2アイテム×各2〜3商品のCSVで検算。アイテム計 5/7/9・10/20/30 → ブランドX計 15/27/39、アイテム計 103/204/305・7/8/9 → ブランドY計 110/212/314 → **総合計 125/239/353**。すべて期待値と一致。グループ階層は level3(アイテム計)→level2(ブランド計)→level1(総合計) で逆転なし。
- Den: DEN001(3明細) 15/40/23、DEN002(2明細) 103/1,234,767/154 → 総合計 118/1,234,807/177。期待値と一致。
- **改ページ**: Sku で 3ブランド×3アイテム×5SKU=45明細の大規模データを流し、2ページに分割されてもページ2冒頭でグループが正しく継続し、分断されたアイテム計 15/30/45・ブランド計 45/90/135・総合計 135/270/405 が正しく計算されることを確認。ページ番号も P.0001→P.0002 と正常。
- 抽出条件の定数列は4形式ともヘッダに1回だけ出力され、明細行ごとの繰り返しは無い。
- 4ファイルとも XML 妥当・cp932・item数（12/7/8/8）は修正前後で不変。

### 未実施・残余リスク
- 本番 CvServer 経由での実PDF確認は未実施。portrait のX位置クリップが本番でも起きるかは未検証。
- Den の商品名列（幅26mm）は全角20文字超で切り詰められる。SQL側の列構成と12列レイアウト全体に影響するため今回は未修正。
- 改ページの詳細検証は Sku のみ。Hin/HinTok/Den は同一の `breaktype`/`grouplevel` 実装のため同等と判断した。

### 補足: qfm の改行コードについて（前回記載の訂正）
Step 4 の作業ログに「全 qfm が CRLF で格納されている」と記載したが、**誤りだった**。`git cat-file blob` をバイト単位で再確認したところ、既存・新規を問わず全 qfm の blob は **CR=0 の LF** であり、`.gitattributes` の `*.qfm text eol=lf` は正しく機能している。前回は Git Bash のパイプ経由で改行変換がかかった計測ミスだった。規約と実態の差異は無い。

## [2026-09-17] 配分帳票 刷新 Step 4: 配分出荷リスト（通常4形式）の新規実装

### 実施内容
- 配分4帳票の4本目（区分D=新規）。CV10に画面自体が無かったため、`HaibunShippingListReportView` / `HaibunShippingListReportViewModel` を新規作成し、`MenuData.cs`「▲ 出荷」へ「配分出荷リスト」を追加登録した。
- **出力単位を表示モードとして1画面に統合**し、qfm を形式ごとに4本作成した。
  - `HaibunShippingListDen.qfm`（伝票毎、12列）: 仮想ヘッダキー単位の明細。ソートキー(伝票キー/得意先/配分指示日/納品日)を選択可。伝票計＋総合計。
  - `HaibunShippingListHin.qfm`（商品毎、7列）: ブランド→アイテム→商品の階層。アイテム計・ブランド計・総合計。
  - `HaibunShippingListSku.qfm`（SKU毎、8列）: 上記に色・サイズを追加。
  - `HaibunShippingListHinTok.qfm`（商品得意先毎、8列）: 色サイズ列を得意先列に置換。
- 絞込条件は 配分指示日範囲／納品日範囲／倉庫／得意先／商品／ブランド／アイテム／展示会／メーカーの各範囲、区分(`EnumHaibun`)、印刷区分(残のみ=未出荷 `EndFlag=0` / 全て)、出力単位、伝票毎のときだけ有効なソートキー。

### 判断・仕様
- **マトリクス形式2種は今回スコープ外**。qfmに動的列生成の仕組みが無く、Phase 0 の H10「旧7形式の一括移植はしない」に沿って利用実績を見て判断する方針で確定済み。
- **確定数量 = `KakuteiDay` が空でなければ `JitsuSu`、未確定なら 0**。`JitsuSu` は出荷実績のため未確定時は 0 が妥当と解釈した。
- **受注数** = `RelateNo1` が指す `Tran12Jyuchu.Jmeisai`（明細JSON）から同一SKU(`Id_Shohin`/`Id_Col`/`Id_Siz`)の `Su` を合算する相関サブクエリ。`json_valid()` でガードした（AGENTS.md 7.1）。受注配分以外の区分は元伝票が無いため常に 0。
- 旧の「受注日 or 受注納品日範囲」は絞込条件として実装しなかった。受注結合を WHERE 条件に使うと受注配分以外の区分の行が全滅するため、受注数集計目的の相関サブクエリに留めた。
- 展示会/ブランド/アイテム/メーカー範囲は `MasterShohin` に `Id_Tenji`/`Id_Brand`/`Id_Item`/`Id_Maker` が揃っていたため4つとも実装した。
- 総合計は Step 1 と同じく、全行同値の抽出条件列を最上位グループ(level 1)にして表現する。`grouplevel` は 1/2/3 のみで 0 は使っていない。

### 検証
- **4形式すべての帳票SQLを `cv-sqlite` で実行し、SQLエラーが無いこと・データが取得できることを確認**。特に受注数の相関サブクエリ（`json_each` + `json_extract`）を実データで検算し、受注数6／予定数量6／確定数量6 が一致することを確認した。品番別の `VBrand`/`VItem` 展開は値が未設定でも NULL 安全に空文字となることを確認。
- SELECT列順 = qfm `itemN` が4形式とも一致（Den 12・Hin 7・Sku 8・HinTok 8）。
- qfm は cp932・XML妥当、`grouplevel`/`group level` に 0 が無いことを確認。新規 `.cs`/`.xaml` は UTF-8 BOM + CRLF。
- `dotnet build CvWpfclient/CvWpfclient.csproj` 成功（0警告0エラー）、`git diff --check` clean。

### 未実施・残余リスク
- 実PDF出力によるレイアウト目視確認は未実施（Step 1〜3 は qfmprint ハーネスで確認済みだが本形式は未実施）。
- `TranHaibun` の実データが1件のみのため、複数ブランド・アイテムにまたがる多段小計の集計結果自体は未検証（SQL構造と GROUP BY / ORDER BY の整合性のみ確認）。

### 補足: qfm の改行コードについて
`.gitattributes` は `*.qfm text eol=lf` を指定し AGENTS.md も「`printform/*.qfm` は LF」と記すが、**実際には既存の `IdoInputOut_detail.qfm` を含め全 qfm が CRLF で格納されている**ことを確認した（`git cat-file blob` をバイト単位で確認）。本 Step の qfm も既存に合わせ CRLF のままとした。規約と実態の差異として報告する。

## [2026-09-17] 配分帳票 刷新 Step 3: 納入一覧表の新規実装

### 実施内容
- 配分4帳票の3本目（区分D=新規）。空スタブだった `ShippingListReportView` / `ShippingListReportViewModel` を実装し、`MenuData.cs` の当該エントリから `準備中` を外した。
- `printform/ShippingDeliveryList.qfm`（28列）を新規作成。品番×倉庫×表示基準値ごとに改ページし、縦軸=得意先／横軸=色またはサイズのマトリクスで配分数を出力する。
- 絞込条件は 表示基準(色基準/サイズ基準)／商品コード範囲／配分指示日範囲／納品日範囲／出庫倉庫／得意先コード範囲／区分(`EnumHaibun`)／出力数量方式。

### 判断・仕様
- **ピボットはC#側で行い `PrintByCsvParam` でCSVを渡す方式**とした。印刷経路はサーバがSQLを実行してCSV化するため、SQL側で動的ピボットを書くと列数が固定できない。SQLは素直な明細JOINのみ。
- **横軸は固定10列**。qfmに動的列生成の仕組みが無いため（列数違いは専用qfmを用意するのが既存方式）。10列を超える場合は11件目以降を続きページへ折り返し、基準表示に `（続き n/m）` を付記する。
- **出力数量方式**は旧資料に2方式とだけあり内容不明のため、「予定数量(`Su`)」／「実数量(`JitsuSu`)」の切替と解釈した。
- 同一得意先×同一SKUが複数の指示日にまたがる場合は SUM して1セルに集約する（行展開するとマトリクスが崩れるため）。
- グループの代表上代は `MAX(Jodai)` を採用（行ごとに異なりうるため）。横軸の色/サイズの列順は Code 昇順とした（仕様に明記が無いための設計判断）。
- 旧仕様どおり 完了FLG・確定FLG では絞らず全件対象とする。
- 抽出条件はCSVの定数列として全行に乗せる方式（Step 1・2 と統一）。0件時は警告ダイアログで印刷中止。

### 検証
- **明細取得SQLを `cv-sqlite` で実行し、SQLエラーが無いこと・データが取得できることを確認**。`MasterShohin` / 倉庫 / 得意先 の3結合がすべて解決。
- **`.agents/skills/author-printstream-qfm` のローカルPDF描画ハーネス(qfmprint)で実PDFを出力して目視確認**。2得意先×3列、10列超1得意先の3パターンで、得意先ごとの横計（5+0=5、3+10=13、計20）、10列の合計55、品番×倉庫×基準値ごとの改ページがいずれも正しいことを確認した。
- CSV列順 = qfm `itemN` が28対28で一致。qfm は cp932・LF・XML妥当、`grouplevel` の出現値は 1 のみで 0 が無いことを確認。
- 新規 `.cs`/`.xaml` が UTF-8 BOM + CRLF であることを確認。
- `dotnet build CvWpfclient/CvWpfclient.csproj` 成功、`git diff --check` clean。

### 未実施・残余リスク
- 本番の gRPC 経由（サーバ＋DB稼働状態）での実行確認は未実施。ローカルqfmprintハーネスでの構造・束縛検証まで。
- 実データでの10列超の折り返しは人工CSVでの確認にとどまる（`TranHaibun` の実データが1件のみのため）。

## [2026-09-17] 配分帳票 刷新 Step 2: 出荷指示明細書の新規実装

### 実施内容
- 配分4帳票の2本目（区分D=新規）。空スタブだった `ShippingConfirmDetailPrintView` / `ShippingConfirmDetailPrintViewModel` を実装し、`MenuData.cs` の当該エントリから `準備中` を外した。
- `printform/ShippingInstructionDetail_header.qfm`（12列）と `ShippingInstructionDetail_detail.qfm`（19列）を新規作成。単一の印刷ボタン(F6)から header → detail の順に `PrintPdfHelper.RunPrintPdfAsync` を2回呼ぶ（同ヘルパは1呼出1帳票のため）。
- 絞込条件は 区分(`EnumHaibun`)／出庫倉庫コード範囲／出荷先コード範囲／配分指示日範囲／確定済みのみ(既定ON)。

### 判断・仕様
- **伝票キー**: `TranHaibun` は伝票NO列を持たないため、仮想ヘッダキー `HaibunHeaderKey` から `DenDay(8) + Kubun(1) + Id_Soko(6) + Id_Tenpo(6) + RelateNo1(6)` の27桁固定長・数字のみで組み立てた。CODE39 が扱える文字種に収まる。実値例 `202609052002818002819000005`。
- header 側は仮想ヘッダキーの6列で `GROUP BY` し、伝票計（数量計・上代金額計）を集計する。`Id_Shain` は `MAX()` で取り、bare column を避けた。
- **上代金額 = `Su` × `Jodai`**。`TranHaibun.Jodai` は `[Comment("上代")]` と `OldTableCommentAttr("上代金額")` が食い違うが、`ShopHaibunInputViewModel` の登録処理が `Tanka = jodai` / `Kingaku = Su * jodai` / `Jodai = jodai` と書いており、`Jodai` は単価であることを確認した（`Gedai` も `TankaGenka` で単価）。
- **入庫部門**は旧CVnetでは空欄だったが、CV10では `Id_Tenpo` のコード・名称を印字する（旧の空欄は踏襲しない）。
- 旧の「印刷区分(通常/再発行)」「伝票NO範囲」「原価FLG」「JAN2段目」はスコープ外。伝票NOが存在しない、再発行の管理データが無い等の理由による。
- 抽出条件はSQLの定数列として全行に乗せる方式（Step 1 と統一）。0件時は警告ダイアログで印刷中止。

### 検証
- **header / detail 両方の帳票SQLを `cv-sqlite` で実行し、SQLエラーが無いこと・データが取得できることを確認**。header 12列・detail 19列を取得、倉庫/出荷先/社員/商品/`DerivedShohinColSiz` の結合がすべて解決。上代金額計 12000 = 数量6 × 単価2000 で単価解釈の整合も確認した。
- SELECT列順 = qfm `itemN` が header 12対12・detail 19対19 で一致。
- qfm は cp932・LF・XML妥当、`grouplevel` の出現値は 1 のみで 0 が無いこと、CODE39 バーコード（`type="0"`）が伝票キーと出庫部門の2本入っていることを確認。
- `dotnet build CvWpfclient/CvWpfclient.csproj` 成功、`git diff --check` clean。

### 未実施・残余リスク
- 実PDF出力によるレイアウト・バーコード可読性の確認は未実施。
- `TranHaibun` の実データが1件のみのため、複数ヘッダキーにまたがる改ページと伝票計の実挙動は未確認。
- header SQL の `soko.Code` 等は `GROUP BY` 対象外の bare column（`Id_Soko`/`Id_Tenpo` に関数従属のため SQLite では決定的）。10.2 以降で PostgreSQL / MariaDB を本格対応する際は `ONLY_FULL_GROUP_BY` 相当の制約に触れるため見直しが要る。

## [2026-09-17] 配分帳票 刷新 Step 1: 滞留・欠品例外（出荷指示一覧）PDF帳票の新規実装

### 実施内容
- `Doc/spec/2026-09-16_CV10次世代帳票_Phase0帳票台帳・再評価.md` 4.5 配分の4帳票（区分D=新規）のうち1本目。画面・検索・CSV出力は実装済みで qfm 帳票だけが未実装だった（コード中に「PDF帳票は別途qfmが要るためCSVで代替」のコメントあり）。
- `ShippingConfirmListViewModel` に `DoOutputPdfCommand`（F6）・`BuildPrintSqlParam`・`BuildConditionText` を追加し、WHERE句構築を `BuildWhere()` へ切り出して画面検索と印刷でSQLを共用した。既存の `ExportCsv` は残置。
- `printform/ShippingStagnationList.qfm` を新規作成。出力項目は既存 `BuildCsv()` の12列と同一（確定日/納品予定日/経過日数/予定日超過/倉庫/出荷先/種別/商品/色サイズ/指示数/実数量/欠品）＋帳票ヘッダの抽出条件表示用の定数列1本で計13列。

### 判断・仕様
- **印刷SQLの並び順のみ画面と変えた**（画面 `KakuteiDay, Id_Soko, Id_Tenpo, Id` → 印刷 `Id_Soko, Id_Tenpo, KakuteiDay, Id`）。qfmのグループ小計はキー変化での区切りで動くため、確定日優先のままでは同じ倉庫/出荷先が日付をまたぐたびに小計が分断され「倉庫ごとの合計」にならない。`DeliveryScheduleTable` も同様にグループキーを先頭に置いている。
- **抽出条件（モード・期間・倉庫・出荷先）はSQLの定数列として全行に乗せる方式**とした。qfm には実行時に決まる値を渡す手段が無いため。配分帳票の残り3本でも同方式で統一する。
- グループ階層は level 1=総合計（定数列）/ 2=倉庫 / 3=出荷先。当初 総合計を `grouplevel="0"` で書いていたが、`Doc/spec/PrintStream_qfmフォーマット仕様.md` 3.4 の観測値は `group level` が 1/2/3/4 のみで **0 は 0 件**であり前例のない発明値だったため、定数列を最上位グループにする形へ修正した。
- 旧CVnetの 伝票NO・送信?・区分2・メモ は今回スコープ外。`TranHaibun` は伝票NO列を持たず（仮想ヘッダキー `HaibunHeaderKey` で括る設計）、現行画面の検索結果に対応する列が無いため。
- 0件時は空白PDFを出さず、既存 `ExportCsv` と同じく警告ダイアログを出して印刷を中止する。

### 検証
- **帳票SQLを `cv-sqlite` で実際に実行し、SQLエラーが無いこと・データが取得できることを確認**（13列取得、倉庫/出荷先/商品/`DerivedShohinColSiz` の4結合すべて解決）。
- SELECT列順 = CSV列順 = qfm `itemN` が13対13で一致することを確認。
- qfm は cp932・LF・XML妥当、`grouplevel` / `group level` の出現値が 1/2/3 のみで 0 が無いことを確認。
- `dotnet build CvWpfclient/CvWpfclient.csproj` 成功、`git diff --check` clean。

### 未実施・残余リスク
- 画面(F6→gRPC→サーバ)経由の実PDF出力と、2段グループ小計・総合計・条件行のレイアウト目視確認は未実施。
- `TranHaibun` の実データがUAT由来の1件のみのため、複数伝票・複数SKU・欠品ありでのグループ改行と小計の挙動は未確認。

## [2026-09-16] POS日別精算入力画面 印刷機能の追加

### 実施内容
- `PosDailySeisanInputView`/`PosDailySeisanInputViewModel` に印刷ボタン（F6）を追加した。他画面が持つ `FormFile`/`PrintBySqlParam` の仕組みに合わせ、`Tran04PosSeisan` を1行1帳票行として出力する `PosDailySeisanInput.qfm` を新規作成した（旧cvnet移植ではなく新規様式、13列: 営業日/店舗CD/店舗名/レジNo/精算回数/社員CD/社員名/客数/準備金/実金額/計算金額/差異/メモ）。
- `CurrentEdit` のフィールド初期化子では `OnCurrentEditChanged` が呼ばれず初期インスタンスが購読されない問題に対し、コンストラクタで `OnCurrentEditChangedCore` を明示呼び出しするよう修正した。

### 検証
- `dotnet build creativevision10.slnx` 0警告0エラー、`git diff --check` clean。
- qfm は `printform/*.qfm` の規約通り LF・cp932 で作成した。
- 画面(F6→gRPC→サーバ)経由の実出力、PDFレイアウトの目視確認は未実施。

## [2026-09-15] POS日別精算入力画面（Tran04PosSeisan メンテ）の新規実装

### 実施内容
- `PosDailySeisanInputView` / `PosDailySeisanInputViewModel` は「精算を保存するテーブルが無く仕様確定待ち」という理由で空スタブのまま保留されていたが、`Tran04PosSeisan`（`CvBase/BaseDb4Pos.cs`）が既に定義・`DefineDataTable.TableTypes` 登録済みであり保留理由が失効していたため、一覧表示・新規登録・修正・削除ができるメンテ画面として実装した。ViewModel に残っていた保留理由のXMLコメントは削除した。
- ViewModel は `BaseMenteViewModel<Tran04PosSeisan>` を継承。`BaseTranInputViewModel` は `TranAllHeader`＋明細前提で `Tran04PosSeisan`（明細を持たない単票）に適合しないため採用しなかった。構造は `TranShopPromotionMenteViewModel` に合わせた。
- 保存経路はユーザー判断により**汎用CRUD一本**（`Msg101_Op_Query` / `Msg201_Op_Execute` + `InsertParam`/`UpdateParam`/`DeleteParam`）とした。`Tran04PosSeisan` は `BaseDbClass` 継承かつ `TableTypes` 登録済みのため**サーバ側の追加実装は不要**。POS端末用の専用RPC `Msg073_PosSaveSeisan`（INSERT専用、金額と `SeisanCnt` をサーバ算出）は本画面からは使用していない。
- 一覧は基底既定の型付き `QueryListParam` 経路を使用し、生SQLは書いていない。`VTenpo`/`VShain`/`Jsummary` が `[SerializedColumn]`（JSON）であり、型付き経路でないと正しくデシリアライズされないため。`ListOrder` は `DenDay DESC, Id_Tenpo, RegisterNo, SeisanCnt`。
- 検索条件は営業日From/To（パラメータ化）と店舗Id。数値列の `Id_Tenpo` は基底 `BuildSelectCodeWhere` と同様にリテラル埋め込みとした（`AddSqlParameter` は値を文字列化するため、PostgreSQL 等で `bigint = text` の型不一致になる）。
- 金額はユーザー判断により**金種枚数からの自動算出**とした。`RealAmount` = Σ(`Mai*` × 額面)、`AmountDiff` = `RealAmount` - `CalcAmount` を `CurrentEdit.PropertyChanged` 購読で再計算し、画面上は両項目を読取専用にした。`CalcAmount` / `JunbiAmount` はユーザー入力値。
- `SeisanCnt` は新規登録時に同一（`DenDay`, `Id_Tenpo`, `RegisterNo`）の最大値＋1 を採番する。`CreateInsertParam` が同期メソッドのため DB 照会はせず `ListData` から算出している。
- `Jsummary`（精算時点の売上集計スナップショット）は画面の編集対象外とし、修正時も既存値をそのまま保持する。
- View は `TranShopPromotionMenteView` の構造（F2修正/F3削除/F4追加/F5一覧の `InputBindings`、`materialDesign:ColorZone` ツールバー、左右2ペイン＋`GridSplitter`、左 `DataGrid` ＋ 右 `TabControl`）を踏襲し、「基本」「金種」の2タブ構成とした。新規のスタイル・Converter は追加していない。
- `MenuData.cs` の「POS日別精算入力」エントリから、実態と矛盾していた `addInfo:"未実装 日別精算を保存するテーブルが無く仕様確定待ち"` を削除した。

### 検証
- `dotnet build CvWpfclient/CvWpfclient.csproj` 0警告0エラー。
- `git diff --check` clean。変更は `MenuData.cs` / `PosDailySeisanInputViewModel.cs` / `PosDailySeisanInputView.xaml` の3ファイルのみ。

### 追記: 実機起動で発覚した2件の修正
- ユーザーが実機で一覧取得したところ `SQLite Error 1: 'no such column: RegisterNo'` で失敗した。`Tran04PosSeisan` の物理テーブル（`CvServer/server-user163.db`）を直接確認したところ、クラス定義にある `RegisterNo` 列が存在せず、インデックス `Tran04PosSeisan_nk1` も `(DenDay,Id_Tenpo)` のままで `RegisterNo` が入っていなかった（クラス側は `KeyDml("nk1", false, [DenDay, Id_Tenpo, RegisterNo])`）。画面側の不具合ではなくスキーマ移行漏れ。同テーブルの他の列（Id/Vdc/Vdu/DenDay/Id_Tenpo/VTenpo/Id_Shain/VShain/SeisanCnt/KyakuSu/Mai10000〜Mai1/JunbiAmount/RealAmount/CalcAmount/AmountDiff/Jsummary/Memo）は定義と一致しておりズレは無かった。
- `CvBase/UpdateDb.cs` の `versions` に `26_09_15_01` を1行追加し、`ALTER TABLE Tran04PosSeisan ADD COLUMN RegisterNo TEXT NOT NULL DEFAULT '';` と、`DROP INDEX IF EXISTS Tran04PosSeisan_nk1;CREATE INDEX IF NOT EXISTS Tran04PosSeisan_nk1 ON Tran04PosSeisan(DenDay,Id_Tenpo,RegisterNo);` を実行するようにした。`ExDatabase.CreateIndex` は `IF NOT EXISTS` の追加専用で既存インデックスの列変更ができないため、DROP してから作り直す（`26_09_08_04` と同じ理由・同じ書式）。既存行の `RegisterNo` は `''` になる。
- 「基本」タブの編集フォームで「メモ」が「差異」と重なって表示された。`Grid.RowDefinitions` が10行（0〜9）しか無いのに「メモ」が `Grid.Row="10"` を使っており、WPF が最終行へクランプしていたのが原因。`RowDefinition` を1行追加して解消した。「金種」タブは10行/10項目で整合しており修正不要。
- なお「店舗Id」「担当者Id」がIdの数値入力欄になっている点は、`MenteSearchTextBox`（Id入力＋選択ダイアログ）＋ `MasterRefText`（名称表示）という他メンテ画面と共通のパターンであり不具合ではない。

### 残余リスク・未実施
- **CvServer 再起動による移行適用後の、実機での画面動作確認（一覧取得・登録・修正・削除・レイアウト見切れ）は未実施。** XAMLコンパイルとビルドが通ることのみ確認している。
- **`SeisanCnt` の採番は `ListData` に依存する。** 検索条件で対象日を除外した状態や `MaxCount` で打ち切られた状態で新規登録すると、既存レコードと同じ `SeisanCnt` が採番されうる。`KeyDml("nk1", ...)` は非ユニークのため DB エラーにはならず重複が静かに成立する。厳密な採番が必要ならサーバ側での採番（専用RPC相当）への切り替えが必要。
- **汎用CRUD経路は POS 端末側の精算確定ロジックをバイパスする。** 本画面からの登録・修正・削除は、サーバ側の金額算出・`SeisanCnt` 採番・`V*` スナップショット設定を経由しない。事務所からの照会・補正用という位置づけを前提としている。
- 新規レコードの `DenDay` はエンティティ既定値 `19010101` のまま `DatePicker` に表示される。登録時の検証で8桁妥当性は見るが、既定日付の扱いは要確認。

## [2026-09-14] 店舗売上入力画面 印刷帳票の旧cvnetフォーマット差し替え
### 実施内容
- `printform/ShopUriageInput_header.qfm` / `_detail.qfm` を旧cvnetの `cvnet01prn_header.qfm` / `cvnet01prn_detail.qfm`（cp932・LF、`.gitattributes` の `*.qfm text eol=lf` に合わせ改行はLFへ変換）で差し替えた。
- 現行 `SubDIgInp01.crs` の非コメントSQL（`OnQueryPrint`/`OnQueryDetailPrint`）と `d_sql.txt`（一覧53列/明細80列）を突き合わせた結果、供給された旧qfmの `itemN` 束縛にズレは無く（一覧qfmはitem1〜45、明細qfmはitem1〜72までを実際に使用）、位置ズレ修正は不要だった。
- `HEAD*` 見出しは、実際のspool `data.txt` に `H` レコードが1行も無い（旧cvnetの実運用でも見出しは供給されていなかった）ことを確認したうえで、`HEADn` ラベルの座標と同位置にある `itemN` 値の座標を機械的に突き合わせて対応列名を実測し、`calctype="static"` の固定文字列へ変換した（一覧: 伝票No/計上日/伝票区分/店種区分/取引先/掛率1/拡張項目01〜03/数量合計/金額合計/上代合計/下代合計/関連伝票No・No2/入力社員/メモ/CUST01/CUST02/消費税、明細は上記相当に加え明細行部の下代端数区分/年代/下代計算FLG/下代桁切指定/明細区分/商品CD/数量/金額/消費税/商品名/色CD/単価/内税消費税/HHT_SEQ_NO/関連商品CD）。帳票タイトルは `item4` 束縛（他画面と同じ方式）。
- `BuildListPrintSql`(45列)/`BuildDetailPrintSql`(72列)を `d_sql.txt` の列順に合わせて全面書き換えし、全列へ `itemN` 別名を付けた。V*列は `json_extract` でコード・名称を別列化。CV10に対応列が無い旧項目（外税/内税消費税の内訳、掛率2、伝票処理区分、MOD_SEQ、関連伝票NO2、CUST01/02、拡張項目01〜03、消費税率、担当者CD、手入力伝票NO、SYSFLG、送信FLG、セール掛率、消費税CD、消費税計算方法、MEMO2、店種区分、下代桁切指定・端数区分・計算FLG、最終締日、年代、取引詳細名、購入者名、明細の商品シリアル・関連伝票NO/行NO・原価FLG・関連商品CD・HHT_SEQ_NO等）は空欄で出す。ユーザー確認済みの方針: 内税/外税消費税は税合計(`Tax1+Tax2+Tax3`、明細は`Tax`)を外税消費税列へ、掛計上日はDenDay流用、消費税端数はTaxRoundingをそのまま出力。
- 不要になった旧ヘルパー（`KubunLabel`定数、`CodeNameViewSql`、`DetailCodeNameSql`）を削除した。

### 検証
- cv-sqlite で新SQL(45列/72列)を実データに対して実行し、列数・列名(item1〜N)の一意性・値を確認した。
- `tools/qfmprint` で差し替え後の両qfmをプローブ描画し、`IsSuccess=True` でPDF生成されることを確認した（構造的な妥当性確認）。
- `git diff --check` 済み。`dotnet build creativevision10.slnx` 0警告0エラー。
- **残余リスク**: 本セッションの実行環境に `pdftoppm`/poppler-data が無く、旧 `data.pdf`・自前プローブPDFとも日本語CID文字がテキスト層抽出・画像化できず、レイアウトの目視突合ができなかった（過去の同種作業ログでは目視確認できていたため、環境差と思われる）。見出し対応表はHEAD/item座標の機械突合とSQL列名（`d_sql.txt`が正典）のクロスチェックで代替した。画面(F6→gRPC→サーバ)経由の実出力、および目視でのレイアウト最終確認は未実施。

### 追記: 明細印刷の実画面確認で発覚した誤りの修正
- ユーザーが実際に画面(F6→gRPC→サーバ)から明細印刷を行ったところ、明細行部（商品CD/商品名/色/サイズ/数量/単価等）が完全に破綻して表示された（例: 商品CDがカンマ区切りの金額として描画される等）。原因は、上記の「残余リスク」で述べた通り目視突合ができなかったため、`d_sql.txt`（正典のはずのCRS列順）をそのまま `item45`〜`item72` の割当根拠にしたが、この特定の明細行部(`Rec03`/`Rec04`)は実際には別の並びに差し替わっており、CRS列順との対応が崩れていた（ヘッダ一覧部の `item1`〜`item45` は結果的にCRS列順のままで問題なかった）。
- 本セッション中に winget で poppler（`pdftoppm`/`pdftotext` + poppler-data の Adobe-Japan1 CMap）を導入し、旧 `data.pdf` のテキスト層抽出が可能になった。これにより `refer/printwrk/ShopUriageInputView/wk_spool_headerdetail/data.pdf` の実レイアウトと `data.txt` の実データ（SEQ_NO=9178140）を列名単位で突合し、明細行部の真の `itemN` 対応（`item45`=サイズCD, `item46`=商品CD, `item47`=色CD, `item49`=商品名, `item50`=数量, `item51`=単価, `item52`=金額, `item54`=消費税, `item55`=上代単価, `item56`=上代金額, `item57`=下代単価, `item58`=下代金額, `item59`=摘要, `item68`=色名, `item69`=サイズ名, `item71`=行No）を実測し直した。
- [ShopUriageInput_detail.qfm](printform/ShopUriageInput_detail.qfm) の `Rec04`（明細行見出し）14箇所の static 文字列と、[ShopUriageInputViewModel.cs](CvWpfclient/ViewModels/06Uriage/ShopUriageInputViewModel.cs) の `BuildDetailPrintSql` の `item45`〜`item72` を実測結果に合わせて修正した。
- `tools/qfmprint` のプローブ描画（今回から `pdftotext -layout` で読み取り可能）で見出し配置が参照PDFと一致することを確認、cv-sqlite で実データ(`OldSeqNo=9178277`)を新SQLに通し値が正しく載ることを確認した。`dotnet build creativevision10.slnx` 0警告0エラー。
- **未対応で残る既知の問題**: ヘッダ一覧部(`Rec01`/`Rec02`)の見出し文言は今回未着手で、`CUST01`/`CUST02`のように旧列名をそのまま見出しにしている箇所や、`item27`(幅3単位)と`item44`が隣接して詰まって見える箇所がある。値は空欄のため実害は無いが、見出し文言の整備は別途対応が必要。

## [2026-09-14] 出荷・売上入力画面 印刷帳票の新設（旧cvnetフォーマット準拠）
### 実施内容
- 他の伝票入力画面と異なり `ShukkaUriageInputView` には印刷ボタン自体が無かったため、View（一覧タブのツールバーに「一覧印刷」「明細印刷」ボタン）・ViewModel（`FormFilePrefix`、`DoPrintList`/`DoPrintDetail`、`BuildListPrintSql`/`BuildDetailPrintSql`）を新設した。既存の `ShiireInputView` と同じ構成（一覧タブ選択時のみ活性）に合わせた。
- `printform/ShukkaUriageInput_header.qfm` / `_detail.qfm` を旧cvnetの `cvnet00prn_header.qfm` / `cvnet00prn_detail.qfm`（cp932・LF、`.gitattributes` の `*.qfm text eol=lf` に合わせ改行はLFへ変換）で差し替え、`HEAD*` 見出しを `calctype="static"` の固定文字列へ変換した（伝票No/売上日/伝票区分/取引区分/掛率/SYSFLG/送信FLG/数量計/金額計/上代合計/下代合計/店舗/倉庫/入力者/消費税計/手入力No/関連No1/関連No2/メモ、明細は行No/商品CD/商品名/色/サイズ/数量/単価/上代単価/下代単価/関連伝票No/消費税/金額/上代金額/下代金額を追加）。帳票タイトルは `item4` 束縛（`ShiireInput` と同じ方式）。
- 供給された旧qfmは実運用版より古く、`d_sql.txt`（一覧44列/明細70列、現行 `SubDIgInp00.crs` の非コメントSQLと一致＝正典）に対して `itemN` のズレが多数あった。`tools/qfmprint` を数値プレースホルダ（`itemN`→"N"文字列）でプローブ描画し、スロット位置と `datasrc` を全数実測した上で `<text id="TxtNN">` スコープで修正した（一覧: item39→42・item37→40・item38→41・item26→29・item27→30・item28→31／明細: 上記6件相当に加え明細行テーブル16件、詳細は差分参照）。
- `BuildListPrintSql`(44列)/`BuildDetailPrintSql`(70列)を `d_sql.txt` の列順に合わせて新規実装した。V*列は `json_extract` でコード・名称を別列化、取引区分名は `KubunLabelSql`（画面の `KubunOptions` 表記に合わせた `"CD 名称"`）で算出。CV10に対応列が無い旧項目（外税/内税消費税の内訳、掛率2、MOD_SEQ、消費税率、納品先CD/名、担当者CD/名、セール掛率、消費税CD、下代桁切指定・端数区分・計算FLG、最終締日、SYSFLG、送信FLG、明細の商品シリアル・関連伝票NO・原価FLG・関連商品CD・HHT_SEQ_NO等）は空欄で出す。名称列は1文字値がPrintStreamで描画されない問題に対し `Pad1` で回避した。
- 伝票区分（旧`伝票処理区分`）は旧 `.crs` の既定値(`Form1.v_denkbn=0`)を定数 `DenpyoShoriKubun=0` として固定出力した。

### 検証
- `tools/qfmprint` で旧qfmのプローブ描画（数値プレースホルダ版）により全 `HEADn`/`itemN` とスロット位置の対応を実測し、`d_sql.txt` の列名・実データ`data.pdf`（一覧・明細とも）と突合して列順のズレを洗い出した。
- 修正後のqfmを、静的キャプション適用後の新item番号に対応した実データ形状のプローブで再描画し、`data.pdf` の実レコード（伝票No 9176129等）とレイアウト・見出しが一致することを確認した。
- cv-sqlite で新SQL(44列/70列)を実データ(`Tran00Uriage.Id=50313`、明細2行)に対して実行し、列数44/70・列名の一意性・値を確認した。結果を実データ形状のCSVへ変換のうえ `tools/qfmprint` で最終qfmを描画し、`wk_spool_headerdetail/data.pdf` の対応レコード（旧SEQ_NO=9178289相当、CV10側Id=50313）と値が一致することを確認した。
- `git diff --check` 済み。`dotnet build CvWpfclient/CvWpfclient.csproj` 0警告0エラー。
- 画面(F6→gRPC→サーバ)経由の実出力は未実施（残余リスクとして残す）。明細の「関連伝票No」「摘要」相当（CV10 `Tran99Meisai` に対応フィールドが無い）は空欄仕様とした。

## [2026-09-14] 生地・付属仕入入力画面 印刷帳票の旧cvnetフォーマット差し替え
### 実施内容
- `printform/MaterialInput_header.qfm` / `MaterialInput_detail.qfm` を旧cvnetの `cvnet02prn_header.qfm` / `cvnet02prn_detail.qfm`（cp932・LF、`.gitattributes` の `*.qfm text eol=lf` に合わせ改行はLFへ変換）で差し替え、`HEAD*` 見出しを `calctype="static"` の固定文字列へ変換した（伝票No/仕入日/取引区分/送信FLG/仕入先/倉庫/手入力No/関連No1/掛計上日/掛率/最終締日/SYSFLG/数量計/金額計/消費税計/入力者/関連No2/メモ）。
- 明細qfmの `Rec03`/`Rec04`（明細行テーブル）は旧qfmでは来勘・サイズCD・消費税計算方法・下代金額等（`d_sql.txt` の実列と対応しない古いバージョンの束縛）だったため流用せず、目標spool `wk_spool_headerdetail/data.pdf` の実際の列構成（行No/商品CD/商品名/関連商品CD/摘要/単価/数量/金額/消費税）で新規に組んだ（ユーザー確認済み）。関連商品CDは `Tran99MaterialMeisai.Code_Shohin`（諸掛費用負担商品）を充当した（ユーザー確認済み）。
- 明細qfmの共有ヘッダ行に残っていた `datasrc="item61"` は他ファイルと不整合（同じ位置が一覧qfmでは `item37`=送信FLG）だったため `item37` へ修正した。
- ヘッダ部の「伝票区分」スロット(旧`item22`=伝票処理区分、CV10に対応なく常に空欄)を、算出済みで未使用だった `item38`(取引区分名 "10 仕入" 相当)へ差し替え、見出しも「取引区分」に変更した。
- `MaterialInputViewModel.BuildListPrintSql`(38列)/`BuildDetailPrintSql`(47列)を旧SQL(`SubDIgInp02.crs` の `col_list`)の列順に合わせて全面書き換え、全列へ `itemN` 別名を付けた。V*列は `json_extract` でコード・名称を別列化。
- CV10に対応列が無い旧項目（倉庫CD/名、掛率1、内税消費税、上代・下代合計、伝票処理区分、MOD_SEQ、関連伝票NO/NO2、来勘FLG、消費税率、消費税CD、最終締日、SYSFLG、送信FLG、明細の色CD/サイズCD等）は空欄で出す。消費税計は `Tax1+Tax2+Tax3`、消費税計算方法/端数は `TaxCalcUnit`/`TaxRounding` の数値をそのまま出力（ラベル化は未実施）。

### 検証
- `tools/qfmprint` で旧qfmのプローブ描画により `HEADn`/`itemN` とスロット位置の対応を実測し、`d_sql.txt`・実データ`data.txt`の1行と突合して列順の一致を確認した。
- cv-sqlite で新SQL(38列/47列)を実データ(`Tran02Material.Id=1729`)に対して実行し、結果を実データ形状のCSVへ変換のうえ `tools/qfmprint` で最終qfmを描画、明細行(行No/商品CD/商品名/関連商品CD/単価/数量/金額/消費税)が期待どおり表示されることを確認した。
- `git diff --check` 済み。`dotnet build creativevision10.slnx` 0警告0エラー。
- 画面(F6→gRPC→サーバ)経由の実出力、担当名(VShain)が非空のケースでの仕入先セルとの隣接表示崩れ、消費税計算方法/端数の文字ラベル化は未実施・未確認（残余リスクとして残す）。

## [2026-09-14] 仕入入力画面 印刷帳票の旧cvnetフォーマット差し替え
### 実施内容
- `ShiireInput_header.qfm` / `ShiireInput_detail.qfm` を参考QFM（cp932・CRLF）で差し替え、`HEAD*` 見出しを固定文字列化、帳票タイトルを `item4` 束縛に変更した。
- 旧 `d_sql.txt` の列順に合わせ、一覧SQLを42列、明細SQLを71列とし、全列へ `itemN` 別名を付与した。`V*` はコード・名称を別列化し、明細は `json_each(Jmeisai)` で展開した。
- CV10にない関連No2、来勘FLG、在庫計上FLG、SYSFLG、送信FLG、明細原価FLG、経費金額等は空欄とした。メーカー品番・仕入区分は `MasterShohin` から取得する。

### 検証
- 参考spoolの `data.txt` で一覧・明細PDFをローカル描画し、タイトル、日付、コード・名称、数量、金額、メーカー品番、仕入区分の配置を旧PDFと突合した。
- QFMはXML整形式、cp932、CRLF、`HEAD*` 参照0件、`itemN` 定義数（42/71）を確認した。

### 追記: 明細帳票の伝票グループ化不具合の修正
- 明細PDFで伝票ヘッダが1回しか出力されず（しかも最終伝票の値）、全伝票の明細が連続してしまう不具合を修正した。原因は `Rec02`（伝票ヘッダ行）が `recordtype="1"` のままで、キーブレイク時に出力するための `grouplevel` / `breaktype` を持っていなかったこと。`JuchuInput_detail.qfm` / `HenpinInput_detail.qfm` / `StockInputView_detail.qfm` と同じ前例に合わせ `grouplevel="1" breaktype="2"`（`recordtype` なし）へ変更した。`<group level="1" pagechange="0"/>` は据え置きで、伝票ごとに改ページせず1ページに連続出力する。
- 掛率(item12)の `decode format` が空になっていたため、旧qfmと同じ `@"%"` を復元した。
- 消費税率(item28)は CV10 に伝票単位の税率が無く値が常に空で `%` だけが描画されていたため、見出し `Txt128` と値 `Txt129` のセルを削除した（CV10にない項目は出さない方針）。
- PrintStream は全角1文字だけの値を描画しない（色名「黒」「赤」「白」が消える）ことを実測で確認した。明細SQLに `Pad1` を追加し、担当名・仕入先名・倉庫名・メモ・商品名・明細メモ・色名・サイズ名が1文字のときだけ半角空白を付けて回避する。
- 作業ツリーで LF になっていた `ShiireInput_header.qfm` / `ShiireInput_detail.qfm` を CRLF へ戻した（`.gitattributes` は `*.qfm text eol=crlf`）。
- 検証は `tools/qfmprint` で、旧spoolの `data.txt`（2伝票×4明細）と CV10 実データ形状のCSV（4伝票・負数量・1文字色名を含む）を描画して実施。`CvWpfclient` のビルドは 0 警告 0 エラー。画面(F6→gRPC→サーバ)経由の実出力は未実施。
## [2026-09-14] 受注入力画面 印刷帳票の旧cvnetフォーマット差し替え
### 実施内容
- `printform/JuchuInput_header.qfm` / `JuchuInput_detail.qfm` を旧cvnetの `cvnet12prn_header.qfm` / `cvnet12prn_detail.qfm` （cp932・CRLF）で差し替えた。発注（cvnet13prn）と違い、この2本は見出しが最初から `calctype="static"` で `HEAD*` 束縛も `<prefix>` も `<script>` も持たないため、見出しの static 化は不要だった。
- プローブ描画（`.agents/skills/upgrade-cvnet-print-form/scripts/make_probe_data.pl` + `tools/qfmprint`）で全スロットを実測し、`datasrc="itemN"` が `wk_spool_*/d_sql.txt` の列番号（header 45列 / headerdetail 76列）と完全に一致していることを確認した。発注で見つかった datasrc のズレはこの帳票には無く、qfm の `datasrc` 修正は 0 件。
- qfm の調整は1箇所のみ。明細の「明細納品日」(`Txt93` / item64) は CV10 の明細JSONに納品日が無く常に空欄になるが、旧qfmの日付編集 `format="S0.4/S4.2/S6.2"` があると空値でも `//` が描画されてしまうため、この `<decode format>` を空にした。伝票の「納品日」(item7) は CV10 に `NouhinDay` があるので旧仕様の日付編集のまま残している（値が無い伝票では `//` が出る）。
- `JuchuInputViewModel` の `BuildListPrintSql`(45列) / `BuildDetailPrintSql`(76列) を旧cvnet `SubDIgInp12.crs` のSELECT列順へ書き換えた。`PrintPdfService` が結果列名を `Dictionary` のキーにするため、全SELECT列へ列順どおりの `as itemN` を付けている。
- 旧の画面表示用ヘルパー（`KubunLabel` / `CodeNameViewSql` / `DetailCodeNameSql` / `MeisaiKubunLabelSql`）は旧帳票の「コードと名称を別列」という形と合わないため印刷SQLから外し、発注と同じ `VCd` / `VMei` / `KubunNameSql` / `KubunLabelSql` に置き換えた。

### 旧項目のCV10対応
- 伝票処理区分(item22)は旧cvnetの受注固定値 `12`。取引区分名(item42/item69)は `「区分コード 区分名」` 形式。
- 消費税(item17)は `Tax1+Tax2+Tax3`、明細の消費税(item51)は明細JSONの `Tax`。上代金額/下代金額(item53/item55)は `数量 × 上代(下代)単価`。
- CV10に無い項目は空欄で出す。作成日時・更新日時・外税対象金額・内税消費税・掛率2・MOD_SEQ・関連伝票NO2・展示会CD/展示会・手入力伝票NO・納品先CD/納品先名・担当者CD/担当者名・SYSFLG・送信FLG・セール掛率・消費税CD・消費税計算方法・下代桁切指定/端数区分/計算FLG、明細側の内税消費税・商品シリアル・関連伝票NO/行NO・原価FLG・明細承認FLG・上代・スワッチ頁/位置。
- 関連伝票NO2 と 手入力伝票NO は `Tran12Jyuchu.Jdetail` の `Yobi1` / `Yobi2` に旧値が移行されている形跡があるが、`Yobi1/Yobi2` は汎用予備項目で受注入力画面にも出ていないため、意味が保証されないと判断して空欄にした（ユーザー確認済み）。
- 明細納品日は CV10 に明細単位の値が無いため空欄。明細の完了FLG(item63)と完了FLG名(item71)は伝票単位の `EndFlag` を明細行へ繰り返す（発注帳票と同じ扱い・ユーザー確認済み）。

### 検証
- cv-sqlite で明細SQL（76列）を旧伝票 `OldSeqNo=9178275` に対して実行し、列数・列名の一意性と、商品CD・色/サイズ・数量・単価・金額・取引区分名・完了FLG名が旧 `data.pdf` と一致することを確認した。
- 旧 `wk_spool_header` / `wk_spool_headerdetail` の `data.pdf` と、CV10のSQLが出す形（負値・空欄・複数明細）で描画したPDFを突合し、レイアウト一致を確認した。
- `CvWpfclient` は `-t:Compile` でエラー0。アプリ起動中のため出力コピーを伴う通常ビルドは未実施。画面（F6→gRPC→サーバ）からの実出力は未確認。

## [2026-09-14] 発注入力画面 印刷帳票の旧cvnetフォーマット差し替え
### 実施内容
- `printform/HachuInput_header.qfm` / `HachuInput_detail.qfm` を旧cvnetの `cvnet13prn_header.qfm` / `cvnet13prn_detail.qfm` （cp932・CRLF）で差し替えたうえで、CV10向けに次の調整を入れた。
  - 列見出しは旧cvnetが `data.txt` 先頭の `H` レコード（`HEAD1..91`）で流し込んでいたが、CV10 の `PrintPdfService` → `WriteDynamicCsv` はヘッダ行を出力しないため見出しが全て空欄になる。`HEAD*` 束縛の見出し（header 22件 / detail 39件）を `calctype="static"` の固定文字列へ変換した。帳票タイトルは CSV 4列目（`item4`）束縛に変更した。
  - 供給された旧qfmは旧CRSの `#29919 関連伝票NO2 取得位置移動` にSQL列が追随しておらず、`datasrc` が1〜2列ずれていた。旧 `data.pdf` とローカル実描画の突合で特定し、header 4箇所（仕入先名・入庫先名・入力者名・関連No2）、detail 11箇所（取引区分名・仕入先名・入庫先名・入力者名・関連No2・行No・色名・サイズ名・完了FLG名・メーカー品番・仕入区分）を修正した。
- `HachuInputViewModel` の `BuildListPrintSql`（39列）/ `BuildDetailPrintSql`（68列）を旧cvnetのSELECT列順へ全面的に書き換えた。`PrintPdfService` が結果列名を `Dictionary` キーにするため、全SELECT列へ列順どおりの `as itemN` を付けている。
- 旧cvnetにありCV10に無い項目（手入力伝票NO・関連No2・SYSFLG・送信FLG・連携・掛計上FLG・MOD_SEQ・消費税CD・消費税計算方法・内税消費税・原価FLG・商品シリアル）は空欄で出力する。掛計上日は qfm 側スクリプトが `19010101` のとき列を非表示にするため、同値を固定出力している。
### 仕様上の対応付け
- 入庫先（旧 取引先CD2／得意先名）は CV10 の倉庫 `VSoko` のコード・名称を割り当てた。旧データでも取引先CD2と倉庫CDは同値だった。
- 消費税計は `Tax1+Tax2+Tax3`。CV10 は税率別に分割済みのため合算する。
- 明細の完了FLG・完了名は CV10 が伝票単位でしか持たないため `Tran13Hachu.EndFlag` を明細行へ繰り返す。
- メーカー品番・仕入区分は `MasterShohin` を明細の `Id_Shohin` で left join して取得する。仕入区分は CV10 の `PurchaseType`（0=通常仕入 / 3=消化仕入）で、旧の 1買取/2委託/3消化 とはコード体系が異なる。
### 確認
- `dotnet build CvWpfclient/CvWpfclient.csproj` 成功（0 警告 / 0 エラー）。
- `.agents/skills/author-printstream-qfm/tools/qfmprint` で両qfmを実PDF描画し、見出し・列対応・負値・日付書式・改ページを旧 `data.pdf` と突合した。
- cv-sqlite MCP で両帳票SQLを実データへ実行し、列数（39 / 68）・列名の一意性・値を確認した。
### 残余リスク・未実施
- 画面（F6→gRPC→サーバ）からの実PDF出力は未実施。サーバ経由のCSV生成と本番フォント環境での描画は未確認。
- qfm の末尾タブによる `git diff --check` の trailing whitespace 警告は、差し替え元の旧qfmに元から存在するものでそのまま残している。

---

## [2026-09-12] DB初期化・標準データ・サンプルデータの分離（Step 3）
### 実施内容
- DDL前に実表一覧を取得し、実表0件のときだけ新規DBフラグを保持するようにした。`Sys*`表を含み、SQLite内部表・ビューは除外する。SQLite/MariaDB/PostgreSQLで実装し、名前指定時の`GetTableCounts`が`Sys*`表を除外する不整合も修正した。
- `DefineDataTable`の初期データを管理者・ログイン・会社設定と、新規DB限定のサンプルデータへ分離した。標準データを先に投入し、サンプル名称は`MasterMeisho.CreateDefaultData`から除外した。
- 新規DBは過去Migrationを実行しないため、現行標準名称へKIJ、C30〜C32、SCA、SCGを追加した。既存DBではMigration側の投入責務を維持する。
### 確認
- `dotnet test --project Tests/TestServer/TestServer.csproj` 成功（911件、失敗0、スキップ0）。実表一覧、新規DBの標準・サンプル投入、再起動時の不変性、既存空表でのサンプル非投入を確認した。

---

## [2026-09-12] 担当者Role・Scope・業務権限の基盤実装（10.0）
### 実施内容
- [担当者Role・Scope・権限判定 詳細設計](spec/2026-09-12_担当者Role・Scope・権限判定_詳細設計.md)の10.0スコープ（テーブル定義・基盤のみ。判定処理は10.2以降）を実装した。チェックリストの未決事項`D-10`への回答にあたる。
- 前身設計（2026-08-28、commit `e5f59e6`）が入れた`MasterShain.ResponsibilityScope`・`SysPermissionProfile`系は判定処理が一度も実装されず業務ロジックからの参照がゼロだったため、加算ではなく全面改定とした。
- 本設計の権限は**業務権限**（どの業務メニューを開けるか、開いた画面が参照のみか編集可か）であり、`SysLogin`のログイン可否という**システム権限**とは別物として分離した。判定はクライアント側で完結し、サーバ側に強制ポイントを置かない。
- `CvBase`: `EnumResponsibilityScope`/`EnumResponsibilityExternalScope`を削除、`EnumPermissionType`を`View`/`Edit`の2値へ縮小、`EnumResponsibilityRoleCategory`/`EnumScopeKubun`/`EnumPermissionDefaultMode`を追加。`MasterResponsibilityRole`（P01〜P22の22件）/`MasterShainResponsibility`（兼務）/`MasterShainResponsibilityScope`（担当範囲）の3テーブルを新設。`MasterShain.ResponsibilityScope`と`SysPermissionProfile.ResponsibilityScope`/`IsDefault`を削除。`MasterSysman.PermissionDefaultMode`（未登録FunctionIdの既定ポリシー）と`MasterMeisho`の`SCA`/`SCG`定数を追加。
- `CvWpfclient`: `MenuData`から`AllowedRoles`/`IsVisibleFor`/`FilterByRole`を削除し、`FunctionId`/`EditScopeKubuns`プロパティと`DeriveFunctionId`/`IsScopeTargetView`の静的メソッドを追加した。FunctionIdは`ViewType`の名前空間・型名から機械導出し、台帳を二重定義しない。Scope判定の対象は`*MenteView`19画面と`*InputView`20画面のみ。
- **10.0では判定処理を一切呼び出さない**ため、全メニュー・全機能が従来どおり使用可能。`AllowedRoles`削除後も代替フィルタを入れていない。
- マイグレーション`26_09_12_01`〜`05`を追加。`26_09_12_03`は、新規DBでは`WriteVersionInfoAsync`がSQLを実行しない一方で`CreateDefaultData`が先に走るという順序を利用し、既存DBにだけ新プロファイル・明細を投入する構成とした。
- 実DB検証で`ALTER TABLE SysPermissionProfile DROP COLUMN ResponsibilityScope`が旧インデックス`SysPermissionProfile_nk2`の残存により失敗することが判明したため、`DROP INDEX IF EXISTS SysPermissionProfile_nk2;`を同マイグレーションの先頭に追加した（`26_09_08_04`の前例に倣う）。`MasterShain.ResponsibilityScope`と`IsDefault`には同種の索引が無いことをgit履歴で確認済み。
### 確認
- `C:\gitroot\UT\vscmd.bat dotnet build creativevision10.slnx`成功（0エラー0警告）。
- `DdlSnapshotTests`7件・`UpdateDbTests`15件・`MasterCascadeDbTests`23件すべてPASS。`UpdateDbTests`の権限明細件数アサートを11→17へ更新した。
- 新規SQLiteで初期化し、3テーブル作成・Role22件・プロファイル4件・明細17件ちょうど（マイグレーションのINSERTと二重にならないこと）・旧3列の消滅・`PermissionDefaultMode=0`・`SysUpdateDb`最新`26_09_12_05`を実測確認した。
- 実DB（`server-user163.db`、1824社員）のコピーで`01`〜`05`の適用を確認。`ResponsibilityScope`列・`SysPermissionProfile_nk2`索引・`IsDefault`列の消滅、新4プロファイル・17明細の投入、`MasterShain`1824件/`Tran00Uriage`50315件/`Tran13Hachu`6694件が無変化であることを確認した。旧`ResponsibilityScope`値1/2/3/90を持つ社員が実データに存在しなかったため、人工データで1→P17・2→P16・3→P15・90→P02の移行と`Id_Tenpo`/`Id_Bumon`からのScope行生成を別途検証した。
- **既存の不具合を検出（本件では未修正）**: `DefineDataTable.InitializeAsync`は`InitializeDatabase(db)`が`MasterMeisho`へ5行を無条件投入した後に`MasterMeisho.CreateDefaultData(db)`を呼ぶため、件数0ガードにより名称マスタのIDXカタログ（`BRD`/`ITM`/`COL`/`SIZ`/`SLE`/`CHR`/`KIJ`/`C30`〜`C32`、および今回の`SCA`/`SCG`）が新規DBでは作られない。DB初期化・マイグレーションを扱う別タスクの範囲のため本件では触れていない。

---

## [2026-09-11] 伝票入力画面への「新規登録」ボタン追加
### 実施内容
- 受注・売上など伝票入力12画面には「新規」に相当するボタンが無く、一覧が0件のときだけ「〜詳細」ボタンが新規伝票を開く仕様だった。一覧に既存伝票があると新規入力できないため、「〜詳細」の右隣に「新規登録」ボタンを追加した。
- ViewModel側は各画面の`GoToDetail`内にインライン記述されていた新規伝票の初期化を`CreateNewDenpyo()`へ切り出し（`BaseIdoInputViewModel`の既存メソッド名に統一）、`[RelayCommand] GoToNew()`で`Current = CreateNewDenpyo(); SelectedTabIndex = 1;`とした。`GoToDetail`の挙動は変えていない。
- 対象View12本: HachuInput / JuchuInput / MaterialInput / ShiharaiInput / ShiireInput / NyukinInput / ShopUriageInput / ShukkaUriageInput / IdoInputOut / IdoInputSoku / IdoInputUke / StockInput。ViewModel9本（基底2本 + 個別7本）。
- 出荷売上入力（`ShukkaUriageInputViewModel`）は元々`GoToDetail`に新規初期化が無く、白紙の新規作成を想定していない画面だった。他画面に揃えて`CreateNewDenpyo()`を新設した。運用上この画面で白紙入力を許すかは要確認。
### 確認
- `dotnet build CvWpfclient/CvWpfclient.csproj`成功（0エラー0警告）。
- UatVmに`denpyonew`シナリオを追加。受注・店舗売上・棚卸・入金の4画面で、一覧に既存伝票がある状態から`GoToNewCommand`→詳細タブ遷移・`Current.Id==0`・伝票日付が当日・明細が空・`GoToListCommand`で一覧へ復帰、を24判定すべてPASS（終了コード0）。DBへの書き込みは行っていない。

---

## [2026-09-11] メインメニュー右クリックメニューの追加
### 実施内容
- メインメニュー画面のクライアント領域右クリックに、VersionUp/環境設定/RefreshToken/ログイン/テーマ切替/メニューのみ/小Window/終了の8項目のコンテキストメニューを追加した。既存のRelayCommandへバインドするだけで、ViewModelは変更していない。
- 上部120px（WindowChrome CaptionHeight）はOS標準のシステムメニューのまま残す仕様とした。下部の既存ボタン群とF9〜F12のInputBindingsも維持した。
- ContextMenuは独立Popupのため、半透明カード用の`MainMenuDashboardCardBackgroundBrush`では背後が透ける。専用の不透明キー（`MainMenuContextMenu{Background,Border,HoverBackground,Foreground}Brush`）を新設し、緑/橙/紫/赤の各メインテーマとDark切替に追従させた。色は各テーマの既存GradientStopから採取した。
### 確認
- `dotnet build CvWpfclient/CvWpfclient.csproj`成功（0エラー0警告）。
- 実行して右クリック表示と不透過の配色を確認した。

---
