## [2026-08-18] Rebuild締日変更ブロックの独立レビュー指摘を修正

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- Rebuild締日変更ブロックのサーバ型解決、入力競合、受入テスト不足を解消する。

### 実施内容
- `SummaryClosingCheckRow` と締日判定規則を `CvBase` の公開共有型へ追加し、`StockKakeUpdate` と請求計算／支払計算の締日取得が WPF private 型を `QueryListSqlParam.ItemType` に渡さないよう統一した。
- Rebuild実行は確認直後に対象・年月・対象月をスナップショット化し、締日照会から要求生成・完了表示まで同じ値を使用する。`IsProcessing` は照会前に設定し、照会・送信を単一の `try` / `finally` で保護した。不一致、キャンセル、例外では要求列生成・`Msg051`〜`Msg057`の送信へ進まない。
- 請求残／支払残を含む確認文へ、保存済み集計行がない場合は締日変更を検出できない制約を明示した。
- 既存 TestServer に、共有DTOの実サーバ `Msg101_Op_Query` 型解決、対象4種、1日／31日月末丸め、99月末、保存行なし、不一致、最大5件＋残件数、送信可否ゲートを追加した。月次掛集計には19／29／39／89境界と90／99除外を売掛・買掛対称に追加した。

### 確認
- `Tests/TestServer/TestServer.csproj` と `CvWpfclient/CvWpfclient.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。
- `SummaryKakeDbTests` は25件すべて成功し、共有DTOの実サーバ型解決テストも1件成功した。
- 締日照会の `await` より後にだけ要求列生成・ストリーム送信が存在することを静的に確認した。WPFの確認ダイアログ操作、通信取消、実画面警告は自動化用プロジェクトがないため未実施。
- 区分Cの金額集計とRebuild安全策を含むため、独立再レビュー待ちとする。

## [2026-08-18] Rebuildの締日変更ブロックを追加

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 締日マスタ変更後の保存済み請求残・支払残を、在庫・掛再更新で無条件に削除・再作成しないようにする。

### 実施内容
- `StockKakeUpdate` は利用者確認後かつ要求列生成前に、対象側の `SummaryUriSei`／`SummaryKaiShi` と現在マスタ締日をパラメータ化照会で照合するようにした。
- 不一致時は最大5件と残件数、手動再計算の案内を警告し、`Msg051`〜`Msg057`を送信せず処理を開始しない。
- 全ては両側、売掛のみ／買掛のみは該当側、在庫のみは検査なしとし、照会・期待締日判定・警告組立を小メソッドへ分離した。

### 確認
- `CvWpfclient/CvWpfclient.csproj` と `Tests/TestServer/TestServer.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。
- `SummaryKakeDbTests` を再実行し、21件すべて成功した。
- XAMLは未変更。既存バインディングとApp共通リソースを確認した。WPFの実サーバ照会・警告表示は自動化用プロジェクトがないため未確認。
- 区分C作業のため、独立レビュー待ちとする。

## [2026-08-18] 月次売掛・買掛の確定集計ルールを実装

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 月次売掛・買掛を請求残・支払残と同じ金額列・内訳・残高符号へ統一する。

### 実施内容
- `CalcSummaryUriKake`／`CalcSummaryKaiKake` を、`Total`の正値内訳、返品税だけの符号化、内訳からの合計算出、`TotalIn/TotalOut - TotalSales/TotalShiire`方向の残高へ対称に更新した。
- 入金・支払は有効JSONの明細だけを集計し、ヘッダ`KingakuTotal`を正値源から除外した。05・未知KINはOther、不正JSONは空明細・0として処理を継続する。
- `SummaryKakeDbTests` を売掛・買掛対称に拡張し、区分範囲、99除外、税、残高、後続月、冪等性、KINフォールバック、不正JSONを固定した。テストヘルパーは`Total`／`KingakuTotal`と明細／ヘッダを意図的に異ならせる。

