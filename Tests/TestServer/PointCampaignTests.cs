using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CodeShare;
using CvAsset;
using CvBase;
using CvBase.Share;
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

/// <summary>ポイントキャンペーンのスキーマ・汎用保存保護・対象保存(Msg064)の重複置換を実SQLiteで検証する。</summary>
[TestClass]
public sealed class PointCampaignTests {
	private ExDatabaseSqlite _db = null!;
	private SqliteConnection _connection = null!;
	private SqliteConnection _anchor = null!;
	private ServiceProvider _provider = null!;
	private CoreService _service = null!;
	private long _base;
	private int _seq;

	[TestInitialize]
	public void Initialize() {
		var connectionString = $"Data Source=PointCampaign-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
		_anchor = new SqliteConnection(connectionString);
		_anchor.Open();
		_connection = new SqliteConnection(connectionString);
		_connection.Open();
		_db = new ExDatabaseSqlite(_connection) { KeepConnectionAlive = true };
		foreach (var type in new[] { typeof(MasterPointBase), typeof(MasterPointRank), typeof(MasterPointBonus), typeof(MasterPointCampaign), typeof(MasterPointCampaignShop), typeof(MasterPointCampaignShohin), typeof(TranPointEvent), typeof(SummaryPoint), typeof(MasterTokui), typeof(MasterShohin) })
			_db.CreateTable(type, true, true);
		_provider = new ServiceCollection().BuildServiceProvider();
		_service = new CoreService(NullLogger<CoreService>.Instance, new ConfigurationBuilder().Build(), new FakeWebHostEnvironment(), new HttpContextAccessor(), _db, _provider.GetRequiredService<IServiceScopeFactory>(), new PointOfSaleService(_db, NullLogger<PointOfSaleService>.Instance));
		// 共通の親: 2026年通年のベース版とランク1・2
		var parent = new MasterPointBase { Code = "B", Name = "基本", Version = 1, DayFrom = "20260101", DayTo = "20261231", IsEnabled = 1 };
		_db.Insert(parent);
		_base = parent.Id;
		_db.Insert(new MasterPointRank { Id_PointBase = _base, Kubun = 1, Name = "ランク1" });
		_db.Insert(new MasterPointRank { Id_PointBase = _base, Kubun = 2, Name = "ランク2" });
	}

	[TestCleanup]
	public void Cleanup() { _db.Close(); _connection.Dispose(); _anchor.Dispose(); _provider.Dispose(); }

	#region ヘルパー
	private Task<CvMsg> Send(object param) => _service.QueryMsgAsync(new CvMsg { Flag = CvFlag.Msg201_Op_Execute, DataType = param.GetType(), DataMsg = Common.SerializeObject(param) });
	private Task<CvMsg> Insert(BaseDbClass item) => Send(new InsertParam(item.GetType(), Common.SerializeObject(item)));
	private Task<CvMsg> Update(BaseDbClass item) => Send(new UpdateParam(item.GetType(), Common.SerializeObject(item)));
	private T Row<T>(long id) => _db.Fetch<T>("WHERE Id=@0", id).Single();
	private int Count<T>() => _db.Fetch<T>("SELECT * FROM " + typeof(T).Name).Count;

	private MasterPointCampaign Camp(string code, EnumPointCampaignPriority priority, string from = "20260401", string to = "20260430", int rank = 0, int enabled = 1) => new() {
		Code = code, Name = "キャンペーン" + code, Id_PointBase = _base, DayFrom = from, DayTo = to, IsEnabled = enabled, PriorityType = (int)priority,
		PointUnitPrice = 100, PointAmountProper = 5, PointAmountSale = 2, RankKubun = rank,
	};

	/// <summary>Vdu=1で直接登録する（Msg064でのVdu更新を検出するため）。</summary>
	private long SeedCamp(MasterPointCampaign row) { row.Vdc = 1; row.Vdu = 1; _db.Insert(row); return row.Id; }

	private long Shop(int tenType = 6) {
		var code = $"T{++_seq:000}";
		var row = new MasterTokui { Code = code, Name = "店舗" + code, TenType = tenType };
		_db.Insert(row);
		return row.Id;
	}

	private long Item() {
		var code = $"S{++_seq:000}";
		var row = new MasterShohin { Code = code, Name = "商品" + code };
		_db.Insert(row);
		return row.Id;
	}

	private void Link(long campaign, long[]? shops = null, long[]? shohins = null) {
		foreach (var id in shops ?? []) _db.Insert(new MasterPointCampaignShop { Id_PointCampaign = campaign, Id_Tenpo = id });
		foreach (var id in shohins ?? []) _db.Insert(new MasterPointCampaignShohin { Id_PointCampaign = campaign, Id_Shohin = id });
	}

