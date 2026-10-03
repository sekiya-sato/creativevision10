using System.IO;
using System.Windows;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using CvDomainLogic;
using CvWpfclient.ViewModels._07Haibun;
using CvWpfclient.Views._07Haibun;
using Microsoft.Data.Sqlite;
using UatVm.Seed;

namespace UatVm.Scenarios;

/// <summary>
/// 配分再設計 Step 5 の取置配分入力を実View/ViewModelで検証する。
/// <para>
/// UAT-02 と同じシードの直営店(TS)に在庫3を入れ、顧客を1人登録して、
/// 取置登録 → 有効在庫超過の警告つき登録 → 期限変更 → 数量変更 → 売上変換（店舗売上） → 取消 →
/// 期限切れの自動取消（日次タスクと同じ <see cref="ReservationDb.ExpireOverdue"/> を複製DBへ直接呼ぶ）までを通す。
/// 画面はJPG保存と表示崩れの自動判定を行う。
/// 仕様は `Doc/spec/2026-10-03_配分再設計_Step5_取置配分入力_詳細設計.md`。
/// </para>
/// </summary>
public static class CustomerReservationScenario {
	const string ScreenDirectory = "..\\Doc\\test\\uat20261003\\haibun\\screens";
	const string CustomerCode = "UATVM-RS-C1";
	const int StoreStock = 3;
	static JuchuShippingSeeder.Result? _seeded;
	static string? _dbPath;

	public static void Seeder(string dbPath) {
		_dbPath = dbPath;
		_seeded = JuchuShippingSeeder.Seed(dbPath, message => Console.WriteLine($"[seed] {message}"));
	}

