using CvAsset;
using CvBase;
using CvWpfclient.ViewModels._00System;
using CvWpfclient.ViewModels._08Zaiko;
using CvWpfclient.Views._00System;
using CvWpfclient.Views._08Zaiko;
using UatVm.Seed;

namespace UatVm.Scenarios;

/// <summary>UAT-03 店舗間の即時移動・積送・受入・未受取消を検証する。</summary>
public static class TransferScenario {
	private static TransferSeeder.Result? _seeded;

	public static void Seeder(string dbPath) =>
		_seeded = TransferSeeder.Seed(dbPath, message => Console.WriteLine($"[seed] {message}"));

	public static async Task RunAsync(VmSession session) {
		var seeded = _seeded ?? throw new InvalidOperationException("シードが実行されていません。");
		session.SetDialogResponder(request => request.Button is System.Windows.MessageBoxButton.YesNo or System.Windows.MessageBoxButton.YesNoCancel
			? System.Windows.MessageBoxResult.Yes : System.Windows.MessageBoxResult.OK);

		var soku = session.OpenView<IdoInputSokuView, IdoInputSokuViewModel>();
		soku.Input("UAT-03:即時移動", vm => vm.CurrentEdit = CreateSoku(seeded, TransferSeeder.ImmediateQty, "SOKU"),
			new { seeded.SourceWarehouseCode, seeded.DestinationWarehouseCode, TransferSeeder.ImmediateQty });
		await soku.RunAsync("UAT-03:即時移動登録", vm => vm.DoInsertOnDetailTabCommand);
		var sokuId = soku.Vm.CurrentEdit.Id;
		await CheckStock(session, seeded, "UAT-03 即時移動", sourceSu: 17, destinationSu: 3, destinationTransit: 0);
		await soku.RunAsync("UAT-03:即時移動取消", vm => vm.DoDeleteOnDetailTabCommand);
		await CheckStock(session, seeded, "UAT-03 即時移動取消", sourceSu: 20, destinationSu: 0, destinationTransit: 0);

		var outVm = session.OpenView<IdoInputOutView, IdoInputOutViewModel>();
		outVm.Input("UAT-03:積送出庫", vm => vm.CurrentEdit = CreateOut(seeded, TransferSeeder.TransitQty, "OUT"),
			new { TransferSeeder.TransitQty });
		await outVm.RunAsync("UAT-03:積送出庫登録", vm => vm.DoInsertOnDetailTabCommand);
		var outId = outVm.Vm.CurrentEdit.Id;
		await CheckStock(session, seeded, "UAT-03 積送出庫", sourceSu: 15, destinationSu: 0, destinationTransit: 5);

		var uke = session.OpenView<IdoInputUkeView, IdoInputUkeViewModel>();
		uke.Input("UAT-03:全量受入", vm => vm.CurrentEdit = CreateUke(seeded, outId, TransferSeeder.TransitQty, "UKE"),
			new { outId, TransferSeeder.TransitQty });
		await uke.RunAsync("UAT-03:移動受登録", vm => vm.DoInsertOnDetailTabCommand);
		if (!session.Check("UAT-03 移動受が積送出庫へ紐付く", uke.Vm.CurrentEdit.RelateNo1 == outId, new { uke.Vm.CurrentEdit.Id, uke.Vm.CurrentEdit.RelateNo1, outId })) return;
		await CheckStock(session, seeded, "UAT-03 全量受入", sourceSu: 15, destinationSu: 5, destinationTransit: 0);

		var cancel = session.OpenView<IdoInputOutView, IdoInputOutViewModel>();
		cancel.Input("UAT-03:未受積送", vm => vm.CurrentEdit = CreateOut(seeded, TransferSeeder.CancelQty, "CANCEL"),
			new { TransferSeeder.CancelQty });
		await cancel.RunAsync("UAT-03:未受積送登録", vm => vm.DoInsertOnDetailTabCommand);
		await CheckStock(session, seeded, "UAT-03 未受積送", sourceSu: 13, destinationSu: 5, destinationTransit: 2);
		await cancel.RunAsync("UAT-03:未受積送取消", vm => vm.DoDeleteOnDetailTabCommand);
		await CheckStock(session, seeded, "UAT-03 未受積送取消", sourceSu: 15, destinationSu: 5, destinationTransit: 0);
		session.Check("UAT-03 即時移動は削除済み", (await session.QueryAsync<Tran05Ido>("where Id=@0", sokuId.ToString())).Count == 0, new { sokuId });
		await CheckRebuildAsync(session, seeded);
		session.SetDialogResponder(null);
	}

	private static Tran05Ido CreateSoku(TransferSeeder.Result seeded, int quantity, string suffix) => new() {
		DenDay = TransferSeeder.ScenarioDay, Id_Soko = seeded.SourceWarehouseId, VSoko = SourceView(seeded),
		Id_Ido = seeded.DestinationWarehouseId, VIdo = DestinationView(seeded), Id_Shain = seeded.EmployeeId,
		VShain = new CodeNameView(seeded.EmployeeId, seeded.EmployeeCode, string.Empty), ManualNo = TransferSeeder.ManualNoPrefix + suffix,
		Jmeisai = [Meisai(seeded, quantity)],
	};

