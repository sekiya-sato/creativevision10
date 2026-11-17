using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using CvBase;
using CvWpfclient.ViewModels._23AiQuery;
using CvWpfclient.Views._23AiQuery;

namespace UatVm.Scenarios;

/// <summary>
/// 全社分析（読み取りのみ）のUAT。2021/08〜2022/07 の月単位で、推移表の売上・期首/期末在庫を独立SQLと照合し、
/// 期首(n)=期末(n−1)、内訳合計=推移の期間計、ドリルダウン・戻る・全社に戻る、日・年単位との整合、
/// 条件変更での結果の無効化、標準／最小サイズの見切れを確認する。
/// </summary>
/// <remarks>
/// 独立SQLの在庫は SummaryStock を期首から積み上げる（画面側は「現在庫−期末より後の増減」で求めるため別経路）。
/// </remarks>
public static class CorporateAnalysisScenario {
	public static async Task RunAsync(VmSession session) {
		var screens = Path.GetFullPath(Path.Combine("..", "Doc", "test", "UatVm", "out", "corporate-screens-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")));
		Directory.CreateDirectory(screens);
		var d = session.OpenView<CorporateAnalysisView, CorporateAnalysisViewModel>();
		var view = d.View;
		await Settle(view);
		await Capture(session, view, screens, "01_Initial");
		session.CheckEqual("既定:単位=月", CorporateAnalysisUnit.Month, d.Vm.SelectedUnit.Value);
		session.CheckEqual("既定:終了=当月", DateTime.Today.ToString("yyyy/MM"), d.Vm.ToText);

		// 37か月はサーバで拒否され結果なし
		session.ClearDialogs();
		d.Input("条件:2019/01-2022/01(37か月)", vm => { vm.FromText = "2019/01"; vm.ToText = "2022/01"; });
		await d.RunAsync("集計:37か月", vm => vm.SearchCommand);
		session.Check("37か月は警告で結果なし", !d.Vm.HasResult && session.Dialogs.Any(x => x.Request.Message.Contains("36")),
			new { Dialogs = session.Dialogs.Select(x => x.Request.Message) });

		// 月単位 2021/08〜2022/07
		d.Input("条件:2021/08-2022/07・ブランド", vm => {
			vm.FromText = "2021/08";
			vm.ToText = "2022/07";
			vm.SelectedAxis = vm.AxisItems.First(a => a.Value == CorporateAnalysisAxis.Brand);
		});
		var sw = Stopwatch.StartNew();
		await d.RunAsync("集計:月", vm => vm.SearchCommand);
		session.Note("集計時間(推移+内訳)", new { ms = sw.ElapsedMilliseconds });
		if (!session.Check("結果表示", d.Vm.HasResult, new { d.Vm.Message })) return;
		session.CheckEqual("推移:期間計+12か月", 13, d.Vm.TrendRows.Count);

		var expected = (await session.QueryAsync<CorporateAnalysisRow>("""
			SELECT
			 (SELECT SUM(h.CalcFlag * CAST(IFNULL(json_extract(m.value,'$.Kingaku'),0) AS INTEGER))
			   FROM Tran00Uriage h, json_each(h.Jmeisai) m WHERE json_valid(h.Jmeisai) AND h.DenDay BETWEEN '20210801' AND '20220731')
			 + (SELECT SUM(h.CalcFlag * CAST(IFNULL(json_extract(m.value,'$.Kingaku'),0) AS INTEGER))
			   FROM Tran01Tenuri h, json_each(h.Jmeisai) m WHERE json_valid(h.Jmeisai) AND h.DenDay BETWEEN '20210801' AND '20220731') AS SalesAmount,
			 (SELECT SUM(s.Su * IFNULL(sh.TankaGenka,0)) FROM SummaryStock s LEFT JOIN MasterShohin sh ON sh.Id=s.Id_Shohin WHERE s.SumMonth < '202108') AS OpeningStockAmount,
			 (SELECT SUM(s.Su * IFNULL(sh.TankaGenka,0)) FROM SummaryStock s LEFT JOIN MasterShohin sh ON sh.Id=s.Id_Shohin WHERE s.SumMonth <= '202207') AS ClosingStockAmount
			""")).Single();
		var total = d.Vm.TrendRows[0];
		session.Check("推移:先頭は期間計", total.IsTotal, new { total.Label });
		session.CheckEqual("期間計:売上=独立SQL", expected.SalesAmount, total.SalesAmount);
		session.CheckEqual("期間計:期首在庫=独立SQL(積上げ)", expected.OpeningStockAmount, total.OpeningStockAmount);
		session.CheckEqual("期間計:期末在庫=独立SQL(積上げ)", expected.ClosingStockAmount, total.ClosingStockAmount);
		var months = d.Vm.TrendRows.Skip(1).ToList();
		var chainOk = months.Zip(months.Skip(1)).All(p => p.First.ClosingStockAmount == p.Second.OpeningStockAmount);
		session.Check("期首(n)=期末(n-1)", chainOk);
		session.CheckEqual("月別売上の合計=期間計", total.SalesAmount, months.Sum(x => x.SalesAmount));

