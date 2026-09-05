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
