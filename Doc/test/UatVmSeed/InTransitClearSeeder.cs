using CvAsset;
using CvBase;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;

namespace UatVm.Seed;

/// <summary>共有DBを使わず、正負の積送残を持つ独立したUATデータを作る。</summary>
public static class InTransitClearSeeder {
	public sealed record Result(string DbPath, long SourceId, long A, long B, long C, long Product1, long Product2, long Color, long Size);

	public static Result Seed(string dbPath, Action<string> trace) {
		var fullPath = Path.GetFullPath(dbPath);
		if (!fullPath.Contains(Path.DirectorySeparatorChar + ".tmp_ui_check" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException("積送中クリアUATは .tmp_ui_check 内の新規専用DBだけで実行します。");
		if (File.Exists(fullPath)) throw new InvalidOperationException("既存DBへシードしません。新しいDBパスを指定してください。");
		Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
		using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = fullPath, Pooling = false }.ToString());
		connection.Open();
		var db = new ExDatabaseSqlite(connection) { KeepConnectionAlive = true };
		if (!new DefineDataTable().InitializeAsync(db, false).GetAwaiter().GetResult()) throw new InvalidOperationException("DB初期化失敗");
		var stamp = Common.GetVdate();
		long Warehouse(string suffix) {
			var warehouse = new MasterTokui { Code = "UATVM-TC-" + suffix, Name = "積送中クリア " + suffix, IsZaiko = 1, Vdc = stamp, Vdu = stamp };
			db.Insert(warehouse);
			return warehouse.Id;
		}
		var source = Warehouse("SRC"); var a = Warehouse("A"); var b = Warehouse("B"); var c = Warehouse("C");
		var color = db.Fetch<MasterMeisho>("where Kubun=@0 order by Id", "COL").First();
		var size = db.Fetch<MasterMeisho>("where Kubun=@0 order by Id", "SIZ").First();
		long Product(string suffix) {
			var product = new MasterShohin { Code = "UATVM-TC-" + suffix, Name = "積送中クリア商品 " + suffix, IsZaiko = 1,
				Vdc = stamp, Vdu = stamp, Jcolsiz = [new MasterShohinColSiz { Id_Col = color.Id, Code_Col = color.Code, Mei_Col = color.Name,
					Id_Siz = size.Id, Code_Siz = size.Code, Mei_Siz = size.Name, Jan1 = "UATVMT" + suffix }] };
			db.Insert(product);
			db.Execute(DerivedShohinColSiz.InsertSql, product.Id);
			return product.Id;
		}
		var p1 = Product("P1"); var p2 = Product("P2");
		var result = new Result(fullPath, source, a, b, c, p1, p2, color.Id, size.Id);
		foreach (var (warehouse, product, quantity, day) in new[] { (a, p1, 10, "20260901"), (a, p2, -10, "20261001"), (b, p1, 7, "20260901"), (c, p1, -3, "20261001") }) {
			var slip = CreateOut(result, warehouse, product, quantity, day);
			db.Insert(slip);
			new SummaryDb(db).CalcTran2SummaryStock(nameof(Tran10IdoOut), nameof(ITranSoko.Id_Soko), slip.Id, false);
			new SummaryDb(db).CalcTran2SummaryStock(nameof(Tran10IdoOut), nameof(ITranIdo.Id_Ido), slip.Id, false);
		}
		trace("専用積送残 A=+10/-10、B=+7、C=-3 を投入");
		return result;
	}

	public static Tran10IdoOut CreateOut(Result seeded, long warehouse, long product, int quantity, string? day = null) => new() {
		DenDay = day ?? DateTime.Today.ToString("yyyyMMdd"), Id_Soko = seeded.SourceId, Id_Ido = warehouse,
		ManualNo = "UATVM-TC", SuTotal = quantity, Vdc = Common.GetVdate(), Vdu = Common.GetVdate(),
		Jmeisai = [new Tran99Meisai { No = 1, Id_Shohin = product, Id_Col = seeded.Color, Id_Siz = seeded.Size, Su = quantity }],
	};
}
