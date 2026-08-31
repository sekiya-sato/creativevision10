/*
# description
SqliteRouteGuardTests は「既存のSQLite実行経路を壊さない」ことを機械的に固定します。

方言変換の作業で最も守らなければならないのは、現に動いている SQLite の挙動です。
このテストは、そのための保証を1つずつ検証します。

- SQLite方言は SQL を書き換えない（TranslatesSql が false）。CvServer はこのフラグで
  変換処理を丸ごと飛ばすため、SQLiteの経路には方言変換のコードが1行も通らない。
- SQLite方言の Translate は引数と同一参照を返す。
- 手書きSQLの差し替え表に SQLite を登録できない。
- クライアントの自己検査はどんな入力でも例外を投げない。
- `QueryListSqlParam` に QueryKey を足したが、旧形式のJSON（QueryKeyを持たない）も
  そのまま読める（配布物の組合せが変わっても既存経路が壊れない）。
 */
using CvAsset;
using CvBase;
using CvBase.Sql;
using CvBase.Share;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace TestSqlDialect;

[TestClass]
public sealed class SqliteRouteGuardTests {

	[TestInitialize]
	public void Setup() {
		SqlDialectOptions.Mode = SqlDialectMode.Auto;
		SqlDialectOptions.EnableNullsFirst = false;
		SqlOverrideCatalog.Clear();
	}

	[TestCleanup]
	public void Cleanup() => SqlOverrideCatalog.Clear();

	[TestMethod]
	public void SQLite方言はSQLを書き換えないと宣言する() {
		// CvServer.HandlerClass はこのフラグを見て変換処理を丸ごと飛ばす
		Assert.IsFalse(SqlDialects.Sqlite.TranslatesSql);
		Assert.IsFalse(PassThroughSqlDialect.Instance.TranslatesSql);
		Assert.IsTrue(SqlDialects.Postgre.TranslatesSql);
		Assert.IsTrue(SqlDialects.Maria.TranslatesSql);
	}

	[TestMethod]
	public void SQLite方言はどのモードでも同一参照を返す() {
		const string sql = "select ifnull(json_extract(m.value,'$.Su'),0) from T h, json_each(h.Jmeisai) m";
		foreach (var mode in new[] { SqlDialectMode.Auto, SqlDialectMode.Strict, SqlDialectMode.Off }) {
			SqlDialectOptions.Mode = mode;
			Assert.IsTrue(ReferenceEquals(sql, SqlDialects.Sqlite.Translate(sql)), mode.ToString());
			Assert.AreEqual(0, SqlDialects.Sqlite.Inspect(sql).Count, mode.ToString());
		}
		SqlDialectOptions.Mode = SqlDialectMode.Auto;
	}

	[TestMethod]
	public void SQLite方言はStrictモードでも例外を投げない() {
		SqlDialectOptions.Mode = SqlDialectMode.Strict;
		try {
			// 変換ルールが無い構文を含んでいてもSQLiteでは何も起きない
			const string sql = "select json_group_array(x), strftime('%j', d), julianday(a) from T";
			Assert.AreEqual(sql, SqlDialects.Sqlite.Translate(sql));
		}
		finally {
			SqlDialectOptions.Mode = SqlDialectMode.Auto;
		}
	}

	[TestMethod]
	public void 手書きSQLの差し替えはSQLiteに登録できない() {
		Assert.ThrowsExactly<ArgumentException>(() => SqlOverrideCatalog.Register("K", nameof(EnumSqlDialect.Sqlite), "select 1"));
		// 他方言へ登録しても SQLite 名では引けない
		SqlOverrideCatalog.Register("K", nameof(EnumSqlDialect.Postgre), "select 1");
		Assert.IsFalse(SqlOverrideCatalog.TryGet("K", SqlDialects.Sqlite.Name, out _));
	}

	[TestMethod]
	public void クライアント自己検査は例外を投げない() {
		SqlDialectGuard.Enabled = true;
		// 未終端リテラル、空、記号だけ、巨大な入れ子など、壊れた入力でも落ちない
		foreach (var sql in new[] {
			"", "   ", "'", "\"", "((((((((((", "select 'unterminated",
			"select 1 /* unterminated", "json_each(", "date(", "strftime('",
			"select json_extract(", "printf('%0",
		}) {
			SqlDialectGuard.WarnIfUnsupported(sql, nameof(SqliteRouteGuardTests));
			SqlDialectGuard.Inspect(sql);
		}
	}

	[TestMethod]
	public void 変換器自体も壊れた入力で例外を投げない() {
		foreach (var sql in new[] {
			"'", "\"", "((((((((((", "select 'unterminated", "json_each(", "date(",
			"strftime('%Y%m'", "printf('%02d'", "julianday(a) -", "cast(a as",
		}) {
			SqlDialects.Postgre.Translate(sql);
			SqlDialects.Maria.Translate(sql);
		}
	}

	[TestMethod]
	public void 自己検査はOffモードで何もしない() {
		SqlDialectOptions.Mode = SqlDialectMode.Off;
		try {
			SqlDialectGuard.Enabled = true;
			Assert.AreEqual(0, SqlDialectGuard.Inspect("select json_group_array(x) from T").Count);
		}
		finally {
			SqlDialectOptions.Mode = SqlDialectMode.Auto;
		}
	}

	[TestMethod]
	public void QueryListSqlParamは旧形式のJSONも読める() {
		// QueryKey を持たない旧形式。配布物の組合せが変わっても既存経路が壊れないこと
		const string legacyJson = """
{"Sql":"select 1 from MasterTokui","Parameters":["a","b"],"ItemType":"CvBase.MasterTokui, CvBase"}
""";
		var restored = Common.DeserializeObject(legacyJson, typeof(QueryListSqlParam)) as QueryListSqlParam;
		Assert.IsNotNull(restored);
		Assert.AreEqual("select 1 from MasterTokui", restored.Sql);
		Assert.AreEqual(2, restored.Parameters.Length);
		Assert.IsNull(restored.QueryKey);
		Assert.AreEqual(typeof(MasterTokui), restored.ItemType);
	}

	[TestMethod]
	public void QueryListSqlParamは往復して同じ内容になる() {
		var original = new QueryListSqlParam(typeof(MasterTokui), "select 1", ["x"], "Master.Tokui");
		var json = Common.SerializeObject(original);
		var restored = Common.DeserializeObject(json, typeof(QueryListSqlParam)) as QueryListSqlParam;
		Assert.IsNotNull(restored);
		Assert.AreEqual(original.Sql, restored.Sql);
		Assert.AreEqual(original.QueryKey, restored.QueryKey);
		Assert.AreEqual(original.ItemType, restored.ItemType);
		CollectionAssert.AreEqual(original.Parameters, restored.Parameters);
	}

	[TestMethod]
	public void QueryKey未指定なら差し替えは起きない() {
		SqlOverrideCatalog.Register("K", nameof(EnumSqlDialect.Postgre), "select 1");
		var param = new QueryListSqlParam(typeof(MasterTokui), "select 2");
		Assert.IsNull(param.QueryKey);
		Assert.IsFalse(SqlOverrideCatalog.TryGet(param.QueryKey, nameof(EnumSqlDialect.Postgre), out _));
	}
}
