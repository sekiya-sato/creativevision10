/*
# description
CategoryBJsonRuleTests は カテゴリB のJSON変換ルール（B01〜B04）を検証します。

要点は「呼び出し側SQLを書き換えずに済むか」です。
`json_each` の変換で PostgreSQL の `AS m(value)` と MariaDB の
`COLUMNS(value JSON PATH '$')` がどちらも `m.value` を提供するため、
展開結果を参照する側のSQLは変更不要になります。この性質をテストで固定します。

併せて「対応できない形は変換しない」ことも確認します。入れ子パスや添字パス、
引数の数が違う呼び出しは変換せず、未対応構文として報告させます。
 */
using CvBase.Sql;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace TestSqlDialect;

[TestClass]
public sealed class CategoryBJsonRuleTests {

	[TestInitialize]
	public void Setup() {
		SqlDialectOptions.Mode = SqlDialectMode.Auto;
		SqlDialectOptions.EnableNullsFirst = false;
	}

	// ---- B01 json_extract ----

	[TestMethod]
	public void B01_PostgreSQLは矢印演算子へ写像する() {
		Assert.AreEqual("select ((m.value)::jsonb ->> 'Su') from T",
			SqlDialects.Postgre.Translate("select json_extract(m.value,'$.Su') from T"));
	}

	[TestMethod]
	public void B01_MariaDBはJSON_VALUEへ写像する() {
		Assert.AreEqual("select JSON_VALUE(m.value,'$.Su') from T",
			SqlDialects.Maria.Translate("select json_extract(m.value,'$.Su') from T"));
	}

	[TestMethod]
	public void B01_ifnullとcastの組み合わせを保つ() {
		Assert.AreEqual("cast(coalesce(((m.value)::jsonb ->> 'Su'),0) as integer)",
			SqlDialects.Postgre.Translate("cast(ifnull(json_extract(m.value,'$.Su'),0) as integer)"));
		Assert.AreEqual("cast(ifnull(JSON_VALUE(m.value,'$.Su'),0) as SIGNED)",
			SqlDialects.Maria.Translate("cast(ifnull(json_extract(m.value,'$.Su'),0) as integer)"));
	}

	[TestMethod]
	public void B01_入れ子パスと添字パスは変換しない() {
		foreach (var sql in new[] {
			"select json_extract(m.value,'$.a.b') from T",
			"select json_extract(m.value,'$[0]') from T",
			"select json_extract(m.value,'$') from T",
			"select json_extract(m.value, @0) from T",
		}) {
			Assert.AreEqual(sql, SqlDialects.Postgre.Translate(sql), sql);
			Assert.AreEqual(sql, SqlDialects.Maria.Translate(sql), sql);
		}
	}

	[TestMethod]
	public void B01_引数の数が違えば変換しない() {
		const string sql = "select json_extract(m.value,'$.Su','$.Kin') from T";
		Assert.AreEqual(sql, SqlDialects.Postgre.Translate(sql));
	}

	// ---- B02 json_each ----

	[TestMethod]
	public void B02_PostgreSQLは別名付きで行展開へ写像する() {
		Assert.AreEqual("from Tran00Uriage h, jsonb_array_elements((h.Jmeisai)::jsonb) AS m(value)",
			SqlDialects.Postgre.Translate("from Tran00Uriage h, json_each(h.Jmeisai) m"));
	}

	[TestMethod]
	public void B02_MariaDBはJSON_TABLEへ写像する() {
		Assert.AreEqual("from Tran00Uriage h, JSON_TABLE(h.Jmeisai,'$[*]' COLUMNS(value JSON PATH '$')) AS m",
			SqlDialects.Maria.Translate("from Tran00Uriage h, json_each(h.Jmeisai) m"));
	}

	[TestMethod]
	public void B02_AS付きの別名も扱える() {
		Assert.AreEqual("from T h, jsonb_array_elements((h.Jmeisai)::jsonb) AS meisai(value)",
			SqlDialects.Postgre.Translate("from T h, json_each(h.Jmeisai) AS meisai"));
	}

	[TestMethod]
	public void B02_別名を省略した場合は既定名を補う() {
		// 既定名は引用識別子にする。生成SQLを再度変換にかけても関数呼び出しとして再照合されない
		Assert.AreEqual("from T t, jsonb_array_elements((t.Jmeisai)::jsonb) AS \"json_each\"(value) where json_each.value is not null",
			SqlDialects.Postgre.Translate("from T t, json_each(t.Jmeisai) where json_each.value is not null"));
		Assert.AreEqual("from T t, JSON_TABLE(t.Jmeisai,'$[*]' COLUMNS(value JSON PATH '$')) AS `json_each` where 1=1",
			SqlDialects.Maria.Translate("from T t, json_each(t.Jmeisai) where 1=1"));
	}

