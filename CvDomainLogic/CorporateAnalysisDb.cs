using System.Globalization;
using System.Text;
using CvBase;

namespace CvDomainLogic;

/// <summary>
/// 全社分析（Doc/spec/2026-11-17_全社分析_設計.md）の集計。読み取りのみでDBは変更しない。
/// <para>
/// 集計はすべて1本のSQL（GROUP BY とウィンドウ関数）で行い、C# は区切りの算出・件数上限だけを担う。
/// 期末在庫は「現在庫(SummaryRealStock) − 期末より後の増減」で求める。期首から積み上げると
/// SummaryStock の全件走査になり遅いため（設計書 §3.3-2）。期首在庫 = 期末在庫 − 区切り内の増減。
/// </para>
/// <para>
/// 原価は商品マスタの現在原価(TankaGenka)。金額は伝票明細の Kingaku（税抜）× CalcFlag。
/// </para>
/// </summary>
public sealed class CorporateAnalysisDb(ExDatabase db) {
	/// <summary>在庫集計のアンカー行（期間末時点の在庫）の区切りキー。区切りキー(数字)と衝突しない</summary>
	private const string AnchorKey = "~";

	/// <summary>在庫を動かす伝票。符号・除外条件は SummaryDb.CreateSummaryStockSql と同じにする</summary>
	private static readonly string[] StockTables = [
		nameof(Tran00Uriage), nameof(Tran01Tenuri), nameof(Tran03Shiire),
		nameof(Tran05Ido), nameof(Tran10IdoOut), nameof(Tran11IdoIn), nameof(Tran61Chosei),
	];

	/// <summary>金額集計の源泉（種別 1=発注 / 2=受注 / 3=仕入 / 4=売上）</summary>
	private static readonly (string Table, int Kind)[] FlowTables = [
		(nameof(Tran13Hachu), 1), (nameof(Tran12Jyuchu), 2), (nameof(Tran03Shiire), 3),
		(nameof(Tran00Uriage), 4), (nameof(Tran01Tenuri), 4),
	];

	/// <summary>集計する。条件不正は <see cref="ArgumentException"/>。</summary>
	public CorporateAnalysisResult Analyze(CorporateAnalysisParameter p) {
		ArgumentNullException.ThrowIfNull(p);
		if (p.GroupAxis == CorporateAnalysisAxis.Shohin && p.ByBucket) {
			throw new ArgumentException("商品別の内訳は期間全体でのみ集計できます。");
		}
		var shime = new SummaryDb(db).GetOwnClosingDay();
		var fiscalStart = GetFiscalStartMonth();
		var buckets = CorporateAnalysisPeriod.Build(p.Unit, p.From, p.To, p.ByBucket, shime, fiscalStart);

		var sql = BuildSql(p, buckets, shime);
		var aggRows = db.Fetch<AggRow>(ExDatabase.SuppressAutoSelectForWith(db.TranslateDialect(sql)));
		var rows = BuildRows(buckets, aggRows);

		var total = rows.Count;
		if (p.GroupAxis == CorporateAnalysisAxis.Shohin) {
			var limit = p.Limit <= 0 ? CorporateAnalysisPeriod.DefaultLimit : Math.Min(p.Limit, CorporateAnalysisPeriod.MaxLimit);
			Func<CorporateAnalysisRow, long> key = p.SortKey switch {
				CorporateAnalysisSortKey.ClosingStock => r => r.ClosingStockAmount,
				CorporateAnalysisSortKey.Hachu => r => r.HachuAmount,
				_ => r => r.SalesAmount,
			};
			rows = [.. rows.OrderByDescending(key).ThenBy(r => r.GroupCode, StringComparer.Ordinal).Take(limit)];
		}
		return new CorporateAnalysisResult {
			Buckets = buckets, Rows = rows, TotalCount = total, ShimeBi = shime, FiscalStartMonth = fiscalStart,
		};
	}

