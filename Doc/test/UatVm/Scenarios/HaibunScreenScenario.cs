using System.IO;
using System.Windows;
using CvBase;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels._07Haibun;
using CvWpfclient.Views._07Haibun;
using UatVm.Seed;

namespace UatVm.Scenarios;

/// <summary>
/// 配分再設計 Step 1 で変えた配分確定(商品/得意先)・滞留/欠品実績・出荷指示明細書の実画面を表示し、
/// JPG保存と表示崩れ（ボタンのはみ出し・文字切れ）の自動判定を行う。
/// <para>
/// データは UAT-02 と同じシード（倉庫在庫8）に配分3行を登録して作る。卸先5点・直営店2点・前日指示1点。
/// 仕様は `Doc/spec/2026-10-03_配分再設計_Step1_共通基盤・確定一本化_詳細設計.md` 5章。
/// </para>
/// </summary>
public static class HaibunScreenScenario {
	const string ScreenDirectory = "..\\Doc\\test\\uat20261003\\haibun\\screens";
	const string DenDay = "2026/09/05";
	const string OldDenDay = "2026/09/01";
	static JuchuShippingSeeder.Result? _seeded;

	public static void Seeder(string dbPath) =>
		_seeded = JuchuShippingSeeder.Seed(dbPath, message => Console.WriteLine($"[seed] {message}"));

	public static async Task RunAsync(VmSession session) {
		var seeded = _seeded ?? throw new InvalidOperationException("シードが実行されていません。");
		var screens = Path.Combine(Path.GetFullPath(ScreenDirectory), DateTime.Now.ToString("yyyyMMdd_HHmmss"));
		Directory.CreateDirectory(screens);
		session.Note("実行方法", new { Command = "UatVm haibunscreen --sqlite <複製DB> --manage-server", Screens = screens });
		session.SetDialogResponder(request => request.Button is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel
			? MessageBoxResult.Yes : MessageBoxResult.OK);

		var toTokui = await session.InsertAsync(NewHaibun(seeded, seeded.TokuiId, DenDay, 5));
		var toDirect = await session.InsertAsync(NewHaibun(seeded, seeded.DirectStoreId, DenDay, 2));
		await session.InsertAsync(NewHaibun(seeded, seeded.TokuiId, OldDenDay, 1));
		session.Check("配分3行を登録", toTokui.Id > 0 && toDirect.Id > 0, new { Tokui = toTokui.Id, Direct = toDirect.Id });

		// 配分確定(商品)
		var shohin = session.OpenView<ShippingConfirmShohinView, ShippingConfirmShohinViewModel>();
		InputConfirmConditions(shohin, seeded);
		await shohin.RunAsync("配分確定(商品):検索", vm => vm.SearchCommand);
		var row = shohin.Vm.Rows.SingleOrDefault(x => x.Id == toTokui.Id);
		if (!session.Check("配分確定(商品):3行表示", shohin.Vm.Rows.Count == 3 && row != null, new { rows = shohin.Vm.Rows.Count })) return;
		shohin.Input("配分確定(商品):確定数3（欠品2）", vm => {
			row!.KakuteiSu = 3;
			row.IsChecked = true;
		});
		session.Check("配分確定(商品):欠品2を表示", row!.ShortSu == 2, new { row.KakuteiSu, row.ShortSu });
		await CaptureAsync(session, shohin.View, screens, "01_HaibunCommitShohin");

		// 配分確定(得意先)
		var tokui = session.OpenView<ShippingConfirmTokuiView, ShippingConfirmTokuiViewModel>();
		InputConfirmConditions(tokui, seeded);
		await tokui.RunAsync("配分確定(得意先):検索", vm => vm.SearchCommand);
		session.Check("配分確定(得意先):3行表示", tokui.Vm.Rows.Count == 3, new { rows = tokui.Vm.Rows.Count });
		await CaptureAsync(session, tokui.View, screens, "02_HaibunCommitTokui");

		// 滞留（未確定のまま指示日からN日）
		var list = session.OpenView<ShippingConfirmListView, ShippingConfirmListViewModel>();
		list.Input("滞留:検索条件", vm => {
			vm.ViewKind = "滞留";
			vm.DayFromText = OldDenDay;
			vm.DayToText = DenDay;
			vm.SokoCode = seeded.WarehouseCode;
			vm.StagnationDaysText = "0";
			vm.MaxCountText = "500";
		});
		await list.RunAsync("滞留:検索", vm => vm.SearchCommand);
		session.Check("滞留:3行表示", list.Vm.Rows.Count == 3, new { rows = list.Vm.Rows.Count, list.Vm.Message });
		await CaptureAsync(session, list.View, screens, "03_StagnationList");

		// 確定（欠品あり）→ 欠品実績
		await shohin.RunAsync("配分確定(商品):確定数3で確定", vm => vm.ConfirmSelectedCommand);
		var committed = (await session.QueryAsync<TranHaibun>("where Id=@0", toTokui.Id.ToString())).Single();
		session.Check("配分確定:完了・欠品2", committed is { EndFlag: 1, JitsuSu: 3, ShortSu: 2 } && committed.RelateNo2 > 0,
			new { committed.EndFlag, committed.JitsuSu, committed.ShortSu, committed.RelateNo2 });
		await CaptureAsync(session, shohin.View, screens, "04_HaibunCommitShohinAfter");

		list.Input("欠品実績:検索条件", vm => {
			vm.ViewKind = "欠品実績";
			vm.DayFromText = DenDay;
			vm.DayToText = DenDay;
		});
		await list.RunAsync("欠品実績:検索", vm => vm.SearchCommand);
		session.Check("欠品実績:1行表示", list.Vm.Rows.Count == 1 && list.Vm.Rows[0].ShortSu == 2,
			new { rows = list.Vm.Rows.Count, list.Vm.Message });
		await CaptureAsync(session, list.View, screens, "05_ShortageResult");

		// 出荷指示明細書（ピッキングリスト）の条件画面
		var print = session.OpenView<ShippingConfirmDetailPrintView, ShippingConfirmDetailPrintViewModel>();
		session.Check("出荷指示明細書:未完了のみが既定", print.Vm.MikanryoOnly, new { print.Vm.MikanryoOnly });
		await CaptureAsync(session, print.View, screens, "06_PickingListPrint");

		session.SetDialogResponder(null);
	}

