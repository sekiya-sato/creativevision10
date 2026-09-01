## [2026-09-01] 請求書印刷の旧cvnet帳票移植

### Agent
- Sekiya Sato Codex

### 目的
- 請求書印刷を、旧cvnetの伝票単位・商品CD単位帳票とCSV列順へ合わせる。

### 実施内容
- `SeikyuBalanceDetailViewModel`で旧CRSと同じ61列の帳票SQLを組み立て、伝票単位と商品CD単位を切り替えるようにした。
- 請求期間の売上・入金明細、請求残高、税率別内訳をCSV列へ固定順で出力する。商品CD単位の集約SQLはDB方言間で成立するよう、非集約列をすべて`GROUP BY`へ明記した。
- 画面に出力単位のラジオボタンを追加し、選択に応じてコピーしたqfmを使用する。
- 旧`CVPSK003f.qfm`と`CVPSK003_hinf.qfm`を、内容を変更せずcp932のままそれぞれ新名称へコピーした。

### 確認
- `CvWpfclient\CvWpfclient.csproj`をビルドし、警告0・エラー0を確認した。
- `Tests\TestSqlDialect\TestSqlDialect.csproj`の135件が成功した。
- コピー元・先qfmのSHA-256一致、cp932 XML読込、各`item`数69を確認した。
- `git diff --check`を確認した。

---

## [2026-08-31] SQL方言名のEnum化

### Agent
- Sekiya Sato Codex

### 目的
- SQL方言の識別名を`EnumSqlDialect`へ集約し、文字列定義の変更漏れを防ぐ。

### 実施内容
- `EnumSqlDialect`を追加し、InfoServer、方言実装、SQLite判定、SQL差し替え登録、CvServerの既定プロバイダーで`nameof`を使用するよう統一した。
- 方言解決は従来どおり大文字小文字を区別せず、未対応のMySql／Oracleは恒等方言へフォールバックする。
- SQL差し替えと方言解決のテストをEnum名へ統一した。

### 確認
- `Tests\TestSqlDialect\TestSqlDialect.csproj`の135件が成功した。
- `CvServer\CvServer.csproj`をビルドし、警告0・エラー0を確認した。
- `git diff --check`を確認した。

---

## [2026-08-29] 顧客変換時の会員情報登録

### Agent
- Sekiya Sato Codex

### 目的
- 旧顧客マスター変換時に、確定した顧客Idへ紐づく会員情報を1顧客1件作成する。

### 実施内容
- `CnvMasterEndCustomer`で顧客を先に一括登録し、顧客コードから確定Idを再取得して`MasterEndCustomerAccount.Id_Customer`へ設定した。
- 旧ログイン、ポイント残、ポイントランクおよび顧客集計列を、`MasterEndCustomerAccount`の旧列対応コメントに従って移行した。
- 初期化時は子テーブルを先に削除し、親子テーブルを再作成してから同一トランザクションで登録するようにした。

### 確認
- `CvDomainLogic\CvDomainLogic.csproj`をビルドし、警告0・エラー0を確認した。
- `git diff --check`を確認した。

---

## [2026-08-28] D-05 請求書の税率別内訳

### Agent
- Sekiya Sato Codex

### 目的
- 10.0の適格請求書要件として、請求書へ標準10%・軽減8%・非課税の税率別内訳を印字する。

### 実施内容
- `SummaryUriSei`へ列を追加せず、請求書印刷時に対象期間の`Tran00Uriage.Jmeisai`を展開し、保存済みの`TaxRate`/`Tax`から集計する。
- 返品・値引は負符号で相殺し、税抜課税標準額と税額を税率別にCSVへ追加した。
- 明細集計と請求集計の不一致、不正JSON、未対応税率を印刷前に検出し、PDF生成を停止する。
- 印刷前検査は`SummaryUriSei`のWHERE句＋相関副問合せとしてサーバーへ送る。先頭`WITH`の完全SQLを型指定照会へ渡してSQLite構文エラーになる経路を解消した。
- 区分99を請求集計と同じ特殊算式で扱い、返品は`CalcFlag`を使って明細税額の符号を決める。対象5伝票の10%明細を復元し、既存請求集計を変更せずに税率別内訳へ整合させた。
- `invoicepreflight` VM駆動UATシナリオを追加し、実View・実gRPCで印刷前検査の警告を証跡化する。
- `SeikyuBalanceDetail.qfm`へ`item16`〜`item20`と税率別内訳を追加し、cp932を維持した。
- D-10の実効権限・メニュー公開状態は、ユーザー決定により10.0リリース対象外とした。

### 確認
- QFMをcp932で読込み、XML整形式、`item`数20、`datasrc`数20を確認した。
- `CvWpfclient\CvWpfclient.csproj`をビルドし、警告0・エラー0を確認した。
- `Tests\TestServer` 232件、`Tests\TestSqlDialect` 135件の成功を確認した。
- `git diff --check`を確認した。
- `UatVm.exe invoicepreflight --manage-server`で、2026/07/31の2件が明細不一致としてPDF生成前に停止することを確認した。
- 明細復元後に同コマンドを再実行し、印刷前検査通過後にPrintPdf開始へ到達することを確認した（PDF内容は未確認）。

---

## [2026-08-28] SysPermissionProfile初期データ登録

### Agent
- Sekiya Sato Codex

### 目的
- 空の権限プロファイルテーブルへ、標準プロファイル4件と権限明細11件を初期登録する。

### 実施内容
- `DefineDataTable`は既に`SysPermissionProfile.CreateDefaultData`を呼び出しているため、その初期投入経路を使用する。
- 原因修正後は次回起動時に初期投入できるため、重複する`UpdateDb`マイグレーションは追加しない。
- `GetTableCounts`が指定済みの`SysPermissionProfile`まで除外していたため、テーブル名指定時は`Sys*`も件数取得するよう修正した。

