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
using CvWpfclient.ViewModels._32LoyalCustomer;
using CvWpfclient.ViewModels.Sub;
using CvWpfclient.Views._32LoyalCustomer;
using CvWpfclient.Views.Sub;
using Microsoft.Data.Sqlite;

namespace UatVm.Scenarios;

/// <summary>独立DBでポイントマスタ（キャンペーン）の実画面CRUD・入力検証・対象設定済み保護・標準/最小サイズ表示を確認する。</summary>
public static class PointMasterCampaignScenario {
	static string? databasePath;
	static long seededBaseId;
	static long emptyBaseId;
	// 一覧取得前の条件選択Win（PointMasterSearchParamView）への自動応答。
	static bool searchHookRegistered;
	static VmSession? searchSession;
	static string? searchScreens;
	static Func<PointMasterSearchParameter, PointMasterSearchParameter>? searchCondition;
	static int searchDialogCount;
	static readonly HashSet<string> searchCaptured = [];

	public static void Seeder(string dbPath) {
		var path = Path.GetFullPath(dbPath);
		if (!Path.GetFileName(path).StartsWith("point-campaign-uat-", StringComparison.OrdinalIgnoreCase)
			|| !path.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
			|| path.Split(Path.DirectorySeparatorChar).Any(x => x.Equals("CvServer", StringComparison.OrdinalIgnoreCase))
			|| (File.Exists(path) && new FileInfo(path).Length > 0))
			throw new InvalidOperationException("未作成/空の専用point-campaign-uat-*.dbだけ使用できます。");
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
		connection.Open();
		using var db = new ExDatabaseSqlite(connection) { KeepConnectionAlive = true };
		if (!new DefineDataTable().InitializeAsync(db, false).GetAwaiter().GetResult()) throw new InvalidOperationException("専用DB初期化失敗");
		var basis = Base("CB", 1); db.Insert(basis);
		db.Insert(new MasterPointRank { Id_PointBase = basis.Id, Kubun = 2, Name = "シルバー", PointUnitPrice = 100, PointAmountProper = 2, PointAmountSale = 0 });
		// ランクのない親版。親切替でランク候補が全ランクだけになることを確認する。
		var empty = Base("CB", 2); empty.DayFrom = "20261101"; empty.DayTo = "20261130"; db.Insert(empty);
		// 対象店舗が設定済みの店別キャンペーン。対象は専用保存(Msg064)の代わりにサーバ起動前に直接用意する。
		var used = Campaign(basis.Id, "USED-C"); used.PriorityType = (int)EnumPointCampaignPriority.Shop; db.Insert(used);
		db.Insert(new MasterPointCampaignShop { Id_PointCampaign = used.Id, Id_Tenpo = 1 });
		seededBaseId = basis.Id;
		emptyBaseId = empty.Id;
		databasePath = path;
	}

