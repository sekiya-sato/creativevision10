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