		var brands = d.Vm.BreakdownRows.Where(r => !r.IsTotal).ToList();
		session.Check("内訳:ブランド行あり", brands.Count > 0, new { d.Vm.BreakdownCaption });
		session.CheckEqual("内訳:売上合計=期間計", total.SalesAmount, brands.Sum(x => x.SalesAmount));
		session.CheckEqual("内訳:期末在庫合計=期間計", total.ClosingStockAmount, brands.Sum(x => x.ClosingStockAmount));
		session.CheckEqual("内訳:発注合計=期間計", total.HachuAmount, brands.Sum(x => x.HachuAmount));
		await Capture(session, view, screens, "02_Month_Brand");

		// 推移の行選択でその月の内訳
		var mar = d.Vm.TrendRows.First(r => r.BucketKey == "202203");
		d.Input("推移選択:2022/03", vm => vm.SelectedTrendRow = mar);
		await WaitIdle(d.Vm);
		var marBrands = d.Vm.BreakdownRows.Where(r => !r.IsTotal).ToList();
		session.CheckEqual("2022/03内訳:売上合計=推移行", mar.SalesAmount, marBrands.Sum(x => x.SalesAmount));
		session.CheckEqual("2022/03内訳:期首在庫合計=推移行", mar.OpeningStockAmount, marBrands.Sum(x => x.OpeningStockAmount));
		session.Check("2022/03内訳:見出しに年月", d.Vm.BreakdownCaption.Contains("2022/03"), new { d.Vm.BreakdownCaption });
		d.Input("推移選択:期間計", vm => vm.SelectedTrendRow = vm.TrendRows[0]);
		await WaitIdle(d.Vm);

		// ドリルダウン: 売上最大のブランド → アイテム
		var top = d.Vm.BreakdownRows.Where(r => !r.IsTotal).OrderByDescending(r => r.SalesAmount).First();
		await d.RunAsync("ドリル:ブランド", vm => vm.DrillDownCommand, top);
		session.CheckEqual("ドリル後:軸=アイテム", CorporateAnalysisAxis.Item, d.Vm.SelectedAxis.Value);
		session.Check("ドリル後:絞込表示", d.Vm.FilterText.Contains(top.Label), new { d.Vm.FilterText });
		session.CheckEqual("ドリル後:推移の売上=ブランド行", top.SalesAmount, d.Vm.TrendRows[0].SalesAmount);
		session.CheckEqual("ドリル後:推移の期末在庫=ブランド行", top.ClosingStockAmount, d.Vm.TrendRows[0].ClosingStockAmount);
		session.CheckEqual("ドリル後:アイテム合計=ブランド行", top.SalesAmount, d.Vm.BreakdownRows.Where(r => !r.IsTotal).Sum(r => r.SalesAmount));
		await Capture(session, view, screens, "03_Drill_Item");

