# AI Coding Log

このファイルは、Cvnet10プロジェクトにおけるAI支援開発の作業履歴を記録します。

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

## [2026-04-01] 08:55 Cvnet10Prints の printenable 切替対応
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：VS2026のソリューションで、環境変数 `printenable` を使って `Cvnet10Prints.csproj` の `printstream.jar` / IKVM 参照を内部実装だけ切り替えたい
### 実施内容
- Cvnet10Prints/Cvnet10Prints.csproj: 環境変数 `printenable` を `PrintEnable` プロパティとして受け取り、`true` 時のみ `PRINT_ENABLE` 定義、`IKVM` の `PackageReference`、`printstream.jar` の `IkvmReference` を有効化
- Cvnet10Prints/PrintAdapter.cs: `jp.axissoft.printstream` の using と PrintStream 利用処理を `#if PRINT_ENABLE` で分岐し、無効時はダミー結果を返す実装に変更
### 技術決定 Why
- `Cvnet10Prints` の公開APIを維持したまま内部実装だけを切り替えることで、`Cvnet10Server` やテストプロジェクトの `ProjectReference` を変更せずにビルド構成だけで印刷機能の有効/無効を切り替えられるため
### 確認
- `set printenable=false&& C:\gitroot\UT\vscmd.bat dotnet build Cvnet10Prints/Cvnet10Prints.csproj` → ビルド成功
- `set printenable=true&& C:\gitroot\UT\vscmd.bat dotnet build Cvnet10Prints/Cvnet10Prints.csproj` → ビルド成功
- `set printenable=false&& C:\gitroot\UT\vscmd.bat dotnet build Cvnet10Server/Cvnet10Server.csproj` → ビルド成功
- `set printenable=true&& C:\gitroot\UT\vscmd.bat dotnet build Cvnet10Server/Cvnet10Server.csproj` → ビルド成功

---

## [2026-03-31] 22:15 マスターメンテ系「詳細を読み込みました」メッセージ非表示化
### Agent
- claude-opus-4.6 : GitHub-Copilot
### Editor
- OpenCode
### 目的
- ユーザーからの要望：マスターメンテ系画面で「詳細を読み込みました」メッセージが不要なので表示しないようにする
### 実施内容
- Cvnet10Wpfclient/Helpers/ViewModels/BaseLightMenteViewModel.cs: `ApplyLoadedDetail` メソッド内の `Message = $"詳細を読み込みました (Id={detail.Id})"` 行を削除
### 技術決定 Why
- BaseLightMenteViewModelは全マスターメンテ画面の基底クラスであり、ここで1行削除するだけで全画面に反映される
### 確認
- Cvnet10Wpfclient ビルド成功（エラー0、既存warning4件のみ）

---

## [2026-03-31] 17:30 SysLoginHistoryView に社員名表示を追加
### Agent
- claude-opus-4.6 : GitHub-Copilot
### Editor
- OpenCode
### 目的
- ユーザーからの要望：SysLoginHistoryView の右側詳細パネルで、ログインIDに対応したユーザ名（MasterShain の Code, Name）も表示する。ログインIDと有効期限の間に1行で配置。
### 実施内容
- Cvnet10Wpfclient/ViewModels/00System/SysLoginHistoryViewModel.cs: AfterList で SysLogin を一括取得し loginId → 社員表示文字列のマッピングを構築。OnCurrentEditChangedCore で選択変更時に ShainDisplay を更新。
- Cvnet10Wpfclient/Views/00System/SysLoginHistoryView.xaml: 右パネル詳細の Grid に Row を1行追加（Row 1: 社員）。ログインId(Row 0) と有効期限(Row 2) の間に ShainDisplay をバインド表示。既存行を Row+1 にシフト。
### 技術決定 Why
- SysHistJwt には社員情報が直接保持されていないため、Id_Login → SysLogin → VShain (CodeNameView) の間接参照で解決。AfterList での一括取得により N+1 問題を回避。
### 確認
- WPFクライアントビルド成功（0 エラー、既存警告のみ）

---

## [2026-03-29] 18:31 opentmux 3ペイン集中作業向け設定
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`opentmux` プラグインの設定ファイルを README に沿って確認し、基本 3 ペイン運用向けの案から集中作業向け設定を適用する
### 実施内容
- `~/.config/opencode/opencode.json`: `plugin` に `opentmux` が設定済みであることを確認した
- `~/.config/opencode/opentmux.json`: 3 ペイン時に作業ペインを広く確保するため、`layout` は `main-vertical` のまま `main_pane_size` を `60` から `72` に変更した
- `tmux` と `opentmux` のインストール状態を確認し、セットアップ済みであることを確認した
### 技術決定 Why
- 3 ペイン運用ではメインペインを広めに取る方がコード編集やコマンド実行を継続しやすいため、監視用サブペインを細めにする `main-vertical` + `72` を採用した
### 確認
- `~/.config/opencode/opencode.json` に `opentmux` プラグイン設定が存在することを確認
- `~/.config/opencode/opentmux.json` の更新内容を確認
- `which tmux` と `which opentmux`、`npm list -g opentmux --depth=0` で利用可能な状態を確認

