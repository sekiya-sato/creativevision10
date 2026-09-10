## [2026-09-10] 旧DBのSysSequence再作成による移行停止回避
### 実施内容
- DBバージョンが26090301の場合だけ、SysSequenceを削除して現行定義で再作成する処理を追加した。
### 確認
- 実行環境ログで、SysSeqType未追加のSysSequenceインデックス作成失敗がUpdateDb実行前に発生することを確認した。

---

## [2026-09-10] UAT-08 期首残高登録後のVM駆動検証
### 実施内容
- 期首売掛・請求・買掛・支払残を残高登録画面で投入し、代表取引後の売掛/買掛再作成を2回実行した。
- 期首4行の凍結、当期純増減、税・合計・掛フラグ、帳票前残、再実行不変を検証した。
### 確認
- 隔離SQLiteの`UatVm uat08opening --sqlite ... --manage-server --hide-views`で25判定すべてPASS。
- `dotnet build Doc/test/UatVm/UatVm.csproj --no-restore`成功（既存TaxMix警告3件）。

---

## [2026-09-10] UAT-01 新規商品登録から仕入のVM駆動検証
### 実施内容
- 商品マスタ画面で色サイズJAN付き商品を登録し、発注10→仕入4→仕入6を実行した。
- 派生SKU、発注残6→0・自動完了、在庫10、買掛10,000・税1,000・残11,000を検証した。
### 確認
- 隔離SQLiteの`UatVm uat01screen --sqlite ... --manage-server --hide-views`で11判定すべてPASS。
- `dotnet build Doc/test/UatVm/UatVm.csproj --no-restore`成功（既存TaxMix警告3件）。

---

## [2026-09-10] UAT-02 移動伝票のVM駆動検証
### 実施内容
- 直営店（TenType=6）向け受注を配分・確定・出荷し、移動出庫を作成した。
- RelateNo2、引当解除、出庫元在庫、売上未作成、受注残未消化を検証した。
### 確認
- 隔離SQLiteの`UatVm.exe juchushipping --url http://127.0.0.1:5005 --sqlite ... --manage-server --hide-views`で50判定すべてPASS。
- `dotnet build Doc/test/UatVmSeed/UatVmSeed.csproj --no-restore`、`dotnet build Doc/test/UatVm/UatVm.csproj --no-restore --no-dependencies`成功（既存TaxMix警告3件）。

---

## [2026-09-10] UAT-02 出荷競合・再読込のVM駆動検証
### 実施内容
- 出荷一覧取得後、別画面で確定取消・再確定してVdu競合を発生させた。
- 競合時の全件未処理・一覧破棄と、再検索後の最新Vduによる再実行成功を検証した。
### 確認
- 隔離SQLiteの`UatVm.exe juchushipping --url http://127.0.0.1:5004 --sqlite ... --manage-server --hide-views`で40判定すべてPASS。
- `dotnet build Doc/test/UatVm/UatVm.csproj --no-restore --no-dependencies`成功（既存TaxMix警告3件）。

---

## [2026-09-10] UAT-02 出荷確定取消のVM駆動検証
### 実施内容
- 滞留一覧から確定済み・未出荷の配分を取消し、未確定へ戻した。
- 在庫8・引当8と売上0を維持し、確定画面で再指示・再確定できることを検証した。
### 確認
- 隔離SQLiteの`UatVm.exe juchushipping --url http://127.0.0.1:5004 --sqlite ... --manage-server --hide-views`で28判定すべてPASS。
- `dotnet build Doc/test/UatVm/UatVm.csproj --no-restore --no-dependencies`成功（既存TaxMix警告3件）。

---

## [2026-09-10] UAT-02 全量欠品・強制完了のVM駆動検証
### 実施内容
- 既存受注の残4から在庫2を再配分・確定し、滞留一覧画面の強制完了を実行した。
- 実出荷0・欠品2・伝票未作成、在庫2維持・引当0、受注残4を検証した。
### 確認
- 隔離SQLiteの`UatVm.exe juchushipping --sqlite ... --manage-server --hide-views`で22判定すべてPASS。
- `dotnet build Doc/test/UatVm/UatVm.csproj --no-restore`成功（既存TaxMix警告3件）。

---

## [2026-09-10] UAT-03 在庫Rebuild一致
### 実施内容
- 即時移動、積送受入、未受取消後に在庫Rebuildを実行し、専用SKUの月次・実在庫を再照合した。
### 確認
- 隔離SQLiteの`UatVm.exe transfer --sqlite ... --manage-server --hide-views`で11判定すべてPASS。
- `dotnet build Doc/test/UatVm/UatVm.csproj --no-restore`成功（既存TaxMix警告3件）。

---

## [2026-09-10] UAT-02 在庫割れ・欠品のVM駆動検証
### 実施内容
- 受注10・在庫8で配分10の確定が原子的に拒否されることを確認し、配分8へ訂正した。
- 実出荷6・欠品2で売上6、在庫2、引当0、受注残4となることを検証した。
### 確認
- 隔離SQLiteの`UatVm.exe juchushipping --sqlite ... --manage-server --hide-views`で15判定すべてPASS。
- `dotnet build Doc/test/UatVm/UatVm.csproj --no-restore`成功（既存TaxMix警告3件）。

---

## [2026-09-10] UAT-04 過去棚卸訂正と再確定
### 実施内容
- 初回確定後に棚卸入力画面で実棚7を8へ訂正し、棚卸確定画面の再確定要表示を検証した。
- 再確定で調整伝票-3が-2へ置換され、調整1件・実在庫8となり二重計上しないことを確認した。
### 確認
- 隔離SQLiteの`UatVm.exe stocktake --sqlite ... --manage-server --hide-views`で11判定すべてPASS。
- `dotnet build Doc/test/UatVm/UatVm.csproj --no-restore`成功（既存TaxMix警告3件）。

