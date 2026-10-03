using System.IO;
using System.Windows;
using CvBase;
using CvWpfclient.ViewModels._07Haibun;
using CvWpfclient.Views._07Haibun;
using UatVm.Seed;

namespace UatVm.Scenarios;

/// <summary>
/// 配分再設計 Step 3 の在庫配分入力を実View/ViewModelで検証する。
/// <para>
/// UAT-02 と同じシード（倉庫在庫8・卸先TK・直営店TS、上代2000）に、前回配分（完了済み、TK1・TS3）を置き、
/// 前回の配分先読込 → 同数 → 前回配分比率で按分 → 登録 → 配分確定までを通す。
/// 卸先の掛率を60%にして、卸先の単価が 上代×掛率 になることと、確定で卸先＝出荷売上・直営店＝移動になることを確認する。
/// 画面はJPG保存と表示崩れの自動判定を行う。
/// 仕様は `Doc/spec/2026-10-03_配分再設計_Step3_在庫配分入力_詳細設計.md`。
/// </para>
/// </summary>
public static class InventoryAllocationScenario {
	const string ScreenDirectory = "..\\Doc\\test\\uat20261003\\haibun\\screens";
	const string DenDay = "2026/09/05";
	static JuchuShippingSeeder.Result? _seeded;

	public static void Seeder(string dbPath) =>
		_seeded = JuchuShippingSeeder.Seed(dbPath, message => Console.WriteLine($"[seed] {message}"));

