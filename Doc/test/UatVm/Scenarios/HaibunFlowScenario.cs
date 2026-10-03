using System.IO;
using System.Windows;
using CvAsset;
using CvBase;
using CvBaseSqlite;
using CvDomainLogic;
using CvWpfclient.ViewModels._07Haibun;
using CvWpfclient.Views._07Haibun;
using Microsoft.Data.Sqlite;
using UatVm.Seed;

namespace UatVm.Scenarios;

/// <summary>
/// 配分再設計 Step 7 の通し検証。4つの配分（仕入・在庫・受注・取置）を同じDB・同じ商品で続けて動かし、
/// 配分確定・欠品実績・取置の売上変換と期限切れまで通したあと、全件再集計の結果が通常の更新と一致することを確かめる。
/// <para>
/// UAT-02 のシード（倉庫SK在庫8、卸先TK、直営店TS、1SKU）に、前回配分（完了済み TK4・TS6）・発注10・受注TK6・
/// 直営店の在庫2・顧客1人を足す。数量は、全部を確定すると倉庫の在庫も引当も0になるように組んである。
/// </para>
/// <list type="number">
/// <item>仕入配分：発注10を前回比率で TK4・TS6 → 仕入5で入荷済み TK4・TS1（店舗コード順）、倉庫の引当5</item>
/// <item>在庫配分：有効在庫8（13−5）から同数2で TK2・TS2、引当9</item>
/// <item>受注配分：有効在庫4の範囲で TK の受注残6 から 4、引当13</item>
/// <item>取置：直営店で顧客へ1、店舗の引当1。ピッキングリストには出ない</item>
/// <item>配分確定（商品順）：5行すべて。TSの仕入配分は入荷済み1で確定し欠品5。倉庫の在庫0・引当0</item>
/// <item>欠品実績：仕入配分の欠品5が出て、取置は出ない</item>
/// <item>取置：売上変換で店舗売上、期限切れの取置は自動取消</item>
/// <item>全件再集計（在庫は2026/09〜10、引当は全件）の前後で、この商品の在庫・引当が一致する</item>
/// </list>
/// 仕様は `Doc/spec/2026-10-04_配分再設計_Step7_通し検証_詳細設計.md` 3章。
/// </summary>
public static class HaibunFlowScenario {
	const string ScreenDirectory = "..\\Doc\\test\\uat20261003\\haibun\\screens";
	const string DenDay = "2026/09/05";
	const string CustomerCode = "UATVM-FL-C1";
	static JuchuShippingSeeder.Result? _seeded;
	static string? _dbPath;

	public static void Seeder(string dbPath) {
		_dbPath = dbPath;
		_seeded = JuchuShippingSeeder.Seed(dbPath, message => Console.WriteLine($"[seed] {message}"));
	}

