---

name: makeDocument

description: 作業ドキュメントを作成します

argument-hint: Doc/aicording_log.md に作業ログを記録

---

作業完了時には、必ず以下のフォーマットで doc/aicording_log.md の最後に履歴を記録すること：
'''
## [YYYY-MM-DD] 作業タイトル
### Agent
- [使用した AI Model 名 : AI Provider 名]
### Editor
- [使用したエディタ: 不明な場合は"VS2026", 例 "VS2026", "VSCode", "OpenCode", "GitHubCopilot-Cli"] 
### 目的
- ユーザーからの要望：[内容]
### 実施内容
- [プロジェクト名]/[ファイル名]: [変更内容の要約]
### 技術決定 Why
- [例: ProtobufのOrder欠番を避けるため、既存のFlag定義を維持しつつ新機能を追加した]
### 確認
- [Buildした結果を確認。クロスプラットフォームの場合はBuild Error がでる可能性があるので省略可]

---
'''
