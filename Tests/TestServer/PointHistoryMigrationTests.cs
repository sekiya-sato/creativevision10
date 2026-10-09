using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Tests.CvServer;

/// <summary>旧CV履歴の取り込みと初回ポイント再構築を実SQLiteで検証する。</summary>
[TestClass]
public sealed class PointHistoryMigrationTests {
	private ExDatabaseSqlite _db = null!;
	private SqliteConnection _connection = null!;
	private SqliteConnection _anchor = null!;
	private ExDatabaseSqlite _source = null!;
	private SqliteConnection _sourceConnection = null!;
	private SqliteConnection _sourceAnchor = null!;
	private ConvertDb Convert => new(_source, _db);

	[TestInitialize]
	public void Initialize() {
		var sourceString = $"Data Source=PointMigrationSource-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
		_sourceAnchor = new SqliteConnection(sourceString); _sourceAnchor.Open();
		_sourceConnection = new SqliteConnection(sourceString); _sourceConnection.Open();
		_source = new ExDatabaseSqlite(_sourceConnection) { KeepConnectionAlive = true };
		_source.Execute("CREATE TABLE HC$TRAN_POINT_RIREKI (SEQ_NO INTEGER PRIMARY KEY, VDATE_CREATE NUMERIC DEFAULT 0, VDATE_UPDATE NUMERIC DEFAULT 0, 顧客CD TEXT, ランク TEXT DEFAULT '1', ポイント計上日 TEXT, 店舗CD TEXT DEFAULT 'S', レジNO INTEGER DEFAULT 1, レシートNO INTEGER DEFAULT 2, 社員CD TEXT DEFAULT 'E', 取引区分 INTEGER DEFAULT 10, 発生区分 INTEGER DEFAULT 0, 付与ポイント数 INTEGER DEFAULT 0, 使用ポイント数 INTEGER DEFAULT 0, 失効ポイント数 INTEGER DEFAULT 0, 備考 TEXT DEFAULT '')");
		_source.Execute("CREATE TABLE HC$POINT_REAL (顧客CD TEXT PRIMARY KEY, REALポイント INTEGER)");
		var connectionString = $"Data Source=PointMigration-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
		_anchor = new SqliteConnection(connectionString); _anchor.Open();
		_connection = new SqliteConnection(connectionString); _connection.Open();
		_db = new ExDatabaseSqlite(_connection) { KeepConnectionAlive = true };
		foreach (var type in new[] { typeof(MasterEndCustomer), typeof(MasterEndCustomerAccount), typeof(MasterTokui), typeof(MasterShain),
			typeof(TranPointEvent), typeof(SummaryPoint), typeof(Tran01Tenuri), typeof(MasterPointBase), typeof(MasterPointRank),
			typeof(MasterPointCampaign), typeof(MasterPointCampaignShop), typeof(MasterPointCampaignShohin), typeof(MasterPointBonus), typeof(MasterSysman) })
			_db.CreateTable(type, true, false);
	}

	[TestCleanup]
	public void Cleanup() { _db.Close(); _connection.Dispose(); _anchor.Dispose(); _source.Close(); _sourceConnection.Dispose(); _sourceAnchor.Dispose(); }

	private TranPointEvent[] Events() => [.. _db.Fetch<TranPointEvent>("SELECT * FROM TranPointEvent ORDER BY Id")];
	private int Balance(long id) => _db.Fetch<SummaryPoint>("SELECT * FROM SummaryPoint WHERE Id_Customer=@0", id).Single().Point;
	private int AccountPoint(long id) => _db.Fetch<MasterEndCustomerAccount>("SELECT * FROM MasterEndCustomerAccount WHERE Id_Customer=@0", id).Single().Point;

