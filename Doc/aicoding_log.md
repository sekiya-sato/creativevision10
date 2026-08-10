## [2026-08-10] 15:50 仕入返品入力を在庫一覧方式へ全面変更
### Agent
- Opus 5 : Anthropic : Sekiya Sato Claude
### Editor
- Claude Code
### 目的
- ユーザーからの要望：`CvWpfclient.Views._05Shiire.HenpinInputView` の View / ViewModel を添付画面のとおり全面変更する。取引区分(20 仕入返品 固定)・仕入日(初期値 今日)・仕入先・倉庫・入力者(いずれも Id を選択し「コード 名称」表示)・備考を入力し、[在庫取得] で該当倉庫にある該当仕入先の商品(商品マスタのメーカーCD = 仕入先CD)を一覧表示、[実行] で仕入返品データ(区分20)を作成する。
### 実施内容
- CvWpfclient/ViewModels/05Shiire/HenpinInputViewModel.cs: `ShiireInputViewModel`(伝票明細方式)の継承をやめ、`BaseStockSheetInputViewModel<Tran03Shiire>`(一覧方式)の派生に全面書き換え。取引区分/仕入先/倉庫/入力者のコンボ選択、在庫取得SQL、`BuildDenpyo` による仕入返品伝票の組み立てを実装した。
- CvWpfclient/Views/05Shiire/HenpinInputView.xaml: 一覧/詳細の2タブ構成をやめ、ヘッダ入力＋[在庫取得(F5)]＋数量計/上代金額計/下代金額計＋明細DataGrid＋[実行(F8)]/[クリア]/[戻る(ESC)] の1画面構成へ全面書き換え。
- CvWpfclient/Helpers/ViewModels/BaseStockSheetInputViewModel.cs: `StockSheetRow.JodaiKingaku` / `GedaiKingaku`(入力数×単価)と、画面合計 `JodaiKingakuTotal` / `GedaiKingakuTotal` を追加(既存の棚卸入力・在庫移動入力に影響しない追加のみ)。
### 技術決定 Why
- 返品は「今その倉庫にある在庫を仕入先へ送り返す」作業で、対象SKUを先に全部並べて数量だけ直す一覧方式が実務に合う。棚卸入力(一覧方式)・在庫移動入力と同じ基底を使い、伝票登録・在庫取得ヘルパを再利用した。作成後の修正・削除は従来どおり【商品仕入入力】が担う(画面上にも明記)。
- 仕入先での商品絞り込みは、商品マスタのメーカー(`MasterShohin.Id_Maker` → `MasterMeisho.Kubun='MKR'`)のコードと仕入先コードの一致で行う。Id による関連が張られていないため、旧システム同様コード一致で突き合わせる。
- 数量は必ずプラスで登録する。`Tran03Shiire.Kubun=20` により `OnKubunChanged` が `CalcFlag=-1` を立て、在庫集計が `Su * CalcFlag * calcFlag` で減算するため、マイナス入力すると符号が二重反転して在庫が増えてしまう。マイナス行は登録前に弾き、在庫超過は確認ダイアログで警告する。
- 消費税・総合計は `ShiireInputViewModel.UpdateHeaderTotals` と同じ積み方(`|金額計| * 税率`)に揃え、税率は仕入日時点の `AppGlobal.LogicGetTax(1, 仕入日)` を使う。掛計上日は仕入日と同じにした。
- 在庫取得は `SummaryRealStock` を `Su > 0` で絞る。在庫の無いSKUは返品対象にならないため、SQL側で落として取得件数上限の枠を無駄にしない。
### 確認
- `vscmdclaude.bat dotnet build creativevision10.slnx --nologo`: 成功（警告0、エラー0）。
- 実画面確認: CvServer 起動後、MainMenu 経由で `HenpinInputView` を開き `PrintWindow` でキャプチャ。初期表示(仕入日=今日、区分=20 仕入返品)と、仕入先195/倉庫000990 選択時の [在庫取得] 500件表示(数量=在庫数の初期値、上代/下代金額、計 2,739 / ￥15,131,200 / ￥100,473)を目視確認した。確認用の一時フックは削除済み。
- XAMLリソースキー/バインディングパス照合: 未定義参照なし。CRLF・`git diff --check`: 問題なし。
- `TestServer.exe`: 35件中1件失敗。`SummaryDbTests.CalcSummaryRealStockRange_RebuildsOnlyTargetWarehouseProductColor` は単独実行でも失敗する **既存の失敗**(TestServer は CvWpfclient を参照しておらず本変更とは無関係)。
### 未実施
- [実行] による伝票登録の実データ検証は未実施。共有DB(server-user163.db)に仕入返品伝票が実際に作られ在庫集計が動くため、意図的に実行していない。

---

