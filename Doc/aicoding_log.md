## [2026-08-11] 14:20 上代一括変更 一連の作業の点検と展開数表示の不備修正
### Agent
- Opus 5 : Anthropic : Sekiya Sato Claude
### Editor
- Claude Code
### 目的
- ユーザーからの要望：一連の作業のチェックと、残作業に漏れがないかを確認する。
### 実施内容
- CvWpfclient/ViewModels/01Master/MasterJouDaiBulkChangeViewModel.cs: 伝票一覧の展開数を `TranJodai.ExpandCnt` 列ではなく `DerivedJodai` の相関サブクエリで数えるよう是正。一覧SQLに別名 `J` を付け WHERE 句も修飾。`LoadEditAsync` と `DoRegister` で `ExpandCnt` 列を読む処理を `ReloadExpandCountAsync()` に置き換え。
- CvBase/BaseDbJodai.cs: `TranJodai.ExpandCnt` のコメントに「更新するのは `JodaiDb.Rebuild()` だけで、通常の展開では追随しない」ことを明記。
- Doc/spec/spec.database.cvbase.md: `DerivedJodai` の定義位置を `CvBase/BaseDbJodai.cs:424` → `:484` に訂正（重複排除の追加で行がずれていた）。
### 技術決定 Why
- **不備の内容**: 展開は `TranJodai` の `IDerivedOrigin` 経由でサーバの `HandlerDerived` が自動実行するため、画面が伝票を保存しても `ExpandCnt` 列は更新されない。列を更新するのは修復用の `JodaiDb.Rebuild()` だけなので、**確定済み伝票の一覧「展開数」が常に 0 のまま**だった。
- **保存し直す案は却下**。確定後に `ExpandCnt` を入れて再保存すると `HandlerDerived` が Update で再び発火し、展開（削除＋再INSERT、最大10万行）が2回走る。
- **一覧SQLで数える方式を採用**。`nk2(Id_Tran)` のインデックスで引けるため相関サブクエリでも安く、常に現在値になる。`ExpandCnt` 列は `Rebuild()` の記録用として残す。
### 影響範囲
- 上代一括変更画面のみ。他画面・サーバ処理への影響なし。
### 確認
- `vscmd.bat dotnet build creativevision10.slnx`: 成功（警告0、エラー0）。
- `TestServer.exe`: 35件すべて成功。
- 一時検証（スクラッチ）: 対象2店舗×明細3件の確定伝票に対し、一覧SQLが `ShopCnt=2 / MeisaiCnt=3 / ExpandCnt=6` を返し、JSON列(`Jshop`/`Jmeisai`)は未取得(0件)、保存された `ExpandCnt` 列は 0 のままであることを確認（不備の再現と修正の両方を確認）。
- 一時検証（スクラッチ・WPF実体化）: XAMLパース・StaticResource解決・タブ2実体化に成功。バインディングエラーは既存画面と同種のフレームワーク由来のみ。
- 全プロジェクトを `TankaJodai` で走査し、残作業の対象（12ファイル）と意図的な対象外（商品マスタメンテ／旧システム変換／初期データ／表示バインディング）を確定。`CvPrints` と `printform/*.qfm` に直接参照が無いことも確認。

---