	[TestMethod]
	public void LegacySnapshot_ZeroValuesAndEscapedMemoRoundTrip() {
		var snapshot = new LegacyPointHistory { SourceSystem = "CV", SeqNo = 14, Day = "20261001", Memo = "備考\"\n改行\\", GrantPoints = -5 };
		var json = Common.SerializeObject(snapshot);
		var parsed = JObject.Parse(json);
		Assert.AreEqual(0L, (long)parsed["UsePoints"]!, "省略せず0を保存");
		Assert.AreEqual(0L, (long)parsed["ExpirePoints"]!);
		Assert.AreEqual(0, (int)parsed["TransactionType"]!);
		var restored = JsonConvert.DeserializeObject<LegacyPointHistory>(json)!;
		Assert.AreEqual("CV", restored.SourceSystem); Assert.AreEqual(14L, restored.SeqNo);
		Assert.AreEqual("20261001", restored.Day); Assert.AreEqual(-5L, restored.GrantPoints);
		Assert.AreEqual("備考\"\n改行\\", restored.Memo);
		Assert.AreEqual(0L, restored.UsePoints); Assert.AreEqual(0L, restored.ExpirePoints);
	}
	private long SeedCustomer(string code = "K", int point = 999) {
		var customer = new MasterEndCustomer { Code = code, Name = "移行顧客" }; _db.Insert(customer);
		_db.Insert(new MasterEndCustomerAccount { Id_Customer = customer.Id, Point = point, PointRank = "2", LastVisitDate = "20260901" });
		return customer.Id;
	}
	private void SeedReferences() {
		_db.Insert(new MasterTokui { Code = "S", Name = "店舗", TenType = 6 });
		_db.Insert(new MasterShain { Code = "E", Name = "社員" });
	}
	private void SeedLegacy(long seq, long grant = 0, long use = 0, long expire = 0, string day = "20260901", string code = "K", string memo = "備考") =>
		_source.Execute("INSERT INTO HC$TRAN_POINT_RIREKI (SEQ_NO,顧客CD,ポイント計上日,付与ポイント数,使用ポイント数,失効ポイント数,備考,VDATE_CREATE,VDATE_UPDATE) VALUES (@0,@1,@2,@3,@4,@5,@6,20260101010101,20260901010203)", seq, code, day, grant, use, expire, memo);
	private void SeedReal(long point, string code = "K") => _source.Execute("INSERT INTO HC$POINT_REAL (顧客CD,REALポイント) VALUES (@0,@1)", code, point);

	[TestMethod]
	public void CustomerReimport_WithInitializationCannotReplaceExistingIdsAndBalances() {
		var id = SeedCustomer(point: 123);
		Assert.ThrowsExactly<InvalidOperationException>(() => Convert.CnvMasterEndCustomer(isInit: true));
		Assert.AreEqual(id, _db.Fetch<MasterEndCustomer>("SELECT * FROM MasterEndCustomer").Single().Id);
		Assert.AreEqual(123, AccountPoint(id));
	}

	[TestMethod]
	public void Import_NormalReturnMixedAndZeroPreserveOneSourceRow() {
		var id = SeedCustomer(); SeedReferences();
		SeedLegacy(1, grant: 100); SeedLegacy(2, grant: -40, use: -10); SeedLegacy(3, grant: 80, use: 20, expire: 5); SeedLegacy(4);
		SeedReal(125);
		Assert.AreEqual(4, Convert.CnvTranPointHistory(chunkSize: 2));
		var rows = Events(); Assert.AreEqual(4, rows.Length);
		CollectionAssert.AreEqual(new long[] { 100, -30, 55, 0 }, rows.Select(x => x.PointDelta).ToArray());
		CollectionAssert.AreEqual(new[] { "LEGACY:CV:POINT:1", "LEGACY:CV:POINT:2", "LEGACY:CV:POINT:3", "LEGACY:CV:POINT:4" }, rows.Select(x => x.EventKey).ToArray());
		Assert.IsTrue(rows.All(x => x.EventType == (int)EnumPointEventType.LegacyHistory && x.Id_Tenuri == 0 && x.Id_PointBase == 0 && x.Id_Customer == id));
		var shop = _db.Fetch<MasterTokui>("SELECT * FROM MasterTokui").Single();
		var employee = _db.Fetch<MasterShain>("SELECT * FROM MasterShain").Single();
		Assert.IsTrue(rows.All(x => x.Id_Tenpo == shop.Id && x.Id_Shain == employee.Id));
		var source = Common.DeserializeObject<LegacyPointHistory>(rows[2].Jcalc)!;
		Assert.AreEqual(80L, source.GrantPoints); Assert.AreEqual(20L, source.UsePoints); Assert.AreEqual(5L, source.ExpirePoints);
		Assert.AreEqual(20260101010101m, source.CreatedAt); Assert.AreEqual(20260901010203m, source.UpdatedAt);
		Assert.AreEqual("K", source.CustomerCode); Assert.AreEqual("1", source.Rank); Assert.AreEqual("S", source.ShopCode); Assert.AreEqual("E", source.EmployeeCode);
		Assert.AreEqual(1L, source.RegisterNo); Assert.AreEqual(2L, source.ReceiptNo); Assert.AreEqual(10, source.TransactionType); Assert.AreEqual(0, source.OriginType);
		Assert.AreEqual(125, Balance(id)); Assert.AreEqual(125, AccountPoint(id));
	}

