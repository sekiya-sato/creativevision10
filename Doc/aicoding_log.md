## [2026-08-12] Phase 1 業務仕様の決定内容をドラフト化
### Agent
- GPT-5.6 : OpenAI : Sekiya Sato Codex
### Editor
- Codex
### 目的
- 機能完成度チェックリストで未決だった、消込・引当・掛更新・受注発注完了・納品予定日の業務方針を実装前に記録する。
### 実施内容
- 消込を、売上又は仕入伝票の `EndFlag` を消込済みに変更する伝票単位の処理と定義した。入金・支払伝票との対応付け、部分消込、FIFO充当は対象外とした。
- 引当数列を `SummaryRealStock` / `SummaryStock` に追加し、有効在庫を `Su - 引当数` とする方針を記録した。
- 売掛・買掛を即時更新せず、既存の夜間月次再集計により翌日整合させる方針を記録した。
- 発注・受注に `EndFlag` を追加し、納品予定日は配分日と別のリードタイム目安とする方針を記録した。
- 在庫調整の原票方式は、再集計・監査に対応する調整専用伝票案を推奨し、ユーザー承認待ちとした。
### 確認
- `SchedulerService.ExecuteMonthlyResummaryCoreAsync()` が在庫・売掛・買掛を前月、当月の順に処理する現行構造を確認。
- `SummaryRealStock` / `SummaryStock` の現行列と `SummaryDb` の通常更新・Rebuild経路を確認。
### 追加決定
- 在庫調整は調整専用伝票を新設し、調整伝票から `SummaryStock` / `SummaryRealStock` を再生成する方式で進めることを承認された。

---

## [2026-08-12] AGENTS.md の調査・検証最小化規約を追記
### Agent
- GPT-5.6 : OpenAI : Sekiya Sato Codex
### Editor
- Codex
### 目的
- 不要な生成物の参照、過大なログ出力、無関係な build/test を抑制する。
### 実施内容
- `bin/`、`obj/`、`generated/`、生成済み gRPC C# の原則非参照・非編集、必要ファイル限定、巨大ログの絞り込み、minimal diff、影響する最小プロジェクトでの build/test を AGENTS.md に追記した。
### 確認
- `git diff --check`: 成功。

---
## [2026-08-12] 17:07 自動実行履歴のエラー表示を強調
### Agent
- GPT-5.6 : OpenAI : Sekiya Sato Codex
### Editor
- Codex
### 目的
- 月次再集計に失敗したタスクをシステム管理者が自動実行履歴一覧で識別しやすくする。
### 実施内容
- 月次Schedulerの失敗経路を再確認した。各集計ストリームのエラーは `ExecuteMonthlyResummaryCoreAsync()` で `InternalError=9` と区分別 `NG` メモに集約され、`ExecuteWithAutoexecHistoryAsync()` の `finally` から `SysHistAutoexec.ReturnCode` / `Memo` / 終了日時等を更新する。
- `SysAutoExecHistoryView` の一覧で、`ReturnCode != 0` の行は Id列とタスク名列を `MaterialDesignValidationErrorBrush` で赤字表示し、`ReturnCode == 0` は通常色にした。
- 判定は `SysHistAutoexec` の既存契約「0=成功、0以外=エラー」を直接使い、ViewModel、モデル、Converterは変更していない。
### 影響範囲
- `CvWpfclient/Views/00System/SysAutoExecHistoryView.xaml` の一覧表示色のみ。履歴保存処理とDBスキーマは変更していない。
### 確認
- XAMLのXML構文、名前空間、`ReturnCode` / `Id` / `TaskName` バインディング、MaterialDesignリソースを確認。
- `CvWpfclient/CvWpfclient.csproj` ビルド: 成功（警告0、エラー0）。
- 独立レビュー承認。
### 残課題
- 通常のDB書込み可能な経路ではエラー履歴が保存される。履歴INSERT/UPDATE自体の失敗はサーバーログに残るが、履歴行の非0コード保存までは保証されない。プロセス強制終了も同様であり、必要時はサーバーログを確認する。
- `.omo` の完成度チェックリストとhandoffへ確認結果を反映したが、規約どおりコミット対象外。

