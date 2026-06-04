## [2026-06-04] 10:36 WebpdfView F5リロード安定化
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：WebpdfView の View / ViewModel のリロード機能が正しく動いているか確認し、うまく動いていない場合は修正、ログ、commit まで実施する。
### 実施内容
- CvWpfclient/Views/Sub/WebpdfView.xaml.cs: WebView2 内にフォーカスがある状態でも F5 を捕捉できるよう `PreviewKeyDown` で `ReloadCommand` を明示実行する処理を追加
- CvWpfclient/ViewModels/Sub/WebpdfViewModel.cs: `Pdfdata` を空文字や一時URLにせず、最終URLの `cv_reload` クエリを付け直す方式に整理。同一ミリ秒内の連続実行でも URL が必ず変わるよう連番を追加
- Doc/aicoding_log_005.md: 800行を超えた既存ログをアーカイブ
### 技術決定 Why
- Window の `InputBindings` だけでは WebView2 内フォーカス時の F5 捕捉が不安定になり得るため、View の UI イベントで ViewModel のコマンドへ委譲した
- WebView2 の実行時例外を避けるため、空文字や一時URLへの遷移ではなく、表示対象 PDF URL 自体にキャッシュバスターを付ける方式を維持した
- `UriBuilder` によるURL正規化を避け、既存URL文字列の形を保ったまま `cv_reload` のみ差し替える実装にした
### 確認
- `git diff --check` で空白エラーなしを確認（既存ファイルの LF→CRLF 警告のみ）
- `WebpdfView.xaml` を XML として読み込み可能なことを確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認
- GUI 上の F5 実操作は未実施

---

## [2026-06-04] 13:55 CodeShare契約名とDTO整合性の整理
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：CodeShareの実装計画1を実施し、`Msg042_GetTableList = 44`は`42`へ変更する。開発段階のためgRPC契約名変更も許容して実装計画2も積極的に整理し、`JapanPostBizOptions`をCvServer側へ移す。ログとcommitまで行う。
### 実施内容
- CodeShare/ICoreService.cs: `ICvnetCore.cs`を現行インターフェース名に合わせてリネームし、`CvFlag`へ`DataContract`/`EnumMember`を付与。`Msg042_GetTableList`の値を`42`へ変更
- CodeShare/PrintOperation.cs: `IPrintOperation.cs`をDTO実体名に合わせてリネーム
- CodeShare/ISchedulerService.cs: `IScheduler.cs`を`ISchedulerService`へリネームし、`AddTaskAsync`/`RemoveTaskAsync`/`RemoveAllTasksAsync`/`GetTasksAsync`へメソッド名を整理
- CodeShare/IPostalAddress.cs: 郵便番号検索契約を`CallContext`受けに統一し、検索結果DTOへ`DataMember`を追加。`JapanPostBizOptions`をCodeShareから削除
- CodeShare/IWeather.cs: 天気サービス契約を`CallContext`受けに統一
- CvServer/Services/JapanPostBizOptions.cs: 日本郵便API設定型をCvServer側へ移動
- CvServer/Services/SearchByPostalCodeService.cs, WeatherService.cs, SchedulerService.cs: 変更後のCodeShare契約名と`CallContext`へ追従
- CvWpfclient/Helpers/PostalAddressSearchHelper.cs, CvWpfclient/ViewModels/MainMenuViewModel.cs, CvWpfclient/ViewModels/00System/SysSchedulerJobMenteViewModel.cs: 変更後のgRPC契約呼び出しへ追従
- Tests/TestServer/TestServer.cs, readme.md, Doc/spec/spec.general.readme.md, Doc/opencode_usage_guide.md, Doc/opencode_prompts.md: 旧Core契約名の記述を現行名へ整理
### 技術決定 Why
- 開発段階で契約名変更が許容されるため、互換aliasを残さず公開契約名と実装名を一致させた
- `JapanPostBizOptions`はgRPC DTOではなくCvServer固有の外部API設定であるため、CodeShareからCvServerへ移して共有契約面を小さくした
- `CallContext`へ統一することで、JWTメタデータとキャンセル伝播の扱いを他のCodeShare gRPC契約と揃えた
### 確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CodeShare/CodeShare.csproj"` でビルド成功（0 warnings / 0 errors）
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build creativevision10.slnx"` でビルド成功（0 warnings / 0 errors）
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet run --project Tests/TestLogin/TestLogin.csproj --no-build"` で3件成功
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet run --project Tests/TestServer/TestServer.csproj --no-build"` で6件成功
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet format CodeShare/CodeShare.csproj --verify-no-changes --no-restore"` で差分なしを確認
- `git diff --check` で空白エラーなしを確認

---