---

## [2026-09-10] UAT-06 支払境界のVM駆動検証
### 実施内容
- 隔離SQLiteへ過払い・全額相殺・現金/相殺/手数料の専用仕入・支払を投入した。
- 支払計算画面で2026/07末締めを実行し、005〜007相当の支払残と再実行不変を検証した。
### 確認
- `UatVm.exe uat06payment --sqlite ... --manage-server --hide-views`で59判定すべてPASS。
- `dotnet build Doc/test/UatVm/UatVm.csproj --no-restore`成功。

---

## [2026-09-10] UAT-10 原価4項目のVM駆動検証
### 実施内容
- 隔離SQLiteに専用の通常商品・消化仕入商品・諸掛・仕入/返品・売上/返品を投入するシナリオを追加した。
- 消化仕入、諸掛確認、総平均原価5004円、評価替え80%で4003円、履歴取消後の5004円復元を実ViewModel経路で検証した。
### 確認
- `UatVm.exe costuat --sqlite ... --manage-server --hide-views`で7判定すべてPASS。
- `dotnet build Doc/test/UatVm/UatVm.csproj --no-restore`成功（既存TaxMix警告3件）。

---

## [2026-09-10] UatVm 隔離SQLite指定
### 実施内容
- `--sqlite`でCvServerとSeederへ同じテストDBを渡せるようにした。
### 確認
- リハーサルDBコピーでCvServerの起動・正規終了を確認した。

---

## [2026-09-09] UAT-04 棚卸のVM駆動検証
### 目的
- 棚卸開始、実棚入力、確定調整の在庫遷移を自動検証する。
### 実施内容
- 専用倉庫・SKU・帳簿在庫10を用意し、実棚7で確定する。
- 調整伝票-3、月次在庫（帳簿10・実棚7）、実在庫7を確認する。
### 確認
- `UatVm.exe stocktake --manage-server`で6判定すべてPASS。
- `dotnet build Doc/test/UatVm/UatVm.csproj --no-restore`成功。
### 注意
- 開始処理は帳簿在庫のみ保存し、実棚は確定時に棚卸伝票から集計される。

---

## [2026-09-09] UAT-03 店舗間移動のVM駆動検証
### 目的
- 即時移動、積送出庫、全量受入、未受積送取消の在庫遷移を自動検証する。
### 実施内容
- 専用元先倉庫・SKU・初期在庫20を用意し、移動3・積送5・取消2を実行する。
- 各状態の実在庫と積送中在庫、受入の出庫伝票紐付けを確認する。
### 確認
- `UatVm.exe transfer --manage-server`で8判定すべてPASS。
- `dotnet build Doc/test/UatVm/UatVm.csproj --no-restore`成功。
### 注意
- 移動受の出庫選択ダイアログは無人化対象外とし、全量受入の既存登録経路を検証する。

---

## [2026-09-09] UAT-02 受注・配分・出荷のVM駆動検証
### 目的
- 受注から受注配分、出荷確定、出荷売上までの実ViewModel経路を自動検証する。
### 実施内容
- 専用倉庫・卸先・SKU・在庫8をシードし、受注6、配分、確定、出荷を実行する。
- 出荷後の配分完了、関連売上、在庫2、引当0を確認する。
### 確認
- `UatVm.exe juchushipping --manage-server`で9判定すべてPASS。
- `dotnet build Doc/test/UatVm/UatVm.csproj --no-restore`成功。
### 注意
- 出荷社員は画面の同期選択ダイアログを避けるため、UAT側だけで内部入力値を設定する。

---

## [2026-09-09] UAT-01 再実行ランナーの現行判定修正
### 目的
- 現行の消費税率解決と買掛残高の符号に合わせ、UAT-01を再実行する。
### 実施内容
- 税率1を`TaxRateResolver.ResolveTaxRatePercent`で伝票月時点に解決する。
- 買掛・支払残高を`TotalShiire - TotalOut`（未払を正）で検証する。
### 確認
- 隔離DBでUAT-01ランナーを実行し、発注・仕入・返品・在庫・支払・冪等性の全判定がPASS。
- `dotnet build Doc/test/UAT01/UAT01Runner.csproj --no-restore`成功（既存のPkcs競合警告1件）。
### 注意
- 起動中の現行DBは使用していない。PDF/WPF目視は未実施。

---

## [2026-09-09] summaryreconcile 在庫Rebuild明細検証
### 目的
- 202607シードの`Jmeisai`欠落を解消し、UAT-07の在庫Rebuild前提を自動検証する。
### 実施内容
- `summaryreconcile`へ金額0・明細付きの専用仕入10/売上4を追加し、年月在庫（入庫10、出庫4、在庫6）と現在庫（在庫6）を突合する`stockrebuild`を追加した。
- `all`へ同検証を組み込み、専用SKUの集計行だけを`Clean`で除去する。
### 確認
- DBコピーで`summaryreconcile.exe stockrebuild <copy>`がPASS。`all`は例外なく完走し、`stockrebuild=PASS`。
- `dotnet build Doc/spec/tools/summaryreconcile/summaryreconcile.csproj --no-restore`、`git diff --check`が成功。
### 注意
- 既存DBの保存締日不一致により`closingcheck=FAIL`となる場合がある。本変更の在庫Rebuild検証とは無関係。

---

