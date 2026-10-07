using CvBase;

namespace CvWpfclient.ViewModels._07Haibun;

/// <summary>
/// 配分問合わせ（出庫側）。旧CV.net【配分】-【配分問合わせ】に相当する。
/// 商品別に、倉庫×色サイズの<b>配分数</b>（<see cref="TranHaibun"/> の未完了行 <c>EndFlag=0</c>）を展開する。
/// <para>
/// 配分数は仕入配分(<c>Kubun=0</c>)の未入荷分も含む生の振り分け数で、引当数（区分0は入荷済み数 <c>ArrivedSu</c> だけ）とは定義が異なる。
/// 引当は未完了行を対象とし、区分0はArrivedSu、他区分は未確定ならSu・旧確定済み未出荷ならJitsuSuを積む。
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
