/*
# description
SqlOverrideCatalogTests は QueryKey オーバーライド機構を検証します。

この機構は「変換器で表現できない形が出ても、クライアントのSQLite側SQLを書き換えなくて済む」
ための逃げ道です。SQLite への登録を禁じているのは、SQLite方言が正典であり
差し替える意味が無いためです。
 */
using CvBase.Sql;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace TestSqlDialect;

[TestClass]
public sealed class SqlOverrideCatalogTests {

	[TestInitialize]
	public void Setup() => SqlOverrideCatalog.Clear();

	[TestCleanup]
	public void Cleanup() => SqlOverrideCatalog.Clear();

	[TestMethod]
	public void QueryKeyと方言名で手書きSQLを取り出せる() {
		SqlOverrideCatalog.Register("Zaiko.Aging", "Postgre", "select 1");
		Assert.IsTrue(SqlOverrideCatalog.TryGet("Zaiko.Aging", "Postgre", out var sql));
		Assert.AreEqual("select 1", sql);
	}

	[TestMethod]
	public void 別の方言には適用されない() {
		SqlOverrideCatalog.Register("Zaiko.Aging", "Postgre", "select 1");
		Assert.IsFalse(SqlOverrideCatalog.TryGet("Zaiko.Aging", "MariaDb", out _));
		Assert.IsFalse(SqlOverrideCatalog.TryGet("Zaiko.Aging", "Sqlite", out _));
	}

	[TestMethod]
	public void QueryKey未指定なら常に不一致() {
		SqlOverrideCatalog.Register("Zaiko.Aging", "Postgre", "select 1");
		Assert.IsFalse(SqlOverrideCatalog.TryGet(null, "Postgre", out _));
		Assert.IsFalse(SqlOverrideCatalog.TryGet("", "Postgre", out _));
		Assert.IsFalse(SqlOverrideCatalog.TryGet("   ", "Postgre", out _));
	}

	[TestMethod]
	public void 同じ組の再登録は上書きになる() {
		SqlOverrideCatalog.Register("K", "Postgre", "select 1");
		SqlOverrideCatalog.Register("K", "Postgre", "select 2");
		Assert.AreEqual(1, SqlOverrideCatalog.Count);
		Assert.IsTrue(SqlOverrideCatalog.TryGet("K", "Postgre", out var sql));
		Assert.AreEqual("select 2", sql);
	}

	[TestMethod]
	public void SQLiteへの登録は拒否する() {
		Assert.ThrowsExactly<ArgumentException>(() => SqlOverrideCatalog.Register("K", "Sqlite", "select 1"));
		Assert.ThrowsExactly<ArgumentException>(() => SqlOverrideCatalog.Register("K", "sqlite", "select 1"));
	}

	[TestMethod]
	public void 空の引数は拒否する() {
		Assert.ThrowsExactly<ArgumentException>(() => SqlOverrideCatalog.Register("", "Postgre", "select 1"));
		Assert.ThrowsExactly<ArgumentException>(() => SqlOverrideCatalog.Register("K", "", "select 1"));
		Assert.ThrowsExactly<ArgumentException>(() => SqlOverrideCatalog.Register("K", "Postgre", ""));
	}
}