## [2026-09-09] 上代一括変更 Doc更新 Step10
### Agent
- Claude Opus-5 : Anthropic : Claude Code
### Editor
- Claude Code
### 目的
- 設計書`Doc/spec/2026-09-05_上代一括変更_詳細設計.md` Step 10（`Doc/`更新、本ログ追記）。コードは変更していない
### 実施内容
- `Doc/spec/2026-09-05_上代一括変更_詳細設計.md`: 冒頭のステータスを`原価4項目_詳細設計.md`の書式に倣い「状態」＋「実装状況（§8実装ステップに対応）」表へ置き換え、Step 1a〜9完了・実測の検証結果（`TestServer`898件・`TestSqlDialect`141件・UatVm 5本161件）・**業務UAT未実施**を明記した。§7未決事項へU8（仕入先抽出見送り）／U9（Timelineの価格グループ選択未実装）／U10（Escの扱い未実装）を追加。§8実装ステップ表へ「状態」列を追加しStep1a〜9=完了、Step10=本作業とした
- `Doc/spec/2026-09-05_CV10機能完成度チェックリスト.md`: `grep -n 上代一括変更`で洗い出した該当箇所を、実態（Step1a〜9実装完了・自動テスト全パス・業務UAT=UAT-11が残る）へ更新。「承認待ち」「実装未着手」の記述をすべて解消した（詳細は本コミット時のレビュー参照）
- `Doc/aicoding_log.md`: 本エントリを追記
### 確認
- ドキュメントのみの変更のため、ビルド・テストは実施していない（直前Step9時点の`dotnet build`0警告0エラー、`TestServer`898件、`TestSqlDialect`141件、UatVm 5本161件PASSの実測値をそのまま引用した）
### 注意
- 5.2の仕入先抽出、5.5 Timelineの価格グループ選択、Escの扱い（`ITranInputTab`）は未実装のまま。設計書§7 U8〜U10に明記し、チェックリストにも反映した
- 業務UAT（UAT-11）は本作業の対象外。実施は別途

---

## [2026-09-09] 上代一括変更 承認・確定フロー統合・旧UI撤去 Step9
### Agent
- Claude Opus-5 : Anthropic : Claude Code
### Editor
- Claude Code
### 目的
- 設計書`Doc/spec/2026-09-05_上代一括変更_詳細設計.md` Step 9（承認列の入力と確定フローの統合、旧UIの撤去）を実装する
### 実施内容
- ヘッダへ承認者（`SelectedApproveShain`）・承認日表示（`ApproveDayText`）を追加。社員選択は既存の入力者（`SelectedShain`/`SelectShainDialog`）と同じ仕組み（`ShainOptions`を共用、`SelectApproveShainDialog`を追加）に倣った。`VApproveShain`はTran系のV*列のため`MasterCascadeDb.VRules`へは登録していない（既存どおり未登録のまま）
- `BuildDenpyoAsync`で、承認者が選択されていれば保存のたびに`ApproveDay`を当日で記録するようにした（`Id_Shain`/`VShain`と対になる形。承認はStatus遷移のゲートにしない＝設計書2.10のまま）
- `DoFix`（確定）を設計書5.6の順序へ統合: ①`CheckConflicts`を内部で必ず実行し直し、C1/C2があれば④確認タブへ誘導して中止 → ②`MasterConfig.JodaiNeedApprove`=1のときだけ承認者未選択で中止 → ③プレビュー集計・警告件数を添えた確認ダイアログ（いいえで中止できる）→ ④Status=1で保存（`Jshop`スナップショットはStep6実装済みの`BuildDenpyoAsync`内`JodaiScopeResolver.Resolve`をそのまま利用、確認して漏れ無し）
- 内側タブ（①〜④）を切り替えるための`SelectedInnerTabIndex`を追加し、C1/C2検出時に④確認タブへ自動遷移するようにした
- 旧UI撤去: ヘッダの`CalcType`/`CalcRate`/`RoundUnit`/`RoundType`/`CalcValue`から直接`MeisaiRows.JodaiNew`を書き換える「変更」ボタン（`ApplyCalcAllCommand`。ツールチップに「旧Scope1件運用の互換用」と明記されていた）と、その実装`ApplyCalc`/`ApplyRound`（`double`版）を削除。`JodaiMeisaiRow.JodaiNew`/`RateOff`/`PriceInTax`（行単位の遺物。保存経路は`Cells`側の`JodaiPriceCell.JodaiNew`のみを使っており、この行単位フィールドは画面表示にも保存にも使われていなかった）も削除した。`CalcType`等の入力欄自体（新規Scope作成時の既定値）はXAML・DB列とも維持（設計書3.6）
### 技術決定 Why
- **丸め実装の「置き換え」ではなく削除とした**: `ApplyCalc`/`ApplyRound`（`MeisaiRows`の`JodaiNew`/`RateOff`/`PriceInTax`を書き換える経路）は、Step3/4で`CvBase.JodaiPriceRule`ベースのPrice Matrix（Scope単位の`Cells`）が導入されて以降、保存（`BuildJmeisaiCells`はCells側`cell.JodaiNew`のみを使用）にも画面表示（Price Matrix DataGridは`Cells[i].JodaiNew`と`JodaiOld`/`TankaGenka`の固定列のみを表示）にも一切参照されていない死コードだった。置き換えるべき「現在使われている丸め処理」自体が実質Cells側の`JodaiPriceRule.Calculate`（Step3で実装済み・変更なし）にのみ存在したため、旧UI撤去（Step9本来のタスク）と統合して削除した。既存伝票の再計算結果が変わらないことは、UatVm（jodaipricematrix・jodaiconfirmflow）で確定後の`DerivedJodai.Jodai`が`JodaiPriceRule.Calculate`の算出値と一致することにより担保した
- **DoFixからCheckConflictsを呼び直す**: `BuildDenpyoAsync`自体は登録時からC1/C2（エラー）を独立して禁止済みのため、確定操作の安全性そのものは元から確保されていた。それでも`CheckConflicts`を呼ばないと、④確認タブを一度も開かずに確定した場合に`PreviewXxx`・`ConflictRows`が未算出のまま残り、利用者が確定直後に④確認タブで内容を振り返れない。設計書5.6の「確定=競合チェック→プレビュー確認→…」という明文の順序に合わせるため、内部で必ずやり直す形にした
### 確認
- `dotnet build creativevision10.slnx`：0警告0エラー
- `Tests/TestServer/TestServer.exe`：898件成功
- `Tests/TestSqlDialect/TestSqlDialect.exe`：141件成功
- `dotnet build Doc/test/UatVm/UatVm.csproj`：0エラー（既存`TaxMixScenario`の無関係な警告3件のみ）
- `UatVm.exe jodaibulkextract/jodaiscope/jodaipricematrix/jodaiconfirm/jodaiconfirmflow(新規) --manage-server`：すべてPASS（23/41/39/36/22件）。**`--hide-views`を付けるとInit()がトリガーされず`FieldOptions`読込待ちでハングする**（`BaseWindow.OnContentRendered`はViewを`Show()`しないと発火しないため）と判明。前景（`--hide-views`無し）で実行すれば正常完走することを確認した（Step8のログにある「gRPCがハングする」報告は、おそらくこの`--hide-views`の付け方が原因）
- 実行後、`MasterConfig.JodaiNeedApprove`等の設定値・投入した伝票（Status=2で取消済み）を確認し、テストで変更した値は元へ復元済み
- CvServerプロセスの残留無しを確認
### 注意
- Esc（`ITranInputTab`）は設計書Step9の範囲外のため今回変更していない
- 判断がつかず残したものは無し（旧UIと判断した箇所はすべて上記のとおり撤去済み）
- `DoFix`のC1/C2判定は`BuildDenpyoAsync`が登録時から共通で行っているため、確定操作固有の追加判定は実質「状態の可視化」（`HasBlockingConflicts`/`ConflictRows`/`PreviewXxx`の更新）が主目的になる。設計書5.6の文言どおりの統合をしたが、安全性そのものは元から確保されていた点は報告しておく

