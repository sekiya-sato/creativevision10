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