## [2026-08-11] 14:01 上代一括変更 Phase4 期限切れパージのスケジューラ登録と送信FLG運用
### Agent
- Opus 5 : Anthropic : Sekiya Sato Claude
### Editor
- Claude Code
### 目的
- ユーザーからの要望：後続フェーズ 2,3,4 を順次実施する（本ログは Phase4）。
### 実施内容
- CvDomainLogic/JodaiDb.cs: `PurgeExpiredByConfig()` / `GetKeepDays()` / `MarkSent()` と定数 `ConfigKeepDaysName`(=JodaiKeepDays) / `DefaultKeepDays`(=90) を追加。
- CvServer/Services/SchedulerService.cs: システムタスク `JodaiPurgeTaskName`（毎日1:30）を追加。`RegisterJodaiPurgeTask()` と `ExecuteJodaiPurgeCoreAsync()` を実装し、`IsSystemTask` にも登録。
- CvServer/Program.cs: `ApplicationStarted` で `RegisterJodaiPurgeTask()` を呼ぶ。
- CvWpfclient/ViewModels/01Master/MasterJouDaiBulkChangeViewModel.cs: `EditSendFlg` / `SendFlgName` / `DoMarkSentCommand` を追加。確定時に `SendFlg=0`（未送信）へ戻す。一覧行にも送信状態を追加。
- CvWpfclient/Views/01Master/MasterJouDaiBulkChangeView.xaml: ヘッダの送信状態表示、一覧の「送信」列、[送信済] ボタンを追加。
### 技術決定 Why
- **パージは新しい `SchedulerTaskType` を足さず、システムタスクとして登録した**。既存の WALチェックポイント／ワークファイル削除／月次再集計と同じ形で、固定Guid＋固定cronで `Program.cs` から登録する。クライアントのジョブ登録画面は TaskType を `LogOnly` 固定でしか送らないため、enum値を足しても画面から到達できず、使えない選択肢が増えるだけになる。
- **保持日数は `MasterConfig` の `JodaiKeepDays`（既定90日）**。`DerivedJodai` は伝票から再生成できるので消しても復元可能。プロパー(P)区分は `DayTo="99991231"` なので自動的に対象外になる。
- **送信FLGは「価格の配信」ではなく「値札・棚札を差し替えた記録」と定義した**。cv10 の POS は `PointOfSaleService` 経由でサーバの適用上代を直接引くため価格配信の実体が存在しない。存在しない配信処理を作らず、運用管理用のマークとして意味づけを明記した。確定のたびに未送信へ戻すのは、価格が変われば貼り替えが再度必要になるため。
- `MarkSent` は `Status=1`（確定）の伝票だけを対象にする。入力中・取消の伝票を送信済みにできてしまうと運用管理の意味が失われる。
### 影響範囲
- 既存のスケジューラ動作には影響なし（タスクを1つ追加するのみ）。`MasterConfig` に `JodaiKeepDays` が無い環境は既定90日で動作する。
### 確認
- `vscmd.bat dotnet build creativevision10.slnx`: 成功（警告0、エラー0）。
- `TestServer.exe`: 35件すべて成功。
- 一時検証（スクラッチ）: `GetKeepDays` が既定90／`MasterConfig` 設定後30を返すこと、`PurgeExpiredByConfig` が保持日数より前に終了した1件だけを削除し無期限(99991231)を残すこと、`MarkSent` が確定済み伝票のみ1件更新し未確定は0件であることを確認。
- 一時検証（スクラッチ・WPF実体化）: XAMLパース・StaticResource解決・タブ2実体化に成功。バインディングエラーは既存画面と同種のフレームワーク由来のみ。

---

## [2026-08-11] 13:52 上代一括変更 Phase3 画面(検索画面/修正・登録画面)の実装
### Agent
- Opus 5 : Anthropic : Sekiya Sato Claude
### Editor
- Claude Code
### 目的
- ユーザーからの要望：後続フェーズ 2,3,4 を順次実施する（本ログは Phase3）。
### 実施内容
- CvWpfclient/ViewModels/01Master/MasterJouDaiBulkChangeViewModel.cs: 空のスタブから全面実装。検索(一覧)/新規/読込/対象一覧取得/明細取得/一括計算/登録/確定/取消。行クラス `JodaiListRow` `JodaiCondRow` `JodaiShopRow` `JodaiMeisaiRow` を追加。
- CvWpfclient/Views/01Master/MasterJouDaiBulkChangeView.xaml: 空の `<Grid />` から全面実装。旧画面と同じ2タブ構成（検索画面 / 修正・登録画面）、伝票ヘッダ・一括変更条件・抽出条件・対象一覧（店舗別期間）・対象明細。
- CvWpfclient/Models/MenuData.cs: 「AfterToDo: 上代一括変更」の準備中表記を外し、addInfo を実装内容に更新（項目位置と並び順は変更なし）。
### 技術決定 Why
- **一覧では JSON列を SELECT しない**。`Jcond`/`Jshop`/`Jmeisai` は明細500件で約128KBになるため、タブ1の一覧SQLは列を明示して除外し、規模は `ShopCnt`/`MeisaiCnt`/`ExpandCnt` の非正規化列で表示する。編集時だけ `SELECT *` で読む。
- **展開処理を画面から呼ばない**。`TranJodai` が `IDerivedOrigin` を実装しているため、確定(Status=1)で保存した時点でサーバの `HandlerDerived` が同一トランザクションで `DerivedJodai` を展開する。画面は Status を変えて保存するだけにし、展開処理の二重実装を避けた。
- **登録前に `TranJodai.Normalize()` を必ず呼ぶ**。重複したまま確定すると `DerivedJodai` のユニークキー違反でトランザクションごと失敗するため、`FindDuplicates()` で利用者に確認してから後勝ちで除去する。
- **プロパー(P)選択時は終了日を 99991231 に寄せる**。ヘッダ・店舗別期間の両方で寄せることで、無期限オーバーレイという設計上の表現とUIの入力値がずれないようにした。
- 対象系統(`TaishoType`)を切り替えたら対象候補が全く別物（直営店 ⇔ 卸先）になるので、選択済みの対象一覧を破棄する。
- `DataGridComboBoxColumn` は視覚ツリーの外にあり DataContext を辿れないため、検索項目の選択肢は `FieldOptionsStatic` として静的公開し `x:Static` で参照する。
- 一括計算の率は**上代からのOFF率**（新販売価格 = 上代 ×(1 − 率/100) を丸め）、金額は**新販売価格の直接指定**とした。旧画面の「新割引率」列と整合する解釈。
### 影響範囲
- 新規画面の実装のみ。既存画面・既存ロジックへの変更なし（MenuData は表示名と説明文の変更のみで項目位置は不変）。
### 確認
- `vscmd.bat dotnet build creativevision10.slnx`: 成功（警告0、エラー0）。
- StaticResourceキー15種（FormLabel/FormComboBox/FormDatePicker/NumericFormTextBox/BudgetActionButtonStyle/MenteDataGridColumnHeader/DataGridRightTextBlock 他）の実在をリソース辞書で確認。
- 一時検証（スクラッチ・WPF実体化）: XAMLパースとStaticResource解決、タブ2の実体化、`ApplyCalcAllCommand` 実行まで成功。バインディングエラーは既存の `HenpinInputView` と同種のフレームワーク由来ノイズのみで、本画面固有のものは0件。

