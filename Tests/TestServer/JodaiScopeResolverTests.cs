using System.Collections.Generic;
using System.Linq;
using CvBase;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// 上代一括変更 Step4前半 <see cref="JodaiScopeResolver"/> の単体テスト。
/// <para>
/// 純粋クラスのためDBは不要。仕様は `Doc/spec/2026-09-05_上代一括変更_詳細設計.md` 2.5・2.6・2.8・6.1、
/// および同設計書の曖昧さを解消したタスク指示（優先順位・期間の重なり判定・C1/C2/C5の検出条件）。
/// </para>
/// </summary>
[TestClass]
public class JodaiScopeResolverTests {
	private const string P1From = "20260910";
	private const string P1To = "20260920";
	private const string P2From = "20260921";
	private const string P2To = "20260930";
	private const string P3From = "20261001";
	private const string P3To = "20261010";

	private static MasterTokui Store(long id, string code, string name, long priceGroup = 0, long priceArea = 0, long priceChannel = 0) => new() {
		Id = id,
		Code = code,
		Name = name,
		Id_PriceGroup = priceGroup,
		Id_PriceArea = priceArea,
		Id_PriceChannel = priceChannel,
	};

	private static TranJodaiScope Scope(
		int no,
		string name,
		EnumJodaiRangeType rangeType,
		EnumJodaiIncExc incExc = EnumJodaiIncExc.Include,
		string dayFrom = P1From,
		string dayTo = P1To,
		int groupAxis = 0,
		long idGroup = 0,
		long idTenpo = 0,
		int odr = 0) => new() {
		No = no,
		Name = name,
		RangeType = (int)rangeType,
		IncExc = (int)incExc,
		GroupAxis = groupAxis,
		Id_Group = idGroup,
		Id_Tenpo = idTenpo,
		DayFrom = dayFrom,
		DayTo = dayTo,
		Odr = odr,
	};

	// ============================================================
	// 範囲判定（RangeType 0/1/2）
	// ============================================================

	[TestMethod]
	public void RangeType0_All_AllStoresAdopted() {
		var stores = new[] { Store(1, "T01", "店1"), Store(2, "T02", "店2"), Store(3, "T03", "店3") };
		var scopes = new[] { Scope(1, "全国", EnumJodaiRangeType.All) };

		var result = JodaiScopeResolver.Resolve(stores, scopes);

		Assert.AreEqual(3, result.Jshop.Count);
		CollectionAssert.AreEquivalent(new long[] { 1, 2, 3 }, result.Jshop.Select(s => s.Id_Tenpo).ToList());
		Assert.IsTrue(result.Jshop.All(s => s.No_Scope == 1));
	}

	[TestMethod]
	public void RangeType1_PriceGroupAxis_FiltersByIdPriceGroup() {
		var stores = new[] { Store(1, "T01", "OUTLET店", priceGroup: 10), Store(2, "T02", "通常店") };
		var scopes = new[] { Scope(1, "OUTLET", EnumJodaiRangeType.PriceGroup, groupAxis: (int)EnumJodaiGroupAxis.PriceGroup, idGroup: 10) };

		var result = JodaiScopeResolver.Resolve(stores, scopes);

		Assert.AreEqual(1, result.Jshop.Count);
		Assert.AreEqual(1, result.Jshop[0].Id_Tenpo);
	}

	[TestMethod]
	public void RangeType1_PriceAreaAxis_FiltersByIdPriceArea() {
		var stores = new[] { Store(1, "T01", "関西店", priceArea: 20), Store(2, "T02", "通常店") };
		var scopes = new[] { Scope(1, "関西", EnumJodaiRangeType.PriceGroup, groupAxis: (int)EnumJodaiGroupAxis.PriceArea, idGroup: 20) };

		var result = JodaiScopeResolver.Resolve(stores, scopes);

		Assert.AreEqual(1, result.Jshop.Count);
		Assert.AreEqual(1, result.Jshop[0].Id_Tenpo);
	}

	[TestMethod]
	public void RangeType1_PriceChannelAxis_FiltersByIdPriceChannel() {
		var stores = new[] { Store(1, "T01", "EC店", priceChannel: 30), Store(2, "T02", "通常店") };
		var scopes = new[] { Scope(1, "EC", EnumJodaiRangeType.PriceGroup, groupAxis: (int)EnumJodaiGroupAxis.PriceChannel, idGroup: 30) };

		var result = JodaiScopeResolver.Resolve(stores, scopes);

		Assert.AreEqual(1, result.Jshop.Count);
		Assert.AreEqual(1, result.Jshop[0].Id_Tenpo);
	}

