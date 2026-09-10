using System.Collections.ObjectModel;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.ViewModels._00System;
using CvWpfclient.ViewModels._01Master;
using CvWpfclient.ViewModels._03Hatchu;
using CvWpfclient.ViewModels._05Shiire;
using CvWpfclient.Views._00System;
using CvWpfclient.Views._01Master;
using CvWpfclient.Views._03Hatchu;
using CvWpfclient.Views._05Shiire;
using UatVm.Seed;

namespace UatVm.Scenarios;

/// <summary>UAT-01の新規商品登録から発注・仕入までを実ViewModelで検証する。</summary>
public static class Uat01ScreenScenario {
	const string DenDayOrder = "20260901";
	const string DenDayReceipt1 = "20260905";
	const string DenDayReceipt2 = "20260910";
	const int OrderQuantity = 10;
	const int Receipt1Quantity = 4;
	const int Receipt2Quantity = 6;
	const int UnitPrice = 1000;
	static Uat01ScreenSeeder.Result? seeded;

	public static void Seeder(string dbPath) =>
		seeded = Uat01ScreenSeeder.Seed(dbPath, message => Console.WriteLine($"[seed] {message}"));

	public static async Task RunAsync(VmSession session) {
		var data = seeded ?? throw new InvalidOperationException("シードが実行されていません。");
		session.SetDialogResponder(request => request.Button is System.Windows.MessageBoxButton.YesNo or System.Windows.MessageBoxButton.YesNoCancel
			? System.Windows.MessageBoxResult.Yes : System.Windows.MessageBoxResult.OK);

		var productInput = session.OpenView<MasterShohinMenteView, MasterShohinMenteViewModel>();
		productInput.Input("UAT-01:商品登録", vm => {
			vm.CurrentEdit = new MasterShohin {
				Code = Uat01ScreenSeeder.ShohinCode, Name = "UAT-VM UAT-01新規商品", Ryaku = "UAT01新規商品",
				Id_Soko = data.WarehouseId, VSoko = new CodeNameView(data.WarehouseId, data.WarehouseCode, "UAT-VM UAT-01倉庫"),
				Id_Tax = 1, IsZaiko = 1, TankaJodaiOrg = 2000, TankaJodai = 2000, TankaGenka = UnitPrice, TankaShiire = UnitPrice,
			};
			vm.EditJcolsiz = new ObservableCollection<MasterShohinColSiz>([
				new() { Id_Col = data.ColorId, Code_Col = data.ColorCode, Mei_Col = data.ColorName,
					Id_Siz = data.SizeId, Code_Siz = data.SizeCode, Mei_Siz = data.SizeName, Jan1 = Uat01ScreenSeeder.JanCode },
			]);
		}, new { Uat01ScreenSeeder.ShohinCode, Uat01ScreenSeeder.JanCode, data.WarehouseCode });
		await productInput.RunAsync("UAT-01:商品登録", vm => vm.DoInsertCommand);
		var product = (await session.QueryAsync<MasterShohin>("where Code=@0", Uat01ScreenSeeder.ShohinCode)).SingleOrDefault();
		if (!session.Check("UAT-01 商品マスタが画面経由で登録される", product is { Id: > 0, IsZaiko: 1, Id_Tax: 1 },
			new { product?.Id, product?.Code, product?.IsZaiko, product?.Id_Tax })) return;
		var sku = (await session.QueryAsync<DerivedShohinColSiz>("where Id_Shohin=@0", product!.Id.ToString())).SingleOrDefault();
		if (!session.Check("UAT-01 商品登録で色サイズJANの派生SKUが作られる", sku is { Id_Shohin: > 0 } && sku.Jan1 == Uat01ScreenSeeder.JanCode
			&& sku.Id_Col == data.ColorId && sku.Id_Siz == data.SizeId,
			new { sku?.Id, sku?.Id_Shohin, sku?.Id_Col, sku?.Id_Siz, sku?.Jan1 })) return;

		var hachu = session.OpenView<HachuInputView, HachuInputViewModel>();
		hachu.Input("UAT-01:発注10", vm => vm.CurrentEdit = CreateHachu(data, product, sku!, OrderQuantity),
			new { data.SupplierCode, data.WarehouseCode, Uat01ScreenSeeder.ShohinCode, OrderQuantity });
		await hachu.RunAsync("UAT-01:発注登録", vm => vm.DoInsertOnDetailTabCommand);
		var orderId = hachu.Vm.CurrentEdit.Id;
		if (!session.Check("UAT-01 発注10が画面経由で登録される", orderId > 0, new { orderId })) return;

		var shiire = session.OpenView<ShiireInputView, ShiireInputViewModel>();
		await InsertReceiptAsync(session, shiire, data, product, sku!, orderId, DenDayReceipt1, Receipt1Quantity, "仕入4");
		await VerifyOrderAndStockAsync(session, data, product, sku!, orderId, Receipt1Quantity, OrderQuantity - Receipt1Quantity, 0, "仕入4後");
		await InsertReceiptAsync(session, shiire, data, product, sku!, orderId, DenDayReceipt2, Receipt2Quantity, "仕入6");
		await VerifyOrderAndStockAsync(session, data, product, sku!, orderId, OrderQuantity, 0, 1, "仕入6後");

		var rebuild = session.OpenView<StockKakeUpdateView, StockKakeUpdateViewModel>();
		await rebuild.RunAsync("UAT-01:買掛再集計初期化", vm => vm.InitCommand);
		rebuild.Input("UAT-01:買掛再集計条件", vm => {
			vm.YearMonthFrom = "2026/09";
			vm.YearMonthTo = "2026/09";
			vm.UpdateTarget = "買掛のみ";
		}, new { Month = "2026/09" });
		await rebuild.RunAsync("UAT-01:買掛再集計", vm => vm.ExecuteCommand);
		session.Check("UAT-01 買掛再集計が画面経由で完了", rebuild.Vm.ProgressValue == 100 && rebuild.Vm.StatusMessage.Contains("完了", StringComparison.Ordinal),
			new { rebuild.Vm.ProgressValue, rebuild.Vm.StatusMessage });
		var payable = (await session.QueryAsync<SummaryKaiKake>("where Id_Shiire=@0 AND DenMonth=@1", data.SupplierId.ToString(), Uat01ScreenSeeder.ScenarioMonth)).SingleOrDefault();
		session.Check("UAT-01 買掛が仕入10,000・税1,000・残11,000になる", payable is { Shiire: 10000, TotalShiire: 11000, TotalOut: 0, Balance: 11000 }
			&& payable.Tax1 + payable.Tax2 + payable.Tax3 == 1000,
			new { payable?.Shiire, Tax = payable == null ? 0 : payable.Tax1 + payable.Tax2 + payable.Tax3, payable?.TotalShiire, payable?.TotalOut, payable?.Balance });
		session.SetDialogResponder(null);
	}