	[TestMethod]
	public void Import_RepeatedEvenWithInitKeepsNativeHistoryAndRepairsBothBalances() {
		var id = SeedCustomer(); SeedReferences(); SeedLegacy(1, 100); SeedReal(100);
		_db.Insert(new TranPointEvent { EventKey = "NATIVE", DenDay = "20261001", Id_Customer = id, EnEventType = EnumPointEventType.Adjustment, PointDelta = 7 });
		Assert.AreEqual(1, Convert.CnvTranPointHistory());
		_db.Execute("UPDATE SummaryPoint SET Point=999"); _db.Execute("UPDATE MasterEndCustomerAccount SET Point=888");
		Assert.AreEqual(0, Convert.CnvTranPointHistory(isInit: true));
		Assert.AreEqual(2, Events().Length); Assert.AreEqual(7L, Events().Single(x => x.EventKey == "NATIVE").PointDelta);
		Assert.AreEqual(107, Balance(id)); Assert.AreEqual(107, AccountPoint(id));
	}

	[TestMethod]
	public void Import_ChangedExistingKeyStopsBeforeAddingNewRow() {
		var id = SeedCustomer(); SeedReferences(); SeedLegacy(1, 100); SeedReal(100); Convert.CnvTranPointHistory();
		_source.Execute("UPDATE HC$TRAN_POINT_RIREKI SET 備考='変更' WHERE SEQ_NO=1"); SeedLegacy(2);
		Assert.ThrowsExactly<InvalidOperationException>(() => Convert.CnvTranPointHistory(chunkSize: 1));
		Assert.AreEqual(1, Events().Length); Assert.AreEqual("備考", Events()[0].Memo);
		Assert.AreEqual(100, Balance(id)); Assert.AreEqual(100, AccountPoint(id));
	}

	[TestMethod]
	[DataRow("ポイント計上日", ".")]
	[DataRow("ポイント計上日", "20260230")]
	[DataRow("顧客CD", "UNKNOWN")]
	[DataRow("店舗CD", "UNKNOWN")]
	[DataRow("社員CD", "UNKNOWN")]
	public void Inspect_UnresolvedReferenceOrInvalidDayStopsWithoutMutation(string column, string value) {
		var id = SeedCustomer(); SeedReferences(); SeedLegacy(1); SeedReal(0);
		_source.Execute($"UPDATE HC$TRAN_POINT_RIREKI SET {column}=@0", value);
		var report = Convert.InspectPointHistory();
		Assert.AreEqual(1, report.SourceRows); Assert.AreEqual(1, report.IssueCount); Assert.AreEqual(1L, report.Issues[0].SeqNo);
		Assert.ThrowsExactly<InvalidOperationException>(() => Convert.CnvTranPointHistory());
		Assert.AreEqual(0, Events().Length); Assert.AreEqual(999, AccountPoint(id));
	}