---
## [2026-08-12] 17:05 P0 在庫即時更新と範囲再構築の整合性修正
### Agent
- GPT-5.6 : OpenAI : Sekiya Sato Codex
### Editor
- Codex
### 目的
- 現行業務規則と `GetCalcIdosaki()` に基づく在庫即時更新を完成させ、Tranからの範囲Rebuildを冪等かつ原子的にする。
### 実施内容
- `CalcTran2SummaryStock()` は実在庫を `Su` フラグが非0の場合だけ更新し、`SummaryStock` は在庫・入庫・出庫・積送中の4フラグのいずれかが非0なら更新するよう分離した。これにより `Tran10IdoOut` 移動先の積送中数量だけの更新も反映する。
- `SummaryAllAsyncStream()` の在庫再構築を単一ステップへ集約し、対象範囲の旧キー退避、対象月削除、6伝票種からの再生成、現在庫再構築を単一Serializableトランザクションで実行するようにした。途中例外は全体をrollbackする。
- 旧・新自然キーの和集合で `SummaryRealStock` を削除・再生成し、最後のTranが消えたキーを除去する。対象月より前の在庫がある場合は過去累計へ戻す。
- Tranから復元できない `CumulativeSu` / `AdjustQty` / `StocktakeDdate` / `ActualQty` は削除前に一時表へ退避し、再生成された同一自然キー行へ復元する。対象月から消滅した行は復活させない。
- `SummaryDbTests` を2件から12件へ拡張し、即時移動、積送出庫・移動受、仕入・返品、修正時の旧値反転、通常更新とRebuild一致、Rebuild冪等性、消滅キー、前月値復帰、範囲外保持、途中失敗rollback、非Tran列保持を確認した。
### 技術決定 Why
- 進捗runnerは各ステップ例外を捕捉して次へ進むため、範囲削除と伝票別再生成を別ステップにすると部分再構築が確定する。公開APIを変えず、同期処理1ステップ内でtransactionを完結させた。
- Rebuild対象月の行を単純DELETEすると棚卸・調整値が失われるため、伝票由来でない4列だけを退避・復元対象とした。
### 影響範囲
- `CvDomainLogic/SummaryDb.cs` の在庫即時集計と在庫範囲再構築。DBスキーマと公開gRPC APIは変更していない。
- `Tests/TestServer/SummaryDbTests.cs` のテスト資産。
### 確認
- `Tests/TestServer/TestServer.csproj` ビルド: 成功（警告0、エラー0）。
- `SummaryDbTests`: 12/12成功。`TestServer` 全体: 45/45成功。
- 実装担当、独立Tester、独立レビューを分離し、最終レビュー承認。
### 残課題
- 月次Schedulerは前月・当月を別呼出するため、前月成功後に当月が失敗すると現在庫が一時的に前月末値になる。各呼出の原子性は成立し、通常経路の失敗は自動実行履歴とサーバーログへ残るため、システム管理者が対応する運用とする。
- テストはHandlerが行う `Id_Soko` / `Id_Ido` の2呼出を再現したもので、実DBを使う画面からサービスまでのE2Eは未実施。
- `.omo` の完成度チェックリストとhandoffを最新状態へ更新したが、規約どおりコミット対象外。

---
## [2026-08-12] 16:27 最新ソースと現行リポジトリによるP0課題の再確認
### Agent
- GPT-5.6 : OpenAI : Sekiya Sato Codex
### Editor
- Codex
### 目的
- 作業対象を旧 `C:\gitroot\new2022\cv10-claude` worktree から現行 `C:\gitroot\new2022\cv10` へ切り替え、ユーザーが修正した最新HEAD `5399328` を基にP0状態を再判定する。
### 実施内容
- 両 `.omo` 文書の対象リポジトリ、Git実行方法、コミット名、HEAD説明を現在のCodex作業環境へ変更した。
- `CalcTran2SummaryStock()` が `idSoko == "Id_Ido"` の場合に `GetCalcIdosaki()` を選択することを確認し、移動先の増減方向修正を「コード修正済み」へ更新した。
- `SummaryStock` 更新が外側の `calcFlag.Item1 != 0` 内に残るため、`Tran10IdoOut` の移動先 `(0,0,0,1)` が `TransitQty` 更新前にスキップされることを確認した。
- `SummaryAllAsyncStream()` が対象月の `SummaryStock` を削除せず加算UPSERTする構造と、消滅キーの再構築方式が未設計である点は未解消と判定した。
- `SummaryDbTests` は既存2件のままで、移動3種、登録/修正/削除、通常更新=Rebuild、Rebuild冪等性のテストは未追加と確認した。
### 影響範囲
- 文書更新のみ。`CvDomainLogic/SummaryDb.cs`、テスト、DBスキーマ、公開API、WPF画面は変更していない。
### 確認
- `Tests/TestServer/TestServer.csproj` ビルド: 成功（警告0、エラー0）。
- `TestServer.dll --filter FullyQualifiedName~SummaryDbTests --minimum-expected-tests 2`: 2件成功。
### 残課題
- P0は部分完了。積送中のみの更新、Rebuild冪等性、Tran消滅キー、在庫不変条件の回帰テストが残る。
- L4候補8画面の確定はP0残作業と回帰テスト完了後に行う。

