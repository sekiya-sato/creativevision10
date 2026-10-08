using System;
using System.Linq;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>店舗売上のポイント付与計算・台帳同期・再計算を実SQLiteで検証する。</summary>
[TestClass]
public sealed class PointCalcDbTests {
	private ExDatabaseSqlite _db = null!;
	private SqliteConnection _connection = null!;
	private SqliteConnection _anchor = null!;
	private PointCalcDb Calc => new(_db);

	[TestInitialize]
	public void Initialize() {
		var connectionString = $"Data Source=PointCalc-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
		_anchor = new SqliteConnection(connectionString);
		_anchor.Open();
		_connection = new SqliteConnection(connectionString);
		_connection.Open();
		_db = new ExDatabaseSqlite(_connection) { KeepConnectionAlive = true };
		foreach (var type in new[] { typeof(MasterPointBase), typeof(MasterPointRank), typeof(TranPointEvent), typeof(SummaryPoint), typeof(Tran01Tenuri), typeof(MasterEndCustomerAccount),
			typeof(MasterPointCampaign), typeof(MasterPointCampaignShop), typeof(MasterPointCampaignShohin),
			typeof(MasterPointBonus), typeof(MasterEndCustomer), typeof(MasterSysman) }) _db.CreateTable(type, true, false);
	}

	[TestCleanup]
	public void Cleanup() { _db.Close(); _connection.Dispose(); _anchor.Dispose(); }

	private MasterPointBase SeedBase(int calcUnit = 0, int rounding = (int)EnumRounding.Floor, int taxBasis = 0) {
		var row = new MasterPointBase { Code = "B", Name = "基本", DayFrom = "20260101", DayTo = "20261231", IsEnabled = 1, PointUnitPrice = 100, PointAmountProper = 1, PointAmountSale = 2, CalcUnit = calcUnit, Rounding = rounding, TaxBasis = taxBasis };
		_db.Insert(row);
		return row;
	}
	private void SeedAccount(long idCustomer, string rank, int point) => _db.Insert(new MasterEndCustomerAccount { Id_Customer = idCustomer, PointRank = rank, Point = point });
	// P 250円(税25) + S 150円(税15)
	private Tran01Tenuri SeedSlip(long idCustomer = 7, int kubun = 10, string denDay = "20261005") {
		var slip = new Tran01Tenuri { DenDay = denDay, Id_Customer = idCustomer, Id_Tenpo = 3, Kubun = kubun, Vdc = 1, Vdu = 1,
			Jmeisai = [new() { Kubun = 0, Kingaku = 250, Tax = 25 }, new() { Kubun = 1, Kingaku = 150, Tax = 15 }] };
		_db.Insert(slip);
		return slip;
	}
	private MasterPointCampaign SeedCampaign(long idBase, EnumPointCampaignPriority priority, long unitPrice, long amount, long[]? shops = null, long[]? shohins = null, int rankKubun = 0, string code = "C") {
		var row = new MasterPointCampaign { Code = code + (int)priority, Name = "キャンペーン", Id_PointBase = idBase, DayFrom = "20261001", DayTo = "20261031", IsEnabled = 1,
			EnPriorityType = priority, PointUnitPrice = unitPrice, PointAmountProper = amount, PointAmountSale = amount, RankKubun = rankKubun };
		_db.Insert(row);
		foreach (var id in shops ?? []) _db.Insert(new MasterPointCampaignShop { Id_PointCampaign = row.Id, Id_Tenpo = id });
		foreach (var id in shohins ?? []) _db.Insert(new MasterPointCampaignShohin { Id_PointCampaign = row.Id, Id_Shohin = id });
		return row;
	}
	// プロパー明細のみ (商品Id, 金額)
	private Tran01Tenuri SlipOf(long idTenpo, params (long Id_Shohin, long Kingaku)[] lines) =>
		new() { DenDay = "20261005", Id_Customer = 7, Id_Tenpo = idTenpo, Kubun = 10, Vdc = 1, Vdu = 1,
			Jmeisai = [.. lines.Select(x => new Tran99Meisai { Kubun = 0, Id_Shohin = x.Id_Shohin, Kingaku = x.Kingaku })] };
	private TranPointEvent[] Events() => [.. _db.Fetch<TranPointEvent>("SELECT * FROM TranPointEvent ORDER BY Id")];
	private int Balance(long idCustomer) => _db.Fetch<SummaryPoint>("SELECT * FROM SummaryPoint WHERE Id_Customer=@0", idCustomer).Single().Point;
	private int AccountPoint(long idCustomer) => _db.Fetch<MasterEndCustomerAccount>("SELECT * FROM MasterEndCustomerAccount WHERE Id_Customer=@0", idCustomer).Single().Point;

