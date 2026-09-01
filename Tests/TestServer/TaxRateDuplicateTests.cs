using System.Collections.Generic;
using System.Linq;
using CvBase;
using CvBase.Share;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// <see cref="TaxRateResolver.FindDuplicateTaxRates"/> の税率重複チェック。
/// 仕様は `Doc/spec/2026-09-01_消費税計算単位・端数処理_全体設計.md` の 3.6・3.6.1。
/// `MasterSysKanriMenteViewModel`（システム管理マスタ保守画面、`CvWpfclient`）の保存時バリデーションから
/// 呼ばれる判定ロジックだが、`Tests/TestServer` から `CvWpfclient` を参照できないため
/// `CvBase` 側へ切り出した純粋な static メソッドを直接テストする。
/// </summary>
[TestClass]
public class TaxRateDuplicateTests {

	static MasterSysTax Tax(long id, int taxRate, string dateFrom, int taxNewRate) =>
		new() { Id = id, TaxRate = taxRate, DateFrom = dateFrom, TaxNewRate = taxNewRate };

	[TestMethod]
	public void 重複あり_同一区間で2つのId_Taxが同率なら検出される() {
		// Id=1,2 とも DateFrom が無効(切替なし)で、現行税率がどちらも10%
		var taxes = new List<MasterSysTax> {
			Tax(1, 10, "19010101dummy", 0),
			Tax(2, 10, "", 0),
			Tax(3, 8, "", 0),
		};

		var duplicates = TaxRateResolver.FindDuplicateTaxRates(taxes);

		Assert.AreEqual(1, duplicates.Count);
		Assert.AreEqual(1, duplicates[0].IdTaxA);
		Assert.AreEqual(2, duplicates[0].IdTaxB);
		Assert.AreEqual(10, duplicates[0].RatePercent);
	}

	[TestMethod]
	public void 重複なし_10パーセント8パーセント0パーセントなら検出されない() {
		// DateFromはいずれも無効(切替なし)にして、常に現行税率(TaxRate)だけで判定させる。
		// 10%/8%/0%(未使用枠)はどの組も重複しない
		var taxes = new List<MasterSysTax> {
			Tax(1, 10, "", 0),
			Tax(2, 8, "", 0),
			Tax(3, 0, "", 0),
		};

		var duplicates = TaxRateResolver.FindDuplicateTaxRates(taxes);

		Assert.AreEqual(0, duplicates.Count);
	}

	[TestMethod]
	public void 税率0の重複は複数の未使用枠があっても警告しない() {
		// Id=2,3 はともに未使用枠(0%)。0どうしの重複は制度上問題ない運用なので対象外
		var taxes = new List<MasterSysTax> {
			Tax(1, 10, "", 0),
			Tax(2, 0, "", 0),
			Tax(3, 0, "", 0),
		};

		var duplicates = TaxRateResolver.FindDuplicateTaxRates(taxes);

		Assert.AreEqual(0, duplicates.Count);
	}

	[TestMethod]
	public void DateFromの前後で判定が変わる_切替前は重複し切替後は解消する() {
		// Id=1は2019/10/01に8%→10%へ切替。Id=2は切替なしでずっと8%。
		// 切替前(現行)は両方8%で重複、切替後はId=1が10%になり重複が解消する。
		var taxes = new List<MasterSysTax> {
			Tax(1, 8, "20191001", 10),
			Tax(2, 8, "", 8),
		};

		var duplicates = TaxRateResolver.FindDuplicateTaxRates(taxes);

		Assert.AreEqual(1, duplicates.Count);
		var dup = duplicates[0];
		Assert.AreEqual(1, dup.IdTaxA);
		Assert.AreEqual(2, dup.IdTaxB);
		Assert.AreEqual(8, dup.RatePercent);
		// 重複が起きるのは切替前(現行=null)の区間のみで、切替後(20191001)は含まれない
		CollectionAssert.AreEqual(new List<string?> { null }, dup.EffectiveDates.ToList());
	}

	[TestMethod]
	public void DateFromが未設定不正なら全期間現行税率として判定される() {
		// DateFromが不正(8桁でない)なDefine同士でも、現行税率(TaxRate)で正しく突合される
		var taxes = new List<MasterSysTax> {
			Tax(1, 10, "not-a-date", 99),
			Tax(2, 10, "2019/10/01", 99),
		};

		var duplicates = TaxRateResolver.FindDuplicateTaxRates(taxes);

		Assert.AreEqual(1, duplicates.Count);
		Assert.AreEqual(10, duplicates[0].RatePercent);
	}

	[TestMethod]
	public void 警告メッセージには消費税区分と税率が具体的に含まれる() {
		var taxes = new List<MasterSysTax> {
			Tax(1, 10, "", 0),
			Tax(2, 10, "", 0),
			Tax(3, 8, "", 0),
		};

		var message = TaxRateResolver.BuildDuplicateTaxRateWarning(taxes);

		Assert.IsNotNull(message);
		StringAssert.Contains(message, "消費税区分1");
		StringAssert.Contains(message, "消費税区分2");
		StringAssert.Contains(message, "10%");
	}

	[TestMethod]
	public void 重複が無ければ警告メッセージはnull() {
		var taxes = new List<MasterSysTax> {
			Tax(1, 10, "", 0),
			Tax(2, 8, "", 0),
			Tax(3, 0, "", 0),
		};

		var message = TaxRateResolver.BuildDuplicateTaxRateWarning(taxes);

		Assert.IsNull(message);
	}
}