### 確認
- `Tests/TestServer/TestServer.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。
- Microsoft.Testing.Platform の実行形式から `SummaryKakeDbTests` を実行し、21件すべて成功した。
- 金額結果を広範囲に変更する区分C作業のため、独立レビュー待ちとする。

## [2026-08-18] 請求・支払計算の不正JSON時の明細集計設計を訂正

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 不正JSONを空配列へ防御する場合に明細金額を復元できないことを、KIN Otherフォールバックと矛盾しない設計へ訂正する。

### 実施内容
- Otherフォールバックを有効JSON内の `Id_Kin=0`、未知・未登録・空・01〜05以外のKINコードに限定した。
- 不正JSONは例外にせず空明細として扱い、その伝票の `TotalIn` / `TotalOut` を0とすること、不正JSONの検知・補正は別のデータ品質課題であることを明記した。
- G/W/T-8とテスト方針を、有効JSONの未知KIN→Other、不正JSON→例外なし・0へ分離した。`KingakuTotal`を正値源にしない方針は維持した。

### 確認
- 詳細設計のSQL集計、請求計算、支払計算、受入、テスト方針の記述を同じ期待値へ統一した。
- 今回は設計文書のみを変更し、ソース実装・テスト・完成度チェックリストは変更していない。

## [2026-08-18] 請求・支払計算のRebuild安全策と月次掛集計ルールを詳細設計へ反映

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 締日変更済みマスタでのRebuild誤更新を防ぎ、月次売掛・買掛の金額列・残高符号を請求残・支払残と整合させる次段階実装の詳細設計を確定する。

### 実施内容
- `StockKakeUpdate` の対象別締日変更ブロック、パラメータ化照会、送信0件保証、警告内容、既存残高行がない場合の検出限界を設計化した。
- `CalcSummaryUriKake`／`CalcSummaryKaiKake` の `Total` 正値集計、正値の返品・値引内訳、税符号、明細だけを正値源とするKIN集計、未知KINのOtherフォールバック、残高符号、JSON防御を具体化した。
- テストデータの `Total`／`KingakuTotal` 分離、Rebuildブロック受入条件、区分Cの独立確認必須を追記した。
- Renbanのmigration既定0と計算生成時の業務既定1を区別し、月別予定表・帳票qfmはスコープ外のままとした。

### 確認
- 文書差分を現行 `SummaryDb`、`SummaryKakeDbTests`、`StockKakeUpdateViewModel` と照合した。
- 今回は設計文書のみを変更し、ソース実装・テスト・完成度チェックリストは変更していない。

## [2026-08-18] 在庫・掛再更新へ請求残・支払残のRebuildを追加

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 在庫・掛再更新の売掛・買掛再集計後に、同じ対象年月の請求残・支払残も通常再計算で再作成できるようにする。

### 実施内容
- `StockKakeUpdate` が売掛・買掛再集計の完了後、得意先／仕入先マスタに実在する有効締日（1～31日・末日）を取得し、対象年月×締日で `Msg056`／`Msg057` を順次実行するようにした。
- Rebuildから渡す `BillingParameter.IsReissue` は常に `false` とし、既存の請求書番号・連番を保持する通常再計算を使用する。
- 実行確認時に請求残・支払残も再作成することを表示するようにした。

### 確認
- `CvServer/CvServer.csproj`、`CvWpfclient/CvWpfclient.csproj`、`Tests/TestServer/TestServer.csproj` を Development 環境で直列ビルドし、警告・エラーなしを確認した。
- Microsoft.Testing.Platform の実行形式から `SummaryKakeDbTests` を実行し、15件すべて成功した。

## [2026-08-18] 請求書の明示的再発行を追加

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 通常再計算の採番冪等性を維持しつつ、明示的な請求書再発行時だけ請求書連番を更新できるようにする。

### 実施内容
- `BillingParameter.IsReissue` を追加し、請求計算のgRPCストリーミングへ伝播した。
- 請求残の再作成時、通常実行は既存 `SeikyuNo`／`Renban` を維持し、再発行指定時は `Renban` を+1して請求書番号を再採番するようにした。
- 請求計算画面に再発行チェックボックスを追加した。
- 採番維持と再発行連番のテストを追加した。

### 確認
- `Tests/TestServer/TestServer.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。
- Microsoft.Testing.Platform の実行形式から `SummaryKakeDbTests` を実行し、15件すべて成功した。
- `CvWpfclient/CvWpfclient.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。

## [2026-08-18] 請求計算・支払計算の実行画面を追加

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 得意先／仕入先の締日、計算月、コード範囲を指定して請求残・支払残をストリーミング実行できる画面を提供する。

### 実施内容
- `BillingCalculationView`／`PaymentCalculationView` と共通ViewModelを追加し、実データから動的に取得した締日、計算月、コード範囲、進捗、実行・キャンセルを実装した。
- 請求計算は親子の締日不一致を事前検出し、「マスタ変更および請求再計算が必要」と警告するが、処理はブロックしない。
- 掛管理メニューの請求計算・支払計算を実装済み表示へ更新した。

### 確認
- 追加XAMLのXML構文、名前空間、App共通リソース、ViewModelバインディングを確認した。
- `CvWpfclient/CvWpfclient.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。

## [2026-08-18] 請求・支払計算のgRPCストリーミングを結線

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 請求残・支払残の計算を既存の掛再更新と同じgRPCストリーミング経路から実行できるようにする。

