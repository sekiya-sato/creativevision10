## [2026-08-25] macOS ZIP内日本語ファイル名正規化スキルの追加

### Agent
- Sekiya Sato Codex

### 目的
- macOSで圧縮されたZIP内の分解形式の日本語ファイル名を、Windows 11で扱いやすいUnicode NFC形式へ変換する手順をスキル化する。

### 実施内容
- `.agents/skills/normalize-macos-zip-filenames/SKILL.md` に適用条件、安全な出力モード、完了条件、対象外を記録した。
- `scripts/normalize-zip-filenames.ps1` を追加し、ZIP内エントリ名のNFC正規化、重複検出、一時ZIP生成、エントリ数・展開後SHA-256検証、明示時のみの元ZIP置換を実装した。
- `agents/openai.yaml` にスキル表示名と説明を設定した。

### 検証
- NFD形式の日本語エントリを含むテストZIPで、別ZIP出力を確認（RenamedEntryCount=1、SHA-256 OK）。
- 同テストZIPで `-ReplaceInput` を確認し、NFC名への変換と一時ファイル残存なしを確認した。
- `quick_validate.py` は同梱Pythonに `PyYAML` がないため実行できなかった。

## [2026-08-25] PostgreSQL用DBプロバイダーの追加

### Agent
- Sekiya Sato Codex

### 目的
- SQLite、MariaDB、Oracle用プロジェクトと同じレイヤーに `CvBasePostgre` を追加し、NpgsqlおよびPostgreSQL固有処理をプロバイダー内へ隔離する。

### 実施内容
- `CvBasePostgre` を追加し、接続生成、Open/Close、Clone、タイムアウト、DDL型変換、テーブル存在確認、コメント、テーブル件数、診断SQLをPostgreSQL向けに実装した。
- NPocoが引用する識別子を小文字へ統一し、既存の非引用SQLがPostgreSQLで小文字化される規則と物理名を一致させた。
- `CvBase.ExDatabase` にDB種別指定コンストラクターとPostgreSQL側で必要な仮想拡張点を追加した。既存プロバイダーの既定動作は維持した。
- `CvBasePostgre/ExDatabasePostgre.cs` に、残存するSQLite固有SQLと将来のSQL方言抽象化方針をコメントとして記録した。
- `Directory.Packages.props` に Npgsql 10.0.3、`creativevision10.slnx` と `readme.md` に新規プロジェクトを追加した。

### 検証
- `dotnet build CvBasePostgre/CvBasePostgre.csproj --no-restore` 成功（警告0・エラー0）。
- `dotnet build creativevision10.slnx --no-restore` 成功（警告0・エラー0）。
- `dotnet run --project Tests/TestServer/TestServer.csproj --no-build --no-restore` 成功（212件、失敗0、スキップ0）。
- PostgreSQL実サーバーがローカル環境にないため、実接続によるDDL/CRUD検証は未実施。

## [2026-08-24] HHTデータ更新（TranVulcanHht → Tran系）の実装

### Agent
- Opus 5 : Anthropic : Claude Code

### Editor
- Claude Code

### 目的
- `HhtManualDataReceiveView` が取り込んだ `TranVulcanHht`（VULCANデータレイアウトの一次取込）を業務トランザクション（`Tran00Uriage` ほか）へ展開する「HHTデータ更新」を実装する。
- 変換エラーになったデータを確認・修正する「HHTエラーデータ修正入力」を実装する。
- 詳細設計は `Doc/spec/2026-08-24_HHTデータ更新詳細設計.md`。本設計外の課題は `Doc/spec/2026-08-24_Rate列_掛率と税率の分離課題.md` へ分離した。

### 事前調査で判明した事実
- `TranVulcanHht` には `VdCnvDate` / `TargetTableName` / `TargetId` / `ErrorMsg` が既にあり、**DDL変更は不要**だった。
- `CvDomainLogic/HhtProcessTransfer.cs` の `TransferValcan2Hhtdata()`（→`TranHhtData`）はどこからも呼ばれていない。CV10の正規経路は `TranVulcanHht` から Tran系へ直接展開する方式とし、既存メソッドは位置づけコメントだけ追記して残した。
- 実データ（`server-user163.db`、cv-sqlite MCPで確認）の `TranVulcanHht` は181件すべて棚卸(Type0=7)・単一日・単一店舗で、`Shop`=8002 と全JANが商品マスタと別採番空間だった。既存181件は**全件エラーになるのが正しい状態**とし、正常系は実マスタの値で作ったテストデータで検証する方針にした（ユーザー判断）。
- 実データに**同一ファイルの4重受信**があった（`HhtNo`・`Serial`・棚番・件数まで一致）。重複を許すと在庫・実棚数が4倍になるため、重複検知（E016）を仕様に追加した。
- `DerivedShohinColSiz.Jan1` にサイズCD（"24" "25" など2桁）の誤登録が1,285行あり、JAN照合に桁数フィルタ（8桁以上）が必須だった。
- `MasterTokui` に `"16"`(TenType=0/IsZaiko=0) と `"000016"`(TenType=6/IsZaiko=1) が同名で併存し、前0除去だけではコードが一意に決まらない。優先順位（完全一致→IsZaiko=1→TenType=6→E014）を定めた。
- `MasterEndCustomer` が155万件あり全件メモリロード不可。バッチ内に出現したキーだけを IN句で分割検索する方式にした。
- `Tran00Uriage.Rate` ほかは列コメントが「掛率」だが**入力画面は消費税率として使っていた**（`Tran13Hachu` だけが税率をローカル変数に分離済み）。ユーザー方針により `Rate` は掛率として使い、税率は `MasterSysman` 由来のローカル値で `Tax`/`Total` を計算する形にした。掛率の単位は `MasterTokui.RateProper`（実データは 100 / 60）に合わせたパーセント整数。

