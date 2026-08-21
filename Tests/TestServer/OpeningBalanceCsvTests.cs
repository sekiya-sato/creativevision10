using System.Collections.Generic;
using System.Linq;
using CvBase;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// 期首残高CSV（標準形式）の解析・数値正規化・行生成のテスト。
/// <para>
/// `Doc/spec/2026-08-21_残高登録処理_詳細設計.md` 4.4 / 4.5 の規則を固定する。
/// 特に「繰越の引き継ぎ方が売掛・買掛(Balance列)と請求・支払(TotalIn-TotalSales)で異なる」ため、
/// どちらでも期首残が起点になるよう両方の列が埋まることを検証する。
/// </para>
/// </summary>
[TestClass]
public class OpeningBalanceCsvTests {
	private const string FiscalStart = "20260701";

	// ---- 数値の正規化 ------------------------------------------------------------

	[TestMethod]
	public void TryParseAmount_AcceptsExcelStyleNotation() {
		Assert.IsTrue(OpeningBalanceCsv.TryParseAmount("1,200", out var comma, out _));
		Assert.AreEqual(1200L, comma, "桁区切りカンマを受け入れる");

		Assert.IsTrue(OpeningBalanceCsv.TryParseAmount("１２００", out var wide, out _));
		Assert.AreEqual(1200L, wide, "全角数字を受け入れる");

		Assert.IsTrue(OpeningBalanceCsv.TryParseAmount("¥1,200", out var yen, out _));
		Assert.AreEqual(1200L, yen, "通貨記号を受け入れる");

		Assert.IsTrue(OpeningBalanceCsv.TryParseAmount("(1200)", out var paren, out _));
		Assert.AreEqual(-1200L, paren, "会計表記のかっこは負数");

		Assert.IsTrue(OpeningBalanceCsv.TryParseAmount("-1200", out var minus, out _));
		Assert.AreEqual(-1200L, minus);

		Assert.IsTrue(OpeningBalanceCsv.TryParseAmount("  ", out var blank, out _));
		Assert.AreEqual(0L, blank, "空欄は0扱い");
	}

	[TestMethod]
	public void TryParseAmount_RejectsFractionAndGarbage() {
		Assert.IsFalse(OpeningBalanceCsv.TryParseAmount("1200.5", out _, out var fractionError));
		StringAssert.Contains(fractionError, "円単位の整数");

		Assert.IsFalse(OpeningBalanceCsv.TryParseAmount("未確定", out _, out var garbageError));
		StringAssert.Contains(garbageError, "未確定");
	}

	// ---- 解析 --------------------------------------------------------------------

	[TestMethod]
	public void Parse_ReadsHeaderByNameIgnoringOrderCommentAndBlankLines() {
		var text = string.Join("\r\n", [
			"﻿# CV10 期首残高取込 / 区分=売掛 / 金額は正数=未回収残",
			"期首残高,得意先コード,得意先名",
			"150000,00123,株式会社アルファ",
			"",
			"80000,00124,ベータ商事",
		]);

		var parsed = OpeningBalanceCsv.Parse(text, EnumOpeningBalanceKind.UriKake);

		Assert.IsFalse(parsed.HasError, string.Join(" / ", parsed.Errors.Select(x => x.Detail)));
		Assert.AreEqual(2, parsed.Rows.Count);
		Assert.AreEqual("00123", parsed.Rows[0].Code);
		Assert.AreEqual(150000L, parsed.Rows[0].Amount);
		Assert.AreEqual("株式会社アルファ", parsed.Rows[0].Name);
		Assert.AreEqual("00124", parsed.Rows[1].Code);
	}

	[TestMethod]
	public void Parse_ReportsMissingRequiredColumn() {
		var text = "得意先コード,得意先名\r\n00123,アルファ\r\n";

		var parsed = OpeningBalanceCsv.Parse(text, EnumOpeningBalanceKind.UriKake);

		Assert.IsTrue(parsed.HasError);
		Assert.IsTrue(parsed.Errors.Exists(x => x.Detail.Contains("期首残高")), "必須列の欠落を指摘する");
	}

