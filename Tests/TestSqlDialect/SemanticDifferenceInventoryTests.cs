/*
# description
SemanticDifferenceInventoryTests は、方言変換では直せない意味差の箇所を棚卸しします（Phase 7 の入口）。

変換器はSQLの**文法**を合わせますが、**意味**は合わせられません。SQLを書き換えなくても
結果が変わる差が残ります。CV10 1.0 は SQLite のみを扱うため修正は 1.0 以降ですが、
「どこに何件あるか」を数字で固定しておかないと、監査が始まる前に増えてしまいます。

このテストは件数の上限を固定するだけで、修正は促しません。上限を超えたら失敗するので、
新しい画面が意味差を増やしたことに気づけます。修正して減ったら上限を下げて進捗を記録します。

検出する意味差:
- **整数除算**: SQLite と PostgreSQL は `int/int` を切り捨てるが、MariaDB は小数を返す。
  整数結果を意図している箇所は `CAST(... AS INTEGER)` で包む（SQLiteでは結果が変わらない）。
- **`strftime('%w')` の算術**: 変換後は文字列を返すため、PostgreSQL で `文字列 + 数値` が通らない。
  `cast(... as integer)` を足す（SQLiteでは結果が変わらない）。
 */
using CvBase.Sql;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TestSqlDialect;

[TestClass]
public sealed class SemanticDifferenceInventoryTests {

	/// <summary>除算を含むSQLリテラルの本数の上限（実測に合わせて更新する）</summary>
	const int DivisionLiteralMaximum = 13;

	/// <summary>`strftime('%w')` を算術に使っている箇所の上限</summary>
	const int WeekdayArithmeticMaximum = 1;

	[TestMethod]
	public void 整数除算を含むSQLの本数が増えていない() {
		var hits = FindLiterals(HasDivision);
		Assert.IsTrue(hits.Count <= DivisionLiteralMaximum,
			$"除算を含むSQL {hits.Count} 本 (上限 {DivisionLiteralMaximum})。"
			+ $"MariaDBは int/int が小数になる。整数結果を意図するなら CAST(... AS INTEGER) で包む。"
			+ Environment.NewLine + Format(hits));
	}

	[TestMethod]
	public void 曜日の算術利用が増えていない() {
		var hits = FindLiterals(HasWeekdayArithmetic);
		Assert.IsTrue(hits.Count <= WeekdayArithmeticMaximum,
			$"strftime('%w') を算術に使っているSQL {hits.Count} 本 (上限 {WeekdayArithmeticMaximum})。"
			+ $"変換後は文字列を返すためPostgreSQLで算術が通らない。cast(... as integer) を足す。"
			+ Environment.NewLine + Format(hits));
	}

	[TestMethod]
	public void 検出ロジックが期待どおり動く() {
		Assert.IsTrue(HasDivision("select a / b from T"));
		Assert.IsTrue(HasDivision("select (x)/2 from T"));
		// 文字列やコメントの中の / は数えない
		Assert.IsFalse(HasDivision("select 'a/b' from T"));
		Assert.IsFalse(HasDivision("select 1 -- a/b\r\nfrom T"));
		Assert.IsFalse(HasDivision("select /* a/b */ 1 from T"));
		Assert.IsFalse(HasDivision("select a from T"));

		Assert.IsTrue(HasWeekdayArithmetic("select (strftime('%w', d) + 6) % 7 from T"));
		Assert.IsTrue(HasWeekdayArithmetic("select strftime('%w', a) - strftime('%w', b) from T"));
		// 比較だけなら算術ではない
		Assert.IsFalse(HasWeekdayArithmetic("case strftime('%w', d) when '0' then 1 end"));
		// cast で包んであれば対応済み
		Assert.IsFalse(HasWeekdayArithmetic("select (cast(strftime('%w', d) as integer) + 6) % 7 from T"));
	}

	static List<SqlLiteral> FindLiterals(Func<string, bool> predicate) =>
		[.. SqlCorpus.LoadWithLocation("CvWpfclient", "CvBase", "CvDomainLogic").Where(x => predicate(x.Sql))];

	static string Format(List<SqlLiteral> hits) =>
		string.Join(Environment.NewLine, hits.Select(h => $"  {h.File}:{h.Line}"));

	/// <summary>SQL内に除算があるか。文字列リテラルとコメントは除く。</summary>
	static bool HasDivision(string sql) {
		var tokens = SqlTokenizer.Tokenize(sql);
		for (var i = 0; i < tokens.Count; i++) {
			if (!tokens[i].IsOperator("/"))
				continue;
			var left = PrevCode(tokens, i);
			var right = NextCode(tokens, i);
			if (left < 0 || right < 0)
				continue;
			var leftOk = tokens[left].Kind is SqlTokenKind.Word or SqlTokenKind.Number or SqlTokenKind.Parameter
				|| tokens[left].IsOperator(")");
			var rightOk = tokens[right].Kind is SqlTokenKind.Word or SqlTokenKind.Number or SqlTokenKind.Parameter
				|| tokens[right].IsOperator("(");
			if (leftOk && rightOk)
				return true;
		}
		return false;
	}

	/// <summary>
	/// <c>strftime('%w', ...)</c> の結果を算術に使っているか。
	/// 直前または直後が算術演算子で、かつ <c>cast</c> で包まれていないものを拾う。
	/// </summary>
	static bool HasWeekdayArithmetic(string sql) {
		var tokens = SqlTokenizer.Tokenize(sql);
		for (var i = 0; i < tokens.Count; i++) {
			if (!tokens[i].IsWord("strftime"))
				continue;
			var open = NextCode(tokens, i);
			if (open < 0 || !tokens[open].IsOperator("("))
				continue;
			var format = NextCode(tokens, open);
			if (format < 0 || tokens[format].Kind != SqlTokenKind.StringLiteral || tokens[format].Text != "'%w'")
				continue;
			// cast( で包まれていれば対応済みとみなす
			var beforeCall = PrevCode(tokens, i);
			if (beforeCall >= 0 && tokens[beforeCall].IsOperator("(")) {
				var castWord = PrevCode(tokens, beforeCall);
				if (castWord >= 0 && tokens[castWord].IsWord("cast"))
					continue;
			}
			var close = FindClose(tokens, open);
			if (close < 0)
				continue;
			if (IsArithmeticNeighbor(tokens, PrevCode(tokens, i)) || IsArithmeticNeighbor(tokens, NextCode(tokens, close)))
				return true;
		}
		return false;
	}

	static bool IsArithmeticNeighbor(IReadOnlyList<SqlToken> tokens, int index) =>
		index >= 0 && (tokens[index].IsOperator("+") || tokens[index].IsOperator("-")
			|| tokens[index].IsOperator("*") || tokens[index].IsOperator("/") || tokens[index].IsOperator("%"));

	static int FindClose(IReadOnlyList<SqlToken> tokens, int openIndex) {
		var depth = 0;
		for (var i = openIndex; i < tokens.Count; i++) {
			if (tokens[i].IsOperator("("))
				depth++;
			else if (tokens[i].IsOperator(")")) {
				depth--;
				if (depth == 0)
					return i;
			}
		}
		return -1;
	}

	static int NextCode(IReadOnlyList<SqlToken> tokens, int index) {
		for (var i = index + 1; i < tokens.Count; i++) {
			if (tokens[i].IsCode)
				return i;
		}
		return -1;
	}

	static int PrevCode(IReadOnlyList<SqlToken> tokens, int index) {
		for (var i = index - 1; i >= 0; i--) {
			if (tokens[i].IsCode)
				return i;
		}
		return -1;
	}
}
