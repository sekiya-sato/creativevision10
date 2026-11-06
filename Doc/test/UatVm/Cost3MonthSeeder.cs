using System.Data;
using System.IO;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using Microsoft.Data.Sqlite;

namespace UatVm.Seed;

/// <summary>
/// 原価3か月UAT（2026/11〜2027/01）の専用データを隔離して投入する。
/// 期待値は Doc/test/2026-11-06_原価3か月UAT手順.md の2章・3章を正典とする。
/// </summary>
public static class Cost3MonthSeeder {
	public const string Prefix = "UATVM-C3M-";
	public const string EmployeeCode = Prefix + "SH";
	public const string ShiireCode = Prefix + "SR";
	public const string MaterialCode = Prefix + "MTL";
	public const string CodeA = Prefix + "A";
	public const string CodeB = Prefix + "B";
	public const string CodeC = Prefix + "C";
	public const string CodeD = Prefix + "D";
	public const string CodeF = Prefix + "F";
	public const string CodeG = Prefix + "G";
	public const string OpeningMonth = "202610";
	public const string Month1 = "202611";
	public const string Month2 = "202612";
	public const string Month3 = "202701";

	public const string WarehouseCode = Prefix + "WH";

	public sealed record Result(long EmployeeId, long ShiireId, long A, long B, long C, long D, long F, long G);

	/// <summary>シードで決めた在庫対象倉庫。手順5の追加仕入（<see cref="NewCascadePurchase"/>）も同じ倉庫に入れる。</summary>
	private static long _sokoId;

	private static MasterTokui EnsureWarehouse(ExDatabaseSqlite db, MasterShain employee) {
		var row = db.Fetch<MasterTokui>("where Code=@0", WarehouseCode).FirstOrDefault();
		if (row != null) return row;
		row = new MasterTokui { Code = WarehouseCode, Name = "C3M原価倉庫", Ryaku = WarehouseCode, TenType = 0, IsZaiko = 1,
			Id_Shain = employee.Id, VShain = new CodeNameView(employee.Id, employee.Code, employee.Name) };
		db.Insert(row); return row;
	}

