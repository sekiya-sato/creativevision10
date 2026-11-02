using System.Data;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using Microsoft.Data.Sqlite;

namespace UatVm.Seed;

/// <summary>
/// 支払系3帳票（買掛金管理表・支払台帳・月別支払予定表）の照合用データを投入する。
/// <para>
/// 専用仕入先 <c>UATVM-PRP-*</c> に 2026/04〜07 の仕入・返品・値引・支払を入れ、繰越（前月に動きなし・前々月以前の残）、
/// 複数締日（15日＋末日で同じ支払予定日）、支払月0/1/2、支払日0（自社締日）/10/20/31、過払い、取引ゼロ、改ページ用の多数社を作る。
/// </para>
/// <para>
/// 期待値は製品の集計を呼ばずに手計算した締期間ごとの値（<see cref="Period"/>）で持つ。
/// 自社締日=末日（開発DB 2026-11-02 確認）を前提とし、買掛（月次）は締期間を月で合算して求める。
/// </para>
/// </summary>
public static class PaymentReportSeeder {
	public const string CodeFrom = "UATVM-PRP-001";
	public const string CodeTo = "UATVM-PRP-999";
	public const string MonthFrom = "2026/04";
	public const string MonthTo = "2026/07";
	/// <summary>改ページ確認用の仕入先数（コード UATVM-PRP-101 から連番）</summary>
	public const int PagingCount = 60;

	/// <summary>伝票。返品・値引も金額・税は正値で持ち、符号は区分の CalcFlag が決める。</summary>
	public sealed record Slip(string KakeDay, EnumShiire Kubun, long Amount, long Tax);

	public sealed record Payment(string KakeDay, long Cash, long Fee);

	/// <summary>
	/// 手計算した締期間ごとの期待値。Tax は符号付き、TotalShiire = Shiire - Henpin - Nebiki + Tax、
	/// Cumulative は締期間末の累積残高（全過去期間の仕入合計 - 支払合計）。
	/// </summary>
	public sealed record Period(
		string DayFrom, string DayTo, long Shiire, long Henpin, long Nebiki, long Tax,
		long Cash, long Fee, long Cumulative, string ShiharaiYoteiDay) {
		public long TotalShiire => Shiire - Henpin - Nebiki + Tax;
		public long TotalOut => Cash + Fee;
	}

	public sealed record Supplier(
		string Code, string Label, int Shime1, int Shime2, int PayMonth, int PayDay,
		IReadOnlyList<Slip> Slips, IReadOnlyList<Payment> Payments, IReadOnlyList<Period> Periods);

	public sealed record Result(string DbPath, IReadOnlyList<Supplier> Suppliers);

	static Period Zero(string from, string to, long cumulative, string due) => new(from, to, 0, 0, 0, 0, 0, 0, cumulative, due);

