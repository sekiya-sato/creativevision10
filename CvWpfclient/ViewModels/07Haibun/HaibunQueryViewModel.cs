using CvBase;

namespace CvWpfclient.ViewModels._07Haibun;

/// <summary>
/// 配分問合わせ（出庫側）。旧CV.net【配分】-【配分問合わせ】に相当する。
/// 商品別に、倉庫×色サイズの<b>配分数</b>（<see cref="TranHaibun"/> の未完了行 <c>EndFlag=0</c>）を展開する。
/// <para>
/// 配分数は初回配分(<c>Kubun=0</c>)を含む生の振り分け数で、引当数（<c>Kubun&lt;&gt;0</c>・確定切替）とは定義が異なる。
/// 仕様は `Doc/spec/archive/2026-08-18_I9_配分照会3画面_詳細設計.md` および同 2026-08-17 判断材料 5.1.0 / I9 を参照する。
/// </para>
/// </summary>
public sealed class HaibunQueryViewModel : BaseHaibunInquiryViewModel {
	protected override string DrillLabel => "配分数";

	protected override async Task<List<SummaryRealStock>> LoadDrillRowsAsync(long shohinId, CancellationToken ct) {
		List<string> parameters = [];
		List<string> clauses = BuildHaibunClauses("h", "D", "Soko", parameters, shohinId);
		string sql = $"""
			SELECT
				0 AS Id, 0 AS Vdc, 0 AS Vdu,
				h.Id_Soko, h.Id_Shohin, h.Id_Col, h.Id_Siz,
				IFNULL(SUM(h.Su), 0) AS Su,
				0 AS ReserveQty
			FROM TranHaibun h
				LEFT JOIN DerivedShohinColSiz D
					ON D.Id_Shohin = h.Id_Shohin
					AND D.Id_Col = h.Id_Col
					AND D.Id_Siz = h.Id_Siz
				LEFT JOIN MasterTokui Soko ON Soko.Id = h.Id_Soko
			WHERE {string.Join(" AND ", clauses)}
			GROUP BY h.Id_Soko, h.Id_Shohin, h.Id_Col, h.Id_Siz
			ORDER BY Soko.Code, D.Code_Col, D.Code_Siz
			""";

		return await QuerySqlListAsync<SummaryRealStock>(sql, parameters, ct);
	}
}
