## [2026-10-06] 顧客：ポイントマスタ3画面のデザイン統一と一覧条件選択Win

- ベース/ランク/ボーナス画面を `TranShopPromotionMenteView` 様式へ統一（アイコン付きツールボタン、件数・メッセージ帯、52*:48*、TabControl内2列フォーム・セクション見出し・登録/修正日、xstyler整形）。
- 一覧取得前に共通条件選択Win `Views/Sub/PointMasterSearchParamView` を表示。コード前方一致・親ベース（ランクは移行設定待ち含む）・適用日・有効・件数を画面別に表示しバインド変数でWhere生成。仕様書へ条件表を追記。
- `PointMasterScenario` を条件選択Win自動応答・絞り込みCheck・標準/最小サイズの右端/下端見切れ判定へ追従。条件Win高さ、ComboBox間隔、フォーム余白、文字数カウンタ重なりを修正。
- 検証: CvWpfclient build警告・エラー0、UatVm pointmaster 112/112 PASS、JPG目視で文字切れ・ボタン/右端/下端見切れなし。条件Winは固定サイズのみ確認。
- 作業ログ800行超のため2026-09-16以前を整理し、重要判断4件を設計判断記録へ移動。

## [2026-10-06] 顧客：ポイント台帳・付与条件と3マスタメンテ

- `TranPointRireki`定義/登録と古いToDoを削除し伝票別`TranPointEvent`へ置換。long増減・適用条件参照Id・再送キー・enum変換を定義、旧実表/データは保持。
- `MasterPointBase`/`MasterPointBonus`追加、`MasterPointRank`を親版×ランクへ拡張。丸め/YesNoを流用し専用enumとEn変換を追加。設計は`Doc/spec/2026-10-06_ポイント制度_テーブル設計.md`。
- UpdateDb 26_10_06_01で旧ランクを親Id=0のまま値保持・索引再作成。新列の索引は移行後に作成。
- 詳細設計後に3マスタメンテを顧客管理メニューへ組込み。サーバで単件/一括の期間・参照・使用済み版・Vduを保護し台帳/残高の汎用書込みを禁止。JSONの0省略を定義側で防止。独立レビュー指摘の追加Idによる重複検査迂回も修正。
- 検証: CvServer/CvWpfclient build警告・エラー0、TestServer 1079/1079（保存保護42・SQLiteスキーマ6含む）、DDL7/7、専用SQLite UatVm69/69・標準/最小サイズの文字/ボタン/全列到達と12画像確認成功。計算・利用・残高・会員ランク対応・過去履歴移行は未実装。他DB実移行・同時書込み実機は未確認。

## [2026-10-06] 配分：配分データメンテ画面

### 実施内容
- `HaibunDataMenteView`（管理者用、準備中を解除）を実装。`BaseMenteViewModel<TranHaibun>` で修正(F2)・削除(F3)のみ、追加なし。編集可は納品日・確定日・実数量・欠品数・完了FLG・完了理由・送信FLG・関連No2・メモ。キー列と数量は読取専用。引当数はサーバ汎用更新・削除の既存処理で引き直す。
- 選択Winは既存 `RangeInputParamView` を再利用（Id・日付・店舗Id・倉庫Id・商品Id・JAN・商品名・件数）。`SelectInputParameter.RequireDirectConditionForShohin`（既定true）を追加し、本画面だけ商品単独検索を許可。
- `TranHaibun` に一覧表示用の `[ResultColumn]`（倉庫名・店舗名・商品CD/名・色/サイズ名）を追加。完了かつ `Su≠JitsuSu+ShortSu` は確認ダイアログ。
- UatVm シナリオ `haibun-data-mente` を追加。

### 検証
- slnx build 成功、TestServer 1,031/1,031 成功。UatVm で一覧取得（選択Win経由）・行選択を実表示しJPG確認、文字切れ・列はみ出しを修正済み（区分・商品名列は横スクロール）。
- UatVm `haibun-data-mente` で修正・削除の実行UAT（正常系・整合警告はい/いいえ・入力エラー5種・削除・Vdu競合）を実施し全PASS。修正・削除で引当数（SummaryStock/SummaryRealStock）が減ることも確認。

### 残余リスク・未実施
- 区分0の `ArrivedSu` は汎用更新では再計算しない（次回の仕入・配分保存・全件再集計で反映）。

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

## [2026-10-05] 月次：自動発注・補充

### 実施内容
- 配分由来の倉庫不足と直営店のSKU別基準在庫を対象に、既存発注残・未完了配分・積送を一度ずつ控除する補充を実装。通常商品の発注先は商品マスタの委託仕入先。消化仕入の追加発注は理由表示して停止する。
- 基準在庫・除外の設定2画面、月次の計算・保存・履歴・取消・確定を実装。補充はTranHojuに保存し、確定で区分1配分と区分15発注を同一トランザクション生成。Fingerprint再検査・実行キー・未確定バッチ一意制約で変更競合と再試行の重複を防ぐ。
- migration 26_10_05_02で補充列を追加。既存HHT migrationを保持し、旧DBの新列索引はmigration後に作成する。仕様書・商品メンテの入力制限解除はf0d4d675で先行commit済み。
- 基準設定のラベルを通常表示にし、長い名称は省略とTooltipを追加。月次の日付をyyyy/MM/ddへ統一し、配分・発注数量を初期表示へ移動。

### 検証
- 独立補充テスト22/22、TestServer全件回帰1,031/1,031成功。旧DB実起動migration、新DB起動、並行保存・確定、改変拒否、生成途中の在庫・引当を含むrollbackを検証。
- 専用新規SQLiteの実View→gRPC UAT autoreplenishは30/30 PASS。設定・除外・無効化・補充保存・履歴・競合拒否・取消・確定・再試行・入荷・次回補充・配分出庫・積送控除・商品仕入先入力を確認。
- 通常/最小サイズの全操作ボタン到達、横スクロール右端、日付・ラベル表示を検証し画像7枚を確認。証跡はDoc/test/UatVm/out/autoreplenish-20261005-165458.jsonl（生成物・commit対象外）。
- CvServer/CvWpfclient build成功、UatVm build成功（既存TaxMixScenarioのnull警告3件）。XML・UTF-8/CRLF・git diff --check成功。専用サーバは正常終了、共有開発DBは更新していない。

### 残余リスク・未実施
- SQLite以外の実DB・大量件数・通信障害注入は未実施。店舗対象は直営店（店種6）、消化仕入の追加発注、納期別・発注ロット・Scheduler自動実行は対象外。

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