---

## [2026-08-11] 13:31 上代一括変更 Phase2 上代解決経路の差し替え(POS/在庫評価)
### Agent
- Opus 5 : Anthropic : Sekiya Sato Claude
### Editor
- Claude Code
### 目的
- ユーザーからの要望：後続フェーズ 2,3,4 を順次実施する（本ログは Phase2）。
### 実施内容
- CvBase/BaseDbJodai.cs: 倉庫軸用の `DerivedJodai.ResolveSokoSql()` / `FinalJodaiSokoSql()` と、判定日の既定式 `TodaySql` を追加。
- CodeShare/IPointOfSaleService.cs: `PosBarcodeLookupRequest` に `StoreId`(Order=2) を追加。
- CvServer/Services/PointOfSaleService.cs: `LookupProductAsync` と `CreateLine` の単価を `JodaiDb.ResolveJodai` 経由に変更。`ResolveJodai` プライベートメソッドを追加。`CreateSale` で `denDay` を1回だけ算出して明細へ渡すよう整理。
- CvWpfclient/Helpers/ViewModels/StockSql.cs: `TankaJodai()` を適用上代対応に変更（引数を `stock`/`shohin` の別名2つに）。定価のみが必要な箇所向けに `TankaJodaiMaster()` を追加。
### 技術決定 Why
- **在庫評価は倉庫軸の2段解決にした**: `SummaryRealStock.Id_Soko` は倉庫(TenType=0)のことも直営店(TenType=6)のこともある。設計時の既定（倉庫軸は常に本部基準）だと直営店の在庫を本部価格で評価してしまうため、「店舗系の当該店舗 > 本部売上系の全件 > マスタ定価」の順で解決する `ResolveSokoSql` を用意した。倉庫の場合は店舗系の行が一致しないので自然に本部基準へ落ちる。
- **既存の集計値は変わらない**: 適用行が1件も無ければ `ifnull` で `MasterShohin.TankaJodai` を返すため、上代一括変更を使っていない環境では従来と同じ値になる。
- `StockSql.TankaJodai()` の引数を変えたのは、解決に在庫行の `Id_Shohin`/`Id_Soko` が必要になったため。既存呼び出し2箇所（`GeneralStockTableViewModel` / `SokoSummaryReportViewModel`）はどちらも既定の別名 `s`/`sh` なので呼び出し側の変更は不要。
- POS の `StoreId` は Order=2 の追加のみで、未指定(0)なら全店行だけが適用される。既存端末との後方互換を壊さない。
### 影響範囲
- **未対応（次フェーズ以降）**: 入力VM群（仕入/受注/発注/売上/在庫/移動/配分/棚卸）の `shohin.TankaJodai` 直読み約10箇所は差し替えていない。これらは商品選択ダイアログから受け取った `MasterShohin` を同期コマンド内で参照しており、解決には店舗/得意先・伝票日付を伴うサーバ問い合わせが必要でコマンドの非同期化を伴う。基幹の入力画面を広く触るため、独立した作業として分離した。
### 確認
- `vscmd.bat dotnet build creativevision10.slnx`: 成功（警告0、エラー0）。
- 一時検証（スクラッチ）: 直営店(501)の在庫は店舗系の2500、倉庫(999)の在庫は本部系全件の2900で解決。適用行を全削除するとどちらもマスタ定価2000へフォールバック。`TodaySql` が `20260811` を返すことも確認。