---
## [2026-08-12] 15:23 CV10機能完成度マトリクスの再確認と後続作業整理
### Agent
- GPT-5.6 : OpenAI : Sekiya Sato Codex
### Editor
- Codex
### 目的
- `.omo/2026-08-12_CV10機能完成度チェックリスト.md` と `.omo/20260812_completion_matrix_handoff.md` を現行 HEAD で再確認し、事実誤認を訂正して実行順序を整理する。
### 実施内容
- 1.0必須範囲の View/ViewModel を再集計し、129画面/L0 49画面を130画面/L0 50画面へ訂正。`07Haibun` は18画面、うち17画面がL0と確認した。
- 自動テスト件数を43から42へ訂正。内訳は `MasterCascadeDbTests` 23 / `TestServer` 10 / `TestLogin` 7 / `SummaryDbTests` 2。
- 現存しないと記載されていた `.omo/2026-07-31_kesikomi_design.md` を確認し、`TranKesikomi` 新設の推奨案が既存であることを両資料に反映。
- 引当は「未設計」ではなく、`TranHaibun` に受注配分・取置の部分設計がある一方、引当範囲と有効在庫算式が未決定と整理。
- L4=8は再分類前の暫定値とした。移動先の即時更新が `GetCalcIdosaki()` を使わず Rebuild と一致しない候補、および Tran からの Rebuild 再実行で二重加算する候補をP0作業に追加。
### 影響範囲
- 今回は調査資料と作業ログの更新のみ。業務計算コード、DBスキーマ、公開API、WPF画面は未変更。
### 確認
- `Tests/TestServer/TestServer.csproj` ビルド: 成功（警告0、エラー0）。
- `TestServer.dll --filter FullyQualifiedName~SummaryDbTests --minimum-expected-tests 2`: 2件成功。
- 現行 HEAD `54e5903` と引継ぎ資料の調査元 HEAD が一致し、中間のコード変更はないことを確認。
### 残課題
- P0の在庫集計修正は業務計算と既存データに影響するため、計画承認後に実装担当と独立Testerを分けて行う。
- 仕様判断6件は、既存推奨案あり（消込・在庫調整）、部分設計あり（引当/配分/取置・納品予定）、追加設計が必要（掛更新・完了FLG）の3群で個別承認が必要。