### 確認
- `SysPermissionProfile.CreateDefaultData`による初期登録を`SysPermissionProfileDefaultDataTests`で確認する。
- `CvBase\CvBase.csproj`のビルドは警告0・エラー0、`SysPermissionProfileDefaultDataTests`は成功を確認した。
- CRLFと`git diff --check`を確認した。

---

## [2026-08-28] MasterShain担当区分・権限プロファイルの画面反映

### Agent
- Sekiya Sato Codex

### 目的
- `MasterShain` に追加された担当区分と権限プロファイルIdを、社員マスターメンテで確認・編集できるようにする。

### 実施内容
- `MasterShainMenteView.xaml` の基本情報タブへ、担当区分の固定選択肢と権限プロファイルId入力欄を追加した。
- 既存の `CurrentEdit` バインディングと保存経路を再利用し、ViewModelおよび一覧取得列は変更していない。

### 確認
- XAMLのXML構文、既存リソース参照、`CurrentEdit.ResponsibilityScope` / `CurrentEdit.Id_PermissionProfile` のバインディングを確認した。
- CRLFと`git diff --check`を確認した。
- `CvWpfclient\CvWpfclient.csproj`をビルドし、警告0・エラー0を確認した。

---

## [2026-08-27] MasterMente競合時の一覧保持

### Agent
- Sekiya Sato Codex

### 目的
- 他端末更新を検知したとき、マスタ保守画面の一覧を消さずに残し、競合した編集状態だけを破棄する。

### 実施内容
- `BaseMenteViewModel.HandleConcurrentUpdate`で`ListData`と件数を維持し、選択行と編集データだけを初期化するよう変更した。
- エラーメッセージに、修正内容が保存されなかったこと、一覧は残るが最新ではない可能性があること、F5で再取得することを明記した。

### 確認
- `CvWpfclient`をビルドし、警告0・エラー0を確認した。
- CRLFと`git diff --check`を確認した。

---

## [2026-08-27] 日付・年月入力の自動書式補正

### Agent
- Sekiya Sato Codex

### 目的
- 日付入力で`yyyyMMdd`、年月入力で`yyyyMM`を入力した場合に、既存の表示形式である`yyyy/MM/dd`、`yyyy/MM`へ自動補正する。
- 日付と年月の入力対象を分離し、6桁のコード・ID等を年月として誤変換しない。

### 実施内容
- `DatePickerYmdInputBehavior`を追加し、`FormDatePicker`共通スタイル経由で全DatePickerに適用した。実在する8桁日付だけを補正し、DatePickerの範囲外・除外日・不正日付は既存の検証を維持する。
- `YearMonthInputBehavior`を追加し、年月専用と確認した29個のTextBoxだけへ適用した。実在する6桁年月だけを補正し、日付・コード・ID・バーコード等のTextBoxには適用していない。

### 確認
- 変更した29個のXAMLをXMLとして解析し、構文エラーがないことを確認した。
- `CvWpfclient`をビルドし、警告0・エラー0を確認した。
- CRLFと`git diff --check`を確認した。
- 実行時の手入力確認は未実施。

---

## [2026-08-27] MasterMente系一覧にId列を追加

### Agent
- Claude Sonnet 5 : Anthropic

### Editor
- Claude Code

### 目的
- MasterMente系（コード/名前/略称型）の一覧DataGridにコード重複時の一意特定用として`Id`列が無く、問い合わせ時に不便だった。
- 詳細設計: `Doc/spec/2026-08-27_MasterMente一覧Id列追加_詳細設計.md`

### 実施内容
- `MasterShohinMenteView.xaml` / `MasterTokuiMenteView.xaml` / `MasterShiireMenteView.xaml` / `MasterShainMenteView.xaml` / `MasterEndCustomerMenteView.xaml` / `MasterMaterialMenteView.xaml` / `MasterMeishoMenteView.xaml` の一覧DataGridで、`Id`列を先頭に追加した。
- 表示は既存の`CodeNameDisplay.Format`と同じ「Idは括弧書き」規約に合わせ`Binding="{Binding Id, StringFormat={}({0})}"`で`(123)`形式にした。
- `FrozenColumnCount`を`2`→`3`に変更し、`Id / コード / 名前`（`MasterMeishoMenteView`のみ`Id / 区分 / コード`）をロック列にした。
- ViewModel側は`ListData`行に既に`Id`があるため変更なし。
- 対象外: `MasterConfigMenteView`（コード/名前型でない）、`MasterSysKanriMenteView`（一覧なし）、`TranShopPromotionMenteView` / `TranTokuiPromotionMenteView` / `MasterYosanHanbaiMenteView` / `MasterYosanBrandMenteView`（既にId列あり）。

### 技術決定 Why
- 新規コンバータは追加せず`StringFormat`のみで対応した。理由: `IdCodeNameDisplayConverter`はId/コード/名前を1列に結合する用途で、本件は列を分けたまま`Id`だけ括弧表示したいため既存コンバータの流用対象ではなかった。
- `MasterMeishoMenteView`は列順（区分がコードより前）を変更せず、`Id`は最先頭に追加するのみとした。理由: ロック列指示は「Id・コード・名前を固定する」という運用一貫性の指示であり、既存の列順自体の変更は依頼範囲外のため。

### 確認
- `dotnet build CvWpfclient\CvWpfclient.csproj /p:EnableWindowsTargeting=true /p:UseAppHost=false`: 成功（警告0、エラー0）。
- 画面起動しての目視確認は未実施。

---

## [2026-08-27] 09:35 MasterShainMente印刷のJsub展開

### Agent
- Sekiya Sato Codex

### 目的
- 社員マスタ印刷のQFM item12へMasterShain.JsubをKb順で展開する。

