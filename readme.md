# プロジェクトの目的

このプロジェクト`cvnet10`は、正式名称 `Creative Vision 10`として、アパレル会社向け販売管理ドメインを対象としたオープンソースパッケージの開発を目的としています。
アパレル業界では、販売管理システムの導入が遅れているケースが多く、特に中小企業ではコストや技術的なハードルが高いことが課題となっています。
`Creative Vision 10`は、これらの企業が安心して導入できる本格的な基幹業務ソリューションを提供することを目指しています。
3Tier-System アーキテクチャを採用し、サーバ、クライアント間はHTTP2.0/gRPCで接続、プレゼンテーション層にはWPF MVVMを採用しています。
https://github.com/sekiya-sato/cvnet10
AIアシスタントは `AGENTS.md` を参照してください。
インストールガイドは別途 `INSTALL.md` を用意する予定です。


# 目次

- [ソリューション概要](#ソリューション概要)
- [特徴・メリット](#特徴・メリット)
- [プロジェクト別概要](#プロジェクト別概要)
  - [CodeShare](#CodeShare)
  - [Cvnet10Asset](#Cvnet10Asset)
  - [Cvnet10Base](#Cvnet10Base)
  - [Cvnet10BaseMariadb](#Cvnet10BaseMariadb)
  - [Cvnet10BaseOracle](#Cvnet10BaseOracle)
  - [Cvnet10BaseSqlite](#Cvnet10BaseSqlite)
  - [Cvnet10DomainLogic](#Cvnet10DomainLogic)
  - [Cvnet10Prints](#Cvnet10Prints)
  - [Cvnet10Server](#Cvnet10Server)
  - [Cvnet10Wpfclient](#Cvnet10Wpfclient)
  - [Tests.*](#Tests.*)


# ソリューション概要 Cvnet10.slnx

本ソリューションは、販売管理ドメインを gRPC ベースで分散実装するための統合環境です。  
契約定義（CodeShare）、共通ロジック（Cvnet10Base / Cvnet10DomainLogic）、gRPC サーバ（Cvnet10Server）、WPF クライアント（Cvnet10Wpfclient）、テスト（Tests.*）で構成されています。  
`.NET 10 / C# 14` を前提に、`protobuf-net.Grpc`、`CommunityToolkit.Mvvm`、`NPoco`、`Newtonsoft.Json` を利用しています。  
NuGet のバージョンは `Directory.Packages.props` で集中管理し、依存関係の整合性を担保しています。  
ソリューション項目として `readme.md` を保持しています。

# 特徴・メリット (Creative Vision 10 の十のメリット)

壱. **ライセンスコストの大幅削減**
	Oracle、Windows Server、Biz/Browser などの商用基盤への依存を減らし、DB・サーバOS・業務実行基盤にかかるライセンス費を大きく抑えられる。
弐. **オープンな技術基盤への移行**
	従来のクローズドな構成から、.NET 10、SQLite、MariaDB、GitHub などを活用したオープンな構成へ移行できる。
参. **将来にわたり保守しやすい**
	特定ベンダーや旧来技術への依存を減らすことで、今後の技術更新や人材確保、保守継続がしやすくなる。
肆. **通信性能の向上**
	HTTP/2 + gRPC への移行により、従来より高速で効率のよい通信が可能になり、全体の応答性向上が期待できる。
伍. **画面の操作性と表現力の向上**
	クライアントを Biz/Browser から WPF に移すことで、画面デザインの自由度が増し、操作性や表示性能の改善が見込める。
陸. **既存ユーザーが移行しやすい**
	これまでのCV利用実績を踏まえた再設計のため、既存ユーザーにとって移行しやすい業務システムとして展開しやすい。
漆. **業務機能の充実**
	アパレル販売管理に必要な実務機能が長年蓄積されており、オープンソース化しても業務で使える完成度を維持できる。
	標準機能が充実しているため、個別開発を最小限にして導入できる可能性が高い。
捌. **大規模運用の実績がある**
	大規模データ、多店舗運用、長年の導入実績があり、基幹業務システムとしての信頼性を訴求できる。
玖. **長く使える基幹システムを目指せる**
	オープン技術を採用することで、特定製品の終了リスクを下げ、より広く、より長く使える基幹システムとして育てやすい。
拾. **SaaS展開との相性がよい**
	オープンで保守しやすい構成にすることで、協力会社を含めたSaaS型提供や月額課金モデルにも展開しやすくなる。
	月額課金体系 サーバインフラ費/サーバ保守費/ソフトウェア保守費/サポート保守費 など


# プロジェクト別概要

## CodeShare
- gRPC コントラクト（サービス/メッセージ）をコードファーストで定義。
- サーバー `Cvnet10Server` とクライアント `Cvnet10Wpfclient` が参照し、型安全通信を担保。

## Cvnet10Asset
- 複数プロジェクトに共通して使われる軽量ユーティリティ、定数、補助クラスを集約。
- 依存性を極力減らし、基盤層として安定性を重視。

## Cvnet10Base
- 共通モデル、NPoco ベースの DB エンティティ、基底インフラを提供。
- `CommunityToolkit.Mvvm` を利用した共通モデルの再利用を前提。

## Cvnet10BaseMariadb
- MariaDB 向けの DB 接続/拡張 NPoco 実装を提供。

## Cvnet10BaseOracle
- Oracle 向けの DB 接続/拡張 NPoco 実装を提供。

## Cvnet10BaseSqlite
- SQLite 向けの DB 接続/拡張 NPoco 実装を提供。

## Cvnet10DomainLogic
- ExDatabase（汎用 DB I/F）とドメインロジック、変換バッチなどを提供。
- ビジネスロジックの実装をこの層に集約。

## Cvnet10Prints
- 印刷関連のロジックやテンプレートを提供するプロジェクト。
- プロジェクトファイルの`PrintEnable`が`true`の場合は印刷機能が有効、`false`の場合は無効になる。
- 印刷機能が有効の場合は`printstream.jar` をプロジェクトフォルダルートに配置する。

## Cvnet10Server
- gRPC サーバアプリ。`CvnetCoreService` が `ICvnetCore` を実装して API を公開。Tableに対するCRUD操作を提供。
- JSON シリアライズ設定（`JsonSerializerSettings`）を共通化し、`protobuf-net.Grpc` と併用。
- `Microsoft.AspNetCore.Authentication.JwtBearer` による認証基盤を利用。

## Cvnet10Wpfclient
- `CommunityToolkit.Mvvm` を利用した WPF クライアント。
- `Cvnet10Server` の gRPC API を呼び出し、販売管理のマスタメンテ、受発注、仕入、売上、移動、棚卸しなどの業務機能を提供。

## Tests.*
- テスト用プロジェクト（ユニット/結合テストの実装場所）。