---

## [2026-09-08] 上代一括変更 画面④確認（プレビュー・競合一覧・Timeline）Step8
### Agent
- Claude Opus-5 : Anthropic : Claude Code
### Editor
- Claude Code
### 目的
- 設計書`Doc/spec/2026-09-05_上代一括変更_詳細設計.md` Step 8（画面④確認）を実装する
### 実施内容
- ④確認タブを内側`TabControl`（①②③の後）へ追加。プレビュー（カード表示）・競合一覧（DataGrid）・Timeline（ItemsControlの比例幅バー）の3ブロック
- プレビュー: 対象Style数・SKU数（Step5のSKU数集計を再利用）・対象店舗数・適用期間・現在/変更後平均上代・平均値下率・展開見込行数・競合件数(C1〜C6)・原価割れ(C7)・最低販売価格違反(C8)。分母0でも例外・ゼロ除算にしない
- 展開見込行数が`MasterConfig.JodaiExpandWarnRows`（既定20万）を超えると警告表示
- 競合一覧: C1〜C8を種別・深刻度・件数・明細でDataGrid表示。C1/C2（エラー）があれば`DoFixCommand`のCanExecuteをfalseにして確定ボタンを無効化
- 競合チェックコマンド`CheckConflictsCommand`を追加。C1/C2/C5は`JodaiScopeResolver.Resolve`、C7/C8は`JodaiPriceRule.IsBelowCost`/`IsBelowMinPrice`をそのまま使い、C4/C6はgRPCの`QueryListSqlParam`でDB参照する（設計書6.3）
- Timeline: 選択商品×選択店舗の実効価格推移を`JodaiTimelineSegment`（開始日/終了日/価格/由来/通常上代へ戻る区間か/他伝票由来か）として算出。Scope期間の区間・期間外の通常上代区間・他伝票の確定済み`DerivedJodai`の重ね合わせをコマンド`BuildTimelineCommand`で構築し、描画（ItemsControl）はこのデータを見るだけにした
- `Doc/test/UatVm/Scenarios/JodaiConfirmScenario.cs`を追加し`Program.cs`へ登録(`jodaiconfirm`)
### 技術決定 Why
- **SQLを1箇所に集約**: C4/C6の判定SQL（設計書2.8に例示済みのDerivedJodai問い合わせ）を`CvBase/JodaiConflictSql.cs`（新規）へ切り出した。`CvDomainLogic.JodaiConflictChecker`（サーバ側、NPoco経由）と`CvWpfclient`の画面（gRPC `QueryListSqlParam`経由）の両方が`JodaiConflictSql.BuildOtherSlipConflictSql`を呼ぶ形にし、二重管理をなくした。結果行の型`JodaiConflictSql.OtherSlipRow`も共有（`QueryListSqlParam.ItemType`はサーバ側で型解決するため、クライアント内の入れ子クラスではなく共有アセンブリに置く必要がある。既存の`ScalarCountRow`/`JodaiEffectiveRow`と同じ理由）
- `BuildDenpyoAsync`（保存直前の明細組み立て）から`BuildJmeisaiCells`を抽出し、④確認タブの競合チェック（保存しない）と保存フローの両方が同じ組み立てロジックを共有するようにした（値は変えない、単純な抽出リファクタ）
- C7/C8の判定はStep7で`CvBase.JodaiPriceRule`へ切り出し済みの`IsBelowCost`/`IsBelowMinPrice`をそのまま再利用（画面はCvDomainLogicを参照できないため）
### 確認
- `dotnet build creativevision10.slnx`：0警告0エラー
- `Tests/TestServer/TestServer.exe`：898件成功
- `Tests/TestSqlDialect/TestSqlDialect.exe`：141件成功
- `dotnet build Doc/test/UatVm/UatVm.csproj`：0警告0エラー(既存TaxMixScenarioの無関係な警告3件のみ)
- `UatVm.exe jodaibulkextract/jodaiscope/jodaipricematrix/jodaiconfirm --manage-server`: **このセッションの実行環境ではgRPC呼び出し(初期化時のFieldOptions読込)がハングし完走できなかった**。CvServerは正常起動するが、画面側の最初の非同期クエリが数分たっても完了しない。変更前の`jodaibulkextract`（無改修）でも同一のハングを再現したため、Step8の実装起因ではなくこのセッションのサンドボックス環境（ネットワーク/スレッド）固有の問題と判断した。同日21:59に別セッションで`jodaibulkextract`が23件成功(PASS)している記録が`Doc/test/UatVm/out/`に残っており、環境が正常な状態では動作することを示している。ビルド・ユニットテスト・コードレビューでの担保に留め、UatVm実機確認は持ち越し
### 注意
- 上記の理由により、`jodaiconfirm`シナリオ自体は今回未実行。次回、正常に動く環境でまず`jodaibulkextract`等の既存3本が通ることを確認してから`jodaiconfirm`を流すこと
- 診断のため`Doc/test/UatVm/ViewDriver.cs`の`WaitAsync`既定タイムアウトを一時的に300秒へ変更したが、確認後60秒へ復元済み（`git diff`で無変更を確認済み）

