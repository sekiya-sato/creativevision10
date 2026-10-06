using System;
using System.Linq;
using System.Threading.Tasks;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Tests.CvServer;

/// <summary>ポイント制度の実DDL・保存型・既存ランク移行を確認する。</summary>
[TestClass]
public class PointSchemaTests {
	private SqliteConnection? _connection;
	private ExDatabaseSqlite? _db;
	private ExDatabaseSqlite Db => _db ?? throw new AssertFailedException("DB未初期化");

	[TestInitialize]
	public void Initialize() {
		_connection = new SqliteConnection("Data Source=:memory:");
		_connection.Open();
		_db = new ExDatabaseSqlite(_connection) { KeepConnectionAlive = true };
	}

	[TestCleanup]
	public void Cleanup() {
		_db?.Close();
		_connection?.Dispose();
	}

	[TestMethod]
	public void Event_SameCustomerDayDifferentSlips_AcceptsBothAndRejectsDuplicateKey() {
		Assert.IsTrue(Db.CreateTable<TranPointEvent>());
		Db.Insert(new TranPointEvent { EventKey = "101:grant", Id_Customer = 7, DenDay = "20261006", Id_Tenuri = 101, PointDelta = 10 });
		Db.Insert(new TranPointEvent { EventKey = "102:grant", Id_Customer = 7, DenDay = "20261006", Id_Tenuri = 102, PointDelta = 20 });
		Assert.ThrowsExactly<SqliteException>(() => Db.Insert(new TranPointEvent { EventKey = "101:grant", Id_Customer = 8, DenDay = "20261007", Id_Tenuri = 103 }));
		var events = Db.Fetch<TranPointEvent>("ORDER BY Id_Tenuri");
		CollectionAssert.AreEqual(new long[] { 101, 102 }, events.Select(x => x.Id_Tenuri).ToArray());
		Assert.AreEqual(30L, events.Sum(x => x.PointDelta));
	}

	[TestMethod]
	public void Definitions_LongValuesBeyondIntAndDoublePrecision_RoundTripExactly() {
		const long large = 9_007_199_254_740_993;
		Assert.IsTrue(Db.CreateTable<MasterPointBase>());
		Assert.IsTrue(Db.CreateTable<MasterPointRank>());
		Assert.IsTrue(Db.CreateTable<MasterPointBonus>());
		Assert.IsTrue(Db.CreateTable<TranPointEvent>());
		Db.Insert(new MasterPointBase { Code = "BASE", PointUnitPrice = large, PointAmountProper = large - 2, PointAmountSale = large - 4 });
		Db.Insert(new MasterPointRank { Id_PointBase = large, Kubun = 3, PointUnitPrice = large, PointAmountProper = large - 2, PointAmountSale = large - 4 });
		Db.Insert(new MasterPointBonus { Code = "BONUS", Id_PointBase = large, MinimumKingaku = large, PointAmount = large - 2 });
		Db.Insert(new TranPointEvent { EventKey = "large:use", Id_Customer = large, Id_Tenuri = large - 2, Id_PointBase = large, Id_PointRank = large - 2, Id_PointBonus = large - 4, PointDelta = -large });
		var basis = Db.Fetch<MasterPointBase>("").Single();
		var rank = Db.Fetch<MasterPointRank>("").Single();
		var bonus = Db.Fetch<MasterPointBonus>("").Single();
		var ev = Db.Fetch<TranPointEvent>("").Single();
		CollectionAssert.AreEqual(new[] { large, large - 2, large - 4 }, new[] { basis.PointUnitPrice, basis.PointAmountProper, basis.PointAmountSale });
		CollectionAssert.AreEqual(new[] { large, large - 2, large - 4 }, new[] { rank.PointUnitPrice, rank.PointAmountProper, rank.PointAmountSale });
		Assert.AreEqual(large, bonus.Id_PointBase);
		Assert.AreEqual(large, bonus.MinimumKingaku);
		Assert.AreEqual(large - 2, bonus.PointAmount);
		CollectionAssert.AreEqual(new[] { large, large - 2, large, large - 2, large - 4, -large }, new[] { ev.Id_Customer, ev.Id_Tenuri, ev.Id_PointBase, ev.Id_PointRank, ev.Id_PointBonus, ev.PointDelta });
	}

	[TestMethod]
	public void Rank_SameCodeDifferentBase_AcceptsBothAndRejectsDuplicateWithinBase() {
		Assert.IsTrue(Db.CreateTable<MasterPointRank>());
		Db.Insert(new MasterPointRank { Id_PointBase = 10, Kubun = 2, Name = "シルバー" });
		Db.Insert(new MasterPointRank { Id_PointBase = 20, Kubun = 2, Name = "シルバー新版" });
		Assert.ThrowsExactly<SqliteException>(() => Db.Insert(new MasterPointRank { Id_PointBase = 10, Kubun = 2 }));
		Assert.AreEqual(2, Db.Fetch<MasterPointRank>("").Count);
	}

