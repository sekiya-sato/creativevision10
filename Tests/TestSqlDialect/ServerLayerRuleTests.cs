/*
# description
ServerLayerRuleTests は、サーバ側SQL（CvDomainLogic / CvBase）に現れる構文の変換を検証します。

対象は2種類です。

1. JSON配列の作り直し。`MasterCascadeDb` / `RebuildDb` / `ConvertDbTran` が
   `json_group_array` + `json_set` + `json_each` の `key` 列（要素順の保持）で
   Jsub / Jcolsiz / Jmeisai を作り直しています。
2. UPSERT。`SummaryDb` が `ON CONFLICT ... DO UPDATE` で在庫・引当サマリを再作成しています。

これらのSQLはSQLite方言のまま残し、`ExDatabase.ExecuteDialect` を通して実行時に変換します。
SQLiteでは短絡するので、既存の実行経路には手が入りません。
 */
using CvBase.Sql;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace TestSqlDialect;

[TestClass]
public sealed class ServerLayerRuleTests {

	/// <summary>MasterCascadeDb.ExecuteJsubCodeNameRule と同じ形</summary>
	const string JsubRebuildSql = """
update MasterTokui as S
   set Jsub = ( select json_group_array(json(X.value2))
                   from ( select J.key,
                                 case when json_extract(J.value, '$.Sid') = @0
                                      then json_set(J.value, '$.Cd', @1, '$.Mei', @2)
                                      else J.value end as value2
                            from json_each(S.Jsub) as J
                           order by cast(J.key as integer) ) as X ),
       Vdu = @3
 where json_valid(S.Jsub)
   and exists ( select 1 from json_each(S.Jsub) as J
                 where json_extract(J.value, '$.Sid') = @0 )
""";

	/// <summary>SummaryDb.CreateReserveRealSql の末尾と同じ形</summary>
	const string UpsertSql = """
INSERT INTO SummaryRealStock (Id_Soko, Su, Vdc, Vdu, ReserveQty)
SELECT h.Id_Soko, 0 AS Su, 1 AS Vdc, 1 AS Vdu, SUM(h.Su) AS ReserveQty
FROM TranHaibun AS h
GROUP BY h.Id_Soko
ON CONFLICT(Id_Soko, Id_Shohin, Id_Col, Id_Siz) DO UPDATE
SET ReserveQty = excluded.ReserveQty, Vdu = 1
""";

	[TestInitialize]
	public void Setup() {
		SqlDialectOptions.Mode = SqlDialectMode.Auto;
		SqlDialectOptions.EnableNullsFirst = false;
	}

	// ---- 行番号列（json_each の key） ----

	[TestMethod]
	public void 要素順の保持に使うkey列を行番号列へ読み替える() {
		var pg = SqlDialects.Postgre.Translate(JsubRebuildSql);
		StringAssert.Contains(pg, "WITH ORDINALITY AS J(value, jkey)");
		StringAssert.Contains(pg, "order by cast(J.jkey as integer)");
		Assert.IsFalse(pg.Contains("J.key"), pg);

		var maria = SqlDialects.Maria.Translate(JsubRebuildSql);
		StringAssert.Contains(maria, "jkey FOR ORDINALITY");
		StringAssert.Contains(maria, "order by cast(J.jkey as SIGNED)");
		Assert.IsFalse(maria.Contains("J.key"), maria);
	}

	[TestMethod]
	public void key列を参照しない場合は行番号列を作らない() {
		const string sql = "select json_extract(m.value,'$.Su') from T h, json_each(h.Jmeisai) m";
		Assert.IsFalse(SqlDialects.Postgre.Translate(sql).Contains("ORDINALITY"));
		Assert.IsFalse(SqlDialects.Maria.Translate(sql).Contains("ORDINALITY"));
	}

	[TestMethod]
	public void 無関係なkey列は書き換えない() {
		// json_each の別名ではないので触らない
		const string sql = "select t.key from SomeTable t";
		Assert.AreEqual(sql, SqlDialects.Postgre.Translate(sql));
		Assert.AreEqual(sql, SqlDialects.Maria.Translate(sql));
	}

	// ---- JSON組み立て ----