	[TestMethod]
	[DataRow(0, (int)EnumRounding.Floor, 0, 5L)]   // 伝票: (250*1+150*2)/100=5.5 → 切捨5
	[DataRow(0, (int)EnumRounding.Ceiling, 0, 6L)] // 伝票: 5.5 → 切上6
	[DataRow(1, (int)EnumRounding.Round, 0, 6L)]   // 明細: 2.5→3 + 3.0→3
	[DataRow(1, (int)EnumRounding.Floor, 0, 5L)]   // 明細: 2 + 3
	[DataRow(0, (int)EnumRounding.Floor, 1, 6L)]   // 税込: (275+165*2)/100=6.05 → 6
	public void Calc_AppliesUnitRoundingAndTaxBasis(int calcUnit, int rounding, int taxBasis, long expected) {
		SeedBase(calcUnit, rounding, taxBasis);
		Assert.AreEqual(expected, Calc.Calc(SeedSlip())!.PointDelta);
	}

	[TestMethod]
	public void Calc_RankKubunReplacesBaseAndReturnIsNegative() {
		var parent = SeedBase();
		_db.Insert(new MasterPointRank { Id_PointBase = parent.Id, Kubun = 2, Name = "ゴールド", PointUnitPrice = 50, PointAmountProper = 1, PointAmountSale = 2 });
		SeedAccount(7, "2", 0);
		var grant = Calc.Calc(SeedSlip())!;
		Assert.AreEqual(11L, grant.PointDelta); // (250+300)/50
		Assert.IsGreaterThan(0L, grant.Id_PointRank);
		Assert.AreEqual(-11L, Calc.Calc(SeedSlip(kubun: 20))!.PointDelta);
	}

	[TestMethod]
	public void Calc_ExcludedSlipsHaveNoGrant() {
		Assert.IsNull(Calc.Calc(SeedSlip()), "条件なし");
		SeedBase();
		Assert.AreEqual(5L, Calc.Calc(SeedSlip(kubun: 14))!.PointDelta, "社販は対象");
		Assert.AreEqual(-5L, Calc.Calc(SeedSlip(kubun: 24))!.PointDelta, "社販返品は負の付与");
		Assert.IsNull(Calc.Calc(SeedSlip(kubun: 99)), "消費税");
		Assert.IsNull(Calc.Calc(SeedSlip(idCustomer: 0)), "顧客なし");
		Assert.IsNull(Calc.Calc(SeedSlip(denDay: "20270101")), "期間外");
	}

	[TestMethod]
	public void SyncTenuri_IsIdempotentAndCorrectsUpdateAndDelete() {
		SeedBase();
		SeedAccount(7, string.Empty, 100);
		SeedAccount(8, string.Empty, 0);
		var slip = SeedSlip();
		Assert.AreEqual(1, Calc.SyncTenuri(slip.Id, slip));
		Assert.AreEqual(0, Calc.SyncTenuri(slip.Id, slip), "同じ内容の再実行で二重計上しない");
		Assert.AreEqual(5, Balance(7));
		Assert.AreEqual(105, AccountPoint(7), "移行ポイントへ加算する");

		slip.Jmeisai![0].Kingaku = 450; // (450+300)/100=7
		slip.Vdu = 2;
		Assert.AreEqual(2, Calc.SyncTenuri(slip.Id, slip));
		var events = Events();
		Assert.AreEqual((int)EnumPointEventType.Cancel, events[1].EventType);
		Assert.AreEqual(events[0].Id, events[1].Id_OriginalEvent);
		Assert.AreEqual(-5L, events[1].PointDelta);
		Assert.AreEqual(7, Balance(7));

		slip.Id_Customer = 8;
		Assert.AreEqual(2, Calc.SyncTenuri(slip.Id, slip));
		Assert.AreEqual(0, Balance(7));
		Assert.AreEqual(100, AccountPoint(7));
		Assert.AreEqual(7, Balance(8));

		Assert.AreEqual(1, Calc.SyncTenuri(slip.Id, null));
		Assert.AreEqual(0, Balance(8));
		Assert.AreEqual(0, AccountPoint(8));
		Assert.AreEqual(Events().Length, Events().Select(x => x.EventKey).Distinct().Count());
	}