### 実施内容
- `CvWpfclient/ViewModels/01Master/MasterShainMenteViewModel.cs` のPrintBySqlParamにJsub展開列を追加した。Kb昇順、同一Kbは元配列順とし、`Kb:Kbname Cd:Mei`を`/`で連結する。
- JSONがnull・空配列・配列以外の場合は空文字とし、CSVレコード内に改行を含めない。
- `printform/MasterShainMente.qfm` はユーザー変更のitem12名称リスト列レイアウトを保持してコミット対象に含めた。

### 確認
- cvsqliteのMasterShain Id<10で12列の取得、E01→E02のKb順、空・不正形式Jsubの処理を確認。
- qfmprintでcp932のdata.txtからPDFを生成し、item12の名称リスト列と`/`区切り表示を目視確認。
- `Tests/TestSqlDialect` 成功（135件、失敗0件）。
- `CvWpfclient` build 成功（警告0、エラー0）。
- Python製QFM validatorは実行環境にPythonがないため未実行。XML・cp932・CRLFは代替確認済み。

---

## [2026-08-26] 自社締日基準の在庫・掛計上月対応

### Agent
- Sekiya Sato Codex

### 目的
- `MasterSysman.ShimeBi` を基準に、在庫は `DenDay`、売掛・買掛は `KakeDay` から計上月を決定する。
- 在庫・掛再更新画面に、指定した計上月へ対応する具体的な集計期間を表示する。

### 実施内容
- 共通の `ClosingMonthCalculator` を追加し、「日 > 締日なら翌月、それ以外は当月」の計上月判定と、計上月に属する実日付範囲の算出を集約した。
- 在庫の通常更新・範囲再作成・引当再集計は `DenDay`、売掛・買掛の再集計とHHT後処理は `KakeDay` を使って自社締日基準の月へ集計するよう変更した。
- 月次自動再集計の対象月も、実行日と自社締日から算出するよう変更した。
- 在庫・掛再更新画面で自社締日を読み込み、対象年月の入力に応じて「yyyy/MM/dd ～ yyyy/MM/dd」の集計期間を表示するよう変更した。
- 在庫再更新は `Msg050_Summary` で指定年月を伝票から再集計するよう変更し、既存 `SummaryStock` の合算のみで終わる経路を除いた。
- 締日20の20日／21日境界、末締め、年越し、うるう日、およびSQLite SQLの他DB方言変換テストを追加した。

### 検証
- `C:\gitroot\UT\vscmd.bat dotnet test Tests\TestServer\TestServer.csproj --no-build --no-restore` 成功（231件、失敗0）。
- `C:\gitroot\UT\vscmd.bat dotnet test Tests\TestSqlDialect\TestSqlDialect.csproj --no-restore` 成功（135件、失敗0）。
- `C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient\CvWpfclient.csproj --no-restore` 成功（警告0、エラー0）。
- `StockKakeUpdateView.xaml` のXML構文、追加バインディングを確認。

## [2026-08-25] 接続先DB方言のクライアント通知

### Agent
- Sekiya Sato Codex

### 目的
- CvWpfclient がログイン済みサーバの DB 方言を判別できるようにする。

### 実施内容
- `InfoServer` に `DbProvider` を追加し、未ログイン時と旧サーバ応答時の既定値を `Sqlite` とした。
- サーバ初期化時に、構成値ではなく実際の `ExDatabase.Dialect.Name` を `InfoServer.DbProvider` へ設定する。
- `AppGlobal.ServerSqlDialect` を追加し、`StaticInfoServer.DbProvider` から `SqlDialects` を取得できるようにした。
- `InfoServer.DbProvider` から各方言を解決する単体テストを追加する。

### 検証
- `git diff --check` 成功。
- `C:\gitroot\UT\vscmd.bat dotnet build CvBase\CvBase.csproj --no-restore` 成功（警告0、エラー0）。
- `C:\gitroot\UT\vscmd.bat dotnet test Tests\TestSqlDialect\TestSqlDialect.csproj --no-restore` は、既存未コミット変更で `ExDatabase.CloneDb()` が削除され、`ExDatabasePostgre.CloneDb()` と `ExDatabaseMaria.CloneDb()` の `override` が不成立となるためコンパイル不能（CS0115）。今回の変更ではないため未修正。

## [2026-08-25] SQL方言変換器 Phase 6/8 完了と Phase 7 の棚卸し

### Agent
- Sekiya Sato Claude

### 目的
- Phase 6: DDLの照合順序を固定し、3DBのDDL生成を突き合わせるテストを入れる。
- Phase 8: クライアントSQLが変換対象範囲に収まっているかの静的検査と `AGENTS.md` の規約追記。
- Phase 7: 修正は1.0対象外だが、意味差の箇所を機械検出して件数をベースラインで固定する。

### 実施内容（Phase 6）
- `ExDatabase` に `CreateTableSuffix`（CREATE TABLEの末尾へ付ける定義）と `ValidateSchema()`（スキーマ前提の検証）を追加した。既定は空・検証なしなのでSQLiteの生成DDLは変わらない。
- `ExDatabaseMaria` でテーブルを `DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin` で作成するようにした。MariaDBの既定照合順序は大文字小文字とかなを同一視し、`=`/`LIKE`/`ORDER BY`/`DISTINCT`/`GROUP BY` の結果がSQLiteと変わる。方言変換では直せない差なのでテーブル作成時に決め切る。あわせて `@@collation_database` を起動時に検証する。
- `ExDatabasePostgre` で `pg_database.datcollate` がバイト順（C / C.UTF-8 / POSIX）であることを起動時に検証するようにした。PostgreSQLの照合順序はDB作成時にしか決められないため、検証して起動失敗にするしかない。
- `CvServer/Program.cs` でバージョン検証と併せて `ValidateSchema()` を実行する。SQLiteは警告のみ、他DBは起動失敗。
- `DefineDataTable` のテーブル型一覧を `InitializeAsync` 内のローカル変数から `public static TableTypes` へ出した（DDLスナップショットテストから参照するため）。
- `Tests/TestSqlDialect/DdlSnapshotTests` を追加した（6件）。実DB接続なしで3プロバイダーのDDLを生成し、全テーブルで列集合が一致すること、MariaDBが照合順序を固定すること、自動採番列の定義がDBごとに正しいこと、PostgreSQLの列名が小文字になることを検証する。

