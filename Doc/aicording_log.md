# AI Coding Log

このファイルは、Creative Vision 10プロジェクトにおけるAI支援開発の作業履歴を記録します。

## 使用するAIツール
- **GitHub Copilot**: インライン補完、クイックフィックス、小規模編集（VS2026統合）
- **OpenCode**: 大規模機能実装、複数ファイル編集、ドキュメント作成（CLI）

## 記録フォーマット
```markdown
## [YYYY-MM-DD] hh:mm 作業タイトル
### Agent
- [使用した AI Model 名 : AI Provider 名]
  例: claude-sonnet-4.5 : GitHub-Copilot
      gpt-5.4 : OpenAI
### Editor
- [使用したエディタ]
  例: OpenCode, VS2026, VSCode, GitHubCopilot-Cli
### 目的
- ユーザーからの要望：[内容]
### 実施内容
- [プロジェクト名]/[ファイル名]: [変更内容の要約]
### 技術決定 Why
- [技術的判断の理由]
### 確認
- [Build結果やテスト結果]
```

## アーカイブルール
- 800行を超える場合、既存履歴を `aicording_log_[001-999].md` として連番保存
- 新規に `aicording_log.md` を作成して記録を継続

---

## [2026-04-11] 20:31 EffectiveSettings導入とログイン後ホスト再構築
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`AppGlobal.InfoApiKey` を優先する設定解決クラスを追加し、`SetLoginJwt(reply.JwtMessage, reply.InfoPayload)` 実行後に `RestartHostAsync()` でDIサービスへ再反映する
### 実施内容
- CvWpfclient/Models/EffectiveSettings.cs: `AppGlobal.InfoApiKey > IConfiguration > 既定値` の優先順位で設定を解決するクラスを追加
- CvWpfclient/Models/JapanPostBizOptions.cs: JapanPostBiz設定クラスをModels配下へ分離
- CvWpfclient/App.xaml.cs: `EffectiveSettings` をDI登録し、JapanPostBiz用 `HttpClient` 構成でも同クラスを利用するよう変更
- CvWpfclient/Services/WeatherService.cs: OpenWeather APIキーと地域設定の参照先を `EffectiveSettings` に統一
- CvWpfclient/Services/JapanPostBizTokenProvider.cs: ClientId/SecretKey/TokenPath/RefreshMargin の参照を `EffectiveSettings` 経由へ変更
- CvWpfclient/Services/PostalAddressService.cs: JapanPostBiz設定クラスの定義を分離し、検索URL生成時の設定参照を `EffectiveSettings` 経由へ変更
- CvWpfclient/ViewModels/00System/LoginViewModel.cs: Login/Refresh 成功時に `SetLoginJwt(...)` の直後で `App.RestartHostAsync()` を await するよう変更
- CvBase/Share/InfoApiKey.cs: ビルドを阻害していたプロパティ末尾の余分なセミコロンを除去
### 技術決定 Why
- APIキーはログイン応答の `InfoPayload` が最新になり得るため、クライアント側設定より優先する形に統一した
- JapanPostBiz系サービスはコンストラクタ時に設定を固定すると更新を拾えないため、ログイン直後にホストを再構築して新しいDIサービスへ切り替える形を採用した
### 確認
- `C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj` でビルド成功

---

## [2026-04-11] 18:30 マスタメンテ住所入力画面へ〒API検索を横展開
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：顧客マスタの検索ボタン表示を `〒API検索` に変更し、同様に住所1,2,3を持つマスタメンテ系画面すべてへ `〒API検索` ボタンを追加する。さらに、この手順を再利用できるskillとしてまとめる
### 実施内容
- `CvWpfclient/Helpers/PostalAddressSearchHelper.cs`: 郵便番号検索サービス呼び出し、1件ヒット時の適用、メッセージ表示を共通化するHelperを追加した。
- `CvWpfclient/ViewModels/01Master/MasterEndCustomerMenteViewModel.cs`: 既存の郵便番号検索処理を共通Helper呼び出しへ置き換えた。
- `CvWpfclient/ViewModels/01Master/MasterTokuiMenteViewModel.cs`: `SearchPostalCodeCommand` を追加し、検索結果を `CurrentEdit.PostalCode` と `Address1-3` へ反映するようにした。
- `CvWpfclient/ViewModels/01Master/MasterShiireMenteViewModel.cs`: `SearchPostalCodeCommand` を追加し、検索結果を `CurrentEdit.PostalCode` と `Address1-3` へ反映するようにした。
- `CvWpfclient/ViewModels/01Master/MasterSysKanriMenteViewModel.cs`: `Current.*` 直バインド画面向けに `SearchPostalCodeCommand` を追加し、検索結果を `Current.PostalCode` と `Address1-3` へ反映するようにした。
- `CvWpfclient/Views/01Master/MasterEndCustomerMenteView.xaml`: 検索ボタン文言を `〒API検索` に変更した。
- `CvWpfclient/Views/01Master/MasterTokuiMenteView.xaml`: 郵便番号欄を短縮し、`〒API検索` ボタンを追加した。
- `CvWpfclient/Views/01Master/MasterShiireMenteView.xaml`: 郵便番号欄を短縮し、`〒API検索` ボタンを追加した。
- `CvWpfclient/Views/01Master/MasterSysKanriMenteView.xaml`: `〒 住所` 行の郵便番号欄の横に `〒API検索` ボタンを追加した。
- `.agents/skills/add-postal-api-search-master-mente/SKILL.md`: マスタメンテ画面へ郵便番号API検索を追加する手順をskillとして追加した。
### 技術決定 Why
- 住所反映ロジックを各ViewModelへ都度複製すると保守点が増えるため、共通の `PostalAddressSearchHelper` に寄せて横展開しやすい形にした。
- `SysKanri` は `Current.*` 直バインドで他のマスタと構造が異なるため、共通Helperは再利用しつつ、反映先だけ `Current` に切り替える最小差分にした。
- skillには対象画面、認証前提、URL生成前提、View/ViewModelの変更パターンをまとめ、次回以降の横展開を定型化した。
### 確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj /p:OutDir=c:\gitroot\documents\new2022\cv10\artifacts\postalout\"` → ビルド成功（0警告、0エラー）

