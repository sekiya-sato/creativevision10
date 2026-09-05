# CV10 エージェント作業規約

## 1. 適用範囲と優先順位

- 対象リポジトリは CreativeVision10（`creativevision10.slnx`）である。
- 指示の優先順位は、ユーザーの明示指示、実行環境・安全上の制約、本書、`handoff.md`、個別エージェントの判断とする。
- 本書はプロジェクト全体の規約、[`handoff.md`](handoff.md) は複数エージェント作業の役割・引継ぎ・競合回避の規約である。並列作業または設計・実装・レビューを分担する作業では、着手前に両方を確認する。

## 2. 作業の基本原則

- 既存の実装・設計・未コミット変更を尊重し、依頼範囲外のリファクタリングを混在させない。
- 着手時と完了前に `git status` と対象差分を確認する。既存の変更・未追跡ファイルはユーザーの所有物として扱い、勝手に削除、上書き、stash、reset しない。
- 要件・DBスキーマ・公開API・既存業務動作を実質的に変える作業は、調査結果、TODO、影響範囲、未決事項を提示し、承認後に実装する。明確に限定された修正は、簡潔な計画を示して実装してよい。
- 一度に進行中にする作業は一つだけとする。複数作業を分担する場合は `handoff.md` のファイル所有権と引継ぎ形式に従う。
- 説明、計画、ソースコメント、ログは日本語で記載する。日本語テキストは UTF-8 とする。
- 検索は原則 `rg` を用い、日本語を含む検索語・出力の文字コードを確認する。
- 調査は必要ファイルに限定し、`bin/`、`obj/`、`generated/` および生成済み gRPC C# は原則読まず編集しない。巨大なログは全件出力せず、対象箇所を絞る。
- 変更は minimal diff とし、検証は影響する最小プロジェクトの build/test から行う。未変更プロジェクトの毎回の build/test は行わない。

## 3. 技術基盤と編集規約

- スタック: .NET 10 / C# 14 / protobuf-net.Grpc / WPF（MVVM、CommunityToolkit）/ SQLite 3.46 以降。
- 変更・作成するファイルの改行は CRLF に統一する。`printform/*.qfm` は Shift_JIS（cp932）、それ以外の日本語テキストは UTF-8 とする。
- C# は `.editorconfig`、XAML は `Settings.XamlStyler` に従い、名前空間は file-scoped を優先する。
- 不要な依存性注入、新規フレームワーク、テスト専用の実行プログラムは追加しない。必要な場合は根拠と影響を計画に明記する。
- PowerShell でファイルを扱う場合は UTF-8 の入出力を明示する。ビルド等では `DOTNET_ENVIRONMENT=Development` および `ASPNETCORE_ENVIRONMENT=Development` を使用する。

## 4. アーキテクチャとデータ不変条件

- 依存方向は `CodeShare` / `CvAsset`（層0）→ `CvBase`（層1、DB 1.2、Prints 1.4）→ `CvDomainLogic`（1.5）→ `CvServer`（2）とする。クライアントは層0 → 層1 → `CvWpfclient`（2）とする。
- `Id_*` と対になる `V*`（`CodeNameView`、`[SerializedColumn]`）はテーブル種別で意味が異なる。
  - `Tran*` の `V*` は伝票時点の監査値であり、マスタ改名時に伝播しない。`[ComputedColumn]` 化、伝播対象化、現行マスタJOINへの置換はしない。
  - `Master*` / `Sys*` / `Derived*` の `V*` は現行名称である。追加時は `MasterCascadeDb.VRules` に登録し、JSONスナップショットも伝播対象を確認する。
  - JSON を扱う SQLite SQL は `json_valid()` または `MasterCascadeDb.SafeJsonColumn` / `JsonArrayReady` で不正JSONを防御する。設計根拠は `.omo/20260727_master_vcolumn_sync_design.md` を参照する。
