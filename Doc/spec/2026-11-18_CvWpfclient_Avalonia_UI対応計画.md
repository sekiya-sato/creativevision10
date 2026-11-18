# CvWpfclient Avalonia UI 対応計画

作成・調査日: 2026-11-18
調査対象HEAD: `f2572d9cf54eaebaf39e7a04bd31d30401934003`
状態: 製品移行は計画案。2026-11-18の追加依頼により、ダミー応答の1画面試作を作成。製品移行のパッケージ選定・対象OSは未承認。
検証範囲: 現行ソースと公式資料の静的確認、Windows上の試作build・描画・一部操作確認。実サーバ・DB接続、製品同等性、他OSは未検証。

## 1. 目的と推奨方針

CvWpfclient の業務機能を Avalonia UI でも利用できる構成へ段階的に移す。既存 WPF 版を維持し、別の Avalonia クライアントで共通基盤と代表画面を先行検証した後、機能群ごとに対応する方式を推奨する。

WPF の XAML を一括変換するだけでは対応できない。画面・テーマの再構成に加え、ViewModel に混在する Window 操作、同期ダイアログ、ファイル選択、PDF表示などの依存分離が必要になる。

- 初期検証は Windows デスクトップを対象とする案とし、現行の操作性・業務結果を比較する。
- macOS／Linux は候補。対象OS・CPU・配布方式を確定してから、その組合せを受入対象に加える。Windows で動いたことを他OS対応の証明にはしない。
- ブラウザ、モバイル、CvWpfpos の移行は本計画の対象外とする案。
- サーバの業務処理、DBスキーマ、gRPCの公開契約、SQL・QFMの意味は維持する。
- 初版は計画書のみ。追加依頼による試作は `Doc/test/uat20261118` に隔離し、製品ソース・依存パッケージ・solutionを変更しない。

## 2. 現状と移行規模

### 2.1 静的棚卸し

`git ls-files CvWpfclient` を基準とし、生成物を含めず集計した。

| 対象 | 件数 | 解釈 |
|---|---:|---|
| `Views/**/*.xaml` | 216 | 子画面を含むファイル数。メニュー機能数ではない |
| `ViewModels/**/*.cs` | 233 | 基底・補助型を含むファイル数 |
| `Helpers/**/*.cs` | 62 | 画面基底、ViewModel基底、Behavior、Converter等 |
| `Resources/**/*.xaml` | 13 | 共有スタイル・テーマ等 |

`ViewModels` 内で `System.Windows`、`Microsoft.Win32`、`Microsoft.Web.WebView2`、`MaterialDesignThemes` のいずれかを含む C# ファイルは63件。これは文字列検索による一次抽出であり、間接依存や型参照を含む移植対象の総数ではない。

### 2.2 確認した構成・根拠