	[TestMethod]
	public void Recalc_GrantsMissingCorrectsMasterChangeAndIsRepeatable() {
		var parent = SeedBase();
		SeedSlip();
		SeedSlip(kubun: 20);
		SeedSlip(denDay: "20260905");
		Assert.AreEqual(2, Calc.Recalc("202610", "202610"), "対象月の2伝票だけ");
		Assert.AreEqual(0, Calc.Recalc("202610", "202610"));
		Assert.AreEqual(0, Balance(7), "売上5 + 返品-5");

		parent.PointAmountProper = 3; // (750+300)/100=10
		_db.Update(parent);
		Assert.AreEqual(4, Calc.Recalc("202610", "202610"), "2伝票ぶん取消+付与");
		Assert.AreEqual(1, Calc.Recalc("202609", "202609"));
		Assert.AreEqual(10, Balance(7));

		parent.IsEnabled = 0;
		_db.Update(parent);
		Assert.AreEqual(3, Calc.Recalc("202609", "202610"), "無効化で全付与を取消");
		Assert.AreEqual(0, Balance(7));
		Assert.AreEqual(0L, Events().Sum(x => x.PointDelta));
	}

	[TestMethod]
	public void Calc_CampaignAppliesFirstMatchPerLineByPriority() {
		var parent = SeedBase(calcUnit: 1);
		SeedCampaign(parent.Id, EnumPointCampaignPriority.AllShops, 10, 1);
		SeedCampaign(parent.Id, EnumPointCampaignPriority.Shop, 10, 2, shops: [3]);
		SeedCampaign(parent.Id, EnumPointCampaignPriority.ShohinAllShops, 10, 3, shohins: [11, 12]);
		SeedCampaign(parent.Id, EnumPointCampaignPriority.ShohinShop, 10, 4, shops: [3], shohins: [11]);
		SeedCampaign(parent.Id, EnumPointCampaignPriority.ShohinShop, 10, 9, shops: [99], shohins: [13], code: "X");
		SeedCampaign(parent.Id, EnumPointCampaignPriority.ShohinAllShops, 10, 9, code: "Y"); // 対象未設定は適用しない
		var lines = new (long, long)[] { (11, 1000), (12, 1000), (13, 1000), (14, 1000) };
		var grant = Calc.Calc(SlipOf(3, lines))!;
		Assert.AreEqual(1100L, grant.PointDelta, "商品店別400+商品全店300+店別200+店別200");
		StringAssert.Contains(grant.Jcalc, "\"Rule\":\"Campaign\"");
		Assert.AreEqual(800L, Calc.Calc(SlipOf(5, lines))!.PointDelta, "他店: 商品全店300+300+全店100+100");
	}

