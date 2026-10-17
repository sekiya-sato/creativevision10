using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using CvWpfclient.ViewModels._32LoyalCustomer;
using CvWpfclient.Views._32LoyalCustomer;
using Microsoft.Data.Sqlite;

namespace UatVm.Scenarios;

/// <summary>専用DBで顧客カルテ（顧客検索→ポイント履歴照会）の検索・退会除外・累計残高・表示絞込み・残高不一致・旧結果無効化と画面レイアウトを確認する。照会専用で更新しない。</summary>
public static class CustomerProfileScenario {
	static string? databasePath;
	const string CodeA = "CPK1";
	const string CodeB = "CPK2";
	const string CodeC = "CPK3";
	const string ShopCode = "CPS1";

	public static void Seeder(string dbPath) {
		var path = Path.GetFullPath(dbPath);
		if (!Path.GetFileName(path).StartsWith("customer-profile-uat-", StringComparison.OrdinalIgnoreCase)
			|| !path.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
			|| path.Split(Path.DirectorySeparatorChar).Any(x => x.Equals("CvServer", StringComparison.OrdinalIgnoreCase))
			|| (File.Exists(path) && new FileInfo(path).Length > 0))
			throw new InvalidOperationException("未作成/空の専用customer-profile-uat-*.dbだけ使用できます。");
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
		connection.Open();
		using var db = new ExDatabaseSqlite(connection) { KeepConnectionAlive = true };
		if (!new DefineDataTable().InitializeAsync(db, false).GetAwaiter().GetResult()) throw new InvalidOperationException("専用DB初期化失敗");
		var shop = new MasterTokui { Code = ShopCode, Name = "カルテ確認店舗", TenType = 6 }; db.Insert(shop);
		var vTenpo = new CodeNameView { Sid = shop.Id, Cd = shop.Code, Mei = shop.Name };

		// A: 旧CV履歴（日付不明「.」と内訳付き）・手動付与とその取消・調整。台帳合計=集計=会員=70
		var a = new MasterEndCustomer { Code = CodeA, Name = "カルテ確認 太郎", Kana = "カルテカクニンタロウ", Tel = "03-1111-2222", Id_Tenpo = shop.Id, VTenpo = vTenpo, Rank = "A" };
		db.Insert(a);
		db.Insert(new MasterEndCustomerAccount { Id_Customer = a.Id, Point = 70, PointRank = "GOLD" });
		db.Insert(new SummaryPoint { Id_Customer = checked((int)a.Id), Point = 70 });
		foreach (var (seq, day, grant, use, expire) in new[] { (1L, ".", 10L, 0L, 0L), (2L, "20260901", 100L, 30L, 20L) }) {
			var source = new LegacyPointHistory { SourceSystem = "UAT", SeqNo = seq, CustomerCode = CodeA, Day = day, ShopCode = ShopCode,
				GrantPoints = grant, UsePoints = use, ExpirePoints = expire, Memo = "旧履歴" + seq };
			db.Insert(new TranPointEvent { EventKey = $"LEGACY:UAT:CP:{seq}", DenDay = day, Id_Customer = a.Id, Id_Tenpo = shop.Id,
				EventType = (int)EnumPointEventType.LegacyHistory, PointDelta = grant - use - expire, Jcalc = Common.SerializeObject(source), Memo = "旧履歴" + seq });
		}
		var grantRow = new TranPointEvent { EventKey = "MANUAL:CP-A1", DenDay = "20261001", Id_Customer = a.Id, EventType = (int)EnumPointEventType.Grant, PointDelta = 30, Memo = "手動付与" };
		db.Insert(grantRow);
		db.Insert(new TranPointEvent { EventKey = $"MANUAL:C:{grantRow.Id}", DenDay = "20261002", Id_Customer = a.Id, EventType = (int)EnumPointEventType.Cancel,
			PointDelta = -30, Id_OriginalEvent = grantRow.Id, Memo = $"取消(元Id={grantRow.Id}) 手動付与" });
		db.Insert(new TranPointEvent { EventKey = "MANUAL:CP-A2", DenDay = "20261003", Id_Customer = a.Id, EventType = (int)EnumPointEventType.Adjustment, PointDelta = 10, Memo = "調整" });

		// B: 退会会員。台帳5・集計0・会員5で不一致
		var b = new MasterEndCustomer { Code = CodeB, Name = "カルテ確認 花子", Kana = "カルテカクニンハナコ", Tel = "06-3333-4444" };
		db.Insert(b);
		db.Insert(new MasterEndCustomerAccount { Id_Customer = b.Id, Point = 5, IsWithdrawalFlag = 1, WithdrawnDate = "20260930" });
		db.Insert(new SummaryPoint { Id_Customer = checked((int)b.Id), Point = 0 });
		db.Insert(new TranPointEvent { EventKey = "MANUAL:CP-B1", DenDay = "20260915", Id_Customer = b.Id, EventType = (int)EnumPointEventType.Grant, PointDelta = 5, Memo = "退会前付与" });

		// C: 会員情報・集計・履歴なし
		db.Insert(new MasterEndCustomer { Code = CodeC, Name = "カルテ確認 履歴なし", Kana = "カルテカクニンリレキナシ" });
		databasePath = path;
	}

