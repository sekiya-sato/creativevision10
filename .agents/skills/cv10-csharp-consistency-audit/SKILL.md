---
name: cv10-csharp-consistency-audit
description: Audit cv10 C# subprojects such as CvBase, CvDomainLogic, CvServer, CvAsset, and CodeShare for consistency, naming drift, code-style drift, layering risks, and safe compatibility-preserving cleanup plans. Use when the user asks to 精査 a C# project for 整合性, 命名規則, コード記述のブレ, or asks for a human-actionable implementation plan without immediate source edits.
---

# cv10 C# Consistency Audit

このスキルは `C:\gitroot\new2022\cv10` の C# サブプロジェクトを、修正前に監査し、人間が作業できる `.omo` 文書へ落とすための手順です。

## 原則

- まず監査。ユーザーが実装を明示するまでソース修正しない。
- 説明、計画、文書は日本語で書く。
- `Doc/aicoding_log.md` と commit は、ユーザーが明示した時だけ行う。
- `.omo` は作業メモ・計画の置き場。`.sisyphus` と `.omo` は commit 対象にしない前提で扱う。
- 共有契約・DB列・既存DTOの名前を変える時は、旧名を `[Obsolete]` alias として残すことを第一候補にする。
- DB物理列・DDL・シリアライズ名の変更は、既存DBと呼び出し側を確認してから別段階に分ける。

## 事前確認

```powershell
git status --short
git rev-parse HEAD
Get-Content graphify-out\GRAPH_REPORT.md -TotalCount 120
```

- `GRAPH_REPORT.md` の built commit と HEAD が一致しない場合、graphify は参考扱いにし、実ファイル確認を優先する。
- 未コミット変更がある場合、ユーザー変更を戻さない。対象外なら無視し、対象内なら内容を読んで作業する。
- ユーザーが「log, commit不要」と言った場合、`Doc/aicoding_log.md` 追記と commit は行わない。

## 調査観点

### 構成

- `rg --files <ProjectName>` で対象ファイルを一覧する。
- `.csproj` の TargetFramework、PackageReference、ProjectReference を確認する。
- solution 上の位置と参照方向を確認する。
- `bin/obj` や画像など、ソース以外を監査対象から外す。

### 命名

重点検索:

```powershell
rg -n "Reflesh|Histry|Genger|Decript|NoUse|TODO|ToDo|NotImplemented|Param.*NoUse|byId|tableName|tranId|calcFlag" <ProjectName>
rg -n "public (class|record|interface|enum)|public sealed|public partial|namespace " <ProjectName> --glob "*.cs"
```

見るポイント:

- typo らしい英字名。
- PascalCase / camelCase の混在。
- 型名とファイル名の不一致。
- record class / class / sealed の粒度差。
- `Async` 接尾辞、CancellationToken 名、引数名の揺れ。
- コメントの日本語/英語併記や説明粒度の差。

### 整合性

見るポイント:

- コメントと実処理が逆、または古い。
- 初期化リストと属性定義の差。
- `if (isForce)` など条件により新規環境で動かない箇所。
- `Wait()` / `.Result` による同期ブロック。
- transaction の rollback 漏れ。
- 例外処理で握りつぶし、ログだけ、または状態更新漏れ。
- SQL文字列の直結、JSON列の扱い、DB依存SQLの混在。
- Lower layer から upper layer への参照がないか。

### 互換性

公開型や共有DTOの変更候補は、必ず横断検索する。

```powershell
rg -n "<TypeOrMemberName>" .
```

推奨パターン:

- canonical 名を追加する。
- 旧名は `[Obsolete("Use NewName.")]` で残す。
- DB列名や JSON property 名は初回で変えない。
- 呼び出し側は段階的に新名へ移す。

## .omo 文書の構成

文書名の例:

- `.omo/cvbase_consistency_impl_plan_2026-06-04.md`
- `.omo/cvdomainlogic_consistency_impl_plan_2026-06-04.md`
- `.omo/cvserver_consistency_impl_plan_2026-06-04.md`

推奨構成:

```markdown
# <ProjectName> 整合性精査・実装計画 (YYYY-MM-DD)

## 目的
## 現状メモ
## 守る前提
## 主要な問題一覧
### P0: ...
対象:
根拠:
影響:
実装方針:
確認:
## 推奨実装順
## 作業手順案
## 検証コマンド
## 完了条件
```

優先度:

- P0: 実害、初期化失敗、履歴不整合、データ破損、セキュリティ、運用停止リスク。
- P1: 共有契約の命名揺れ、互換 alias が必要な修正、保守性に強く影響する設計差。
- P2: コメント退避、スタイル統一、低リスクな記述整理。

## 検証計画

対象単体:

```powershell
C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build <ProjectName>/<ProjectName>.csproj"
```

関連プロジェクトも必要に応じて追加する。

- `CvBase`: `CvBaseSqlite`, `CvBaseMariadb`, `CvBaseOracle`, solution。
- `CvDomainLogic`: `CvBase`, `CvBaseSqlite`, `CvServer`, `Tests/TestServer`。
- `CvServer`: `CvBase`, `CvDomainLogic`, `CvBaseSqlite`, `Tests/TestServer`, solution。

この repo では `Tests/TestServer` は以下を優先する。

```powershell
dotnet run --project Tests/TestServer/TestServer.csproj
```

`CvServer` build で DLL copy lock が出た場合は、実行中サーバのロックを疑い、検証用に `-p:OutputPath=obj\CodexBuildOutput\` を使う。

## 仕上げ

- `.omo` 文書の CRLF / UTF-8 No BOM を整える。
- `git status --short` で、ソース修正が混ざっていないか確認する。
- ユーザーが log / commit 不要とした場合、最終回答では「未実施」と明記する。
