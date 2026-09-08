using CvBase;
using CvWpfclient.ViewModels._02Yosan;
using CvWpfclient.Views._02Yosan;
using System.Data;

namespace UatVm.Scenarios;

/// <summary>
/// 「店別売上表」照会画面（<see cref="DailyShopBudgetQueryView"/>）のUAT。
/// </summary>
/// <remarks>
/// 検証年月は 2023/12 を使う。事前に cv-sqlite MCP で確認した内容:
/// <list type="bullet">
/// <item>Tran01Tenuri × MasterTokui.TenType=6 で 2023/12 に売上があるのは
/// 000014(富山)/000023(桑名)/000027(大和)/000028(イオンモール盛岡)の4店舗のみ（売上合計111,816円）。</item>
/// <item>MasterYosanBrand（予算）は 202206 分までしか無く、2023/12 分は無い
/// （＝この月の予算比は 0 になるのが正しい。予算そのものの整合はSQL側の合計値と突き合わせる）。</item>
/// <item>前年同月（2022/12）に売上があるのは 000016/000032 のみで、上記4店舗とは重ならないため、
/// 前年売上列は全て0になるのが正しい。</item>
/// </list>
/// 添付の旧CVnet CSV（2019/09）は現DBの実データと数値が対応しないため数値完全一致照合はせず、
/// 本シナリオは「DataTable内部の整合性」と「DB実データとの突合」で検証する。
/// </remarks>
public static class ShopDailySalesQueryScenario {
	const string YearMonth = "2023/12";
	const string DateFrom = "20231201";
	const string DateTo = "20231231";
	const string PrevDateFrom = "20221201";
	const string PrevDateTo = "20221231";
	const int DaysInMonth = 31;
	const string SingleShopCode = "000023";

	static readonly HashSet<string> AggregateColumns = ["日付", "曜日", "売上計", "予算計", "予算比", "前年売上計", "前年売上比"];