	[TestMethod]
	public void RangeType1_IdGroupZero_MatchesNoStore() {
		// 既存の得意先は全軸0（未設定）。Id_Group=0のScopeが未設定の店舗を巻き込んではいけない（設計書2.4）。
		var stores = new[] { Store(1, "T01", "未設定店") };
		var scopes = new[] { Scope(1, "未設定グループ", EnumJodaiRangeType.PriceGroup, groupAxis: (int)EnumJodaiGroupAxis.PriceGroup, idGroup: 0) };

		var result = JodaiScopeResolver.Resolve(stores, scopes);

		Assert.AreEqual(0, result.Jshop.Count);
	}

	[TestMethod]
	public void RangeType2_Store_MatchesOnlyThatStore() {
		var stores = new[] { Store(1, "T01", "銀座店"), Store(2, "T02", "新宿店") };
		var scopes = new[] { Scope(1, "銀座店除外", EnumJodaiRangeType.Store, idTenpo: 1) };

		var result = JodaiScopeResolver.Resolve(stores, scopes);

		Assert.AreEqual(1, result.Jshop.Count);
		Assert.AreEqual(1, result.Jshop[0].Id_Tenpo);
	}

	// ============================================================
	// 設計書2.5の入力例
	// ============================================================

	[TestMethod]
	public void DesignDocExample_NationalSaleWithExcludeAndOverrides() {
		const long OutletGroup = 100;
		const long EcChannel = 200;

		var ginza = Store(1, "T01", "銀座店");
		var shinjuku = Store(2, "T02", "新宿店");
		var outlet = Store(3, "T03", "OUTLET店", priceGroup: OutletGroup);
		var ec = Store(4, "T04", "EC店", priceChannel: EcChannel);
		var normal = Store(5, "T05", "通常店");
		var stores = new[] { ginza, shinjuku, outlet, ec, normal };

		var scopes = new[] {
			Scope(1, "全国", EnumJodaiRangeType.All, dayFrom: P1From, dayTo: P1To),
			Scope(2, "銀座除外", EnumJodaiRangeType.Store, EnumJodaiIncExc.Exclude, dayFrom: P1From, dayTo: P1To, idTenpo: ginza.Id),
			Scope(3, "新宿除外", EnumJodaiRangeType.Store, EnumJodaiIncExc.Exclude, dayFrom: P1From, dayTo: P1To, idTenpo: shinjuku.Id),
			Scope(4, "OUTLET", EnumJodaiRangeType.PriceGroup, dayFrom: P1From, dayTo: P1To, groupAxis: (int)EnumJodaiGroupAxis.PriceGroup, idGroup: OutletGroup),
			Scope(5, "EC", EnumJodaiRangeType.PriceGroup, dayFrom: P1From, dayTo: P2To, groupAxis: (int)EnumJodaiGroupAxis.PriceChannel, idGroup: EcChannel),
		};

		var result = JodaiScopeResolver.Resolve(stores, scopes);

		var byStore = result.Jshop.ToDictionary(s => s.Id_Tenpo);
		Assert.IsFalse(byStore.ContainsKey(ginza.Id), "銀座店は除外Scopeに負けて全国Scopeが採用されないはず");
		Assert.IsFalse(byStore.ContainsKey(shinjuku.Id), "新宿店は除外Scopeに負けて全国Scopeが採用されないはず");
		Assert.AreEqual(4, byStore[outlet.Id].No_Scope);
		Assert.AreEqual(5, byStore[ec.Id].No_Scope);
		Assert.AreEqual(1, byStore[normal.Id].No_Scope);
		Assert.AreEqual(3, result.Jshop.Count);
	}

	// ============================================================
	// 段階値下げ（Markdown Ladder）
	// ============================================================

