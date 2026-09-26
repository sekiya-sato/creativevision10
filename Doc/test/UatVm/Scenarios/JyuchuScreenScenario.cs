using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels._04Juchu;
using CvWpfclient.Views._04Juchu;
using UatVm.Seed;

namespace UatVm.Scenarios;

/// <summary>大メニュー「受注・展示会」10画面を実表示し、読み取り専用の検索・条件設定とPNG保存を確認する。</summary>
public static class JyuchuScreenScenario {
	const string ScreenDirectory = "..\\Doc\\test\\uat20260926\\jyuchu\\screens";
	const string DenDayFrom = "2026/07/01";
	const string DenDayTo = "2026/07/31";
	static JyuchuUatSeeder.Result? _seeded;

	public static void Seeder(string dbPath) =>
		_seeded = JyuchuUatSeeder.Seed(dbPath, message => Console.WriteLine($"[seed] {message}"));

	public static async Task RunAsync(VmSession session) {
		var seeded = _seeded ?? throw new InvalidOperationException("シードが実行されていません。");
		var screens = Path.Combine(Path.GetFullPath(ScreenDirectory), DateTime.Now.ToString("yyyyMMdd_HHmmssfff"));
		Directory.CreateDirectory(screens);
		session.Note("実行方法", new { Command = "UatVm jyuchu20260926 --manage-server", Screens = screens, Database = "UatVm既定接続先" });
		session.Note("対象範囲", new {
			Screens = new[] { "JuchuInput", "JuchuInputDetail", "NouhinYoteiTable", "TokuiSakiJuchuTable", "ShouhinJuchuTable",
				"ShouhinJuchuSummaryTable", "JuchuZanKanriTable", "JuchuZanCompletionSetting", "TokuiSakiUriageYoteiTable",
				"TantoTenjiJuchuGoukeiTable", "JuchuBestTable" },
			ReadOnly = true,
			Reports = "画面条件の表示まで。PDF生成・印刷データ分岐は別途ReportRunnerで確認",
			SeededData = new { seeded.OrderCount, seeded.TotalSu, seeded.TotalKingaku, seeded.Tokui1Code, seeded.Tokui2Code },
		});

		var input = session.OpenView<JuchuInputView, JuchuInputViewModel>();
		await AwaitInitializationAsync(input.Vm, input.View);
		session.Check("受注入力:一覧初期表示", input.Vm.ListData != null, new { input.Vm.Count });
		await CaptureAsync(session, input, screens, "JuchuInput", close: false);

		var order1 = (await session.QueryAsync<Tran12Jyuchu>("where Id_Tokui=@0 AND DenDay=@1", seeded.Tokui1Id.ToString(), "20260705")).SingleOrDefault();
		if (order1 != null) {
			input.Input("受注入力:明細1件表示", vm => {
				vm.CurrentEdit = order1;
				vm.SelectedTabIndex = 1;
			}, new { order1.Id, order1.DenDay });
			session.Check("受注入力:明細に色サイズ複数行", order1.Jmeisai?.Count == 2, new { count = order1.Jmeisai?.Count });
			await CaptureAsync(session, input, screens, "JuchuInputDetail");
		} else {
			session.Note("受注入力:明細1件表示は対象データなしのため未実施", new { seeded.Tokui1Id });
			input.View.Close();
		}

		var delivery = session.OpenView<NouhinYoteiTableView, NouhinYoteiTableViewModel>();
		await AwaitInitializationAsync(delivery.Vm, delivery.View);
		delivery.Input("納品予定照会:既定条件", vm => { vm.IncompleteOnly = true; vm.OverdueOnly = false; });
		await delivery.RunAsync("納品予定照会:検索", vm => vm.SearchCommand);
		session.Check("納品予定照会:データあり", delivery.Vm.Rows.Count > 0, new { delivery.Vm.Rows.Count, delivery.Vm.OverdueCount, delivery.Vm.Message });
		await CaptureAsync(session, delivery, screens, "NouhinYoteiTable");

		var tokuiJuchu = session.OpenView<TokuiSakiJuchuTableView, TokuiSakiJuchuTableViewModel>();
		await AwaitInitializationAsync(tokuiJuchu.Vm, tokuiJuchu.View);
		tokuiJuchu.Input("得意先別受注表:対象月条件", vm => { vm.DenDayFrom = DenDayFrom; vm.DenDayTo = DenDayTo; });
		await CaptureAsync(session, tokuiJuchu, screens, "TokuiSakiJuchuTable");

		var shohinJuchu = session.OpenView<ShouhinJuchuTableView, ShouhinJuchuTableViewModel>();
		await AwaitInitializationAsync(shohinJuchu.Vm, shohinJuchu.View);
		shohinJuchu.Input("商品別受注表:対象月条件", vm => { vm.DenDayFrom = DenDayFrom; vm.DenDayTo = DenDayTo; });
		await CaptureAsync(session, shohinJuchu, screens, "ShouhinJuchuTable");

		var shohinSummary = session.OpenView<ShouhinJuchuSummaryTableView, ShouhinJuchuSummaryTableViewModel>();
		await AwaitInitializationAsync(shohinSummary.Vm, shohinSummary.View);
		shohinSummary.Input("商品別受注集計表:対象月条件", vm => { vm.DenDayFrom = DenDayFrom; vm.DenDayTo = DenDayTo; });
		await CaptureAsync(session, shohinSummary, screens, "ShouhinJuchuSummaryTable");

		var zanKanri = session.OpenView<JuchuZanKanriTableView, JuchuZanKanriTableViewModel>();
		await AwaitInitializationAsync(zanKanri.Vm, zanKanri.View);
		zanKanri.Input("受注残管理表:対象月条件", vm => { vm.DenDayFrom = DenDayFrom; vm.DenDayTo = DenDayTo; });
		await CaptureAsync(session, zanKanri, screens, "JuchuZanKanriTable");

		var completion = session.OpenView<JuchuZanCompletionSettingView, JuchuZanCompletionSettingViewModel>();
		await AwaitInitializationAsync(completion.Vm, completion.View);
		await completion.RunAsync("受注残完了設定:既定条件一覧取得", vm => vm.SearchCommand);
		session.Check("受注残完了設定:一覧取得成功", !completion.Vm.IsBusy && completion.Vm.DenRows.Count > 0,
			new { Count = completion.Vm.DenRows.Count, completion.Vm.Message });
		await CaptureAsync(session, completion, screens, "JuchuZanCompletionSetting");

		var uriageYotei = session.OpenView<TokuiSakiUriageYoteiTableView, TokuiSakiUriageYoteiTableViewModel>();
		await AwaitInitializationAsync(uriageYotei.Vm, uriageYotei.View);
		uriageYotei.Input("得意先別売上予定表:対象月条件", vm => { vm.DenDayFrom = DenDayFrom; vm.DenDayTo = DenDayTo; });
		await CaptureAsync(session, uriageYotei, screens, "TokuiSakiUriageYoteiTable");

		var tenjiGoukei = session.OpenView<TantoTenjiJuchuGoukeiTableView, TantoTenjiJuchuGoukeiTableViewModel>();
		await AwaitInitializationAsync(tenjiGoukei.Vm, tenjiGoukei.View);
		tenjiGoukei.Input("担当別展示会受注合計表:対象月条件", vm => { vm.DenDayFrom = DenDayFrom; vm.DenDayTo = DenDayTo; vm.IncludeNoTenji = true; });
		await CaptureAsync(session, tenjiGoukei, screens, "TantoTenjiJuchuGoukeiTable");

		var best = session.OpenView<JuchuBestTableView, JuchuBestTableViewModel>();
		await AwaitInitializationAsync(best.Vm, best.View);
		best.Input("受注ベスト表:対象月条件", vm => { vm.DenDayFrom = DenDayFrom; vm.DenDayTo = DenDayTo; });
		await CaptureAsync(session, best, screens, "JuchuBestTable");
	}

	static async Task AwaitInitializationAsync<TViewModel>(TViewModel vm, Window view) where TViewModel : class {
		await SettleWindowAsync(view);
		if (vm.GetType().GetProperty("InitCommand")?.GetValue(vm) is IAsyncRelayCommand asyncCommand
			&& asyncCommand.ExecutionTask is { } task) {
			await task;
		}
		await Application.Current.Dispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
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
