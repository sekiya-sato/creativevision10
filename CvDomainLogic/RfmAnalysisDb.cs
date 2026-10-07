using System.Globalization;
using CvBase;

namespace CvDomainLogic;

/// <summary>
/// RFMクロス分析表（C08）の集計。読み取りのみでDBは変更しない。
/// <para>
/// 源泉は顧客付きの店舗売上（Tran01Tenuri）で、期間は DayFrom～BaseDay（未来日付の売上は含めない）。
/// R=基準日−最終購入日。最終購入日は CalcFlag=1 の数量合計が正の日で、返品だけの日は来店とみなさない。
/// F=CalcFlag=1 の伝票数（数量が負の取消伝票は −1、0未満は0）。返品伝票は回数に影響しない。
/// M=(KingakuTotal+消費税)×CalcFlag の純額（税込、返品を差し引く）。
/// 期間内に購入日がない顧客は「購入なし」群とし、ランク集計に含めない。
/// </para>
/// </summary>
public sealed class RfmAnalysisDb(ExDatabase db) {
	private sealed class DayRow {
		public long Id_Customer { get; set; }
		public string DenDay { get; set; } = string.Empty;
		public long Qty { get; set; }
		public long Freq { get; set; }
		public long Amount { get; set; }
	}

	private sealed class CustomerName {
		public long Id { get; set; }
		public string Code { get; set; } = string.Empty;
		public string Name { get; set; } = string.Empty;
		public string Kana { get; set; } = string.Empty;
	}

	private sealed record Score(long Id_Customer, string LastDay, int Days, long Frequency, long Amount, int RRank, int FRank, int MRank);

	/// <summary>R×F×Mランク別の顧客数・金額を集計する。</summary>
	public RfmAnalysisResult Analyze(RfmAnalysisParameter p) {
		var scores = Calc(p);
		var cells = scores
			.GroupBy(s => (s.RRank, s.FRank, s.MRank))
			.Select(g => new RfmCell { RRank = g.Key.RRank, FRank = g.Key.FRank, MRank = g.Key.MRank, Count = g.Count(), Amount = g.Sum(x => x.Amount) })
			.OrderBy(c => c.RRank).ThenBy(c => c.FRank).ThenBy(c => c.MRank)
			.ToList();
		var target = db.ExecuteScalar<long>(db.TranslateDialect(p.IncludeWithdrawn
			? "SELECT COUNT(*) FROM MasterEndCustomer"
			: "SELECT COUNT(*) FROM MasterEndCustomer c WHERE NOT EXISTS (SELECT 1 FROM MasterEndCustomerAccount a WHERE a.Id_Customer=c.Id AND a.IsWithdrawalFlag=1)"));
		return new RfmAnalysisResult { Cells = cells, TargetCount = target, NoPurchaseCount = target - scores.Count };
	}

	/// <summary>指定ランクの顧客を金額降順で最大 Limit 件返す。ランク0は指定なし。</summary>
	public RfmCustomerListResult ListCustomers(RfmAnalysisParameter p) {
		var hits = Calc(p)
			.Where(s => (p.RRank == 0 || s.RRank == p.RRank) && (p.FRank == 0 || s.FRank == p.FRank) && (p.MRank == 0 || s.MRank == p.MRank))
			.OrderByDescending(s => s.Amount).ThenBy(s => s.Id_Customer)
			.ToList();
		var limit = p.Limit > 0 ? p.Limit : RfmRank.DefaultLimit;
		var page = hits.Take(limit).ToList();
		var names = new Dictionary<long, CustomerName>();
		foreach (var chunk in page.Select(s => s.Id_Customer).Chunk(500)) {
			// Idはlongのみのため直接埋め込む
			foreach (var n in db.FetchDialect<CustomerName>($"SELECT Id, Code, Name, Kana FROM MasterEndCustomer WHERE Id IN ({string.Join(",", chunk)})")) {
				names[n.Id] = n;
			}
		}
		var rows = page.Select(s => {
			var n = names.GetValueOrDefault(s.Id_Customer);
			return new RfmCustomerRow {
				Id_Customer = s.Id_Customer, Code = n?.Code ?? string.Empty, Name = n?.Name ?? string.Empty, Kana = n?.Kana ?? string.Empty,
				LastDay = s.LastDay, Days = s.Days, Frequency = s.Frequency, Amount = s.Amount,
				RRank = s.RRank, FRank = s.FRank, MRank = s.MRank,
			};
		}).ToList();
		return new RfmCustomerListResult { Rows = rows, TotalCount = hits.Count };
	}

	/// <summary>購入顧客ごとの R/F/M 値とランクを計算する。</summary>
	private List<Score> Calc(RfmAnalysisParameter p) {
		var error = RfmRank.Validate(p);
		if (error != null) throw new ArgumentException(error);
		var baseDate = ParseDay(p.BaseDay, "基準日");
		var fromDate = ParseDay(p.DayFrom, "期間開始日");
		if (fromDate > baseDate) throw new ArgumentException("期間開始日は基準日以前を指定してください。");

		var args = new List<object> { p.DayFrom, p.BaseDay };
		var tenpo = string.Empty;
		if (p.Id_Tenpo > 0) {
			tenpo = " AND t.Id_Tenpo=@2";
			args.Add(p.Id_Tenpo);
		}
		var withdrawn = p.IncludeWithdrawn ? string.Empty
			: " AND NOT EXISTS (SELECT 1 FROM MasterEndCustomerAccount a WHERE a.Id_Customer=t.Id_Customer AND a.IsWithdrawalFlag=1)";
		// 顧客×日で集計し、顧客単位の判定はC#で行う（顧客マスタに無いIdは対象外）
		var sql = $@"SELECT t.Id_Customer, t.DenDay,
 SUM(CASE WHEN t.CalcFlag=1 THEN t.SuTotal ELSE 0 END) AS Qty,
 SUM(CASE WHEN t.CalcFlag=1 AND t.SuTotal>0 THEN 1 WHEN t.CalcFlag=1 AND t.SuTotal<0 THEN -1 ELSE 0 END) AS Freq,
 SUM((t.KingakuTotal+t.Tax1+t.Tax2+t.Tax3)*t.CalcFlag) AS Amount
FROM Tran01Tenuri t
WHERE t.DenDay BETWEEN @0 AND @1 AND t.Id_Customer>0 AND t.CalcFlag<>0{tenpo}
 AND EXISTS (SELECT 1 FROM MasterEndCustomer c WHERE c.Id=t.Id_Customer){withdrawn}
GROUP BY t.Id_Customer, t.DenDay";
		var days = db.FetchDialect<DayRow>(sql, [.. args]);

		var scores = new List<Score>();
		foreach (var g in days.GroupBy(d => d.Id_Customer)) {
			var lastDay = g.Where(d => d.Qty > 0).Select(d => d.DenDay).DefaultIfEmpty(string.Empty).Max(StringComparer.Ordinal)!;
			if (lastDay.Length == 0) continue; // 返品・取消のみは購入なし
			var elapsed = (baseDate - ParseDay(lastDay, "伝票日付")).Days;
			var freq = Math.Max(0, g.Sum(d => d.Freq));
			var amount = g.Sum(d => d.Amount);
			scores.Add(new Score(g.Key, lastDay, elapsed, freq, amount,
				RfmRank.RankR(elapsed, p.RBounds), RfmRank.RankUp(freq, p.FBounds), RfmRank.RankUp(amount, p.MBounds)));
		}
		return scores;
	}

	private static DateTime ParseDay(string day, string label) =>
		DateTime.TryParseExact(day, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
			? d : throw new ArgumentException($"{label}が不正です: {day}");
}
