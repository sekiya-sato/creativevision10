# Mini-UAT 自動化計画（ViewModel駆動ハーネス方式）

作成日: 2026-08-27
対象: `Doc/spec/2026-08-18_CV10機能完成度チェックリスト.md` の P0「WPF Mini-UAT」および D-09
基準HEAD: `56c0741`

## 0. 方針

チェックリストのRelease Gateは5点あり、うち3点（2.2の1・2・5）が「実運用相当データでの数値突合」と「WPF実操作での証跡」で止まっている。従来はこれを人手のMini-UATで埋める前提だったため、D-09（実施者・承認者・証跡）が未決である限り前進しなかった。

本計画では担当を次のように置き換える。

| 役割 | 担当 |
|---|---|
| UATデータ作成、実行、DB検算、帳票生成、証跡作成、課題修正 | **AI** |
| 業務仕様の選択（D-01/D-04/D-05/D-07/在庫原価期首残）、実DB書き込み許可、最終合格承認 | **ユーザー** |

WPFの画面操作は、UIAutomationやマウス自動操作ではなく、**サーバーAPIが認証なしで実行できる性質を利用し、実Viewを生成したうえでViewModelのコマンド／フックを直接駆動する**方式で行う。これにより人手の画面操作を介さずに「画面からサーバー、DB、帳票まで完遂する」ことを証跡付きで確認する。

## 1. 実コードで確認済みの前提

| 事実 | 根拠 |
|---|---|
| MVVMが徹底されている（ViewModel 約231 / View 約222、`CommunityToolkit.Mvvm`） | `CvWpfclient/ViewModels/`, `CvWpfclient/Views/` |
| **全ViewModelが引数なしコンストラクタで、`DataContext`はXAMLで直接生成**。テストから`new XxxViewModel()`が可能 | 例: `CvWpfclient/Views/05Shiire/ShiireInputView.xaml:13` |
| **AIによる実画面確認の手順が既にスキル化されている**。`MainMenuViewModel.Init()`へ環境変数フック（`CV10_AUTOMATION_OPEN_MENU`）を一時追加し`SelectedMenu`→`DoMenuCommand.Execute(null)`で画面を開く方式。キー／マウス／`SendKeys`エミュレーションは最後の手段と明記 | `.agents/skills/verify-wpf-screen-runtime/SKILL.md` |
| **UAT-01〜09のメニュー名→View型名の対応表が既に存在** | `.omo/2026-08-20_uat-menu-view-list.md` |
| 全画面キャプチャ手順あり（PowerShell `CopyFromScreen`→`.tmp_ui_check/`） | 同SKILL.md |
| 請求計算・支払計算は共通VMに集約され、コマンドと状態が外部から駆動・観測可能 | [BaseBillingCalculationViewModel.cs](CvWpfclient/ViewModels/31Monthly/BaseBillingCalculationViewModel.cs) |
| 実行は`[RelayCommand] ExecuteAsync`（`IncludeCancelCommand`）。入力は`BillingMonth`/`SelectedShime`/`TorihikiCodeFrom`/`TorihikiCodeTo`/`IsReissue`、結果は`StatusMessage`/`WarningMessage`/`ProgressValue`/`IsProcessing` | 同 51-142行 |
| E7（親子締日不一致）は`GetPreExecuteWarningAsync`が`WarningMessage`へ格納し、非ブロック警告としてダイアログ表示 | 同 91-94, 147-164行 |
| サーバー呼び出しは`AppGlobal.GetGrpcService<ICoreService>()`＋`QueryMsgStreamAsync`。`LoginJwt`が空でも`Authorization: Bearer `を送るため匿名実行可能 | [AppGlobal.cs:133-155](CvWpfclient/AppGlobal.cs:133) |
| 開発DBは実運用相当規模（`CvServer/server-user163.db` 約10.4GB、`refer/back/` に約9.6GBのバックアップ2世代） | ファイル実測 |
| ヘッドレス実行の前例あり（DB投入＋業務計算＋検算SQL＋帳票PDF生成） | [Doc/test/UAT01/](Doc/test/UAT01/README.md) |
| **計算層の実DB突合ハーネスが既に存在し、2026-08-24時点で全PASS**。`seed`/`show`/`idempotent`/`closingcheck`/`paysakicheck`/`all`をCLI1コマンドで実行でき、手計算の期待値表も保持 | [Doc/spec/tools/summaryreconcile/README.md](Doc/spec/tools/summaryreconcile/README.md) |
| 帳票PDFはDB・サーバー不要でローカル描画できる（フォームqfm＋cp932のdata.txt→outfile.pdf） | `.agents/skills/author-printstream-qfm/tools/qfmprint/Program.cs` |
| 計算ロジックは`SummaryDb`に集約され、請求`CalcSummaryUriSei`／支払`CalcSummaryKaiShi`／売掛`CalcSummaryUriKake`／買掛`CalcSummaryKaiKake`として直接呼べる | `CvDomainLogic/SummaryDb.cs` |
| サーバーは`CvFlag`でディスパッチ（`Msg056_SummaryUriSei`／`Msg057_SummaryKaiShi`ほか） | `CvServer/Services/QueryMsgStreamService.cs:131` |
| 証跡様式は既に確立（9章構成：実施概要／判定／テストデータ／実施内容と結果／検算／帳票確認／検出課題／バックアップ・証跡／再テスト資材） | [UAT01結果レポート](Doc/test/UAT01_20260821_テスト結果レポート.md) |

