using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using CvWpfclient.Helpers;
using CvWpfclient.Models;
using CvWpfclient.ViewModels._32LoyalCustomer;
using CvWpfclient.Views._32LoyalCustomer;
using Microsoft.Data.Sqlite;

namespace UatVm.Scenarios;

/// <summary>独立DBでポイント3マスタの実画面CRUD・入力検証・使用済み保護を確認する。</summary>
public static class PointMasterScenario {
	static string? databasePath;

	public static void Seeder(string dbPath) {
		var path = Path.GetFullPath(dbPath);
		if (!Path.GetFileName(path).StartsWith("point-master-uat-", StringComparison.OrdinalIgnoreCase)
			|| !path.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
			|| path.Split(Path.DirectorySeparatorChar).Any(x => x.Equals("CvServer", StringComparison.OrdinalIgnoreCase))
			|| (File.Exists(path) && new FileInfo(path).Length > 0))
			throw new InvalidOperationException("未作成/空の専用point-master-uat-*.dbだけ使用できます。");
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
		connection.Open();
		using var db = new ExDatabaseSqlite(connection) { KeepConnectionAlive = true };
		if (!new DefineDataTable().InitializeAsync(db, false).GetAwaiter().GetResult()) throw new InvalidOperationException("専用DB初期化失敗");
		var basis = Base("USED"); db.Insert(basis);
		var rank = Rank(basis.Id); db.Insert(rank);
		var bonus = Bonus(basis.Id, "USED-B"); db.Insert(bonus);
		// 製品の台帳汎用書込みを使わず、サーバ起動前に使用済み状態だけ用意する。
		db.Insert(new TranPointEvent { EventKey = "uat:used", DenDay = "20261006", Id_Customer = 1,
			Id_PointBase = basis.Id, Id_PointRank = rank.Id, Id_PointBonus = bonus.Id, EventType = 1, PointDelta = 5 });
		databasePath = path;
	}