	private List<long> Shops(long campaign) => _db.Fetch<MasterPointCampaignShop>("WHERE Id_PointCampaign=@0", campaign).Select(x => x.Id_Tenpo).Order().ToList();
	private List<long> Shohins(long campaign) => _db.Fetch<MasterPointCampaignShohin>("WHERE Id_PointCampaign=@0", campaign).Select(x => x.Id_Shohin).Order().ToList();

	private Task<CvMsg> Targets(long campaign, long[]? shops = null, long[]? shohins = null, bool preview = false, List<PointCampaignConflict>? confirmed = null, long? vdu = null) {
		var param = new PointCampaignTargetParameter {
			Id_PointCampaign = campaign, Vdu = vdu ?? Row<MasterPointCampaign>(campaign).Vdu,
			Ids_Tenpo = [.. shops ?? []], Ids_Shohin = [.. shohins ?? []], IsPreview = preview, Confirmed = confirmed ?? [],
		};
		return _service.QueryMsgAsync(new CvMsg { Flag = CvFlag.Msg064_PointCampaignTargetSave, DataType = typeof(PointCampaignTargetParameter), DataMsg = Common.SerializeObject(param) });
	}

	private static PointCampaignTargetResult Result(CvMsg reply) {
		Assert.AreEqual(0, reply.Code, reply.Option + " " + reply.DataMsg);
		return (PointCampaignTargetResult)Common.DeserializeObject(reply.DataMsg, typeof(PointCampaignTargetResult))!;
	}

	private async Task<List<PointCampaignConflict>> Preview(long campaign, long[]? shops = null, long[]? shohins = null) =>
		Result(await Targets(campaign, shops, shohins, preview: true)).Conflicts;

	/// <summary>DBの状態（対象行・Vdu）を比較用文字列にする。</summary>
	private string Snapshot() =>
		string.Join("|", _db.Fetch<MasterPointCampaign>("ORDER BY Id").Select(x => $"{x.Id}:{x.Vdu}:{x.IsEnabled}:{x.DayFrom}-{x.DayTo}"))
		+ "#" + string.Join(",", _db.Fetch<MasterPointCampaignShop>("ORDER BY Id_PointCampaign, Id_Tenpo").Select(x => $"{x.Id_PointCampaign}/{x.Id_Tenpo}"))
		+ "#" + string.Join(",", _db.Fetch<MasterPointCampaignShohin>("ORDER BY Id_PointCampaign, Id_Shohin").Select(x => $"{x.Id_PointCampaign}/{x.Id_Shohin}"));
	#endregion

	#region ランク参照
	[TestMethod]
	public async Task Rank_ReferencedByCampaign_DeleteAndKubunChangeRejected() {
		SeedCamp(Camp("R", EnumPointCampaignPriority.AllShops, rank: 2));
		var rank = _db.Fetch<MasterPointRank>("WHERE Kubun=2").Single();
		Assert.AreNotEqual(0, (await Send(new DeleteByIdParam(typeof(MasterPointRank), rank.Id, rank.Vdu))).Code);
		rank.Kubun = 3;
		Assert.AreNotEqual(0, (await Update(rank)).Code);
		Assert.AreEqual(2, Row<MasterPointRank>(rank.Id).Kubun);
		// 参照されていないランクは削除できる
		var free = _db.Fetch<MasterPointRank>("WHERE Kubun=1").Single();
		Assert.AreEqual(0, (await Send(new DeleteByIdParam(typeof(MasterPointRank), free.Id, free.Vdu))).Code);
	}
	#endregion

	#region スキーマ・JSON
	[TestMethod]
	public void Schema_UniqueKeysRejectDuplicates() {
		var a = SeedCamp(Camp("A", EnumPointCampaignPriority.Shop));
		var b = SeedCamp(Camp("B", EnumPointCampaignPriority.Shop));
		Assert.ThrowsExactly<SqliteException>(() => _db.Insert(Camp("A", EnumPointCampaignPriority.Shop)));
		Link(a, shops: [10], shohins: [20]);
		Link(b, shops: [10], shohins: [20]);
		Assert.ThrowsExactly<SqliteException>(() => _db.Insert(new MasterPointCampaignShop { Id_PointCampaign = a, Id_Tenpo = 10 }));
		Assert.ThrowsExactly<SqliteException>(() => _db.Insert(new MasterPointCampaignShohin { Id_PointCampaign = a, Id_Shohin = 20 }));
		Assert.AreEqual(2, Count<MasterPointCampaignShop>());
		Assert.AreEqual(2, Count<MasterPointCampaignShohin>());
	}

	[TestMethod]
	public async Task Initialize_NewDatabase_CreatesCampaignTables() {
		using var conn = new SqliteConnection("Data Source=:memory:");
		conn.Open();
		var db = new ExDatabaseSqlite(conn) { KeepConnectionAlive = true };
		try {
			Assert.IsTrue(await new DefineDataTable().InitializeAsync(db, false));
			Assert.IsTrue(db.IsExistTable(typeof(MasterPointCampaign)));
			Assert.IsTrue(db.IsExistTable(typeof(MasterPointCampaignShop)));
			Assert.IsTrue(db.IsExistTable(typeof(MasterPointCampaignShohin)));
			db.Insert(new MasterPointCampaign { Code = "C1" });
			Assert.ThrowsExactly<SqliteException>(() => db.Insert(new MasterPointCampaign { Code = "C1" }));
			Assert.IsTrue(await new DefineDataTable().InitializeAsync(db, false), "2回目の起動でも成立する");
		}
		finally { db.Close(); }
	}