	[TestMethod]
	public void Calc_CampaignRankBaseAndEnabledConditions() {
		var parent = SeedBase();
		_db.Insert(new MasterPointRank { Id_PointBase = parent.Id, Kubun = 2, Name = "ゴールド", PointUnitPrice = 50, PointAmountProper = 1, PointAmountSale = 2 });
		var other = new MasterPointBase { Code = "Z", Name = "別", DayFrom = "20260101", DayTo = "20261231", IsEnabled = 1, PointUnitPrice = 100, PointAmountProper = 1, PointAmountSale = 1 };
		_db.Insert(other);
		SeedCampaign(other.Id, EnumPointCampaignPriority.AllShops, 1, 1, code: "O"); // 適用ベース版(コード順先頭B)と異なる
		var disabled = SeedCampaign(parent.Id, EnumPointCampaignPriority.AllShops, 1, 1, code: "D");
		disabled.IsEnabled = 0;
		_db.Update(disabled);
		SeedCampaign(parent.Id, EnumPointCampaignPriority.AllShops, 100, 5, rankKubun: 2);
		SeedAccount(7, "2", 0);
		SeedAccount(8, "1", 0);
		Assert.AreEqual(20L, Calc.Calc(SeedSlip())!.PointDelta, "ランク2はキャンペーンでランク条件を置換: 400*5/100");
		Assert.AreEqual(-20L, Calc.Calc(SeedSlip(kubun: 20))!.PointDelta, "返品は負");
		Assert.AreEqual(5L, Calc.Calc(SeedSlip(idCustomer: 8))!.PointDelta, "ランク不一致はベース");
		Assert.AreEqual(11L, Calc.Calc(SeedSlip(denDay: "20261101"))!.PointDelta, "期間外はランク条件: (250+300)/50");
	}

	[TestMethod]
	public void Calc_SlipUnitRoundsOnceAcrossUnitPrices() {
		var parent = SeedBase();
		parent.PointUnitPrice = 3;
		_db.Update(parent);
		SeedCampaign(parent.Id, EnumPointCampaignPriority.ShohinAllShops, 6, 1, shohins: [21]);
		SeedCampaign(parent.Id, EnumPointCampaignPriority.ShohinShop, 9, 1, shops: [3], shohins: [22]);
		// 1/3 + 2/6 + 3/9 = 1。条件ごとの丸めなら0、割り算の誤差でも0になる
		Assert.AreEqual(1L, Calc.Calc(SlipOf(3, (0, 1), (21, 2), (22, 3)))!.PointDelta);
	}

	[TestMethod]
	public void SyncAndRecalc_ReflectCampaign() {
		var parent = SeedBase();
		SeedAccount(7, string.Empty, 0);
		var slip = SeedSlip();
		Assert.AreEqual(1, Calc.SyncTenuri(slip.Id, slip));
		Assert.AreEqual(5, Balance(7));

		var campaign = SeedCampaign(parent.Id, EnumPointCampaignPriority.Shop, 100, 2, shops: [3]);
		Assert.AreEqual(2, Calc.SyncTenuri(slip.Id, slip), "自動更新(保存時同期)で取消+キャンペーン付与");
		Assert.AreEqual(8, Balance(7)); // 400*2/100
		Assert.AreEqual(0, Calc.Recalc("202610", "202610"));

		campaign.PointAmountProper = 3;
		_db.Update(campaign);
		Assert.AreEqual(2, Calc.Recalc("202610", "202610"), "手動更新(再計算)でマスタ変更を反映");
		Assert.AreEqual(10, Balance(7)); // (250*3+150*2)/100
		Assert.AreEqual(10, AccountPoint(7));

		_db.Execute("DELETE FROM MasterPointCampaignShop");
		Assert.AreEqual(2, Calc.Recalc("202610", "202610"), "対象解除でベースへ戻る");
		Assert.AreEqual(5, Balance(7));
		Assert.AreEqual(0, Calc.Recalc("202610", "202610"));
	}

	private MasterPointBonus SeedBonus(long idBase, EnumPointBonusTrigger trigger, long amount, EnumPointLimitPeriod limit = EnumPointLimitPeriod.Slip, int limitCount = 1, long minimum = 0, string code = "BN", int isAllRanks = 1, int rankKubun = 0) {
		var row = new MasterPointBonus { Code = code, Name = "ボーナス", Version = 1, Id_PointBase = idBase, DayFrom = "20260101", DayTo = "20261231", IsEnabled = 1,
			TriggerType = (int)trigger, PointAmount = amount, MinimumKingaku = minimum, IsAllRanks = isAllRanks, RankKubun = rankKubun, LimitPeriodType = (int)limit, LimitCount = limitCount };
		_db.Insert(row);
		return row;
	}
	private long SlipGrantPoint(long id) => _db.Fetch<Tran01Tenuri>("SELECT * FROM Tran01Tenuri WHERE Id=@0", id).Single().GrantPoint;
	private TranPointEvent[] Active(long idTenuri) {
		var events = Events().Where(x => x.Id_Tenuri == idTenuri).ToArray();
		var cancelled = events.Where(x => x.EventType == (int)EnumPointEventType.Cancel).Select(x => x.Id_OriginalEvent).ToHashSet();
		return [.. events.Where(x => x.EventType != (int)EnumPointEventType.Cancel && !cancelled.Contains(x.Id))];
	}
	private Tran01Tenuri Slip(string denDay, int kubun = 10, long usePoint = 0, long idCustomer = 7) {
		var slip = SeedSlip(idCustomer, kubun, denDay);
		if (usePoint != 0) { slip.UsePoint = usePoint; _db.Update(slip); }
		return slip;
	}

