# VM駆動UATハーネス（UatVm）

CvWpfclientの**実View**を生成し、そのViewModelのコマンドを直接駆動してUATを無人実行する。
UIAutomationやマウス・キー操作のエミュレーションは使わない。

計画と位置づけは [Mini-UAT自動化計画](../../spec/archive/2026-08-27_Mini-UAT自動化計画_VM駆動ハーネス.md) を参照する。

## 1. 何を確認するものか

| 層 | 確認手段 |
|---|---|
| 計算・DB（金額の正しさ） | `Doc/spec/tools/summaryreconcile` |
| 帳票PDF | `qfmprint` / `Doc/test/UAT01/ReportRunner` |
| **画面→サーバー→画面** | **本ハーネス** |

数値の再検算はしない。画面の入力値が`BillingParameter`等へ正しく変換されてサーバーへ渡り、
ストリームの進捗・完了・警告・エラーが画面（ViewModel）へ正しく戻ることを確認する。
Viewを実インスタンスとして生成するため、XAMLのバインディング不整合やコンバータ例外も同時に検出できる。

## 2. 実行

CvServerの起動と終了までハーネスに任せる場合（推奨）。

```bash
dotnet build Doc/test/UatVm/UatVm.csproj
```

```bash
./Doc/test/UatVm/bin/Debug/net10.0-windows10.0.19041/UatVm.exe billing --manage-server --month 2026/07 --code 000002
```

事前に `dotnet build CvServer/CvServer.csproj` が済んでいること。
終了コードは 0 が全PASS、1 が FAIL、2 が引数誤り。

既に動作中のCvServerへ接続する場合は `--manage-server` を外して `--url` を指定する。

```bash
./Doc/test/UatVm/bin/Debug/net10.0-windows10.0.19041/UatVm.exe billing --url http://127.0.0.1:5002 --no-execute
```

### オプション

| オプション | 意味 |
|---|---|
| `--manage-server` | CvServerの起動と、終了時のCtrl+C相当での正規終了をハーネスが行う |
| `--url <url>` | 接続先CvServer。既定は`--manage-server`時`http://127.0.0.1:5002`、それ以外はappsettings.jsonの値 |
| `--month <yyyy/MM>` | 請求月 |
| `--code <code>` | 対象取引先コード |
| `--no-execute` | 更新を伴う実行を省き、入力検証だけ行う（DBへ書かない） |
| `--hide-views` | Viewを表示しない |

## 3. 証跡

`Doc/test/UatVm/out/<scenario>-<日時>.jsonl` に1行1事象で出る。`boot.log`は起動段階の記録。

| kind | 内容 |
|---|---|
| `server` | CvServerの起動・待ち受け・終了 |
| `resources` | App.xamlから構築したリソースの内訳 |
| `host` | gRPCホストの起動と接続先 |
| `view` | 生成したViewとViewModel |
| `input` | ViewModelへ与えた入力値 |
| `command` | コマンドの開始・終了と所要ms |
| `dialog` | 出たダイアログの種別・本文・返した応答 |
| `state` | ViewModelの状態スナップショット |
| `wait` / `check` / `note` / `fail` / `result` | 待ち合わせ、判定、備考、失敗、総合結果 |

`dialog` が要になる。E7警告の本文、完了メッセージと処理件数、エラー本文がそのまま残るため、
「画面に何が出たか」を人の目視に頼らず検証できる。

## 4. シナリオの追加

1. `Scenarios/` に `public static Task RunAsync(VmSession session)` を持つクラスを作る。
2. `Program.cs` の `scenarios` へ1行追加する。

ハーネス本体（`VmHost` / `VmSession` / `ViewDriver`）は変更しない。

```csharp
var d = session.OpenView<TargetView, TargetViewModel>();
await d.WaitAsync("初期化", vm => vm.Items.Count > 0);      // BaseWindowがInitCommandを自動実行する
d.Input("条件", vm => vm.Month = "2026/07");
await d.RunAsync("実行", vm => vm.ExecuteCommand);
session.CheckEqual("進捗", 100, d.Vm.ProgressValue);
```

確認ダイアログを進めたい場合は `session.SetDialogResponder(...)` で本文を検証しつつYesを返す。
既定は安全側（Yes/NoにはNo）である。

## 5. 仕組みと注意点

### 5.1 MessageExのテスト専用ルート

`CvWpfclient/Helpers/MessageExTestRoute.cs` を有効化すると、`MessageEx`の7メソッドは
実ダイアログ（`MessageBoxView`）を生成せず、応答を返して内容を記録する。
製品側の変更は`MessageEx`の各メソッド先頭の分岐だけで、呼び出し側142ファイルは無変更。
既定（無効）では従来動作と完全に同一である。

### 5.2 App.xamlのリソースを自前で構築する理由

App.xamlは`/Resources/UIColors.xaml`のように**アセンブリ名なし**でリソースを参照する。
この解決先は`Application.ResourceAssembly`で決まるが、これはWPF側の初期化時点で
エントリアセンブリ（＝ハーネス）に確定し、**後から変更できない**。
`ModuleInitializer`で最初に代入しても「設定後に変更することはできません」となる。

そのため`App.InitializeComponent()`は使わず、`ClientResources`がApp.xamlを実行時に解析し、
`pack://application:,,,/CreativeVision10;component/...`へ修飾して読み込む。
定義はハードコードせず常にApp.xamlから読むため、App.xamlに辞書やコンバータを追加しても追従する。
解釈できない定義があれば`resources:未処理の定義`としてFAILになる。

### 5.3 素のApplicationを使う理由

`CvWpfclient.App`を生成すると、Dispatcherを回した時点で`OnStartup`が走り、
StartupUriでMainMenuViewが開き、保存テーマの適用と起動時更新確認（ダイアログを伴う）まで
実行される。UATでは不要かつ危険なので、素の`Application`を使う。
gRPCホストと`AppGlobal.Init`は静的な`App.RestartHostAsync`で通せるため、これで足りる。

### 5.4 カレントディレクトリ

CvWpfclientは設定と相対パスをカレントディレクトリ基準で解決するため、
ハーネスは`CvWpfclient`フォルダをカレントにしてから起動する（`VmHost`が自動で行う）。

### 5.5 CvServerの終了

強制終了するとKestrelの停止処理とSQLiteのWAL後始末が飛び、`server-user163.db-wal`が残る。
`--manage-server`では`CREATE_NEW_PROCESS_GROUP`で起動し、そのグループへ
`CTRL_BREAK_EVENT`を送って人のCtrl+Cと同じ経路で終了させる。
`CTRL_C_EVENT`はプロセスグループを指定して送れないため`CTRL_BREAK_EVENT`を使う。
ASP.NET CoreのConsoleLifetimeはどちらも同じgraceful shutdownとして扱う。

`Stop-CvServer.ps1` は、ハーネス以外の方法で起動したCvServerを止めるための補助である。
ただし別コンソールで起動されたプロセスには届かないことがあるため、
UATでは`--manage-server`を使うほうが確実である。

### 5.6 対象DB

`CvServer/server-user163.db`（約10GB、実運用相当の移行データ）をそのまま使う。コピーは作らない。
更新を伴うシナリオは、識別可能なコード帯に限定し、削除・再投入が単独で完結するように作る。

## 6. 既知の制約

- `MasterTokui`の締日は現状`99`（末日）のみで、20日締めの境界（C-01）は検証できない。
  網羅データの投入が必要（計画書 3.3）。
- 並列実行はできない（単一の開発DBを共有するため）。
