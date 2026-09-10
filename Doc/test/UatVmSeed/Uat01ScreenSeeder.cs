using System.Data;
using CvAsset;
using CvBase;
using CvBaseSqlite;
using Microsoft.Data.Sqlite;

namespace UatVm.Seed;

/// <summary>UAT-01画面経路用に、商品以外の前提マスタだけを準備する。</summary>
public static class Uat01ScreenSeeder {
	public const string SupplierCode = "UATVM-U01-SI";
	public const string WarehouseCode = "UATVM-U01-SK";
	public const string ShohinCode = "UATVM-U01-P01";
	public const string JanCode = "UATVMU010001";
	public const string ScenarioMonth = "202609";

	public sealed record Result(
		long SupplierId, string SupplierCode,
		long WarehouseId, string WarehouseCode,
		long EmployeeId, string EmployeeCode,
		long ColorId, string ColorCode, string ColorName,
		long SizeId, string SizeCode, string SizeName);

	public static Result Seed(string dbPath, Action<string> trace) {
		ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
		ArgumentNullException.ThrowIfNull(trace);
		if (!File.Exists(dbPath)) throw new FileNotFoundException("対象DBが見つかりません。", dbPath);

		var cs = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString();
		using var connection = new SqliteConnection(cs);
		connection.Open();
		var db = new ExDatabaseSqlite(connection) { KeepConnectionAlive = true };
		UpdateDb.WriteVersionInfoAsync(db).GetAwaiter().GetResult();
		var employee = db.Fetch<MasterShain>("order by Id").FirstOrDefault()
			?? throw new InvalidOperationException("社員マスタがありません。");
		var color = db.Fetch<MasterMeisho>("where Kubun=@0 order by Id", "COL").FirstOrDefault()
			?? throw new InvalidOperationException("色マスタがありません。");
		var size = db.Fetch<MasterMeisho>("where Kubun=@0 order by Id", "SIZ").FirstOrDefault()
			?? throw new InvalidOperationException("サイズマスタがありません。");
		var supplier = EnsureSupplier(db, employee, trace);
		var warehouse = EnsureWarehouse(db, employee, trace);
		CleanScenario(db, supplier.Id, warehouse.Id, trace);
		return new Result(supplier.Id, supplier.Code, warehouse.Id, warehouse.Code,
			employee.Id, employee.Code, color.Id, color.Code, color.Name, size.Id, size.Code, size.Name);
	}

	static void CleanScenario(ExDatabaseSqlite db, long supplierId, long warehouseId, Action<string> trace) {
		var product = db.Fetch<MasterShohin>("where Code=@0", ShohinCode).FirstOrDefault();
		if (product == null) return;
		var hachu = db.Execute("DELETE FROM Tran13Hachu WHERE Id_Shiire=@0 AND Id_Soko=@1 AND json_valid(Jmeisai)=1 AND EXISTS (SELECT 1 FROM json_each(Jmeisai) j WHERE CAST(json_extract(j.value, '$.Id_Shohin') AS INTEGER)=@2)", supplierId, warehouseId, product.Id);
		var shiire = db.Execute("DELETE FROM Tran03Shiire WHERE Id_Shiire=@0 AND Id_Soko=@1 AND json_valid(Jmeisai)=1 AND EXISTS (SELECT 1 FROM json_each(Jmeisai) j WHERE CAST(json_extract(j.value, '$.Id_Shohin') AS INTEGER)=@2)", supplierId, warehouseId, product.Id);
		var real = db.Execute("DELETE FROM SummaryRealStock WHERE Id_Soko=@0 AND Id_Shohin=@1", warehouseId, product.Id);
		var monthly = db.Execute("DELETE FROM SummaryStock WHERE Id_Soko=@0 AND Id_Shohin=@1", warehouseId, product.Id);
		db.Execute("DELETE FROM SummaryKaiKake WHERE Id_Shiire=@0 AND DenMonth=@1", supplierId, ScenarioMonth);
		db.Execute("DELETE FROM DerivedShohinColSiz WHERE Id_Shohin=@0", product.Id);
		db.Delete<MasterShohin>("where Id=@0", product.Id);
		trace($"UAT-01画面経路を掃除 発注={hachu} 仕入={shiire} 在庫={real}/{monthly} 商品={product.Id}");
	}

	static MasterShiire EnsureSupplier(ExDatabaseSqlite db, MasterShain employee, Action<string> trace) {
		var row = db.Fetch<MasterShiire>("where Code=@0", SupplierCode).FirstOrDefault();
		if (row != null) return row;
		row = new MasterShiire {
			Code = SupplierCode, Name = "UAT-VM UAT-01仕入先", Ryaku = SupplierCode,
			Id_Shain = employee.Id, VShain = new CodeNameView(employee.Id, employee.Code, employee.Name),
			RateProper = 100, RateSale = 100, Shime1 = 99, PayMonth = 0, PayDay = 0, IsPay = 1,
		};
		Insert(db, row);
		trace($"仕入先 {row.Code} を追加 Id={row.Id}");
		return row;
	}

	static MasterTokui EnsureWarehouse(ExDatabaseSqlite db, MasterShain employee, Action<string> trace) {
		var row = db.Fetch<MasterTokui>("where Code=@0", WarehouseCode).FirstOrDefault();
		if (row != null) return row;
		row = new MasterTokui {
			Code = WarehouseCode, Name = "UAT-VM UAT-01倉庫", Ryaku = WarehouseCode, TenType = 0, IsZaiko = 1,
			Id_Shain = employee.Id, VShain = new CodeNameView(employee.Id, employee.Code, employee.Name),
		};
		Insert(db, row);
		trace($"倉庫 {row.Code} を追加 Id={row.Id}");
		return row;
	}

	static void Insert<T>(ExDatabaseSqlite db, T row) where T : BaseDbClass {
		var vdate = Common.GetVdate();
		row.Vdc = vdate;
		row.Vdu = vdate;
		try {
			db.BeginTransaction(IsolationLevel.Serializable);
			db.Insert(row);
			db.CompleteTransaction();
		}
		catch {
			db.AbortTransaction();
			throw;
		}
	}
}
