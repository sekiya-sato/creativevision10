---
name: skillopt-skill-improvement
description: Improve cv10 repo-local agent skills under .agents/skills using the SkillOpt method: rollout evidence, separate success/failure reflection, bounded add/delete/replace edits, held-out validation gates, rejected-edit buffers, and compact best-skill export. Use when the user asks to incorporate SkillOpt, create or update skills, optimize agent skills from prior work, or fix repeated skill-driven failures.
---

# SkillOpt Skill Improvement

このスキルは、SkillOpt の考え方を `C:\gitroot\new2022\cv10` の `.agents/skills` 運用へ適用するための手順です。外部 SkillOpt 本体を導入するのではなく、repo-local skill を証拠ベースで小さく改善します。

## 原則

- `SKILL.md` を訓練対象の状態として扱う。モデル、作業者、通常のソースコードを「学習済み」と見なさない。
- 実タスクの証拠から改善する。推測だけで既存 skill を大きく書き換えない。
- 成功例と失敗例を分けて反省する。失敗を直しながら、成功している手順を壊さない。
- 変更は add / delete / replace の小さな編集に限定する。全面 rewrite は、skill の構造が破綻している場合だけにする。
- 採用前に held-out 検証を行う。編集案の根拠に使った事例だけで通った変更は採用しない。
- 最終成果物は compact な `SKILL.md`。検討メモ、 rejected edits、slow update の観察は scratch に残し、通常は commit しない。

## 1. Evidence を集める

対象 skill と関連作業について、次を最小限で確認する。

- ユーザー依頼と制約: `計画、修正、ログ、コミットまで`、`確認のみ` などの stop rule。
- 実行経路: 読んだ skill、参照したファイル、使ったコマンド、検証結果。
- 成功証拠: 期待通りに動いた手順、再利用できる判断、安定した検証コマンド。
- 失敗証拠: ビルド失敗、XAML/resource 不備、コミット範囲ミス、ログ追記ミス、ユーザー指摘、不要な大規模変更。
- 影響範囲: 汎用 skill に入れるべきか、機能専用 skill に閉じるべきか。

証拠が古い場合は、低コストで再確認できるものを現在の repo で確認する。

## 2. Reflect を分ける

失敗 minibatch:

- 同じ失敗が複数回起きるか。
- skill が曖昧で agent の自由度が高すぎるか。
- 検証手順、ステージ対象、互換性確認、文字コード、ログ追記位置のどれが欠けているか。

成功 minibatch:

- 既存 skill のどの手順が効いたか。
- 残すべき表現、コマンド、ファイル名、検証順は何か。
- 汎用化すると壊れる repo 固有ルールがないか。

## 3. Bounded Edit を作る

編集案は最大 4 個程度の atomic edit に抑える。

- Add: 欠けている検証、前提確認、失敗回避ルールを追加する。
- Delete: 誤誘導、古い前提、実際に使われない冗長説明を削る。
- Replace: ぼんやりした指示を、repo で実証済みの手順へ置き換える。
- Split: 汎用手順と機能固有トラブルシュートが混ざる場合、別 skill に分ける。

frontmatter の `description` は trigger 判定に使われるため、適用条件が変わった場合だけ更新する。

## 4. Validation Gate

候補 skill は採用前に、編集根拠とは別の held-out 事例で確認する。

- 既存 skill の対象作業から、今回の編集に使っていない代表例を 1 件以上選ぶ。
- その事例で、agent が正しいファイル、検証、ログ/commit 判断へ進めるかを読む。
- 可能なら `git diff --check`、対象 build、qfm validator、XAML 構文確認など、skill が要求する検証を実行する。
- Markdown と YAML frontmatter が壊れていないことを確認する。
- 失敗した候補は採用しない。理由を scratch memo に残し、同じ方向の編集を繰り返さない。

## 5. Export

- 採用した内容だけを `SKILL.md` に残す。
- optimizer 側の長い反省、候補一覧、失敗ログは skill 本体に入れない。
- `.sisyphus/` や `.omo/` の scratch memo は、ユーザーが明示しない限り commit 対象にしない。
- `Doc/aicoding_log.md` には、変更した skill、採用理由、検証結果を簡潔に記録する。

## 確認コマンド

```powershell
git diff --check
git status --short
```

必要に応じて、対象 skill が示す build/test/validator を追加で実行する。