	[TestMethod]
	public async Task Json_DefaultsAndZeroAmountsRoundTrip() {
		var fresh = (MasterPointCampaign)Common.DeserializeObject(Common.SerializeObject(new MasterPointCampaign()), typeof(MasterPointCampaign))!;
		CollectionAssert.AreEqual(new long[] { 100, 1, 1 }, new[] { fresh.PointUnitPrice, fresh.PointAmountProper, fresh.PointAmountSale });
		var zero = Camp("Z", EnumPointCampaignPriority.AllShops); zero.PointAmountProper = 0; zero.PointAmountSale = 0; zero.IsEnabled = 0;
		var copy = (MasterPointCampaign)Common.DeserializeObject(Common.SerializeObject(zero), typeof(MasterPointCampaign))!;
		CollectionAssert.AreEqual(new long[] { 100, 0, 0 }, new[] { copy.PointUnitPrice, copy.PointAmountProper, copy.PointAmountSale });
		var unit0 = new MasterPointCampaign { PointUnitPrice = 0 };
		Assert.AreEqual(0L, ((MasterPointCampaign)Common.DeserializeObject(Common.SerializeObject(unit0), typeof(MasterPointCampaign))!).PointUnitPrice);
		// 汎用保存経路でも0付与・無効・全店・全ランクが既定値に戻らない
		Assert.AreEqual(0, (await Insert(zero)).Code);
		var saved = _db.Fetch<MasterPointCampaign>("WHERE Code='Z'").Single();
		CollectionAssert.AreEqual(new long[] { 100, 0, 0 }, new[] { saved.PointUnitPrice, saved.PointAmountProper, saved.PointAmountSale });
		CollectionAssert.AreEqual(new[] { 0, 0, 0 }, new[] { saved.IsEnabled, saved.PriorityType, saved.RankKubun });
		var target = new PointCampaignTargetParameter { Id_PointCampaign = 5, Vdu = 7, IsPreview = true, Ids_Tenpo = [1, 2], Confirmed = [new PointCampaignConflict { Id_PointCampaign = 3, TargetKind = 1, Id_Target = 2, Code = "X" }] };
		var targetCopy = (PointCampaignTargetParameter)Common.DeserializeObject(Common.SerializeObject(target), typeof(PointCampaignTargetParameter))!;
		Assert.IsTrue(targetCopy.IsPreview);
		CollectionAssert.AreEqual(target.Ids_Tenpo, targetCopy.Ids_Tenpo);
		Assert.AreEqual(target.Confirmed.Single(), targetCopy.Confirmed.Single());
	}
	#endregion

	#region マスタ入力検証・汎用経路
	[TestMethod]
	[DataRow("codeEmpty")]
	[DataRow("codeLong")]
	[DataRow("codeSpace")]
	[DataRow("nameLong")]
	[DataRow("memoLong")]
	[DataRow("date")]
	[DataRow("order")]
	[DataRow("enabled")]
	[DataRow("priority")]
	[DataRow("price")]
	[DataRow("proper")]
	[DataRow("sale")]
	[DataRow("baseZero")]
	[DataRow("baseMissing")]
	[DataRow("beforeBase")]
	[DataRow("afterBase")]
	[DataRow("rankMissing")]
	[DataRow("rankNegative")]
	[DataRow("duplicateCode")]
	public async Task Insert_InvalidInputRejectedWithoutMutation(string kind) {
		SeedCamp(Camp("EXIST", EnumPointCampaignPriority.AllShops));
		var row = Camp("NEW", EnumPointCampaignPriority.Shop);
		switch (kind) {
			case "codeEmpty": row.Code = ""; break;
			case "codeLong": row.Code = new string('C', 21); break;
			case "codeSpace": row.Code = " NEW"; break;
			case "nameLong": row.Name = new string('名', 81); break;
			case "memoLong": row.Memo = new string('備', 201); break;
			case "date": row.DayTo = "20260431"; break;
			case "order": row.DayFrom = "20260501"; break;
			case "enabled": row.IsEnabled = 2; break;
			case "priority": row.PriorityType = 4; break;
			case "price": row.PointUnitPrice = 0; break;
			case "proper": row.PointAmountProper = -1; break;
			case "sale": row.PointAmountSale = -1; break;
			case "baseZero": row.Id_PointBase = 0; break;
			case "baseMissing": row.Id_PointBase = 999; break;
			case "beforeBase": row.DayFrom = "20251231"; break;
			case "afterBase": row.DayTo = "20270101"; break;
			case "rankMissing": row.RankKubun = 3; break;
			case "rankNegative": row.RankKubun = -1; break;
			case "duplicateCode": row.Code = "EXIST"; break;
		}
		Assert.AreNotEqual(0, (await Insert(row)).Code, kind);
		Assert.AreEqual(1, Count<MasterPointCampaign>());
		// バルク経路でも同じ検査が掛かり、前の正常行も巻き戻る
		var ok = Camp("OK", EnumPointCampaignPriority.Shop);
		Assert.AreNotEqual(0, (await Send(new InsertBulkParam(typeof(MasterPointCampaign), Common.SerializeObject(new[] { ok, row })))).Code, kind);
		Assert.AreEqual(1, Count<MasterPointCampaign>());
	}

