using System.IO;
using System.Windows;
using CvAsset;
using CvBase;
using CvWpfclient.ViewModels._03Hatchu;
using CvWpfclient.ViewModels._07Haibun;
using CvWpfclient.Views._03Hatchu;
using CvWpfclient.Views._07Haibun;
using UatVm.Seed;

namespace UatVm.Scenarios;

/// <summary>
/// 配分再設計 Step 4 の仕入配分入力と入荷割当を実View/ViewModelで検証する。
/// <para>
/// UAT-02 のシード（倉庫・卸先TK・直営店TS）に発注10点（納品予定9/10）と前回配分（完了済み TK6・TS4）を置き、
/// 仕入配分入力(商品別)で前回配分比率 6:4 に按分して登録 → 仕入5 → 店舗コード順にTKへ入荷済み5 →
/// 配分確定（TKは入荷済み5で確定、TSは入荷前で確定数0）→ 追加の仕入5 → TSへ入荷済み4 → 仕入返品2 → TSは3、を確認する。
/// 仕入配分入力(商品別)・配分確定(入荷済列)・仕入配分入力(伝票別)の画面をJPG保存し、表示崩れを自動判定する。
/// 仕様は `Doc/spec/2026-10-03_配分再設計_Step4_仕入配分入力_詳細設計.md`。
/// </para>
/// </summary>
public static class PurchaseReceiptAllocationScenario {
	const string ScreenDirectory = "..\\Doc\\test\\uat20261003\\haibun\\screens";
	const string DenDay = "2026/09/05";
	static JuchuShippingSeeder.Result? _seeded;

	public static void Seeder(string dbPath) =>
		_seeded = JuchuShippingSeeder.Seed(dbPath, message => Console.WriteLine($"[seed] {message}"));