	static TranHaibun NewHaibun(JuchuShippingSeeder.Result seeded, long idTenpo, string denDay, int su) => new() {
		DenDay = denDay.Replace("/", string.Empty),
		NouhinDay = denDay.Replace("/", string.Empty),
		Id_Soko = seeded.WarehouseId,
		Id_Tenpo = idTenpo,
		Kubun = (int)EnumHaibun.Zaiko,
		Id_Shohin = seeded.ShohinId,
		Id_Col = seeded.Id_Col,
		Id_Siz = seeded.Id_Siz,
		JanCode = JuchuShippingSeeder.JanCode,
		Su = su,
		Tanka = 2000,
		Kingaku = su * 2000,
		Jodai = 2000,
		Gedai = 1000,
		Id_Shain = seeded.EmployeeId,
	};

	static void InputConfirmConditions<TViewModel>(ViewDriver<TViewModel> driver, JuchuShippingSeeder.Result seeded)
		where TViewModel : BaseShippingConfirmViewModel =>
		driver.Input("配分確定:検索条件", vm => {
			vm.DenDayFromText = OldDenDay;
			vm.DenDayToText = DenDay;
			vm.KakuteiDayText = DenDay;
			vm.SokoCode = seeded.WarehouseCode;
			vm.MaxCountText = "500";
		});

	/// <summary>描画を落ち着かせてからJPG保存と表示崩れ判定を行う。崩れは1件ずつ判定に残す</summary>
	internal static async Task CaptureAsync(VmSession session, Window view, string directory, string name) {
		await Application.Current.Dispatcher.InvokeAsync(view.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
		await Task.Delay(300);
		await Application.Current.Dispatcher.InvokeAsync(() => {
			var path = ScreenLayoutCheck.SaveJpeg(view, directory, name);
			session.Note($"{name}:画面画像", new { Path = path });
			var all = ScreenLayoutCheck.Inspect(view);
			var issues = all.Where(x => x.Kind != ScreenLayoutCheck.LongCellKind).ToList();
			session.Check($"{name}:表示崩れなし", issues.Count == 0, new { Count = issues.Count, Issues = issues.Take(40) });
			var longCells = all.Where(x => x.Kind == ScreenLayoutCheck.LongCellKind).ToList();
			if (longCells.Count > 0) session.Note($"{name}:長い名称のセル文字切れ", new { Count = longCells.Count, Cells = longCells.Take(20) });
		}, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
	}
}