	[TestMethod]
	public void Import_UnsetOptionalReferencesResolveToZero() {
		var id = SeedCustomer(); SeedLegacy(1, 5); SeedReal(5);
		_source.Execute("UPDATE HC$TRAN_POINT_RIREKI SET 店舗CD='.',社員CD=''");
		Assert.AreEqual(1, Convert.CnvTranPointHistory());
		Assert.AreEqual(0L, Events()[0].Id_Tenpo); Assert.AreEqual(0L, Events()[0].Id_Shain); Assert.AreEqual(5, AccountPoint(id));
	}

	[TestMethod]
	public void Rebuild_SourceBalanceMismatchDoesNotInitializeTarget() {
		var id = SeedCustomer(); SeedReferences(); SeedLegacy(1, 100); SeedReal(101);
		_db.Insert(new TranPointEvent { EventKey = "OLD", DenDay = "20261001", Id_Customer = id, PointDelta = 9 });
		_db.Insert(new SummaryPoint { Id_Customer = checked((int)id), Point = 9 });
		var report = Convert.InspectPointHistory(); Assert.AreEqual(1, report.IssueCount);
		Assert.ThrowsExactly<InvalidOperationException>(() => Convert.RebuildPointHistory());
		Assert.AreEqual("OLD", Events().Single().EventKey); Assert.AreEqual(9, Balance(id)); Assert.AreEqual(999, AccountPoint(id));
	}

	[TestMethod]
	public void Import_OutOfIntBalanceStopsBeforeAnyChunk() {
		var id = SeedCustomer(); SeedReferences(); SeedLegacy(1, 100); SeedLegacy(2, int.MaxValue); SeedReal((long)int.MaxValue + 100);
		Assert.ThrowsExactly<InvalidOperationException>(() => Convert.CnvTranPointHistory(chunkSize: 1));
		Assert.AreEqual(0, Events().Length); Assert.AreEqual(999, AccountPoint(id));
	}

	private MasterPointBase SeedBase() {
		var row = new MasterPointBase { Code = "B", Name = "基本", DayFrom = "20260101", DayTo = "20261231", IsEnabled = 1, PointUnitPrice = 100, PointAmountProper = 1, PointAmountSale = 1 };
		_db.Insert(row); return row;
	}
	private Tran01Tenuri SeedSlip(long id, string day, long oldSeq = 0, long use = 0) {
		var row = new Tran01Tenuri { Id_Customer = id, DenDay = day, Kubun = 10, OldSeqNo = oldSeq, UsePoint = use, GrantPoint = 777,
			Jmeisai = [new() { Kingaku = 1000, Tax = 100 }] };
		_db.Insert(row); return row;
	}

	[TestMethod]
	public void Rebuild_InitializesPointsPreservesSettingsUseAndPurchasesAndOnlyRecalculatesNative() {
		var id = SeedCustomer(); var emptyId = SeedCustomer("EMPTY", 88); SeedReferences(); SeedLegacy(1, 100); SeedReal(100); var rule = SeedBase();
		_db.Insert(new SummaryPoint { Id_Customer = checked((int)id), Point = 12, SalesCount = 3, SalesKingaku = 6000 });
		_db.Insert(new TranPointEvent { EventKey = "TRIAL", DenDay = "20261001", Id_Customer = id, PointDelta = 12 });
		var old = SeedSlip(id, "20260901", oldSeq: 12, use: 3); var later = SeedSlip(id, "20261101", use: 5); var earlier = SeedSlip(id, "20261001");
		Assert.AreEqual(4, Convert.RebuildPointHistory(chunkSize: 1), "旧履歴1 + native付与2 + 使用1");
		Assert.AreEqual(4, Events().Length); Assert.IsFalse(Events().Any(x => x.EventKey == "TRIAL"));
		Assert.AreEqual(115, Balance(id)); Assert.AreEqual(115, AccountPoint(id)); Assert.AreEqual(0, AccountPoint(emptyId)); Assert.AreEqual(0, Balance(emptyId));
		var oldAfter = _db.Fetch<Tran01Tenuri>("SELECT * FROM Tran01Tenuri WHERE Id=@0", old.Id).Single(); Assert.AreEqual(0L, oldAfter.GrantPoint); Assert.AreEqual(3L, oldAfter.UsePoint);
		Assert.AreEqual(10L, _db.Fetch<Tran01Tenuri>("SELECT * FROM Tran01Tenuri WHERE Id=@0", earlier.Id).Single().GrantPoint);
		Assert.AreEqual(5L, _db.Fetch<Tran01Tenuri>("SELECT * FROM Tran01Tenuri WHERE Id=@0", later.Id).Single().UsePoint);
		var summary = _db.Fetch<SummaryPoint>("SELECT * FROM SummaryPoint WHERE Id_Customer=@0", id).Single(); Assert.AreEqual(3, summary.SalesCount); Assert.AreEqual(6000, summary.SalesKingaku);
		Assert.AreEqual(100L, _db.Fetch<MasterPointBase>("SELECT * FROM MasterPointBase WHERE Id=@0", rule.Id).Single().PointUnitPrice);
		Assert.AreEqual("2", _db.Fetch<MasterEndCustomerAccount>("SELECT * FROM MasterEndCustomerAccount WHERE Id_Customer=@0", id).Single().PointRank);
		Assert.AreEqual(0, new PointCalcDb(_db).Recalc("202610", "202611")); Assert.AreEqual(115, Balance(id));
	}

