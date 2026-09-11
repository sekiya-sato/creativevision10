namespace CvBase;

/// <summary>
/// C4（他伝票との競合）・C6（恒久上代変更との基準不整合）の判定SQLを1箇所に集約する（設計書2.8・6.3）。
/// <para>
/// <c>CvDomainLogic.JodaiConflictChecker</c>（NPoco経由、サーバ側）と<c>CvWpfclient</c>の画面（④確認タブ、
/// gRPCの<c>QueryListSqlParam</c>経由。設計書6.3「競合検出とプレビューはQueryListSqlParamで取得する」）の
/// 双方が同じSQL文字列を使うための共有ビルダ。<c>CvWpfclient</c>は<c>CvDomainLogic</c>を参照できない
/// （層1.5はサーバ側専用。<c>CvWpfclient.csproj</c>は<c>CodeShare</c>/<c>CvAsset</c>/<c>CvBase</c>のみ参照）ため、
/// SQL組み立ての中核だけを双方が参照できる<c>CvBase</c>（層1）へ切り出した
/// （<see cref="JodaiPriceRule"/>・<see cref="JodaiScopeResolver"/>と同じ方針。設計書6.1「CvBaseは
/// CvWpfclientとCvServerの双方から依存できる最も低い層」。タスク指示「SQLを二重に書かないこと」）。
/// </para>
/// </summary>
public static class JodaiConflictSql {
	/// <summary>C4・C6の問い合わせ結果1行。</summary>
	public sealed class OtherSlipRow {
		public long Id_Shohin { get; set; }
		public string Code_Shohin { get; set; } = string.Empty;
		public string Mei_Shohin { get; set; } = string.Empty;
		public long Id_Tenpo { get; set; }
		public int Jodai { get; set; }
		public long Id_Tran { get; set; }
	}

	/// <summary>
	/// C4（<paramref name="properOnly"/>=false）／C6（true。<c>Kubun=Proper</c>のみ）の判定SQLを組み立てる
	/// （設計書2.8。SQL例は同節に例示済み）。
	/// <para>
	/// パラメータは呼び出し側の実行方法（NPocoの位置引数／gRPCの<c>QueryListSqlParam</c>）によらず
	/// <c>@0</c>=自伝票Id、<c>@1</c>=TaishoType、<c>@2</c>=DayTo（期間終端）、<c>@3</c>=DayFrom（期間始端）の
	/// 並びで統一する。呼び出し側はこの順で4つの値を渡すこと。
	/// </para>
	/// <para>
	/// <paramref name="shohinIds"/>/<paramref name="tenpoIds"/>はSQL文字列へ直接埋め込む（内部Idの一覧であり、
	/// 対象商品・対象店舗が万単位でもパラメータ配列を増やさないための既存の方式を踏襲）。
	/// </para>
	/// </summary>
	/// <param name="shohinIds">対象商品Idの一覧。空なら常に0件を返すSQLを返す（呼び出し元の空チェックの手間を省く）。</param>
	/// <param name="tenpoIds">対象店舗Idの一覧。0（全件ワイルドカード）を含む場合は店舗条件を付けない。</param>
	/// <param name="properOnly">true でC6（<c>Kubun=Proper</c>のみ）、false でC4（区分を問わない）。</param>
	public static string BuildOtherSlipConflictSql(IReadOnlyList<long> shohinIds, IReadOnlyList<long> tenpoIds, bool properOnly) {
		if (shohinIds.Count == 0) {
			return "SELECT 0 AS Id_Shohin, '' AS Code_Shohin, '' AS Mei_Shohin, 0 AS Id_Tenpo, 0 AS Jodai, 0 AS Id_Tran WHERE 0=1";
		}
		var restrictTenpo = !tenpoIds.Contains(0);
		return @$"
SELECT dj.Id_Shohin, sh.Code AS Code_Shohin, sh.Name AS Mei_Shohin, dj.Id_Tenpo, dj.Jodai, dj.Id_Tran
  FROM {nameof(DerivedJodai)} dj
  JOIN {nameof(MasterShohin)} sh ON sh.Id = dj.Id_Shohin
 WHERE dj.Id_Tran <> @0
   AND dj.TaishoType = @1
   {(properOnly ? $"AND dj.Kubun = {(int)EnumJodaiKubun.Proper}" : "")}
   AND dj.Id_Shohin IN ({string.Join(",", shohinIds)})
   {(restrictTenpo ? $"AND dj.Id_Tenpo IN ({string.Join(",", tenpoIds)},0)" : "")}
   AND dj.DayFrom <= @2 AND dj.DayTo >= @3
 ORDER BY dj.Id_Shohin, dj.Id_Tenpo";
	}

	/// <summary>
	/// <paramref name="jshop"/>全行の適用期間（店舗別期間が空ならヘッダ既定期間へフォールバック。
	/// <see cref="DerivedJodai.CreateSql"/>のR2と同じ規則）の和（最小DayFrom〜最大DayTo）を返す。
	/// C4・C6の検出範囲を緩めに（見逃しなく）取るための全体幅であり、個々の店舗×商品の厳密な期間ではない
	/// （設計書2.8「伝票全体の期間幅で緩く判定する」）。
	/// </summary>
	public static (string DayFrom, string DayTo) OverallPeriod(IReadOnlyList<TranJodaiShop> jshop, string headerDayFrom, string headerDayTo) {
		if (jshop.Count == 0) {
			return (headerDayFrom, headerDayTo);
		}
		var froms = jshop.Select(s => string.IsNullOrEmpty(s.DayFrom) ? headerDayFrom : s.DayFrom).ToList();
		var tos = jshop.Select(s => string.IsNullOrEmpty(s.DayTo) ? headerDayTo : s.DayTo).ToList();
		// yyyyMMdd固定長文字列なので序数比較がそのまま日付の大小比較になる（JodaiScopeResolver.PeriodsOverlapと同じ理由）。
		return (froms.Min(StringComparer.Ordinal)!, tos.Max(StringComparer.Ordinal)!);
	}
}