## [2026-03-29] 17:52 PrintServer設定で印刷フォルダを切替
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`Cvnet10Server.csproj` で読み込まれる `appsettings.json` の `PrintServer` セクションを使い、`CvnetCorePrintService.cs` の `printPdf()` 内で印刷フォルダ設定を切り替える。Write-Log と Git-Commit まで実行する
### 実施内容
- `Cvnet10Server/Services/CvnetCorePrintService.cs`: `printPdf()` の手書きディレクトリ探索を廃止し、`IConfiguration` の `PrintServer` から `PrintBaseDir`、`PrintFormDir`、`PrintDataDir`、`PrintOutputDir` を読んで `PrintContext` を構築するよう変更した
- `Cvnet10Server/Services/CvnetCorePrintService.cs`: 相対パスを `ContentRootPath` 基準で絶対化し、出力先フォルダを `Directory.CreateDirectory` で事前生成するようにした
- `Cvnet10Server/appsettings.json`: 実際のテンプレート配置に合わせ、既定の `PrintServer` を `.. / printdata / printdata / Cvnet10Server/wrk` に更新した
- `Cvnet10Server/Cvnet10Server.csproj`: ASP.NET Core の既定動作で `appsettings*.json` が読み込まれるため変更不要と判断した
### 技術決定 Why
- 実行環境ごとの親ディレクトリ探索や Linux 分岐に依存すると保守しづらいため、サーバー設定から一貫して印刷入出力パスを決める構成へ寄せた
- `PrintBaseDir` を基点に相対パスを解決することで、フォーム名とデータ名を固定のままでも配置変更を設定だけで吸収できるようにした
### 確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build Cvnet10Server/Cvnet10Server.csproj"` でビルド成功

## [2026-03-28] 22:03 ConvertTranHeadersByRangeへの統一
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`Cvnet10DomainLogic/ConvertDbTran.cs` で `ConvertTranHeaders<T>` を呼んでいる処理を `ConvertTranHeadersByRange<T>` に変更し、不要になった `ConvertTranHeaders<T>` を削除して Write-Log と Git-Commit まで行う
### 実施内容
- `Cvnet10DomainLogic/ConvertDbTran.cs`: `CnvTran00HonUri`、`CnvTran03Shiire`、`CnvTran05Ido`、`CnvTran06Nyukin`、`CnvTran07Shiharai`、`CnvTran60Tana`、`CnvTran10Ido`、`CnvTran11IdoIn`、`CnvTran12Jyuchu`、`CnvTran13Hachu` の呼び出し先を `ConvertTranHeadersByRange` に統一した
- `Cvnet10DomainLogic/ConvertDbTran.cs`: 未使用になった `ConvertTranHeaders<T>` を削除し、範囲分割変換メソッドの要約コメントを現状に合わせて更新した
### 技術決定 Why
- 伝票変換処理の呼び出し経路を範囲分割版へ統一することで、大量データ時のメモリ負荷を抑える実装に揃えつつ、20000件未満でも同じ経路で動作させて保守対象を一本化した
### 確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build Cvnet10DomainLogic/Cvnet10DomainLogic.csproj"` でビルド成功

## [2026-03-28] 10:10 ConvertDbTran範囲変換の整理
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`ConvertDbTran.cs` の中の `ConvertTranHeadersByRange` と関連する処理をリファクタリングして
### 実施内容
- `Cvnet10DomainLogic/ConvertDbTran.cs`: `ConvertTranHeaders` と `ConvertTranHeadersByRange` で共通だったヘッダ取得 SQL と一括 insert 処理を共通ヘルパーへ集約した
- `Cvnet10DomainLogic/ConvertDbTran.cs`: 範囲分割処理を `SplitRange` と `GetTranHeaderRangeInfo` に整理し、範囲変換の責務を読みやすく分離した
- `Cvnet10DomainLogic/ConvertDbTran.cs`: 生成AI由来の作成者コメントや補助メソッド名を整理し、既存挙動を保ったまま意図が伝わる命名へ更新した
### 技術決定 Why
- 通常変換と範囲分割変換で SQL 組み立てと insert 処理が重複していたため、クエリ組み立てと bulk insert を共通化して保守時の差分発生を防ぎやすくした
- `ConvertTranHeadersByRange` は範囲取得・分割・読込・insert の流れだけに絞ることで、処理本体の見通しを優先した
### 確認
- `dotnet build "Cvnet10DomainLogic/Cvnet10DomainLogic.csproj"` でビルド成功

## [2026-03-27] 14:15 改行コードのCRLF統一
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：プロジェクト内のテキストファイルの改行コードが CRLF で統一されているか確認し、CRLF でないものは改行コードのみ変更して統一する。git commit は行わない
### 実施内容
- `git ls-files` で取得した追跡済みファイルを対象に、バイナリ判定後のテキストファイルだけを走査し、CRLF でないファイルを CRLF に統一した
- リポジトリ全体を再走査し、追跡済みテキストファイルで CRLF でないものが 0 件であることを確認した
### 技術決定 Why
- 既存内容を変更しないため、改行コード以外の文字列・空白・構文には触れず、バイト列の改行表現のみを `CRLF` へ正規化した
- バイナリファイルや改行を持たないファイルを除外し、テキストファイルの改行コード統一だけに作用範囲を限定した
### 確認
- 再スキャン結果: 追跡済みテキストファイルの非 `CRLF` 件数は 0
- 内容変更を伴う作業ではないため、ビルドは未実行

---