---

## [2026-08-11] 13:22 上代一括変更 対象店舗・対象明細の重複排除を追加
### Agent
- Opus 5 : Anthropic : Sekiya Sato Claude
### Editor
- Claude Code
### 目的
- ユーザーからの要望：UI側で重複排除の処理を追加する。
### 実施内容
- CvBase/BaseDbJodai.cs: `TranJodai.Normalize()`（重複除去・行No振り直し・件数列同期）、`TranJodai.FindDuplicates()`（利用者向け重複メッセージ）、内部ヘルパ `RemoveDuplicates` を追加。
### 技術決定 Why
- `DerivedJodai` の `uk1(Id_Tran, TaishoType, Id_Tenpo, Id_Shohin)` はユニークキーなので、`Jshop` に同じ店舗・`Jmeisai` に同じ商品が重複していると展開時に制約違反となり、`HandlerClass` のトランザクションごと失敗して伝票の保存自体が通らない。入力時点で取り除く。
- 重複時は**後の指定を残す**。期間重複を「後の伝票が勝つ」で解決するのと同じ考え方に揃え、最後に入力した価格が有効になるようにした（先勝ちだと後から直した価格が黙って捨てられる）。
- 展開SQL側での重複排除（`NOT EXISTS` による後勝ち抽出）も検討したが、明細500件×店舗200件で出力10万行×明細500件の相関評価となり展開時間が桁で悪化するため採用しない。入力時の `Normalize()` とユニークキーによる明示的失敗の二段構えとする。
- `FindDuplicates()` を分離したのは、黙って捨てる前に利用者へ確認を出せるようにするため。
### 確認
- `vscmd.bat dotnet build creativevision10.slnx`: 成功（警告0、エラー0）。
- 一時検証（スクラッチ）: 重複2件を検出しメッセージ生成、`Normalize()` で店舗3→2・明細2→1、残るのは後の指定（期間20260805・価格1200）、行No振り直しと `ShopCnt`/`MeisaiCnt` 同期を確認。正規化後は展開2行で成功。未正規化のまま展開すると `UNIQUE constraint failed: DerivedJodai...` で失敗し、部分挿入0行であることも確認。

---