		// 商品まで降りる（軸を直接商品へ）
		d.Input("軸:商品・期末在庫順", vm => {
			vm.SelectedSort = vm.SortItems.First(s => s.Value == CorporateAnalysisSortKey.ClosingStock);
			vm.SelectedAxis = vm.AxisItems.First(a => a.Value == CorporateAnalysisAxis.Shohin);
		});
		await WaitIdle(d.Vm);
		var items = d.Vm.BreakdownRows.Where(r => !r.IsTotal).ToList();
		session.Check("商品内訳:行あり", items.Count > 0, new { d.Vm.BreakdownCaption });
		session.Check("商品内訳:期末在庫順", items.Zip(items.Skip(1)).All(p => p.First.ClosingStockAmount >= p.Second.ClosingStockAmount));
		session.Note("商品内訳:判定の件数", new {
			Furyo = items.Count(r => r.Judgement.Contains("不良在庫")),
			Kajo = items.Count(r => r.Judgement.Contains("過剰発注")),
			Kata = items.Count(r => r.Judgement.Contains("在庫過多")),
			Keppin = items.Count(r => r.Judgement.Contains("欠品注意")),
		});
		await Capture(session, view, screens, "04_Shohin");
		await d.RunAsync("ドリル:商品(終端)", vm => vm.DrillDownCommand, items.First());
		session.Check("商品は終端で絞込増えない", d.Vm.Filters.Count == 1 && d.Vm.Message.Contains("これ以上"), new { d.Vm.Message });

		// 戻る
		await d.RunAsync("1つ戻る", vm => vm.BackCommand);
		session.CheckEqual("戻る:絞込なし", 0, d.Vm.Filters.Count);
		session.CheckEqual("戻る:軸=ブランド", CorporateAnalysisAxis.Brand, d.Vm.SelectedAxis.Value);
		session.CheckEqual("戻る:期間計売上が全社に戻る", total.SalesAmount, d.Vm.TrendRows[0].SalesAmount);

		// 最小サイズ
		view.Width = view.MinWidth; view.Height = view.MinHeight;
		await Capture(session, view, screens, "05_Minimum");
		view.Width = double.NaN; view.Height = double.NaN;

		// 日単位: 2022/03 の月初在庫・月末在庫が月単位と一致
		d.Input("単位:日 2022/03/01-03/31", vm => {
			vm.SelectedUnit = vm.UnitItems.First(u => u.Value == CorporateAnalysisUnit.Day);
			vm.FromText = "2022/03/01";
			vm.ToText = "2022/03/31";
		});
		sw.Restart();
		await d.RunAsync("集計:日", vm => vm.SearchCommand);
		session.Note("集計時間(日31日)", new { ms = sw.ElapsedMilliseconds });
		session.CheckEqual("日:期間計+31日", 32, d.Vm.TrendRows.Count);
		session.CheckEqual("日:月初在庫=月単位の期首", mar.OpeningStockAmount, d.Vm.TrendRows[0].OpeningStockAmount);
		session.CheckEqual("日:月末在庫=月単位の期末", mar.ClosingStockAmount, d.Vm.TrendRows[0].ClosingStockAmount);
		session.CheckEqual("日:売上=月単位", mar.SalesAmount, d.Vm.TrendRows[0].SalesAmount);
		var days = d.Vm.TrendRows.Skip(1).ToList();
		session.Check("日:期首(n)=期末(n-1)", days.Zip(days.Skip(1)).All(p => p.First.ClosingStockAmount == p.Second.OpeningStockAmount));
		await Capture(session, view, screens, "06_Day");

		// 年単位（期首月から12か月）: 2021年度の期末在庫は SummaryStock の積上げと一致
		d.Input("単位:年 2020-2022", vm => {
			vm.SelectedUnit = vm.UnitItems.First(u => u.Value == CorporateAnalysisUnit.Year);
			vm.FromText = "2020";
			vm.ToText = "2022";
		});
		sw.Restart();
		await d.RunAsync("集計:年", vm => vm.SearchCommand);
		session.Note("集計時間(年3年度)", new { ms = sw.ElapsedMilliseconds });
		session.CheckEqual("年:期間計+3年度", 4, d.Vm.TrendRows.Count);
		var years = d.Vm.TrendRows.Skip(1).ToList();
		session.Check("年:期首(n)=期末(n-1)", years.Zip(years.Skip(1)).All(p => p.First.ClosingStockAmount == p.Second.OpeningStockAmount));
		await Capture(session, view, screens, "07_Year");

