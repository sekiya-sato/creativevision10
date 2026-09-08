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