	public static Result Seed(string dbPath, Action<string> trace) {
		ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
		ArgumentNullException.ThrowIfNull(trace);
		if (!File.Exists(dbPath)) throw new FileNotFoundException("対象DBが見つかりません。", dbPath);
		var cs = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString();
		using var connection = new SqliteConnection(cs);
		connection.Open();
		var db = new ExDatabaseSqlite(connection) { KeepConnectionAlive = true };
		var sys = db.Fetch<MasterSysman>("order by Id").FirstOrDefault() ?? throw new InvalidOperationException("システムマスタがありません。");
		if (sys.ShimeBi != (int)EnumShime.DayLast) throw new InvalidOperationException($"自社締日が末日ではありません（ShimeBi={sys.ShimeBi}）。期待値は末日締めが前提です。");
		sys.CostMethod = (int)EnumCostMethod.TotalAverage;
		db.Update(sys);

		var employee = EnsureEmployee(db);
		var shiire = EnsureShiire(db, employee);
		var material = EnsureMaterial(db);
		// 開発DBの Id=1 は在庫対象外（MasterTokui.IsZaiko=0）のため、在庫対象の専用倉庫を使う。
		_sokoId = EnsureWarehouse(db, employee).Id;
		var a = EnsureNormal(db, CodeA, "C3M通常A", 1000);
		var b = EnsureNormal(db, CodeB, "C3M通常B", 2000);
		var c = EnsureNormal(db, CodeC, "C3M原価未設定C", 0);
		var d = EnsureNormal(db, CodeD, "C3M負在庫D", 500);
		var f = EnsureConsumption(db, CodeF, "C3M消化F原価代用", shiire, EnumConsumptionCalcType.CostBased, 0, 1, 500);
		var g = EnsureConsumption(db, CodeG, "C3M消化G掛率", shiire, EnumConsumptionCalcType.RateBased, 6000, 10, 0);
		long[] ids = [a.Id, b.Id, c.Id, d.Id, f.Id, g.Id];
		Clean(db, shiire.Id, ids);
		try {
			db.BeginTransaction(IsolationLevel.Serializable);
			// 前月在庫（2026/10）。再集計範囲（2026/11〜2027/01）の外に置く。
			db.Insert(new SummaryStock { SumMonth = OpeningMonth, Id_Soko = _sokoId, Id_Shohin = a.Id, Su = 10 });
			db.Insert(new SummaryStock { SumMonth = OpeningMonth, Id_Soko = _sokoId, Id_Shohin = b.Id, Su = 5 });

			// 2026/11
			Purchase(db, "20261105", EnumShiire.Shiire, a.Id, 20, 22_000, shiire.Id);
			Purchase(db, "20261110", EnumShiire.Shiire, b.Id, 5, 11_000, shiire.Id);
			Purchase(db, "20261112", EnumShiire.Shiire, c.Id, 3, 1_000, shiire.Id);
			Purchase(db, "20261115", EnumShiire.Henpin, a.Id, 4, 4_400, shiire.Id);
			Sundry(db, "20261112", EnumShiire.Shiire, material.Id, a.Id, 300, shiire.Id);
			Sale(db, "20261116", f.Id, 3, 1_000);
			Sale(db, "20261117", g.Id, 2, 1_234);
			Sale(db, "20261118", d.Id, 5, 900);
			Sale(db, "20261120", a.Id, 12, 1_800);
			Sale(db, "20261125", a.Id, 2, 1_800, EnumUri00.Henpin);

			// 2026/12（Bは同日2伝票。最終仕入は後の伝票Idを採用する）
			Purchase(db, "20261208", EnumShiire.Shiire, a.Id, 10, 12_500, shiire.Id);
			Purchase(db, "20261210", EnumShiire.Shiire, b.Id, 6, 11_400, shiire.Id);
			Purchase(db, "20261210", EnumShiire.Shiire, b.Id, 4, 7_800, shiire.Id);
			Purchase(db, "20261215", EnumShiire.Shiire, d.Id, 3, 1_800, shiire.Id);
			Purchase(db, "20261220", EnumShiire.Shiire, a.Id, 5, 6_500, shiire.Id);
			Sundry(db, "20261209", EnumShiire.Shiire, material.Id, a.Id, 200, shiire.Id);
			Sundry(db, "20261222", EnumShiire.Henpin, material.Id, a.Id, 50, shiire.Id);
			Sale(db, "20261211", f.Id, 2, 1_000);
			Sale(db, "20261218", a.Id, 8, 1_800);
			Sale(db, "20261219", b.Id, 4, 3_000);
			Sale(db, "20261228", f.Id, 1, 1_000, EnumUri00.Henpin);

			// 2027/01
			Purchase(db, "20270112", EnumShiire.Shiire, b.Id, 4, 6_800, shiire.Id);
			Purchase(db, "20270115", EnumShiire.Shiire, c.Id, 2, 1_003, shiire.Id);
			Sale(db, "20270110", a.Id, 3, 1_800);
			Sale(db, "20270120", f.Id, 4, 1_000);
			db.CompleteTransaction();
		} catch { db.AbortTransaction(); throw; }
		trace($"原価3か月UAT専用データ: 商品={string.Join(",", CodeA, CodeB, CodeC, CodeD, CodeF, CodeG)} 期間={Month1}〜{Month3} 原価方式=総平均");
		return new Result(employee.Id, shiire.Id, a.Id, b.Id, c.Id, d.Id, f.Id, g.Id);
	}

	/// <summary>手順5（T-04）でシナリオがgRPC経由で追加する仕入。シードでは投入しない。</summary>
	public static Tran03Shiire NewCascadePurchase(long productA, long shiireId) => NewPurchase("20261128", EnumShiire.Shiire, productA, 6, 6_000, shiireId);

	private static MasterShain EnsureEmployee(ExDatabaseSqlite db) {
		var row = db.Fetch<MasterShain>("where Code=@0", EmployeeCode).FirstOrDefault();
		if (row != null) return row;
		row = new MasterShain { Code = EmployeeCode, Name = "C3M原価担当", Ryaku = EmployeeCode };
		db.Insert(row); return row;
	}

	private static MasterShiire EnsureShiire(ExDatabaseSqlite db, MasterShain employee) {
		var row = db.Fetch<MasterShiire>("where Code=@0", ShiireCode).FirstOrDefault();
		if (row != null) return row;
		row = new MasterShiire { Code = ShiireCode, Name = "C3M原価仕入先", Ryaku = ShiireCode, Shime1 = 99, PayDay = 99, Id_Shain = employee.Id,
			VShain = new CodeNameView(employee.Id, employee.Code, employee.Name) };
		db.Insert(row); return row;
	}