### 1.1 特定した障害と対処

| 障害 | 内容 | 対処 |
|---|---|---|
| B-1 モーダルダイアログ | `MessageEx.ShowQuestionDialog`が実行確認で応答待ちになり自動実行が停止する。Warning/Error/Informationも同様 | `MessageEx`へ**インターセプタ**を追加（下記2.1） |
| B-2 `Application.Current`依存 | `ClientLib.GetActiveView`は`Application.Current.Windows`を走査、`Cursor2Wait`は`Mouse.OverrideCursor`を触るため、WPF Applicationが無いと動かない | ハーネスをSTAスレッド上の実`Application`として起動し、実Viewを生成する |
| B-3 グリッド操作 | 行選択・確定・取消など、Viewのマウス操作前提の処理は外部から起動できない | 対象VMへテスト駆動用フックを追加（下記2.3） |
| B-4 既存手順書の前提破れ | `UAT01_再テスト手順.md`が参照する`C:\gitroot\UT\sqlite3.exe`は**存在せず**、記載のリポジトリパス`C:\gitroot\documents\new2022\cv10`も現在の`C:\gitroot\new2022\cv10`と異なる | 検算は読み取り専用MCP（cv-sqlite）へ移行し、手順書を修正 |
| B-5 実行中プロセス | `CreativeVision10.exe`が稼働中。DB書き込み前の停止が必須（10GB級DBの破損リスク） | ハーネスにプロセス停止・バックアップ・復元を組み込む |

## 2. Phase 0: VM駆動UATハーネスの構築

### 2.1 `MessageEx` のTest専用ルート（恒久設備）

**方針（2026-08-27 ユーザー指示）**: テスト時は実際のモーダル画面を出さず、プログラムから応答を制御できるTest専用ルートを`MessageEx`の起動箇所へ用意する。今後も継続利用する設備とし、ソースの変更量は最小に抑える。

実装は次の2点のみとする。

1. `MessageEx`へ静的なインターセプタを1つ追加する（種別・本文・付加文を受け取り`MessageBoxResult`を返すデリゲート）。
2. `ShowInformationDialog`／`ShowQuestionDialog`／`ShowWarningDialog`／`ShowErrorDialog`の各先頭に1行の分岐を入れる。

- 非`null`の間は`MessageBoxView`を生成せず、記録して応答を返す。
- `null`（既定）のときは現行動作と完全に同一で、本番挙動への影響はない。

対象は[MessageBoxView.xaml.cs:321](CvWpfclient/Helpers/MessageBoxView.xaml.cs:321)の`MessageEx`のみで、呼び出し側（VM 238本）は一切変更しない。

これは単なる回避策ではなく**証跡源**である。E7警告文、完了メッセージと処理件数、エラー本文がそのまま構造化ログとして残るため、「画面に何が出たか」を人の目視に頼らず記録できる。恒久設備とするため、応答を返すだけでなく「どのダイアログが何回出たか」を検証対象にできる形（期待ダイアログの宣言と実際の照合）で設計する。

### 2.2 ハーネス本体（`Doc/test/UatVm/`）

UAT01の前例に倣い`Doc/test/`配下へ置く（製品単体テストである`Tests/`とは分離する）。

1. STAスレッドで`Application`を起動し`AppGlobal.Init()`を実行する。
2. `MessageEx`のインターセプタを装着し、シナリオ定義に沿って応答（Yes/No/OK）を返す。
3. **対象Viewを実インスタンスとして生成**し、その`DataContext`からVMを取得する。
4. VMのプロパティへ入力値を設定し、`InitCommand`→`ExecuteCommand`を`ExecuteAsync`で駆動する。
5. `StatusMessage`/`WarningMessage`/`ProgressValue`の遷移とダイアログ記録をJSONLへ追記する。
6. CvServerのlocalhost限定起動・停止、DBバックアップ・復元、`CreativeVision10.exe`停止確認を制御する。

Viewを実生成するため、XAMLバインディング不整合・`DataContext`結線漏れ・コンバータ例外も同時に検出できる。これが「ViewModel単体テスト」との差であり、WPF実操作証跡として成立させる根拠である。

### 2.3 VM側フックの追加

