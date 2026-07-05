# プロジェクトの目的

このプロジェクト **Creative Vision 10** は、アパレル企業向け販売管理ドメインを対象とした、オープンソースパッケージの開発を目的としています。

アパレル業界では販売管理システムの導入が遅れているケースも多く、特に中小企業ではコスト面や技術面のハードルが高いことが課題です。

**Creative Vision 10** は、そうした企業でも安心して導入できる、本格的な基幹業務ソリューションを目指しています。

アーキテクチャには 3-tier system を採用し、データベース層は SQLite、アプリケーションサーバ層は HTTP/2.0 + gRPC、プレゼンテーション層は WPF + MVVM で構成しています。


2000年代半ば、`CV.net` はクラサバ型の `dbMagic` から `Oracle + .NET 2.0 + Biz/Browser V4` へと開発プラットフォームを移し、3-tier 構成の最初のシステムとなりました。
その後 `CV.net` は、アパレル企業を中心に延べ 200 社へ導入されるまでに成長しました。 参考: [3階層基幹業務システム](https://jglobal.jst.go.jp/detail?JGLOBAL_ID=201103030643532794)

2020年代に入ってからは、よりオープンで使いやすいシステムへ生まれ変わるための試行錯誤が続けられました。
**cvnetclient** は、クライアント部分を Biz/Browser から WPF へ置き換えたオープンソースパッケージですが、DB とアプリケーションサーバは従来と同じものを利用していました。

2025年10月に .NET 10 がリリースされ、2026年には AI 開発環境も加速度的に進化し、**Creative Vision 10** の開発も急速に進みました。

2025年11月25日に約 30 ファイルの初期リポジトリを登録して以降、既存の `CV.net` 機能の 8 割をカバーできる状態を目指し、<B><Font Color="Red">2027年1月のリリース</Font></B>に向けて開発を進めています。

(リリース予定は秋頃に再度アナウンスします)

ドキュメント系は [Wiki](https://github.com/sekiya-sato/creativevision10/wiki) へ集約しました。

[アクティビティ](https://github.com/sekiya-sato/creativevision10/activity?)  [Insights](https://github.com/sekiya-sato/creativevision10/pulse)  [Contributors](https://github.com/sekiya-sato/creativevision10/graphs/contributors?)  [Commits](https://github.com/sekiya-sato/creativevision10/graphs/commit-activity)


---

<div style="display: flex; gap: 20px; align-items: center;">
<img alt="cv10-logo" src="Doc/cv10logo01.png" style="margin-left: 30px;width: 10%; height: auto;" />
<img alt="cv10-logo" src="Doc/cv10logo02.png" style="margin-left: 30px;width: 25%; height: auto;" />
<img width="50" height="49" alt="cv10-orange100" src="Doc/cv10-orange100.png" style="margin-left: 30px;" />
</div>

# 目次

- [ソリューション概要](#ソリューション概要)
- [特徴・メリット](#特徴メリット-creative-vision-10-の十のメリット)
- [プロジェクト別概要](#プロジェクト別概要)
  - [CodeShare](#codeshare)
  - [CvAsset](#cvasset)
  - [CvBase](#cvbase)
  - [CvBase-DB](#cvbase-db)
  - [CvDomainLogic](#cvdomainlogic)
  - [CvPrints](#cvprints)
  - [CvServer](#cvserver)
  - [CvWpfclient](#cvwpfclient)
  - [Tests.*](#tests)

# ソリューション概要

本ソリューションは、販売管理ドメインを gRPC ベースで分散実装するための統合環境です。

契約定義（CodeShare）、共通ロジック（CvBase / CvDomainLogic）、gRPC サーバ（CvServer）、WPF クライアント（CvWpfclient）、テスト（Tests.*）で構成されています。

`.NET 10 / C# 14` を前提とし、`protobuf-net.Grpc`、`CommunityToolkit.Mvvm`、`NPoco`、`Newtonsoft.Json` を利用しています。

NuGet パッケージのバージョンは `Directory.Packages.props` で集中管理し、依存関係の整合性を担保しています。

# 特徴・メリット (Creative Vision 10 の十のメリット)

壱. **ライセンスコストの大幅削減**

Oracle、Windows Server、Biz/Browser などの商用基盤への依存を減らし、DB・サーバ OS・業務実行基盤にかかるライセンス費を大きく抑えられます。

弐. **オープンな技術基盤への移行**

従来のクローズドな構成から、.NET 10、SQLite、MariaDB、GitHub などを活用したオープンな構成へ移行できます。

参. **将来にわたり保守しやすい**

特定ベンダーや旧来技術への依存を減らすことで、今後の技術更新や人材確保、保守継続がしやすくなります。

肆. **通信性能の向上**

HTTP/2 + gRPC への移行により、従来より高速で効率のよい通信が可能になり、全体の応答性向上が期待できます。

伍. **画面の操作性と表現力の向上**

クライアントを Biz/Browser から WPF に移行することで、画面デザインの自由度が増し、操作性や表示性能の改善が見込めます。

陸. **既存ユーザーが移行しやすい**

これまでの CV 利用実績を踏まえた再設計により、既存ユーザーにとって移行しやすい業務システムとして展開しやすくなります。

漆. **業務機能の充実**

アパレル販売管理に必要な実務機能が長年蓄積されており、オープンソース化しても業務で使える完成度を維持できます。

標準機能が充実しているため、個別開発を最小限に抑えて導入できる可能性が高くなります。

捌. **大規模運用の実績がある**

大規模データ、多店舗運用、長年の導入実績があり、基幹業務システムとしての信頼性を訴求できます。

玖. **長く使える基幹システムを目指せる**

オープン技術を採用することで、特定製品の終了リスクを下げ、より広く、より長く使える基幹システムとして育てやすくなります。

拾. **SaaS 展開との相性がよい**

オープンで保守しやすい構成にすることで、協力会社を含めた SaaS 型提供や月額課金モデルにも展開しやすくなります。

たとえば、サーバインフラ費、サーバ保守費、ソフトウェア保守費、サポート保守費などを含む月額課金体系とも親和性があります。

# プロジェクト別概要

## CodeShare (Layer 0)

- gRPC コントラクト（サービス / メッセージ）をコードファーストで定義します。
- サーバ `CvServer` とクライアント `CvWpfclient` が参照し、型安全な通信を担保します。

## CvAsset (Layer 0)

- 複数プロジェクトで共通利用する軽量ユーティリティ、定数、補助クラスを集約しています。

## CvBase (Layer 1)

- 共通モデル、NPoco ベースの DB エンティティ、基底インフラを提供します。
- サーバ `CvServer` とクライアント `CvWpfclient` が参照します。

## CvBase-DB (Layer 1.2)

- データベースを共通的に扱うための汎用 DB I/F を提供します。
- `CvBaseSqlite`（SQLite 用）、`CvBaseMariadb`（MariaDB 用）、`CvBaseOracle`（Oracle 用）で構成されています。

## CvDomainLogic (Layer 1.5)

- `ExDatabase`（汎用 DB I/F）とドメインロジック、変換バッチなどを提供します。
- ビジネスロジックの実装をこの層に集約しています。

## CvPrints (Layer 1.4)

- 印刷関連のロジックやテンプレートを提供するプロジェクトです。
- プロジェクトファイルの `PrintEnable` が `true` の場合は印刷機能が有効になり、`false` の場合は無効になります。

## CvServer (Layer 2)

- gRPC サーバアプリです。`CoreService` が `ICoreService` を実装し、API を公開します。
- Table に対する CRUD 操作を提供します。
- JSON シリアライズ設定（`JsonSerializerSettings`）を共通化し、`protobuf-net.Grpc` と併用しています。
- `Microsoft.AspNetCore.Authentication.JwtBearer` による認証基盤を利用しています。

## CvWpfclient (Layer 2)

- `CommunityToolkit.Mvvm` を利用した WPF クライアントです。
- `CvServer` の gRPC API を呼び出し、販売管理のマスタメンテ、受発注、仕入、売上、移動、棚卸などの業務機能を提供します。

## Tests.* (Layer 3)

- テスト用プロジェクトです。
- ユニットテストや結合テストの実装場所として利用します。
