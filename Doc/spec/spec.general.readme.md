# Creative Vision 10 プロジェクト仕様書

## 目次

- [プロジェクト概要](#プロジェクト概要)
- [技術スタック](#技術スタック)
- [レイヤー構造](#レイヤー構造)
- [プロジェクト一覧](#プロジェクト一覧)
- [開発環境](#開発環境)
- [フォルダ構成](#フォルダ構成)


# プロジェクト概要

Creative Vision 10は、gRPCベースの分散アーキテクチャを採用した販売管理パッケージです。

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
        A[CvServer<br/>(gRPCサーバ)]
        B[CvWpfclient<br/>(WPFクライアント)]
    end
    subgraph Layer15 [Layer 1.5]
        C[CvDomainLogic<br/>(ビジネスロジック)]
    end
    subgraph Layer12 [Layer 1.2 - Read-Only]
        E[(CvBaseSqlite)]
        F[(CvBaseMariadb)]
        G[(CvBaseOracle)]
    end
    subgraph Layer1 [Layer 1]
        D[(CvBase<br/>(共通モデル))]
    end
    subgraph Layer0 [Layer 0 - Read-Only]
        H[CodeShare<br/>(gRPC契約)]
        I[CvAsset<br/>(ユーティリティ)]
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
| 0 | CvAsset | 共通ユーティリティ、定数、補助クラス | なし |
| 1 | CvBase | 共通モデル、DBエンティティ、基底インフラ | なし |
| 1 | CvBaseSqlite | SQLite向けDB接続（拡張NPoco） | CvBase |
| 1 | CvBaseMariadb | MariaDB向けDB接続 | CvBase |
| 1 | CvBaseOracle | Oracle向けDB接続 | CvBase |
| 1.5 | CvDomainLogic | ビジネスロジック、ドメインサービス | CvBase |
| 2 | CvServer | gRPCサービス実装、API公開 | CodeShare, CvAsset, CvBase, CvDomainLogic |
| 2 | CvWpfclient | WPF GUI（Views/ViewModels） | CodeShare, CvAsset, CvBase |

**読み取り専用プロジェクト**
以下のプロジェクトはAIによる変更禁止（明示的な依頼がある場合のみ）：
- CodeShare
- CvAsset
- CvBase
- CvBaseSqlite
- CvBaseMariadb
- CvBaseOracle


# プロジェクト一覧

## Layer 0

### CodeShare
- gRPCコントラクト（サービス/メッセージ）をコードファーストで定義
- サーバ`CvServer`とクライアント`CvWpfclient`が参照
- 型安全通信を担保

### CvAsset
- 複数プロジェクトで共有される軽量ユーティリティ
- 定数、拡張メソッド、補助クラス
- 依存性を最小限に抑え、基盤層として安定性を重視

## Layer 1

### CvBase
- 共通モデル、NPocoベースのDBエンティティ
- 基底インフラ，提供
- `CommunityToolkit.Mvvm`を利用した共通モデルの再利用

### CvBaseSqlite
- SQLite向けのDB接続
- 拡張NPoco実装

### CvBaseMariadb
- MariaDB向けのDB接続
- 拡張NPoco実装

### CvBaseOracle
- Oracle向けのDB接続
- 拡張NPoco実装

## Layer 1.5

### CvDomainLogic
- `ExDatabase`（汎用DB I/F）による抽象化
- ドメインロジック、変換バッチなど
- ビジネスロジックの実装を集約

## Layer 2

### CvServer
- gRPCサーバアプリケーション
- `CvnetCoreService`が`ICvnetCore`を実装してAPIを公開
- JSONシリアライズ設定の共通化
- JWT Bearer認証基盤

### CvWpfclient
- WPFクライアントアプリケーション
- `CommunityToolkit.Mvvm`によるMVVMパターン
- Material Designテーマ

## Tests

### Tests.CvServer
- CvServerのユニット/結合テスト

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
dotnet build Cv.slnx

# サーバビルド
dotnet build CvServer/CvServer.csproj

# WPFクライアントビルド
dotnet build CvWpfclient/CvWpfclient.csproj /p:EnableWindowsTargeting=true /p:UseAppHost=false