	public static async Task RunAsync(VmSession session) {
		var seeded = _seeded ?? throw new InvalidOperationException("シードが実行されていません。");
		var screens = Path.Combine(Path.GetFullPath(ScreenDirectory), "flow_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
		Directory.CreateDirectory(screens);
		session.SetDialogResponder(request => request.Button is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel
			? MessageBoxResult.Yes : MessageBoxResult.OK);
		var day = DateTime.Parse(DenDay);
		var today = DateTime.Today;
		string Ymd(DateTime d) => d.ToString("yyyyMMdd");

		// ---- 準備 ----
		await session.InsertAsync(Previous(seeded, seeded.TokuiId, 4));
		await session.InsertAsync(Previous(seeded, seeded.DirectStoreId, 6));
		var hachu = await session.InsertAsync(new Tran13Hachu {
			DenDay = "20260901", NouhinDay = "20260905", Id_Soko = seeded.WarehouseId,
			VSoko = new CodeNameView(seeded.WarehouseId, seeded.WarehouseCode, string.Empty),
			Id_Shain = seeded.EmployeeId, Kubun = 10, Rate = 100, SuTotal = 10, KingakuTotal = 10000,
			Jmeisai = [Line(seeded, 10, 1000)],
		});
		var juchu = await session.InsertAsync(new Tran12Jyuchu {
			DenDay = "20260901", NouhinDay = "20260905", Id_Tokui = seeded.TokuiId,
			VTokui = new CodeNameView(seeded.TokuiId, seeded.TokuiCode, seeded.TokuiCode),
			Id_Soko = seeded.WarehouseId, VSoko = new CodeNameView(seeded.WarehouseId, seeded.WarehouseCode, string.Empty),
			Id_Shain = seeded.EmployeeId, VShain = new CodeNameView(seeded.EmployeeId, seeded.EmployeeCode, string.Empty),
			Kubun = (int)EnumJuchu.Juchu, Rate = 100, SuTotal = 6, KingakuTotal = 12000, Jmeisai = [Line(seeded, 6, 2000)],
		});
		var customer = (await session.QueryAsync<MasterEndCustomer>("where Code=@0", CustomerCode)).FirstOrDefault()
			?? await session.InsertAsync(new MasterEndCustomer { Code = CustomerCode, Name = "UAT-VM 通し顧客", Id_Tenpo = seeded.DirectStoreId });
		await session.InsertAsync(Purchase(seeded, seeded.DirectStoreId, 0, 2, "20260905"));
		session.Check("準備:発注・受注・顧客・直営店在庫2", hachu.Id > 0 && juchu.Id > 0 && customer.Id > 0, new { hachu = hachu.Id, juchu = juchu.Id });

		// ---- 1. 仕入配分 ----
		var receipt = session.OpenView<PurchaseReceiptAllocationInputView, PurchaseReceiptAllocationInputViewModel>();
		receipt.Input("仕入配分:条件", vm => {
			vm.SokoCode = seeded.WarehouseCode;
			vm.ShohinCode = seeded.ShohinCode;
			vm.ShijiDay = day;
			vm.NouhinDay = day;
		});
		await receipt.RunAsync("仕入配分:検索", vm => vm.DoSearchCommand);
		await receipt.RunAsync("仕入配分:前回の配分先を読込", vm => vm.LoadPreviousDestinationsCommand);
		await receipt.RunAsync("仕入配分:比率を計算", vm => vm.CalcRatioCommand);
		receipt.Run("仕入配分:按分実行", vm => vm.ApplyAllocationCommand);
		if (!session.Check("仕入配分:前回比率で TK4・TS6",
			Total(receipt.Vm.Rows.SingleOrDefault(r => r.Id_Tenpo == seeded.TokuiId)?.TotalSu) == 4
			&& Total(receipt.Vm.Rows.SingleOrDefault(r => r.Id_Tenpo == seeded.DirectStoreId)?.TotalSu) == 6,
			new { rows = receipt.Vm.Rows.Select(r => new { r.Id_Tenpo, r.TotalSu }), receipt.Vm.Message })) return;
		await HaibunScreenScenario.CaptureAsync(session, receipt.View, screens, "41_FlowPurchaseReceipt");
		await receipt.RunAsync("仕入配分:登録", vm => vm.DoRegisterCommand);
		await session.InsertAsync(Purchase(seeded, seeded.WarehouseId, hachu.Id, 5, "20260905"));
		var kubun0 = await session.QueryAsync<TranHaibun>($"where Kubun=0 AND RelateNo1={hachu.Id}");
		session.Check("仕入配分:仕入5で入荷済み TK4・TS1（店舗コード順）",
			Arrived(kubun0, seeded.TokuiId) == 4 && Arrived(kubun0, seeded.DirectStoreId) == 1,
			new { rows = kubun0.Select(h => new { h.Id_Tenpo, h.Su, h.ArrivedSu }) });
		await CheckStockAsync(session, seeded, seeded.WarehouseId, "仕入配分:倉庫 在庫13・引当5", 13, 5);

		// ---- 2. 在庫配分 ----
		var stock = session.OpenView<InventoryAllocationInputView, InventoryAllocationInputViewModel>();
		stock.Input("在庫配分:条件", vm => {
			vm.SokoCode = seeded.WarehouseCode;
			vm.ShohinCodeFrom = seeded.ShohinCode;
			vm.ShohinCodeTo = seeded.ShohinCode;
		});
		await stock.RunAsync("在庫配分:検索", vm => vm.DoSearchCommand);
		if (!session.Check("在庫配分:有効在庫8（13−仕入配分の入荷済み5）", stock.Vm.SearchRows.SingleOrDefault()?.YukoSu == 8,
			new { yuko = stock.Vm.SearchRows.SingleOrDefault()?.YukoSu })) return;
		await stock.RunAsync("在庫配分:配分入力へ", vm => vm.GoToEditCommand);
		await stock.RunAsync("在庫配分:前回の配分先を読込", vm => vm.LoadPreviousDestinationsCommand);
		stock.Input("在庫配分:同数2・指示日", vm => {
			vm.Mode = InventoryAllocationInputViewModel.ModeEqual;
			vm.SameQty = 2;
			vm.ShijiDay = day;
			vm.NouhinDay = day;
		});
		stock.Run("在庫配分:按分実行", vm => vm.ApplyAllocationCommand);
		session.Check("在庫配分:TK2・TS2", stock.Vm.Rows.Count == 2 && stock.Vm.Rows.All(r => r.TotalSu == 2),
			new { rows = stock.Vm.Rows.Select(r => new { r.Id_Tenpo, r.TotalSu }) });
		await HaibunScreenScenario.CaptureAsync(session, stock.View, screens, "42_FlowInventory");
		await stock.RunAsync("在庫配分:登録", vm => vm.DoRegisterCommand);
		await CheckStockAsync(session, seeded, seeded.WarehouseId, "在庫配分:倉庫 引当9", 13, 9);

		// ---- 3. 受注配分 ----
		var order = session.OpenView<SalesOrderAllocationInputView, SalesOrderAllocationInputViewModel>();
		order.Input("受注配分:条件", vm => {
			vm.SokoCode = seeded.WarehouseCode;
			vm.ShohinCode = seeded.ShohinCode;
			vm.JuchuDayFrom = new DateTime(2026, 9, 1);
			vm.JuchuDayTo = day;
			vm.ShijiDay = day;
			vm.NouhinDay = day;
		});
		await order.RunAsync("受注配分:検索", vm => vm.DoSearchCommand);
		if (!session.Check("受注配分:TKの受注残6・有効在庫4", order.Vm.Rows.Count == 1 && order.Vm.Rows[0].ZanTotalSu == 6
			&& order.Vm.SkuColumns.Single().YukoSu == 4,
			new { rows = order.Vm.Rows.Count, zan = order.Vm.Rows.FirstOrDefault()?.ZanTotalSu, yuko = order.Vm.SkuColumns.FirstOrDefault()?.YukoSu })) return;
		order.Run("受注配分:在庫内で受注日順に読込", vm => vm.FillByStockCommand);
		session.Check("受注配分:有効在庫の範囲で4", order.Vm.Rows[0].TotalSu == 4, new { su = order.Vm.Rows[0].TotalSu });
		await HaibunScreenScenario.CaptureAsync(session, order.View, screens, "43_FlowSalesOrder");
		await order.RunAsync("受注配分:登録", vm => vm.DoRegisterCommand);
		await CheckStockAsync(session, seeded, seeded.WarehouseId, "受注配分:倉庫 引当13（有効在庫0）", 13, 13);

		// ---- 4. 取置 ----
		var reservation = session.OpenView<CustomerReservationAllocationInputView, CustomerReservationAllocationInputViewModel>();
		reservation.Input("取置:店舗", vm => vm.TenpoCode = seeded.DirectStoreCode);
		await reservation.RunAsync("取置:検索", vm => vm.DoSearchCommand);
		reservation.Input("取置:登録欄", vm => {
			vm.EntryCustomerCode = CustomerCode;
			vm.EntryShohinCode = seeded.ShohinCode;
			vm.EntryDenDay = today;
			vm.EntrySu = 1;
		});
		await reservation.RunAsync("取置:商品読込", vm => vm.LoadEntryShohinCommand);
		await reservation.RunAsync("取置:登録", vm => vm.DoRegisterCommand);
		var overdue = await session.InsertAsync(new TranHaibun {
			Kubun = (int)EnumHaibun.Reservation, DenDay = Ymd(today.AddDays(-9)), LimitDay = Ymd(today.AddDays(-2)),
			Id_Tenpo = seeded.DirectStoreId, Id_Soko = seeded.DirectStoreId, Id_Customer = customer.Id,
			Id_Shohin = seeded.ShohinId, Id_Col = seeded.Id_Col, Id_Siz = seeded.Id_Siz, JanCode = JuchuShippingSeeder.JanCode, Su = 1, Tanka = 2000, Jodai = 2000,
		});
		await reservation.RunAsync("取置:再検索", vm => vm.DoSearchCommand);
		session.Check("取置:取置中2件", reservation.Vm.Rows.Count == 2, new { rows = reservation.Vm.Rows.Count });
		await CheckStockAsync(session, seeded, seeded.DirectStoreId, "取置:直営店 在庫2・引当2", 2, 2);
		await HaibunScreenScenario.CaptureAsync(session, reservation.View, screens, "44_FlowReservation");

		// ピッキングリスト：取置だけの日付範囲では「対象データがありません」
		session.ClearDialogs();
		var print = session.OpenView<ShippingConfirmDetailPrintView, ShippingConfirmDetailPrintViewModel>();
		print.Input("ピッキング:取置日の範囲・区分すべて", vm => {
			vm.SelectedKubun = -1;
			vm.DenDayFromText = today.AddDays(-9).ToString("yyyy/MM/dd");
			vm.DenDayToText = today.ToString("yyyy/MM/dd");
		});
		await print.RunAsync("ピッキング:印刷", vm => vm.DoPrintCommand);
		session.Check("ピッキング:取置は印刷対象にならない", session.Dialogs.Any(d => d.Request.Message.Contains("対象データがありません")),
			new { dialogs = session.Dialogs.Select(d => d.Request.Message) });

		// ---- 5. 配分確定（商品順・全行） ----
		var commit = session.OpenView<HaibunCommitView, HaibunCommitViewModel>();
		commit.Input("配分確定:条件", vm => {
			vm.DenDayFromText = DenDay;
			vm.DenDayToText = DenDay;
			vm.KakuteiDayText = DenDay;
			vm.SokoCode = seeded.WarehouseCode;
			vm.MaxCountText = "500";
		});
		await commit.RunAsync("配分確定:検索", vm => vm.SearchCommand);
		var commitRows = commit.Vm.Rows;
		if (!session.Check("配分確定:5行（仕入2・在庫2・受注1）、指示数18・確定数の初期値13（TSの仕入配分は入荷済み1まで）",
			commitRows.Count == 5 && commitRows.Sum(r => r.KakuteiSu) == 13 && commitRows.Sum(r => r.Su) == 18,
			new { rows = commitRows.Select(r => new { r.TenpoDisplay, r.Su, r.KakuteiSu, r.ArrivedDisplay }) })) return;
		await HaibunScreenScenario.CaptureAsync(session, commit.View, screens, "45_FlowCommit");
		foreach (var r in commitRows) r.IsChecked = true;
		await commit.RunAsync("配分確定:全行を確定", vm => vm.ConfirmSelectedCommand);
		var open = await session.QueryAsync<TranHaibun>("where EndFlag=0 AND Id_Soko=@0 AND Id_Shohin=@1", seeded.WarehouseId.ToString(), seeded.ShohinId.ToString());
		var uriage = await session.QueryAsync<Tran00Uriage>("where Id_Tokui=@0 AND Memo='配分出荷'", seeded.TokuiId.ToString());
		var ido = await session.QueryAsync<Tran10IdoOut>("where Id_Ido=@0 AND Memo='配分出荷'", seeded.DirectStoreId.ToString());
		session.Check("配分確定:未完了0、TKは出荷売上10点（仕入4・在庫2・受注4の3伝票）、TSは移動3点（仕入1・在庫2の2伝票）",
			open.Count == 0 && uriage.Count == 3 && uriage.Sum(u => u.SuTotal) == 10 && ido.Count == 2 && ido.Sum(i => i.SuTotal) == 3,
			new { open = open.Count, uriage = uriage.Select(u => u.SuTotal), ido = ido.Select(i => i.SuTotal) });
		var juchuUriage = uriage.Where(u => u.RelateNo1 == (int)juchu.Id).Sum(u => u.SuTotal);
		session.Check("配分確定:受注に紐付く出荷売上は受注配分の4点だけ", juchuUriage == 4, new { juchuUriage });
		await CheckStockAsync(session, seeded, seeded.WarehouseId, "配分確定:倉庫 在庫0・引当0", 0, 0);

		// ---- 6. 欠品実績 ----
		var list = session.OpenView<ShippingConfirmListView, ShippingConfirmListViewModel>();
		list.Input("欠品実績:条件", vm => {
			vm.ViewKind = "欠品実績";
			vm.DayFromText = DenDay;
			vm.DayToText = today.ToString("yyyy/MM/dd");
			vm.MaxCountText = "500";
		});
		await list.RunAsync("欠品実績:検索", vm => vm.SearchCommand);
		var ours = list.Vm.Rows.Where(r => r.TenpoDisplay.Contains(seeded.DirectStoreCode) || r.TenpoDisplay.Contains(seeded.TokuiCode)).ToList();
		session.Check("欠品実績:TSの仕入配分の欠品5だけ（取置は出ない）", ours.Count == 1 && ours[0].ShortSu == 5,
			new { rows = ours.Select(r => new { r.TenpoDisplay, r.ShortSu }) });
		await HaibunScreenScenario.CaptureAsync(session, list.View, screens, "46_FlowShortage");

		// ---- 7. 取置の売上変換・期限切れ ----
		var storeBefore = (await session.QueryAsync<SummaryRealStock>("where Id_Soko=@0 AND Id_Shohin=@1 AND Id_Col=@2 AND Id_Siz=@3",
			seeded.DirectStoreId.ToString(), seeded.ShohinId.ToString(), seeded.Id_Col.ToString(), seeded.Id_Siz.ToString())).Single().Su;
		foreach (var r in reservation.Vm.Rows) r.IsChecked = r.Source.Id != overdue.Id;
		reservation.Input("取置:売上日", vm => vm.UriDay = today);
		await reservation.RunAsync("取置:売上変換", vm => vm.DoConvertCommand);
		var tenuri = await session.QueryAsync<Tran01Tenuri>("where Id_Tenpo=@0 AND Memo='取置売上'", seeded.DirectStoreId.ToString());
		var expired = ExpireOverdue(today);
		var overdueRow = (await session.QueryAsync<TranHaibun>("where Id=@0", overdue.Id.ToString())).Single();
		session.Check("取置:売上変換で店舗売上1点、期限切れは自動取消",
			tenuri.Count == 1 && tenuri[0].SuTotal == 1 && expired == 1 && overdueRow.EndReason == (int)EnumHaibunEndReason.Expired,
			new { tenuri = tenuri.Count, expired, overdueRow.EndReason });
		// 直営店：店舗売上で1減り、引当は0（配分確定の移動は積送なので、店舗の現在庫への入りは移動受まで待つ）
		await CheckStockAsync(session, seeded, seeded.DirectStoreId, "取置:直営店 売上変換で在庫−1・引当0", storeBefore - 1, 0);

		// ---- 8. 全件再集計との一致 ----
		var before = await SnapshotAsync(session, seeded);
		var rebuild = Rebuild();
		var after = await SnapshotAsync(session, seeded);
		session.Check("全件再集計:この商品の在庫・引当が通常の更新と一致", rebuild == null && before.SequenceEqual(after),
			new { rebuild, before, after });

		session.SetDialogResponder(null);
	}

	static int Total(int? value) => value ?? -1;

	static int Arrived(IEnumerable<TranHaibun> rows, long tenpo) => rows.Where(h => h.Id_Tenpo == tenpo && h.EndFlag == 0).Sum(h => h.ArrivedSu);

	static Tran99Meisai Line(JuchuShippingSeeder.Result seeded, int su, int tanka) => new() {
		No = 1, Id_Shohin = seeded.ShohinId, Id_Col = seeded.Id_Col, Id_Siz = seeded.Id_Siz, JanCode = JuchuShippingSeeder.JanCode,
		Su = su, Tanka = tanka, Kingaku = su * tanka, Jodai = 2000, Gedai = 1000, Id_Tax = 1,
	};

	static Tran03Shiire Purchase(JuchuShippingSeeder.Result seeded, long idSoko, long hachuId, int su, string denDay) {
		var tran = new Tran03Shiire {
			DenDay = denDay, KakeDay = denDay, Id_Soko = idSoko, RelateNo1 = (int)hachuId,
			Id_Shain = seeded.EmployeeId, Rate = 100, SuTotal = su, KingakuTotal = su * 1000, Jmeisai = [Line(seeded, su, 1000)],
		};
		tran.EnKubun = EnumShiire.Shiire;
		return tran;
	}

	static TranHaibun Previous(JuchuShippingSeeder.Result seeded, long idTenpo, int su) => new() {
		DenDay = "20260801", NouhinDay = "20260801", Id_Soko = seeded.WarehouseId, Id_Tenpo = idTenpo,
		Kubun = (int)EnumHaibun.Zaiko, Id_Shohin = seeded.ShohinId, Id_Col = seeded.Id_Col, Id_Siz = seeded.Id_Siz,
		JanCode = JuchuShippingSeeder.JanCode, Su = su, JitsuSu = su, KakuteiDay = "20260801", EndFlag = 1,
	};

	static async Task CheckStockAsync(VmSession session, JuchuShippingSeeder.Result seeded, long idSoko, string name, int su, int reserve) {
		var row = (await session.QueryAsync<SummaryRealStock>("where Id_Soko=@0 AND Id_Shohin=@1 AND Id_Col=@2 AND Id_Siz=@3",
			idSoko.ToString(), seeded.ShohinId.ToString(), seeded.Id_Col.ToString(), seeded.Id_Siz.ToString())).SingleOrDefault();
		session.Check(name, row != null && row.Su == su && row.ReserveQty == reserve, new { row?.Su, row?.ReserveQty });
	}

	/// <summary>この商品の月次在庫（2026/09〜10）と現在庫・引当</summary>
	static async Task<List<string>> SnapshotAsync(VmSession session, JuchuShippingSeeder.Result seeded) {
		var monthly = await session.QueryAsync<SummaryStock>("where Id_Shohin=@0 AND SumMonth IN ('202609','202610') order by SumMonth, Id_Soko, Id_Col, Id_Siz", seeded.ShohinId.ToString());
		var real = await session.QueryAsync<SummaryRealStock>("where Id_Shohin=@0 order by Id_Soko, Id_Col, Id_Siz", seeded.ShohinId.ToString());
		return [
			.. monthly.Select(x => $"M:{x.SumMonth}:{x.Id_Soko}:{x.Id_Col}:{x.Id_Siz}:{x.Su}:{x.InQty}:{x.OutQty}:{x.TransitQty}:{x.ReserveQty}"),
			.. real.Select(x => $"R:{x.Id_Soko}:{x.Id_Col}:{x.Id_Siz}:{x.Su}:{x.ReserveQty}"),
		];
	}

	static ExDatabaseSqlite OpenDb(SqliteConnection connection) {
		connection.Open();
		return new ExDatabaseSqlite(connection) { KeepConnectionAlive = true };
	}

	static SqliteConnection Connection() => new(new SqliteConnectionStringBuilder {
		DataSource = _dbPath ?? throw new InvalidOperationException("DBパスがありません。"),
		Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 60,
	}.ToString());

	/// <summary>日次タスク「取置期限切れ自動取消」と同じ処理を、UATの複製DBへ直接実行する</summary>
	static int ExpireOverdue(DateTime today) {
		using var connection = Connection();
		var db = OpenDb(connection);
		db.BeginTransaction();
		var count = new ReservationDb(db).ExpireOverdue(today);
		db.CompleteTransaction();
		return count;
	}

	/// <summary>在庫の全件再集計（2026/09〜10）と引当の全件再集計を、UATの複製DBへ直接実行する。エラーなら内容を返す</summary>
	static string? Rebuild() {
		using var connection = Connection();
		var db = OpenDb(connection);
		var summaryDb = new SummaryDb(db);
		var errors = new List<string>();
		Task.Run(async () => {
			await foreach (var progress in summaryDb.SummaryAllAsyncStream(new CalcDateTermParameter("202609", "202610"))) {
				if (progress.IsError) errors.Add($"{progress.StepName}: {progress.ErrorMessage}");
			}
		}).GetAwaiter().GetResult();
		summaryDb.CalcReserveQtyAll();
		return errors.Count == 0 ? null : string.Join(" / ", errors);
	}
}