	[TestMethod]
	public void MarkdownLadder_NonOverlappingPeriods_SameStoreGetsThreeRows() {
		var stores = new[] { Store(1, "T01", "店1") };
		var scopes = new[] {
			Scope(1, "第1段", EnumJodaiRangeType.All, dayFrom: P1From, dayTo: P1To),
			Scope(2, "第2段", EnumJodaiRangeType.All, dayFrom: P2From, dayTo: P2To),
			Scope(3, "第3段", EnumJodaiRangeType.All, dayFrom: P3From, dayTo: P3To),
		};

		var result = JodaiScopeResolver.Resolve(stores, scopes);

		Assert.AreEqual(3, result.Jshop.Count);
		CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, result.Jshop.Select(s => s.No_Scope).ToList());
		Assert.IsFalse(result.Conflicts.Any(c => c.Kind is EnumJodaiConflictKind.ScopeOverlapSameRange or EnumJodaiConflictKind.ScopeDefinitionOverlap),
			"期間が重ならない段階値下げは競合ではない");
	}

	// ============================================================
	// 優先順位の同点解消（各段を単独で検証）
	// ============================================================

	[TestMethod]
	public void Priority_RangeType_HigherWinsOverLowerWhenOverlapping() {
		var store = Store(1, "T01", "OUTLET店", priceGroup: 10);
		var scopes = new[] {
			Scope(1, "全国", EnumJodaiRangeType.All, dayFrom: P1From, dayTo: P1To, odr: 0),
			Scope(2, "OUTLET", EnumJodaiRangeType.PriceGroup, dayFrom: P1From, dayTo: P1To, groupAxis: (int)EnumJodaiGroupAxis.PriceGroup, idGroup: 10, odr: 0),
		};

		var result = JodaiScopeResolver.Resolve([store], scopes);

		Assert.AreEqual(1, result.Jshop.Count);
		Assert.AreEqual(2, result.Jshop[0].No_Scope);
	}

	[TestMethod]
	public void Priority_IncExc_ExcludeWinsOverIncludeWhenSameRangeType() {
		var store = Store(1, "T01", "銀座店");
		var scopes = new[] {
			Scope(1, "個別対象", EnumJodaiRangeType.Store, EnumJodaiIncExc.Include, dayFrom: P1From, dayTo: P1To, idTenpo: 1, odr: 0),
			Scope(2, "個別除外", EnumJodaiRangeType.Store, EnumJodaiIncExc.Exclude, dayFrom: P1From, dayTo: P1To, idTenpo: 1, odr: 0),
		};

		var result = JodaiScopeResolver.Resolve([store], scopes);

		// Include(Scope#1)は同一RangeTypeのExclude(Scope#2)に負けて採用されない。
		Assert.AreEqual(0, result.Jshop.Count);
	}

	[TestMethod]
	public void Priority_Odr_HigherOdrWinsWhenRangeTypeAndIncExcTie() {
		var store = Store(1, "T01", "店1");
		var scopes = new[] {
			Scope(1, "低優先", EnumJodaiRangeType.All, dayFrom: P1From, dayTo: P1To, odr: 0),
			Scope(2, "高優先", EnumJodaiRangeType.All, dayFrom: P1From, dayTo: P1To, odr: 5),
		};

		var result = JodaiScopeResolver.Resolve([store], scopes);

		Assert.AreEqual(1, result.Jshop.Count);
		Assert.AreEqual(2, result.Jshop[0].No_Scope);
		// RangeType・IncExcが同点でOdrにより決着する場合もC1（同一範囲の重複該当）として報告する。
		Assert.IsTrue(result.Conflicts.Any(c => c.Kind == EnumJodaiConflictKind.ScopeOverlapSameRange));
	}

	[TestMethod]
	public void Priority_ListOrder_LaterScopeWinsWhenAllOtherKeysTie() {
		var store = Store(1, "T01", "店1");
		var scopes = new[] {
			Scope(1, "先", EnumJodaiRangeType.All, dayFrom: P1From, dayTo: P1To, odr: 0),
			Scope(2, "後", EnumJodaiRangeType.All, dayFrom: P1From, dayTo: P1To, odr: 0),
		};

		var result = JodaiScopeResolver.Resolve([store], scopes);

		Assert.AreEqual(1, result.Jshop.Count);
		Assert.AreEqual(2, result.Jshop[0].No_Scope);
	}

	// ============================================================
	// 競合検出（C1・C2・C5）
	// ============================================================

	[TestMethod]
	public void C1_DetectedWhenSameRangeTypeIncludeScopesOverlap() {
		var store = Store(1, "T01", "店1");
		var scopes = new[] {
			Scope(1, "第1", EnumJodaiRangeType.All, dayFrom: P1From, dayTo: P1To),
			Scope(2, "第2", EnumJodaiRangeType.All, dayFrom: P1From, dayTo: P2To),
		};

		var result = JodaiScopeResolver.Resolve([store], scopes);

		Assert.IsTrue(result.Conflicts.Any(c => c.Kind == EnumJodaiConflictKind.ScopeOverlapSameRange && c.Severity == EnumJodaiConflictSeverity.Error));
	}

	[TestMethod]
	public void C2_DetectedWhenSameRangeScopesOverlap_WithoutNeedingStores() {
		var scopes = new[] {
			Scope(1, "第1", EnumJodaiRangeType.All, dayFrom: P1From, dayTo: P1To),
			Scope(2, "第2", EnumJodaiRangeType.All, dayFrom: P1From, dayTo: P2To),
		};

		// 店舗が空でもC2はJscopeだけで検出できる。
		var result = JodaiScopeResolver.Resolve([], scopes);

		Assert.AreEqual(0, result.Jshop.Count);
		Assert.IsTrue(result.Conflicts.Any(c => c.Kind == EnumJodaiConflictKind.ScopeDefinitionOverlap && c.Severity == EnumJodaiConflictSeverity.Error));
	}

	[TestMethod]
	public void C5_DetectedAndAdoptedScopeIsNamedInMessage() {
		var store = Store(1, "T01", "OUTLET店", priceGroup: 10);
		var scopes = new[] {
			Scope(1, "全国", EnumJodaiRangeType.All, dayFrom: P1From, dayTo: P1To),
			Scope(2, "OUTLET", EnumJodaiRangeType.PriceGroup, dayFrom: P1From, dayTo: P1To, groupAxis: (int)EnumJodaiGroupAxis.PriceGroup, idGroup: 10),
		};

		var result = JodaiScopeResolver.Resolve([store], scopes);

		var c5 = result.Conflicts.Single(c => c.Kind == EnumJodaiConflictKind.PriorityResolvedAcrossRangeType);
		Assert.AreEqual(EnumJodaiConflictSeverity.Info, c5.Severity);
		StringAssert.Contains(c5.Message, "OUTLET");
	}

	[TestMethod]
	public void NoConflicts_WhenScopesDoNotOverlapInPeriodOrRange() {
		var stores = new[] { Store(1, "T01", "店1"), Store(2, "T02", "OUTLET店", priceGroup: 10) };
		var scopes = new[] {
			Scope(1, "第1段", EnumJodaiRangeType.All, dayFrom: P1From, dayTo: P1To),
			Scope(2, "第2段", EnumJodaiRangeType.All, dayFrom: P2From, dayTo: P2To),
			Scope(3, "OUTLET", EnumJodaiRangeType.PriceGroup, dayFrom: P3From, dayTo: P3To, groupAxis: (int)EnumJodaiGroupAxis.PriceGroup, idGroup: 10),
		};

		var result = JodaiScopeResolver.Resolve(stores, scopes);

		Assert.AreEqual(0, result.Conflicts.Count);
	}

	// ============================================================
	// 空入力
	// ============================================================

	[TestMethod]
	public void EmptyStores_ReturnsEmptyResult_NoException() {
		var scopes = new[] { Scope(1, "全国", EnumJodaiRangeType.All) };

		var result = JodaiScopeResolver.Resolve([], scopes);

		Assert.AreEqual(0, result.Jshop.Count);
		Assert.AreEqual(0, result.Conflicts.Count);
	}

	[TestMethod]
	public void EmptyScopes_ReturnsEmptyResult_NoException() {
		var stores = new[] { Store(1, "T01", "店1") };

		var result = JodaiScopeResolver.Resolve(stores, []);

		Assert.AreEqual(0, result.Jshop.Count);
		Assert.AreEqual(0, result.Conflicts.Count);
	}

	[TestMethod]
	public void NullStoresAndScopes_ReturnsEmptyResult_NoException() {
		var result = JodaiScopeResolver.Resolve(null, null);

		Assert.AreEqual(0, result.Jshop.Count);
		Assert.AreEqual(0, result.Conflicts.Count);
	}
}
