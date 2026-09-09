using System.Data;
using CvAsset;
using CvBase;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;

namespace UatVm.Seed;

/// <summary>UAT-04（棚卸）で使う専用マスターと帳簿在庫を用意する。</summary>
public static class StocktakeSeeder {
	public const string WarehouseCode = "UATVM-ST-SK";
	public const string EmployeeCode = "UATVM-ST-SH";
	public const string ShohinCode = "UATVM-ST-P01";
	public const string JanCode = "UATVMST0001";
	public const string StockDay = "20260901";
	public const string StocktakeDay = "20260905";
	public const string SumMonth = "202609";
	public const int InitialStock = 10;

	public sealed record Result(long WarehouseId, string WarehouseCode, long EmployeeId, string EmployeeCode,
		long ShohinId, string ShohinCode, long SkuId, long Id_Col, long Id_Siz, int InitialStock) {
		public long ProductId => ShohinId;
		public long ColorId => Id_Col;
		public long SizeId => Id_Siz;
		public int Stock => InitialStock;
	}

	public static Result Seed(string dbPath, Action<string> trace) {
		ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
		ArgumentNullException.ThrowIfNull(trace);
		if (!File.Exists(dbPath)) throw new FileNotFoundException("対象DBが見つかりません。", dbPath);

		var cs = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString();
		using var connection = new SqliteConnection(cs);
		connection.Open();
		var db = new ExDatabaseSqlite(connection) { KeepConnectionAlive = true };
		var color = db.Fetch<MasterMeisho>("where Kubun=@0 order by Id", "COL").FirstOrDefault()
			?? throw new InvalidOperationException("色マスタがありません。");
		var size = db.Fetch<MasterMeisho>("where Kubun=@0 order by Id", "SIZ").FirstOrDefault()
			?? throw new InvalidOperationException("サイズマスタがありません。");
		var employee = EnsureEmployee(db, trace);
		var warehouse = EnsureWarehouse(db, employee, trace);
		var product = EnsureShohin(db, warehouse, color, size, trace);
		db.Execute("DELETE FROM DerivedShohinColSiz WHERE Id_Shohin=@0", product.Id);
		db.Execute(DerivedShohinColSiz.InsertSql, product.Id);
		var sku = db.Fetch<DerivedShohinColSiz>("where Id_Shohin=@0 order by Id", product.Id).FirstOrDefault()
			?? throw new InvalidOperationException("専用SKUを作成できませんでした。");

		CleanScenario(db, warehouse.Id, product.Id, sku.Id_Col, sku.Id_Siz, trace);
		var tanaDate = db.Fetch<Tran60TanaDate>("where Id_Shop=@0", warehouse.Id).FirstOrDefault() ?? new Tran60TanaDate { Id_Shop = warehouse.Id };
		tanaDate.TanaDay = StocktakeDay;
		var vdate = Common.GetVdate();
		try {
			db.BeginTransaction(IsolationLevel.Serializable);
			if (tanaDate.Id == 0) db.Insert(tanaDate);
			else db.Update(tanaDate);
			db.CompleteTransaction();
		} catch { db.AbortTransaction(); throw; }

		var receipt = new Tran03Shiire {
			DenDay = StockDay, KakeDay = StockDay, Id_Soko = warehouse.Id, Id_Shain = employee.Id,
			Id_Shiire = 0, Kubun = (int)EnumShiire.Shiire, Rate = 100, SuTotal = InitialStock,
			Jmeisai = [new Tran99Meisai { No = 1, Id_Shohin = product.Id, Id_Col = sku.Id_Col, Id_Siz = sku.Id_Siz,
				JanCode = JanCode, Su = InitialStock }],
		};
		receipt.Vdc = vdate; receipt.Vdu = vdate;
		try {
			db.BeginTransaction(IsolationLevel.Serializable);
			db.Insert(receipt);
			db.CompleteTransaction();
		} catch { db.AbortTransaction(); throw; }
		new SummaryDb(db).CalcTran2SummaryStock(nameof(Tran03Shiire), nameof(ITranSoko.Id_Soko), receipt.Id, false);
		trace($"UAT-04専用データ: 倉庫={warehouse.Code} SKU={sku.Id} 帳簿在庫={InitialStock} 棚卸日={StocktakeDay}");
		return new Result(warehouse.Id, warehouse.Code, employee.Id, employee.Code, product.Id, product.Code,
			sku.Id, sku.Id_Col, sku.Id_Siz, InitialStock);
	}

