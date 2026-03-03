# [!IMPORTANT] This file is NOT Instractions ** IGNORE This File! **

- このファイルは人間用のメモ。


# プロジェクトの運用および設定に関するメモ

## Nginx リバースプロキシ設定例

- /etc/nginx/sites-available/default などで HTTPSポート1つのみで複数のgRPCサービスを振り分ける例

```
server {
    listen 443 ssl http2;
    # ... SSL設定はそのまま ...
    # 1. デフォルト（DB1：5001番ポートへ）
    location / {
        grpc_set_header Content-Type application/grpc;
        grpc_pass grpc://localhost:5001;
    }
    # 2. 第二のDB（DB2：5002番ポートへ）
    # クライアント側でベースアドレスを "https://domain/cv10second" と指定する場合
    location /cv10second/ {
        # 先頭の /cv10second を削ってバックエンドに渡す
        rewrite ^/cv10second/(.*) /$1 break;
        grpc_set_header Content-Type application/grpc;
        grpc_pass grpc://localhost:5002;
    }
}

```


# AIに関するメモ


## AI関係の豆知識

LLMの要素: パラメーター数 入力トークン数 次元数
Promptの必要要素: ロール、キャラクター、タスク、制約ルール、出力フォーマット、例を示す、step by step、
Prompt: 不明点や選択肢がある場合は、独断で進めず askQuestions を使って私に確認してください。
Prompt原則: 曖昧な指示は避け、具体的に、どんな専門家として回答すべきか、手順（Chain-of-Thought）を促す、Few-shot（例示）、出力フォーマットを指定する
構造化プロンプト（Structured Prompting）
Mermaid で視覚的コンテキストを強化する: テーブル間の関係を表すMermaidのER図（erDiagram）を生成し docs/schema.mermaid に保存 
AIによるplaywrightを使ったテスト?: npm init playwright@latest
Copilot使用量確認: http://github.com/settings/billing/usage?period=3&group=0&customer=13353310
メタプロンプト：「あなたは優秀なエンジニア。今の内容をmarkdown記号を使ってAIが理解しやすいよう整理・構造化し、最高のプロンプトに書き換えてください」
but 最新のプロンプトエンジニアリングでは、ペルソナを設定しないほうが良い(Roleはもはや不要)、目的や制約を入力する
マルチターンでは殆どの場合、回答精度が落ちる。AIが最初の不完全な前提条件に引っ張られてしまう。会話をまとめて新しいsessionで始める。


## AI用のタグおよび指示語のメモ

<rules> ... </rules>: 絶対に守ってほしい行動指針。
<context> ... </context>: プロジェクトの背景や前提。
<example> ... </example>: 理想的な出力例（Few-shot）。
<glossary> ... </glossary>: アパレル業界用語など、独自の定義。
<project_info> ... </project_info>: プロジェクトの概要
<coding_conventions> ... </coding_conventions>: コード作成ルールなど - (タイトル): 内容 ...
太字 (**...**): 重要な制約事項に使用します。
警告ブロック (> [!IMPORTANT])
コードスパン (` `): クラス名やメソッド名
NOTE: / WARNING: / TODO:: 文章の冒頭に置くことで、情報の種類を明示します。
"NEVER" / "ALWAYS"	「絶対に〜するな」「常に〜せよ」という強い制約をかけます。
"Strictly adhere to..."	「〜を厳密に遵守せよ」と、規約の優先度を上げます。
"Think step-by-step"	推論の質を上げ、論理的なコード生成を促します。
"If ambiguous, ask questions"	独断で進めず確認するよう促します。
Prompt:  # 見出し - 箇条書き *..* 強調ポイント > 引用 ``` コードブロック、データ形式
コパイロットソリューション指示ファイル: .github\copilot-instructions.md
プロンプトファイル: .github\prompts\プロンプト名.prompt.md で作成、/プロンプト名 で呼び出す
GitHub Actions のワークフロー定義ファイル(.yml): .github\workflows\ワークフロー名.yml



