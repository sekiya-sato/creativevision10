using System.Data;
using CvAsset;
using CvBase;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;

namespace UatVm.Seed;

/// <summary>UAT-02（受注・配分・出荷）で使う専用マスターと初期在庫を用意する。</summary>
public static class JuchuShippingSeeder {
	public const string WarehouseCode = "UATVM-JS-SK";
	public const string TokuiCode = "UATVM-JS-TK";
	public const string DirectStoreCode = "UATVM-JS-TS";
	public const string ShohinCode = "UATVM-JS-P01";
	public const string JanCode = "UATVMJS0001";
	public const int InitialStock = 8;
	public const string StockDay = "20260901";

	public sealed record Result(
		long WarehouseId, string WarehouseCode, long TokuiId, string TokuiCode,
		long DirectStoreId, string DirectStoreCode,
		long EmployeeId, string EmployeeCode, long ShohinId, string ShohinCode,
		long SkuId, long Id_Col, long Id_Siz, int InitialStock) {
		public long WholesalerId => TokuiId;
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

		var warehouse = EnsureTokui(db, WarehouseCode, "UAT-VM 受注出荷倉庫", 0, employee, trace);
		var tokui = EnsureTokui(db, TokuiCode, "UAT-VM 受注出荷卸先", 1, employee, trace);
		var directStore = EnsureTokui(db, DirectStoreCode, "UAT-VM 受注出荷直営店", 6, employee, trace);
		var product = EnsureShohin(db, warehouse, color, size, employee, trace);
		// SeederはCvServerのWriteEffectRunnerを通らないため、Jcolsiz由来のSKUを明示的に再構築する。
		db.Execute("DELETE FROM DerivedShohinColSiz WHERE Id_Shohin=@0", product.Id);
		db.Execute(DerivedShohinColSiz.InsertSql, product.Id);
		var sku = db.Fetch<DerivedShohinColSiz>("where Id_Shohin=@0 order by Id", product.Id).FirstOrDefault()
			?? throw new InvalidOperationException("専用SKUを作成できませんでした。");

		CleanPendingScenario(db, warehouse.Id, tokui.Id, directStore.Id, product.Id, sku.Id_Col, sku.Id_Siz, trace);
		// 再実行時は専用伝票だけを除去し、通常の仕入伝票経路で在庫を再構築する。
		db.Execute("DELETE FROM Tran03Shiire WHERE Id_Soko=@0", warehouse.Id);
		var receipt = new Tran03Shiire {
			DenDay = StockDay, KakeDay = StockDay, Id_Soko = warehouse.Id,
			Id_Shiire = 0, Id_Shain = employee.Id, Kubun = (int)EnumShiire.Shiire,
			Rate = 100, SuTotal = InitialStock, KingakuTotal = 0, JodaiTotal = 0, GedaiTotal = 0,
			Jmeisai = [new Tran99Meisai { No = 1, Id_Shohin = product.Id, Id_Col = sku.Id_Col, Id_Siz = sku.Id_Siz,
				JanCode = JanCode, Su = InitialStock, Tanka = 0, Kingaku = 0, Jodai = 0, Gedai = 0 }],
		};
		var vdate = Common.GetVdate();
		receipt.Vdc = vdate; receipt.Vdu = vdate;
		try {
			db.BeginTransaction(IsolationLevel.Serializable);
			db.Insert(receipt);
			db.CompleteTransaction();
		} catch { db.AbortTransaction(); throw; }
		new SummaryDb(db).CalcTran2SummaryStock(nameof(Tran03Shiire), nameof(ITranSoko.Id_Soko), receipt.Id, false);
		trace($"UAT-02専用データ: 倉庫={warehouse.Code} 卸先={tokui.Code} 直営店={directStore.Code} SKU={sku.Id} 在庫={InitialStock}");
		return new Result(warehouse.Id, warehouse.Code, tokui.Id, tokui.Code, directStore.Id, directStore.Code, employee.Id, employee.Code,
			product.Id, product.Code, sku.Id, sku.Id_Col, sku.Id_Siz, InitialStock);
	}

	private static void CleanPendingScenario(ExDatabaseSqlite db, long warehouseId, long tokuiId, long directStoreId, long shohinId, long idCol, long idSiz, Action<string> trace) {
		var orders = db.Fetch<Tran12Jyuchu>("where Id_Soko=@0 AND Id_Tokui IN (@1,@2) AND DenDay=@3", warehouseId, tokuiId, directStoreId, "20260905");
		var ids = orders.Count == 0 ? string.Empty : string.Join(",", orders.Select(x => x.Id));
		var sales = ids.Length == 0 ? 0 : db.Execute($"DELETE FROM Tran00Uriage WHERE RelateNo1 IN ({ids})");
		var haibun = ids.Length == 0 ? 0 : db.Execute($"DELETE FROM TranHaibun WHERE RelateNo1 IN ({ids})");
		var juchu = ids.Length == 0 ? 0 : db.Execute($"DELETE FROM Tran12Jyuchu WHERE Id IN ({ids})");
		var ido = db.Execute("DELETE FROM Tran10IdoOut WHERE Id_Soko=@0 AND Id_Ido=@1 AND Memo=@2", warehouseId, directStoreId, "配分出荷");
		var real = db.Execute("DELETE FROM SummaryRealStock WHERE Id_Soko=@0 AND Id_Shohin=@1 AND Id_Col=@2 AND Id_Siz=@3", warehouseId, shohinId, idCol, idSiz);
		var monthly = db.Execute("DELETE FROM SummaryStock WHERE Id_Soko=@0 AND Id_Shohin=@1 AND Id_Col=@2 AND Id_Siz=@3", warehouseId, shohinId, idCol, idSiz);
		new SummaryDb(db).CalcReserveQtyAll();
		trace($"UAT-02を掃除 売上={sales} 移動={ido} 受注={juchu} 配分={haibun} 在庫={real}/{monthly}");
	}

	private static MasterTokui EnsureTokui(ExDatabaseSqlite db, string code, string name, int tenType, MasterShain employee, Action<string> trace) {
		var row = db.Fetch<MasterTokui>("where Code=@0", code).FirstOrDefault();
		if (row != null) return row;
		row = new MasterTokui { Code = code, Name = name, Ryaku = code, TenType = tenType, IsZaiko = 1,
			Id_Shain = employee.Id, VShain = new CodeNameView(employee.Id, employee.Code, employee.Name) };
		var vdate = Common.GetVdate(); row.Vdc = vdate; row.Vdu = vdate;
		try { db.BeginTransaction(IsolationLevel.Serializable); db.Insert(row); db.CompleteTransaction(); }
		catch { db.AbortTransaction(); throw; }
		trace($"得意先 {code} を追加 Id={row.Id}"); return row;
	}

	private static MasterShohin EnsureShohin(ExDatabaseSqlite db, MasterTokui warehouse, MasterMeisho color, MasterMeisho size, MasterShain employee, Action<string> trace) {
		var row = db.Fetch<MasterShohin>("where Code=@0", ShohinCode).FirstOrDefault();
		if (row != null) return row;
		row = new MasterShohin { Code = ShohinCode, Name = "UAT-VM 受注出荷商品", Id_Soko = warehouse.Id,
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
