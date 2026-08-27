using System.Data;
using CvAsset;
using CvBase;
using CvBaseSqlite;
using Microsoft.Data.Sqlite;

namespace UatVm.Seed;

/// <summary>
/// 締日20の請求期間境界（C-01）を検証するためのデータを投入する。
/// </summary>
/// <remarks>
/// <para>
/// 開発DBは得意先2812件・仕入先501件すべてが締日99（末日）で、20日締めの境界を検証できない。
/// そこで**専用の得意先を1件だけ追加**し、境界日ちょうどの売上を並べる。
/// 既存の実マスタ（締日・支払条件）には触らないため、影響が局所化される。
/// 自社締日 `MasterSysman.ShimeBi` は99のまま変更しない（影響範囲が広すぎるため）。
/// </para>
/// <para>
/// 締日20のとき、請求月の対象期間は <c>SummaryDb.GetClosingPeriod</c> により
/// 「前月21日〜当月20日」となる。境界の直前・当日・翌日に金額の違う売上を置くことで、
/// 誤って隣の月へ計上された場合に金額で必ず判別できるようにしている。
/// </para>
/// <para>
/// 再実行しても累積しないよう、投入前に対象得意先の伝票と集計を掃除する。
/// 削除対象は追加した得意先に限定され、単独で完結する。
/// </para>
/// </remarks>
public static class ShimeBoundarySeeder {
	/// <summary>検証用得意先のコード。UAT専用と分かる値にする。</summary>
	public const string TokuiCode = "UATVM-T20";
	/// <summary>検証用得意先の締日。</summary>
	public const int Shime = 20;

	/// <summary>1件の売上と、それが属するべき請求月。</summary>
	public sealed record SalesRow(string KakeDay, int Total, int Tax, string ExpectedBillingMonth);

	/// <summary>請求月ごとの期待値。</summary>
	public sealed record Expected(string BillingMonth, string DayFrom, string DayTo, int Uriage, int Tax, int TotalSales, int Balance);

	/// <summary>投入結果。シナリオはこの期待値と実際の画面・DB値を突き合わせる。</summary>
	public sealed record Result(long TokuiId, string TokuiCode, int Shime, List<SalesRow> Sales, List<Expected> Expectations);

	/// <summary>
	/// 境界検証データ。金額はすべて異なる値にして、混入時に判別できるようにしている。
	/// 20260620 は請求月202606に属するため、202607〜202609のどの計算にも現れてはならない。
	/// </summary>
	private static readonly List<SalesRow> _sales = [
		new("20260620", 10_000, 1_000, "202606"),
		new("20260621", 20_000, 2_000, "202607"),
		new("20260720", 30_000, 3_000, "202607"),
		new("20260721", 40_000, 4_000, "202608"),
		new("20260820", 50_000, 5_000, "202608"),
		new("20260821", 60_000, 6_000, "202609"),
	];

	/// <summary>検証対象の請求月。</summary>
	private static readonly string[] _billingMonths = ["202607", "202608", "202609"];

	/// <summary>
	/// データを投入し、期待値を返す。
	/// </summary>
	/// <param name="dbPath">対象SQLiteのパス。</param>
	/// <param name="trace">経過の記録先。</param>
	public static Result Seed(string dbPath, Action<string> trace) {
		ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
		if (!File.Exists(dbPath)) throw new FileNotFoundException("対象DBが見つかりません。", dbPath);

		var connectionString = new SqliteConnectionStringBuilder {
			DataSource = dbPath,
			Mode = SqliteOpenMode.ReadWrite,
			Pooling = false,
		}.ToString();

		using var connection = new SqliteConnection(connectionString);
		connection.Open();
		var db = new ExDatabaseSqlite(connection) { KeepConnectionAlive = true };

		var tokui = EnsureTokui(db, trace);
		Clean(db, tokui.Id, trace);
		InsertSales(db, tokui.Id, trace);

		var expectations = BuildExpectations();
		foreach (var e in expectations) {
			trace($"期待値 {e.BillingMonth}: 期間={e.DayFrom}〜{e.DayTo} 売上={e.Uriage:N0} 税={e.Tax:N0} 売上額={e.TotalSales:N0} 残高={e.Balance:N0}");
		}
		return new Result(tokui.Id, TokuiCode, Shime, _sales, expectations);
	}