	[TestMethod]
	public void B02_直後のキーワードを別名と誤認しない() {
		var translated = SqlDialects.Maria.Translate("from T t, json_each(t.Jmeisai) where 1=1");
		StringAssert.Contains(translated, "AS `json_each`");
		StringAssert.Contains(translated, "where 1=1");
	}

	[TestMethod]
	public void B02_展開結果の参照側SQLは変更されない() {
		// 呼び出し側SQLを書き換えずに済むことがこのルールの目的
		const string sql = "select json_extract(m.value,'$.Su') from Tran00Uriage h, json_each(h.Jmeisai) m where json_valid(h.Jmeisai)";
		var pg = SqlDialects.Postgre.Translate(sql);
		var maria = SqlDialects.Maria.Translate(sql);
		StringAssert.Contains(pg, "AS m(value)");
		StringAssert.Contains(pg, "(m.value)::jsonb ->> 'Su'");
		StringAssert.Contains(maria, "COLUMNS(value JSON PATH '$')) AS m");
		StringAssert.Contains(maria, "JSON_VALUE(m.value,'$.Su')");
	}

	[TestMethod]
	public void B02_引数の中の構文も変換される() {
		// 右から左へ走査するため、範囲ごと差し替えるルールでも内側が変換済みで取り込まれる
		var pg = SqlDialects.Postgre.Translate(
			"from T t, json_each(CASE WHEN json_valid(t.Jmeisai) THEN t.Jmeisai ELSE '[]' END) AS m");
		StringAssert.Contains(pg, "IS JSON");
		StringAssert.Contains(pg, "AS m(value)");
		Assert.IsFalse(pg.Contains("json_valid"), pg);
	}

	// ---- B03 json_valid ----

	[TestMethod]
	public void B03_PostgreSQLはIS_JSON述語へ写像する() {
		Assert.AreEqual("where ((h.Jmeisai) IS JSON)",
			SqlDialects.Postgre.Translate("where json_valid(h.Jmeisai)"));
	}

	[TestMethod]
	public void B03_MariaDBは同名関数なので書き換えない() {
		const string sql = "where json_valid(h.Jmeisai)";
		Assert.AreEqual(sql, SqlDialects.Maria.Translate(sql));
		Assert.AreEqual(0, SqlDialects.Maria.Inspect(sql).Count);
	}

	// ---- B04 json ----

	[TestMethod]
	public void B04_jsonキャストを写像する() {
		Assert.AreEqual("select ((X.value2)::jsonb) from T",
			SqlDialects.Postgre.Translate("select json(X.value2) from T"));
		Assert.AreEqual("select CAST(X.value2 AS JSON) from T",
			SqlDialects.Maria.Translate("select json(X.value2) from T"));
	}

	// ---- 未対応構文の報告 ----

	[TestMethod]
	public void 対応済み構文だけのSQLは未対応構文を報告しない() {
		const string sql = "select cast(ifnull(json_extract(m.value,'$.Su'),0) as integer) from T h, json_each(h.Jmeisai) m where json_valid(h.Jmeisai)";
		Assert.AreEqual(0, SqlDialects.Postgre.Inspect(sql).Count,
			string.Join(", ", SqlDialects.Postgre.Inspect(sql).Select(f => f.ToString())));
		Assert.AreEqual(0, SqlDialects.Maria.Inspect(sql).Count,
			string.Join(", ", SqlDialects.Maria.Inspect(sql).Select(f => f.ToString())));
	}

	[TestMethod]
	public void 未対応の書式はStrictモードで例外になる() {
		SqlDialectOptions.Mode = SqlDialectMode.Strict;
		try {
			var ex = Assert.ThrowsExactly<SqlDialectUnsupportedException>(
				() => SqlDialects.Postgre.Translate("select strftime('%j', DenDay) from T"));
			Assert.IsTrue(ex.Findings.Any(f => f.Construct.Id == "B05-Strftime"));
		}
		finally {
			SqlDialectOptions.Mode = SqlDialectMode.Auto;
		}
	}

	[TestMethod]
	public void 変換できない形はStrictモードで例外になる() {
		SqlDialectOptions.Mode = SqlDialectMode.Strict;
		try {
			// パスが入れ子なので B01 が一致せず、json_extract が残る
			Assert.ThrowsExactly<SqlDialectUnsupportedException>(
				() => SqlDialects.Postgre.Translate("select json_extract(m.value,'$.a.b') from T"));
		}
		finally {
			SqlDialectOptions.Mode = SqlDialectMode.Auto;
		}
	}
}
