---
name: verify-wpf-screen-runtime
description: Runs CvServer and CvWpfclient to verify an actual WPF screen visually after edits. Use when the user asks to launch the app, confirm a CvWpfclient View on screen, capture a screenshot, or check UI behavior in a running WPF window. Prefers ViewModel-owned state setup and command execution such as MainMenuViewModel SelectedMenu plus DoMenuCommand over key or mouse emulation, and avoids Japanese literals in temporary scripts.
---

# Verify WPF Screen Runtime

このスキルは、`CvServer` と `CvWpfclient` を実行して、実画面で WPF 画面を確認するための手順です。通常の WPF 作業規約は `wpf-project-guide`、画面改修手順は `wpf-view-workflow` を先に使います。

## 原則

- UI の確認は、可能な限り ViewModel 側に状態をセットして行う。
- メニュー起動は `MainMenuViewModel.SelectedMenu` をセットし、`DoMenuCommand.Execute(null)` を呼ぶ経路を優先する。
- View への `SendKeys`、マウス座標クリック、Enter エミュレーションは最後の手段にする。
- PowerShell などの一時スクリプトには日本語文字列を入れない。画面名は View 型名、プロセス名、英数字の環境変数で扱う。
- 一時フックは環境変数でだけ動かし、確認後に必ず削除する。
- 確認用ファイル、スクリーンショット、起動スクリプトは `.tmp_ui_check/` や `.omo/` などに置き、通常は commit 対象にしない。

## 推奨フロー

1. 対象 View / ViewModel / `MenuData.cs` の起動導線を確認する。
2. `CvWpfclient` をビルドする。
3. 必要なら ViewModel に一時確認フックを追加する。
4. `CvServer` を起動する。
5. `CvWpfclient` を起動し、ViewModel 経由で対象画面を開く。
6. スクリーンショットを取り、目視確認する。
7. 一時フック、一時スクリプト、一時画像を削除する。
8. 最終状態で `git diff --check` と対象ビルドを再実行する。

## ViewModel 経由でメニューを開く

`MainMenuViewModel.Init()` の最後に、一時的に次のようなフックを入れる。環境変数名と値は ASCII のみにする。

```csharp
void StartAutomationMenuOpenIfRequested() {
    var menuKey = Environment.GetEnvironmentVariable("CV10_AUTOMATION_OPEN_MENU");
    if (string.IsNullOrWhiteSpace(menuKey)) return;

    Application.Current.Dispatcher.BeginInvoke(async () => {
        await Task.Delay(500);
        var menu = FindMenu(MenuItems, menuKey);
        if (menu == null) return;
        SelectedMenu = menu;
        if (DoMenuCommand.CanExecute(null)) DoMenuCommand.Execute(null);
    });
}

private static MenuData? FindMenu(IEnumerable<MenuData> nodes, string key) {
    foreach (var node in nodes) {
        if (string.Equals(node.ViewType.Name, key, StringComparison.Ordinal)
            || string.Equals(node.Header, key, StringComparison.Ordinal)) return node;
        if (node.SubItems == null) continue;
        var found = FindMenu(node.SubItems, key);
        if (found != null) return found;
    }
    return null;
}
```

呼び出し側の一時スクリプトでは日本語ヘッダーではなく View 型名を渡す。

```powershell
$env:CV10_AUTOMATION_OPEN_MENU = 'StockInputView'
$client = Start-Process -FilePath $clientExe -PassThru
$env:CV10_AUTOMATION_OPEN_MENU = $null
```

## 画面初期状態を作る

一覧条件ダイアログ、選択ダイアログ、検索結果などが邪魔になる場合、ViewModel の `Init` や専用初期化メソッドに一時分岐を入れて対象状態を作る。

```csharp
[RelayCommand]
async Task Init() {
    if (Environment.GetEnvironmentVariable("CV10_AUTOMATION_TARGET_STATE") == "DetailWithRow") {
        SelectedTabIndex = 1;
        AddMeisai();
        return;
    }

    await DoList(CancellationToken.None);
}
```

この分岐は確認後に削除する。永続化する場合は、確認用ではなく製品仕様として名前と導線を設計し直す。

## 一時スクリプトの注意

- 日本語タイトルで `Wait-WindowByTitle` しない。文字化けやクォート崩れで PowerShell parse error になりやすい。
- 対象ウィンドウは、`CreativeVision10` プロセスのうちメインタイトル以外、またはプロセス ID とハンドルで拾う。
- スクリーンショット名、環境変数名、状態名は ASCII にする。
- `Start-Process -WindowStyle Hidden` は `CvServer` など非対話プロセスだけに使う。`CvWpfclient` は通常ウィンドウで起動する。
- GUI 起動、スクリーンショット取得、プロセス停止は権限付き実行が必要になる場合がある。

## 検証コマンド

```powershell
cmd /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient\CvWpfclient.csproj"
git diff --check -- <changed-files>
```

`Access to the path 'C:\Users\sekiya\AppData\Local\Microsoft SDKs' is denied` が出た場合は、同じ build を権限付きで再実行する。AGENTS.md でこの SDK 参照は許可されている。

## 後片付け

- 確認用に ViewModel へ入れた環境変数分岐を削除する。
- 確認用に作った `.ps1` やスクリーンショットを削除する。
- `Get-Process -Name CreativeVision10,CvServer -ErrorAction SilentlyContinue` で残プロセスを確認し、確認用に起動したものは停止する。
- 触った `.cs` / `.xaml` / `.md` は UTF-8 CRLF に戻す。
- `git status --short` で、製品変更・skill・ログだけが残っていることを確認する。

## Example: StockInputView P/S UI check

今回の `CvWpfclient.Views._08Zaiko.StockInputView` 確認では、キー操作エミュレーションで `TreeView` の `Enter` やダブルクリックを送る方法は安定しなかった。成功した手順は次の通り。

1. `MainMenuViewModel.Init()` の最後に、環境変数 `CV10_AUTOMATION_OPEN_MENU` を見て `MenuItems` から対象を探す一時フックを追加した。
2. 検索キーは日本語の `棚卸入力` ではなく、View 型名 `StockInputView` を使った。
3. 対象 `MenuData` を `SelectedMenu` にセットし、`DoMenuCommand.Execute(null)` で通常のメニュー起動ロジックを直接呼んだ。
4. `StockInputViewModel.Init()` に `CV10_AUTOMATION_TARGET_STATE=DetailWithRow` の一時分岐を入れ、一覧条件ダイアログを避けて詳細タブへ移動し、明細行を1行作った。
5. `CvServer` と `CvWpfclient` を起動し、対象ウィンドウのスクリーンショットで明細先頭が `行No` になり、P/S コンボが表示されないことを確認した。
6. 確認後、`MainMenuViewModel` と `StockInputViewModel` の一時フック、一時 PowerShell、一時スクリーンショットを削除した。
7. 最終状態で `cmd /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient\CvWpfclient.csproj"` と `git diff --check` を実行した。

失敗からの学び:

- PowerShell スクリプトに日本語リテラルを入れると、文字化けやクォート崩れで parse error になりやすい。
- WPF の `TreeView` は UI Automation の選択や `SendKeys` だけでは、実際の `SelectedMenu` / `DoMenuCommand` 経路に届かないことがある。
- 画面状態は ViewModel に作らせる方が、座標クリックやキー入力より再現性が高い。