## [2026-08-10] 14:18 SummaryRealStock範囲再計算を追加
### Agent
- GPT-5 : OpenAI
### Editor
- Codex
### 目的
- ユーザーからの要望：指定年月範囲で再計算したSummaryStockに対応するSummaryRealStockだけを再計算する。
### 実施内容
- CvDomainLogic/SummaryDb.cs: `CalcSummaryRealStockRange(string DateFromYyyymm, string DateToYyyymm)` を追加。範囲内の `(Id_Soko, Id_Shohin, Id_Col)` を対象に、指定終了月までの全月データから全サイズの現在庫を再作成する。`SummaryAllAsyncStream` の月別集計完了後に実行するステップへ組み込んだ。
- Tests/TestServer/SummaryDbTests.cs: 対象キーの全サイズが開始月以前を含む累計に更新され、対象外キーが維持されるSQLite回帰テストを追加した。
### 技術決定 Why
- 期間内の差分だけを加減算すると再集計済みの月別データとの差異が残るおそれがあるため、対象キーを範囲で限定した上で、指定終了月までのSummaryStockから完全再集計する。削除と挿入は直列化トランザクションで一体化した。
### 確認
- `C:\gitroot\UT\vscmd.bat dotnet build CvDomainLogic\CvDomainLogic.csproj --no-restore`: 成功（警告0、エラー0）。
- `C:\gitroot\UT\vscmd.bat dotnet build Tests\TestServer\TestServer.csproj --no-restore`: 成功（警告0、エラー0）。
- `TestServer.exe --filter "Name=CalcSummaryRealStockRange_RebuildsOnlyTargetWarehouseProductColor"`: 成功（1件）。

---

## [2026-08-09] 12:19 長期gRPC通信設定と郵便番号APIトークン共有を改善
### Agent
- GPT-5 : OpenAI
### Editor
- Codex
### 目的
- ユーザーからの要望：共通gRPC通信の無期限設定を意図として明記し将来の有限値化に備え、郵便番号APIのトークンをリクエスト間で再利用する。
### 実施内容
- CvWpfclient/App.xaml.cs: 共有gRPCの接続アイドル寿命・接続寿命・HTTPタイムアウトを `GrpcTransportSettings` に集約。すべて無期限とする意図および有限値へ切替える変更点をコメントで明記した。
- CvServer/Services/SearchByPostalCodeService.cs: トークン、有効期限、排他制御を静的フィールドへ移動。RPCごとに生成されるサービスインスタンスをまたいでトークンを再利用し、同時要求時のトークン取得を1件に直列化した。
### 技術決定 Why
- 共通gRPCは常駐WPFクライアントでHTTP/2接続を維持する設計のため、無期限設定を維持する。一方で値を集約し、運用要件が変わった際は通信パイプラインを変更せず有限値へ変更できるようにした。
- 日本郵便APIのトークンキャッシュがサービスインスタンスに属していたため、gRPCリクエスト間で再利用されなかった。プロセス共有キャッシュと静的 `SemaphoreSlim` により、有効期限までの再利用と同時更新の重複防止を行う。
### 確認
- `git diff --check`: 成功。
- `C:\gitroot\UT\vscmd.bat dotnet build CvServer\CvServer.csproj --no-restore --no-dependencies -p:OutputPath=obj\CodexBuildOutput\`: 成功（警告0、エラー0）。
- `CvWpfclient` の同条件ビルドは今回と無関係の `StockKakeUpdateViewModel.cs` にある未定義 `CvFlag.Msg052_SummaryUriKake` / `CvFlag.Msg053_SummaryKaiKake` により失敗。

---

## [2026-08-09] 06:53 WeatherService長期稼働時の431応答を修正
### Agent
- GPT-5 : OpenAI
### Editor
- Codex
### 目的
- ユーザーからの要望：OpenWeather API 呼出しが数日後に 431 で失敗する原因を修正し、ログとコミットまで行う。
### 実施内容
- CvServer/Services/WeatherService.cs: 静的 HttpClient の生成処理をファクトリへ集約。User-Agent をサービス生成時ではなくクライアント初期化時に一度だけ設定し、PooledConnectionLifetime を15分に設定した。
- Doc/aicoding_log_011.md: 800行超過の既存作業ログをアーカイブした。
- Doc/aicoding_log.md: 今回の作業記録を追加した。
### 技術決定 Why
- 保存ログで OpenWeather API から 431 (Request Header Fields Too Large) を77件確認した。WeatherService のコンストラクタがリクエストごとに共有 HttpClient の DefaultRequestHeaders.UserAgent へ追加していたため、ヘッダーが無制限に肥大化していた。初期化時の一度だけの設定に変更して累積を防止した。加えて接続プールを15分で更新し、DNS変更にも追随させる。
### 確認
- `git diff --check`: 成功。
- `C:\gitroot\UT\vscmd.bat dotnet build CvServer\CvServer.csproj --no-restore --no-dependencies -p:OutputPath=C:\tmp\cv10-weather-build\CvServer\`: 成功（警告0、エラー0）。実行中の CvServer が通常出力先DLLをロックしているため、隔離出力先で検証した。

---
