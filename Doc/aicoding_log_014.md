## [2026-08-22] 機能完成度チェックリストの反映漏れ整理・更新

### Agent
- kimi-k2.6 : OpenCode : Sisyphus

### Editor
- OpenCode

### 目的
- ユーザーからの要望：Doc/spec/2026-08-18_CV10機能完成度チェックリスト.md の最新コミットまでの反映漏れと不要項目を整理・更新する

### 実施内容
- Doc/spec/2026-08-18_CV10機能完成度チェックリスト.md:
  - §3.8 配分・出荷：受注配分入力（JuchuHaibunInputView、コミット1721f029）を「実装済み」に反映し、1.1予定から削除
  - UAT-02：受注配分入力実装済み（2026-08-21）を明記
  - ヘッダー：調査基準日を2026-08-22、調査対象HEADを6d0cc4c4へ更新
  - §2.1 メニュー規模：216→217、198→199、掛・分析46→47、掛管理17→18へ更新
  - §2.4 P2、§3.13、§8.3コミット3、UAT-08：残高登録方式を「外部CSV取込」から「専用画面 BalanceRegistrationView（2026-08-21実装）」へ統一
  - §4.1：TestServerテスト件数を125/125から168/168へ更新
  - §9 リスク：Id_Paysaki実データ不足（2026-08-21解消済み）の行を削除し、E7 WPF実操作未確認を「請求/支払/Rebuildの実画面操作」リスクへ統合

### 技術決定 Why
- 受注配分入力は2026-08-21に実装済みだがチェックリストが1.1予定のままだったため、現在地へ反映
- 残高登録は2026-08-21にBalanceRegistrationView専用画面へ方針転換したが、2.4/3.13/8.3/UAT-08に古い「外部CSV取込」記述が残っていたため統一
- Id_Paysakiリスクは2026-08-21にtools/summaryreconcile --paysakicheckで解消済み。残るWPF実操作未確認は既存の「実画面操作」リスクと同じMini-UAT(D-09)対象であるため統合

### 確認
- 文書のみの変更のためbuild未実施。git diff --checkにて空白エラーなしを確認済み

---

## [2026-08-21] 残高登録処理（BalanceRegistrationView）の実装

### Agent
- Anthropic Claude Opus 5 : Anthropic : Claude Code

### Editor
- Claude Code

### 目的
- 移行時の期首売掛残・請求残・買掛残・支払残を投入する専用画面を新設する。
- 2026-08-19 の D-08 決定（専用画面を作らず外部CSVマスタ取込で代替）を置き換え、外部CSVマスタ取込・取込レイアウト作成の実装を流用する。

### 判断
- 汎用CSV取込では次の4点を担保できないため専用画面を新設した。(1) `InsertBulkParam` は Insert のみで再取込が一意キー(uk1)違反になる、(2) 期首前チェックが無い、(3) 繰越の引き継ぎ方が区分で異なる、(4) 行単位 Delete では部分失敗で不整合になる。
- **繰越の引き継ぎ方が2方式ある**ことを `SummaryDb.cs` の再計算SQLで確認した。売掛/買掛は前月行の `Balance` 列、請求/支払は `DayTo < 開始日` の全行の `SUM(TotalIn - TotalSales)`（`Balance` 列は読まない）である。請求/支払で `Balance` だけを入れた期首行は繰越に効かないため、画面は4区分すべてで `Balance` と合計列の双方を埋める。
- ユーザーが触れるCSVは日本語1行ヘッダの「標準形式」とし、金額は常に正数＝未回収残とした。内部の `Balance`（負＝未回収）への変換は画面が行い、符号の切替UIは置かない。旧3行ヘッダの「詳細形式」も自動判別で受け付ける（`Balance` は内部符号として解釈）。
- 残高0の行は、既存行があれば削除、無ければ何もしない。テンプレートが全取引先を出力するため、この規則が無いと `Summary*` に0行が大量に作られる。
- 洗い替えはCSVに現れた取引先だけを対象にし、CSVに無い取引先の既存行は触らない。
- テンプレート出力は `TenType IN (1,3)`（卸先・売仕店のみ）・選択締日・コード範囲で絞る。取込時のコード解決は一切絞らない。絞ると対象外の取引先が「マスタにありません」という誤ったエラーになり、本来の警告・エラーが出せなくなる。
- CSV解析・検証・行生成は WPF/DB 非依存の純ロジックとして `CvBase/OpeningBalanceCsv.cs` へ置き、`Tests/TestServer` から直接検証した（`PaysakiClosingCheck` と同じ置き方）。クライアント専用の場所に置くとテストのためにWPF参照のテストプロジェクトが必要になるため。
- Excel経由のCSVを前提に、桁区切りカンマ・通貨記号・全角数字・会計表記 `(1200)` を受け付ける数値正規化を入れた。この緩和は残高登録の標準形式だけに適用し、`ExternalCsvImportView` の既存挙動は変えていない。
- テンプレートCSVは Excel で開いて化けないよう BOM 付き UTF-8 で出力する。取込は BOM 有無どちらも受け付ける。

### 実施内容
- `CvAsset/CsvText.cs` を新設し、RFC4180相当のCSV解析・組み立てを層0へ集約した。
- `CvBase/OpeningBalanceCsv.cs` を新設した。区分定義（対象テーブル・キー列・取引先マスタ）、標準形式CSVの解析と出力、数値・日付の正規化、内訳の自動生成と整合検査、行状態（新規/上書き/削除/対象外）の判定、取引先照会SQLの組み立て、期首行キー日付の算出を持つ。
- `CvBase/Parameters.cs` に `OpeningBalanceImportParam` / `OpeningBalanceImportResult` を追加した。
- `CodeShare/ICoreService.cs` に `CvMsgErrorCode.InvalidParameter`(-9904) を追加した。
- `CvDomainLogic/OpeningBalanceDb.cs` を新設し、許可リスト・期首ガード・洗い替え範囲の整合を再検査してから、削除と登録を Serializable の1トランザクションで実行するようにした。
- `CvServer/Services/HandlerClass.cs` の `HandleOpExecute` に分岐と `HandleOpeningBalanceImport` を追加した。
- `CvWpfclient/Helpers/CsvImportEngine.cs` を新設し、`ExternalCsvImportViewModel` から取込レイアウトの列定義解決・値変換・マスタコード解決・型マップを抽出した。`ExternalCsvImportViewModel` と `ImportTemplateCreateViewModel` を委譲に変更した（振る舞いは不変）。
- `BalanceRegistrationViewModel` / `BalanceRegistrationView.xaml` を実装した。①期首情報→②テンプレートCSV出力→③CSV読込と登録の3ブロック構成、F3=テンプレート出力・F4=選択・F5=再検証・F6=登録実行・ESC=戻る。
- `CvWpfclient/Models/MenuData.cs` の `addInfo` を「準備中」から実内容へ更新した。
- 詳細設計 `Doc/spec/2026-08-21_残高登録処理_詳細設計.md` を作成し、機能完成度チェックリストの D-08 と残高登録行を専用画面ありへ更新した。
- 併せて請求書印刷の前回残高の符号誤りを修正した。`SeikyuBalanceDetailViewModel` の `s.Balance - s.TotalSales + s.TotalIn` を `s.Balance + s.TotalSales - s.TotalIn` へ改めた。`Balance = 前回残高 + TotalIn - TotalSales` の逆算であり、旧式は当月増減を2回効かせて `前回残高 + 2×(TotalIn - TotalSales)` を出していた。期首残高を投入すると請求書の前回残高欄（`SeikyuBalanceDetail.qfm` の item5）に必ず現れるため本作業に含めた。回帰テスト `CalcSummaryUriSei_PreviousBalanceIsRecoveredByAddingSalesAndSubtractingPayments` を `Tests/TestServer/SummaryKakeDbTests.cs` へ追加した。

### 検証
- `creativevision10.slnx` のビルド: 成功（警告0、エラー0）。
- `Tests/TestServer` 全件: 168件成功・0件失敗。うち本作業の新規は44件（`OpeningBalanceCsvTests` 29件、`OpeningBalanceDbTests` 14件、前回残高の回帰テスト1件）。
- `OpeningBalanceDbTests` は一意キー(uk1)を含むインデックスを作った実スキーマで実行し、同一CSVの再取込で uk1 違反にならず件数も増えないこと、途中失敗で1件も残らないこと、`OwnerIds` に無い取引先の既存行が消えないことを確認した。
- 取引先照会SQLは4区分×2スコープの計8本を実SQLiteスキーマで実行して確認した。`MasterShiire` に `TenType` 列が無い問題をこのテストで検出し、買掛・支払では `0 AS TenType` を出力するよう修正した。
- 期首行の繰越結合を確認した。売掛は `CalcSummaryUriKake` 実行後も期首前行が上書きされないこと、請求は期首行の `TotalIn-TotalSales` が翌締期間（20260731）の `Balance` の起点になることを実DBで確認した。
- 残高の符号規約は開発DB（`server-user163.db`）の実データで確認した。`SummaryKaiKake` / `SummaryKaiShi` の `Balance=-10400`・`TotalShiire=15400`・`TotalOut=5000` は前残0のとき `-10400 = 0 + 5000 - 15400` であり、`Balance = 前残 + 入金 - 売上`（未回収は負）が成り立つ。
- 前回残高の回帰テストは `SummaryDb` が実際に作った連続2締期間に対して、正しい式が前月の当月残高と一致し旧式が一致しないことを確認する。ただし `SeikyuBalanceDetailViewModel` のSQL文字列自体は実行していない（CvWpfclient はWPF参照のためTestServerから呼べない）。式の一致はソースコメントで担保している。
- 既存画面の非回帰はビルドと既存テスト全件成功で確認した。`ExternalCsvImportView` / `ImportTemplateCreateView` の実アプリ操作での確認は未実施。
- `BalanceRegistrationView.xaml` の全バインディングパス（48件）をViewModel・行クラスのメンバと機械的に突合し、未解決が無いことを確認した。
- **実アプリ起動・画面操作・実DBでの4区分投入は未実施**（サーバ起動とログインを伴う対話操作が必要）。詳細設計8章の実行時確認（`tools/summaryreconcile` での突合）も未実施で残っている。
- `git diff --check` はクリーン。新規・変更ファイルはCRLF・UTF-8。