	[TestMethod]
	public void Parse_WarnsUnknownColumnWithoutBlocking() {
		var text = "得意先コード,期首残高,担当メモ\r\n00123,150000,あとで確認\r\n";

		var parsed = OpeningBalanceCsv.Parse(text, EnumOpeningBalanceKind.UriKake);

		Assert.IsFalse(parsed.HasError, "未知の列は登録を止めない");
		Assert.IsTrue(parsed.Errors.Exists(x => x.IsWarning && x.ColumnName == "担当メモ"));
		Assert.AreEqual(1, parsed.Rows.Count);
	}

	[TestMethod]
	public void Parse_ReadsBreakdownColumns() {
		var text = string.Join("\r\n", [
			"得意先コード,期首残高,売上,返品,値引,消費税,現金入金",
			"00123,90000,100000,0,0,10000,20000",
		]);

		var parsed = OpeningBalanceCsv.Parse(text, EnumOpeningBalanceKind.UriKake);

		Assert.IsFalse(parsed.HasError, string.Join(" / ", parsed.Errors.Select(x => x.Detail)));
		var row = parsed.Rows.Single();
		Assert.AreEqual(100000L, row.Breakdown.Main);
		Assert.AreEqual(10000L, row.Breakdown.Tax);
		Assert.AreEqual(20000L, row.Breakdown.Cash);
		Assert.AreEqual(110000L, row.Breakdown.DebitTotal);
		Assert.AreEqual(20000L, row.Breakdown.CreditTotal);
		Assert.AreEqual(90000L, row.Breakdown.NetAmount);
	}

	// ---- 行生成: 売掛（Balance列で繰越） -----------------------------------------

	[TestMethod]
	public void Build_UriKake_WithoutBreakdown_FillsBalanceAndTotalSalesOnly() {
		var result = BuildUriKake([Row(4, "00123", 150000)], existing: new Dictionary<long, long>());

		Assert.IsFalse(result.HasError, string.Join(" / ", result.Errors.Select(x => x.Detail)));
		var entry = result.Entries.Single();
		Assert.AreEqual(EnumOpeningBalanceStatus.New, entry.Status);
		var record = (SummaryUriKake)entry.Record!;
		Assert.AreEqual("202606", record.DenMonth);
		Assert.AreEqual(-150000L, record.Balance, "内部のBalanceは負=未回収");
		Assert.AreEqual(150000L, record.TotalSales, "請求・支払側と同じ形にするため合計も埋める");
		Assert.AreEqual(0L, record.TotalIn);
		Assert.AreEqual(0L, record.Uriage, "内訳未記入の期首行は内訳を持たない");
		Assert.AreEqual(0L, record.Tax);
	}

	[TestMethod]
	public void Build_UriKake_WithBreakdown_KeepsBreakdownAndDerivesTotals() {
		var breakdown = new OpeningBalanceBreakdown { Main = 100000, Tax = 10000, Cash = 20000 };
		var result = BuildUriKake([Row(4, "00123", 90000, breakdown)], existing: new Dictionary<long, long>());

		Assert.IsFalse(result.HasError, string.Join(" / ", result.Errors.Select(x => x.Detail)));
		var record = (SummaryUriKake)result.Entries.Single().Record!;
		Assert.AreEqual(100000L, record.Uriage);
		Assert.AreEqual(10000L, record.Tax);
		Assert.AreEqual(20000L, record.Cash);
		Assert.AreEqual(110000L, record.TotalSales);
		Assert.AreEqual(20000L, record.TotalIn);
		Assert.AreEqual(-90000L, record.Balance);
	}

	[TestMethod]
	public void Build_RejectsBreakdownThatDoesNotMatchAmount() {
		var breakdown = new OpeningBalanceBreakdown { Main = 100000, Tax = 10000 };
		var result = BuildUriKake([Row(4, "00123", 90000, breakdown)], existing: new Dictionary<long, long>());

		Assert.IsTrue(result.HasError);
		StringAssert.Contains(result.Errors[0].Detail, "一致しません");
		Assert.AreEqual(0, result.Entries.Count);
	}

	// ---- 行生成: 請求（TotalIn-TotalSales で繰越） -------------------------------

