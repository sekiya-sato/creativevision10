using System.Data;
using System.IO;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using Microsoft.Data.Sqlite;

namespace UatVm.Seed;

/// <summary>UAT-10 原価4項目の専用データを隔離して投入する。</summary>
public static class CostUatSeeder {
	public const string EmployeeCode = "UATVM-COST-SH";
	public const string ShiireCode = "UATVM-COST-SR";
	public const string NormalCode = "UATVM-COST-NORMAL";
	public const string ConsumptionCode = "UATVM-COST-CONSUMP";
	public const string MaterialCode = "UATVM-COST-MTL";
	public const string Month = "202609";
	public const string OpeningMonth = "202608";
	public const string PurchaseDay = "20260905";
	public const string ReturnDay = "20260910";
	public const string SundryDay = "20260906";
	public const string SaleDay = "20260915";
	public const string ConsumptionSaleDay = "20260916";
	public const string SaleReturnDay = "20260920";

	public sealed record Result(long EmployeeId, long ShiireId, long NormalId, long ConsumptionId, string Month);

	public static Result Seed(string dbPath, Action<string> trace) {
		ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
		ArgumentNullException.ThrowIfNull(trace);
		if (!File.Exists(dbPath)) throw new FileNotFoundException("対象DBが見つかりません。", dbPath);
		var cs = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString();
		using var connection = new SqliteConnection(cs);
		connection.Open();
		var db = new ExDatabaseSqlite(connection) { KeepConnectionAlive = true };
		var sys = db.Fetch<MasterSysman>("order by Id").FirstOrDefault() ?? throw new InvalidOperationException("システムマスタがありません。");
		sys.CostMethod = (int)EnumCostMethod.TotalAverage;
		db.Update(sys);
		var employee = EnsureEmployee(db);
		var shiire = EnsureShiire(db, employee);
		var material = EnsureMaterial(db);
		var normal = EnsureNormal(db);
		var consumption = EnsureConsumption(db, shiire);
		Clean(db, shiire.Id, normal.Id, consumption.Id);
		try {
			db.BeginTransaction(IsolationLevel.Serializable);
			InsertOpening(db, normal.Id);
			InsertPurchase(db, PurchaseDay, EnumShiire.Shiire, normal.Id, 20, 100_000, shiire.Id);
			InsertPurchase(db, ReturnDay, EnumShiire.Henpin, normal.Id, 5, 25_000, shiire.Id);
			InsertSundry(db, material.Id, normal.Id, shiire.Id);
			InsertSale(db, SaleDay, normal.Id, 6, 8_000);
			InsertSale(db, ConsumptionSaleDay, consumption.Id, 3, 2_000);
			InsertSale(db, SaleReturnDay, normal.Id, 1, 8_000, EnumUri00.Henpin);
			db.CompleteTransaction();
		} catch { db.AbortTransaction(); throw; }
		trace($"UAT-10専用データ: 通常商品={NormalCode} 消化仕入商品={ConsumptionCode} 仕入/返品=100,000/25,000 諸掛=100 売上/返品を投入");
		return new Result(employee.Id, shiire.Id, normal.Id, consumption.Id, Month);
	}

	private static MasterShain EnsureEmployee(ExDatabaseSqlite db) {
		var row = db.Fetch<MasterShain>("where Code=@0", EmployeeCode).FirstOrDefault();
		if (row != null) return row;
		row = new MasterShain { Code = EmployeeCode, Name = "UAT-10原価担当", Ryaku = EmployeeCode };
		db.Insert(row); return row;
	}

	private static MasterShiire EnsureShiire(ExDatabaseSqlite db, MasterShain employee) {
		var row = db.Fetch<MasterShiire>("where Code=@0", ShiireCode).FirstOrDefault();
		if (row != null) return row;
		row = new MasterShiire { Code = ShiireCode, Name = "UAT-10原価仕入先", Ryaku = ShiireCode, Shime1 = 99, PayDay = 99, Id_Shain = employee.Id,
			VShain = new CodeNameView(employee.Id, employee.Code, employee.Name) };
		db.Insert(row); return row;
	}

	private static MasterMaterial EnsureMaterial(ExDatabaseSqlite db) {
		var row = db.Fetch<MasterMaterial>("where Code=@0", MaterialCode).FirstOrDefault();
		if (row != null) return row;
		row = new MasterMaterial { Code = MaterialCode, Name = "UAT-10諸掛" }; db.Insert(row); return row;
	}

