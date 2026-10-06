using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CvWpfclient.ViewModels._32LoyalCustomer;
using CvWpfclient.Views._32LoyalCustomer;

namespace UatVm.Scenarios;

/// <summary>
/// ポイント再計算画面の表示確認（描画のみ）。サーバ処理は実行せず、初期状態と処理中相当の状態をJPG保存し、
/// 文字・ボタンの見切れを共通判定(ScreenLayoutCheck)で確認する。画面は ResizeMode=NoResize のため標準=最小サイズ。
/// </summary>
public static class PointSummaryLayoutScenario {
	public static async Task RunAsync(VmSession session) {
		var screens = Path.GetFullPath(Path.Combine("..", "Doc", "test", "UatVm", "out", "point-summary-screens-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")));
		Directory.CreateDirectory(screens);
		session.Note("対象", new { Screens = screens });
		var driver = session.OpenView<PointSummaryView, PointSummaryViewModel>();
		var view = driver.View;
		await Settle(view);
		session.Check("標準サイズ", view.ActualWidth > 0 && view.ResizeMode == ResizeMode.NoResize, new { view.ActualWidth, view.ActualHeight, view.MinWidth, view.MinHeight });
		await Capture(session, view, screens, "01_Initial");

		// 処理中相当: 入力無効・キャンセル有効・進捗途中・長いメッセージ
		driver.Input("処理中相当の状態", vm => {
			vm.YearMonthFrom = "2026/04";
			vm.YearMonthTo = "2026/09";
			vm.IsProcessing = true;
			vm.ProgressValue = 55;
			vm.StatusMessage = string.Join(Environment.NewLine, Enumerable.Range(1, 30)
				.Select(i => $"{i:00}: 202604 ～ 202609 のポイント台帳・残高を再計算しています（店舗売上から伝票日付の暦月で集計）"));
		});
		await Settle(view);
		await Capture(session, view, screens, "02_Processing");
		var bar = Descendants(view).OfType<ProgressBar>().Single(x => x.Height == 18);
		var card = Descendants(view).OfType<FrameworkElement>().First(x => x.GetType().Name == "Card");
		session.Note("進捗バー幅", new { Bar = bar.ActualWidth, Card = card.ActualWidth, Parent = ((FrameworkElement)bar.Parent).ActualWidth });

		// 最小サイズ（BaseWindow の MinWidth/MinHeight）。NoResize のため利用者は縮められないが、余裕を確認する。
		view.Width = view.MinWidth; view.Height = view.MinHeight;
		await Settle(view);
		await Capture(session, view, screens, "03_Minimum");
		driver.Input("処理終了状態", vm => vm.IsProcessing = false);
		await Settle(view);
	}

	static async Task Capture(VmSession session, Window view, string screens, string name) {
		await Task.Delay(800);
		await Settle(view);
		var path = ScreenLayoutCheck.SaveJpeg(view, screens, name);
		session.Note(name + ":画面画像", new { Path = path });
		var issues = ScreenLayoutCheck.Inspect(view);
		session.Check(name + ":文字・ボタン見切れなし", issues.Count == 0, new { issues.Count, Issues = issues });
	}

	static IEnumerable<DependencyObject> Descendants(DependencyObject parent) {
		for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
			var child = VisualTreeHelper.GetChild(parent, i);
			yield return child;
			foreach (var nested in Descendants(child)) yield return nested;
		}
	}

	static async Task Settle(Window view) {
		view.UpdateLayout();
		await view.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
	}
}