### 実施内容
- `BillingParameter`、`Msg056_SummaryUriSei`、`Msg057_SummaryKaiShi` を追加した。
- `SummaryDb` に請求残・支払残のストリーミング入口を追加し、`QueryMsgStreamService` から結線した。
- 両ストリーミング入口がエラーなく完了通知を返すテストを追加した。

### 確認
- `Tests/TestServer/TestServer.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。
- Microsoft.Testing.Platform の実行形式から `SummaryKakeDbTests` を実行し、15件すべて成功した。

## [2026-08-18] 支払残の計算処理を追加

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 指定締日・支払月・仕入先コード範囲から支払残を冪等に作成し、支払予定日を保持する。

### 実施内容
- `SummaryDb.CalcSummaryKaiShi` を追加し、仕入先締日から支払期間を算出して対象仕入先ごとに支払残をDELETE→再作成するようにした。
- 仕入／返品／値引、返品税の符号、支払内訳、累計残、`PayMonth`／`PayDay` による支払予定日を実装した。
- 支払残の期間・内訳・累計残・冪等性・月末予定日を検証するテストを追加した。

### 確認
- `Tests/TestServer/TestServer.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。
- Microsoft.Testing.Platform の実行形式から `SummaryKakeDbTests` を実行し、13件すべて成功した。

## [2026-08-18] 請求残の計算処理を追加

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 指定締日・請求月・得意先コード範囲から請求残を冪等に作成し、請求書番号と入金予定日を保持する。

### 実施内容
- `SummaryDb.CalcSummaryUriSei` を追加し、締日から請求期間を算出して対象得意先ごとに請求残をDELETE→再作成するようにした。
- 売上／返品／値引、返品税の符号、入金内訳、累計残、`PayMonth`／`PayDay` による入金予定日を実装した。
- 通常再計算では既存の `SeikyuNo`／`Renban` を維持し、未採番時は連番1で採番するようにした。
- 請求残の期間・内訳・累計残・採番維持・予定日を検証するテストを追加した。

### 確認
- `Tests/TestServer/TestServer.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。
- Microsoft.Testing.Platform の実行形式から `SummaryKakeDbTests` を実行し、11件すべて成功した。

## [2026-08-18] 請求・支払計算の請求残／支払残スキーマを追加

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 請求計算・支払計算の詳細設計に基づき、請求書番号・連番・入金予定日・支払予定日を保持できるようにする。

### 実施内容
- `SummaryUriSei` に `SeikyuNo`、`Renban`、`NyukinYoteiDay` を追加した。
- `SummaryKaiShi` に `ShiharaiYoteiDay` を追加した。
- `UpdateDb` の `26_08_18_02` で既存DBへ同列を追加し、既存行は空文字または0で初期化する。

### 確認
- `CvBase/CvBase.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。

## [2026-08-18] 請求・支払計算の集計・締日ルールを詳細設計へ反映

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 請求・支払計算と掛月次集計について、締日、対象期間、区分別内訳、累計残高、税率・丸めのユーザー決定を詳細設計へ反映する。

### 実施内容
- 得意先・仕入先の締日をそれぞれ `MasterTokui.Shime1` / `MasterShiire.Shime1` と明記し、`PayMonth`/`PayDay` は予定日の算出専用とした。
- `SummaryUriKake` / `SummaryKaiKake`、`SummaryUriSei` / `SummaryKaiShi` の区分別集計、合計式、対象期間分のみを保持する内訳、対象期間までの累計 `Balance` を明文化した。
- 税は取引の `Tax` を集計し、返品は `CalcFlag` により負値とすること、1.0の新規税額算出は `MasterSysMan` の `No=1` の `Tax` と四捨五入を使うことを記載した。
- 通常再計算・Rebuild時の採番維持と、明示的再発行時だけの `Renban` 増加を明記した。

### 確認
- Markdownの見出し・表・用語を確認し、`git diff --check` を実行する。
- 文書のみの変更のため .NET build/test は省略する。

## [2026-08-18] CV10機能完成度チェックリストを現行メニュー基準で再作成

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 2026-08-12版の機能完成度チェックリストを履歴として保存し、最新コミットと `CvWpfclient/Models/MenuData.cs` を基準に2026-08-18版を新設する。
- 実装済み機能の詳細列挙から、1.0/1.1/1.2以降の計画、必要な仕様決定、最小UAT、旧CV.net機能の継承計画を中心とする文書へ改める。
- 後続担当（Luna等）が、請求・支払計算から設計・実装・検証へ着手できる引継ぎ粒度にする。

### 調査根拠
- HEAD `c0dc00c`（請求計算・支払計算の詳細設計。実装未着手）。
- `CvWpfclient/Models/MenuData.cs` の16大メニュー、232表示参照、重複を除く212 View。
- `Doc/spec/2026-08-18_請求計算・支払計算_詳細設計.md` と2026-08-18付の各詳細設計。
- `Doc/spec/2026-08-17_旧cvnet比較_仕様決定判断材料.md`、未適用・保留課題台帳。
- `refer/cvnet-knowlege/` のマニュアル、業務フロー、帳票集、DB定義の既存調査結果。

