# CV10 エージェント作業規約

## 1. 基本方針

- 分業時または明示された大規模設計案件では、[`handoff.md`](handoff.md) のファイル所有権、レビュー条件、引継ぎ形式に従う。
- 要件・DBスキーマ・公開API・既存業務動作を実質的に変える作業は、調査結果、TODO、影響範囲、未決事項を提示し、承認後に実装する。明確に限定された修正は、簡潔な計画を示して実装してよい。
- 説明、計画、ソースコメント、ログは日本語で記載する。日本語テキストは UTF-8 BOM とする。
- 実装前に計画を提示し、Step ごとに合意を取って進める。実作業は subagent（Sonnet / Haiku）へ委任し、こちらは監督とレビューを行う。
- 報告は要点のみとし、確認質問を重ねない。
- ユーザーに渡すコマンドは PowerShell 構文で書く。`VAR=value cmd` は通らない。Python の UTF-8 出力は `-X utf8` を付ける。

## 2. 実装制約

- `printform/*.qfm` は Shift_JIS（cp932）を維持する。
- 不要な依存性注入、新規フレームワーク、テスト専用の実行プログラムは追加しない。必要な場合は根拠と影響を計画に明記する。
- ビルド等では `DOTNET_ENVIRONMENT=Development` および `ASPNETCORE_ENVIRONMENT=Development` を使用する。

## 3. アーキテクチャとデータ不変条件

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
- WPF変更では先に `App.xaml` と該当リソースを確認し、既存の View / ViewModel / 共有スタイルを踏襲する。

## 4. 検証

- ビルドは `C:\gitroot\UT\vscmd.bat dotnet build <対象.slnx|csproj>` で実行する。
- WPF変更は XAML/XML の妥当性、バインディング、対象プロジェクトのビルドを確認する。画面表示や操作を変更した場合は、可能なら実行時確認も行う。
- CvWpfclientは、違うフォルダからだとresourceエラーで起動できないためテスト実行は注意。
- UAT は ViewModel 駆動で自動化する。見た目の確認以外で人手の GUI 操作を前提にしない。シナリオは `Doc/test/UatVm` に追加し、画面を直したら `dotnet build Doc/test/UatVm/UatVm.csproj` も実行してから流す。
- `printform/*.qfm` 変更は SQL の別名と `itemN` の対応を検証する。

## 5. 記録と Git

- 実装・設定・運用文書を変更した作業は `Doc/aicoding_log.md` の先頭に所定形式で追記する。ただし軽微な変更やドキュメントのみの変更はログ不要。800行を超える場合は番号付きアーカイブへ退避する。
- コミット本文はリポジトリ既定の形式と JST の作業時間を記載する。
- `master` で直接作業する。作業ブランチは作らない。push は指示があったときだけ行う。
- 多行のコミットメッセージは `git commit -F <ファイル>` で渡す。Bash 経由で PowerShell の here-string を使わない（本文に余計な文字が混入する）。

## 6. 調査ツール（graphify）

- コードベースに関する問いは、まず `graphify query "<質問>"` を使う。関係は `graphify path "<A>" "<B>"`、個別概念は `graphify explain "<概念>"`。生の grep より狭い部分グラフが返る。
- 広い把握は `graphify-out/wiki/index.md` を使い、それでも足りないときだけ `graphify-out/GRAPH_REPORT.md` を読む。
- **`graphify update` は実行しない。** グラフの更新は人間側が最後に行う。