- SQL は **SQLite 方言を正典** とする。`CvWpfclient` 側で SQL を組み立てる現行ルールは維持し、PostgreSQL / MariaDB へは `CvBase/Sql` の方言変換器が実行時に変換する。設計は `.omo/2026-08-25_sql_dialect_translator_detail_design.md` を参照する。
  - **SQLite の実行経路は変えない。** SQLite では方言変換が恒等（`ISqlDialect.TranslatesSql` が false で呼び出し側が短絡）になる。既存 SQL を他DB互換に書き換える改修は行わない。
  - 使える構文は `CvBase/Sql/SqliteConstructCatalog.cs` に登録済みのものに限る。新しい SQLite 固有構文を使うときは、変換ルール（`CvBase/Sql/Rules/`）を足すか、`QueryKey` と `SqlOverrideCatalog` で方言別の手書き SQL へ差し替える。`Tests/TestSqlDialect` の静的検査が対象外の構文をファイル/行付きで指摘する。
  - サーバ側で DB 間の構文差がある SQL は、SQLite 方言のまま書いて `ExDatabase.ExecuteDialect` / `FetchDialect` 経由で実行する。DB 別に書き分けない。
  - 意味差（MariaDB の整数除算、PostgreSQL の `GROUP BY` 厳格化、集約の戻り型）は変換器では直せない。整数結果を意図する除算は `CAST(... AS INTEGER)` で包むなど、**SQLite で結果が変わらない書き方**に寄せる。
  - 下限バージョンは SQLite 3.38 / MariaDB 10.11 LTS / PostgreSQL 16。MariaDB の照合順序は `utf8mb4_bin`、PostgreSQL は `LC_COLLATE=C` で作成する。起動時に検証し、SQLite 以外は不足なら起動失敗させる。
- WPF変更では先に `App.xaml` と該当リソースを確認し、既存の View / ViewModel / 共有スタイルを踏襲する。必要なときは `.agents/skills/wpf-project-guide`、`check-xaml-layout`、`wpf-view-workflow` など該当スキルを読む。

## 5. 調査、実装、検証

1. 依頼、既存差分、対象コード、影響範囲を確認する。
2. 必要なスキルを `.agents/skills/` から選び、その `SKILL.md` を読んで適用する。
3. 最小変更を実装し、CRLF、差分、関連する静的検査を確認する。
4. 変更範囲に見合う最小の検証を実施する。共有基盤、DB、公開API、認可、印刷形式の変更は検証範囲を広げる。

基本コマンド（Windows の Developer Command Prompt 経由）:

```text
C:\gitroot\UT\vscmd.bat dotnet build creativevision10.slnx
C:\gitroot\UT\vscmd.bat dotnet build CvServer\CvServer.csproj
C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient\CvWpfclient.csproj
```

- WPF変更は XAML/XML の妥当性、バインディング、対象プロジェクトのビルドを確認する。画面表示や操作を変更した場合は、可能なら実行時確認も行う。
- `printform/*.qfm` 変更は cp932 を維持し、SQLの別名と `itemN` の対応を検証する。
- 完了前に `git diff --check` を実行し、実行できなかった検証は理由と残余リスクを明記する。

## 6. 記録と Git

- 実装・設定・運用文書を変更した作業は `Doc/aicoding_log.md` の先頭に所定形式で追記する。ただし軽微な変更やドキュメントのみの変更はログ不要。800行を超える場合は番号付きアーカイブへ退避する。
- コミット、rebase、merge、push はユーザーが明示した場合だけ実行する。コミット本文はリポジトリ既定の形式と JST の作業時間を記載する。
- コミット対象は依頼に直接関係するファイルと作業ログに限定する。


## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

When the user types `/graphify`, use the installed graphify skill or instructions before doing anything else.

Rules:
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- Dirty graphify-out/ files are expected after hooks or incremental updates; dirty graph files are not a reason to skip graphify. Only skip graphify if the task is about stale or incorrect graph output, or the user explicitly says not to use it.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).