---
## [2026-08-12] 13:35 新メニュー構成への再編とログインロール(Group)の設定対応
### Agent
- Opus 5 : Anthropic : Sekiya Sato Claude
### Editor
- Claude Code
### 目的
- ユーザーからの要望：`.omo/2026-08-新メニュー案.md` をもとにメニュー構造を変更する。あわせて SysLogin の Group を 1=店舗 / 2=倉庫担当 とするロール設定を定義し、`CvWpfclient.Views._00System.SysLoginView` で Group を選択できるようにする。
### 実施内容
- CvBase/Share/BaseEnumClass.cs: `EnumLoginRole`（Standard=0 / Shop=1 / Warehouse=2）を追加。SysLogin.Id_Role に対応する。
- CodeShare/ILogin.cs: `LoginReply.Role`（long, `DataMember(Order = 5)`）を追加。ログイン結果でクライアントへロールを返す。
- CvServer/Services/LoginService.cs: `LoginAsync` で `loginData.Id_Role` を、`LoginRefreshAsync` で SysLogin 再取得時のロールを `LoginReply.Role` に設定。
- CvWpfclient/AppGlobal.cs: `CurrentRole` と `ToLoginRole(long)` を追加。未定義値は標準として扱う。
- CvWpfclient/Helpers/Converters/LoginRoleDisplayConverter.cs: 新規。Id_Role を「0:標準 / 1:店舗 / 2:倉庫担当」へ変換する。App.xaml へ登録。
- CvWpfclient/ViewModels/00System/SysLoginViewModel.cs: `RoleOption` レコードと `RoleOptions` を追加。
- CvWpfclient/Views/00System/SysLoginView.xaml: Group を `IsEnabled=False` の読取専用 TextBox から ComboBox へ変更（既存の `TranTokuiPromotionMenteView` と同じ `DisplayMemberPath` / `SelectedValuePath` 方式）。一覧の「グループId」列はコンバータでロール名を表示する「Group」列にした。
- CvWpfclient/Models/MenuData.cs: `AllowedRoles` と `CreateDefault(EnumLoginRole)` / `FilterByRole` を追加し、メニュー定義を新メニュー案の3階層構成へ全面再編。
- CvWpfclient/ViewModels/MainMenuViewModel.cs: `Init` を `CreateDefault(AppGlobal.CurrentRole)` に変更し、`afterLogin` から `UpdateMenuForRole` でロール別メニューを作り直すようにした。
### 技術決定 Why
- **ロールはJWTのRoleクレームではなく `LoginReply.Role` で渡す**。既存のRoleクレームは `Id_Role != 0 ? Id_Role : Id_Shain` で、ロール未設定時に社員Idが入る。社員Id 1/2 を店舗/倉庫担当と誤認するため、ロール判定には使えない。既存クレームは他で未使用のため変更していない。
- **ロール別メニューは標準業務メニューを削らないショートカット**とした。新メニュー案 5.8 が「店舗Role向けショートカットへ変更」「機能は削除しない」としているため。利用可否の制御は Permission の実装課題として残す。
- **旧「■ 店舗」の原価無し帳票（`Views._40Shop.*`）は「分析 > 店舗配布版(原価無)」にも配置**した。ロール別メニューだけに置くと標準ロールから到達できなくなるため。
- **新メニュー案に無い既存メニューは削除せず、最も近い小分類へ配置**した（配分関連メンテナンス、在庫強制調整入力、一時処理用、セット売上分析表、オンラインモニタ、HHT配下の各明細書印刷など）。「得意先別売上推移表」は同種の帳票がある「分析 > 卸・販売員・経営分析」へ移した。
- **外部連携に POS 小分類は作っていない**。新メニュー案には「POS連携関連設定・処理」があるが、該当するViewが未作成のため。POS日別精算入力と売上金種Viewerは従来どおり「売上」配下に残した。
- **倉庫業務メニューの内容は暫定**。新メニュー案に倉庫担当の一覧が無いため、在庫照会・移動・出荷の既存機能から構成し、その旨をコードにコメントで明記した。
### 影響範囲
- メニューの表示構成が全面的に変わる。View / ViewModel / namespace の移動はしていないため、各画面の実装には影響しない。
- `LoginReply` にフィールドを追加したので、サーバとクライアントは同時に更新する必要がある（protobuf-net の Order 追加のため旧クライアントからの呼び出しは壊れない）。
- SysLoginView で Group が編集可能になる。既存レコードの Id_Role は 0 のままなので、設定するまで全ユーザーが標準ロールとして動作する。
### 確認
- `dotnet build creativevision10.slnx`（VsDevCmd 経由、cv10-claude ワークツリー）: 成功（警告0、エラー0）。
- 実行時確認: CvServer と CvWpfclient を起動し、メインメニューのTreeViewが「■ マスター > 基本設定 > システム管理マスタ」の3階層で正しく描画され、未ログイン（標準ロール）ではロール別メニューが表示されないことをウィンドウキャプチャで確認した。確認後、起動したプロセスは停止済み。
- `git diff --check` クリーン、変更・新規ファイルは UTF-8 / CRLF。
### 残課題
- SysLoginView の Group ComboBox と一覧のGroup列は、ビルドと既存実装パターンの一致までの確認。ログインを伴う実画面での表示・保存は未確認。
- ロール2（倉庫担当）でログインしたときのメニュー切替は実機未確認。
- 新メニュー案 6章の MenuDefinition（BusinessArea / Capability / Availability / RequiredPermission）は未実装。今回は AllowedRoles のみ導入した。
- TreeViewItem のテンプレートに展開トグルが無く全階層が常時展開されるため、3階層化でメニューが縦に長くなった。折りたたみ対応は別途検討が必要。
- 新メニュー案の「保守ツール」の Support / Developer 権限分離は Permission 未実装のため見送った。

