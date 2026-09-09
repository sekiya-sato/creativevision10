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
	private const int Quantity = 6;
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
					JanCode = JuchuShippingSeeder.JanCode, Su = Quantity, Tanka = 2000,
					Kingaku = Quantity * 2000, Jodai = 2000, Gedai = 1000, Id_Tax = 1,
				}],
			};
		}, new { seeded.TokuiCode, seeded.WarehouseCode, seeded.ShohinCode, Quantity });
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
		await allocated.RunAsync("UAT-02:配分登録", vm => vm.DoRegisterCommand);

		var beforeConfirm = (await session.QueryAsync<TranHaibun>("where RelateNo1=@0", orderId.ToString())).Single();
		if (!session.Check("UAT-02 配分は受注へ紐付く", beforeConfirm.Su == Quantity && beforeConfirm.EndFlag == 0 && string.IsNullOrEmpty(beforeConfirm.KakuteiDay),
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
		await confirm.RunAsync("UAT-02:出荷確定", vm => vm.ConfirmSelectedCommand);
		var confirmed = (await session.QueryAsync<TranHaibun>("where Id=@0", beforeConfirm.Id.ToString())).Single();
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
		var shippingRow = shipping.Vm.Rows.SingleOrDefault(x => x.Id == beforeConfirm.Id);
		if (!session.Check("UAT-02 確定配分が出荷一覧に出る", shippingRow != null, new { beforeConfirm.Id, rows = shipping.Vm.Rows.Count, shipping.Vm.Message })) return;
		shippingRow!.IsChecked = true;
		await shipping.RunAsync("UAT-02:出荷実行", vm => vm.ExecuteCommand);

		var shipped = (await session.QueryAsync<TranHaibun>("where Id=@0", beforeConfirm.Id.ToString())).Single();
		var sales = await session.QueryAsync<Tran00Uriage>("where RelateNo1=@0", orderId.ToString());
		var stock = (await session.QueryAsync<SummaryRealStock>("where Id_Soko=@0 AND Id_Shohin=@1 AND Id_Col=@2 AND Id_Siz=@3",
			seeded.WarehouseId.ToString(), seeded.ShohinId.ToString(), seeded.Id_Col.ToString(), seeded.Id_Siz.ToString())).Single();
		session.Check("UAT-02 出荷で配分を完了・引当解除", shipped.EndFlag == 1 && shipped.JitsuSu == Quantity && shipped.RelateNo2 > 0,
			new { shipped.EndFlag, shipped.JitsuSu, shipped.RelateNo2 });
		session.Check("UAT-02 出荷売上が受注へ紐付く", sales.Count == 1 && sales[0].RelateNo1 == orderId, new { count = sales.Count, orderId });
		session.Check("UAT-02 在庫と引当が出荷後の値になる", stock.Su == seeded.InitialStock - Quantity && stock.ReserveQty == 0,
			new { stock.Su, stock.ReserveQty, seeded.InitialStock, Quantity });
		session.SetDialogResponder(null);
	}

	private static void SetShippingEmployee(ShippingInputViewModel viewModel, long id, string code) {
		var field = typeof(ShippingInputViewModel).GetField("IdShain", BindingFlags.Instance | BindingFlags.NonPublic)
			?? throw new InvalidOperationException("出荷入力社員の内部フィールドを取得できません。");
		field.SetValue(viewModel, id);
		viewModel.ShainText = code;
	}
}
