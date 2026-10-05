using CvAsset;
using CvBase;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;

namespace UatVm.Seed;

/// <summary>共有DBを拒否し、新しい専用小型DBだけを初期化する。</summary>
public static class AutoReplenishSeeder {
	public sealed record Result(MasterTokui Warehouse, MasterTokui Store, MasterShohin Product,
		MasterShiire Supplier, MasterShain Employee, DerivedShohinColSiz Sku);

	public static Result Seed(string dbPath, Action<string> trace) {
		var path = Path.GetFullPath(dbPath);
		var name = Path.GetFileName(path);
		if (!name.StartsWith("auto-replenish-uat-", StringComparison.OrdinalIgnoreCase)
			|| !name.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
			|| path.Split(Path.DirectorySeparatorChar).Any(x => x.Equals("CvServer", StringComparison.OrdinalIgnoreCase))
			|| (File.Exists(path) && new FileInfo(path).Length > 0))
			throw new InvalidOperationException("自動補充UATは未作成/空の auto-replenish-uat-*.db のみ使用できます。共有DB・既存DBは使用できません。");
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		using var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
			DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
		connection.Open();
		using var db = new ExDatabaseSqlite(connection) { KeepConnectionAlive = true };
		if (!new DefineDataTable().InitializeAsync(db, false).GetAwaiter().GetResult())
			throw new InvalidOperationException("UATスキーマを初期化できませんでした。");
		SeedSchema.ApplyMigrations(db, trace);
		var employee = db.Fetch<MasterShain>("ORDER BY Id").First();
		var color = db.Fetch<MasterMeisho>("WHERE Kubun=@0 ORDER BY Id", "COL").First();
		var size = db.Fetch<MasterMeisho>("WHERE Kubun=@0 ORDER BY Id", "SIZ").First();
		var warehouse = new MasterTokui { Code = "AR-WH", Name = "補充UAT倉庫", TenType = 0, IsZaiko = 1 };
		var store = new MasterTokui { Code = "AR-SH", Name = "補充UAT直営店", TenType = 6, IsZaiko = 1 };
		var supplier = new MasterShiire { Code = "AR-SP", Name = "補充UAT仕入先", RateProper = 100, RateSale = 100, IsPay = 1, Shime1 = 99 };
		db.Insert(warehouse); db.Insert(store); db.Insert(supplier);
		var product = new MasterShohin { Code = "AR-P01", Name = "補充UAT通常商品", IsZaiko = 1,
			Id_Soko = warehouse.Id, VSoko = new(warehouse.Id, warehouse.Code, warehouse.Name), Id_Tax = 1,
			Id_ConsignmentShiire = supplier.Id, VConsignmentShiire = new(supplier.Id, supplier.Code, supplier.Name),
			TankaGenka = 1000, TankaShiire = 1000, TankaJodai = 2000, TankaJodaiOrg = 2000,
			Jcolsiz = [new() { Id_Col = color.Id, Code_Col = color.Code, Mei_Col = color.Name,
				Id_Siz = size.Id, Code_Siz = size.Code, Mei_Siz = size.Name, Jan1 = "AR00001" }] };
		db.Insert(product);
		db.Execute(DerivedShohinColSiz.InsertSql, product.Id);
		var sku = db.Fetch<DerivedShohinColSiz>("WHERE Id_Shohin=@0", product.Id).Single();
		db.Insert(new Tran60TanaDate { Id_Shop = store.Id, AutoHoju = 127 });
		var day = DateTime.Today.ToString("yyyyMMdd");
		var receipt = new Tran03Shiire { DenDay = day, KakeDay = day, Id_Soko = warehouse.Id,
			Id_Shiire = supplier.Id, Id_Shain = employee.Id, Kubun = (int)EnumShiire.Shiire,
			SuTotal = 8, Jmeisai = [new() { No = 1, Id_Shohin = product.Id, Id_Col = color.Id, Id_Siz = size.Id, Su = 8 }] };
		db.Insert(receipt);
		new SummaryDb(db).CalcTran2SummaryStock(nameof(Tran03Shiire), nameof(ITranSoko.Id_Soko), receipt.Id, false);
		trace($"小型補充UAT DB={path} 倉庫在庫8・店舗在庫0・全曜日・通常商品1SKU");
		return new(warehouse, store, product, supplier, employee, sku);
	}
}
