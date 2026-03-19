# OpenCode 利用ガイド

## OpenCodeとは

OpenCodeは、マルチファイル編集・大規模リファクタリング・ドキュメント作成に特化したAIコーディングアシスタントです。
GitHub Copilotのインライン補完と組み合わせることで、開発効率を最大化できます。

---

## 起動方法

### CLI経由
```bash
# OpenCodeのインタラクティブモードで起動
opencode

# もしくは短縮形
oc
```

### VS Code / Cursor経由
- OpenCode Extensionをインストール済みの場合、エディタ内からコマンドパレット経由で起動可能

---

## Cvnet10プロジェクトでの基本的な使い方

### 1. 新規画面（View/ViewModel）の作成

**プロンプト例：**
```
doc/instruction_20260317_10_vmcreate.md の指示に従って、
02Yosan フォルダに「予算入力View」を作成してください。
MasterShohinMenteView.xaml を参考にしてください。
```

**OpenCodeが自動で実行すること：**
- 指示書の内容を理解
- 参考ファイルの構造を解析
- `02Yosan/YosanInputView.xaml` および `.xaml.cs` を作成
- `02Yosan/YosanInputViewModel.cs` を作成
- `MenuData.cs` に新しいメニューエントリを追加
- Build確認
- `doc/aicording_log.md` に作業履歴を記録

---

### 2. 既存コードのリファクタリング

**プロンプト例：**
```
Cvnet10Wpfclient/ViewModels/01Master/ 配下の全ViewModelで、
重複している初期化ロジックを BaseMenteViewModel に移動してください。
影響範囲をdoc/に記録してください。
```

**OpenCodeの強み：**
- 複数ファイルを同時に編集可能
- 共通パターンを自動検出して抽出
- 変更前後の影響範囲を明確にドキュメント化
- Layer構造を理解して適切な配置を提案

---

### 3. ドキュメント作成・更新

**プロンプト例：**
```
Cvnet10DomainLogic プロジェクトの主要クラスについて、
アーキテクチャ設計書を doc/ に作成してください。
クラス図とシーケンス図をMermaid形式で含めてください。
```

**OpenCodeができること：**
- コードベース全体を俯瞰して構造を理解
- 依存関係を自動解析
- Markdown + Mermaid形式で見やすいドキュメント生成
- 既存ドキュメントとの整合性を保持

---

### 4. トラブルシューティング

**プロンプト例：**
```
dotnet build でエラーが出ています。
エラーログを解析して、原因と修正方法を提案してください。

[エラーログをペースト]
```

**OpenCodeのアプローチ：**
- エラーメッセージから原因箇所を特定
- 関連ファイルを自動的に調査
- 複数の修正案を提示
- 修正後にBuild確認まで実施

---

## OpenCodeの特徴的な機能

### TodoWrite Tool（タスク管理）

OpenCodeは複数ステップのタスクを自動的に計画・追跡します。

```
ユーザー：「受注入力画面を新規作成してください」

OpenCode：
[TodoWrite] 以下のタスクを作成しました：
1. 既存の入力画面構造を調査 [in_progress]
2. 受注入力View/ViewModelを作成 [pending]
3. MenuDataに接続 [pending]
4. Build確認 [pending]
5. aicording_logに記録 [pending]

現在、タスク1を実行中です...
```

### Task Tool（コードベース探索）

大規模な探索タスクには専用のExploreエージェントを起動します。

```
ユーザー：「gRPCサービスの実装パターンを調査してください」

OpenCode：
[Task: explore] コードベース探索を開始します...
- Cvnet10Server/Services/ 配下を検索
- ICvnetCore の実装を解析
- パターンをドキュメント化
```

---

## プロンプトのベストプラクティス

### ✅ 良いプロンプト例

**具体的な参照を含む：**
```
MasterTokuiMenteView.xaml と同じ構成で、
MasterShiireMenteView.xaml を作成してください。
タブ構成は「基本情報」「支払情報」「詳細」の3つにしてください。
```

**明確な制約を指定：**
```
Cvnet10Base プロジェクトは Read-Only なので編集せず、
Cvnet10DomainLogic 側に新しいロジックを追加してください。
```

**作業範囲を明示：**
```
ViewModels/06Uriage/ フォルダ配下の全ファイルで、
ObservableProperty を使用するように統一してください。
```

### ❌ 避けるべきプロンプト例

**曖昧な指示：**
```
画面を作って  # どの画面？どのフォルダ？参考は？
```

**範囲が不明確：**
```
全部リファクタリングして  # 何をどこまで？基準は？
```

**禁止事項を無視：**
```
CodeShare プロジェクトに新しいメソッドを追加して  # Read-Onlyプロジェクト！
```

---

## よくある質問（FAQ）

### Q1: OpenCodeとGitHub Copilotの使い分けは？