## [2026-08-21] 発注書QFM差し替えと出力SQL列調整

### Agent
- OpenAI GPT-5 : OpenAI : Sekiya Sato Codex

### Editor
- Codex

### 目的
- `CvWpfclient.Views._03Hatchu.HachuFormView` の出力を、旧cvnetの `HachuForm.qfm` と `d_sql.txt` の列定義に合わせる。

### 判断
- 旧 `data.txt` は57列だったため、SQLはitem1～item57を出力し、差し替えQFMに定義されているitem58・item59は空欄のままとする。
- item16「総合計」は現行発注ヘッダの税計算後合計ではなく、旧帳票に合わせて `KingakuTotal` を出力する。
- item3「支払先CD」は `Id_Paysaki` がある場合は支払先コード、ない場合は仕入先コードへフォールバックする。
- 現行マスタにFAX項目がないため、item41・item47・item51は空欄とする。

### 実施内容
- `refer/qfmsample/HachuFormView/HachuForm.qfm` を `printform/HachuForm.qfm` へ内容を変更せず上書きした。
- `HachuFormViewModel.cs` のSQLを、旧列順に合わせてヘッダ・明細・住所情報・取引区分名・バーコードを57列で出力する構成へ変更した。
- 仕入先・入庫先・自社の住所系情報は現行マスタから取得し、伝票時点の名称はV列を優先した。

### 検証
- `CvWpfclient/CvWpfclient.csproj` のビルド: 成功（警告0、エラー0）。
- `server-user163.db` を読み取り専用で使用し、同SQL構成を57列・8行で実行できることを確認。
- SQLのitemコメント57件と旧 `d_sql.txt` の57列名を比較し、不一致なし。
- QFMはcp932ラウンドトリップ、XML構文、`printstream`、CSV `data.txt`、item59件を確認。元ファイルと差し替え先のSHA256も一致した。
- 共通 `validate_qfm.py` は旧cvnet QFMの位置（y=16、height=264）が共通期待値（y=8、height=272）と異なるため終了コード1となった。指定どおりQFMをそのまま使用するため、位置属性は変更していない。
- 実アプリ起動・画面操作・実DBデータでのPDF出力確認は未実施。

---

## [2026-08-21] 受注配分入力を実装

### Agent
- Claude Opus 5 : Anthropic : Sekiya Sato Claude Code

### Editor
- Claude Code

### 目的
- L0（空クラス）だった `CvWpfclient.Views._07Haibun.JuchuHaibunInputView` の仕様を確定し、実装する。旧CV.net【配分】-【受注配分入力】(`SubDlg_60_hbn15.crs`) に相当する画面。

### 判断
- 入力単位を**受注伝票1件＝配分1件**に決定（ユーザー確定 2026-08-21）。旧は「倉庫＋商品を選び SKU行×得意先列のクロス表」だったが、`RelateNo1 = 元伝票Id` の規約と出荷処理の伝票まとめ単位（`ShippingDb.CreateShippingSlips`）に素直に合う受注伝票単位へ変更した。複数得意先へ横断的に割り振る用途は「得意先別配分入力」で扱う。
- 受注残は用途で2種に分けて定義した。一覧の「未配分残」= Σ_SKU MAX(受注数 − 出荷済 − 配分, 0)、明細の「受注残」= MAX(受注数 − 出荷済 − 確定済配分, 0)。明細側は洗い替え対象の配分を差し引かない（旧CV.netも編集対象の配分を残から除外していた）。
- 配分可能数（有効在庫）= 在庫 − 引当 + 洗い替え対象配分。引当は `SummaryRealStock.ReserveQty`（materialize済み、決定 5.2.2c）を読み、洗い替えで消える自分の分だけ足し戻す。
- 超過は有効在庫割れ・受注残超過とも**警告のみで登録可**（ユーザー確定）。受注残を超えた分は `RelateNo1 = 0` の別行にして、受注の自動完了判定へ混ざらないようにした。
- 単価（`Tanka` / `Jodai` / `Gedai`）は受注明細の値をそのまま引き継ぐ。`DerivedJodai` の再解決はしない（伝票時点の価格を優先）。
- 旧画面のCSV取込／出力、商品分類の可変条件グリッド、在庫数範囲、店舗表示のランク順は見送り（詳細設計 1.2）。

### 実施内容
- `Doc/spec/2026-08-21_受注配分入力_詳細設計.md` を新設し、入力単位の変更理由、受注残・有効在庫の算式、超過の扱い、画面構成、SQLの受け皿を定義。
- `CvWpfclient/ViewModels/07Haibun/JuchuHaibunInputViewModel.cs`: 検索（受注No／受注日／指示日／納品日／得意先／入力者／商品CD／取引区分／配分状況）と受注一覧、配分明細（商品×色サイズ）、受注残読込・同数展開・全クリア・削除・登録（洗い替え）を実装。
- `CvWpfclient/Views/07Haibun/JuchuHaibunInputView.xaml`: 旧画面と同じ2タブ構成（検索画面／修正・登録画面）を `HachuHaibunInputView` の構造に合わせて実装。
- `CvWpfclient/Models/MenuData.cs`: 受注配分入力の「準備中」を外し、実装内容の説明へ更新。
- サーバ・DBスキーマ・帳票の変更は無し。引当数は書き込み時にサーバ（`WriteEffectRunner` → `SummaryDb.CalcHaibun2Reserve`）が自動で引き直すため、クライアントからの再計算指示は入れていない。

### 検証
- `CvWpfclient/CvWpfclient.csproj` をビルド: 成功（警告0、エラー0）。
- 新規SQL（受注残の3段結合、SKU別の出荷済・確定済配分、受注一覧のEXISTS絞り込み）を `server-user163.db` へ read-only で実行し、構文と結果形を確認。受注データが0件のDBのため件数検証は未実施。
- `git diff --check`、変更・新規ファイルのCRLF（XAMLはBOM維持）を確認。
- 実行時の画面確認（起動して操作）は未実施。受注データが無いため、受注登録後にMini-UATが必要。

---
## [2026-08-20] サーバー・クライアントのログ出力を見直し

### Agent
- OpenAI GPT-5 : Sekiya Sato Codex

### Editor
- Codex

### 目的
- 通常時のログ量と機微情報の残存を抑えつつ、障害時にクライアントとサーバーを横断して追跡できるログへ改める。

### 実施内容
- `Doc/spec/2026-08-20_ログ出力見直し_詳細設計.md` を新設し、詳細ログの切替、相関ID、例外記録、検証条件を定義。
- `CvServer`: `Diagnostics:EnableDetailedRequestLogging`（既定false）を追加。要求／応答ヘッダとCoreServiceのPayload・SQL全量出力をこの設定でのみ有効化し、詳細モードでは従来の内容を出力するよう変更。
- `CvServer`: `X-CV-Correlation-ID` を受信または生成して応答へ返却し、NLogレイアウトと未処理gRPC例外へ相関IDを追加。
- `CvWpfclient`: gRPC HTTPハンドラーで相関IDを付与し、HTTP通信失敗に相関ID・メソッド・パス・例外全文を記録。NLogファイルレイアウトへ例外全文を追加。

### 検証
- `CvServer/CvServer.csproj` を Development 環境でビルド: 成功（警告0、エラー0）。
- `CvWpfclient/CvWpfclient.csproj` を Development 環境でビルド: 成功（警告0、エラー0）。
- `git diff --check`、変更ファイルのCRLF確認、全量Payload／SQLログの詳細フラグ経由確認: 成功。

---

## [2026-08-20] E7 親子締日チェックのワーニング表示を実装

### Agent
- Claude Sonnet 5 / Claude Opus 5 : Anthropic : Sekiya Sato Claude Code

### Editor
- Claude Code

### 目的
- `Doc/spec/2026-08-17_旧cvnet比較_未適用・保留課題.md` E7（親子締日チェック）を実装する。親（請求先／支払先＝`Id_Paysaki`）と子（得意先／仕入先）の締日（`Shime1`）が異なる場合に警告を出す。ブロックはしない。

### 判断
- 表示画面・タイミングは「不整合が**発生する瞬間**（マスタ編集の保存後）」と「**影響が出る瞬間**（請求／支払計算の実行前）」の両方に置く方針とした（`.omo/2026-08-20_E7_親子締日ワーニング_作業計画.md`）。保存前ブロックや帳票出力時の警告は決定事項（ブロックしない／手遅れでノイズになる）に反するため採らない。
- マスタメンテでは編集した1件を軸に**双方向**（自分が子として見た親、自分が親として見た子）で検査する。親の締日変更は子側からしか検出できないため。
- 既存の `CvBase/SummaryRebuildClosingCheck.cs`（保存済み集計の`DayTo`と現在マスタ`Shime1`の突合＝Rebuild時のブロック用）はE7とは別ロジックであり、混同しないよう新規に `PaysakiClosingCheck` を切り出した。
- `BillingCalculationViewModel` には請求計算の事前警告が実装済みだったため、これを `BaseBillingCalculationViewModel` 側の共通実装へ移し、支払計算にも同じ検査を適用した。