グリッド行選択・確定・取消のように画面操作が前提の処理について、対象VMを洗い出し、テストから駆動できる公開コマンドまたはメソッドを追加する。追加は業務ロジックを複製せず、既存の内部処理へ委譲する形に限定する（実装と検証がずれないようにする）。

対象候補: 配分確定、出荷確定、棚卸確定、消込、強制調整取消、残高登録の読込・登録。

### 2.4 規約適合（AGENTS.md §3）

`AGENTS.md:25` は「不要な依存性注入、新規フレームワーク、テスト専用の実行プログラムは追加しない。必要な場合は根拠と影響を計画に明記する」と定める。本計画は次のとおり適合させる。

| 項目 | 判断 | 根拠・影響 |
|---|---|---|
| 新規フレームワーク | **追加しない** | FlaUI／WinAppDriver／Appium等は導入しない。Viewsに`AutomationId`が0件で識別子が日本語ラベル由来、かつSKILL.mdに「`TreeView`はUIAの選択や`SendKeys`だけでは実際の`SelectedMenu`／`DoMenuCommand`経路に届かないことがある」と実測知見があり、UIA経路は費用対効果が低い |
| 依存性注入 | **追加しない** | `MessageEx`を`IDialogService`へ抽象化して142ファイルへ注入する案は採らない。静的インターセプタ1点で足りるため、変更範囲を`MessageEx`のみに封じる |
| テスト専用の実行プログラム | **追加する（根拠明記）** | `Doc/test/UAT01/UAT01Runner`、`Doc/spec/tools/summaryreconcile`、`同/taxmix`と同じ「slnx非収録のExeハーネス」の既存前例に倣う。影響は`Doc/test/`配下に限定され、製品ビルド・配布物には含まれない |
| 製品コードの変更 | **`MessageEx`のみ** | 呼び出し側142ファイルは無変更。既定`null`で現行動作と同一 |

また`.agents/skills/verify-wpf-screen-runtime/SKILL.md`の環境変数フック（`CV10_AUTOMATION_OPEN_MENU`／`CV10_AUTOMATION_TARGET_STATE`）は**毎回追加して確認後に削除する一時フック**として運用されている。本計画のTest専用ルートは、この使い捨て運用を恒久設備へ置き換えるものであり、ユーザー指示「今後も使う」に対応する。既存スキルの手順は本設備の完成後に追従改訂する。

### 2.5 Phase 0の完了条件

`BillingCalculationView`を実生成して請求計算が1件完走し、`StatusMessage`・ダイアログ記録・DB更新結果がJSONL証跡として出力されること。

## 3. Phase 1: UAT-05 / UAT-06 の自動実行

### 3.1 既存資産との役割分担（重要）

`Doc/spec/tools/summaryreconcile` が**計算層の突合を既に達成している**（冪等性＝D-02/D-03、締日変更ブロック、親子締日E7、手計算期待値、2026-08-24全PASS）。したがってPhase 1で数値を再導出するのは重複であり、次のように分担する。

| 層 | 検証手段 | 状態 |
|---|---|---|
| 計算・DB | `summaryreconcile -- all` | **済**。回帰実行の足場として再利用する |
| 帳票PDF | `qfmprint` によるローカル描画＋テキスト層突合 | 一部済（2026-08-21目視）。テキスト層の機械照合へ引き上げる |
| **画面→サーバー** | **本計画のVM駆動ハーネス** | **未**。ここがチェックリストの残作業 |

つまりVM駆動ハーネスの目的は数値の再検算ではなく、**画面の入力値がサーバーへ正しく渡り、結果が画面へ正しく戻ることの証明**である。具体的には、VMの`BillingMonth`/`SelectedShime`/`TorihikiCodeFrom`/`TorihikiCodeTo`/`IsReissue`が`BillingParameter`へ正しく変換されて送信され、ストリームの進捗・完了・エラーが`StatusMessage`/`ProgressValue`へ反映され、E7が`WarningMessage`とダイアログとして発火することを確認する。

### 3.2 実行ケース

**自社締日の扱い（2026-08-27 ユーザー指示）**: `MasterSysman.ShimeBi`は**99のまま変更しない**（影響範囲が広すぎるため）。締日境界の検証は**得意先の`Shime1`のみ**で行う。したがってC-01は請求期間の切れ目の検証であり、D-16の在庫`DenDay`／売掛`KakeDay`側は本Phaseの対象外とする。