	[TestMethod]
	public void Build_UriSei_SeedsCarryForwardThroughTotalDifference() {
		var request = new OpeningBalanceBuildRequest {
			Kind = EnumOpeningBalanceKind.UriSei,
			KeyDate = "20260630",
			DayFrom = "20260601",
			FiscalStartDate = FiscalStart,
			SelectedShime = 99,
			Rows = [Row(4, "00123", 150000)],
			Owners = Owners(new OpeningBalanceOwner(11, "00123", "株式会社アルファ", 99, 1)),
		};

		var result = OpeningBalanceCsv.Build(request);

		Assert.IsFalse(result.HasError, string.Join(" / ", result.Errors.Select(x => x.Detail)));
		var record = (SummaryUriSei)result.Entries.Single().Record!;
		Assert.AreEqual("20260630", record.DenDay);
		Assert.AreEqual("20260601", record.DayFrom);
		Assert.AreEqual("20260630", record.DayTo, "CalcSummaryUriSei の previousBalance は DayTo で絞る");
		Assert.AreEqual(-150000L, record.TotalIn - record.TotalSales,
			"請求の繰越は Balance 列ではなく TotalIn-TotalSales の合計で読まれる");
		Assert.AreEqual(-150000L, record.Balance, "帳票が読む Balance 列も揃えておく");
		Assert.AreEqual(string.Empty, record.SeikyuNo, "期首行に請求書番号は振らない");
	}

	[TestMethod]
	public void Build_KaiShi_SeedsCarryForwardThroughTotalDifference() {
		var request = new OpeningBalanceBuildRequest {
			Kind = EnumOpeningBalanceKind.KaiShi,
			KeyDate = "20260630",
			DayFrom = "20260601",
			FiscalStartDate = FiscalStart,
			SelectedShime = 99,
			Rows = [Row(4, "S001", 70000)],
			Owners = Owners(new OpeningBalanceOwner(21, "S001", "仕入先A", 99, 0)),
		};

		var result = OpeningBalanceCsv.Build(request);

		Assert.IsFalse(result.HasError, string.Join(" / ", result.Errors.Select(x => x.Detail)));
		var record = (SummaryKaiShi)result.Entries.Single().Record!;
		Assert.AreEqual(-70000L, record.TotalOut - record.TotalShiire);
		Assert.AreEqual(-70000L, record.Balance);
	}

	[TestMethod]
	public void Build_UriSei_RejectsClosingDayMismatch() {
		var request = new OpeningBalanceBuildRequest {
			Kind = EnumOpeningBalanceKind.UriSei,
			KeyDate = "20260630",
			DayFrom = "20260601",
			FiscalStartDate = FiscalStart,
			SelectedShime = 99,
			Rows = [Row(4, "00123", 150000)],
			Owners = Owners(new OpeningBalanceOwner(11, "00123", "株式会社アルファ", 20, 1)),
		};

		var result = OpeningBalanceCsv.Build(request);

		Assert.IsTrue(result.HasError);
		StringAssert.Contains(result.Errors[0].Detail, "締日");
		Assert.AreEqual(0, result.Entries.Count);
	}

	// ---- 行状態 ------------------------------------------------------------------

	[TestMethod]
	public void Build_DecidesStatusFromAmountAndExistingRow() {
		var result = BuildUriKake(
			[Row(4, "00123", 150000), Row(5, "00124", 80000), Row(6, "00125", 0), Row(7, "00126", 0)],
			existing: new Dictionary<long, long> { [12] = 75000, [13] = 12000 });

		Assert.IsFalse(result.HasError, string.Join(" / ", result.Errors.Select(x => x.Detail)));
		Assert.AreEqual(EnumOpeningBalanceStatus.New, result.Entries[0].Status);
		Assert.AreEqual(EnumOpeningBalanceStatus.Overwrite, result.Entries[1].Status);
		Assert.AreEqual(EnumOpeningBalanceStatus.Delete, result.Entries[2].Status);
		Assert.AreEqual(EnumOpeningBalanceStatus.Skip, result.Entries[3].Status);

		Assert.AreEqual(1, result.NewCount);
		Assert.AreEqual(1, result.OverwriteCount);
		Assert.AreEqual(1, result.DeleteCount);
		Assert.AreEqual(1, result.SkipCount);
		Assert.AreEqual(2, result.Records.Count, "削除だけの行と対象外の行は登録しない");
		CollectionAssert.AreEquivalent(new long[] { 11, 12, 13 }, result.OwnerIds,
			"洗い替え対象には削除だけの取引先も含める。対象外は含めない");
	}

