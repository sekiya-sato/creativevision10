/*
# description
CategoryARuleTests は カテゴリA の変換ルール（T2）を検証します。

- A01 ifnull → coalesce（PostgreSQLのみ。MariaDBは同名関数なので書き換えない）
- A02 CAST の型名写像（MariaDBのみ。PostgreSQLはSQLiteの型名をそのまま解釈する）
- A03 予約語と衝突する列名の引用
- A04 PostgreSQL の ORDER BY へ NULLS FIRST（既定は無効）

いずれも「一致しない形は書き換えない」ことを併せて確認します。文字列リテラルや
コメントの中の同名語が書き換わらないことが、現行SQLiteを壊さない前提になります。
 */
using CvBase.Sql;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestSqlDialect;

[TestClass]
public sealed class CategoryARuleTests {

	[TestInitialize]
	public void Setup() {
		SqlDialectOptions.Mode = SqlDialectMode.Auto;
		SqlDialectOptions.EnableNullsFirst = false;
	}

	[TestCleanup]
	public void Cleanup() {
		SqlDialectOptions.Mode = SqlDialectMode.Auto;
		SqlDialectOptions.EnableNullsFirst = false;
	}

	// ---- A01 ifnull ----

	[TestMethod]
	public void A01_PostgreSQLはifnullをcoalesceにする() {
		Assert.AreEqual("select coalesce(a, 0) from T",
			SqlDialects.Postgre.Translate("select ifnull(a, 0) from T"));
		Assert.AreEqual("select COALESCE(a, 0) from T".Replace("COALESCE", "coalesce"),
			SqlDialects.Postgre.Translate("select IFNULL(a, 0) from T"));
	}

	[TestMethod]
	public void A01_入れ子のifnullも全て置き換える() {
		Assert.AreEqual("select coalesce(coalesce(a,b), 0) from T",
			SqlDialects.Postgre.Translate("select ifnull(ifnull(a,b), 0) from T"));
	}

	[TestMethod]
	public void A01_MariaDBはifnullを書き換えない() {
		const string sql = "select ifnull(a, 0) from T";
		Assert.AreEqual(sql, SqlDialects.Maria.Translate(sql));
	}

	[TestMethod]
	public void A01_MariaDBはifnullを未対応構文として報告しない() {
		Assert.AreEqual(0, SqlDialects.Maria.Inspect("select ifnull(a,0) from T").Count);
	}

	[TestMethod]
	public void A01_文字列とコメントの中は書き換えない() {
		Assert.AreEqual("select 'ifnull(a)' from T",
			SqlDialects.Postgre.Translate("select 'ifnull(a)' from T"));
		Assert.AreEqual("select 1 -- ifnull(a)\r\nfrom T",
			SqlDialects.Postgre.Translate("select 1 -- ifnull(a)\r\nfrom T"));
	}

	[TestMethod]
	public void A01_括弧が続かない語は書き換えない() {
		Assert.AreEqual("select ifnull from T", SqlDialects.Postgre.Translate("select ifnull from T"));
	}

	// ---- A02 CAST ----

	[TestMethod]
	public void A02_MariaDBはCASTの型名を写像する() {
		Assert.AreEqual("select cast(a as CHAR) from T", SqlDialects.Maria.Translate("select cast(a as text) from T"));
		Assert.AreEqual("select cast(a/b as DOUBLE) from T", SqlDialects.Maria.Translate("select cast(a/b as real) from T"));
		Assert.AreEqual("select cast(a as SIGNED) from T", SqlDialects.Maria.Translate("select cast(a as integer) from T"));
	}

	[TestMethod]
	public void A02_入れ子のCASTも写像する() {
		Assert.AreEqual("select cast(cast(a as CHAR) as DOUBLE) from T",
			SqlDialects.Maria.Translate("select cast(cast(a as text) as real) from T"));
	}

	[TestMethod]
	public void A02_写像表に無い型は触らない() {
		const string sql = "select cast(a as varchar) from T";
		Assert.AreEqual(sql, SqlDialects.Maria.Translate(sql));
	}

	[TestMethod]
	public void A02_型に長さ指定がある場合は触らない() {
		const string sql = "select cast(a as decimal(10,2)) from T";
		Assert.AreEqual(sql, SqlDialects.Maria.Translate(sql));
	}

	[TestMethod]
	public void A02_PostgreSQLはCASTを書き換えない() {
		const string sql = "select cast(a as text), cast(b as real) from T";
		Assert.AreEqual(sql, SqlDialects.Postgre.Translate(sql));
	}

