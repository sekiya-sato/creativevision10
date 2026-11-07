using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using CodeShare;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using CvWpfclient.Models;
using CvWpfclient.ViewModels;
using CvWpfclient.ViewModels._00System;
using CvWpfclient.ViewModels._01Master;
using CvWpfclient.ViewModels._02Yosan;
using CvWpfclient.ViewModels._03Hatchu;
using CvWpfclient.ViewModels._06Uriage;
using CvWpfclient.ViewModels._08Zaiko;
using CvWpfclient.ViewModels._20UriageAnalysis;
using CvWpfclient.ViewModels._23AiQuery;
using CvWpfclient.ViewModels._40Shop;
using CvWpfclient.Views;
using CvWpfclient.Views._00System;
using CvWpfclient.Views._01Master;
using CvWpfclient.Views._02Yosan;
using CvWpfclient.Views._03Hatchu;
using CvWpfclient.Views._06Uriage;
using CvWpfclient.Views._08Zaiko;
using CvWpfclient.Views._20UriageAnalysis;
using CvWpfclient.Views._23AiQuery;
using CvWpfclient.Views._40Shop;

namespace UatVm.Scenarios;

/// <summary>
/// デモ動画（Doc/promo/demo）用に、実画面の操作を録画する。
/// <para>
/// 架空のデモ用DB（Doc/promo/demo/work/db/demo-*.db の複製）と専用サーバで動かし、ViewModel のプロパティとコマンドだけで操作する。
/// 画面は左上 1920x1080 に最前面で置き、ffmpeg（gdigrab）でその範囲を録画する（環境変数 CV_FFMPEG）。
/// 1カット＝1つの mp4 と、操作の時刻・強調位置のキュー（.cues.json）。出力は <see cref="OutputDirectory"/>。
/// </para>
/// </summary>
public static class DemoCaptureScenario {
	/// <summary>録画の出力先（既定: Doc/promo/demo/capture）</summary>
	public static string? OutputDirectory { get; set; }
	/// <summary>撮るカット（カンマ区切り、all で全部）</summary>
	public static string Cuts { get; set; } = "all";

	const string Today = "20261107";
	static readonly Int32Rect Region = new(0, 0, 1920, 1080);
	static string _outDir = string.Empty;

	public static async Task RunAsync(VmSession session) {
		_outDir = Path.GetFullPath(OutputDirectory ?? Path.Combine("..", "Doc", "promo", "demo", "capture"));
		Directory.CreateDirectory(_outDir);
		// 確認（登録してよいか等）は Yes、条件ダイアログは既定条件で OK
		session.SetDialogResponder(r => r.Button is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel ? MessageBoxResult.Yes : MessageBoxResult.OK);
		DialogTestRoute.Enable(ConditionResponder);
		var cuts = new (string Name, Func<VmSession, Task> Run)[] {
			("menu", MenuAsync),
			("master_shohin", MasterShohinAsync),
			("master_tokui", MasterTokuiAsync),
			("master_csv", MasterCsvAsync),
			("input_hachu", InputHachuAsync),
			("input_haibun", InputHaibunAsync),
			("input_tenuri", InputTenuriAsync),
			("input_zaiko", InputZaikoAsync),
			("report_preview", ReportPreviewAsync),
			("report_costless", ReportCostlessAsync),
			("ai_query", AiQueryAsync),
			("sys_perm", SysPermAsync),
			("sys_sched", SysSchedAsync),
		};
		var wanted = Cuts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
		try {
			foreach (var (name, run) in cuts) {
				if (!wanted.Contains("all") && !wanted.Contains(name)) continue;
				Console.WriteLine($"[demo] {name} 開始");
				try {
					await run(session);
					session.Check($"{name}: 録画", File.Exists(Path.Combine(_outDir, name + ".mp4")));
				}
				catch (Exception ex) {
					session.Fail($"{name}: 例外", ex.ToString());
					Console.WriteLine($"[demo] {name} 失敗: {ex.Message}");
				}
				finally {
					CloseAll(session);
				}
			}
		}
		finally {
			DialogTestRoute.Disable();
			session.SetDialogResponder(null);
		}
	}