	public static async Task RunAsync(VmSession session) {
		var seeded = _seeded ?? throw new InvalidOperationException("シードが実行されていません。");
		var screens = Path.Combine(Path.GetFullPath(ScreenDirectory), "reservation_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
		Directory.CreateDirectory(screens);
		session.SetDialogResponder(request => request.Button is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel
			? MessageBoxResult.Yes : MessageBoxResult.OK);
		var today = DateTime.Today;
		string Ymd(DateTime d) => d.ToString("yyyyMMdd");

		// 直営店に在庫3、顧客1人（その店舗の顧客）
		var customer = (await session.QueryAsync<MasterEndCustomer>("where Code=@0", CustomerCode)).FirstOrDefault()
			?? await session.InsertAsync(new MasterEndCustomer { Code = CustomerCode, Name = "UAT-VM 取置顧客", Kana = "ﾄﾘｵｷｺｷｬｸ", Id_Tenpo = seeded.DirectStoreId });
		var shiire = new Tran03Shiire {
			DenDay = Ymd(today.AddDays(-1)), KakeDay = Ymd(today.AddDays(-1)), Id_Soko = seeded.DirectStoreId,
			Jmeisai = [new Tran99Meisai { No = 1, Id_Shohin = seeded.ShohinId, Id_Col = seeded.Id_Col, Id_Siz = seeded.Id_Siz, JanCode = JuchuShippingSeeder.JanCode, Su = StoreStock }],
		};
		shiire.EnKubun = EnumShiire.Shiire;
		await session.InsertAsync(shiire);

		var screen = session.OpenView<CustomerReservationAllocationInputView, CustomerReservationAllocationInputViewModel>();
		screen.Input("取置:店舗", vm => vm.TenpoCode = seeded.DirectStoreCode);
		await screen.RunAsync("取置:検索", vm => vm.DoSearchCommand);
		if (!session.Check("取置:検索 0件", screen.Vm.Rows.Count == 0, new { rows = screen.Vm.Rows.Count, screen.Vm.Message })) return;

		// 1件目: 2点。期限日の初期値は取置日の1週間後（D11）
		screen.Input("取置:登録欄", vm => {
			vm.EntryCustomerCode = CustomerCode;
			vm.EntryShohinCode = seeded.ShohinCode;
			vm.EntryDenDay = today;
			vm.EntrySu = 2;
		});
		await screen.RunAsync("取置:商品読込", vm => vm.LoadEntryShohinCommand);
		session.Check("取置:色サイズ1件・有効在庫3・期限日は1週間後",
			screen.Vm.EntrySkus.Count == 1 && screen.Vm.EntrySku?.YukoSu == StoreStock && screen.Vm.EntryLimitDay == today.AddDays(7),
			new { skus = screen.Vm.EntrySkus.Count, screen.Vm.EntrySku?.YukoSu, screen.Vm.EntryLimitDay });
		await screen.RunAsync("取置:登録 2点", vm => vm.DoRegisterCommand);
		var first = (await session.QueryAsync<TranHaibun>("where Kubun=6 AND Id_Tenpo=@0 order by Id", seeded.DirectStoreId.ToString())).LastOrDefault();
		if (!session.Check("取置:区分6・顧客・期限日・出庫元=店舗・単価=上代",
			first is { Su: 2, EndFlag: 0 } && first.Id_Customer == customer.Id && first.LimitDay == Ymd(today.AddDays(7))
			&& first.Id_Soko == seeded.DirectStoreId && first.Tanka == first.Jodai,
			new { first?.Su, first?.Id_Customer, first?.LimitDay, first?.Id_Soko, first?.Tanka, first?.Jodai })) return;
		await CheckReserveAsync(session, seeded, "取置:店舗の引当2", 2);

		// 2件目: 有効在庫1に対して5点 → 警告して登録できる（判断 2）
		session.ClearDialogs();
		screen.Input("取置:2件目 5点", vm => vm.EntrySu = 5);
		await screen.RunAsync("取置:登録 5点（在庫超過）", vm => vm.DoRegisterCommand);
		session.Check("取置:有効在庫超過の警告が出る", session.Dialogs.Any(d => d.Request.Message.Contains("有効在庫（1）を超えて")),
			new { dialogs = session.Dialogs.Select(d => d.Request.Message) });
		// 3件目: 期限が明日（期限3日以内の色付け確認用）
		screen.Input("取置:3件目 期限明日", vm => {
			vm.EntrySu = 1;
			vm.EntryLimitDay = today.AddDays(1);
		});
		await screen.RunAsync("取置:登録 期限明日", vm => vm.DoRegisterCommand);
		var rows = screen.Vm.Rows;
		session.Check("取置:取置中3件・期限明日の行は橙", rows.Count == 3 && rows.Count(r => r.IsNearLimit) == 1,
			new { rows = rows.Select(r => new { r.Su, r.LimitDayDisp, r.RemainDaysDisp, r.IsNearLimit }) });
		await CheckReserveAsync(session, seeded, "取置:店舗の引当8", 8);
		await HaibunScreenScenario.CaptureAsync(session, screen.View, screens, "31_ReservationList");

		// 期限変更（1件目）
		var firstRow = rows.Single(r => r.Source.Id == first!.Id);
		firstRow.IsChecked = true;
		screen.Input("取置:新期限 +10日", vm => vm.NewLimitDay = today.AddDays(10));
		await screen.RunAsync("取置:期限変更", vm => vm.ChangeLimitCommand);
		var afterLimit = (await session.QueryAsync<TranHaibun>("where Kubun=6 AND EndFlag=0 AND Id_Tenpo=@0 AND Su=2", seeded.DirectStoreId.ToString())).SingleOrDefault();
		session.Check("取置:期限日を変更（洗い替え）", afterLimit?.LimitDay == Ymd(today.AddDays(10)), new { afterLimit?.LimitDay });

		// 数量変更（5点 → 1点）
		var bigRow = screen.Vm.Rows.Single(r => r.Su == 5);
		bigRow.IsChecked = true;
		screen.Input("取置:新数量1", vm => vm.NewSu = 1);
		await screen.RunAsync("取置:数量変更", vm => vm.ChangeQtyCommand);
		session.Check("取置:数量を1に変更", screen.Vm.Rows.Count(r => r.Su == 5) == 0 && screen.Vm.Rows.Count(r => r.Su == 1) == 2,
			new { rows = screen.Vm.Rows.Select(r => r.Su) });
		await CheckReserveAsync(session, seeded, "取置:店舗の引当4", 4);

		// 売上変換（2点の取置）
		session.ClearDialogs();
		foreach (var r in screen.Vm.Rows) r.IsChecked = r.Su == 2;
		screen.Input("取置:売上日", vm => vm.UriDay = today);
		await screen.RunAsync("取置:売上変換", vm => vm.DoConvertCommand);
		session.Check("取置:売上変換の確認にPOS二重計上の注意", session.Dialogs.Any(d => d.Request.Message.Contains("POS で同じ商品を会計しないでください")),
			new { dialogs = session.Dialogs.Select(d => d.Request.Message) });
		var tenuri = await session.QueryAsync<Tran01Tenuri>("where Id_Tenpo=@0 AND Memo='取置売上'", seeded.DirectStoreId.ToString());
		var converted = (await session.QueryAsync<TranHaibun>("where Kubun=6 AND Id_Tenpo=@0 AND EndReason=1", seeded.DirectStoreId.ToString())).SingleOrDefault();
		session.Check("取置:店舗売上1件（顧客・2点・税込合計）・取置は売上変換で完了",
			tenuri.Count == 1 && tenuri[0].Id_Customer == customer.Id && tenuri[0].SuTotal == 2 && tenuri[0].Id_Soko == seeded.DirectStoreId
			&& tenuri[0].Total == tenuri[0].KingakuTotal + tenuri[0].Tax1 + tenuri[0].Tax2 + tenuri[0].Tax3
			&& converted is { EndFlag: 1, JitsuSu: 2 } && converted.RelateNo2 == (int)tenuri[0].Id && converted.KakuteiDay == Ymd(today),
			new { tenuri = tenuri.Select(t => new { t.Id, t.Id_Customer, t.SuTotal, t.KingakuTotal, t.Tax1, t.Total }), converted?.RelateNo2 });
		await CheckReserveAsync(session, seeded, "取置:店舗の引当2・在庫1", 2, StoreStock - 2);

		// 取消（数量1にした取置）
		var cancelRow = screen.Vm.Rows.First(r => r.Su == 1 && !r.IsNearLimit);
		foreach (var r in screen.Vm.Rows) r.IsChecked = ReferenceEquals(r, cancelRow);
		await screen.RunAsync("取置:取消", vm => vm.CancelReservationCommand);
		var cancelled = (await session.QueryAsync<TranHaibun>("where Id=@0", cancelRow.Source.Id.ToString())).SingleOrDefault();
		session.Check("取置:取消で完了（欠品1・伝票なし）", cancelled is { EndFlag: 1, JitsuSu: 0, ShortSu: 1, RelateNo2: 0 }
			&& cancelled.EndReason == (int)EnumHaibunEndReason.Cancelled,
			new { cancelled?.EndFlag, cancelled?.ShortSu, cancelled?.EndReason });
		await CheckReserveAsync(session, seeded, "取置:店舗の引当1", 1);

		// 期限切れ: 期限が過ぎた取置を置き、日次タスクと同じ処理を複製DBへ直接呼ぶ
		var overdue = await session.InsertAsync(new TranHaibun {
			Kubun = (int)EnumHaibun.Reservation, DenDay = Ymd(today.AddDays(-10)), LimitDay = Ymd(today.AddDays(-3)),
			Id_Tenpo = seeded.DirectStoreId, Id_Soko = seeded.DirectStoreId, Id_Customer = customer.Id,
			Id_Shohin = seeded.ShohinId, Id_Col = seeded.Id_Col, Id_Siz = seeded.Id_Siz, JanCode = JuchuShippingSeeder.JanCode, Su = 1,
		});
		var expired = ExpireOverdue(today);
		var overdueRow = (await session.QueryAsync<TranHaibun>("where Id=@0", overdue.Id.ToString())).SingleOrDefault();
		session.Check("取置:期限切れの自動取消（完了日は期限日の翌日）", expired == 1 && overdueRow is { EndFlag: 1 }
			&& overdueRow.EndReason == (int)EnumHaibunEndReason.Expired && overdueRow.KakuteiDay == Ymd(today.AddDays(-2)),
			new { expired, overdueRow?.EndReason, overdueRow?.KakuteiDay });

		// 完了を含めて表示（状態: 売上変換・取消・期限切れ・取置中）
		screen.Input("取置:状態=完了を含む", vm => vm.StatusIndex = 2);
		await screen.RunAsync("取置:再検索", vm => vm.DoSearchCommand);
		var statuses = screen.Vm.Rows.Select(r => r.StatusText).ToList();
		session.Check("取置:状態の表示（取置中・売上変換・取消・期限切れ）",
			statuses.Count == 4 && statuses.Contains("取置中") && statuses.Contains("売上変換") && statuses.Contains("取消") && statuses.Contains("期限切れ")
			&& screen.Vm.Rows.Single(r => r.StatusText == "売上変換").SlipDisp == tenuri[0].Id.ToString(),
			new { statuses });
		await HaibunScreenScenario.CaptureAsync(session, screen.View, screens, "32_ReservationHistory");

		// 滞留・欠品例外の欠品実績には取置の取消・期限切れを出さない（判断 6）
		var list = session.OpenView<ShippingConfirmListView, ShippingConfirmListViewModel>();
		list.Input("欠品実績:条件", vm => {
			vm.ViewKind = "欠品実績";
			vm.DayFromText = today.AddDays(-30).ToString("yyyy/MM/dd");
			vm.DayToText = today.AddDays(1).ToString("yyyy/MM/dd");
			vm.TokuiCode = seeded.DirectStoreCode;
		});
		await list.RunAsync("欠品実績:検索", vm => vm.SearchCommand);
		session.Check("欠品実績:取置は出ない", list.Vm.Rows.All(r => r.Id != cancelRow.Source.Id && r.Id != overdue.Id),
			new { rows = list.Vm.Rows.Count });

		session.SetDialogResponder(null);
	}

	static async Task CheckReserveAsync(VmSession session, JuchuShippingSeeder.Result seeded, string name, int reserve, int? su = null) {
		var stock = (await session.QueryAsync<SummaryRealStock>("where Id_Soko=@0 AND Id_Shohin=@1 AND Id_Col=@2 AND Id_Siz=@3",
			seeded.DirectStoreId.ToString(), seeded.ShohinId.ToString(), seeded.Id_Col.ToString(), seeded.Id_Siz.ToString())).SingleOrDefault();
		session.Check(name, stock != null && stock.ReserveQty == reserve && (su == null || stock.Su == su),
			new { stock?.Su, stock?.ReserveQty });
	}

	/// <summary>日次タスク「取置期限切れ自動取消」と同じ処理を、UATの複製DBへ直接実行する</summary>
	static int ExpireOverdue(DateTime today) {
		var path = _dbPath ?? throw new InvalidOperationException("DBパスがありません。");
		var cs = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 30 }.ToString();
		using var connection = new SqliteConnection(cs);
		connection.Open();
		var db = new ExDatabaseSqlite(connection) { KeepConnectionAlive = true };
		db.BeginTransaction();
		var count = new ReservationDb(db).ExpireOverdue(today);
		db.CompleteTransaction();
		return count;
	}
}
