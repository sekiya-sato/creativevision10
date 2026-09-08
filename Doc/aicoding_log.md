## [2026-09-08] 上代一括変更 画面①対象商品（抽出条件拡張・可変行・AND/OR・Style/SKU数）Step5
### Agent
- Claude Opus-5 : Anthropic : Claude Code
### Editor
- Claude Code
### 目的
- 設計書`Doc/spec/2026-09-05_上代一括変更_詳細設計.md` Step 5（画面①対象商品）を実装する
### 実施内容
- 抽出条件の検索項目を拡張: 素材/原産国/発売日(店頭投入日)/現在上代(数値比較)/商品分類(Jsub)の枠。仕入先は実装せず理由をコードコメントに残した
- `FieldOptionsStatic`(static)を廃止し`FieldOptions`(非static、Jsubの枠を`MasterMeisho`から動的読込)へ変更。XAMLは`DataGridComboBoxColumn`から`DataGridTemplateColumn`+`ComboBox`(`DataContext.FieldOptions`を`AncestorType=DataGrid`経由参照)へ置換
- 抽出条件行を可変化(初期1行、追加/削除コマンド、3行決め打ちパディング廃止)
- `TranJodaiCond.Ope`(AND/OR)に対応。1行が生む`>=`/`<=`は必ず1単位で括弧化し、行同士は左結合で明示的に括弧を入れて畳み込む。1行目のOpeは無視(UI側も1行目は無効化/空表示)
- 対象Style数(MeisaiRows件数)/SKU数(`DerivedShohinColSiz`件数)を表示。SKU数は件数が多くても`IN`にIdを直接並べず、抽出条件のWHERE句をサブクエリとして再利用
- `CvBase/Parameters.cs`にスカラーCOUNT受け取り用共有DTO`ScalarCountRow`を追加(`QueryListSqlParam.ItemType`はサーバ側で型解決するため、クライアント内の入れ子クラスは使えないことが判明したため)
- `Doc/test/UatVm/Scenarios/JodaiBulkExtractScenario.cs`を追加し`Program.cs`へ登録(`jodaibulkextract`)
### 技術決定 Why
- 数値項目は`CAST(@x AS INTEGER)`で比較し文字列比較による桁ズレ("9800">"12800")を避ける
- Jsubの枠は`MasterMeisho`の`Kubun='IDX'`かつ`Code IN('B01'..'B10')`に登録済みの行だけを検索項目にし、固定名でハードコードしない(`MasterTokuiMenteViewModel.DoGetKubun`に倣う)
- Jsubの`json_each`はNULL安全だが不正JSON文字列では例外になるため`json_valid()`ガードを併用(`MasterCascadeDb`と同じ考え方)
### 確認
- `dotnet build creativevision10.slnx`：0警告0エラー
- `Tests/TestServer/TestServer.exe`：898件成功
- `Tests/TestSqlDialect/TestSqlDialect.exe`：141件成功
- `Doc/test/UatVm/.../UatVm.exe jodaibulkextract --manage-server`：23件成功(PASS)

---

## [2026-09-08] 店舗イベントを使う日別予算配分と店舗予算票
### Agent
- GPT-5.6 Terra : OpenAI : Codex
- GPT-5.6 Luna : OpenAI : Codex
- GPT-5.6 Sol : OpenAI : Codex
### Editor
- Codex
### 目的
- 店舗イベントを日別予算配分の判断材料とし、店舗予算票で予実変動の理由を確認可能にする
### 実施内容
- 店ブランド予算マスタ（月一括）へイベント名・重要度・イベント係数・有効係数を表示し、低1.1／中1.3／高1.5を曜日係数へ乗算して自動配分
- 休業日はイベント有無にかかわらず係数0を維持し、保存時の手修正・洗替え・丸め差表示は変更しない
- 店舗予算票の店舗別PDFへイベント名（重要度）を追加し、全店出力は空欄を維持
### 技術決定 Why
- イベント係数は配分案の再計算時だけに使い、確定済み予算へ根拠を永続化しないことで既存のMasterYosanBrand契約を維持する
- 帳票は既存の空白領域とitem27を使い、既存の金額列・全店集計を変更しない
### 確認
- CvWpfclient build：警告0、エラー0
- QFM：cp932、XML整形式、item27参照を確認
- QFM実PDF描画：ローカルハーネスの配置不整合により未実施