	[TestMethod]
	public async Task Insert_BoundaryValuesAccepted() {
		var row = Camp("C", EnumPointCampaignPriority.ShohinShop, "20260101", "20261231", rank: 2);
		row.Code = new string('C', 20); row.Name = new string('名', 80); row.Memo = new string('備', 200); row.PointAmountProper = 0; row.PointAmountSale = 0;
		Assert.AreEqual(0, (await Insert(row)).Code);
		var one = Camp("D", EnumPointCampaignPriority.Shop, "20260615", "20260615");
		Assert.AreEqual(0, (await Insert(one)).Code, "開始=終了の1日キャンペーン");
		Assert.AreEqual(2, Count<MasterPointCampaign>());
	}

	[TestMethod]
	public async Task Update_InvalidInputAndStaleVduRejected() {
		var id = SeedCamp(Camp("A", EnumPointCampaignPriority.Shop));
		SeedCamp(Camp("B", EnumPointCampaignPriority.Shop));
		var row = Row<MasterPointCampaign>(id); row.Code = "B";
		Assert.AreNotEqual(0, (await Update(row)).Code, "コード重複");
		row = Row<MasterPointCampaign>(id); row.DayTo = "20270131";
		Assert.AreNotEqual(0, (await Update(row)).Code, "ベース期間外");
		row = Row<MasterPointCampaign>(id); row.Name = "古い"; row.Vdu = 999;
		Assert.AreEqual(CvMsgErrorCode.ConcurrentUpdate, (await Update(row)).Code);
		row = Row<MasterPointCampaign>(id); row.Name = "変更";
		Assert.AreEqual(0, (await Update(row)).Code, "自分自身のコードは重複扱いしない");
		Assert.AreEqual("変更", Row<MasterPointCampaign>(id).Name);
	}

	[TestMethod]
	public async Task PartialUpdate_CampaignRejected() {
		var id = SeedCamp(Camp("A", EnumPointCampaignPriority.Shop));
		Assert.AreNotEqual(0, (await Send(new PartialUpdateParam(typeof(MasterPointCampaign), ["DayTo"], [new(id, 1, ["20271231"])]))).Code);
		Assert.AreEqual("20260430", Row<MasterPointCampaign>(id).DayTo);
	}

	[TestMethod]
	[DataRow(typeof(MasterPointCampaignShop))]
	[DataRow(typeof(MasterPointCampaignShohin))]
	public async Task TargetTables_AllSevenGenericRoutesRejectWithoutMutation(Type type) {
		var campaign = SeedCamp(Camp("A", EnumPointCampaignPriority.ShohinShop));
		BaseDbClass row = type == typeof(MasterPointCampaignShop) ? new MasterPointCampaignShop { Id_PointCampaign = campaign, Id_Tenpo = Shop() } : new MasterPointCampaignShohin { Id_PointCampaign = campaign, Id_Shohin = Item() };
		_db.Insert(row);
		var fresh = (BaseDbClass)Activator.CreateInstance(type)!;
		type.GetProperty("Id_PointCampaign")!.SetValue(fresh, campaign);
		object[] requests = [
			new InsertParam(type, Common.SerializeObject(fresh)), new InsertBulkParam(type, Common.SerializeObject(new[] { fresh })),
			new UpdateParam(type, Common.SerializeObject(row)), new DeleteParam(type, Common.SerializeObject(row)),
			new DeleteByIdParam(type, row.Id, row.Vdu), new DeleteBulkParam(type, [new(row.Id, row.Vdu)]),
			new PartialUpdateParam(type, ["Id_PointCampaign"], [new(row.Id, row.Vdu, ["999"])]),
		];
		foreach (var request in requests) {
			Assert.AreNotEqual(0, (await Send(request)).Code, request.GetType().Name);
			var rows = _db.Fetch(type, "SELECT * FROM " + type.Name);
			Assert.HasCount(1, rows, request.GetType().Name);
			Assert.AreEqual(campaign, type.GetProperty("Id_PointCampaign")!.GetValue(rows.Single()));
		}
	}

