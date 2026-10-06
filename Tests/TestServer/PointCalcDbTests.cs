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
		foreach (var type in new[] { typeof(MasterPointBase), typeof(MasterPointRank), typeof(TranPointEvent), typeof(SummaryPoint), typeof(Tran01Tenuri), typeof(MasterEndCustomerAccount) }) _db.CreateTable(type, true, false);
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
}
