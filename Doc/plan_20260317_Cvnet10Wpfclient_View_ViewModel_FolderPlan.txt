# Cvnet10Wpfclient Views/ViewModels フォルダ構成案

`Cvnet10Wpfclient` の `Views` と `ViewModels` で、同一の業務カテゴリフォルダを対にして運用する。

## 命名ルール
- 先頭2桁の業務番号 + 英字カテゴリ名（例: `02Yosan`）
- `Views` と `ViewModels` は同名フォルダで揃える
- ローマ字は以下で統一する
  - 発注: `Hatchu`
  - 受注: `Juchu`
  - 物流: `Butsuryu`

## 追加フォルダ一覧
- `02Yosan`（予算関連）
- `03Hatchu`（発注関連）
- `04Juchu`（受注/展示会関連）
- `05Shiire`（仕入関連）
- `06Uriage`（売上関連）
- `07Haibun`（配分関連）
- `08Zaiko`（在庫管理関連）
- `20UriageAnalysis`（売上分析関連）
- `21OroshiAnalysis`（卸・販売員・経営分析関連）
- `22CPA`（C.P.A関連）
- `30HHT`（HHT/POS連携関連）
- `31Getsuji`（月次/更新処理関連）
- `32LoyalCustomer`（Loyal Customer関連）
- `40Tenpo`（店舗関連）
- `41Butsuryu`（物流関連）

## 備考
- Git で空フォルダを管理するため、各フォルダに `.gitkeep` を配置する。
