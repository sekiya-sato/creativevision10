/*
# description
CostFourItemsCoverageTests は、原価4項目（詳細設計 `Doc/spec/2026-09-05_原価4項目_詳細設計.md` §11.2）の
「漏れ検知」を目的とした2本の方言テストです。

既存の `DdlSnapshotTests` は `DefineDataTable.TableTypes` を総なめにしており、新規3テーブル
（TranGenka / TranConsumptionPurchaseLink / TranGenkaReval）はすでにそのループへ含まれています。
`SqlCorpus.LoadWithLocation` の既定走査対象にも `CvDomainLogic` が含まれるため、
`CostUpdateDb*.cs` のSQLはすでにコーパスへ収集されています。

したがって本ファイルでは重複するスナップショットテストを新設せず、
「将来これらの前提が崩れたら気づける」ための2本だけを置きます。
- 新規3テーブルが `DefineDataTable.TableTypes` から漏れていないこと
- `CvDomainLogic/CostUpdateDb*.cs` 由来のSQLが `SqlCorpus` に1本以上収集されていること

既存テーブルへの列追加（設計書§11.2で明記された対象外）はここでは扱わない。
 */
using CvBase;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace TestSqlDialect;

[TestClass]
public sealed class CostFourItemsCoverageTests {

	[TestMethod]
	public void 原価4項目の新規3テーブルがDefineDataTable_TableTypesに含まれる() {
		// 将来DefineDataTable.TableTypesの定義からこの3型が漏れたら、DdlSnapshotTestsの
		// 「全テーブル」ループが対象を静かに減らすだけで気づけない。ここで名指しして固定する。
		Assert.IsTrue(DefineDataTable.TableTypes.Contains(typeof(TranGenka)),
			$"{nameof(TranGenka)}がDefineDataTable.TableTypesに含まれていません。");
		Assert.IsTrue(DefineDataTable.TableTypes.Contains(typeof(TranConsumptionPurchaseLink)),
			$"{nameof(TranConsumptionPurchaseLink)}がDefineDataTable.TableTypesに含まれていません。");
		Assert.IsTrue(DefineDataTable.TableTypes.Contains(typeof(TranGenkaReval)),
			$"{nameof(TranGenkaReval)}がDefineDataTable.TableTypesに含まれていません。");
	}

	[TestMethod]
	public void CostUpdateDb由来のSQLがSqlCorpusに1本以上収集される() {
		// SqlCorpus.LoadWithLocationの既定走査対象からCvDomainLogicが外れたり、
		// CostUpdateDb*.csがファイル名変更等で対象から漏れたりしたら、方言テストの母集団から
		// 静かに脱落する。それに気づくための最小限の存在確認。
		var literals = SqlCorpus.LoadWithLocation("CvDomainLogic");
		var fromCostUpdateDb = literals.Where(l => l.File.Contains("CostUpdateDb", System.StringComparison.Ordinal)).ToList();

		Assert.IsTrue(fromCostUpdateDb.Count > 0,
			"CvDomainLogic/CostUpdateDb*.cs 由来のSQLがSqlCorpusから収集されませんでした。" +
			"SqlCorpus.LoadWithLocationの走査対象、またはCostUpdateDb*.csのSQL記述方式(verbatim/raw文字列)を確認してください。");
	}
}
