using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CvBase;
using CvBaseSqlite;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels._32LoyalCustomer;
using CvWpfclient.Views._32LoyalCustomer;
using Microsoft.Data.Sqlite;

namespace UatVm.Scenarios;

/// <summary>
/// 独立DBで「店舗別キャンペーン設定」「商品店舗別ポイント設定」の実画面操作を確認する。
/// 一覧条件・選択切替確認・Preview/Apply(重複解除)・Vdu競合・条件変更での無効化・コピー・取消・対象解除・
/// 商品のコード追加/CSV取込/未登録エラー・商品全店の店舗無効・閉じる確認と、標準/最小サイズの画像を取る。
/// </summary>
public static class PointCampaignTargetScenario {
	static string? databasePath;

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
		var basis = new MasterPointBase { Code = "PCBASE", Name = "キャンペーン用ベース", DayFrom = "20261001", DayTo = "20261231", IsEnabled = 1, PointUnitPrice = 100, PointAmountProper = 1, PointAmountSale = 1 };
		db.Insert(basis);
		var shops = new Dictionary<string, MasterTokui>();
		foreach (var (code, type) in new[] { ("PS1", 6), ("PS2", 6), ("PS3", 6), ("PS4", 3), ("PW9", 0) }) {
			var shop = new MasterTokui { Code = code, Name = $"UAT店舗{code}", Ryaku = code, TenType = type };
			db.Insert(shop);
			shops[code] = shop;
		}
		var shohins = new Dictionary<string, MasterShohin>();
		foreach (var code in new[] { "PP1", "PP2", "PP3" }) {
			var shohin = new MasterShohin { Code = code, Name = $"UAT商品{code}" };
			db.Insert(shohin);
			shohins[code] = shohin;
		}
		// 対象2表は製品では汎用書込み禁止のため、サーバ起動前に初期状態だけ直接用意する。
		var a = Campaign(basis.Id, "PCA", 1, "20261001", "20261031"); db.Insert(a);
		var b = Campaign(basis.Id, "PCB", 1, "20261010", "20261020"); db.Insert(b);
		var x = Campaign(basis.Id, "PCX", 1, "20261201", "20261210"); db.Insert(x);
		var c = Campaign(basis.Id, "PCC", 2, "20261001", "20261031"); db.Insert(c);
		var d = Campaign(basis.Id, "PCD", 3, "20261001", "20261031"); db.Insert(d);
		var e = Campaign(basis.Id, "PCE", 3, "20261005", "20261015"); db.Insert(e);
		// 画面に出せない保存済み行: PCM=店種0の倉庫、PCN=マスタにない商品Id。読込直後に未保存扱いにしないことを見る。
		var m = Campaign(basis.Id, "PCM", 1, "20261001", "20261031"); db.Insert(m);
		var n = Campaign(basis.Id, "PCN", 2, "20261001", "20261031"); db.Insert(n);
		db.Insert(new MasterPointCampaignShop { Id_PointCampaign = m.Id, Id_Tenpo = shops["PW9"].Id });
		db.Insert(new MasterPointCampaignShohin { Id_PointCampaign = n.Id, Id_Shohin = shohins["PP3"].Id });
		db.Insert(new MasterPointCampaignShohin { Id_PointCampaign = n.Id, Id_Shohin = 999999 });
		db.Insert(new MasterPointCampaignShop { Id_PointCampaign = a.Id, Id_Tenpo = shops["PS1"].Id });
		foreach (var s in new[] { "PS1", "PS2", "PS3" }) db.Insert(new MasterPointCampaignShop { Id_PointCampaign = b.Id, Id_Tenpo = shops[s].Id });
		db.Insert(new MasterPointCampaignShop { Id_PointCampaign = d.Id, Id_Tenpo = shops["PS1"].Id });
		db.Insert(new MasterPointCampaignShohin { Id_PointCampaign = d.Id, Id_Shohin = shohins["PP1"].Id });
		db.Insert(new MasterPointCampaignShop { Id_PointCampaign = e.Id, Id_Tenpo = shops["PS2"].Id });
		foreach (var s in new[] { "PP1", "PP2" }) db.Insert(new MasterPointCampaignShohin { Id_PointCampaign = e.Id, Id_Shohin = shohins[s].Id });
		databasePath = path;
	}

	static MasterPointCampaign Campaign(long basis, string code, int priority, string from, string to) => new() {
		Code = code, Name = $"キャンペーン{code}", Id_PointBase = basis, DayFrom = from, DayTo = to, IsEnabled = 1, PriorityType = priority,
		PointUnitPrice = 100, PointAmountProper = 2, PointAmountSale = 1, RankKubun = 0,
	};

	public static async Task RunAsync(VmSession session) {
		if (databasePath is null) throw new InvalidOperationException("専用Seederを実行してください。");
		var screens = Path.Combine(Path.GetDirectoryName(databasePath)!, "point-campaign-screens-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
		Directory.CreateDirectory(screens);
		session.Note("対象", new { Database = databasePath, Screens = screens });
		// 選択切替・閉じる確認は「いいえ」、それ以外の確認は「はい」で応答する。
		session.SetDialogResponder(r => r.Button is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel
			? (r.Message.Contains("別のキャンペーン", StringComparison.Ordinal) || r.Message.Contains("閉じますか", StringComparison.Ordinal) ? MessageBoxResult.No : MessageBoxResult.Yes)
			: MessageBoxResult.OK);
		try {
			var shop = session.OpenView<ShopCampaignSettingView, ShopCampaignSettingViewModel>();
			var item = session.OpenView<ShohinShopPointSettingView, ShohinShopPointSettingViewModel>();
			await Ready(shop); await Ready(item);
			var ids = await Ids(session);

			// ---- 店舗別キャンペーン設定 ----
			await List(shop, "店別一覧(10月)", "");
			session.Check("店別:優先区分1・期間重複だけを一覧", Codes(shop.Vm).SequenceEqual(["PCA", "PCB", "PCM"]), Codes(shop.Vm));
			session.Check("店別:対象件数", Row(shop.Vm, "PCA").ShopCount == 1 && Row(shop.Vm, "PCB").ShopCount == 3);
			// 初期化で入る既定店舗も含め、店種3・6だけを出し倉庫(PW9)は出さない。
			session.Check("店別:店舗一覧は店種3・6だけ", shop.Vm.Shops.All(x => x.Shop.TenType is 3 or 6)
				&& new[] { "PS1", "PS2", "PS3", "PS4" }.All(c => shop.Vm.Shops.Any(x => x.Shop.Code == c)) && shop.Vm.Shops.All(x => x.Shop.Code != "PW9"),
				shop.Vm.Shops.Select(x => new { x.Shop.Code, x.Shop.TenType }));
			await Select(shop, "PCA");
			session.Check("店別:選択で保存済み店舗を表示", Checked(shop.Vm).SequenceEqual(["PS1"]) && !shop.Vm.IsDirty && shop.Vm.IsShopEnabled);
			Check(shop.Vm, "PS2", true);
			session.Check("店別:変更で未保存・条件入力不可", shop.Vm.IsDirty && !shop.Vm.IsConditionEditable && shop.Vm.DoSaveCommand.CanExecute(null));
			session.ClearDialogs();
			// 実View(DataGrid の SelectedItem TwoWay)経由で選択を切り替える。
			CampaignGrid(shop.View).SelectedItem = Row(shop.Vm, "PCB");
			await Settle(shop.View);
			session.Check("店別:未保存で別選択→確認いいえで編集継続", shop.Vm.Target?.Code == "PCA" && shop.Vm.IsDirty
				&& session.Dialogs.Any(x => x.Request.Message.Contains("別のキャンペーン", StringComparison.Ordinal)));
			await CaptureLayout(session, shop.View, screens, "01_Shop_Dirty");

			session.ClearDialogs();
			// 保存処理中は一覧・店舗一覧を無効化し、選択切替を受け付けない。
			var saving = shop.Vm.DoSaveCommand.ExecuteAsync(null);
			var busyGrids = Descendants(shop.View).OfType<DataGrid>().ToList();
			session.Check("店別:保存処理中は一覧・店舗一覧を無効化", shop.Vm.IsBusy && busyGrids.All(g => !g.IsEnabled), busyGrids.Select(g => g.IsEnabled));
			shop.Vm.SelectedCampaign = Row(shop.Vm, "PCB");
			await saving;
			await Settle(shop.View);
			session.Check("店別:保存処理中の選択切替は無視", shop.Vm.Target?.Code == "PCA");
			// 保存後の一覧再取得(Clear で DataGrid が null を押し戻す)で未保存確認・選択解除が起きないこと。
			session.Check("店別:保存後の再取得で未保存確認を出さない", !session.Dialogs.Any(x => x.Request.Message.Contains("未保存の変更", StringComparison.Ordinal)),
				session.Dialogs.Select(x => x.Request.Message));
			session.Check("店別:保存後も実DataGridの選択がPCA", (CampaignGrid(shop.View).SelectedItem as PointCampaignListRow)?.Campaign.Code == "PCA");
			var conflictDialog = session.Dialogs.FirstOrDefault(x => x.Request.Message.Contains("重複", StringComparison.Ordinal));
			session.Check("店別:Previewの重複(PCBのPS1・PS2)を一覧表示して確認", conflictDialog != null
				&& conflictDialog.Request.AppendedMessage.Contains("PCB", StringComparison.Ordinal)
				&& conflictDialog.Request.AppendedMessage.Contains("PS1", StringComparison.Ordinal) && conflictDialog.Request.AppendedMessage.Contains("PS2", StringComparison.Ordinal)
				&& !conflictDialog.Request.AppendedMessage.Contains("PS3", StringComparison.Ordinal), conflictDialog?.Request);
			session.Check("店別:Apply後 PCA={PS1,PS2}・PCB={PS3}", (await ShopCodes(session, ids, "PCA")).SequenceEqual(["PS1", "PS2"])
				&& (await ShopCodes(session, ids, "PCB")).SequenceEqual(["PS3"]));
			session.Check("店別:保存後に一覧件数・選択を再表示", Row(shop.Vm, "PCA").ShopCount == 2 && Row(shop.Vm, "PCB").ShopCount == 1
				&& shop.Vm.Target?.Code == "PCA" && !shop.Vm.IsDirty && Checked(shop.Vm).SequenceEqual(["PS1", "PS2"]), shop.Vm.Message);
			session.CheckEqual("店別:保存後のVduを画面へ反映", (await Campaign(session, "PCA")).Vdu, shop.Vm.Target?.Vdu ?? 0);

			// 他端末の更新（キャンペーンのVduを進める）後の保存は競合として拒否し、編集対象を外す。
			var other = await Campaign(session, "PCA");
			other.Memo = "他端末で更新";
			await session.UpdateAsync(other);
			Check(shop.Vm, "PS3", true);
			session.ClearDialogs();
			await shop.RunAsync("店別:競合時の登録", vm => vm.DoSaveCommand);
			session.Check("店別:Vdu競合はエラー表示・編集対象を外し再取得案内", !shop.Vm.HasTarget && !shop.Vm.IsDirty
				&& session.Dialogs.Any(x => x.Request.Image == MessageBoxImage.Error) && shop.Vm.Message.Contains("一覧取得", StringComparison.Ordinal));
			session.Check("店別:競合時はDB不変", (await ShopCodes(session, ids, "PCA")).SequenceEqual(["PS1", "PS2"]));

			// 条件変更で旧結果・編集対象を無効化する。
			await List(shop, "店別一覧(再取得)", "");
			await Select(shop, "PCA");
			shop.Input("条件:コードPCB", vm => vm.SearchCode = "PCB");
			session.Check("店別:条件変更で一覧・編集対象を無効化", shop.Vm.Campaigns.Count == 0 && !shop.Vm.HasTarget && !shop.Vm.DoSaveCommand.CanExecute(null));
			await shop.RunAsync("店別一覧(PCB)", vm => vm.DoListCommand);
			session.Check("店別:コード前方一致", Codes(shop.Vm).SequenceEqual(["PCB"]), Codes(shop.Vm));
			shop.Input("条件:コードPCM", vm => vm.SearchCode = "PCM");
			await shop.RunAsync("店別一覧(PCM)", vm => vm.DoListCommand);
			await Select(shop, "PCM");
			session.Check("店別:画面にない保存済み店舗は未保存扱いせず件数を案内", !shop.Vm.IsDirty && Checked(shop.Vm).Count == 0
				&& shop.Vm.Message.Contains("店舗一覧にない対象店舗 1 件", StringComparison.Ordinal) && shop.Vm.DoClearTargetsCommand.CanExecute(null), shop.Vm.Message);
			shop.Input("条件:コードPCB", vm => vm.SearchCode = "PCB");
			await shop.RunAsync("店別一覧(PCB再)", vm => vm.DoListCommand);
			await Select(shop, "PCB");
			shop.Input("コピー元PCA", vm => vm.SelectedCopySource = vm.CopySources.Single(c => c.Code == "PCA"));
			await shop.RunAsync("店別:他キャンペーンからコピー", vm => vm.CopyShopsCommand);
			session.Check("店別:コピーで対象店舗を置換", Checked(shop.Vm).SequenceEqual(["PS1", "PS2"]) && shop.Vm.IsDirty);
			await shop.RunAsync("店別:取消", vm => vm.DoRevertCommand);
			session.Check("店別:取消で保存済みへ戻す", Checked(shop.Vm).SequenceEqual(["PS3"]) && !shop.Vm.IsDirty);
			shop.Input("絞込PS1", vm => vm.ShopFilter = "PS1");
			shop.Run("店別:表示中を全選択", vm => vm.SelectAllShopsCommand);
			session.Check("店別:絞込中の全選択は表示行だけ", Checked(shop.Vm).SequenceEqual(["PS1", "PS3"]));
			shop.Input("絞込解除", vm => vm.ShopFilter = "");
			shop.Run("店別:全解除", vm => vm.ClearAllShopsCommand);
			session.Check("店別:全解除", Checked(shop.Vm).Count == 0);
			await shop.RunAsync("店別:取消2", vm => vm.DoRevertCommand);
			session.ClearDialogs();
			await shop.RunAsync("店別:対象解除", vm => vm.DoClearTargetsCommand);
			session.Check("店別:対象解除は確認後に空で保存", session.Dialogs.Any(x => x.Request.Message.Contains("解除しますか", StringComparison.Ordinal))
				&& (await ShopCodes(session, ids, "PCB")).Count == 0 && Row(shop.Vm, "PCB").ShopCount == 0);
			shop.Input("条件:全コード", vm => vm.SearchCode = "");
			await shop.RunAsync("店別一覧(画像用)", vm => vm.DoListCommand);
			await Select(shop, "PCA");
			await CheckLayout(session, shop.View, screens, "02_Shop");

			// ---- 商品店舗別ポイント設定 ----
			await List(item, "商品店別一覧(10月)", "");
			session.Check("商品:優先区分2・3を一覧", Codes(item.Vm).SequenceEqual(["PCC", "PCD", "PCE", "PCN"]), Codes(item.Vm));
			session.Check("商品:対象件数", Row(item.Vm, "PCD").ShohinCount == 1 && Row(item.Vm, "PCD").ShopCount == 1 && Row(item.Vm, "PCE").ShohinCount == 2);
			await Select(item, "PCC");
			session.Check("商品:商品全店は店舗一覧無効", !item.Vm.IsShopEnabled && item.Vm.IsShopNotRequired && !item.Vm.SelectAllShopsCommand.CanExecute(null));
			item.Input("商品コードPP1", vm => vm.ShohinCodeInput = "PP1");
			await item.RunAsync("商品:コード追加", vm => vm.AddShohinCommand);
			session.ClearDialogs();
			item.Input("未登録コード", vm => vm.ShohinCodeInput = "NOPE");
			await item.RunAsync("商品:未登録コード追加", vm => vm.AddShohinCommand);
			session.Check("商品:未登録コードは警告し追加しない", item.Vm.Shohins.Select(x => x.Code).SequenceEqual(["PP1"])
				&& session.Dialogs.Any(x => x.Request.Image == MessageBoxImage.Warning));
			var csv = Path.Combine(screens, "shohin.csv");
			await File.WriteAllTextAsync(csv, "商品コード\r\nPP2\r\nNOPE2\r\nPP1\r\n", new UTF8Encoding(true));
			session.ClearDialogs();
			await item.Vm.ImportCsvFileAsync(csv, CancellationToken.None);
			var csvDialog = session.Dialogs.FirstOrDefault(x => x.Request.Image == MessageBoxImage.Warning);
			session.Check("商品:CSV取込(見出し除外・登録済み追加・重複無視・未登録エラー)", item.Vm.Shohins.Select(x => x.Code).SequenceEqual(["PP1", "PP2"])
				&& csvDialog != null && csvDialog.Request.AppendedMessage.Contains("3行目: NOPE2", StringComparison.Ordinal)
				&& !csvDialog.Request.AppendedMessage.Contains("商品コード", StringComparison.Ordinal), csvDialog?.Request);
			await item.RunAsync("商品:商品全店の登録", vm => vm.DoSaveCommand);
			await Select(item, "PCN");
			session.Check("商品:マスタにない保存済み商品は未保存扱いせず件数を案内", !item.Vm.IsDirty && item.Vm.Shohins.Select(x => x.Code).SequenceEqual(["PP3"])
				&& item.Vm.ShohinCount == 1 && item.Vm.Message.Contains("商品マスタにない対象商品 1 件", StringComparison.Ordinal), item.Vm.Message);
			await Select(item, "PCC");
			session.Check("商品:商品全店は商品だけ保存", (await ShohinCodes(session, ids, "PCC")).SequenceEqual(["PP1", "PP2"]) && (await ShopCodes(session, ids, "PCC")).Count == 0);
			item.Run("商品:行削除", vm => vm.RemoveShohinsCommand, new System.Collections.ArrayList { item.Vm.Shohins.Single(x => x.Code == "PP2") });
			session.Check("商品:行削除で未保存", item.Vm.IsDirty && item.Vm.Shohins.Count == 1);
			await item.RunAsync("商品:取消", vm => vm.DoRevertCommand);

			await Select(item, "PCD");
			session.Check("商品:商品店別は店舗一覧有効", item.Vm.IsShopEnabled && Checked(item.Vm).SequenceEqual(["PS1"]) && item.Vm.Shohins.Select(x => x.Code).SequenceEqual(["PP1"]));
			Check(item.Vm, "PS2", true);
			item.Input("商品コードPP2", vm => vm.ShohinCodeInput = "PP2");
			await item.RunAsync("商品:コード追加PP2", vm => vm.AddShohinCommand);
			session.ClearDialogs();
			await item.RunAsync("商品:商品店別の登録(重複あり)", vm => vm.DoSaveCommand);
			var itemConflict = session.Dialogs.FirstOrDefault(x => x.Request.Message.Contains("重複", StringComparison.Ordinal));
			session.Check("商品:重複(PCEのPP1・PP2)を確認表示", itemConflict != null && itemConflict.Request.AppendedMessage.Contains("PCE", StringComparison.Ordinal)
				&& itemConflict.Request.AppendedMessage.Contains("PP1", StringComparison.Ordinal) && itemConflict.Request.AppendedMessage.Contains("PP2", StringComparison.Ordinal), itemConflict?.Request);
			session.Check("商品:Apply後 PCD={PS1,PS2}×{PP1,PP2}・PCEの商品を外す", (await ShopCodes(session, ids, "PCD")).SequenceEqual(["PS1", "PS2"])
				&& (await ShohinCodes(session, ids, "PCD")).SequenceEqual(["PP1", "PP2"]) && (await ShohinCodes(session, ids, "PCE")).Count == 0
				&& (await ShopCodes(session, ids, "PCE")).SequenceEqual(["PS2"]));

			// 未保存のまま閉じる操作は確認し、「いいえ」なら閉じない。
			Check(item.Vm, "PS3", true);
			session.ClearDialogs();
			item.Vm.ExitCommand.Execute(null);
			await Settle(item.View);
			session.Check("商品:未保存で閉じる→確認いいえで閉じない", item.View.IsVisible && item.Vm.IsDirty
				&& session.Dialogs.Any(x => x.Request.Message.Contains("閉じますか", StringComparison.Ordinal)));
			await item.RunAsync("商品:取消(閉じる確認後)", vm => vm.DoRevertCommand);
			await CheckLayout(session, item.View, screens, "03_Shohin_ShohinShop");
			await Select(item, "PCC");
			await CheckLayout(session, item.View, screens, "04_Shohin_AllShops");
		} finally { session.SetDialogResponder(null); }
	}

	// ---- 補助 ----

	sealed record CampaignIds(Dictionary<string, long> Shops, Dictionary<string, long> Shohins);

	static async Task<CampaignIds> Ids(VmSession session) => new(
		(await session.QueryAsync<MasterTokui>("WHERE Code LIKE 'P%'")).ToDictionary(x => x.Code, x => x.Id),
		(await session.QueryAsync<MasterShohin>("WHERE Code LIKE 'PP%'")).ToDictionary(x => x.Code, x => x.Id));

	static async Task<MasterPointCampaign> Campaign(VmSession session, string code) => (await session.QueryAsync<MasterPointCampaign>("WHERE Code=@0", code)).Single();

	static async Task<List<string>> ShopCodes(VmSession session, CampaignIds ids, string code) {
		var id = (await Campaign(session, code)).Id;
		var rows = await session.QueryAsync<MasterPointCampaignShop>($"WHERE Id_PointCampaign={id}");
		return [.. rows.Select(x => ids.Shops.Single(s => s.Value == x.Id_Tenpo).Key).OrderBy(x => x, StringComparer.Ordinal)];
	}

	static async Task<List<string>> ShohinCodes(VmSession session, CampaignIds ids, string code) {
		var id = (await Campaign(session, code)).Id;
		var rows = await session.QueryAsync<MasterPointCampaignShohin>($"WHERE Id_PointCampaign={id}");
		return [.. rows.Select(x => ids.Shohins.Single(s => s.Value == x.Id_Shohin).Key).OrderBy(x => x, StringComparer.Ordinal)];
	}

	static async Task List<TVm>(ViewDriver<TVm> driver, string name, string code) where TVm : PointCampaignTargetViewModelBase {
		driver.Input(name + ":条件", vm => {
			vm.SearchCode = code;
			vm.SearchDayFrom = "20261001";
			vm.SearchDayTo = "20261031";
			vm.SearchEnabled = PointCampaignTargetViewModelBase.AllEnabled;
		});
		await driver.RunAsync(name, vm => vm.DoListCommand);
	}

	static async Task Select<TVm>(ViewDriver<TVm> driver, string code) where TVm : PointCampaignTargetViewModelBase {
		// 実View の一覧(DataGrid SelectedItem TwoWay)から選ぶ。
		driver.Input("選択:" + code, vm => CampaignGrid(driver.View).SelectedItem = Row(vm, code));
		await driver.WaitAsync("選択読込:" + code, vm => vm.Target?.Code == code && !vm.IsBusy);
		await Settle(driver.View);
	}

	static DataGrid CampaignGrid(Window view) => Descendants(view).OfType<DataGrid>().First();

	static PointCampaignListRow Row(PointCampaignTargetViewModelBase vm, string code) => vm.Campaigns.Single(x => x.Campaign.Code == code);
	static List<string> Codes(PointCampaignTargetViewModelBase vm) => [.. vm.Campaigns.Select(x => x.Campaign.Code)];
	static List<string> Checked(PointCampaignTargetViewModelBase vm) => [.. vm.Shops.Where(x => x.IsChecked).Select(x => x.Shop.Code).OrderBy(x => x, StringComparer.Ordinal)];
	static void Check(PointCampaignTargetViewModelBase vm, string code, bool value) => vm.Shops.Single(x => x.Shop.Code == code).IsChecked = value;

	static async Task Ready<TVm>(ViewDriver<TVm> driver) where TVm : PointCampaignTargetViewModelBase {
		await Settle(driver.View);
		await driver.WaitAsync("画面初期化", vm => driver.View.IsLoaded && !vm.HasRunningCommand && !vm.IsBusy && vm.Shops.Count > 0);
	}

	/// <summary>標準(1244×860)・最小(900×600)で撮影し、表示崩れ・操作ボタン到達・一覧右端列を確認する。</summary>
	static async Task CheckLayout(VmSession session, Window view, string screens, string name) {
		var originalWidth = view.Width;
		var originalHeight = view.Height;
		try {
			foreach (var minimum in new[] { false, true }) {
				view.Width = minimum ? 900 : 1244;
				view.Height = minimum ? 600 : 860;
				await Settle(view);
				var label = name + (minimum ? "_Minimum900x600" : "_Standard");
				var buttons = Descendants(view).OfType<Button>().Where(x => x.Command != null && x.IsVisible).ToList();
				var hidden = buttons.Where(x => !Reachable(view, x)).Select(x => x.ToolTip?.ToString() ?? "").ToList();
				session.Check(label + ":操作ボタン全表示", hidden.Count == 0, new { Buttons = buttons.Count, Hidden = hidden });
				var grids = Descendants(view).OfType<DataGrid>().ToList();
				foreach (var grid in grids) {
					var rect = grid.TransformToAncestor(view).TransformBounds(new Rect(grid.RenderSize));
					session.Check(label + ":一覧の表示高さ確保", grid.ActualHeight >= 90 && Reachable(view, grid), new { grid.Name, grid.ActualHeight, rect });
				}
				await CaptureLayout(session, view, screens, label);
				var left = grids.First();
				var scroll = Descendants(left).OfType<ScrollViewer>().First();
				scroll.ScrollToRightEnd(); await Settle(view);
				await CaptureLayout(session, view, screens, label + "_ListRightEnd");
				scroll.ScrollToLeftEnd(); await Settle(view);
			}
		} finally { view.Width = originalWidth; view.Height = originalHeight; await Settle(view); }
	}

	static async Task CaptureLayout(VmSession session, Window view, string screens, string name) {
		await Settle(view); await Task.Delay(100); await Settle(view);
		var path = ScreenLayoutCheck.SaveJpeg(view, screens, name);
		session.Note(name + ":画面画像", new { Path = path });
		var issues = ScreenLayoutCheck.Inspect(view).Where(x => x.Kind != ScreenLayoutCheck.LongCellKind && x.Kind != "列が横スクロール外").ToList();
		session.Check(name + ":文字・ボタン見切れなし", issues.Count == 0, new { Count = issues.Count, Issues = issues });
	}

	static IEnumerable<DependencyObject> Descendants(DependencyObject parent) {
		for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
			var child = VisualTreeHelper.GetChild(parent, i);
			yield return child;
			foreach (var descendant in Descendants(child)) yield return descendant;
		}
	}

	static bool Reachable(Window view, FrameworkElement target) {
		var bounds = target.TransformToAncestor(view).TransformBounds(new Rect(target.RenderSize));
		var root = (FrameworkElement)view.Content;
		var visible = root.TransformToAncestor(view).TransformBounds(new Rect(root.RenderSize));
		visible.Inflate(1, 1);
		return target.IsVisible && visible.Contains(bounds);
	}

	static async Task Settle(Window view) => await Application.Current.Dispatcher.InvokeAsync(view.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
}