**A:** 以下の基準で判断してください：

| タスク | 推奨ツール |
|:---|:---|
| メソッド内のコード補完 | GitHub Copilot |
| 複数ファイルの新規作成 | OpenCode |
| 5ファイル以上のリファクタリング | OpenCode |
| クイックフィックス | GitHub Copilot |
| ドキュメント作成 | OpenCode |
| XAML バインディング補完 | GitHub Copilot |

詳細は `doc/ai_tool_selection_guide.md` を参照してください。

---

### Q2: OpenCodeが作業履歴を記録しない場合は？

**A:** OpenCodeは通常、作業完了時に自動的に `doc/aicording_log.md` へ記録します。
記録されない場合は、以下を確認してください：

- `AGENTS.md` の "After-Completion" セクションが正しく記載されているか
- OpenCodeが最後まで正常に完了したか（エラー中断していないか）

手動で記録する場合は、以下のフォーマットを使用してください：

```markdown
## [2026-03-19] 10:30 作業タイトル
### Agent
- [claude-sonnet-4.5 : GitHub-Copilot]
### Editor
- [OpenCode]
### 目的
- ユーザーからの要望：[内容]
### 実施内容
- [ファイルパス]: [変更内容]
### 技術決定 Why
- [理由]
### 確認
- Build OK
```

---

### Q3: Read-Onlyプロジェクトを編集したい場合は？

**A:** 以下のプロジェクトは原則として編集禁止です：

- `CodeShare`
- `Cvnet10Asset`
- `Cvnet10Base`
- `Cvnet10BaseMariadb` / `Cvnet10BaseOracle` / `Cvnet10BaseSqlite`

どうしても編集が必要な場合は、プロンプトで **明示的に許可を指定** してください：

```
[重要] CodeShare プロジェクトの編集を許可します。
ICvnetCore インターフェースに新しいメソッドを追加してください。
```

---

### Q4: Build エラーが出た場合は？

**A:** OpenCodeは作業完了時に自動的にBuildを実行します。エラーが出た場合：

1. **エラーログをOpenCodeに渡す：**
   ```
   Build でエラーが出ました。以下のログを解析して修正してください：
   [エラーログ]
   ```

2. **手動で確認する場合：**
   ```bash
   # プロジェクト全体
   dotnet build "Cvnet10.slnx"
   
   # WPF Client (WSL2/Linux環境)
   dotnet build "Cvnet10Wpfclient/Cvnet10Wpfclient.csproj" \
     /p:EnableWindowsTargeting=true /p:UseAppHost=false
   ```

---

### Q5: 大規模な変更を行う前に計画だけ確認したい

**A:** プロンプトで「計画のみ」を依頼してください：

```
以下の作業を行う前に、まず計画を提示してください。
実装は計画承認後に進めてください。

[作業内容]
```

OpenCodeは TodoWrite で計画を提示し、承認後に実装を開始します。

---

## トラブルシューティング

### OpenCodeが起動しない

**原因：** 環境変数またはインストールの問題

**対処法：**
```bash
# OpenCodeのバージョン確認
opencode --version

# 再インストール（必要に応じて）
npm install -g @opencode/cli
```

---

### 生成されたコードがCRLFではなくLFになる

**原因：** Git の autocrlf 設定または OpenCode のデフォルト設定

**対処法：**
```bash
# .gitattributes で強制
echo "*.cs text eol=crlf" >> .gitattributes
echo "*.xaml text eol=crlf" >> .gitattributes

# または dotnet format で修正
dotnet format "Cvnet10.slnx"
```

---

### 作業途中でOpenCodeが停止した

**原因：** タイムアウトまたはトークン上限

**対処法：**
1. 前回の作業内容を確認：
   ```
   前回の作業の続きから再開してください。
   既に完了しているタスクは [タスク名] です。
   ```

2. タスクを小分けにする：
   ```
   まず 01Master フォルダのみリファクタリングしてください。
   完了後、02Yosan 以降を別途依頼します。
   ```

---

## 参考資料

- **OpenCode公式ドキュメント:** https://opencode.ai/docs
- **GitHub Copilot設定:** `.github/copilot-instructions.md`
- **AI使い分けガイド:** `doc/ai_tool_selection_guide.md`
- **プロンプトライブラリ:** `doc/opencode_prompts.md`
- **作業履歴:** `doc/aicording_log.md`

---

## まとめ

OpenCodeは **複数ファイルにまたがる作業** と **計画的な開発** に最適なツールです。

✅ **OpenCodeを使うべき場面：**
- 新規画面の一括作成
- 大規模リファクタリング
- ドキュメント作成・更新
- トラブルシューティング

✅ **GitHub Copilotを使うべき場面：**
- コード補完
- クイックフィックス
- インラインサジェスト

両者を適切に使い分けて、開発効率を最大化しましょう！
