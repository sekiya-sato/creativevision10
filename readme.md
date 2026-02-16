# プロジェクトの目的

サーバ、クライアント間をgRPCで接続し、本格的なオープンソース販売管理パッケージとして公開することを目的としています。
AIアシスタントは `.github/copilot-instructions.md` を参照してください。


# 目次

- [ソリューション概要](#ソリューション概要)
- [プロジェクト別概要](#プロジェクト別概要)
  - [CodeShare](#CodeShare)
  - [Cvnet10Asset](#Cvnet10Asset)
  - [Cvnet10Base](#Cvnet10Base)
  - [Cvnet10BaseMariadb](#Cvnet10BaseMariadb)
  - [Cvnet10BaseOracle](#Cvnet10BaseOracle)
  - [Cvnet10BaseSqlite](#Cvnet10BaseSqlite)
  - [Cvnet10DomainLogic](#Cvnet10DomainLogic)
  - [Cvnet10Server](#Cvnet10Server)
  - [Cvnet10Wpfclient](#Cvnet10Wpfclient)
  - [TestLogin](#TestLogin)


# ソリューション概要 Cvnet10.slnx

本ソリューションは、販売管理ドメインを gRPC ベースで分散実装するための統合環境です。  
契約定義（CodeShare）、共通ロジック（Cvnet10Base / Cvnet10DomainLogic）、gRPC サーバ（Cvnet10Server）、WPF クライアント（Cvnet10Wpfclient）、テスト（TestLogin）で構成されています。  
`.NET 10 / C# 14` を前提に、`protobuf-net.Grpc`、`CommunityToolkit.Mvvm`、`NPoco`、`Newtonsoft.Json` を利用しています。  
NuGet のバージョンは `Directory.Packages.props` で集中管理し、依存関係の整合性を担保しています。  
ソリューション項目として `readme.md` を保持しています。


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

## Cvnet10Server
- gRPC サーバアプリ。`CvnetCoreService` が `ICvnetCore` を実装して API を公開。
- JSON シリアライズ設定（`JsonSerializerSettings`）を共通化し、`protobuf-net.Grpc` と併用。
- `Microsoft.AspNetCore.Authentication.JwtBearer` による認証基盤を利用。

## Cvnet10Wpfclient
- `CommunityToolkit.Mvvm` を利用した WPF クライアント。

## TestLogin
- テスト用プロジェクト（ユニット/結合テストの実装場所）。

