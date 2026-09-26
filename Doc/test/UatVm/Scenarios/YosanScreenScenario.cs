using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodeShare;
using CvBase;
using CvWpfclient.ViewModels._02Yosan;
using CvWpfclient.Views._02Yosan;

namespace UatVm.Scenarios;

/// <summary>予算メニュー8画面の初期化、画面バインディング、主要条件を実Viewで確認する。</summary>
public static class YosanScreenScenario {
	const string YearMonth = "2026/08";
	const string ScreenDirectory = "..\\Doc\\test\\uat20260926\\yosan\\screens";

	public static async Task RunAsync(VmSession session) {
		var screens = Path.GetFullPath(ScreenDirectory);
		Directory.CreateDirectory(screens);
		session.Note("実行方法", new { Command = "UatVm yosan20260926 --manage-server", Screens = screens });

		var shopBudget = session.OpenView<ShopBrandBudgetMasterView, ShopBrandBudgetMasterViewModel>();
		await SettleWindowAsync(shopBudget.View);
		shopBudget.Input("店ブランド月次予算条件", vm => {
			vm.SelectedYearMonthString = YearMonth;
			vm.SelectedYearMonth = new DateTime(2026, 8, 1);
			vm.SelectedShopCode = "000029";
			vm.SelectedShopName = "イオンモール成田";
			vm.SelectedShopId = 7;
			vm.SelectedBrandCode = "201";
			vm.SelectedBrandName = "axes femme";
			vm.SelectedBrandId = 7899;
		}, new { YearMonth, ShopId = 7, ShopCode = "000029", BrandId = 7899, BrandCode = "201" });
		await shopBudget.RunAsync("店ブランド月次予算読込", vm => vm.LoadBudgetCommand);
		session.Check("店ブランド月次予算:31日・売上225千円・粗利45千円",
			shopBudget.Vm.DailyBudgets.Count == 31 && shopBudget.Vm.MonthlyBudget == 225 && shopBudget.Vm.MonthlyGrossProfitBudget == 45,
			new { Days = shopBudget.Vm.DailyBudgets.Count, shopBudget.Vm.MonthlyBudget, shopBudget.Vm.MonthlyGrossProfitBudget, shopBudget.Vm.Message });
		await CaptureAsync(session, shopBudget, screens, "ShopBrandBudgetMaster");

		var brandList = session.OpenView<MasterYosanBrandMenteView, MasterYosanBrandMenteViewModel>();
		await SettleWindowAsync(brandList.View);
		if (brandList.Vm.InitCommand.ExecutionTask is { } brandInit) await brandInit;
		// 通常の一覧取得は選択ダイアログを要する。画面描画用に実DB行をVMへ設定する。
		var brandRows = await session.QueryAsync<MasterYosanBrand>("where DenDay between @0 and @1 order by DenDay,Id_Tenpo,Id_Brand", "20260801", "20260831");
		brandList.Input("店ブランド予算マスタ:実DB行を一覧表示", vm => {
			vm.ListData = new ObservableCollection<MasterYosanBrand>(brandRows);
			vm.Count = brandRows.Count;
		}, new { Count = brandRows.Count });
		session.CheckEqual("店ブランド予算マスタ:2026/08一覧10行", 10, brandList.Vm.ListData.Count);
		await CaptureAsync(session, brandList, screens, "MasterYosanBrandMente");

		var shopReport = session.OpenView<ShopBudgetReportView, ShopBudgetReportViewModel>();
		await SettleWindowAsync(shopReport.View);
		shopReport.Input("店舗予算表条件", vm => {
			vm.SelectedYearMonthString = YearMonth;
			vm.SelectedYearMonth = new DateTime(2026, 8, 1);
		});
		session.Check("店舗予算表:条件表示", shopReport.Vm.SelectedYearMonthString == YearMonth);
		await CaptureAsync(session, shopReport, screens, "ShopBudgetReport");

		var dailyQuery = session.OpenView<DailyShopBudgetQueryView, DailyShopBudgetQueryViewModel>();
		await SettleWindowAsync(dailyQuery.View);
		dailyQuery.Input("店別売上表条件", vm => {
			vm.SelectedYearMonthString = YearMonth;
			vm.SelectedYearMonth = new DateTime(2026, 8, 1);
			vm.ShopCodeFrom = "000023";
			vm.ShopCodeTo = "000029";
			vm.IsByShop = true;
		});
		await dailyQuery.RunAsync("店別売上表検索", vm => vm.SearchCommand);
		if (session.Check("店別売上表:結果表示", dailyQuery.Vm.IsResultVisible && dailyQuery.Vm.ResultTable != null,
			new { dailyQuery.Vm.IsResultVisible, dailyQuery.Vm.Message })) {
			DataTable result = dailyQuery.Vm.ResultTable!;
			session.CheckEqual("店別売上表:31日", 31, result.Rows.Count);
			long sales = result.Rows.Cast<DataRow>().Sum(row => Convert.ToInt64(row["売上計"], CultureInfo.InvariantCulture));
			long budget = result.Rows.Cast<DataRow>().Sum(row => Convert.ToInt64(row["予算計"], CultureInfo.InvariantCulture));
			session.Check("店別売上表:店舗000029の月間売上21,850円・全ブランド予算295,000円",
				sales == 21850 && budget == 295000, new { sales, budget, Columns = result.Columns.Count });
		}
		await CaptureAsync(session, dailyQuery, screens, "DailyShopBudgetQuery");

		var shopVsActual = session.OpenView<ShopBrandBudgetVsActualView, ShopBrandBudgetVsActualViewModel>();
		await SettleWindowAsync(shopVsActual.View);
		shopVsActual.Input("店舗ブランド別予算実績対比条件", vm => {
			vm.SelectedYearMonthString = YearMonth;
			vm.SelectedYearMonth = new DateTime(2026, 8, 1);
			vm.ShopCodeFrom = "000023";
			vm.ShopCodeTo = "000029";
			vm.BrandCodeFrom = "201";
			vm.BrandCodeTo = "201";
		});
		session.Check("店舗ブランド別予算実績対比:条件表示", shopVsActual.Vm.SelectedYearMonthString == YearMonth);
		await CaptureAsync(session, shopVsActual, screens, "ShopBrandBudgetVsActual");

		var staffBudget = session.OpenView<SalesStaffBudgetMasterView, SalesStaffBudgetMasterViewModel>();
		await SettleWindowAsync(staffBudget.View);
		staffBudget.Input("販売員月次予算条件", vm => {
			vm.SelectedYearMonthString = YearMonth;
			vm.SelectedYearMonth = new DateTime(2026, 8, 1);
			vm.SelectedStaffCode = "000003";
			vm.SelectedStaffName = "システム担当";
			vm.SelectedStaffId = 1;
		}, new { YearMonth, StaffId = 1, StaffCode = "000003" });
		await staffBudget.RunAsync("販売員月次予算読込", vm => vm.LoadBudgetCommand);
		session.Check("販売員月次予算:31日・売上180千円・粗利36千円",
			staffBudget.Vm.DailyBudgets.Count == 31 && staffBudget.Vm.MonthlyBudget == 180 && staffBudget.Vm.MonthlyGrossProfitBudget == 36,
			new { Days = staffBudget.Vm.DailyBudgets.Count, staffBudget.Vm.MonthlyBudget, staffBudget.Vm.MonthlyGrossProfitBudget, staffBudget.Vm.Message });
		await CaptureAsync(session, staffBudget, screens, "SalesStaffBudgetMaster");

		var staffList = session.OpenView<MasterYosanHanbaiMenteView, MasterYosanHanbaiMenteViewModel>();
		await SettleWindowAsync(staffList.View);
		if (staffList.Vm.InitCommand.ExecutionTask is { } staffInit) await staffInit;
		var staffRows = await session.QueryAsync<MasterYosanHanbai>("where DenDay between @0 and @1 order by DenDay,Id_Shain", "20260801", "20260831");
		staffList.Input("販売員予算マスタ:実DB行を一覧表示", vm => {
			vm.ListData = new ObservableCollection<MasterYosanHanbai>(staffRows);
			vm.Count = staffRows.Count;
		}, new { Count = staffRows.Count });
		session.CheckEqual("販売員予算マスタ:2026/08一覧9行", 9, staffList.Vm.ListData.Count);
		await CaptureAsync(session, staffList, screens, "MasterYosanHanbaiMente");

		var staffReport = session.OpenView<SalesStaffBudgetReportView, SalesStaffBudgetReportViewModel>();
		await SettleWindowAsync(staffReport.View);
		staffReport.Input("販売員予算表条件", vm => {
			vm.SelectedYearMonthString = YearMonth;
			vm.SelectedYearMonth = new DateTime(2026, 8, 1);
			vm.ShainCodeFrom = "000003";
			vm.ShainCodeTo = "000003";
		});
		session.Check("販売員予算表:条件表示", staffReport.Vm.SelectedYearMonthString == YearMonth);
		await CaptureAsync(session, staffReport, screens, "SalesStaffBudgetReport");
	}