---

## [2026-04-11] 18:14 郵便番号検索のAuthorizationスキームをBearer固定に修正
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：日本郵便APIのAuthorization仕様が `Bearer {トークン}` 固定である前提に合わせ、トークン応答の `token_type` に依存しないよう修正する
### 実施内容
- `CvWpfclient/Services/JapanPostBizTokenProvider.cs`: キャッシュ済みトークン返却時と新規取得時の両方で、Authorizationヘッダを常に `Bearer` スキームで返すよう変更した。内部の `token_type` キャッシュは削除した。
### 技術決定 Why
- token APIレスポンスの `token_type` が `jwt` でも、検索API側のAuthorization仕様は `HTTP Authorization Scheme: bearer` で固定のため、応答値をそのままHTTPスキームへ使うと不正ヘッダになる。送信スキームを `Bearer` 固定にするのが正しい。
### 確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj /p:OutDir=c:\gitroot\documents\new2022\cv10\artifacts\postalout\"` → ビルド成功（0警告、0エラー）

---

## [2026-04-11] 18:05 郵便番号検索URL組み立ての見直し
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：日本郵便APIの検索時に400が返るため、`BuildSearchUrl` 周辺を再検討し、`/api/v2/searchcode/{search_code}` の7桁郵便番号検索向けにURL生成を見直す
### 実施内容
- `CvWpfclient/Services/PostalAddressService.cs`: `BuildSearchUrl` を見直し、通常の7桁郵便番号検索では `page` と `limit` の必須クエリのみを付与するよう変更した。`ec_uid` は設定時のみ付与するようにした。`DefaultLimit` はAPI既定に合わせて `1000` に変更した。
- `CvWpfclient/appsettings.json`: `JapanPostBiz:EcUid` を追加し、`DefaultLimit` を `1000` に更新した。
### 技術決定 Why
- API仕様上、`page` と `limit` は必須だが、`choikitype` と `searchtype` は任意であるため、まず通常運用の7桁郵便番号検索で必要な最小パラメータに絞って400要因を減らした。
- `ec_uid` はプロバイダー固有の追加条件になりうるため、コード固定ではなく設定で付与できる形にした。
### 確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj /p:OutDir=c:\gitroot\documents\new2022\cv10\artifacts\postalout\"` → ビルド成功（0警告、0エラー）

---

## [2026-04-11] 17:51 顧客マスターメンテに郵便番号検索を追加
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：CvWpfclient から日本郵便APIを呼び、顧客マスターメンテ画面の `〒` 欄の横幅を半分程度にして検索ボタンを追加し、郵便番号から住所1,2,3を自動設定する
### 実施内容
- `CvWpfclient/Services/PostalAddressService.cs`: 郵便番号7桁専用の `IPostalAddressService`、検索結果DTO、JapanPostBiz設定、検索サービス実装を追加した。検索前にトークンProviderから認証ヘッダを取得し、検索結果を `Address1/2/3` へマッピングできる形に正規化した。
- `CvWpfclient/Services/JapanPostBizTokenProvider.cs`: `expires_in` を見て有効期限を管理するトークンProviderを追加した。期限切れ前に再取得し、認証失敗時は無効化して再取得できるようにした。
- `CvWpfclient/App.xaml.cs`: 日本郵便API向け `HttpClient` を設定し、`IPostalAddressService` と `IJapanPostBizTokenProvider` をDI登録した。
- `CvWpfclient/appsettings.json`: `JapanPostBiz` セクションを追加し、BaseUrl、TokenPath、SearchCodePath、UserAgent などの設定キーを追加した。ClientId と SecretKey は空欄のままにした。
- `CvWpfclient/ViewModels/01Master/MasterEndCustomerMenteViewModel.cs`: `SearchPostalCodeCommand` を追加し、検索結果1件時に `CurrentEdit.PostalCode`、`Address1`、`Address2`、`Address3` を更新するようにした。
- `CvWpfclient/Views/01Master/MasterEndCustomerMenteView.xaml`: `〒` 行を内側Grid化し、郵便番号欄を短くしたうえで検索ボタンを追加した。
- `.sisyphus/20260411_postal_address_customer_master.md`: 作業メモを追加した。
### 技術決定 Why
- 日本郵便APIのトークンは `expires_in` で失効管理できるため、ViewModel側ではなくサービス内部でトークン再取得を閉じ込めて画面側の責務を増やさない設計にした。
- 住所自動入力の通常利用は7桁郵便番号固定のため、初版は前方一致検索や候補選択UIを持たせず、1件ヒット時のみ反映する最小構成にした。
- API資格情報は秘密情報に当たるため、設定キーのみコミットし、値は空欄で管理する形にした。
### 確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` → 実行中 `CreativeVision10` によるDLLロックで失敗
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj /p:OutDir=c:\gitroot\documents\new2022\cv10\artifacts\postalout\"` → ビルド成功（0警告、0エラー）

---

## [2026-04-10] 23:54 MainMenuView に Sunrise/Sunset 表示を追加
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`MainMenuView` の「天気アイコン＋気温」領域の下側に、ViewModel 側で用意済みの `Sunrise` と `Sunset` を1行横並びで表示する。
### 実施内容
- `CvWpfclient/Views/MainMenuView.xaml`: 天気情報カード内の `WeatherDescription` の下に、`Sunrise` と `Sunset` を横並びで表示する `StackPanel` を追加した。既存デザインに合わせて補足情報として小さめ文字・半透明で表示するようにした。
### 技術決定 Why
- ViewModel 側のプロパティと値設定は既に実装済みだったため、責務を増やさず View のバインディング追加だけで要件を満たす最小差分にした。
- `Sunrise` と `Sunset` は同一粒度の情報なので、1行横並びにしてカード高さの増加を抑えつつ視認性を確保した。
### 確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` → ビルド成功（警告0、エラー0）

---

