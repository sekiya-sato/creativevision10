/*
# description
UpdateDbDialectTests は、DBバージョン更新(UpdateDb)の SQL が接続先の方言で正しく選ばれることを固定します。

- SQLite は従来どおり InnerVersion.Sql（正典）を実行する。
- MariaDB / PostgreSQL は DialectSql に定義した方言のSQLだけを実行し、未定義なら実行しない（null）。
  SQLite の DDL（NUMBER 型・ALTER の文法など）を他DBへそのまま流さないため。
 */
using CvBase;
using CvBase.Sql;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace TestSqlDialect;

[TestClass]
public sealed class UpdateDbDialectTests {

	[TestMethod]
	public void SQLiteは従来どおり正典のSQLを使う() {
		var v = new InnerVersion(1, "ALTER TABLE T ADD COLUMN A NUMBER not null default 0;", "m");
		Assert.AreEqual(v.Sql, v.ResolveSql(SqlDialects.Sqlite));
		Assert.AreEqual(v.Sql, v.ResolveSql(PassThroughSqlDialect.Instance));
	}

	[TestMethod]
	public void 方言SQLが未定義ならMariaDBとPostgreSQLでは実行しない() {
		var v = new InnerVersion(1, "ALTER TABLE T ADD COLUMN A NUMBER not null default 0;", "m");
		Assert.IsNull(v.ResolveSql(SqlDialects.Maria));
		Assert.IsNull(v.ResolveSql(SqlDialects.Postgre));
	}

	[TestMethod]
	public void 方言SQLが定義されていればその方言だけで使う() {
		var v = new InnerVersion(1, "ALTER TABLE T ADD COLUMN A NUMBER not null default 0;", "m") {
			DialectSql = new Dictionary<string, string> { [SqlDialects.Postgre.Name] = "ALTER TABLE t ADD COLUMN a bigint NOT NULL DEFAULT 0;" },
		};
		Assert.AreEqual("ALTER TABLE t ADD COLUMN a bigint NOT NULL DEFAULT 0;", v.ResolveSql(SqlDialects.Postgre));
		Assert.IsNull(v.ResolveSql(SqlDialects.Maria));
		Assert.AreEqual(v.Sql, v.ResolveSql(SqlDialects.Sqlite));
	}
}
