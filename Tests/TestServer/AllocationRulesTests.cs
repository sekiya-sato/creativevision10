using System.Linq;
using CvBase;
using CvDomainLogic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// 配分の制約判定（<see cref="AllocationRules"/>）の単体テスト。DBを使わない純粋関数だけを検証する。
/// 仕様は `Doc/spec/2026-10-03_配分再設計_Step1_共通基盤・確定一本化_詳細設計.md` 3章。
/// </summary>
[TestClass]
public class AllocationRulesTests {
	[TestMethod]
	public void FindShortages_SubtractsOwnReservedFromReserveQty() {
		// 実在庫10・引当10（全て自分）・確定10 → 有効在庫(確定前)10、割れない
		var ok = AllocationRules.FindShortages([new(1, 10, 100, 1000, RealSu: 10, ReserveQty: 10, OwnReserved: 10, CommitSu: 10)]);
		Assert.AreEqual(0, ok.Count);

		// 他の引当が3ある → 有効在庫(確定前)7 に対し確定10 で割れる
		var ng = AllocationRules.FindShortages([new(1, 10, 100, 1000, RealSu: 10, ReserveQty: 13, OwnReserved: 10, CommitSu: 10)]);
		Assert.AreEqual(1, ng.Count);
		Assert.AreEqual(7, ng[0].Yuko);
		Assert.AreEqual(10, ng[0].Shiji);
	}

	[TestMethod]
	public void FindShortages_HatsukaiHasNoOwnReserveButCommitIsChecked() {
		// 仕入配分は引当に入らない(OwnReserved=0)。実在庫5に確定8 → 割れる（P6）
		var ng = AllocationRules.FindShortages([new(1, 10, 100, 1000, RealSu: 5, ReserveQty: 0, OwnReserved: 0, CommitSu: 8)]);
		Assert.AreEqual(1, ng.Count);
		Assert.AreEqual(5, ng[0].Yuko);
	}

	[TestMethod]
	public void FindShortages_ShortageQtyDoesNotUseStock() {
		// 指示10を確定5（欠品5）にすれば実在庫5で足りる
		var ok = AllocationRules.FindShortages([new(1, 10, 100, 1000, RealSu: 5, ReserveQty: 10, OwnReserved: 10, CommitSu: 5)]);
		Assert.AreEqual(0, ok.Count);
	}

	[TestMethod]
	public void FindShortages_ZeroCommitIsNotChecked() {
		// 在庫が無くても確定数0（全量欠品）は検査しない
		var ok = AllocationRules.FindShortages([new(1, 10, 100, 1000, RealSu: 0, ReserveQty: 6, OwnReserved: 6, CommitSu: 0)]);
		Assert.AreEqual(0, ok.Count);
	}

	[TestMethod]
	public void FindShortages_ReturnsOnlyBrokenKeysInKeyOrder() {
		var result = AllocationRules.FindShortages([
			new(2, 10, 100, 1000, RealSu: 0, ReserveQty: 0, OwnReserved: 0, CommitSu: 1),
			new(1, 10, 100, 1000, RealSu: 9, ReserveQty: 0, OwnReserved: 0, CommitSu: 1),
			new(1, 10, 100, 1001, RealSu: 0, ReserveQty: 0, OwnReserved: 0, CommitSu: 1),
		]);
		CollectionAssert.AreEqual(new long[] { 1, 2 }, result.Select(x => x.Id_Soko).ToArray());
		Assert.AreEqual(1001, result[0].Id_Siz);
	}