| 現行構成 | 移行上の意味 | 根拠 |
|---|---|---|
| `net10.0-windows10.0.19041`、`UseWPF=true` | 現行exeのまま Avalonia に差し替える方式は採らない | [CvWpfclient.csproj](../../CvWpfclient/CvWpfclient.csproj) |
| CommunityToolkit.Mvvm、code-first gRPC | ObservableObject・コマンド・通信契約を再利用する候補 | 同上、[App.xaml.cs](../../CvWpfclient/App.xaml.cs) |
| CodeShare、CvAsset、CvBase は `net10.0` | 新クライアントからの直接参照候補。実行時のOS互換性は別途確認 | 各プロジェクトの `.csproj` |
| MaterialDesignThemes／Colors 5.4.0 | WPF用テーマを直接再利用せず、対応するスタイルを作る | [Directory.Packages.props](../../Directory.Packages.props)、[App.xaml](../../CvWpfclient/App.xaml) |
| `BaseViewModel` → `ClientLib` | 基底ViewModelも現状では画面基盤から独立していない | [BaseViewModel.cs](../../CvWpfclient/Helpers/ViewModels/BaseViewModel.cs) |
| `BaseMenteViewModel<T>` が Window・確認・保存ダイアログを使用 | マスタ画面を共通化する前にUIとの境界を分ける必要がある | [BaseMenteViewModel.cs](../../CvWpfclient/Helpers/ViewModels/BaseMenteViewModel.cs) |
| `MenuData.ViewType` と WPF Window の型判定 | メニュー定義をそのまま共有できない | [MenuData.cs](../../CvWpfclient/Models/MenuData.cs)、[MainMenuViewModel.cs](../../CvWpfclient/ViewModels/MainMenuViewModel.cs) |
| `BaseWindow` が初期化・Esc・キャンセル・画面配置を担当 | 同等のライフサイクルを移植する必要がある。モニター処理には user32.dll 依存もある | [BaseWindow.cs](../../CvWpfclient/Helpers/Windows/BaseWindow.cs) |
| サーバがPDFを生成し、WebView2画面が表示 | 帳票生成と表示は分離できるが、表示部の方式選定が必要 | [PrintPdfHelper.cs](../../CvWpfclient/Helpers/ViewModels/PrintPdfHelper.cs)、[WebPdfView.xaml.cs](../../CvWpfclient/Views/Sub/WebPdfView.xaml.cs) |
| DataTable／DataView を使う動的な一覧 | 列生成・編集・ソート・CSV出力を個別確認する | [ZaikoQueryViewModel.cs](../../CvWpfclient/ViewModels/08Zaiko/ZaikoQueryViewModel.cs)、[AiQueryViewModel.cs](../../CvWpfclient/ViewModels/23AiQuery/AiQueryViewModel.cs) |
| Velopack による起動時処理・更新 | 新exe用の配布・更新経路を検証する | [App.xaml.cs](../../CvWpfclient/App.xaml.cs)、[UpdateService.cs](../../CvWpfclient/Services/UpdateService.cs) |
| UatVm は WPF と現行クライアントを参照 | シナリオ・期待値は活用できるが、実行基盤の流用には改修が必要 | [UatVm.csproj](../test/UatVm/UatVm.csproj)、[README](../test/UatVm/README.md) |

graphify の [GRAPH_REPORT](../../graphify-out/GRAPH_REPORT.md) は生成元 `f2572d9c` で調査対象HEADと一致する。依存関係の概要を参照し、表の事実は実ファイルで確認した。

## 3. 対応範囲と維持する契約

### 3.1 再利用・分離・再実装の区分

| 区分 | 対象 | 方針 |
|---|---|---|
| 再利用候補 | CodeShare の契約、CvAsset のモデル、CvBase の共通処理 | 新クライアントから参照する。TFMだけで移植可能と判定せず、推移的依存と実行経路を確認 |
| 条件付きで共有 | ViewModelの状態・入力検証・要求組立・進捗処理 | WPF型・View生成・静的UI呼出を分離できた単位だけ抽出 |
| UIごとに実装 | View、code-behind、共有コントロール、Converter、Behavior、テーマ | Avalonia用に作成。項目配置・入力意味・操作順を維持 |
| 境界を分けて実装 | ダイアログ、ファイル選択、UIスレッド、PDF、外部起動、保存先 | 必要な箇所に限定したUIサービスとして各クライアントに実装 |
| 現状維持 | CvServer、DB、印刷データ生成、QFM | 移行に便乗した契約・計算・帳票様式の変更を行わない |

`System.Windows.Input.ICommand` は名前空間だけで WPF 固有と判定しない。WPF の `CommandManager` や `RoutedCommand` 等との依存を区別し、CommunityToolkit のコマンドを維持する。

### 3.2 維持する不変条件

- 接続URLとサブパス処理、JWT、`X-ClientId`、`X-Cv-Screen`、タイムアウト、キャンセル、ストリームの完了／警告／エラー解釈を維持する。DI登録以外にViewModel自身が生成するgRPCクライアントも棚卸しする。
- メニューの機能ID、`InitParam`、`AddInfo`、起動引数、実行権限、お気に入り、操作履歴を維持する。型名変更から別の FunctionId が導出されないよう、現行IDとの対応を固定して比較する。
- メニュー権限は現行の [設計判断記録 §2.17](2026-09-28_設計判断記録.md) を踏襲する。UI移行を理由とした認可方式の変更は別計画とする。
- `Id_*` と `V*`、伝票の監査値、JSONの0／false、返品の `CalcFlag`、金額・税・数量・日付の扱いを変えない。
- 条件変更時の旧検索結果無効化、保存確認、二重実行防止、競合時の表示を維持する。
- 印刷後の発行済更新などは、現行が成功とみなす段階を調査して維持する。PDFの保存や外部起動を、そのまま現行の表示成功と扱わない。

