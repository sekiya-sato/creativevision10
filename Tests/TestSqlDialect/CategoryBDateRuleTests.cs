/*
# description
CategoryBDateRuleTests は カテゴリB の日付・整形ルール（B05〜B08）を検証します。

永続列は `yyyyMMdd` / `yyyyMM` の文字列のままなので、対象は物理型ではなく
「文字列日付を整形・加減算する式」です。使われている形が閉じている（書式5種、
修飾子3単位、printfは %0Nd のみ）ことを前提にした写像なので、
その外側の形を変換しないことも併せて確認します。

`PeriodSql` と帳票SQLで実際に使われている組み合わせを最後にまとめて確認します。
 */
using CvBase.Sql;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestSqlDialect;

[TestClass]
public sealed class CategoryBDateRuleTests {

	[TestInitialize]
	public void Setup() {
		SqlDialectOptions.Mode = SqlDialectMode.Auto;
		SqlDialectOptions.EnableNullsFirst = false;
	}

	// ---- B05 strftime ----

	[TestMethod]
	public void B05_年月と年月日の書式を写像する() {
		Assert.AreEqual("select to_char((d)::date,'YYYYMM') from T",
			SqlDialects.Postgre.Translate("select strftime('%Y%m', d) from T"));
		Assert.AreEqual("select DATE_FORMAT(d,'%Y%m') from T",
			SqlDialects.Maria.Translate("select strftime('%Y%m', d) from T"));
		Assert.AreEqual("select to_char((d)::date,'YYYYMMDD') from T",
			SqlDialects.Postgre.Translate("select strftime('%Y%m%d', d) from T"));
		Assert.AreEqual("select DATE_FORMAT(d,'%Y%m%d') from T",
			SqlDialects.Maria.Translate("select strftime('%Y%m%d', d) from T"));
	}

	[TestMethod]
	public void B05_曜日はSQLiteと同じ文字列で返す() {
		Assert.AreEqual("extract(dow from (d)::date)::integer::text",
			SqlDialects.Postgre.Translate("strftime('%w', d)"));
		Assert.AreEqual("CAST(DAYOFWEEK(d)-1 AS CHAR)",
			SqlDialects.Maria.Translate("strftime('%w', d)"));
	}

	[TestMethod]
	public void B05_日はゼロ埋め2桁で返す() {
		Assert.AreEqual("cast(to_char((d)::date,'DD') as integer)",
			SqlDialects.Postgre.Translate("cast(strftime('%d', d) as integer)"));
		Assert.AreEqual("cast(DATE_FORMAT(d,'%d') as SIGNED)",
			SqlDialects.Maria.Translate("cast(strftime('%d', d) as integer)"));
	}

	[TestMethod]
	public void B05_エポック秒は数値で返す() {
		Assert.AreEqual("extract(epoch from now())::bigint*10000000",
			SqlDialects.Postgre.Translate("strftime('%s','now')*10000000"));
		Assert.AreEqual("UNIX_TIMESTAMP()*10000000",
			SqlDialects.Maria.Translate("strftime('%s','now')*10000000"));
	}

	[TestMethod]
	public void B05_now_localtimeの3引数形を扱える() {
		Assert.AreEqual("to_char(now(),'YYYYMMDD')",
			SqlDialects.Postgre.Translate("strftime('%Y%m%d','now','localtime')"));
		Assert.AreEqual("DATE_FORMAT(NOW(),'%Y%m%d')",
			SqlDialects.Maria.Translate("strftime('%Y%m%d','now','localtime')"));
	}

	[TestMethod]
	public void B05_未対応の書式と修飾子は変換しない() {
		foreach (var sql in new[] {
			"select strftime('%j', d) from T",
			"select strftime('%Y-%m-%d', d) from T",
			"select strftime('%Y%m', d, 'start of month') from T",
			"select strftime(@0, d) from T",
		}) {
			Assert.AreEqual(sql, SqlDialects.Postgre.Translate(sql), sql);
			Assert.AreEqual(sql, SqlDialects.Maria.Translate(sql), sql);
		}
	}

