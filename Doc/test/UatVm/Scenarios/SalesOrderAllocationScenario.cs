using System.IO;
using System.Windows;
using CvAsset;
using CvBase;
using CvWpfclient.ViewModels._07Haibun;
using CvWpfclient.Views._07Haibun;
using UatVm.Seed;

namespace UatVm.Scenarios;

/// <summary>
/// 配分再設計 Step 2 の受注配分入力(商品別)を実View/ViewModelで検証する。
/// <para>
/// UAT-02 と同じシード（倉庫在庫8・卸先TK・直営店TS）に、卸先の受注2件（9/1 4点・9/3 3点）と直営店の受注1件（9/2 3点）を登録し、
/// 「在庫内で受注日順に読込」→ 登録で受注日の古い受注から割り付くこと、受注残超過分が受注に紐付かない配分になること、
/// 洗い替えで重複しないことを確認する。画面はJPG保存と表示崩れの自動判定を行う。
/// 画面処理は SalesOrderAllocationInputViewModel、受注への割付規則は HaibunOrderDistributor を参照する。
/// </para>
/// </summary>
public static class SalesOrderAllocationScenario {
	const string ScreenDirectory = "..\\Doc\\test\\uat20261003\\haibun\\screens";
	static JuchuShippingSeeder.Result? _seeded;

	public static void Seeder(string dbPath) =>
		_seeded = JuchuShippingSeeder.Seed(dbPath, message => Console.WriteLine($"[seed] {message}"));