### 実施内容
- `CvBase/PaysakiClosingCheck.cs`: 新設。`PaysakiClosingCheckRow`（Msg101_Op_Queryの共有DTO）、範囲検査SQL（計算画面用）、対象行検査SQL（マスタメンテ用、編集Idを子または親条件に埋め込み双方向検査）、`FindMismatches`、`BuildMismatchWarning`（先頭5件＋「ほかN件」＋案内文）を実装。
- `CvWpfclient/ViewModels/31Monthly/BaseBillingCalculationViewModel.cs`: `GetPreExecuteWarningAsync`の既定実装を`PaysakiClosingCheck`ベースへ変更し、`ChecksPaysakiClosing`（既定true）・`PaysakiParentLabel`（抽象）を追加。
- `CvWpfclient/ViewModels/31Monthly/BillingCalculationViewModel.cs`: 個別実装だった親子締日チェックを削除し、`PaysakiParentLabel => "請求先"`のみ指定。
- `CvWpfclient/ViewModels/31Monthly/PaymentCalculationViewModel.cs`: `PaysakiParentLabel => "支払先"`を追加し、支払計算にも親子締日警告を適用（従来は未実装だった）。
- `CvWpfclient/Helpers/ViewModels/BaseMenteViewModel.cs`: `QuerySqlListAsync<TRow>(sql, ct)`を追加（一覧表示外の任意SQL照会、保存後の気付き警告などで使う共通ヘルパー）。
- `CvWpfclient/ViewModels/01Master/MasterTokuiMenteViewModel.cs`: `AfterInsert`/`AfterUpdate`をオーバーライドし、保存した得意先を軸に請求先との締日不一致を非同期検査、不一致時は`MessageEx.ShowWarningDialog`で警告（照会失敗は握りつぶす）。
- `CvWpfclient/ViewModels/01Master/MasterShiireMenteViewModel.cs`: 同様に仕入先側（支払先との締日不一致）を実装。
- `Tests/TestServer/PaysakiClosingCheckTests.cs`: 新設。`FindMismatches`（一致/不一致の判定）、`BuildMismatchWarning`（空文字・親子ラベル・案内文・5件超の省略表示）、SQL生成（テーブル名・編集Id・Where句の埋め込み）を検証する7件。
- `.omo/2026-08-20_E7_親子締日ワーニング_作業計画.md`: 詳細設計メモ（現状調査・画面選定の判断根拠・作業リスト）。
- `Doc/spec/2026-08-17_旧cvnet比較_未適用・保留課題.md`: E7行を完了に更新。

### 検証
- ビルド成功（警告0、エラー0）。
- `dotnet test Tests/TestServer`: 125/125 成功（新規7件含む）。
- `dotnet test Tests/TestLogin`: 7/7 成功。
- 実行時のUI確認は未実施（開発DB`server-user163.db`は`Id_Paysaki`が全件0のため、警告発火自体は実データ未確認）。

### 残課題
- `Id_Paysaki`を持つ実データが投入されていないため、警告メッセージの実表示は未確認。実運用データ投入後の確認が必要。
- 請求計算・支払計算画面、得意先・仕入先マスターメンテ画面の実行時UI確認（ボタン操作からの警告ダイアログ表示）は未実施。

---

## [2026-08-20] E11 その他売上（区分99）を請求残へ分離集計、掛集計は畳み込みへ修正

### Agent
- Claude Sonnet 5 : Anthropic : Sekiya Sato Claude Code

### Editor
- Claude Code

### 目的
- `Doc/spec/2026-08-17_旧cvnet比較_未適用・保留課題.md` E11（その他売上）を実装する。区分99（その他売上/その他仕入）の扱いを、請求一覧表・掛集計（元帳）の双方で確定させる。

### 判断
- 課題文「区分99は現在Uriageへ畳んでいる」は2026-08-17時点の草案の記述で誤り。実際は`CalcSummaryUriKake`/`CalcSummaryKaiKake`/`CalcSummaryUriSei`のいずれも区分99をUriage/Henpin/Nebiki(Shiire)のどこにも含めず、単に集計から漏れていた（`TotalSales`に入らない）。
- 請求残(`SummaryUriSei`)は請求一覧表で内訳を示す必要があるため、新規列`Sonota`へ**分離集計**（Uriageへは畳み込まない）。`TotalSales = Uriage-Henpin-Nebiki+Sonota+Tax`（旧cvnet式 売上-返品-値引+その他売上+消費税 に合わせる）。
- ユーザーからの追加指示により、掛集計（`SummaryUriKake`=売掛/`SummaryKaiKake`=買掛、元帳・締日変更検査等が参照）は課題文の原意どおり**区分99をUriage/Shiireへ畳み込む**方針へ変更。請求残とは扱いが異なるが、ネットの`TotalSales`/`Balance`はどちらの経路でも一致する。
- 支払残(`SummaryKaiShi`)側は本課題のスコープ外（請求一覧表のみが対象）のため無改修。

### 実施内容
- `CvBase/UpdateDb.cs`: マイグレーション`26_08_20_01`で`SummaryUriSei`へ`Sonota`列を追加。
- `CvBase/BaseDbKake.cs`: `SummaryUriSei`に`Sonota`プロパティを追加（Nebikiの直後、Taxの前）。
- `CvDomainLogic/SummaryDb.cs`: `CalcSummaryUriSei`の`sales`CTEに`Sonota = SUM(CASE WHEN Kubun=99 THEN Total ELSE 0 END)`を追加し`calculated`CTE・`INSERT`列・`TotalSales`/`Balance`算式に反映。`CalcSummaryUriKake`のUriage判定・`CalcSummaryKaiKake`のShiire判定にそれぞれ`OR Kubun = 99`を追加（follow-up）。
- `CvWpfclient/ViewModels/06Uriage/SeikyuListReportViewModel.cs`: 請求一覧表SQLに`u.Sonota AS sonota`を追加。
- `printform/SeikyuListReport.qfm`: 11列目「その他」を追加。既存の用紙サイズ(region width=150/page width=156)は変更せず、対象期間(28→20)・売上額(15→13)・入金額(13→12)を切り詰めて新列分の幅を確保。
- `Tests/TestServer/SummaryKakeDbTests.cs`: `CalcSummaryUriSei_SeparatesKubun99AsSonotaWithoutFoldingIntoUriage`を新設。既存の`CalcSummaryUriKake_UsesTotalForPositiveBreakdownAndNegativeBalance`/`CalcSummaryKaiKake_UsesTotalForPositiveBreakdownAndNegativeBalance`の期待値を畳み込み後の値へ更新。
- `tools/summaryreconcile/Program.cs`: Kubun=99の伝票(8000円/税800円)をSeedに追加、`Show()`にSummaryUriKake/SummaryKaiKakeの表示（畳み込み確認用）を追加、起動時に`UpdateDb.WriteVersionInfoAsync`を呼び開発DBのスキーマを追随させる一行を追加。
- `Doc/spec/2026-08-17_旧cvnet比較_未適用・保留課題.md`のE11行を完了に更新。`Doc/spec/archive/2026-08-18_請求計算・支払計算_詳細設計.md`へ本follow-upによる訂正の追記注記を追加。

### 検証
- `dotnet test Tests/TestServer`: 118/118 成功（新規1件・既存2件更新含む）。
- `tools/summaryreconcile all`を開発DB(`server-user163.db`、事前バックアップ済み)で実行：請求残側 Uriage=100000/Henpin=20000/Nebiki=5000/その他売上=8000/Tax=9300/売上額(TotalSales)=92300 が期待値と一致。掛集計側`SummaryUriKake.Uriage=108000`（区分99分8000を含む）で畳み込みを確認。`idempotent`(D-02/D-03)・`closingcheck`(E7)ともPASS。
- `qfmprint`ローカルPDF描画で11列すべて（「その他」列含む）が用紙内に収まり欠落なく出力されることを確認。

---

## [2026-08-20] 請求/支払計算を実DBで突合するツール(summaryreconcile)を新設し UAT-05/06 を通し検証

### Agent
- Claude Opus 4.8 : Anthropic : Sekiya Sato Claude Code

### Editor
- Claude Code

### 目的
- P0 Release Gate（完成度チェックリスト §4.1 段階7 / §3.11）と UAT-05（売掛・請求）/ UAT-06（買掛・支払）を、開発DB `server-user163.db` で実際に成立させる。請求台帳（発行控え）・支払台帳（発行控え）が計算の確定値を忠実に出力することを実データで突合する。

### 判断
- 開発DB調査（読み取り専用）で `SummaryUriSei`/`SummaryKaiShi`=0行＝請求/支払計算が未実行、支払側は元データ不足（`Tran03Shiire`=2行, `Tran07Shiharai`=0行）と判明（[[cv10-realdb-user163-state]]相当）。→ テストデータ投入が必要。
- 投入は生SQLの手入れでなく `ExDatabaseSqlite`＋ドメインオブジェクトの `Insert`→`SummaryDb.Calc*`（`Tests/TestServer/SummaryKakeDbTests.cs` と同じ正準方式）。
- ユーザー指示により対象は「既存取引先を使う」。得意先 000002/000014・仕入先 001/002 に、既存取引ゼロのテスト月 202607 で管理された伝票を紐づけ、実マスタの締日/条件（末日締・翌月末/当月末）のまま計算する。

### 実施内容
- `tools/summaryreconcile`（`creativevision10.slnx` 非包含の開発ツール）を新設。サブコマンド `seed`/`show`/`idempotent`/`closingcheck`/`all`。README に UAT-05/06 手順・期待値表を記載。
- CLEAN→SEED→CALC→台帳SQL突合を実装。再実行時は 202607・対象取引先分のTran/Summaryのみ掃除し累積しない。

### 検証（開発DB `server-user163.db`、`refer/back/` にバックアップ済み）
- 数値突合: 請求台帳（000002=売上額83,500/入金額50,440/残高-33,060/入金予定日20260831/番号1-20260731-01, 000014=33,000/33,000/0/20260831）・支払台帳（001=仕入額77,000/支払額70,000/残高-7,000/支払予定日20260731, 002=16,500/0/-16,500/20260731）が**手計算の期待値と完全一致**。返品税の符号反転（8,500=10,000-2,000+500 / 7,000=8,000-1,000）も一致。
- `idempotent`=PASS: 2回目計算でSummaryスナップショット完全一致（`SeikyuNo`/`Renban`維持＝D-03、通常計算値=Rebuild値＝D-02）。
- `closingcheck`=PASS: 締日を99→20に変更すると売掛2・買掛2の不一致を検出し送信ブロック（`SummaryRebuildClosingCheck`）、警告文生成後に締日を99へ復元（E7 締日変更警告）。
- 帳票PDFの目視確認・実スケール（既存2019-2022売上での請求計算）は次段（B）。

---

## [2026-08-20] 支払台帳（発行控え）帳票を新設（請求台帳の支払側の対）