	[TestMethod]
	public void Use_RecordsDeductsUpdatesGrantPointAndRejectsShortage() {
		var parent = SeedBase();
		parent.DeductPointUse = 1;
		_db.Update(parent);
		SeedAccount(7, string.Empty, 300);
		_db.Insert(new TranPointEvent { EventKey = "OPEN:7", DenDay = "20260101", Id_Customer = 7, EventType = (int)EnumPointEventType.OpeningBalance, PointDelta = 300 });

		var slip = Slip("20261005", usePoint: 200);
		Assert.AreEqual(2, Calc.SyncTenuri(slip.Id, slip));
		var active = Active(slip.Id);
		Assert.AreEqual(2L, active.Single(x => x.EventType == (int)EnumPointEventType.Grant).PointDelta, "(250+300)×(400-200)/400/100=2.75→2");
		Assert.AreEqual(-200L, active.Single(x => x.EventType == (int)EnumPointEventType.Use).PointDelta);
		Assert.AreEqual(2L, SlipGrantPoint(slip.Id));
		Assert.AreEqual(102, Balance(7));
		Assert.AreEqual(102, AccountPoint(7));
		Assert.AreEqual(0, Calc.SyncTenuri(slip.Id, slip), "再実行で追記しない");

		slip.UsePoint = 100; // 使用の減少は残高に関係なく許可。(550×300/400)/100=4.125→4
		slip.Vdu = 2;
		Assert.AreEqual(4, Calc.SyncTenuri(slip.Id, slip));
		Assert.AreEqual(4L, SlipGrantPoint(slip.Id));
		Assert.AreEqual(204, Balance(7));

		var over = Slip("20261006", usePoint: 300); // 204 + 2(付与) - 300 < 0
		_db.BeginTransaction();
		var ex = Assert.ThrowsExactly<ArgumentException>(() => Calc.SyncTenuri(over.Id, over));
		_db.AbortTransaction();
		StringAssert.Contains(ex.Message, "ポイント残高が不足");
		Assert.AreEqual(204, Balance(7), "拒否時は台帳・残高を戻す");

		var ret = Slip("20261007", kubun: 20, usePoint: 100); // 返品は使用を戻す。付与は (550×300/400)/100=4 の負
		Assert.AreEqual(2, Calc.SyncTenuri(ret.Id, ret));
		Assert.AreEqual(100L, Active(ret.Id).Single(x => x.EventType == (int)EnumPointEventType.Use).PointDelta);
		Assert.AreEqual(-4L, SlipGrantPoint(ret.Id));
		Assert.AreEqual(300, Balance(7));

		Assert.AreEqual(2, Calc.SyncTenuri(slip.Id, null), "削除で付与・使用を取消");
		Assert.AreEqual(300 - 4 + 100, Balance(7));
		var bad = Slip("20261008", kubun: 99, usePoint: 1);
		Assert.ThrowsExactly<ArgumentException>(() => Calc.SyncTenuri(bad.Id, bad), "対象外区分の使用");
	}