### 実施内容
- `CvDomainLogic/HhtProcessUpdate.cs`（追加）: 対象抽出、重複判定、連続ラン方式のグルーピング、伝票登録、在庫・掛集計の反映、エラー格納。トランザクションは `StocktakeDb.FixStocktake` と同じ形で1本にまとめた。
- `CvDomainLogic/HhtProcessUpdateMap.cs`（追加）: マスタ解決（コード索引・JAN索引）と12区分の伝票組み立て。
- `CodeShare/ICoreService.cs`: `Msg058_HhtDataUpdate = 58` を追加。
- `CvBase/Parameters.cs`: `HhtUpdateParameter` と `HhtTargetCountRow` を追加。
- `CvServer/Services/QueryMsgStreamService.cs`: `Msg058` を集計処理と同じストリーム分岐へ追加。
- `CvWpfclient/ViewModels/30HHT/HhtDataUpdateViewModel.cs` / `Views/30HHT/HhtDataUpdateView.xaml`: 条件入力・対象件数プレビュー・進捗表示。
- `CvWpfclient/ViewModels/30HHT/HhtErrorDataInputViewModel.cs` / `Views/30HHT/HhtErrorDataInputView.xaml`: エラー一覧のDataGrid編集・保存・削除・表示中データだけの更新実行。
- `CvWpfclient/Models/MenuData.cs`: 2画面の addInfo を「準備中」から実装内容へ更新。
- `Tests/TestServer/HhtProcessUpdateTests.cs`（追加）: 31ケース。
- `Doc/spec/tools/make_hht_testdata.ps1`（追加）: 実マスタの値で `HKALLS` テストファイルを作る。

### 設計から変えた点（テストで判明）
- ヘッダキーに `BackupFileName` を追加。別ファイルの行が1伝票へ混ざらないようにする。
- 重複判定をグルーピングより**前**に移した。再受信行はヘッダキーが元の行と完全一致するため、後で判定すると同じ伝票ランへ吸収され1件目まで巻き込んでエラーになった。
- `HhtTargetCountRow` は CvBase へ置いた（`QueryListSqlParam.ItemType` はサーバ側で型解決するためクライアント内の入れ子クラスは使えない）。列名を `TargetRows` にしたのは SQLite のキーワード `ROWS` を避けるため。
- `ResolveTaxRatePercent` に桁数ガードを追加。`Common.CompareYmd` は8桁以外で例外を投げるため、`MasterSysTax.DateFrom` 未設定で落ちていた。

### 検証
- `dotnet build creativevision10.slnx` 成功（警告0・エラー0）。
- `Tests/TestServer` 204件すべて成功（新規31件＋既存173件）。
- 実DBでの受信→更新の通し確認はサーバ＋クライアント起動が必要なため未実施。

### 残課題
- `Doc/spec/2026-08-24_Rate列_掛率と税率の分離課題.md`: 入力画面側の `Rate`／税率の分離と、既存 `Tran00Uriage.Rate=10`（50,311件）の移行方針。**分離が済むまでは HHT生成伝票を既存の入力画面で開くと `Tax` が誤って上書きされる**。
- `TranVulcanHht.ComputerName` / `UserName` はモデルが `string?` だが生成DDLは NOT NULL。受信画面が必ず値を入れるため実運用では出ないが、モデルとDDLの不一致は残っている。
- 客数(Type0=12)は対象外扱い。`Tran02PosSeisan.KyakuSu` へ入れる場合は別途仕様が必要。

## [2026-08-23] 移行データ変換（ConvertDbTran）のTotal/Tax/IsPay算出ロジック実装

### Agent
- Sonnet 5 : Anthropic : Claude Code

### Editor
- Claude Code

