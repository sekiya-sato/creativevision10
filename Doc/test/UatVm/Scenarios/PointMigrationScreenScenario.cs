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
using CvWpfclient.ViewModels._00System;
using CvWpfclient.ViewModels._01Master;
using CvWpfclient.ViewModels._32LoyalCustomer;
using CvWpfclient.Views._00System;
using CvWpfclient.Views._01Master;
using CvWpfclient.Views._32LoyalCustomer;
using CvWpfclient.ViewModels.Sub;
using CvWpfclient.Views.Sub;
using Microsoft.Data.Sqlite;

namespace UatVm.Scenarios;

/// <summary>専用DBで旧ポイント履歴の実表示・売上除外・再構築選択の安全境界を確認する。Oracle変換は実行しない。</summary>
public static class PointMigrationScreenScenario {
	static string? databasePath;
	const string CustomerCode = "PMK1";
	const string RebuildTask = "RebuildPointHistory";
	static bool rangeHookRegistered;
	static VmSession? rangeSession;

	public static void Seeder(string dbPath) {
		var path = Path.GetFullPath(dbPath);
		if (!Path.GetFileName(path).StartsWith("point-migration-uat-", StringComparison.OrdinalIgnoreCase)
			|| !path.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
			|| path.Split(Path.DirectorySeparatorChar).Any(x => x.Equals("CvServer", StringComparison.OrdinalIgnoreCase))
			|| (File.Exists(path) && new FileInfo(path).Length > 0))
			throw new InvalidOperationException("未作成/空の専用point-migration-uat-*.dbだけ使用できます。");
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
		connection.Open();
		using var db = new ExDatabaseSqlite(connection) { KeepConnectionAlive = true };
		if (!new DefineDataTable().InitializeAsync(db, false).GetAwaiter().GetResult()) throw new InvalidOperationException("専用DB初期化失敗");
		var customer = new MasterEndCustomer { Code = CustomerCode, Name = "移行確認会員" }; db.Insert(customer);
		db.Insert(new MasterEndCustomerAccount { Id_Customer = customer.Id, Point = 50 });
		db.Insert(new SummaryPoint { Id_Customer = checked((int)customer.Id), Point = 50, SalesCount = 2, SalesKingaku = 2000 });
		var shop = new MasterTokui { Code = "PMS1", Name = "移行確認店舗", TenType = 6 }; db.Insert(shop);
		var product = new MasterShohin { Code = "PMP1", Name = "移行確認商品" }; db.Insert(product);
		db.Insert(new MasterPointBase { Code = "PMBASE", Name = "移行確認基本", DayFrom = "20260101", DayTo = "20261231", IsEnabled = 1,
			PointUnitPrice = 100, PointAmountProper = 1, PointAmountSale = 1 });
		foreach (var (seq, grant, use, expire, memo) in new[] { (1L, 100L, 30L, 20L, "旧付与・使用・失効"), (2L, 0L, 0L, 0L, "旧全ゼロ履歴") }) {
			var source = new LegacyPointHistory { SourceSystem = "UAT", SeqNo = seq, CustomerCode = CustomerCode, Day = "20260901", ShopCode = "PMS1",
				GrantPoints = grant, UsePoints = use, ExpirePoints = expire, Memo = memo };
			db.Insert(new TranPointEvent { EventKey = $"LEGACY:UAT:POINT:{seq}", DenDay = source.Day, Id_Customer = customer.Id, Id_Tenpo = shop.Id,
				EventType = (int)EnumPointEventType.LegacyHistory, PointDelta = grant - use - expire, Jcalc = Common.SerializeObject(source), Memo = memo });
		}
		foreach (var (oldSeq, memo) in new[] { (123L, "PM_LEGACY"), (0L, "PM_NATIVE") })
			db.Insert(new Tran01Tenuri { OldSeqNo = oldSeq, Memo = memo, DenDay = "20261001", Kubun = 10, Id_Customer = customer.Id, Id_Tenpo = shop.Id, Id_Soko = shop.Id,
				Jmeisai = [new Tran99Meisai { Id_Shohin = product.Id, Su = 1, Kingaku = 1000, Tanka = 1000 }] });
		databasePath = path;
	}