	[TestMethod]
	public void Build_RejectsDuplicateCodeAndUnknownCode() {
		var result = BuildUriKake([Row(4, "00123", 1000), Row(5, "00123", 2000), Row(6, "09999", 3000)], existing: new Dictionary<long, long>());

		Assert.IsTrue(result.HasError);
		Assert.IsTrue(result.Errors.Exists(x => x.LineNo == 5 && x.Detail.Contains("4行目")), "重複を先出の行番号付きで示す");
		Assert.IsTrue(result.Errors.Exists(x => x.LineNo == 6 && x.Detail.Contains("マスタにありません")));
	}

	[TestMethod]
	public void Build_WarnsWhenTokuiIsNotWholesaleOrRetailShop() {
		var result = BuildUriKake([Row(4, "00900", 1000)], existing: new Dictionary<long, long>());

		Assert.IsFalse(result.HasError, "TenType の警告は登録を止めない");
		Assert.IsTrue(result.Errors.Exists(x => x.IsWarning && x.Detail.Contains("卸先・売仕店ではありません")));
		Assert.AreEqual(EnumOpeningBalanceStatus.New, result.Entries.Single().Status);
	}

	[TestMethod]
	public void Build_WarnsOnNameMismatch() {
		var rows = new List<OpeningBalanceCsvRow> {
			new() { LineNo = 4, Code = "00123", Name = "アルファ", Amount = 1000 },
		};
		var result = BuildUriKake(rows, existing: new Dictionary<long, long>());

		Assert.IsFalse(result.HasError);
		Assert.IsTrue(result.Errors.Exists(x => x.IsWarning && x.ColumnName == "得意先名"));
	}

	// ---- 期首ガード --------------------------------------------------------------

	[TestMethod]
	public void Build_RejectsKeyDateOnOrAfterFiscalStart() {
		var request = new OpeningBalanceBuildRequest {
			Kind = EnumOpeningBalanceKind.UriKake,
			KeyDate = "202607",
			FiscalStartDate = FiscalStart,
			Rows = [Row(4, "00123", 1000)],
			Owners = Owners(new OpeningBalanceOwner(11, "00123", "株式会社アルファ", 99, 1)),
		};

		var result = OpeningBalanceCsv.Build(request);

		Assert.IsTrue(result.HasError);
		StringAssert.Contains(result.Errors[0].Detail, "期首");
		Assert.AreEqual(0, result.Entries.Count);
	}

	[TestMethod]
	public void Build_RejectsUnsetFiscalStartDate() {
		var request = new OpeningBalanceBuildRequest {
			Kind = EnumOpeningBalanceKind.UriKake,
			KeyDate = "202606",
			FiscalStartDate = OpeningBalanceCsv.UnsetFiscalStartDate,
			Rows = [Row(4, "00123", 1000)],
			Owners = Owners(new OpeningBalanceOwner(11, "00123", "株式会社アルファ", 99, 1)),
		};

		var result = OpeningBalanceCsv.Build(request);

		Assert.IsTrue(result.HasError);
		StringAssert.Contains(result.Errors[0].Detail, "期首日が未設定");
	}

	// ---- テンプレート ------------------------------------------------------------

	[TestMethod]
	public void BuildTemplateLines_EmitsCommentHeaderAndRows() {
		var lines = OpeningBalanceCsv.BuildTemplateLines(
			EnumOpeningBalanceKind.UriKake, includeBreakdown: false,
			FiscalStart, "202606", 0,
			[new OpeningBalanceTemplateRow("00123", "株式会社アルファ", 99, 0, null, string.Empty),
			 new OpeningBalanceTemplateRow("00124", "ベータ, 商事", 99, 75000, null, string.Empty)]);

		Assert.AreEqual(4, lines.Count);
		StringAssert.StartsWith(lines[0], "# CV10 期首残高取込");
		StringAssert.Contains(lines[0], "期首日=2026/07/01");
		StringAssert.Contains(lines[0], "対象年月=2026/06");
		Assert.AreEqual("得意先コード,得意先名,期首残高", lines[1]);
		Assert.AreEqual("00123,株式会社アルファ,", lines[2], "残高が無い行は空欄で出す");
		Assert.AreEqual("00124,\"ベータ, 商事\",75000", lines[3], "カンマを含む名称は引用符で囲む");
	}

