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
		session.Check("UAT-02 出荷後の受注残は4", afterOrder is { ShukkaSu: ShippingQuantity, HaibunSu: 0 }
			&& afterOrder.ZanSu == OrderQuantity - ShippingQuantity,
			new { afterOrder?.ShukkaSu, afterOrder?.HaibunSu, afterOrder?.ZanSu });
		session.SetDialogResponder(null);
	}

	private static void SetShippingEmployee(ShippingInputViewModel viewModel, long id, string code) {
		var field = typeof(ShippingInputViewModel).GetField("IdShain", BindingFlags.Instance | BindingFlags.NonPublic)
			?? throw new InvalidOperationException("出荷入力社員の内部フィールドを取得できません。");
		field.SetValue(viewModel, id);
		viewModel.ShainText = code;
	}
}