	public static async Task RunAsync(VmSession session) {
		var seeded = _seeded ?? throw new InvalidOperationException("シードが実行されていません。");
		var screens = Path.Combine(Path.GetFullPath(ScreenDirectory), "order_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
		Directory.CreateDirectory(screens);
		session.SetDialogResponder(request => request.Button is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel
			? MessageBoxResult.Yes : MessageBoxResult.OK);

		var tkOld = await session.InsertAsync(NewOrder(seeded, seeded.TokuiId, seeded.TokuiCode, "20260901", 4));
		var tkNew = await session.InsertAsync(NewOrder(seeded, seeded.TokuiId, seeded.TokuiCode, "20260903", 3));
		var ts = await session.InsertAsync(NewOrder(seeded, seeded.DirectStoreId, seeded.DirectStoreCode, "20260902", 3));
		session.Check("受注3件を登録", tkOld.Id > 0 && tkNew.Id > 0 && ts.Id > 0, new { A = tkOld.Id, B = tkNew.Id, C = ts.Id });

		var screen = session.OpenView<SalesOrderAllocationInputView, SalesOrderAllocationInputViewModel>();
		screen.Input("商品別:検索条件", vm => {
			vm.SokoCode = seeded.WarehouseCode;
			vm.ShohinCode = seeded.ShohinCode;
			vm.JuchuDayFrom = new DateTime(2026, 9, 1);
			vm.JuchuDayTo = new DateTime(2026, 9, 5);
			vm.ShijiDay = new DateTime(2026, 9, 5);
			vm.NouhinDay = new DateTime(2026, 9, 5);
		});
		await screen.RunAsync("商品別:検索", vm => vm.DoSearchCommand);
		var rows = screen.Vm.Rows;
		if (!session.Check("商品別:得意先2行（受注日の古い卸先が先）", rows.Count == 2 && rows[0].Id_Tokui == seeded.TokuiId && rows[1].Id_Tokui == seeded.DirectStoreId,
			new { rows = rows.Count, screen.Vm.Message })) return;
		session.Check("商品別:受注残 卸先7・直営店3、在庫8", rows[0].ZanTotalSu == 7 && rows[1].ZanTotalSu == 3 && screen.Vm.SkuColumns.Single().YukoSu == seeded.InitialStock,
			new { tk = rows[0].ZanTotalSu, ts = rows[1].ZanTotalSu, yuko = screen.Vm.SkuColumns.Single().YukoSu });

		screen.Run("商品別:在庫内で受注日順に読込", vm => vm.FillByStockCommand);
		session.Check("商品別:在庫8を卸先7・直営店1へ", rows[0].TotalSu == 7 && rows[1].TotalSu == 1 && screen.Vm.SkuColumns.Single().AfterSu == 0,
			new { tk = rows[0].TotalSu, ts = rows[1].TotalSu });
		await HaibunScreenScenario.CaptureAsync(session, screen.View, screens, "11_SalesOrderAllocation");

		await screen.RunAsync("商品別:登録", vm => vm.DoRegisterCommand);
		var orderIds = $"{tkOld.Id},{tkNew.Id},{ts.Id}";
		var saved = await session.QueryAsync<TranHaibun>($"where Kubun=2 AND EndFlag=0 AND RelateNo1 IN ({orderIds}) order by RelateNo1");
		session.Check("商品別:受注日の古い受注から割り付け（A4・B3・C1）",
			saved.Count == 3 && Su(saved, tkOld.Id) == 4 && Su(saved, tkNew.Id) == 3 && Su(saved, ts.Id) == 1
			&& saved.All(h => h.Id_Soko == seeded.WarehouseId && h.DenDay == "20260905" && h.Tanka == 2000),
			new { rows = saved.Select(h => new { h.RelateNo1, h.Id_Tenpo, h.Su, h.Tanka }) });
		session.Check("商品別:登録後に再読込して入力値を保持", screen.Vm.Rows.Count == 2 && screen.Vm.Rows[0].TotalSu == 7 && screen.Vm.Rows[1].TotalSu == 1,
			new { rows = screen.Vm.Rows.Select(r => r.TotalSu) });
		var stock = (await session.QueryAsync<SummaryRealStock>("where Id_Soko=@0 AND Id_Shohin=@1 AND Id_Col=@2 AND Id_Siz=@3",
			seeded.WarehouseId.ToString(), seeded.ShohinId.ToString(), seeded.Id_Col.ToString(), seeded.Id_Siz.ToString())).Single();
		session.Check("商品別:引当8", stock.ReserveQty == seeded.InitialStock, new { stock.Su, stock.ReserveQty });

		// 直営店を受注残3を超える5にすると、超過2は受注に紐付かない配分になる（洗い替えで重複しない）
		screen.Input("商品別:直営店を5に変更", vm => vm.Rows[1].Cells[0].SuText = "5");
		session.Check("商品別:受注残超過セルを表示", screen.Vm.Rows[1].Cells[0].IsOver, new { screen.Vm.Rows[1].Cells[0].Su });
		await HaibunScreenScenario.CaptureAsync(session, screen.View, screens, "12_SalesOrderAllocationOver");
		await screen.RunAsync("商品別:超過ありで登録", vm => vm.DoRegisterCommand);
		var toTs = await session.QueryAsync<TranHaibun>($"where Kubun=2 AND EndFlag=0 AND Id_Tenpo=@0 AND Id_Shohin=@1 order by RelateNo1 desc",
			seeded.DirectStoreId.ToString(), seeded.ShohinId.ToString());
		session.Check("商品別:直営店は受注C3＋紐付かない2", toTs.Count == 2 && Su(toTs, ts.Id) == 3 && Su(toTs, 0) == 2,
			new { rows = toTs.Select(h => new { h.RelateNo1, h.Su }) });
		var allTk = await session.QueryAsync<TranHaibun>($"where Kubun=2 AND EndFlag=0 AND Id_Tenpo=@0 AND Id_Shohin=@1",
			seeded.TokuiId.ToString(), seeded.ShohinId.ToString());
		session.Check("商品別:卸先は洗い替えで重複しない（合計7）", allTk.Sum(h => h.Su) == 7 && allTk.Count == 2,
			new { rows = allTk.Select(h => new { h.RelateNo1, h.Su }) });

		session.SetDialogResponder(null);
	}

	static int Su(IEnumerable<TranHaibun> rows, long relateNo1) => rows.Where(h => h.RelateNo1 == relateNo1).Sum(h => h.Su);

	static Tran12Jyuchu NewOrder(JuchuShippingSeeder.Result seeded, long idTokui, string tokuiCode, string denDay, int su) => new() {
		DenDay = denDay,
		NouhinDay = denDay,
		Id_Tokui = idTokui,
		VTokui = new CodeNameView(idTokui, tokuiCode, tokuiCode),
		Id_Soko = seeded.WarehouseId,
		VSoko = new CodeNameView(seeded.WarehouseId, seeded.WarehouseCode, string.Empty),
		Id_Shain = seeded.EmployeeId,
		VShain = new CodeNameView(seeded.EmployeeId, seeded.EmployeeCode, string.Empty),
		Kubun = (int)EnumJuchu.Juchu,
		Rate = 100,
		SuTotal = su,
		KingakuTotal = su * 2000,
		Jmeisai = [new Tran99Meisai {
			No = 1, Id_Shohin = seeded.ShohinId, Id_Col = seeded.Id_Col, Id_Siz = seeded.Id_Siz,
			JanCode = JuchuShippingSeeder.JanCode, Su = su, Tanka = 2000,
			Kingaku = su * 2000, Jodai = 2000, Gedai = 1000, Id_Tax = 1,
		}],
	};
}