### Agent
- Claude Opus 4.8 : Anthropic : Sekiya Sato Claude Code

### Editor
- Claude Code

### 目的
- P0 Release Gate（完成度チェックリスト §4.1 段階7 / §3.11）の支払側。支払計算が `SummaryKaiShi` に保存する確定済み `ShiharaiYoteiDay`（支払予定日）を突合・発行控えできる帳票が皆無だったため、請求台帳（発行控え, `943aa16`）の支払側の対として新設する。

### 判断
- `SummaryKaiShi` には請求側の `SeikyuNo`（請求書番号）・`Renban`（再発行世代）に相当する列が存在しない（`CvBase/BaseDbKake.cs` L365-487 で確認）。よって支払台帳は番号・再発行世代を持たない9列構成とし、空いた分を支払額(`TotalOut`)へ充て、仕入額+支払額+残高で対称化した。
- 既存「支払一覧表」は金額中心で支払予定日を出さず、「月別支払予定表」は `MasterShiire` の支払条件からライブ再計算しており、保存済み `ShiharaiYoteiDay` を出す帳票は皆無だった（請求側と同じ欠落）。→ 支払台帳が保存済み支払予定日を出力する唯一の帳票。
- D-04/D-05 にブロックされない（顧客提出用の支払通知書ではなく社内突合・発行控えのため）。

### 実施内容
- 詳細設計 `.omo/2026-08-20_支払台帳_詳細設計.md`（目的・列定義 item1..item9・SQL・qfm レイアウト・受入 G/W/T・実DB突合の段取り）。
- qfm `printform/ShiharaiLedgerReport.qfm` を新設。`ShiharaiListReport.qfm`（10列）を雛型に、返品・値引列を落とし末尾へ支払予定日を追加した9列（支払日/仕入先CD/仕入先名/対象期間/仕入額/消費税/支払額/残高/支払予定日）へ差替、Shift_JIS(cp932) で保存。
- `ShiharaiLedgerReportViewModel`（`BaseReportViewModel` 派生、`SummaryKaiShi` JOIN `MasterShiire` を SELECT 列順=item 順で取得）、`ShiharaiLedgerReportView.xaml(.cs)`、`MenuData.cs` に「支払台帳（発行控え）」を「支払一覧表」の直後へ追加。

### 検証
- qfm validator（Python 実体が無い環境のため .NET XML で代替）: cp932 ラウンドトリップ・encoding=SHIFT_JIS・root=printstream・path csv/data.txt・portrait・A4基本 position・item数9 を確認、OK。
- `CvWpfclient` build 成功（警告0/エラー0）。
- 実 PDF 描画: `.agents/skills/author-printstream-qfm/tools/qfmprint` ハーネスで `ShiharaiLedgerReport.qfm` + 合成 data.txt（正常/負値/大金額999999999・支払予定日空欄 の3行, cp932）を PrintStream エンジンへ渡し、`IsSuccess=True`・ライセンス全 product 有効・`outfile.pdf`(2878B) 生成を確認。PDF 内容ストリームを inflate してテキスト層を検証し、9列が定義順・負値(-5000/-500)・大金額・支払予定日空欄(行3)・日付書式(yyyy/MM/dd)・右詰め金額が桁溢れせず収まることを確認（G/W/T-1/2/5）。
- `TestServer` 117/117 成功（非回帰。影響範囲は WPF + qfm のみ、ドメイン/サーバ非変更）。
- 実 DB での数値突合・Mini-UAT は P1 として未実施（本作業は合成データでの描画確認まで）。実DB `CvServer/server-user163.db` は `refer/back/` へバックアップ済み。

---

## [2026-08-19] Doc/spec 3ドキュメントを最新コミット（73ec7a9 メニュー整理・CPAサブメニュー削除）へ対応

### Agent
- Kimi K3 : Moonshot AI : Sekiya Sato OpenCode

### Editor
- OpenCode

### 目的
- ユーザーからの要望：`CvWpfclient/Models/MenuData.cs` のメニュー整理（`73ec7a9`、C.P.A サブメニュー削除）に合わせ、Doc/spec の3ドキュメント（機能完成度チェックリスト、未適用・保留課題、仕様決定判断材料）を最新コミット内容へ対応させる。

### 実施内容
- `Doc/spec/2026-08-18_CV10機能完成度チェックリスト.md`：調査基準日/対象HEADを 2026-08-19 `73ec7a9` へ更新。メニュー規模を 16大メニュー・216表示参照・198 View へ再集計（232/212 から）。2.1 区分別参照数（基幹入力105→102、掛・分析59→46）、3.6 受注・展示会（14→10）、3.10 在庫管理（21→22）、3.11 掛管理（16→17）、3.12 分析（43→29）を更新。CPA 14画面と展示会サブメニュー4画面の削除を 2.1/2.3/3.6/3.12/D-15/7章へ反映し、実装判断は 1.1・1.2以降 で継続（実装時にメニューへ再追加）と明記。月次の「税」→「再更新」、システム管理の「管理」→「保守ツール」のサブメニュー統合、掛管理への請求台帳（発行控え）追加も注記。
- `Doc/spec/2026-08-17_旧cvnet比較_未適用・保留課題.md`：更新確認時HEADを `73ec7a9` へ更新し、メニュー整理が未実装空画面の削除であり台帳の論点へ影響が無いことを注記。H5（MDマップ）に絵型一覧表の空画面削除済みを追記。
- `Doc/spec/2026-08-17_旧cvnet比較_仕様決定判断材料.md`：履歴資料のため決定本文は変更せず、冒頭へ補足（2026-08-19）を追加（CPA・展示会サブメニュー削除、決定への影響なし、最新の画面数はチェックリスト参照）。9.4 LATER の展示会スワッチ関連行に空画面削除済みを追記。

### 技術決定 Why
- チェックリスト10章の更新ルールに従い、大メニュー変更に伴う参照数・重複View数を `MenuData.cs` の `typeof(Views.` 出現数と一意数で実測再集計した。198への差分18は CPA 14画面＋展示会4画面の削除と一致（直前には 在庫強制調整実績表・請求台帳 の +2 も吸収）。
- 仕様決定判断材料は「履歴資料として変更せず保存する」位置づけのため、決定事項の本文には触れず日付付き補足のみ追加した。
- 改行コードは各ファイルの既存状態（チェックリスト・未適用課題は LF、判断材料・本ログは CRLF）を維持して minimal diff とした。

### 確認
- 区分別参照数の整合（18+102+46+32+18=216）を確認。ドキュメントのみの変更のためビルドは未実施。

---

## [2026-08-19] H1-H4 発注側 納品予定表 PDF を新設（follow-up 残 4/4・完了）

### Agent
- Claude Opus 4.8 : Anthropic : Sekiya Sato Claude Code

### 目的
- H1-H4 follow-up 残の最後「発注側 帳票版納品予定表」。空クラス(L0)だった `DeliveryScheduleTableViewModel` を実装し PDF 帳票にする。

### 実施内容
- qfm `printform/DeliveryScheduleTable.qfm` を新設（9列: 納品予定日/仕入先CD/仕入先名/発注日/伝票NO/発注数/入荷数/残数/納期遅れ、A4縦cp932）。
- `DeliveryScheduleTableViewModel` を空クラス→`BaseReportViewModel` 派生へ置換（陳腐化コメント〔「納品予定日列が無い」= H1で追加済のため誤り〕も除去）。
  - 納品予定日範囲・仕入先範囲・未完了のみ・納期遅れのみで絞る。NouhinDay 非空の発注を納品予定日順に出す。
  - 入荷数は `Tran03Shiire.RelateNo1`=発注Id 紐付け合計（残管理表と同じ規約）。納期遅れは残管理表と同じ判定式。
- `Views/03Hatchu/DeliveryScheduleTableView.xaml` をプレースホルダ（空Grid）→帳票Viewへ置換。`MenuData.cs`「納品予定表」の説明を「準備中」から更新。

### 検証
- `dotnet build creativevision10.slnx` 成功（警告0/エラー0）。
- 実 PDF 描画: `qfmprint` で9列 data.txt（納期遅れ有/無）を渡し `IsSuccess=True`。PDFテキスト層で全9列・納品予定日順・納期遅れ日数が正しいことを確認。

### 完了
- F2・H1-H4 の follow-up 残（着手可能分）4件すべて実装。残るはリードタイム自動計算(2.0対象外)・調整理由マスタの追加編集(区分は確定済み)のみ。
- 次: Doc/spec のドキュメント更新と、完了した詳細設計の archive 移動。

## [2026-08-19] H1-H4 残管理表へ納品予定日・納期遅れ列を追加（follow-up 残 3/4）

### Agent
- Claude Opus 4.8 : Anthropic : Sekiya Sato Claude Code

### 目的
- H1-H4 follow-up 残の3件目。発注残管理表・受注残管理表（qfm帳票2種）へ納品予定日・納期遅れ列を追加する。

### 実施内容
- `printform/HachuZanKanriTable.qfm` / `JuchuZanKanriTable.qfm` を11列→13列へ再構成（末尾に「納品予定日」「納期遅れ」を追加、
  A4縦150幅内へ各列幅を再配分。Shift_JIS(cp932)、item/datasrc とも13）。
- `HachuZanKanriTableViewModel` / `JuchuZanKanriTableViewModel` の印刷SQLに列を追加:
  - CTE で `NouhinDay`・`EndFlag` を取得。
  - joined で `delayDays`＝納品予定日が非空・`EndFlag=0`・予定日超過のときだけ (基準日−納品予定日)。基準日はクライアント日付。
  - SELECT 末尾に `nouhinDayLabel`（空なら空欄）・`delayLabel`（"N日" / 遅れ無しは空欄）を item12/item13 として追加。

### 検証
- `dotnet build creativevision10.slnx` 成功（警告0/エラー0）。
- 実 PDF 描画: `qfmprint` で両qfmに13列 data.txt（納期遅れ有/無・納品予定日空欄を含む, cp932）を渡し `IsSuccess=True`。
  PDFテキスト層で発注側=仕入先/発注日/…/納品予定日/納期遅れ、受注側=得意先/受注日/…/最終売上日/納品予定日/納期遅れ が正しく整列することを確認。