## 4. 提案する構成

名称は案。P0／P1の結果と承認後に確定する。

```text
CodeShare / CvAsset / CvBase
          ↑
CvClientCore（net10.0、UI非依存の処理・契約を必要な単位で抽出）
          ↑                          ↑
CvWpfclient（既存WPF）        CvAvaloniaClient（新規Avalonia）
                                     ↓ gRPC
                                  CvServer
```

- `CvClientCore` は業務ロジック層の代替ではなく、クライアント共通処理の置き場とする。CvBase等の下位層から逆参照させない。
- `CvAvaloniaClient` から `CvWpfclient` のexeアセンブリを参照しない。新クライアントのためにサーバ／下位層へUI依存を追加しない。
- 共有が実証された通信設定・状態・要求組立から抽出する。全ViewModelを先に共通化する作業は行わない。
- UI境界の例は、画面起動・終了、確認／選択、ファイル／フォルダ、UIスレッド、PDF表示。抽象化は両UIで必要な最小範囲とし、汎用フレームワークを新設しない。
- メニューの意味情報とUIごとのView解決を分ける。未移植機能は移行対象表で明示し、WPF版で利用する。Avaloniaプロセス内にWPF画面を混在させる方式は前提にしない。
- 設定は既存形式を読めることを確認する。並行運用時の保存先・更新feedは分離する案とし、既存ファイルを移動・上書きしない。

## 5. 主要な技術課題と先行検証

| 課題 | 対応案 | 合格条件 |
|---|---|---|
| 同期ダイアログ | `ShowDialog<T>` の非同期呼出へ変更し、呼出元までawaitを伝播。確認結果はUI非依存の型で返す | OK／取消／Esc／親終了で元処理が正しく分岐し、二重更新しない。`.Result`／`.Wait()`で同期化しない |
| 初期化・終了 | Avalonia側の画面基底で初期化1回、終了確認、実行中キャンセル、所有者復帰を実装 | タブ内のEsc、画面閉鎖中の非同期完了、複数子画面で例外・取り違えがない |
| DataGrid | 対象版と互換のDataGrid、列テンプレート、入力Behaviorを検証 | Enter／Tab／Shift+Tab、編集中の保存、行追加削除、複数選択、仮想化、右端スクロールが操作仕様と一致 |
| 動的一覧 | DataTable／DataViewの列情報を表示列・行へ適合させる方法を試作 | 動的列、DBNull、数値型、列順、ソート、CSV出力が現行と一致。直接Bindingできると仮定しない |
| 日本語・数値・日付入力 | コントロールとBehaviorを再構成 | IME変換中Enterで確定／保存が誤発火しない。全半角、小数、負数、未入力、年月日と年月の意味を維持 |
| テーマ・Binding | 当初は標準テーマを比較基準にし、既存スタイル相当を構築。静的な型はcompiled binding候補 | 標準／最小サイズ、DPI、明暗テーマで判読・操作できる。動的列や祖先Bindingは個別検証 |
| PDF表示 | Windowsの既存方式を参考に、埋込表示候補と外部ビューアを比較 | 表示、保存、印刷、再読込、通信失敗、表示直後の閉鎖、発行済更新の時点を満たす。外部ビューア案は操作変更として承認が必要 |
| モニター・OS操作 | Avaloniaの画面情報、StorageProvider、Clipboard等を利用する案 | 複数画面／DPI差、画面外位置の復旧、日本語パス、保存取消、書込不可で正常に復帰 |
| 起動・更新・配布 | AppのHost・終了処理・更新処理を再構成 | 初回起動、更新なし／あり／失敗、更新後再起動、旧WPFへの復帰を検証 |

WPF用の MaterialDesignThemes と Microsoft.Xaml.Behaviors.Wpf は直接の移植先にしない。Avaloniaのスタイル・プロパティ体系に合わせて実装する。PDF／WebViewや追加テーマの採否は対応OS、ライセンス、費用、配布容量、保守状況を比較して決める。

## 6. 段階的な実施計画

