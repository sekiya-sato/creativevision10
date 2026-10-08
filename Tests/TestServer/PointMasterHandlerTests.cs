using System;
using System.Linq;
using System.Threading.Tasks;
using CodeShare;
using CvAsset;
using CvBase;
using CvBaseSqlite;
using CvDomainLogic;
using CvServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>汎用保存境界と実SQLite上のポイント条件保護を検証する。</summary>
[TestClass]
public sealed class PointMasterHandlerTests {
	private ExDatabaseSqlite _db = null!;
	private SqliteConnection _connection = null!;
	private SqliteConnection _anchor = null!;
	private ServiceProvider _provider = null!;
	private CoreService _service = null!;
	private PointMasterDb Guard => new(_db);

	[TestInitialize]
	public void Initialize() {
		var connectionString = $"Data Source=Point-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
		_anchor = new SqliteConnection(connectionString);
		_anchor.Open();
		_connection = new SqliteConnection(connectionString);
		_connection.Open();
		_db = new ExDatabaseSqlite(_connection) { KeepConnectionAlive = true };
		foreach (var type in new[] { typeof(MasterPointBase), typeof(MasterPointRank), typeof(MasterPointBonus), typeof(MasterPointCampaign), typeof(TranPointEvent), typeof(SummaryPoint) }) _db.CreateTable(type, true, false);
		_provider = new ServiceCollection().BuildServiceProvider();
		_service = new CoreService(NullLogger<CoreService>.Instance, new ConfigurationBuilder().Build(), new FakeWebHostEnvironment(), new HttpContextAccessor(), _db, _provider.GetRequiredService<IServiceScopeFactory>(), new PointOfSaleService(_db, NullLogger<PointOfSaleService>.Instance));
	}

	[TestCleanup]
	public void Cleanup() { _db.Close(); _connection.Dispose(); _anchor.Dispose(); _provider.Dispose(); }
	private Task<CvMsg> Send(object param) => _service.QueryMsgAsync(new CvMsg { Flag = CvFlag.Msg201_Op_Execute, DataType = param.GetType(), DataMsg = Common.SerializeObject(param) });
	private Task<CvMsg> Insert(BaseDbClass item) => Send(new InsertParam(item.GetType(), Common.SerializeObject(item)));
	private Task<CvMsg> Update(BaseDbClass item) => Send(new UpdateParam(item.GetType(), Common.SerializeObject(item)));
	private T Row<T>(long id) => _db.Fetch<T>("WHERE Id=@0", id).Single();
	private static MasterPointBase Base(string code = "B", int version = 1) => new() { Code = code, Name = "基本", Version = version, DayFrom = "20260101", DayTo = "20261231", IsEnabled = 1 };
	private static MasterPointRank Rank(long parent, int code = 1) => new() { Id_PointBase = parent, Kubun = code, Name = "ランク" };
	private static MasterPointBonus Bonus(long parent) => new() { Id_PointBase = parent, Code = "P", Name = "追加", DayFrom = "20260401", DayTo = "20260430", PointAmount = 10, IsEnabled = 1 };
	private long Seed(BaseDbClass row) { _db.Insert(row); return row.Id; }
	private static T Copy<T>(T row) where T : BaseDbClass => (T)Common.DeserializeObject(Common.SerializeObject(row), typeof(T))!;

	[TestMethod]
	public async Task Crud_ValidConditionsPersistWithAuditAndCanBeDeleted() {
		var reply = await Insert(Base());
		Assert.AreEqual(0, reply.Code, reply.Option);
		var parent = _db.Fetch<MasterPointBase>("SELECT * FROM MasterPointBase").Single();
		Assert.IsGreaterThan(0L, parent.Id);
		Assert.IsGreaterThan(0L, parent.Vdu);
		Assert.AreEqual(0, (await Insert(Rank(parent.Id))).Code);
		Assert.AreEqual(0, (await Insert(Bonus(parent.Id))).Code);
		parent.Name = "変更";
		Assert.AreEqual(0, (await Update(parent)).Code);
		Assert.AreEqual("変更", Row<MasterPointBase>(parent.Id).Name);
		var bonus = _db.Fetch<MasterPointBonus>("SELECT * FROM MasterPointBonus").Single();
		Assert.AreEqual(0, (await Send(new DeleteByIdParam(typeof(MasterPointBonus), bonus.Id, bonus.Vdu))).Code);
		var rank = _db.Fetch<MasterPointRank>("SELECT * FROM MasterPointRank").Single();
		Assert.AreEqual(0, (await Send(new DeleteParam(typeof(MasterPointRank), Common.SerializeObject(rank)))).Code);
		parent = Row<MasterPointBase>(parent.Id);
		Assert.AreEqual(0, (await Send(new DeleteBulkParam(typeof(MasterPointBase), [new(parent.Id, parent.Vdu)]))).Code);
		Assert.AreEqual(0, _db.Fetch<MasterPointBase>("SELECT * FROM MasterPointBase").Count);
	}

