using System.Data;
using CvAsset;
using CvBase;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;

namespace UatVm.Seed;

/// <summary>UAT-03（店舗間移動）で使う専用マスターと初期在庫を用意する。</summary>
public static class TransferSeeder {
	public const string SourceWarehouseCode = "UATVM-TR-SK";
	public const string DestinationWarehouseCode = "UATVM-TR-DK";
	public const string ShohinCode = "UATVM-TR-P01";
	public const string JanCode = "UATVMTR0001";
	public const string StockDay = "20260901";
	public const string ScenarioDay = "20260905";
	public const string ManualNoPrefix = "UATVM-TR-";
	public const int InitialStock = 20;
	public const int ImmediateQty = 3;
	public const int TransitQty = 5;
	public const int CancelQty = 2;

	public sealed record Result(
		long SourceWarehouseId, string SourceWarehouseCode,
		long DestinationWarehouseId, string DestinationWarehouseCode,
		long EmployeeId, string EmployeeCode, long ShohinId, string ShohinCode,
		long SkuId, long Id_Col, long Id_Siz, int InitialStock,
		int ImmediateQty, int TransitQty, int CancelQty) {
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
		var employee = db.Fetch<MasterShain>("order by Id").FirstOrDefault()
			?? throw new InvalidOperationException("社員マスタがありません。");
		var color = db.Fetch<MasterMeisho>("where Kubun=@0 order by Id", "COL").FirstOrDefault()
			?? throw new InvalidOperationException("色マスタがありません。");
		var size = db.Fetch<MasterMeisho>("where Kubun=@0 order by Id", "SIZ").FirstOrDefault()
			?? throw new InvalidOperationException("サイズマスタがありません。");

		var source = EnsureWarehouse(db, SourceWarehouseCode, "UAT-VM 移動元倉庫", employee, trace);
		var destination = EnsureWarehouse(db, DestinationWarehouseCode, "UAT-VM 移動先倉庫", employee, trace);
		var product = EnsureShohin(db, source, color, size, employee, trace);
		db.Execute("DELETE FROM DerivedShohinColSiz WHERE Id_Shohin=@0", product.Id);
		db.Execute(DerivedShohinColSiz.InsertSql, product.Id);
		var sku = db.Fetch<DerivedShohinColSiz>("where Id_Shohin=@0 order by Id", product.Id).FirstOrDefault()
			?? throw new InvalidOperationException("専用SKUを作成できませんでした。");

		CleanScenario(db, source.Id, destination.Id, product.Id, sku.Id_Col, sku.Id_Siz, trace);
		var receipt = new Tran03Shiire {
			DenDay = StockDay, KakeDay = StockDay, Id_Soko = source.Id, Id_Shain = employee.Id,
			Id_Shiire = 0, SuTotal = InitialStock, Rate = 100,
			Jmeisai = [new Tran99Meisai { No = 1, Id_Shohin = product.Id, Id_Col = sku.Id_Col, Id_Siz = sku.Id_Siz,
				JanCode = JanCode, Su = InitialStock }],
		};
		var vdate = Common.GetVdate();
		receipt.Vdc = vdate; receipt.Vdu = vdate;
		try {
			db.BeginTransaction(IsolationLevel.Serializable);
			db.Insert(receipt);
			db.CompleteTransaction();
		} catch { db.AbortTransaction(); throw; }
		new SummaryDb(db).CalcTran2SummaryStock(nameof(Tran03Shiire), nameof(ITranSoko.Id_Soko), receipt.Id, false);
		trace($"UAT-03専用データ: 元={source.Code} 先={destination.Code} SKU={sku.Id} 在庫={InitialStock}");
		return new Result(source.Id, source.Code, destination.Id, destination.Code, employee.Id, employee.Code,
			product.Id, product.Code, sku.Id, sku.Id_Col, sku.Id_Siz, InitialStock, ImmediateQty, TransitQty, CancelQty);
	}

	private static void CleanScenario(ExDatabaseSqlite db, long sourceId, long destinationId, long shohinId, long idCol, long idSiz, Action<string> trace) {
		var tables = new[] { nameof(Tran05Ido), nameof(Tran10IdoOut), nameof(Tran11IdoIn) };
		var deleted = 0;
		foreach (var table in tables)
			deleted += db.Execute($"DELETE FROM {table} WHERE ManualNo LIKE @0 AND DenDay=@1 AND ((Id_Soko=@2 AND Id_Ido=@3) OR (Id_Soko=@3 AND Id_Ido=@2))", $"{ManualNoPrefix}%", ScenarioDay, sourceId, destinationId);
		deleted += db.Execute("DELETE FROM Tran03Shiire WHERE Id_Soko=@0 AND DenDay=@1 AND json_valid(Jmeisai)=1 AND EXISTS (SELECT 1 FROM json_each(Jmeisai) j WHERE CAST(json_extract(j.value, '$.Id_Shohin') AS INTEGER)=@2)", sourceId, StockDay, shohinId);
		var real = db.Execute("DELETE FROM SummaryRealStock WHERE (Id_Soko=@0 OR Id_Soko=@1) AND Id_Shohin=@2 AND Id_Col=@3 AND Id_Siz=@4", sourceId, destinationId, shohinId, idCol, idSiz);
		var monthly = db.Execute("DELETE FROM SummaryStock WHERE (Id_Soko=@0 OR Id_Soko=@1) AND Id_Shohin=@2 AND Id_Col=@3 AND Id_Siz=@4", sourceId, destinationId, shohinId, idCol, idSiz);
		new SummaryDb(db).CalcReserveQtyAll();
		trace($"UAT-03を掃除 伝票={deleted} 在庫={real}/{monthly}");
	}

	private static MasterTokui EnsureWarehouse(ExDatabaseSqlite db, string code, string name, MasterShain employee, Action<string> trace) {
		var row = db.Fetch<MasterTokui>("where Code=@0", code).FirstOrDefault();
		if (row != null) return row;
		row = new MasterTokui { Code = code, Name = name, Ryaku = code, TenType = 0, IsZaiko = 1, Id_Shain = employee.Id,
			VShain = new CodeNameView(employee.Id, employee.Code, employee.Name) };
		var vdate = Common.GetVdate(); row.Vdc = vdate; row.Vdu = vdate;
		try { db.BeginTransaction(IsolationLevel.Serializable); db.Insert(row); db.CompleteTransaction(); }
		catch { db.AbortTransaction(); throw; }
		trace($"倉庫 {code} を追加 Id={row.Id}"); return row;
	}

	private static MasterShohin EnsureShohin(ExDatabaseSqlite db, MasterTokui warehouse, MasterMeisho color, MasterMeisho size, MasterShain employee, Action<string> trace) {
		var row = db.Fetch<MasterShohin>("where Code=@0", ShohinCode).FirstOrDefault();
		if (row != null) return row;
		row = new MasterShohin { Code = ShohinCode, Name = "UAT-VM 移動商品", Id_Soko = warehouse.Id,
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