### 目的
- `Doc/spec/2026-08-18_CV10機能完成度チェックリスト.md` 9章「既知のリスク」に記録された「移行売上の`Total`/`Tax`/`IsPay`が未設定」課題を解消する。
- 開発DBでは`Rate=10%`固定・`IsPay`一括UPDATEという暫定対応のみが行われており、`ConvertDb`（旧システムからの変換エンジン）を再実行すると復元しない状態だったため、変換ロジック本体へ組み込む。

### 事前調査で判明した事実
- 実移行変換の本体は`CvDomainLogic/ConvertDb.cs`/`ConvertDbTran.cs`の`ConvertDb`クラスで、`tools/summaryreconcile`はテストデータをハードコード投入するだけの別ツールであり無関係。
- `CnvTran00HonUri`/`CnvTran01TenUri`/`CnvTran03Shiire`/`CnvTran12Jyuchu`/`CnvTran13Hachu`は旧「掛率1」を`Rate`へ既に変換していたが、`Tax`/`Total`は未算出だった。新規入力側（`ShukkaUriageInputViewModel`等）は`Tax = round(|KingakuTotal| * Rate / 100)`、`Total = |KingakuTotal| + Tax`という共通式を使っており、`Rate`は税率(%)として使われている（命名は旧システムの「掛率」を引き継いでいるだけ）。
- `IsPay`（旧「掛計上FLG」）は`Doc/aicoding_log_013.md`記載のとおり移行データで全件0であり、2026-08-16にユーザーが移行済み50,311件（`Tran03Shiire`は25件）を`1`へ一括UPDATE済みだった。この決定はSQLでの一時対応のみで、コード側には反映されていなかった。
- 入金が売上の約41倍という別課題（`Tran06Nyukin.KingakuTotal`が単月入金でなく残高/累計相当の疑い）は、旧システム側テーブル（`HC$tran_tori0`/`HC$tran_tori1`）の実データの意味を確認しないと判断できず、今回は対象外とした。

### 実施内容
- `CvDomainLogic/ConvertDbTran.cs`: `CalcMigratedTaxTotal(int kingakuTotal, int ratePercent)`を追加し、上記の共通式で`Tax`/`Total`を算出。
- 上記5つの`CnvTran*`関数すべてで、算出した`Tax`/`Total`をヘッダへ設定するよう変更。
- `CnvTran00HonUri`/`CnvTran03Shiire`の`IsPay`を、旧「掛計上FLG」の値をそのまま使う実装から`1`固定へ変更（2026-08-16のユーザー決定を再変換でも再現）。

### 技術決定 Why
- **`Rate`（レコード単位の旧「掛率1」）を税率として使った理由**: 開発DBの暫定対応は`Rate=10%`の固定値だったが、実際には新規入力の各InputViewModelも伝票ヘッダの`Rate`列を税率として使っており、移行データも既に`Rate`へ「掛率1」を格納済みだった。固定値より正確で、既存の計算式（`UpdateHeaderTotals`系）と完全に一致させられる。
- **`IsPay`を条件分岐せず`1`固定にした理由**: 旧「掛計上FLG」は移行データで意味を持たず（全件0）、2026-08-16に売掛/買掛から除外しない方針が確定している。将来別の移行データで旧フラグが意味を持つ可能性はあるが、現時点でそれを判別する情報が無いため、確定済みの決定をそのままコード化した。

### 確認
- `dotnet build CvDomainLogic/CvDomainLogic.csproj --no-restore`: 成功（警告0、エラー0）。
- `dotnet build CvServer/CvServer.csproj --no-restore`、`dotnet build Tests/TestServer/TestServer.csproj --no-restore`: いずれも成功。
- `dotnet test Tests/TestServer/TestServer.csproj --no-build`: 168/168 成功（既存テストに`ConvertDbTran`専用のテストは無し、新規追加もせず）。

### 残課題
- 実移行DB（複製）に対して`ConvertDb`を再実行し、`Total`/`Tax`/`IsPay`が期待どおりになることの確認は未実施。
- 入金金額が売上の約41倍になる問題は未着手。旧システム運用担当者への確認が必要（コードだけでは判断できない）。
- `ConvertDbTran`専用のユニットテストは無く、今回も追加していない（既存も実DB複製での目視確認のみで検証されている）。

---

## [2026-08-22] CvWpfclient の XAML関連部品集約

### Agent
- OpenAI Codex

### Editor
- Codex

### 目的
- XAMLで利用する共通スタイル、DataGrid補助処理、ブラシコンバーター補助処理の重複を削減する。
- 既存の表示、リソースキーの適用範囲、DataGrid操作、業務別コンバーターの責務を維持する。

