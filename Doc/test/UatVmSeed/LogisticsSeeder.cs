using System.Data;
using CvAsset;
using CvBase;
using CvBaseSqlite;
using Microsoft.Data.Sqlite;

namespace UatVm.Seed;

/// <summary>
/// 物流連携（WMS）通し確認（UatVm logisticsflow）の専用マスターと連携設定を用意する。
/// <para>
/// 伝票（発注・仕入・受注・移動・配分）はシナリオ側から CvServer の登録経路（在庫・引当・入荷割当の後処理つき）で作るため、
/// ここではマスター（倉庫2・卸先・直営店・商品1SKU）と <see cref="MasterConfig"/> の物流連携設定だけを書く。
/// 開発DB本体へは書かない（<c>--sqlite</c> で指定した複製DBだけを対象にする）。
/// </para>
/// </summary>
public static class LogisticsSeeder {
	public const string WarehouseCode = "UATVM-LG-SK";
	public const string SourceWarehouseCode = "UATVM-LG-S2";
	public const string TokuiCode = "UATVM-LG-TK";
	public const string DirectStoreCode = "UATVM-LG-TS";
	public const string ShohinCode = "UATVM-LG-P01";
	public const string ShohinName = "UAT-VM 物流連携商品";
	public const string JanCode = "UATVMLG0001";

	public sealed record Result(
		long WarehouseId, string WarehouseCode, long SourceWarehouseId, string SourceWarehouseCode,
		long TokuiId, string TokuiCode, long DirectStoreId, string DirectStoreCode,
		long EmployeeId, string EmployeeCode, long ShiireId, string ShiireCode, string ShiireName,
		long ShohinId, string ShohinCode, long Id_Col, string ColCode, long Id_Siz, string SizCode, string BaseFolder);

	/// <summary>マスターと連携設定を投入する</summary>
	/// <param name="dbPath">複製DB</param>
	/// <param name="baseFolder">連携フォルダ（LogisticsBaseFolder に設定する一時フォルダ）</param>
	/// <param name="trace">記録</param>
	public static Result Seed(string dbPath, string baseFolder, Action<string> trace) {
		ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(baseFolder);
		ArgumentNullException.ThrowIfNull(trace);
		if (!File.Exists(dbPath)) throw new FileNotFoundException("対象DBが見つかりません。", dbPath);
		// 開発DB本体は更新しない
		var full = Path.GetFullPath(dbPath);
		if (string.Equals(Path.GetFileName(full), "server-user163.db", StringComparison.OrdinalIgnoreCase)
			&& string.Equals(Path.GetFileName(Path.GetDirectoryName(full)), "CvServer", StringComparison.OrdinalIgnoreCase)) {
			throw new InvalidOperationException("logisticsflow は開発DB本体を更新しません。--sqlite で複製DBを指定してください。");
		}

		var cs = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString();
		using var connection = new SqliteConnection(cs);
		connection.Open();
		var db = new ExDatabaseSqlite(connection) { KeepConnectionAlive = true };
		SeedSchema.ApplyMigrations(db, trace);

		var employee = db.Fetch<MasterShain>("order by Id").FirstOrDefault()
			?? throw new InvalidOperationException("社員マスタがありません。");
		var color = db.Fetch<MasterMeisho>("where Kubun=@0 order by Id", "COL").FirstOrDefault()
			?? throw new InvalidOperationException("色マスタがありません。");
		var size = db.Fetch<MasterMeisho>("where Kubun=@0 order by Id", "SIZ").FirstOrDefault()
			?? throw new InvalidOperationException("サイズマスタがありません。");
		var shiire = db.Fetch<MasterShiire>("order by Id").FirstOrDefault()
			?? throw new InvalidOperationException("仕入先マスタがありません。");

		var warehouse = EnsureTokui(db, WarehouseCode, "UAT-VM 物流倉庫", 0, employee, trace);
		var source = EnsureTokui(db, SourceWarehouseCode, "UAT-VM 物流移動元倉庫", 0, employee, trace);
		var tokui = EnsureTokui(db, TokuiCode, "UAT-VM 物流卸先", 1, employee, trace);
		var directStore = EnsureTokui(db, DirectStoreCode, "UAT-VM 物流直営店", 6, employee, trace);
		if (db.ExecuteScalar<int>("SELECT COUNT(*) FROM TranHaibun WHERE Id_Soko=@0", warehouse.Id) > 0) {
			// 再実行では送受信履歴・配分の前提が崩れるため、毎回新しい複製DBで実行する
			throw new InvalidOperationException("複製DBに前回の logisticsflow のデータがあります。新しい複製DBで実行してください。");
		}
		var product = EnsureShohin(db, warehouse, color, size, trace);
		// SeederはCvServerのWriteEffectRunnerを通らないため、Jcolsiz由来のSKUを明示的に再構築する。
		db.Execute("DELETE FROM DerivedShohinColSiz WHERE Id_Shohin=@0", product.Id);
		db.Execute(DerivedShohinColSiz.InsertSql, product.Id);
		var sku = db.Fetch<DerivedShohinColSiz>("where Id_Shohin=@0 order by Id", product.Id).FirstOrDefault()
			?? throw new InvalidOperationException("専用SKUを作成できませんでした。");

		Directory.CreateDirectory(baseFolder);
		UpsertConfig(db, MasterConfig.NameLogisticsBaseFolder, baseFolder);
		UpsertConfig(db, MasterConfig.NameLogisticsTargetSoko, warehouse.Code);
		UpsertConfig(db, MasterConfig.NameLogisticsLinkCode, "WMS");
		UpsertConfig(db, MasterConfig.NameLogisticsEncoding, "shift_jis");
		UpsertConfig(db, MasterConfig.NameLogisticsFileFormat, LogisticsFileFormat.Cv10V1);
		trace($"物流連携設定: 連携フォルダ={baseFolder} 対象倉庫={warehouse.Code}");
		trace($"物流連携専用データ: 倉庫={warehouse.Code} 移動元={source.Code} 卸先={tokui.Code} 直営店={directStore.Code} SKU={sku.Id}");
		return new Result(warehouse.Id, warehouse.Code, source.Id, source.Code, tokui.Id, tokui.Code, directStore.Id, directStore.Code,
			employee.Id, employee.Code, shiire.Id, shiire.Code, shiire.Name,
			product.Id, product.Code, sku.Id_Col, sku.Code_Col, sku.Id_Siz, sku.Code_Siz, baseFolder);
	}

	private static void UpsertConfig(ExDatabaseSqlite db, string name, string value) {
		var vdate = Common.GetVdate();
		var row = db.Fetch<MasterConfig>("where Category=@0 AND Name=@1", MasterConfig.CategoryLogistics, name).FirstOrDefault();
		try {
			db.BeginTransaction(IsolationLevel.Serializable);
			if (row == null) {
				db.Insert(new MasterConfig { Category = MasterConfig.CategoryLogistics, Name = name, Val = value, Vdc = vdate, Vdu = vdate });
			}
			else {
				row.Val = value;
				row.Vdu = vdate;
				db.Update(row);
			}
			db.CompleteTransaction();
		}
		catch { db.AbortTransaction(); throw; }
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

	private static MasterShohin EnsureShohin(ExDatabaseSqlite db, MasterTokui warehouse, MasterMeisho color, MasterMeisho size, Action<string> trace) {
		var row = db.Fetch<MasterShohin>("where Code=@0", ShohinCode).FirstOrDefault();
		if (row != null) return row;
		row = new MasterShohin { Code = ShohinCode, Name = ShohinName, Id_Soko = warehouse.Id,
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