## [2026-08-11] 13:11 上代一括変更のテーブル設計と定義追加
### Agent
- Opus 5 : Anthropic : Sekiya Sato Claude
### Editor
- Claude Code
### 目的
- ユーザーからの要望：旧システムの「上代一括変更」機能を cv10 に追加するにあたり、まずテーブル設計の計画を立て、確定した仕様でテーブル定義を作成する。
### 実施内容
- .omo/20260811_jodai_table_design_plan.md: 設計計画を新規作成（仕様の正）。現状分析・方針・テーブル定義・価格解決ロジック・規模見積もり・導入手順。
- CvBase/BaseDbJodai.cs: 新規作成。実テーブル `TranJodai` / `DerivedJodai`、サブクラス `TranJodaiCond` / `TranJodaiShop` / `TranJodaiMeisai`、Enum `EnumJodaiKubun` / `EnumJodaiTaisho`。`DerivedJodai` に展開SQL(`CreateSql`/`InsertSql`/`DeleteSql`)と解決SQL断片(`ResolveSql`/`FinalJodaiSql`)を実装。
- CvBase/BaseDb2Trans.cs: 上代の ToDo コメントブロックを削除（原価 `TranGenka` の枠は残置）。
- CvBase/DefineDataTable.cs: `tableTypes` に `TranJodai` / `DerivedJodai` を追加。上代の ToDo コメントを削除。初期データに名称区分 `SLE`(セール) と `SLE/0001` を追加。
- CvDomainLogic/JodaiDb.cs: 新規作成。`ResolveJodai` / `ResolveJodaiList` / `Rebuild` / `RebuildAll` / `PurgeExpired`。
- Doc/spec/spec.database.cvbase.md: テーブル一覧・件数サマリ・補足を更新。
### 技術決定 Why
- **商品マスタを書き換えないオーバーレイ方式**: `MasterShohin.TankaJodai` は定価として維持し、期間・対象つきの上書きレコードを `DerivedJodai` に積む。セール終了で自動的に元価格へ戻るため戻し忘れ事故が起きず、過去日の再計算も再現できる。プロパー(P)区分も `DayTo="99991231"` の無期限オーバーレイとして統一し、書き込み経路を1本に保つ。
- **物理テーブルは `TranJodai` のみ**: 対象店舗・対象明細は JSON 配列で保持。`List<>` 列には `[ColumnSizeDml(ColumnType.Json)]` を付ける（属性が無いと `ExDatabase.GetSqlColumns` が `continue` して MariaDB/Oracle で列が生成されない）。SQLite は `TEXT` でサイズ制限なし、MariaDB は `JSON` 型となり既存 `Jmeisai` の `varchar(4000)` 制限を回避できる。検索性は `DerivedJodai` 側（`Id_Tran` で伝票へ逆引き）で担保する。
- **展開は `IDerivedOrigin` に載せる**: `TranJodai` に `IDerivedOrigin` を実装し、既存の `HandlerDerived` が Insert/Update/Delete 時に同一トランザクション内で `DerivedJodai` を再展開・削除する（`DerivedShohinColSiz` と同じ仕組み）。専用の確定処理を作らずに整合性が保てる。
- **`DerivedJodai` に V*列を持たせない**: Derived 系に `CodeNameView` 列を置くと `MasterCascadeDb.VRules` への登録が必須になり（`MasterCascadeDbTests.VRules_CoverAllMasterVColumns` が検出）、数万行への伝播 UPDATE が発生する。Summary 系と同じく JOIN 前提とした。
- **対象系統 `TaishoType`**: 店舗用(TenType=6)と本部売上用(TenType=1/3)を分ける。`MasterTokui.Id` は TenType をまたいで一意だが、全件ワイルドカード `Id_Tenpo=0` の意味が系統ごとに変わるため列が必要。
- 価格粒度は商品マスタ単位（色・サイズ別価格を持たない）。展開行数が SKU 数の分だけ減り、キーと解決 SQL が単純になる。
- 新規テーブルのため `UpdateDb.versions` への追記は不要（`CREATE TABLE IF NOT EXISTS` で稼働中DBも起動時に自動作成される）。
### 影響範囲
- 既存テーブル・既存ロジックへの変更なし。`MasterShohin.TankaJodai` を直読みしている箇所（`StockSql.TankaJodai()`、`PointOfSaleService`、各入力VM）の解決API経由への差し替えは次フェーズ。該当行が無ければ従来どおりマスタの上代を返すため、差し替え後も既存動作は変わらない。
### 確認
- `vscmd.bat dotnet build creativevision10.slnx`: 成功（警告0、エラー0）。
- `TestServer.exe`: 35件すべて成功（`VRules_CoverAllMasterVColumns` を含む）。
- 一時検証（スクラッチ）: テーブル/インデックス生成、JSON往復（店舗200件・明細500件、`Jmeisai` 実長 128,285 byte）、展開100,000行（約1.0秒）、未確定伝票は展開0件、空文字JSONでも `json_valid()` ガードにより例外なし、優先順位（個別指定>全件、後の伝票が勝つ、期間外は全件行へフォールバック、該当なしはマスタ定価）7ケースすべて期待どおり。

---

## [2026-08-10] 16:09 SummaryRealStock範囲再集計の色・サイズ単位是正
### Agent
- GPT-5.6 : OpenAI : Sekiya Sato Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：`CalcSummaryRealStockRange` の対象を色・サイズ単位とする。
### 実施内容
- CvDomainLogic/SummaryDb.cs: `CalcSummaryRealStockRange` の `TargetKeys` 結合を倉庫・商品・色・サイズまで一致させ、指定月に存在する組だけを再作成するよう是正。
- Tests/TestServer/SummaryDbTests.cs: テスト名を色・サイズ単位の仕様に合わせ、対象月に存在しない別サイズは既存数量を維持することを確認するよう修正。
- Doc/aicoding_log.md: 調査・修正・確認内容を追記。
### 技術決定 Why
- `SummaryRealStock` の一意キーは倉庫・商品・色・サイズであり、範囲再集計も同じ粒度で行う。対象月に存在しない別サイズは更新対象に含めない。
### 確認
- `SummaryDbTests` を実行し、修正前の期待値が色単位の仕様になっていることを確認した。
- `TestServer.dll --filter FullyQualifiedName~SummaryDbTests`: 成功（2件、失敗0件）。
- `vscmd.bat dotnet build Tests/TestServer/TestServer.csproj --no-restore`: 成功（警告0、エラー0）。
- CRLF・`git diff --check`: 問題なし。