	public static async Task RunAsync(VmSession session) {
		if (databasePath is null) throw new InvalidOperationException("専用Seederを実行してください。");
		var screens = Path.Combine(Path.GetDirectoryName(databasePath)!, "point-migration-screens-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
		Directory.CreateDirectory(screens);
		session.Note("対象", new { Database = databasePath, Screens = screens });
		await LedgerAsync(session, screens);
		await RecalcAsync(session, screens);
		await ConversionSelectionAsync(session, screens);
		await CustomerPointAsync(session, screens);
	}

	static async Task LedgerAsync(VmSession session, string screens) {
		var d = session.OpenView<PointLedgerManualView, PointLedgerManualViewModel>();
		try {
			await d.WaitAsync("履歴初期化", vm => !vm.IsBusy);
			d.Input("旧履歴検索条件", vm => { vm.SearchCustomerCode = CustomerCode; vm.SearchDayFrom = "20260101"; vm.SearchDayTo = "20261231"; vm.ManualOnly = false; vm.SearchType = PointLedgerManualViewModel.AllTypes; });
			await d.RunAsync("履歴全件検索", vm => vm.DoSearchCommand);
			session.CheckEqual("全件検索は旧履歴2行", 2, d.Vm.Rows.Count);
			var row = d.Vm.Rows.Single(x => x.Event.EventKey == "LEGACY:UAT:POINT:1");
			var zero = d.Vm.Rows.Single(x => x.Event.EventKey == "LEGACY:UAT:POINT:2");
			session.Check("旧元内訳と純増減", row.TypeName == "旧CV履歴" && row.PointText == "+50" && row.GrantPointText == "100" && row.UsePointText == "30" && row.ExpirePointText == "20");
			session.Check("旧全ゼロも内訳0で表示", zero.PointText == "0" && zero.GrantPointText == "0" && zero.UsePointText == "0" && zero.ExpirePointText == "0");
			d.Input("旧履歴を選択", vm => vm.SelectedRow = row);
			session.Check("旧履歴は手動取消不可", !row.CanCancel && !d.Vm.CancelSelectedCommand.CanExecute(null));
			session.Check("旧履歴は手動登録種別にない", d.Vm.EntryTypeOptions.All(x => x.Key != (int)EnumPointEventType.LegacyHistory));
			d.Input("旧CV履歴のみ", vm => vm.SearchType = (int)EnumPointEventType.LegacyHistory);
			session.CheckEqual("条件変更で旧結果無効", 0, d.Vm.Rows.Count);
			await d.RunAsync("旧CV履歴検索", vm => vm.DoSearchCommand);
			session.CheckEqual("旧CV種別検索2行", 2, d.Vm.Rows.Count);
			var grid = Descendants(d.View).OfType<DataGrid>().Single();
			await Settle(d.View);
			foreach (var (path, expected) in new[] { ("GrantPointText", "100"), ("UsePointText", "30"), ("ExpirePointText", "20") }) {
				var column = grid.Columns.OfType<DataGridTextColumn>().Single(x => x.Binding is Binding b && b.Path.Path == path);
				var item = d.Vm.Rows.Single(x => x.Event.EventKey == "LEGACY:UAT:POINT:1");
				grid.ScrollIntoView(item, column); await Settle(d.View);
				session.CheckEqual("実セルBinding:" + path, expected, (column.GetCellContent(item) as TextBlock)?.Text);
			}
			foreach (var minimum in new[] { false, true }) {
				if (minimum) { d.View.Width = d.View.MinWidth; d.View.Height = d.View.MinHeight; }
				await Settle(d.View);
				var scroll = Descendants(grid).OfType<ScrollViewer>().First(x => x.ScrollableWidth > 0);
				scroll.ScrollToLeftEnd(); await Settle(d.View);
				await Capture(session, d.View, screens, minimum ? "ledger_minimum_left" : "ledger_standard_left", allowGridBoundaries: true);
				// 追加列を含むすべての見出しを横スクロールで実際に到達確認する。
				foreach (var column in grid.Columns.OrderBy(x => x.DisplayIndex)) {
					grid.ScrollIntoView(grid.Items[0], column); await Settle(d.View);
					var header = Descendants(grid).OfType<DataGridColumnHeader>().FirstOrDefault(x => x.Column == column);
					session.Check((minimum ? "最小" : "標準") + ":列到達:" + column.Header, header != null && VisibleInsideGrid(header, grid), new { column.Header });
				}
				scroll.ScrollToRightEnd(); await Settle(d.View);
				session.Check((minimum ? "最小" : "標準") + ":右端到達", scroll.HorizontalOffset >= scroll.ScrollableWidth - 1);
				await Capture(session, d.View, screens, minimum ? "ledger_minimum_right" : "ledger_standard_right", allowGridBoundaries: true);
			}
		} finally { d.View.Close(); }
	}

	static async Task RecalcAsync(VmSession session, string screens) {
		var before = await PointFlowScenario.Events(session);
		var d = session.OpenView<PointSummaryView, PointSummaryViewModel>();
		session.SetDialogResponder(r => r.Button == MessageBoxButton.YesNo ? MessageBoxResult.Yes : MessageBoxResult.OK);
		try {
			d.Input("CV10売上のみ再計算", vm => { vm.YearMonthFrom = "2026/10"; vm.YearMonthTo = "2026/10"; });
			await d.RunAsync("再計算", vm => vm.ExecuteCommand);
			session.Check("再計算画面完了", d.Vm.StatusMessage.Contains("完了") && d.Vm.ProgressValue == 100, d.Vm.StatusMessage);
			var after = await PointFlowScenario.Events(session);
			var legacy = after.Where(x => x.EventType == (int)EnumPointEventType.LegacyHistory).ToList();
			session.Check("旧履歴・Jcalc・増減不変", legacy.Count == 2 && legacy.All(x => before.Any(old => old.Id == x.Id && old.Jcalc == x.Jcalc && old.PointDelta == x.PointDelta)));
			var oldSlip = (await session.QueryAsync<Tran01Tenuri>("WHERE Memo='PM_LEGACY'")).Single();
			var native = (await session.QueryAsync<Tran01Tenuri>("WHERE Memo='PM_NATIVE'")).Single();
			session.Check("旧売上除外/nativeだけ付与", oldSlip.GrantPoint == 0 && native.GrantPoint == 10 && after.All(x => x.Id_Tenuri != oldSlip.Id));
			var customer = await PointFlowScenario.One<MasterEndCustomer>(session, CustomerCode);
			await PointFlowScenario.CheckBalance(session, "再計算後", customer.Id, 60);
			await d.RunAsync("再計算再実行", vm => vm.ExecuteCommand);
			session.CheckEqual("再計算再実行は追記0", after.Count, (await PointFlowScenario.Events(session)).Count);
			await Capture(session, d.View, screens, "summary_after_recalc");
		} finally { session.SetDialogResponder(null); d.View.Close(); }
	}

	static async Task ConversionSelectionAsync(VmSession session, string screens) {
		var d = session.OpenView<ConvertSelectedView, ConvertSelectedViewModel>();
		session.SetDialogResponder(VmSession.StrictResponder);
		try {
			if (!await d.WaitAsync("変換一覧読取", vm => vm.Tasks.Count > 0)) return;
			var task = d.Vm.Tasks.Single(x => x.Name == RebuildTask);
			d.Run("変換全選択", vm => vm.SelectAllCommand);
			session.Check("全選択は全再構築を除外", !task.IsSelected && d.Vm.Tasks.Where(x => x.Name != RebuildTask).All(x => x.IsSelected));
			d.Input("再構築と他タスクの混在", vm => { task.IsSelected = true; vm.IsInitDb = true; });
			session.ClearDialogs(); await d.RunAsync("混在実行拒否", vm => vm.ExecuteCommand);
			session.Check("混在は警告/未実行", !d.Vm.HasExecuted && session.Dialogs.Any(x => x.Request.Message.Contains("単独")));
			d.Run("選択解除", vm => vm.ClearSelectionCommand);
			d.Input("再構築単独/初期化なし", vm => { task.IsSelected = true; vm.IsInitDb = false; });
			session.ClearDialogs(); await d.RunAsync("初期化なし拒否", vm => vm.ExecuteCommand);
			session.Check("初期化なしは警告/未実行", !d.Vm.HasExecuted && session.Dialogs.Any(x => x.Request.Message.Contains("初期化")));
			d.Input("再構築単独/初期化あり", vm => vm.IsInitDb = true);
			session.ClearDialogs(); var before = (await PointFlowScenario.Events(session)).Count;
			await d.RunAsync("再構築確認はNo", vm => vm.ExecuteCommand);
			session.Check("専用警告で全削除/複製DB/初回切替を明示", session.Dialogs.Any(x => x.Request.Message.Contains("全ポイント履歴を削除") && x.Request.Message.Contains("複製DB") && x.Request.Message.Contains("初回切替")));
			session.Check("Noで変換未実行/台帳不変", !d.Vm.HasExecuted && !d.Vm.IsRunning && d.Vm.StreamMessages.Count == 0 && (await PointFlowScenario.Events(session)).Count == before);
			var grid = Descendants(d.View).OfType<DataGrid>().Single();
			foreach (var minimum in new[] { false, true }) {
				// NoResizeのため利用者の最小サイズは標準と同じ。設定上のMin値も証跡へ残す。
				if (minimum && d.View.ResizeMode != ResizeMode.NoResize) { d.View.Width = d.View.MinWidth; d.View.Height = d.View.MinHeight; }
				grid.ScrollIntoView(task); await Settle(d.View);
				var check = Descendants(grid).OfType<CheckBox>().Single(x => ReferenceEquals(x.DataContext, task));
				var execute = Descendants(d.View).OfType<Button>().Single(x => ReferenceEquals(x.Command, d.Vm.ExecuteCommand));
				session.Check((minimum ? "変換最小" : "変換標準") + ":全再構築選択と実行ボタン到達", WithinView(check, d.View) && WithinView(execute, d.View), new { d.View.ActualWidth, d.View.ActualHeight, d.View.MinWidth, d.View.MinHeight, Resize = d.View.ResizeMode.ToString() });
				await Capture(session, d.View, screens, minimum ? "conversion_minimum_fixed" : "conversion_standard", checkLayout: false);
			}
		} finally { session.SetDialogResponder(null); d.View.Close(); }
	}

	static async Task CustomerPointAsync(VmSession session, string screens) {
		rangeSession = session;
		EnsureRangeAnswered();
		var d = session.OpenView<MasterEndCustomerMenteView, MasterEndCustomerMenteViewModel>();
		try {
			await d.WaitAsync("顧客一覧", vm => vm.ListData.Any(x => x.Code == CustomerCode));
			d.Input("移行会員を選択", vm => vm.Current = vm.ListData.Single(x => x.Code == CustomerCode));
			await d.WaitAsync("顧客詳細読取", vm => !vm.IsDetailLoading && vm.CurrentEdit.Code == CustomerCode);
			await d.WaitAsync("会員ポイント読取", vm => vm.AccountPointText == "60");
			await Settle(d.View);
			var box = Descendants(d.View).OfType<TextBox>().Single(x => x.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == "AccountPointText");
			box.BringIntoView(); await Settle(d.View);
			var binding = box.GetBindingExpression(TextBox.TextProperty)!;
			session.Check("実ポイントTextBoxはreadonly/OneWay", box.IsReadOnly && binding.ParentBinding.Mode == BindingMode.OneWay);
			session.Check("実ポイントBinding成立", !binding.HasError && binding.Status == BindingStatus.Active, new { Path = binding.ParentBinding.Path.Path, Status = binding.Status.ToString() });
			session.CheckEqual("実会員残高表示", "60", box.Text);
			await Capture(session, d.View, screens, "customer_point_readonly", checkLayout: false);
			session.Note("顧客メンテ既存項目", "今回対象は会員ポイント欄。既存SalesCount等のBinding全体は検証対象外。");
			var selected = d.Vm.CurrentEdit;
			d.Input("会員ポイント再読込開始", vm => vm.CurrentEdit = Common.CloneObject(selected));
			d.Input("読込待ちから新規へ切替", vm => vm.CurrentEdit = new MasterEndCustomer());
			session.CheckEqual("新規は即座に残高0へ戻す", "0", d.Vm.AccountPointText);
			await Task.Delay(500); await Settle(d.View);
			session.CheckEqual("旧読込で新規残高を上書きしない", "0", d.Vm.AccountPointText);
			session.CheckEqual("実TextBoxも新規0", "0", box.Text);
		} finally { d.View.Close(); }
	}

	static void EnsureRangeAnswered() {
		if (rangeHookRegistered) return;
		rangeHookRegistered = true;
		EventManager.RegisterClassHandler(typeof(RangeParamView), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, e) => {
			if (rangeSession != null && sender is RangeParamView view && view.DataContext is RangeParamViewModel vm)
				_ = AnswerRangeAsync(view, vm);
		}));
	}
	static async Task AnswerRangeAsync(RangeParamView view, RangeParamViewModel vm) {
		try {
			await Settle(view);
			rangeSession?.Note("顧客一覧条件Win", new { vm.Parameter.DisplayName });
		} finally { vm.OkCommand.Execute(null); }
	}

	static async Task Capture(VmSession session, Window view, string screens, string name, bool allowGridBoundaries = false, bool checkLayout = true) {
		await Settle(view); await Task.Delay(150);
		session.Note(name + ":画面画像", new { Path = ScreenLayoutCheck.SaveJpeg(view, screens, name) });
		if (!checkLayout) return;
		var issues = ScreenLayoutCheck.Inspect(view).Where(x => x.Kind != ScreenLayoutCheck.LongCellKind && (!allowGridBoundaries || x.Kind != "列が横スクロール外")).ToList();
		session.Check(name + ":layout", issues.Count == 0, new { Issues = issues });
	}
	static bool WithinView(FrameworkElement element, Window view) {
			var root = (FrameworkElement)view.Content;
			var bounds = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
			return element.IsVisible && bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= -1 && bounds.Top >= -1
				&& bounds.Right <= root.ActualWidth + 1 && bounds.Bottom <= root.ActualHeight + 1;
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
