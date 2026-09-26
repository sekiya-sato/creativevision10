using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels._03Hatchu;
using CvWpfclient.Views._03Hatchu;

namespace UatVm.Scenarios;

/// <summary>発注メニュー11画面を実表示し、読み取り専用の一覧・検索とPNG保存を確認する。</summary>
public static class HachuScreenScenario {
	const string ScreenDirectory = "..\\Doc\\test\\uat20260926\\hachu\\screens";

	public static async Task RunAsync(VmSession session) {
		var screens = Path.Combine(Path.GetFullPath(ScreenDirectory), DateTime.Now.ToString("yyyyMMdd_HHmmssfff"));
		Directory.CreateDirectory(screens);
		session.Note("実行方法", new { Command = "UatVm hachu20260926 --manage-server", Screens = screens, Database = "UatVm既定接続先" });
		session.Note("対象範囲", new {
			Screens = new[] { "HachuInput", "HachuHaibunInput", "DeliveryScheduleInquiry", "DeliveryScheduleTable", "PendingShiireList", "HachuZanKanriTable", "HachuZanCompletionSetting", "HachuForm", "SupplierHachuTable", "ShohinHachuTable", "ShohinHachuSummaryTable" },
			ReadOnly = true,
			Reports = "画面条件の表示まで。PDF生成・印刷データ分岐は別途確認",
		});

		var input = session.OpenView<HachuInputView, HachuInputViewModel>();
		await AwaitInitializationAsync(input.Vm, input.View);
		session.Check("発注入力:一覧初期表示", input.Vm.ListData != null, new { input.Vm.Count });
		RecordDataPresence(session, "発注入力:発注一覧", input.Vm.Count);
		await CaptureAsync(session, input, screens, "HachuInput");

		var allocation = session.OpenView<HachuHaibunInputView, HachuHaibunInputViewModel>();
		await AwaitInitializationAsync(allocation.Vm, allocation.View);
		allocation.Input("発注配分入力:2025/01条件", vm => {
			vm.HachuDayFrom = new DateTime(2025, 1, 1);
			vm.HachuDayTo = new DateTime(2025, 1, 31);
		});
		await allocation.RunAsync("発注配分入力:既存発注検索", vm => vm.DoSearchCommand);
		session.Check("発注配分入力:2025/01発注2件", allocation.Vm.SearchCount == 2,
			new { allocation.Vm.SearchCount, allocation.Vm.Message });
		RecordDataPresence(session, "発注配分入力:検索一覧", allocation.Vm.SearchCount);
		await CaptureAsync(session, allocation, screens, "HachuHaibunInput", close: false);
		if (allocation.Vm.SearchCount > 0) {
			await allocation.RunAsync("発注配分入力:既存発注明細読込", vm => vm.GoToEditCommand);
			session.Check("発注配分入力:明細読込", allocation.Vm.SelectedTabIndex == 1 && allocation.Vm.HachuNo > 0,
				new { allocation.Vm.HachuNo, allocation.Vm.HachuTotalSu, allocation.Vm.Message });
			await CaptureAsync(session, allocation, screens, "HachuHaibunInputDetail");
		} else allocation.View.Close();

		var deliveryInquiry = session.OpenView<DeliveryScheduleInquiryView, DeliveryScheduleInquiryViewModel>();
		await AwaitInitializationAsync(deliveryInquiry.Vm, deliveryInquiry.View);
		await deliveryInquiry.RunAsync("納品予定照会:既定条件検索", vm => vm.SearchCommand);
		session.Check("納品予定照会:対象0件を正常取得", !deliveryInquiry.Vm.IsBusy && deliveryInquiry.Vm.Rows.Count == 0
			&& deliveryInquiry.Vm.OverdueCount == 0 && deliveryInquiry.Vm.Message == "該当する発注がありません。",
			new { deliveryInquiry.Vm.Rows.Count, deliveryInquiry.Vm.OverdueCount, deliveryInquiry.Vm.Message });
		RecordDataPresence(session, "納品予定照会:該当発注", deliveryInquiry.Vm.Rows.Count);
		await CaptureAsync(session, deliveryInquiry, screens, "DeliveryScheduleInquiry");

		var deliveryTable = session.OpenView<DeliveryScheduleTableView, DeliveryScheduleTableViewModel>();
		await AwaitInitializationAsync(deliveryTable.Vm, deliveryTable.View);
		await CaptureAsync(session, deliveryTable, screens, "DeliveryScheduleTable");

		var pending = session.OpenView<PendingShiireListView, PendingShiireListViewModel>();
		await AwaitInitializationAsync(pending.Vm, pending.View);
		await CaptureAsync(session, pending, screens, "PendingShiireList");

		var remaining = session.OpenView<HachuZanKanriTableView, HachuZanKanriTableViewModel>();
		await AwaitInitializationAsync(remaining.Vm, remaining.View);
		await CaptureAsync(session, remaining, screens, "HachuZanKanriTable");

		var completion = session.OpenView<HachuZanCompletionSettingView, HachuZanCompletionSettingViewModel>();
		await AwaitInitializationAsync(completion.Vm, completion.View);
		await completion.RunAsync("発注残完了設定:既定条件一覧取得", vm => vm.SearchCommand);
		session.Check("発注残完了設定:一覧1件を正常取得", !completion.Vm.IsBusy && completion.Vm.DenRows.Count == 1
			&& completion.Vm.Message.Contains("1 件を取得しました", StringComparison.Ordinal),
			new { Count = completion.Vm.DenRows.Count, completion.Vm.TargetCount, completion.Vm.Message });
		RecordDataPresence(session, "発注残完了設定:一覧", completion.Vm.DenRows.Count);
		await CaptureAsync(session, completion, screens, "HachuZanCompletionSetting");

		var form = session.OpenView<HachuFormView, HachuFormViewModel>();
		await AwaitInitializationAsync(form.Vm, form.View);
		await CaptureAsync(session, form, screens, "HachuForm");

		var supplier = session.OpenView<SupplierHachuTableView, SupplierHachuTableViewModel>();
		await AwaitInitializationAsync(supplier.Vm, supplier.View);
		await CaptureAsync(session, supplier, screens, "SupplierHachuTable");

		var product = session.OpenView<ShohinHachuTableView, ShohinHachuTableViewModel>();
		await AwaitInitializationAsync(product.Vm, product.View);
		await CaptureAsync(session, product, screens, "ShohinHachuTable");

		var productSummary = session.OpenView<ShohinHachuSummaryTableView, ShohinHachuSummaryTableViewModel>();
		await AwaitInitializationAsync(productSummary.Vm, productSummary.View);
		await CaptureAsync(session, productSummary, screens, "ShohinHachuSummaryTable");
	}

	static async Task AwaitInitializationAsync<TViewModel>(TViewModel vm, Window view) where TViewModel : class {
		await SettleWindowAsync(view);
		if (vm.GetType().GetProperty("InitCommand")?.GetValue(vm) is IAsyncRelayCommand asyncCommand
			&& asyncCommand.ExecutionTask is { } task) {
			await task;
		}
		await Application.Current.Dispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
	}

	static void RecordDataPresence(VmSession session, string name, int count) {
		if (count > 0) session.Check($"{name}:データあり", true, new { Count = count });
		else session.Note($"{name}:該当データなし", new { Count = count, Meaning = "空一覧として画面表示を確認" });
	}

	static async Task CaptureAsync<TViewModel>(VmSession session, ViewDriver<TViewModel> driver, string directory, string name, bool close = true)
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
		if (close) driver.View.Close();
		await Application.Current.Dispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
	}

	static async Task SettleWindowAsync(Window view) {
		await Application.Current.Dispatcher.InvokeAsync(view.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
		await Task.Delay(50);
		await Application.Current.Dispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
		view.UpdateLayout();
	}
}