---

## [2026-09-08] 上代一括変更 画面③Price Matrix（動的Scope列・セル一括操作・原価割れ警告）Step7
### Agent
- Claude Opus-5 : Anthropic : Claude Code
### Editor
- Claude Code
### 目的
- 設計書`Doc/spec/2026-09-05_上代一括変更_詳細設計.md` Step 7（画面③ Price Matrix）を実装する
### 実施内容
- ③価格タブを Price Matrix（行=商品／列=Scope／セル=JodaiNew）へ作り替え。固定列は商品CD/商品名/通常上代/原価
- Scope列を実行時に生成。`ScopeRows`の増減とプロパティ変更を購読して列を作り直す
- セルの複数選択→一括操作（率・額・固定・価格ポイント）。算出は`JodaiPriceRule.Calculate()`
- 原価割れ(C7)・最低販売価格違反(C8)のセルを背景色で警告。判定は`JodaiPriceRule.IsBelowCost`/`IsBelowMinPrice`へ切り出し、`CvDomainLogic.JodaiConflictChecker`と同一基準を共有する
- `Doc/test/UatVm/Scenarios/JodaiPriceMatrixScenario.cs`を追加し`Program.cs`へ登録(`jodaipricematrix`)
### 技術決定 Why
- C7/C8の判定述語を`CvBase`へ置いた。`CvWpfclient.csproj`は`CvDomainLogic`を参照していないため（`CodeShare`/`CvAsset`/`CvBase`のみ）、画面とサーバ側で同一基準を共有するには双方が参照できる層1へ切り出すしかない
- Scope列の見出しテンプレートはXAMLリソースではなくコードで組み立てる。`DataGrid.Resources`／`Window.Resources`のどちらに置いても`FindResource`で解決できず、`ResourceReferenceKeyNotFoundException`で画面のロードごと落ちた。列生成自体がコード側の処理なので、テンプレートもコード側に閉じるほうが依存が少なく壊れにくい
- 背景色そのものはUatVmでは観測できないため、判定結果を`JodaiPriceCell.IsCostViolation`/`IsMinPriceViolation`として公開し、UatVmはそちらを検証する
### 確認
- `dotnet build creativevision10.slnx`：0警告0エラー
- `Tests/TestServer/TestServer.exe`：898件成功
- `Tests/TestSqlDialect/TestSqlDialect.exe`：141件成功
- `UatVm.exe jodaipricematrix --manage-server`：39件成功(PASS)
- `UatVm.exe jodaiscope --manage-server`：41件成功(PASS、回帰なし)
- `UatVm.exe jodaibulkextract --manage-server`：23件成功(PASS、回帰なし)
### 注意
- UatVmは`Doc/test/UatVm/bin/`配下に`CreativeVision10`のコピーを持つ。`creativevision10.slnx`のビルドだけでは更新されないため、画面を直したら`dotnet build Doc/test/UatVm/UatVm.csproj`も実行してから流すこと
- `--manage-server`実行が異常終了するとCvServerが残り、次のビルドが`MSB3021`で失敗する。残っていれば停止してからビルドすること

---

