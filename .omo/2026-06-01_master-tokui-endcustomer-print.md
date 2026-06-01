# MasterTokui/MasterEndCustomer 印刷機能追加計画

## 目的
- MasterTokuiMenteView と MasterEndCustomerMenteView の F6 を JSON 出力から PDF 印刷へ変更する。
- 既存の BaseMenteViewModel の印刷経路を使い、ViewModel 側は FormFile と PrintBySqlParam の追加に留める。
- 帳票 qfm は A4 縦、Shift_JIS(cp932) 保存、data.txt CSV 入力とする。

## 方針
- 得意先は MasterShiireMente の取引先系 SQL 帳票を流用し、TenType と IsZaiko を追加する。
- 顧客は MasterEndCustomer の主要項目、住所、店舗、購買集計を一覧 SQL で出力する。
- View は F6 KeyBinding とツールバーボタンを DoOutputPdfCommand / Printer / 印刷 (F6) に差し替える。

## 確認
- qfm 検証スクリプトで A4 縦・Shift_JIS・CSV 入力を確認する。
- XAML 構文確認、git diff --check、WPF クライアント build を実行する。
- Doc/aicoding_log.md に結果を追記し、1コミットにまとめる。