	[TestMethod]
	public void BuildTemplateLines_RoundTripsThroughParse() {
		var lines = OpeningBalanceCsv.BuildTemplateLines(
			EnumOpeningBalanceKind.UriSei, includeBreakdown: true,
			FiscalStart, "20260630", 99,
			[new OpeningBalanceTemplateRow("00123", "株式会社アルファ", 99, 150000,
				new OpeningBalanceBreakdown { Main = 140000, Tax = 10000 }, "20260731")]);

		var parsed = OpeningBalanceCsv.Parse(string.Join("\r\n", lines), EnumOpeningBalanceKind.UriSei);

		Assert.IsFalse(parsed.HasError, string.Join(" / ", parsed.Errors.Select(x => x.Detail)));
		var row = parsed.Rows.Single();
		Assert.AreEqual("00123", row.Code);
		Assert.AreEqual(150000L, row.Amount);
		Assert.AreEqual(140000L, row.Breakdown.Main);
		Assert.AreEqual(10000L, row.Breakdown.Tax);
		Assert.AreEqual("20260731", row.DueDay);
	}

	[TestMethod]
	public void GetColumns_DiffersByKind() {
		var uriSei = OpeningBalanceCsv.GetColumns(EnumOpeningBalanceKind.UriSei, includeBreakdown: true)
			.Select(x => x.Header).ToList();
		CollectionAssert.Contains(uriSei, "その他売上");
		CollectionAssert.Contains(uriSei, "入金予定日");
		CollectionAssert.Contains(uriSei, "締日");

		var kaiKake = OpeningBalanceCsv.GetColumns(EnumOpeningBalanceKind.KaiKake, includeBreakdown: true)
			.Select(x => x.Header).ToList();
		CollectionAssert.Contains(kaiKake, "仕入先コード");
		CollectionAssert.Contains(kaiKake, "現金支払");
		CollectionAssert.DoesNotContain(kaiKake, "その他売上");
		CollectionAssert.DoesNotContain(kaiKake, "締日");
	}

	// ---- 期首行のキー日付 --------------------------------------------------------

	[TestMethod]
	public void GetDefaultKeyDate_ForKakeReturnsMonthBeforeFiscalMonth() {
		var (keyDate, dayFrom) = OpeningBalanceCsv.GetDefaultKeyDate(EnumOpeningBalanceKind.UriKake, "20260701", 0);
		Assert.AreEqual("202606", keyDate);
		Assert.AreEqual(string.Empty, dayFrom);

		// 期首日が月初でなくても、期首年月の前月が対象になる
		Assert.AreEqual("202606", OpeningBalanceCsv.GetDefaultKeyDate(EnumOpeningBalanceKind.UriKake, "20260715", 0).KeyDate);
	}

	[TestMethod]
	public void GetDefaultKeyDate_ForSeiReturnsClosingDayJustBeforeFiscalStart() {
		var monthEnd = OpeningBalanceCsv.GetDefaultKeyDate(EnumOpeningBalanceKind.UriSei, "20260701", 99);
		Assert.AreEqual("20260630", monthEnd.KeyDate);
		Assert.AreEqual("20260601", monthEnd.DayFrom);

		var day20 = OpeningBalanceCsv.GetDefaultKeyDate(EnumOpeningBalanceKind.UriSei, "20260701", 20);
		Assert.AreEqual("20260620", day20.KeyDate, "期首日の直前に来る締日");
		Assert.AreEqual("20260521", day20.DayFrom, "1つ前の締日の翌日");

		var mid = OpeningBalanceCsv.GetDefaultKeyDate(EnumOpeningBalanceKind.KaiShi, "20260715", 20);
		Assert.AreEqual("20260620", mid.KeyDate, "同月の締日(0720)は期首以降なので前月を使う");
	}

