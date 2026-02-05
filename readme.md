# プロジェクトの目的

サーバ、クライアント間をgRPCで接続し、本格的なオープンソース販売管理パッケージとして公開することを目的としています。
AIアシスタントはこのファイルではなく .github/copilot-instructions.md を参照してください。


# 目次

- [ソリューション概要](#ソリューション概要)
- [プロジェクト別概要](#プロジェクト別概要)
  - [CodeShare](#CodeShare)
  - [Cvnet10Asset](#Cvnet10Asset)
  - [Cvnet10Base](#Cvnet10Base)
  - [Cvnet10DomainLogic](#Cvnet10DomainLogic)
  - [Cvnet10Server](#Cvnet10Server)
  - [Cvnet10Wpfclient](#Cvnet10Wpfclient)


# ソリューション概要 Cvnet10.slnx

本ソリューションは、販売管理ドメインを gRPC ベースで分散実装するための統合環境です。
契約定義（CodeShare）、共通ドメインロジック（Cvnet10Base, Cvnet10DomainLogic）、gRPC サーバ（Cvnet10Server）、GUI クライアント（Cvnet10Wpfclient）が役割分担し、
.NET 10 / C# 14 の最新機能と ProtoBuf.Grpc / CommunityToolkit.Mvvm などの NuGet パッケージで構築されています。
サーバー-クライアント間の通信は共通 DTO と JSON/ProtoBuf シリアライズを併用し、将来的な OSS 販売管理パッケージ公開を見据えたスケーラブルなアーキテクチャになっています。


# プロジェクト別概要

## CodeShare
- gRPC コントラクト（サービス/メッセージ）をコードファーストで定義。
- サーバー Cvnet10Server とクライアント Cvnet10Wpfclient の双方が参照し、完全な型安全通信を担保。
- 変更時はサーバー/クライアント両方の再ビルドが必要なため、契約の互換性管理が重要。

## Cvnet10Asset
- 複数プロジェクトに共通して使われるclass定義や処理
- 極力依存性を減らし軽量にする


## Cvnet10Base
- 共通モデル、NPoco ベースの DB エンティティ、ORマッパー などを提供。
- CommunityToolkit.Mvvm を活用した `ObservableObject` / `[ObservableProperty]` により、UI/サーバ双方で同一モデルを活用可能。
- Cvnet10Base[MariaDb|Oracle|Sqlite] として DB 固有の実装を分離。


## Cvnet10DomainLogic
- ExDatabase (Sqlite, 汎用DB I/F)、ドメインロジック、変換バッチなどを提供。
- ビジネスロジックの実装はこの層に集約。


## Cvnet10Server
- gRPC サーバアプリ。`CvnetCoreService` が `ICvnetCore` を実装し、API を公開。
- JSON シリアライズ設定 (`JsonSerializerSettings`) を共通化し、ProtoBuf.Grpc と組み合わせて双方向通信を実現。
- 今後は売上トランザクション等の業務ロジックをこの層に実装していく。


## Cvnet10Wpfclient
- CommunityToolkit.Mvvm を利用した WPF クライアント。
- `MainViewModel` が SQLite（`sample.db`）からマスタデータを読み込み、`MainWindow` で `MasterSysman` などを表示。
- 実装済みコマンド（`Test01` など）を拡張することで、GUI 上での業務フロー検証が可能。


# 進捗状況

- 2026/01/29
	- Cvnet10Server, Cvnet10Wpfclient の基本的な gRPC 通信と認証ロジックが完了しています。
- 2026/01/30
	- 概念検証として、ITest202601 , Test202601Service を追加し、WpfClientから Joinを含んだビューを一覧取得
- 2026/02/01-02/03
	- Cvnet10Base を Cvnet10Base, Cvnet10DomainLogic に分割、データ定義とビジネスロジックを分離して実装
	- gRPCサービスにCRUDの汎用基本操作を追加
	- Cvnet10Wpfclient のメインメニューを修正


# ToDo 残作業