	static Tran13Hachu CreateHachu(Uat01ScreenSeeder.Result data, MasterShohin product, DerivedShohinColSiz sku, int quantity) => new() {
		DenDay = DenDayOrder, NouhinDay = DenDayOrder, Id_Shiire = data.SupplierId,
		VShiire = new CodeNameView(data.SupplierId, data.SupplierCode, "UAT-VM UAT-01仕入先"),
		Id_Soko = data.WarehouseId, VSoko = new CodeNameView(data.WarehouseId, data.WarehouseCode, "UAT-VM UAT-01倉庫"),
		Id_Shain = data.EmployeeId, VShain = new CodeNameView(data.EmployeeId, data.EmployeeCode, string.Empty),
		Kubun = (int)EnumHachu.Hachu, Rate = 100, Jmeisai = [Meisai(product, sku, quantity)],
	};

	static Tran03Shiire CreateShiire(Uat01ScreenSeeder.Result data, MasterShohin product, DerivedShohinColSiz sku, long orderId, string denDay, int quantity) => new() {
		DenDay = denDay, KakeDay = denDay, Id_Shiire = data.SupplierId,
		VShiire = new CodeNameView(data.SupplierId, data.SupplierCode, "UAT-VM UAT-01仕入先"),
		Id_Soko = data.WarehouseId, VSoko = new CodeNameView(data.WarehouseId, data.WarehouseCode, "UAT-VM UAT-01倉庫"),
		Id_Shain = data.EmployeeId, VShain = new CodeNameView(data.EmployeeId, data.EmployeeCode, string.Empty),
		Kubun = (int)EnumShiire.Shiire, IsPay = 1, Rate = 100, TaxCalcUnit = (int)EnumTaxCalcUnit.Slip, RelateNo1 = checked((int)orderId),
		Jmeisai = [Meisai(product, sku, quantity)],
	};