	[TestMethod]
	public void json_group_arrayを写像する() {
		StringAssert.Contains(SqlDialects.Postgre.Translate("select json_group_array(x) from T"), "jsonb_agg(x)");
		StringAssert.Contains(SqlDialects.Maria.Translate("select json_group_array(x) from T"), "JSON_ARRAYAGG(x)");
	}

	[TestMethod]
	public void json_objectを写像する() {
		StringAssert.Contains(
			SqlDialects.Postgre.Translate("select json_object('Sid', s.Id, 'Cd', s.Code) from T s"),
			"jsonb_build_object('Sid', s.Id, 'Cd', s.Code)");
		// MariaDB は同名関数なので書き換えない
		const string sql = "select json_object('Sid', s.Id) from T s";
		Assert.AreEqual(sql, SqlDialects.Maria.Translate(sql));
	}

	[TestMethod]
	public void json_setをPostgreSQLの入れ子へ展開する() {
		Assert.AreEqual(
			"jsonb_set(jsonb_set(J.value, '{Cd}', to_jsonb(@1), true), '{Mei}', to_jsonb(@2), true)",
			SqlDialects.Postgre.Translate("json_set(J.value, '$.Cd', @1, '$.Mei', @2)"));
	}

	[TestMethod]
	public void json_setはMariaDBでは同名なので書き換えない() {
		const string sql = "json_set(J.value, '$.Cd', @1, '$.Mei', @2)";
		Assert.AreEqual(sql, SqlDialects.Maria.Translate(sql));
	}

	[TestMethod]
	public void json_setの引数の対が揃わなければ変換しない() {
		const string sql = "select json_set(J.value, '$.Cd') from T";
		Assert.AreEqual(sql, SqlDialects.Postgre.Translate(sql));
	}

	[TestMethod]
	public void Jsub再構築SQLは両方言で未対応構文が残らない() {
		foreach (var dialect in new[] { SqlDialects.Postgre, SqlDialects.Maria }) {
			var findings = dialect.Inspect(JsubRebuildSql);
			Assert.AreEqual(0, findings.Count,
				$"{dialect.Name}: {string.Join(", ", findings.Select(f => f.ToString()))}");
		}
	}

	// ---- UPSERT ----

	[TestMethod]
	public void PostgreSQLはUPSERTを書き換えない() {
		// SQLite と同じ ON CONFLICT ... DO UPDATE / excluded.列 が使える
		Assert.AreEqual(UpsertSql, SqlDialects.Postgre.Translate(UpsertSql));
		Assert.AreEqual(0, SqlDialects.Postgre.Inspect(UpsertSql).Count);
	}

	[TestMethod]
	public void MariaDBはUPSERTをON_DUPLICATE_KEY_UPDATEへ写像する() {
		var maria = SqlDialects.Maria.Translate(UpsertSql);
		StringAssert.Contains(maria, "ON DUPLICATE KEY UPDATE");
		StringAssert.Contains(maria, "ReserveQty = VALUES(ReserveQty)");
		StringAssert.Contains(maria, "Vdu = 1");
		Assert.IsFalse(maria.Contains("ON CONFLICT"), maria);
		Assert.IsFalse(maria.Contains("excluded."), maria);
		Assert.AreEqual(0, SqlDialects.Maria.Inspect(UpsertSql).Count);
	}

	[TestMethod]
	public void 衝突対象の列指定が無いUPSERTも写像する() {
		const string sql = "insert into T (a) values (1) ON CONFLICT DO UPDATE SET a = excluded.a";
		StringAssert.Contains(SqlDialects.Maria.Translate(sql), "ON DUPLICATE KEY UPDATE a = VALUES(a)");
	}

	[TestMethod]
	public void DO_NOTHINGは変換しない() {
		// MariaDB に等価な短い書き方が無いため対象外。未対応構文として報告される
		const string sql = "insert into T (a) values (1) ON CONFLICT(a) DO NOTHING";
		Assert.AreEqual(sql, SqlDialects.Maria.Translate(sql));
		Assert.IsTrue(SqlDialects.Maria.Inspect(sql).Any(f => f.Construct.Id == "C04-Upsert"));
	}

	[TestMethod]
	public void SQLiteはサーバ側SQLも一切書き換えない() {
		foreach (var sql in new[] { JsubRebuildSql, UpsertSql }) {
			Assert.IsTrue(ReferenceEquals(sql, SqlDialects.Sqlite.Translate(sql)));
		}
	}
}