	public static async Task RunAsync(VmSession session) {
		var d = session.OpenView<DailyShopBudgetQueryView, DailyShopBudgetQueryViewModel>();

		// --- 1) 店別モード：全店（コード範囲指定なし） ---
		d.Input("条件:店別/全店/2023-12", vm => {
			vm.SelectedYearMonthString = YearMonth;
			vm.ShopCodeFrom = "";
			vm.ShopCodeTo = "";
			vm.IsByShop = true;
		});
		await d.RunAsync("検索:店別", vm => vm.SearchCommand);

		if (!session.Check("店別:結果表示", d.Vm.IsResultVisible, new { d.Vm.Message })) return;
		var detail = d.Vm.ResultTable;
		var total = d.Vm.TotalTable;
		if (!session.Check("店別:テーブル取得", detail != null && total != null, new { d.Vm.Message })) return;

		session.CheckEqual("店別:日数", DaysInMonth, detail!.Rows.Count);

		var shopColumns = ShopColumns(detail);

		var dbShopCount = await session.QueryAsync<ShopDailySalesRow>("""
			SELECT u.Id_Tenpo AS Id_Tenpo, 0 AS Day, 0 AS Sales, 0 AS Budget, 0 AS PrevSales
			FROM Tran01Tenuri u JOIN MasterTokui m ON m.Id = u.Id_Tenpo AND m.TenType = 6
			WHERE u.DenDay BETWEEN @0 AND @1
			GROUP BY u.Id_Tenpo
			""", DateFrom, DateTo);
		session.CheckEqual("店別:対象店舗数(DB実データ)", dbShopCount.Count, shopColumns.Count);

		var expectedColumnOrder = new List<string> { "日付", "曜日" };
		expectedColumnOrder.AddRange(shopColumns);
		expectedColumnOrder.AddRange(["売上計", "予算計", "予算比", "前年売上計", "前年売上比"]);
		session.CheckEqual("店別:列順",
			string.Join(",", expectedColumnOrder),
			string.Join(",", detail.Columns.Cast<DataColumn>().Select(c => c.ColumnName)));

		// 縦計/横計の整合。DataTable内部の整合性チェックであり、DBへは問い合わせない。
		var totalRow = total!.Rows.Cast<DataRow>().Single(r => (string)r["日付"] == "売上計");
		foreach (var col in shopColumns) {
			double colSum = detail.Rows.Cast<DataRow>().Sum(r => Convert.ToDouble(r[col]));
			session.CheckEqual($"店別:縦計整合[{col}]", colSum, Convert.ToDouble(totalRow[col]));
		}
		foreach (DataRow row in detail.Rows) {
			double rowShopSum = shopColumns.Sum(col => Convert.ToDouble(row[col]));
			session.CheckEqual($"店別:横計整合[{row["日付"]}]", rowShopSum, Convert.ToDouble(row["売上計"]));
		}

		double salesGrandTotal = detail.Rows.Cast<DataRow>().Sum(r => Convert.ToDouble(r["売上計"]));
		session.CheckEqual("店別:売上計合計(TotalTable)", salesGrandTotal, Convert.ToDouble(totalRow["売上計"]));

		double budgetGrandTotal = detail.Rows.Cast<DataRow>().Sum(r => Convert.ToDouble(r["予算計"]));
		var ratioRow = total.Rows.Cast<DataRow>().Single(r => (string)r["日付"] == "予算比");
		double expectedRatio = budgetGrandTotal != 0 ? Math.Round(salesGrandTotal * 100.0 / budgetGrandTotal, 1) : 0.0;
		session.CheckEqual("店別:予算比整合", expectedRatio, Convert.ToDouble(ratioRow["予算比"]));

		// DB実データとの突合（対象店舗は「その月に売上がある店舗」に自然に限定される: JOIN先の集計で
		// 売上0件の店舗は行そのものが現れないため、VM側の対象店舗抽出条件と一致する）。
		var dbFacts = await session.QueryAsync<ShopDailySalesRow>("""
			SELECT 0 AS Id_Tenpo, 0 AS Day, SUM(u.KingakuTotal) AS Sales, 0 AS Budget, 0 AS PrevSales
			FROM Tran01Tenuri u JOIN MasterTokui m ON m.Id = u.Id_Tenpo AND m.TenType = 6
			WHERE u.DenDay BETWEEN @0 AND @1
			""", DateFrom, DateTo);
		session.CheckEqual("店別:DB実額との突合", dbFacts.Single().Sales, (long)Math.Round(salesGrandTotal));

		// 前年同月(2022/12)は対象4店舗の売上が無いため、前年売上列は全て0になるはず。
		bool prevAllZero = detail.Rows.Cast<DataRow>().All(r => Convert.ToDouble(r["前年売上計"]) == 0);
		session.Check("店別:前年売上(データ無し年)は全て0", prevAllZero);
		session.CheckEqual("店別:前年売上比(合計)は0",
			0.0, Convert.ToDouble(total.Rows.Cast<DataRow>().Single(r => (string)r["日付"] == "前年売上比")["前年売上比"]));

		// SUM()にGROUP BYを付けない集合関数クエリは対象行が0件でも必ず1行返る(NULL→COALESCEで0)ため、
		// 行数ではなく合計金額そのものを検証する。
		var dbPrev = await session.QueryAsync<ShopDailySalesRow>("""
			SELECT 0 AS Id_Tenpo, 0 AS Day, COALESCE(SUM(u.KingakuTotal), 0) AS Sales, 0 AS Budget, 0 AS PrevSales
			FROM Tran01Tenuri u JOIN MasterTokui m ON m.Id = u.Id_Tenpo AND m.TenType = 6
			WHERE u.DenDay BETWEEN @0 AND @1
			  AND m.Code IN ('000014','000023','000027','000028')
			""", PrevDateFrom, PrevDateTo);
		session.CheckEqual("店別:前年同月に対象店舗の売上が無いことをDBで確認", 0L, dbPrev.Single().Sales);

		// 日計モードとの突合用に、日付→売上計を保持しておく。
		var byShopDaySales = detail.Rows.Cast<DataRow>()
			.ToDictionary(r => (string)r["日付"], r => Convert.ToDouble(r["売上計"]));

		// --- 2) 日計モード（同一条件、IsByShop=false） ---
		d.Input("条件:日計へ切替", vm => vm.IsByShop = false);
		await d.RunAsync("検索:日計", vm => vm.SearchCommand);
		var dailyDetail = d.Vm.ResultTable;
		if (!session.Check("日計:テーブル取得", dailyDetail != null, new { d.Vm.Message })) return;

		session.CheckEqual("日計:列数", 7, dailyDetail!.Columns.Count);
		session.CheckEqual("日計:列順",
			"日付,曜日,売上計,予算計,予算比,前年売上計,前年売上比",
			string.Join(",", dailyDetail.Columns.Cast<DataColumn>().Select(c => c.ColumnName)));

		bool dailyMatchesByShop = dailyDetail.Rows.Cast<DataRow>()
			.All(r => Math.Abs(Convert.ToDouble(r["売上計"]) - byShopDaySales[(string)r["日付"]]) < 0.001);
		session.Check("日計:店別モードと同一店舗集合で一致", dailyMatchesByShop);

		// --- 3) 店舗CD範囲の絞り込み（1店舗のみになる範囲） ---
		d.Input("条件:店別/000023のみ", vm => {
			vm.IsByShop = true;
			vm.ShopCodeFrom = SingleShopCode;
			vm.ShopCodeTo = SingleShopCode;
		});
		await d.RunAsync("検索:単一店舗", vm => vm.SearchCommand);
		var singleDetail = d.Vm.ResultTable;
		if (!session.Check("単一店舗:テーブル取得", singleDetail != null, new { d.Vm.Message })) return;

		var singleShopColumns = ShopColumns(singleDetail!);
		if (!session.Check("単一店舗:店舗列は1本", singleShopColumns.Count == 1, new { singleShopColumns })) return;

		double singleShopTotal = singleDetail!.Rows.Cast<DataRow>().Sum(r => Convert.ToDouble(r[singleShopColumns[0]]));
		var dbSingle = await session.QueryAsync<ShopDailySalesRow>("""
			SELECT 0 AS Id_Tenpo, 0 AS Day, SUM(u.KingakuTotal) AS Sales, 0 AS Budget, 0 AS PrevSales
			FROM Tran01Tenuri u JOIN MasterTokui m ON m.Id = u.Id_Tenpo AND m.TenType = 6
			WHERE m.Code = @0 AND u.DenDay BETWEEN @1 AND @2
			""", SingleShopCode, DateFrom, DateTo);
		session.CheckEqual("単一店舗:DB実額と一致", dbSingle.Single().Sales, (long)Math.Round(singleShopTotal));

		// --- 4) 年月移動 ---
		// 2023/11 は 000028(イオンモール盛岡)のみ売上がある月（cv-sqlite MCPで事前確認、合計13,184円/30日）。
		// 「データなし」ではなく「別の1店舗だけの月」に切り替わることを確認する。
		d.Input("条件:範囲クリア", vm => { vm.ShopCodeFrom = ""; vm.ShopCodeTo = ""; });
		await d.RunAsync("前月へ", vm => vm.PrevMonthCommand);
		session.CheckEqual("前月:2023/11相当への移動", "2023/11", d.Vm.SelectedYearMonthString);
		var prevMonthDetail = d.Vm.ResultTable;
		if (session.Check("前月:結果あり(000028のみの月)", prevMonthDetail != null, new { d.Vm.Message })) {
			session.CheckEqual("前月:日数", 30, prevMonthDetail!.Rows.Count);
			var prevMonthShopColumns = ShopColumns(prevMonthDetail);
			session.CheckEqual("前月:店舗列は1本(000028)", 1, prevMonthShopColumns.Count);
			if (prevMonthShopColumns.Count == 1) {
				double prevMonthTotal = prevMonthDetail.Rows.Cast<DataRow>().Sum(r => Convert.ToDouble(r[prevMonthShopColumns[0]]));
				var dbPrevMonth = await session.QueryAsync<ShopDailySalesRow>("""
					SELECT 0 AS Id_Tenpo, 0 AS Day, COALESCE(SUM(u.KingakuTotal), 0) AS Sales, 0 AS Budget, 0 AS PrevSales
					FROM Tran01Tenuri u JOIN MasterTokui m ON m.Id = u.Id_Tenpo AND m.TenType = 6
					WHERE u.DenDay BETWEEN '20231101' AND '20231130'
					""");
				session.CheckEqual("前月:DB実額と一致", dbPrevMonth.Single().Sales, (long)Math.Round(prevMonthTotal));
			}
		}

		await d.RunAsync("翌月へ", vm => vm.NextMonthCommand);
		session.CheckEqual("翌月:2023/12へ復帰", YearMonth, d.Vm.SelectedYearMonthString);
		session.CheckEqual("翌月:日数再確認", DaysInMonth, d.Vm.ResultTable?.Rows.Count ?? -1);

		await d.RunAsync("今月へ", vm => vm.ThisMonthCommand);
		session.Check("今月:例外なく完走(IsBusy解除)", !d.Vm.IsBusy);

		// --- 5) データが全く無い年月 ---
		d.Input("条件:データなし年月", vm => vm.SelectedYearMonthString = "1900/01");
		await d.RunAsync("検索:データなし", vm => vm.SearchCommand);
		session.Check("データなし:メッセージ",
			d.Vm.Message.Contains("対象データがありません", StringComparison.Ordinal), new { d.Vm.Message });
		session.Check("データなし:ResultTableはnull", d.Vm.ResultTable == null);
	}

	static List<string> ShopColumns(DataTable table) => table.Columns.Cast<DataColumn>()
		.Select(c => c.ColumnName)
		.Where(n => !AggregateColumns.Contains(n))
		.ToList();
}