## [2026-04-09] 16:59 MasterShohinMenteView に商品画像表示を追加
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`MasterShohinMenteView.xaml` でサーバURL + `/img/[MasterShohinのCode].jpg` の画像を表示し、価格欄を縮小して右側に画像エリアを追加する。current変更中の画像読込はキャンセルし、画像が無い場合は `画像なし([Code].img)` を表示する。
### 実施内容
- `CvWpfclient/ViewModels/01Master/MasterShohinMenteViewModel.cs`: `ShohinImage`、`IsShohinImageLoading`、`ShohinImageStatusText` を追加し、`CurrentEdit` 切替時にサーバ画像を非同期読込する処理を実装。新しい選択へ切り替わった場合は `CancellationTokenSource` で前回の読込をキャンセルするようにした。
- `CvWpfclient/Views/01Master/MasterShohinMenteView.xaml`: 基本情報タブを3列構成に変更し、`元上代`、`上代`、`原価`、`仕入単価` の `TextBox` 幅を縮小。右側に画像表示カードを追加し、読込中オーバーレイと `画像なし([Code].img)` 表示をバインドした。
### 技術決定 Why
- 画像取得はViewModel側の非同期処理に寄せることで、選択変更時のキャンセル制御と表示状態の一元管理を行いやすくした。
- 画像は `BitmapImage` を `OnLoad` で生成して `Freeze()` し、ストリーム寿命やUIスレッド境界の問題を避けた。
### 確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` → ビルド成功（警告0、エラー0）

---
## [2026-04-07] 10:45 Git履歴からの不要バイナリ削除
### Agent
- gemini-3.1-flash : Google : (wsl2への手動コピペ)
### Editor
- OpenCode / Terminal
### 目的
- ユーザーからの要望：Git履歴に含まれる過去の不要なPDFファイル（test_server.pdf, test.pdf）を完全に削除し、リポジトリを軽量化・クリーンアップする。
### 実施内容
- `git filter-branch`（または filter-repo）を使用して、全履歴から対象ファイルを削除。
- 参照のクリーンアップ、リフレグの期限切れ処理、およびガベージコレクション（`git gc`）を実行。
- `git push origin master --force` によりリモートリポジトリへ変更を強制反映。
### 技術決定 Why
- 不要なバイナリファイルが履歴に残っているとリポジトリサイズが増大し続けるため、過去の全コミットを書き換えて完全に抹消した。
### 確認
- リモートへの強制プッシュ成功を確認。
- ローカルにて `git rev-list --all | xargs git ls-tree -r --name-only | grep .pdf` で対象ファイルが存在しないことを確認。

---


## [2026-04-06] 17:48 ShopUriageInputView の数値表示改善・区分日本語化・金額自動計算
### Agent
- claude-opus-4.6 : GitHub-Copilot
### Editor
- OpenCode
### 目的
- ユーザーからの要望：ShopUriageInputView（店舗売上入力画面）の一覧画面・詳細画面の数値表示改善、区分の日本語表示化、金額自動計算、商品選択時の単価自動設定を実装する
### 実施内容
- CvWpfclient/Views/06Uriage/ShopUriageInputView.xaml: 一覧画面の合計数量・合計金額を3桁区切り右詰めに変更、詳細画面の明細行（数量/単価/金額/上代/下代）を3桁区切り右詰めに変更、上部の合計数量・合計金額を右詰め+3桁区切りに変更、金額列をIsReadOnly=Trueに変更、区分ComboBoxにEnumUri01DisplayConverter適用のItemTemplate追加、Window.ResourcesにDataGridRightTextBlockスタイル追加
- CvWpfclient/ViewModels/06Uriage/ShopUriageInputViewModel.cs: KubunOptionsからOther(99)を除外、OnMeisaiPropertyChangedでSu/Tanka変更時に金額自動計算(数量*単価=金額)を追加、DoSelectShohinでMasterShohinのTankaJodai→単価/上代、TankaGenka→下代を自動設定
- CvWpfclient/Helpers/Converters/EnumUri01DisplayConverter.cs: 新規作成。EnumUri01→日本語表示名（売上/セール売上/返品/セール返品/その他）変換用IValueConverter
- CvWpfclient/App.xaml: EnumUri01DisplayConverterをアプリケーションリソースに登録
### 技術決定 Why
- 数値フォーマットは既存プロジェクトの標準パターン（StringFormat={}{0:N0}）に統一し、IValueConverterではなくXAMLのStringFormatを使用
- DataGridの右詰めはElementStyleでTextBlock.TextAlignment=Rightを設定する既存パターンに従った
- EnumUri01DisplayConverterは既存のEnumShimeDisplayConverterと同じパターン（Dictionary<Enum,string>マッピング）で実装
- KubunOptionsからOtherを除外する際、Enum定義（CvBase）は変更せずViewModel側で対応することでREAD-ONLYルールを遵守
### 確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` → ビルド成功（警告0、エラー0）

---

