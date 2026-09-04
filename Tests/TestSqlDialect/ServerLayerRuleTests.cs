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

	// ---- 棚卸(StocktakeDb) ----

	/// <summary>
	/// StocktakeDb.BuildBookQtyAsOfSql が組む帳簿在庫の逆算SQL。派生表の中で json_each を CROSS JOIN し、
	/// UNION ALL で複数伝票分の明細展開を束ねる。key列は参照しないので ORDINALITY は付かない。
	/// </summary>
	const string BookQtyAsOfSql = """
SELECT
  c.Id_Shohin AS Id_Shohin,
  c.Id_Col    AS Id_Col,
  c.Id_Siz    AS Id_Siz,
  c.Su - COALESCE(v.Su, 0) AS BookQty
FROM (
  SELECT Id_Shohin, Id_Col, Id_Siz, SUM(Su) AS Su
  FROM SummaryStock
  WHERE Id_Soko = 1
    AND SumMonth <= @2
  GROUP BY Id_Shohin, Id_Col, Id_Siz
) AS c
LEFT JOIN (
  SELECT Id_Shohin, Id_Col, Id_Siz, SUM(Su) AS Su
  FROM (
  SELECT
    json_extract(j.value, '$.Id_Shohin') AS Id_Shohin,
    json_extract(j.value, '$.Id_Col')    AS Id_Col,
    json_extract(j.value, '$.Id_Siz')    AS Id_Siz,
    json_extract(j.value, '$.Su')*t.CalcFlag*1 AS Su
  FROM Tran00Uriage AS t
       CROSS JOIN json_each(t.Jmeisai) AS j
       LEFT JOIN MasterTokui AS mt ON mt.Id = t.Id_Soko
       LEFT JOIN MasterShohin AS ms ON ms.Id = json_extract(j.value, '$.Id_Shohin')
  WHERE t.Id_Soko = 1
    AND t.DenDay > @0
    AND t.DenDay <= @1
    AND json_type(t.Jmeisai) = 'array'
    AND COALESCE(mt.IsZaiko, 1) = 1
    AND COALESCE(ms.IsZaiko, 1) = 1
  UNION ALL
  SELECT
    json_extract(j.value, '$.Id_Shohin') AS Id_Shohin,
    json_extract(j.value, '$.Id_Col')    AS Id_Col,
    json_extract(j.value, '$.Id_Siz')    AS Id_Siz,
    json_extract(j.value, '$.Su')*t.CalcFlag*-1 AS Su
  FROM Tran03Shiire AS t
       CROSS JOIN json_each(t.Jmeisai) AS j
       LEFT JOIN MasterTokui AS mt ON mt.Id = t.Id_Soko
       LEFT JOIN MasterShohin AS ms ON ms.Id = json_extract(j.value, '$.Id_Shohin')
  WHERE t.Id_Soko = 1
    AND t.DenDay > @0
    AND t.DenDay <= @1
    AND json_type(t.Jmeisai) = 'array'
    AND COALESCE(mt.IsZaiko, 1) = 1
    AND COALESCE(ms.IsZaiko, 1) = 1
  ) AS d
  GROUP BY Id_Shohin, Id_Col, Id_Siz
) AS v ON v.Id_Shohin = c.Id_Shohin AND v.Id_Col = c.Id_Col AND v.Id_Siz = c.Id_Siz
ORDER BY c.Id_Shohin, c.Id_Col, c.Id_Siz
""";

	/// <summary>
	/// StocktakeDb.StartStocktakeOne が実棚だけのSKUを拾うために使う、Tran60Tana の json_each を
	/// CROSS JOIN する形。同じく key列は参照しない。
	/// </summary>
	const string TanaOnlySkuSql = """
INSERT INTO TempStocktakeBookQty (Id_Shohin, Id_Col, Id_Siz, BookQty)
SELECT s.Id_Shohin, s.Id_Col, s.Id_Siz, 0
FROM (
  SELECT DISTINCT
    json_extract(j.value, '$.Id_Shohin') AS Id_Shohin,
    json_extract(j.value, '$.Id_Col')    AS Id_Col,
    json_extract(j.value, '$.Id_Siz')    AS Id_Siz
  FROM Tran60Tana AS t
       CROSS JOIN json_each(t.Jmeisai) AS j
  WHERE t.Id_Soko = 1
    AND t.DenDay = @0
    AND json_type(t.Jmeisai) = 'array'
) AS s
WHERE NOT EXISTS (
  SELECT 1 FROM TempStocktakeBookQty AS x
  WHERE x.Id_Shohin = s.Id_Shohin AND x.Id_Col = s.Id_Col AND x.Id_Siz = s.Id_Siz
)
""";

	/// <summary>StocktakeDb.StoreActualQty が組む、実棚数をSummaryStock.ActualQtyへ反映するSQL。カンマ結合のFROM句</summary>
	const string StoreActualQtySql = """
UPDATE SummaryStock
SET ActualQty = ifnull((
      SELECT SUM(cast(ifnull(json_extract(m.value,'$.Su'),0) as integer))
      FROM Tran60Tana h, json_each(h.Jmeisai) m
      WHERE json_valid(h.Jmeisai)
        AND h.DenDay = @1
        AND h.Id_Soko = SummaryStock.Id_Soko
        AND cast(ifnull(json_extract(m.value,'$.Id_Shohin'),0) as integer) = SummaryStock.Id_Shohin
        AND cast(ifnull(json_extract(m.value,'$.Id_Col'),0) as integer)    = SummaryStock.Id_Col
        AND cast(ifnull(json_extract(m.value,'$.Id_Siz'),0) as integer)    = SummaryStock.Id_Siz
    ), BookQty),
    Vdu = 1
WHERE SumMonth = @0
  AND Id_Soko = 1
""";

	/// <summary>
	/// StocktakeDb.StartStocktakeOne の行補完INSERT。ON CONFLICTを使わない素のINSERT/SELECTなので
	/// UPSERTの書き換え対象にはならない(4方言そのままで通る)。
	/// </summary>
	const string BookQtyRowCompletionInsertSql = """
INSERT INTO SummaryStock
  (SumMonth, Id_Soko, Id_Shohin, Id_Col, Id_Siz,
   Su, ReserveQty, CumulativeSu, InQty, OutQty, TransitQty, AdjustQty,
   StocktakeDdate, BookQty, ActualQty, Vdc, Vdu)
SELECT @1, 1, t.Id_Shohin, t.Id_Col, t.Id_Siz,
   0, 0, 0, 0, 0, 0, 0,
   @0, t.BookQty, t.BookQty, 1, 1
FROM TempStocktakeBookQty AS t
WHERE NOT EXISTS (
  SELECT 1 FROM SummaryStock AS s
  WHERE s.SumMonth = @1 AND s.Id_Soko = 1
    AND s.Id_Shohin = t.Id_Shohin AND s.Id_Col = t.Id_Col AND s.Id_Siz = t.Id_Siz
)
""";

	/// <summary>棚卸の帳簿在庫逆算SQLを各方言へ変換できる</summary>
	[TestMethod]
	public void 棚卸の帳簿在庫逆算SQLを各方言へ変換できる() {
		var pg = SqlDialects.Postgre.Translate(BookQtyAsOfSql);
		StringAssert.Contains(pg, "jsonb_array_elements");
		Assert.IsFalse(pg.Contains("ORDINALITY"), "key列を参照していないのでORDINALITYは付かない");
		Assert.IsFalse(pg.Contains("json_each"), pg);
		Assert.AreEqual(0, SqlDialects.Postgre.Inspect(BookQtyAsOfSql).Count);

		var maria = SqlDialects.Maria.Translate(BookQtyAsOfSql);
		StringAssert.Contains(maria, "JSON_TABLE");
		Assert.IsFalse(maria.Contains("ORDINALITY"), "key列を参照していないのでORDINALITYは付かない");
		Assert.IsFalse(maria.Contains("json_each"), maria);
		Assert.AreEqual(0, SqlDialects.Maria.Inspect(BookQtyAsOfSql).Count);
	}

	/// <summary>棚卸開始処理が実棚だけのSKUを拾うSQL(json_each の CROSS JOIN)も各方言へ変換できる</summary>
	[TestMethod]
	public void 棚卸の実棚専用SKU補完SQLを各方言へ変換できる() {
		var pg = SqlDialects.Postgre.Translate(TanaOnlySkuSql);
		StringAssert.Contains(pg, "jsonb_array_elements");
		Assert.IsFalse(pg.Contains("ORDINALITY"));
		Assert.IsFalse(pg.Contains("json_each"), pg);
		Assert.AreEqual(0, SqlDialects.Postgre.Inspect(TanaOnlySkuSql).Count);

		var maria = SqlDialects.Maria.Translate(TanaOnlySkuSql);
		StringAssert.Contains(maria, "JSON_TABLE");
		Assert.IsFalse(maria.Contains("ORDINALITY"));
		Assert.IsFalse(maria.Contains("json_each"), maria);
		Assert.AreEqual(0, SqlDialects.Maria.Inspect(TanaOnlySkuSql).Count);
	}

	/// <summary>実棚数の反映SQL(カンマ結合のFROM句の json_each)を各方言へ変換できる</summary>
	[TestMethod]
	public void 実棚数の反映SQLを各方言へ変換できる() {
		var pg = SqlDialects.Postgre.Translate(StoreActualQtySql);
		StringAssert.Contains(pg, "jsonb_array_elements");
		Assert.IsFalse(pg.Contains("json_each"), pg);
		Assert.AreEqual(0, SqlDialects.Postgre.Inspect(StoreActualQtySql).Count);

		var maria = SqlDialects.Maria.Translate(StoreActualQtySql);
		StringAssert.Contains(maria, "JSON_TABLE");
		Assert.IsFalse(maria.Contains("json_each"), maria);
		Assert.AreEqual(0, SqlDialects.Maria.Inspect(StoreActualQtySql).Count);
	}

	/// <summary>棚卸開始処理の行補完INSERTはON CONFLICTを使っていないのでUPSERTの書き換えが起きない</summary>
	[TestMethod]
	public void 棚卸の行補完INSERTはUPSERT書き換えの対象にならない() {
		Assert.AreEqual(BookQtyRowCompletionInsertSql, SqlDialects.Postgre.Translate(BookQtyRowCompletionInsertSql));
		Assert.AreEqual(BookQtyRowCompletionInsertSql, SqlDialects.Maria.Translate(BookQtyRowCompletionInsertSql));
		Assert.AreEqual(0, SqlDialects.Postgre.Inspect(BookQtyRowCompletionInsertSql).Count);
		Assert.AreEqual(0, SqlDialects.Maria.Inspect(BookQtyRowCompletionInsertSql).Count);
	}
}