	// ---- A03 予約語列名 ----

	[TestMethod]
	public void A03_PostgreSQLは予約語列名を小文字で引用する() {
		Assert.AreEqual("select s.\"offset\" from SummaryUriKake s",
			SqlDialects.Postgre.Translate("select s.Offset from SummaryUriKake s"));
	}

	[TestMethod]
	public void A03_MariaDBは予約語列名をバッククォートで引用する() {
		Assert.AreEqual("select s.`Offset` from SummaryUriKake s",
			SqlDialects.Maria.Translate("select s.Offset from SummaryUriKake s"));
	}

	[TestMethod]
	public void A03_別名や裸の列名も引用する() {
		Assert.AreEqual("select 0 as `Offset`, sum(`Offset`) from T",
			SqlDialects.Maria.Translate("select 0 as Offset, sum(Offset) from T"));
	}

	[TestMethod]
	public void A03_LIMIT_OFFSET句のキーワードは引用しない() {
		Assert.AreEqual("select a from T limit 10 offset 20",
			SqlDialects.Maria.Translate("select a from T limit 10 offset 20"));
		Assert.AreEqual("select a from T limit 10 offset @1",
			SqlDialects.Maria.Translate("select a from T limit 10 offset @1"));
	}

	[TestMethod]
	public void A03_文字列リテラル内の予約語は引用しない() {
		const string sql = "select 'Offset' from T";
		Assert.AreEqual(sql, SqlDialects.Maria.Translate(sql));
	}

	// ---- A04 NULLS FIRST ----

	[TestMethod]
	public void A04_既定では無効なので書き換えない() {
		const string sql = "select a from T order by Code";
		Assert.AreEqual(sql, SqlDialects.Postgre.Translate(sql));
	}

	[TestMethod]
	public void A04_有効時は単純な列参照にNULLS_FIRSTを付ける() {
		SqlDialectOptions.EnableNullsFirst = true;
		Assert.AreEqual("select a from T order by Code NULLS FIRST",
			SqlDialects.Postgre.Translate("select a from T order by Code"));
		Assert.AreEqual("select a from T order by Code NULLS FIRST, t.Name desc NULLS FIRST",
			SqlDialects.Postgre.Translate("select a from T order by Code, t.Name desc"));
	}

	[TestMethod]
	public void A04_有効時もLIMITより前で止まる() {
		SqlDialectOptions.EnableNullsFirst = true;
		Assert.AreEqual("select a from T order by Code NULLS FIRST limit 10",
			SqlDialects.Postgre.Translate("select a from T order by Code limit 10"));
	}

	[TestMethod]
	public void A04_有効時も式や関数呼び出しには付けない() {
		SqlDialectOptions.EnableNullsFirst = true;
		const string expression = "select a from T order by substr(a,1,4)";
		Assert.AreEqual(expression, SqlDialects.Postgre.Translate(expression));
		const string position = "select a from T order by 1";
		Assert.AreEqual(position, SqlDialects.Postgre.Translate(position));
	}

	[TestMethod]
	public void A04_既にNULLS指定がある項には付けない() {
		SqlDialectOptions.EnableNullsFirst = true;
		const string sql = "select a from T order by Code NULLS LAST";
		Assert.AreEqual(sql, SqlDialects.Postgre.Translate(sql));
	}

	[TestMethod]
	public void A04_MariaDBには付けない() {
		SqlDialectOptions.EnableNullsFirst = true;
		const string sql = "select a from T order by Code";
		Assert.AreEqual(sql, SqlDialects.Maria.Translate(sql));
	}

	// ---- 組み合わせ ----

	[TestMethod]
	public void 複数ルールが同一SQLへ適用される() {
		Assert.AreEqual("select coalesce(s.\"offset\", 0) from SummaryUriKake s",
			SqlDialects.Postgre.Translate("select ifnull(s.Offset, 0) from SummaryUriKake s"));
		Assert.AreEqual("select ifnull(cast(s.`Offset` as CHAR), '') from SummaryUriKake s",
			SqlDialects.Maria.Translate("select ifnull(cast(s.Offset as text), '') from SummaryUriKake s"));
	}

	[TestMethod]
	public void 対象構文が無いSQLは同一参照を返す() {
		const string sql = "select Id, Code from MasterTokui where Code >= @0 order by Code limit 100";
		Assert.IsTrue(ReferenceEquals(sql, SqlDialects.Postgre.Translate(sql)));
		Assert.IsTrue(ReferenceEquals(sql, SqlDialects.Maria.Translate(sql)));
	}
}