### 実施内容
- 在庫・配分照会4画面で重複していた使用中の6スタイルを `UIStockQueryStyles.xaml` へ集約し、既存の汎用キーと衝突しない `StockQuery*` キーへ変更した。
- `NumericSignBrushConverter` は `UIStockQueryStyles.xaml` 内で定義し、MergedDictionaryロード中の `StaticResource` 解決を辞書内で完結させた。
- 4画面すべてで未使用だった `StockSokoCell` は共有先へ移さず削除した。
- 3画面で重複していた期限超過セルスタイルを `UIFormStyles.xaml` の `OverdueTextBlock` へ集約した。
- `DataGridCellEnterNavigation` と `DataGridSelectionBehavior` のセル取得処理を `DataGridCellHelper` へ集約した。
- `NumericSignBrushConverter` と `TranKubunBrushConverter` は業務別の型を維持し、ブラシリソース解決とフォールバック生成のみ `ResourceBrushHelper` へ集約した。

### 検証
- 変更対象XAMLのXML解析: 成功。
- 共通リソースキーの定義数・利用数と旧キーが残っていないことを確認した。
- `C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient\CvWpfclient.csproj --no-restore -p:UseAppHost=false -v:minimal`: 成功（警告0、エラー0）。
- UTF-8 BOMなし、CRLF、`git diff --check`を確認する。

---

## [2026-08-22] CvWpfclient の通信処理共通化・不要呼び出し削減

### Agent
- OpenAI Codex

### Editor
- Codex

### 目的
- CvWpfclient プロジェクト内に限定し、同一の gRPC 照会・実行処理を通信専用ヘルパーへ集約する。
- 未使用コードと不要な非同期処理を削減し、既存の業務処理、XAML、空画面、Sample画面の動作を維持する。

### 実施内容
- `CoreServiceClient` を追加し、型付きSQL照会、通常一覧照会、実行要求の生成・送信・応答判定を共通化した。
- 各 ViewModel の既存ローカルメソッドは1行の委譲として残し、呼び出し側、継承関係、XAMLコマンド名を維持した。
- `ClientLib` からプロジェクト内で未使用の `ExitAll`、`GetActiveWin`、`SetDataGridDic` を削除した。
- URL起動時の不要な `Task.Run` と、処理を転送するだけの `async` / `await` を削減した。
- `BaseViewModel` への通信処理集約、帳票ラッパー・選択コマンドの追加共通化は、責務過多を避けるため実施しなかった。
- 応答後のキャンセル判定が異なる月次処理2箇所は、動作を変えないため共通化対象から外した。
- 空の View / ViewModel と `SampleView` / `SampleViewModel` は変更していない。

### 検証
- `C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient\CvWpfclient.csproj --no-restore -p:UseAppHost=false -v:minimal`: 成功（警告0、エラー0）。
- `git diff --check`、UTF-8 BOMなし、CRLFを確認する。

---

## [2026-08-22] CvServer の不要呼び出し削減・共通化

### Agent
- OpenAI Codex

### Editor
- Codex

### 目的
- CvServer プロジェクト内に限定し、重複処理と不要な呼び出しを削減する。
- JWT設定、サーバー情報、印刷パス解決を共通化し、既存の認証・印刷・gRPC動作を維持する。

### 実施内容
- `SchedulerService.ExecuteSqliteWalCheckpoint` で二重実行されていた `PRAGMA optimize` を1回に修正した。
- `PrintPdfService` の `Wait` / `Result` とPDF生成待機中の `Thread.Sleep` を、`await` とキャンセル対応 `Task.Delay` に変更した。
- `JwtSettings` を追加し、JWT検証・発行で使用するIssuer、Audience、SecretKey、有効期間、署名設定を共通化した。
- `LoginService` のJWT生成、応答生成、`SysHistJwt` 登録を共通化した。
- `AppGlobal.Shared` を共有インスタンスとしてDI登録し、サーバー情報取得時の都度生成を廃止した。将来利用予定の `Counter` は維持した。
- `PrintServerPathResolver` を追加し、印刷処理とワークファイル削除処理のパス解決を、従来と同じContentRoot・PrintBaseDir基準で共通化した。
- 未使用DIは将来利用の可能性があるため維持した。
- `HandlerClass` の検索・書き込み処理は、共通化により処理順が見えにくくなることを避けるため現状を維持した。
- 800行を超えていた旧 `Doc/aicoding_log.md` を `Doc/aicoding_log_014.md` へ退避した。

### 検証
- `C:\gitroot\UT\vscmd.bat dotnet build CvServer\CvServer.csproj --no-restore -p:OutputPath=obj\CodexBuildOutput\`: 成功（警告0、エラー0）。
- `C:\gitroot\UT\vscmd.bat dotnet run --project Tests\TestServer\TestServer.csproj --no-restore`: 168件成功、0件失敗。
- `git diff --check`、UTF-8 BOMなし、CRLFを確認する。

---