### 残（次の作業）
- H-b 発注側 帳票版納品予定表（`DeliveryScheduleTable` 空クラス実装＋qfm新設）。

## [2026-08-19] F2 在庫強制調整実績表 PDF を新設（follow-up 残 2/4）

### Agent
- Claude Opus 4.8 : Anthropic : Sekiya Sato Claude Code

### 目的
- F2 follow-up 残の2件目「在庫強制調整実績表（PDF）」。強制調整伝票を倉庫・調整日範囲で一覧印刷する帳票を新設する。
- qfm 作成は `author-printstream-qfm` スキルに従い、`qfmprint` ハーネスで実 PDF 描画確認まで行う。

### 設計判断
- 明細（SKU別調整数）は `Tran61Chosei.Jmeisai`(JSON) にあり SQL 展開できないため、本表は**伝票単位**（調整数計）で出す。
  SKU別内訳は既存の「在庫強制調整実績照会」画面で見る。帳票は請求台帳と同じ `BaseReportViewModel` 型で配線。

### 実施内容
- qfm `printform/StockForceReport.qfm` を新設（`SeikyuLedgerReport.qfm` をコピーし8列へ差替: 調整日/伝票No/倉庫CD/倉庫名/調整理由/調整数計/担当者/メモ）。A4縦・Shift_JIS(cp932)。
- `StockForceReportViewModel`（`BaseReportViewModel` 派生。Tran61Chosei を MasterTokui/MasterMeisho/MasterShain と LEFT JOIN し Kubun=強制調整・調整日範囲・倉庫で絞る。SELECT 列順=item1..item8）。
- `Views/08Zaiko/StockForceReportView.xaml(.cs)`（請求台帳Viewを土台に調整日範囲＋倉庫選択）。
- `Models/MenuData.cs` に「在庫強制調整実績表」を追加。

### 検証
- `dotnet build creativevision10.slnx` 成功（警告0/エラー0）。
- qfm validator 代替（cp932読取り・root・path・page・item8/datasrc8）OK。
- 実 PDF 描画: `qfmprint` で合成 data.txt（正常/負値−3/負値−10・担当者空欄の3行, cp932）を PrintStream へ渡し `IsSuccess=True`・ライセンス全product有効・`outfile.pdf` 生成。
  PDFテキスト層で全8列・負値・空欄・日付が正しいことを確認。

### 残（次の作業）
- H-a 残管理表への納品予定日・納期遅れ列、H-b 発注側 帳票版納品予定表（qfm新設）。

## [2026-08-19] F2 調整理由マスタ＋選択UIを実装（follow-up 残 1/4）

### Agent
- Claude Opus 4.8 : Anthropic : Sekiya Sato Claude Code

### 目的
- Doc/spec の完了詳細設計10件を `Doc/spec/archive/` へ退避後、F2・H1-H4 の follow-up 残が最新ソースで未完であることを確認。
  ユーザーが調整理由区分を確定したため、着手可能な4残作業のうち 1件目「調整理由マスタ＋選択UI」を実装する。
- 計画: `.omo/2026-08-19_F2H_followup_残作業計画.md`。

### 調整理由区分（ユーザー確定）
- CalcFlag: コード10〜19=加算(+)/20〜29=減算(−)。10 入庫 / 20 紛失 / 21 盗難 / 22 破損 / 23 検品ミス / 29 その他。
- `Tran61Chosei.Id_Riyu` は `MasterMeisho`（Kubun=`CHR`）の行を指す。符号は行の Code(int) から算出。

### 実施内容
- `CvBase/BaseDb2Trans.cs`: `Tran61Chosei.Id_Riyu` に `[ForeignKey(MasterMeisho, meishoKubun:"CHR")]`。
  静的クラス `ChoseiRiyu`（`Kubun="CHR"`、`CalcFlag(int)`／`CalcFlag(string)`=10〜19で+1/他−1）を新設。
- `CvBase/DefineDataTable.cs`: 新規DB向けに Kubun=`CHR` の名称区分1行＋理由6行を seed。
- `CvBase/UpdateDb.cs`: 既存DB向けに `26_08_19_01` を追加し MasterMeisho へ同6行＋IDX行を INSERT（Vdc/Vdu は UTC Ticks を SQLite 式で生成）。
- `CvWpfclient/Helpers/ViewModels/BaseStockSheetInputViewModel.cs`: `protected virtual int RegisterSign => 1;` を追加し、
  Register で Su/Kingaku に符号を掛ける（棚卸・移動・返品は既定+1で不変）。
- `CvWpfclient/ViewModels/08Zaiko/StockForceInputViewModel.cs`: 理由 ComboBox（`Reasons`/`SelectedReason`）を追加。
  入力は絶対値、`RegisterSign` を選択理由の CalcFlag で決定。理由未選択・入力数<0 は登録前検証で弾く。`BuildDenpyo` で `Id_Riyu` を積む。
- `CvWpfclient/Views/08Zaiko/StockForceInputView.xaml`: 調整理由 ComboBox（必須）を追加し、注意書きを「増減は理由で決まる」に更新。
- `CvWpfclient/ViewModels/08Zaiko/StockForceHistoryViewModel.cs` + `View`: 実績照会に「調整理由」列を追加（Id_Riyu を MasterMeisho で解決）。

### 検証
- `dotnet build creativevision10.slnx` 成功（警告0/エラー0）。
- `Tests/TestServer` 直接実行: 合計114 / 成功114 / 失敗0（非回帰）。
- 実機の画面操作確認は未実施（実データに調整対象在庫が要るため別途）。

### 残（次の作業）
- F2-b 在庫強制調整実績表 PDF（qfm新設）、H-a 残管理表への納品予定日・納期遅れ列、H-b 発注側 帳票版納品予定表（qfm新設）。

## [2026-08-19] 請求台帳（発行控え）帳票を新設し qfm スキルを実地検証・追記

### Agent
- Claude Opus 4.8 : Anthropic : Sekiya Sato Claude Code

### 目的
- 完成度チェックリスト残タスクから qfm スキル検証に適したものを選び、詳細設計→実装→build→実 PDF 描画まで通す。検証で判明した実手順を `author-printstream-qfm` skill へ追記する。

### 選定
- 請求・支払の主要 6 帳票は qfm+SQL+画面+メニューが全て実装済みで「未実装の qfm」は無かった。唯一のギャップは、請求計算が `SummaryUriSei` に保存する `SeikyuNo`（請求書番号）・`Renban`（再発行世代）・`NyukinYoteiDay`（入金予定日）を**どの帳票も出力していない**こと（`CvWpfclient` で参照ゼロ）。これは P0 Release Gate（§4.1 段階7 の数値突合）の核心で、D-04/D-05 にブロックされない。→ 新帳票「請求台帳（発行控え）」を新設対象に選定。

### 実施内容
- 詳細設計 `Doc/spec/2026-08-19_請求台帳（発行控え）_詳細設計.md` を作成（目的・列定義 item1..item10・SQL・qfm レイアウト・受入 G/W/T）。
- qfm `printform/SeikyuLedgerReport.qfm` を新設。`SeikyuListReport.qfm` をコピーし列（請求書番号/請求日/得意先CD/得意先名/対象期間/売上額/消費税/残高/入金予定日/再発行）・見出し・タイトルを最小差分で差替、Shift_JIS(cp932) で保存。
- `SeikyuLedgerReportViewModel`（`BaseReportViewModel` 派生、`SummaryUriSei` JOIN `MasterTokui` を SELECT 列順=item 順で取得）、`SeikyuLedgerReportView.xaml(.cs)`、`MenuData.cs` に「請求台帳（発行控え）」を追加。
- skill `author-printstream-qfm/SKILL.md` へ実手順を追記: (1)Python 無し環境での validator 代替（iconv/grep + .NET XML）、(2)DB・サーバ不要で実 PDF を描画する検証ハーネス、(3)`BaseReportViewModel` 帳票配線パターン、(4)worked example。ハーネス一式を `tools/qfmprint/`（`PrintPdfService` の PrintContext 構築を最小再現）として同梱。

### 検証
- `CvWpfclient` build 成功（警告0/エラー0）。TestServer 114/114 成功（非回帰）。
- 実 PDF 描画: `tools/qfmprint` ハーネスで `SeikyuLedgerReport.qfm` + 合成 data.txt（正常/負値/大金額/入金予定日空欄の 3 行, cp932）を PrintStream エンジンへ渡し、`IsSuccess=True`・ライセンス全 product 有効・`outfile.pdf` 生成を確認。PDF テキスト層で全 10 列・負値・空欄・日付書式が正しいことを確認。
- qfm validator（Python）はこの環境に Python 実体が無く実行不可のため、構造チェック（encoding/root/path csv/portrait/A4/item・datasrc 数）と .NET XML 整形式チェックで代替した。
- 実 DB での数値突合・Mini-UAT は P1 として未実施（本作業は合成データでの描画確認まで）。ローカルハーネスはフォント埋め込みが本番サーバと異なり、ラスタ画像で一部 CJK グリフが欠けるが、テキスト層は正しい。

## [2026-08-19] PrintStream qfm フォーマット仕様を新設し author-printstream-qfm skill を再構成

### Agent
- Claude Opus 4.8 : Anthropic : Sekiya Sato Claude Code

### 目的
- qfm を AI が新規作成・修正できる粒度のフォーマット仕様を整備し、既存スキルをその仕様を核とする 2 層構成へ刷新する。

### 判断（順次）
- CHM `refer/printdll/PrintStream.chm` は `PrintStream_decompiled/` に展開済みで読める（FormEditor 116 ページ + Javadoc）。
- CHM には raw qfm XML の文法書がない（`datadesc`/`calctype` 等のタグ名で全文検索してもヒット0）。GUI 操作とスクリプト API の解説にとどまる。
- そのため仕様化は「実 qfm コーパスの機械マイニング（文法）＋ CHM の意味論（属性値）」のハイブリッドで実施可能と判断した。