	[TestMethod]
	[DataRow("code")]
	[DataRow("name")]
	[DataRow("version")]
	[DataRow("date")]
	[DataRow("order")]
	[DataRow("price")]
	[DataRow("point")]
	[DataRow("tax")]
	[DataRow("round")]
	[DataRow("flag")]
	public async Task Insert_InvalidInputLeavesDatabaseEmpty(string kind) {
		var row = Base();
		switch (kind) {
			case "code": row.Code = " "; break;
			case "name": row.Name = new string('名', 81); break;
			case "version": row.Version = 0; break;
			case "date": row.DayFrom = "20260230"; break;
			case "order": row.DayFrom = "20270101"; break;
			case "price": row.PointUnitPrice = 0; break;
			case "point": row.PointAmountSale = -1; break;
			case "tax": row.TaxBasis = 999; break;
			case "round": row.Rounding = 999; break;
			case "flag": row.IsEnabled = -1; break;
		}
		Assert.AreNotEqual(0, (await Insert(row)).Code);
		Assert.AreEqual(0, _db.Fetch<MasterPointBase>("SELECT * FROM MasterPointBase").Count);
	}

	[TestMethod]
	public void ValidateSave_ReferencesAndRankIdentityAreEnforced() {
		Assert.Throws<ArgumentException>(() => Guard.ValidateSave(Rank(0), null));
		Assert.Throws<ArgumentException>(() => Guard.ValidateSave(Rank(99), null));
		var parent = Seed(Base());
		Seed(Rank(parent));
		Assert.Throws<ArgumentException>(() => Guard.ValidateSave(Rank(parent), null));
		var bonus = Bonus(parent); bonus.IsAllRanks = 0; bonus.RankKubun = 2;
		Assert.Throws<ArgumentException>(() => Guard.ValidateSave(bonus, null));
		bonus.RankKubun = 1;
		Guard.ValidateSave(bonus, null);
		bonus.IsAllRanks = 1;
		Assert.Throws<ArgumentException>(() => Guard.ValidateSave(bonus, null));
		bonus.RankKubun = 0; bonus.DayTo = "20270101";
		Assert.Throws<ArgumentException>(() => Guard.ValidateSave(bonus, null));
	}

	[TestMethod]
	public async Task BulkInsert_OverlappingBoundaryRollsBackEarlierRows() {
		var first = Base(); first.DayTo = "20260401";
		var second = Base(version: 2); second.DayFrom = "20260401";
		var reply = await Send(new InsertBulkParam(typeof(MasterPointBase), Common.SerializeObject(new[] { first, second })));
		Assert.AreNotEqual(0, reply.Code);
		Assert.AreEqual(0, _db.Fetch<MasterPointBase>("SELECT * FROM MasterPointBase").Count);
		second.DayFrom = "20260402";
		Assert.AreEqual(0, (await Send(new InsertBulkParam(typeof(MasterPointBase), Common.SerializeObject(new[] { first, second })))).Code);
		Assert.AreEqual(2, _db.Fetch<MasterPointBase>("SELECT * FROM MasterPointBase").Count);
	}

