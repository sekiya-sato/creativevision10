/*
# description
SqlDialectDetectionTests は未対応構文の検出（T5）とバージョン検証を確認します。

Strictモードでは、SQLite固有構文が変換されずに残ったら例外にします。
素通しして接続先DBのエラーに委ねると「本番の特定画面だけ動かない」状態を作るためです。
Autoモードでは警告のみで実行を続けます。

SQLite は現行運用を止めないため、この検出の対象外です。
 */
using CvBase.Share;
using CvBase.Sql;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace TestSqlDialect;

[TestClass]
public sealed class SqlDialectDetectionTests {

	const string JsonSql = "select ifnull(json_extract(m.value,'$.Su'),0) from Tran00Uriage h, json_each(h.Jmeisai) m where json_valid(h.Jmeisai)";

	/// <summary>
	/// 変換できない構文・形を含むSQL。未対応構文の検出に使う。
	/// <c>changes()</c> は他DBに同名関数が無く（実行APIの戻り値で取る必要がある）、
	/// <c>strftime('%j')</c> は書式が写像表に無く、単独の <c>julianday</c> は減算パターンでない。
	/// </summary>
	const string UnsupportedSql = "select changes(), strftime('%j', DenDay), julianday(a) from Tran00Uriage";

	[TestInitialize]
	public void Setup() => SqlDialectOptions.Mode = SqlDialectMode.Auto;

	[TestCleanup]
	public void Cleanup() => SqlDialectOptions.Mode = SqlDialectMode.Auto;

	[TestMethod]
	public void 目録はSQLite固有構文を検出する() {
		var findings = SqliteConstructCatalog.Scan(JsonSql);
		var ids = findings.Select(f => f.Construct.Id).Distinct().ToList();
		CollectionAssert.Contains(ids, "A01-Ifnull");
		CollectionAssert.Contains(ids, "B01-JsonExtract");
		CollectionAssert.Contains(ids, "B02-JsonEach");
		CollectionAssert.Contains(ids, "B03-JsonValid");
	}

	[TestMethod]
	public void 目録は3DB共通の語を検出しない() {
		var findings = SqliteConstructCatalog.Scan("select substr(a,1,4) || '-' || replace(b,'x','y') from T order by a limit 10");
		Assert.AreEqual(0, findings.Count, string.Join(", ", findings.Select(f => f.ToString())));
	}

	[TestMethod]
	public void 文字列リテラル内の関数名は検出しない() {
		var findings = SqliteConstructCatalog.Scan("select 'json_each(x)' as memo from T");
		Assert.AreEqual(0, findings.Count);
	}

	[TestMethod]
	public void Strictモードでは未対応構文が例外になる() {
		SqlDialectOptions.Mode = SqlDialectMode.Strict;
		var ex = Assert.ThrowsExactly<SqlDialectUnsupportedException>(() => SqlDialects.Postgre.Translate(UnsupportedSql));
		Assert.AreEqual("Postgre", ex.DialectName);
		Assert.IsTrue(ex.Findings.Count >= 3);
	}

	[TestMethod]
	public void Autoモードでは未対応構文でも実行を続ける() {
		SqlDialectOptions.Mode = SqlDialectMode.Auto;
		// 例外にならず、変換できなかった構文はそのまま残る
		var translated = SqlDialects.Postgre.Translate(UnsupportedSql);
		StringAssert.Contains(translated, "changes(");
		StringAssert.Contains(translated, "strftime(");
		StringAssert.Contains(translated, "julianday(");
	}

	[TestMethod]
	public void 対応済み構文だけならStrictモードでも通る() {
		SqlDialectOptions.Mode = SqlDialectMode.Strict;
		StringAssert.Contains(SqlDialects.Postgre.Translate(JsonSql), "jsonb_array_elements");
		StringAssert.Contains(SqlDialects.Maria.Translate(JsonSql), "JSON_TABLE");
	}