---

## [2026-08-10] 16:00 仕入返品入力の画面デザインをcv10共通規約へ是正
### Agent
- Opus 5 : Anthropic : Sekiya Sato Claude
### Editor
- Claude Code
### 目的
- ユーザーからの要望：画面デザインを cv10 のスタイルに合わせる。
### 実施内容
- CvWpfclient/Views/05Shiire/HenpinInputView.xaml: `behaviors:Interaction.Triggers` による `ContentRendered` → `InitCommand` 起動を削除（`wpf-project-guide` 違反。`BaseWindow.OnContentRendered` が既に `TryExecuteInitCommand()` を呼ぶため二重起動になり、マスタ取得が2回走っていた）。
- 同: 仕入日を `TextBox`(文字列)から `FormDatePicker` + `DatePickerTodayButtonBehavior` の `DatePicker` へ変更し、商品仕入入力(`ShiireInputView`)の計上日と揃えた。
- 同: 仕入先/倉庫/入力者の `StackPanel` + 固定幅 `Width="330"` を、`Grid`(`*` + `Auto`)へ変更。`ColumnDefinition` も `Auto/Auto` から `*` + `MinWidth` にし、ウィンドウ幅に追従させた（`check-xaml-layout` の「固定幅ハードコード」「StackPanel Horizontal に幅可変要素」対策）。
- 同: 合計欄の固定 `Width` を `MinWidth` 化。説明文・ステータス文に `StatusTextBlockStyle` と `TextTrimming` を付与。`<<` ボタンを素の `Button` から `BudgetActionButtonStyle` へ、取得件数上限を `NumericFormTextBox` へ差し替えた。
- 同: `FormLabel` と重複していた `HintAssist.Hint` を削除（備考のみ残す）。ラベルが二重表示されており、`ShiireInputView` の「左に FormLabel、コントロールに Hint 無し」の慣習に合わせた。
- CvWpfclient/ViewModels/05Shiire/HenpinInputViewModel.cs: 基底の `DenDayText`(文字列)を DatePicker へ見せる `DenDay` (DateTime?) プロパティを追加。
### 技術決定 Why
- `BaseWindow` が初期化・Escape・Cancel の既定動作を持つため、View 側で同じ導線を追加しない（`wpf-project-guide` の Window 規約）。今回はこの規約違反が実害（マスタ二重取得）になっていた。
- 幅は固定値ではなく `Auto`/`*` + `MinWidth` を優先し、右端見切れとテーマ/DPI差で破綻しないようにする（`check-xaml-layout` の修正方針）。
- 共通スタイルは `UIFormStyles.xaml` の既存キーのみを流用し、新規スタイルは追加していない。
### 確認
- `vscmdclaude.bat dotnet build creativevision10.slnx --nologo`: 成功（警告0、エラー0）。
- 実画面確認: 修正後の初期表示と、仕入先195/倉庫000990 での [在庫取得] 500件表示を再キャプチャして目視確認。ラベル重複の解消、コンボのウィンドウ幅追従、明細の全列表示（横スクロール無し）を確認した。確認用の一時フックは削除済み。
- CRLF・`git diff --check`: 問題なし。

---