	private static Tran10IdoOut CreateOut(TransferSeeder.Result seeded, int quantity, string suffix) => new() {
		DenDay = TransferSeeder.ScenarioDay, Id_Soko = seeded.SourceWarehouseId, VSoko = SourceView(seeded),
		Id_Ido = seeded.DestinationWarehouseId, VIdo = DestinationView(seeded), Id_Shain = seeded.EmployeeId,
		VShain = new CodeNameView(seeded.EmployeeId, seeded.EmployeeCode, string.Empty), ManualNo = TransferSeeder.ManualNoPrefix + suffix,
		Jmeisai = [Meisai(seeded, quantity)],
	};

	private static Tran11IdoIn CreateUke(TransferSeeder.Result seeded, long outId, int quantity, string suffix) => new() {
		DenDay = TransferSeeder.ScenarioDay, Id_Soko = seeded.SourceWarehouseId, VSoko = SourceView(seeded),
		Id_Ido = seeded.DestinationWarehouseId, VIdo = DestinationView(seeded), Id_Shain = seeded.EmployeeId,
		VShain = new CodeNameView(seeded.EmployeeId, seeded.EmployeeCode, string.Empty), RelateNo1 = outId,
		ManualNo = TransferSeeder.ManualNoPrefix + suffix, Jmeisai = [Meisai(seeded, quantity)],
	};

	private static Tran99Meisai Meisai(TransferSeeder.Result seeded, int quantity) => new() {
		No = 1, Id_Shohin = seeded.ShohinId, Id_Col = seeded.Id_Col, Id_Siz = seeded.Id_Siz,
		JanCode = TransferSeeder.JanCode, Su = quantity,
	};

	private static CodeNameView SourceView(TransferSeeder.Result seeded) => new(seeded.SourceWarehouseId, seeded.SourceWarehouseCode, "UAT-VM 移動元倉庫");
	private static CodeNameView DestinationView(TransferSeeder.Result seeded) => new(seeded.DestinationWarehouseId, seeded.DestinationWarehouseCode, "UAT-VM 移動先倉庫");

	private static async Task CheckStock(VmSession session, TransferSeeder.Result seeded, string name, int sourceSu, int destinationSu, int destinationTransit) {
		var source = await Stock(session, seeded, seeded.SourceWarehouseId);
		var destination = await Stock(session, seeded, seeded.DestinationWarehouseId);
		session.Check(name, source.RealSu == sourceSu && source.MonthlySu == sourceSu
			&& destination.RealSu == destinationSu && destination.MonthlySu == destinationSu
			&& destination.TransitQty == destinationTransit,
			new { source, destination, sourceSu, destinationSu, destinationTransit });
	}

	private static async Task CheckRebuildAsync(VmSession session, TransferSeeder.Result seeded) {
		var sourceBefore = await Stock(session, seeded, seeded.SourceWarehouseId);
		var destinationBefore = await Stock(session, seeded, seeded.DestinationWarehouseId);
		var rebuild = session.OpenView<StockKakeUpdateView, StockKakeUpdateViewModel>();
		await rebuild.RunAsync("UAT-03:在庫Rebuild初期化", vm => vm.InitCommand);
		rebuild.Input("UAT-03:在庫Rebuild対象", vm => {
			vm.YearMonthFrom = "2026/09";
			vm.YearMonthTo = "2026/09";
			vm.UpdateTarget = "在庫のみ";
		}, new { Month = "2026/09", UpdateTarget = "在庫のみ" });
		await rebuild.RunAsync("UAT-03:在庫Rebuild", vm => vm.ExecuteCommand);
		session.Check("UAT-03 在庫Rebuild完了", rebuild.Vm.ProgressValue == 100
			&& rebuild.Vm.StatusMessage.Contains("完了", StringComparison.Ordinal),
			new { rebuild.Vm.ProgressValue, rebuild.Vm.StatusMessage });

		var sourceAfter = await Stock(session, seeded, seeded.SourceWarehouseId);
		var destinationAfter = await Stock(session, seeded, seeded.DestinationWarehouseId);
		session.Check("UAT-03 Rebuild後も移動元の月次・実在庫が一致", sourceAfter == sourceBefore,
			new { before = sourceBefore, after = sourceAfter });
		session.Check("UAT-03 Rebuild後も移動先の月次・実在庫が一致", destinationAfter == destinationBefore,
			new { before = destinationBefore, after = destinationAfter });
	}

	private static async Task<StockValue> Stock(VmSession session, TransferSeeder.Result seeded, long warehouseId) {
		var real = (await session.QueryAsync<SummaryRealStock>("where Id_Soko=@0 AND Id_Shohin=@1 AND Id_Col=@2 AND Id_Siz=@3",
			warehouseId.ToString(), seeded.ShohinId.ToString(), seeded.Id_Col.ToString(), seeded.Id_Siz.ToString())).FirstOrDefault();
		var monthly = (await session.QueryAsync<SummaryStock>("where SumMonth=@0 AND Id_Soko=@1 AND Id_Shohin=@2 AND Id_Col=@3 AND Id_Siz=@4",
			TransferSeeder.ScenarioDay[..6], warehouseId.ToString(), seeded.ShohinId.ToString(), seeded.Id_Col.ToString(), seeded.Id_Siz.ToString())).FirstOrDefault();
		return new StockValue(real?.Su ?? 0, monthly?.Su ?? 0, monthly?.TransitQty ?? 0);
	}

	private sealed record StockValue(int RealSu, int MonthlySu, int TransitQty);
}