### Phase 6 で検出した不具合
- **基底 `ExDatabase.GetSqlColumns` が列を1個も返さなかった（修正済み）。** `classT.GetProperties(BindingFlags.Public)` は `Instance`/`Static` の指定が無いと何も返さない。MariaDBでは列0個の `CREATE TABLE` が生成されていた。SQLite と PostgreSQL は `GetSqlColumns` を override しているため影響は無かった。`MariaDBの列が0個にならない` テストで回帰を防ぐ。
- **`DerivedJodai.TodaySql` がSQLiteのDDLにだけ余剰列として現れる（未修正）。** `ExDatabaseSqlite.GetSqlColumns` が `GetProperties()` を引数なしで呼ぶため static プロパティも拾う。`TodaySql` は判定日の既定値を返すSQL式で列としては使われない。稼働中のSQLiteのDDLを変えないため修正せず、テストの既知差一覧で許容した。

### 実施内容（Phase 8）
- `SqlCorpus` にファイル名・行番号を持たせ、`LoadWithLocation` を追加した。
- `Tests/TestSqlDialect/SqlSubsetCheckTests` を追加した（3件）。`CvWpfclient` とサーバ層のSQLリテラルを走査し、方言変換の対象外の構文をファイル:行と構文IDで指摘する。許容一覧に理由付きで載せたファイルだけ除外し、許容一覧が実在ファイルを指しているかも検証する。SQLiteの動作は阻害しない。
- `AGENTS.md` §4 に規約を追記した。SQLはSQLite方言を正典とすること、SQLiteの実行経路は変えないこと、使える構文は構文目録に登録済みのものに限ること、サーバ側のDB間差は `ExecuteDialect` 経由にしてDB別に書き分けないこと、意味差はSQLiteで結果が変わらない書き方に寄せること、下限バージョンと照合順序。

### 実施内容（Phase 7 の棚卸し）
- `Tests/TestSqlDialect/SemanticDifferenceInventoryTests` を追加した（3件）。整数除算と `strftime('%w')` の算術利用を字句解析で検出し、件数の上限を固定する。上限を超えたら失敗するので、新しい画面が意味差を増やしたことに気づける。
- **見積りの訂正**: 意味差の実測値は当初見積りより大幅に小さい。MariaDBの整数除算は「`/` を含むファイル147」と見積もっていたが、C#コードを除いてSQLリテラルだけを数えると**除算を含むSQLは13本**、`strftime('%w')` の算術は**1本**だった。Phase 7 の作業量は見積りより小さい。

### 検証
- `C:\gitroot\UT\vscmd.bat dotnet build creativevision10.slnx` 成功（警告0・エラー0）。
- `dotnet test Tests\TestSqlDialect\TestSqlDialect.csproj` 成功（133件、失敗0）。
- `dotnet test Tests\TestServer\TestServer.csproj` 成功（212件、失敗0）。
- `dotnet test Tests\TestLogin\TestLogin.csproj` 成功（7件、失敗0）。
- `git diff --check` 成功。

### 残作業
- 3DB差分テスト（T7）は実PostgreSQL/MariaDB接続が必要なため未着手。CV10 1.0 は SQLite のみを扱うため 1.0 の受入条件には含めない。
- 意味差の修正（除算13本、曜日算術1本、PostgreSQLの `GROUP BY` 厳格化、集約の戻り型）は 1.0 以降。
- `changes()`（`RebuildDb` / `ConvertDbTran` が更新件数の取得に使用）の他DB対応。
- `ExDatabase.CloneDb` の潜在不具合（`ExDatabaseSqlite` が override しておらず、clone するとSQLite固有の振る舞いが失われる）。呼び出し元が無いため未修正。
## [2026-08-25] SQL方言変換器 Phase 5 完了（サーバ層のJSON配列再構築とUPSERT）

### Agent
- Sekiya Sato Claude

### 目的
- Phase 5 の残作業（`MasterCascadeDb` / `RebuildDb` / `ConvertDbTran` のJSON配列再構築と `SummaryDb` の UPSERT）を実装する。
- 稼働中のSQLite用SQL文を1文字も書き換えずに済ませる。

### 方式（設計変更）
当初の設計は「サーバ層のSQLは変換器を通さず、DB別分岐を手で書く」だったが、9本の複雑なSQLをDB別に3通り手書きすることになり、稼働中のSQLite用SQLへ手を入れる必要があった。
そこで**サーバ層のSQLをSQLite方言のまま残し、実行時に変換器へ通す**方式へ変えた。`ExDatabase` に `ExecuteDialect` / `FetchDialect` / `TranslateDialect` を追加し、`Dialect.TranslatesSql` が false（SQLite）のときは引数をそのまま渡す。SQLite接続では従来の `Execute` / `Fetch` と完全に同じ動作になる。
保証G2は「変換を差すのはクライアントSQLの受け口1点のみ」から「変換を差す箇所は明示的に指定したものだけ。SQLiteでは常に恒等」へ更新した。非破壊の根拠を「変換器を通さないこと」ではなく「SQLiteでは変換が恒等であること」に置く。