---
## [2026-08-12] 09:15 上代一括変更 商品バーコードブックの印字価格を適用上代へ差し替え
### Agent
- Opus 5 : Anthropic : Sekiya Sato Claude
### Editor
- Claude Code
### 目的
- ユーザーからの要望：上代一括変更の残作業（各入力画面の上代直読みを適用上代の解決経路へ差し替える）を再開し、順次実施する。本ログは最後の作業7（商品バーコードブック）。印字価格の扱いは業務判断項目だったためユーザーへ確認し、「適用上代に差し替え」と決定した。
### 実施内容
- CvWpfclient/ViewModels/01Master/MasterPrintBarcodeViewModel.cs: 印字価格のSQL断片 `JodaiPrintSql` を追加し、SKU出力・商品出力の両SQL（2箇所）の `S.TankaJodaiOrg 元上代,` を `{JodaiPrintSql} 元上代,` へ差し替え。
- Doc/aicoding_log.md: 本日の上代一括変更4件（在庫照会／店舗配分／棚卸系一覧／入力VM群）の見出し時刻を、実際のコミット時刻（08:36 / 08:34 / 08:31 / 08:28）へ訂正した。
### 技術決定 Why
- **`FinalJodaiSql` ではなく `ResolveSql` を直接 `ifnull` で包んだ**。`FinalJodaiSql` のフォールバックは `TankaJodai`（上代）だが、この画面が従来印字していたのは `TankaJodaiOrg`（元上代）で、両者は実データで別値（例：元上代2800 / 上代1960）。`FinalJodaiSql` をそのまま使うと**適用上代が1件も無い環境でも印字価格が変わってしまう**ため、`ifnull(ResolveSql(...), ifnull(S.TankaJodaiOrg,0))` として従来値へ落とす形にした。
- **対象軸は店舗系の全件行（`EnumJodaiTaisho.Tenpo` / `Id_Tenpo=0`）**。値札・棚札は直営店で使うので系統は店舗用が正しい。ただしこの画面の抽出条件は展示会・ブランド・商品CD/商品名だけで店舗を指定できないため、**店舗ごとに価格を変えた分は反映されない**。店舗別の値札が必要になった時点で画面へ店舗指定を足すこと（コード内にも明記）。
- 判定日は今日（`DerivedJodai.TodaySql`）。印刷時点で有効な価格を刷るため。
- `ReplaceServerSqlQuery()` が書き換えるのは `__serverdate__` / `__serverimg__` / `__serverimgshain__` のトークンだけなので、追加した `strftime` はそのままSQLiteへ渡る（同メソッド自身も `strftime` を生成している）。
### 影響範囲
- 商品バーコードブックの印字価格（SKU出力・商品出力の両方、`元上代` 列）。帳票定義 `printform/*.qfm` は列名・列数とも変えていないため改修不要。
- 適用上代が無ければ従来どおり `TankaJodaiOrg` を印字するため、上代一括変更を使っていない環境では出力が一切変わらない。
- これで引き継ぎ資料の作業1〜7がすべて完了し、生の商品マスタ上代を直読みしている入力経路は残っていない。
### 確認
- `vscmdclaude.bat dotnet build creativevision10.slnx`: 成功（警告0、エラー0）。※作業中に .NET SDK が 10.0.302 → 10.0.400 へ更新され一時的にビルド不可となったため、復旧後に再ビルドして確認した。
- `TestServer.exe`: 35件すべて成功。
- SQL検証（cv-sqlite MCP・読み取り専用）: `DerivedJodai` を CTE で差し替えて印字価格の式を実行し、適用行がある商品は適用価格(12345)、無い商品は `TankaJodaiOrg`(4200/3800/4700) が返ることを確認。
- SQL検証（型親和性）: 実テーブル（`NUMBER` 宣言＝NUMERIC親和性）に対し `Id_Soko = @p` / `Id_Soko IN (@p, 0)` を**文字列パラメータと整数パラメータの両方**で実行し、件数が一致すること（26件 / 4件）を確認。クライアントが全パラメータを文字列で送る現行方式のままで数値列の比較が成立する。
- CvWpfclient 全体を `TankaJodai` で走査し、残る参照が「解決経路を通ったオブジェクト」「表示バインディング」「意図的な対象外（商品マスタメンテ／上代一括変更画面自身／`StockSql.TankaJodaiMaster`）」だけであることを確認。
- `git diff --check` クリーン、変更ファイルは CR+LF。
### 残課題
- 本日先行4件の**コミットメッセージ内**の「作業時間」行は誤った時刻のまま（ログ本文のみ訂正済み）。既にmasterへ統合済みで履歴の書き換えが必要なため未修正。

---
## [2026-08-12] 09:06 大規模設計案件用エキスパート引継ぎ規約の新設
### Agent
- GPT-5.6 : OpenAI : Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：将来の設計起点の大規模タスクだけに使う `handoff-expert.md` を作成する。
### 実施内容
- handoff-expert.md: Sato、CV10 Manager、Architect、C# Developer、AI Expert、DB Expert、Reviewer、Tester、Git Commitの主鎖、役割、ゲート、専門引継ぎ、モデル割当、共有ワークスペース規約を定義。
- handoff.md: 通常の引継ぎ規約ではなく、明示された大規模設計案件だけで `handoff-expert.md` を使う参照を追加。
### 技術決定 Why
- 大規模案件では設計・データ・業務受入の独立確認が必要だが、通常作業に同じ負荷を持ち込まないため、通常規約から分離した。Git Commitは既存規約どおりSatoの明示承認をゲートとした。
### 確認
- Markdownの構造と通常規約からの参照を確認。対象3ファイルは CRLF、`git diff --check` は成功。