### 実施内容
- `Doc/spec/2026-08-18_CV10機能完成度チェックリスト.md` を新設した。
- 2章を現行メニューの規模、1.0完成までの断絶、期別ロードマップ、優先順位へ変更した。
- 3章を16大メニュー別に再構成し、各領域を「現在地 / 1.0実装予定 / 1.1予定 / 1.2以降・決定」で整理した。
- 4章以降を大幅に再構成し、請求・支払詳細設計の7段階、月次・原価・残高登録、15件の仕様決定、最小UAT 9シナリオ、旧CV.net資料別の期別計画、後続担当の開始/停止条件、既知リスクを記載した。
- 請求のDELETE→再作成による冪等性と、再実行時の`Renban+1`が競合する点をD-03として明示し、通常再計算と明示的再発行の分離を推奨した。
- ユーザー指示により、LCVの全9画面・移行・セキュリティ審査・UATを1.1へ配置した。
- ユーザー指示により、最大3締日と月末以外締日の税対応を1.2以降へ配置し、1.1の締日更新は1得意先1締日の運用制御として残した。
- 2026-08-12版チェックリストは変更せず履歴として保持した。

### ログアーカイブ
- 旧 `Doc/aicoding_log.md`（945行）を `Doc/aicoding_log_013.md` へ移動した。
- アーカイブ作成後、今回分だけを記載した新しい `Doc/aicoding_log.md` を作成した。

### 確認
- 文書内の大メニュー名と表示参照数を `MenuData.cs` から再集計した。
- LCVが1.1、最大3締日が1.2以降で一貫していることを検索確認した。
- Markdown見出し、表、相対リンク、UTF-8、CRLF、`git diff --check` を確認した。
- 文書とログのみの変更であるため、.NET build/testは省略する。

### 残る重要判断
- D-01: 1.0の月次・原価範囲。
- D-03: 通常再計算と請求再発行の採番・冪等性。
- D-05: 適格請求書要件。
- D-06/D-07: 総平均原価の`TQ`と月次処理順。
- D-08/D-09: 期首残高移行とMini-UAT責任者。
- D-14: 1.1 LCVの個人情報、ポイント会計、返品・失効、移行責務。
## [2026-08-18] McpOracleをOracle接続用MCPサーバとして追加

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- `McpSql` と同じ stdio MCP サーバ構成で、Oracle 接続文字列を引数または環境変数から受け取り、スキーマ参照・照会・任意時の更新を行える `McpOracle` プロジェクトを追加する。

### 設計・実装
- `McpOracle/McpOracle.csproj` を新設し、既存の中央パッケージ管理から `ModelContextProtocol.Core` と `Oracle.ManagedDataAccess.Core` を参照する。
- 起動引数の第1非オプション引数、または `MCPORACLE_CONNECTION_STRING` を接続文字列として利用する。`--allow-write` を指定しない既定時には更新ツールを MCP に公開しない。
- `list_tables` / `describe_table` / `list_indexes` は `USER_OBJECTS`、`USER_TAB_COLUMNS`、`USER_CONSTRAINTS`、`USER_INDEXES` などの `USER_*` データディクショナリを使用し、接続ユーザー所有のオブジェクトに限定する。
- `query` は単文の `SELECT` または `WITH ... SELECT` に限定し、行数・応答サイズ・セルサイズを上限管理する。値は `:p0`、`:p1` 形式でバインドする。
- `explain` は `EXPLAIN PLAN FOR` と `DBMS_XPLAN.DISPLAY()` を使用する。DDL は `DBMS_METADATA.GET_DDL` が許可されない環境でも、列・制約情報を返せるようにする。
- 接続文字列・パスワードをログや応答へ出力しない。読取り時の SQL 検証に加え、Oracle アカウント権限を最終的なアクセス制御境界とする。
- `creativevision10.slnx` に `McpOracle` を追加した。

### 確認
- `C:\gitroot\UT\vscmd.bat dotnet build McpOracle\McpOracle.csproj` 成功（警告 0、エラー 0）。
- 引数なし起動で、接続文字列の指定方法を stderr に表示して終了することを確認した。
- `git diff --check` を実行した。

### 使用例
- `McpOracle.exe "Data Source=192.168.9.243/cvnet;User Id=CV00PKG;Password=CV00PKG;"`
- 環境変数: `MCPORACLE_CONNECTION_STRING` に同じ接続文字列を設定して `McpOracle.exe` を起動する。
- 更新を許可する場合: 上記に `--allow-write` を追加する。
