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