	[TestMethod]
	public void Use_DetailUnitAndNoBaseStillRecordsUse() {
		var parent = SeedBase(calcUnit: 1);
		parent.DeductPointUse = 1;
		parent.DayTo = "20261031";
		_db.Update(parent);
		SeedAccount(7, string.Empty, 0);
		_db.Insert(new TranPointEvent { EventKey = "OPEN:7", DenDay = "20260101", Id_Customer = 7, EventType = (int)EnumPointEventType.OpeningBalance, PointDelta = 1000 });
		var slip = Slip("20261005", usePoint: 100);
		Assert.AreEqual(3L, Calc.Calc(slip)!.PointDelta, "明細: 250×3/4/100=1.875→1 + 150×2×3/4/100=2.25→2");
		var noBase = Slip("20261105", usePoint: 100);
		Assert.AreEqual(1, Calc.SyncTenuri(noBase.Id, noBase), "ベース期間外でも使用は記録");
		Assert.AreEqual(-100L, Active(noBase.Id).Single().PointDelta);
		Assert.AreEqual(900, Balance(7));
	}

	[TestMethod]
	public void Bonus_TriggersMinimumRankAndReturn() {
		var parent = SeedBase();
		_db.Insert(new MasterEndCustomer { Code = "K7", Name = "誕生10月", BirthNoyear = "1015" });
		_db.Insert(new MasterEndCustomer { Code = "K8", Name = "誕生1月", Birthday = "19900105" });
		_db.Execute("UPDATE MasterEndCustomer SET Id=CASE Code WHEN 'K7' THEN 7 ELSE 8 END");
		SeedAccount(7, "2", 0);
		SeedAccount(8, string.Empty, 0);
		SeedBonus(parent.Id, EnumPointBonusTrigger.Purchase, 10, minimum: 400, code: "P");
		SeedBonus(parent.Id, EnumPointBonusTrigger.Purchase, 7, minimum: 401, code: "PMIN"); // 対象額400で不足
		SeedBonus(parent.Id, EnumPointBonusTrigger.Purchase, 3, code: "PRANK", isAllRanks: 0, rankKubun: 2);
		SeedBonus(parent.Id, EnumPointBonusTrigger.BirthdayMonth, 20, code: "BD");
		SeedBonus(parent.Id, EnumPointBonusTrigger.FirstPurchase, 50, limit: EnumPointLimitPeriod.Lifetime, code: "FIRST");

		var first = Slip("20261005");
		Calc.SyncTenuri(first.Id, first);
		Assert.AreEqual(5 + 10 + 3 + 20 + 50, SlipGrantPoint(first.Id), "基本+期間内+ランク限定+誕生月+初回");
		Assert.AreEqual(4, Active(first.Id).Count(x => x.Id_PointBonus > 0));
		Assert.AreEqual(88, Balance(7));

		var second = Slip("20261106");
		Calc.SyncTenuri(second.Id, second);
		Assert.AreEqual(5 + 10 + 3, SlipGrantPoint(second.Id), "誕生月外・2回目は初回なし");

		var other = Slip("20261005", idCustomer: 8);
		Calc.SyncTenuri(other.Id, other);
		Assert.AreEqual(5 + 10 + 50, SlipGrantPoint(other.Id), "ランクなし・誕生1月・顧客8の初回");

		var ret = Slip("20261007", kubun: 20);
		Calc.SyncTenuri(ret.Id, ret);
		Assert.AreEqual(-(5 + 10 + 3 + 20), SlipGrantPoint(ret.Id), "返品は初回購入以外を負で記録");

		first.Jmeisai![0].Kingaku = 240; // 対象額390 < 最低額400
		first.Vdu = 2;
		Calc.SyncTenuri(first.Id, first);
		Assert.IsFalse(Active(first.Id).Any(x => x.Id_PointBonus > 0 && Events().Any(b => b.Id == x.Id && b.Jcalc.Contains("\"Code\":\"P\""))), "訂正で条件外は取消");
		Assert.AreEqual(Events().Sum(x => x.Id_Customer == 7 ? x.PointDelta : 0), Balance(7));
	}

