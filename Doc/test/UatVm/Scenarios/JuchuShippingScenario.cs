using System.Reflection;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels._04Juchu;
using CvWpfclient.ViewModels._07Haibun;
using CvWpfclient.Views._04Juchu;
using CvWpfclient.Views._07Haibun;
using UatVm.Seed;

namespace UatVm.Scenarios;

/// <summary>UAT-02 受注から配分・確定・出荷までを実ViewModel経路で検証する。</summary>
public static class JuchuShippingScenario {
	private const string DenDay = "2026/09/05";
	private const int OrderQuantity = 10;
	private const int AllocationQuantity = 8;
	private const int ShippingQuantity = 6;
	private const int ConcurrencyQuantity = 1;
	private static JuchuShippingSeeder.Result? _seeded;

	public static void Seeder(string dbPath) =>
		_seeded = JuchuShippingSeeder.Seed(dbPath, message => Console.WriteLine($"[seed] {message}"));

	public static async Task RunAsync(VmSession session) {
		var seeded = _seeded ?? throw new InvalidOperationException("シードが実行されていません。");
		session.SetDialogResponder(request => request.Button is System.Windows.MessageBoxButton.YesNo or System.Windows.MessageBoxButton.YesNoCancel
			? System.Windows.MessageBoxResult.Yes : System.Windows.MessageBoxResult.OK);

		var order = session.OpenView<JuchuInputView, JuchuInputViewModel>();
		order.Input("UAT-02:受注入力", vm => {
			vm.SelectedTabIndex = 1;
			vm.CurrentEdit = new Tran12Jyuchu {
				DenDay = DenDay.Replace("/", string.Empty),
				NouhinDay = DenDay.Replace("/", string.Empty),
				Id_Tokui = seeded.TokuiId,
				VTokui = new CodeNameView(seeded.TokuiId, seeded.TokuiCode, "UAT-VM 受注出荷卸先"),
				Id_Soko = seeded.WarehouseId,
				VSoko = new CodeNameView(seeded.WarehouseId, seeded.WarehouseCode, "UAT-VM 受注出荷倉庫"),
				Id_Shain = seeded.EmployeeId,
				VShain = new CodeNameView(seeded.EmployeeId, seeded.EmployeeCode, string.Empty),
				Kubun = (int)EnumJuchu.Juchu,
				Rate = 100,
				Jmeisai = [new Tran99Meisai {
					No = 1, Id_Shohin = seeded.ShohinId, Id_Col = seeded.Id_Col, Id_Siz = seeded.Id_Siz,
					JanCode = JuchuShippingSeeder.JanCode, Su = OrderQuantity, Tanka = 2000,
					Kingaku = OrderQuantity * 2000, Jodai = 2000, Gedai = 1000, Id_Tax = 1,
				}],
			};
		}, new { seeded.TokuiCode, seeded.WarehouseCode, seeded.ShohinCode, OrderQuantity });
		await order.RunAsync("UAT-02:受注登録", vm => vm.DoInsertOnDetailTabCommand);
		var orderId = order.Vm.CurrentEdit.Id;
		if (!session.Check("UAT-02 受注が採番される", orderId > 0, new { orderId })) return;

		var allocated = session.OpenView<JuchuHaibunInputView, JuchuHaibunInputViewModel>();
		allocated.Input("UAT-02:配分検索条件", vm => {
			vm.JuchuDayFrom = DateTime.Parse(DenDay);
			vm.JuchuDayTo = DateTime.Parse(DenDay);
			vm.CondTokuiDisplay = seeded.TokuiCode;
		}, new { DenDay, seeded.TokuiCode });
		await allocated.RunAsync("UAT-02:配分検索", vm => vm.DoSearchCommand);
		allocated.Vm.SelectedSearchRow = allocated.Vm.SearchRows.SingleOrDefault(x => x.Id == orderId);
		if (!session.Check("UAT-02 受注が配分一覧に出る", allocated.Vm.SelectedSearchRow != null, new { orderId })) return;
		await allocated.RunAsync("UAT-02:配分入力へ", vm => vm.GoToEditCommand);
		allocated.Input("UAT-02:配分指示日", vm => {
			vm.ShijiDay = DateTime.Parse(DenDay);
			vm.NouhinDay = DateTime.Parse(DenDay);
		}, new { DenDay });
		allocated.Run("UAT-02:受注残読込", vm => vm.LoadJuchuZanCommand);
		var allocationRow = allocated.Vm.MeisaiRows.Single();
		if (!session.Check("UAT-02 受注10・在庫8を配分画面へ表示", allocationRow.Su == OrderQuantity && allocationRow.HaibunKanoSu == seeded.InitialStock,
			new { allocationRow.Su, allocationRow.HaibunKanoSu, seeded.InitialStock })) return;
		await allocated.RunAsync("UAT-02:在庫割れ配分10を登録", vm => vm.DoRegisterCommand);

		var beforeConfirm = (await session.QueryAsync<TranHaibun>("where RelateNo1=@0", orderId.ToString())).Single();
		if (!session.Check("UAT-02 在庫割れ配分は受注へ紐付く", beforeConfirm.Su == OrderQuantity && beforeConfirm.EndFlag == 0 && string.IsNullOrEmpty(beforeConfirm.KakuteiDay),
			new { beforeConfirm.Id, beforeConfirm.Su, beforeConfirm.RelateNo1, beforeConfirm.EndFlag })) return;

		var confirm = session.OpenView<ShippingConfirmShohinView, ShippingConfirmShohinViewModel>();
		confirm.Input("UAT-02:確定検索条件", vm => {
			vm.DenDayFromText = DenDay;
			vm.DenDayToText = DenDay;
			vm.KakuteiDayText = DenDay;
			vm.SokoCode = seeded.WarehouseCode;
			vm.MaxCountText = "500";
		}, new { DenDay, seeded.WarehouseCode });
		await confirm.RunAsync("UAT-02:出荷確定検索", vm => vm.SearchCommand);
		var confirmRow = confirm.Vm.Rows.SingleOrDefault(x => x.Id == beforeConfirm.Id);
		if (!session.Check("UAT-02 未確定配分が確定一覧に出る", confirmRow != null, new { beforeConfirm.Id, rows = confirm.Vm.Rows.Count, confirm.Vm.Message })) return;
		confirmRow!.IsChecked = true;
		await confirm.RunAsync("UAT-02:在庫割れ確定", vm => vm.ConfirmSelectedCommand);
		var rejected = (await session.QueryAsync<TranHaibun>("where Id=@0", beforeConfirm.Id.ToString())).Single();
		if (!session.Check("UAT-02 在庫割れは1件も確定しない", string.IsNullOrEmpty(rejected.KakuteiDay)
			&& confirm.Vm.Message.Contains("有効在庫が不足", StringComparison.Ordinal),
			new { rejected.KakuteiDay, confirm.Vm.Message, confirmRow.Yuko })) return;

		allocated.Input("UAT-02:配分を在庫8へ訂正", vm => vm.MeisaiRows.Single().Su = AllocationQuantity,
			new { AllocationQuantity });
		await allocated.RunAsync("UAT-02:訂正配分登録", vm => vm.DoRegisterCommand);
		var corrected = (await session.QueryAsync<TranHaibun>("where RelateNo1=@0", orderId.ToString())).Single();
		if (!session.Check("UAT-02 配分訂正で引当8", corrected.Su == AllocationQuantity && corrected.EndFlag == 0,
			new { corrected.Id, corrected.Su, AllocationQuantity })) return;

		await confirm.RunAsync("UAT-02:訂正後の確定検索", vm => vm.SearchCommand);
		confirmRow = confirm.Vm.Rows.SingleOrDefault(x => x.Id == corrected.Id);
		if (!session.Check("UAT-02 訂正配分が確定一覧に出る", confirmRow != null && confirmRow.Yuko == 0,
			new { corrected.Id, rows = confirm.Vm.Rows.Count, yuko = confirmRow?.Yuko })) return;
		confirmRow!.IsChecked = true;
		await confirm.RunAsync("UAT-02:訂正後の出荷確定", vm => vm.ConfirmSelectedCommand);
		var confirmed = (await session.QueryAsync<TranHaibun>("where Id=@0", corrected.Id.ToString())).Single();
		if (!session.Check("UAT-02 配分が確定される", !string.IsNullOrEmpty(confirmed.KakuteiDay), new { confirmed.Id, confirmed.KakuteiDay })) return;

		var stockBeforeCancel = (await session.QueryAsync<SummaryRealStock>("where Id_Soko=@0 AND Id_Shohin=@1 AND Id_Col=@2 AND Id_Siz=@3",
			seeded.WarehouseId.ToString(), seeded.ShohinId.ToString(), seeded.Id_Col.ToString(), seeded.Id_Siz.ToString())).Single();
		var salesBeforeCancel = await session.QueryAsync<Tran00Uriage>("where RelateNo1=@0", orderId.ToString());
		var cancel = session.OpenView<ShippingConfirmListView, ShippingConfirmListViewModel>();
		cancel.Input("UAT-02:確定取消の滞留検索条件", vm => {
			vm.ViewKind = "滞留";
			vm.KakuteiFromText = DenDay;
			vm.KakuteiToText = DenDay;
			vm.SokoCode = seeded.WarehouseCode;
			vm.TokuiCode = seeded.TokuiCode;
			vm.StagnationDaysText = "0";
			vm.MaxCountText = "500";
		}, new { DenDay, seeded.WarehouseCode, seeded.TokuiCode });
		await cancel.RunAsync("UAT-02:確定取消の滞留検索", vm => vm.SearchCommand);
		var cancelRow = cancel.Vm.Rows.SingleOrDefault(x => x.Id == corrected.Id);
		if (!session.Check("UAT-02 確定済み未出荷配分が滞留一覧に出る", cancelRow != null,
			new { corrected.Id, rows = cancel.Vm.Rows.Count, cancel.Vm.Message })) return;
		cancelRow!.IsChecked = true;
		await cancel.RunAsync("UAT-02:出荷確定取消", vm => vm.CancelConfirmCommand);

		var canceled = (await session.QueryAsync<TranHaibun>("where Id=@0", corrected.Id.ToString())).Single();
		var stockAfterCancel = (await session.QueryAsync<SummaryRealStock>("where Id_Soko=@0 AND Id_Shohin=@1 AND Id_Col=@2 AND Id_Siz=@3",
			seeded.WarehouseId.ToString(), seeded.ShohinId.ToString(), seeded.Id_Col.ToString(), seeded.Id_Siz.ToString())).Single();
		var salesAfterCancel = await session.QueryAsync<Tran00Uriage>("where RelateNo1=@0", orderId.ToString());
		if (!session.Check("UAT-02 確定取消で未確定へ復帰", canceled is { EndFlag: 0, RelateNo2: 0 } && string.IsNullOrEmpty(canceled.KakuteiDay),
			new { canceled.Id, canceled.EndFlag, canceled.RelateNo2, canceled.KakuteiDay })) return;
		if (!session.Check("UAT-02 確定取消で在庫・引当を維持", stockAfterCancel.Su == stockBeforeCancel.Su
			&& stockAfterCancel.ReserveQty == stockBeforeCancel.ReserveQty,
			new { before = new { stockBeforeCancel.Su, stockBeforeCancel.ReserveQty }, after = new { stockAfterCancel.Su, stockAfterCancel.ReserveQty } })) return;
		if (!session.Check("UAT-02 確定取消で売上を作成しない", salesBeforeCancel.Count == 0 && salesAfterCancel.Count == 0,
			new { before = salesBeforeCancel.Count, after = salesAfterCancel.Count })) return;

		await confirm.RunAsync("UAT-02:取消後の確定検索", vm => vm.SearchCommand);
		confirmRow = confirm.Vm.Rows.SingleOrDefault(x => x.Id == corrected.Id);
		if (!session.Check("UAT-02 取消後の配分を再指示できる", confirmRow != null,
			new { corrected.Id, rows = confirm.Vm.Rows.Count, confirm.Vm.Message })) return;
		confirmRow!.IsChecked = true;
		await confirm.RunAsync("UAT-02:取消後の出荷再確定", vm => vm.ConfirmSelectedCommand);
		var reconfirmed = (await session.QueryAsync<TranHaibun>("where Id=@0", corrected.Id.ToString())).Single();
		if (!session.Check("UAT-02 取消後に再確定される", !string.IsNullOrEmpty(reconfirmed.KakuteiDay),
			new { reconfirmed.Id, reconfirmed.KakuteiDay })) return;

		var shipping = session.OpenView<ShippingInputView, ShippingInputViewModel>();
		shipping.Input("UAT-02:出荷検索条件", vm => {
			vm.KakuteiFromText = DenDay;
			vm.KakuteiToText = DenDay;
			vm.DenDayText = DenDay;
			vm.SokoCode = seeded.WarehouseCode;
			vm.MaxCountText = "500";
			SetShippingEmployee(vm, seeded.EmployeeId, seeded.EmployeeCode);
		}, new { DenDay, seeded.WarehouseCode, seeded.EmployeeCode });
		await shipping.RunAsync("UAT-02:出荷検索", vm => vm.SearchCommand);
		var shippingRow = shipping.Vm.Rows.SingleOrDefault(x => x.Id == corrected.Id);
		if (!session.Check("UAT-02 確定配分が出荷一覧に出る", shippingRow != null, new { corrected.Id, rows = shipping.Vm.Rows.Count, shipping.Vm.Message })) return;
		shippingRow!.JitsuSu = ShippingQuantity;
		if (!session.Check("UAT-02 実出荷6・欠品2を画面で設定", shippingRow.JitsuSu == ShippingQuantity && shippingRow.ShortSu == AllocationQuantity - ShippingQuantity,
			new { shippingRow.JitsuSu, shippingRow.ShortSu })) return;
		shippingRow!.IsChecked = true;
		await shipping.RunAsync("UAT-02:出荷実行", vm => vm.ExecuteCommand);

		var shipped = (await session.QueryAsync<TranHaibun>("where Id=@0", corrected.Id.ToString())).Single();
		var sales = await session.QueryAsync<Tran00Uriage>("where RelateNo1=@0", orderId.ToString());
		var stock = (await session.QueryAsync<SummaryRealStock>("where Id_Soko=@0 AND Id_Shohin=@1 AND Id_Col=@2 AND Id_Siz=@3",
			seeded.WarehouseId.ToString(), seeded.ShohinId.ToString(), seeded.Id_Col.ToString(), seeded.Id_Siz.ToString())).Single();
		session.Check("UAT-02 欠品確定で配分を完了・引当解除", shipped.EndFlag == 1 && shipped.JitsuSu == ShippingQuantity
			&& shipped.ShortSu == AllocationQuantity - ShippingQuantity && shipped.RelateNo2 > 0,
			new { shipped.EndFlag, shipped.JitsuSu, shipped.ShortSu, shipped.RelateNo2 });
		session.Check("UAT-02 出荷売上6が受注へ紐付く", sales.Count == 1 && sales[0].RelateNo1 == orderId
			&& sales[0].Jmeisai?.Single().Su == ShippingQuantity, new { count = sales.Count, orderId, su = sales.FirstOrDefault()?.Jmeisai?.SingleOrDefault()?.Su });
		session.Check("UAT-02 在庫2・引当0になる", stock.Su == seeded.InitialStock - ShippingQuantity && stock.ReserveQty == 0,
			new { stock.Su, stock.ReserveQty, seeded.InitialStock, ShippingQuantity });

		await allocated.RunAsync("UAT-02:出荷後の受注残再検索", vm => vm.DoSearchCommand);
		var afterOrder = allocated.Vm.SearchRows.SingleOrDefault(x => x.Id == orderId);
		if (!session.Check("UAT-02 出荷後の受注残は4", afterOrder is { ShukkaSu: ShippingQuantity, HaibunSu: 0 }
			&& afterOrder.ZanSu == OrderQuantity - ShippingQuantity,
			new { afterOrder?.ShukkaSu, afterOrder?.HaibunSu, afterOrder?.ZanSu })) return;

		allocated.Input("UAT-02:全量欠品用の受注残を再読込", vm => {
			vm.SelectedTabIndex = 0;
			vm.SelectedSearchRow = afterOrder;
		}, new { orderId, afterOrder?.ZanSu });
		await allocated.RunAsync("UAT-02:全量欠品用の配分入力へ", vm => vm.GoToEditCommand);
		allocated.Input("UAT-02:在庫2を全量欠品用に配分", vm => {
			vm.ShijiDay = DateTime.Parse(DenDay);
			vm.NouhinDay = DateTime.Parse(DenDay);
			vm.MeisaiRows.Single().Su = seeded.InitialStock - ShippingQuantity;
		}, new { quantity = seeded.InitialStock - ShippingQuantity });
		await allocated.RunAsync("UAT-02:全量欠品用の配分登録", vm => vm.DoRegisterCommand);
		var allShortage = (await session.QueryAsync<TranHaibun>(
			"where RelateNo1=@0 AND EndFlag=0 AND ifnull(KakuteiDay,'')=''", orderId.ToString())).Single();
		if (!session.Check("UAT-02 全量欠品用に在庫2を引当", allShortage.Su == seeded.InitialStock - ShippingQuantity,
			new { allShortage.Id, allShortage.Su })) return;

		await confirm.RunAsync("UAT-02:全量欠品用の確定検索", vm => vm.SearchCommand);
		confirmRow = confirm.Vm.Rows.SingleOrDefault(x => x.Id == allShortage.Id);
		if (!session.Check("UAT-02 全量欠品用の配分が確定一覧に出る", confirmRow != null,
			new { allShortage.Id, rows = confirm.Vm.Rows.Count })) return;
		confirmRow!.IsChecked = true;
		await confirm.RunAsync("UAT-02:全量欠品用の出荷確定", vm => vm.ConfirmSelectedCommand);
		var allShortageConfirmed = (await session.QueryAsync<TranHaibun>("where Id=@0", allShortage.Id.ToString())).Single();
		if (!session.Check("UAT-02 全量欠品用の配分が確定される", !string.IsNullOrEmpty(allShortageConfirmed.KakuteiDay),
			new { allShortageConfirmed.Id, allShortageConfirmed.KakuteiDay })) return;

		var stagnation = session.OpenView<ShippingConfirmListView, ShippingConfirmListViewModel>();
		stagnation.Input("UAT-02:滞留検索条件", vm => {
			vm.ViewKind = "滞留";
			vm.KakuteiFromText = DenDay;
			vm.KakuteiToText = DenDay;
			vm.SokoCode = seeded.WarehouseCode;
			vm.TokuiCode = seeded.TokuiCode;
			vm.StagnationDaysText = "0";
			vm.MaxCountText = "500";
		}, new { DenDay, seeded.WarehouseCode, seeded.TokuiCode });
		await stagnation.RunAsync("UAT-02:滞留検索", vm => vm.SearchCommand);
		var stagnationRow = stagnation.Vm.Rows.SingleOrDefault(x => x.Id == allShortage.Id);
		if (!session.Check("UAT-02 確定済み未処理が滞留一覧に出る", stagnationRow != null,
			new { allShortage.Id, rows = stagnation.Vm.Rows.Count, stagnation.Vm.Message })) return;
		stagnationRow!.IsChecked = true;
		await stagnation.RunAsync("UAT-02:全量欠品で強制完了", vm => vm.ForceCompleteCommand);

		var forceCompleted = (await session.QueryAsync<TranHaibun>("where Id=@0", allShortage.Id.ToString())).Single();
		var salesAfterForce = await session.QueryAsync<Tran00Uriage>("where RelateNo1=@0", orderId.ToString());
		var stockAfterForce = (await session.QueryAsync<SummaryRealStock>("where Id_Soko=@0 AND Id_Shohin=@1 AND Id_Col=@2 AND Id_Siz=@3",
			seeded.WarehouseId.ToString(), seeded.ShohinId.ToString(), seeded.Id_Col.ToString(), seeded.Id_Siz.ToString())).Single();
		session.Check("UAT-02 強制完了で実出荷0・全量欠品2・伝票なし", forceCompleted is { EndFlag: 1, JitsuSu: 0 }
			&& forceCompleted.ShortSu == seeded.InitialStock - ShippingQuantity && forceCompleted.RelateNo2 == 0
			&& salesAfterForce.Count == sales.Count,
			new { forceCompleted.EndFlag, forceCompleted.JitsuSu, forceCompleted.ShortSu, forceCompleted.RelateNo2,
				salesBefore = sales.Count, salesAfter = salesAfterForce.Count });
		session.Check("UAT-02 強制完了で在庫2を維持し引当解除", stockAfterForce.Su == seeded.InitialStock - ShippingQuantity
			&& stockAfterForce.ReserveQty == 0, new { stockAfterForce.Su, stockAfterForce.ReserveQty });

		allocated.Input("UAT-02:強制完了後の受注残再検索", vm => vm.SelectedTabIndex = 0, new { orderId });
		await allocated.RunAsync("UAT-02:強制完了後の受注残再検索", vm => vm.DoSearchCommand);
		var afterForceOrder = allocated.Vm.SearchRows.SingleOrDefault(x => x.Id == orderId);
		if (!session.Check("UAT-02 強制完了後も受注残は4", afterForceOrder is { ShukkaSu: ShippingQuantity, HaibunSu: 0 }
			&& afterForceOrder.ZanSu == OrderQuantity - ShippingQuantity,
			new { afterForceOrder?.ShukkaSu, afterForceOrder?.HaibunSu, afterForceOrder?.ZanSu })) return;

		allocated.Input("UAT-02:競合確認用の受注残を読込", vm => {
			vm.SelectedTabIndex = 0;
			vm.SelectedSearchRow = afterForceOrder;
		}, new { orderId, afterForceOrder!.ZanSu });
		await allocated.RunAsync("UAT-02:競合確認用の配分入力へ", vm => vm.GoToEditCommand);
		allocated.Input("UAT-02:競合確認用に1点配分", vm => {
			vm.ShijiDay = DateTime.Parse(DenDay);
			vm.NouhinDay = DateTime.Parse(DenDay);
			vm.MeisaiRows.Single().Su = ConcurrencyQuantity;
		}, new { ConcurrencyQuantity });
		await allocated.RunAsync("UAT-02:競合確認用の配分登録", vm => vm.DoRegisterCommand);
		var concurrencyAllocation = (await session.QueryAsync<TranHaibun>(
			"where RelateNo1=@0 AND EndFlag=0 AND ifnull(KakuteiDay,'')=''", orderId.ToString())).Single();
		if (!session.Check("UAT-02 競合確認用に1点を引当", concurrencyAllocation.Su == ConcurrencyQuantity,
			new { concurrencyAllocation.Id, concurrencyAllocation.Su, ConcurrencyQuantity })) return;

		await confirm.RunAsync("UAT-02:競合確認用の確定検索", vm => vm.SearchCommand);
		confirmRow = confirm.Vm.Rows.SingleOrDefault(x => x.Id == concurrencyAllocation.Id);
		if (!session.Check("UAT-02 競合確認用配分が確定一覧に出る", confirmRow != null,
			new { concurrencyAllocation.Id, rows = confirm.Vm.Rows.Count })) return;
		confirmRow!.IsChecked = true;
		await confirm.RunAsync("UAT-02:競合確認用の出荷確定", vm => vm.ConfirmSelectedCommand);

		await shipping.RunAsync("UAT-02:競合前の出荷検索", vm => vm.SearchCommand);
		var staleShippingRow = shipping.Vm.Rows.SingleOrDefault(x => x.Id == concurrencyAllocation.Id);
		if (!session.Check("UAT-02 競合前のVduを出荷一覧へ保持", staleShippingRow != null,
			new { concurrencyAllocation.Id, rows = shipping.Vm.Rows.Count })) return;
		var staleVdu = staleShippingRow!.Vdu;
		var salesBeforeConflict = await session.QueryAsync<Tran00Uriage>("where RelateNo1=@0", orderId.ToString());
		var stockBeforeConflict = (await session.QueryAsync<SummaryRealStock>("where Id_Soko=@0 AND Id_Shohin=@1 AND Id_Col=@2 AND Id_Siz=@3",
			seeded.WarehouseId.ToString(), seeded.ShohinId.ToString(), seeded.Id_Col.ToString(), seeded.Id_Siz.ToString())).Single();

		await cancel.RunAsync("UAT-02:他端末で確定取消検索", vm => vm.SearchCommand);
		var concurrentCancelRow = cancel.Vm.Rows.SingleOrDefault(x => x.Id == concurrencyAllocation.Id);
		if (!session.Check("UAT-02 他端末で確定取消対象を取得", concurrentCancelRow != null,
			new { concurrencyAllocation.Id, rows = cancel.Vm.Rows.Count })) return;
		concurrentCancelRow!.IsChecked = true;
		await cancel.RunAsync("UAT-02:他端末で出荷確定取消", vm => vm.CancelConfirmCommand);
		await confirm.RunAsync("UAT-02:他端末で再確定検索", vm => vm.SearchCommand);
		var concurrentConfirmRow = confirm.Vm.Rows.SingleOrDefault(x => x.Id == concurrencyAllocation.Id);
		if (!session.Check("UAT-02 他端末で再確定対象を取得", concurrentConfirmRow != null,
			new { concurrencyAllocation.Id, rows = confirm.Vm.Rows.Count })) return;
		concurrentConfirmRow!.IsChecked = true;
		await confirm.RunAsync("UAT-02:他端末で出荷再確定", vm => vm.ConfirmSelectedCommand);
		var concurrentUpdated = (await session.QueryAsync<TranHaibun>("where Id=@0", concurrencyAllocation.Id.ToString())).Single();
		var stockAfterConcurrentUpdate = (await session.QueryAsync<SummaryRealStock>("where Id_Soko=@0 AND Id_Shohin=@1 AND Id_Col=@2 AND Id_Siz=@3",
			seeded.WarehouseId.ToString(), seeded.ShohinId.ToString(), seeded.Id_Col.ToString(), seeded.Id_Siz.ToString())).Single();
		if (!session.Check("UAT-02 他端末の取消・再確定でVduのみ進む", concurrentUpdated.Vdu != staleVdu
			&& !string.IsNullOrEmpty(concurrentUpdated.KakuteiDay) && concurrentUpdated.EndFlag == 0
			&& stockAfterConcurrentUpdate.Su == stockBeforeConflict.Su
			&& stockAfterConcurrentUpdate.ReserveQty == stockBeforeConflict.ReserveQty,
			new { staleVdu, concurrentUpdated.Vdu, concurrentUpdated.KakuteiDay, concurrentUpdated.EndFlag,
				before = new { stockBeforeConflict.Su, stockBeforeConflict.ReserveQty },
				after = new { stockAfterConcurrentUpdate.Su, stockAfterConcurrentUpdate.ReserveQty } })) return;

		staleShippingRow.JitsuSu = ConcurrencyQuantity;
		staleShippingRow.IsChecked = true;
		await shipping.RunAsync("UAT-02:古いVduで出荷実行", vm => vm.ExecuteCommand);
		var rejectedConflict = (await session.QueryAsync<TranHaibun>("where Id=@0", concurrencyAllocation.Id.ToString())).Single();
		var salesAfterConflict = await session.QueryAsync<Tran00Uriage>("where RelateNo1=@0", orderId.ToString());
		var stockAfterConflict = (await session.QueryAsync<SummaryRealStock>("where Id_Soko=@0 AND Id_Shohin=@1 AND Id_Col=@2 AND Id_Siz=@3",
			seeded.WarehouseId.ToString(), seeded.ShohinId.ToString(), seeded.Id_Col.ToString(), seeded.Id_Siz.ToString())).Single();
		if (!session.Check("UAT-02 競合時は出荷せず一覧を破棄", shipping.Vm.Rows.Count == 0 && shipping.Vm.CheckedCount == 0
			&& shipping.Vm.Message.Contains("他端末で更新", StringComparison.Ordinal),
			new { rows = shipping.Vm.Rows.Count, shipping.Vm.CheckedCount, shipping.Vm.Message })) return;
		if (!session.Check("UAT-02 競合時は配分・売上・在庫・引当を更新しない", rejectedConflict is { EndFlag: 0, RelateNo2: 0, JitsuSu: 0, ShortSu: 0 }
			&& rejectedConflict.Vdu == concurrentUpdated.Vdu
			&& salesAfterConflict.Count == salesBeforeConflict.Count
			&& stockAfterConflict.Su == stockBeforeConflict.Su && stockAfterConflict.ReserveQty == stockBeforeConflict.ReserveQty,
			new { rejectedConflict.EndFlag, rejectedConflict.RelateNo2, rejectedConflict.JitsuSu, rejectedConflict.ShortSu,
				rejectedConflict.Vdu, salesBefore = salesBeforeConflict.Count, salesAfter = salesAfterConflict.Count,
				stockBefore = new { stockBeforeConflict.Su, stockBeforeConflict.ReserveQty },
				stockAfter = new { stockAfterConflict.Su, stockAfterConflict.ReserveQty } })) return;

		await shipping.RunAsync("UAT-02:競合後の出荷再検索", vm => vm.SearchCommand);
		var reloadedShippingRow = shipping.Vm.Rows.SingleOrDefault(x => x.Id == concurrencyAllocation.Id);
		if (!session.Check("UAT-02 再検索で最新Vduを読込", reloadedShippingRow?.Vdu == concurrentUpdated.Vdu,
			new { expected = concurrentUpdated.Vdu, actual = reloadedShippingRow?.Vdu, rows = shipping.Vm.Rows.Count })) return;
		reloadedShippingRow!.JitsuSu = ConcurrencyQuantity;
		reloadedShippingRow.IsChecked = true;
		await shipping.RunAsync("UAT-02:最新Vduで出荷再実行", vm => vm.ExecuteCommand);

		var shippedAfterReload = (await session.QueryAsync<TranHaibun>("where Id=@0", concurrencyAllocation.Id.ToString())).Single();
		var salesAfterReload = await session.QueryAsync<Tran00Uriage>("where RelateNo1=@0", orderId.ToString());
		var stockAfterReload = (await session.QueryAsync<SummaryRealStock>("where Id_Soko=@0 AND Id_Shohin=@1 AND Id_Col=@2 AND Id_Siz=@3",
			seeded.WarehouseId.ToString(), seeded.ShohinId.ToString(), seeded.Id_Col.ToString(), seeded.Id_Siz.ToString())).Single();
		if (!session.Check("UAT-02 最新Vduで1点を出荷", shippedAfterReload is { EndFlag: 1, JitsuSu: ConcurrencyQuantity, ShortSu: 0 }
			&& shippedAfterReload.RelateNo2 > 0 && salesAfterReload.Count == salesBeforeConflict.Count + 1
			&& salesAfterReload.Sum(x => x.Jmeisai?.Sum(m => m.Su) ?? 0) == ShippingQuantity + ConcurrencyQuantity,
			new { shippedAfterReload.EndFlag, shippedAfterReload.JitsuSu, shippedAfterReload.ShortSu, shippedAfterReload.RelateNo2,
				salesBefore = salesBeforeConflict.Count, salesAfter = salesAfterReload.Count })) return;
		if (!session.Check("UAT-02 再実行後は在庫1・引当0", stockAfterReload.Su == stockBeforeConflict.Su - ConcurrencyQuantity
			&& stockAfterReload.ReserveQty == 0,
			new { before = new { stockBeforeConflict.Su, stockBeforeConflict.ReserveQty }, after = new { stockAfterReload.Su, stockAfterReload.ReserveQty } })) return;

		allocated.Input("UAT-02:競合再実行後の受注残再検索", vm => vm.SelectedTabIndex = 0, new { orderId });
		await allocated.RunAsync("UAT-02:競合再実行後の受注残再検索", vm => vm.DoSearchCommand);
		var afterConcurrencyOrder = allocated.Vm.SearchRows.SingleOrDefault(x => x.Id == orderId);
		if (!session.Check("UAT-02 競合再実行後の受注残は3", afterConcurrencyOrder is { ShukkaSu: ShippingQuantity + ConcurrencyQuantity, HaibunSu: 0 }
			&& afterConcurrencyOrder.ZanSu == OrderQuantity - ShippingQuantity - ConcurrencyQuantity,
			new { afterConcurrencyOrder?.ShukkaSu, afterConcurrencyOrder?.HaibunSu, afterConcurrencyOrder?.ZanSu })) return;

		const int TransferQuantity = 1;
		order.Input("UAT-02:移動伝票用受注入力", vm => {
			vm.SelectedTabIndex = 1;
			vm.CurrentEdit = new Tran12Jyuchu {
				DenDay = DenDay.Replace("/", string.Empty),
				NouhinDay = DenDay.Replace("/", string.Empty),
				Id_Tokui = seeded.DirectStoreId,
				VTokui = new CodeNameView(seeded.DirectStoreId, seeded.DirectStoreCode, "UAT-VM 受注出荷直営店"),
				Id_Soko = seeded.WarehouseId,
				VSoko = new CodeNameView(seeded.WarehouseId, seeded.WarehouseCode, "UAT-VM 受注出荷倉庫"),
				Id_Shain = seeded.EmployeeId,
				VShain = new CodeNameView(seeded.EmployeeId, seeded.EmployeeCode, string.Empty),
				Kubun = (int)EnumJuchu.Juchu,
				Rate = 100,
				Jmeisai = [new Tran99Meisai {
					No = 1, Id_Shohin = seeded.ShohinId, Id_Col = seeded.Id_Col, Id_Siz = seeded.Id_Siz,
					JanCode = JuchuShippingSeeder.JanCode, Su = TransferQuantity, Tanka = 2000,
					Kingaku = TransferQuantity * 2000, Jodai = 2000, Gedai = 1000, Id_Tax = 1,
				}],
			};
		}, new { seeded.DirectStoreCode, seeded.WarehouseCode, TransferQuantity });
		await order.RunAsync("UAT-02:移動伝票用受注登録", vm => vm.DoInsertOnDetailTabCommand);
		var transferOrderId = order.Vm.CurrentEdit.Id;
		if (!session.Check("UAT-02 直営店向け受注が採番される", transferOrderId > 0, new { transferOrderId })) return;

		allocated.Input("UAT-02:移動伝票用配分検索条件", vm => {
			vm.SelectedTabIndex = 0;
			vm.JuchuDayFrom = DateTime.Parse(DenDay);
			vm.JuchuDayTo = DateTime.Parse(DenDay);
			vm.CondTokuiDisplay = seeded.DirectStoreCode;
		}, new { DenDay, seeded.DirectStoreCode });
		await allocated.RunAsync("UAT-02:移動伝票用配分検索", vm => vm.DoSearchCommand);
		allocated.Vm.SelectedSearchRow = allocated.Vm.SearchRows.SingleOrDefault(x => x.Id == transferOrderId);
		if (!session.Check("UAT-02 直営店向け受注が配分一覧に出る", allocated.Vm.SelectedSearchRow != null, new { transferOrderId })) return;
		await allocated.RunAsync("UAT-02:移動伝票用配分入力へ", vm => vm.GoToEditCommand);
		allocated.Input("UAT-02:移動伝票用配分", vm => {
			vm.ShijiDay = DateTime.Parse(DenDay);
			vm.NouhinDay = DateTime.Parse(DenDay);
			vm.MeisaiRows.Single().Su = TransferQuantity;
		}, new { TransferQuantity });
		await allocated.RunAsync("UAT-02:移動伝票用配分登録", vm => vm.DoRegisterCommand);
		var transferAllocation = (await session.QueryAsync<TranHaibun>("where RelateNo1=@0", transferOrderId.ToString())).Single();
		if (!session.Check("UAT-02 直営店向け配分を登録", transferAllocation.Su == TransferQuantity && transferAllocation.Id_Tenpo == seeded.DirectStoreId,
			new { transferAllocation.Id, transferAllocation.Su, transferAllocation.Id_Tenpo, seeded.DirectStoreId })) return;

		confirm.Input("UAT-02:移動伝票用確定検索条件", vm => {
			vm.DenDayFromText = DenDay;
			vm.DenDayToText = DenDay;
			vm.KakuteiDayText = DenDay;
			vm.SokoCode = seeded.WarehouseCode;
			vm.MaxCountText = "500";
		}, new { DenDay, seeded.WarehouseCode });
		await confirm.RunAsync("UAT-02:移動伝票用出荷確定検索", vm => vm.SearchCommand);
		confirmRow = confirm.Vm.Rows.SingleOrDefault(x => x.Id == transferAllocation.Id);
		if (!session.Check("UAT-02 直営店向け配分が確定一覧に出る", confirmRow != null, new { transferAllocation.Id, rows = confirm.Vm.Rows.Count })) return;
		confirmRow!.IsChecked = true;
		await confirm.RunAsync("UAT-02:移動伝票用出荷確定", vm => vm.ConfirmSelectedCommand);

		shipping.Input("UAT-02:移動伝票用出荷検索条件", vm => {
			vm.KakuteiFromText = DenDay;
			vm.KakuteiToText = DenDay;
			vm.DenDayText = DenDay;
			vm.SokoCode = seeded.WarehouseCode;
			vm.MaxCountText = "500";
			SetShippingEmployee(vm, seeded.EmployeeId, seeded.EmployeeCode);
		}, new { DenDay, seeded.WarehouseCode, seeded.EmployeeCode });
		await shipping.RunAsync("UAT-02:移動伝票用出荷検索", vm => vm.SearchCommand);
		var transferShippingRow = shipping.Vm.Rows.SingleOrDefault(x => x.Id == transferAllocation.Id);
		if (!session.Check("UAT-02 直営店向け配分が出荷一覧に出る", transferShippingRow != null,
			new { transferAllocation.Id, rows = shipping.Vm.Rows.Count, shipping.Vm.Message })) return;
		transferShippingRow!.JitsuSu = TransferQuantity;
		transferShippingRow.IsChecked = true;
		await shipping.RunAsync("UAT-02:移動伝票用出荷実行", vm => vm.ExecuteCommand);

		var transferCompleted = (await session.QueryAsync<TranHaibun>("where Id=@0", transferAllocation.Id.ToString())).Single();
		var idoOut = (await session.QueryAsync<Tran10IdoOut>("where Id=@0", transferCompleted.RelateNo2.ToString())).SingleOrDefault();
		var transferSales = await session.QueryAsync<Tran00Uriage>("where RelateNo1=@0", transferOrderId.ToString());
		var stockAfterTransfer = (await session.QueryAsync<SummaryRealStock>("where Id_Soko=@0 AND Id_Shohin=@1 AND Id_Col=@2 AND Id_Siz=@3",
			seeded.WarehouseId.ToString(), seeded.ShohinId.ToString(), seeded.Id_Col.ToString(), seeded.Id_Siz.ToString())).Single();
		if (!session.Check("UAT-02 直営店出荷は移動伝票へ紐付き完了・引当解除", transferCompleted is { EndFlag: 1, JitsuSu: TransferQuantity, ShortSu: 0 }
			&& transferCompleted.RelateNo2 > 0 && stockAfterTransfer.ReserveQty == 0,
			new { transferCompleted.EndFlag, transferCompleted.JitsuSu, transferCompleted.ShortSu, transferCompleted.RelateNo2, stockAfterTransfer.ReserveQty })) return;
		if (!session.Check("UAT-02 移動出庫は出庫元・直営店・数量を保持", idoOut is { Id_Soko: var source, Id_Ido: var destination, SuTotal: TransferQuantity }
			&& source == seeded.WarehouseId && destination == seeded.DirectStoreId && idoOut.Jmeisai?.SingleOrDefault()?.Su == TransferQuantity,
			new { idoOut?.Id, idoOut?.Id_Soko, idoOut?.Id_Ido, idoOut?.SuTotal, su = idoOut?.Jmeisai?.SingleOrDefault()?.Su })) return;
		if (!session.Check("UAT-02 直営店出荷は売上伝票を作成しない", transferSales.Count == 0, new { transferOrderId, sales = transferSales.Count })) return;
		if (!session.Check("UAT-02 移動出庫で出庫元在庫0", stockAfterTransfer.Su == 0, new { stockAfterTransfer.Su, stockAfterTransfer.ReserveQty })) return;

		allocated.Input("UAT-02:移動伝票後の受注残再検索", vm => {
			vm.SelectedTabIndex = 0;
			vm.CondTokuiDisplay = seeded.DirectStoreCode;
		}, new { transferOrderId });
		await allocated.RunAsync("UAT-02:移動伝票後の受注残再検索", vm => vm.DoSearchCommand);
		var afterTransferOrder = allocated.Vm.SearchRows.SingleOrDefault(x => x.Id == transferOrderId);
		session.Check("UAT-02 移動伝票では受注残を消化しない", afterTransferOrder is { ShukkaSu: 0, HaibunSu: 0, ZanSu: TransferQuantity },
			new { afterTransferOrder?.ShukkaSu, afterTransferOrder?.HaibunSu, afterTransferOrder?.ZanSu });
		session.SetDialogResponder(null);
	}

	private static void SetShippingEmployee(ShippingInputViewModel viewModel, long id, string code) {
		var field = typeof(ShippingInputViewModel).GetField("IdShain", BindingFlags.Instance | BindingFlags.NonPublic)
			?? throw new InvalidOperationException("出荷入力社員の内部フィールドを取得できません。");
		field.SetValue(viewModel, id);
		viewModel.ShainText = code;
	}
}