| ケース | 層 | 確認内容 |
|---|---|---|
| C-01 締日20の請求期間境界 | 画面 | **完了（PASS 25）**。専用得意先`UATVM-T20`（`Shime1=20`）を追加し、境界日ちょうどに金額の違う売上6件を投入。請求月202607/202608/202609を画面から実行し、期間（20260621〜20260720 / 20260721〜20260820 / 20260821〜20260920）、売上（50,000 / 90,000 / 60,000）、税、売上額、繰越残高（-55,000 → -154,000 → -220,000）が全件一致。期間外の20260620分（10,000）が混入しないことも金額で確認 |
| C-02 末締め（99） | 画面 | **完了**。`billing`シナリオで締日99・得意先000002を実行し、「末日」として表示・送信され完走することを確認 |
| C-03 E7発火 | 画面 | **完了（PASS 7）**。UAT専用の親子2組（不一致: `UATVM-C20`(締日20)→`UATVM-P99`(締日99) / 一致: `UATVM-C20M`→`UATVM-P20`）を投入。不一致側で`WarningMessage`と警告ダイアログが1回出て、**進捗100で完走**（非ブロック）、エラー扱いにならないことを確認。一致側では警告が出ないことも確認。実際の文言「請求先（親）と得意先の締日が異なるデータがあります: UATVM-C20→UATVM-P99／マスタ変更および請求再計算が必要です。」を証跡に記録 |
| C-04 明細別消費税 | 画面 | **完了（PASS 12）。ただし重大な副作用あり（下記6.1参照）**。「伝票税額再更新」画面（システム管理）から実行し、UAT専用の未処理伝票1件（標準税率10%＋軽減税率8%混在）でヘッダTax=1,400・Total=16,400、明細のId_Tax/TaxRate/Taxが正しく計算されることを確認した。**同時に、実行前チェックの不備により実運用相当データ48,691件を書き換えた（下記参照）** |
| C-05 冪等性 | 画面 | **完了（PASS 18、C-06と同シナリオ）**。通常再実行で番号`2813-20260720-01`・連番1・金額すべて不変。再発行後の通常再実行でも連番が巻き戻らないことを確認 |
| C-06 明示的再発行 | 画面 | **完了**。`IsReissue=true`で連番が1→2、請求書番号の枝番も`-02`へ追随し、金額は不変 |
| C-07 Rebuild締日変更ブロック | 画面 | **完了（PASS 8）**。対象は「在庫・掛再更新」画面（`StockKakeUpdateViewModel`）。専用得意先の締日を20→15へ変更後に「売掛のみ」を実行すると、`SummaryRebuildClosingCheck`が不一致を検出して**送信されず**（進捗0のまま）、警告ダイアログ「締日変更を検出したため、再更新を開始しません。売掛: UATVM-T20 / 保存締日 20260720 / 現在締日 15日」が1回出て、DBの請求残（`SeikyuNo`/`Renban`/`TotalSales`/`Balance`）が変わらないことを確認。締日を20へ復元する後始末も検証に含めた |
| C-08 入力検証 | 画面 | **完了（PASS 2）**。月形式不正・コード範囲逆転で、送信せず警告ダイアログのみ出ることを確認 |
| C-09 生地・付属仕入の合算 | 計算＋画面 | **完了（PASS 7）**。支払計算画面から、UAT専用仕入先1件の`Tran02Material`（仕入30,000／返品4,000／値引1,000／その他2,000）を実行。区分99（その他）が仕入へ畳み込まれず`Shiire`列は仕入分のみ、`Tax`列へその他のTotal全額(2,000)が加算され税4,700円（仕入税3,000-返品税400+値引税100+その他2,000）、`TotalShiire`=29,700円が一致することを確認（UAT-06残作業） |
| C-10 キャンセル | 画面 | **完了（PASS 4）。重大な仕様上のギャップを確認（下記6.3）**。「在庫・掛再更新」画面（在庫のみ）を実行しキャンセルすると、画面は`IsProcessing`解除・「キャンセルしました」表示という**正しいUI状態**に戻る。しかし**サーバー側の集計処理は実際には中断されず最後まで完走し、DBは常にコミットされる**（クラッシュしない・DBが破損しないという意味では安全だが、「キャンセルした処理が本当に止まる」という利用者の期待とは異なる）。サーバー側へ一時的なsleepを注入し、処理中に確実にキャンセルを送っても結果が変わらないことで実証した（検証後、注入は完全に削除・復元済み） |

### 3.3 投入した検証用マスタ

いずれもUAT専用に追加した得意先で、既存の実マスタの締日・`Id_Paysaki`には触らない。

| コード | 締日 | 用途 |
|---|---:|---|
| `UATVM-T20` | 20 | C-01境界／C-05・C-06の番号検証。境界日の売上6件を保持 |
| `UATVM-C20` | 20 | C-03 不一致側の子 |
| `UATVM-P99` | 99 | C-03 不一致側の親（請求先） |
| `UATVM-C20M` | 20 | C-03 一致側の子 |
| `UATVM-P20` | 20 | C-03 一致側の親（請求先） |

期待値は表計算ではなくコード内の明示的な定義として保持し、再実行で差分が出る形にする。

### 3.4 支払側データ不足への対応

`summaryreconcile`のREADMEおよびチェックリストのとおり、**支払（仕入）側は実移行データがほぼ無く、仕入先005〜007は境界補完用の合成データ**である。請求側は実移行データがあるため非対称である。

