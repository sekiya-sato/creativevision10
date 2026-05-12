# 2026-05-12 商品検索選択画面 作業メモ

## 目的

`Doc/wrk/instruction-20260512-Desktop-SearchShohin.txt` に従い、`CvWpfclient` に商品検索・一覧選択画面 `Sub/SelectShohinView` を新規作成する。

## 要求整理

- 検索画面から商品一覧選択画面へ遷移する2画面構成にする。
- 一覧で商品を選択したら画面全体を閉じ、選択した商品マスタを返す。
- 一覧画面の戻るボタンは検索画面へ戻す。
- 検索条件は商品CD範囲、商品名部分一致、ブランドCD範囲、アイテムCD範囲、JAN部分一致。
- ブランドCD・アイテムCDの範囲入力はテキスト入力付き検索ボタンを使う。
- 一覧は Id、商品CD、商品名、上代、ブランドCD+名称、アイテムCD+名称を表示する。
- 右端・下端が切れないレイアウトにする。

## 実装方針

- `CvWpfclient` の既存 `Sub/Select*` 画面と `SearchTextBox` 利用パターンを踏襲する。
- 業務画面は `helpers:BaseWindow` と既存リソースを使う。
- 必要最小限のクライアント側追加で実装し、下位レイヤー変更は既存APIだけで不足する場合に限定する。

## 確認予定

- 新規XAMLの構文・Binding・Resource参照を確認する。
- `CvWpfclient/CvWpfclient.csproj` をビルドする。
- `Doc/aicording_log.md` へ作業ログを追記する。