	/// <summary>主要パターン（手計算）。</summary>
	static readonly Supplier[] _main = [
		new("UATVM-PRP-001", "繰越・返品・値引", 99, 0, 1, 31,
			[
				new("20260410", EnumShiire.Shiire, 100_000, 10_000),
				new("20260512", EnumShiire.Henpin, 20_000, 2_000),
				new("20260520", EnumShiire.Nebiki, 5_000, 500),
				new("20260705", EnumShiire.Shiire, 30_000, 3_000),
			],
			[new("20260525", 50_000, 0), new("20260728", 39_560, 440)],
			[
				new("20260401", "20260430", 100_000, 0, 0, 10_000, 0, 0, 110_000, "20260531"),
				new("20260501", "20260531", 0, 20_000, 5_000, -2_500, 50_000, 0, 32_500, "20260630"),
				Zero("20260601", "20260630", 32_500, "20260731"),
				new("20260701", "20260731", 30_000, 0, 0, 3_000, 39_560, 440, 25_500, "20260831"),
			]),
		new("UATVM-PRP-002", "前々月以前の残のみ", 99, 0, 1, 31,
			[new("20260415", EnumShiire.Shiire, 50_000, 5_000)],
			[],
			[
				new("20260401", "20260430", 50_000, 0, 0, 5_000, 0, 0, 55_000, "20260531"),
				Zero("20260501", "20260531", 55_000, "20260630"),
				Zero("20260601", "20260630", 55_000, "20260731"),
				Zero("20260701", "20260731", 55_000, "20260831"),
			]),
		new("UATVM-PRP-003", "複数締日15・末", 15, 99, 1, 10,
			[
				new("20260510", EnumShiire.Shiire, 20_000, 2_000),
				new("20260525", EnumShiire.Shiire, 10_000, 1_000),
			],
			[new("20260605", 22_000, 0)],
			[
				Zero("20260401", "20260415", 0, "20260510"),
				Zero("20260416", "20260430", 0, "20260510"),
				new("20260501", "20260515", 20_000, 0, 0, 2_000, 0, 0, 22_000, "20260610"),
				new("20260516", "20260531", 10_000, 0, 0, 1_000, 0, 0, 33_000, "20260610"),
				new("20260601", "20260615", 0, 0, 0, 0, 22_000, 0, 11_000, "20260710"),
				Zero("20260616", "20260630", 11_000, "20260710"),
				Zero("20260701", "20260715", 11_000, "20260810"),
				Zero("20260716", "20260731", 11_000, "20260810"),
			]),
		new("UATVM-PRP-004", "支払日0(自社締日)・支払月2", 99, 0, 2, 0,
			[new("20260420", EnumShiire.Shiire, 40_000, 4_000)],
			[new("20260630", 44_000, 0)],
			[
				new("20260401", "20260430", 40_000, 0, 0, 4_000, 0, 0, 44_000, "20260630"),
				Zero("20260501", "20260531", 44_000, "20260731"),
				new("20260601", "20260630", 0, 0, 0, 0, 44_000, 0, 0, "20260831"),
				Zero("20260701", "20260731", 0, "20260930"),
			]),
		new("UATVM-PRP-005", "支払月0・過払い", 99, 0, 0, 20,
			[new("20260610", EnumShiire.Shiire, 10_000, 1_000)],
			[new("20260615", 15_000, 0)],
			[
				Zero("20260401", "20260430", 0, "20260420"),
				Zero("20260501", "20260531", 0, "20260520"),
				new("20260601", "20260630", 10_000, 0, 0, 1_000, 15_000, 0, -4_000, "20260620"),
				Zero("20260701", "20260731", -4_000, "20260720"),
			]),
		new("UATVM-PRP-006", "取引ゼロ", 99, 0, 1, 31,
			[],
			[],
			[
				Zero("20260401", "20260430", 0, "20260531"),
				Zero("20260501", "20260531", 0, "20260630"),
				Zero("20260601", "20260630", 0, "20260731"),
				Zero("20260701", "20260731", 0, "20260831"),
			]),
	];

	/// <summary>改ページ用: i社目は 2026/06/10 に仕入 i×1,000（税 i×100）だけを持つ。</summary>
	static Supplier Paging(int i) => new($"UATVM-PRP-{100 + i:000}", $"改ページ{i:00}", 99, 0, 1, 31,
		[new("20260610", EnumShiire.Shiire, i * 1_000L, i * 100L)],
		[],
		[
			Zero("20260401", "20260430", 0, "20260531"),
			Zero("20260501", "20260531", 0, "20260630"),
			new("20260601", "20260630", i * 1_000L, 0, 0, i * 100L, 0, 0, i * 1_100L, "20260731"),
			Zero("20260701", "20260731", i * 1_100L, "20260831"),
		]);

	public static IReadOnlyList<Supplier> Suppliers { get; } =
		[.. _main, .. Enumerable.Range(1, PagingCount).Select(Paging)];

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
		SeedSchema.ApplyMigrations(db, trace);

		var sysman = db.Fetch<MasterSysman>("order by Id").First();
		if (sysman.ShimeBi != 99) {
			throw new InvalidOperationException($"自社締日が末日(99)ではありません（ShimeBi={sysman.ShimeBi}）。期待値は末日前提です。");
		}
		if (string.CompareOrdinal(sysman.FiscalStartDate, "20260401") > 0) {
			throw new InvalidOperationException($"期首日 {sysman.FiscalStartDate} が対象期間より後です。");
		}

