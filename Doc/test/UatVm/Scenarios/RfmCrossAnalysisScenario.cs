using System.IO;
using System.Windows;
using System.Windows.Threading;
using CvBase;
using CvWpfclient.ViewModels._32LoyalCustomer;
using CvWpfclient.Views._32LoyalCustomer;

namespace UatVm.Scenarios;

/// <summary>
/// RFMクロス分析表（読み取りのみ）のUAT。2021/01/01～2021/12/31 を対象に、画面の合計を独立SQLと照合し、
/// 軸切替で合計が不変なこと、セル明細、条件変更での無効化、標準／最小サイズの見切れを確認する。
/// </summary>
/// <remarks>
/// 独立SQLは「CalcFlag=1 の数量合計が正の日を持つ顧客」を購入顧客、その顧客の期間内 (KingakuTotal+消費税)×CalcFlag を金額とする。
/// cv-sqlite（開発DB）の確認値は 208,771人・5,125,414,918円。
/// </remarks>
public static class RfmCrossAnalysisScenario {
	const string BaseDay = "20211231";
	const string DayFrom = "20210101";

	public static async Task RunAsync(VmSession session) {
		var screens = Path.GetFullPath(Path.Combine("..", "Doc", "test", "UatVm", "out", "rfm-screens-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")));
		Directory.CreateDirectory(screens);
		var d = session.OpenView<RfmCrossAnalysisTableView, RfmCrossAnalysisTableViewModel>();
		var view = d.View;
		await Settle(view);
		await Capture(session, view, screens, "01_Initial");
		var today = DateTime.Today;
		session.CheckEqual("既定:開始年月=当月-11", today.AddMonths(-11).ToString("yyyy/MM"), d.Vm.MonthFrom);
		session.CheckEqual("既定:終了年月=当月", today.ToString("yyyy/MM"), d.Vm.MonthTo);

		// 13ヶ月以上はエラー（サーバへ送らない）
		session.ClearDialogs();
		d.Input("条件:2020/12-2021/12(13ヶ月)", vm => { vm.MonthFrom = "2020/12"; vm.MonthTo = "2021/12"; });
		await d.RunAsync("検索:13ヶ月", vm => vm.SearchCommand);
		session.Check("13ヶ月はエラーで結果なし", !d.Vm.HasResult && session.Dialogs.Any(x => x.Request.Message.Contains("12ヶ月以内")),
			new { Dialogs = session.Dialogs.Select(x => x.Request.Message) });
		session.ClearDialogs();
		d.Input("条件:開始>終了", vm => { vm.MonthFrom = "2021/12"; vm.MonthTo = "2021/01"; });
		await d.RunAsync("検索:開始>終了", vm => vm.SearchCommand);
		session.Check("開始>終了はエラーで結果なし", !d.Vm.HasResult && session.Dialogs.Count > 0);

		d.Input("条件:2021/01-2021/12・全店", vm => {
			vm.MonthFrom = "2021/01";
			vm.MonthTo = "2021/12";
			vm.ShopCode = string.Empty;
			vm.IncludeWithdrawn = false;
		});
		await d.RunAsync("検索", vm => vm.SearchCommand);
		if (!session.Check("結果表示", d.Vm.HasResult, new { d.Vm.Message })) return;