	public static async Task RunAsync(VmSession session) {
		if (databasePath is null) throw new InvalidOperationException("専用Seederを実行してください。");
		var screens = Path.Combine(Path.GetDirectoryName(databasePath)!, "point-master-screens-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
		Directory.CreateDirectory(screens);
		session.Note("対象", new { Database = databasePath, Screens = screens });
		session.SetDialogResponder(request => request.Button is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel ? MessageBoxResult.Yes : MessageBoxResult.OK);
		try {
			var basis = session.OpenView<PointMasterBaseAdminView, PointMasterBaseAdminViewModel>();
			var rank = session.OpenView<PointMasterRankView, PointMasterRankViewModel>();
			var bonus = session.OpenView<PointMasterBonusView, PointMasterBonusViewModel>();
			await Ready(basis); await Ready(rank); await Ready(bonus);
			await basis.RunAsync("ベース一覧", vm => vm.DoListCommand);
			await rank.RunAsync("ランク一覧", vm => vm.DoListCommand);
			await bonus.RunAsync("ボーナス一覧", vm => vm.DoListCommand);
			session.Check("3画面の初期一覧", basis.Vm.ListData.Count == 1 && rank.Vm.ListData.Count == 1 && bonus.Vm.ListData.Count == 1);
			var customerMenu = MenuData.CreateDefault().Single(x => x.Header.Contains("顧客管理", StringComparison.Ordinal));
			var pointMenu = customerMenu.SubItems!.Single(x => x.Header.Contains("ポイント", StringComparison.Ordinal));
			var menuTypes = new[] { typeof(PointMasterBaseAdminView), typeof(PointMasterRankView), typeof(PointMasterBonusView) };
			session.Check("顧客管理ポイントに3画面の実行メニュー", menuTypes.All(type => pointMenu.SubItems!.Any(x => x.ViewType == type && x.IsExecutable && x.AddInfo != "準備中")));

			basis.Input("付与単価0", vm => { vm.CurrentEdit = Base("UAT"); vm.CurrentEdit.PointUnitPrice = 0; });
			await Reject(session, "ベース単価0拒否", () => basis.RunAsync("追加", vm => vm.DoInsertCommand));
			session.Check("検証失敗でも入力保持", basis.Vm.CurrentEdit.Code == "UAT" && basis.Vm.CurrentEdit.PointUnitPrice == 0);
			basis.Input("不正日付", vm => { vm.CurrentEdit = Base("UAT"); vm.CurrentEdit.DayFrom = "20260230"; });
			await Reject(session, "実在しない日付拒否", () => basis.RunAsync("追加", vm => vm.DoInsertCommand));
			basis.Input("ベース正常値", vm => vm.CurrentEdit = Base("UAT"));
			await basis.RunAsync("ベース追加", vm => vm.DoInsertCommand);
			var savedBase = (await session.QueryAsync<MasterPointBase>("WHERE Code='UAT'")).Single();
			session.Check("ベース登録・付与0・丸め0保持", savedBase.Id > 0 && savedBase.Rounding == (int)EnumRounding.Round && savedBase.PointAmountSale == 0);
			basis.Input("名称修正", vm => { vm.Current = vm.ListData.Single(x => x.Id == savedBase.Id); vm.CurrentEdit.Name = "ベース修正"; });
			await basis.RunAsync("ベース修正", vm => vm.DoUpdateCommand);
			session.CheckEqual("ベース修正保存", "ベース修正", (await session.QueryAsync<MasterPointBase>("WHERE Code='UAT'")).Single().Name);
			var priceInput = Descendants(basis.View).OfType<TextBox>().Single(x => x.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == "CurrentEdit.PointUnitPrice");
			priceInput.Text = "invalid";
			priceInput.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
			session.Check("実TextBoxの数値変換エラー", Validation.GetHasError(priceInput));
			await Reject(session, "変換失敗時に旧モデル値を保存しない", () => basis.RunAsync("修正", vm => vm.DoUpdateCommand));
			session.CheckEqual("変換失敗後の保存単価維持", 100L, (await session.QueryAsync<MasterPointBase>("WHERE Code='UAT'")).Single().PointUnitPrice);
			priceInput.Text = "100";
			priceInput.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
			basis.Input("境界日の版重複", vm => { vm.CurrentEdit = Base("UAT"); vm.CurrentEdit.Version = 2; vm.CurrentEdit.DayFrom = "20261031"; vm.CurrentEdit.DayTo = "20261130"; });
			await Reject(session, "有効版の境界日重複拒否", () => basis.RunAsync("追加", vm => vm.DoInsertCommand));
			session.CheckEqual("重複登録なし", 1, (await session.QueryAsync<MasterPointBase>("WHERE Code='UAT'")).Count);
			await rank.RunAsync("親追加後ランク一覧更新", vm => vm.DoListCommand);
			await bonus.RunAsync("親追加後ボーナス一覧更新", vm => vm.DoListCommand);

			rank.Input("親未設定", vm => vm.CurrentEdit = Rank(0));
			await Reject(session, "ランク親未設定拒否", () => rank.RunAsync("追加", vm => vm.DoInsertCommand));
			rank.Input("存在しない親", vm => vm.CurrentEdit = Rank(999999));
			await Reject(session, "ランク親不存在拒否", () => rank.RunAsync("追加", vm => vm.DoInsertCommand));
			rank.Input("親を指定", vm => vm.CurrentEdit = Rank(savedBase.Id));
			await rank.RunAsync("ランク追加", vm => vm.DoInsertCommand);
			var savedRank = (await session.QueryAsync<MasterPointRank>($"WHERE Id_PointBase={savedBase.Id}")).Single();
			session.Check("ランク親参照・付与0保存", savedRank.Id_PointBase == savedBase.Id && savedRank.Kubun == 2 && savedRank.PointAmountSale == 0);
			rank.Input("ランク名称修正", vm => { vm.Current = vm.ListData.Single(x => x.Id == savedRank.Id); vm.CurrentEdit.Name = "ランク修正"; });
			await rank.RunAsync("ランク修正", vm => vm.DoUpdateCommand);
			session.CheckEqual("ランク修正保存", "ランク修正", (await session.QueryAsync<MasterPointRank>($"WHERE Id={savedRank.Id}")).Single().Name);

			bonus.Input("親期間外", vm => { vm.CurrentEdit = Bonus(savedBase.Id, "UAT-B"); vm.CurrentEdit.DayTo = "20261101"; });
			await Reject(session, "ボーナス親期間外拒否", () => bonus.RunAsync("追加", vm => vm.DoInsertCommand));
			bonus.Input("回数上限0", vm => { vm.CurrentEdit = Bonus(savedBase.Id, "UAT-B"); vm.CurrentEdit.LimitCount = 0; });
			await Reject(session, "回数上限0拒否", () => bonus.RunAsync("追加", vm => vm.DoInsertCommand));
			bonus.Input("存在しない対象ランク", vm => { vm.CurrentEdit = Bonus(savedBase.Id, "UAT-B"); vm.CurrentEdit.IsAllRanks = 0; vm.CurrentEdit.RankKubun = 99; });
			await Reject(session, "対象ランク不存在拒否", () => bonus.RunAsync("追加", vm => vm.DoInsertCommand));
			bonus.Input("期間1日・指定ランク", vm => { vm.CurrentEdit = Bonus(savedBase.Id, "UAT-B"); vm.CurrentEdit.IsAllRanks = 0; vm.CurrentEdit.RankKubun = 2; });
			await bonus.RunAsync("ボーナス追加", vm => vm.DoInsertCommand);
			var savedBonus = (await session.QueryAsync<MasterPointBonus>("WHERE Code='UAT-B'")).Single();
			session.Check("ボーナス親・限定ランク0・1日境界保存", savedBonus.Id_PointBase == savedBase.Id && savedBonus.IsAllRanks == 0 && savedBonus.RankKubun == 2 && savedBonus.DayFrom == savedBonus.DayTo);
			bonus.Input("追加付与数修正", vm => { vm.Current = vm.ListData.Single(x => x.Id == savedBonus.Id); vm.CurrentEdit.PointAmount = 8; });
			await bonus.RunAsync("ボーナス修正", vm => vm.DoUpdateCommand);
			session.CheckEqual("ボーナス修正保存", 8L, (await session.QueryAsync<MasterPointBonus>("WHERE Code='UAT-B'")).Single().PointAmount);

			basis.Vm.Current = basis.Vm.ListData.Single(x => x.Id == savedBase.Id);
			rank.Vm.Current = rank.Vm.ListData.Single(x => x.Id == savedRank.Id);
			bonus.Vm.Current = bonus.Vm.ListData.Single(x => x.Id == savedBonus.Id);
			// 同Idの再選択は基底が入力を保持するので、取消した編集値を明示的に読み直す。
			basis.Vm.CurrentEdit = Common.CloneObject(basis.Vm.Current);
			rank.Vm.CurrentEdit = Common.CloneObject(rank.Vm.Current);
			bonus.Vm.CurrentEdit = Common.CloneObject(bonus.Vm.Current);
			await CheckLayout(session, basis.View, screens, "01_Base");
			await CheckLayout(session, rank.View, screens, "02_Rank");
			await CheckLayout(session, bonus.View, screens, "03_Bonus");
			await Reject(session, "子のあるベース削除拒否", () => basis.RunAsync("削除", vm => vm.DoDeleteCommand));
			await Reject(session, "対象ボーナスのあるランク削除拒否", () => rank.RunAsync("削除", vm => vm.DoDeleteCommand));
			await bonus.RunAsync("ボーナス削除", vm => vm.DoDeleteCommand);
			await rank.RunAsync("ランク削除", vm => vm.DoDeleteCommand);
			await basis.RunAsync("ベース削除", vm => vm.DoDeleteCommand);
			session.Check("3マスタ削除保存", (await session.QueryAsync<MasterPointBase>("WHERE Code='UAT'")).Count == 0
				&& (await session.QueryAsync<MasterPointRank>($"WHERE Id={savedRank.Id}")).Count == 0 && (await session.QueryAsync<MasterPointBonus>("WHERE Code='UAT-B'")).Count == 0);

			basis.Vm.Current = (await session.QueryAsync<MasterPointBase>("WHERE Code='USED'")).Single();
			basis.Vm.CurrentEdit.PointUnitPrice = 200;
			await Reject(session, "使用済みベース条件修正拒否", () => basis.RunAsync("修正", vm => vm.DoUpdateCommand));
			basis.Vm.Current = (await session.QueryAsync<MasterPointBase>("WHERE Code='USED'")).Single();
			await Reject(session, "使用済みベース削除拒否", () => basis.RunAsync("削除", vm => vm.DoDeleteCommand));
			basis.Vm.CurrentEdit = Common.CloneObject(basis.Vm.Current);
			basis.Vm.CurrentEdit.IsEnabled = 0;
			await basis.RunAsync("使用済みベース無効化", vm => vm.DoUpdateCommand);
			session.CheckEqual("使用済みベース有効切替許可", 0, (await session.QueryAsync<MasterPointBase>("WHERE Code='USED'")).Single().IsEnabled);
			rank.Vm.Current = (await session.QueryAsync<MasterPointRank>($"WHERE Id_PointBase={basis.Vm.CurrentEdit.Id}")).Single();
			rank.Vm.CurrentEdit.PointAmountProper = 9;
			await Reject(session, "使用済みランク修正拒否", () => rank.RunAsync("修正", vm => vm.DoUpdateCommand));
			await Reject(session, "使用済みランク削除拒否", () => rank.RunAsync("削除", vm => vm.DoDeleteCommand));
			bonus.Vm.Current = (await session.QueryAsync<MasterPointBonus>("WHERE Code='USED-B'")).Single();
			bonus.Vm.CurrentEdit.PointAmount = 9;
			await Reject(session, "使用済みボーナス条件修正拒否", () => bonus.RunAsync("修正", vm => vm.DoUpdateCommand));
			await Reject(session, "使用済みボーナス削除拒否", () => bonus.RunAsync("削除", vm => vm.DoDeleteCommand));
			bonus.Vm.Current = (await session.QueryAsync<MasterPointBonus>("WHERE Code='USED-B'")).Single();
			bonus.Vm.CurrentEdit = Common.CloneObject(bonus.Vm.Current);
			bonus.Vm.CurrentEdit.IsEnabled = 0;
			await bonus.RunAsync("使用済みボーナス無効化", vm => vm.DoUpdateCommand);
			session.CheckEqual("使用済みボーナス有効切替許可", 0, (await session.QueryAsync<MasterPointBonus>("WHERE Code='USED-B'")).Single().IsEnabled);
			session.Check("使用済み条件の保存値維持", (await session.QueryAsync<MasterPointBase>("WHERE Code='USED'")).Single().PointUnitPrice == 100
				&& (await session.QueryAsync<MasterPointRank>($"WHERE Id_PointBase={basis.Vm.CurrentEdit.Id}")).Single().PointAmountProper == 2
				&& (await session.QueryAsync<MasterPointBonus>("WHERE Code='USED-B'")).Single().PointAmount == 5);
		} finally { session.SetDialogResponder(null); }
	}

	static async Task Ready<TVm>(ViewDriver<TVm> driver) where TVm : BaseViewModel {
		await Application.Current.Dispatcher.InvokeAsync(driver.View.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
		await driver.WaitAsync("画面初期化", vm => driver.View.IsLoaded && !vm.HasRunningCommand);
	}
	static IEnumerable<DependencyObject> Descendants(DependencyObject parent) {
		for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
			var child = VisualTreeHelper.GetChild(parent, i);
			yield return child;
			foreach (var descendant in Descendants(child)) yield return descendant;
		}
	}
	static async Task CheckLayout(VmSession session, Window view, string screens, string name) {
		var originalWidth = view.Width;
		var originalHeight = view.Height;
		try {
			foreach (var minimum in new[] { false, true }) {
				view.Width = minimum ? 900 : originalWidth;
				view.Height = minimum ? 600 : originalHeight;
				await Settle(view);
				var label = name + (minimum ? "_Minimum900x600" : "_Standard");
				var form = Descendants(view).OfType<ScrollViewer>().Single(x => x.Content is StackPanel);
				var buttons = Descendants(view).OfType<Button>().Where(x => x.Command != null && x.IsVisible).ToList();
				var hiddenButtons = buttons.Where(x => !Reachable(view, x)).Select(x => x.Content?.ToString()).ToList();
				session.Check(label + ":標準操作ボタン全表示", buttons.Count >= 5 && hiddenButtons.Count == 0, new { view.ActualWidth, view.ActualHeight, Hidden = hiddenButtons });
				var targets = Descendants((DependencyObject)form.Content).OfType<FrameworkElement>()
					.Where(x => x is TextBox box && box.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path.StartsWith("CurrentEdit.", StringComparison.Ordinal) == true
						|| x is ComboBox combo && combo.ItemsSource != null || x is DatePicker || x is TextBlock tb && tb.Parent == form.Content).ToList();
				var unreachable = new List<string>();
				var clipped = new List<string>();
				foreach (var target in targets) {
					target.BringIntoView();
					await Settle(view);
					if (!Reachable(view, target)) unreachable.Add(target is TextBlock block ? block.Text : target.GetType().Name);
					if (target is TextBlock text && text.TextWrapping == TextWrapping.NoWrap && !string.IsNullOrEmpty(text.Text)) {
						var formatted = new FormattedText(text.Text, System.Globalization.CultureInfo.CurrentUICulture, text.FlowDirection,
							new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(text).PixelsPerDip);
						if (formatted.WidthIncludingTrailingWhitespace > text.ActualWidth + 1) clipped.Add(text.Text);
					}
				}
				session.Check(label + ":全入力・ラベルへ縦スクロール到達", unreachable.Count == 0 && targets.Count > 5, new { Targets = targets.Count, Unreachable = unreachable });
				session.Check(label + ":ラベル文字切れ・編集横はみ出しなし", clipped.Count == 0 && form.ExtentWidth <= form.ViewportWidth + 1,
					new { Clipped = clipped, form.ExtentWidth, form.ViewportWidth });
				var grid = Descendants(view).OfType<DataGrid>().Single();
				var hiddenHeaders = new List<string>();
				foreach (var column in grid.Columns) {
					grid.ScrollIntoView(grid.Items[0], column); await Settle(view);
					var header = Descendants(grid).OfType<DataGridColumnHeader>().FirstOrDefault(x => x.Column == column);
					if (header is null || !Reachable(view, header)) hiddenHeaders.Add(column.Header?.ToString() ?? "");
				}
				session.Check(label + ":一覧全列へ横スクロール到達", hiddenHeaders.Count == 0, new { Columns = grid.Columns.Count, HiddenHeaders = hiddenHeaders });
				grid.ScrollIntoView(grid.Items[0], grid.Columns[0]); await Settle(view);
				form.ScrollToTop(); await Settle(view);
				await CaptureLayout(session, view, screens, label + "_Top");
				form.ScrollToBottom(); await Settle(view);
				await CaptureLayout(session, view, screens, label + "_Bottom");
			}
		} finally { view.Width = originalWidth; view.Height = originalHeight; await Settle(view); }
	}
	static async Task CaptureLayout(VmSession session, Window view, string screens, string name) {
		await Settle(view); await Task.Delay(100);
		var path = ScreenLayoutCheck.SaveJpeg(view, screens, name);
		session.Note(name + ":画面画像", new { Path = path });
		// 横スクロール境界の部分列だけは直前の全列到達実測で判定する。
		var issues = ScreenLayoutCheck.Inspect(view).Where(x => x.Kind != ScreenLayoutCheck.LongCellKind && x.Kind != "列が横スクロール外").ToList();
		session.Check(name + ":文字・ボタン見切れなし", issues.Count == 0, new { Count = issues.Count, Issues = issues });
	}
	static bool Reachable(Window view, FrameworkElement target) {
		var bounds = target.TransformToAncestor(view).TransformBounds(new Rect(target.RenderSize));
		var visible = new Rect(0, 0, view.ActualWidth, view.ActualHeight);
		for (var parent = VisualTreeHelper.GetParent(target); parent != null && parent != view; parent = VisualTreeHelper.GetParent(parent))
			if (parent is ScrollContentPresenter presenter) visible.Intersect(presenter.TransformToAncestor(view).TransformBounds(new Rect(presenter.RenderSize)));
		visible.Inflate(1, 1);
		return target.IsVisible && visible.Contains(bounds);
	}
	static async Task Settle(Window view) => await Application.Current.Dispatcher.InvokeAsync(view.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
	static async Task Reject(VmSession session, string name, Func<Task> execute) {
		session.ClearDialogs();
		await execute();
		session.Check(name, session.Dialogs.Any(x => x.Request.Image == MessageBoxImage.Error || x.Request.Image == MessageBoxImage.Warning), session.Dialogs);
	}
	static MasterPointBase Base(string code) => new() { Code = code, Name = "ポイントベース", DayFrom = "20261001", DayTo = "20261031", IsEnabled = 1, PointUnitPrice = 100, PointAmountProper = 1, PointAmountSale = 0, Rounding = 0 };
	static MasterPointRank Rank(long parent) => new() { Id_PointBase = parent, Kubun = 2, Name = "シルバー", PointUnitPrice = 100, PointAmountProper = 2, PointAmountSale = 0 };
	static MasterPointBonus Bonus(long parent, string code) => new() { Code = code, Name = "ボーナス", Id_PointBase = parent, DayFrom = "20261006", DayTo = "20261006", IsEnabled = 1, PointAmount = 5, MinimumKingaku = 0, LimitCount = 1 };
}