| 段階 | 作業と成果物 | 完了条件・次への条件 |
|---|---|---|
| P0: 要件・棚卸し | 対象OS／CPU、対応メニュー、必須操作、印刷要件を確定。全View／ViewModelを共通基底・UI依存・動的一覧・ファイル・印刷で分類し、機能ID×InitParam×子画面の対応表を作る | 対象範囲・先行画面・性能基準・復帰方式を承認。Avalonia／DataGrid／テーマ等の版とライセンスを確定 |
| P1: 最小クライアント | 別プロジェクトで起動・ログイン・メニュー・1つの参照画面を試作。実サーバとのgRPC、サブパス、ヘッダ、終了処理を確認 | 同じサーバに接続でき、メニュー権限・機能ID・操作履歴が現行と一致。既存WPF buildを維持 |
| P2: 共通基盤と高難度PoC | 必要な共通処理を抽出。画面ライフサイクル、非同期確認／選択、DataGrid、日本語入力、動的一覧、PDF、ファイルを試作 | §5の主要課題に実機の合格証跡がある。対象OSで未解決なら画面量産を止め、範囲・方式を再判断 |
| P3: 代表画面の縦断移植 | 名称マスタ、商品マスタ、仕入入力、在庫照会、帳票を代表として、画面→gRPC→保存／表示まで対応 | 複製DBで同じ入力から同じ要求・保存値・結果になる。業務担当者がキーボード操作を確認 |
| P4: 機能群展開 | 共通選択画面→マスタ→照会／帳票→伝票→配分・物流→月次／一括処理の順を基本に対応。各回で対象表と検証状況を更新 | 各機能の子画面、印刷、入出力、権限、異常系が揃うまで「対応済み」にしない。WPF側変更の反映を確認 |
| P5: 配布・受入 | 対象OS別のインストール・更新・実印刷を検証し、限定利用者で試行 | 対象機能の受入完了、重大不具合なし、復帰演習成功。展開範囲を承認 |
| P6: 切替・整理 | 段階配布し、旧版への復帰条件を監視。WPF版の保守終了・削除は別途判断 | 対応範囲と運用期間を満たした後、WPF廃止を別途承認。切替だけを理由に削除しない |

P3の代表候補は `MasterMeishoMenteView`、`MasterShohinMenteView`、`ShiireInputView`、`ZaikoQueryView` と、実利用される帳票1本。名称マスタだけでは編集明細・動的列・印刷の難所を検証できないため、P2のPoCを省略しない。

工数・納期は未確定。P0で各分類の対象数を確定し、P2／P3で実測した「移植＋検証＋手戻り」の工数から機能群ごとに見積もる。216というViewファイル数だけから一律の期間を算出しない。

## 7. 検証計画

### 7.1 比較基準と試験環境

1. 固定の入力・期待値・複製DBを用意し、現行WPF版の表示、操作、通信要求、保存結果を基準として記録する。
2. Avalonia版は同じ初期DBの別コピー・専用CvServerで実行する。WPF試験後の更新済DBを使い回して比較しない。
3. UatVmのシナリオ・期待値・データ準備を活用し、WPFのDispatcher／Window／描画に依存する部分はAvalonia用に分ける。既存ハーネスがそのまま動くとは扱わない。
4. UI非依存処理のテスト、Avalonia側のBinding／画面生成確認、実ウィンドウ→gRPCのUAT、OS実機での目視・操作確認を分ける。ヘッドレス検証だけでIME・DPI・PDF・印刷を合格にしない。

### 7.2 受入項目

| 分類 | 必須確認 |
|---|---|
| 起動・認証 | 設定読込、接続失敗、ログイン成功／失敗、JWT失効、サブパス付きURL、終了 |
| メニュー | 権限による表示・起動判定、既定ポリシー、FunctionId、引数、お気に入り、操作履歴 |
| マスタ | 検索条件、選択取消、編集値の保持、追加・修正・削除、入力不備、保存失敗 |
| 伝票 | 行編集・追加削除、選択戻り、編集中確定、返品・値引、税・丸め、競合・二重実行 |
| 長時間処理 | 進捗・警告・エラー、取消、通信断、閉鎖後の応答、応答不明時に勝手に再実行しないこと |
| 照会・帳票 | 動的列、集計・並び順、CSV、PDF表示・保存・印刷、NoData、失敗時に発行済へ進まないこと |
| 表示・入力 | 標準／最小サイズ、100／150／200% DPI、複数画面、ライト／ダーク、日本語フォント、IME、キー移動 |
| 配布 | 対象OS／CPU別の初回インストール、更新、設定保存、ネットワーク・権限エラー、旧版復帰 |