## [2026-09-08] 上代一括変更 画面②適用範囲（Scope編集・内側タブ・No_Scope展開・段階値下げ）Step6
### Agent
- Claude Opus-5 : Anthropic : Claude Code
### Editor
- Claude Code
### 目的
- 設計書`Doc/spec/2026-09-05_上代一括変更_詳細設計.md` Step 6（画面②適用範囲）を実装する
### 実施内容
- 「修正・登録画面」タブの中身を内側`TabControl`（①対象商品／②適用範囲／③価格）へ組み替え。伝票ヘッダと操作ボタンはタブの外に残し、`ITranInputTab`は実装せず（Escの挙動は現状維持）
- ①対象商品＝Step5の抽出条件Card。②適用範囲＝新規のScope一覧DataGrid＋（旧）一括変更条件Card（「新規Scope作成時の既定値」に改称）＋対象店舗Card（移設）。③価格＝旧明細Cardをそのまま移設（Price Matrix化はStep7）
- `JodaiScopeRow`（VM内, `ObservableObject`）を追加し`ScopeRows`を編集。範囲種別・軸・対象/除外・価格方式は`EnumJodaiRangeType`/`EnumJodaiGroupAxis`/`EnumJodaiIncExc`/`EnumJodaiPriceMethod`（Step1b定義）をそのまま使用。グループ/店舗選択は「解決結果を確認」ダイアログでも使う`ScopeStoreOptions`/`PriceGroupOptions`等をXAMLのStyle.Triggersで軸により切替
- 初期状態はScope1件（全店/対象/ヘッダ既定期間・価格ルール）。「Scope追加」「段を追加」（元のDayToの翌日から同じ日数で複製）「Scope削除」「解決結果を確認」（`JodaiScopeResolver.Resolve()`を呼び、店舗→採用Scopeと競合をSeverity別に提示）を実装
- `LoadEditAsync`で`den.NormalizeLegacyScope()`を呼び、Scope空の既存伝票を画面へ全店Scope1件で載せる（DB不変更）
- `BuildDenpyo`を`BuildDenpyoAsync`化。`Jmeisai`を「商品×Scope」へ複製し`JodaiPriceRule.Calculate()`で`JodaiNew`/`JodaiBase`/`TankaGenka`を設定。`Jshop`は`JodaiScopeResolver.Resolve()`の結果を採用しつつ、読込時点の`Jshop`と(Id_Tenpo,No_Scope)一致する行は期間を保持（設計3.3の店舗別微調整を維持）。C1/C2（エラー）があれば保存中止。`MasterConfig.JodaiMaxCells`（商品数×Scope数）を抽出時・Scope追加時・保存直前の3箇所でチェックし超過時は中止
- 方式4（実効上代からの値下率）は`DerivedJodai.FinalJodaiSql`を商品×日付ぶんUNION ALLする1本のSQLで一括解決（店舗非依存、対象系統全件0基準の近似）
- `CvBase/BaseDb0System.cs`に`MasterMeisho.KubunPricePoint="PPT"`を追加。`CvBase/Parameters.cs`に`JodaiEffectiveRow`（方式4解決用スカラー行）を追加。`Doc/test/UatVm/VmSession.cs`に`InsertAsync<T>`を追加（既存伝票の直接投入用）
- `CvWpfclient/Helpers/Converters/JodaiScopeDisplayConverters.cs`を追加し`App.xaml`へ登録（Scope表示用コンバータ7種）
- `Doc/test/UatVm/Scenarios/JodaiScopeScenario.cs`を追加し`Program.cs`へ登録(`jodaiscope`)
### 技術決定 Why
- 「グループ/店舗」選択はXAMLの`Style.Triggers`（`RangeType`/`GroupAxis`によるItemsSource切替）で実現し、行ごとに複数の選択肢参照を持たせる複雑さを避けた。選択反映は`JodaiScopeRow.SelectedGroupOrStoreOption`が`Id_Group`/`Id_Tenpo`とコード・名称(時点値)を同時更新する
- 「対象店舗」Cardは設計書5.1の表には明記が無いが、店舗選択(IsTarget)がScope解決の入力になる（設計3.3・5.6）ため②適用範囲タブに配置した（判断1件目）
- 店舗ごとの期間微調整(設計3.3・U5)は、保存のたびにResolverの出力を全面上書きせず、直前に読み込んだ`Jshop`の(Id_Tenpo,No_Scope)一致行から期間を引き継ぐ方式とした
- 方式4の基準額（実効上代）は店舗別に異なり得るが、Scope1セルにつき1つの値しか持てないため、対象系統の全件(0)基準への近似とした（判断2件目。厳密な店舗別実効上代の反映はStep8以降で要検討）
### 確認
- `dotnet build creativevision10.slnx`：0警告0エラー
- `Tests/TestServer/TestServer.exe`：898件成功
- `Tests/TestSqlDialect/TestSqlDialect.exe`：141件成功
- `Doc/test/UatVm/.../UatVm.exe jodaibulkextract --manage-server`：23件成功(PASS、回帰確認)
- `Doc/test/UatVm/.../UatVm.exe jodaiscope --manage-server`：30件成功(PASS)。Scope1件展開の後方互換一致・段追加の日付シフト・段階値下げ3段展開・Scope重複競合(C1/C2)での登録中止・JodaiMaxCells超過中止・既存伝票(Jscope空)読込時の全店Scope1件補完を確認
- テスト実行で書き換えた`MasterConfig.JodaiMaxCells`は事後に`30000`へ復元済み（cv-sqlite MCPで確認）

---