## [2026-03-27] 13:18 AppGlobal低リスク整理
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`Cvnet10Wpfclient` の `AppGlobal.cs` を読み込み、低リスクな範囲に限定して具体計画を確認したうえで step by step で実装し、Git-Commit は行わずユーザー確認待ちにする
### 実施内容
- `Cvnet10Wpfclient/AppGlobal.cs`: `LoginJwt` をプロパティ化し、`SetLoginJwt` / `ClearLoginJwt` を追加して更新経路を明示した
- `Cvnet10Wpfclient/AppGlobal.cs`: `GetgRPCService<T>` を `GetGrpcService<T>` に改名し、初期化前例外文言とログ内の `AppCurrent` 表記を `AppGlobal` に統一した
- `Cvnet10Wpfclient/AppGlobal.cs`: `CallContext` 用メタデータ生成を `CreateDefaultMetadata` に分離して見通しを改善した
- `Cvnet10Wpfclient/ViewModels/00System/LoginViewModel.cs`: JWT 代入の重複を解消し、新しい API を使うよう追従した
- `Cvnet10Wpfclient/Helpers/ViewModels/BaseMenteViewModel.cs`: gRPC サービス取得呼び出しを新メソッド名へ追従した
- `Cvnet10Wpfclient/ViewModels/00System/SysSetConfigViewModel.cs`: gRPC サービス取得呼び出しを新メソッド名へ追従した
- `Cvnet10Wpfclient/ViewModels/SampleViewModel.cs`: gRPC サービス取得呼び出しを新メソッド名へ追従した
- `Cvnet10Wpfclient/ViewModels/Sub/SelectWinViewModel.cs`: gRPC サービス取得呼び出しを新メソッド名へ追従した
- `Cvnet10Wpfclient/ViewModels/Sub/SelectKubunViewModel.cs`: gRPC サービス取得呼び出しを新メソッド名へ追従した
- `Cvnet10Wpfclient/ViewModels/04Juchu/JuchuInputViewModel.cs`: gRPC サービス取得呼び出しを新メソッド名へ追従した
- `Cvnet10Wpfclient/ViewModels/06Uriage/ShukkaUriageInputViewModel.cs`: gRPC サービス取得呼び出しを新メソッド名へ追従した
### 技術決定 Why
- `AppGlobal` の責務分割や DI 注入への移行は影響範囲が大きいため今回は見送り、公開 API の整理と内部ヘルパー化だけに絞ってコンパイル影響を最小化した
- `LoginViewModel` の JWT 更新は同一値の二重代入になっていたため、成功確定時のみ設定する形へ寄せて意図を明確化した
### 確認
- `dotnet build "Cvnet10Wpfclient/Cvnet10Wpfclient.csproj" /p:EnableWindowsTargeting=true /p:UseAppHost=false` でビルド成功

## [2026-03-27] 13:35 CallContext正規化と懸念コメント追記
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`CallContext` を正とする方針を固定し、`JwtAuthorizationHandler` との重複を整理したうえで、実害候補の懸念点をコメントとして残す。コミットは行わない
### 実施内容
- `Cvnet10Wpfclient/Helpers/Communication/JwtAuthorizationHandler.cs`: 認証系ヘッダー付与を廃止し、`AppGlobal.GetDefaultCallContext()` を正とする方針コメントへ更新した
- `Cvnet10Wpfclient/AppGlobal.cs`: `CreateDefaultMetadata()` に、匿名呼び出し時でも `Authorization: Bearer ` を送る現状挙動と、将来見直し時の起点にする旨のコメントを追記した
### 技術決定 Why
- 既存の gRPC 呼び出しは `GetDefaultCallContext(...)` 利用で概ね統一されており、`CancellationToken` も同じ経路で扱っているため、`DelegatingHandler` 側を削るほうが影響範囲を最小化できる
- 重複責務を外したうえで匿名 API まわりの挙動差だけをコメントで残し、次回調査や仕様変更時に判断しやすくした
### 確認
- `dotnet build "Cvnet10Wpfclient/Cvnet10Wpfclient.csproj" /p:EnableWindowsTargeting=true /p:UseAppHost=false` でビルド成功

## [2026-03-27] 12:41 MasterSysKanriMenteView上部レイアウト統一
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`MasterSysKanriMenteView.xaml` の画面上部を、名称マスタや商品マスタのようなレイアウトに合わせる。ただし単一レコード画面のため、現在の入力項目配置部分は変更しない
### 実施内容
- `Cvnet10Wpfclient/Views/01Master/MasterSysKanriMenteView.xaml`: 旧来の上部ボタン帯を `ColorZone` ベースの共通ヘッダーへ置き換え、閉じる・再読込・修正の操作を名称マスタ系の見た目に統一した
- `Cvnet10Wpfclient/Views/01Master/MasterSysKanriMenteView.xaml`: 詳細入力部は既存の行列配置を維持したまま `Card` とステータス帯で囲み、`Desc0` と `Current.Id` を上部情報として表示する構成へ整理した
### 技術決定 Why
- 単一レコード画面のため一覧ペインは追加せず、共通化対象をヘッダーと情報帯に限定することで、ユーザー指定どおり入力フォームの並びやバインディングを変えずにデザインだけを寄せた
### 確認
- `dotnet build "Cvnet10Wpfclient/Cvnet10Wpfclient.csproj" /p:EnableWindowsTargeting=true /p:UseAppHost=false` でビルド成功