**方針（2026-08-27 ユーザー指示）**: テスト用データが不足する場合は、**妥当性があり、テストケースを網羅するデータをAIが用意する**。対象DBは`CvServer/server-user163.db`をそのまま使用し、コピーは作らない。

- 生地・付属仕入（`Tran02Material`）を含む補完データを生成投入する。投入はUAT専用コード帯で識別し、既存の実移行データと混ざらないようにする。
- 「妥当性がある」の基準は次のとおりとする。実在するマスタ（取引先・商品・倉庫・税区分）を参照し、業務上ありえる金額・数量・日付の組合せであり、締日・税率・支払条件がマスタ設定と矛盾しないこと。境界値（締日前日・当日・翌日、税率混在、支払超過、全額相殺、負数）を意図的に含めること。
- 支払側の判定は「実運用データでの合格」ではなく「網羅データによる境界確認＋実運用データ不足の明示」として提示し、実データ入手後の再実行を残課題とする（この非対称性の是非はA-8）。
- **コピーを作らないため、投入は必ず識別可能なコード帯に限定し、削除・再投入が単独で完結すること**を投入コードの設計要件とする（`summaryreconcile`の`seed`が既に同方式）。

## 4. Phase 2以降

| Phase | 内容 | 依存 |
|---|---|---|
| 2 | UAT-02（受注・配分・出荷）、UAT-03（移動）、UAT-04（棚卸）の通し自動実行 | 2.3のVMフック |
| 3 | UAT-07（月次・原価境界）。原価4項目は仕様未決のため、実装済み範囲の境界突合まで | D-01/D-06 |
| 4 | UAT-08（移行）。実移行DBコピーで再変換→期首残高登録→Rebuild→突合。Rate/Tax/Total/IsPay、旧処理区分18の検証 | 実DBコピー、D-08の在庫原価分 |
| 5 | 結果レポート（UAT01と同じ9章構成）を作成し、ユーザーへ最終OKを依頼 | 上記 |

## 5. ユーザーに委ねる判断

AIが進行できない、または業務責任者の選択が必要な項目のみを残す。いずれもAIが推奨案・影響・代替を添えて提示し、ユーザーは選択と承認のみを行う。

| ID | 判断 | AIの提示物 |
|---|---|---|
| ~~A-1~~ | ~~実DB（10GB級）への書き込み許可~~ | **2026-08-27 決定**：`CvServer/server-user163.db`をそのままテストに使用し、コピーは作らない（ユーザー指示）。フォールバックは`refer/back/`の既存2世代とする |
| A-2 | D-09の承認者としての最終サインオフ | 全ケースの証跡と判定表 |
| A-3 | D-01 原価4項目（最終仕入原価／総平均原価／諸掛／消化仕入）の10.0必須・10.1分類 | 各項目の帳票・在庫評価・会計への影響整理 |
| A-4 | D-04 親子請求の出力方式、D-05 適格請求書の税率別内訳出力方式 | 出力サンプルPDFを複数案で生成し比較提示 |
| A-5 | D-07 月次処理の順序とロック。集計系Rebuildのキャンセルが実際にはサーバー処理を中断しない件（6.3）は**2026-08-28 決定：現状を許容する**（クラッシュ・DB破損なし。CancellationToken対応は将来10.1で必要になれば検討） | 現行実装から導いた順序案と部分失敗時の挙動 |
| A-6 | 在庫／原価の期首残の投入元（D-08残件） | 既存`BalanceRegistrationView`の拡張案と外部CSV案の比較 |
| A-7 | D-06 総平均原価の`TQ`の解釈 | 旧帳票式からの読み取り候補と0除算・負在庫時の扱い |
| ~~A-8~~ | ~~支払側の実移行データ不足を、合成データによる境界網羅で10.0合格と認めるか~~ | **2026-08-28 決定：合格として認める**。境界パターン（支払超過・全額相殺・複数明細）は網羅済み。実データ入手後の再実施は残課題として記録 |

## 6. 実行順

1. `MessageEx`のTest専用ルートを追加し、既存動作の非回帰を確認する（変更は`MessageEx`のみ）。
2. `Doc/test/UatVm/`を作成し、`BillingCalculationView`で請求計算を1件完走させる（Phase 0完了条件）。
3. 現状把握のため`summaryreconcile -- all`を実行し、2026-08-24のPASSが現HEADで再現することを確認する（回帰の基準線）。
4. A-1の許可を得たうえで、DBバックアップを取得しPhase 1のC-01〜C-10を実行する。
5. 検出課題を修正し、再実行して収束させる。
6. 結果レポートを作成し、A-2〜A-8の判断材料と併せて提示する。

なお、`UAT01_再テスト手順.md`はB-4のとおり前提が破れているため、3の実行と併せて修正する。

## 6.1 インシデント: C-04実行時の実データ大規模更新（2026-08-28）

