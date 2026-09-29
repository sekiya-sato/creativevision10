using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels._05Shiire;
using CvWpfclient.Views._05Shiire;

namespace UatVm.Scenarios;

/// <summary>大メニュー「仕入」7画面を実表示し、読み取り専用の一覧・検索とPNG保存を確認する。</summary>
public static class ShiireScreenScenario {
	const string ScreenDirectory = "..\\Doc\\test\\uat20260926\\shiire\\screens";
	const string DenDayFrom = "2022/05/01";
	const string DenDayTo = "2022/05/31";
	const string StartYearMonth = "2022/05";
	const string MonthCount = "3";

	public static async Task RunAsync(VmSession session) {
		var screens = Path.Combine(Path.GetFullPath(ScreenDirectory), DateTime.Now.ToString("yyyyMMdd_HHmmssfff"));
		Directory.CreateDirectory(screens);
		session.Note("実行方法", new { Command = "UatVm shiire20260926 --manage-server", Screens = screens, Database = "UatVm既定接続先" });
		session.Note("対象範囲", new {
			Screens = new[] { "ShiireInput", "HenpinInput", "MaterialInput", "HinbanShiireCheckList",
				"BrandShiireKingakuTable", "ShiireSlipPrint", "ShiireTrendReport" },
			ReadOnly = true,
			Reports = "画面条件の表示まで。PDF生成・印刷データ分岐は別途ReportRunnerで確認",
		});

		// ---- 商品仕入入力 --------------------------------------------------------
		var input = session.OpenView<ShiireInputView, ShiireInputViewModel>();
		await AwaitInitializationAsync(input.Vm, input.View);
		session.Check("商品仕入入力:一覧初期表示", input.Vm.ListData != null, new { input.Vm.Count });
		RecordDataPresence(session, "商品仕入入力:仕入一覧", input.Vm.Count);
		await CaptureAsync(session, input, screens, "ShiireInput", close: false);

		// 2022/05・区分10(仕入)・色サイズ明細27行の既存伝票(読み取り専用SQLで選定済み)
		var order1 = (await session.QueryAsync<Tran03Shiire>("where Id=@0", "8587")).SingleOrDefault();
		if (order1 != null) {
			input.Input("商品仕入入力:明細1件表示", vm => {
				vm.CurrentEdit = order1;
				vm.SelectedTabIndex = 1;
			}, new { order1.Id, order1.DenDay, order1.Kubun });
			session.Check("商品仕入入力:明細に色サイズ複数行", order1.Jmeisai?.Count == 27, new { count = order1.Jmeisai?.Count });
			await CaptureAsync(session, input, screens, "ShiireInputDetail");
		}
		else {
			session.Note("商品仕入入力:明細1件表示は対象データなしのため未実施", new { Id = 8587L });
			input.View.Close();
		}

		// ---- 仕入返品入力 ----------------------------------------------------------
		// BeforeListAsync系の範囲指定ダイアログと違い、本画面のInit()は同期メソッドで
		// LoadMastersCommand(非同期)を発火するだけのため、AwaitInitializationAsyncの
		// IAsyncRelayCommand待ち合わせに乗らない。明示的にRunAsyncで完了を待つ。
		var henpin = session.OpenView<HenpinInputView, HenpinInputViewModel>();
		await AwaitInitializationAsync(henpin.Vm, henpin.View);
		await henpin.RunAsync("仕入返品入力:マスタ読込", vm => vm.LoadMastersCommand);
		await CaptureAsync(session, henpin, screens, "HenpinInput", close: false);

		// 仕入先・倉庫の選択はSelectShiireDialog/SelectSokoDialogが実モーダル(ShowDialogView)を
		// 開くため操作せず、既存仕入(Id_Shiire=300/Id_Soko=286、2022/05 Id=8587と同じ仕入先)の
		// コード・名称をVMプロパティへ直接設定して代替する。
		henpin.Input("仕入返品入力:仕入先・倉庫選択(ダイアログ未操作・VM直接設定で代替)", vm => {
			vm.SelectedShiire = new HenpinInputViewModel.MasterOption(300, "734", "ＡＴＲＩＡＤＥＳＩＧＮ株式会社");
			vm.SelectedSoko = new HenpinInputViewModel.MasterOption(286, "000990", "キズ物在庫（返品可）");
		}, new { ShiireId = 300, SokoId = 286 });
		await henpin.RunAsync("仕入返品入力:在庫検索", vm => vm.SearchCommand);
		session.Check("仕入返品入力:検索結果あり", henpin.Vm.RowCount > 0, new { henpin.Vm.RowCount, henpin.Vm.Message });
		RecordDataPresence(session, "仕入返品入力:検索結果", henpin.Vm.RowCount);
		await CaptureAsync(session, henpin, screens, "HenpinInputSearched");

		// ---- 生地付属入力 ------------------------------------------------------------
		var material = session.OpenView<MaterialInputView, MaterialInputViewModel>();
		await AwaitInitializationAsync(material.Vm, material.View);
		session.Check("生地付属入力:一覧初期表示", material.Vm.ListData != null, new { material.Vm.Count });
		RecordDataPresence(session, "生地付属入力:生地付属仕入一覧", material.Vm.Count);
		await CaptureAsync(session, material, screens, "MaterialInput", close: false);

		// 2026/07・区分10(仕入)の既存伝票(明細0行、読み取り専用SQLで選定済み)
		var material1 = (await session.QueryAsync<Tran02Material>("where Id=@0", "1730")).SingleOrDefault();
		if (material1 != null) {
			material.Input("生地付属入力:明細1件表示", vm => {
				vm.CurrentEdit = material1;
				vm.SelectedTabIndex = 1;
			}, new { material1.Id, material1.DenDay, material1.Kubun });
			RecordDataPresence(session, "生地付属入力:明細", material.Vm.EditMeisai.Count);
			await CaptureAsync(session, material, screens, "MaterialInputDetail");
		}
		else {
			session.Note("生地付属入力:明細1件表示は対象データなしのため未実施", new { Id = 1730L });
			material.View.Close();
		}

		// ---- 品番別仕入チェックリスト -------------------------------------------------
		var hinban = session.OpenView<HinbanShiireCheckListView, HinbanShiireCheckListViewModel>();
		await AwaitInitializationAsync(hinban.Vm, hinban.View);
		hinban.Input("品番別仕入チェックリスト:対象月条件", vm => { vm.DenDayFrom = DenDayFrom; vm.DenDayTo = DenDayTo; });
		session.Check("品番別仕入チェックリスト:条件表示", hinban.Vm.DenDayFrom == DenDayFrom && hinban.Vm.DenDayTo == DenDayTo,
			new { hinban.Vm.DenDayFrom, hinban.Vm.DenDayTo });
		await CaptureAsync(session, hinban, screens, "HinbanShiireCheckList");

		// ---- ブランド別仕入金額表 ------------------------------------------------------
		var brand = session.OpenView<BrandShiireKingakuTableView, BrandShiireKingakuTableViewModel>();
		await AwaitInitializationAsync(brand.Vm, brand.View);
		brand.Input("ブランド別仕入金額表:対象月条件", vm => { vm.StartYearMonth = StartYearMonth; vm.MonthCountText = MonthCount; });
		session.Check("ブランド別仕入金額表:条件表示", brand.Vm.StartYearMonth == StartYearMonth && brand.Vm.MonthCountText == MonthCount,
			new { brand.Vm.StartYearMonth, brand.Vm.MonthCountText });
		await CaptureAsync(session, brand, screens, "BrandShiireKingakuTable");

		// ---- 仕入伝票印刷 --------------------------------------------------------------
		var slip = session.OpenView<ShiireSlipPrintView, ShiireSlipPrintViewModel>();
		await AwaitInitializationAsync(slip.Vm, slip.View);
		slip.Input("仕入伝票印刷:対象月条件", vm => { vm.DenDayFrom = new DateTime(2022, 5, 1); vm.DenDayTo = new DateTime(2022, 5, 31); });
		session.Check("仕入伝票印刷:条件表示", slip.Vm.DenDayFrom == new DateTime(2022, 5, 1) && slip.Vm.DenDayTo == new DateTime(2022, 5, 31),
			new { slip.Vm.DenDayFrom, slip.Vm.DenDayTo });
		await CaptureAsync(session, slip, screens, "ShiireSlipPrint");

		// ---- 仕入先別仕入推移表 --------------------------------------------------------
		var trend = session.OpenView<ShiireTrendReportView, ShiireTrendReportViewModel>();
		await AwaitInitializationAsync(trend.Vm, trend.View);
		trend.Input("仕入先別仕入推移表:対象月条件", vm => { vm.StartYearMonth = StartYearMonth; vm.MonthCountText = MonthCount; });
		session.Check("仕入先別仕入推移表:条件表示", trend.Vm.StartYearMonth == StartYearMonth && trend.Vm.MonthCountText == MonthCount,
			new { trend.Vm.StartYearMonth, trend.Vm.MonthCountText });
		await CaptureAsync(session, trend, screens, "ShiireTrendReport");
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
