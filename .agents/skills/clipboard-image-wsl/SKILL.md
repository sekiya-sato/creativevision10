---
name: clipboard-image-wsl
description: Save a Windows clipboard image as a PNG in WSL using clipimg and inspect it from an agent that can read the saved image.
---

# Clipboard Image WSL

Windows のクリップボード画像を WSL の `clipimg` で保存し、保存先を画像閲覧機能に渡す。

## 前提

- Windows 側のクリップボードに画像がある。
- WSL の `~/bin/clipimg` が実行可能で、成功時に保存先の WSL パスを出力する。

## 手順

1. WSL で `clipimg` を実行し、出力されたパスのファイルを画像閲覧機能で開く。Windows 側のツールを使う場合は `wslpath -w <WSLパス>` で変換する。
2. 保存済み画像を再利用する場合は `clipimg --last`、ユーザーが目視する場合は `clipimg --open` を使う。
3. 画像がない場合はコピーし直してもらう。コマンドが見つからない場合は `~/bin/clipimg` と `PATH` を確認する。

パスをプロンプトに渡す場合も、エージェントが画像自体を読める手段を使う。単にパス文字列を読んだだけで画像を確認したことにはしない。
