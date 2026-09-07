using CvBase;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Tests.CvServer;

/// <summary>
/// ConvertDbのTran商品補足マスタ生成と、明細JSONを全行保持する後処理のテスト。
/// </summary>
[TestClass]
public sealed class ConvertDbTranSupplementTests {
	const string SupplementSuffix = "（Tran用補足マスタ）";

	private ExDatabaseSqlite? _db;

	private ExDatabaseSqlite Db => _db ?? throw new AssertFailedException("Database not initialized");

	[TestInitialize]
	public void Initialize() {
		var connection = new SqliteConnection("Data Source=:memory:");
		connection.Open();
		_db = new ExDatabaseSqlite(connection) { KeepConnectionAlive = true };
	}

	[TestCleanup]
	public void Cleanup() {
		_db?.Close();
		(_db?.Connection as SqliteConnection)?.Close();
	}

	[TestMethod]
	public void CnvTranShohinSupplement_補足マスタ生成とId再設定を冪等に行いTran02は除外する() {
		CreateSupplementTables();
		var existing = new MasterShohin { Code = "EXIST001", Name = "既存商品", Vdc = 1, Vdu = 1 };
		Db.Insert(existing);

		var tooLongCode = "12345678901234567";
		var longDetailName = new string('長', 100);
		var uriageBefore = new List<Tran99Meisai> {
			CreateMeisai(10, "EXIST001", "伝票時点の既存商品", su: 1, tanka: 110, kingaku: 110, memo: "既存"),
			CreateMeisai(20, "SUPP001", "補足商品", su: 2, tanka: 220, kingaku: 440, memo: "補足"),
			CreateMeisai(30, "", "コードなし商品", su: 3, tanka: 330, kingaku: 990, memo: "空コード"),
			CreateMeisai(40, tooLongCode, "長すぎるコードの商品", su: 4, tanka: 440, kingaku: 1760, memo: "長コード"),
		};
		var choseiBefore = new List<Tran99Meisai> {
			CreateMeisai(50, "SUPP001", "別伝票の同一商品", su: 5, tanka: 550, kingaku: 2750, memo: "重複コード"),
			CreateMeisai(60, "LONGNAME", longDetailName, su: 6, tanka: 660, kingaku: 3960, memo: "長い商品名"),
		};
		Db.Insert(new Tran00Uriage { DenDay = "20260901", KakeDay = "20260901", Jmeisai = uriageBefore, Vdc = 1, Vdu = 1 });
		Db.Insert(new Tran61Chosei { DenDay = "20260902", Jmeisai = choseiBefore, Vdc = 1, Vdu = 1 });

		var materialBefore = new Tran99MaterialMeisai {
			No = 70,
			Id_Material = 700,
			Code_Material = "MAT001",
			Mei_Material = "生地",
			Id_Shohin = 0,
			Code_Shohin = "MATREL01",
			Mei_Shohin = "原価負担商品",
			Su = 7,
			Tanka = 770,
			Kingaku = 5390,
			Memo = "Tran02除外確認",
		};
		Db.Insert(new Tran02Material {
			DenDay = "20260903",
			KakeDay = "20260903",
			Jmeisai = [materialBefore],
			Vdc = 1,
			Vdu = 1,
		});

		var convertDb = new ConvertDb(Db, Db);
		var firstCount = convertDb.CnvTranShohinSupplement();

		Assert.AreEqual(4, firstCount, "追加マスタ2件＋更新伝票2件");
		var masters = Db.Fetch<MasterShohin>().OrderBy(x => x.Code).ToList();
		Assert.AreEqual(3, masters.Count, "既存1件＋補足2件。重複コードは1件に集約される");
		Assert.AreEqual(1, masters.Count(x => x.Code == "SUPP001"), "複数伝票の同一コードから補足マスタを重複作成しない");
		Assert.IsFalse(masters.Any(x => string.IsNullOrEmpty(x.Code)), "空コードの商品マスタは作成しない");
		Assert.IsNull(Db.FirstOrDefault<MasterShohin>("where Code=@0", tooLongCode), "17文字コードは作成しない");
		Assert.IsNull(Db.FirstOrDefault<MasterShohin>("where Code=@0", "MATREL01"), "Tran02Materialの関連商品は作成しない");

		var supplement = masters.Single(x => x.Code == "SUPP001");
		Assert.AreEqual("補足商品" + SupplementSuffix, supplement.Name);
		Assert.AreEqual(1L, supplement.Id_Tax, "最低限項目はMasterShohinの既定値を使う");
		Assert.AreEqual(1, supplement.IsZaiko, "最低限項目はMasterShohinの既定値を使う");
		Assert.AreEqual(MasterMeisho.KubunSize, supplement.SizeKu, "最低限項目はMasterShohinの既定値を使う");

		var longNameSupplement = masters.Single(x => x.Code == "LONGNAME");
		Assert.AreEqual(80, longNameSupplement.Name.Length, "商品名はMasterShohinの80文字制限内");
		Assert.IsTrue(longNameSupplement.Name.EndsWith(SupplementSuffix, StringComparison.Ordinal), "切り詰めても補足マスタ接尾辞を残す");

		var uriageAfter = Db.Fetch<Tran00Uriage>().Single().Jmeisai ?? [];
		var choseiAfter = Db.Fetch<Tran61Chosei>().Single().Jmeisai ?? [];
		AssertOnlyPropertyChanged(
			uriageBefore,
			uriageAfter,
			nameof(Tran99Meisai.Id_Shohin),
			(index, before, after) => {
				var expectedId = before.Code_Shohin switch {
					"EXIST001" => existing.Id,
					"SUPP001" => supplement.Id,
					_ => before.Id_Shohin,
				};
				Assert.AreEqual(expectedId, after.Id_Shohin, $"売上明細[{index}]の商品Id");
			});
		AssertOnlyPropertyChanged(
			choseiBefore,
			choseiAfter,
			nameof(Tran99Meisai.Id_Shohin),
			(index, before, after) => {
				var expectedId = before.Code_Shohin == "SUPP001" ? supplement.Id : longNameSupplement.Id;
				Assert.AreEqual(expectedId, after.Id_Shohin, $"在庫調整明細[{index}]の商品Id");
			});

		var materialAfter = Db.Fetch<Tran02Material>().Single().Jmeisai?.Single();
		Assert.IsNotNull(materialAfter);
		Assert.AreEqual(materialBefore.Id_Shohin, materialAfter.Id_Shohin, "Tran02Material.Id_Shohinは変更しない");
		Assert.AreEqual(materialBefore.Code_Shohin, materialAfter.Code_Shohin);
		Assert.AreEqual(materialBefore.Mei_Shohin, materialAfter.Mei_Shohin);
		Assert.AreEqual(materialBefore.Id_Material, materialAfter.Id_Material);

		var masterIdsBeforeSecondRun = masters.Select(x => x.Id).OrderBy(x => x).ToArray();
		var secondCount = convertDb.CnvTranShohinSupplement();

		Assert.AreEqual(0, secondCount, "2回目は追加・更新とも0件");
		CollectionAssert.AreEqual(
			masterIdsBeforeSecondRun,
			Db.Fetch<MasterShohin>().Select(x => x.Id).OrderBy(x => x).ToArray(),
			"再実行で商品マスタを増減しない");
		AssertOnlyPropertyChanged(
			uriageAfter,
			Db.Fetch<Tran00Uriage>().Single().Jmeisai ?? [],
			string.Empty,
			static (_, _, _) => { });
		AssertOnlyPropertyChanged(
			choseiAfter,
			Db.Fetch<Tran61Chosei>().Single().Jmeisai ?? [],
			string.Empty,
			static (_, _, _) => { });
	}