	[TestMethod]
	public async Task UsedBaseAndChildren_ConditionsFrozenButEnabledToggleAllowed() {
		var parent = Seed(Base());
		var rank = Rank(parent); Seed(rank);
		var bonus = Bonus(parent); Seed(bonus);
		Seed(new TranPointEvent { EventKey = "used", Id_PointRank = rank.Id });
		var row = Row<MasterPointBase>(parent); row.Name = "不正変更";
		Assert.AreNotEqual(0, (await Update(row)).Code);
		Assert.AreEqual("基本", Row<MasterPointBase>(parent).Name);
		row = Row<MasterPointBase>(parent); row.IsEnabled = 0;
		Assert.AreEqual(0, (await Update(row)).Code);
		bonus.IsEnabled = 0;
		Assert.AreEqual(0, (await Update(bonus)).Code);
		Assert.AreNotEqual(0, (await Insert(Rank(parent, 2))).Code);
		var newBonus = Bonus(parent); newBonus.Code = "NEW";
		Assert.AreNotEqual(0, (await Insert(newBonus)).Code);
		rank.PointAmountProper++;
		Assert.AreNotEqual(0, (await Update(rank)).Code);
		Assert.AreNotEqual(0, (await Send(new DeleteByIdParam(typeof(MasterPointRank), rank.Id, rank.Vdu))).Code);
		Assert.AreEqual(1, _db.Fetch<MasterPointRank>("SELECT * FROM MasterPointRank").Count);
	}

	[TestMethod]
	public void UsedBase_ByBonusReferenceAlsoProtectsParentAndDeletes() {
		var parent = Seed(Base()); var bonus = Bonus(parent); Seed(bonus);
		Seed(new TranPointEvent { EventKey = "bonus", Id_PointBonus = bonus.Id });
		var row = Row<MasterPointBase>(parent); var changed = Copy(row); changed.PointUnitPrice++;
		Assert.Throws<ArgumentException>(() => Guard.ValidateSave(changed, row));
		Assert.Throws<ArgumentException>(() => Guard.ValidateDelete(row));
		Assert.Throws<ArgumentException>(() => Guard.ValidateDelete(bonus));
	}

	[TestMethod]
	public async Task Reactivation_UsedVersionStillChecksOverlap() {
		var old = Base(); old.IsEnabled = 0; Seed(old);
		Seed(new TranPointEvent { EventKey = "base", Id_PointBase = old.Id });
		Seed(Base(version: 2));
		old.IsEnabled = 1;
		Assert.AreNotEqual(0, (await Update(old)).Code);
		Assert.AreEqual(0, Row<MasterPointBase>(old.Id).IsEnabled);
	}

	[TestMethod]
	public void ParentAndRankChanges_DoNotOrphanExistingBonus() {
		var parent = Seed(Base()); var rank = Rank(parent); Seed(rank);
		var bonus = Bonus(parent); bonus.IsAllRanks = 0; bonus.RankKubun = 1; Seed(bonus);
		var row = Row<MasterPointBase>(parent); var changed = Copy(row); changed.DayTo = "20260415";
		Assert.Throws<ArgumentException>(() => Guard.ValidateSave(changed, row));
		Assert.Throws<ArgumentException>(() => Guard.ValidateDelete(row));
		var rankChanged = Copy(rank); rankChanged.Kubun = 2;
		Assert.Throws<ArgumentException>(() => Guard.ValidateSave(rankChanged, rank));
		Assert.Throws<ArgumentException>(() => Guard.ValidateDelete(rank));
	}

	[TestMethod]
	public async Task BulkDelete_OneProtectedParentRollsBackAll() {
		var free = Base("FREE"); Seed(free);
		var parent = Base("PARENT"); Seed(parent); Seed(Rank(parent.Id));
		Assert.AreNotEqual(0, (await Send(new DeleteBulkParam(typeof(MasterPointBase), [new(free.Id, free.Vdu), new(parent.Id, parent.Vdu)]))).Code);
		Assert.AreEqual(2, _db.Fetch<MasterPointBase>("SELECT * FROM MasterPointBase").Count);
	}

	[TestMethod]
	public async Task Update_StaleAuditDoesNotChangeConditions() {
		var row = Base(); Seed(row); row.Vdu = -1; row.Name = "古い更新";
		Assert.AreEqual(CvMsgErrorCode.ConcurrentUpdate, (await Update(row)).Code);
		Assert.AreEqual("基本", Row<MasterPointBase>(row.Id).Name);
	}