## Copilot スラッシュコマンド

   セッション管理

     - /clear, /new - 会話履歴をクリア
     - /resume [sessionId] - 別のセッションに切り替え
     - /session - セッション情報とワークスペース概要を表示
     - /rename <name> - 現在のセッションの名前を変更
     - /exit, /quit - CLIを終了

   コンテキスト管理

     - /context - コンテキストウィンドウのトークン使用状況を表示
     - /compact - 会話履歴を要約してコンテキストウィンドウを削減
     - /cwd, /cd [directory] - 作業ディレクトリを変更または表示

   ファイルアクセス

     - /add-dir <directory> - ファイルアクセス用のディレクトリを許可リストに追加
     - /list-dirs - 許可されたすべてのディレクトリを表示
     - /diff - 現在のディレクトリの変更を確認

   AI設定

     - /model, /models [model] - 使用するAIモデルを選択（Claude Sonnet
   4.5、GPT-5など）
     - /agent - 利用可能なエージェントを参照・選択

   計画とレビュー

     - /plan [prompt] - コーディング前に実装計画を作成（プランモード）
     - /review [prompt] - コードレビューエージェントを実行して変更を分析

   GitHub連携

     - /delegate [prompt] - リモートリポジトリにAI生成PRで変更を委任
     - /share [file|gist] [path] - セッションをMarkdownファイルまたはGitHub Gistに共有

   認証

     - /login - Copilotにログイン
     - /logout - Copilotからログアウト
     - /user [show|list|switch] - GitHubユーザーリストを管理

   拡張機能

     - /mcp [show|add|edit|delete|disable|enable] [server-name] -
   MCPサーバー設定を管理

     - /skills [list|info|add|remove|reload] [args...] - スキル管理（機能拡張）
     - /ide - IDEワークスペースに接続

   その他

     - /help - インタラクティブコマンドのヘルプを表示
     - /feedback - CLIについてのフィードバックを送信
     - /usage - セッションの使用状況メトリクスと統計を表示
     - /theme [show|set|list] [auto|dark|light] - ターミナルテーマを設定
     - /terminal-setup - 複数行入力サポート（Shift+Enter、Ctrl+Enter）を設定
     - /reset-allowed-tools - 許可されたツールのリストをリセット

   設定ファイル

   1. メイン設定ファイル

     C:\Users\(ユーザ名)\.copilot\config.json

     - バナー表示設定
     - ログインユーザー情報
     - テーマ設定
     - 信頼されたフォルダー
     - 許可されたURL

   2. グローバルインストラクション

     C:\Users\(ユーザ名)\.copilot\copilot-instructions.md


   3. コマンド履歴

     C:\Users\(ユーザ名)\.copilot\command-history-state.json

     - 過去のコマンド履歴

   4. MCP サーバー設定（予想される場所）

     C:\Users\(ユーザ名)\.copilot\mcp-servers.json

   または

     C:\Users\(ユーザ名)\.copilot\mcp\config.json

   5. セッション状態（セッションごと）

     C:\Users\(ユーザ名)\.copilot\session-state\{session-id}\

   6. ログ

     C:\Users\(ユーザ名)\.copilot\logs\


   環境変数での設定:

     - COPILOT_CUSTOM_INSTRUCTIONS_DIRS - 追加のインストラクションディレクトリ
     - GH_TOKEN または GITHUB_TOKEN - 認証トークン

   これらの設定ファイルは、特定のセッションやワークスペースに依存せず、Copilot CLI 全体に適用されます。

## 参考にするURLなど

    - GitHub Copilot CLI ドキュメント: https://docs.github.com/copilot/cli
    - https://github.com/RyoMurakami1983/skills_repository?tab=readme-ov-file


 
# 進捗状況と今後の予定


## 開発履歴

  - Cvnet10プロジェクト AI駆動開発 以前