	// ------------------------------------------------------------------ 共通
	/// <summary>条件ダイアログは既定条件で確定する（OkCommand、なければ商品条件の「決定」= SearchOrCommitCommand）</summary>
	static bool? ConditionResponder(Window window) {
		var vm = window.DataContext;
		foreach (var name in new[] { "OkCommand", "SearchOrCommitCommand" }) {
			if (vm?.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(vm) is System.Windows.Input.ICommand c && c.CanExecute(null)) {
				c.Execute(null);
				return null;
			}
		}
		return false;
	}

	/// <summary>画面の中身の拡大率（録画で文字を読みやすくする。1920x1080 の枠に 1536x864 相当で描く）</summary>
	const double ContentScale = 1.25;

	static void ScaleContent(Window w) {
		if (w.Content is FrameworkElement fe && fe.LayoutTransform is not System.Windows.Media.ScaleTransform)
			fe.LayoutTransform = new System.Windows.Media.ScaleTransform(ContentScale, ContentScale);
	}
	/// <summary>メニューと同じ手順で View を作り、録画範囲に最前面で置いて初期化を待つ</summary>
	static async Task<(TView View, TVm Vm)> OpenAsync<TView, TVm>(string? title = null, string? addInfo = null)
		where TView : Window, new() where TVm : class {
		var view = new TView();
		if (title != null) view.Title = title;
		if (view.DataContext is BaseViewModel b && addInfo != null) b.AddInfo = addInfo;
		Place(view);
		ScaleContent(view);
		var rendered = new TaskCompletionSource();
		view.ContentRendered += (_, _) => rendered.TrySetResult();
		view.Show();
		await Task.WhenAny(rendered.Task, Task.Delay(10_000));
		Place(view);
		await AwaitInitAsync(view.DataContext);
		Place(view);
		await Idle();
		return (view, (TVm)view.DataContext);
	}

	static void Place(Window w) {
		w.WindowStartupLocation = WindowStartupLocation.Manual;
		w.WindowState = WindowState.Normal;
		w.Left = Region.X; w.Top = Region.Y;
		w.Width = Region.Width; w.Height = Region.Height;
		w.Topmost = true;
	}

	static async Task AwaitInitAsync(object? vm) {
		await Task.Delay(200);
		if (vm?.GetType().GetProperty("InitCommand")?.GetValue(vm) is IAsyncRelayCommand c && c.ExecutionTask is { } t) {
			try { await t; } catch { /* 初期化の失敗は画面に出る */ }
		}
		for (var i = 0; i < 300 && HasRunningCommand(vm); i++) await Task.Delay(100);
	}

	static bool HasRunningCommand(object? vm) =>
		vm != null && vm.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
			.Where(p => typeof(IAsyncRelayCommand).IsAssignableFrom(p.PropertyType) && p.GetIndexParameters().Length == 0)
			.Select(p => { try { return p.GetValue(vm) as IAsyncRelayCommand; } catch { return null; } })
			.Any(c => c?.IsRunning == true);