在庫・残高等を更新する代表シナリオでは、正常系に加え競合・再実行・途中失敗を確認する。数量・金額・DB副作用は既存の独立検算手段と照合し、画面の表示一致だけで合格としない。

P0で代表データ件数、機器、ネットワーク条件、起動・検索・一覧スクロール・帳票表示の許容値を合意する。実測はWPF版と同条件で行い、結果を見てから基準を緩めない。

### 7.3 段階ごとの品質ゲート

- 共有コード抽出後は既存WPFの対象build／回帰確認を通す。新クライアント側の対象build・UATも実施する。
- UI移行の確認と既存のSQL方言／サーバ業務検証は区別し、サーバ未変更でも送信パラメータの同等性を確認する。
- 新しい共通構成・認可周辺・金額／在庫・同時更新に及ぶ実装では、`AGETS-HANDOFF.md` に従い独立Reviewer／Testerを置く。本書作成は文書作業として単独で行う。
- 各機能を「未着手／移植済／build済／UAT済／実機受入済」で管理し、未検証の状態を明示する。

## 8. 配布・並行運用・ロールバック

- WPF版の配布物と更新経路を維持し、新版の更新が旧版を置換しないよう製品ID・インストール先・feedを設計する。
- 同じ利用者が両版を使う場合の設定、お気に入り、ログ、キャッシュ、保存先の扱いを決める。共有する場合は形式互換・同時保存を確認する。
- DB／gRPC契約を変更しないことを復帰の前提とし、不具合時はAvalonia版の配布を止め、対応機能をWPF版で再開する。
- クライアントを戻しても業務DBの更新は戻らない。障害時はサーバの確定結果を照会し、完了不明の処理を再送しない。クライアント移行を理由にDBを巻き戻さない。
- 切替前に、旧WPF版が同じサーバと接続できること、設定が読めること、実行中処理の確認・再開手順を演習する。
- 初期化不能、保存値不一致、権限／機能IDの不一致、重大な入力・印刷障害を配布停止条件とする。復旧責任者と判断窓口はP5前に決める。

## 9. 実装前に承認する事項

| 判断事項 | 推奨案／確認内容 |
|---|---|
| 対象プラットフォーム | Windowsで先行検証。macOS／Linuxの要否、最低OS、CPU、利用機器を確定 |
| 対応範囲 | デスクトップのCvWpfclient。対象メニュー一覧と初回リリース範囲を確定 |
| 移行方式 | 別クライアント＋必要部分だけ共通化。WPF版の保守を継続 |
| UI基盤 | Avalonia・DataGrid・テーマの採用版、.NET 10との適合、ライセンス・保守を確認して固定 |
| 見た目・操作 | 既存の情報構造とキー操作を維持する範囲、テーマの差を許容する範囲を確定 |
| PDF・印刷 | 埋込表示の必須性、保存・実印刷、外部ビューア利用の可否、追加費用を確定 |
| 配布・更新 | 署名、OS別パッケージ、更新feed、設定の共有／分離、旧版保守期間を確定 |
| 受入・体制 | 性能基準、業務担当者、Reviewer／Tester、限定配布期間、停止・復帰判断を確定 |

本計画書と§11のダミー試作は実施済み。実サーバ接続、共有コード抽出、製品画面の本移行は、範囲と判断事項を承認してから着手する。

## 10. 参考資料と確認状況

### リポジトリ内

- [設計判断記録](2026-09-28_設計判断記録.md): §2.17 メニュー権限、§3.7 返品表示、§3.8 PrintStreamなしの一覧PDF。確認用の一覧PDFを正式QFM帳票の代替としない。
- [WPF共通ガイド](../../.agents/skills/wpf-project-guide/SKILL.md): 既存の画面構造・ライフサイクル・検証方針。
- [UAT使用ガイドライン](使用ガイドライン.md)、[UatVm README](../test/UatVm/README.md): DB隔離と現行の画面→サーバ検証。
- [公開手順](公開手順.md): 新クライアントの公開時に現行との差分を調査する対象。

### Avalonia公式資料（2026-11-18参照）