	/// <summary>SQL の集計結果1行（区切り×軸）。在庫は区切り内の増減、アンカー行(~)は期間末の在庫。</summary>
	internal sealed class AggRow {
		public string BucketKey { get; set; } = string.Empty;
		public long GroupId { get; set; }
		public string GroupCode { get; set; } = string.Empty;
		public string GroupName { get; set; } = string.Empty;
		public long DeltaQty { get; set; }
		public long DeltaAmount { get; set; }
		public long HachuQty { get; set; }
		public long HachuAmount { get; set; }
		public long JuchuQty { get; set; }
		public long JuchuAmount { get; set; }
		public long ShiireQty { get; set; }
		public long ShiireAmount { get; set; }
		public long SalesQty { get; set; }
		public long SalesAmount { get; set; }
		public long SalesCost { get; set; }
	}

	/// <summary>
	/// 集計済みの行から期首/期末在庫を積み戻す。期末(最後の区切り)=アンカー、期末(n−1)=期首(n)=期末(n)−増減(n)。
	/// <para>
	/// SQL の結果（区切り数×軸の値の数）に対する加減算だけで、DBの読み直しはしない。
	/// SQL 側で区切り×軸の格子とウィンドウ関数を作ると、集計結果を複数回参照することになり、
	/// CTE を使い回さない MariaDB では集計が参照回数ぶん繰り返されて約3倍遅くなったため（設計書 §3.3）。
	/// </para>
	/// </summary>
	internal static List<CorporateAnalysisRow> BuildRows(IReadOnlyList<CorporateAnalysisBucket> buckets, IEnumerable<AggRow> aggRows) {
		var map = new Dictionary<(string, long), AggRow>();
		var groups = new Dictionary<long, AggRow>();
		foreach (var a in aggRows) {
			map[(a.BucketKey, a.GroupId)] = a;
			// 名称はどの行でも同じ（軸の Id から引く）。最初に見えた行を使う
			groups.TryAdd(a.GroupId, a);
		}
		var result = new List<(int Ord, CorporateAnalysisRow Row)>();
		foreach (var (gid, named) in groups) {
			var closingQty = map.GetValueOrDefault((AnchorKey, gid))?.DeltaQty ?? 0;
			var closingAmount = map.GetValueOrDefault((AnchorKey, gid))?.DeltaAmount ?? 0;
			for (var i = buckets.Count - 1; i >= 0; i--) {
				var a = map.GetValueOrDefault((buckets[i].Key, gid));
				var row = new CorporateAnalysisRow {
					BucketKey = buckets[i].Key, GroupId = gid, GroupCode = named.GroupCode, GroupName = named.GroupName,
					ClosingStockQty = closingQty, ClosingStockAmount = closingAmount,
					OpeningStockQty = closingQty - (a?.DeltaQty ?? 0), OpeningStockAmount = closingAmount - (a?.DeltaAmount ?? 0),
					HachuQty = a?.HachuQty ?? 0, HachuAmount = a?.HachuAmount ?? 0,
					JuchuQty = a?.JuchuQty ?? 0, JuchuAmount = a?.JuchuAmount ?? 0,
					ShiireQty = a?.ShiireQty ?? 0, ShiireAmount = a?.ShiireAmount ?? 0,
					SalesQty = a?.SalesQty ?? 0, SalesAmount = a?.SalesAmount ?? 0, SalesCost = a?.SalesCost ?? 0,
				};
				closingQty = row.OpeningStockQty;
				closingAmount = row.OpeningStockAmount;
				if (!IsEmpty(row)) result.Add((i, row));
			}
		}
		return [.. result.OrderBy(x => x.Ord).ThenBy(x => x.Row.GroupCode, StringComparer.Ordinal).ThenBy(x => x.Row.GroupId).Select(x => x.Row)];
	}

	static bool IsEmpty(CorporateAnalysisRow r) =>
		r.OpeningStockQty == 0 && r.OpeningStockAmount == 0 && r.ClosingStockQty == 0 && r.ClosingStockAmount == 0
		&& r.HachuQty == 0 && r.HachuAmount == 0 && r.JuchuQty == 0 && r.JuchuAmount == 0
		&& r.ShiireQty == 0 && r.ShiireAmount == 0 && r.SalesQty == 0 && r.SalesAmount == 0;

