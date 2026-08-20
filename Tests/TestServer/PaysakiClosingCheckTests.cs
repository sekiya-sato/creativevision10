using System.Collections.Generic;
using CvBase;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// 親子締日チェック（E7）の純ロジックのテスト。
/// `Doc/spec/2026-08-17_旧cvnet比較_未適用・保留課題.md` E7: 親（請求先／支払先）と子（得意先／仕入先）の
/// 締日が異なる場合は警告を出す（ブロックはしない）。
/// </summary>
[TestClass]
public class PaysakiClosingCheckTests {
	[TestMethod]
	public void FindMismatches_締日が同じ行は対象外() {
		List<PaysakiClosingCheckRow> rows = [
			new() { ChildCode = "T001", ParentCode = "T000", ChildShime = 20, ParentShime = 20 },
		];

		var mismatches = PaysakiClosingCheck.FindMismatches(rows);

		Assert.AreEqual(0, mismatches.Count);
	}

	[TestMethod]
	public void FindMismatches_締日が異なる行のみ抽出する() {
		List<PaysakiClosingCheckRow> rows = [
			new() { ChildCode = "T001", ParentCode = "T000", ChildShime = 20, ParentShime = 20 },
			new() { ChildCode = "T002", ParentCode = "T000", ChildShime = 99, ParentShime = 20 },
		];

		var mismatches = PaysakiClosingCheck.FindMismatches(rows);

		Assert.AreEqual(1, mismatches.Count);
		Assert.AreEqual("T002", mismatches[0].ChildCode);
	}

	[TestMethod]
	public void BuildMismatchWarning_不一致なしなら空文字() {
		var warning = PaysakiClosingCheck.BuildMismatchWarning("請求先", "得意先", []);

		Assert.AreEqual(string.Empty, warning);
	}

	[TestMethod]
	public void BuildMismatchWarning_親子ラベルと再計算案内を含む() {
		List<PaysakiClosingCheckRow> mismatches = [
			new() { ChildCode = "T002", ParentCode = "T000", ChildShime = 99, ParentShime = 20 },
		];

		var warning = PaysakiClosingCheck.BuildMismatchWarning("請求先", "得意先", mismatches);

		StringAssert.Contains(warning, "請求先");
		StringAssert.Contains(warning, "得意先");
		StringAssert.Contains(warning, "T002→T000");
		StringAssert.Contains(warning, PaysakiClosingCheck.MismatchGuidance);
	}

	[TestMethod]
	public void BuildMismatchWarning_6件以上は先頭5件とほかN件を表示する() {
		List<PaysakiClosingCheckRow> mismatches = [];
		for (var i = 0; i < 7; i++) {
			mismatches.Add(new() { ChildCode = $"T{i:000}", ParentCode = "T900", ChildShime = 99, ParentShime = 20 });
		}

		var warning = PaysakiClosingCheck.BuildMismatchWarning("支払先", "仕入先", mismatches);

		StringAssert.Contains(warning, "ほか2件");
		StringAssert.DoesNotMatch(warning, new System.Text.RegularExpressions.Regex("T005|T006"));
	}

	[TestMethod]
	public void BuildAffectedRowCheckSql_編集Idを子または親の条件に埋め込む() {
		var sql = PaysakiClosingCheck.BuildAffectedRowCheckSql(nameof(MasterTokui), 42);

		StringAssert.Contains(sql, "c.Id = 42");
		StringAssert.Contains(sql, "p.Id = 42");
		StringAssert.Contains(sql, "MasterTokui");
	}

	[TestMethod]
	public void BuildRangeCheckSql_指定テーブル名とWhere句を埋め込む() {
		var sql = PaysakiClosingCheck.BuildRangeCheckSql(nameof(MasterShiire), "WHERE c.Shime1 = @0");

		StringAssert.Contains(sql, "MasterShiire");
		StringAssert.Contains(sql, "WHERE c.Shime1 = @0");
	}
}