	private static void CleanScenario(ExDatabaseSqlite db, long warehouseId, long shohinId, long idCol, long idSiz, Action<string> trace) {
		var tana = db.Execute("DELETE FROM Tran60Tana WHERE Id_Soko=@0 AND DenDay=@1", warehouseId, StocktakeDay);
		var chosei = db.Execute("DELETE FROM Tran61Chosei WHERE Id_Soko=@0 AND TanaMonth=@1 AND json_valid(Jmeisai)=1 AND EXISTS (SELECT 1 FROM json_each(Jmeisai) j WHERE CAST(json_extract(j.value, '$.Id_Shohin') AS INTEGER)=@2)", warehouseId, SumMonth, shohinId);
		var receipt = db.Execute("DELETE FROM Tran03Shiire WHERE Id_Soko=@0 AND json_valid(Jmeisai)=1 AND EXISTS (SELECT 1 FROM json_each(Jmeisai) j WHERE CAST(json_extract(j.value, '$.Id_Shohin') AS INTEGER)=@1)", warehouseId, shohinId);
		var real = db.Execute("DELETE FROM SummaryRealStock WHERE Id_Soko=@0 AND Id_Shohin=@1 AND Id_Col=@2 AND Id_Siz=@3", warehouseId, shohinId, idCol, idSiz);
		var monthly = db.Execute("DELETE FROM SummaryStock WHERE Id_Soko=@0 AND Id_Shohin=@1 AND Id_Col=@2 AND Id_Siz=@3", warehouseId, shohinId, idCol, idSiz);
		new SummaryDb(db).CalcReserveQtyAll();
		trace($"UAT-04を掃除 棚卸={tana} 調整={chosei} 仕入={receipt} 在庫={real}/{monthly}");
	}

	private static MasterShain EnsureEmployee(ExDatabaseSqlite db, Action<string> trace) {
		var row = db.Fetch<MasterShain>("where Code=@0", EmployeeCode).FirstOrDefault();
		if (row != null) return row;
		row = new MasterShain { Code = EmployeeCode, Name = "UAT-VM 棚卸担当", Ryaku = EmployeeCode };
		var vdate = Common.GetVdate(); row.Vdc = vdate; row.Vdu = vdate;
		try { db.BeginTransaction(IsolationLevel.Serializable); db.Insert(row); db.CompleteTransaction(); }
		catch { db.AbortTransaction(); throw; }
		trace($"社員 {EmployeeCode} を追加 Id={row.Id}"); return row;
	}

	private static MasterTokui EnsureWarehouse(ExDatabaseSqlite db, MasterShain employee, Action<string> trace) {
		var row = db.Fetch<MasterTokui>("where Code=@0", WarehouseCode).FirstOrDefault();
		if (row != null) return row;
		row = new MasterTokui { Code = WarehouseCode, Name = "UAT-VM 棚卸倉庫", Ryaku = WarehouseCode, TenType = 0, IsZaiko = 1,
			Id_Shain = employee.Id, VShain = new CodeNameView(employee.Id, employee.Code, employee.Name) };
		var vdate = Common.GetVdate(); row.Vdc = vdate; row.Vdu = vdate;
		try { db.BeginTransaction(IsolationLevel.Serializable); db.Insert(row); db.CompleteTransaction(); }
		catch { db.AbortTransaction(); throw; }
		trace($"倉庫 {WarehouseCode} を追加 Id={row.Id}"); return row;
	}

	private static MasterShohin EnsureShohin(ExDatabaseSqlite db, MasterTokui warehouse, MasterMeisho color, MasterMeisho size, Action<string> trace) {
		var row = db.Fetch<MasterShohin>("where Code=@0", ShohinCode).FirstOrDefault();
		if (row != null) return row;
		row = new MasterShohin { Code = ShohinCode, Name = "UAT-VM 棚卸商品", Id_Soko = warehouse.Id,
			VSoko = new CodeNameView(warehouse.Id, warehouse.Code, warehouse.Name), Id_Tax = 1, IsZaiko = 1,
			TankaJodaiOrg = 2000, TankaJodai = 2000, TankaGenka = 1000, TankaShiire = 1000,
			Jcolsiz = [new MasterShohinColSiz { Id_Col = color.Id, Code_Col = color.Code, Mei_Col = color.Name,
				Id_Siz = size.Id, Code_Siz = size.Code, Mei_Siz = size.Name, Jan1 = JanCode }] };
		var vdate = Common.GetVdate(); row.Vdc = vdate; row.Vdu = vdate;
		try { db.BeginTransaction(IsolationLevel.Serializable); db.Insert(row); db.CompleteTransaction(); }
		catch { db.AbortTransaction(); throw; }
		trace($"商品 {ShohinCode} を追加 Id={row.Id}"); return row;
	}
}
