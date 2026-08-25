/*
# description
SqliteIdentityTests は「SQLiteでは変換が1行も走らない」ことを検証します（T3 / 保証G1）。

方言変換を入れる作業で最も守らなければならないのは、現に動いている SQLite の挙動です。
このテストは収集した実SQL全件について、SQLite方言の Translate が
**引数と同一参照**を返すことを確認します。参照等価で見るので、
文字列を作り直す実装に変わった時点で失敗します。

Phase 1 では PostgreSQL / MariaDB のルールが空なので、両方言も同一参照を返します。
ルールを追加する作業が「意図した変更」であることを、このテストの失敗で気づけるようにしています。
 */
using CvBase.Sql;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestSqlDialect;

[TestClass]
public sealed class SqliteIdentityTests {

	[TestInitialize]
	public void Setup() => SqlDialectOptions.Mode = SqlDialectMode.Auto;

	[TestMethod]
	public void SQLite方言は収集した実SQL全件で同一参照を返す() {
		var corpus = SqlCorpus.Load();
		Assert.IsTrue(corpus.Count > 50, $"SQLリテラルの収集数が少なすぎます: {corpus.Count}");
		foreach (var sql in corpus) {
			var translated = SqlDialects.Sqlite.Translate(sql);
			Assert.IsTrue(ReferenceEquals(sql, translated),
				$"SQLite方言が変換を行いました。SQL={Head(sql)}");
		}
	}

	[TestMethod]
	public void SQLite方言は未対応構文を報告しない() {
		foreach (var sql in SqlCorpus.Load())
			Assert.AreEqual(0, SqlDialects.Sqlite.Inspect(sql).Count, $"SQL={Head(sql)}");
	}

	[TestMethod]
	public void SQLite方言はセッション設定を持たない() {
		Assert.AreEqual(0, SqlDialects.Sqlite.SessionSetupCommands.Count);
	}

	[TestMethod]
	public void 他方言の変換は冪等である() {
		// 2回変換しても結果が変わらないこと。差し替えた断片を再照合しない設計の確認。
		foreach (var sql in SqlCorpus.Load()) {
			var pg = SqlDialects.Postgre.Translate(sql);
			Assert.AreEqual(pg, SqlDialects.Postgre.Translate(pg), $"PG SQL={Head(sql)}");
			var maria = SqlDialects.Maria.Translate(sql);
			Assert.AreEqual(maria, SqlDialects.Maria.Translate(maria), $"Maria SQL={Head(sql)}");
		}
	}

	[TestMethod]
	public void 他方言の変換結果も字句復元できる() {
		foreach (var sql in SqlCorpus.Load()) {
			foreach (var translated in new[] { SqlDialects.Postgre.Translate(sql), SqlDialects.Maria.Translate(sql) }) {
				Assert.AreEqual(translated, SqlTokenizer.Render(SqlTokenizer.Tokenize(translated)),
					$"SQL={Head(sql)}");
			}
		}
	}

	[TestMethod]
	public void Offモードでは全方言が恒等変換になる() {
		try {
			SqlDialectOptions.Mode = SqlDialectMode.Off;
			const string sql = "select ifnull(json_extract(m.value,'$.Su'),0) from Tran00Uriage h, json_each(h.Jmeisai) m";
			Assert.IsTrue(ReferenceEquals(sql, SqlDialects.Postgre.Translate(sql)));
			Assert.IsTrue(ReferenceEquals(sql, SqlDialects.Maria.Translate(sql)));
			Assert.IsTrue(ReferenceEquals(sql, SqlDialects.Sqlite.Translate(sql)));
		}
		finally {
			SqlDialectOptions.Mode = SqlDialectMode.Auto;
		}
	}

	static string Head(string sql) => sql.Length <= 120 ? sql : sql[..120] + "...";
}