- [Migrating from WPF](https://docs.avaloniaui.net/docs/migration/wpf): スタイル・コントロール等の移行論点。
- [WPF to Avalonia cheat sheet](https://docs.avaloniaui.net/docs/migration/wpf/cheat-sheet): プロパティ、Binding、スレッド等の対応関係。概念が似ていても型・挙動の互換性を意味しない。
- [Dialogs](https://docs.avaloniaui.net/docs/how-to/dialogs-how-to): 非同期の結果付きダイアログ。
- [DataGrid](https://docs.avaloniaui.net/controls/data-display/structured-data/datagrid/): 追加パッケージと本体との版整合。

公式資料の最新ページを特定バージョンの保証として扱わない。P0で採用版のドキュメント・サポート条件を再確認する。WebView／PDF製品の選定、各OSの動作保証、移行工数の実測は未実施。

## 11. ダミー応答による1画面試作（2026-11-18追加）

### 11.1 目的・配置

- [試作プロジェクト](../test/uat20261118/UatAvalonia.csproj)／[起動・操作手順](../test/uat20261118/README.md) を作成した。試作の承認は製品全体の移行方式・対応OSの承認を意味しない。
- `MasterMeishoMenteView` の操作バー・一覧／編集フォームと、`MasterShohinMenteView` の商品入力を参考に、商品マスタ風の1画面として代表的なUI部品を仮構築した。
- 実サーバ要求はメモリ内のDummyProductServiceに置き換え、架空商品40件を検索・保存する。gRPC・DB・製品プロジェクトへの参照はなく、再起動でデータが初期化される。
- 単体テスト用プロジェクトではなく、画面を起動して操作・描画を確認する実行アプリ。P1の実サーバ接続やP2の業務基盤共通化を完了したものではない。

### 11.2 部品と移行観点

| 部品・動作 | 試作の内容 | 残る確認 |
|---|---|---|
| 操作バー・カード・Fキー | Border、Style、WrapPanel、KeyBindingで仮構築 | MaterialDesign相当の外観・全ショートカット |
| 一覧・編集・分割 | DataGrid、選択コピー、GridSplitter、TabControl | 動的列、複数選択、仮想化の性能 |
| 入力 | 文字、分類、数値、日付、有効フラグ、複数行メモ | IME、桁数・日付検証、製品の独自入力Behavior |
| 明細 | 編集DataGridと行追加 | Enter移動・編集中確定・行削除。明細は保存対象外 |
| 確認・選択 | asyncの結果付き子Window | 多段の所有者・終了中取消、製品ViewModelの非同期化 |
| ファイル | StorageProviderでダミー一覧CSVを保存 | 実操作、入出力形式の製品との一致 |
| 進捗・異常・取消 | 遅延ダミー応答、エラー切替、キャンセル、状態表示 | 実通信のタイムアウト・中断・再実行 |
| テーマ・小画面 | Fluent明暗、標準／最小サイズ、フォームスクロール | DPI、複数モニター、他OS |
| 印刷 | ダミー要求メッセージのみ | PDF表示・保存・実印刷・発行済更新 |

### 11.3 依存・検証結果

- `net10.0`、Avalonia／Desktop／Fluent／DataGrid 11.3.13、CommunityToolkit.Mvvm 8.4.2。試作フォルダ内の中央パッケージ定義で固定した。製品採用バージョンを決定したものではない。
- Avalonia.X11の推移依存Tmds.DBus.Protocolは、既知の警告GHSA-xrw6-gwf8-vvr9を受け、修正バックポート0.21.3を試作内で直接指定した。
- Windowsでbuild成功、警告0・エラー0。起動時40件表示、仕入先選択反映、F2確認→メモリ内保存、ダミー通信エラー後の操作復帰を実画面で確認した。
- 標準1280×900、最小960×700、明細タブのダーク表示を描画して目視した。最小サイズの編集フォームは縦スクロールし、下部操作領域を表示する。
- 追加・削除・CSV保存・処理中取消・IME・DPI変更・他OSの実操作、製品WPFとの同等性・実サーバ通信・DB検証は未実施。各機能は「仮構築」と「実操作確認済み」を区別する。

次の候補は、この試作で入力・一覧操作を確認した後、製品固有のDataGrid操作、動的列、PDF表示を追加検証すること。実gRPCへの切替は接続先・DB隔離を決めてから別途実施する。