### 実施内容
- `ExDatabase` に `ExecuteDialect` / `FetchDialect` / `TranslateDialect` を追加した。
- 呼び出し箇所を差し替えた。SQL文自体は一切変更していない。
  - `MasterCascadeDb`: `_db.Execute(sql, [...])` 9箇所と `_db.Fetch<long>` 2箇所。
  - `SummaryDb`: 全SQLの実行が集約されている `ExecuteAndCounts` 1箇所（UPSERT 4箇所を含む）。
  - `RebuildDb`: 4箇所が集約されている `ExecuteUpdateAndGetChanges` 1箇所。
  - `ConvertDbTran`: `subCnvTranHeaderSize` 1箇所。
- 変換ルールを追加した。
  - `B02-JsonEach` の拡張: `json_each` の `key` 列（配列の要素順保持に使用）を行番号列へ写像する。PostgreSQL は `WITH ORDINALITY AS J(value, jkey)`、MariaDB は `jkey FOR ORDINALITY`。`key` は MariaDB の予約語なので列名を `jkey` にした。`J.key` の参照側は `JsonEachKeyRule` が合わせる。走査は右から左に進み `J.key` の位置ではFROM句を見ていないため、別名の収集だけ `SqlRewriteContext` の生成時に1回行う。`.key` を参照していない `json_each` には行番号列を作らない。
  - `B04-JsonGroupArray`: PostgreSQL `jsonb_agg` / MariaDB `JSON_ARRAYAGG`。
  - `B04-JsonObject`: PostgreSQL `jsonb_build_object`（MariaDBは同名）。
  - `B04-JsonSet`: PostgreSQL は `jsonb_set` をパスごとに入れ子へ展開し、値を `to_jsonb` で包む（MariaDBは同名・同じ引数並び）。引数の対が揃わない形は変換しない。
  - `C04-Upsert`: MariaDB 向けに `ON CONFLICT(...) DO UPDATE SET` → `ON DUPLICATE KEY UPDATE`、`excluded.列` → `VALUES(列)`。衝突対象の列指定は落とす（対象テーブルは判定列そのものへ一意索引があるため判定は変わらない）。`DO NOTHING` は等価な短い書き方が無いため変換せず未対応構文として報告する。PostgreSQL は SQLite と同一構文なのでルール不要。
  - `C05-Changes` を構文目録へ追加した（変換は未実装）。
- `Tests/TestSqlDialect/ServerLayerRuleTests` を追加した（14件）。実際の `Jsub` 再構築SQLと UPSERT SQL を入力に、行番号列の写像、無関係な `key` 列を書き換えないこと、`json_set` の入れ子展開、UPSERT の写像、SQLiteでは同一参照が返ることを検証する。

### 検証
- `C:\gitroot\UT\vscmd.bat dotnet build creativevision10.slnx` 成功（警告0・エラー0）。
- `dotnet test Tests\TestSqlDialect\TestSqlDialect.csproj` 成功（120件、失敗0）。
- `dotnet test Tests\TestServer\TestServer.csproj` 成功（212件、失敗0）。今回差し替えた `MasterCascadeDb` / `SummaryDb` / `RebuildDb` を直接検証する `MasterCascadeDbTests` / `SummaryDbTests` / `SummaryKakeDbTests` / `TranTaxRebuildTests` を含む。
- 実SQL180本の変換到達率が PostgreSQL 168→**178**、MariaDB 170→**178** に上がった。残る2本は `ExDatabase` の `sqlite_master` 参照（プロバイダー側で override 済み）と、JSONパスがC#の文字列補間穴のままのテンプレート（実行時には実名へ置き換わる）。`CorpusCoverageTests` の下限を更新した。
- `git diff --check` 成功。

### 残作業
- `changes()`（`RebuildDb` / `ConvertDbTran` が更新件数の取得に使用）は他DBに同名関数が無く、実行APIの戻り値で取る必要がある。構文目録へ登録済みだが変換は未実装。
- Phase 6: DDLの照合順序固定（MariaDB は `utf8mb4_bin`、PostgreSQL は `LC_COLLATE=C`）とDDLスナップショットテスト。
- Phase 7: 意味差の監査（MariaDBの整数除算、PostgreSQLの `GROUP BY` 厳格化、集約戻り型、`strftime('%w')` の算術2箇所）と3DB差分テスト。CV10 1.0 は SQLite のみを扱うため 1.0 の受入条件には含めない。
- Phase 8: CvWpfclient のSQL静的検査テストと `AGENTS.md` の規約追記。
- `ExDatabase.CloneDb` の潜在不具合（`ExDatabaseSqlite` が override しておらず、clone するとSQLite固有の振る舞いが失われる）。呼び出し元が無いため未修正。
## [2026-08-25] SQL方言変換器 SQLite経路の非破壊を構造的に保証 + Phase 5 一部

### Agent
- Sekiya Sato Claude

### 目的
- 「既存のSQLite のSQL実行経路は絶対に破壊しない」を、実装の性質への依存ではなく分岐とテストで保証する。
- Phase 5（サーバ層のDB別分岐）のうち、SQLiteの動作を1バイトも変えない項目を実装する。

### 実施内容
- `ISqlDialect` に `TranslatesSql` を追加した。SQLite（恒等変換）は false、PostgreSQL / MariaDB は true。
  `HandlerClass.TranslateClientSql` は先頭でこれを見て即座に引数を返す。SQLite接続時に増える処理は bool 判定1回だけになり、差し替え表の参照も字句解析も走らない。