## [2026-04-06] 15:30 CvWpfclient のログ使用方法を統一
### Agent
- claude-opus-4.6 : GitHub-Copilot
### Editor
- OpenCode
### 目的
- ユーザーからの要望：CvWpfclient プロジェクト内のログの使い方を統一する。CvServer と同じ ILogger<T> + NLog provider パターンに統一
### 実施内容
- CvWpfclient/App.xaml.cs: ConfigureLogging 内で `logging.AddNLog(context.Configuration)` に変更、UpdateService の DI 登録を `services.AddSingleton<IUpdateService, UpdateService>()` に変更
- CvWpfclient/Services/UpdateService.cs: NLog 直接利用 → `ILogger<UpdateService>` (Microsoft.Extensions.Logging) に完全移行。`_logger.Info` → `_logger.LogInformation`、`_logger.Error` → `_logger.LogError`
- CvWpfclient/AppGlobal.cs: `private static readonly NLog.Logger _logger` を追加、`Debug.WriteLine` → `_logger.Info` に変更、インライン `LogManager.GetCurrentClassLogger()` → static フィールド利用に統一
- CvWpfclient/ViewModels/00System/SysUpgradeViewModel.cs: `using NLog` → `using Microsoft.Extensions.Logging`、`ILogger` → `ILogger<SysUpgradeViewModel>`、`LogManager.GetCurrentClassLogger()` → `ILoggerFactory` DI 経由取得、`_logger.Error` → `_logger.LogError`
- CvWpfclient/ViewModels/00System/LoginViewModel.cs: `using System.Diagnostics` 削除、`private static readonly NLog.Logger _logger` 追加、`Debug.WriteLine` → `_logger.Debug` に変更（2箇所）
- CvWpfclient/ViewModels/MainMenuViewModel.cs: `private static readonly NLog.Logger _logger` 追加、`Console.WriteLine` → `_logger.Warn` に変更
- CvWpfclient/Helpers/ClientLib.cs: クラスレベルに `private static readonly NLog.Logger _logger` 追加、catch 内のローカル `LogManager.GetCurrentClassLogger()` を static フィールド利用に統一
- CvWpfclient/Helpers/Communication/GrpcSubPathHandler.cs: コメント化された `Debug.WriteLine` 行を削除
### 技術決定 Why
- DI で解決されるクラス（Service, ViewModel）は `Microsoft.Extensions.Logging.ILogger<T>` に統一し、NLog provider 経由で出力することで CvServer と同じパターンにした
- Static クラスや DI 外のクラス（AppGlobal, ClientLib, MainMenuViewModel, LoginViewModel）は NLog 直接利用のまま `private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger()` に書き方を統一した
- Debug.WriteLine / Console.WriteLine は本番環境でログが残らないため、全て NLog 経由に置換した
### 確認
- `dotnet build CvWpfclient/CvWpfclient.csproj` → ビルド成功（警告0、エラー0）

---

## [2026-04-05] 09:45 Scheduler gRPC契約をEnumベースへ整理
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：NuGetパッケージ `NCrontab.Scheduler` の使い方調査に続き、`TaskType` を Enum で扱うパターン2の契約整理を実装し、チェックリスト作成からコミットまで実行する
### 実施内容
- CodeShare/IScheduler.cs: `SchedulerTaskType` enum、`AddSchedulerTaskRequest`、`RemoveSchedulerTaskRequest` を追加し、`ICvnetScheduler` の要求DTOを追加用・削除用に分離。`SchedulerResult` に `TaskId` を追加
- Cvnet10Server/Services/CvnetSchedulerService.cs: 新DTOへ追従し、cron式検証、`TaskType` の入力検証、結果コード返却、`TaskId` ベース削除、`LogOnly` 実行分岐、運用ログ出力を追加
- Cvnet10Server/Program.cs: `NCrontab.Scheduler` セクションを `appsettings` から読み込む構成へ変更
- Cvnet10Server/appsettings.json: `NCrontab.Scheduler:DateTimeKind=Local` を追加
- Cvnet10Server/appsettings.Development.json: `NCrontab.Scheduler:DateTimeKind=Local` を追加
- Cvnet10Server/appsettings.Production.json: `NCrontab.Scheduler:DateTimeKind=Local` を追加
- .sisyphus/2026-04-05_scheduler-contract-note.md: 今回の契約整理メモを追加
### 技術決定 Why
- `Type` を DTO に載せると gRPC 境界で不安定なため、`SchedulerTaskType` enum に置き換えて契約を明確化した
- 追加要求と削除要求を分離し、`CronExpression` と `TaskId` の責務混在を解消した
- scheduler の時刻解釈をコード固定値から設定ファイルへ移し、環境差分に追従しやすくした
### 確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet restore creativevision10.slnx && dotnet build Cvnet10Server/Cvnet10Server.csproj"` → ビルド成功（警告0、エラー0）

---

## [2026-04-04] 22:32 publish-velopack 実行時に appsettings.json の Version を自動加算
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`Cvnet10Wpfclient/publish-velopack.bat` 実行時に `appsettings.json` の `Application.Version` を読み取り、末尾の数値をカウントアップしてから publish 処理を続行したい
### 実施内容
- Cvnet10Wpfclient/publish-velopack.version.ps1: `-Increment` スイッチを追加し、`x.y.z` 形式の `Version` の第3要素を `+1` して `appsettings.json` へ書き戻す処理を実装
- Cvnet10Wpfclient/publish-velopack.version.ps1: Windows PowerShell 5.x でも UTF-8(BOMなし) の日本語コメントが壊れないように `ReadAllText` / `WriteAllText` を UTF-8 指定で統一
- Cvnet10Wpfclient/publish-velopack.version.ps1: 置換文字列の `$11` 誤解釈を避けるため、正規表現置換を MatchEvaluator 方式に変更
- Cvnet10Wpfclient/publish-velopack.bat: `publish-velopack.version.ps1 -Increment` を呼ぶように変更し、更新後の `APP_VERSION` を publish / pack に渡すよう調整
- Cvnet10Wpfclient/appsettings.json: 検証と `publish-velopack.bat` 本実行により `Application.Version` が `1.0.1` から `1.0.4` へ更新された状態を確認
### 技術決定 Why
- 既存の PowerShell スクリプトを拡張することで、バッチ側の変更を最小限に抑えつつ既存のバージョン取得フローを維持した
- Windows 11 上の `powershell.exe` 実行を前提にすると、UTF-8(BOMなし) の既存 JSON は明示的に UTF-8 指定しないと文字化けするため、I/O を .NET API に統一した
### 確認
- `powershell.exe -File publish-velopack.version.ps1 -AppSettingsPath ... -Increment` を 2 回実行し、`1.0.1 -> 1.0.2 -> 1.0.3` と更新されることを確認
- `cmd.exe /d /c "C:\gitroot\documents\new2022\cv10\Cvnet10Wpfclient\publish-velopack.bat"` → publish / vpk pack / `bash ~/bin/publish.sh` まで成功し、`Version=1.0.4` で完了することを確認

---

