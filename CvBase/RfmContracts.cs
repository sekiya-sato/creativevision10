namespace CvBase;

/// <summary>
/// RFMクロス分析の条件。Msg065_RfmCrossAnalysis（集計）と Msg066_RfmCustomerList（セル明細）で使用する。
/// 源泉は顧客付きの店舗売上（Tran01Tenuri）。金額は税込（KingakuTotal+消費税）×CalcFlag の純額。
/// </summary>
public sealed class RfmAnalysisParameter {
	/// <summary>分析期間の開始日 yyyyMMdd</summary>
	public string DayFrom { get; set; } = string.Empty;
	/// <summary>基準日 yyyyMMdd。期間の終了日で、R はこの日からの経過日数</summary>
	public string BaseDay { get; set; } = string.Empty;
	/// <summary>購入店舗Id。0=全店</summary>
	public long Id_Tenpo { get; set; }
	/// <summary>退会者を含めるか</summary>
	public bool IncludeWithdrawn { get; set; }
	/// <summary>R閾値（経過日数の昇順4値）。経過日数がN番目以下ならランク5-N</summary>
	public List<int> RBounds { get; set; } = [];
	/// <summary>F閾値（購入回数の昇順4値）。以上の値の数+1がランク</summary>
	public List<long> FBounds { get; set; } = [];
	/// <summary>M閾値（購入金額の昇順4値）。以上の値の数+1がランク</summary>
	public List<long> MBounds { get; set; } = [];
	/// <summary>明細取得時のRランク（1-5）。0=指定なし</summary>
	public int RRank { get; set; }
	/// <summary>明細取得時のFランク（1-5）。0=指定なし</summary>
	public int FRank { get; set; }
	/// <summary>明細取得時のMランク（1-5）。0=指定なし</summary>
	public int MRank { get; set; }
	/// <summary>明細の最大取得件数（金額降順）</summary>
	public int Limit { get; set; }
}

/// <summary>RFMランクの段数と既定閾値。</summary>
public static class RfmRank {
	/// <summary>各軸のランク段数（5=最良）</summary>
	public const int Levels = 5;
	/// <summary>明細の既定最大件数</summary>
	public const int DefaultLimit = 1000;
	public static readonly int[] DefaultRBounds = [30, 90, 180, 270];
	public static readonly long[] DefaultFBounds = [2, 3, 5, 10];
	public static readonly long[] DefaultMBounds = [10000, 20000, 50000, 100000];

	/// <summary>経過日数のランク。日数が少ないほど高い</summary>
	public static int RankR(int days, IReadOnlyList<int> bounds) => Levels - bounds.Count(b => days > b);

	/// <summary>回数・金額のランク。値が大きいほど高い</summary>
	public static int RankUp(long value, IReadOnlyList<long> bounds) => 1 + bounds.Count(b => value >= b);

	/// <summary>閾値が段数-1個の狭義昇順で、Rは1以上であることを検査する。エラー時はメッセージを返す</summary>
	public static string? Validate(RfmAnalysisParameter p) {
		static bool Ascending<T>(IReadOnlyList<T> xs) where T : IComparable<T> =>
			xs.Count == Levels - 1 && xs.Zip(xs.Skip(1)).All(x => x.First.CompareTo(x.Second) < 0);
		if (!Ascending(p.RBounds) || p.RBounds[0] < 1) return "R閾値は1以上の昇順で4つ指定してください。";
		if (!Ascending(p.FBounds) || p.FBounds[0] < 2) return "F閾値は2以上の昇順で4つ指定してください。";
		if (!Ascending(p.MBounds) || p.MBounds[0] < 1) return "M閾値は1以上の昇順で4つ指定してください。";
		return null;
	}
}

/// <summary>R×F×Mランク1組の集計。</summary>
public sealed class RfmCell {
	public int RRank { get; set; }
	public int FRank { get; set; }
	public int MRank { get; set; }
	/// <summary>顧客数</summary>
	public long Count { get; set; }
	/// <summary>期間内の純購入額合計（税込）</summary>
	public long Amount { get; set; }
}

/// <summary>RFMクロス分析の集計結果。</summary>
public sealed class RfmAnalysisResult {
	/// <summary>購入顧客のランク別集計（該当0のセルは含めない）</summary>
	public List<RfmCell> Cells { get; set; } = [];
	/// <summary>対象顧客数（顧客マスタ。退会者除外時は除いた数）</summary>
	public long TargetCount { get; set; }
	/// <summary>期間内に有効な購入がない顧客数（購入なし群）</summary>
	public long NoPurchaseCount { get; set; }
}

/// <summary>セル明細の顧客1行。</summary>
public sealed class RfmCustomerRow {
	public long Id_Customer { get; set; }
	public string Code { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string Kana { get; set; } = string.Empty;
	/// <summary>最終購入日 yyyyMMdd</summary>
	public string LastDay { get; set; } = string.Empty;
	/// <summary>基準日からの経過日数</summary>
	public int Days { get; set; }
	/// <summary>購入回数</summary>
	public long Frequency { get; set; }
	/// <summary>純購入額（税込）</summary>
	public long Amount { get; set; }
	public int RRank { get; set; }
	public int FRank { get; set; }
	public int MRank { get; set; }
}

/// <summary>セル明細の結果。</summary>
public sealed class RfmCustomerListResult {
	/// <summary>金額降順で最大 Limit 件</summary>
	public List<RfmCustomerRow> Rows { get; set; } = [];
	/// <summary>条件に一致した全件数</summary>
	public long TotalCount { get; set; }
}