		// 条件変更で旧結果を無効化
		d.Input("条件変更:開始", vm => vm.FromText = "2019");
		session.Check("条件変更で推移・内訳をクリア", !d.Vm.HasResult && d.Vm.TrendRows.Count == 0 && d.Vm.BreakdownRows.Count == 0);
	}

	static async Task WaitIdle(CorporateAnalysisViewModel vm) {
		await Task.Delay(200);
		for (var i = 0; i < 600 && vm.IsBusy; i++) {
			await Task.Delay(100);
		}
	}

	/// <summary>
	/// 画像保存と見切れ判定。内訳表は15列あり横スクロールが前提のため、横スクロール境界の列と長い名称のセルは記録にとどめ、
	/// 各表を右端までスクロールして最終列の見出しが表示されることを確認する（AutoReplenishScenario と同じ考え方）。
	/// </summary>
	static async Task Capture(VmSession session, Window view, string screens, string name) {
		await Task.Delay(800);
		await Settle(view);
		var path = ScreenLayoutCheck.SaveJpeg(view, screens, name);
		session.Note(name + ":画面画像", new { Path = path });
		var issues = ScreenLayoutCheck.Inspect(view);
		var scrollIssues = issues.Where(x => x.Kind == "列が横スクロール外").ToList();
		var longCells = issues.Where(x => x.Kind == ScreenLayoutCheck.LongCellKind).ToList();
		var other = issues.Except(scrollIssues).Except(longCells).ToList();
		session.Check(name + ":文字・ボタン見切れなし", other.Count == 0, new { Issues = other });
		if (scrollIssues.Count + longCells.Count > 0) {
			session.Note(name + ":横スクロール境界・長い名称", new { ScrollBoundary = scrollIssues.Select(x => x.Text), LongCells = longCells.Count });
		}
		var scrolled = false;
		foreach (var grid in Descendants(view).OfType<DataGrid>()) {
			var scroll = Descendants(grid).OfType<ScrollViewer>().FirstOrDefault(x => x.ScrollableWidth > 0);
			if (scroll == null) continue;
			scroll.ScrollToRightEnd();
			await Settle(view);
			var last = grid.Columns.OrderBy(x => x.DisplayIndex).Last();
			var header = Descendants(grid).OfType<DataGridColumnHeader>().FirstOrDefault(x => x.Column == last);
			var bounds = header?.TransformToAncestor(view).TransformBounds(new Rect(header.RenderSize)) ?? Rect.Empty;
			session.Check($"{name}:右端まで最終列「{last.Header}」を表示できる",
				header is { IsVisible: true } && bounds.Width > 0 && bounds.Right <= view.ActualWidth + 1, new { scroll.ScrollableWidth, scroll.HorizontalOffset, bounds });
			scrolled = true;
		}
		if (scrolled) {
			session.Note(name + "_Right:画面画像", new { Path = ScreenLayoutCheck.SaveJpeg(view, screens, name + "_Right") });
			foreach (var grid in Descendants(view).OfType<DataGrid>()) {
				Descendants(grid).OfType<ScrollViewer>().FirstOrDefault(x => x.ScrollableWidth > 0)?.ScrollToLeftEnd();
			}
			await Settle(view);
		}
	}

	static IEnumerable<DependencyObject> Descendants(DependencyObject root) {
		for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) {
			var child = VisualTreeHelper.GetChild(root, i);
			yield return child;
			foreach (var d in Descendants(child)) yield return d;
		}
	}

	static async Task Settle(Window view) {
		view.UpdateLayout();
		await view.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
	}
}