---
## [2026-08-12] 09:06 複数エージェント規約へのTesterとモデル選定の追加
### Agent
- GPT-5.6 : OpenAI : Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：直近コミットの調査結果を反映し、軽微変更の単一エージェント実行、Testerの実施条件、GPT-5.6モデル別の推奨を `handoff.md` に追加する。
### 実施内容
- handoff.md: 軽微変更の判定と単一エージェントの必須化、Testerの責務・必須条件・引継ぎ内容、Testerとレビューの並行確認フローを追加。
- handoff.md: Sol、Terra、Lunaの公式分類を基に、CV10の役割別推奨モデルとreasoning初期値、Lunaの利用境界を追加。
### 技術決定 Why
- 直近27コミットでテストファイルの変更は3件であり、`SummaryRealStock`ではサイズキー漏れを後続修正・テスト補強した。高リスク変更には独立した受入確認を設け、軽微変更には役割分担の負荷を持ち込まない形とした。
### 確認
- Markdownの構造と追加条件を確認。対象2ファイルは CRLF、`git diff --check` は成功。

---
## [2026-08-12] 08:40 エージェント規約と複数エージェント引継ぎ規約の再構成
### Agent
- GPT-5.6 : OpenAI : Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：`AGENTS.md` の不要な重複を整理・統合し、CV10向けの複数エージェント用 `handoff.md` を再構成する。
### 実施内容
- AGENTS.md: 役割名の固定、常時stash、重複した手順を廃止し、編集安全性、仕様変更時の承認、文字コード、CV10不変条件、検証、Git規約へ統合。`handoff.md` の参照を追加。
- handoff.md: 共有ワークスペースのファイル所有権、調整・調査・実装・レビューの責務、承認ゲート、引継ぎ様式、設計変更要求、CV10固有検証、Git統合責務を定義。
### 技術決定 Why
- 同一ワークスペースでの複数エージェント作業では、役割説明だけでは競合を防げないため、同時編集禁止・所有権・統合担当を明文化した。既存のCV10固有規約は `AGENTS.md` に集約し、詳細な引継ぎ手順を `handoff.md` へ分離した。
### 確認
- Markdownの構造と相互参照を確認。対象3ファイルは CRLF、`git diff --check` は成功。

---
## [2026-08-12] 08:36 上代一括変更 在庫照会の上代解決差し替え(本部基準)
### Agent
- Opus 5 : Anthropic : Sekiya Sato Claude
### Editor
- Claude Code
### 目的
- ユーザーからの要望：上代一括変更の残作業を順次実施する（本ログは作業6：在庫照会）。
### 実施内容
- CvWpfclient/ViewModels/08Zaiko/ZaikoQueryViewModel.cs: `LoadShohinListAsync()` の `M.TankaJodai` を `DerivedJodai.FinalJodaiSql("M.Id", 本部売上用, "0", 今日, "M")` へ差し替え。
### 技術決定 Why
- **本部基準(`Id_Tenpo=0`)で解決する**。倉庫を `SokoCodeFrom` / `SokoCodeTo` のコード範囲で絞る画面のため単一の倉庫Idが取れない。また評価金額を表示する画面なので、在庫評価SQL(`StockSql.TankaJodai`)と軸が食い違わないよう本部基準へ揃えた。
- 判定日は今日（`DerivedJodai.TodaySql`）。在庫照会は現時点の評価を見る画面のため。
- 行クラスの `TankaJodai` はSQL側で解決済みの値が入るため変更していない。
### 影響範囲
- 在庫照会の「上代」列と評価金額。
- 適用上代が無ければ `ifnull` で `MasterShohin.TankaJodai` に落ちるため既存環境では値が変わらない。
### 確認
- `vscmdclaude.bat dotnet build creativevision10.slnx`: 成功（警告0、エラー0）。
- `TestServer.exe`: 35件すべて成功。
- `git diff --check` クリーン、変更ファイルは CR+LF。
- CvWpfclient 全体を `TankaJodai` で走査し、残る参照が「解決経路を通ったオブジェクト」「表示バインディング」「意図的な対象外（商品マスタメンテ／上代一括変更画面自身／`StockSql.TankaJodaiMaster`）」だけであることを確認。生の商品マスタ直読みは残っていない。
- 残作業は作業7（商品バーコードブックの印字価格）のみ。業務判断待ち。