	/// <summary>MasterSysman.FiscalStartDate の月。未設定・不正は1月（暦年）とする。</summary>
	private int GetFiscalStartMonth() {
		var day = db.FirstOrDefaultDialect<string>("SELECT FiscalStartDate FROM MasterSysman ORDER BY Id LIMIT 1") ?? string.Empty;
		return day.Length >= 6 && int.TryParse(day.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m) && m is >= 1 and <= 12
			? m : 1;
	}

	/// <summary>
	/// 集計SQLを組み立てる。埋め込む値は区切り計算で検証済みの数字文字列と long の Id だけ。
	/// </summary>
	internal static string BuildSql(CorporateAnalysisParameter p, IReadOnlyList<CorporateAnalysisBucket> buckets, int shime) {
		var first = buckets[0];
		var last = buckets[^1];
		var isDay = p.Unit == CorporateAnalysisUnit.Day;

		// ---- 在庫（k=0）: アンカー行(~)＋区切り内の増減 ----
		var stock = new List<string> {
			Grouped($"SELECT '{AnchorKey}' AS b, Id_Shohin AS sid, Su AS q FROM SummaryRealStock"),
		};
		if (isDay) {
			// 日単位: 終了日の計上月より後は月次集計、終了日の翌日〜その計上月末は伝票から差し引く
			var monthTo = ClosingMonthCalculator.CalculateKakeMonth(last.DayTo, shime);
			var monthEnd = ClosingMonthCalculator.GetPeriod(monthTo, shime).DayTo;
			stock.Add(Grouped($"SELECT '{AnchorKey}' AS b, Id_Shohin AS sid, -Su AS q FROM SummaryStock WHERE SumMonth > '{monthTo}'"));
			var dayKey = p.ByBucket ? "t.DenDay" : "''";
			foreach (var table in StockTables) {
				var soko = TranCalcBase.GetCalcSoko(table).Item1;
				if (soko != 0) stock.Add(Grouped(StockMoveSql(table, "Id_Soko", soko, first.DayFrom, last.DayTo, monthEnd, dayKey)));
				var idosaki = TranCalcBase.GetCalcIdosaki(table).Item1;
				if (idosaki != 0) stock.Add(Grouped(StockMoveSql(table, "Id_Ido", idosaki, first.DayFrom, last.DayTo, monthEnd, dayKey)));
			}
		}
		else {
			stock.Add(Grouped($"SELECT '{AnchorKey}' AS b, Id_Shohin AS sid, -Su AS q FROM SummaryStock WHERE SumMonth > '{last.MonthTo}'"));
			var monthKey = p.ByBucket
				? CaseRange("SumMonth", buckets.Select(b => (b.MonthFrom, b.MonthTo, b.Key)))
				: "''";
			stock.Add(Grouped($"SELECT {monthKey} AS b, Id_Shohin AS sid, Su AS q FROM SummaryStock WHERE SumMonth BETWEEN '{first.MonthFrom}' AND '{last.MonthTo}'"));
		}

		// ---- 金額（k=1..4） ----
		var flowKey = !p.ByBucket ? "''"
			: isDay ? "h.DenDay"
			: CaseRange("h.DenDay", buckets.Select(b => (b.DayFrom, b.DayTo, b.Key)));
		var flows = FlowTables.Select(f => GroupedFlow($@"SELECT {flowKey} AS b, CAST(json_extract(m.value, '$.Id_Shohin') AS INTEGER) AS sid, {f.Kind} AS k,
  h.CalcFlag * CAST(IFNULL(json_extract(m.value, '$.Su'), 0) AS INTEGER) AS su,
  h.CalcFlag * CAST(IFNULL(json_extract(m.value, '$.Kingaku'), 0) AS INTEGER) AS kin
 FROM {f.Table} AS h CROSS JOIN json_each(h.Jmeisai) AS m
 WHERE h.DenDay BETWEEN '{first.DayFrom}' AND '{last.DayTo}' AND json_valid(h.Jmeisai)"));

		var (groupExpr, nameJoin, codeExpr, nameExpr) = Axis(p.GroupAxis);
		// 全社は gid=0 の1行。GROUP BY に整数定数を書くと列番号扱いになるため、区切りだけで括る
		var groupBy = p.GroupAxis == CorporateAnalysisAxis.None ? string.Empty : $", {groupExpr}";
		var filters = new StringBuilder();
		foreach (var f in p.Filters ?? []) {
			filters.Append($" AND {FilterColumn(f.Axis)} = {f.Id.ToString(CultureInfo.InvariantCulture)}");
		}

		return $@"WITH fact AS (
 SELECT s.b, s.sid, 0 AS k, SUM(s.q) AS su, 0 AS kin FROM (
  {string.Join("\n  UNION ALL ", stock)}
 ) AS s GROUP BY s.b, s.sid
 UNION ALL
 SELECT f.b, f.sid, f.k, SUM(f.su), SUM(f.kin) FROM (
  {string.Join("\n  UNION ALL ", flows)}
 ) AS f GROUP BY f.b, f.sid, f.k
),
agg AS (
 SELECT x.b, {groupExpr} AS gid,
  SUM(CASE WHEN x.k = 0 THEN x.su ELSE 0 END) AS dq,
  SUM(CASE WHEN x.k = 0 THEN x.su * COALESCE(sh.TankaGenka, 0) ELSE 0 END) AS da,
  SUM(CASE WHEN x.k = 1 THEN x.su ELSE 0 END) AS hq,
  SUM(CASE WHEN x.k = 1 THEN x.kin ELSE 0 END) AS ha,
  SUM(CASE WHEN x.k = 2 THEN x.su ELSE 0 END) AS jq,
  SUM(CASE WHEN x.k = 2 THEN x.kin ELSE 0 END) AS ja,
  SUM(CASE WHEN x.k = 3 THEN x.su ELSE 0 END) AS sq,
  SUM(CASE WHEN x.k = 3 THEN x.kin ELSE 0 END) AS sa,
  SUM(CASE WHEN x.k = 4 THEN x.su ELSE 0 END) AS uq,
  SUM(CASE WHEN x.k = 4 THEN x.kin ELSE 0 END) AS ua,
  SUM(CASE WHEN x.k = 4 THEN x.su * COALESCE(sh.TankaGenka, 0) ELSE 0 END) AS uc
 FROM fact AS x LEFT JOIN MasterShohin AS sh ON sh.Id = x.sid
 WHERE 1 = 1{filters}
 GROUP BY x.b{groupBy}
)
SELECT a.b AS BucketKey, a.gid AS GroupId, {codeExpr} AS GroupCode, {nameExpr} AS GroupName,
 a.dq AS DeltaQty, a.da AS DeltaAmount, a.hq AS HachuQty, a.ha AS HachuAmount, a.jq AS JuchuQty, a.ja AS JuchuAmount,
 a.sq AS ShiireQty, a.sa AS ShiireAmount, a.uq AS SalesQty, a.ua AS SalesAmount, a.uc AS SalesCost
FROM agg AS a{nameJoin}";
	}

	/// <summary>
	/// 在庫伝票1種・1側の在庫増減。終了日より後（その計上月末まで）はアンカーから差し引くため符号を反転し '~' に入れる。
	/// </summary>
	private static string StockMoveSql(string table, string sokoColumn, int flag, string dayFrom, string dayTo, string monthEnd, string dayKey) {
		var isStock = table == nameof(Tran03Shiire) ? " AND t.IsStock = 1" : string.Empty;
		return $@"SELECT CASE WHEN t.DenDay > '{dayTo}' THEN '{AnchorKey}' ELSE {dayKey} END AS b,
  CAST(json_extract(j.value, '$.Id_Shohin') AS INTEGER) AS sid,
  CAST(json_extract(j.value, '$.Su') AS INTEGER) * t.CalcFlag * {flag} * (CASE WHEN t.DenDay > '{dayTo}' THEN -1 ELSE 1 END) AS q
 FROM {table} AS t CROSS JOIN json_each(t.Jmeisai) AS j
  LEFT JOIN MasterTokui AS mt ON mt.Id = t.{sokoColumn}
  LEFT JOIN MasterShohin AS ms ON ms.Id = CAST(json_extract(j.value, '$.Id_Shohin') AS INTEGER)
 WHERE t.DenDay BETWEEN '{dayFrom}' AND '{monthEnd}' AND json_type(t.Jmeisai) = 'array'
  AND COALESCE(mt.IsZaiko, 1) = 1 AND COALESCE(ms.IsZaiko, 1) = 1{isStock}";
	}

	/// <summary>
	/// 在庫の源泉1つを (区切り, 商品) で先に集計する。行のまま UNION ALL すると、MariaDB は数百万行を
	/// 一時表へ書き出してから集計するため遅い（現在庫だけで約178万行）。源泉ごとに商品単位へ縮めてから結合する。
	/// </summary>
	private static string Grouped(string rowSelect) =>
		$"SELECT x.b, x.sid, SUM(x.q) AS q FROM ({rowSelect}) AS x GROUP BY x.b, x.sid";

	/// <summary>金額の源泉1つを (区切り, 商品, 種別) で先に集計する（理由は <see cref="Grouped"/> と同じ）。</summary>
	private static string GroupedFlow(string rowSelect) =>
		$"SELECT x.b, x.sid, x.k, SUM(x.su) AS su, SUM(x.kin) AS kin FROM ({rowSelect}) AS x GROUP BY x.b, x.sid, x.k";

	/// <summary>範囲→区切りキーの CASE 式。</summary>
	private static string CaseRange(string column, IEnumerable<(string From, string To, string Key)> ranges) {
		var sb = new StringBuilder("CASE");
		foreach (var (from, to, key) in ranges) {
			sb.Append($" WHEN {column} BETWEEN '{from}' AND '{to}' THEN '{key}'");
		}
		return sb.Append(" ELSE '' END").ToString();
	}

	/// <summary>軸ごとの集計列と名称の結合。</summary>
	private static (string Group, string Join, string Code, string Name) Axis(CorporateAnalysisAxis axis) {
		if (axis == CorporateAnalysisAxis.None) {
			return ("0", string.Empty, "''", "'全社'");
		}
		if (axis == CorporateAnalysisAxis.Shohin) {
			return ("x.sid", " LEFT JOIN MasterShohin AS nm ON nm.Id = a.gid",
				"COALESCE(nm.Code, '')", "COALESCE(nm.Name, '(商品マスタなし)')");
		}
		var kubun = MeishoKubun(axis);
		return (FilterColumn(axis), $" LEFT JOIN MasterMeisho AS nm ON nm.Id = a.gid AND nm.Kubun = '{kubun}'",
			"COALESCE(nm.Code, '')", "COALESCE(nm.Name, '(未設定)')");
	}

	/// <summary>絞込・集計に使う商品マスタの列（agg 段の別名 sh / x）。</summary>
	private static string FilterColumn(CorporateAnalysisAxis axis) => axis switch {
		CorporateAnalysisAxis.Brand => "COALESCE(sh.Id_Brand, 0)",
		CorporateAnalysisAxis.Item => "COALESCE(sh.Id_Item, 0)",
		CorporateAnalysisAxis.Tenji => "COALESCE(sh.Id_Tenji, 0)",
		CorporateAnalysisAxis.Season => "COALESCE(sh.Id_Season, 0)",
		CorporateAnalysisAxis.Material => "COALESCE(sh.Id_Material, 0)",
		CorporateAnalysisAxis.Shohin => "x.sid",
		_ => throw new ArgumentException($"絞込の軸が不正です: {axis}"),
	};

	private static string MeishoKubun(CorporateAnalysisAxis axis) => axis switch {
		CorporateAnalysisAxis.Brand => MasterMeisho.KubunBrand,
		CorporateAnalysisAxis.Item => MasterMeisho.KubunItem,
		CorporateAnalysisAxis.Tenji => MasterMeisho.KubunTenji,
		CorporateAnalysisAxis.Season => MasterMeisho.KubunSeason,
		CorporateAnalysisAxis.Material => MasterMeisho.KubunMaterial,
		_ => throw new ArgumentException($"内訳の軸が不正です: {axis}"),
	};
}