C-04（明細別消費税）の検証で「伝票税額再更新」（`TranTaxRebuildDb.RebuildAll`、`Msg059_TranTaxRebuild`）を
実行した結果、想定外に実運用相当データが大規模に更新された。

### 事象

実行前チェックとして`Tran00Uriage`（対象0件）のみ確認し、`Tran01Tenuri`/`Tran03Shiire`/
`Tran12Jyuchu`/`Tran13Hachu`を未確認のまま実行した。結果:

```
Tran00Uriage   走査    50,323件 / 更新     1件 / ヘッダTax変化     1件 差額         1,400円（UAT投入分）
Tran01Tenuri   走査 3,432,479件 / 更新 33,395件 / ヘッダTax変化     0件 差額             0円
Tran03Shiire   走査     8,608件 / 更新  8,602件 / ヘッダTax変化 8,598件 差額 -8,631,689,207円
Tran12Jyuchu   走査         1件 / 更新     0件 / ヘッダTax変化     0件 差額             0円
Tran13Hachu    走査     6,693件 / 更新  6,693件 / ヘッダTax変化 6,690件 差額 -5,810,119,012円

更新合計 48,691件　ヘッダTax変化 15,289件　差額合計 -14,441,806,819円
```

### 原因

`TranTaxRebuildDb`は「明細Tax合計が0の伝票（＝明細別消費税導入前の伝票）」を対象とする
**移行救済用の一時処理**（2026-08-25設計）。実DBには`Tran03Shiire`（仕入）・`Tran13Hachu`（発注）に
この条件へ該当する大量の未処理データが残っていたが、C-04シナリオ設計時に確認したのは
`Tran00Uriage`だけだった。

### 対応（2026-08-28 ユーザー判断）

**変更を受け入れる。** ロールバックしない。理由: `TranTaxRebuildDb`は設計文書で承認済みの
正規機能であり、動作は仕様通り（バグではない）。明細別消費税未対応の既存データへ正しく
税区分・税率・税額を投入した結果であり、是正として扱う。

### 教訓（今後の同種作業への適用）

- **DB全体へ書き込む処理を画面から実行する前は、対象となる全テーブルの該当件数を確認する。**
  一部テーブルだけの確認で「対象0件だから安全」と判断してはならない。
- 移行救済用など「一時処理」と明記された機能は、実DBに対して初めて実行される可能性を疑う
  （既に実行済みとは限らない）。
- 大きな差額・件数が出た処理は、実行後直ちにユーザーへ報告し、受け入れるかロールバックするかの
  判断を仰ぐ。今回はこの手順を踏んだ。

## 6.2 発見: UAT系シードデータの明細(Jmeisai)欠落と在庫Rebuild失敗（2026-08-28）

C-10（キャンセル検証）の実行中、対象範囲に202607を含めると在庫Rebuildが
`SQLite Error 19: NOT NULL constraint failed: SummaryStock.InQty`で失敗する事象に遭遇した。

### 原因

在庫Rebuild（`SummaryDb.CalcSummaryStockTrn`）は`json_extract(Jmeisai明細, '$.Su')`を
`SUM`する。明細（`Jmeisai`）を持たない伝票ヘッダを投入すると、`json_each`が空または`null`要素を
生み、`SUM(NULL*...)`が`NULL`になって`SummaryStock.InQty`のNOT NULL制約に違反する。

該当伝票は202607（2026年7月）に限定して5〜11件存在した。内訳:

- `Doc/test/UatVmSeed/ShimeBoundarySeeder.cs`が投入した専用得意先`UATVM-T20`宛の売上（**本ハーネスの不備**、修正済み）。
- `Doc/spec/tools/summaryreconcile/Program.cs`の`Seed()`が投入した得意先000002/000014宛の売上（**既存ツールの同種の不備、未修正**）。

実運用の他の期間（202607を除く全期間）にはこの種の不整合は存在しないことを確認済み
（`Tran00Uriage`/`Tran01Tenuri`/`Tran03Shiire`/`Tran05Ido`/`Tran10IdoOut`/`Tran11IdoIn`/`Tran61Chosei`
全テーブルをクエリで確認）。**製品コードのバグではなく、テストデータ投入側の不備**である。

### 対応

- `ShimeBoundarySeeder`を修正し、投入する売上へ明細（`Su`を持つ`Tran99Meisai`）を追加した。
- C-10の対象範囲は202607を避け、実データが厚い2020/01〜2020/07（店舗売上14万件超/月）へ変更した。
- `summaryreconcile`側の同種の不備は本計画のスコープ外だが、**将来UAT-07（月次・原価）で
  202607を含む在庫Rebuildを画面から検証する際に同じエラーで詰まる**ため、着手前に
  `Doc/spec/tools/summaryreconcile/Program.cs`の`Uri`/`Shi`ヘルパーへの明細追加を検討すること。

## 6.3 発見: 集計系Rebuildのキャンセルはサーバー処理を中断しない（2026-08-28）