	public static async Task RunAsync(VmSession session) {
		if (databasePath is null) throw new InvalidOperationException("専用Seederを実行してください。");
		var screens = Path.Combine(Path.GetDirectoryName(databasePath)!, "point-campaign-screens-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
		Directory.CreateDirectory(screens);
		session.Note("対象", new { Database = databasePath, Screens = screens });
		session.SetDialogResponder(request => request.Button is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel ? MessageBoxResult.Yes : MessageBoxResult.OK);
		searchSession = session;
		searchScreens = screens;
		EnsureSearchDialogAnswered();
		try {
			var campaign = session.OpenView<PointMasterCampaignView, PointMasterCampaignViewModel>();
			await Ready(campaign);
			var today = DateTime.Today;
			string Ymd(DateTime day) => day.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
			// 起動時はInitで一覧取得済み（先頭行を選択）。
			session.Check("起動時の一覧取得", campaign.Vm.ListData.Count == 1 && campaign.Vm.CurrentEdit.Code == "USED-C");
			await SearchList(session, "キャンペーン条件:すべて", () => campaign.RunAsync("キャンペーン一覧", vm => vm.DoListCommand), null);
			session.Check("初期一覧", campaign.Vm.ListData.Count == 1 && campaign.Vm.ListData[0].Code == "USED-C");
			session.CheckEqual("親ベース候補に2版", 2, campaign.Vm.BaseOptions.Count);

			campaign.Input("新規(Id=0)の期間初期値", vm => vm.CurrentEdit = new MasterPointCampaign());
			session.Check("新規入力の期間初期値", campaign.Vm.CurrentEdit.DayFrom == Ymd(today) && campaign.Vm.CurrentEdit.DayTo == Ymd(today.AddDays(7)));
			campaign.Input("親ベース版1", vm => vm.CurrentEdit.Id_PointBase = seededBaseId);
			session.Check("ランク候補=全ランク+親版ランク", campaign.Vm.RankOptions.Select(x => x.Key).SequenceEqual([0, 2]), campaign.Vm.RankOptions);
			campaign.Input("親ベース版2(ランクなし)", vm => vm.CurrentEdit.Id_PointBase = emptyBaseId);
			session.Check("ランクなし親版の候補は全ランクのみ", campaign.Vm.RankOptions.Select(x => x.Key).SequenceEqual([0]), campaign.Vm.RankOptions);

			campaign.Input("付与単価0", vm => { vm.CurrentEdit = Campaign(seededBaseId, "UAT-C"); vm.CurrentEdit.PointUnitPrice = 0; });
			await Reject(session, "付与単価0拒否", () => campaign.RunAsync("追加", vm => vm.DoInsertCommand));
			session.Check("検証失敗でも入力保持", campaign.Vm.CurrentEdit.Code == "UAT-C" && campaign.Vm.CurrentEdit.PointUnitPrice == 0);
			campaign.Input("S付与数負数", vm => { vm.CurrentEdit = Campaign(seededBaseId, "UAT-C"); vm.CurrentEdit.PointAmountSale = -1; });
			await Reject(session, "付与数負数拒否", () => campaign.RunAsync("追加", vm => vm.DoInsertCommand));
			campaign.Input("不正日付", vm => { vm.CurrentEdit = Campaign(seededBaseId, "UAT-C"); vm.CurrentEdit.DayFrom = "20261032"; });
			await Reject(session, "実在しない日付拒否", () => campaign.RunAsync("追加", vm => vm.DoInsertCommand));
			campaign.Input("親期間外", vm => { vm.CurrentEdit = Campaign(seededBaseId, "UAT-C"); vm.CurrentEdit.DayTo = "20261101"; });
			await Reject(session, "親ベース期間外拒否", () => campaign.RunAsync("追加", vm => vm.DoInsertCommand));
			campaign.Input("親版にないランク", vm => { vm.CurrentEdit = Campaign(emptyBaseId, "UAT-C"); vm.CurrentEdit.DayFrom = "20261101"; vm.CurrentEdit.DayTo = "20261107"; vm.CurrentEdit.RankKubun = 2; });
			session.Check("親版にない現在値も候補に表示", campaign.Vm.RankOptions.Any(x => x.Key == 2 && x.Value.Contains("未登録", StringComparison.Ordinal)), campaign.Vm.RankOptions);
			await Reject(session, "親版にないランク拒否", () => campaign.RunAsync("追加", vm => vm.DoInsertCommand));
			campaign.Input("備考201文字", vm => { vm.CurrentEdit = Campaign(seededBaseId, "UAT-C"); vm.CurrentEdit.Memo = new string('あ', 201); });
			await Reject(session, "備考201文字拒否", () => campaign.RunAsync("追加", vm => vm.DoInsertCommand));
			campaign.Input("コード重複", vm => vm.CurrentEdit = Campaign(seededBaseId, "USED-C"));
			await Reject(session, "コード重複拒否(サーバ)", () => campaign.RunAsync("追加", vm => vm.DoInsertCommand));
			session.CheckEqual("拒否後の登録なし", 1, (await session.QueryAsync<MasterPointCampaign>("")).Count);

			campaign.Input("正常値(P付与0・ランク2・商品全店・備考200文字)", vm => {
				vm.CurrentEdit = Campaign(seededBaseId, "UAT-C");
				vm.CurrentEdit.PointAmountProper = 0;
				vm.CurrentEdit.RankKubun = 2;
				vm.CurrentEdit.PriorityType = (int)EnumPointCampaignPriority.ShohinAllShops;
				vm.CurrentEdit.Memo = new string('メ', 200);
			});
			await campaign.RunAsync("キャンペーン追加", vm => vm.DoInsertCommand);
			var saved = (await session.QueryAsync<MasterPointCampaign>("WHERE Code='UAT-C'")).SingleOrDefault();
			session.Check("登録・付与数0・ランク・優先区分・備考保存", saved != null && saved.Id_PointBase == seededBaseId && saved.PointAmountProper == 0
				&& saved.RankKubun == 2 && saved.PriorityType == (int)EnumPointCampaignPriority.ShohinAllShops && saved.Memo.Length == 200, saved);
			if (saved == null) return;

			await SearchList(session, "キャンペーン条件:コード前方一致UAT・有効のみ", () => campaign.RunAsync("条件一覧", vm => vm.DoListCommand),
				p => p with { Code = "UAT", EnabledState = 1 });
			session.Check("コード前方一致・有効で絞込", campaign.Vm.ListData.Count == 1 && campaign.Vm.ListData[0].Id == saved.Id, campaign.Vm.ListData.Select(x => new { x.Id, x.Code }));
			await SearchList(session, "キャンペーン条件:期間外の適用日", () => campaign.RunAsync("条件一覧", vm => vm.DoListCommand),
				p => p with { TargetDay = "20261011" });
			session.CheckEqual("期間外の適用日は0件", 0, campaign.Vm.ListData.Count);
			await SearchList(session, "キャンペーン条件:ランクなし親版", () => campaign.RunAsync("条件一覧", vm => vm.DoListCommand),
				p => p with { Id_PointBase = emptyBaseId });
			session.CheckEqual("ランクなし親版は0件", 0, campaign.Vm.ListData.Count);
			await SearchList(session, "キャンペーン条件:すべて", () => campaign.RunAsync("全件一覧", vm => vm.DoListCommand), null);
			session.Check("一覧順 Code, Id", campaign.Vm.ListData.Select(x => x.Code).SequenceEqual(["UAT-C", "USED-C"]), campaign.Vm.ListData.Select(x => x.Code));

			campaign.Input("名称・S付与数修正", vm => { vm.Current = vm.ListData.Single(x => x.Id == saved.Id); vm.CurrentEdit.Name = "キャンペーン修正"; vm.CurrentEdit.PointAmountSale = 0; });
			session.Check("一覧選択でランク候補が親版に追随", campaign.Vm.RankOptions.Select(x => x.Key).SequenceEqual([0, 2]) && campaign.Vm.CurrentEdit.RankKubun == 2, campaign.Vm.RankOptions);
			await campaign.RunAsync("キャンペーン修正", vm => vm.DoUpdateCommand);
			var updated = (await session.QueryAsync<MasterPointCampaign>("WHERE Code='UAT-C'")).Single();
			session.Check("修正保存(S付与数0)", updated.Name == "キャンペーン修正" && updated.PointAmountSale == 0 && updated.RankKubun == 2, updated);

			campaign.Input("対象設定済みの優先区分変更", vm => { vm.Current = vm.ListData.Single(x => x.Code == "USED-C"); vm.CurrentEdit.PriorityType = (int)EnumPointCampaignPriority.AllShops; });
			await Reject(session, "対象設定済みの優先区分変更拒否(サーバ)", () => campaign.RunAsync("修正", vm => vm.DoUpdateCommand));
			session.CheckEqual("優先区分維持", (int)EnumPointCampaignPriority.Shop, (await session.QueryAsync<MasterPointCampaign>("WHERE Code='USED-C'")).Single().PriorityType);
			campaign.Vm.CurrentEdit = Common.CloneObject(campaign.Vm.Current);
			await Reject(session, "対象設定済みの削除拒否(サーバ)", () => campaign.RunAsync("削除", vm => vm.DoDeleteCommand));
			session.CheckEqual("対象設定済みキャンペーン維持", 1, (await session.QueryAsync<MasterPointCampaign>("WHERE Code='USED-C'")).Count);

			campaign.Vm.Current = campaign.Vm.ListData.Single(x => x.Id == saved.Id);
			campaign.Vm.CurrentEdit = Common.CloneObject(campaign.Vm.Current);
			await CheckLayout(session, campaign.View, screens, "04_Campaign");
			await campaign.RunAsync("キャンペーン削除", vm => vm.DoDeleteCommand);
			session.CheckEqual("削除保存", 0, (await session.QueryAsync<MasterPointCampaign>("WHERE Code='UAT-C'")).Count);
		} finally { session.SetDialogResponder(null); }
	}

	/// <summary>条件を指定して一覧取得し、条件選択Winが1回出て OK で閉じたことを確認する。condition=null は全条件解除。</summary>
	static async Task SearchList(VmSession session, string name, Func<Task> run, Func<PointMasterSearchParameter, PointMasterSearchParameter>? condition) {
		var before = searchDialogCount;
		searchCondition = condition;
		try { await run(); } finally { searchCondition = null; }
		session.CheckEqual(name + ":条件選択Winを表示", before + 1, searchDialogCount);
	}

	static void EnsureSearchDialogAnswered() {
		if (searchHookRegistered) return;
		searchHookRegistered = true;
		EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, e) => {
			if (sender is PointMasterSearchParamView win && win.DataContext is PointMasterSearchParamViewModel) _ = AnswerSearchDialogAsync(win);
		}));
	}

	/// <summary>既定（全条件）へ戻してから指定条件を入れ、初回だけ撮影・表示崩れ判定して OK で閉じる。</summary>
	static async Task AnswerSearchDialogAsync(PointMasterSearchParamView win) {
		var vm = (PointMasterSearchParamViewModel)win.DataContext;
		var session = searchSession;
		try {
			var reset = vm.Parameter with { Code = null, TargetDay = null, EnabledState = PointMasterSearchParameter.AllEnabled, Id_PointBase = PointMasterSearchParameter.AllBase };
			// record を差し替えて ObservableProperty の変更通知で画面へ反映させる。
			vm.Parameter = searchCondition?.Invoke(reset) ?? reset;
			await Settle(win);
			session?.Note("条件選択Win:条件", new { vm.Parameter.DisplayName, vm.Parameter.Code, vm.Parameter.TargetDay, vm.Parameter.EnabledState, vm.Parameter.Id_PointBase, vm.Parameter.MaxCount });
			var key = vm.Parameter.DisplayName ?? "";
			if (session != null && searchScreens != null && searchCaptured.Add(key)) {
				await Task.Delay(200);
				await Settle(win);
				var path = ScreenLayoutCheck.SaveJpeg(win, searchScreens, "00_SearchParam_" + key);
				session.Note("条件選択Win:" + key + ":画面画像", new { Path = path });
				var issues = InspectLayout(win);
				var buttons = Descendants(win).OfType<Button>().Where(x => x.Command != null && x.IsVisible).ToList();
				session.Check("条件選択Win:" + key + ":確定・戻るボタン表示", buttons.Count == 2 && buttons.All(x => Reachable(win, x)));
				session.Check("条件選択Win:" + key + ":文字・ボタン見切れなし", issues.Count == 0, new { Count = issues.Count, Issues = issues });
			}
		} catch (Exception ex) {
			session?.Check("条件選択Win:自動応答例外なし", false, ex.ToString());
		} finally {
			searchDialogCount++;
			vm.OkCommand.Execute(null);
		}
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
				// 右フォームは TabControl > ScrollViewer > 2列Grid。テンプレート内部の ScrollViewer は除外する。
				var form = Descendants(view).OfType<ScrollViewer>().Single(x => x.Content is Grid && x.TemplatedParent is null);
				var buttons = Descendants(view).OfType<Button>().Where(x => x.Command != null && x.IsVisible).ToList();
				// ボタンの Content はアイコン+テキストの StackPanel なので ToolTip で識別する。
				var hiddenButtons = buttons.Where(x => !Reachable(view, x)).Select(x => x.ToolTip?.ToString() ?? x.Content?.ToString()).ToList();
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
				// 一覧最終列は右端まで横スクロールした状態で列見出しが表示域に収まることを確認・撮影する。
				var gridScroll = Descendants(grid).OfType<ScrollViewer>().First();
				gridScroll.ScrollToRightEnd(); await Settle(view);
				var lastHeader = Descendants(grid).OfType<DataGridColumnHeader>().FirstOrDefault(x => x.Column == grid.Columns[^1]);
				session.Check(label + ":一覧最終列が右端で見切れない", lastHeader != null && Reachable(view, lastHeader), new { Column = grid.Columns[^1].Header });
				form.ScrollToBottom(); await Settle(view);
				await CaptureLayout(session, view, screens, label + "_RightEnd");
				gridScroll.ScrollToLeftEnd(); await Settle(view);
				form.ScrollToTop(); await Settle(view);
				await CaptureLayout(session, view, screens, label + "_Top");
				form.ScrollToBottom(); await Settle(view);
				// 右フォーム下端（登録日・修正日の行）が最下部スクロールで全体表示されること。
				var lastRow = ((Grid)form.Content).Children.OfType<FrameworkElement>().OrderByDescending(Grid.GetRow).First();
				session.Check(label + ":右フォーム下端行が見切れない", Reachable(view, lastRow), new { Text = (lastRow as TextBlock)?.Text });
				await CaptureLayout(session, view, screens, label + "_Bottom");
			}
		} finally { view.Width = originalWidth; view.Height = originalHeight; await Settle(view); }
	}
	static async Task CaptureLayout(VmSession session, Window view, string screens, string name) {
		await Settle(view); await Task.Delay(100);
		var path = ScreenLayoutCheck.SaveJpeg(view, screens, name);
		session.Note(name + ":画面画像", new { Path = path });
		// 横スクロール境界の部分列だけは直前の全列到達実測で判定する。
		var issues = InspectLayout(view).Where(x => x.Kind != "列が横スクロール外").ToList();
		session.Check(name + ":文字・ボタン見切れなし", issues.Count == 0, new { Count = issues.Count, Issues = issues });
	}
	/// <summary>
	/// 共通判定(ScreenLayoutCheck)に、右端・下端の見切れ判定を足して誤検知を除く。
	/// <list type="bullet">
	/// <item>除外: MaterialDesign の浮動ヒント(SmartHint)は縮小表示されるため、縮小前の幅で収まっていれば文字切れにしない</item>
	/// <item>除外: スクロール表示域が窓内にあり、その中で一部だけ見えている入力欄は「ウィンドウ外」にしない（縦スクロール到達は別途確認済み）</item>
	/// <item>追加: スクロール外の要素が、親Card・ウィンドウ表示域の右端・下端で切れていないか</item>
	/// </list>
	/// </summary>
	static List<ScreenLayoutCheck.Issue> InspectLayout(Window view) {
		var root = (FrameworkElement)view.Content;
		var area = new Rect(root.RenderSize);
		area.Inflate(1, 1);
		var excused = new HashSet<(string, double, double)>();
		var extra = new List<ScreenLayoutCheck.Issue>();
		foreach (var element in Descendants(root).OfType<FrameworkElement>()) {
			if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0) continue;
			Rect bounds;
			try { bounds = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize)); } catch (InvalidOperationException) { continue; }
			var key = (Math.Round(bounds.Left), Math.Round(bounds.Top));
			var presenter = Ancestor<ScrollContentPresenter>(element);
			if (element is TextBlock tb && Ancestors(tb).Any(x => x.GetType().Name == "SmartHint") && TextWidth(tb) <= tb.ActualWidth + 1)
				excused.Add(("ラベル文字切れ", key.Item1, key.Item2));
			if (element is ButtonBase or TextBox or ComboBox or DatePicker && presenter != null
				&& area.Contains(presenter.TransformToAncestor(root).TransformBounds(new Rect(presenter.RenderSize))))
				excused.Add(("ウィンドウ外", key.Item1, key.Item2));
			if (presenter != null || element is not (ButtonBase or TextBox or ComboBox or DatePicker or TextBlock)) continue;
			// スクロールバー部品は共通判定と同じく対象外（角丸Cardの角に掛かるだけで操作・表示に影響しない）
			if (Ancestors(element).Any(x => x is ButtonBase or TextBox or ComboBox or DatePicker or DataGrid or ScrollBar)) continue;
			// Card 内の要素は Card の枠、それ以外はウィンドウ表示域に収まること（右端・下端の見切れ）
			var card = Ancestors(element).OfType<FrameworkElement>().FirstOrDefault(x => x.GetType().Name == "Card");
			var frame = card is null ? area : card.TransformToAncestor(root).TransformBounds(new Rect(card.RenderSize));
			if (card != null) frame.Inflate(1, 1);
			if (!frame.Contains(bounds))
				extra.Add(new ScreenLayoutCheck.Issue(card is null ? "表示域の端で見切れ" : "Cardの端で見切れ", element.GetType().Name,
					element switch { TextBlock t => t.Text, TextBox t => t.Text, ContentControl c => c.ToolTip?.ToString() ?? "", _ => "" },
					key.Item1, key.Item2, Math.Round(bounds.Width), Math.Round(bounds.Height), Math.Round(frame.Bottom)));
		}
		return ScreenLayoutCheck.Inspect(view)
			.Where(x => x.Kind != ScreenLayoutCheck.LongCellKind && !excused.Contains((x.Kind, x.Left, x.Top)))
			.Concat(extra).ToList();
	}
	static IEnumerable<DependencyObject> Ancestors(DependencyObject element) {
		for (var parent = VisualTreeHelper.GetParent(element); parent != null; parent = VisualTreeHelper.GetParent(parent)) yield return parent;
	}
	static T? Ancestor<T>(DependencyObject element) where T : DependencyObject => Ancestors(element).OfType<T>().FirstOrDefault();
	static double TextWidth(TextBlock tb) => new FormattedText(tb.Text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
		new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch), tb.FontSize, Brushes.Black, null,
		TextOptions.GetTextFormattingMode(tb), VisualTreeHelper.GetDpi(tb).PixelsPerDip).WidthIncludingTrailingWhitespace + tb.Padding.Left + tb.Padding.Right;
	static bool Reachable(Window view, FrameworkElement target) {
		var bounds = target.TransformToAncestor(view).TransformBounds(new Rect(target.RenderSize));
		// ウィンドウ枠を除いたクライアント領域（Window.Content）を表示域とする。
		var root = (FrameworkElement)view.Content;
		var visible = root.TransformToAncestor(view).TransformBounds(new Rect(root.RenderSize));
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
	static MasterPointBase Base(string code, int version) => new() { Code = code, Name = "キャンペーン親ベース", Version = version, DayFrom = "20261001", DayTo = "20261031", IsEnabled = 1, PointUnitPrice = 100, PointAmountProper = 1, PointAmountSale = 0, Rounding = 0 };
	static MasterPointCampaign Campaign(long parent, string code) => new() { Code = code, Name = "キャンペーン", Id_PointBase = parent, DayFrom = "20261006", DayTo = "20261010", IsEnabled = 1, PriorityType = 0, PointUnitPrice = 100, PointAmountProper = 3, PointAmountSale = 1, RankKubun = 0 };
}
