using System.IO;
using System.Windows;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using CvWpfclient.ViewModels._32LoyalCustomer;
using CvWpfclient.Views._32LoyalCustomer;
using Microsoft.Data.Sqlite;

namespace UatVm.Scenarios;

/// <summary>
/// 独立DBでポイントの使用・控除・ボーナス・失効・手動登録を通しで確認する。
/// 店舗売上の登録(gRPC Msg201、保存時同期)、残高不足の拒否、返品、実画面「ポイント再計算」での再計算と失効処理、
/// 実画面「ポイント手動登録」での登録・取消を行い、台帳(TranPointEvent)・伝票の付与ポイント・SummaryPoint・会員ポイントを照合する。
/// </summary>
public static class PointFlowScenario {
	static string? databasePath;

	public static void Seeder(string dbPath) {
		var path = Path.GetFullPath(dbPath);
		if (!Path.GetFileName(path).StartsWith("point-flow-uat-", StringComparison.OrdinalIgnoreCase)
			|| !path.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
			|| path.Split(Path.DirectorySeparatorChar).Any(x => x.Equals("CvServer", StringComparison.OrdinalIgnoreCase))
			|| (File.Exists(path) && new FileInfo(path).Length > 0))
			throw new InvalidOperationException("未作成/空の専用point-flow-uat-*.dbだけ使用できます。");
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
		connection.Open();
		using var db = new ExDatabaseSqlite(connection) { KeepConnectionAlive = true };
		if (!new DefineDataTable().InitializeAsync(db, false).GetAwaiter().GetResult()) throw new InvalidOperationException("専用DB初期化失敗");
		// ベース: 100円で1P、利用控除する、最終購入から12か月で失効
		var basis = new MasterPointBase { Code = "PFBASE", Name = "通し確認ベース", DayFrom = "20260101", DayTo = "20261231", IsEnabled = 1,
			PointUnitPrice = 100, PointAmountProper = 1, PointAmountSale = 1, DeductPointUse = 1, ExpireMonths = 12 };
		db.Insert(basis);
		// 期間内購入: 対象額1000円以上で10P、適用期間内1回。誕生月購入: 20P、伝票ごと
		db.Insert(new MasterPointBonus { Code = "PFBP", Name = "期間内購入", Version = 1, Id_PointBase = basis.Id, DayFrom = "20261001", DayTo = "20261031", IsEnabled = 1,
			TriggerType = (int)EnumPointBonusTrigger.Purchase, PointAmount = 10, MinimumKingaku = 1000, IsAllRanks = 1, LimitPeriodType = (int)EnumPointLimitPeriod.Period, LimitCount = 1 });
		db.Insert(new MasterPointBonus { Code = "PFBD", Name = "誕生月", Version = 1, Id_PointBase = basis.Id, DayFrom = "20261001", DayTo = "20261031", IsEnabled = 1,
			TriggerType = (int)EnumPointBonusTrigger.BirthdayMonth, PointAmount = 20, IsAllRanks = 1, LimitPeriodType = (int)EnumPointLimitPeriod.Slip, LimitCount = 1 });
		db.Insert(new MasterTokui { Code = "PF1", Name = "UAT店舗PF1", Ryaku = "PF1", TenType = 6 });
		db.Insert(new MasterShohin { Code = "PFP1", Name = "UAT商品PFP1" });
		// PFK1: 10月生まれ・期首500P。PFK2: 退会済み・期首30P。PFK3: 売上なし・最終来店が古い移行顧客・期首50P
		foreach (var (code, birth, point, withdrawn, lastVisit) in new[] { ("PFK1", "1005", 500, "", ""), ("PFK2", "", 30, "20261001", "20261001"), ("PFK3", "", 50, "", "20240101") }) {
			var customer = new MasterEndCustomer { Code = code, Name = $"UAT会員{code}", BirthNoyear = birth };
			db.Insert(customer);
			db.Insert(new MasterEndCustomerAccount { Id_Customer = customer.Id, Point = point, IsWithdrawalFlag = withdrawn.Length > 0 ? 1 : 0, WithdrawnDate = withdrawn, LastVisitDate = lastVisit });
			db.Insert(new TranPointEvent { EventKey = $"OPEN:{customer.Id}", DenDay = "20260101", Id_Customer = customer.Id, EventType = (int)EnumPointEventType.OpeningBalance, PointDelta = point, Memo = "期首残高" });
			db.Insert(new SummaryPoint { Id_Customer = checked((int)customer.Id), Point = point });
		}
		databasePath = path;
	}

