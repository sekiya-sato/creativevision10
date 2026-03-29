---
name: clipboard-image-wsl
description: Guides opencode to save Windows clipboard images into WSL with `clipimg` and pass the resulting file path into prompts.
---

# Clipboard Image WSL

このスキルは、Windows 側のクリップボード画像を WSL2 上の `clipimg` で PNG ファイルとして保存し、その保存パスを `opencode` の入力へ安全に渡すための作業手順を定義します。

## いつ使うか

- ユーザーが「今コピーした画像を見て」「クリップボードの画像を説明して」と依頼したとき
- WSL2 上の `opencode` に、Windows 側でコピーした画像を渡したいとき
- ドラッグ&ドロップではなく、クリップボード経由で画像を取り込みたいとき
- 保存済みの直近画像を `opencode` に再利用したいとき

## 前提条件

- `~/bin/clipimg` が存在し、実行権限があること
- `clipimg` が成功時に WSL パスを 1 行だけ標準出力すること
- 画像は Windows 側のクリップボードに入っていること
- `opencode` はローカルファイルパスを含むテキスト入力を読めること

## このスキルの目的

- クリップボード画像を `~/clip_images` 配下の PNG として保存する
- `opencode` に画像パスを貼りやすい定型を使う
- 画像が無い場合の失敗パターンを統一して扱う
- 直近画像の再利用や画像確認のための補助コマンドを案内する

## 基本ワークフロー

1. Windows 側で対象画像をコピーする
2. WSL2 上で `clipimg` を実行して PNG 保存と WSL パス取得を行う
3. 返ってきたパスを含めて `opencode` に依頼文を渡す
4. 必要に応じて `clipimg --last` や `clipimg --open` を使う

## 推奨コマンド

### 1. その場で画像を保存して `opencode` に渡す

```bash
opencode "この画像を確認して: $(clipimg)"
```

### 2. 保存済みの直近画像を再利用する

```bash
opencode "この画像を確認して: $(clipimg --last)"
```

### 3. 先に画像を目視確認してから使う

```bash
clipimg --open
opencode "この画像を確認して: $(clipimg --last)"
```

### 4. 対話中の `opencode` に手で貼る

```text
この画像を確認して: /home/user2010/clip_images/clip_YYYYMMDD_HHMMSS.png
```

## エージェントの推奨ふるまい

- ユーザーが「クリップボードの画像を見て」と言ったら、まず `clipimg` 実行を優先する
- 成功したら、返却された WSL パスをそのまま参照して画像を読む
- 失敗したら、画像がクリップボードに入っていない可能性を短く案内する
- 同じ画像を再確認する場合は `clipimg --last` を使って再保存を避ける
- ユーザーが見た目確認もしたい場合は `clipimg --open` を案内する

## 想定プロンプト例

- `今クリップボードにコピーした画像を確認して説明して`
- `この画像の内容を教えて: $(clipimg)`
- `直前に保存した画像をもう一度見て: $(clipimg --last)`
- `この画像のテキストを読んで要約して: $(clipimg)`

## トラブルシュート

### `クリップボードに画像がありません` と出る

- Windows 側で画像をコピーし直す
- テキストではなく画像そのものがクリップボードに入っているか確認する
- 必要なら別ツールで画像を一度開き、再度コピーする

### `clipimg` が見つからない

- `~/bin/clipimg` が存在するか確認する
- `~/bin` が `PATH` に入っているか確認する
- フルパスで `~/bin/clipimg` を実行して確認する

### 画像保存後に Windows 側で開きたい

```bash
clipimg --open
```

### 直近画像だけ取得したい

```bash
clipimg --last
```

## このスキルで扱わないこと

- 画像そのものを `opencode` にバイナリアップロードする仕組みの提供
- 汎用の Windows 連携ツール群としての MCP サーバー実装
- `clipimg` 本体の詳細実装変更

## 将来拡張の方針

- クリップボード画像取得をツール呼び出しとして完全自動化したい場合は、後続作業で MCP 化を検討する
- 現段階では、軽量で保守しやすい `clipimg` + Skill の組み合わせを標準とする

## 更新履歴

- **v0.1.0 (2026-03-29)**: `clipimg` を使って Windows クリップボード画像を WSL2 の `opencode` へ渡す運用スキルを追加