## [2026-03-27] 12:09 軽量一覧と詳細遅延取得の共通基底実装
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：Cvnet10Wpfclient のマスタメンテ等で、100,000件規模でも扱いやすいように「一覧は軽量、詳細は非同期取得＆キャッシュ」を共通基底まで含めて設計し、`IBaseCodeName` あり/なしの2系統で段階実装する
### 実施内容
- `Cvnet10Wpfclient/Helpers/ViewModels/BaseMenteViewModel.cs`: 一覧取得メッセージ生成を `CreateListMessage` に分離し、軽量一覧基底から差し替え可能にした
- `Cvnet10Wpfclient/Helpers/ViewModels/BaseLightMenteViewModel.cs`: 200msデバウンス、`QuerybyIdParam` による詳細非同期取得、`Id`/`Vdu` キャッシュ、一覧行の詳細差し替えを行う共通基底と、`IBaseCodeName` あり/なしの派生基底を新規追加した
- `Cvnet10Wpfclient/ViewModels/01Master/MasterShohinMenteViewModel.cs`: `BaseCodeNameLightMenteViewModel` 継承へ切り替え、軽量一覧に `VBrand` を含めた商品マスタの代表実装へ変更した
- `Cvnet10Wpfclient/ViewModels/00System/SysLoginViewModel.cs`: `BasePlainLightMenteViewModel` 継承へ切り替え、ログイン一覧の軽量取得列を ViewModel 側で定義する代表実装へ変更した
### 技術決定 Why
- 既存XAMLの `ListData` / `Current` / `CurrentEdit` バインディングを崩さず段階導入するため、一覧DTOを別コレクションへ分離せず、`ListData<T>` 自体を軽量行→詳細行へ差し替える方式を採用した
- `IBaseCodeName` 実装テーブルは共通列が明確なため基底で既定化し、非実装テーブルは画面ごとに必要列が異なるため ViewModel 側で選択列を定義する分離設計にした
### 確認
- `dotnet build "Cvnet10Wpfclient/Cvnet10Wpfclient.csproj" /p:EnableWindowsTargeting=true /p:UseAppHost=false` でビルド成功

## [2026-03-27] 12:17 軽量一覧詳細反映時のDataGridフリーズ修正
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`MasterShohinMenteViewModel` と `SysLoginViewModel` で、一覧から詳細取得した時に画面が固まる原因を調査し、正常動作するよう修正する
### 実施内容
- `Cvnet10Wpfclient/Helpers/ViewModels/BaseLightMenteViewModel.cs`: 詳細取得後に `ListData[index] = 新規インスタンス` で一覧行を差し替えていた処理をやめ、既存選択行へ `Common.DeepCopyValue` で詳細を上書きする方式へ変更した
### 技術決定 Why
- 既存画面は `DataGrid.ItemsSource=ListData` と `SelectedItem=Current` を併用しており、選択中行を別インスタンスへ置換すると WPF の現在行同期が不安定になり、詳細取得直後にUIが固まる原因になるため、参照を維持したまま中身だけ更新する方式へ切り替えた
### 確認
- `dotnet build "Cvnet10Wpfclient/Cvnet10Wpfclient.csproj" /p:EnableWindowsTargeting=true /p:UseAppHost=false` でビルド成功

## [2026-03-27] 12:22 詳細取得キャンセル時の破棄例外抑止
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：商品マスタ終了時に `CancellationTokenSource has been disposed` の未観測例外が出るため、終了時も正常に閉じられるよう修正する
### 実施内容
- `Cvnet10Wpfclient/Helpers/ViewModels/BaseLightMenteViewModel.cs`: 進行中詳細取得のキャンセル時に `CancellationTokenSource` を即時 `Dispose` しないよう変更し、待機タスク側の `finally` でのみ破棄する構成へ修正した
- `Cvnet10Wpfclient/Helpers/ViewModels/BaseLightMenteViewModel.cs`: `Task.Delay`/`Dispose` 周辺に `ObjectDisposedException` の保護を追加し、未観測例外が finalizer thread へ流れないようにした
### 技術決定 Why
- デバウンス待機タスクが `CancellationTokenSource` を参照中のまま別経路で `Dispose` すると、終了時に未観測の `ObjectDisposedException` が発生するため、キャンセルと破棄の責務を分離した
### 確認
- `dotnet build "Cvnet10Wpfclient/Cvnet10Wpfclient.csproj" /p:EnableWindowsTargeting=true /p:UseAppHost=false` を実行したが、起動中プロセスが `Cvnet10Wpfclient/bin/Debug/net10.0-windows/*.dll` をロックしておりコピー失敗で確認不能

## [2026-03-27] 12:30 全マスターメンテへの軽量一覧展開
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：中間コミット後、軽量一覧と詳細遅延取得の仕組みを全マスターへ展開し、作業ログ記録とGitコミットまで実施する
### 実施内容
- `Cvnet10Wpfclient/ViewModels/01Master/MasterMeishoMenteViewModel.cs`: `BaseCodeNameLightMenteViewModel` 継承へ切り替え、一覧列に `Kubun` / `Odr` / `KubunName` を追加した
- `Cvnet10Wpfclient/ViewModels/01Master/MasterEndCustomerMenteViewModel.cs`: `BaseCodeNameLightMenteViewModel` 継承へ切り替え、一覧列に `Rank` / `VTenpo` を追加した
- `Cvnet10Wpfclient/ViewModels/01Master/MasterShainMenteViewModel.cs`: `BaseCodeNameLightMenteViewModel` 継承へ切り替え、一覧列に `Mail` / `VTenpo` / `VBumon` を追加した
- `Cvnet10Wpfclient/ViewModels/01Master/MasterShiireMenteViewModel.cs`: `BaseCodeNameLightMenteViewModel` 継承へ切り替えた
- `Cvnet10Wpfclient/ViewModels/01Master/MasterTokuiMenteViewModel.cs`: `BaseCodeNameLightMenteViewModel` 継承へ切り替え、一覧列に `TenType` を追加した
### 技術決定 Why
- 一覧DataGridに表示している列だけを軽量取得対象へ追加し、それ以外の詳細情報は既存の詳細取得とキャッシュへ委ねることで、既存XAMLを変えずに全マスターメンテへ横展開した
- `MasterSysKanriMenteViewModel` は一覧を持たず `Current.*` へ直接バインドする単一レコード画面のため、今回の一覧最適化対象から除外した
### 確認
- `dotnet build "Cvnet10Wpfclient/Cvnet10Wpfclient.csproj" /p:EnableWindowsTargeting=true /p:UseAppHost=false` でビルド成功