### 実施内容
- 実 qfm を機械解析する抽出器 `Doc/spec/tools/extract_qfm_grammar.ps1`（PowerShell・cp932 対応）を作成した。要素／属性／enum 値の分布を出力する。
- cv10 `printform/`（108 本）と旧cv.net `C:\gitroot\cv\cvnet_pkg\cvnetpss`（1967 本）の計 **2075 本**を解析し、要素 23 種・属性・enum 値を実測で確定した。
- 仕様本体 `Doc/spec/PrintStream_qfmフォーマット仕様.md` を新設した。要素ツリー、要素×属性×値の文法表、decode 編集文字列チートシート（文字列/数値/日付）、骨格テンプレ、作成ワークフロー、CHM 出所を収録。各項目に出所（`[C]`CHM確定／`[M]`コーパス実測／`[C+M]`両方）を明記した。
- 入力は CSV のみ（`path datatype="csv"` が 2072/2072）というユーザー指示に合わせ、固定長・XML 入力は対象外とした。複数レコード様式のみ `prefix` を任意扱いで記載した。
- `.agents/skills/author-printstream-qfm/SKILL.md` を刷新した。書式詳細を仕様 md へ委譲し、SKILL 側は運用規律（Shift_JIS(cp932)・CSV data.txt・コピー起点・validator・PDF 確認・ロールバック）に集約した。雛形選択を表化し、description を仕様核参照へ更新した。

### 確認
- 仕様 md が参照する雛形 qfm 7 本（MasterShainMente / MasterMeishoMente / MasterSysKanriMente / MasterPrintBarcode002 / ...Code39 / ...Nw7 / ...Sho）の実在を確認した。
- SKILL.md から仕様 md への相対パス `../../../Doc/spec/...` が構成上正しいことを確認した。
- コード・テストの変更はない。ドキュメント 2 新設＋スクリプト 1 新設＋スキル 1 更新のみ。実 PDF 出力によるレイアウト確認は本作業では未実施（帳票を実際に作る際に実施する）。

## [2026-08-18] 請求・支払計算の完成度チェックリストを最終更新

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 独立最終レビュー指摘なしを受け、請求・支払計算の実装済み範囲、Release Gate、残余リスクを最新HEADへ整合させる。

### 実施内容
- `2026-08-18_CV10機能完成度チェックリスト.md` の基準HEADを `6a976d3` へ更新した。DBスキーマ、日次請求/支払残、月次売掛/買掛、gRPCストリーミング、WPF請求/支払/再発行/Rebuild、締日変更ブロックを実装済みとして反映した。
- 月次掛の `Total` 正値源、返品/値引の正値、返品税だけの符号、KIN明細、Other、不正JSON=0、負方向Balance、後続月再計算、冪等性を要約した。Rebuildの `DenDay` / `DayTo` 締日照合、不一致時の手動再計算案内と `Msg051`〜`Msg057`送信0件保証、保存行なしの検出限界を記録した。
- D-02/D-03、UAT-05/UAT-06、旧CV.net対応表、次担当指示を実態に更新し、帳票・月次予定表の数値突合、実DB/Mini-UAT、期首残高/移行、D-04/D-05、D-01/D-07/性能/メニュー公開・権限を優先順として明記した。

### 確認
- 関連テスト30/30、TestServer全114/114、`CvBase` / `TestServer` / `CvWpfclient` build成功、独立最終レビュー指摘なしの記録を反映した。本作業は文書2ファイルだけであり、ソース・テスト・完成度以外の文書は変更していない。
- 実画面操作の自動化、帳票・月次予定表の数値突合、実DB/Mini-UAT、`substr(DenDay, 1, 6)` の性能測定は未実施として残した。

## [2026-08-18] Rebuild締日変更ブロックの独立再々レビューP2を修正

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- Rebuild要求計画と実WPF送信経路を同一の共有記述子・送信境界へ統一し、締日変更ブロックの送信0件保証を実経路で検証可能にする。

### 実施内容
- `CvBase` の共有Plannerを、`CvFlag`、対象年月範囲、対象月、締日まで展開済みの順序付き要求記述子へ変更した。旧テスト専用のフラグ計画は削除し、在庫、売掛、買掛、請求残、支払残の順序を実行記述子だけで表す。締日0件では売掛・買掛の記述子は残し、該当する請求残・支払残だけを0件とする。
- 締日照会、不一致判定、記述子生成、要求変換、送信コールバックを同じ汎用非同期送信境界に集約した。不一致、照会例外、照会取消では、記述子生成・要求変換・送信コールバックへ進まない。
- `StockKakeUpdateViewModel` は共有記述子を一対一で表示名・`CvMsg`・パラメータへ変換し、実際の `QueryMsgStreamAsync` を共有送信境界のコールバック内へ移動した。画面側に対象別の要求回数展開は残さない。
- 詳細設計9.2、G/W/T-10、テスト方針を、共有記述子と送信境界・締日0件の受入条件へ更新した。

### 確認
- `SummaryKakeDbTests` に、展開済み記述子の完全な順序・年月・締日、締日0件の対象4種、正常時の要求変換・送信順序と件数、不一致・照会例外・照会取消での記述子生成／要求変換／送信0件を追加した。
- `CvBase/CvBase.csproj`、`Tests/TestServer/TestServer.csproj`、`CvWpfclient/CvWpfclient.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。TestServer全114件は成功した。XAMLは未変更。
- WPF実装の `QueryMsgStreamAsync` は `SummaryRebuildRequestDispatchGate.ExecuteAsync` の送信コールバック内だけにあり、共有テストが検証する同一の送信境界を通ることをセルフレビューした。実画面のダイアログ操作・通信取消は自動化用プロジェクトがないため未実施。
- 区分Cの金額集計とRebuild安全策を含むため、独立最終レビュー待ちとする。

## [2026-08-18] Rebuild締日変更ブロックの最終レビュー指摘を修正

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 空・不正な保存締日が照会範囲から脱落する問題と、対象別の要求計画・確認文・送信境界の受入不足を解消する。

### 実施内容
- `SummaryUriSei`／`SummaryKaiShi` の物理スキーマ、`DenDay` 索引、生成SQLの `DenDay=DayTo=締日` を確認し、締日照会の対象月を `DayTo` ではなく `DenDay` で絞る共有SQLへ変更した。`DayTo` は nullable DTO として受け、NULL・空・8桁以外・不正年月を安全に不一致化する。
- `CvBase` に要求計画・確認追加文・送信境界を追加し、WPFは共有計画順（在庫、売掛、買掛、請求残、支払残）で要求を組み立てるようにした。締日照会の不一致・例外・取消では要求作成へ進まない。
- 確認文を全て＝請求残・支払残、売掛のみ＝請求残、買掛のみ＝支払残、在庫のみ＝追加文なしへ確定した。
- 詳細設計9.1、G/W/T-10、テスト方針を `DenDay` 範囲選択とNULL・空・不正 `DayTo` の不一致化へ整合させた。

### 確認
- `SummaryKakeDbTests` 29件が成功。`DenDay` 月範囲で空・不正 `DayTo` を両側とも取得すること、範囲外を除外すること、物理 `DayTo` のNULL更新が拒否されること、DTOのNULL判定、4対象の正確なCvFlag順序、確認文、照会不一致・例外・取消時の要求作成0件を確認した。
- `Tests/TestServer/TestServer.csproj` と `CvWpfclient/CvWpfclient.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。XAMLは未変更。
- WPFのストリーム送信は共有送信境界が要求を返した後にだけ存在するため、照会中の不一致・例外・取消で送信へ進まないことをテストと構造で確認した。実画面のダイアログ操作・通信取消は未自動化。
- 区分Cのため、独立再々レビュー待ちとする。

## [2026-08-18] Rebuild締日変更ブロックの独立レビュー指摘を修正

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- Rebuild締日変更ブロックのサーバ型解決、入力競合、受入テスト不足を解消する。

### 実施内容
- `SummaryClosingCheckRow` と締日判定規則を `CvBase` の公開共有型へ追加し、`StockKakeUpdate` と請求計算／支払計算の締日取得が WPF private 型を `QueryListSqlParam.ItemType` に渡さないよう統一した。
- Rebuild実行は確認直後に対象・年月・対象月をスナップショット化し、締日照会から要求生成・完了表示まで同じ値を使用する。`IsProcessing` は照会前に設定し、照会・送信を単一の `try` / `finally` で保護した。不一致、キャンセル、例外では要求列生成・`Msg051`〜`Msg057`の送信へ進まない。
- 請求残／支払残を含む確認文へ、保存済み集計行がない場合は締日変更を検出できない制約を明示した。
- 既存 TestServer に、共有DTOの実サーバ `Msg101_Op_Query` 型解決、対象4種、1日／31日月末丸め、99月末、保存行なし、不一致、最大5件＋残件数、送信可否ゲートを追加した。月次掛集計には19／29／39／89境界と90／99除外を売掛・買掛対称に追加した。

### 確認
- `Tests/TestServer/TestServer.csproj` と `CvWpfclient/CvWpfclient.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。
- `SummaryKakeDbTests` は25件すべて成功し、共有DTOの実サーバ型解決テストも1件成功した。
- 締日照会の `await` より後にだけ要求列生成・ストリーム送信が存在することを静的に確認した。WPFの確認ダイアログ操作、通信取消、実画面警告は自動化用プロジェクトがないため未実施。
- 区分Cの金額集計とRebuild安全策を含むため、独立再レビュー待ちとする。

## [2026-08-18] Rebuildの締日変更ブロックを追加

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 締日マスタ変更後の保存済み請求残・支払残を、在庫・掛再更新で無条件に削除・再作成しないようにする。

### 実施内容
- `StockKakeUpdate` は利用者確認後かつ要求列生成前に、対象側の `SummaryUriSei`／`SummaryKaiShi` と現在マスタ締日をパラメータ化照会で照合するようにした。
- 不一致時は最大5件と残件数、手動再計算の案内を警告し、`Msg051`〜`Msg057`を送信せず処理を開始しない。
- 全ては両側、売掛のみ／買掛のみは該当側、在庫のみは検査なしとし、照会・期待締日判定・警告組立を小メソッドへ分離した。

### 確認
- `CvWpfclient/CvWpfclient.csproj` と `Tests/TestServer/TestServer.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。
- `SummaryKakeDbTests` を再実行し、21件すべて成功した。
- XAMLは未変更。既存バインディングとApp共通リソースを確認した。WPFの実サーバ照会・警告表示は自動化用プロジェクトがないため未確認。
- 区分C作業のため、独立レビュー待ちとする。

