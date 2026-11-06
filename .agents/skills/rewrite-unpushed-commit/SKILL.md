---
name: rewrite-unpushed-commit
description: Safely rewrite explicitly requested local unpushed commits in cv10, including commit messages and Author or Committer metadata. Use for push前commit rewrite.
---

# Rewrite Unpushed Commit

明示された未 push コミットのメッセージや Author / Committer だけを修正する。変更対象外のメタデータとユーザーの未コミット変更は保持する。

## 事前確認

- `git status --short --branch`、`git show --no-patch --pretty=fuller <target-sha>`、`git branch -vv`、`git branch -r --contains <target-sha>` を確認する。リモート追跡参照が古い可能性にも注意し、upstream がない場合や push 済みか不明な場合は判断を求める。
- 対象コミットと後続コミットの範囲、merge commit の有無、変更前の tree・parent・日時・メッセージを確認する。履歴途中の merge commit は parent が複数あるため、単純な `commit-tree -p` では作り直さない。
- 未コミット変更がある場合は退避・破棄せず、既存変更を保持できない操作は行わない。特に amend では、依頼と無関係な staged 差分を含めない。
- 作業者名・メール・日時は依頼された項目だけ変更し、値を推測しない。指定がなければ元の値を保持する。
- メッセージは現行 `AGENTS.md` とユーザー指定に従う。既存 trailer は依頼がない限り保持する。

## 実施

1. 対象が HEAD なら、ステージ内容を確認し、変更不要の差分を含めずに `git commit --amend` で指定項目を修正する。メッセージ以外を変えないときは `--no-edit` を使い、変更対象外の Committer や日時は明示的に保持する。
2. 履歴途中なら、対象の tree・parent と保持するメタデータから新コミットを作り、後続コミットを載せ直す。対象と後続の履歴形状、日時、署名への影響を事前確認し、保持できない場合は判断を求める。
3. rebase の競合時は状態を確認し、ユーザーの変更を破棄せず判断を求める。`reset --hard` やユーザー変更の stash / checkout は行わない。

## 検証・報告

対象の変更前後の tree、指定外のメタデータ、後続コミットの差分・数・順序、ブランチの状態を照合する。旧 SHA と新 SHA、変更項目、後続履歴への影響、作業ツリーの状態を報告する。
