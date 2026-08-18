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