- `SqlDialectGuard`（CvWpfclient の開発時自己検査）を例外を投げない実装にした。全画面のクエリ経路を通るため、変換ルールの不具合がSQLiteで動いている画面を壊しうる唯一の箇所だった。検査失敗時は警告ログのみで続行し、`Mode=Off` では検査自体を行わない。
- `Tests/TestSqlDialect/SqliteRouteGuardTests` を追加した（10件）。SQLite方言が全モードで同一参照を返すこと、Strictモードでも例外を投げないこと、差し替え表にSQLiteを登録できないこと、未終端リテラルや空入力などの壊れたSQLで変換器と自己検査が例外を投げないこと、`QueryListSqlParam` が QueryKey を持たない旧形式JSONも読めること（配布物の組合せが変わっても既存経路が壊れない）を検証する。
- `ExDatabase.ChangeTimeout` の SQLite固有処理（`PRAGMA busy_timeout`）を `ExDatabaseSqlite` の override へ移し、基底は何もしないようにした。MariaDB / PostgreSQL は既に override 済み。呼び出し元は現在1箇所も無い。
- `HandlerClass.HandlePartialUpdate` の値バインドを見直した。通信上すべて文字列で来る値を、**SQLiteでは現行どおり文字列のまま渡し**（列アフィニティで解釈させる）、型に厳しい他DBのときだけ列のCLR型へ変換する。変換できない値はサーバで丸めずDB側のエラーに委ねる。

### 調査で判明したこと
- `UpdateDb` のプロバイダー別ベースライン版数は**不要**だった。`WriteVersionInfoAsync` は `SysUpdateDb` が空（新規DB）のとき最新版数を書いて移行SQLを1本も実行せず返るため、新規作成する PostgreSQL / MariaDB では SQLite 前提の `ALTER TABLE` 22件が最初から流れない。設計書の該当項目を対応不要に更新した。
- `ExDatabase.CloneDb` に潜在不具合がある（**未修正**）。`ExDatabaseSqlite` が override していないため、SQLite接続を clone すると基底 `ExDatabase` が返り、`Dialect`・`GetSqlColumns`・WALモードの Open/Close というSQLite固有の振る舞いが失われる。現在呼び出し元が1箇所も無いため実害は無い。既存のSQLite経路を変えないため今回は手を付けず、設計書へ記録した。

### 検証
- `C:\gitroot\UT\vscmd.bat dotnet build` を `CvBase` / `CvBaseSqlite` / `CvBasePostgre` / `CvBaseMariadb` / `CvServer` / `CvWpfclient` に対して実行し、いずれも成功（警告0・エラー0）。
- `dotnet test Tests\TestSqlDialect\TestSqlDialect.csproj` 成功（106件、失敗0）。
- `dotnet test Tests\TestServer\TestServer.csproj` 成功（212件、失敗0）。`MasterCascadeDbTests` / `SummaryDbTests` を含む既存のSQLite経路に影響が無いことを確認した。
- `git diff --check` 成功。

### 残作業
- Phase 5 の残り: `MasterCascadeDb` / `RebuildDb` / `ConvertDbTran` のJSON配列再構築（`json_group_array` + `json_set`）と `SummaryDb` の UPSERT（`ON CONFLICT` → MariaDB は `ON DUPLICATE KEY UPDATE`）。いずれも稼働中のSQLite用SQLに触るため、SQLite分岐は現行SQLをそのまま保持する形で、独立した変更として実施する。
- Phase 6: DDLの照合順序固定（MariaDB は `utf8mb4_bin`、PostgreSQL は `LC_COLLATE=C`）。
- Phase 7: 意味差の監査と3DB差分テスト（CV10 1.0 対象外）。
- Phase 8: CvWpfclient のSQL静的検査テストと `AGENTS.md` の規約追記。
## [2026-08-25] SQL方言変換器 Phase 2〜4（カテゴリA/B のルールと QueryKey 差し替え）

### Agent
- Sekiya Sato Claude

### 目的
- Phase 1 で入れた骨格へ、カテゴリA（単純写像）とカテゴリB（JSON・日付）の変換ルールを実装する。
- 変換器で表現できない形が出てもクライアントのSQLite側SQLを書き換えなくて済む逃げ道（QueryKey 差し替え）を用意する。
- 現行SQLiteの挙動は一切変えない。既存SQL文字列への変更は0件。

### 実施内容
- カテゴリA のルールを追加した。
  - `A01-Ifnull`: `ifnull` → `coalesce`（PostgreSQLのみ。MariaDBは同名関数なので `NativeConstructIds` で扱い書き換えない）。
  - `A02-CastType`: `CAST(x AS TEXT/REAL/INTEGER)` → MariaDB の `CHAR`/`DOUBLE`/`SIGNED`。長さ指定付きの型は触らない。
  - `A03-ReservedIdent`: 予約語と衝突する列名（`Offset`/`Sql`）を引用。PostgreSQLは小文字で引用しDDLの小文字化と揃える。`LIMIT n OFFSET m` の句キーワードは引用しない。
  - `A04-NullsOrder`: PostgreSQL の `ORDER BY` へ `NULLS FIRST`。**既定は無効**（`Database:SqlRules:A04-NullsOrder`）。単純な列参照の項だけを対象にする。
- MariaDB のセッション設定を Phase 1 から継続して有効化した。`PIPES_AS_CONCAT` と `NO_BACKSLASH_ESCAPES` で `||`（約110箇所）と `ESCAPE '\'`（6箇所）をSQL書換なしで解決する。
- カテゴリB JSON のルールを追加した。
  - `B01-JsonExtract`: PostgreSQL `((X)::jsonb ->> 'P')` / MariaDB `JSON_VALUE(X,'$.P')`。単一階層パスのみ。
  - `B02-JsonEach`: PostgreSQL `jsonb_array_elements((X)::jsonb) AS a(value)` / MariaDB `JSON_TABLE(X,'$[*]' COLUMNS(value JSON PATH '$')) AS a`。どちらも `a.value` を提供するため、展開結果を参照する側のSQLは変更不要。
  - `B03-JsonValid`: PostgreSQL は `IS JSON` 述語（16以降）。MariaDB は同名関数。
  - `B04-JsonCast`: `json(X)` を PostgreSQL `((X)::jsonb)` / MariaDB `CAST(X AS JSON)` へ。