## [2026-04-04] 18:39 publish-velopack.bat を Windows 11 の cmd.exe で実行可能に修正
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`Cvnet10Wpfclient` 配下の `publish-velopack.bat` が Windows 11 の `cmd.exe` で正常動作するよう修正する
### 実施内容
- Cvnet10Wpfclient/publish-velopack.bat: `for /f` 内のインライン PowerShell を廃止し、補助スクリプト呼び出しへ変更
- Cvnet10Wpfclient/publish-velopack.bat: エラーメッセージと TODO コメントを ASCII ベースへ変更し、`cmd.exe` の文字コード解釈で構文が壊れにくい形へ整理
- Cvnet10Wpfclient/publish-velopack.version.ps1: `appsettings.json` から `Application.Version` を正規表現で抽出する補助スクリプトを追加
### 技術決定 Why
- `for /f (...) do` の中で PowerShell の丸括弧を含むインライン式を使うと `cmd.exe` 側で `FOR` 構文が壊れるため、`-File` 呼び出しへ分離して解釈系を分けた
- `appsettings.json` に `/* ... */` コメントが含まれており `ConvertFrom-Json` が安定しないため、コメント付きでも取得できる文字列抽出に切り替えた
- 実バッチだけ失敗して最小テストが通る状態だったため、非 ASCII 文字列も除去して `cmd.exe` 依存の文字コード要因を避けた
### 確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\documents\new2022\cv10\Cvnet10Wpfclient\publish-velopack.bat"` → `dotnet publish` と `vpk pack` が完走し、`[INFO] Velopack finished task for creating package. Version=1.0.0` を確認

---

## [2026-04-04] 22:55 Wpfclient の Velopack 自動更新確認と SysUpgrade 改修
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`Cvnet10Wpfclient` で Velopack の自動更新確認が動いていない原因を解消し、起動時の自動更新確認と `SysUpgradeViewModel` の手動更新処理に例外処理を入れて、最後に git commit まで完了する
### 実施内容
- Cvnet10Wpfclient/App.xaml.cs: `appsettings.Production.json` を既定読込に追加し、`IUpdateService` を DI 登録、起動後に自動更新確認して更新があれば確認ダイアログから適用できる処理を追加
- Cvnet10Wpfclient/Services/UpdateService.cs: 更新確認結果と更新適用結果を返す record を追加し、`FeedUrl` 未設定時や通信失敗時を含むメッセージ生成と `try/catch` によるエラーハンドリングを整理
- Cvnet10Wpfclient/ViewModels/00System/SysUpgradeViewModel.cs: `IUpdateService` を DI から取得するよう変更し、手動更新確認/適用の `try/catch`、状態文言更新、`ExecuteUpdateCommand` の `CanExecute` 再評価、表示情報更新を追加
- Cvnet10Wpfclient/ViewModels/SampleViewModel.cs: Velopack 診断表示の `PackId` 表記を実際の配布スクリプトに合わせて統一
- Doc/velopack_release_manual.md: `packId` の記載を実運用値へ統一
### 技術決定 Why
- 自動更新が動かなかった主因は `Update:FeedUrl` が `appsettings.Production.json` にしかない一方で、通常起動時にその設定を読まない構成だったため、既定読込へ追加して更新先 URL を常に解決できるようにした
- 起動時の自動更新確認と手動更新画面で別ロジックを持つと挙動差が出やすいため、`UpdateService` の結果オブジェクトに状態文言を集約して同一経路で扱うようにした
### 確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build Cvnet10Wpfclient/Cvnet10Wpfclient.csproj"` → ビルド成功（警告0、エラー0）

---

## [2026-04-04] 18:08 Cvnet10WpfclientのVelopack導入と配布手順整備
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`Cvnet10Wpfclient` に Velopack を導入し、ClickOnce 相当の更新処理と配布手順へ置き換える
### 実施内容
- Directory.Packages.props: `Velopack` の中央管理パッケージを追加
- Cvnet10Wpfclient/Cvnet10Wpfclient.csproj: `Velopack` 参照、WPF の `Main` 起動設定、ClickOnce 発行ターゲット削除を反映
- Cvnet10Wpfclient/App.xaml.cs: `VelopackApp.Build().Run()` を先頭で実行する `Main` エントリーポイントを追加
- Cvnet10Wpfclient/Services/UpdateService.cs: ClickOnce 風の更新処理を `UpdateManager` ベースへ置換
- Cvnet10Wpfclient/ViewModels/00System/SysUpgradeViewModel.cs: Velopack 更新確認文言へ調整し、設定から `FeedUrl` を読む形へ変更
- Cvnet10Wpfclient/ViewModels/SampleViewModel.cs: ClickOnce テスト表示を Velopack 設定表示/実行情報表示へ置換
- Cvnet10Wpfclient/Views/SampleView.xaml: サンプル画面のボタン文言とバインディングを Velopack 用に変更
- Cvnet10Wpfclient/appsettings.json: `Application.Version` を追加
- Cvnet10Wpfclient/appsettings.Production.json: `Update:FeedUrl` と `Channel` の本番設定雛形を追加
- Cvnet10Wpfclient/pre-publish-backup.bat: 廃止のため削除
- Cvnet10Wpfclient/publish-velopack.bat: `appsettings.json` の版数を読み取って `dotnet publish` と `vpk pack` を実行する配布バッチを追加
- Doc/velopack_release_manual.md: 版数更新から Velopack 配布までの手順書を追加
### 技術決定 Why
- WPF の `Main` を明示して `VelopackApp.Build().Run()` を最初に実行することで、更新適用時の起動経路を Velopack 推奨形へ寄せた
- 版数源を `appsettings.json` に一本化し、実行表示と配布版数の不整合を減らした
- 配布先URLは `appsettings.Production.json` に分離し、環境ごとの差し替えをしやすくした
### 確認
- `dotnet build "Cvnet10Wpfclient/Cvnet10Wpfclient.csproj" /p:EnableWindowsTargeting=true /p:UseAppHost=false` → ビルド成功（警告0、エラー0）

---