## [2026-08-10] 15:50 仕入返品入力を在庫一覧方式へ全面変更
### Agent
- Opus 5 : Anthropic : Sekiya Sato Claude
### Editor
- Claude Code
### 目的
- ユーザーからの要望：`CvWpfclient.Views._05Shiire.HenpinInputView` の View / ViewModel を添付画面のとおり全面変更する。取引区分(20 仕入返品 固定)・仕入日(初期値 今日)・仕入先・倉庫・入力者(いずれも Id を選択し「コード 名称」表示)・備考を入力し、[在庫取得] で該当倉庫にある該当仕入先の商品(商品マスタのメーカーCD = 仕入先CD)を一覧表示、[実行] で仕入返品データ(区分20)を作成する。
### 実施内容
- CvWpfclient/ViewModels/05Shiire/HenpinInputViewModel.cs: `ShiireInputViewModel`(伝票明細方式)の継承をやめ、`BaseStockSheetInputViewModel<Tran03Shiire>`(一覧方式)の派生に全面書き換え。取引区分/仕入先/倉庫/入力者のコンボ選択、在庫取得SQL、`BuildDenpyo` による仕入返品伝票の組み立てを実装した。
- CvWpfclient/Views/05Shiire/HenpinInputView.xaml: 一覧/詳細の2タブ構成をやめ、ヘッダ入力＋[在庫取得(F5)]＋数量計/上代金額計/下代金額計＋明細DataGrid＋[実行(F8)]/[クリア]/[戻る(ESC)] の1画面構成へ全面書き換え。
- CvWpfclient/Helpers/ViewModels/BaseStockSheetInputViewModel.cs: `StockSheetRow.JodaiKingaku` / `GedaiKingaku`(入力数×単価)と、画面合計 `JodaiKingakuTotal` / `GedaiKingakuTotal` を追加(既存の棚卸入力・在庫移動入力に影響しない追加のみ)。
### 技術決定 Why
- 返品は「今その倉庫にある在庫を仕入先へ送り返す」作業で、対象SKUを先に全部並べて数量だけ直す一覧方式が実務に合う。棚卸入力(一覧方式)・在庫移動入力と同じ基底を使い、伝票登録・在庫取得ヘルパを再利用した。作成後の修正・削除は従来どおり【商品仕入入力】が担う(画面上にも明記)。
- 仕入先での商品絞り込みは、商品マスタのメーカー(`MasterShohin.Id_Maker` → `MasterMeisho.Kubun='MKR'`)のコードと仕入先コードの一致で行う。Id による関連が張られていないため、旧システム同様コード一致で突き合わせる。
- 数量は必ずプラスで登録する。`Tran03Shiire.Kubun=20` により `OnKubunChanged` が `CalcFlag=-1` を立て、在庫集計が `Su * CalcFlag * calcFlag` で減算するため、マイナス入力すると符号が二重反転して在庫が増えてしまう。マイナス行は登録前に弾き、在庫超過は確認ダイアログで警告する。
- 消費税・総合計は `ShiireInputViewModel.UpdateHeaderTotals` と同じ積み方(`|金額計| * 税率`)に揃え、税率は仕入日時点の `AppGlobal.LogicGetTax(1, 仕入日)` を使う。掛計上日は仕入日と同じにした。
- 在庫取得は `SummaryRealStock` を `Su > 0` で絞る。在庫の無いSKUは返品対象にならないため、SQL側で落として取得件数上限の枠を無駄にしない。
### 確認
- `vscmdclaude.bat dotnet build creativevision10.slnx --nologo`: 成功（警告0、エラー0）。
- 実画面確認: CvServer 起動後、MainMenu 経由で `HenpinInputView` を開き `PrintWindow` でキャプチャ。初期表示(仕入日=今日、区分=20 仕入返品)と、仕入先195/倉庫000990 選択時の [在庫取得] 500件表示(数量=在庫数の初期値、上代/下代金額、計 2,739 / ￥15,131,200 / ￥100,473)を目視確認した。確認用の一時フックは削除済み。
- XAMLリソースキー/バインディングパス照合: 未定義参照なし。CRLF・`git diff --check`: 問題なし。
- `TestServer.exe`: 35件中1件失敗。`SummaryDbTests.CalcSummaryRealStockRange_RebuildsOnlyTargetWarehouseProductColor` は単独実行でも失敗する **既存の失敗**(TestServer は CvWpfclient を参照しておらず本変更とは無関係)。
### 未実施
- [実行] による伝票登録の実データ検証は未実施。共有DB(server-user163.db)に仕入返品伝票が実際に作られ在庫集計が動くため、意図的に実行していない。

---