---

## [2026-09-08] システム管理マスタ選択項目の整数バインド修正
### Agent
- GPT-5.6 Terra : OpenAI : Codex
- GPT-5.6 Sol : OpenAI : Codex
### Editor
- Codex
### 目的
- 後追加設定の選択値を表示・保存可能にする
### Agent(修正)
- Claude Opus-5 : Anthropic : Claude Code
### 実施内容
- TaxRounding/CostMethod を ItemsSource + DisplayMemberPath/SelectedValuePath 方式へ変更（VMに int 値リスト TaxRoundingItems/CostMethodItems を追加）
### 技術決定 Why
- ComboBoxItem の Tag は string/enum になり int プロパティと Equals 一致せず選択が復元されない。SelectedIndex は項目未生成時に -1 を書き戻す危険がある。得意先メンテの入金月(PayMonthItems)と同じ、値の型が一致する方式に統一
### 確認
- ビルド成功、画面確認待ち

---

## [2026-09-08] システム管理マスタの後追加項目表示
### Agent
- GPT-5.6 Terra : OpenAI : Codex
- GPT-5.6 Luna : OpenAI : Codex
### Editor
- Codex
### 目的
- システム管理マスタで後追加した税設定と原価方式を編集可能にする
### 実施内容
- `TaxRounding` と `CostMethod` の選択欄を既存フォームへ追加
### 技術決定 Why
- 既存の `Current` 保存経路と列挙型を直接使い、ViewModel・DBを変更しない
### 確認
- XAML XML、行定義、`git diff --check`：問題なし
- 画面確認：ユーザー確認済み

---

## [2026-09-08] 14:30 自動実行履歴の実行種別表示
### Agent
- GPT-5.6 Terra : OpenAI : Codex
- GPT-5.6 Luna : OpenAI : Codex
### Editor
- Codex
### 目的
- 自動実行履歴で実行種別を日本語表示する
### 実施内容
- 一覧と詳細へ実行種別を追加
- int値を `EmSysHistType` のCommentへ変換して表示
### 技術決定 Why
- DBモデルを変更せず、既存enumの表示定義を共通Converterから再利用する
### 確認
- XAML XML、CRLF、git diff --check：問題なし
- 画面確認：ユーザー確認済み

---
## [2026-09-08] 14:03 ストリーム処理の手動実行履歴
### Agent
- GPT-5.6 Terra : OpenAI : Codex
- GPT-5.6 Luna : OpenAI : Codex
### Editor
- Codex
### 目的
- 業務ストリームの開始・終了を `SysHistAutoexec` へ記録する
### 実施内容
- `Msg040`、`Msg050`〜`058`、`Msg082`/`085`/`087`/`089` を共通履歴ラッパー経由に変更
- 開始時は短期DBスコープで履歴を追加してIdだけを保持し、終了時は別スコープで更新
- 選択変換の終了履歴へ変換プログラム内部名を記録
### 技術決定 Why
- 既存の業務・排他履歴は維持し、ストリーム実行履歴を一元化する
### 確認
- CvServer build：警告0、エラー0
- git diff --check：問題なし