		var expected = (await session.QueryAsync<RfmCell>("""
			SELECT COUNT(DISTINCT Id_Customer) AS Count,
			  (SELECT SUM((KingakuTotal+Tax1+Tax2+Tax3)*CalcFlag) FROM Tran01Tenuri WHERE DenDay BETWEEN @0 AND @1 AND CalcFlag<>0
			   AND Id_Customer IN (SELECT Id_Customer FROM Tran01Tenuri WHERE DenDay BETWEEN @0 AND @1 AND Id_Customer>0 AND CalcFlag=1
			    GROUP BY Id_Customer, DenDay HAVING SUM(SuTotal)>0)
			   AND Id_Customer IN (SELECT Id FROM MasterEndCustomer)
			   AND Id_Customer NOT IN (SELECT Id_Customer FROM MasterEndCustomerAccount WHERE IsWithdrawalFlag=1)) AS Amount
			FROM (SELECT Id_Customer FROM Tran01Tenuri WHERE DenDay BETWEEN @0 AND @1 AND Id_Customer>0 AND CalcFlag=1
			  AND Id_Customer IN (SELECT Id FROM MasterEndCustomer)
			  AND Id_Customer NOT IN (SELECT Id_Customer FROM MasterEndCustomerAccount WHERE IsWithdrawalFlag=1)
			  GROUP BY Id_Customer, DenDay HAVING SUM(SuTotal)>0) p
			""", DayFrom, BaseDay)).Single();
		var total = TotalCell(d.Vm);
		session.CheckEqual("購入顧客数=独立SQL", expected.Count, total.Count);
		session.CheckEqual("金額合計=独立SQL", expected.Amount, total.Amount);
		var rankCells = d.Vm.GridCells.Where(c => !c.IsHeader && c.RowRank > 0 && c.ColRank > 0).ToList();
		session.CheckEqual("全セル人数合計=購入顧客数", total.Count, rankCells.Sum(c => c.Count));
		session.Check("要約に購入なしを表示", d.Vm.SummaryText.Contains("購入なし"), new { d.Vm.SummaryText });
		await Capture(session, view, screens, "02_Result_RxF");

		// 軸切替は再検索せず、合計が変わらないこと
		foreach (var (v, h) in new[] { (RfmAxis.F, RfmAxis.M), (RfmAxis.M, RfmAxis.R), (RfmAxis.R, RfmAxis.F) }) {
			d.Input($"軸:{v}×{h}", vm => { vm.VerticalAxis = v; vm.HorizontalAxis = h; });
			var t = TotalCell(d.Vm);
			session.CheckEqual($"軸{v}×{h}:人数合計不変", expected.Count, t.Count);
			session.CheckEqual($"軸{v}×{h}:金額合計不変", expected.Amount, t.Amount);
		}

		// セル明細（R5×F5 があれば。なければ人数最大のセル）
		var target = d.Vm.GridCells.Where(c => c.IsSelectable && c.RowRank > 0 && c.ColRank > 0)
			.OrderByDescending(c => c.RowRank == 5 && c.ColRank == 5).ThenByDescending(c => c.Count).First();
		await d.RunAsync("セル明細", vm => vm.SelectCellCommand, target);
		var rows = d.Vm.CustomerRows;
		session.Check("明細件数", rows.Count == Math.Min(target.Count, RfmRank.DefaultLimit), new { Rows = rows.Count, target.Count, d.Vm.CustomerCaption });
		await Capture(session, view, screens, "03_Customers");

		// 最小サイズ
		view.Width = view.MinWidth; view.Height = view.MinHeight;
		await Capture(session, view, screens, "04_Minimum");
		view.Width = double.NaN; view.Height = double.NaN;

		// 条件変更で旧結果を無効化
		d.Input("条件変更:開始年月", vm => vm.MonthFrom = "2021/07");
		session.Check("条件変更で表・明細をクリア", !d.Vm.HasResult && d.Vm.GridCells.Count == 0 && d.Vm.CustomerRows.Count == 0);

		// 閾値不正は検索前に拒否（サーバへ送らない）
		d.Input("閾値不正:R降順", vm => { vm.Thresholds[0].Value1 = "300"; });
		session.ClearDialogs();
		await d.RunAsync("検索:閾値不正", vm => vm.SearchCommand);
		session.Check("閾値不正で結果なし", !d.Vm.HasResult);
	}

	static RfmGridCell TotalCell(RfmCrossAnalysisTableViewModel vm) =>
		vm.GridCells.Single(c => !c.IsHeader && c.RowRank == 0 && c.ColRank == 0);

	static async Task Capture(VmSession session, Window view, string screens, string name) {
		await Task.Delay(800);
		await Settle(view);
		var path = ScreenLayoutCheck.SaveJpeg(view, screens, name);
		session.Note(name + ":画面画像", new { Path = path });
		var issues = ScreenLayoutCheck.Inspect(view);
		session.Check(name + ":文字・ボタン見切れなし", issues.Count == 0, new { issues.Count, Issues = issues });
	}

	static async Task Settle(Window view) {
		view.UpdateLayout();
		await view.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
	}
}