	[TestMethod]
	public void Bonus_LimitCountByPeriodFiscalYearAndRecalc() {
		var parent = SeedBase();
		_db.Insert(new MasterSysman { FiscalStartDate = "20250401" });
		SeedAccount(7, string.Empty, 0);
		SeedBonus(parent.Id, EnumPointBonusTrigger.Purchase, 10, limit: EnumPointLimitPeriod.FiscalYear, limitCount: 1, code: "FY");
		SeedBonus(parent.Id, EnumPointBonusTrigger.Purchase, 1, limit: EnumPointLimitPeriod.Period, limitCount: 2, code: "PD");
		var a = Slip("20260331"); var b = Slip("20260401"); var c = Slip("20260402");
		foreach (var s in new[] { a, b, c }) Calc.SyncTenuri(s.Id, s);
		Assert.AreEqual(5 + 10 + 1, SlipGrantPoint(a.Id), "2025年度の1回目");
		Assert.AreEqual(5 + 10 + 1, SlipGrantPoint(b.Id), "2026年度の1回目");
		Assert.AreEqual(5, SlipGrantPoint(c.Id), "年度内2回目・期間内3回目は上限");

		_db.Execute("DELETE FROM Tran01Tenuri WHERE Id=@0", b.Id);
		Calc.SyncTenuri(b.Id, null); // 削除で上限が空く
		Assert.AreEqual(2, Calc.Recalc("202604", "202604"), "再計算で後の伝票にボーナスを付与");
		Assert.AreEqual(5 + 10 + 1, SlipGrantPoint(c.Id));
		Assert.AreEqual(0, Calc.Recalc("202603", "202604"));
		Assert.AreEqual(Events().Sum(x => x.PointDelta), Balance(7));
	}

	private void SeedLedger(long idCustomer, long point) =>
		_db.Insert(new TranPointEvent { EventKey = $"OPEN:{idCustomer}", DenDay = "20250101", Id_Customer = idCustomer, EventType = (int)EnumPointEventType.OpeningBalance, PointDelta = point });

	[TestMethod]
	public void Expire_ElapsedWithdrawnIdempotentAndBalance() {
		var parent = SeedBase();
		parent.ExpireMonths = 12;
		_db.Update(parent);
		// 7: 最終購入 2025/10/01 → 基準日 2026/10/07 の12か月前(2025/10/07)以前で失効
		SeedAccount(7, string.Empty, 100); SeedLedger(7, 100); SeedSlip(7, denDay: "20251001");
		// 8: 最近購入 → 対象外
		SeedAccount(8, string.Empty, 50); SeedLedger(8, 50); SeedSlip(8, denDay: "20261001");
		// 9: 退会(基準日以前) → 最近購入でも失効
		_db.Insert(new MasterEndCustomerAccount { Id_Customer = 9, Point = 30, IsWithdrawalFlag = 1, WithdrawnDate = "20261001" }); SeedLedger(9, 30); SeedSlip(9, denDay: "20261002");
		// 10: 負残高 → 対象外
		SeedAccount(10, string.Empty, -5); SeedLedger(10, -5); SeedSlip(10, denDay: "20240101");
		// 11: 売上なし・移行の最終来店日が古い → 失効
		_db.Insert(new MasterEndCustomerAccount { Id_Customer = 11, Point = 70, LastVisitDate = "20250901" }); SeedLedger(11, 70);
		// 12: 最終来店日は古いが売上が新しい → 対象外。13: 退会日が基準日より後 → 対象外
		_db.Insert(new MasterEndCustomerAccount { Id_Customer = 12, Point = 20, LastVisitDate = "20240101" }); SeedLedger(12, 20); SeedSlip(12, denDay: "20260601");
		_db.Insert(new MasterEndCustomerAccount { Id_Customer = 13, Point = 10, IsWithdrawalFlag = 1, WithdrawnDate = "20261010", LastVisitDate = "20261001" }); SeedLedger(13, 10);

		Assert.AreEqual(3, new PointExpireDb(_db).Expire("20261007"));
		var expired = Events().Where(x => x.EventType == (int)EnumPointEventType.Expire).ToDictionary(x => x.Id_Customer);
		CollectionAssert.AreEquivalent(new long[] { 7, 9, 11 }, expired.Keys.ToArray());
		Assert.AreEqual(-100L, expired[7].PointDelta);
		Assert.AreEqual("退会の為失効", expired[9].Memo);
		Assert.AreEqual("20261007", expired[11].DenDay);
		Assert.AreEqual(0, Balance(7)); Assert.AreEqual(0, AccountPoint(7));
		Assert.AreEqual(0, Balance(11)); Assert.AreEqual(0, AccountPoint(11));
		Assert.AreEqual(50, AccountPoint(8));

		Assert.AreEqual(0, new PointExpireDb(_db).Expire("20261007"), "同じ基準日の再実行で二重に失効しない");
		_db.Insert(new TranPointEvent { EventKey = "ADJ:7", DenDay = "20261007", Id_Customer = 7, EventType = (int)EnumPointEventType.Adjustment, PointDelta = 5 });
		Assert.AreEqual(0, new PointExpireDb(_db).Expire("20261007"), "同じ基準日はEventKeyで1回だけ");
		Assert.AreEqual(1, new PointExpireDb(_db).Expire("20261008"), "翌日は残った残高を失効");

		parent.ExpireMonths = 0;
		_db.Update(parent);
		_db.Insert(new TranPointEvent { EventKey = "ADJ:8", DenDay = "20261008", Id_Customer = 8, EventType = (int)EnumPointEventType.Adjustment, PointDelta = 1 });
		Assert.AreEqual(1, new PointExpireDb(_db).Expire("20281231"), "経過失効なし(ベース期間外)。退会日を過ぎた13だけ失効");
		Assert.AreEqual(51L, Events().Where(x => x.Id_Customer == 8).Sum(x => x.PointDelta));
		Assert.ThrowsExactly<ArgumentException>(() => new PointExpireDb(_db).Expire("20261332"));
	}