---
## [2026-09-07] 14:48 Tran未解決商品の補足マスタ生成
### Agent
- GPT-5.6 Sol : OpenAI : Codex
- GPT-5.6 Terra : OpenAI : Codex
### Editor
- Codex
### 目的
- Tran通常商品明細で未解決の `MasterShohin` を補足生成し、明細を保持したまま `Id_Shohin` を再設定する
### 実施内容
- 全通常Tran 10型の変換後に補足マスタ生成・明細再紐付けステップを追加
- 空・16文字超コードと `Tran02Material` の関連商品を生成対象外として保持
- JSON更新を全明細保持型へ変更し、サイズ補完の不一致明細脱落も修正
- 変換選択画面の表示名とSQLite自動テストを追加
### 技術決定 Why
- 補足マスタ追加とJSON再紐付けをSerializableトランザクションにまとめ、再実行時の重複作成を防ぐ
### 確認
- CvDomainLogic build：警告0、エラー0
- TestServer 対象テスト：2件成功
- TestSqlDialect：141件成功
- CvWpfclient build：警告0、エラー0
- git diff --check：問題なし

---
## [2026-09-05] 15:00 追加 skill 整理
### Agent
- GPT-5.6 Sol : OpenAI : Codex
- GPT-5.6 Terra : OpenAI : Codex
- GPT-5.6 Luna : OpenAI : Codex
### Editor
- Codex
### 目的
- 現行規約・ソース根拠に合わせ、廃止3 skillと存続skillの参照関係を整理する
### 実施内容
- 廃止対象 `check-xaml` / `create-print-view-from-crs` / `fix-scheduler-job-management-wpf` の存続skillからの参照を除去
- 更新skillへ App.xaml、DataGridAssist、UatVm、Scheduler契約、CRS/QFM列対応、commit user.name規約を反映
### 技術決定 Why
- 現行の `Doc/test/UatVm/README.md`、`CvWpfclient/App.xaml`、MasterShohinMenteView、ISchedulerServiceを根拠に、重複skillを増やさず共通guideへ統合した
### 確認
- 存続18 skillのfrontmatter/name、TODO、CRLFを手動確認
- 削除対象skill内部以外の参照をrg確認
- git diff --check：問題なし
- quick_validate.py：同梱Pythonで実行したがPyYAML不足のため未実行、手動検証で代替

---
## [2026-09-04] 20:42 POS専用gRPC契約と公開経路の削除
### Agent
- GPT-5.6 Terra : OpenAI
### Editor
- Codex
### 目的
- ユーザーからの要望：cvpos10の共通メッセージ経路化に伴い、cv10の不要なPOS専用gRPC定義を削除する
### 実施内容
- CodeShare/PosContracts.cs: POS DTOを専用I/Fから分離し、IPointOfSaleServiceを削除
- CvServer/Program.cs: PointOfSaleServiceの専用gRPC公開を削除
- CvServer/Services/PointOfSaleService.cs: 専用エンドポイント向け属性と契約実装を削除し、共通経路の内部業務処理として保持
### 技術決定 Why
- POSの売上・取消・精算ロジックとDTOはCoreServiceのCvMsg経路で引き続き必要なため保持し、直接利用されなくなった専用契約と公開経路だけを削除する
### 確認
- creativevision10.slnx のビルド成功
- TestServer の PointOfSaleServiceTests：15件成功
- cvpos10.slnx：警告 0、エラー 0
- git diff --check：問題なし

---
## [2026-09-07] 店舗予算表の予算なし店舗・客数集計対応
### Agent
- GPT-5.6 Terra : OpenAI : Codex
- GPT-5.6 Luna : OpenAI : Codex
- GPT-5.6 Sol : OpenAI : Codex
### Editor
- Codex
### 目的
- 当月に予算がなくても売上伝票がある店舗を店舗予算表へ出力し、客数を伝票数として表示する
### 実施内容
- 出力店舗を当月予算または売上・返品伝票のある店舗へ変更
- 社販売上列をSQLから削除して以降を1列前詰め
- 客数へ売上・返品伝票ヘッダ数を設定
### 技術決定 Why
- `Tran01Tenuri` は伝票ヘッダ単位のため、売上・返品伝票を各1客として数える
### 確認
- SQLの店舗別・全店別SELECTが各26列、社販売上列なしを確認
- QFMはcp932でXML整形式、最大参照item26、item27参照なしを確認
- `git diff --check`：問題なし
- CvWpfclient build（--no-restore）：警告0、エラー0

---