	[TestMethod]
	public void ValidateNewRow_RejectsObsoleteKubunAndInvalidValues() {
		static TranHaibun Row(int kubun = (int)EnumHaibun.Zaiko, int su = 1, long soko = 1, long tenpo = 2, long shohin = 3) =>
			new() { Kubun = kubun, Su = su, Id_Soko = soko, Id_Tenpo = tenpo, Id_Shohin = shohin };

		foreach (var kubun in new[] { EnumHaibun.Hatsukai, EnumHaibun.Zaiko, EnumHaibun.Juchu, EnumHaibun.Reservation }) {
			Assert.IsNull(AllocationRules.ValidateNewRow(Row(kubun: (int)kubun)), $"{kubun} は作成できる");
		}
		foreach (var kubun in new[] { EnumHaibun.Tokui, EnumHaibun.ShopRequest, EnumHaibun.ZaikoHin, EnumHaibun.IdoShiji }) {
			Assert.IsNotNull(AllocationRules.ValidateNewRow(Row(kubun: (int)kubun)), $"{kubun} は廃止区分（決定 D2）");
		}
		Assert.IsNotNull(AllocationRules.ValidateNewRow(Row(su: 0)));
		Assert.IsNotNull(AllocationRules.ValidateNewRow(Row(soko: 0)));
		Assert.IsNotNull(AllocationRules.ValidateNewRow(Row(tenpo: 0)));
		Assert.IsNotNull(AllocationRules.ValidateNewRow(Row(shohin: 0)));
	}

	[TestMethod]
	public void IsCommittableKubun_ExcludesReservation() {
		Assert.IsTrue(AllocationRules.IsCommittableKubun((int)EnumHaibun.Hatsukai));
		Assert.IsTrue(AllocationRules.IsCommittableKubun((int)EnumHaibun.Zaiko));
		Assert.IsTrue(AllocationRules.IsCommittableKubun((int)EnumHaibun.Juchu));
		Assert.IsFalse(AllocationRules.IsCommittableKubun((int)EnumHaibun.Reservation));
		Assert.IsFalse(AllocationRules.IsCommittableKubun((int)EnumHaibun.Tokui));
	}

	[TestMethod]
	public void IsEditable_RequiresUnsentUnconfirmedAndOpen() {
		Assert.IsTrue(AllocationRules.IsEditable(new TranHaibun()));
		Assert.IsFalse(AllocationRules.IsEditable(new TranHaibun { SendFlg = 1 }));
		Assert.IsFalse(AllocationRules.IsEditable(new TranHaibun { EndFlag = 1 }));
		Assert.IsFalse(AllocationRules.IsEditable(new TranHaibun { KakuteiDay = "20260801" }));
	}

	[TestMethod]
	public void NormalizeNewRow_ResetsStateColumns() {
		var row = new TranHaibun {
			Id = 5, KakuteiDay = "20260801", EndFlag = 1, JitsuSu = 3, ShortSu = 2, RelateNo2 = 9, SendFlg = 2, Su = 5,
		};
		AllocationRules.NormalizeNewRow(row);
		Assert.AreEqual(0, row.Id);
		Assert.AreEqual("", row.KakuteiDay);
		Assert.AreEqual(0, row.EndFlag);
		Assert.AreEqual(0, row.JitsuSu);
		Assert.AreEqual(0, row.ShortSu);
		Assert.AreEqual(0, row.RelateNo2);
		Assert.AreEqual(0, row.SendFlg);
		Assert.AreEqual(5, row.Su, "指示数は変えない");
	}

	[TestMethod]
	public void ClampCommitSu_ClampsToZeroAndShiji() {
		Assert.AreEqual(0, AllocationRules.ClampCommitSu(-1, 10));
		Assert.AreEqual(10, AllocationRules.ClampCommitSu(99, 10));
		Assert.AreEqual(4, AllocationRules.ClampCommitSu(4, 10));
	}

	[TestMethod]
	public void ReservedQty_MatchesReserveFormula() {
		Assert.AreEqual(7, AllocationRules.ReservedQty(new TranHaibun { Kubun = (int)EnumHaibun.Zaiko, Su = 7 }));
		Assert.AreEqual(0, AllocationRules.ReservedQty(new TranHaibun { Kubun = (int)EnumHaibun.Hatsukai, Su = 7 }), "仕入配分は引当外");
		Assert.AreEqual(0, AllocationRules.ReservedQty(new TranHaibun { Kubun = (int)EnumHaibun.Zaiko, Su = 7, EndFlag = 1 }), "完了は引当外");
		Assert.AreEqual(4, AllocationRules.ReservedQty(new TranHaibun { Kubun = (int)EnumHaibun.Zaiko, Su = 7, KakuteiDay = "20260801", JitsuSu = 4 }),
			"確定済み（旧状態）は実数量");
	}
}
