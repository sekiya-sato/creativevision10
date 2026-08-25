/*
# description
NullsOrderRule は PostgreSQL の `ORDER BY` へ `NULLS FIRST` を付けます（ルール A04）。

SQLite と MariaDB は昇順でNULLを先頭に置きますが、PostgreSQL の既定は `NULLS LAST` で
並び位置が逆になります。帳票の行順が変わるため、合わせる必要があります。

ただし `ORDER BY` 句へ手を入れる唯一のルールで、副作用が読みにくいため
**既定は無効**です（`SqlDialectOptions.EnableNullsFirst`）。3DB差分テストで
必要性を確認してから有効化します。

安全側に倒すため、対象は「単純な列参照（`a` または `t.a`）に任意の `ASC`/`DESC` が付く形」
だけです。式・関数呼び出し・`CASE`・添字を含む項には何もしません。

# example
order by Code, t.Name desc  ->  order by Code NULLS FIRST, t.Name desc NULLS FIRST
order by substr(a,1,4)      ->  変更しない
 */
namespace CvBase.Sql.Rules;

/// <summary>A04: PostgreSQL の ORDER BY へ NULLS FIRST を付ける（既定は無効）</summary>
public sealed class NullsOrderRule : ISqlRewriteRule {

	/// <summary>ORDER BY 句の終わりとみなす語</summary>
	static readonly string[] _terminators = ["limit", "offset", "fetch", "for", "union", "except", "intersect", "window"];

	public string Id => "A04-NullsOrder";

	public bool Apply(SqlRewriteContext context, int index) {
		if (!SqlDialectOptions.EnableNullsFirst)
			return false;
		// `order` `by` の並びを探す
		if (!context.Tokens[index].IsWord("order"))
			return false;
		var byIndex = context.NextCode(index);
		if (byIndex < 0 || !context.Tokens[byIndex].IsWord("by"))
			return false;

		var end = FindClauseEnd(context, byIndex);
		if (end < byIndex)
			return false;

		// 後ろから処理して、差し替えによる位置ずれを避ける
		var items = SplitItems(context, byIndex + 1, end);
		var applied = false;
		for (var i = items.Count - 1; i >= 0; i--) {
			var (start, last) = items[i];
			if (!IsSimpleColumnItem(context, start, last))
				continue;
			context.ReplaceRange(start, last - start + 1, context.TextOf(start, last) + " NULLS FIRST");
			applied = true;
		}
		return applied;
	}

	/// <summary>ORDER BY 句の最後の字句位置を返す。</summary>
	static int FindClauseEnd(SqlRewriteContext context, int byIndex) {
		var depth = 0;
		var last = byIndex;
		for (var i = byIndex + 1; i < context.Tokens.Count; i++) {
			var token = context.Tokens[i];
			if (token.IsOperator("(")) {
				depth++;
			}
			else if (token.IsOperator(")")) {
				if (depth == 0)
					return last; // 括弧の外へ出た
				depth--;
			}
			else if (depth == 0 && token.Kind == SqlTokenKind.Word && _terminators.Contains(token.Text.ToLowerInvariant())) {
				return last;
			}
			else if (depth == 0 && token.IsOperator(";")) {
				return last;
			}
			if (token.IsCode)
				last = i;
		}
		return last;
	}

	/// <summary>ORDER BY 句をカンマで項に分ける。返すのは各項の先頭と末尾の実コード位置。</summary>
	static List<(int Start, int End)> SplitItems(SqlRewriteContext context, int from, int to) {
		var items = new List<(int, int)>();
		var depth = 0;
		var start = -1;
		var last = -1;
		for (var i = from; i <= to; i++) {
			var token = context.Tokens[i];
			if (token.IsOperator("("))
				depth++;
			else if (token.IsOperator(")"))
				depth--;
			if (depth == 0 && token.IsOperator(",")) {
				if (start >= 0)
					items.Add((start, last));
				start = -1;
				last = -1;
				continue;
			}
			if (!token.IsCode)
				continue;
			if (start < 0)
				start = i;
			last = i;
		}
		if (start >= 0)
			items.Add((start, last));
		return items;
	}

	/// <summary>
	/// 項が「単純な列参照 + 任意の ASC/DESC」かを判定する。
	/// 式や関数呼び出しには NULLS FIRST を付けない（安全側）。
	/// </summary>
	static bool IsSimpleColumnItem(SqlRewriteContext context, int start, int end) {
		var codeIndexes = new List<int>();
		for (var i = start; i <= end; i++) {
			if (context.Tokens[i].IsCode)
				codeIndexes.Add(i);
		}
		if (codeIndexes.Count == 0)
			return false;

		// 末尾の ASC / DESC は許容する
		var lastCode = context.Tokens[codeIndexes[^1]];
		if (lastCode.IsWord("asc") || lastCode.IsWord("desc"))
			codeIndexes.RemoveAt(codeIndexes.Count - 1);
		if (codeIndexes.Count == 0)
			return false;

		// 既に NULLS FIRST / NULLS LAST が付いていれば触らない
		foreach (var i in codeIndexes) {
			if (context.Tokens[i].IsWord("nulls"))
				return false;
		}
		// 残りは Word、または Word . Word（数値の位置指定 `order by 1` も対象外にする）
		return codeIndexes.Count switch {
			1 => context.Tokens[codeIndexes[0]].Kind == SqlTokenKind.Word,
			3 => context.Tokens[codeIndexes[0]].Kind == SqlTokenKind.Word
				&& context.Tokens[codeIndexes[1]].IsOperator(".")
				&& context.Tokens[codeIndexes[2]].Kind == SqlTokenKind.Word,
			_ => false,
		};
	}
}