## [2026-03-27] 10:05 WPFスキル分離と参照整理
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`AGENTS.md` からWPF作成・改修ルールを分離し、Cvnet10Wpfclient全体向けスキルと個別View/ViewModel向けスキルに整理したうえで、`AGENTS.md` から必須参照させる
### 実施内容
- `.agents/skills/wpf-project-guide/SKILL.md`: `Cvnet10Wpfclient` 全体の共通規約、ResourceDictionary確認、`BaseWindow` 運用、検証方針を新規追加した
- `.agents/skills/wpf-view-workflow/SKILL.md`: 個別画面の新規作成・既存改修・`MenuData.cs` 連携・検証手順を新規追加した
- `AGENTS.md`: WPF詳細規約を縮約し、`wpf-project-guide` と `wpf-view-workflow` の必須参照、および既存WPF系スキルの使い分けに更新した
- `.agents/skills/check-xaml/SKILL.md`: 検証専用スキルである位置付けを追記した
- `.agents/skills/update-design-mente/SKILL.md`: 新しいWPF共通スキル群を前提とする説明を追記した
- `.agents/skills/change-sublist-to-observablecollection/SKILL.md`: 新しいWPF共通スキル群を前提とする説明を追記した
### 技術決定 Why
- `Cvnet10Wpfclient` 全体の前提知識と、個別画面の作成・改修手順は責務が異なるため、共通ガイドと画面ワークフローへ分離した
- 既存のWPF系スキルは用途特化のまま残し、新規2スキルを上位ガイドとして位置付けることで重複と参照迷いを減らした
### 確認
- 変更後ファイルを読み返し、`AGENTS.md` から `wpf-project-guide` / `wpf-view-workflow` の必須参照になっていることを確認
- ドキュメントとスキル定義のみの変更のため、ビルドは未実行

## [2026-03-25] 14:49 keep-mcp の OpenCode グローバル追加
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`https://github.com/feuerdev/keep-mcp` を確認し、OpenCode のグローバル環境に Google Keep 用 MCP サーバーを追加する
### 実施内容
- `~/.config/opencode/opencode.jsonc`: `keep-mcp` をローカル MCP サーバーとして追加し、専用ラッパースクリプト経由で起動する構成にした
- `~/.config/opencode/bin/keep-mcp-opencode`: `~/.config/opencode/keep-mcp.env` を読み込み、必須資格情報を検証したうえで `~/.local/share/keep-mcp/.venv/bin/python -m server` を起動するスクリプトを追加した
- `~/.config/opencode/keep-mcp.env.example`: `GOOGLE_EMAIL` / `GOOGLE_MASTER_TOKEN` / `UNSAFE_MODE` の設定ひな形を追加した
- `~/.local/share/keep-mcp/.venv`: Python 仮想環境を作成し、`keep-mcp==0.3.1` をインストールした
### 技術決定 Why
- `pipx` と `uv` が未導入だったため、システム Python を汚さないようユーザー配下の仮想環境で `keep-mcp` を隔離インストールした
- Google Keep の資格情報を OpenCode 設定本体へ直書きしないため、外部 env ファイルを読むラッパースクリプト方式を採用した
### 確認
- `~/.local/share/keep-mcp/.venv/bin/python -m server --help` でモジュール起動が可能なことを確認
- `~/.config/opencode/bin/keep-mcp-opencode` 実行時に、資格情報未設定の場合は案内付きエラーで停止することを確認
- `opencode mcp list` で `keep-mcp` エントリが認識されることを確認（現時点では資格情報未設定のため `failed` 表示）

---

## [2026-03-27] 10:26 copilot-instructions整合
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`AGENTS.md` の内容に合わせて `copilot-instructions.md` を最小限で修正する
### 実施内容
- `.github/copilot-instructions.md`: リポジトリ情報、復元・ビルド・フォーマット確認コマンド、レイヤー/読み取り専用ルール、最小差分方針、WPF参照ルール、Write-Log/Git-Commit 記述を `AGENTS.md` に合わせて最小限で補正した
### 技術決定 Why
- 既存の英語ベース構成は維持しつつ、`AGENTS.md` と不整合だった閾値、手順、参照先、運用ルールのみを補正して差分を最小化した
### 確認
- 変更後の `.github/copilot-instructions.md` を読み返し、`AGENTS.md` との差分が主に不足事項の補完に留まっていることを確認
- ドキュメント変更のみのため、ビルドは未実行

## [2026-03-27] 10:32 copilot-instructions英語統一
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`.github/copilot-instructions.md` の記述を英語に統一し、作業後に変更全体をコミットする
### 実施内容
- `.github/copilot-instructions.md`: コードブロック外の日本語記述を英語へ置換し、英語化の過程で残っていた日本語見出しを解消した
### 技術決定 Why
- ユーザー指定どおりコードブロックは変更対象にせず、通常本文だけを英語化して既存テンプレート構造への影響を抑えた
### 確認
- `grep` により `.github/copilot-instructions.md` のコードブロック外に日本語が残っていないことを確認
- ドキュメント変更のみのため、ビルドは未実行