## [2026-08-18] 月次売掛・買掛の確定集計ルールを実装

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 月次売掛・買掛を請求残・支払残と同じ金額列・内訳・残高符号へ統一する。

### 実施内容
- `CalcSummaryUriKake`／`CalcSummaryKaiKake` を、`Total`の正値内訳、返品税だけの符号化、内訳からの合計算出、`TotalIn/TotalOut - TotalSales/TotalShiire`方向の残高へ対称に更新した。
- 入金・支払は有効JSONの明細だけを集計し、ヘッダ`KingakuTotal`を正値源から除外した。05・未知KINはOther、不正JSONは空明細・0として処理を継続する。
- `SummaryKakeDbTests` を売掛・買掛対称に拡張し、区分範囲、99除外、税、残高、後続月、冪等性、KINフォールバック、不正JSONを固定した。テストヘルパーは`Total`／`KingakuTotal`と明細／ヘッダを意図的に異ならせる。

### 確認
- `Tests/TestServer/TestServer.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。
- Microsoft.Testing.Platform の実行形式から `SummaryKakeDbTests` を実行し、21件すべて成功した。
- 金額結果を広範囲に変更する区分C作業のため、独立レビュー待ちとする。

## [2026-08-18] 請求・支払計算の不正JSON時の明細集計設計を訂正

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 不正JSONを空配列へ防御する場合に明細金額を復元できないことを、KIN Otherフォールバックと矛盾しない設計へ訂正する。

### 実施内容
- Otherフォールバックを有効JSON内の `Id_Kin=0`、未知・未登録・空・01〜05以外のKINコードに限定した。
- 不正JSONは例外にせず空明細として扱い、その伝票の `TotalIn` / `TotalOut` を0とすること、不正JSONの検知・補正は別のデータ品質課題であることを明記した。
- G/W/T-8とテスト方針を、有効JSONの未知KIN→Other、不正JSON→例外なし・0へ分離した。`KingakuTotal`を正値源にしない方針は維持した。

### 確認
- 詳細設計のSQL集計、請求計算、支払計算、受入、テスト方針の記述を同じ期待値へ統一した。
- 今回は設計文書のみを変更し、ソース実装・テスト・完成度チェックリストは変更していない。

## [2026-08-18] 請求・支払計算のRebuild安全策と月次掛集計ルールを詳細設計へ反映

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 締日変更済みマスタでのRebuild誤更新を防ぎ、月次売掛・買掛の金額列・残高符号を請求残・支払残と整合させる次段階実装の詳細設計を確定する。

### 実施内容
- `StockKakeUpdate` の対象別締日変更ブロック、パラメータ化照会、送信0件保証、警告内容、既存残高行がない場合の検出限界を設計化した。
- `CalcSummaryUriKake`／`CalcSummaryKaiKake` の `Total` 正値集計、正値の返品・値引内訳、税符号、明細だけを正値源とするKIN集計、未知KINのOtherフォールバック、残高符号、JSON防御を具体化した。
- テストデータの `Total`／`KingakuTotal` 分離、Rebuildブロック受入条件、区分Cの独立確認必須を追記した。
- Renbanのmigration既定0と計算生成時の業務既定1を区別し、月別予定表・帳票qfmはスコープ外のままとした。

### 確認
- 文書差分を現行 `SummaryDb`、`SummaryKakeDbTests`、`StockKakeUpdateViewModel` と照合した。
- 今回は設計文書のみを変更し、ソース実装・テスト・完成度チェックリストは変更していない。

## [2026-08-18] 在庫・掛再更新へ請求残・支払残のRebuildを追加

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 在庫・掛再更新の売掛・買掛再集計後に、同じ対象年月の請求残・支払残も通常再計算で再作成できるようにする。

### 実施内容
- `StockKakeUpdate` が売掛・買掛再集計の完了後、得意先／仕入先マスタに実在する有効締日（1～31日・末日）を取得し、対象年月×締日で `Msg056`／`Msg057` を順次実行するようにした。
- Rebuildから渡す `BillingParameter.IsReissue` は常に `false` とし、既存の請求書番号・連番を保持する通常再計算を使用する。
- 実行確認時に請求残・支払残も再作成することを表示するようにした。

### 確認
- `CvServer/CvServer.csproj`、`CvWpfclient/CvWpfclient.csproj`、`Tests/TestServer/TestServer.csproj` を Development 環境で直列ビルドし、警告・エラーなしを確認した。
- Microsoft.Testing.Platform の実行形式から `SummaryKakeDbTests` を実行し、15件すべて成功した。

## [2026-08-18] 請求書の明示的再発行を追加

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 通常再計算の採番冪等性を維持しつつ、明示的な請求書再発行時だけ請求書連番を更新できるようにする。

### 実施内容
- `BillingParameter.IsReissue` を追加し、請求計算のgRPCストリーミングへ伝播した。
- 請求残の再作成時、通常実行は既存 `SeikyuNo`／`Renban` を維持し、再発行指定時は `Renban` を+1して請求書番号を再採番するようにした。
- 請求計算画面に再発行チェックボックスを追加した。
- 採番維持と再発行連番のテストを追加した。

### 確認
- `Tests/TestServer/TestServer.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。
- Microsoft.Testing.Platform の実行形式から `SummaryKakeDbTests` を実行し、15件すべて成功した。
- `CvWpfclient/CvWpfclient.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。

## [2026-08-18] 請求計算・支払計算の実行画面を追加

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 得意先／仕入先の締日、計算月、コード範囲を指定して請求残・支払残をストリーミング実行できる画面を提供する。

### 実施内容
- `BillingCalculationView`／`PaymentCalculationView` と共通ViewModelを追加し、実データから動的に取得した締日、計算月、コード範囲、進捗、実行・キャンセルを実装した。
- 請求計算は親子の締日不一致を事前検出し、「マスタ変更および請求再計算が必要」と警告するが、処理はブロックしない。
- 掛管理メニューの請求計算・支払計算を実装済み表示へ更新した。

### 確認
- 追加XAMLのXML構文、名前空間、App共通リソース、ViewModelバインディングを確認した。
- `CvWpfclient/CvWpfclient.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。

## [2026-08-18] 請求・支払計算のgRPCストリーミングを結線

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 請求残・支払残の計算を既存の掛再更新と同じgRPCストリーミング経路から実行できるようにする。

### 実施内容
- `BillingParameter`、`Msg056_SummaryUriSei`、`Msg057_SummaryKaiShi` を追加した。
- `SummaryDb` に請求残・支払残のストリーミング入口を追加し、`QueryMsgStreamService` から結線した。
- 両ストリーミング入口がエラーなく完了通知を返すテストを追加した。

### 確認
- `Tests/TestServer/TestServer.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。
- Microsoft.Testing.Platform の実行形式から `SummaryKakeDbTests` を実行し、15件すべて成功した。

## [2026-08-18] 支払残の計算処理を追加

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 指定締日・支払月・仕入先コード範囲から支払残を冪等に作成し、支払予定日を保持する。

### 実施内容
- `SummaryDb.CalcSummaryKaiShi` を追加し、仕入先締日から支払期間を算出して対象仕入先ごとに支払残をDELETE→再作成するようにした。
- 仕入／返品／値引、返品税の符号、支払内訳、累計残、`PayMonth`／`PayDay` による支払予定日を実装した。
- 支払残の期間・内訳・累計残・冪等性・月末予定日を検証するテストを追加した。

### 確認
- `Tests/TestServer/TestServer.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。
- Microsoft.Testing.Platform の実行形式から `SummaryKakeDbTests` を実行し、13件すべて成功した。

## [2026-08-18] 請求残の計算処理を追加

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 指定締日・請求月・得意先コード範囲から請求残を冪等に作成し、請求書番号と入金予定日を保持する。

### 実施内容
- `SummaryDb.CalcSummaryUriSei` を追加し、締日から請求期間を算出して対象得意先ごとに請求残をDELETE→再作成するようにした。
- 売上／返品／値引、返品税の符号、入金内訳、累計残、`PayMonth`／`PayDay` による入金予定日を実装した。
- 通常再計算では既存の `SeikyuNo`／`Renban` を維持し、未採番時は連番1で採番するようにした。
- 請求残の期間・内訳・累計残・採番維持・予定日を検証するテストを追加した。

### 確認
- `Tests/TestServer/TestServer.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。
- Microsoft.Testing.Platform の実行形式から `SummaryKakeDbTests` を実行し、11件すべて成功した。

## [2026-08-18] 請求・支払計算の請求残／支払残スキーマを追加

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 請求計算・支払計算の詳細設計に基づき、請求書番号・連番・入金予定日・支払予定日を保持できるようにする。

### 実施内容
- `SummaryUriSei` に `SeikyuNo`、`Renban`、`NyukinYoteiDay` を追加した。
- `SummaryKaiShi` に `ShiharaiYoteiDay` を追加した。
- `UpdateDb` の `26_08_18_02` で既存DBへ同列を追加し、既存行は空文字または0で初期化する。

### 確認
- `CvBase/CvBase.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。

## [2026-08-18] 請求・支払計算の集計・締日ルールを詳細設計へ反映

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 請求・支払計算と掛月次集計について、締日、対象期間、区分別内訳、累計残高、税率・丸めのユーザー決定を詳細設計へ反映する。

### 実施内容
- 得意先・仕入先の締日をそれぞれ `MasterTokui.Shime1` / `MasterShiire.Shime1` と明記し、`PayMonth`/`PayDay` は予定日の算出専用とした。
- `SummaryUriKake` / `SummaryKaiKake`、`SummaryUriSei` / `SummaryKaiShi` の区分別集計、合計式、対象期間分のみを保持する内訳、対象期間までの累計 `Balance` を明文化した。
- 税は取引の `Tax` を集計し、返品は `CalcFlag` により負値とすること、1.0の新規税額算出は `MasterSysMan` の `No=1` の `Tax` と四捨五入を使うことを記載した。
- 通常再計算・Rebuild時の採番維持と、明示的再発行時だけの `Renban` 増加を明記した。