---
## [2026-08-12] 08:34 上代一括変更 店舗配分の上代解決差し替え(店舗軸・配分先店舗別)
### Agent
- Opus 5 : Anthropic : Sekiya Sato Claude
### Editor
- Claude Code
### 目的
- ユーザーからの要望：上代一括変更の残作業を順次実施する（本ログは作業5：店舗配分）。
### 実施内容
- CvWpfclient/ViewModels/07Haibun/ShopHaibunInputViewModel.cs:
  - `LoadShohinListAsync()` の `M.TankaJodai` を `DerivedJodai.FinalJodaiSql("M.Id", 店舗系, "0", 指示日, "M")` へ差し替え（一覧の代表値）。
  - `LoadJodaiByTenpoAsync()` を追加。配分先店舗Idの集合に対し「店舗Id → 適用上代」の対応表を1本のクエリで作る。
  - `BuildNewRecords()` を `BuildNewRecords(IReadOnlyDictionary<long,int> jodaiByTenpo)` に変更し、`Tanka` / `Kingaku` / `Jodai` を配分先店舗別の適用上代で組み立てる。該当が無い店舗は一覧と同じ代表値へ落とす。
  - `DoRegister()` で `BuildNewRecords()` の前に対応表を取得する（同メソッドは非同期なので追加の非同期化は不要）。
  - 判定日の式を作る `JodaiDayExpr()` と対象系統の定数 `TenpoTaishoExpr` を追加。
### 技術決定 Why
- **一覧・登録とも店舗系(`EnumJodaiTaisho.Tenpo`)で統一した**（引き継ぎ資料10章の判断項目3）。店舗配分の配分先は必ず直営店(TenType=6)であり、本部売上用(TenType in (1,3)＝卸先・売仕店)の行を引くのは系統として誤りになるため。本部基準へ寄せる案は採らなかった。
- **一覧は店舗系の全件行(`Id_Tenpo=0`)を代表値として表示する**。一覧の時点では配分先店舗が決まっておらず、明細ごとに配分先が違うため商品1件に価格を決め打ちできない。
- **登録は配分先店舗別に引き直す**。`BuildNewRecords()` は同期メソッドなので、非同期化せずに呼び出し前へ対応表の取得を挟む形にした。
- **判定日は配分指示日(`ShijiDay`)**。未入力なら今日へ落とす（`DoRegister` は事前に未入力を弾いている）。
- 返す型は `MasterShohin` のまま（`Id` に店舗Idを載せる）。サーバは `QueryListSqlParam.ItemType` で型を解決するためクライアント独自のPOCOは使えない。
### 影響範囲
- 店舗配分の一覧「上代」、タブ2の `TargetJodai` 表示、登録される `TranHaibun` の `Tanka` / `Kingaku` / `Jodai`。
- 適用上代が無ければ `ifnull` で `MasterShohin.TankaJodai` に落ちるため既存環境では値が変わらない。
### 確認
- `vscmdclaude.bat dotnet build creativevision10.slnx`: 成功（警告0、エラー0）。
- `TestServer.exe`: 35件すべて成功。
- `git diff --check` クリーン、変更ファイルは CR+LF。
- **要確認（業務判断）**: 店舗配分の価格軸を店舗系で統一した点。本部基準へ寄せる運用であれば差し戻しが必要。

---
## [2026-08-12] 08:31 上代一括変更 棚卸系一覧の上代解決差し替え(倉庫軸)
### Agent
- Opus 5 : Anthropic : Sekiya Sato Claude
### Editor
- Claude Code
### 目的
- ユーザーからの要望：上代一括変更の残作業を順次実施する（本ログは作業4：棚卸系一覧）。
### 実施内容
- CvWpfclient/Helpers/ViewModels/BaseStockSheetInputViewModel.cs: `LoadShohinMapAsync()` のSQLへ別名 `M` を付け、`M.TankaJodai` を `DerivedJodai.FinalJodaiSokoSql("M.Id", <IdSoko>, <棚卸日>, "M")` へ差し替え。
- CvWpfclient/Helpers/ViewModels/BaseQueryViewModel.cs: 警告ダイアログを出さない `TryParseDateQuiet()` を追加し、既存の `TryParseDate()` をその上に組み直した（振る舞いは同じ）。
### 技術決定 Why
- **倉庫軸(`FinalJodaiSokoSql`)を使う**。棚卸・移動・返品の `Id_Soko` は倉庫(TenType=0)のことも直営店(TenType=6)のこともあるため、「店舗系の当該店舗 > 本部売上系の全件 > マスタ定価」の順で解決する。在庫評価SQL(`StockSql.TankaJodai`)と同じ軸なので評価金額が食い違わない。
- **判定日は棚卸日(`DenDayText`)**。`OnSearchAsync` が検索前に `TryParseDate` で検証済みなので通常は必ず解釈できるが、データ取得中に警告ダイアログが出るのは不適切なため `TryParseDateQuiet` で静かに解釈し、不正なら今日へ落とす。
- 行クラスの `TankaJodai` / `JodaiKingaku` / `Tran99Meisai.Jodai` 生成箇所はSQL側で解決済みの値が入るため変更していない。
### 影響範囲
- 棚卸入力一覧(StockInputListViewModel)、在庫移動入力(StockIdoInputViewModel)、返品入力(HenpinInputViewModel)の上代・上代金額。
- 適用上代が無ければ `ifnull` で `MasterShohin.TankaJodai` に落ちるため既存環境では値が変わらない。
### 確認
- `vscmdclaude.bat dotnet build creativevision10.slnx`: 成功（警告0、エラー0）。
- `TestServer.exe`: 35件すべて成功。
- `git diff --check` クリーン、変更ファイルは CR+LF。