	[TestMethod]
	[DataRow(typeof(TranPointEvent))]
	[DataRow(typeof(SummaryPoint))]
	public async Task LedgerAndBalance_AllSevenGenericRoutesRejectWithoutMutation(Type type) {
		var row = (BaseDbClass)Activator.CreateInstance(type)!; Seed(row);
		object[] requests = [new InsertParam(type, Common.SerializeObject(row)), new InsertBulkParam(type, Common.SerializeObject(new[] { row })), new UpdateParam(type, Common.SerializeObject(row)), new DeleteParam(type, Common.SerializeObject(row)), new DeleteByIdParam(type, row.Id, row.Vdu), new DeleteBulkParam(type, [new(row.Id, row.Vdu)]), new PartialUpdateParam(type, [type == typeof(TranPointEvent) ? "PointDelta" : "Point"], [new(row.Id, row.Vdu, ["100"])])];
		foreach (var request in requests) {
			var reply = await Send(request);
			Assert.AreNotEqual(0, reply.Code, request.GetType().Name);
			Assert.HasCount(1, _db.Fetch(type, "SELECT * FROM " + type.Name));
		}
		if (type == typeof(TranPointEvent)) Assert.AreEqual(0L, Row<TranPointEvent>(row.Id).PointDelta);
		else Assert.AreEqual(0, Row<SummaryPoint>(row.Id).Point);
	}

	[TestMethod]
	[DataRow(typeof(MasterPointBase))]
	[DataRow(typeof(MasterPointRank))]
	[DataRow(typeof(MasterPointBonus))]
	public async Task Master_PartialUpdateCannotBypassValidation(Type type) {
		var parent = Seed(Base());
		BaseDbClass row = type == typeof(MasterPointBase) ? Row<MasterPointBase>(parent) : type == typeof(MasterPointRank) ? Rank(parent) : Bonus(parent);
		if (type != typeof(MasterPointBase)) Seed(row);
		Assert.AreNotEqual(0, (await Send(new PartialUpdateParam(type, ["Name"], [new(row.Id, row.Vdu, ["迂回"])]))).Code);
		var loaded = _db.Fetch(type, "WHERE Id=@0", row.Id).Single();
		Assert.AreEqual(type.GetProperty("Name")!.GetValue(row), type.GetProperty("Name")!.GetValue(loaded));
	}
	[TestMethod]
	public async Task CommonPayload_LegalZeroValuesRoundTrip() {
		var parent = Base(); parent.PointAmountProper = 0; parent.PointAmountSale = 0; parent.Rounding = 0;
		Assert.AreEqual(0, (await Send(new InsertParam(typeof(MasterPointBase), Common.SerializeObject(parent)))).Code);
		parent = _db.Fetch<MasterPointBase>("SELECT * FROM MasterPointBase").Single();
		Assert.AreEqual(0L, parent.PointAmountProper);
		Assert.AreEqual(0L, parent.PointAmountSale);
		Assert.AreEqual(0, parent.Rounding);
		var rank = Rank(parent.Id); rank.PointAmountProper = 0; rank.PointAmountSale = 0;
		Assert.AreEqual(0, (await Send(new InsertParam(typeof(MasterPointRank), Common.SerializeObject(rank)))).Code);
		rank = _db.Fetch<MasterPointRank>("SELECT * FROM MasterPointRank").Single();
		Assert.AreEqual(0L, rank.PointAmountProper);
		Assert.AreEqual(0L, rank.PointAmountSale);
		var bonus = Bonus(parent.Id); bonus.IsAllRanks = 0; bonus.RankKubun = rank.Kubun;
		Assert.AreEqual(0, (await Send(new InsertParam(typeof(MasterPointBonus), Common.SerializeObject(bonus)))).Code);
		bonus = _db.Fetch<MasterPointBonus>("SELECT * FROM MasterPointBonus").Single();
		Assert.AreEqual(0, bonus.IsAllRanks);
		Assert.AreEqual(rank.Kubun, bonus.RankKubun);
	}

	[TestMethod]
	[DataRow("amount")]
	[DataRow("minimum")]
	[DataRow("limit")]
	[DataRow("trigger")]
	[DataRow("period")]
	public void Bonus_InvalidNumericAndEnumConditionsRejected(string kind) {
		var bonus = Bonus(Seed(Base()));
		switch (kind) {
			case "amount": bonus.PointAmount = 0; break;
			case "minimum": bonus.MinimumKingaku = -1; break;
			case "limit": bonus.LimitCount = 0; break;
			case "trigger": bonus.TriggerType = 999; break;
			case "period": bonus.LimitPeriodType = 999; break;
		}
		Assert.Throws<ArgumentException>(() => Guard.ValidateSave(bonus, null));
	}