	static Tran99Meisai Meisai(MasterShohin product, DerivedShohinColSiz sku, int quantity) => new() {
		No = 1, Id_Shohin = product.Id, Code_Shohin = product.Code, Mei_Shohin = product.Name,
		Id_Col = sku.Id_Col, Code_Col = sku.Code_Col, Mei_Col = sku.Mei_Col,
		Id_Siz = sku.Id_Siz, Code_Siz = sku.Code_Siz, Mei_Siz = sku.Mei_Siz, JanCode = sku.Jan1,
		Su = quantity, Tanka = UnitPrice, Kingaku = quantity * UnitPrice, Jodai = 2000, Gedai = UnitPrice, Id_Tax = product.Id_Tax,
	};

	static async Task InsertReceiptAsync(VmSession session, ViewDriver<ShiireInputViewModel> driver, Uat01ScreenSeeder.Result data,
		MasterShohin product, DerivedShohinColSiz sku, long orderId, string denDay, int quantity, string label) {
		driver.Input($"UAT-01:{label}", vm => vm.CurrentEdit = CreateShiire(data, product, sku, orderId, denDay, quantity), new { denDay, quantity, orderId });
		await driver.RunAsync($"UAT-01:{label}登録", vm => vm.DoInsertOnDetailTabCommand);
		session.Check($"UAT-01 {label}伝票が画面経由で採番される", driver.Vm.CurrentEdit.Id > 0, new { driver.Vm.CurrentEdit.Id });
	}

	static async Task VerifyOrderAndStockAsync(VmSession session, Uat01ScreenSeeder.Result data, MasterShohin product, DerivedShohinColSiz sku,
		long orderId, int received, int remaining, int endFlag, string label) {
		var order = (await session.QueryAsync<Tran13Hachu>("where Id=@0", orderId.ToString())).SingleOrDefault();
		var receipts = await session.QueryAsync<Tran03Shiire>("where RelateNo1=@0", orderId.ToString());
		var receivedQty = receipts.SelectMany(x => x.Jmeisai ?? []).Where(x => x.Id_Shohin == product.Id && x.Id_Col == sku.Id_Col && x.Id_Siz == sku.Id_Siz).Sum(x => x.Su);
		var stock = (await session.QueryAsync<SummaryRealStock>("where Id_Soko=@0 AND Id_Shohin=@1 AND Id_Col=@2 AND Id_Siz=@3",
			data.WarehouseId.ToString(), product.Id.ToString(), sku.Id_Col.ToString(), sku.Id_Siz.ToString())).SingleOrDefault();
		session.Check($"UAT-01 {label} 発注残{remaining}・完了{endFlag}", order is { } && order.EndFlag == endFlag && receivedQty == received && OrderQuantity - receivedQty == remaining,
			new { order?.EndFlag, receivedQty, remaining });
		session.Check($"UAT-01 {label} 在庫が{received}", stock?.Su == received && stock.ReserveQty == 0,
			new { stock?.Su, stock?.ReserveQty, received });
	}
}