## [2026-04-03] 16:44 SelectServerTableViewの取得件数対応と汎用メンテ強化
### Agent
- gpt-5.4-mini : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`SelectServerTableView` を readonly 化し、取得件数を指定して `SysGeneralMenteView` を起動できるようにする
### 実施内容
- Cvnet10Wpfclient/Views/Sub/SelectServerTableView.xaml: 一覧を readonly 化し、取得件数入力欄を追加
- Cvnet10Wpfclient/ViewModels/Sub/SelectServerTableViewModel.cs: `SelectedRowCount` を追加して既定値を 200 に設定
- Cvnet10Wpfclient/ViewModels/MainMenuViewModel.cs: 選択テーブル名と取得件数を `AddInfo` で `SysGeneralMenteViewModel` に引き渡すよう調整
- Cvnet10Wpfclient/ViewModels/00System/SysGeneralMenteViewModel.cs: `AddInfo` の `テーブル名|取得件数` 形式を解釈し、`MasterMeisho` 依存を除去して汎用一覧に対応
### 技術決定 Why
- 既存の画面遷移と `AddInfo` を活用して連携点を最小化しつつ、取得件数は `QueryListParam.MaxCount` を使うことで既存の検索基盤に自然に統合した
- 編集行のタイトル生成を固定項目依存から外し、任意テーブルでも破綻しないようにした
### 確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build Cvnet10Wpfclient/Cvnet10Wpfclient.csproj"` → ビルド成功（警告0、エラー0）

---

## [2026-04-03] 14:45 SysGeneralMenteView起動前のテーブル選択導線追加
### Agent
- gpt-5.3-codex : GitHub-Copilot
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`SysGeneralMenteView` の前に `SelectServerTableView` を呼び出し、選択したテーブル名を元に `SysGeneralMenteView` を実行できるようにする
### 実施内容
- Cvnet10Wpfclient/ViewModels/MainMenuViewModel.cs: `SysGeneralMenteView` 起動時のみ `SelectServerTableView` を先に表示し、選択テーブル名を `AddInfo` で引き渡す処理を追加
- Cvnet10Wpfclient/ViewModels/00System/SysGeneralMenteViewModel.cs: `AddInfo` のテーブル名から `BaseDbClass` 派生型を解決し、対象型を動的に切り替えて一覧取得/追加/更新/削除が動くよう汎用化
- Doc/aicording_log_001.md: 既存ログを800行超過ルールに従ってアーカイブ
- Doc/aicording_log.md: 新規ログファイルを作成し本作業を記録
### 技術決定 Why
- 既存メニュー基盤（`MenuData` + `MainMenuViewModel.DoMenu`）を保ちつつ、`SysGeneralMenteView` だけに前段ダイアログを差し込むことで他画面への影響を最小化した
- 汎用メンテ対象はテーブル名から `TableNameAttribute` を逆引きして型解決し、既存の編集UI構造を維持したまま対象テーブルを切り替えられる設計にした
### 確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build Cvnet10Wpfclient/Cvnet10Wpfclient.csproj"` → ビルド成功（エラー0、警告0）

---

## [2026-04-03] 15:30 SysGeneralMenteViewModel で SerializedColumn 付き項目を JSON 編集可能にする
### Agent
- claude-opus-4.6 : GitHub-Copilot
### Editor
- OpenCode
### 目的
- ユーザーからの要望：SysGeneralMenteViewModel の `GetEditableProperties()` で読み飛ばしている `List<MasterGeneralMeisho>` や `BaseDetailClass` を JSON serialize して修正可能な項目にする
### 実施内容
- Cvnet10Wpfclient/ViewModels/00System/SysGeneralMenteViewModel.cs:
  - `SysGeneralEditCell` に `IsJsonColumn` プロパティを追加
  - `IsSupportedProperty()` に `[SerializedColumn]` 属性チェックを追加し、JSON 格納型も編集対象に含める
  - `IsJsonSerializedProperty()` ヘルパーを追加（`[SerializedColumn]` かつ非 primitive 型を判定）
  - `CreateRow()` で JSON 列には `ToJsonText()` を使用し `IsJsonColumn` フラグを設定
  - `ToItem()` で JSON 列には `ConvertFromJsonText()` で逆変換
  - `ToJsonText()`: `JsonConvert.SerializeObject` で整形済み JSON 文字列を生成
  - `ConvertFromJsonText()`: `JsonConvert.DeserializeObject` で JSON 文字列から型復元
### 技術決定 Why
- `IsSupportedType` の primitive ホワイトリストは変更せず、`IsSupportedProperty` のレベルで `[SerializedColumn]` を先にチェックすることで既存の primitive 列への影響をゼロにした
- NPoco の `[SerializedColumn]` 属性は DB に JSON 格納されることを意味するため、同じ JSON 形式でユーザーに編集させるのが自然
- `Newtonsoft.Json` は既に using 済みで `JsonConvert` が使えるため新規依存なし
### 確認
- `dotnet build Cvnet10Wpfclient/Cvnet10Wpfclient.csproj` → ビルド成功（エラー0、警告0）

---

## [2024-06-07] 11:43 MainMenuウィンドウ右上ボタンのMaterialDesignアイコン化
### Agent
- GitHub Copilot : OpenAI
### Editor
- VS2026
### 目的
- ユーザーからの要望：MainMenuウィンドウ右上のテーマ切替・最小化・メニューのみ・終了ボタンのアイコンをMaterialDesignのPackIconに差し替え、より洗練されたデザインにする。
### 実施内容
- CvWpfclient/Views/MainMenuView.xaml: 各ボタンのContentをPackIcon(ThemeLightDark, WindowMinimize, ViewList, Close)に変更。
### 技術決定 Why
- MaterialDesignInXamlToolkitのPackIconを利用することで、統一感のある最新UI/UXを実現。
### 確認
- ビルド成功、エラーなし

---