	[TestMethod]
	public void Rebuild_CalculatedBalanceOverflowRollsBackInitializationAndImportedHistory() {
		var id = SeedCustomer(); SeedReferences(); SeedLegacy(1, int.MaxValue); SeedReal(int.MaxValue); SeedBase();
		_db.Insert(new TranPointEvent { EventKey = "TRIAL", DenDay = "20260901", Id_Customer = id, PointDelta = 12 });
		_db.Insert(new SummaryPoint { Id_Customer = checked((int)id), Point = 12 });
		var good = SeedSlip(id, "20261001");
		Assert.ThrowsExactly<OverflowException>(() => Convert.RebuildPointHistory(chunkSize: 1));
		Assert.AreEqual("TRIAL", Events().Single().EventKey); Assert.AreEqual(12, Balance(id)); Assert.AreEqual(999, AccountPoint(id));
		Assert.AreEqual(777L, _db.Fetch<Tran01Tenuri>("SELECT * FROM Tran01Tenuri WHERE Id=@0", good.Id).Single().GrantPoint);
	}
	[TestMethod]
	public async Task Rebuild_StreamRequiresExclusiveSelectionAndInitialization() {
		var id = SeedCustomer(); SeedReferences(); SeedLegacy(1, 100); SeedReal(100);
		_db.Insert(new TranPointEvent { EventKey = "TRIAL", Id_Customer = id, PointDelta = 7 });
		Assert.AreEqual(1, Convert.GetAllTaskNames().Count(x => x == nameof(ConvertDb.RebuildPointHistory)));
		Assert.ThrowsExactly<ArgumentException>(() => Convert.ConvertSelectAsyncStream([nameof(ConvertDb.RebuildPointHistory), nameof(ConvertDb.CnvTranPointHistory)]));
		var progress = new List<StreamStepProgress>();
		await foreach (var step in Convert.ConvertSelectAsyncStream([nameof(ConvertDb.RebuildPointHistory)], isInit: false)) progress.Add(step);
		Assert.IsTrue(progress.Any(x => x.IsError)); Assert.AreEqual("TRIAL", Events().Single().EventKey); Assert.AreEqual(999, AccountPoint(id));
		progress.Clear();
		await foreach (var step in Convert.ConvertSelectAsyncStream([nameof(ConvertDb.RebuildPointHistory)], isInit: true)) progress.Add(step);
		Assert.IsFalse(progress.Any(x => x.IsError)); Assert.IsTrue(progress.Any(x => x.IsCompleted));
		Assert.AreEqual("LEGACY:CV:POINT:1", Events().Single().EventKey); Assert.AreEqual(100, AccountPoint(id));
	}
}
