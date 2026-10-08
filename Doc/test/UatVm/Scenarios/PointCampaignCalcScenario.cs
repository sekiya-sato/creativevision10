using System.IO;
using System.Windows;
using CvBase;
using CvBaseSqlite;
using CvWpfclient.ViewModels._32LoyalCustomer;
using CvWpfclient.Views._32LoyalCustomer;
using Microsoft.Data.Sqlite;

namespace UatVm.Scenarios;

/// <summary>
/// 独立DBでポイントキャンペーンの付与計算を確認する。
/// 店舗売上の登録・訂正(gRPC Msg201、保存時同期)と、キャンペーンのマスタ変更後に実画面「ポイント再計算」(Msg063)で
/// 台帳(TranPointEvent)・SummaryPoint・会員ポイントへ反映されることを、明細ごとの優先順(商品店別→商品全店→店別→全店)で見る。
/// </summary>
public static class PointCampaignCalcScenario {
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
		// ベース: 100円で1P(伝票単位)。ランク2: 100円で2P
		var basis = new MasterPointBase { Code = "PCBASE", Name = "キャンペーン用ベース", DayFrom = "20261001", DayTo = "20261231", IsEnabled = 1, PointUnitPrice = 100, PointAmountProper = 1, PointAmountSale = 1 };
		db.Insert(basis);
		db.Insert(new MasterPointRank { Id_PointBase = basis.Id, Kubun = 2, Name = "ゴールド", PointUnitPrice = 100, PointAmountProper = 2, PointAmountSale = 2 });
		var shops = new Dictionary<string, MasterTokui>();
		foreach (var code in new[] { "PS1", "PS2" }) {
			var shop = new MasterTokui { Code = code, Name = $"UAT店舗{code}", Ryaku = code, TenType = 6 };
			db.Insert(shop);
			shops[code] = shop;
		}
		var shohins = new Dictionary<string, MasterShohin>();
		foreach (var code in new[] { "PP1", "PP2", "PP3" }) {
			var shohin = new MasterShohin { Code = code, Name = $"UAT商品{code}" };
			db.Insert(shohin);
			shohins[code] = shohin;
		}
		foreach (var (code, rank) in new[] { ("PK1", ""), ("PK2", "2") }) {
			var customer = new MasterEndCustomer { Code = code, Name = $"UAT会員{code}" };
			db.Insert(customer);
			db.Insert(new MasterEndCustomerAccount { Id_Customer = customer.Id, PointRank = rank, Point = 100 });
		}
		// 対象2表は製品では汎用書込み禁止のため、サーバ起動前に直接用意する。
		var all = Campaign(basis.Id, "PCA", 0, 3, rank: 2); db.Insert(all);
		var shop1 = Campaign(basis.Id, "PCS", 1, 2); db.Insert(shop1);
		var item = Campaign(basis.Id, "PCP", 2, 4); db.Insert(item);
		var itemShop = Campaign(basis.Id, "PCD", 3, 5); db.Insert(itemShop);
		var off = Campaign(basis.Id, "PCX", 0, 9); off.IsEnabled = 0; db.Insert(off);
		db.Insert(new MasterPointCampaignShop { Id_PointCampaign = shop1.Id, Id_Tenpo = shops["PS1"].Id });
		db.Insert(new MasterPointCampaignShohin { Id_PointCampaign = item.Id, Id_Shohin = shohins["PP1"].Id });
		db.Insert(new MasterPointCampaignShop { Id_PointCampaign = itemShop.Id, Id_Tenpo = shops["PS1"].Id });
		db.Insert(new MasterPointCampaignShohin { Id_PointCampaign = itemShop.Id, Id_Shohin = shohins["PP2"].Id });
		databasePath = path;
	}

	static MasterPointCampaign Campaign(long basis, string code, int priority, long amount, int rank = 0) => new() {
		Code = code, Name = $"キャンペーン{code}", Id_PointBase = basis, DayFrom = "20261001", DayTo = "20261031", IsEnabled = 1, PriorityType = priority,
		PointUnitPrice = 100, PointAmountProper = amount, PointAmountSale = amount, RankKubun = rank,
	};

	public static async Task RunAsync(VmSession session) {
		if (databasePath is null) throw new InvalidOperationException("専用Seederを実行してください。");
		session.Note("対象", new { Database = databasePath });
		var shop1 = await One<MasterTokui>(session, "PS1");
		var shop2 = await One<MasterTokui>(session, "PS2");
		var pp1 = (await One<MasterShohin>(session, "PP1")).Id;
		var pp2 = (await One<MasterShohin>(session, "PP2")).Id;
		var pp3 = (await One<MasterShohin>(session, "PP3")).Id;
		var k1 = (await One<MasterEndCustomer>(session, "PK1")).Id;
		var k2 = (await One<MasterEndCustomer>(session, "PK2")).Id;

		// 1. 保存時同期(登録)。PS1: PP1=商品全店4P、PP2(セール)=商品店別5P、PP3=店別2P → (4000+5000+2000)/100=110
		var s1 = await session.InsertAsync(Slip("UATPC1", k1, shop1, (pp1, 0, 1000), (pp2, 1, 1000), (pp3, 0, 1000)));
		// PS2・ランク2: PP3=全店(ランク2限定)3P → 30。PS2・ランクなし: ベース1P → 10
		var s2 = await session.InsertAsync(Slip("UATPC2", k2, shop2, (pp3, 0, 1000)));
		var s3 = await session.InsertAsync(Slip("UATPC3", k1, shop2, (pp3, 0, 1000)));
		await CheckGrant(session, "登録:4区分の明細別適用", s1.Id, 110, "Campaign");
		await CheckGrant(session, "登録:ランク限定の全店", s2.Id, 30, "Campaign");
		await CheckGrant(session, "登録:該当なしはベース", s3.Id, 10, "Base");
		await CheckBalance(session, "登録後", k1, 120, k2, 30);

		// 2. 保存時同期(訂正)。PP3 2000円 → 店別 +20 = 130。取消+付与の2行が追記される
		var before = (await Events(session)).Count;
		var latest = (await session.QueryAsync<Tran01Tenuri>("WHERE Memo='UATPC1'")).Single();
		latest.Jmeisai![2].Kingaku = 2000;
		await session.UpdateAsync(latest);
		session.CheckEqual("訂正:取消+付与の2行", before + 2, (await Events(session)).Count);
		await CheckGrant(session, "訂正後", s1.Id, 130, "Campaign");
		await CheckBalance(session, "訂正後", k1, 140, k2, 30);

		// 3. 再計算(実画面)。変更なしなら台帳は増えない
		before = (await Events(session)).Count;
		await Recalc(session, "変更なし");
		session.CheckEqual("再計算:変更なしは追記0", before, (await Events(session)).Count);

		// 4. マスタ変更 → 再計算。店別 2P→6P: PP3 2000*6/100=120 → 40+50+120=210
		var pcs = await One<MasterPointCampaign>(session, "PCS");
		pcs.PointAmountProper = 6;
		pcs.PointAmountSale = 6;
		await session.UpdateAsync(pcs);
		session.CheckEqual("マスタ変更直後は台帳不変", 130L, await ActiveTotal(session, s1.Id));
		await Recalc(session, "店別付与数変更");
		await CheckGrant(session, "再計算:店別変更を反映", s1.Id, 210, "Campaign");

		// 5. 商品全店を無効化 → 再計算。PP1 は店別6Pへ: 60+50+120=230
		var pcp = await One<MasterPointCampaign>(session, "PCP");
		pcp.IsEnabled = 0;
		await session.UpdateAsync(pcp);
		await Recalc(session, "商品全店無効化");
		await CheckGrant(session, "再計算:無効化で次の優先へ", s1.Id, 230, "Campaign");
		await CheckBalance(session, "最終", k1, 240, k2, 30);
		before = (await Events(session)).Count;
		await Recalc(session, "再実行");
		session.CheckEqual("再計算:再実行で二重計上しない", before, (await Events(session)).Count);
	}

	static Tran01Tenuri Slip(string memo, long idCustomer, MasterTokui shop, params (long Id_Shohin, int Kubun, long Kingaku)[] lines) => new() {
		DenDay = "20261005", Id_Customer = idCustomer, Id_Tenpo = shop.Id, Id_Soko = shop.Id, Kubun = 10, Memo = memo,
		Jmeisai = [.. lines.Select(x => new Tran99Meisai { Id_Shohin = x.Id_Shohin, Kubun = x.Kubun, Su = 1, Tanka = (int)x.Kingaku, Kingaku = x.Kingaku })],
	};

	static async Task<T> One<T>(VmSession session, string code) where T : BaseDbClass =>
		(await session.QueryAsync<T>("WHERE Code=@0", code)).Single();

	static Task<List<TranPointEvent>> Events(VmSession session) => session.QueryAsync<TranPointEvent>("ORDER BY Id");

	static async Task<long> ActiveTotal(VmSession session, long idTenuri) =>
		(await Events(session)).Where(x => x.Id_Tenuri == idTenuri).Sum(x => x.PointDelta);

	static async Task CheckGrant(VmSession session, string name, long idTenuri, long expected, string rule) {
		var events = (await Events(session)).Where(x => x.Id_Tenuri == idTenuri).ToList();
		var cancelled = events.Where(x => x.EventType == (int)EnumPointEventType.Cancel).Select(x => x.Id_OriginalEvent).ToHashSet();
		var active = events.Where(x => x.EventType == (int)EnumPointEventType.Grant && !cancelled.Contains(x.Id)).ToList();
		session.Check(name, active.Count == 1 && active[0].PointDelta == expected && events.Sum(x => x.PointDelta) == expected
			&& active[0].Jcalc.Contains($"\"Rule\":\"{rule}\""), new { expected, rule, events = events.Select(x => new { x.EventType, x.PointDelta, x.Jcalc }) });
	}

	static async Task CheckBalance(VmSession session, string name, long k1, int p1, long k2, int p2) {
		var summary = await session.QueryAsync<SummaryPoint>("ORDER BY Id_Customer");
		var accounts = await session.QueryAsync<MasterEndCustomerAccount>("ORDER BY Id_Customer");
		var events = await Events(session);
		int Sum(long id) => summary.SingleOrDefault(x => x.Id_Customer == id)?.Point ?? 0;
		int Acc(long id) => accounts.Single(x => x.Id_Customer == id).Point;
		session.Check(name + ":残高", Sum(k1) == p1 && Sum(k2) == p2 && Acc(k1) == 100 + p1 && Acc(k2) == 100 + p2
			&& events.Where(x => x.Id_Customer == k1).Sum(x => x.PointDelta) == p1,
			new { expected = new { p1, p2 }, summary = new { k1 = Sum(k1), k2 = Sum(k2) }, account = new { k1 = Acc(k1), k2 = Acc(k2) } });
	}

	static async Task Recalc(VmSession session, string name) {
		var driver = session.OpenView<PointSummaryView, PointSummaryViewModel>();
		session.SetDialogResponder(r => r.Button == MessageBoxButton.YesNo ? MessageBoxResult.Yes : MessageBoxResult.OK);
		try {
			driver.Input("年月2026/10", vm => { vm.YearMonthFrom = "2026/10"; vm.YearMonthTo = "2026/10"; });
			await driver.RunAsync("再計算:" + name, vm => vm.ExecuteCommand);
			session.Check("再計算完了:" + name, driver.Vm.StatusMessage.Contains("完了") && driver.Vm.ProgressValue == 100, driver.Vm.StatusMessage);
		}
		finally {
			session.SetDialogResponder(null);
			driver.View.Close();
		}
	}
}
