using CvBase;

namespace CvWpfclient.ViewModels._07Haibun;

/// <summary>
/// 有効在庫問合わせ。旧CV.net【配分】-【有効在庫問合わせ】に相当する。
/// 商品別に、倉庫×色サイズの<b>有効在庫</b>（<see cref="SummaryRealStock.Su"/> − <see cref="SummaryRealStock.ReserveQty"/>）を展開する。
/// <para>
/// 旧は「受注済みで出荷未完了」も差し引いたが、CV10 は受注残を引かない（2026-08-17 決定 I1-y）。
/// 引当の源泉は <see cref="TranHaibun"/> だけである。在庫実績が無いSKUへ配分するとマイナスで見える（意図どおり）。
/// 仕様は `Doc/spec/2026-08-18_I9_配分照会3画面_詳細設計.md` を参照する。
/// </para>
/// </summary>
public sealed class YukoZaikoQueryViewModel : BaseHaibunInquiryViewModel {
	protected override string DrillLabel => "有効在庫";

	protected override async Task<List<SummaryRealStock>> LoadDrillRowsAsync(long shohinId, CancellationToken ct) {
		List<string> parameters = [];
		List<string> clauses = BuildStockClauses("R", "D", "Soko", parameters, [shohinId]);
		string sql = $"""
			SELECT
				0 AS Id, 0 AS Vdc, 0 AS Vdu,
				R.Id_Soko, R.Id_Shohin, R.Id_Col, R.Id_Siz,
				(R.Su - R.ReserveQty) AS Su,
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