		var employee = db.Fetch<MasterShain>("order by Id").First();
		var kin = LoadKin(db);
		var ids = Suppliers.ToDictionary(x => x.Code, x => EnsureShiire(db, employee, x).Id);
		Clean(db, [.. ids.Values]);

		try {
			db.BeginTransaction(IsolationLevel.Serializable);
			foreach (var supplier in Suppliers) {
				var id = ids[supplier.Code];
				foreach (var slip in supplier.Slips) InsertSlip(db, id, slip);
				foreach (var payment in supplier.Payments) InsertPayment(db, id, payment, kin);
			}
			db.CompleteTransaction();
		}
		catch {
			db.AbortTransaction();
			throw;
		}

		trace($"支払系帳票の照合データ: {Suppliers.Count}社（主要{_main.Length}＋改ページ{PagingCount}）・{MonthFrom}〜{MonthTo}を投入");
		return new Result(dbPath, Suppliers);
	}

	static Dictionary<string, MasterMeisho> LoadKin(ExDatabaseSqlite db) {
		var names = new Dictionary<string, string> {
			["01"] = "現金",
			["02"] = "振込手数料",
		};
		var result = db.Fetch<MasterMeisho>("where Kubun=@0 AND Code IN (@1,@2)", MasterMeisho.KubunKin, "01", "02")
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

	static MasterShiire EnsureShiire(ExDatabaseSqlite db, MasterShain employee, Supplier supplier) {
		var row = db.Fetch<MasterShiire>("where Code=@0", supplier.Code).FirstOrDefault();
		if (row == null) {
			row = new MasterShiire {
				Code = supplier.Code,
				Name = $"支払帳票照合 {supplier.Label}",
				Ryaku = supplier.Code,
				Id_Shain = employee.Id,
				VShain = new CodeNameView(employee.Id, employee.Code, employee.Name),
			};
			var vdate = Common.GetVdate();
			row.Vdc = vdate;
			row.Vdu = vdate;
			db.Insert(row);
		}
		row.Shime1 = supplier.Shime1;
		row.Shime2 = supplier.Shime2;
		row.Shime3 = 0;
		row.PayMonth = supplier.PayMonth;
		row.PayDay = supplier.PayDay;
		row.TaxCalcUnit = (int)EnumTaxCalcUnit.Slip;
		row.TaxRounding = (int)EnumRounding.Round;
		db.Update(row);
		return row;
	}

	static void Clean(ExDatabaseSqlite db, long[] supplierIds) {
		foreach (var id in supplierIds) {
			db.Execute("DELETE FROM Tran03Shiire WHERE Id_Shiire=@0", id);
			db.Execute("DELETE FROM Tran07Shiharai WHERE Id_Torisaki=@0", id);
			db.Execute("DELETE FROM SummaryKaiKake WHERE Id_Shiire=@0", id);
			db.Execute("DELETE FROM SummaryKaiShi WHERE Id_Shiire=@0", id);
		}
	}

	static void InsertSlip(ExDatabaseSqlite db, long shiireId, Slip slip) {
		var row = new Tran03Shiire {
			DenDay = slip.KakeDay,
			KakeDay = slip.KakeDay,
			Id_Shiire = shiireId,
			KingakuTotal = slip.Amount,
			TaxableAmount1 = slip.Amount,
			Tax1 = slip.Tax,
			Total = slip.Amount + slip.Tax,
			TaxCalcUnit = (int)EnumTaxCalcUnit.Slip,
			IsPay = 1,
			Jmeisai = [new Tran99Meisai { No = 1, Su = 1, Kingaku = slip.Amount }],
		};
		// Kubun の設定で CalcFlag（返品・値引は -1）が決まる
		row.EnKubun = slip.Kubun;
		db.Insert(row);
	}

	static void InsertPayment(ExDatabaseSqlite db, long shiireId, Payment payment, IReadOnlyDictionary<string, MasterMeisho> kin) {
		var amounts = new[] { (Code: "01", Amount: payment.Cash), (Code: "02", Amount: payment.Fee) }
			.Where(x => x.Amount != 0)
			.ToArray();
		db.Insert(new Tran07Shiharai {
			KakeDay = payment.KakeDay,
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