	static Task Idle() => Application.Current.Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle).Task;

	static Task<ScreenRecorder> RecAsync(string name) => ScreenRecorder.StartAsync(Path.Combine(_outDir, name + ".mp4"), Region);

	static async Task WaitAsync(Func<bool> cond, int timeoutMs = 60_000) {
		var sw = System.Diagnostics.Stopwatch.StartNew();
		while (!cond() && sw.ElapsedMilliseconds < timeoutMs) await Task.Delay(50);
	}

	static async Task RunCmd(IAsyncRelayCommand c, object? p = null) {
		await c.ExecuteAsync(p);
		if (c.ExecutionTask is { IsCompleted: false } t) await t;
		await Idle();
	}

	static T? Find<T>(DependencyObject root, string? name = null) where T : FrameworkElement =>
		DemoTyping.Find<T>(root, x => name == null || x.Name == name);

	static void CloseAll(VmSession session) {
		session.CloseViews();
		foreach (var w in Application.Current.Windows.OfType<Window>().ToList()) {
			if (w == Application.Current.MainWindow) continue;
			try { w.Close(); } catch (InvalidOperationException) { }
		}
	}

	static async Task<T> One<T>(VmSession s, string where, params string[] p) =>
		(await s.QueryAsync<T>(where, p)).FirstOrDefault() ?? throw new InvalidOperationException($"{typeof(T).Name} {where} {string.Join(",", p)} がありません。");

	// ------------------------------------------------------------------ 01 メインメニュー
	static async Task MenuAsync(VmSession s) {
		var (view, vm) = await OpenAsync<MainMenuView, MainMenuViewModel>();
		var login = new LoginViewModel { LoginId = "demo-capture", LoginData = new LoginReply { JwtMessage = "", PermissionProfileId = 0 } };
		var update = typeof(MainMenuViewModel).GetMethod("UpdateMenuForLoginAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
		await (Task)update.Invoke(vm, [login])!;
		await Idle();
		await using var rec = await RecAsync("menu");
		await ScreenRecorder.Pause(1.5);
		var tree = Find<TreeView>(view, "MenuTree");
		// メニュー一覧をゆっくり下までスクロールして、業務の幅を見せる
		if (tree != null && DemoTyping.Find<ScrollViewer>(tree) is { } sv) {
			rec.Mark("scroll", tree, "業務ごとのメニュー");
			var end = sv.ScrollableHeight;
			for (var i = 1; i <= 120; i++) {
				sv.ScrollToVerticalOffset(end * (1 - Math.Cos(Math.PI * i / 120)) / 2);
				await Task.Delay(50);
			}
			await ScreenRecorder.Pause(1.0);
			sv.ScrollToVerticalOffset(0);
			await Idle();
		}
		foreach (var header in new[] { "■ 発注", "■ 配分・出荷", "■ 売上", "■ 分析" }) {
			var folder = vm.MenuItems.FirstOrDefault(x => x.Header == header);
			if (folder == null) continue;
			folder.IsExpanded = true;
			await Idle();
			if (tree != null) rec.Mark("expand:" + header, tree, header);
			await ScreenRecorder.Pause(1.6);
		}
		var target = vm.MenuItems.SelectMany(x => x.SubItems ?? []).SelectMany(x => (x.SubItems ?? []).Prepend(x))
			.FirstOrDefault(x => x.Header == "在庫問合せ");
		if (target != null) {
			vm.SelectedMenu = target;
			await Idle();
			rec.Mark("select:在庫問合せ");
		}
		await ScreenRecorder.Pause(2.5);
		foreach (var f in vm.MenuItems) f.IsExpanded = false;
		await ScreenRecorder.Pause(1.0);
		// 店舗業務・倉庫業務の入口
		foreach (var header in new[] { "■ 店舗業務", "■ 倉庫業務" }) {
			var folder = vm.MenuItems.FirstOrDefault(x => x.Header == header);
			if (folder == null) continue;
			folder.IsExpanded = true;
			await Idle();
			rec.Mark("expand:" + header, tree ?? (FrameworkElement)view.Content, header);
			await ScreenRecorder.Pause(1.8);
		}
		await ScreenRecorder.Pause(1.5);
		_ = view;
	}

	// ------------------------------------------------------------------ 02 商品マスタ
	static async Task MasterShohinAsync(VmSession s) {
		var (view, vm) = await OpenAsync<MasterShohinMenteView, MasterShohinMenteViewModel>("商品マスタ", "商品マスタメンテ画面");
		if (vm.Count == 0) await RunCmd(vm.DoListCommand);
		await using var rec = await RecAsync("master_shohin");
		await ScreenRecorder.Pause(1.2);
		if (Find<DataGrid>(view, "ShohinGrid") is { } grid) rec.Mark("list", grid, "品番の一覧");
		await ScreenRecorder.Pause(1.5);
		var item = vm.ListData.FirstOrDefault(x => x.Code == "VL-S001") ?? vm.ListData.FirstOrDefault();
		if (item != null) {
			vm.Current = item;
			await WaitAsync(() => !vm.IsDetailLoading && vm.CurrentEdit?.Code == item.Code, 15_000);
			await Idle();
			rec.Mark("select", view, item.Name);
		}
		await ScreenRecorder.Pause(3.0);
		// 「色サイズ」タブへ
		// 詳細側のタブ（Id / 色サイズ / 原価(旧) …）。名前がないので項目数で見分ける
		var tabs = DemoTyping.Find<TabControl>(view, t => t.Items.Count >= 5);
		var colTab = tabs?.Items.OfType<TabItem>().ElementAtOrDefault(1);
		if (tabs != null && colTab != null) {
			tabs.SelectedItem = colTab;
			await Idle();
			await ScreenRecorder.Pause(0.4);
		}
		if (Find<DataGrid>(view, "JcolsizGrid") is { } cs) rec.Mark("colsiz", cs, "カラー × サイズ");
		await ScreenRecorder.Pause(4.0);
		if (tabs != null) tabs.SelectedIndex = 0;
		// 冬物も見せる
		var winter = vm.ListData.FirstOrDefault(x => x.Code == "VL-W001");
		if (winter != null) {
			vm.Current = winter;
			await WaitAsync(() => !vm.IsDetailLoading && vm.CurrentEdit?.Code == winter.Code, 15_000);
			await Idle();
			rec.Mark("select2", view, winter.Name);
		}
		await ScreenRecorder.Pause(4.0);
	}

	// ------------------------------------------------------------------ 02 得意先マスタ
	static async Task MasterTokuiAsync(VmSession s) {
		var (view, vm) = await OpenAsync<MasterTokuiMenteView, MasterTokuiMenteViewModel>("得意先マスタメンテ", "得意先マスタメンテ画面");
		if (vm.Count == 0) await RunCmd(vm.DoListCommand);
		await using var rec = await RecAsync("master_tokui");
		await ScreenRecorder.Pause(1.2);
		var item = vm.ListData.FirstOrDefault(x => x.Code == "100001") ?? vm.ListData.FirstOrDefault();
		if (item != null) {
			vm.Current = item;
			await WaitAsync(() => vm.CurrentEdit?.Code == item.Code, 15_000);
			await Idle();
			rec.Mark("select", view, item.Name);
		}
		await ScreenRecorder.Pause(2.0);
		// 「請求情報」タブ
		var tabs = DemoTyping.Find<TabControl>(view, t => t.Items.OfType<TabItem>().Any(i => (i.Header?.ToString() ?? "").Contains("請求")));
		if (tabs != null) {
			var tab = tabs.Items.OfType<TabItem>().First(i => (i.Header?.ToString() ?? "").Contains("請求"));
			tabs.SelectedItem = tab;
			await Idle();
			rec.Mark("billing", tabs, "締日・回収条件");
		}
		await ScreenRecorder.Pause(5.0);
	}

	// ------------------------------------------------------------------ 02 外部CSV取込
	static async Task MasterCsvAsync(VmSession s) {
		var (view, vm) = await OpenAsync<ImportTemplateCreateView, ImportTemplateCreateViewModel>("取込レイアウト作成");
		await using var rec = await RecAsync("master_csv");
		await ScreenRecorder.Pause(1.2);
		var table = vm.TableList.FirstOrDefault(x => x.ToString()!.Contains("MasterShohin")) ?? vm.TableList.FirstOrDefault();
		if (table != null) {
			vm.SelectedTable = table;
			await Idle();
			await WaitAsync(() => vm.ColumnList.Count > 0, 10_000);
			rec.Mark("table", view, "取り込む表を選ぶ");
		}
		await ScreenRecorder.Pause(1.5);
		var n = 0;
		foreach (var col in vm.ColumnList.Take(8)) {
			col.IsSelected = true;
			n++;
			await ScreenRecorder.Pause(0.35);
		}
		rec.Mark("columns", view, "取り込む列を選ぶ");
		await ScreenRecorder.Pause(3.0);
	}

	// ------------------------------------------------------------------ 03 発注入力
	static async Task InputHachuAsync(VmSession s) {
		var shiire = await One<MasterShiire>(s, "where Code=@0", "1001");
		var soko = await One<MasterTokui>(s, "where Code=@0", "000001");
		var shain = await One<MasterShain>(s, "where Code<>@0 order by Code", "0001");
		var shohin = await One<MasterShohin>(s, "where Code=@0", "VL-A003");
		var skus = await s.QueryAsync<DerivedShohinColSiz>("where Id_Shohin=@0 order by Id", shohin.Id.ToString());

		var (view, vm) = await OpenAsync<HachuInputView, HachuInputViewModel>("発注入力");
		await using var rec = await RecAsync("input_hachu");
		await ScreenRecorder.Pause(1.5);
		rec.Mark("list", view, "発注の一覧");
		vm.GoToNewCommand.Execute(null);
		await Idle();
		await ScreenRecorder.Pause(1.0);
		rec.Mark("new", view, "新規登録");
		var e = vm.CurrentEdit;
		e.DenDay = Today;
		e.NouhinDay = "20261121";
		e.Kubun = (int)EnumHachu.Hachu;
		e.Rate = 100;
		await ScreenRecorder.Pause(0.6);
		e.Id_Shiire = shiire.Id; e.VShiire = new CodeNameView(shiire.Id, shiire.Code, shiire.Name);
		rec.Mark("shiire", view, "仕入先");
		await ScreenRecorder.Pause(0.9);
		e.Id_Soko = soko.Id; e.VSoko = new CodeNameView(soko.Id, soko.Code, soko.Name);
		await ScreenRecorder.Pause(0.7);
		e.Id_Shain = shain.Id; e.VShain = new CodeNameView(shain.Id, shain.Code, shain.Name);
		await ScreenRecorder.Pause(1.0);
		var grid = Find<DataGrid>(view, "MeisaiGrid");
		var qty = new[] { 6, 10, 8, 4 };
		var no = 1;
		foreach (var sku in skus.Take(9)) {
			var su = qty[(no - 1) % qty.Length];
			var m = new Tran99Meisai {
				No = no++, Id_Shohin = shohin.Id, Code_Shohin = shohin.Code, Mei_Shohin = shohin.Name,
				Id_Col = sku.Id_Col, Code_Col = sku.Code_Col, Mei_Col = sku.Mei_Col,
				Id_Siz = sku.Id_Siz, Code_Siz = sku.Code_Siz, Mei_Siz = sku.Mei_Siz, JanCode = sku.Jan1,
				Su = su, Tanka = shohin.TankaGenka, Kingaku = (long)su * shohin.TankaGenka, Jodai = shohin.TankaJodai, Gedai = shohin.TankaGenka, Id_Tax = shohin.Id_Tax,
			};
			vm.EditMeisai.Add(m);
			vm.SelectedMeisai = m;
			await ScreenRecorder.Pause(0.45);
		}
		if (grid != null) rec.Mark("meisai", grid, "カラー・サイズごとの数量");
		await ScreenRecorder.Pause(2.5);
		await RunCmd(vm.DoInsertOnDetailTabCommand);
		rec.Mark("insert", view, "登録");
		await ScreenRecorder.Pause(2.5);
		s.Check("input_hachu: 発注登録", vm.CurrentEdit.Id > 0, new { vm.CurrentEdit.Id });
		// 発注残（納品予定照会）
		var (v2, q) = await OpenAsync<DeliveryScheduleInquiryView, DeliveryScheduleInquiryViewModel>("納品予定照会");
		rec.Mark("zan", v2, "発注残・納品予定");
		if (q.Rows.Count == 0) await RunCmd(q.SearchCommand);
		await ScreenRecorder.Pause(5.0);
	}

	// ------------------------------------------------------------------ 03 仕入配分入力（発注を呼び出して店舗へ配分）
	static async Task InputHaibunAsync(VmSession s) {
		var (view, vm) = await OpenAsync<HachuHaibunInputView, HachuHaibunInputViewModel>("仕入配分入力(伝票別)");
		vm.HachuDayFrom = new DateTime(2026, 10, 1);
		vm.HachuDayTo = new DateTime(2026, 11, 7);
		await using var rec = await RecAsync("input_haibun");
		await ScreenRecorder.Pause(1.0);
		await RunCmd(vm.DoSearchCommand);
		if (Find<DataGrid>(view, "SearchGrid") is { } sg) rec.Mark("search", sg, "入荷を待つ発注");
		await ScreenRecorder.Pause(2.5);
		var row = vm.SearchRows.OrderByDescending(r => r.ToString()).FirstOrDefault();
		var pick = vm.SearchRows.LastOrDefault();
		if (pick == null) return;
		vm.SelectedSearchRow = pick;
		await ScreenRecorder.Pause(1.0);
		await RunCmd(vm.GoToEditCommand);
		await ScreenRecorder.Pause(1.5);
		if (Find<DataGrid>(view, "HaibunGrid") is { } hg) rec.Mark("matrix", hg, "店舗 × カラー・サイズ");
		// 店舗ごとに配分数を入れる
		var tenpo = vm.TenpoRows.ToList();
		var pattern = new[] { 2, 1, 1, 1, 2, 1 };
		var k = 0;
		foreach (var r in tenpo.Take(4)) {
			foreach (var c in r.Cells.Take(6)) {
				c.Su = pattern[k++ % pattern.Length];
				await ScreenRecorder.Pause(0.18);
			}
		}
		rec.Mark("filled", view, "配分数を入力");
		await ScreenRecorder.Pause(2.5);
		await RunCmd(vm.DoRegisterCommand);
		rec.Mark("register", view, "登録");
		await ScreenRecorder.Pause(3.0);
		_ = row;
	}

	// ------------------------------------------------------------------ 03 店舗売上入力
	static async Task InputTenuriAsync(VmSession s) {
		var shop = await One<MasterTokui>(s, "where Code=@0", "000101");
		var items = new[] { ("VL-W002", 1), ("VL-A006", 1), ("KT-W002", 2) };
		var (view, vm) = await OpenAsync<ShopUriageInputView, ShopUriageInputViewModel>("店舗売上入力");
		await using var rec = await RecAsync("input_tenuri");
		await ScreenRecorder.Pause(1.5);
		rec.Mark("list", view, "店舗売上の一覧");
		vm.GoToNewCommand.Execute(null);
		await Idle();
		await ScreenRecorder.Pause(1.0);
		var e = vm.CurrentEdit;
		e.DenDay = Today;
		e.Id_Tenpo = shop.Id; e.VTenpo = new CodeNameView(shop.Id, shop.Code, shop.Name);
		e.Id_Soko = shop.Id;
		rec.Mark("shop", view, shop.Name);
		await ScreenRecorder.Pause(1.2);
		var no = 1;
		foreach (var (code, su) in items) {
			var sh = await One<MasterShohin>(s, "where Code=@0", code);
			var sku = (await s.QueryAsync<DerivedShohinColSiz>("where Id_Shohin=@0 order by Id", sh.Id.ToString())).Skip(1).FirstOrDefault()
				?? throw new InvalidOperationException(code);
			var m = new Tran99Meisai {
				No = no++, Id_Shohin = sh.Id, Code_Shohin = sh.Code, Mei_Shohin = sh.Name,
				Id_Col = sku.Id_Col, Code_Col = sku.Code_Col, Mei_Col = sku.Mei_Col, Id_Siz = sku.Id_Siz, Code_Siz = sku.Code_Siz, Mei_Siz = sku.Mei_Siz,
				JanCode = sku.Jan1, Su = su, Tanka = sh.TankaJodai, Kingaku = (long)su * sh.TankaJodai, Jodai = sh.TankaJodai, Gedai = sh.TankaGenka, Id_Tax = sh.Id_Tax,
			};
			vm.EditMeisai.Add(m);
			vm.SelectedMeisai = m;
			await ScreenRecorder.Pause(0.9);
		}
		if (Find<DataGrid>(view, "MeisaiGrid") is { } g) rec.Mark("meisai", g, "商品・数量");
		await ScreenRecorder.Pause(2.0);
		await RunCmd(vm.DoInsertOnDetailTabCommand);
		rec.Mark("insert", view, "登録");
		s.Check("input_tenuri: 店舗売上登録", vm.CurrentEdit.Id > 0, new { vm.CurrentEdit.Id });
		await ScreenRecorder.Pause(3.0);
	}

	// ------------------------------------------------------------------ 03 在庫問合せ
	static async Task InputZaikoAsync(VmSession s) {
		var (view, vm) = await OpenAsync<ZaikoQueryView, ZaikoQueryViewModel>("在庫問合せ");
		await using var rec = await RecAsync("input_zaiko");
		await ScreenRecorder.Pause(1.2);
		await DemoTyping.TypeAsync(v => vm.ShohinCodeFrom = v, "VL-W002", 110);
		await DemoTyping.TypeAsync(v => vm.ShohinCodeTo = v, "VL-W002", 70);
		rec.Mark("cond", view, "品番を入れて検索");
		await ScreenRecorder.Pause(0.8);
		await RunCmd(vm.SearchCommand);
		await ScreenRecorder.Pause(1.5);
		var p = vm.ProductRows.FirstOrDefault();
		if (p != null) {
			vm.SelectedProduct = p;
			await ScreenRecorder.Pause(0.8);
			await RunCmd(vm.OpenStockTabCommand);
			await WaitAsync(() => !vm.IsBusy, 15_000);
			await Idle();
			rec.Mark("stock", view, "店舗・倉庫 × カラー・サイズの在庫");
		}
		await ScreenRecorder.Pause(6.0);
	}

	// ------------------------------------------------------------------ 04 帳票（画面プレビュー）
	static IAsyncRelayCommand PdfCommand(BaseReportViewModel vm) =>
		typeof(BaseReportViewModel).GetProperty("DoOutputPdfCommand", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
			?.GetValue(vm) as IAsyncRelayCommand ?? throw new InvalidOperationException("DoOutputPdfCommand を取得できません。");

	static async Task ShowPdfAsync(ScreenRecorder rec, BaseReportViewModel vm, string label, double hold) {
		await RunCmd(PdfCommand(vm));
		await WaitAsync(() => Application.Current.Windows.OfType<CvWpfclient.Views.Sub.WebPdfView>().Any(), 30_000);
		var pdf = Application.Current.Windows.OfType<CvWpfclient.Views.Sub.WebPdfView>().LastOrDefault();
		if (pdf != null) {
			Place(pdf);
			pdf.Activate();
			await ScreenRecorder.Pause(2.0); // WebView2 の描画待ち
			rec.Mark("pdf:" + label, pdf, label);
			await ScreenRecorder.Pause(hold);
			pdf.Close();
			await Idle();
		}
	}

	static async Task ReportPreviewAsync(VmSession s) {
		var (v1, quick) = await OpenAsync<SalesQuickReportView, SalesQuickReportViewModel>("売上速報");
		await using var rec = await RecAsync("report_preview");
		await ScreenRecorder.Pause(1.0);
		quick.TargetDay = "2026/11/06";
		rec.Mark("cond:quick", v1, "売上速報");
		await ScreenRecorder.Pause(1.5);
		await ShowPdfAsync(rec, quick, "売上速報", 4.0);
		v1.Close();
		var (v2, budget) = await OpenAsync<ShopBrandBudgetVsActualView, ShopBrandBudgetVsActualViewModel>("店舗ブランド別予算実績対比");
		budget.SelectedYearMonth = new DateTime(2026, 10, 1);
		rec.Mark("cond:budget", v2, "予算実績対比");
		await ScreenRecorder.Pause(1.5);
		await ShowPdfAsync(rec, budget, "予算実績対比", 4.0);
	}

	static async Task ReportCostlessAsync(VmSession s) {
		var (v1, vm) = await OpenAsync<SalesQuickReportCostlessView, SalesQuickReportCostlessViewModel>("売上速報(原価無)");
		await using var rec = await RecAsync("report_costless");
		await ScreenRecorder.Pause(1.0);
		vm.TargetDay = "2026/11/06";
		rec.Mark("cond", v1, "店舗配布版（原価なし）");
		await ScreenRecorder.Pause(1.2);
		await ShowPdfAsync(rec, vm, "原価なし", 4.0);
	}

	// ------------------------------------------------------------------ 05 AI照会
	static async Task AiQueryAsync(VmSession s) {
		var (view, vm) = await OpenAsync<AiQueryView, AiQueryViewModel>("AI照会（CSV）");
		await using var rec = await RecAsync("ai_query");
		await ScreenRecorder.Pause(1.5);
		await DemoTyping.TypeAsync(v => vm.InputText = v,
			"2026年10月の店舗売上を、店舗別・ブランド別に数量と税抜金額で集計して。計上日基準で、返品と値引は差し引いて。", 70);
		rec.Mark("ask", view, "ふだんの言葉で質問");
		await ScreenRecorder.Pause(1.0);
		await RunAiAsync(vm, vm.SendCommand);
		rec.Mark("reply", view, "AIが条件を確認");
		await ScreenRecorder.Pause(2.5);
		for (var i = 0; i < 4 && vm.ChatStatus == EnumAiQueryStatus.Asking; i++) {
			var last = vm.ChatLines.LastOrDefault()?.Text ?? string.Empty;
			var answer = last.Contains("A ") || last.Contains("A.") || last.Contains("Ａ") ? "A でお願いします。" : "おまかせします。その条件で進めてください。";
			await DemoTyping.TypeAsync(v => vm.InputText = v, answer, 80);
			await ScreenRecorder.Pause(0.6);
			await RunAiAsync(vm, vm.SendCommand);
			await ScreenRecorder.Pause(2.0);
		}
		if (vm.ChatStatus == EnumAiQueryStatus.Confirming) {
			await RunAiAsync(vm, vm.GenerateCommand);
			rec.Mark("sql", view, "SQLを作成");
			await ScreenRecorder.Pause(3.0);
		}
		await RunCmd(vm.ValidateCommand);
		rec.Mark("validate", view, "実行前に検査");
		await ScreenRecorder.Pause(2.5);
		await RunCmd(vm.RunCommand);
		await WaitAsync(() => !vm.IsBusy && (vm.HasResult || vm.ResultInfo.Length > 0), 120_000);
		await Idle();
		rec.Mark("result", view, "結果をその場で表示");
		await ScreenRecorder.Pause(5.0);
		await RunCmd(vm.SaveSharedCommand);
		rec.Mark("shared", view, "社内で共有");
		await ScreenRecorder.Pause(2.5);
		// 書き換える命令は受け付けない
		vm.DetailTabIndex = 0; // 検査結果が出る「条件・SQL」タブ
		vm.IsSqlEditable = true;
		await Idle();
		await DemoTyping.TypeAsync(v => vm.SqlText = v, "DELETE FROM Tran01Tenuri", 60);
		await ScreenRecorder.Pause(0.8);
		await RunCmd(vm.ValidateCommand);
		rec.Mark("reject", view, "書き換える命令は拒否");
		await ScreenRecorder.Pause(4.0);
	}

	static async Task RunAiAsync(AiQueryViewModel vm, IAsyncRelayCommand cmd) {
		var t = cmd.ExecuteAsync(null);
		await WaitAsync(() => vm.IsAiWaiting || vm.IsBusy, 10_000);
		await t;
		await WaitAsync(() => !vm.IsBusy, 600_000);
		await Idle();
	}

	// ------------------------------------------------------------------ 06 システム
	static async Task SysPermAsync(VmSession s) {
		var (view, vm) = await OpenAsync<SysPermissionProfileMenteView, SysPermissionProfileMenteViewModel>("権限プロファイル設定");
		await WaitAsync(() => vm.Profiles.Count > 0 && vm.MenuRows.Count > 0 && !vm.IsBusy, 20_000);
		await using var rec = await RecAsync("sys_perm");
		await ScreenRecorder.Pause(1.0);
		rec.Mark("perm", view, "メニューごとの権限");
		await ScreenRecorder.Pause(5.0);
	}

	static async Task SysSchedAsync(VmSession s) {
		var (view, vm) = await OpenAsync<SysSchedulerJobMenteView, SysSchedulerJobMenteViewModel>("自動実行管理マスタ");
		await using var rec = await RecAsync("sys_sched");
		await ScreenRecorder.Pause(1.0);
		rec.Mark("sched", view, "夜間の自動実行");
		await ScreenRecorder.Pause(5.0);
	}
}
