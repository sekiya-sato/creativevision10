using CvBase;
using CvWpfclient.ViewModels._08Zaiko;
using CvWpfclient.ViewModels._31Monthly;
using CvWpfclient.Views._08Zaiko;
using CvWpfclient.Views._31Monthly;
using System.Windows;
using UatVm.Seed;

namespace UatVm.Scenarios;

/// <summary>UAT-04 棚卸開始、実棚入力、確定調整の在庫遷移を検証する。</summary>
public static class StocktakeScenario {
	private const int ActualQty = 7;
	private const int AdjustQty = ActualQty - StocktakeSeeder.InitialStock;
	private const string StocktakeNo = "UATVM-ST-01";
	private static StocktakeSeeder.Result? _seeded;

	public static void Seeder(string dbPath) =>
		_seeded = StocktakeSeeder.Seed(dbPath, message => Console.WriteLine($"[seed] {message}"));

	public static async Task RunAsync(VmSession session) {
		var seeded = _seeded ?? throw new InvalidOperationException("シードが実行されていません。");
		session.SetDialogResponder(request => request.Button is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel
			? MessageBoxResult.Yes : MessageBoxResult.OK);

		var start = session.OpenView<StockTakeInitiationView, StockTakeInitiationViewModel>();
		await LoadTargetAsync(start, seeded.WarehouseId);
		await start.RunAsync("UAT-04:棚卸開始", vm => vm.ExecuteCommand);
		var afterStart = await Stock(session, seeded);
		if (!session.Check("UAT-04 開始時に帳簿在庫だけを保存", afterStart.Monthly?.BookQty == StocktakeSeeder.InitialStock
			&& afterStart.Monthly.ActualQty == 0
			&& afterStart.Monthly.StocktakeDdate == StocktakeSeeder.StocktakeDay,
			new { afterStart.Monthly })) return;

		var input = session.OpenView<StockInputView, StockInputViewModel>();
		input.Input("UAT-04:実棚7入力", vm => {
			vm.SelectedTabIndex = 1;
			vm.CurrentEdit = CreateStocktake(seeded);
		}, new { ActualQty });
		await input.RunAsync("UAT-04:棚卸入力登録", vm => vm.DoInsertOnDetailTabCommand);
		var afterInput = await Stock(session, seeded);
		var adjustmentsBeforeFix = await session.QueryAsync<Tran61Chosei>("where Id_Soko=@0 AND TanaMonth=@1", seeded.WarehouseId.ToString(), StocktakeSeeder.SumMonth);
		if (!session.Check("UAT-04 実棚入力だけでは実在庫を動かさない", afterInput.Real?.Su == StocktakeSeeder.InitialStock && adjustmentsBeforeFix.Count == 0,
			new { afterInput.Real, adjustmentCount = adjustmentsBeforeFix.Count })) return;

		var fix = session.OpenView<StockTakeFinalizationView, StockTakeFinalizationViewModel>();
		await LoadTargetAsync(fix, seeded.WarehouseId);
		await fix.RunAsync("UAT-04:棚卸確定", vm => vm.ExecuteCommand);
		var adjustment = (await session.QueryAsync<Tran61Chosei>("where Id_Soko=@0 AND TanaMonth=@1", seeded.WarehouseId.ToString(), StocktakeSeeder.SumMonth)).SingleOrDefault();
		var afterFix = await Stock(session, seeded);
		session.Check("UAT-04 確定で棚卸調整-3を作成", adjustment?.Kubun == (int)EnumChosei.Tanaoroshi
			&& adjustment.DenDay == StocktakeSeeder.StocktakeDay
			&& adjustment.SuTotal == AdjustQty
			&& (adjustment.Jmeisai ?? []).SingleOrDefault()?.Su == AdjustQty,
			new { adjustment?.Kubun, adjustment?.DenDay, adjustment?.SuTotal, meisai = adjustment?.Jmeisai });
		session.Check("UAT-04 確定後の月次在庫", afterFix.Monthly?.BookQty == StocktakeSeeder.InitialStock
			&& afterFix.Monthly.ActualQty == ActualQty
			&& afterFix.Monthly.AdjustQty == AdjustQty,
			new { afterFix.Monthly });
		session.Check("UAT-04 確定後の実在庫", afterFix.Real?.Su == ActualQty, new { afterFix.Real });
		session.Check("UAT-04 確定済表示", fix.Vm.Rows.Single(row => row.Id_Soko == seeded.WarehouseId).StatusText == "確定済",
			new { rows = fix.Vm.Rows.Select(row => new { row.Id_Soko, row.StatusText }) });
		session.SetDialogResponder(null);
	}

	private static async Task LoadTargetAsync<T>(ViewDriver<T> driver, long warehouseId) where T : BaseStocktakeViewModel {
		driver.Input("UAT-04:対象月", vm => vm.FallbackMonth = "2026/09", new { warehouseId });
		await driver.RunAsync("UAT-04:棚卸状況取得", vm => vm.LoadStatusCommand);
		driver.Vm.Rows.ToList().ForEach(row => row.IsTarget = row.Id_Soko == warehouseId);
		if (!driver.Vm.Rows.Any(row => row.IsTarget)) throw new InvalidOperationException("専用倉庫が棚卸状況にありません。");
	}

	private static Tran60Tana CreateStocktake(StocktakeSeeder.Result seeded) => new() {
		DenDay = StocktakeSeeder.StocktakeDay,
		Id_Soko = seeded.WarehouseId,
		VSoko = new CodeNameView(seeded.WarehouseId, seeded.WarehouseCode, "UAT-VM 棚卸倉庫"),
		Id_Shain = seeded.EmployeeId,
		VShain = new CodeNameView(seeded.EmployeeId, seeded.EmployeeCode, string.Empty),
		TanaNo = StocktakeNo,
		Jmeisai = [new Tran99Meisai {
			No = 1, Id_Shohin = seeded.ShohinId, Id_Col = seeded.Id_Col, Id_Siz = seeded.Id_Siz,
			JanCode = StocktakeSeeder.JanCode, Su = ActualQty,
		}],
	};

	private static async Task<StockPair> Stock(VmSession session, StocktakeSeeder.Result seeded) {
		var where = "where Id_Soko=@0 AND Id_Shohin=@1 AND Id_Col=@2 AND Id_Siz=@3";
		var values = new[] { seeded.WarehouseId.ToString(), seeded.ShohinId.ToString(), seeded.Id_Col.ToString(), seeded.Id_Siz.ToString() };
		return new StockPair(
			(await session.QueryAsync<SummaryStock>(where, values)).SingleOrDefault(),
			(await session.QueryAsync<SummaryRealStock>(where, values)).SingleOrDefault());
	}

	private sealed record StockPair(SummaryStock? Monthly, SummaryRealStock? Real);
}