	private static MasterMaterial EnsureMaterial(ExDatabaseSqlite db) {
		var row = db.Fetch<MasterMaterial>("where Code=@0", MaterialCode).FirstOrDefault();
		if (row != null) return row;
		row = new MasterMaterial { Code = MaterialCode, Name = "C3M諸掛" }; db.Insert(row); return row;
	}

	/// <summary>通常商品。既存なら初期原価を戻す（再シード時に前回の原価更新結果を持ち越さない）。</summary>
	private static MasterShohin EnsureNormal(ExDatabaseSqlite db, string code, string name, int cost) {
		var row = db.Fetch<MasterShohin>("where Code=@0", code).FirstOrDefault();
		if (row == null) {
			row = new MasterShohin { Code = code, Name = name, IsZaiko = 1, PurchaseType = (int)EnumPurchaseType.Normal, TankaGenka = cost };
			db.Insert(row); return row;
		}
		row.TankaGenka = cost;
		db.Update(row);
		return row;
	}

	private static MasterShohin EnsureConsumption(ExDatabaseSqlite db, string code, string name, MasterShiire shiire,
		EnumConsumptionCalcType calcType, int rateBasisPoints, int roundingUnit, int tankaShiire) {
		var row = db.Fetch<MasterShohin>("where Code=@0", code).FirstOrDefault();
		if (row != null) return row;
		row = new MasterShohin { Code = code, Name = name, IsZaiko = 1, PurchaseType = (int)EnumPurchaseType.Consumption,
			Id_ConsignmentShiire = shiire.Id, ConsumptionCalcType = (int)calcType, ConsumptionRateBasisPoints = rateBasisPoints,
			ConsumptionRoundingUnit = roundingUnit, ConsumptionRounding = (int)EnumRounding.Round, TankaShiire = tankaShiire };
		db.Insert(row); return row;
	}

	private static void Clean(ExDatabaseSqlite db, long shiireId, long[] ids) {
		var csv = string.Join(",", ids);
		const string usesProduct = "json_valid(Jmeisai)=1 AND EXISTS (SELECT 1 FROM json_each(Jmeisai) j WHERE CAST(json_extract(j.value,'$.Id_Shohin') AS INTEGER) IN ({0}))";
		db.Execute($"DELETE FROM Tran03Shiire WHERE Id_Shiire=@0 OR ({string.Format(usesProduct, csv)})", shiireId);
		db.Execute("DELETE FROM Tran02Material WHERE Id_Shiire=@0", shiireId);
		db.Execute($"DELETE FROM Tran00Uriage WHERE {string.Format(usesProduct, csv)}");
		db.Execute($"DELETE FROM SummaryStock WHERE Id_Shohin IN ({csv})");
		db.Execute($"DELETE FROM TranGenka WHERE Id_Shohin IN ({csv})");
		db.Execute($"DELETE FROM TranConsumptionPurchaseLink WHERE Id_Shohin IN ({csv})");
	}

	private static Tran03Shiire NewPurchase(string day, EnumShiire kind, long product, int qty, long amount, long shiire) => new() {
		DenDay = day, KakeDay = day, Id_Soko = _sokoId, Id_Shiire = shiire, IsStock = 1, IsPay = 1, Kubun = (int)kind,
		Jmeisai = [new Tran99Meisai { No = 1, Id_Shohin = product, Su = qty, Tanka = (int)(amount / qty), Kingaku = amount }]
	};

	private static void Purchase(ExDatabaseSqlite db, string day, EnumShiire kind, long product, int qty, long amount, long shiire) =>
		db.Insert(NewPurchase(day, kind, product, qty, amount, shiire));

	private static void Sundry(ExDatabaseSqlite db, string day, EnumShiire kind, long material, long product, long amount, long shiire) => db.Insert(new Tran02Material {
		DenDay = day, KakeDay = day, Id_Shiire = shiire, Kubun = (int)kind,
		Jmeisai = [new Tran99MaterialMeisai { No = 1, Id_Material = material, Id_Shohin = product, Su = 1, Tanka = (int)amount, Kingaku = amount }]
	});

	private static void Sale(ExDatabaseSqlite db, string day, long product, int qty, int price, EnumUri00 kind = EnumUri00.Uriage) => db.Insert(new Tran00Uriage {
		DenDay = day, KakeDay = day, Id_Soko = _sokoId, Kubun = (int)kind,
		Jmeisai = [new Tran99Meisai { No = 1, Id_Shohin = product, Su = qty, Tanka = price, Kingaku = (long)qty * price }]
	});
}
