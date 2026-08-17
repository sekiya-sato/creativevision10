using CvBase;

namespace CvWpfclient.ViewModels._07Haibun;

/// <summary>
/// 引当問合わせ（入庫側）。旧CV.net【配分】-【引当問合わせ】に相当する。
/// 商品別に、倉庫×色サイズの<b>引当数</b>（<see cref="SummaryRealStock.ReserveQty"/>）を展開する。
/// <para>
/// 引当数の定義は <c>SummaryDb.ReserveTargetWhere</c>（<c>EndFlag=0 AND Kubun&lt;&gt;0</c>）／
/// <c>ReserveQtySumExpr</c>（未確定=Su／確定済み=JitsuSu）に集約済みで、結果は
/// <see cref="SummaryRealStock.ReserveQty"/> へ materialize 済み。ここでは再集計せず列を読む。
/// 仕様は `Doc/spec/2026-08-18_I9_配分照会3画面_詳細設計.md` を参照する。
/// </para>
/// </summary>
public sealed class HikiateQueryViewModel : BaseHaibunInquiryViewModel {
	protected override string DrillLabel => "引当数";

	protected override async Task<List<SummaryRealStock>> LoadDrillRowsAsync(long shohinId, CancellationToken ct) {
		List<string> parameters = [];
		List<string> clauses = BuildStockClauses("R", "D", "Soko", parameters, [shohinId]);
		string sql = $"""
			SELECT
				0 AS Id, 0 AS Vdc, 0 AS Vdu,
				R.Id_Soko, R.Id_Shohin, R.Id_Col, R.Id_Siz,
				R.ReserveQty AS Su,
				0 AS ReserveQty
			FROM SummaryRealStock R
				LEFT JOIN DerivedShohinColSiz D
					ON D.Id_Shohin = R.Id_Shohin
					AND D.Id_Col = R.Id_Col
					AND D.Id_Siz = R.Id_Siz
				LEFT JOIN MasterTokui Soko ON Soko.Id = R.Id_Soko
			WHERE {string.Join(" AND ", clauses)}
			ORDER BY Soko.Code, D.Code_Col, D.Code_Siz
			""";

		return await QuerySqlListAsync<SummaryRealStock>(sql, parameters, ct);
	}
}