### 確認
- Markdownの見出し・表・用語を確認し、`git diff --check` を実行する。
- 文書のみの変更のため .NET build/test は省略する。

## [2026-08-18] CV10機能完成度チェックリストを現行メニュー基準で再作成

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 2026-08-12版の機能完成度チェックリストを履歴として保存し、最新コミットと `CvWpfclient/Models/MenuData.cs` を基準に2026-08-18版を新設する。
- 実装済み機能の詳細列挙から、1.0/1.1/1.2以降の計画、必要な仕様決定、最小UAT、旧CV.net機能の継承計画を中心とする文書へ改める。
- 後続担当（Luna等）が、請求・支払計算から設計・実装・検証へ着手できる引継ぎ粒度にする。

### 調査根拠
- HEAD `c0dc00c`（請求計算・支払計算の詳細設計。実装未着手）。
- `CvWpfclient/Models/MenuData.cs` の16大メニュー、232表示参照、重複を除く212 View。
- `Doc/spec/2026-08-18_請求計算・支払計算_詳細設計.md` と2026-08-18付の各詳細設計。
- `Doc/spec/2026-08-17_旧cvnet比較_仕様決定判断材料.md`、未適用・保留課題台帳。
- `refer/cvnet-knowlege/` のマニュアル、業務フロー、帳票集、DB定義の既存調査結果。

### 実施内容
- `Doc/spec/2026-08-18_CV10機能完成度チェックリスト.md` を新設した。
- 2章を現行メニューの規模、1.0完成までの断絶、期別ロードマップ、優先順位へ変更した。
- 3章を16大メニュー別に再構成し、各領域を「現在地 / 1.0実装予定 / 1.1予定 / 1.2以降・決定」で整理した。
- 4章以降を大幅に再構成し、請求・支払詳細設計の7段階、月次・原価・残高登録、15件の仕様決定、最小UAT 9シナリオ、旧CV.net資料別の期別計画、後続担当の開始/停止条件、既知リスクを記載した。
- 請求のDELETE→再作成による冪等性と、再実行時の`Renban+1`が競合する点をD-03として明示し、通常再計算と明示的再発行の分離を推奨した。
- ユーザー指示により、LCVの全9画面・移行・セキュリティ審査・UATを1.1へ配置した。
- ユーザー指示により、最大3締日と月末以外締日の税対応を1.2以降へ配置し、1.1の締日更新は1得意先1締日の運用制御として残した。
- 2026-08-12版チェックリストは変更せず履歴として保持した。

### ログアーカイブ
- 旧 `Doc/aicoding_log.md`（945行）を `Doc/aicoding_log_013.md` へ移動した。
- アーカイブ作成後、今回分だけを記載した新しい `Doc/aicoding_log.md` を作成した。

### 確認
- 文書内の大メニュー名と表示参照数を `MenuData.cs` から再集計した。
- LCVが1.1、最大3締日が1.2以降で一貫していることを検索確認した。
- Markdown見出し、表、相対リンク、UTF-8、CRLF、`git diff --check` を確認した。
- 文書とログのみの変更であるため、.NET build/testは省略する。

### 残る重要判断
- D-01: 1.0の月次・原価範囲。
- D-03: 通常再計算と請求再発行の採番・冪等性。
- D-05: 適格請求書要件。
- D-06/D-07: 総平均原価の`TQ`と月次処理順。
- D-08/D-09: 期首残高移行とMini-UAT責任者。
- D-14: 1.1 LCVの個人情報、ポイント会計、返品・失効、移行責務。
## [2026-08-18] McpOracleをOracle接続用MCPサーバとして追加

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- `McpSql` と同じ stdio MCP サーバ構成で、Oracle 接続文字列を引数または環境変数から受け取り、スキーマ参照・照会・任意時の更新を行える `McpOracle` プロジェクトを追加する。

### 設計・実装
- `McpOracle/McpOracle.csproj` を新設し、既存の中央パッケージ管理から `ModelContextProtocol.Core` と `Oracle.ManagedDataAccess.Core` を参照する。
- 起動引数の第1非オプション引数、または `MCPORACLE_CONNECTION_STRING` を接続文字列として利用する。`--allow-write` を指定しない既定時には更新ツールを MCP に公開しない。
- `list_tables` / `describe_table` / `list_indexes` は `USER_OBJECTS`、`USER_TAB_COLUMNS`、`USER_CONSTRAINTS`、`USER_INDEXES` などの `USER_*` データディクショナリを使用し、接続ユーザー所有のオブジェクトに限定する。
- `query` は単文の `SELECT` または `WITH ... SELECT` に限定し、行数・応答サイズ・セルサイズを上限管理する。値は `:p0`、`:p1` 形式でバインドする。
- `explain` は `EXPLAIN PLAN FOR` と `DBMS_XPLAN.DISPLAY()` を使用する。DDL は `DBMS_METADATA.GET_DDL` が許可されない環境でも、列・制約情報を返せるようにする。
- 接続文字列・パスワードをログや応答へ出力しない。読取り時の SQL 検証に加え、Oracle アカウント権限を最終的なアクセス制御境界とする。
- `creativevision10.slnx` に `McpOracle` を追加した。

### 確認
- `C:\gitroot\UT\vscmd.bat dotnet build McpOracle\McpOracle.csproj` 成功（警告 0、エラー 0）。
- 引数なし起動で、接続文字列の指定方法を stderr に表示して終了することを確認した。
- `git diff --check` を実行した。

### 使用例
- `McpOracle.exe "Data Source=192.168.9.243/cvnet;User Id=CV00PKG;Password=CV00PKG;"`
- 環境変数: `MCPORACLE_CONNECTION_STRING` に同じ接続文字列を設定して `McpOracle.exe` を起動する。
- 更新を許可する場合: 上記に `--allow-write` を追加する。
## [2026-08-21] UAT-01派生SKU列順不整合修正とテスト成果物保存

### Agent
- OpenAI GPT-5 : Sekiya Sato Codex

### Editor
- Codex

### 目的
- UAT-01で検出した`DerivedShohinColSiz`の列順不整合を修正し、UAT結果・再テスト手順・実行ソースを保存する。

### 実施内容
- `CvBase/BaseDbDerived.cs`: `DerivedShohinColSiz.CreateSql`を明示列INSERTへ変更し、DB物理列順に依存しないよう修正。
- `Doc/spec/2026-08-18_CV10機能完成度チェックリスト.md`: UAT-01の通し実施結果、課題修正、残作業を追記。
- `Doc/test/`: UAT-01結果レポート、再テスト手順、DB投入・集計検証ソース、帳票検証ソース、読み取り検算SQLを保存。

### 確認
- `C:\gitroot\UT\vscmd.bat dotnet build CvBase\CvBase.csproj --no-restore`: 成功（警告0、エラー0）。
- `C:\gitroot\UT\vscmd.bat dotnet build Doc\test\UAT01\UAT01Runner.csproj`: 成功（警告0、エラー0）。
- `C:\gitroot\UT\vscmd.bat dotnet build Doc\test\UAT01\ReportRunner.csproj`: 成功（警告0、エラー0）。
- DBファイル、WAL/SHM、DBバックアップはコミット対象から除外する。

---
## [2026-08-21] 請求・支払帳票のPDF目視確認とE7親子締日ワーニングの実データ発火確認

### Agent
- Anthropic Claude Opus 5 : Sekiya Sato Claude Code

### Editor
- Claude Code

### 目的
- チェックリスト §8.1 の残P0「帳票PDFの目視確認」と「E7警告の実データ発火確認」を閉じる。

### 実施内容
- 請求台帳／請求一覧表／支払台帳／支払一覧表／月別入金予定表／月別支払予定表の6帳票を、サーバ `PrintPdfService` と同経路
  （帳票SQL → Shift_JIS CSV → `PrintAdapter`/`FormWriter.PDF`）でローカル実描画し、PDFテキスト層と手計算値を突合。
  結果と指摘（R-01 金額のカンマ書式未設定／R-02 残高・予定額の負値表記）は
  `Doc/spec/2026-08-21_請求・支払帳票PDF目視確認_結果.md` に記録。R-01/R-02 は人間側で対応するため未修正。
- `tools/summaryreconcile` に `paysakicheck` コマンドを追加。開発DBへ親子関係（`Id_Paysaki`）と締日不一致を投入し、
  請求計算／支払計算の実行前警告（`BuildRangeCheckSql`）と、得意先／仕入先マスターメンテ保存後の警告
  （`BuildAffectedRowCheckSql`、子編集・親編集の双方向）が実データで発火することを確認。検査後に `Id_Paysaki`・締日を必ず復元する。
- `Doc/spec/2026-08-18_CV10機能完成度チェックリスト.md` / `Doc/spec/2026-08-17_旧cvnet比較_未適用・保留課題.md` /
  `tools/summaryreconcile/README.md` を現在地に合わせて更新。

### 確認
- `qfmprint`（`.agents/skills/author-printstream-qfm/tools/qfmprint`）で6帳票とも `IsSuccess=True`、`CheckLicense` の3プロダクトが `status=True`。
- `dotnet run --project tools/summaryreconcile -- paysakicheck <dbPath>`: `親子締日ワーニング(E7): PASS`
  （投入前0件 → 得意先1件・仕入先1件を検出、コード範囲外は0件、子編集1/親編集1/一致ペア0、警告文に再計算案内を含む）。
  実行後に `MasterTokui`/`MasterShiire` の `Id_Paysaki=0`・`Shime1=99` へ復元されていることをSQLで確認。
- 検証データの `data.txt` は Shift_JIS・CRLF単独であること（`\r\r\n` だと全レコードが重複印字される）。
- 開発DBは事前に `refer/back/P0PDF_20260821_pre-seed_server-user163.db` へ退避。DB・WAL/SHM・バックアップはコミット対象外。

---