	public static async Task RunAsync(VmSession session) {
		if (databasePath is null) throw new InvalidOperationException("専用Seederを実行してください。");
		var screens = Path.Combine(Path.GetDirectoryName(databasePath)!, "point-flow-screens-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
		Directory.CreateDirectory(screens);
		session.Note("対象", new { Database = databasePath, Screens = screens });
		var shop = await One<MasterTokui>(session, "PF1");
		var shohin = (await One<MasterShohin>(session, "PFP1")).Id;
		var k1 = (await One<MasterEndCustomer>(session, "PFK1")).Id;
		var k2 = (await One<MasterEndCustomer>(session, "PFK2")).Id;
		var k3 = (await One<MasterEndCustomer>(session, "PFK3")).Id;

		// 1. 使用200P・2000円: (2000-200)/100=18 + 期間内購入10 + 誕生月20 = 48。残高 500+48-200=348
		var s1 = await session.InsertAsync(Slip("UATPF1", k1, shop, 10, 2000, 200, shohin));
		session.CheckEqual("登録:付与ポイント(控除・ボーナス)", 48L, (await SlipOf(session, "UATPF1")).GrantPoint);
		await CheckEvents(session, "登録:台帳", s1.Id, grant: 18, bonus: 30, use: -200);
		await CheckBalance(session, "登録後", k1, 348);

		// 2. 1500円: 15 + 誕生月20(期間内購入は上限1回) = 35。残高383
		var s2 = await session.InsertAsync(Slip("UATPF2", k1, shop, 10, 1500, 0, shohin));
		session.CheckEqual("2件目:期間内購入は上限", 35L, (await SlipOf(session, "UATPF2")).GrantPoint);
		await CheckBalance(session, "2件目後", k1, 383);

		// 3. 残高を超える使用は警告で拒否し、伝票も台帳も残さない
		var before = (await Events(session)).Count;
		try {
			await session.InsertAsync(Slip("UATPF3", k1, shop, 10, 1000, 1000, shohin));
			session.Fail("残高不足:拒否", "保存できてしまった");
		}
		catch (InvalidOperationException ex) {
			session.Check("残高不足:拒否メッセージ", ex.Message.Contains("ポイント残高が不足"), ex.Message);
		}
		session.Check("残高不足:伝票・台帳なし", (await session.QueryAsync<Tran01Tenuri>("WHERE Memo='UATPF3'")).Count == 0 && (await Events(session)).Count == before);

		// 4. 返品1000円・使用戻し100P: 付与 -(900/100=9) - 誕生月20 = -29、使用+100。残高 383-29+100=454
		var s4 = await session.InsertAsync(Slip("UATPF4", k1, shop, 20, 1000, 100, shohin));
		session.CheckEqual("返品:付与ポイント(負のボーナス)", -29L, (await SlipOf(session, "UATPF4")).GrantPoint);
		await CheckEvents(session, "返品:台帳", s4.Id, grant: -9, bonus: -20, use: 100);
		await CheckBalance(session, "返品後", k1, 454);

		// 5. 再計算(実画面)は変更なしなら追記しない
		before = (await Events(session)).Count;
		await RunSummary(session, screens, "再計算", vm => { vm.YearMonthFrom = "2026/10"; vm.YearMonthTo = "2026/10"; }, vm => vm.ExecuteCommand);
		session.CheckEqual("再計算:追記0", before, (await Events(session)).Count);

		// 6. 失効(実画面) 基準日2026/10/31: PFK2=退会、PFK3=最終来店2024/01/01が12か月超で失効。PFK1は最近購入で対象外
		await RunSummary(session, screens, "失効", vm => vm.ExpireBaseDay = "2026/10/31", vm => vm.ExpireCommand);
		var expired = (await Events(session)).Where(x => x.EventType == (int)EnumPointEventType.Expire).ToList();
		session.Check("失効:対象", expired.Count == 2 && expired.Any(x => x.Id_Customer == k2 && x.PointDelta == -30 && x.Memo == "退会の為失効")
			&& expired.Any(x => x.Id_Customer == k3 && x.PointDelta == -50), expired.Select(x => new { x.Id_Customer, x.PointDelta, x.Memo }));
		await CheckBalance(session, "失効後PFK2", k2, 0);
		await CheckBalance(session, "失効後PFK3", k3, 0);
		await CheckBalance(session, "失効後PFK1", k1, 454);
		before = (await Events(session)).Count;
		await RunSummary(session, screens, "失効再実行", vm => vm.ExpireBaseDay = "2026/10/31", vm => vm.ExpireCommand);
		session.CheckEqual("失効:同じ基準日の再実行で追記0", before, (await Events(session)).Count);

		// 7. 手動登録(実画面)
		await ManualLedger(session, screens, k1);
	}

	/// <summary>
	/// 実画面「ポイント手動登録」: 調整・手動失効の登録、残高不足の拒否、同じEventKeyの再送拒否、取消と二重取消不可、
	/// 手動のみ／全件の検索、標準・最小サイズの画像と見切れ確認。PFK1 の残高は 454 から始まる。
	/// </summary>
	static async Task ManualLedger(VmSession session, string screens, long idCustomer) {
		var driver = session.OpenView<PointLedgerManualView, PointLedgerManualViewModel>();
		session.SetDialogResponder(r => r.Button == MessageBoxButton.YesNo ? MessageBoxResult.Yes : MessageBoxResult.OK);
		try {
			await driver.WaitAsync("初期表示", vm => !vm.IsBusy);
			driver.Input("条件:PFK1・2026年・手動のみ", vm => { vm.SearchCustomerCode = "PFK1"; vm.SearchDayFrom = "20260101"; vm.SearchDayTo = "20261231"; vm.ManualOnly = true; });
			await driver.RunAsync("検索", vm => vm.DoSearchCommand);
			session.Check("手動:初期は手動行なし・残高454", driver.Vm.Rows.Count == 0 && driver.Vm.BalanceText.Contains("454"), new { driver.Vm.Rows.Count, driver.Vm.BalanceText });
			await Capture(session, driver.View, screens, "manual_01_Standard");

			async Task Register(string name, EnumPointEventType type, string point, string memo) {
				driver.Input(name, vm => { vm.EntryCustomerCode = "PFK1"; vm.EntryDay = "20261010"; vm.EntryType = (int)type; vm.EntryPoint = point; vm.EntryMemo = memo; });
				await driver.RunAsync(name, vm => vm.DoRegisterCommand);
			}
			await Register("登録:調整+46", EnumPointEventType.Adjustment, "46", "UAT調整");
			var firstKey = driver.Vm.Rows.SingleOrDefault()?.Event.EventKey;
			session.Check("手動:調整で残高500", driver.Vm.Rows.Count == 1 && driver.Vm.BalanceText.Contains("500") && firstKey?.StartsWith("MANUAL:") == true, new { driver.Vm.Rows.Count, driver.Vm.BalanceText, firstKey });
			await CheckBalance(session, "手動:調整後", idCustomer, 500);

			var before = (await Events(session)).Count;
			session.ClearDialogs();
			await Register("登録:使用600(残高不足)", EnumPointEventType.Use, "600", "UAT使用");
			session.Check("手動:残高不足は警告で拒否", (await Events(session)).Count == before && session.Dialogs.Any(x => (x.Request.Message + x.Request.AppendedMessage).Contains("ポイント残高が不足")),
				session.Dialogs.Select(x => x.Request.Message + x.Request.AppendedMessage));

			var keyBefore = driver.Vm.EntryEventKey;
			driver.Input("再送:1件目と同じEventKey", vm => vm.EntryEventKey = firstKey!);
			session.ClearDialogs();
			await Register("登録:同じEventKeyで再送", EnumPointEventType.Adjustment, "46", "UAT調整");
			session.Check("手動:再送は二重計上しない", (await Events(session)).Count == before && session.Dialogs.Any(x => (x.Request.Message + x.Request.AppendedMessage).Contains("既に保存")), session.Dialogs.Select(x => x.Request.Message + x.Request.AppendedMessage));
			driver.Input("EventKeyを戻す", vm => vm.EntryEventKey = keyBefore);

			await Register("登録:手動失効100", EnumPointEventType.Expire, "100", "UAT手動失効");
			await CheckBalance(session, "手動:失効後", idCustomer, 400);
			var expireRow = driver.Vm.Rows.Single(x => x.Event.EventType == (int)EnumPointEventType.Expire);
			session.CheckEqual("手動:失効は負で記録", -100L, expireRow.Event.PointDelta);

			driver.Input("取消対象:手動失効", vm => vm.SelectedRow = vm.Rows.Single(x => x.Event.Id == expireRow.Event.Id));
			await driver.RunAsync("取消", vm => vm.CancelSelectedCommand);
			await CheckBalance(session, "手動:取消後", idCustomer, 500);
			var cancelled = driver.Vm.Rows.Single(x => x.Event.Id == expireRow.Event.Id);
			driver.Input("取消済み行を選択", vm => vm.SelectedRow = cancelled);
			session.Check("手動:取消済みは再取消不可", cancelled.IsCancelled && !driver.Vm.CancelSelectedCommand.CanExecute(null), new { cancelled.IsCancelled });
			var cancelRow = driver.Vm.Rows.Single(x => x.Event.EventType == (int)EnumPointEventType.Cancel);
			driver.Input("取消行を選択", vm => vm.SelectedRow = cancelRow);
			session.Check("手動:取消行は取消不可", !driver.Vm.CancelSelectedCommand.CanExecute(null) && cancelRow.Event.EventKey == $"MANUAL:C:{expireRow.Event.Id}", cancelRow.Event.EventKey);

			driver.Input("条件:手動のみ解除", vm => vm.ManualOnly = false);
			session.Check("手動:条件変更で旧結果を無効化", driver.Vm.Rows.Count == 0 && !driver.Vm.CancelSelectedCommand.CanExecute(null));
			await driver.RunAsync("検索(全件)", vm => vm.DoSearchCommand);
			var salesRows = driver.Vm.Rows.Where(x => !x.IsManual).ToList();
			session.Check("手動:全件では店舗売上・期首の行も表示し取消不可", salesRows.Count > 0 && salesRows.All(x => !x.CanCancel), new { All = driver.Vm.Rows.Count, Sales = salesRows.Count });
			await Capture(session, driver.View, screens, "manual_02_All");

			driver.View.Width = driver.View.MinWidth; driver.View.Height = driver.View.MinHeight;
			await Capture(session, driver.View, screens, "manual_03_Minimum");
		}
		finally {
			session.SetDialogResponder(null);
			driver.View.Close();
		}
	}

	static async Task Capture(VmSession session, Window view, string screens, string name) {
		await Task.Delay(800);
		view.UpdateLayout();
		await view.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
		var path = ScreenLayoutCheck.SaveJpeg(view, screens, name);
		session.Note(name + ":画面画像", new { Path = path });
		var issues = ScreenLayoutCheck.Inspect(view);
		session.Check(name + ":文字・ボタン見切れなし", issues.Count == 0, new { issues.Count, Issues = issues });
	}

	static Tran01Tenuri Slip(string memo, long idCustomer, MasterTokui shop, int kubun, long kingaku, long usePoint, long idShohin) => new() {
		DenDay = "20261005", Id_Customer = idCustomer, Id_Tenpo = shop.Id, Id_Soko = shop.Id, Kubun = kubun, Memo = memo, UsePoint = usePoint,
		Jmeisai = [new Tran99Meisai { Id_Shohin = idShohin, Kubun = 0, Su = 1, Tanka = (int)kingaku, Kingaku = kingaku }],
	};

	internal static async Task<T> One<T>(VmSession session, string code) where T : BaseDbClass =>
		(await session.QueryAsync<T>("WHERE Code=@0", code)).Single();

	static async Task<Tran01Tenuri> SlipOf(VmSession session, string memo) => (await session.QueryAsync<Tran01Tenuri>("WHERE Memo=@0", memo)).Single();

	internal static Task<List<TranPointEvent>> Events(VmSession session) => session.QueryAsync<TranPointEvent>("ORDER BY Id");

	static async Task CheckEvents(VmSession session, string name, long idTenuri, long grant, long bonus, long use) {
		var events = (await Events(session)).Where(x => x.Id_Tenuri == idTenuri).ToList();
		var g = events.Where(x => x.EventType == (int)EnumPointEventType.Grant && x.Id_PointBonus == 0).Sum(x => x.PointDelta);
		var b = events.Where(x => x.EventType == (int)EnumPointEventType.Grant && x.Id_PointBonus > 0).Sum(x => x.PointDelta);
		var u = events.Where(x => x.EventType == (int)EnumPointEventType.Use).Sum(x => x.PointDelta);
		session.Check(name, g == grant && b == bonus && u == use, new { expected = new { grant, bonus, use }, actual = new { g, b, u } });
	}

	internal static async Task CheckBalance(VmSession session, string name, long idCustomer, long expected) {
		var summary = (await session.QueryAsync<SummaryPoint>("ORDER BY Id_Customer")).SingleOrDefault(x => x.Id_Customer == idCustomer)?.Point ?? 0;
		var account = (await session.QueryAsync<MasterEndCustomerAccount>("ORDER BY Id_Customer")).Single(x => x.Id_Customer == idCustomer).Point;
		var ledger = (await Events(session)).Where(x => x.Id_Customer == idCustomer).Sum(x => x.PointDelta);
		session.Check(name + ":残高", summary == expected && account == expected && ledger == expected, new { expected, summary, account, ledger });
	}

	static async Task RunSummary(VmSession session, string screens, string name, Action<PointSummaryViewModel> input, Func<PointSummaryViewModel, CommunityToolkit.Mvvm.Input.IAsyncRelayCommand> command) {
		var driver = session.OpenView<PointSummaryView, PointSummaryViewModel>();
		session.SetDialogResponder(r => r.Button == MessageBoxButton.YesNo ? MessageBoxResult.Yes : MessageBoxResult.OK);
		try {
			driver.Input(name + ":入力", input);
			await driver.RunAsync(name, command);
			session.Check(name + ":完了", driver.Vm.StatusMessage.Contains("完了") && driver.Vm.ProgressValue == 100, driver.Vm.StatusMessage);
			var path = ScreenLayoutCheck.SaveJpeg(driver.View, screens, "summary_" + name);
			session.Note(name + ":画面画像", new { Path = path });
		}
		finally {
			session.SetDialogResponder(null);
			driver.View.Close();
		}
	}
}