## [2026-08-10] 14:18 SummaryRealStock範囲再計算を追加
### Agent
- GPT-5 : OpenAI
### Editor
- Codex
### 目的
- ユーザーからの要望：指定年月範囲で再計算したSummaryStockに対応するSummaryRealStockだけを再計算する。
### 実施内容
- CvDomainLogic/SummaryDb.cs: `CalcSummaryRealStockRange(string DateFromYyyymm, string DateToYyyymm)` を追加。範囲内の `(Id_Soko, Id_Shohin, Id_Col)` を対象に、指定終了月までの全月データから全サイズの現在庫を再作成する。`SummaryAllAsyncStream` の月別集計完了後に実行するステップへ組み込んだ。
- Tests/TestServer/SummaryDbTests.cs: 対象キーの全サイズが開始月以前を含む累計に更新され、対象外キーが維持されるSQLite回帰テストを追加した。
### 技術決定 Why
- 期間内の差分だけを加減算すると再集計済みの月別データとの差異が残るおそれがあるため、対象キーを範囲で限定した上で、指定終了月までのSummaryStockから完全再集計する。削除と挿入は直列化トランザクションで一体化した。
### 確認
- `C:\gitroot\UT\vscmd.bat dotnet build CvDomainLogic\CvDomainLogic.csproj --no-restore`: 成功（警告0、エラー0）。
- `C:\gitroot\UT\vscmd.bat dotnet build Tests\TestServer\TestServer.csproj --no-restore`: 成功（警告0、エラー0）。
- `TestServer.exe --filter "Name=CalcSummaryRealStockRange_RebuildsOnlyTargetWarehouseProductColor"`: 成功（1件）。

---

## [2026-08-09] 12:19 長期gRPC通信設定と郵便番号APIトークン共有を改善
### Agent
- GPT-5 : OpenAI
### Editor
- Codex
### 目的
- ユーザーからの要望：共通gRPC通信の無期限設定を意図として明記し将来の有限値化に備え、郵便番号APIのトークンをリクエスト間で再利用する。
### 実施内容
- CvWpfclient/App.xaml.cs: 共有gRPCの接続アイドル寿命・接続寿命・HTTPタイムアウトを `GrpcTransportSettings` に集約。すべて無期限とする意図および有限値へ切替える変更点をコメントで明記した。
- CvServer/Services/SearchByPostalCodeService.cs: トークン、有効期限、排他制御を静的フィールドへ移動。RPCごとに生成されるサービスインスタンスをまたいでトークンを再利用し、同時要求時のトークン取得を1件に直列化した。
### 技術決定 Why
- 共通gRPCは常駐WPFクライアントでHTTP/2接続を維持する設計のため、無期限設定を維持する。一方で値を集約し、運用要件が変わった際は通信パイプラインを変更せず有限値へ変更できるようにした。
- 日本郵便APIのトークンキャッシュがサービスインスタンスに属していたため、gRPCリクエスト間で再利用されなかった。プロセス共有キャッシュと静的 `SemaphoreSlim` により、有効期限までの再利用と同時更新の重複防止を行う。
### 確認
- `git diff --check`: 成功。
- `C:\gitroot\UT\vscmd.bat dotnet build CvServer\CvServer.csproj --no-restore --no-dependencies -p:OutputPath=obj\CodexBuildOutput\`: 成功（警告0、エラー0）。
- `CvWpfclient` の同条件ビルドは今回と無関係の `StockKakeUpdateViewModel.cs` にある未定義 `CvFlag.Msg052_SummaryUriKake` / `CvFlag.Msg053_SummaryKaiKake` により失敗。

---

## [2026-08-09] 06:53 WeatherService長期稼働時の431応答を修正
### Agent
- GPT-5 : OpenAI
### Editor
- Codex
### 目的
- ユーザーからの要望：OpenWeather API 呼出しが数日後に 431 で失敗する原因を修正し、ログとコミットまで行う。
### 実施内容
- CvServer/Services/WeatherService.cs: 静的 HttpClient の生成処理をファクトリへ集約。User-Agent をサービス生成時ではなくクライアント初期化時に一度だけ設定し、PooledConnectionLifetime を15分に設定した。
- Doc/aicoding_log_011.md: 800行超過の既存作業ログをアーカイブした。
- Doc/aicoding_log.md: 今回の作業記録を追加した。
### 技術決定 Why
- 保存ログで OpenWeather API から 431 (Request Header Fields Too Large) を77件確認した。WeatherService のコンストラクタがリクエストごとに共有 HttpClient の DefaultRequestHeaders.UserAgent へ追加していたため、ヘッダーが無制限に肥大化していた。初期化時の一度だけの設定に変更して累積を防止した。加えて接続プールを15分で更新し、DNS変更にも追随させる。
### 確認
- `git diff --check`: 成功。
- `C:\gitroot\UT\vscmd.bat dotnet build CvServer\CvServer.csproj --no-restore --no-dependencies -p:OutputPath=C:\tmp\cv10-weather-build\CvServer\`: 成功（警告0、エラー0）。実行中の CvServer が通常出力先DLLをロックしているため、隔離出力先で検証した。

---