	/// <summary>検証用得意先を用意する。既にあれば締日と支払条件だけ揃える。</summary>
	private static MasterTokui EnsureTokui(ExDatabaseSqlite db, Action<string> trace) {
		var existing = db.Fetch<MasterTokui>("where Code=@0", TokuiCode).FirstOrDefault();
		if (existing != null) {
			if (existing.Shime1 != Shime) {
				db.Execute($"UPDATE {db.GetTableName(typeof(MasterTokui))} SET Shime1=@0 WHERE Id=@1", Shime, existing.Id);
				trace($"得意先 {TokuiCode} の締日を{Shime}へ更新 Id={existing.Id}");
				existing.Shime1 = Shime;
			}
			else {
				trace($"得意先 {TokuiCode} は既に存在 Id={existing.Id} 締日={existing.Shime1}");
			}
			return existing;
		}

		// 支払条件は既存の実マスタと同じ「翌月末回収」(PayMonth=1, PayDay=99) に合わせる。
		var employee = db.Fetch<MasterShain>("order by Id").First();
		var tokui = new MasterTokui {
			Code = TokuiCode,
			Name = "UAT-VM 締日20 境界検証",
			Ryaku = "UAT-VM T20",
			Shime1 = Shime,
			PayMonth = 1,
			PayDay = 99,
			Id_Shain = employee.Id,
			VShain = new CodeNameView(employee.Id, employee.Code, employee.Name),
		};
		var vdate = Common.GetVdate();
		tokui.Vdc = vdate;
		tokui.Vdu = vdate;
		try {
			db.BeginTransaction(IsolationLevel.Serializable);
			db.Insert(tokui);
			db.CompleteTransaction();
		}
		catch {
			db.AbortTransaction();
			throw;
		}
		trace($"得意先 {TokuiCode} を追加 Id={tokui.Id} 締日={Shime}");
		return tokui;
	}

	/// <summary>再実行で累積しないよう、対象得意先の伝票と集計を消す。</summary>
	private static void Clean(ExDatabaseSqlite db, long tokuiId, Action<string> trace) {
		var uriage = db.Execute("DELETE FROM Tran00Uriage WHERE Id_Tokui=@0", tokuiId);
		var nyukin = db.Execute("DELETE FROM Tran06Nyukin WHERE Id_Torisaki=@0", tokuiId);
		var kake = db.Execute("DELETE FROM SummaryUriKake WHERE Id_Tokui=@0", tokuiId);
		var sei = db.Execute("DELETE FROM SummaryUriSei WHERE Id_Tokui=@0", tokuiId);
		trace($"掃除 Tran00Uriage={uriage} Tran06Nyukin={nyukin} SummaryUriKake={kake} SummaryUriSei={sei}");
	}

	private static void InsertSales(ExDatabaseSqlite db, long tokuiId, Action<string> trace) {
		try {
			db.BeginTransaction(IsolationLevel.Serializable);
			foreach (var row in _sales) {
				var tran = new Tran00Uriage {
					DenDay = row.KakeDay,
					KakeDay = row.KakeDay,
					Id_Tokui = tokuiId,
					Total = row.Total,
					KingakuTotal = row.Total,
					Tax = row.Tax,
					IsPay = 1,
					// 明細(Jmeisai)を持たせないと json_each(Jmeisai) が null 要素を1件生み、
					// 在庫Rebuild(SummaryDb.CalcSummaryStockTrn)のSUM(json_extract(...,'$.Su'))が
					// NULLになって SummaryStock.InQty のNOT NULL制約違反を起こす（C-10で発覚）。
					Jmeisai = [new Tran99Meisai { No = 1, Su = 1, Kingaku = row.Total }],
				};
				tran.EnKubun = EnumUri00.Uriage;
				db.Insert(tran);
			}
			db.CompleteTransaction();
		}
		catch {
			db.AbortTransaction();
			throw;
		}
		trace($"売上を{_sales.Count}件投入（{string.Join(", ", _sales.Select(x => $"{x.KakeDay}:{x.Total:N0}"))}）");
	}

	/// <summary>
	/// 請求月ごとの期待値を、投入データから算出する。
	/// </summary>
	/// <remarks>
	/// 残高は `TotalIn - TotalSales` の累積である（`CalcSummaryUriSei` の previousBalance が
	/// 期間ごとの差分を合算するため）。入金は投入していないので売上額の累積の符号反転になる。
	/// </remarks>
	private static List<Expected> BuildExpectations() {
		var result = new List<Expected>();
		var cumulative = 0;
		foreach (var month in _billingMonths) {
			var period = ClosingMonthCalculator.GetPeriod(month, Shime);
			var rows = _sales.Where(x => x.ExpectedBillingMonth == month).ToList();
			var uriage = rows.Sum(x => x.Total);
			var tax = rows.Sum(x => x.Tax);
			var totalSales = uriage + tax;
			cumulative -= totalSales;
			result.Add(new Expected(month, period.DayFrom, period.DayTo, uriage, tax, totalSales, cumulative));
		}
		return result;
	}
}