	[TestMethod]
	public async Task Initialize_OldRank_PreservesValuesAndReplacesUniqueIndex() {
		Db.Execute("CREATE TABLE MasterPointRank(Id INTEGER PRIMARY KEY AUTOINCREMENT,Vdc BIGINT NOT NULL DEFAULT 0,Vdu BIGINT NOT NULL DEFAULT 0,Kubun INTEGER NOT NULL DEFAULT 0,Name TEXT NOT NULL DEFAULT '',PointUnitPrice INTEGER NOT NULL DEFAULT 100,PointAmountProper INTEGER NOT NULL DEFAULT 1,PointAmountSale INTEGER NOT NULL DEFAULT 1)");
		Db.Execute("CREATE UNIQUE INDEX MasterPointRank_uk1 ON MasterPointRank(Kubun)");
		Db.Execute("INSERT INTO MasterPointRank(Id,Vdc,Vdu,Kubun,Name,PointUnitPrice,PointAmountProper,PointAmountSale) VALUES(41,101,202,2,'旧シルバー',250,3,4)");
		Assert.IsTrue(Db.CreateTable<SysUpdateDb>());
		Db.Insert(new SysUpdateDb { DbVersion = 26_10_05_02 });
		Assert.IsTrue(await new DefineDataTable().InitializeAsync(Db, false));
		var old = Db.Fetch<MasterPointRank>("WHERE Id=41").Single();
		Assert.AreEqual(0L, old.Id_PointBase);
		Assert.AreEqual("旧シルバー", old.Name);
		CollectionAssert.AreEqual(new long[] { 101, 202, 250, 3, 4 }, new[] { old.Vdc, old.Vdu, old.PointUnitPrice, old.PointAmountProper, old.PointAmountSale });
		Db.Insert(new MasterPointRank { Id_PointBase = 10, Kubun = 2 });
		Assert.ThrowsExactly<SqliteException>(() => Db.Insert(new MasterPointRank { Id_PointBase = 0, Kubun = 2 }));
		Assert.IsTrue(await new DefineDataTable().InitializeAsync(Db, false), "2回目の起動でも索引と移行が成立する");
		Assert.IsTrue(Db.IsExistTable(typeof(MasterPointBase)));
		Assert.IsTrue(Db.IsExistTable(typeof(MasterPointBonus)));
		Assert.IsTrue(Db.IsExistTable(typeof(TranPointEvent)));
	}

	[TestMethod]
	public async Task Initialize_NewDatabase_CreatesPointTablesAndUniqueIndexes() {
		Assert.IsTrue(await new DefineDataTable().InitializeAsync(Db, false));
		Db.Insert(new MasterPointBase { Code = "STANDARD", Version = 1 });
		Db.Insert(new MasterPointBase { Code = "STANDARD", Version = 2 });
		Assert.ThrowsExactly<SqliteException>(() => Db.Insert(new MasterPointBase { Code = "STANDARD", Version = 1 }));
		Db.Insert(new MasterPointBonus { Code = "BIRTHDAY", Version = 1 });
		Assert.ThrowsExactly<SqliteException>(() => Db.Insert(new MasterPointBonus { Code = "BIRTHDAY", Version = 1 }));
		Db.Insert(new TranPointEvent { EventKey = "new:grant" });
		Assert.ThrowsExactly<SqliteException>(() => Db.Insert(new TranPointEvent { EventKey = "new:grant" }));
		Assert.AreEqual(2, Db.Fetch<MasterPointBase>("").Count);
		Assert.AreEqual(1, Db.Fetch<MasterPointBonus>("").Count);
	}

	[TestMethod]
	public void EnumAdapters_AllDefinedValues_ConvertBothWaysAndStayOutOfPersistence() {
		object[] definitions = [new MasterPointBase(), new MasterPointBonus(), new TranPointEvent()];
		foreach (var definition in definitions) {
			var type = definition.GetType();
			foreach (var adapter in type.GetProperties().Where(p => p.Name.StartsWith("En", StringComparison.Ordinal) && p.PropertyType.IsEnum)) {
				var column = type.GetProperty(adapter.Name[2..])!;
				foreach (var value in Enum.GetValues(adapter.PropertyType)) {
					adapter.SetValue(definition, value);
					Assert.AreEqual(Convert.ToInt32(value), column.GetValue(definition), adapter.Name);
					adapter.SetValue(definition, Enum.ToObject(adapter.PropertyType, 0));
					column.SetValue(definition, Convert.ToInt32(value));
					Assert.AreEqual(value, adapter.GetValue(definition), adapter.Name);
				}
				Assert.IsFalse(Db.GetSqlCreateTable(type).Contains(adapter.Name, StringComparison.Ordinal), adapter.Name);
				Assert.IsFalse(JsonConvert.SerializeObject(definition).Contains('"' + adapter.Name + '"', StringComparison.Ordinal), adapter.Name);
			}
		}
		Assert.AreEqual(EnumRounding.Floor, new MasterPointBase().EnRounding);
	}
}