	[TestMethod]
	public void Recalc_AfterMovingSlipToAnotherMonthDoesNotRegrantOrCollide() {
		SeedBase();
		SeedAccount(7, string.Empty, 0);
		var slip = SeedSlip();
		Calc.SyncTenuri(slip.Id, slip);
		foreach (var (day, vdu) in new[] { ("20261105", 2L), ("20261205", 3L) }) {
			slip.DenDay = day;
			slip.Vdu = vdu;
			_db.Update(slip);
			Assert.AreEqual(2, Calc.SyncTenuri(slip.Id, slip), "日付変更で取消+付与");
			Assert.AreEqual(0, Calc.Recalc("202610", "202612"), "旧月を含む再計算で再付与・EventKey衝突しない");
			Assert.AreEqual(0, Calc.Recalc("202610", "202610"));
			Assert.AreEqual(5, Balance(7));
		}
	}

	[TestMethod]
	public void Expire_UsesBalanceAsOfBaseDay() {
		var parent = SeedBase();
		parent.ExpireMonths = 12;
		_db.Update(parent);
		SeedAccount(7, string.Empty, 130); SeedLedger(7, 100); SeedSlip(7, denDay: "20251001");
		_db.Insert(new TranPointEvent { EventKey = "ADJ:7", DenDay = "20261020", Id_Customer = 7, EventType = (int)EnumPointEventType.Adjustment, PointDelta = 30 });
		Assert.AreEqual(1, new PointExpireDb(_db).Expire("20261007"));
		Assert.AreEqual(-100L, Events().Single(x => x.EventType == (int)EnumPointEventType.Expire).PointDelta, "基準日より後の付与30は失効しない");
		Assert.AreEqual(30, Balance(7));
	}

	[TestMethod]
	public void Use_DeleteOfReturnIsAllowedAndUseCannotExceedTotal() {
		SeedBase();
		SeedAccount(7, string.Empty, 0);
		var ret = Slip("20261005", kubun: 20, usePoint: 100); // 使用戻し+100、付与-5
		Calc.SyncTenuri(ret.Id, ret);
		Assert.AreEqual(95, Balance(7));
		_db.Insert(new TranPointEvent { EventKey = "ADJ:7", DenDay = "20261006", Id_Customer = 7, EventType = (int)EnumPointEventType.Adjustment, PointDelta = -95 });
		Assert.AreEqual(2, Calc.SyncTenuri(ret.Id, null), "削除は残高が負になっても許可");
		Assert.AreEqual(-95, Balance(7));
		var over = Slip("20261007", usePoint: 441); // 税込合計440
		var ex = Assert.ThrowsExactly<ArgumentException>(() => Calc.SyncTenuri(over.Id, over));
		StringAssert.Contains(ex.Message, "税込合計以下");
	}
}