	public static async Task RunAsync(VmSession session) {
		if (databasePath is null) throw new InvalidOperationException("専用Seederを実行してください。");
		var screens = Path.Combine(Path.GetDirectoryName(databasePath)!, "customer-profile-screens-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
		Directory.CreateDirectory(screens);
		session.Note("対象", new { Database = databasePath, Screens = screens });
		var d = session.OpenView<EndCustomerProfileView, EndCustomerProfileViewModel>();
		try {
			await d.WaitAsync("初期表示", vm => !vm.IsBusy);
			await d.RunAsync("条件なし検索は拒否", vm => vm.DoSearchCommand);
			session.Check("条件なしは検索しない", d.Vm.Customers.Count == 0 && d.Vm.Message.Contains("検索条件"), new { d.Vm.Message });

			d.Input("氏名検索", vm => vm.SearchName = "カルテ確認");
			await d.RunAsync("氏名検索実行", vm => vm.DoSearchCommand);
			session.CheckEqual("退会者を除いた2件", 2, d.Vm.Customers.Count);
			session.Check("退会除外の件数表示", d.Vm.Message.Contains("退会会員 1 件"), new { d.Vm.Message });

			d.Input("退会者を含む", vm => vm.IncludeWithdrawn = true);
			session.CheckEqual("条件変更で旧一覧を外す", 0, d.Vm.Customers.Count);
			await d.RunAsync("退会者込み検索", vm => vm.DoSearchCommand);
			session.CheckEqual("退会者込み3件", 3, d.Vm.Customers.Count);
			session.Check("退会表示", d.Vm.Customers.Single(x => x.Customer.Code == CodeB).WithdrawnText == "退会 2026/09/30");

			// A: 累計残高と三残高一致
			d.Input("A選択", vm => vm.SelectedCustomer = vm.Customers.Single(x => x.Customer.Code == CodeA));
			await d.WaitAsync("A履歴取得", vm => vm.History.Count == 5);
			session.Check("A残高一致", !d.Vm.IsBalanceMismatch && d.Vm.BalanceText.StartsWith("台帳合計 70 P"), new { d.Vm.BalanceText });
			session.Check("A顧客情報", d.Vm.CustomerTitle.Contains(CodeA) && d.Vm.CustomerInfo.Contains("所属店舗: CPS1") && d.Vm.CustomerInfo.Contains("ポイントランク: GOLD"), new { d.Vm.CustomerInfo });
			session.CheckEqual("A累計残高", "10,60,90,60,70", string.Join(",", d.Vm.History.Select(x => x.BalanceText)));
			session.Check("日付不明行は先頭で原値表示", d.Vm.History[0].DayText == ".");
			session.Check("旧CV内訳", d.Vm.History[1].Ledger.GrantPointText == "100" && d.Vm.History[1].Ledger.UsePointText == "30" && d.Vm.History[1].Ledger.ExpirePointText == "20");
			session.Check("取消済と取消元", d.Vm.History[2].Ledger.CancelledText == "取消済" && d.Vm.History[3].OriginalText == d.Vm.History[2].Ledger.Event.Id.ToString());
			session.Check("店舗表示", d.Vm.History[1].TenpoText == "CPS1 カルテ確認店舗");

			d.Input("期間指定", vm => vm.FilterDayFrom = "20261001");
			session.CheckEqual("期間内3行", 3, d.Vm.History.Count);
			session.CheckEqual("期間指定でも累計は繰越込み", "90", d.Vm.History[0].BalanceText);
			session.Check("日付不明非表示の注記", d.Vm.HistoryCountText.Contains("日付不明 1 件"), new { d.Vm.HistoryCountText });
			d.Input("種別=調整", vm => vm.FilterType = (int)EnumPointEventType.Adjustment);
			session.Check("種別絞込み", d.Vm.History.Count == 1 && d.Vm.History[0].BalanceText == "70");
			d.Input("絞込み解除", vm => vm.ClearFilterCommand.Execute(null));
			session.CheckEqual("解除で全5行", 5, d.Vm.History.Count);

			await LayoutAsync(session, d.View, screens);

			// B: 退会・残高不一致
			d.Input("B選択", vm => vm.SelectedCustomer = vm.Customers.Single(x => x.Customer.Code == CodeB));
			await d.WaitAsync("B履歴取得", vm => vm.History.Count == 1);
			session.Check("B不一致警告", d.Vm.IsBalanceMismatch && d.Vm.DetailMessage.Contains("一致しません"), new { d.Vm.BalanceText, d.Vm.DetailMessage });
			session.Check("B退会情報", d.Vm.CustomerInfo.Contains("退会 2026/09/30"), new { d.Vm.CustomerInfo });
			await Capture(session, d.View, screens, "customer_b_mismatch", allowGridBoundaries: true);

			// A→C を連続選択し、遅れて届く A の結果で C を上書きしない
			d.Input("A選択直後にC選択", vm => {
				vm.SelectedCustomer = vm.Customers.Single(x => x.Customer.Code == CodeA);
				vm.SelectedCustomer = vm.Customers.Single(x => x.Customer.Code == CodeC);
			});
			await d.WaitAsync("C残高表示", vm => vm.BalanceText.Length > 0);
			await Task.Delay(500); await Settle(d.View);
			session.Check("Cは履歴なし表示・A結果で上書きなし", d.Vm.CustomerTitle.Contains(CodeC) && d.Vm.History.Count == 0 && d.Vm.BalanceText == "ポイント履歴なし" && d.Vm.CustomerInfo.Contains("会員情報なし"),
				new { d.Vm.CustomerTitle, d.Vm.BalanceText, Rows = d.Vm.History.Count });

			// コード完全一致1件は自動選択。電話・店舗検索
			d.Input("コード検索", vm => { vm.SearchName = string.Empty; vm.SearchCode = CodeA; });
			session.Check("条件変更で詳細も外す", !d.Vm.HasCustomer && d.Vm.History.Count == 0);
			await d.RunAsync("コード検索実行", vm => vm.DoSearchCommand);
			await d.WaitAsync("1件自動選択", vm => vm.SelectedCustomer?.Customer.Code == CodeA && vm.History.Count == 5);
			d.Input("電話検索", vm => { vm.SearchCode = string.Empty; vm.SearchTel = "3333"; });
			await d.RunAsync("電話検索実行", vm => vm.DoSearchCommand);
			session.Check("電話部分一致", d.Vm.Customers.Count == 1 && d.Vm.Customers[0].Customer.Code == CodeB);
			d.Input("店舗検索", vm => { vm.SearchTel = string.Empty; vm.SearchTenpoCode = ShopCode; });
			await d.RunAsync("店舗検索実行", vm => vm.DoSearchCommand);
			session.Check("所属店舗で絞込み", d.Vm.Customers.Count == 1 && d.Vm.Customers[0].Customer.Code == CodeA && d.Vm.SearchTenpoName == "カルテ確認店舗");
			d.Input("存在しない店舗", vm => vm.SearchTenpoCode = "NOSHOP");
			await d.RunAsync("存在しない店舗検索", vm => vm.DoSearchCommand);
			session.Check("店舗なしは警告", d.Vm.Customers.Count == 0 && d.Vm.Message.Contains("NOSHOP"), new { d.Vm.Message });
		} finally { d.View.Close(); }
	}

	/// <summary>標準・最小サイズで両一覧の右端列まで到達できることを確認し、画像を保存する</summary>
	static async Task LayoutAsync(VmSession session, Window view, string screens) {
		var grids = Descendants(view).OfType<DataGrid>().ToList();
		foreach (var minimum in new[] { false, true }) {
			if (minimum) { view.Width = view.MinWidth; view.Height = view.MinHeight; }
			var size = minimum ? "minimum" : "standard";
			await Capture(session, view, screens, $"profile_{size}_left", allowGridBoundaries: true);
			foreach (var grid in grids) {
				var name = grid.GetBindingExpression(ItemsControl.ItemsSourceProperty)?.ParentBinding.Path.Path ?? "grid";
				if (grid.Items.Count == 0) continue;
				foreach (var column in grid.Columns.OrderBy(x => x.DisplayIndex)) {
					grid.ScrollIntoView(grid.Items[0], column); await Settle(view);
					var header = Descendants(grid).OfType<DataGridColumnHeader>().FirstOrDefault(x => x.Column == column);
					session.Check($"{size}:{name}:列到達:{column.Header}", header != null && VisibleInsideGrid(header, grid));
				}
				var scroll = Descendants(grid).OfType<ScrollViewer>().FirstOrDefault(x => x.ScrollableWidth > 0);
				if (scroll != null) {
					scroll.ScrollToRightEnd(); await Settle(view);
					session.Check($"{size}:{name}:右端到達", scroll.HorizontalOffset >= scroll.ScrollableWidth - 1);
				}
			}
			await Capture(session, view, screens, $"profile_{size}_right", allowGridBoundaries: true);
			foreach (var scroll in grids.SelectMany(g => Descendants(g).OfType<ScrollViewer>())) scroll.ScrollToLeftEnd();
		}
		view.Width = 1244; view.Height = 860;
		await Settle(view);
	}

	static async Task Capture(VmSession session, Window view, string screens, string name, bool allowGridBoundaries = false) {
		await Settle(view); await Task.Delay(150);
		session.Note(name + ":画面画像", new { Path = ScreenLayoutCheck.SaveJpeg(view, screens, name) });
		var issues = ScreenLayoutCheck.Inspect(view).Where(x => x.Kind != ScreenLayoutCheck.LongCellKind && (!allowGridBoundaries || x.Kind != "列が横スクロール外")).ToList();
		session.Check(name + ":layout", issues.Count == 0, new { Issues = issues });
	}
	static bool VisibleInsideGrid(FrameworkElement header, DataGrid grid) {
		var bounds = header.TransformToAncestor(grid).TransformBounds(new Rect(header.RenderSize));
		return header.IsVisible && bounds.Width > 0 && bounds.Left >= -1 && bounds.Right <= grid.ActualWidth + 1;
	}
	static async Task Settle(Window view) {
		view.UpdateLayout(); await view.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
	}
	static IEnumerable<DependencyObject> Descendants(DependencyObject parent) {
		for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
			var child = VisualTreeHelper.GetChild(parent, i); yield return child;
			foreach (var nested in Descendants(child)) yield return nested;
		}
	}
}
