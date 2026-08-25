/*
# description
UpsertRule は SQLite の UPSERT を MariaDB の構文へ置き換えます（ルール C04）。

CV10 では `CvDomainLogic/SummaryDb.cs` の在庫・引当サマリ再作成で4箇所使っています。
PostgreSQL は SQLite と同じ `ON CONFLICT ... DO UPDATE` / `excluded.列` が使えるため、
このルールは MariaDB 専用です。

MariaDB の `ON DUPLICATE KEY UPDATE` は衝突対象の列を指定しません。対象テーブルには
衝突判定に使う列そのものへ一意索引が張られているため、指定を落としても判定は変わりません。

`excluded.列` は MariaDB では `VALUES(列)` になります。走査は右から左に進むので、
SET句の `excluded.列` が先に置き換わり、そのあとで `ON CONFLICT ... DO UPDATE SET` の
見出し部分が置き換わります。

# example
ON CONFLICT(SumMonth, Id_Soko) DO UPDATE SET ReserveQty = excluded.ReserveQty, Vdu = 1
  -> ON DUPLICATE KEY UPDATE ReserveQty = VALUES(ReserveQty), Vdu = 1
 */
namespace CvBase.Sql.Rules;

/// <summary>C04: ON CONFLICT ... DO UPDATE SET を ON DUPLICATE KEY UPDATE へ</summary>
public sealed class UpsertHeaderRule : ISqlRewriteRule {

	public string Id => "C04-Upsert";

	public bool Apply(SqlRewriteContext context, int index) {
		if (!context.Tokens[index].IsWord("on"))
			return false;
		var conflict = context.NextCode(index);
		if (conflict < 0 || !context.Tokens[conflict].IsWord("conflict"))
			return false;

		var cursor = context.NextCode(conflict);
		// 衝突対象の列指定は任意。MariaDBでは指定しないので読み飛ばす
		if (cursor >= 0 && context.Tokens[cursor].IsOperator("(")) {
			var close = context.FindMatchingParen(cursor);
			if (close < 0)
				return false;
			cursor = context.NextCode(close);
		}
		// DO NOTHING は MariaDB に等価な短い書き方が無いため変換しない
		if (cursor < 0 || !context.Tokens[cursor].IsWord("do"))
			return false;
		var update = context.NextCode(cursor);
		if (update < 0 || !context.Tokens[update].IsWord("update"))
			return false;
		var set = context.NextCode(update);
		if (set < 0 || !context.Tokens[set].IsWord("set"))
			return false;

		context.ReplaceRange(index, set - index + 1, "ON DUPLICATE KEY UPDATE");
		return true;
	}
}

/// <summary>C04: UPSERT の <c>excluded.列</c> を MariaDB の <c>VALUES(列)</c> へ</summary>
public sealed class ExcludedColumnRule : ISqlRewriteRule {

	public string Id => "C04-Upsert";

	public bool Apply(SqlRewriteContext context, int index) {
		if (!context.Tokens[index].IsWord("excluded"))
			return false;
		var dot = context.NextCode(index);
		if (dot < 0 || !context.Tokens[dot].IsOperator("."))
			return false;
		var column = context.NextCode(dot);
		if (column < 0 || context.Tokens[column].Kind != SqlTokenKind.Word)
			return false;
		context.ReplaceRange(index, column - index + 1, $"VALUES({context.Tokens[column].Text})");
		return true;
	}
}
