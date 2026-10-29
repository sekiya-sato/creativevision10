---
name: epson-escpos-research
description: EPSON TM-seriesプリンタのESC/POSコマンド、文字コード、用紙幅、設定値、応答形式を公式一次資料で調査し、安全に実装へ反映する。TM-m30IIを含むEPSONレシートプリンタの印字不良や設定切替を調査・実装するときに使用する。
---

# EPSON ESC/POS 調査

公式の [TM Printer ESC/POS Command Reference](https://download4.epson.biz/sec_pubs/pos/reference_ja/escpos/) を一次資料として使う。検索エンジンでは `site:download4.epson.biz <機種名> <コマンド名または機能>` を使う。

## 手順

1. 機種別の対応コマンド一覧を開き、対象コマンドが対応機種に含まれることを確認する。
   - TM-m30II: <https://download4.epson.biz/sec_pubs/pos/reference_ja/escpos/tmm30ii.html>
2. コマンドの個別ページで、16進バイト列、前提モード、応答、永続性、リセット条件を確認する。推測でバイト列を作らない。
3. 設定を書き換える `GS ( E` は、Function 1でユーザー設定モードへ移行し、通知を受信してからFunction 5を送り、Function 2で終了する。Function 5は不揮発メモリへ書くため、Function 6で現在値を照会して差分がある場合だけ書き込む。
4. `FS C 1` でShift_JIS体系を選んだ場合、漢字コードの第1バイトに続くバイトは自動で第2バイトとして処理される。`FS &` / `FS .` はJIS体系専用なのでShift_JISの印字データへ混在させない。半角の `0x5C` を円記号にするには、`ESC R 8` で日本の国際文字セットを選択する。
5. 実装後は、生成バイト列（コマンド境界・半角/全角の切替・設定値）を静的に検証し、可能なら実機の印字と設定読出しを確認する。

## 主要資料

- [GS ( E Function 5: カスタム設定値の変更](https://download4.epson.biz/sec_pubs/pos/reference_ja/escpos/gs_lparen_ce_fn05.html)
- [GS ( E Function 6: カスタム設定値の取得](https://download4.epson.biz/sec_pubs/pos/reference_ja/escpos/gs_lparen_ce_fn06.html)
- [FS &: 漢字モード開始](https://download4.epson.biz/sec_pubs/pos/reference_ja/escpos/fs_ampersand.html)
- [FS .: 漢字モード終了](https://download4.epson.biz/sec_pubs/pos/reference_ja/escpos/fs_period.html)
- [FS C: 漢字コード体系の選択](https://download4.epson.biz/sec_pubs/pos/reference_ja/escpos/fs_cc.html)
- [ESC R: 国際文字の選択](https://download4.epson.biz/sec_pubs/pos/reference_ja/escpos/esc_cr.html)