## [2026-09-08] 上代一括変更 画面①対象商品（抽出条件拡張・可変行・AND/OR・Style/SKU数）Step5
### Agent
- Claude Opus-5 : Anthropic : Claude Code
### Editor
- Claude Code
### 目的
- 設計書`Doc/spec/2026-09-05_上代一括変更_詳細設計.md` Step 5（画面①対象商品）を実装する
### 実施内容
- 抽出条件の検索項目を拡張: 素材/原産国/発売日(店頭投入日)/現在上代(数値比較)/商品分類(Jsub)の枠。仕入先は実装せず理由をコードコメントに残した
- `FieldOptionsStatic`(static)を廃止し`FieldOptions`(非static、Jsubの枠を`MasterMeisho`から動的読込)へ変更。XAMLは`DataGridComboBoxColumn`から`DataGridTemplateColumn`+`ComboBox`(`DataContext.FieldOptions`を`AncestorType=DataGrid`経由参照)へ置換
- 抽出条件行を可変化(初期1行、追加/削除コマンド、3行決め打ちパディング廃止)
- `TranJodaiCond.Ope`(AND/OR)に対応。1行が生む`>=`/`<=`は必ず1単位で括弧化し、行同士は左結合で明示的に括弧を入れて畳み込む。1行目のOpeは無視(UI側も1行目は無効化/空表示)
- 対象Style数(MeisaiRows件数)/SKU数(`DerivedShohinColSiz`件数)を表示。SKU数は件数が多くても`IN`にIdを直接並べず、抽出条件のWHERE句をサブクエリとして再利用
- `CvBase/Parameters.cs`にスカラーCOUNT受け取り用共有DTO`ScalarCountRow`を追加(`QueryListSqlParam.ItemType`はサーバ側で型解決するため、クライアント内の入れ子クラスは使えないことが判明したため)
- `Doc/test/UatVm/Scenarios/JodaiBulkExtractScenario.cs`を追加し`Program.cs`へ登録(`jodaibulkextract`)
### 技術決定 Why
- 数値項目は`CAST(@x AS INTEGER)`で比較し文字列比較による桁ズレ("9800">"12800")を避ける
- Jsubの枠は`MasterMeisho`の`Kubun='IDX'`かつ`Code IN('B01'..'B10')`に登録済みの行だけを検索項目にし、固定名でハードコードしない(`MasterTokuiMenteViewModel.DoGetKubun`に倣う)
- Jsubの`json_each`はNULL安全だが不正JSON文字列では例外になるため`json_valid()`ガードを併用(`MasterCascadeDb`と同じ考え方)
### 確認
- `dotnet build creativevision10.slnx`：0警告0エラー
- `Tests/TestServer/TestServer.exe`：898件成功
- `Tests/TestSqlDialect/TestSqlDialect.exe`：141件成功
- `Doc/test/UatVm/.../UatVm.exe jodaibulkextract --manage-server`：23件成功(PASS)

---

## [2026-09-08] 店舗イベントを使う日別予算配分と店舗予算票
### Agent
- GPT-5.6 Terra : OpenAI : Codex
- GPT-5.6 Luna : OpenAI : Codex
- GPT-5.6 Sol : OpenAI : Codex
### Editor
- Codex
### 目的
- 店舗イベントを日別予算配分の判断材料とし、店舗予算票で予実変動の理由を確認可能にする
### 実施内容
- 店ブランド予算マスタ（月一括）へイベント名・重要度・イベント係数・有効係数を表示し、低1.1／中1.3／高1.5を曜日係数へ乗算して自動配分
- 休業日はイベント有無にかかわらず係数0を維持し、保存時の手修正・洗替え・丸め差表示は変更しない
- 店舗予算票の店舗別PDFへイベント名（重要度）を追加し、全店出力は空欄を維持
### 技術決定 Why
- イベント係数は配分案の再計算時だけに使い、確定済み予算へ根拠を永続化しないことで既存のMasterYosanBrand契約を維持する
- 帳票は既存の空白領域とitem27を使い、既存の金額列・全店集計を変更しない
### 確認
- CvWpfclient build：警告0、エラー0
- QFM：cp932、XML整形式、item27参照を確認
- QFM実PDF描画：ローカルハーネスの配置不整合により未実施

---

## [2026-09-08] システム管理マスタ選択項目の整数バインド修正
### Agent
- GPT-5.6 Terra : OpenAI : Codex
- GPT-5.6 Sol : OpenAI : Codex
### Editor
- Codex
### 目的
- 後追加設定の選択値を表示・保存可能にする
### Agent(修正)
- Claude Opus-5 : Anthropic : Claude Code
### 実施内容
- TaxRounding/CostMethod を ItemsSource + DisplayMemberPath/SelectedValuePath 方式へ変更（VMに int 値リスト TaxRoundingItems/CostMethodItems を追加）
### 技術決定 Why
- ComboBoxItem の Tag は string/enum になり int プロパティと Equals 一致せず選択が復元されない。SelectedIndex は項目未生成時に -1 を書き戻す危険がある。得意先メンテの入金月(PayMonthItems)と同じ、値の型が一致する方式に統一
### 確認
- ビルド成功、画面確認待ち

---

## [2026-09-08] システム管理マスタの後追加項目表示
### Agent
- GPT-5.6 Terra : OpenAI : Codex
- GPT-5.6 Luna : OpenAI : Codex
### Editor
- Codex
### 目的
- システム管理マスタで後追加した税設定と原価方式を編集可能にする
### 実施内容
- `TaxRounding` と `CostMethod` の選択欄を既存フォームへ追加
### 技術決定 Why
- 既存の `Current` 保存経路と列挙型を直接使い、ViewModel・DBを変更しない
### 確認
- XAML XML、行定義、`git diff --check`：問題なし
- 画面確認：ユーザー確認済み

---

## [2026-09-08] 14:30 自動実行履歴の実行種別表示
### Agent
- GPT-5.6 Terra : OpenAI : Codex
- GPT-5.6 Luna : OpenAI : Codex
### Editor
- Codex
### 目的
- 自動実行履歴で実行種別を日本語表示する
### 実施内容
- 一覧と詳細へ実行種別を追加
- int値を `EmSysHistType` のCommentへ変換して表示
### 技術決定 Why
- DBモデルを変更せず、既存enumの表示定義を共通Converterから再利用する
### 確認
- XAML XML、CRLF、git diff --check：問題なし
- 画面確認：ユーザー確認済み

