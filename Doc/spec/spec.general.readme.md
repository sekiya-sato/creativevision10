# Cvnet10 プロジェクト仕様書

## 目次

- [プロジェクト概要](#プロジェクト概要)
- [技術スタック](#技術スタック)
- [レイヤー構造](#レイヤー構造)
- [プロジェクト一覧](#プロジェクト一覧)
- [開発環境](#開発環境)
- [フォルダ構成](#フォルダ構成)


# プロジェクト概要

Cvnet10は、gRPCベースの分散アーキテクチャを採用した販売管理パッケージです。

| 項目 | 内容 |
|------|------|
| 目的 | オープンソース販売管理パッケージの公開 |
| アーキテクチャ | WPFクライアント + gRPCサーバ（分散型） |
| 通信方式 | gRPC（Code-first、protobuf-net.Grpc） |

**主な特徴**
- サーバ・クライアント間をgRPCで接続し本格的に分散実装
- 複数データベース対応（SQLite、MariaDB、Oracle）
- WPFによるGUI（MVVMパターン）


# 技術スタック

| カテゴリ | 技術 |
|----------|------|
| ランタイム | .NET 10.0 |
| 言語 | C# 14 |
| 通信 | gRPC（protobuf-net.Grpc） |
| クライアントUI | WPF（CommunityToolkit.Mvvm） |
| ORM | NPoco |
| 認証 | JWT Bearer（Microsoft.AspNetCore.Authentication.JwtBearer） |
| シリアライズ | Newtonsoft.Json |
| ログ | NLog |
| テスト | MSTest + Microsoft.Testing.Platform |
| スタイル | Material Design（MaterialDesignThemes） |


## 主要ライブラリ（集中管理）

|NuGetパッケージ|バージョン|用途|
|---------------|----------|-----|
|CommunityToolkit.Mvvm|8.4.0|MVVMサポート|
|protobuf-net.Grpc|1.2.2|gRPC Code-first|
|Grpc.Net.Client|2.76.0|gRPCクライアント|
|NPoco|6.2.0|ORM|
|Newtonsoft.Json|13.0.4|JSONシリアライズ|
|NLog|6.1.1|ログ出力|
|MaterialDesignThemes|5.3.0|UIスタイル|


# レイヤー構造

本プロジェクトは厳格なレイヤードアーキテクチャを採用しています。

```
mermaid
graph TD
    subgraph Layer2 [Layer 2]
        A[Cvnet10Server<br/>(gRPCサーバ)]
        B[Cvnet10Wpfclient<br/>(WPFクライアント)]
    end
    subgraph Layer15 [Layer 1.5]
        C[Cvnet10DomainLogic<br/>(ビジネスロジック)]
    end
    subgraph Layer12 [Layer 1.2 - Read-Only]
        E[(Cvnet10BaseSqlite)]
        F[(Cvnet10BaseMariadb)]
        G[(Cvnet10BaseOracle)]
    end
    subgraph Layer1 [Layer 1]
        D[(Cvnet10Base<br/>(共通モデル))]
    end
    subgraph Layer0 [Layer 0 - Read-Only]
        H[CodeShare<br/>(gRPC契約)]
        I[Cvnet10Asset<br/>(ユーティリティ)]
    end

    A --> C
    B --> C
    C --> D
    C --> E
    C --> F
    C --> G
    D --> H
    D --> I
    ```

## レイヤー別責任

| Layer | プロジェクト | 責任 | 依存関係 |
|-------|-------------|------|----------|
| 0 | CodeShare | gRPCコントラクト（サービス/メッセージ）の定義 | なし |
| 0 | Cvnet10Asset | 共通ユーティリティ、定数、補助クラス | なし |
| 1 | Cvnet10Base | 共通モデル、DBエンティティ、基底インフラ | なし |
| 1 | Cvnet10BaseSqlite | SQLite向けDB接続（拡張NPoco） | Cvnet10Base |
| 1 | Cvnet10BaseMariadb | MariaDB向けDB接続 | Cvnet10Base |
| 1 | Cvnet10BaseOracle | Oracle向けDB接続 | Cvnet10Base |
| 1.5 | Cvnet10DomainLogic | ビジネスロジック、ドメインサービス | Cvnet10Base |
| 2 | Cvnet10Server | gRPCサービス実装、API公開 | CodeShare, Cvnet10Asset, Cvnet10Base, Cvnet10DomainLogic |
| 2 | Cvnet10Wpfclient | WPF GUI（Views/ViewModels） | CodeShare, Cvnet10Asset, Cvnet10Base |

**読み取り専用プロジェクト**
以下のプロジェクトはAIによる変更禁止（明示的な依頼がある場合のみ）：
- CodeShare
- Cvnet10Asset
- Cvnet10Base
- Cvnet10BaseSqlite
- Cvnet10BaseMariadb
- Cvnet10BaseOracle


# プロジェクト一覧

## Layer 0

### CodeShare
- gRPCコントラクト（サービス/メッセージ）をコードファーストで定義
- サーバ`Cvnet10Server`とクライアント`Cvnet10Wpfclient`が参照
- 型安全通信を担保

### Cvnet10Asset
- 複数プロジェクトで共有される軽量ユーティリティ
- 定数、拡張メソッド、補助クラス
- 依存性を最小限に抑え、基盤層として安定性を重視

## Layer 1

### Cvnet10Base
- 共通モデル、NPocoベースのDBエンティティ
- 基底インフラ，提供
- `CommunityToolkit.Mvvm`を利用した共通モデルの再利用

### Cvnet10BaseSqlite
- SQLite向けのDB接続
- 拡張NPoco実装

### Cvnet10BaseMariadb
- MariaDB向けのDB接続
- 拡張NPoco実装

### Cvnet10BaseOracle
- Oracle向けのDB接続
- 拡張NPoco実装

## Layer 1.5

### Cvnet10DomainLogic
- `ExDatabase`（汎用DB I/F）による抽象化
- ドメインロジック、変換バッチなど
- ビジネスロジックの実装を集約

## Layer 2

### Cvnet10Server
- gRPCサーバアプリケーション
- `CvnetCoreService`が`ICvnetCore`を実装してAPIを公開
- JSONシリアライズ設定の共通化
- JWT Bearer認証基盤

### Cvnet10Wpfclient
- WPFクライアントアプリケーション
- `CommunityToolkit.Mvvm`によるMVVMパターン
- Material Designテーマ

## Tests

### Tests.Cvnet10Server
- Cvnet10Serverのユニット/結合テスト

### TestLogin
- 認証関連のテスト


# 開発環境

## 前提条件

| ツール | バージョン |
|--------|------------|
| .NET SDK | 10.0以上 |
| IDE | Visual Studio 2022 / VS Code + C# Dev Kit |

## ビルドコマンド

```bash
# ソリューション全体ビルド
dotnet build Cvnet10.slnx

# サーバビルド
dotnet build Cvnet10Server/Cvnet10Server.csproj

# WPFクライアントビルド
dotnet build Cvnet10Wpfclient/Cvnet10Wpfclient.csproj /p:EnableWindowsTargeting=true /p:UseAppHost=false