- カテゴリB 日付・整形のルールを追加した。
  - `B05-Strftime`: 書式5種（`%Y%m`/`%Y%m%d`/`%w`/`%d`/`%s`）と `'now','localtime'` の3引数形。`%w` はSQLiteと同じ文字列 '0'〜'6' を返す。
  - `B06-Printf`: `%0Nd` を `LPAD`/`lpad` へ。`printf('%04d-%02d-%02d', y, m, d)` のような区切り付きの形は連結式を組み立てる。
  - `B07-DateModifier`: 修飾子のリテラル形（`'-1 year'`）と連結形（`'+' || n || ' months'`、符号省略形も可）を年月日の加減算へ。修飾子なしの `date(x)` は3DBで解釈できるため触らない。
  - `B08-Julianday`: `julianday(a) - julianday(b)` の減算パターンのみを日数差へ。単独の `julianday` は変換しない。
- `QueryKey` 差し替え機構を入れた。`QueryListSqlParam` に省略可能な `QueryKey` を追加し（既存117呼び出しは無変更で通る）、`SqlOverrideCatalog` が `(QueryKey, 方言名)` で手書きSQLを返す。SQLiteへの登録は拒否する。
- 変換器の走査を右から左に変更した。入れ子の内側が先に確定するため、範囲ごと差し替えるルールが内側の変換結果を取り込める。
- `Inspect()` を「変換を試したうえで残った構文を返す」実装に変えた。ルールが無い構文だけでなく、ルールはあるが対応できない形も検出できる。CvWpfclient の開発時自己検査が精度を得る。
- SQLite固有構文の目録に「関数呼び出しの形が必要」「引数個数の下限」の条件を追加した。`json` は `IS JSON` でも語として現れ、`date` は他DBにも同名関数があるため、条件なしでは誤検出する。

### 検証
- `C:\gitroot\UT\vscmd.bat dotnet build` を `CvBase` / `CvServer` / `CvWpfclient` に対して実行し成功（警告0・エラー0）。
- `dotnet test Tests\TestSqlDialect\TestSqlDialect.csproj` 成功（96件、失敗0）。カテゴリA/B の全ルール、対応できない形を変換しないこと、変換の冪等性、実SQL180本の字句復元性、SQLite方言が同一参照を返すことを確認した。
- 実SQL180本の変換到達率は PostgreSQL 168本、MariaDB 170本。`CorpusCoverageTests` がこの数値を下限として固定する。残りは Phase 5 で扱うサーバ層構文（`json_group_array`/`json_set`/`json_object`/`sqlite_master`）と、文字列補間穴を含むテンプレート1本。
- `dotnet test Tests\TestServer\TestServer.csproj` 成功（212件、失敗0）。既存のSQLite経路に影響が無いことを確認した。
- `git diff --check` 成功。

### 残作業
- Phase 5: サーバ層のDB別分岐（`MasterCascadeDb`/`RebuildDb`/`ConvertDbTran` のJSON配列再構築、`SummaryDb` の UPSERT、`ExDatabase` のSQLite依存、`UpdateDb` のベースライン版数、`HandlePartialUpdate` の型変換）。
- Phase 6: DDLの照合順序固定（MariaDB は `utf8mb4_bin`、PostgreSQL は `LC_COLLATE=C`）。
- Phase 7: 意味差の監査（MariaDBの整数除算、PostgreSQLの `GROUP BY` 厳格化、集約戻り型、`strftime('%w')` の算術2箇所）と3DB差分テスト。CV10 1.0 は SQLite のみを扱うため 1.0 の受入条件には含めない。
- Phase 8: CvWpfclient のSQL静的検査テストと `AGENTS.md` の規約追記。
## [2026-08-25] SQL方言変換器 Phase 1（骨格・SQLite恒等性の保証）

### Agent
- Sekiya Sato Claude

### 目的
- SQLite / PostgreSQL / MariaDB の3DB同時サポートに向け、クライアント由来SQLの方言変換器の骨格を入れる。
- クライアント側でのSQL組み立てルールと既存SQL文字列を一切変えず、現行SQLiteの挙動を1バイトも変えない。
- 設計は `.omo/2026-08-25_sql_dialect_translator_detail_design.md`、構文インベントリは `.omo/2026-08-25_sql_dialect_server_absorption_and_migration_cost.md` を参照する。

### 実施内容
- `CvBase/Sql/` に方言変換の基盤を追加した。方言実装をCvBase(層1)へ置いたため、CvServerとCvWpfclientの双方から参照できる。
  - `ISqlDialect` / `SqlDialectMode` / `SqlDialectOptions`、`SqlToken` / `SqlTokenizer`、`SqlRewriteContext` / `ISqlRewriteRule`、`SqlDialectBase`、`PassThroughSqlDialect` / `SqlDialectVersions`、`SqliteConstructCatalog`、`SqlDialectUnsupportedException`、`SqlDialects`、`SqlDialectGuard`。
  - `Dialects/PostgreSqlDialect`、`Dialects/MariaSqlDialect`。Phase 1 では変換ルールを持たず、未対応構文の検出とバージョン検証のみ行う。