---
## [2026-09-08] 14:03 ストリーム処理の手動実行履歴
### Agent
- GPT-5.6 Terra : OpenAI : Codex
- GPT-5.6 Luna : OpenAI : Codex
### Editor
- Codex
### 目的
- 業務ストリームの開始・終了を `SysHistAutoexec` へ記録する
### 実施内容
- `Msg040`、`Msg050`〜`058`、`Msg082`/`085`/`087`/`089` を共通履歴ラッパー経由に変更
- 開始時は短期DBスコープで履歴を追加してIdだけを保持し、終了時は別スコープで更新
- 選択変換の終了履歴へ変換プログラム内部名を記録
### 技術決定 Why
- 既存の業務・排他履歴は維持し、ストリーム実行履歴を一元化する
### 確認
- CvServer build：警告0、エラー0
- git diff --check：問題なし

---
## [2026-09-07] 14:48 Tran未解決商品の補足マスタ生成
### Agent
- GPT-5.6 Sol : OpenAI : Codex
- GPT-5.6 Terra : OpenAI : Codex
### Editor
- Codex
### 目的
- Tran通常商品明細で未解決の `MasterShohin` を補足生成し、明細を保持したまま `Id_Shohin` を再設定する
### 実施内容
- 全通常Tran 10型の変換後に補足マスタ生成・明細再紐付けステップを追加
- 空・16文字超コードと `Tran02Material` の関連商品を生成対象外として保持
- JSON更新を全明細保持型へ変更し、サイズ補完の不一致明細脱落も修正
- 変換選択画面の表示名とSQLite自動テストを追加
### 技術決定 Why
- 補足マスタ追加とJSON再紐付けをSerializableトランザクションにまとめ、再実行時の重複作成を防ぐ
### 確認
- CvDomainLogic build：警告0、エラー0
- TestServer 対象テスト：2件成功
- TestSqlDialect：141件成功
- CvWpfclient build：警告0、エラー0
- git diff --check：問題なし

---
## [2026-09-05] 15:00 追加 skill 整理
### Agent
- GPT-5.6 Sol : OpenAI : Codex
- GPT-5.6 Terra : OpenAI : Codex
- GPT-5.6 Luna : OpenAI : Codex
### Editor
- Codex
### 目的
- 現行規約・ソース根拠に合わせ、廃止3 skillと存続skillの参照関係を整理する
### 実施内容
- 廃止対象 `check-xaml` / `create-print-view-from-crs` / `fix-scheduler-job-management-wpf` の存続skillからの参照を除去
- 更新skillへ App.xaml、DataGridAssist、UatVm、Scheduler契約、CRS/QFM列対応、commit user.name規約を反映
### 技術決定 Why
- 現行の `Doc/test/UatVm/README.md`、`CvWpfclient/App.xaml`、MasterShohinMenteView、ISchedulerServiceを根拠に、重複skillを増やさず共通guideへ統合した
### 確認
- 存続18 skillのfrontmatter/name、TODO、CRLFを手動確認
- 削除対象skill内部以外の参照をrg確認
- git diff --check：問題なし
- quick_validate.py：同梱Pythonで実行したがPyYAML不足のため未実行、手動検証で代替

---
## [2026-09-04] 20:42 POS専用gRPC契約と公開経路の削除
### Agent
- GPT-5.6 Terra : OpenAI
### Editor
- Codex
### 目的
- ユーザーからの要望：cvpos10の共通メッセージ経路化に伴い、cv10の不要なPOS専用gRPC定義を削除する
### 実施内容
- CodeShare/PosContracts.cs: POS DTOを専用I/Fから分離し、IPointOfSaleServiceを削除
- CvServer/Program.cs: PointOfSaleServiceの専用gRPC公開を削除
- CvServer/Services/PointOfSaleService.cs: 専用エンドポイント向け属性と契約実装を削除し、共通経路の内部業務処理として保持
### 技術決定 Why
- POSの売上・取消・精算ロジックとDTOはCoreServiceのCvMsg経路で引き続き必要なため保持し、直接利用されなくなった専用契約と公開経路だけを削除する
### 確認
- creativevision10.slnx のビルド成功
- TestServer の PointOfSaleServiceTests：15件成功
- cvpos10.slnx：警告 0、エラー 0
- git diff --check：問題なし

---
## [2026-09-07] 店舗予算表の予算なし店舗・客数集計対応
### Agent
- GPT-5.6 Terra : OpenAI : Codex
- GPT-5.6 Luna : OpenAI : Codex
- GPT-5.6 Sol : OpenAI : Codex
### Editor
- Codex
### 目的
- 当月に予算がなくても売上伝票がある店舗を店舗予算表へ出力し、客数を伝票数として表示する
### 実施内容
- 出力店舗を当月予算または売上・返品伝票のある店舗へ変更
- 社販売上列をSQLから削除して以降を1列前詰め
- 客数へ売上・返品伝票ヘッダ数を設定
### 技術決定 Why
- `Tran01Tenuri` は伝票ヘッダ単位のため、売上・返品伝票を各1客として数える
### 確認
- SQLの店舗別・全店別SELECTが各26列、社販売上列なしを確認
- QFMはcp932でXML整形式、最大参照item26、item27参照なしを確認
- `git diff --check`：問題なし
- CvWpfclient build（--no-restore）：警告0、エラー0

---