	// ---- B06 printf ----

	[TestMethod]
	public void B06_単一のゼロ埋めを写像する() {
		Assert.AreEqual("select lpad((n)::text,2,'0') from T",
			SqlDialects.Postgre.Translate("select printf('%02d', n) from T"));
		Assert.AreEqual("select LPAD(n,2,'0') from T",
			SqlDialects.Maria.Translate("select printf('%02d', n) from T"));
	}

	[TestMethod]
	public void B06_区切り付きの日付組み立てを写像する() {
		Assert.AreEqual("(lpad((y)::text,4,'0') || '-' || lpad((m)::text,2,'0') || '-' || lpad((d)::text,2,'0'))",
			SqlDialects.Postgre.Translate("printf('%04d-%02d-%02d', y, m, d)"));
		Assert.AreEqual("CONCAT(LPAD(y,4,'0'), '-', LPAD(m,2,'0'), '-', LPAD(d,2,'0'))",
			SqlDialects.Maria.Translate("printf('%04d-%02d-%02d', y, m, d)"));
	}

	[TestMethod]
	public void B06_指定子と引数の数が合わなければ変換しない() {
		const string sql = "select printf('%02d-%02d', n) from T";
		Assert.AreEqual(sql, SqlDialects.Postgre.Translate(sql));
	}

	[TestMethod]
	public void B06_未対応の指定子は変換しない() {
		foreach (var sql in new[] {
			"select printf('%s', n) from T",
			"select printf('%d', n) from T",
			"select printf('%-2d', n) from T",
		}) {
			Assert.AreEqual(sql, SqlDialects.Postgre.Translate(sql), sql);
		}
	}

	// ---- B07 date 修飾子 ----

	[TestMethod]
	public void B07_リテラル修飾子を写像する() {
		Assert.AreEqual("(((d)::date - ((1) || ' YEAR')::interval)::date)",
			SqlDialects.Postgre.Translate("date(d, '-1 year')"));
		Assert.AreEqual("DATE_SUB(d, INTERVAL (1) YEAR)",
			SqlDialects.Maria.Translate("date(d, '-1 year')"));
		Assert.AreEqual("DATE_ADD(d, INTERVAL (3) MONTH)",
			SqlDialects.Maria.Translate("date(d, '+3 months')"));
	}

	[TestMethod]
	public void B07_連結修飾子を写像する() {
		Assert.AreEqual("DATE_ADD('2026-04-01', INTERVAL (n) MONTH)",
			SqlDialects.Maria.Translate("date('2026-04-01', '+' || n || ' months')"));
		Assert.AreEqual("((('2026-04-01')::date + ((n) || ' MONTH')::interval)::date)",
			SqlDialects.Postgre.Translate("date('2026-04-01', '+' || n || ' months')"));
	}

	[TestMethod]
	public void B07_符号を省いた連結修飾子も扱える() {
		// SQLite は符号なしの修飾子を加算として扱う。負値を返す式でも両DBが減算として解釈する
		Assert.AreEqual("DATE_ADD(d, INTERVAL ((s.PayDay - 1)) DAY)",
			SqlDialects.Maria.Translate("date(d, (s.PayDay - 1) || ' days')"));
		Assert.AreEqual("(((d)::date + (((s.PayDay - 1)) || ' DAY')::interval)::date)",
			SqlDialects.Postgre.Translate("date(d, (s.PayDay - 1) || ' days')"));
	}

	[TestMethod]
	public void B07_修飾子が複数あれば左から順に適用する() {
		Assert.AreEqual("DATE_SUB(DATE_ADD('2026-04-01', INTERVAL (n) MONTH), INTERVAL (1) YEAR)",
			SqlDialects.Maria.Translate("date('2026-04-01', '+' || n || ' months', '-1 year')"));
	}