	[TestMethod]
	public void クライアント自己検査は未対応構文をまとめて返す() {
		SqlDialectGuard.Enabled = true;
		var findings = SqlDialectGuard.Inspect(UnsupportedSql);
		Assert.IsTrue(findings.Count > 0);
		// 位置の昇順で並ぶ
		for (var i = 1; i < findings.Count; i++)
			Assert.IsTrue(findings[i - 1].Position <= findings[i].Position);
		// 対応済みのSQLでは何も報告しない
		Assert.AreEqual(0, SqlDialectGuard.Inspect(JsonSql).Count);
		// 「ルールはあるが対応できない形」も検出する
		Assert.IsTrue(SqlDialectGuard.Inspect("select json_extract(m.value,'$.a.b') from T").Count > 0);
		// SQLを変更しないことの確認（検査は副作用を持たない）
		SqlDialectGuard.WarnIfUnsupported(UnsupportedSql, nameof(SqlDialectDetectionTests));
	}

	[TestMethod]
	public void MariaDBはセッション設定でPIPES_AS_CONCATを付ける() {
		var commands = SqlDialects.Maria.SessionSetupCommands;
		Assert.AreEqual(1, commands.Count);
		StringAssert.Contains(commands[0], "PIPES_AS_CONCAT");
		StringAssert.Contains(commands[0], "NO_BACKSLASH_ESCAPES");
		// SQLiteの緩さに合わせるため、これらは入れない
		Assert.IsFalse(commands[0].Contains("ONLY_FULL_GROUP_BY"));
		Assert.IsFalse(commands[0].Contains("STRICT_TRANS_TABLES"));
	}

	[TestMethod]
	public void バージョン検証は下限未満を弾く() {
		Assert.AreEqual(0, SqlDialects.Postgre.Validate("17.2").Count);
		Assert.AreEqual(1, SqlDialects.Postgre.Validate("15.6").Count);
		Assert.AreEqual(0, SqlDialects.Maria.Validate("11.4.2-MariaDB").Count);
		Assert.AreEqual(1, SqlDialects.Maria.Validate("10.6.16-MariaDB").Count);
		Assert.AreEqual(0, SqlDialects.Sqlite.Validate("3.49.1").Count);
		Assert.AreEqual(1, SqlDialects.Sqlite.Validate("3.37.2").Count);
		Assert.AreEqual(1, SqlDialects.Postgre.Validate("").Count);
	}

	[TestMethod]
	public void バージョン文字列を解釈できる() {
		Assert.AreEqual(new System.Version(3, 49, 1), SqlDialectVersions.Parse("3.49.1"));
		Assert.AreEqual(new System.Version(17, 2), SqlDialectVersions.Parse("17.2"));
		Assert.AreEqual(new System.Version(11, 4, 2), SqlDialectVersions.Parse("11.4.2-MariaDB"));
		Assert.AreEqual(new System.Version(16, 0), SqlDialectVersions.Parse("16"));
		Assert.IsNull(SqlDialectVersions.Parse("unknown"));
		Assert.IsNull(SqlDialectVersions.Parse(""));
	}

	[TestMethod]
	public void モード文字列を解釈できる() {
		Assert.AreEqual(SqlDialectMode.Off, SqlDialectOptions.ParseMode("Off"));
		Assert.AreEqual(SqlDialectMode.Strict, SqlDialectOptions.ParseMode(" strict "));
		Assert.AreEqual(SqlDialectMode.Auto, SqlDialectOptions.ParseMode("Auto"));
		Assert.AreEqual(SqlDialectMode.Auto, SqlDialectOptions.ParseMode(null));
		Assert.AreEqual(SqlDialectMode.Auto, SqlDialectOptions.ParseMode("unknown"));
	}

	[TestMethod]
	public void プロバイダー名から方言を選べる() {
		Assert.AreSame(SqlDialects.Sqlite, SqlDialects.ByProviderName("Sqlite"));
		Assert.AreSame(SqlDialects.Postgre, SqlDialects.ByProviderName("Postgre"));
		Assert.AreSame(SqlDialects.Maria, SqlDialects.ByProviderName("MariaDb"));
		Assert.AreSame(PassThroughSqlDialect.Instance, SqlDialects.ByProviderName("unknown"));
	}

	[TestMethod]
	public void InfoServerのDbProviderから方言を選べる() {
		Assert.AreSame(SqlDialects.Sqlite, SqlDialects.ByProviderName(new InfoServer().DbProvider));
		Assert.AreSame(SqlDialects.Postgre, SqlDialects.ByProviderName(new InfoServer { DbProvider = "Postgre" }.DbProvider));
		Assert.AreSame(SqlDialects.Maria, SqlDialects.ByProviderName(new InfoServer { DbProvider = "MariaDb" }.DbProvider));
	}
}