	[TestMethod]
	public void subCnvTranHeaderSize_一致不一致設定済みが混在しても全明細と順序を保持する() {
		Db.CreateTable(typeof(Tran00Uriage), true, false);
		Db.CreateTable(typeof(DerivedShohinColSiz), true, false);
		Db.Insert(new DerivedShohinColSiz {
			Id_Shohin = 100,
			Id_Col = 10,
			Id_Siz = 20,
			Code_Siz = "M",
			Vdc = 1,
			Vdu = 1,
		});

		var before = new List<Tran99Meisai> {
			CreateMeisai(1, "S001", "一致", su: 1, tanka: 100, kingaku: 100, memo: "更新対象", idShohin: 100, idCol: 10, idSiz: 0, codeSiz: "M"),
			CreateMeisai(2, "S001", "不一致", su: 2, tanka: 200, kingaku: 400, memo: "対応サイズなし", idShohin: 100, idCol: 10, idSiz: 0, codeSiz: "L"),
			CreateMeisai(3, "S001", "設定済み", su: 3, tanka: 300, kingaku: 900, memo: "既存Id維持", idShohin: 100, idCol: 10, idSiz: 30, codeSiz: "M"),
		};
		Db.Insert(new Tran00Uriage { DenDay = "20260904", KakeDay = "20260904", Jmeisai = before, Vdc = 1, Vdu = 1 });

		var convertDb = new ConvertDb(Db, Db);
		var firstCount = convertDb.subCnvTranHeaderSize<Tran00Uriage>();
		var after = Db.Fetch<Tran00Uriage>().Single().Jmeisai ?? [];

		Assert.AreEqual(1, firstCount, "更新対象を含む伝票は1件");
		AssertOnlyPropertyChanged(
			before,
			after,
			nameof(Tran99Meisai.Id_Siz),
			(index, expected, actual) => {
				var expectedId = index == 0 ? 20L : expected.Id_Siz;
				Assert.AreEqual(expectedId, actual.Id_Siz, $"明細[{index}]のサイズId");
			});
		Assert.AreEqual(0, convertDb.subCnvTranHeaderSize<Tran00Uriage>(), "再実行時は更新対象なし");
	}

