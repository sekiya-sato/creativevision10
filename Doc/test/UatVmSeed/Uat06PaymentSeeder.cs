using System.Data;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using Microsoft.Data.Sqlite;

namespace UatVm.Seed;

/// <summary>UAT-06 支払境界（過払い・全額相殺・複数明細）の専用データを投入する。</summary>
public static class Uat06PaymentSeeder {
	public const string CodeFrom = "UATVM-PAY-005";
	public const string CodeTo = "UATVM-PAY-007";
	public const int Shime = 99;
	public const string BillingMonth = "202607";
	public const string DayTo = "20260731";

	public sealed record Expected(
		string Code, long Shiire, long Tax, long TotalShiire, long Cash, long Fee, long Offset,
		long TotalOut, long Balance, string ShiharaiYoteiDay);

	public sealed record Result(IReadOnlyList<Expected> Expectations);

	private static readonly Expected[] _expectations = [
		new(CodeFrom, 50_000, 5_000, 55_000, 60_000, 0, 0, 60_000, -5_000, DayTo),
		new("UATVM-PAY-006", 30_000, 3_000, 33_000, 0, 0, 33_000, 33_000, 0, DayTo),
		new(CodeTo, 40_000, 4_000, 44_000, 20_000, 4_000, 20_000, 44_000, 0, DayTo),
	];

	public static Result Seed(string dbPath, Action<string> trace) {
		ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
		ArgumentNullException.ThrowIfNull(trace);
		if (!File.Exists(dbPath)) throw new FileNotFoundException("対象DBが見つかりません。", dbPath);

		var connectionString = new SqliteConnectionStringBuilder {
			DataSource = dbPath,
			Mode = SqliteOpenMode.ReadWrite,
			Pooling = false,
		}.ToString();
		using var connection = new SqliteConnection(connectionString);
		connection.Open();
		var db = new ExDatabaseSqlite(connection) { KeepConnectionAlive = true };
		var employee = db.Fetch<MasterShain>("order by Id").First();
		var kin = LoadKin(db);

		var suppliers = _expectations.ToDictionary(
			x => x.Code,
			x => EnsureShiire(db, employee, x.Code));
		Clean(db, suppliers.Values.Select(x => x.Id).ToArray());

		try {
			db.BeginTransaction(IsolationLevel.Serializable);
			foreach (var expected in _expectations) {
				var shiire = suppliers[expected.Code];
				InsertPurchase(db, shiire.Id, expected.Shiire);
				InsertPayment(db, shiire.Id, expected, kin);
			}
			db.CompleteTransaction();
		}
		catch {
			db.AbortTransaction();
			throw;
		}

		trace($"UAT-06専用データ: {CodeFrom}〜{CodeTo} / 過払い・全額相殺・現金+相殺+手数料を投入");
		return new Result(_expectations);
	}

	private static Dictionary<string, MasterMeisho> LoadKin(ExDatabaseSqlite db) {
		var names = new Dictionary<string, string> {
			["01"] = "現金",
			["02"] = "振込手数料",
			["04"] = "相殺",
		};
		var result = db.Fetch<MasterMeisho>("where Kubun=@0 AND Code IN (@1,@2,@3)", MasterMeisho.KubunKin, "01", "02", "04")
			.ToDictionary(x => x.Code);
		foreach (var (code, name) in names) {
			if (result.ContainsKey(code)) continue;
			var row = new MasterMeisho { Kubun = MasterMeisho.KubunKin, KubunName = "入金・支払方法", Code = code, Name = name, Ryaku = name };
			var vdate = Common.GetVdate();
			row.Vdc = vdate;
			row.Vdu = vdate;
			db.Insert(row);
			result[code] = row;
		}
		return result;
	}

	private static MasterShiire EnsureShiire(ExDatabaseSqlite db, MasterShain employee, string code) {
		var row = db.Fetch<MasterShiire>("where Code=@0", code).FirstOrDefault();
		if (row == null) {
			row = new MasterShiire {
				Code = code,
				Name = $"UAT-06支払境界 {code[^3..]}",
				Ryaku = code,
				Id_Shain = employee.Id,
				VShain = new CodeNameView(employee.Id, employee.Code, employee.Name),
			};
			var vdate = Common.GetVdate();
			row.Vdc = vdate;
			row.Vdu = vdate;
			db.Insert(row);
		}
		row.Shime1 = Shime;
		row.Shime2 = 0;
		row.Shime3 = 0;
		row.PayMonth = 0;
		row.PayDay = Shime;
		row.TaxCalcUnit = (int)EnumTaxCalcUnit.Billing;
		row.TaxRounding = (int)EnumRounding.Round;
		db.Update(row);
		return row;
	}

	private static void Clean(ExDatabaseSqlite db, long[] supplierIds) {
		foreach (var id in supplierIds) {
			db.Execute("DELETE FROM Tran03Shiire WHERE Id_Shiire=@0", id);
			db.Execute("DELETE FROM Tran07Shiharai WHERE Id_Torisaki=@0", id);
			db.Execute("DELETE FROM SummaryKaiKake WHERE Id_Shiire=@0", id);
			db.Execute("DELETE FROM SummaryKaiShi WHERE Id_Shiire=@0", id);
		}
	}

	private static void InsertPurchase(ExDatabaseSqlite db, long shiireId, long amount) {
		var row = new Tran03Shiire {
			DenDay = "20260707",
			KakeDay = "20260707",
			Id_Shiire = shiireId,
			KingakuTotal = amount,
			TaxableAmount1 = amount,
			IsPay = 1,
			Jmeisai = [new Tran99Meisai { No = 1, Su = 1, Kingaku = amount }],
		};
		row.EnKubun = EnumShiire.Shiire;
		db.Insert(row);
	}

	private static void InsertPayment(
		ExDatabaseSqlite db,
		long shiireId,
		Expected expected,
		IReadOnlyDictionary<string, MasterMeisho> kin) {
		var amounts = new[] { (Code: "01", Amount: expected.Cash), (Code: "02", Amount: expected.Fee), (Code: "04", Amount: expected.Offset) }
			.Where(x => x.Amount != 0)
			.ToArray();
		db.Insert(new Tran07Shiharai {
			KakeDay = "20260727",
			Id_Torisaki = shiireId,
			KingakuTotal = amounts.Sum(x => x.Amount),
			Jmeisai = [.. amounts.Select((x, index) => new TranKinMeisai {
				No = index + 1,
				Id_Kin = kin[x.Code].Id,
				Code_Kin = x.Code,
				Mei_Kin = kin[x.Code].Name,
				Kingaku = x.Amount,
			})],
		});
	}
}