	[TestMethod]
	[DataRow("item", "shop")]
	[DataRow("id", "shop")]
	[DataRow("bulk", "shop")]
	[DataRow("item", "shohin")]
	[DataRow("id", "shohin")]
	[DataRow("bulk", "shohin")]
	public async Task Delete_CampaignWithTargetsRejectedOnAllRoutes(string route, string target) {
		var id = SeedCamp(Camp("A", EnumPointCampaignPriority.ShohinShop));
		if (target == "shop") Link(id, shops: [Shop()]); else Link(id, shohins: [Item()]);
		var free = SeedCamp(Camp("FREE", EnumPointCampaignPriority.Shop));
		var row = Row<MasterPointCampaign>(id);
		object request = route switch {
			"item" => new DeleteParam(typeof(MasterPointCampaign), Common.SerializeObject(row)),
			"id" => new DeleteByIdParam(typeof(MasterPointCampaign), id, row.Vdu),
			_ => new DeleteBulkParam(typeof(MasterPointCampaign), [new(free, 1), new(id, row.Vdu)]),
		};
		Assert.AreNotEqual(0, (await Send(request)).Code);
		Assert.AreEqual(2, Count<MasterPointCampaign>(), "一括は対象なし行も巻き戻る");
		Assert.AreEqual(0, (await Send(new DeleteByIdParam(typeof(MasterPointCampaign), free, 1))).Code, "対象なしは削除できる");
		Assert.AreEqual(1, Count<MasterPointCampaign>());
	}

	[TestMethod]
	public async Task PriorityChange_RejectedWhileTargetsRemainAllowedAfterClear() {
		var id = SeedCamp(Camp("A", EnumPointCampaignPriority.Shop));
		var shop = Shop();
		Link(id, shops: [shop]);
		var row = Row<MasterPointCampaign>(id); row.PriorityType = (int)EnumPointCampaignPriority.ShohinShop;
		Assert.AreNotEqual(0, (await Update(row)).Code);
		Assert.AreEqual((int)EnumPointCampaignPriority.Shop, Row<MasterPointCampaign>(id).PriorityType);
		// 対象を解除すれば変更できる
		Result(await Targets(id));
		Assert.IsEmpty(Shops(id));
		row = Row<MasterPointCampaign>(id); row.PriorityType = (int)EnumPointCampaignPriority.AllShops;
		Assert.AreEqual(0, (await Update(row)).Code);
		Assert.AreEqual((int)EnumPointCampaignPriority.AllShops, Row<MasterPointCampaign>(id).PriorityType);
	}

	[TestMethod]
	public async Task Base_DeleteAndShrinkProtectedByCampaign() {
		SeedCamp(Camp("A", EnumPointCampaignPriority.AllShops, "20260401", "20260430"));
		var parent = Row<MasterPointBase>(_base);
		// ランクも子なので、ランクを除いた状態でキャンペーンだけが削除を止めることを確認する
		_db.Execute("DELETE FROM MasterPointRank");
		Assert.AreNotEqual(0, (await Send(new DeleteByIdParam(typeof(MasterPointBase), _base, parent.Vdu))).Code);
		Assert.AreEqual(1, Count<MasterPointBase>());
		var changed = Row<MasterPointBase>(_base); changed.DayTo = "20260415";
		Assert.AreNotEqual(0, (await Update(changed)).Code, "終了側の縮小");
		changed = Row<MasterPointBase>(_base); changed.DayFrom = "20260402";
		Assert.AreNotEqual(0, (await Update(changed)).Code, "開始側の縮小");
		Assert.AreEqual("20261231", Row<MasterPointBase>(_base).DayTo);
		changed = Row<MasterPointBase>(_base); changed.DayFrom = "20260401"; changed.DayTo = "20260430";
		Assert.AreEqual(0, (await Update(changed)).Code, "キャンペーン期間ちょうどまでは縮小できる");
	}
	#endregion

	#region Msg064 対象検証・競合
	[TestMethod]
	[DataRow(EnumPointCampaignPriority.AllShops, true, false)]
	[DataRow(EnumPointCampaignPriority.AllShops, false, true)]
	[DataRow(EnumPointCampaignPriority.Shop, false, true)]
	[DataRow(EnumPointCampaignPriority.ShohinAllShops, true, false)]
	public async Task Targets_PriorityMismatchRejected(EnumPointCampaignPriority priority, bool withShop, bool withShohin) {
		var id = SeedCamp(Camp("A", priority));
		var shop = Shop(); var item = Item();
		var reply = await Targets(id, withShop ? [shop] : null, withShohin ? [item] : null);
		Assert.AreNotEqual(0, reply.Code);
		Assert.AreEqual(0, Count<MasterPointCampaignShop>() + Count<MasterPointCampaignShohin>());
		Assert.AreEqual(1L, Row<MasterPointCampaign>(id).Vdu);
	}