	[TestMethod]
	public async Task Bonus_DuplicateVersionAndOverlapBlockedButDifferentCodesAllowed() {
		var parent = Seed(Base());
		var bonus = Bonus(parent); Seed(bonus);
		Assert.AreNotEqual(0, (await Insert(Bonus(parent))).Code);
		var other = Bonus(parent); other.Version = 2; other.DayFrom = bonus.DayTo;
		Assert.AreNotEqual(0, (await Insert(other)).Code);
		other.DayFrom = "20260501"; other.DayTo = "20260531";
		Assert.AreEqual(0, (await Insert(other)).Code);
		other = Bonus(parent); other.Code = "ANOTHER";
		Assert.AreEqual(0, (await Insert(other)).Code);
		Assert.AreEqual(3, _db.Fetch<MasterPointBonus>("SELECT * FROM MasterPointBonus").Count);
	}

	[TestMethod]
	[DataRow("item")]
	[DataRow("id")]
	[DataRow("bulk")]
	public async Task UsedBase_AllDeleteRoutesPreserveRow(string route) {
		var row = Base(); Seed(row);
		Seed(new TranPointEvent { EventKey = "ref", Id_PointBase = row.Id });
		object request = route switch {
			"item" => new DeleteParam(typeof(MasterPointBase), Common.SerializeObject(row)),
			"id" => new DeleteByIdParam(typeof(MasterPointBase), row.Id, row.Vdu),
			_ => new DeleteBulkParam(typeof(MasterPointBase), [new(row.Id, row.Vdu)]),
		};
		Assert.AreNotEqual(0, (await Send(request)).Code);
		Assert.AreEqual("基本", Row<MasterPointBase>(row.Id).Name);
	}

	[TestMethod]
	public void Child_MoveCannotEscapeUsedParentOrEnterUsedParent() {
		var used = Seed(Base("USED")); var free = Seed(Base("FREE"));
		var rank = Rank(used); Seed(rank);
		var bonus = Bonus(used); Seed(bonus);
		Seed(new TranPointEvent { EventKey = "used", Id_PointBase = used });
		var movedRank = Copy(rank); movedRank.Id_PointBase = free;
		Assert.Throws<ArgumentException>(() => Guard.ValidateSave(movedRank, rank));
		var movedBonus = Copy(bonus); movedBonus.Id_PointBase = free;
		Assert.Throws<ArgumentException>(() => Guard.ValidateSave(movedBonus, bonus));
		var freeRank = Rank(free, 2); Seed(freeRank); movedRank = Copy(freeRank); movedRank.Id_PointBase = used;
		Assert.Throws<ArgumentException>(() => Guard.ValidateSave(movedRank, freeRank));
	}

	[TestMethod]
	[DataRow("base", false, false)]
	[DataRow("base", false, true)]
	[DataRow("bonus", false, false)]
	[DataRow("bonus", false, true)]
	[DataRow("base", true, true)]
	[DataRow("bonus", true, true)]
	[DataRow("rank", false, false)]
	public async Task Insert_ClientSuppliedIdCannotExcludeExistingConditions(string target, bool bulk, bool overlapOnly) {
		var parent = Base(); Seed(parent);
		BaseDbClass existing = parent;
		if (target == "bonus") { existing = Bonus(parent.Id); Seed(existing); }
		if (target == "rank") { existing = Rank(parent.Id); Seed(existing); }
		var candidate = (BaseDbClass)Common.DeserializeObject(Common.SerializeObject(existing), existing.GetType())!;
		if (overlapOnly) {
			if (candidate is MasterPointBase b) b.Version = 2;
			if (candidate is MasterPointBonus p) p.Version = 2;
		}
		object request = bulk
			? new InsertBulkParam(candidate.GetType(), Common.SerializeObject(new[] { candidate }))
			: new InsertParam(candidate.GetType(), Common.SerializeObject(candidate));
		Assert.AreNotEqual(0, (await Send(request)).Code);
		Assert.HasCount(1, _db.Fetch(candidate.GetType(), "SELECT * FROM " + candidate.GetType().Name));
		Assert.AreEqual(existing.Id, ((BaseDbClass)_db.Fetch(candidate.GetType(), "SELECT * FROM " + candidate.GetType().Name).Single()).Id);
	}

	private TranPointEvent Manual(long idCustomer, EnumPointEventType type, long delta, string key = "", long original = 0) =>
		new() { EventKey = key, DenDay = "20261008", Id_Customer = idCustomer, EventType = (int)type, PointDelta = delta, Id_OriginalEvent = original, Memo = "手動" };