	static async Task CaptureAsync<TViewModel>(VmSession session, ViewDriver<TViewModel> driver, string directory, string name)
		where TViewModel : class {
		await SettleWindowAsync(driver.View);
		await driver.WaitAsync($"{name}:表示準備", _ => driver.View.IsLoaded && driver.View.ActualWidth > 0 && driver.View.ActualHeight > 0, 10_000);
		await Application.Current.Dispatcher.InvokeAsync(() => {
			driver.View.UpdateLayout();
			var dpi = VisualTreeHelper.GetDpi(driver.View);
			int width = Math.Max(1, (int)Math.Ceiling(driver.View.ActualWidth * dpi.DpiScaleX));
			int height = Math.Max(1, (int)Math.Ceiling(driver.View.ActualHeight * dpi.DpiScaleY));
			var bitmap = new RenderTargetBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
			bitmap.Render(driver.View);
			var path = Path.Combine(directory, $"{name}.png");
			using (var stream = File.Create(path)) {
				var encoder = new PngBitmapEncoder();
				encoder.Frames.Add(BitmapFrame.Create(bitmap));
				encoder.Save(stream);
			}
			var pixels = new byte[width * height * 4];
			bitmap.CopyPixels(pixels, width * 4, 0);
			var distinct = new HashSet<uint>();
			for (int i = 0; i < pixels.Length && distinct.Count < 16; i += 4) {
				if (pixels[i + 3] != 0) distinct.Add((uint)(pixels[i] | pixels[i + 1] << 8 | pixels[i + 2] << 16 | pixels[i + 3] << 24));
			}
			session.Check($"{name}:PNG内容あり", new FileInfo(path).Length > 1024 && distinct.Count >= 2,
				new { Path = path, Width = width, Height = height, Bytes = new FileInfo(path).Length, DistinctColors = distinct.Count });
			session.Note($"{name}:画面画像", new { Path = path, Width = width, Height = height });
		}, System.Windows.Threading.DispatcherPriority.Render);
		driver.View.Close();
		await Application.Current.Dispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
	}

	static async Task SettleWindowAsync(Window view) {
		await Application.Current.Dispatcher.InvokeAsync(view.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
		await Task.Delay(50);
		await Application.Current.Dispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
		view.UpdateLayout();
	}
}
