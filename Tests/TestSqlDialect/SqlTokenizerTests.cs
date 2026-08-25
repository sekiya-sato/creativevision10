/*
# description
SqlTokenizerTests は字句解析器の不変条件（T1）を検証します。

不変条件は「返した字句のTextを順に連結すると入力に完全一致する」ことです。
この性質があるため、変換ルールが一致しなかったSQLは1バイトも変わりません。
現行SQLiteを壊さないことの根拠がここにあります。

入力にはリポジトリ内の実ソース全文も使います。C#ソースには引用符、エスケープ、
日本語、コメントが密集しているため、SQLだけを入力にするより厳しい試験になります。
 */
using CvBase.Sql;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

namespace TestSqlDialect;

[TestClass]
public sealed class SqlTokenizerTests {

	static void AssertRoundTrip(string input) {
		var tokens = SqlTokenizer.Tokenize(input);
		Assert.AreEqual(input, SqlTokenizer.Render(tokens), "字句列の連結が入力に一致しません。");
	}

	[TestMethod]
	public void 空文字と空白は復元できる() {
		AssertRoundTrip("");
		AssertRoundTrip("   ");
		AssertRoundTrip("\r\n\t ");
	}

	[TestMethod]
	public void 代表的なSQLを復元できる() {
		var samples = new[] {
			"select Id, Code from MasterTokui where Code >= @0 order by Code limit 100",
			"select ifnull(json_extract(m.value,'$.Su'),0) from Tran00Uriage h, json_each(h.Jmeisai) m where json_valid(h.Jmeisai)",
			"select substr(DenDay,1,4) || '/' || substr(DenDay,5,2) from Tran00Uriage",
			"select strftime('%Y%m', date('2026-04-01', '+' || n || ' months')) from seq",
			"select cast(a/b as real), cast(x as text) from T",
			"select * from T where Name like @0 escape '\\'",
			"-- 先頭コメント\r\nselect 1 /* 途中コメント */ from T",
			"select 'it''s a test', \"quoted\", `back`, [bracket] from T",
			"WITH RECURSIVE seq(n) AS (SELECT 0 UNION ALL SELECT n+1 FROM seq WHERE n < 12) select * from seq",
			"select sum(x) over (partition by y order by z) from T",
			"select printf('%04d-%02d-%02d', 2026, 4, 1)",
			"select julianday('2026-04-01') - julianday('2026-03-01')",
			"select 1.5e-3, 0xFF, .25 from T",
			"select '日本語のリテラル', 名称 from マスタ",
			"select @0, @name, :bind, ?, ?1 from T",
			"select a->>'$.x' from T",
		};
		foreach (var sample in samples)
			AssertRoundTrip(sample);
	}

	[TestMethod]
	public void 未終端のリテラルとコメントでも復元できる() {
		AssertRoundTrip("select 'unterminated");
		AssertRoundTrip("select \"unterminated");
		AssertRoundTrip("select 1 /* unterminated");
		AssertRoundTrip("select 1 -- unterminated");
		AssertRoundTrip("select [unterminated");
	}

	[TestMethod]
	public void 収集したSQLリテラルを復元できる() {
		var corpus = SqlCorpus.Load();
		Assert.IsTrue(corpus.Count > 50, $"SQLリテラルの収集数が少なすぎます: {corpus.Count}");
		foreach (var sql in corpus)
			AssertRoundTrip(sql);
	}

	[TestMethod]
	public void ソース全文を復元できる() {
		var texts = SqlCorpus.LoadSourceTexts();
		Assert.IsTrue(texts.Count > 100, $"ソース収集数が少なすぎます: {texts.Count}");
		foreach (var text in texts)
			AssertRoundTrip(text);
	}

	[TestMethod]
	public void 文字列リテラル内の語は語として扱わない() {
		var tokens = SqlTokenizer.Tokenize("select 'ifnull(x)' from T");
		var words = tokens.Where(t => t.Kind == SqlTokenKind.Word).Select(t => t.Text).ToList();
		CollectionAssert.AreEqual(new List<string> { "select", "from", "T" }, words);
	}

	[TestMethod]
	public void コメント内の語は語として扱わない() {
		var tokens = SqlTokenizer.Tokenize("select 1 -- ifnull\r\nfrom T");
		Assert.IsFalse(tokens.Any(t => t.IsWord("ifnull")));
	}
}