	[TestMethod]
	public async Task Targets_UnknownOrNonShopReferencesRejected() {
		var shopCamp = SeedCamp(Camp("A", EnumPointCampaignPriority.ShohinShop));
		var shop6 = Shop(6); var shop3 = Shop(3); var wholesale = Shop(1); var warehouse = Shop(0); var item = Item();
		Assert.AreNotEqual(0, (await Targets(shopCamp, [shop6, 9999], [item])).Code, "存在しない店舗");
		Assert.AreNotEqual(0, (await Targets(shopCamp, [shop6, wholesale], [item])).Code, "卸先(店種1)");
		Assert.AreNotEqual(0, (await Targets(shopCamp, [warehouse], [item])).Code, "倉庫(店種0)");
		Assert.AreNotEqual(0, (await Targets(shopCamp, [shop6], [item, 9999])).Code, "存在しない商品");
		Assert.AreEqual(0, Count<MasterPointCampaignShop>() + Count<MasterPointCampaignShohin>());
		// 売仕店(店種3)・直営店(店種6)は可。重複Idは1行にまとめる
		Result(await Targets(shopCamp, [shop6, shop3, shop6], [item, item]));
		CollectionAssert.AreEqual(new[] { shop6, shop3 }.Order().ToList(), Shops(shopCamp));
		CollectionAssert.AreEqual(new[] { item }, Shohins(shopCamp));
	}

	[TestMethod]
	public async Task Targets_StaleOrMissingCampaignReturnsConcurrentUpdate() {
		var id = SeedCamp(Camp("A", EnumPointCampaignPriority.Shop));
		var shop = Shop();
		Assert.AreEqual(CvMsgErrorCode.ConcurrentUpdate, (await Targets(id, [shop], vdu: 2)).Code);
		Assert.AreEqual(CvMsgErrorCode.ConcurrentUpdate, (await Targets(id, [shop], preview: true, vdu: 2)).Code);
		Assert.AreEqual(CvMsgErrorCode.ConcurrentUpdate, (await Targets(999, [shop], vdu: 1)).Code);
		Assert.IsEmpty(Shops(id));
		// 正常更新後は旧Vduの再送を拒否する（二重送信の防止）
		var result = Result(await Targets(id, [shop], vdu: 1));
		Assert.AreEqual(result.Vdu, Row<MasterPointCampaign>(id).Vdu);
		Assert.AreNotEqual(1L, result.Vdu);
		Assert.AreEqual(CvMsgErrorCode.ConcurrentUpdate, (await Targets(id, [], vdu: 1)).Code);
		CollectionAssert.AreEqual(new[] { shop }, Shops(id));
	}

	[TestMethod]
	public async Task Preview_ListsConflictsWithoutChangingDatabase() {
		var s1 = Shop(); var s2 = Shop(); var s3 = Shop();
		var other = SeedCamp(Camp("OTHER", EnumPointCampaignPriority.Shop, "20260415", "20260515"));
		Link(other, shops: [s1, s2]);
		var self = SeedCamp(Camp("SELF", EnumPointCampaignPriority.Shop));
		Link(self, shops: [s3]);
		var before = Snapshot();
		var conflicts = await Preview(self, [s2, s3]);
		Assert.AreEqual(before, Snapshot());
		var c = conflicts.Single();
		Assert.AreEqual(other, c.Id_PointCampaign);
		Assert.AreEqual("OTHER", c.Code);
		Assert.AreEqual("キャンペーンOTHER", c.Name);
		Assert.AreEqual("20260415", c.DayFrom);
		Assert.AreEqual("20260515", c.DayTo);
		Assert.AreEqual(PointCampaignDb.TargetShop, c.TargetKind);
		Assert.AreEqual(s2, c.Id_Target);
		Assert.AreEqual(Row<MasterTokui>(s2).Code, c.TargetCode);
		Assert.AreEqual(Row<MasterTokui>(s2).Name, c.TargetName);
	}

	[TestMethod]
	public async Task Apply_RemovesConflictRowsReplacesOwnAndAdvancesBothVdu() {
		var s1 = Shop(); var s2 = Shop(); var s3 = Shop(); var s4 = Shop();
		var other = SeedCamp(Camp("OTHER", EnumPointCampaignPriority.Shop));
		Link(other, shops: [s1, s2]);
		var unrelated = SeedCamp(Camp("UNREL", EnumPointCampaignPriority.Shop, "20260501", "20260531"));
		Link(unrelated, shops: [s2]);
		var self = SeedCamp(Camp("SELF", EnumPointCampaignPriority.Shop));
		Link(self, shops: [s4]);
		var confirmed = await Preview(self, [s2, s3]);
		Assert.HasCount(1, confirmed);
		var result = Result(await Targets(self, [s2, s3], confirmed: confirmed));
		Assert.AreEqual(confirmed.Single(), result.Conflicts.Single());
		CollectionAssert.AreEqual(new[] { s1 }, Shops(other), "相手は重複店舗だけ外れる");
		CollectionAssert.AreEqual(new[] { s2, s3 }.Order().ToList(), Shops(self), "自分は全置換（旧s4は消える）");
		CollectionAssert.AreEqual(new[] { s2 }, Shops(unrelated), "期間が重ならない相手は変えない");
		Assert.AreEqual(result.Vdu, Row<MasterPointCampaign>(self).Vdu);
		Assert.AreEqual(result.Vdu, Row<MasterPointCampaign>(other).Vdu);
		Assert.AreEqual(1L, Row<MasterPointCampaign>(unrelated).Vdu);
		Assert.IsEmpty(await Preview(self, [s2, s3]), "置換後は重複が残らない");
	}