	[TestMethod]
	public void GetDefaultKeyDate_IsAlwaysBeforeFiscalStart() {
		foreach (var shime in new[] { 1, 15, 20, 28, 31, 99 }) {
			foreach (var fiscal in new[] { "20260101", "20260301", "20260701", "20261231", "20260215" }) {
				var (keyDate, _) = OpeningBalanceCsv.GetDefaultKeyDate(EnumOpeningBalanceKind.UriSei, fiscal, shime);
				Assert.IsTrue(
					OpeningBalanceCsv.IsBeforeFiscalStart(keyDate, fiscal, OpeningBalanceCsv.GetSpec(EnumOpeningBalanceKind.UriSei)),
					$"締日={shime} 期首={fiscal} で既定キー日付 {keyDate} が期首以降になっている");
			}
		}
	}

	// ---- 取引先照会SQL ----------------------------------------------------------

	[TestMethod]
	public void BuildOwnerQuerySql_AppliesScopeFilters() {
		var all = OpeningBalanceCsv.BuildOwnerQuerySql(EnumOpeningBalanceKind.UriSei, EnumOpeningBalanceOwnerScope.All);
		Assert.IsFalse(all.Contains("TenType IN"), "取込時のコード解決では絞り込まない");
		Assert.IsFalse(all.Contains("t.Shime1 = @3"));
		StringAssert.Contains(all, "FROM MasterTokui AS t");
		StringAssert.Contains(all, "LEFT JOIN SummaryUriSei AS s ON s.Id_Tokui = t.Id AND s.DenDay = @0");

		var scoped = OpeningBalanceCsv.BuildOwnerQuerySql(
			EnumOpeningBalanceKind.UriSei,
			EnumOpeningBalanceOwnerScope.OwnerTypeFilter | EnumOpeningBalanceOwnerScope.ClosingFilter
			| EnumOpeningBalanceOwnerScope.CodeRange | EnumOpeningBalanceOwnerScope.ExistingOnly);
		StringAssert.Contains(scoped, "t.TenType IN (1, 3)");
		StringAssert.Contains(scoped, "t.Shime1 = @3");
		StringAssert.Contains(scoped, "t.Code >= @1");
		StringAssert.Contains(scoped, "s.Id IS NOT NULL");

		var kaiKake = OpeningBalanceCsv.BuildOwnerQuerySql(
			EnumOpeningBalanceKind.KaiKake, EnumOpeningBalanceOwnerScope.OwnerTypeFilter);
		Assert.IsFalse(kaiKake.Contains("t.TenType"),
			"MasterShiire に TenType 列は無いので参照してはいけない");
		StringAssert.Contains(kaiKake, "0 AS TenType");
		StringAssert.Contains(kaiKake, "IFNULL(s.TotalShiire, 0) AS DebitTotal");
		StringAssert.Contains(kaiKake, "IFNULL(s.Shiire, 0) AS Main");
	}

	// ---- ヘルパ ------------------------------------------------------------------

	private static OpeningBalanceCsvRow Row(int lineNo, string code, long amount, OpeningBalanceBreakdown? breakdown = null) =>
		new() {
			LineNo = lineNo,
			Code = code,
			Amount = amount,
			HasBreakdownColumn = breakdown != null,
			Breakdown = breakdown ?? new OpeningBalanceBreakdown(),
		};

	private static Dictionary<string, OpeningBalanceOwner> Owners(params OpeningBalanceOwner[] owners) =>
		owners.ToDictionary(x => x.Code, x => x, System.StringComparer.OrdinalIgnoreCase);

	/// <summary>売掛の既定の取引先集合（00123〜00126 は卸先、00900 は直営店）で Build する。</summary>
	private static OpeningBalanceBuildResult BuildUriKake(
		IReadOnlyList<OpeningBalanceCsvRow> rows, IReadOnlyDictionary<long, long> existing) =>
		OpeningBalanceCsv.Build(new OpeningBalanceBuildRequest {
			Kind = EnumOpeningBalanceKind.UriKake,
			KeyDate = "202606",
			FiscalStartDate = FiscalStart,
			Rows = rows,
			Owners = Owners(
				new OpeningBalanceOwner(11, "00123", "株式会社アルファ", 99, 1),
				new OpeningBalanceOwner(12, "00124", "ベータ商事", 99, 1),
				new OpeningBalanceOwner(13, "00125", "ガンマ物産", 99, 3),
				new OpeningBalanceOwner(14, "00126", "デルタ商会", 99, 1),
				new OpeningBalanceOwner(19, "00900", "直営店E", 99, 6)),
			ExistingAmounts = existing,
		});
}