2023/11 .NET8 release
2023/11 CV.net 8 : mariaDB + Asp.net core + gRPC + nginx + WPF(MVVM) で開発開始
2024/08 テスト用外部サーバ Ubuntu 24.04 LTS を立てる、mariaDB: "10.6.18-MariaDB-0ubuntu0.22.04.1"
2024/08 DBテーブル構造の試行錯誤 mariaDB, PostgreSQL,Sqlite,JSON関数 の検証
2024/11 .NET9 release VS へのAIチャット機能の統合
2025/10 ClientのみのWPF作成プロジェクト(既存Cvnetサーバ)
2025/11 .NET10 release C# 14 , Visual Studio 2026 AIネイティブIDE GitHub Copilotとの統合, 
2025/11 CV.net 10 : Sqlite + Asp.net core + gRPC + nginx + WPF(MVVM) で再設計
2026/01 本格的なAI駆動開発体制へ移行, Cvnet8, CvnetClinet リポジトリの技術吸収

- 2026/01/29
	- Cvnet10Server, Cvnet10Wpfclient の基本的な gRPC 通信と認証ロジックが完了。
- 2026/01/30
	- 概念検証として、ITest202601 , Test202601Service を追加し、WpfClientから Joinを含んだビューを一覧取得
- 2026/02/01-02/04
	- Cvnet10Base を Cvnet10Base, Cvnet10DomainLogic に分割、データ定義とビジネスロジックを分離して実装
	- gRPCサービスにCRUDの汎用基本操作を追加
	- Cvnet10Wpfclient のメインメニューを修正
- 2026/02/05
	- 全体のコード整理
	- GPT-5 mini によるコードレビューと改善提案(全体設計方針とあわせ、実施を検討)
		- 却下: 指摘: ViewとViewModelの DIコンテナ利用 ViewModel の初期化戦略 == 理由: App.xaml.cs でのViewModelのDIが多すぎ、かつ1回しか使わない,デザイン画面が不便になる
		- 後日検討: gRPC と JWT/TLS 設定のベストプラクティスを Cvnet10Server にドキュメント化＆テンプレート追加。
		- 後日検討: 大量データ転送は gRPC ストリーミングで実装し、ページング/バッチング戦略を定義。
		- 後日検討: テスト戦略(単体/統合/契約テストの戦略) 実施例: GitHub Actions でテスト実行とカバレッジ報告。
		- 後日検討: 実施例: Cvnet10Base にマイグレーション定義 + マイグレーション実行コードを追加。
- 2026/02/06-02/11
	- ショートカットメモ:  ctl+E,D ドキュメントフォーマット  ctl+E,F 選択範囲フォーマット ctl+R,G Usingの整理
	- プロンプトメモ:
	- 現在ファイルのみ、 string? [項目名]; のすべてに対し、 string [項目名] =string.Empty; さらに属性 [property: System.ComponentModel.DefaultValue("")] をつける。また、string 型で初期値が="19010101"; の場合、属性 [property: System.ComponentModel.DefaultValue("19010101")] をつける。
	- 1) ソースコードを説明し、具体的な使用例を見せて 2) upgrade assesment for .NET 10 / C# 14 for only this sourcecode. 3) 全て説明は日本語で。4) 出力はマークダウン形式。
- 2026/02/12-02/18
	- クライアント・サーバ間のCRUDオペレーションの整理、Tran系テーブル作成、Cvnet8とCvnetClientのクライアント画面を除くモジュールの統合
	- サーバ側：PrintロジックはIKVMパッケージ、FtpはFluentFTP、スケジューラはNCrontab.Scheduler.AspNetCoreを使う。
	- Geminiのgemプロンプトの整理、Copilotのmdファイルの整理
	- Cvnet10Base: テーブル整理
- 2026/02/23-03/02
	- .editorconfigの追加、xmstylerの修正、コード整理、設定Option(テキストエディタ - コードのクリーンアップ Profile1)
    - マスターテーブル、トランザクションテーブルの作成

# ToDo 残作業 VSタスク一覧(TODO: 実装予定の処理 // HACK:問題がある可能性のあるコード、バグ回避のため仕方なくした実装など // UNDONE:未完了の処理 )