	[TestMethod]
	public async Task Apply_ConfirmedMismatchRejectedAndRolledBack() {
		var s1 = Shop(); var s2 = Shop();
		var other = SeedCamp(Camp("OTHER", EnumPointCampaignPriority.Shop));
		Link(other, shops: [s1]);
		var self = SeedCamp(Camp("SELF", EnumPointCampaignPriority.Shop));
		var confirmed = await Preview(self, [s1, s2]);
		Assert.HasCount(1, confirmed);
		// 未確認(空)のまま更新は拒否
		var before = Snapshot();
		Assert.AreNotEqual(0, (await Targets(self, [s1, s2])).Code);
		Assert.AreEqual(before, Snapshot());
		// 確認後に他端末が別キャンペーンへs2を設定した → 再計算結果が変わるので拒否
		var third = SeedCamp(Camp("THIRD", EnumPointCampaignPriority.Shop));
		Link(third, shops: [s2]);
		before = Snapshot();
		Assert.AreNotEqual(0, (await Targets(self, [s1, s2], confirmed: confirmed)).Code);
		Assert.AreEqual(before, Snapshot());
		// 確認済みに余分な行がある場合も拒否
		var extra = confirmed.Append(confirmed[0] with { Id_Target = s2 }).ToList();
		Unlink(third);
		before = Snapshot();
		Assert.AreNotEqual(0, (await Targets(self, [s1, s2], confirmed: extra)).Code);
		Assert.AreEqual(before, Snapshot());
	}

	private void Unlink(long campaign) => _db.Execute("DELETE FROM MasterPointCampaignShop WHERE Id_PointCampaign=@0", campaign);
	#endregion

	#region 重複判定
	[TestMethod]
	[DataRow("20260301", "20260401", true, DisplayName = "相手終了日=自分開始日は重複")]
	[DataRow("20260430", "20260531", true, DisplayName = "相手開始日=自分終了日は重複")]
	[DataRow("20260301", "20260331", false, DisplayName = "前日終了は非重複")]
	[DataRow("20260501", "20260531", false, DisplayName = "翌日開始は非重複")]
	[DataRow("20260410", "20260420", true, DisplayName = "包含")]
	public async Task Conflict_PeriodBoundary(string from, string to, bool expected) {
		var shop = Shop();
		var other = SeedCamp(Camp("OTHER", EnumPointCampaignPriority.Shop, from, to));
		Link(other, shops: [shop]);
		var self = SeedCamp(Camp("SELF", EnumPointCampaignPriority.Shop, "20260401", "20260430"));
		Assert.AreEqual(expected, (await Preview(self, [shop])).Count == 1);
	}

	[TestMethod]
	[DataRow(0, 0, true)]
	[DataRow(0, 1, true)]
	[DataRow(1, 0, true)]
	[DataRow(1, 1, true)]
	[DataRow(1, 2, false)]
	public async Task Conflict_RankCollision(int selfRank, int otherRank, bool expected) {
		var shop = Shop();
		var other = SeedCamp(Camp("OTHER", EnumPointCampaignPriority.Shop, rank: otherRank));
		Link(other, shops: [shop]);
		var self = SeedCamp(Camp("SELF", EnumPointCampaignPriority.Shop, rank: selfRank));
		Assert.AreEqual(expected, (await Preview(self, [shop])).Count == 1);
	}

	[TestMethod]
	public async Task Conflict_DisabledOrDifferentPriorityExcluded() {
		var shop = Shop(); var item = Item();
		var disabled = SeedCamp(Camp("OFF", EnumPointCampaignPriority.Shop, enabled: 0));
		Link(disabled, shops: [shop]);
		var shohinShop = SeedCamp(Camp("SS", EnumPointCampaignPriority.ShohinShop));
		Link(shohinShop, shops: [shop], shohins: [item]);
		var self = SeedCamp(Camp("SELF", EnumPointCampaignPriority.Shop));
		Assert.IsEmpty(await Preview(self, [shop]), "無効キャンペーンと別優先区分は対象外");
		// 自分が無効なら重複確認しない（相手を消さない）
		var selfOff = SeedCamp(Camp("SELFOFF", EnumPointCampaignPriority.ShohinShop, enabled: 0));
		Result(await Targets(selfOff, [shop], [item]));
		CollectionAssert.AreEqual(new[] { item }, Shohins(shohinShop));
	}