	public static async Task RunAsync(VmSession session) {
		var seeded = _seeded ?? throw new InvalidOperationException("シードが実行されていません。");
		var screens = Path.Combine(Path.GetFullPath(ScreenDirectory), "stock_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
		Directory.CreateDirectory(screens);
		session.SetDialogResponder(request => request.Button is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel
			? MessageBoxResult.Yes : MessageBoxResult.OK);

		// 卸先の掛率60%
		var tk = (await session.QueryAsync<MasterTokui>("where Id=@0", seeded.TokuiId.ToString())).Single();
		tk.RateProper = 60;
		await session.UpdateAsync(tk);
		// 前回配分（完了済みなので引当には入らない）: TK 1 / TS 3
		await session.InsertAsync(PreviousAllocation(seeded, seeded.TokuiId, 1));
		await session.InsertAsync(PreviousAllocation(seeded, seeded.DirectStoreId, 3));

		var screen = session.OpenView<InventoryAllocationInputView, InventoryAllocationInputViewModel>();
		screen.Input("在庫配分:検索条件", vm => {
			vm.SokoCode = seeded.WarehouseCode;
			vm.ShohinCodeFrom = seeded.ShohinCode;
			vm.ShohinCodeTo = seeded.ShohinCode;
		});
		await screen.RunAsync("在庫配分:検索", vm => vm.DoSearchCommand);
		var listRow = screen.Vm.SearchRows.SingleOrDefault();
		if (!session.Check("在庫配分:商品1件・有効在庫8", listRow is { YukoSu: JuchuShippingSeeder.InitialStock }, new { rows = screen.Vm.SearchRows.Count, listRow?.YukoSu })) return;
		await HaibunScreenScenario.CaptureAsync(session, screen.View, screens, "21_InventoryAllocationList");

		await screen.RunAsync("在庫配分:配分入力へ", vm => vm.GoToEditCommand);
		await screen.RunAsync("在庫配分:前回の配分先を読込", vm => vm.LoadPreviousDestinationsCommand);
		var rows = screen.Vm.Rows;
		if (!session.Check("在庫配分:前回の配分先2件（卸先=出荷売上、直営店=移動）",
			rows.Count == 2 && rows.Any(r => r.Id_Tenpo == seeded.TokuiId && r.KindDisplay == "出荷売上")
			&& rows.Any(r => r.Id_Tenpo == seeded.DirectStoreId && r.KindDisplay == "移動"),
			new { rows = rows.Select(r => new { r.Id_Tenpo, r.KindDisplay }) })) return;
		var tkRow = rows.Single(r => r.Id_Tenpo == seeded.TokuiId);
		var tsRow = rows.Single(r => r.Id_Tenpo == seeded.DirectStoreId);

		screen.Input("在庫配分:同数5", vm => {
			vm.Mode = InventoryAllocationInputViewModel.ModeEqual;
			vm.SameQty = 5;
		});
		screen.Run("在庫配分:同数で按分", vm => vm.ApplyAllocationCommand);
		session.Check("在庫配分:同数は上から5・残り3", rows[0].TotalSu == 5 && rows[1].TotalSu == 3,
			new { first = rows[0].TotalSu, second = rows[1].TotalSu });

		screen.Input("在庫配分:前回配分の比率・四捨五入", vm => {
			vm.Mode = InventoryAllocationInputViewModel.ModeRatio;
			vm.Basis = InventoryAllocationInputViewModel.BasisPrevious;
			vm.Rounding = "四捨五入";
			vm.TotalMode = InventoryAllocationInputViewModel.TotalYuko;
		});
		await screen.RunAsync("在庫配分:比率を計算", vm => vm.CalcRatioCommand);
		session.Check("在庫配分:比率 TK25%・TS75%", tkRow.Ratio == 25m && tsRow.Ratio == 75m, new { tk = tkRow.Ratio, ts = tsRow.Ratio });
		screen.Run("在庫配分:比率で按分", vm => vm.ApplyAllocationCommand);
		session.Check("在庫配分:比率按分 TK2・TS6、配分後在庫0", tkRow.TotalSu == 2 && tsRow.TotalSu == 6 && screen.Vm.SkuColumns.Single().AfterSu == 0,
			new { tk = tkRow.TotalSu, ts = tsRow.TotalSu });
		screen.Input("在庫配分:指示日", vm => {
			vm.ShijiDay = DateTime.Parse(DenDay);
			vm.NouhinDay = DateTime.Parse(DenDay);
		});
		await HaibunScreenScenario.CaptureAsync(session, screen.View, screens, "22_InventoryAllocationMatrix");

		await screen.RunAsync("在庫配分:登録", vm => vm.DoRegisterCommand);
		var saved = await session.QueryAsync<TranHaibun>("where Kubun=1 AND EndFlag=0 AND Id_Soko=@0 AND Id_Shohin=@1",
			seeded.WarehouseId.ToString(), seeded.ShohinId.ToString());
		var tkSaved = saved.SingleOrDefault(h => h.Id_Tenpo == seeded.TokuiId);
		var tsSaved = saved.SingleOrDefault(h => h.Id_Tenpo == seeded.DirectStoreId);
		if (!session.Check("在庫配分:登録 TK2・TS6（区分1・受注に紐付かない）", saved.Count == 2 && tkSaved?.Su == 2 && tsSaved?.Su == 6
			&& saved.All(h => h.RelateNo1 == 0 && h.DenDay == "20260905"),
			new { rows = saved.Select(h => new { h.Id_Tenpo, h.Su, h.RelateNo1, h.Tanka, h.Jodai }) })) return;
		session.Check("在庫配分:卸先の単価は上代×掛率60%", tkSaved!.Tanka == tkSaved.Jodai * 60 / 100 && tkSaved.Jodai > 0 && tsSaved!.Tanka == tsSaved.Jodai,
			new { tk = new { tkSaved.Tanka, tkSaved.Jodai }, ts = new { tsSaved!.Tanka, tsSaved.Jodai } });

		// 配分確定で、卸先は出荷売上、直営店は移動出庫になる
		var confirm = session.OpenView<ShippingConfirmShohinView, ShippingConfirmShohinViewModel>();
		confirm.Input("配分確定:検索条件", vm => {
			vm.DenDayFromText = DenDay;
			vm.DenDayToText = DenDay;
			vm.KakuteiDayText = DenDay;
			vm.SokoCode = seeded.WarehouseCode;
			vm.MaxCountText = "500";
		});
		await confirm.RunAsync("配分確定:検索", vm => vm.SearchCommand);
		foreach (var r in confirm.Vm.Rows.Where(r => r.Id == tkSaved.Id || r.Id == tsSaved.Id)) r.IsChecked = true;
		await confirm.RunAsync("配分確定:確定", vm => vm.ConfirmSelectedCommand);
		var uriage = await session.QueryAsync<Tran00Uriage>("where Id_Tokui=@0 AND Memo='配分出荷'", seeded.TokuiId.ToString());
		var ido = await session.QueryAsync<Tran10IdoOut>("where Id_Ido=@0 AND Memo='配分出荷'", seeded.DirectStoreId.ToString());
		session.Check("配分確定:卸先は出荷売上2点・単価1200、直営店は移動6点",
			uriage.Count == 1 && uriage[0].SuTotal == 2 && uriage[0].Jmeisai?.Single().Tanka == tkSaved.Tanka
			&& ido.Count == 1 && ido[0].SuTotal == 6,
			new { uriage = uriage.Select(u => new { u.SuTotal, Tanka = u.Jmeisai?.FirstOrDefault()?.Tanka }), ido = ido.Select(i => i.SuTotal) });
		var stock = (await session.QueryAsync<SummaryRealStock>("where Id_Soko=@0 AND Id_Shohin=@1 AND Id_Col=@2 AND Id_Siz=@3",
			seeded.WarehouseId.ToString(), seeded.ShohinId.ToString(), seeded.Id_Col.ToString(), seeded.Id_Siz.ToString())).Single();
		session.Check("配分確定:倉庫在庫0・引当0", stock.Su == 0 && stock.ReserveQty == 0, new { stock.Su, stock.ReserveQty });

		session.SetDialogResponder(null);
	}

	static TranHaibun PreviousAllocation(JuchuShippingSeeder.Result seeded, long idTenpo, int su) => new() {
		DenDay = "20260801",
		NouhinDay = "20260801",
		Id_Soko = seeded.WarehouseId,
		Id_Tenpo = idTenpo,
		Kubun = (int)EnumHaibun.Zaiko,
		Id_Shohin = seeded.ShohinId,
		Id_Col = seeded.Id_Col,
		Id_Siz = seeded.Id_Siz,
		JanCode = JuchuShippingSeeder.JanCode,
		Su = su,
		JitsuSu = su,
		KakuteiDay = "20260801",
		EndFlag = 1,
	};
}
