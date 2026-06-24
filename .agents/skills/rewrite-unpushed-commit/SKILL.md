---
name: rewrite-unpushed-commit
description: Safely rewrite local unpushed git commits in cv10, especially commit messages, Author/Committer names, and placeholder messages such as Codex作業分. Use when the user says a commit is not pushed and asks to fix a commit comment, 作業者名, user.name, author, committer, SHA-1 metadata, or push前commit rewrite.
---

# Rewrite Unpushed Commit

このスキルは `C:\gitroot\new2022\cv10` で、push 前のローカルコミットだけを対象に、コミットメッセージと Author / Committer を安全に直すための手順です。

## 原則

- まず `git status --short --branch` と `git log --oneline --decorate --reverse origin/master..HEAD` で未push範囲を確認する。
- 対象SHAが `origin/*` 側に含まれる、または push 済みの可能性がある場合は、履歴を書き換える前にユーザーへ確認する。
- 未コミット変更がある場合は、対象外変更を失わない。必要なら `git stash push -u -m "<作業名>"` を使うが、ユーザー変更を勝手に戻さない。
- コミット作成・書き換え時の作業者名は、repo 指示に従い `Sekiya Sato Codex` を使う。
- 対象コミットが `HEAD` でない場合は、対象コミットだけを作り直し、後続コミットを `rebase --onto` で載せ直す。
- `git reset --hard` や `git checkout -- <path>` は使わない。

## 事前確認

```powershell
git status --short --branch
git show --no-patch --pretty=fuller <target-sha>
git show --stat --oneline <target-sha>
git log --oneline --decorate --reverse origin/master..HEAD
git branch --contains <target-sha>
```

確認する内容:

- 対象SHAが現在ブランチの未push範囲にあるか。
- 対象が `HEAD` か、履歴途中のコミットか。
- 現在の Author / Committer / message。
- 差分内容から、コミットメッセージに入れる作業タイトルと要約。

## メッセージ作成

`AGENTS.md` の Commit-Format に合わせる。

```text
[作業内容]
[使用した AI Model 名 : AI Provider 名 : エージェント名]
作業時間 [開始時間] - [終了時間] : [作業時間]
[ユーザ指示の概略]
```

直前の実績から不明な時刻は、`Doc/aicoding_log.md` の該当ログ、対象コミット日時、作業内容から保守的に推定する。不明なら推定であることをユーザーへ明記する。

## 対象がHEADの場合

`--author` で Author を直し、`-c user.name` で Committer 名を直す。

```powershell
$msgPath = Join-Path $env:TEMP "codex-commit-message.txt"
[IO.File]::WriteAllText($msgPath, @"
[作業内容]
GPT-5 : OpenAI : Codex
作業時間 hh:mm - hh:mm : n分
[ユーザ指示の概略]
"@, [Text.UTF8Encoding]::new($false))

git -c user.name="Sekiya Sato Codex" commit --amend --author="Sekiya Sato Codex <sekiya.sato@gmail.com>" -F $msgPath
Remove-Item -LiteralPath $msgPath
```

日付を維持する必要がある場合は、実行前に `GIT_COMMITTER_DATE` を対象コミットの日時へ設定する。

## 対象が履歴途中の場合

対象コミットの tree と parent を使って新コミットを作り、後続コミットを載せ直す。これは、対象コミットのファイル内容を変えずにメタデータだけ直すための手順。

```powershell
$ErrorActionPreference = "Stop"
$target = "<target-sha>"
$branch = git branch --show-current
$parent = git rev-parse "$target^"
$tree = git show -s --format=%T $target
$authorEmail = git show -s --format=%ae $target
$committerEmail = git show -s --format=%ce $target
$authorDate = git show -s --format=%aI $target
$committerDate = git show -s --format=%cI $target
$msgPath = Join-Path $env:TEMP "codex-rewrite-message.txt"

[IO.File]::WriteAllText($msgPath, @"
[作業内容]
GPT-5 : OpenAI : Codex
作業時間 hh:mm - hh:mm : n分
[ユーザ指示の概略]
"@, [Text.UTF8Encoding]::new($false))

try {
    $env:GIT_AUTHOR_NAME = "Sekiya Sato Codex"
    $env:GIT_AUTHOR_EMAIL = $authorEmail
    $env:GIT_AUTHOR_DATE = $authorDate
    $env:GIT_COMMITTER_NAME = "Sekiya Sato Codex"
    $env:GIT_COMMITTER_EMAIL = $committerEmail
    $env:GIT_COMMITTER_DATE = $committerDate
    $newCommit = git commit-tree $tree -p $parent -F $msgPath
    git rebase --onto $newCommit $target $branch
    Write-Output "NEW_COMMIT=$newCommit"
}
finally {
    Remove-Item -LiteralPath $msgPath -ErrorAction SilentlyContinue
    Remove-Item Env:\GIT_AUTHOR_NAME -ErrorAction SilentlyContinue
    Remove-Item Env:\GIT_AUTHOR_EMAIL -ErrorAction SilentlyContinue
    Remove-Item Env:\GIT_AUTHOR_DATE -ErrorAction SilentlyContinue
    Remove-Item Env:\GIT_COMMITTER_NAME -ErrorAction SilentlyContinue
    Remove-Item Env:\GIT_COMMITTER_EMAIL -ErrorAction SilentlyContinue
    Remove-Item Env:\GIT_COMMITTER_DATE -ErrorAction SilentlyContinue
}
```

rebase が競合した場合は、`git status` を確認し、解決方針をユーザーへ報告する。判断なしに競合解消や破棄を行わない。

## 検証

```powershell
git status --short --branch
git log --oneline --decorate --reverse origin/master..HEAD
git show --no-patch --pretty=fuller <new-sha>
git branch --contains <old-sha>
```

完了条件:

- 作業ツリーが意図せず汚れていない。
- 新しいコミットの Author / Committer が `Sekiya Sato Codex`。
- メッセージが Commit-Format に沿っている。
- 旧SHAが現在ブランチから外れている。`git branch --contains <old-sha>` が空でない場合は、残っているブランチを報告する。
- 後続コミット数と順序が意図通り。

## 報告

ユーザーへ次を簡潔に報告する。

- 旧SHAと新SHA。
- 修正した Author / Committer。
- 修正後のコミットメッセージ要約。
- 後続コミットを rebase したか。
- `git status` の結果。