	[TestMethod]
	public void B07_修飾子なしのdateは触らない() {
		const string sql = "select date(d) from T";
		Assert.AreEqual(sql, SqlDialects.Postgre.Translate(sql));
		Assert.AreEqual(sql, SqlDialects.Maria.Translate(sql));
		// 修飾子が無ければ未対応構文としても報告しない
		Assert.AreEqual(0, SqlDialects.Postgre.Inspect(sql).Count);
	}

	[TestMethod]
	public void B07_未対応の修飾子は変換しない() {
		foreach (var sql in new[] {
			"select date(d, 'start of month') from T",
			"select date(d, 'weekday 0') from T",
			"select date(d, @0) from T",
		}) {
			Assert.AreEqual(sql, SqlDialects.Postgre.Translate(sql), sql);
			Assert.AreEqual(sql, SqlDialects.Maria.Translate(sql), sql);
		}
	}

	// ---- B08 julianday ----

	[TestMethod]
	public void B08_減算パターンを日数差へ写像する() {
		Assert.AreEqual("DATEDIFF(a, b)",
			SqlDialects.Maria.Translate("julianday(a) - julianday(b)"));
		Assert.AreEqual("(((a)::date - ((b)::date)))",
			SqlDialects.Postgre.Translate("julianday(a) - julianday(b)"));
	}

	[TestMethod]
	public void B08_単独のjuliandayは変換しない() {
		const string sql = "select julianday(a) from T";
		Assert.AreEqual(sql, SqlDialects.Postgre.Translate(sql));
		Assert.AreEqual(sql, SqlDialects.Maria.Translate(sql));
		// 変換できなかったので未対応構文として報告される
		Assert.AreEqual(1, SqlDialects.Maria.Inspect(sql).Count);
		Assert.AreEqual("B08-Julianday", SqlDialects.Maria.Inspect(sql)[0].Construct.Id);
	}

	// ---- 実際に使われている組み合わせ ----

	[TestMethod]
	public void PeriodSqlの週キーは変換できる() {
		// PeriodSql.Key(PeriodUnit.Week) と同じ形。%w の算術は SQLite の暗黙数値化に依存するため
		// cast を足した形で確認する（cast を足しても SQLite の結果は変わらない）
		const string sql = "date((substr(h.DenDay,1,4) || '-' || substr(h.DenDay,5,2) || '-' || substr(h.DenDay,7,2)), '-' || ((cast(strftime('%w', d) as integer) + 6) % 7) || ' days')";
		var maria = SqlDialects.Maria.Translate(sql);
		StringAssert.Contains(maria, "DATE_SUB(");
		StringAssert.Contains(maria, "INTERVAL (");
		StringAssert.Contains(maria, "DAY)");
		Assert.IsFalse(maria.Contains("strftime"), maria);
		Assert.AreEqual(0, SqlDialects.Maria.Inspect(sql).Count);
	}

	[TestMethod]
	public void 月次連番の生成は変換できる() {
		const string sql = "SELECT strftime('%Y%m', date('2026-04-01', '+' || n || ' months')) AS ym, strftime('%Y%m', date('2026-04-01', '+' || n || ' months', '-1 year')) AS prevYm FROM seq";
		foreach (var dialect in new[] { SqlDialects.Postgre, SqlDialects.Maria }) {
			var translated = dialect.Translate(sql);
			Assert.IsFalse(translated.Contains("strftime"), translated);
			Assert.AreEqual(0, dialect.Inspect(sql).Count, dialect.Name);
		}
	}

	[TestMethod]
	public void 曜日ラベルのCASE式は変換できる() {
		const string sql = "CASE strftime('%w', d) WHEN '0' THEN '日' WHEN '1' THEN '月' END";
		Assert.AreEqual("CASE CAST(DAYOFWEEK(d)-1 AS CHAR) WHEN '0' THEN '日' WHEN '1' THEN '月' END",
			SqlDialects.Maria.Translate(sql));
	}
}