- `SqlTokenizer` はSQLを構文解析せず、文字列リテラルとコメントの誤認防止に必要な字句だけを認識する。「返した字句のTextを連結すると入力に完全一致する」を不変条件とし、ルール不一致のSQLが1バイトも変わらないことを保証する。
- `ExDatabase` に `Dialect` 仮想プロパティ（既定は恒等変換）を追加し、各プロバイダーで上書きした。`ExDatabaseSqlite` は明示的に恒等変換を返す。
- `ExDatabaseMaria` に接続直後のセッション設定を追加した。`PIPES_AS_CONCAT` と `NO_BACKSLASH_ESCAPES` の2語で、文字列連結 `||`（約110箇所）と `ESCAPE '\'`（6箇所）をSQL書換なしでSQLiteと同じ意味にする。`ONLY_FULL_GROUP_BY` と `STRICT_TRANS_TABLES` は入れない。
- `CvServer/Services/HandlerClass.cs` に `TranslateClientSql()` を追加し、クライアントSQLの受け口3箇所（`HandleQueryOne` / `HandleQueryList` / `HandleQueryListSql`）だけを通した。`CvBase` / `CvDomainLogic` 内部のSQLは変換器を通さない。
- `CvServer/Program.cs` で `Database:SqlTranslation`（`Auto` / `Strict` / `Off`）を読み、起動時にDBバージョンを検証する。SQLiteは警告のみ、他DBは起動失敗にする。
- `CvWpfclient` の `CoreServiceClient.QuerySqlListAsync` に開発時の自己検査を入れた。他DBへ移せない構文を警告するだけで、SQLは変更せず送信も止めない。
- `Tests/TestSqlDialect` を追加した。CvWpfclient / CvBase / CvDomainLogic のソースから実SQLリテラル180本を収集し、字句解析の復元性とSQLite恒等性を検証する。

### 検証
- `C:\gitroot\UT\vscmd.bat dotnet build` を `CvBase` / `CvBaseSqlite` / `CvBasePostgre` / `CvBaseMariadb` / `CvServer` / `CvWpfclient` / `Tests\TestSqlDialect` に対して実行し、いずれも成功（警告0・エラー0）。
- `dotnet test Tests\TestSqlDialect\TestSqlDialect.csproj` 成功（23件、失敗0）。実SQL180本と対象プロジェクトのソース全文で字句列の復元性を確認し、SQLite方言が引数と同一参照を返すことを参照等価で確認した。
- `dotnet test Tests\TestServer\TestServer.csproj` 成功（212件、失敗0）。既存のSQLite経路に影響が無いことを確認した。
- `git diff --check` 成功。

### 残作業
- Phase 2: カテゴリA（`ifnull` → `COALESCE`、`CAST` 型写像、予約語列名の引用）と `QueryKey` オーバーライド機構。
- Phase 3〜4: JSON（`json_extract` / `json_each` / `json_valid`）と日付・整形（`strftime` / `printf` / `date(±)` / `julianday`）の式書換。
- Phase 5〜6: サーバ層のDB別分岐、DDLと照合順序、`A04-NullsOrder` の有効化判断。
- CV10 1.0 は SQLite のみを扱う。PostgreSQL / MariaDB の実接続検証（Phase 7）は 1.0 の受入条件に含めない。
## [2026-08-25] CvServerのDBプロバイダー選択対応

### Agent
- Sekiya Sato Codex

### 目的
- `CvServer/Program.cs` のSQLite固定登録とSQLite専用保守処理を、既定のSQLite運用を維持したままPostgreSQL／MariaDBへ切り替え可能にする。

### 実施内容
- `Database:Provider` を `Sqlite`（既定値）、`Postgre`、`MariaDb` として解釈し、各プロバイダーの接続文字列キー（`sqlite`、`postgres`、`mariadb`）と `ExDatabase` 実装を選択するようにした。
- `CvServer` に `CvBasePostgre` と `CvBaseMariadb` のプロジェクト参照を追加した。
- SQLite専用の定期WALチェックポイント、停止時の `PRAGMA wal_checkpoint`、SQLiteプール解放をSQLite選択時に限定した。外部DBのWAL／redoはアプリケーションから操作しない。
- PostgreSQL／MariaDBを使う場合は、対応する接続文字列と `Database:Provider` を環境別設定へ追加する。業務SQLの方言互換は本変更の対象外である。

### 検証
- `C:\gitroot\UT\vscmd.bat dotnet build CvServer\CvServer.csproj` 成功（警告0・エラー0）。
- `dotnet run --project Tests\TestServer\TestServer.csproj --no-build` 成功（212件、失敗0、スキップ0）。
- `git diff --check` 成功。

## [2026-08-25] MariaDBプロバイダー基盤処理の補完

### Agent
- Sekiya Sato Codex

### 目的
- CvServer/CvWpfclientの個別SQL方言対応は保留し、`CvBase.ExDatabase` と既存SQLite実装を基準に、MariaDB/PostgreSQLプロバイダーの基盤処理を最小差分で整合させる。

### 実施内容
- `CvBase.ExDatabase` に、派生プロバイダーが接続を開くかどうか指定できるprotected constructorを追加した。
- `ExDatabaseMaria.GetDbConn` が `isOpen=false` でも接続を開いていた処理を修正した。
- MariaDBのOpen時にDBバージョンを取得し、Clone時も `ExDatabaseMaria` を維持するようにした。
- MariaDBのタイムアウト変更でSQLite用 `PRAGMA busy_timeout` を使用せず、NPocoの `CommandTimeout` を設定するようにした。
- MariaDBのテーブル一覧・件数取得を `information_schema.tables` ベースで実装し、共通実装の `sqlite_master` 依存を回避した。
- `CvBasePostgre` は必要なoverrideが既に実装済みだったため変更せず、`CvBaseSqlite` と変更元DB専用の `CvBaseOracle` も現状維持とした。

### 検証
- `CvBase`、`CvBaseSqlite`、`CvBaseMariadb`、`CvBasePostgre` を順次buildし、すべて警告0・エラー0。
- `dotnet build creativevision10.slnx --no-restore` 成功（警告0・エラー0）。
- `dotnet run --project Tests/TestServer/TestServer.csproj --no-restore` 成功（212件、失敗0、スキップ0）。
- MariaDB/PostgreSQLの実サーバー接続によるCRUD・メタデータ取得は未実施。

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