C-10の検証中、画面の「キャンセルしました」表示とサーバー側の実際の処理結果が食い違う
ことを、サーバー側への一時的なsleep注入によって実証した。

### 検証方法（一時的な変更、検証後に完全復元済み）

`CvDomainLogic/SummaryDb.cs`の`CalcSummaryStockRange`内、`Tran00Uriage`/`Tran01Tenuri`/
`Tran03Shiire`の各`CalcSummaryStockTrn`呼び出し後に`Thread.Sleep(3000)`を一時挿入し、
処理時間を人為的に9秒以上へ伸ばした。その状態で開始1.5秒後（＝確実にsleep区間の中）に
`ExecuteCancelCommand`を発火させたが、結果は変わらず「対象7か月すべてコミット済み」だった。
検証後、`git diff`で差分ゼロを確認し完全に元へ戻した。

### 原因

`SummaryDb.SummaryAllAsyncStream`は1ステップ（"Summary : SummaryStock"）のみで構成され、
そのステップ本体`CalcSummaryStockRange`は`CancellationToken`を一切受け取らない**同期メソッド**
である。`CvServer/Services/QueryMsgStreamService.cs`の`ForwardProgressStreamAsync`は
`stream.WithCancellation(ct)`でキャンセルを試みるが、これは`IAsyncEnumerable`の**yield点**でしか
効かない。`CalcSummaryStockRange`はyieldを持たず一括で実行されるため、開始した時点で
キャンセル不能になる。クライアントのキャンセルは、サーバー側の処理とは無関係に
gRPCストリームの待受（クライアント側の`await foreach`）を切るだけで、UIには
「キャンセルしました」と表示されるが、裏では処理が最後まで完走しコミットされる。

### 対応

- 動作自体はクラッシュやDB破損を起こさないため安全側ではあるが、「キャンセル＝処理停止」という
  利用者の期待とは乖離する。**D-07（月次処理の順序とロック）で扱うべき仕様決定事項**として追加する。
- 対象は在庫Rebuild（`Msg050_Summary`）で確認したが、同じ構造（1ステップ＝同期メソッド）を持つ
  他の集計系ストリーム（売掛/買掛Rebuild等）も同様の可能性が高い。個別確認は未実施。
- 本ハーネスの`ViewDriver.RunAndCancelAsync`はこの種の検証を今後も再利用できる。

## 7. 進行状況

| # | 作業 | 状態 |
|---|---|---|
| 1 | `MessageEx`のTest専用ルート | **完了（2026-08-27）**。[MessageExTestRoute.cs](CvWpfclient/Helpers/MessageExTestRoute.cs)を新設し、[MessageBoxView.xaml.cs](CvWpfclient/Helpers/MessageBoxView.xaml.cs)の`MessageEx` 7メソッド（`ShowInformationDialog`／`ShowInformation`／`ShowQuestionDialog`／`ShowWarningDialog`／`ShowErrorDialog`／`Show`／`ShowDialog`）へ2行の分岐を追加。呼び出し側142ファイルは無変更。`dotnet build CvWpfclient` 0警告0エラー |
| 2 | `Doc/test/UatVm/` ハーネス骨格＋請求計算1件完走 | **完了（2026-08-27）**。[README](Doc/test/UatVm/README.md)。`BillingCalculationView`を実生成→`BaseWindow`が`InitCommand`を自動実行→実DBから締日取得→VM入力→gRPC`Msg056_SummaryUriSei`→`CalcSummaryUriSei`で3件処理→進捗100・完了メッセージ・完了ダイアログ（件数入り）が画面へ復帰。CvServerの起動とCtrl+C相当の正規終了もハーネスが実施。**PASS 9 / FAIL 0** |
| 3 | `summaryreconcile -- all` の現HEAD再現確認 | **完了（2026-08-27）**。`idempotent=PASS closingcheck=PASS paysakicheck=PASS`。請求台帳・支払台帳の数値もREADMEの手計算期待値と全件一致（000002: 売上額92,300／入金額50,440／残高-41,860 ほか）。ただし`summaryreconcile.csproj`のProjectReferenceが2階層不足で**ビルド不能だったため修正**（B-4と同種） |
| 4 | Phase 1 C-01〜C-10 | **完了**。C-01(PASS25) C-02 C-03(PASS7) C-04(PASS12、実データ更新インシデントあり／6.1参照) C-05(PASS18) C-06 C-07(PASS8) C-08(PASS2) C-09(PASS7) C-10(PASS4、限定的／6.2参照) |
| 5 | 結果レポートとA-2〜A-8の提示 | 未 |

### 7.1 実装中に判明した事項