## [2026-03-25] 13:31 ViewServices参照の削除とHelpers統一
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`Cvnet10Wpfclient` 配下の `using Cvnet10Wpfclient.ViewServices;` を削除し、XAML内の `ViewServices` 参照は不要なら削除、使用中なら `Cvnet10Wpfclient.Helpers` へ切り替える
### 実施内容
- Cvnet10Wpfclient/ViewModels 配下の各ViewModel: 未使用になっていた `using Cvnet10Wpfclient.ViewServices;` を一括削除
- Cvnet10Wpfclient/Helpers/MessageBoxView.xaml: `clr-namespace:Cvnet10Wpfclient.ViewServices` の `xmlns:local` 宣言を削除
- Cvnet10Wpfclient/Cvnet10Wpfclient.csproj: 残存していた `ViewServices\` フォルダー定義を削除
### 技術決定 Why
- `Cvnet10Wpfclient.ViewServices` 名前空間の実体が既に存在せず、XAML側でも当該名前空間の型利用がなかったため、要素参照の置換ではなく不要宣言の削除を優先した
- ユーザー指定の `CvcnetWpfclinet.Helpers` は実在せず、既存コードベースで一貫して使われている `Cvnet10Wpfclient.Helpers` を正とみなして整合性を維持した
### 確認
- `grep` にて `Cvnet10Wpfclient` 配下の `ViewServices` 参照が解消されたことを確認
- `dotnet build Cvnet10Wpfclient/Cvnet10Wpfclient.csproj /p:EnableWindowsTargeting=true /p:UseAppHost=false` 成功（0 warnings, 0 errors）

---

## [2026-03-29] 12:10 clipimg の opencode 連携改善
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：既存の `~/bin/clipimg` を `opencode` で使いやすいように改善し、`--last` `--open` などの補助オプションも含めて実装する
### 実施内容
- `~/bin/clipimg`: 保存時に `wslpath -w` で Windows パスへ変換して PowerShell に渡すよう修正し、成功時は `opencode` に貼りやすい WSL パスのみを標準出力するよう変更した
- `~/bin/clipimg`: `--help` `--last` `--open` を追加し、保存済み画像の再利用や既定アプリ起動に対応した
- `~/bin/clipimg`: 画像なし・保存失敗・パス変換失敗時に日本語エラーメッセージと非 0 終了コードを返すよう整理した
### 技術決定 Why
- `opencode` 連携では説明文より画像パスそのものが扱いやすいため、成功時の標準出力を WSL パス 1 行に固定した
- PowerShell で Linux パスを直接保存先に使うと環境依存で失敗しやすいため、保存処理だけは Windows パスへ明示変換して安定性を優先した
### 確認
- `bash -n "$HOME/bin/clipimg"` で構文確認
- PowerShell でテスト画像をクリップボードに設定後、`$HOME/bin/clipimg` で PNG 保存と WSL パス出力を確認
- `"$HOME/bin/clipimg" --last --open` で直近画像の取得とオープン動作を確認
- PowerShell でクリップボードをクリア後、`$HOME/bin/clipimg` が終了コード `4` で失敗することを確認

## [2026-03-29] 12:18 clipimg 用 Skill の追加
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：Windows クリップボード画像を `opencode` へ渡す `clipimg` 運用を、MCP ではなく Skill として登録する
### 実施内容
- `.agents/skills/clipboard-image-wsl/SKILL.md`: `clipimg` を使って Windows のクリップボード画像を WSL2 上へ保存し、`opencode` に渡すための専用 Skill を新規追加した
- `.agents/skills/clipboard-image-wsl/SKILL.md`: 基本ワークフロー、推奨コマンド、定型プロンプト、トラブルシュート、将来の MCP 化方針を記載した
### 技術決定 Why
- 今回は新しい外部ツールサーバーを増やすよりも、既存の `clipimg` を前提に運用手順を標準化する方が軽量で保守しやすいため、Skill を先行採用した
- `opencode` に渡す際の定型コマンドと失敗時対応を Skill に集約することで、毎回の説明なしに同じ流れを再利用しやすくした
### 確認
- `.agents/skills` 配下に `clipboard-image-wsl/SKILL.md` が追加されたことを確認

---

## [2026-03-30] 10:00 ESCキーでメインメニューに戻る際の実行中処理確認ダイアログ追加
### Agent
- claude-opus-4.6 : GitHub-Copilot
### Editor
- OpenCode
### 目的
- ユーザーからの要望：ESCでメインメニューに戻る際、何か実行中の場合はYes/Noダイアログを出し、Yesで戻る・Noでそのままにする共通処理を追加したい
### 実施内容
- `Cvnet10Wpfclient/Helpers/Windows/BaseWindow.cs`: `using CommunityToolkit.Mvvm.Input;` を追加
- `Cvnet10Wpfclient/Helpers/Windows/BaseWindow.cs`: `OnPreviewKeyDown` に `HasRunningCommand()` チェックを追加し、実行中コマンドがある場合は「処理を実行中です。メインメニューに戻りますか？」確認ダイアログを表示
- `Cvnet10Wpfclient/Helpers/Windows/BaseWindow.cs`: `HasRunningCommand()` メソッドを追加。DataContext の全プロパティを走査し `IAsyncRelayCommand.IsRunning == true` を検出
### 技術決定 Why
- ESCのインターセプト箇所は `BaseWindow.OnPreviewKeyDown` が唯一の共通Entry Pointであり、全画面に一括適用できる
- `IAsyncRelayCommand.IsRunning` は CommunityToolkit.Mvvm が提供する標準インターフェースで、`[RelayCommand(IncludeCancelCommand = true)]` 付き全コマンドが対象になる
- ViewModelは汚染せずView層（BaseWindow）で完結させた（MVVM原則を維持）
- NoをクリックすればClose自体が実行されないため、実行中の処理は継続されるアーキテクチャ上安全
### 確認
- `dotnet build Cvnet10Wpfclient/Cvnet10Wpfclient.csproj` → ビルド成功（エラー0）

---

## [2026-03-31] 14:00 RangeInputParamView/ViewModel の店舗・倉庫・商品CD検索対応改修
### Agent
- claude-sonnet-4.6 : GitHub-Copilot
### Editor
- OpenCode
### 目的
- ユーザーからの要望：RangeInputParamView.xaml と RangeInputParamViewModel.cs を指示書に従って改修する（店舗CD検索ボタン・倉庫CD検索ボタン・商品CD部分一致入力への変更）
### 実施内容
- Cvnet10Wpfclient/ViewModels/Sub/RangeInputParamViewModel.cs: DoSelectFromToriCommand・DoSelectToToriCommand・DoSelectFromSokoCommand・DoSelectToSokoCommand の4つの RelayCommand を追加
- Cvnet10Wpfclient/Views/Sub/RangeInputParamView.xaml: Row2（店舗CD）をラベル動的バインド・Visibility制御・SearchTextBox+名称 DockPanel パターンに変更、Row3（倉庫CD）同様に SearchTextBox+名称パターンに変更、Row4（商品CD）を from-to 2入力から単一部分一致 TextBox に変更、Width を 620→780 に拡大、MenteSearchTextBox スタイルと BooleanToVisibilityConverter をリソースに追加
### 技術決定 Why
- SearchTextBox（helpers:SearchTextBox）+ DockPanel パターンは MasterTokuiMenteView の既存実装に合わせた
- BooleanToVisibilityConverter は WPF標準の System.Windows.Controls.BooleanToVisibilityConverter を採用
- ToriLabel・IsToriVisible は SelectInputParameter 既改修済みプロパティを活用（動的ラベル変更・行表示制御）
- MenteSearchTextBox スタイルは SearchButtonBackgroundBrush（UICommon.xaml で定義済み）を ButtonBackground として参照
### 確認
- `dotnet build Cvnet10Wpfclient/Cvnet10Wpfclient.csproj` → ビルド成功（エラー0、警告4件はすべて既存）

---

## [2026-03-31] 10:00 ShopUriageInputViewModel.cs の実装
### Agent
- claude-sonnet-4.6 : GitHub-Copilot
### Editor
- OpenCode
### 目的
- ユーザーからの要望：店舗売上入力ViewModel（ShopUriageInputViewModel.cs）を指示書に従って作成する
### 実施内容
- Cvnet10Wpfclient/ViewModels/06Uriage/ShopUriageInputViewModel.cs: 空スケルトン(6行)から完全実装に変更。BasePlainLightMenteViewModel<Tran01Tenuri> を継承し、一覧表示・詳細表示・修正・削除・追加・明細行追加/削除・合計計算を実装
### 技術決定 Why
- BasePlainLightMenteViewModel を継承し LightweightSelectColumns をオーバーライドすることで、一覧取得は軽量列のみ、詳細は行選択時に遅延ロードする設計を採用
- BaseMenteViewModel が partial void OnCurrentChanged を既に宣言済みのため、派生クラスでの再定義は不可。代わりに GoToDetail コマンドで明示的にタブ遷移する方式を採用
- 明細行の PropertyChanged を監視して Su/Kingaku 変更時に UpdateTotals を呼ぶことで、リアルタイム合計計算を実現
- SyncMeisaiToCurrentEdit で区分(Kubun)を全明細に伝播させ、Insert/Update 前に呼ぶことで整合性を保つ
### 確認
- `dotnet build Cvnet10Wpfclient/Cvnet10Wpfclient.csproj /p:EnableWindowsTargeting=true /p:UseAppHost=false` → ビルド成功（エラー0、警告2件はすべて既存 MainMenuViewModel.cs）

---

## [2026-03-31] 18:00 ShopUriageInputView.xaml の完全実装
### Agent
- claude-opus-4.6 : GitHub-Copilot
### Editor
- OpenCode
### 目的
- ユーザーからの要望：指示書 `Doc/wrk/instruction-20260331-make-traninput.txt` に従い、店舗売上入力画面（ShopUriageInputView.xaml）のXAMLを完全実装する
### 実施内容
- Cvnet10Wpfclient/Views/06Uriage/ShopUriageInputView.xaml: 空スケルトン(16行)から完全実装(562行)に変更。TabControl で「一覧画面」「詳細画面」の2タブ構成を実装
  - InputBindings: F2=修正, F3=削除, F4=追加, F5=一覧取得
  - Window.Resources: MenteSearchTextBox, MenteDataGridColumnHeader, FormLabel, FormTextBox スタイル定義
  - ColorZone ヘッダー: 戻る/一覧取得/修正/削除/追加ボタン
  - 一覧タブ: Card内にメッセージ+件数表示 + DataGrid(伝票No, 計上日, 店舗CD, 店舗名, 倉庫CD, 倉庫名, 合計数量, 合計金額), FrozenColumnCount=3, ダブルクリックで詳細遷移
  - 詳細タブ: 行追加/行削除ボタン + ヘッダフォーム(伝票No, 計上日, 区分ComboBox, 店舗SearchTextBox+名称, 倉庫SearchTextBox+名称, メモ, 合計数量, 合計金額) + 明細DataGrid(No, 商品CD+検索ボタン+商品名, JAN, カラーCD+検索ボタン+カラー名, サイズCD+検索ボタン+サイズ名, 数量, 単価, 金額, 上代, 下代)
### 技術決定 Why
- MasterShohinMenteView.xaml のデザインパターン（ColorZone, Card, DataGrid, SearchTextBox, DataGridTemplateColumn検索ボタン）を踏襲し、プロジェクト全体のUI一貫性を維持
- 一覧画面と詳細画面をTabControlで分離し、SelectedTabIndex バインディングでViewModelからタブ遷移を制御可能にした
- 明細DataGridの検索ボタンは RelativeSource AncestorType=helpers:BaseWindow パターンで DataContext のコマンドにアクセス（既存パターン踏襲）
### 確認
- `dotnet build Cvnet10Wpfclient/Cvnet10Wpfclient.csproj /p:EnableWindowsTargeting=true /p:UseAppHost=false` → ビルド成功（エラー0、警告0）

---

## [2026-03-31] 17:00 ShopUriageInputView UIレイアウト変更 & Jcolsizベースのカラー/サイズ検索実装
### Agent
- claude-opus-4.6 : GitHub-Copilot
### Editor
- OpenCode
### 目的
- ユーザーからの要望：指示書 `Doc/wrk/instruction-20260331-modify-traninput.txt` に従い、ShopUriageInputViewのUI改善とカラーCD/サイズCD検索をMasterShohin.Jcolsizベースに変更する
### 実施内容
- Cvnet10Wpfclient/Views/06Uriage/ShopUriageInputView.xaml:
  - ボタンテキスト変更: '行追加'→'明細行追加', '行削除'→'明細行削除'
  - 明細フォームから伝票No行を削除（タブヘッダーで既に表示済みのため）
  - 計上日・区分・合計数量・合計金額をWrapPanelで横並びレイアウトに変更
  - 店舗・倉庫・メモは従来通り縦並びGridで維持
- Cvnet10Wpfclient/ViewModels/06Uriage/ShopUriageInputViewModel.cs:
  - `currentShohinJcolsiz`フィールド追加: 選択商品のJcolsizを保持
  - `DoSelectShohin`強化: 商品選択時にJcolsizを保存し、カラー/サイズ/JANコードをクリア
  - `DoSelectCol`修正: Jcolsiz存在時はローカルデータから重複なしカラー候補を表示
  - `DoSelectSiz`修正: Jcolsiz存在時はローカルデータからサイズ候補を表示（選択済みカラーCDでフィルタ）、サイズ選択時にJan1をJanCodeに自動適用
  - `ShowLocalSelectDialog`ヘルパー追加: MasterMeisho形式のローカルデータでSelectWinViewを表示
  - `ApplyJanCodeFromJcolsiz`ヘルパー追加: カラー/サイズ両方選択時にJcolsizからJanCodeを自動適用
- Cvnet10Wpfclient/ViewModels/Sub/SelectWinViewModel.cs:
  - `SetLocalData<T>`メソッド追加: ローカルデータをサーバークエリなしで選択ダイアログに設定
  - `isLocalData`フラグ追加: Init時のサーバークエリをスキップ制御
### 技術決定 Why
- MasterShohinColSizは独立テーブルではなく、MasterShohin.Jcolsiz列にJSON格納されているため、サーバークエリではなくローカルデータとして選択ダイアログに渡す方式を採用
- SelectWinViewModelにSetLocalDataを追加することで、既存のSelectWinView UIを再利用しつつローカルデータ選択に対応
- BaseWindow.OnContentRenderedがInitCommandを自動実行するため、isLocalDataフラグでサーバークエリスキップを制御
- カラー選択時はDistinctByで重複除去、サイズ選択時は選択済みカラーCDでフィルタリングし、商品に紐づく適切な候補のみ表示
### 確認
- `dotnet build Cvnet10Wpfclient/Cvnet10Wpfclient.csproj` → ビルド成功（エラー0、警告0）

---

## [2026-03-31] 22:30 ShopUriageInputView 計上日の表示フォーマット修正
### Agent
- claude-opus-4.6 : GitHub-Copilot
### Editor
- OpenCode
### 目的
- ユーザーからの要望：ShopUriageInputViewの一覧DataGridで計上日が「YYYY/MM/DD hh:mm:ss」と時刻まで表示されているのを「YYYY/MM/DD」のみに変更
### 実施内容
- Cvnet10Wpfclient/Views/06Uriage/ShopUriageInputView.xaml: 一覧DataGridの計上日カラムのBindingに `StringFormat=yyyy/MM/dd` を追加
### 技術決定 Why
- DateYmd8Converterは "yyyyMMdd" stringをDateTime?に変換するため、DataGridTextColumnはDateTimeのデフォルトToString()で時刻まで表示されてしまう。StringFormatで表示形式を明示的に指定することで日付のみの表示とした
### 確認
- `dotnet build Cvnet10Wpfclient/Cvnet10Wpfclient.csproj` → ビルド成功（エラー0、既存警告のみ）

---