	private static MasterShohin EnsureNormal(ExDatabaseSqlite db) {
		var row = db.Fetch<MasterShohin>("where Code=@0", NormalCode).FirstOrDefault();
		if (row != null) return row;
		row = new MasterShohin { Code = NormalCode, Name = "UAT-10通常商品", IsZaiko = 1, PurchaseType = (int)EnumPurchaseType.Normal, TankaGenka = 5000 };
		db.Insert(row); return row;
	}

	private static MasterShohin EnsureConsumption(ExDatabaseSqlite db, MasterShiire shiire) {
		var row = db.Fetch<MasterShohin>("where Code=@0", ConsumptionCode).FirstOrDefault();
		if (row != null) return row;
		row = new MasterShohin { Code = ConsumptionCode, Name = "UAT-10消化仕入商品", IsZaiko = 1, PurchaseType = (int)EnumPurchaseType.Consumption,
			Id_ConsignmentShiire = shiire.Id, ConsumptionCalcType = (int)EnumConsumptionCalcType.CostBased, ConsumptionRoundingUnit = 1,
			ConsumptionRounding = (int)EnumRounding.Round, TankaShiire = 500 };
		db.Insert(row); return row;
	}

	private static void Clean(ExDatabaseSqlite db, long shiireId, long normalId, long consumptionId) {
		db.Execute("DELETE FROM Tran03Shiire WHERE Id_Shiire=@0 OR (json_valid(Jmeisai)=1 AND EXISTS (SELECT 1 FROM json_each(Jmeisai) j WHERE CAST(json_extract(j.value,'$.Id_Shohin') AS INTEGER) IN (@1,@2)))", shiireId, normalId, consumptionId);
		db.Execute("DELETE FROM Tran02Material WHERE Id_Shiire=@0", shiireId);
		db.Execute("DELETE FROM Tran00Uriage WHERE json_valid(Jmeisai)=1 AND EXISTS (SELECT 1 FROM json_each(Jmeisai) j WHERE CAST(json_extract(j.value,'$.Id_Shohin') AS INTEGER) IN (@0,@1))", normalId, consumptionId);
		db.Execute("DELETE FROM SummaryStock WHERE Id_Shohin IN (@0,@1)", normalId, consumptionId);
		db.Execute("DELETE FROM SummaryKaiKake WHERE Id_Shiire=@0", shiireId);
		db.Execute("DELETE FROM TranGenka WHERE Id_Shohin IN (@0,@1)", normalId, consumptionId);
		db.Execute("DELETE FROM TranConsumptionPurchaseLink WHERE Id_Shohin IN (@0,@1)", normalId, consumptionId);
	}

	private static void InsertOpening(ExDatabaseSqlite db, long id) => db.Insert(new SummaryStock { SumMonth = OpeningMonth, Id_Soko = 1, Id_Shohin = id, Su = 10 });

	private static void InsertPurchase(ExDatabaseSqlite db, string day, EnumShiire kind, long product, int qty, long amount, long shiire) => db.Insert(new Tran03Shiire {
		DenDay = day, KakeDay = day, Id_Soko = 1, Id_Shiire = shiire, IsStock = 1, IsPay = 1, Kubun = (int)kind,
		Jmeisai = [new Tran99Meisai { No = 1, Id_Shohin = product, Su = qty, Tanka = (int)(amount / qty), Kingaku = amount }]
	});

	private static void InsertSundry(ExDatabaseSqlite db, long material, long product, long shiire) => db.Insert(new Tran02Material {
		DenDay = SundryDay, KakeDay = SundryDay, Id_Shiire = shiire, Kubun = (int)EnumShiire.Shiire,
		Jmeisai = [new Tran99MaterialMeisai { No = 1, Id_Material = material, Id_Shohin = product, Su = 1, Tanka = 100, Kingaku = 100 }]
	});

	private static void InsertSale(ExDatabaseSqlite db, string day, long product, int qty, int price, EnumUri00 kind = EnumUri00.Uriage) => db.Insert(new Tran00Uriage {
		DenDay = day, KakeDay = day, Id_Soko = 1, Kubun = (int)kind,
		Jmeisai = [new Tran99Meisai { No = 1, Id_Shohin = product, Su = qty, Tanka = price, Kingaku = (long)qty * price }]
	});
}