## [2026-04-08] 21:30 VersionTable.csのリファクタリング（POCO化・バグ修正・VersionSql外部化）
### Agent
- claude-opus-4.6 : GitHub-Copilot
### Editor
- OpenCode
### 目的
- ユーザーからの要望：CvBase/VersionTable.csのバージョン管理ロジックをリファクタリングし、より洗練された内容にする。現在未使用のためI/Fや名前は変更可能。
### 実施内容
- CvBase/VersionTable.cs: ObservableObject継承を除去しPOCO化（partialキーワード・ObservableProperty属性・CommunityToolkit.Mvvm using削除）
- CvBase/VersionTable.cs: 6つのprivateフィールドをpublic auto-propertyに変換（デフォルト値維持）
- CvBase/VersionTable.cs: VersionSql静的配列をコメントアウト（参考として残置、外部から引数で受け取る設計に移行）
- CvBase/VersionTable.cs: SubInsertRecordAsync内のバグ修正（既存レコード時にDDLを再実行→db.UpdateAsyncでレコード更新に修正）
- CvBase/VersionTable.cs: logggerタイポをloggerに修正
### 技術決定 Why
- ObservableObjectはUI通知用のMVVM基盤であり、DB操作ユーティリティクラスには不要。POCOにすることで依存を削減し責務を明確化
- line 133のdb.ExecuteAsync(item.DoSql)は、前段のループ(line 109-115)で実行済みのDDLを再実行するバグ。catch-and-continueにより表面化しなかったが、db.UpdateAsyncでレコードのメタ情報更新が正しい動作
- VersionSql配列は既にWriteVersionInfoAsyncが引数としてInnerVersion[]を受け取る設計になっており、静的配列は冗長。コメントアウトして参考として残す
### 確認
- CvBaseプロジェクトビルド成功（0警告・0エラー）
- 構造検証パス: ObservableObject関連除去確認、NPoco属性保持確認、バグ修正確認、プロパティ変換確認

---

## [2026-04-09] 13:30 LoadShohinImageAsync 商品画像読込バグ修正
### Agent
- claude-opus-4.6 : GitHub-Copilot : Build
### Editor
- OpenCode
### 目的
- ユーザーからの要望：商品マスターメンテの画像読込が失敗する問題の修正。ブラウザでは表示できるURLがアプリ内では読み込めない
### 実施内容
- CvWpfclient/ViewModels/01Master/MasterShohinMenteViewModel.cs: 商品画像読込の3点修正
  - HttpClient を遅延初期化に変更し、localhost自己署名証明書のSSL検証をスキップするよう対応
  - HTTPリクエストにJWT認証ヘッダー（Authorization: Bearer）を付与するよう修正
  - catch(Exception)の握り潰しをやめ、Debug.WriteLineでエラー内容を出力するよう改善
### 技術決定 Why
- `new HttpClient()` はデフォルトのSSL証明書検証を使用するため、開発環境のlocalhostの自己署名証明書を拒否する。ブラウザは手動で信頼できるが、HttpClientはできないため `DangerousAcceptAnyServerCertificateValidator` で対応。localhost限定の条件分岐で本番環境への影響を防止
- gRPC呼び出しでは `GetDefaultCallContext()` で認証ヘッダーを付与しているが、画像取得用の素のHttpClientには認証情報が未設定だった。HttpRequestMessageを使い毎回最新のLoginJwtを付与する方式に変更
- 遅延初期化 (`??=`) にした理由は、static フィールド初期化子の時点では `AppGlobal.Url` が未初期化のため
### 確認
- CvWpfclientビルド成功（0警告・0エラー）

---

## [2026-04-10] 14:30 MainMenuViewに天気・カレンダーダッシュボードを実装
### Agent
- claude-opus-4.6 : GitHub-Copilot : Build
### Editor
- OpenCode
### 目的
- ユーザーからの要望：MainMenuViewに天気情報（OpenWeatherMap API）と今日の予定（Google Calendar API）を表示するダッシュボードを実装する
### 実施内容
- Directory.Packages.props: Google.Apis.Calendar.v3、LiveChartsCore.SkiaSharpView.WPF、SkiaSharp.Views.WPFのバージョンを中央管理に追加
- CvWpfclient/CvWpfclient.csproj: 上記3パッケージのPackageReferenceを追加
- CvWpfclient/Models/WeatherModels.cs: WeatherInfo、HourlyForecast、CalendarEventItemモデルクラスを新規作成
- CvWpfclient/Services/WeatherService.cs: IWeatherService + WeatherService（OpenWeatherMap API呼び出し、MaterialDesignアイコンマッピング）を新規作成
- CvWpfclient/Services/GoogleCalendarService.cs: IGoogleCalendarService + GoogleCalendarService（OAuth2認証、イベント取得）を新規作成
- CvWpfclient/App.xaml.cs: WeatherService（AddHttpClient）とGoogleCalendarService（AddSingleton）のDI登録を追加
- CvWpfclient/ViewModels/MainMenuViewModel.cs: 天気情報プロパティ（WeatherIconKind, WeatherTemperature等）、LiveCharts2のISeries[]/Axis[]バインディング、カレンダーイベントObservableCollection、30分間隔更新タイマーを追加
- CvWpfclient/Views/MainMenuView.xaml: WeatherAndSchedulePanelの内容を削除し、左:天気カード＋右:気温推移チャート（LiveCharts2 CartesianChart）＋下:カレンダーアジェンダ（ItemsControl）に置換。lvc名前空間を追加
- CvWpfclient/appsettings.json: OpenWeatherApiKeyとGoogleOAuthSecretの設定キーを追加
### 技術決定 Why
- MainMenuViewModelはXAMLで直接インスタンス化（DI外）のため、App.AppHost.Services.GetService<T>()パターンでサービスを取得。既存のAppGlobal.GetGrpcServiceパターンと同様のアプローチ
- LiveCharts2のLineSeries with Fillでエリアチャートを表現し、SKColorでMaterialDesign風の配色を適用
- Google OAuth未設定時（clientId=="dummy"）はカレンダー機能を自動スキップし、APIキー未設定でも天気取得失敗をcatchして画面が壊れないように設計
- EventDateTime.DateTimeの非推奨警告をDateTimeDateTimeOffsetに修正
### 確認
- CvWpfclientビルド成功（0エラー、警告はNU1701のみ：既存のOpenTK/SkiaSharp互換性警告）

