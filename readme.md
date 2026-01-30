# プロジェクトの目的

サーバ、クライアント間をgRPCで接続し、本格的なオープンソース販売管理パッケージとして公開することを目的としています。

# 目次

- [ソリューション概要](#ソリューション概要)
- [プロジェクト別概要](#プロジェクト別概要)
  - [CodeShare](#CodeShare)
  - [Cvnet10AppShared](#Cvnet10AppShared)
  - [Cvnet10Base](#Cvnet10Base)
  - [Cvnet10Server](#Cvnet10Server)
  - [Cvnet10Wpfclient](#Cvnet10Wpfclient)

# ソリューション概要

本ソリューションは、販売管理ドメインを gRPC ベースで分散実装するための統合環境です。
契約定義（CodeShare）、共通ドメインロジック（Cvnet10Base）、gRPC サーバ（Cvnet10Server）、GUI クライアント（Cvnet10Wpfclient）が役割分担し、
.NET 10 / C# 14 の最新機能と ProtoBuf.Grpc / CommunityToolkit.Mvvm などの NuGet パッケージで構築されています。
サーバー-クライアント間の通信は共通 DTO と JSON/ProtoBuf シリアライズを併用し、将来的な OSS 販売管理パッケージ公開を見据えたスケーラブルなアーキテクチャになっています。

# 使用するフレームワーク等

protobuf-net.Grpc protobuf-net.Grpc.AspNetCore Grpc.Net.Client : サーバー・クライアント間通信の基盤として採用。高速かつ型安全な RPC を実現。
CommunityToolkit.Mvvm : クライアント側の UI ロジックを整理し、保守性を向上。
NPoco : 軽量ORM として利用し、データベース操作の効率化とコードの簡潔化を図る。
Newtonsoft.Json : Microsoft のNugetパッケージではなくNewtonsoft.Json を使ってください。
Microsoft.AspNetCore.Authentication.JwtBearer : サーバーの認証基盤として利用し、セキュリティを強化。


# プロジェクト別概要

## CodeShare
- gRPC コントラクト（サービス/メッセージ）をコードファーストで定義。
- サーバー Cvnet10Server とクライアント Cvnet10Wpfclient の双方が参照し、完全な型安全通信を担保。
- 変更時はサーバー/クライアント両方の再ビルドが必要なため、契約の互換性管理が重要。

## Cvnet10AppShared
- 複数プロジェクトに共通して使われるclass定義や処理
- 極力依存性を減らし軽量にする


## Cvnet10Base
- 共通モデル、NPoco ベースの DB エンティティ、JSON シリアライズユーティリティ、ExDatabase などの汎用ロジックを提供。
- CommunityToolkit.Mvvm を活用した `ObservableObject` / `[ObservableProperty]` により、UI/サーバ双方で同一モデルを活用可能。
- 変換バッチ（`ConvertDb`）や DTO シリアライズ（`Common.SerializeObject`）など、全レイヤ共通の基盤を担う。

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








