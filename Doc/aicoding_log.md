## [2026-06-13] 12:35 ShopUriageInputView 明細2段レイアウト対応
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：ShopUriageInputView の MeisaiGrid を1レコード2段表示にし、店舗/倉庫の選択CD・Name表示、明細行ボタン位置変更、数量・単価変更時の明細計算と合計即時表示に対応する
### 実施内容
- CvWpfclient/Views/06Uriage/ShopUriageInputView.xaml: 店舗/倉庫ラベルをId表示へ変更し、明細行追加/削除ボタンをメモ直下へ移動、MeisaiGrid を商品・色・サイズ・数量・単価・上代単価・下代単価・明細担当Idの1段目と、CD/名称/各金額の2段目で表示するレイアウトへ変更
- CvWpfclient/ViewModels/06Uriage/ShopUriageInputViewModel.cs: 数量・単価・上代単価・下代単価変更時に金額/上代合計/下代合計を更新し、明細担当者選択コマンドを追加
- CvWpfclient/Helpers/Converters/MultiplyValuesConverter.cs: 明細2段目の単価×数量表示用 MultiBinding コンバーターを追加
- Doc/aicoding_log_006.md: 800行を超えた既存ログをアーカイブ
### 技術決定 Why
- 共通トランザクション型 Tran99Meisai へ画面専用の計算表示プロパティを追加せず、WPF MultiBinding コンバーターで2段目の表示計算を行い、保存値と合計更新は既存の UpdateTotals 経路に集約した
### 確認
- ShopUriageInputView.xaml の XML 構文チェック成功
- 編集ファイルの CRLF を確認（BadLF=0）
- `git diff --check` で空白エラーなし
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認

---