---
### Agent
- claude-opus-4.6 : GitHub-Copilot
### Editor
- OpenCode
### 目的
- ユーザーからの要望：商品マスターメンテ画面のImage表示をWebView2に変更し、URLの画像を直接表示する方式にする
### 実施内容
- CvWpfclient/ViewModels/01Master/MasterShohinMenteViewModel.cs: HttpClientによる画像ダウンロード処理を全削除（約100行）、BitmapImage/IsShohinImageLoading/CancellationTokenSourceを削除し、Uri?型のShohinImageUriプロパティに置換。OnCurrentEditChangedCoreを簡素化
- CvWpfclient/Views/01Master/MasterShohinMenteView.xaml: ImageコントロールをWebView2に置換、xmlns:Wpf名前空間を追加、ローディングオーバーレイ（ProgressBar）を削除、DataTriggerでUri==nullの時はWebView2をCollapsed
### 技術決定 Why
- HttpClientでの画像ダウンロード→BitmapImage生成→Freeze処理は複雑であり、キャンセル管理・エラーハンドリングのコードが大量だった。WebView2でURLを直接表示することで、ViewModel側のHTTP通信処理を全廃し大幅に簡素化
- JWT認証は画像エンドポイントに不要のためWebView2のデフォルト動作で対応可能
- Microsoft.Web.WebView2パッケージはcsproj・WebpdfViewで参照済みのため追加不要
### 確認
- CvWpfclientビルド成功（0警告・0エラー）

---

## [2026-04-10] 17:15 GoogleカレンダーのOAuth2.0認証をAPI Key認証に変更
### Agent
- claude-opus-4.6 : GitHub-Copilot
### Editor
- OpenCode
### 目的
- ユーザーからの要望：MainMenuViewModelで使用しているGoogleカレンダー連携を、Google OAuth2.0 ではなく Google API Key を使うよう変更する
### 実施内容
- CvWpfclient/Services/GoogleCalendarService.cs: OAuth2.0フロー（GoogleWebAuthorizationBroker.AuthorizeAsync、FileDataStore、ClientSecrets）を全て削除し、API Key によるCalendarService初期化に置換。EnsureAuthenticatedAsync(async)をEnsureInitialized(sync)に変更。不要なusing (Google.Apis.Auth.OAuth2, Google.Apis.Util.Store, System.IO) を削除
- CvWpfclient/appsettings.json: GoogleOAuthId、GoogleOAuthSecret設定キーを削除。GoogleApiKeyのみ残存
### 技術決定 Why
- OAuth2.0はブラウザベースの認証フローが必要でユーザー体験が煩雑。API Keyはサーバーレスで公開カレンダーデータの取得に十分であり、トークン管理やリフレッシュの複雑さを排除できる
- API Key方式はCalendarService.Initializer.ApiKeyを設定するだけで初期化でき、非同期処理も不要になるためコードが大幅に簡素化
### 確認
- CvWpfclientビルド成功（0エラー、既存NU1701警告のみ）

---

## [2026-04-10] 18:30 CvWpfclient TFM変更によるOpenTK/.NET Framework依存の解消
### Agent
- claude-opus-4.6 : GitHub-Copilot : Build
### Editor
- OpenCode
### 目的
- ユーザーからの要望：CvWpfclientで使用しているOpenTK NuGetパッケージを.NET 10用のOpenTK.Coreに変更。他のNuGetも.NET Framework用になっていないかチェック
### 実施内容
- CvWpfclient/CvWpfclient.csproj: TargetFrameworkを `net10.0-windows` → `net10.0-windows10.0.19041` に変更
### 技術決定 Why
- OpenTK 3.3.1（.NET Framework用）はCvWpfclientの直接参照ではなく、SkiaSharp.Views.WPF 3.119.2 経由の推移的依存だった
- SkiaSharp.Views.WPFは `net8.0-windows10.0.19041` と `.NETFramework 4.6.2` の2つのTFMアセットを持つ。TFMが `net10.0-windows`（Windows SDKバージョン未指定）の場合、NuGetが正しいアセットグループにマッチできず `.NETFramework` にフォールバックしていた
- TFMにWindows SDKバージョン `10.0.19041` を指定することで、SkiaSharp.Views.WPFが `net8.0-windows10.0.19041` アセット（OpenTK 4.3.0 + OpenTK.GLWpfControl 4.2.3依存）を正しく選択するようになった
- OpenTK.Coreへの直接パッケージ差し替えではなく、TFM修正が根本解決となる
### 影響範囲
- CvWpfclient出力パスが `net10.0-windows10.0.19041` に変更
- 推移的依存: OpenTK 3.3.1→4.3.0、OpenTK.GLWpfControl 3.3.0→4.2.3、OpenTK.Core 4.3.0追加
- 他の全NuGetパッケージに.NET Framework専用パッケージは無いことを確認済み
### 確認
- dotnet restore: NU1701警告ゼロ（変更前は3件のNU1701警告あり）
- dotnet build: 0警告、0エラー

---

## [2026-04-10] 17:30 GoogleCalendarService および関連コードの削除
### Agent
- claude-opus-4.6 : GitHub-Copilot
### Editor
- OpenCode
### 目的
- ユーザーからの要望：CvWpfclient から GoogleCalendarService とそのインターフェースを削除し、MainMenuView のカレンダー表示エリアを削除する（天気情報はそのまま残す）
### 実施内容
- CvWpfclient/CvWpfclient.csproj: Google.Apis.Calendar.v3 パッケージ参照を削除
- CvWpfclient/Services/GoogleCalendarService.cs: ファイルごと削除（IGoogleCalendarService インターフェースと GoogleCalendarService クラス）
- CvWpfclient/Models/WeatherModels.cs: CalendarEventItem クラスを削除（天気関連モデルは残存）
- CvWpfclient/App.xaml.cs: IGoogleCalendarService の DI 登録行を削除
- CvWpfclient/ViewModels/MainMenuViewModel.cs: CalendarEvents / CalendarStatus プロパティ、RefreshCalendarAsync メソッド、StartWeatherAndCalendar 内のカレンダー呼び出しを削除
- CvWpfclient/Views/MainMenuView.xaml: カレンダーアジェンダ表示エリア（materialDesign:Card）を削除、StackPanel 名を WeatherPanel に変更
### 技術決定 Why
- Google Calendar 機能が不要となったため、NuGetパッケージ依存を含めて完全にクリーンアップした。天気情報機能は独立しているためそのまま残した
### 確認
- dotnet build CvWpfclient/CvWpfclient.csproj: 0警告、0エラー

---
