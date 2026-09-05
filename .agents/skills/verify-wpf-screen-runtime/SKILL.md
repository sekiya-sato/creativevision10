---
name: verify-wpf-screen-runtime
description: Runs CvServer and CvWpfclient to verify an actual WPF screen visually after edits. Use when the user asks to launch the app, confirm a CvWpfclient View on screen, capture a screenshot, or check UI behavior in a running WPF window. Prefers ViewModel-owned state setup and command execution such as MainMenuViewModel SelectedMenu plus DoMenuCommand over key or mouse emulation, and avoids Japanese literals in temporary scripts.
---

# Verify WPF Screen Runtime

このスキルは、まず `Doc/test/UatVm/README.md` に従い、実View/ViewModelを直接駆動して画面→サーバー→画面を確認する手順です。実画面の目視が受入条件の場合だけ、`CvServer` と `CvWpfclient` を起動します。通常の WPF 作業規約は `wpf-project-guide`、画面改修手順は `wpf-view-workflow` を先に使います。

## 原則

- UI の確認は、可能な限り ViewModel 側に状態をセットして行う。
- メニュー起動は `MainMenuViewModel.SelectedMenu` をセットし、`DoMenuCommand.Execute(null)` を呼ぶ経路を優先する。
- View への `SendKeys`、マウス座標クリック、Enter エミュレーションは最後の手段にする。
- PowerShell などの一時スクリプトには日本語文字列を入れない。画面名は View 型名、プロセス名、英数字の環境変数で扱う。
- 固定checkout、製品コードへの一時フック、環境依存の起動スクリプトを前提にしない。必要な画面経路はまず `Doc/test/UatVm` の `OpenView` / `RunAsync` で確認し、製品コードを変更するフックは最後の手段とする。
- 確認用ファイル、スクリーンショット、起動スクリプトは `.tmp_ui_check/` や `.omo/` などに置き、通常は commit 対象にしない。

## 推奨フロー

1. 対象 View / ViewModel / `MenuData.cs` の起動導線を確認する。
2. `Doc/test/UatVm/README.md` と対象シナリオを確認し、可能なら UatVm をビルド・実行する。
3. 実画面の目視が必要な場合だけ `CvWpfclient` をビルドし、`CvServer` とクライアントを起動する。
4. 目視確認後は起動したプロセスを停止し、生成物を作業用領域に限定する。
5. 最終状態で `git diff --check` と対象ビルドを再実行する。

## 実画面を使う場合

UatVmで確認できない表示上の受入条件がある場合だけ、現checkoutのビルド成果物を起動する。製品コードへの一時hookや固定パスの起動例は作らず、既存の起動導線と作業用スクリーンショットを使う。

## 検証コマンド

```powershell
cmd /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient\CvWpfclient.csproj"
git diff --check -- <changed-files>
```

`Access to the path 'C:\Users\sekiya\AppData\Local\Microsoft SDKs' is denied` が出た場合は、同じ build を権限付きで再実行する。AGENTS.md でこの SDK 参照は許可されている。

## 後片付け

- 製品コードへ変更を加えた場合は、確認後に必ず元へ戻す。
- 確認用に作った `.ps1` やスクリーンショットを削除する。
- `Get-Process -Name CreativeVision10,CvServer -ErrorAction SilentlyContinue` で残プロセスを確認し、確認用に起動したものは停止する。
- 触った `.cs` / `.xaml` / `.md` は UTF-8 CRLF に戻す。
- `git status --short` で、製品変更・skill・ログだけが残っていることを確認する。