	private void CreateSupplementTables() {
		Db.CreateTable(typeof(MasterShohin), true, false);
		Db.CreateTable(typeof(Tran00Uriage), true, false);
		Db.CreateTable(typeof(Tran61Chosei), true, false);
		Db.CreateTable(typeof(Tran02Material), true, false);
	}

	private static Tran99Meisai CreateMeisai(
		int no,
		string code,
		string name,
		int su,
		int tanka,
		long kingaku,
		string memo,
		long idShohin = 0,
		long idCol = 11,
		long idSiz = 12,
		string codeSiz = "01") => new() {
			No = no,
			Id_Shohin = idShohin,
			Code_Shohin = code,
			Mei_Shohin = name,
			JanCode = "JAN" + no,
			Id_Col = idCol,
			Code_Col = "C" + no,
			Mei_Col = "色" + no,
			Id_Siz = idSiz,
			Code_Siz = codeSiz,
			Mei_Siz = "サイズ" + no,
			Su = su,
			Tanka = tanka,
			Kingaku = kingaku,
			Jodai = tanka + 10,
			Gedai = tanka - 10,
			Nebiki00 = no,
			Nebiki01 = no + 1,
			Nebiki02 = no + 2,
			Id_Tax = 2,
			TaxRate = 8,
			Tax = no + 3,
			Id_Shain = no + 100,
			Code_Shain = "E" + no,
			Mei_Shain = "社員" + no,
			Memo = memo,
		};

	private static void AssertOnlyPropertyChanged(
		IReadOnlyList<Tran99Meisai> expected,
		IReadOnlyList<Tran99Meisai> actual,
		string changedProperty,
		Action<int, Tran99Meisai, Tran99Meisai> assertChangedProperty) {
		Assert.AreEqual(expected.Count, actual.Count, "明細件数");
		var unchangedProperties = typeof(Tran99Meisai)
			.GetProperties()
			.Where(x => x.CanRead && x.Name != changedProperty)
			.ToArray();
		for (var i = 0; i < expected.Count; i++) {
			assertChangedProperty(i, expected[i], actual[i]);
			foreach (var property in unchangedProperties) {
				Assert.AreEqual(
					property.GetValue(expected[i]),
					property.GetValue(actual[i]),
					$"明細[{i}].{property.Name}は変更しない");
			}
		}
	}
}