	[TestMethod]
	public async Task ManualLedger_InsertAppliesBalanceRejectsInvalidAndAppendsOnly() {
		foreach (var t in new[] { typeof(MasterEndCustomer), typeof(MasterEndCustomerAccount), typeof(MasterShain) }) _db.CreateTable(t, true, false);
		var customer = Seed(new MasterEndCustomer { Code = "K1", Name = "会員" });
		Seed(new MasterEndCustomerAccount { Id_Customer = customer, Point = 0 });
		long Summary() => _db.Fetch<SummaryPoint>("SELECT * FROM SummaryPoint WHERE Id_Customer=@0", customer).Single().Point;
		long Account() => _db.Fetch<MasterEndCustomerAccount>("SELECT * FROM MasterEndCustomerAccount WHERE Id_Customer=@0", customer).Single().Point;
		int Count() => _db.Fetch<TranPointEvent>("SELECT * FROM TranPointEvent").Count;

		var reply = await Insert(Manual(customer, EnumPointEventType.Adjustment, 100, "MANUAL:a1"));
		Assert.AreEqual(0, reply.Code, reply.Option);
		Assert.AreEqual(100L, Summary());
		Assert.AreEqual(100L, Account());
		Assert.AreNotEqual(0, (await Insert(Manual(customer, EnumPointEventType.Adjustment, 100, "MANUAL:a1"))).Code, "再送(同じEventKey)は二重計上しない");
		Assert.AreEqual(1, Count());

		Assert.AreEqual(0, (await Insert(Manual(customer, EnumPointEventType.Use, -30))).Code);
		var use = _db.Fetch<TranPointEvent>("SELECT * FROM TranPointEvent WHERE EventType=@0", (int)EnumPointEventType.Use).Single();
		StringAssert.StartsWith(use.EventKey, "MANUAL:");
		Assert.AreEqual(70L, Summary());
		var shortage = await Insert(Manual(customer, EnumPointEventType.Use, -71));
		Assert.AreNotEqual(0, shortage.Code, "残高不足");
		Assert.AreEqual(70L, Summary());
		Assert.AreEqual(2, Count());

		Assert.AreEqual(0, (await Insert(Manual(customer, EnumPointEventType.Cancel, 30, original: use.Id))).Code);
		Assert.AreEqual(100L, Summary());
		Assert.AreEqual(100L, Account());
		Assert.AreNotEqual(0, (await Insert(Manual(customer, EnumPointEventType.Cancel, 30, original: use.Id))).Code, "二重取消");

		var sales = Seed(new TranPointEvent { EventKey = "TENURI:1:G:1:0", DenDay = "20261008", Id_Customer = customer, Id_Tenuri = 1, EventType = (int)EnumPointEventType.Grant, PointDelta = 5 });
		TranPointEvent[] invalid = [
			Manual(customer, EnumPointEventType.Cancel, -5, original: sales), // 店舗売上の行は取消できない
			Manual(customer, EnumPointEventType.Grant, -1),
			Manual(customer, EnumPointEventType.Expire, 1),
			Manual(customer, EnumPointEventType.Adjustment, 0),
			Manual(customer, EnumPointEventType.OpeningBalance, 10),
			Manual(0, EnumPointEventType.Adjustment, 10),
			Manual(customer, EnumPointEventType.Adjustment, 10),
		];
		invalid[^1].Memo = "";
		var tenuri = Manual(customer, EnumPointEventType.Adjustment, 10); tenuri.Id_Tenuri = 9;
		var badKey = Manual(customer, EnumPointEventType.Adjustment, 10, "EXPIRE:1:20261008");
		foreach (var row in invalid.Append(tenuri).Append(badKey)) {
			Assert.AreNotEqual(0, (await Insert(row)).Code, $"{row.EventType}:{row.PointDelta}:{row.EventKey}");
		}
		Assert.AreEqual(4, Count());

		var bulk = await Send(new InsertBulkParam(typeof(TranPointEvent), Common.SerializeObject(new[] { Manual(customer, EnumPointEventType.Grant, 10), Manual(customer, EnumPointEventType.Expire, -20) })));
		Assert.AreEqual(0, bulk.Code, bulk.Option);
		Assert.AreEqual(95L, Summary(), "100+5(店舗売上の直接投入は残高未反映のため台帳合計で再計算)+10-20");
		var stored = Row<TranPointEvent>(use.Id);
		stored.PointDelta = -1;
		Assert.AreNotEqual(0, (await Update(stored)).Code, "台帳は更新できない");
	}
}