---
## [2026-08-12] 08:28 上代一括変更 入力VM群の上代解決差し替え(商品選択・バーコード入力ダイアログ)
### Agent
- Opus 5 : Anthropic : Sekiya Sato Claude
### Editor
- Claude Code
### 目的
- ユーザーからの要望：上代一括変更の残作業（各入力画面が商品マスタの上代を直読みしている箇所を、適用上代の解決経路へ差し替える）を再開する。本ログは商品選択ダイアログとバーコード入力ダイアログの経路（作業1〜3）。
### 実施内容
- CvWpfclient/ViewModels/Sub/SelectShohinViewModel.cs: 解決コンテキストの `JodaiTaishoType` / `JodaiTenpoId` / `JodaiDay` を追加。`LoadShohinListAsync` の後段に `OverwriteJodaiAsync()` を足し、`DerivedJodai.FinalJodaiSql` で解決した値で `MasterShohin.TankaJodai` を上書きする。
- CvWpfclient/ViewModels/Sub/InputBarcodeViewModel.cs: 同じ3プロパティを追加。`LoadShohinAsync` で取得した商品を `shohinCache` へ格納する**前**に `OverwriteJodaiAsync()` で上書きする。生SQLを流すための `QuerySqlListAsync<T>()` / `AddParameter()` を追加。
- 入力VM 7本の `ShowShohinSelectDialog()` と `DoInputBarcode()` に対象軸の設定を追加：
  - ViewModels/06Uriage/ShopUriageInputViewModel.cs（店舗用 / `CurrentEdit.Id_Tenpo`）
  - ViewModels/06Uriage/ShukkaUriageInputViewModel.cs、ViewModels/04Juchu/JuchuInputViewModel.cs（本部売上用 / `CurrentEdit.Id_Tokui`）
  - ViewModels/03Hatchu/HachuInputViewModel.cs、ViewModels/05Shiire/ShiireInputViewModel.cs、Helpers/ViewModels/BaseIdoInputViewModel.cs、ViewModels/08Zaiko/StockInputViewModel.cs（本部売上用 / 全件行 `Id_Tenpo=0`）
### 技術決定 Why
- **商品選択ダイアログは列追加ではなく後段上書き**。一覧SQLが `SELECT M.*` なので `AS TankaJodai` を足すと同名列が2つになり NPoco のマッピングが不定になる。読み込み後にIdリストで1本だけ追加クエリを流して差し替える方式にした。
- **バーコード側はキャッシュ格納前に上書きする**。`shohinCache` に定価が入ると以降のスキャンで解決値が反映されない。1件取得が `QueryByIdParam` で生SQLを書けないため、上書きだけ `QueryListSqlParam` の別クエリで引く。
- **対象軸の決め方**。直営店ではない卸先・倉庫軸は `TaishoType=Honbu`。得意先が特定できる伝票（本部売上・受注）はその得意先Idを渡し、特定できないもの（仕入・発注・移動・在庫）は `Id_Tenpo=0`（本部基準の全件行）を渡す。判定日は伝票日付 `CurrentEdit.DenDay`。
- **返す型は `MasterShohin` のまま**。サーバは `QueryListSqlParam.ItemType` で型を解決するため、クライアント独自のPOCOでは型解決に失敗する。
- **数値は文字列パラメータで渡してよい**。SQLite の型親和性で INTEGER 列との比較時に数値へ変換される。既存の `BaseStockSheetInputViewModel.cs:355`（`s.Id_Soko = @n`）に前例がある。
- **仕入・発注・移動の `Tanka` は原価のまま**変更していない。上代は `Jodai` 列の参考値としてのみ使う。
### 影響範囲
- 適用上代（`DerivedJodai`）に該当行が無ければ `ifnull` で `MasterShohin.TankaJodai` に落ちるため、**上代一括変更を使っていない環境では値が一切変わらない**。
- 商品選択ダイアログの一覧「上代」列にも適用価格が表示されるようになる。
- 残作業：棚卸系一覧（作業4）、店舗配分（作業5）、在庫照会（作業6）、商品バーコードブック（作業7・要業務判断）。
### 確認
- `vscmdclaude.bat dotnet build creativevision10.slnx`: 成功（警告0、エラー0）。
- `TestServer.exe`: 35件すべて成功。
- 変更ファイルが CR+LF であること、`git diff --check` がクリーンであることを確認。

---
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