	public static async Task RunAsync(VmSession session) {
		var seeded = _seeded ?? throw new InvalidOperationException("シードが実行されていません。");
		var screens = Path.Combine(Path.GetFullPath(ScreenDirectory), "receipt_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
		Directory.CreateDirectory(screens);
		session.SetDialogResponder(request => request.Button is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel
			? MessageBoxResult.Yes : MessageBoxResult.OK);

		var hachu = await session.InsertAsync(new Tran13Hachu {
			DenDay = "20260901", NouhinDay = "20260910", Id_Soko = seeded.WarehouseId,
			VSoko = new CodeNameView(seeded.WarehouseId, seeded.WarehouseCode, string.Empty),
			Id_Shain = seeded.EmployeeId, Kubun = 10, Rate = 100, SuTotal = 10, KingakuTotal = 10000,
			Jmeisai = [new Tran99Meisai { No = 1, Id_Shohin = seeded.ShohinId, Id_Col = seeded.Id_Col, Id_Siz = seeded.Id_Siz,
				JanCode = JuchuShippingSeeder.JanCode, Su = 10, Tanka = 1000, Kingaku = 10000, Jodai = 2000, Gedai = 1000, Id_Tax = 1 }],
		});
		await session.InsertAsync(Previous(seeded, seeded.TokuiId, 6));
		await session.InsertAsync(Previous(seeded, seeded.DirectStoreId, 4));
		session.Check("発注10点を登録", hachu.Id > 0, new { hachu.Id });

		// 仕入配分入力(商品別)
		var screen = session.OpenView<PurchaseReceiptAllocationInputView, PurchaseReceiptAllocationInputViewModel>();
		screen.Input("仕入配分:検索条件", vm => {
			vm.SokoCode = seeded.WarehouseCode;
			vm.ShohinCode = seeded.ShohinCode;
			vm.ShijiDay = DateTime.Parse(DenDay);
			vm.NouhinDay = DateTime.Parse(DenDay);
		});
		await screen.RunAsync("仕入配分:検索", vm => vm.DoSearchCommand);
		var sku = screen.Vm.SkuColumns.SingleOrDefault();
		if (!session.Check("仕入配分:発注1件・発注10・入荷0・可能10", screen.Vm.OrderCount == 1 && sku is { HachuSu: 10, NyukaSu: 0, KanoSu: 10 },
			new { screen.Vm.OrderCount, sku?.HachuSu, sku?.NyukaSu, sku?.KanoSu, screen.Vm.Message })) return;
		await screen.RunAsync("仕入配分:前回の配分先を読込", vm => vm.LoadPreviousDestinationsCommand);
		await screen.RunAsync("仕入配分:比率を計算", vm => vm.CalcRatioCommand);
		screen.Run("仕入配分:按分実行", vm => vm.ApplyAllocationCommand);
		var tkRow = screen.Vm.Rows.SingleOrDefault(r => r.Id_Tenpo == seeded.TokuiId);
		var tsRow = screen.Vm.Rows.SingleOrDefault(r => r.Id_Tenpo == seeded.DirectStoreId);
		if (!session.Check("仕入配分:前回配分比率で TK6・TS4、残0", tkRow?.TotalSu == 6 && tsRow?.TotalSu == 4 && sku!.RemainSu == 0,
			new { tk = tkRow?.TotalSu, ts = tsRow?.TotalSu, sku!.RemainSu })) return;
		await HaibunScreenScenario.CaptureAsync(session, screen.View, screens, "31_PurchaseReceiptAllocation");
		await screen.RunAsync("仕入配分:登録", vm => vm.DoRegisterCommand);

		var rows = await Allocations(session, hachu.Id);
		var tk = rows.SingleOrDefault(h => h.Id_Tenpo == seeded.TokuiId);
		var ts = rows.SingleOrDefault(h => h.Id_Tenpo == seeded.DirectStoreId);
		if (!session.Check("仕入配分:発注に紐付く区分0でTK6・TS4、入荷前は入荷済み0", tk is { Su: 6, ArrivedSu: 0 } && ts is { Su: 4, ArrivedSu: 0 },
			new { rows = rows.Select(h => new { h.Id_Tenpo, h.Su, h.ArrivedSu, h.RelateNo1 }) })) return;

		// 仕入5 → 店舗コード順（TK→TS）でTKへ入荷済み5
		await session.InsertAsync(Receipt(seeded, hachu.Id, 5, EnumShiire.Shiire));
		rows = await Allocations(session, hachu.Id);
		session.Check("入荷割当:仕入5でTK5・TS0", Arrived(rows, seeded.TokuiId) == 5 && Arrived(rows, seeded.DirectStoreId) == 0,
			new { rows = rows.Select(h => new { h.Id_Tenpo, h.ArrivedSu }) });

		// 配分確定画面：入荷済列と確定数の初期値
		var confirm = session.OpenView<ShippingConfirmShohinView, ShippingConfirmShohinViewModel>();
		confirm.Input("配分確定:検索条件", vm => {
			vm.DenDayFromText = DenDay;
			vm.DenDayToText = DenDay;
			vm.KakuteiDayText = DenDay;
			vm.SokoCode = seeded.WarehouseCode;
			vm.MaxCountText = "500";
		});
		await confirm.RunAsync("配分確定:検索", vm => vm.SearchCommand);
		var tkConfirm = confirm.Vm.Rows.SingleOrDefault(r => r.Id == tk!.Id);
		var tsConfirm = confirm.Vm.Rows.SingleOrDefault(r => r.Id == ts!.Id);
		session.Check("配分確定:入荷済 TK5・TS0、確定数の初期値も同じ",
			tkConfirm is { ArrivedDisplay: "5", KakuteiSu: 5 } && tsConfirm is { ArrivedDisplay: "0", KakuteiSu: 0 },
			new { tk = new { tkConfirm?.ArrivedDisplay, tkConfirm?.KakuteiSu }, ts = new { tsConfirm?.ArrivedDisplay, tsConfirm?.KakuteiSu } });
		await HaibunScreenScenario.CaptureAsync(session, confirm.View, screens, "32_HaibunCommitArrived");
		tkConfirm!.IsChecked = true;
		await confirm.RunAsync("配分確定:TKを確定", vm => vm.ConfirmSelectedCommand);
		var tkDone = (await session.QueryAsync<TranHaibun>("where Id=@0", tk!.Id.ToString())).Single();
		var uriage = await session.QueryAsync<Tran00Uriage>("where Id_Tokui=@0 AND Memo='配分出荷'", seeded.TokuiId.ToString());
		session.Check("配分確定:TKは入荷済み5で出荷売上・欠品1", tkDone is { EndFlag: 1, JitsuSu: 5, ShortSu: 1 } && uriage.Count == 1 && uriage[0].SuTotal == 5,
			new { tkDone.EndFlag, tkDone.JitsuSu, tkDone.ShortSu, uriage = uriage.Count });

		// 追加の仕入5 → 入荷計10 − 確定5 = 5 → TSは上限4
		await session.InsertAsync(Receipt(seeded, hachu.Id, 5, EnumShiire.Shiire));
		rows = await Allocations(session, hachu.Id);
		session.Check("入荷割当:追加の仕入でTS4", Arrived(rows, seeded.DirectStoreId) == 4, new { ts = Arrived(rows, seeded.DirectStoreId) });
		// 仕入返品2 → 入荷計8 − 確定5 = 3
		await session.InsertAsync(Receipt(seeded, hachu.Id, 2, EnumShiire.Henpin));
		rows = await Allocations(session, hachu.Id);
		session.Check("入荷割当:仕入返品2でTS3", Arrived(rows, seeded.DirectStoreId) == 3, new { ts = Arrived(rows, seeded.DirectStoreId) });
		var stock = (await session.QueryAsync<SummaryRealStock>("where Id_Soko=@0 AND Id_Shohin=@1 AND Id_Col=@2 AND Id_Siz=@3",
			seeded.WarehouseId.ToString(), seeded.ShohinId.ToString(), seeded.Id_Col.ToString(), seeded.Id_Siz.ToString())).Single();
		session.Check("引当:TSの入荷済み3だけが引当", stock.ReserveQty == 3, new { stock.Su, stock.ReserveQty });

		// 仕入配分入力(伝票別)：入荷の見出し
		var byOrder = session.OpenView<HachuHaibunInputView, HachuHaibunInputViewModel>();
		byOrder.Input("伝票別:検索条件", vm => {
			vm.HachuDayFrom = new DateTime(2026, 9, 1);
			vm.HachuDayTo = new DateTime(2026, 9, 1);
		});
		await byOrder.RunAsync("伝票別:検索", vm => vm.DoSearchCommand);
		byOrder.Input("伝票別:発注を選択", vm => vm.SelectedSearchRow = vm.SearchRows.FirstOrDefault(r => r.Id == hachu.Id));
		await byOrder.RunAsync("伝票別:明細読込", vm => vm.GoToEditCommand);
		var nyuka = byOrder.Vm.SelectedShohin?.Skus.FirstOrDefault()?.NyukaSu;
		session.Check("伝票別:SKU見出しに入荷8", nyuka == 8, new { nyuka, byOrder.Vm.Message });
		await HaibunScreenScenario.CaptureAsync(session, byOrder.View, screens, "33_HachuHaibunInputArrived");

		session.SetDialogResponder(null);
	}

	static async Task<List<TranHaibun>> Allocations(VmSession session, long hachuId) =>
		await session.QueryAsync<TranHaibun>($"where Kubun=0 AND RelateNo1={hachuId}");

	static int Arrived(IEnumerable<TranHaibun> rows, long tenpo) => rows.Where(h => h.Id_Tenpo == tenpo && h.EndFlag == 0).Sum(h => h.ArrivedSu);

	static TranHaibun Previous(JuchuShippingSeeder.Result seeded, long idTenpo, int su) => new() {
		DenDay = "20260801", NouhinDay = "20260801", Id_Soko = seeded.WarehouseId, Id_Tenpo = idTenpo,
		Kubun = (int)EnumHaibun.Zaiko, Id_Shohin = seeded.ShohinId, Id_Col = seeded.Id_Col, Id_Siz = seeded.Id_Siz,
		JanCode = JuchuShippingSeeder.JanCode, Su = su, JitsuSu = su, KakuteiDay = "20260801", EndFlag = 1,
	};

	static Tran03Shiire Receipt(JuchuShippingSeeder.Result seeded, long hachuId, int su, EnumShiire kubun) {
		var receipt = new Tran03Shiire {
			DenDay = "20260905", KakeDay = "20260905", Id_Soko = seeded.WarehouseId, RelateNo1 = hachuId,
			Id_Shain = seeded.EmployeeId, Rate = 100, SuTotal = su, KingakuTotal = su * 1000,
			Jmeisai = [new Tran99Meisai { No = 1, Id_Shohin = seeded.ShohinId, Id_Col = seeded.Id_Col, Id_Siz = seeded.Id_Siz,
				JanCode = JuchuShippingSeeder.JanCode, Su = su, Tanka = 1000, Kingaku = su * 1000, Jodai = 2000, Gedai = 1000, Id_Tax = 1 }],
		};
		receipt.EnKubun = kubun;
		return receipt;
	}
}