	[TestMethod]
	public async Task Conflict_ShohinAllShopsByProduct() {
		var i1 = Item(); var i2 = Item(); var i3 = Item();
		var other = SeedCamp(Camp("OTHER", EnumPointCampaignPriority.ShohinAllShops));
		Link(other, shohins: [i1, i2]);
		var self = SeedCamp(Camp("SELF", EnumPointCampaignPriority.ShohinAllShops));
		var confirmed = await Preview(self, shohins: [i2, i3]);
		Assert.AreEqual(PointCampaignDb.TargetShohin, confirmed.Single().TargetKind);
		Assert.AreEqual(i2, confirmed.Single().Id_Target);
		Assert.AreEqual(Row<MasterShohin>(i2).Code, confirmed.Single().TargetCode);
		Result(await Targets(self, shohins: [i2, i3], confirmed: confirmed));
		CollectionAssert.AreEqual(new[] { i1 }, Shohins(other));
		CollectionAssert.AreEqual(new[] { i2, i3 }.Order().ToList(), Shohins(self));
	}

	[TestMethod]
	public async Task Conflict_ShohinShopOnlyWhenShopsOverlapAndRemovesProductWholly() {
		var a = Shop(); var b = Shop(); var c = Shop();
		var x = Item(); var y = Item();
		var other = SeedCamp(Camp("OTHER", EnumPointCampaignPriority.ShohinShop));
		Link(other, shops: [a, c], shohins: [x, y]);
		var self = SeedCamp(Camp("SELF", EnumPointCampaignPriority.ShohinShop));
		Assert.IsEmpty(await Preview(self, [b], [x]), "店舗が重ならなければ商品が同じでも非重複");
		Assert.IsEmpty(await Preview(self, [a], [Item()]), "店舗が重なっても商品が違えば非重複");
		var confirmed = await Preview(self, [a, b], [x]);
		Assert.AreEqual(PointCampaignDb.TargetShohin, confirmed.Single().TargetKind);
		Assert.AreEqual(x, confirmed.Single().Id_Target);
		Result(await Targets(self, [a, b], [x], confirmed: confirmed));
		CollectionAssert.AreEqual(new[] { y }, Shohins(other), "商品単位で外れる（相手の店舗cの分も外れる）");
		CollectionAssert.AreEqual(new[] { a, c }.Order().ToList(), Shops(other), "相手の店舗は残る");
	}
	#endregion

	#region マスタ更新による重複
	[TestMethod]
	[DataRow("extend")]
	[DataRow("enable")]
	[DataRow("rank")]
	public async Task MasterUpdate_CreatingConflictRejected(string kind) {
		var shop = Shop();
		var other = SeedCamp(Camp("OTHER", EnumPointCampaignPriority.Shop, "20260501", "20260531", rank: 1));
		Link(other, shops: [shop]);
		var self = SeedCamp(Camp("SELF", EnumPointCampaignPriority.Shop, "20260401", "20260430", rank: kind == "rank" ? 2 : 1, enabled: kind == "enable" ? 0 : 1));
		if (kind == "enable") _db.Execute("UPDATE MasterPointCampaign SET DayTo='20260501' WHERE Id=@0", self);
		Link(self, shops: [shop]);
		var row = Row<MasterPointCampaign>(self);
		switch (kind) {
			case "extend": row.DayTo = "20260501"; break;
			case "enable": row.IsEnabled = 1; break;
			case "rank": row.DayTo = "20260501"; break;
		}
		if (kind == "rank") {
			// ランク2同士は非衝突なので延長できる。全ランク(0)へ変えると衝突
			Assert.AreEqual(0, (await Update(row)).Code);
			row = Row<MasterPointCampaign>(self); row.RankKubun = 0;
		}
		var before = Snapshot();
		Assert.AreNotEqual(0, (await Update(row)).Code);
		Assert.AreEqual(before, Snapshot());
		// 相手側から同じ重複を作る更新も拒否する
		if (kind == "extend") {
			var o = Row<MasterPointCampaign>(other); o.DayFrom = "20260430";
			Assert.AreNotEqual(0, (await Update(o)).Code);
			Assert.AreEqual("20260501", Row<MasterPointCampaign>(other).DayFrom);
		}
	}

	[TestMethod]
	public async Task MasterUpdate_NonOverlappingTargetsAllowed() {
		var s1 = Shop(); var s2 = Shop();
		var other = SeedCamp(Camp("OTHER", EnumPointCampaignPriority.Shop, "20260501", "20260531"));
		Link(other, shops: [s1]);
		var self = SeedCamp(Camp("SELF", EnumPointCampaignPriority.Shop, "20260401", "20260430"));
		Link(self, shops: [s2]);
		var row = Row<MasterPointCampaign>(self); row.DayTo = "20260531";
		Assert.AreEqual(0, (await Update(row)).Code);
		Assert.AreEqual("20260531", Row<MasterPointCampaign>(self).DayTo);
	}
	#endregion
}