- `MessageEx`のメソッドは当初想定の4本ではなく**7本**だった（非モーダルの`ShowInformation`／`Show`、汎用の`ShowDialog`を含む）。証跡の取りこぼしを避けるため全7本を対象とした。
- `MessageExTestRoute.Respond`は、`IsActive`が偽のあいだに呼ばれた場合も既定応答を返す設計にした。呼び出し側の分岐漏れで実ダイアログが出て自動実行が停止するより、応答して記録するほうが安全である。
- `C:\gitroot\UT\vscmd.bat`は`vswhere.exe`が見つからない旨のエラーを出すが、Developer Command Promptの初期化自体は成功しビルドは通る。ビルド不能ではないため対処不要。
- **`Application.ResourceAssembly` は変更できない**（重要）。App.xamlは`/Resources/UIColors.xaml`のようにアセンブリ名なしでリソースを参照し、その解決先はこのプロパティで決まるが、WPF側の初期化時点でエントリアセンブリ（＝ハーネス）に確定し後から代入できない（`ModuleInitializer`で最初に代入しても「設定後に変更することはできません」）。そのため`App.InitializeComponent()`は使わず、App.xamlを実行時に解析して`pack://application:,,,/CreativeVision10;component/...`へ修飾して読み込む方式にした。定義はハードコードせず常にApp.xamlから読むため、App.xamlの変更に追従し、解釈できない定義はFAILとして検出する。
- **`CvWpfclient.App` はUATでは使わない**。生成するとDispatcherを回した時点で`OnStartup`が走り、StartupUriでMainMenuViewが開き、保存テーマ適用と起動時更新確認（ダイアログを伴う）まで実行される。素の`Application`＋静的な`App.RestartHostAsync`で足りる。
- `MessageExTestRoute`の既定応答は**安全側（Yes/NoにはNo）**とした。起動時更新確認のような想定外の確認にYesを返すと副作用が生じるため、進めたい確認はシナリオが本文を検証しつつ明示する。
- **CvServerの終了は強制終了にしない**。強制終了すると`server-user163.db-wal`が残る（実測16,512バイト）。`CREATE_NEW_PROCESS_GROUP`で起動し`CTRL_BREAK_EVENT`を送る方式では正常終了しWALは0バイトになった。`CTRL_C_EVENT`はプロセスグループを指定して送れないため使えない。
- Windows PowerShell 5.1は**BOM無しUTF-8の.ps1をCP932として読む**ため、日本語コメントが引用符を壊して構文エラーになる。`.ps1`はUTF-8 BOM付きで保存する。here-stringも解釈に失敗しやすいため使わない。
- `MasterTokui`の締日は現状**99（末日）のみ**で、20日締めの境界（C-01）は現データでは検証できない。網羅データの投入が前提となる（3.3）。
- `MessageBoxView`をMVVM化する必要はない。ハーネスはテスト専用ルートで`MessageBoxView`の生成前に分岐するため、その内部構造には触れない。MVVM化しても得られるものが無く、製品コードの変更量だけが増える。
- 非回帰確認: `Tests/TestServer` 231件すべて成功（2026-08-27）。なお`dotnet test`では0件収集となり終了コード5を返すため、`Tests/TestServer/bin/Debug/net10.0/TestServer.exe`を直接実行して確認する。
- **任意SQLでUPDATEを送るgRPC APIは存在しない**。`Msg101_Op_Query`（`VmSession.QueryAsync`）はSELECT/クエリ専用で、サーバー側は`QueryListSqlParam`等の限定された型しか受け付けない。行の更新は`Msg201_Op_Execute`＋`UpdateParam`（`VmSession.UpdateAsync`を追加）を使う。サーバーは楽観排他（`Vdu`一致）で照合するため、直前に`QueryAsync`で取得した行をそのまま書き戻す必要がある。
- Seedプロジェクト（`Doc/test/UatVmSeed`）は`Doc/test/UatVm`配下に置いてはならない。ネストしたcsprojをglobが拾い、`TargetFrameworkAttribute`等が重複してビルド不能になった。並列のフォルダに分離することで解消した。
- **Tran系ヘッダの`Total`列は税抜金額**（`KingakuTotal`と同値）で、`Tax`は別列として持つ。`SummaryDb`の集計SQLは`t.Total`をそのままSUMするため、テストデータで`Total=税抜+税込`のように加算して投入すると二重計上になる（C-09で実際に踏んだ）。`UAT01Runner`・`summaryreconcile`の投入パターンも一貫してこの規則に従っている。

## 8. 更新ルール

- **実装途中で得た知見（障害、前提の破れ、既存資産の発見、設計変更）は随時本書へ反映する**（2026-08-27 ユーザー指示）。反映箇所は「1.1 特定した障害と対処」または該当Phaseの節とし、判断が変わった場合は変更理由を残す。
- 本書は方式と進行状況を保持し、ケースごとの結果は`Doc/test/`の結果レポートへ置く。
- 製品コードへ追加したフックは、目的・影響範囲・本番時の無効化条件を必ず記載する。
- チェックリスト側のP0・D-09の記述は、Phase 0完了時点で本書を参照する形へ更新する。