namespace Cvnet10Base;
 ToDo: 必要となる予定のテーブル
 設定テーブル (システム設定、店舗設定、税率設定、支払条件設定)
 ログテーブル (操作ログ、エラーログ、アクセスログ)
 マスターテーブル (商品マスタ、得意先マスタ、仕入先マスタ、社員マスタ、権限マスタ)
 伝票テーブル (受注伝票、発注伝票、売上伝票、仕入伝票、在庫移動伝票)
 集計テーブル (売上集計、仕入集計、在庫集計、買掛、売掛、支払、請求、顧客ポイント)
(上の作業がある程度完了したら)Test用テーブルの削除

namespace Cvnet10DomainLogic;
 ToDo: 必要となる予定のロジック
在庫更新ロジック
買掛、売掛更新ロジック
支払、請求更新ロジック
外部連携ロジック (委託倉庫、他社POS、他社EC)
自動実行処理 (NCrontab.Scheduler.AspNetCore を利用)
検証終了(Windows上で): 印刷処理 ( <PackageReference Include="IKVM" )
	// PrintStream DLLの登録
	// IKVMの初期化コストを考慮し、インスタンス管理をDIに任せる App.xaml.cs などで登録
		builder.Services.AddScoped<IPrintService, PrintAdapter>();
	// 出力するgRPCサービスにIPrintServiceを注入(IDisposable)
	private readonly IPrintService _printService;
	public PrintController(ILogger<PrintController> logger, IPrintService printService, IWebHostEnvironment env) {
			_logger = logger;
			_printService = printService;
			_env = env;
		}
	// ubuntu上での必要ライブラリ
		sudo apt install -y libgdiplus fontconfig fonts-ipafont


namespace Cvnet10Server;
 ToDo: 必要となる予定のサービス
Cvnet10DomainLogic の各機能を呼び出すサービスを実装する
自動実行処理 (NCrontab.Scheduler.AspNetCore を利用)
(上の作業がある程度完了したら)Test用サービスの削除


namespace Cvnet10Wpfclient;
 ToDo: 必要となる予定の画面
メニューに登録する画面
- マスタ系
- 伝票入力系
- 帳票印刷系
- 各種設定系
- 更新実行系
(上の作業がある程度完了したら)Test用ViewおよびViewModelの削除
				- WPF で DI コンテナ（Microsoft.Extensions.Hosting + Generic Host）を導入し、ViewModel/サービスの注入とテスト容易性を向上。
				- UI リソースは既に分割済み (UIColors.xaml 等) だが、テーマ切替やダークモード対応を早期に設計する。
				- コマンド/非同期処理は CancellationToken を用いてキャンセル可能にする。
					- 実施例: App.xaml.cs を IHost 起動に切替え、MainWindow の DataContext を DI で解決。


# 検討事項 NOTE

- データベースのJSON項目の扱い: TEXT型 (現在)  SQLite 3.45.0 以降はバイナリJSON（JSONB）BLOB型も使用可 (2026/03/03)


| 比較項目 | TEXT型 (現状) | BLOB型 (JSONB) |
|---|---|---|
| 可読性 | ◎ 直接閲覧可能 | △ json() 関数が必要 |
| 検索速度 | △ 毎回パースが必要 | ◎ 高速（パース済み） |
| 保存速度 | ◎ 文字列コピーのみ | ○ jsonb() 変換が必要 |
| データ容量 | ○ 標準 | ◎ 効率的 |
| 開発しやすさ | ◎ マッピングが容易 | △ 型変換の考慮が必要 |

※ 現状のままTEXT型で運用し、速度、容量の問題が発生したらJSONBへの移行を検討する


- Ubuntuサーバ環境での印刷処理
  - IKVMを利用してJavaの印刷ライブラリを呼び出す方法はWindowsでは有効だが、Ubuntu環境ではlibgdiplusとフォントの追加が必要。フォントが正常に印刷できるかの検証が必要

- データベースの簡易操作ツールは GikoSqliteを使用する。必要があれば機能追加や画面の調整をする


# -------------

- 余白

