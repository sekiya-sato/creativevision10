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
