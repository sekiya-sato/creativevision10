using System.Windows;
using CvBase;
using CvBase.Share;
using CvWpfclient.ViewModels._00System;
using CvWpfclient.ViewModels._31Monthly;
using CvWpfclient.Views._00System;
using CvWpfclient.Views._31Monthly;
using UatVm.Seed;

namespace UatVm.Scenarios;

/// <summary>
/// 原価3か月UAT（2026/11〜2027/01、SQLiteのみ）。
/// 期待値は Doc/test/2026-11-06_原価3か月UAT手順.md 3章の手計算値で、製品の計算ロジックを呼ばずに固定値で持つ。
/// </summary>
public static class Cost3MonthScenario {
	private static Cost3MonthSeeder.Result? _seeded;
	public static void Seeder(string dbPath) => _seeded = Cost3MonthSeeder.Seed(dbPath, Console.WriteLine);

	const string M1 = Cost3MonthSeeder.Month1, M2 = Cost3MonthSeeder.Month2, M3 = Cost3MonthSeeder.Month3;
	static readonly string[] Months = [M1, M2, M3];

	/// <summary>総平均の1行（確認一覧・保存値の共通比較形）。</summary>
	sealed record Avg(string Month, string P, long Before, long OpenQty, long OpenAmt, long PurQty, long PurAmt, long Sundry, long After, bool Target = true) {
		public string Key(bool withTarget) => $"{Month} {P} 前原価={Before} 前在庫={OpenQty} 前在庫金額={OpenAmt} 仕入数={PurQty} 仕入金額={PurAmt} 諸掛={Sundry} 原価={After}" + (withTarget ? $" 対象={Target}" : "");
	}

	/// <summary>最終仕入の1行。</summary>
	sealed record Last(string Month, string P, long Before, long After, string Day, long Qty, long Amount) {
		public string Key => $"{Month} {P} 前原価={Before} 原価={After} 採用日={Day} 数量={Qty} 金額={Amount}";
	}

	public static async Task RunAsync(VmSession session) {
		var s = _seeded ?? throw new InvalidOperationException("シードが実行されていません。");
		var names = new Dictionary<long, string> { [s.A] = "A", [s.B] = "B", [s.C] = "C", [s.D] = "D", [s.F] = "F", [s.G] = "G" };
		var idsCsv = string.Join(",", names.Keys);
		session.SetDialogResponder(_ => MessageBoxResult.Yes);
		try {
			// ---- 1. 消化仕入（3か月＋再実行） ----
			long[] consumptionRows = [2, 2, 1];
			long[] consumptionTotals = [2_980, 500, 2_000];
			for (var i = 0; i < Months.Length; i++) {
				if (!await ConsumptionAsync(session, Months[i], consumptionRows[i])) return;
			}
			var generated = await ConsumptionTotalsAsync(session, s);
			session.CheckEqual("消化仕入 月別生成額（2026/11・12・2027/01）", string.Join(",", consumptionTotals), string.Join(",", Months.Select(m => generated.Totals.GetValueOrDefault(m))));
			session.Check("消化仕入 生成仕入はIsStock=0", generated.AllNonStock, new { generated.SlipCount });
			if (!await ConsumptionAsync(session, M1, 2, "再実行")) return;
			var regenerated = await ConsumptionTotalsAsync(session, s);
			session.Check("消化仕入 再実行で二重計上しない", regenerated.Totals.GetValueOrDefault(M1) == 2_980 && regenerated.SlipCount == generated.SlipCount,
				new { before = generated.SlipCount, after = regenerated.SlipCount, m1 = regenerated.Totals.GetValueOrDefault(M1) });

			// ---- 2. 在庫のみ再集計 ----
			if (!await RebuildStockAsync(session, "初回")) return;
			await CheckStockAsync(session, idsCsv, names, "初回", new() {
				["A"] = [16, 23, 20], ["B"] = [10, 16, 20], ["C"] = [3, 3, 5], ["D"] = [-5, -2, -2],
			});

			// ---- 3. 諸掛確認 ----
			await SundryAsync(session, s, M1, 300);
			await SundryAsync(session, s, M2, 150);

			// ---- 4〜6. 総平均 11月→12月、評価替え 12月、総平均 1月 ----
			if (!await TotalAverageAsync(session, names, M1, "初回", [
				new(M1, "A", 1000, 10, 10_000, 16, 17_600, 300, 1073),
				new(M1, "B", 2000, 5, 10_000, 5, 11_000, 0, 2100),
				new(M1, "C", 0, 0, 0, 3, 1_000, 0, 333),
			])) return;
			if (!await TotalAverageAsync(session, names, M2, "初回", [
				new(M2, "A", 1073, 16, 17_168, 15, 19_000, 150, 1171),
				new(M2, "B", 2100, 10, 21_000, 10, 19_200, 0, 2010),
				new(M2, "D", 500, -5, -2_500, 3, 1_800, 0, 0, Target: false),
			])) return;
			if (!await RevaluationAsync(session, s)) return;
			if (!await TotalAverageAsync(session, names, M3, "初回", [
				new(M3, "B", 1608, 16, 25_728, 4, 6_800, 0, 1626),
				new(M3, "C", 333, 3, 999, 2, 1_003, 0, 400),
			])) return;
			await CheckSavedAverageAsync(session, idsCsv, names, "初回", [
				new(M1, "A", 1000, 10, 10_000, 16, 17_600, 300, 1073),
				new(M1, "B", 2000, 5, 10_000, 5, 11_000, 0, 2100),
				new(M1, "C", 0, 0, 0, 3, 1_000, 0, 333),
				new(M2, "A", 1073, 16, 17_168, 15, 19_000, 150, 1171),
				new(M2, "B", 2100, 10, 21_000, 10, 19_200, 0, 2010),
				new(M3, "B", 1608, 16, 25_728, 4, 6_800, 0, 1626),
				new(M3, "C", 333, 3, 999, 2, 1_003, 0, 400),
			]);
			await CheckCurrentCostAsync(session, idsCsv, names, "総平均（初回）後", new() { ["A"] = 1171, ["B"] = 1626, ["C"] = 400, ["D"] = 500 });

			// ---- 7. T-04 確認後の伝票追加で更新を中断 ----
			var stale = session.OpenView<TotalAverageCostUpdateView, TotalAverageCostUpdateViewModel>();
			stale.Vm.TargetMonth = "2026/11";
			await stale.RunAsync("T-04 総平均確認 2026/11", vm => vm.ConfirmCommand);
			var added = await session.InsertAsync(Cost3MonthSeeder.NewCascadePurchase(s.A, s.ShiireId));
			session.Note("T-04 確認後に追加した仕入", new { added.Id, added.DenDay });
			session.ClearDialogs();
			await stale.RunAsync("T-04 総平均更新（中断されるべき）", vm => vm.UpdateCommand);
			var a1 = await session.QueryAsync<TranGenka>($"where Id_Shohin = {s.A} AND SumMonth = '{M1}' AND CostMethod = {(int)EnumCostMethod.TotalAverage} AND ChangeKind = {(int)EnumCostChangeKind.Monthly}");
			session.Check("T-04 確認後の変更で更新を中断し、Aの2026/11原価は1073のまま",
				stale.Vm.StatusMessage.StartsWith("更新に失敗しました", StringComparison.Ordinal) && a1.SingleOrDefault()?.AfterCost == 1073,
				new { stale.Vm.StatusMessage, costs = a1.Select(x => x.AfterCost) });
			stale.View.Close();

			// ---- 8. T-05 過去月修正の後続月再計算 ----
			if (!await RebuildStockAsync(session, "伝票追加後")) return;
			await CheckStockAsync(session, idsCsv, names, "伝票追加後", new() {
				["A"] = [22, 29, 26], ["B"] = [10, 16, 20], ["C"] = [3, 3, 5], ["D"] = [-5, -2, -2],
			});
			List<Avg> cascade = [
				new(M1, "A", 1000, 10, 10_000, 22, 23_600, 300, 1059),
				new(M1, "B", 2000, 5, 10_000, 5, 11_000, 0, 2100),
				new(M1, "C", 0, 0, 0, 3, 1_000, 0, 333),
				new(M2, "A", 1059, 22, 23_298, 15, 19_000, 150, 1147),
				new(M2, "B", 2100, 10, 21_000, 10, 19_200, 0, 2010),
				new(M2, "D", 500, -5, -2_500, 3, 1_800, 0, 0, Target: false),
				new(M3, "B", 1608, 16, 25_728, 4, 6_800, 0, 1626),
				new(M3, "C", 333, 3, 999, 2, 1_003, 0, 400),
			];
			if (!await TotalAverageAsync(session, names, M1, "T-05 後続月再計算", cascade, continueOnPreviewMismatch: true)) return;
			await CheckSavedAverageAsync(session, idsCsv, names, "T-05 後続月再計算", [.. cascade.Where(x => x.Target)]);
			var reval = await session.QueryAsync<TranGenka>($"where Id_Shohin = {s.B} AND ChangeKind = {(int)EnumCostChangeKind.Reval}");
			session.Check("T-05 再計算後も評価替え行（B 2026/12 1608）が残る", reval.Count == 1 && reval[0].AfterCost == 1608 && reval[0].SumMonth == M2,
				new { reval = reval.Select(x => new { x.SumMonth, x.AfterCost, x.EffectiveDay }) });
			await CheckCurrentCostAsync(session, idsCsv, names, "T-05 後", new() { ["A"] = 1147, ["B"] = 1626, ["C"] = 400, ["D"] = 500 });

			// ---- 9. 最終仕入原価 ----
			var sysman = (await session.QueryAsync<MasterSysman>("ORDER BY Id LIMIT 1")).Single();
			sysman.CostMethod = (int)EnumCostMethod.LastPurchase;
			await session.UpdateAsync(sysman);
			session.Note("原価方式を最終仕入原価(1)へ切替（システム管理画面の代用としてgRPC汎用更新）");
			var bSlips = await session.QueryAsync<Tran03Shiire>($"where Id_Shiire = {s.ShiireId} AND DenDay = '20261210' AND Kubun = {(int)EnumShiire.Shiire}");
			var bLaterId = bSlips.Max(x => x.Id);
			if (!await LastPurchaseAsync(session, names, M1, [
				new(M1, "A", 1000, 1000, "20261128", 6, 6_000),
				new(M1, "B", 2000, 2200, "20261110", 5, 11_000),
				new(M1, "C", 0, 333, "20261112", 3, 1_000),
			])) return;
			if (!await LastPurchaseAsync(session, names, M2, [
				new(M2, "A", 1000, 1300, "20261220", 5, 6_500),
				new(M2, "B", 2200, 1950, "20261210", 4, 7_800),
				new(M2, "D", 500, 600, "20261215", 3, 1_800),
			])) return;
			if (!await LastPurchaseAsync(session, names, M3, [
				new(M3, "B", 1950, 1700, "20270112", 4, 6_800),
				new(M3, "C", 333, 502, "20270115", 2, 1_003),
			])) return;
			var lpB2 = await session.QueryAsync<TranGenka>($"where Id_Shohin = {s.B} AND SumMonth = '{M2}' AND CostMethod = {(int)EnumCostMethod.LastPurchase}");
			session.Check("最終仕入 同日2伝票は後の伝票Idを採用", lpB2.SingleOrDefault()?.SourceTranId == bLaterId,
				new { expected = bLaterId, actual = lpB2.Select(x => x.SourceTranId), slips = bSlips.Select(x => x.Id) });
			await CheckCurrentCostAsync(session, idsCsv, names, "最終仕入後", new() { ["A"] = 1300, ["B"] = 1700, ["C"] = 502, ["D"] = 600 });
		}
		finally {
			session.SetDialogResponder(null);
		}
	}

	// ---- 消化仕入 ----

	static async Task<bool> ConsumptionAsync(VmSession session, string month, long expectedRows, string label = "") {
		var d = session.OpenView<ConsumptionPurchaseUpdateView, ConsumptionPurchaseUpdateViewModel>();
		d.Vm.TargetMonth = Slash(month);
		await d.RunAsync($"消化仕入確認 {Slash(month)}{label}", vm => vm.ConfirmCommand);
		var ok = session.Check($"消化仕入確認 {Slash(month)}{label} 行数{expectedRows}・エラー0", d.Vm.Rows.Count == expectedRows && d.Vm.ErrorCount == 0,
			new { rows = d.Vm.Rows.Select(r => new { r.CodeShohin, r.SourceDay, r.Su, r.UnitCost, r.Kingaku, r.ErrorMessage }), d.Vm.ErrorCount, d.Vm.StatusMessage });
		if (ok) {
			session.ClearDialogs();
			await d.RunAsync($"消化仕入更新 {Slash(month)}{label}", vm => vm.UpdateCommand);
			ok = CheckCompleted(session, $"消化仕入更新 {Slash(month)}{label} 完了");
		}
		d.View.Close();
		return ok;
	}

	sealed record GeneratedSummary(Dictionary<string, long> Totals, int SlipCount, bool AllNonStock);

	static async Task<GeneratedSummary> ConsumptionTotalsAsync(VmSession session, Cost3MonthSeeder.Result s) {
		var slips = await session.QueryAsync<Tran03Shiire>($"where Id_Shiire = {s.ShiireId} AND GeneratedKind = {(int)EnumGeneratedKind.ConsumptionPurchase}");
		var totals = slips.GroupBy(x => x.DenDay[..6]).ToDictionary(g => g.Key,
			g => g.Sum(h => (h.Jmeisai ?? []).Where(m => m.Id_Shohin == s.F || m.Id_Shohin == s.G).Sum(m => m.Kingaku * h.CalcFlag)));
		return new(totals, slips.Count, slips.All(x => x.IsStock == 0));
	}

	// ---- 在庫 ----

	static async Task<bool> RebuildStockAsync(VmSession session, string label) {
		var d = session.OpenView<StockKakeUpdateView, StockKakeUpdateViewModel>();
		await d.WaitAsync("init:自社締日の取得", vm => !vm.ClosingPeriodText.Contains("読み込んでいます", StringComparison.Ordinal));
		session.ClearDialogs();
		d.Input("再集計条件", vm => { vm.YearMonthFrom = "2026/11"; vm.YearMonthTo = "2027/01"; vm.UpdateTarget = "在庫のみ"; },
			new { from = "2026/11", to = "2027/01", target = "在庫のみ" });
		await d.RunAsync($"在庫のみ再集計（{label}）", vm => vm.ExecuteCommand);
		var problems = session.Dialogs.Where(x => x.Request.Button != MessageBoxButton.YesNo).Select(x => new { x.Request.Kind, x.Request.Message }).ToList();
		var ok = session.Check($"在庫のみ再集計（{label}）が警告・エラーなしで完走", d.Vm.ProgressValue == 100 && d.Vm.StatusMessage.Contains("が完了しました", StringComparison.Ordinal) && problems.Count == 0,
			new { d.Vm.ProgressValue, d.Vm.StatusMessage, problems });
		d.View.Close();
		return ok;
	}

	static async Task CheckStockAsync(VmSession session, string idsCsv, Dictionary<long, string> names, string label, Dictionary<string, long[]> expected) {
		var rows = await session.QueryAsync<SummaryStock>($"where Id_Shohin IN ({idsCsv}) AND SumMonth <= '{M3}'");
		var actual = expected.Keys.ToDictionary(p => p, p => Months.Select(m => rows.Where(r => names[r.Id_Shohin] == p && string.CompareOrdinal(r.SumMonth, m) <= 0).Sum(r => (long)r.Su)).ToArray());
		Compare(session, $"在庫累計（{label}）",
			expected.Select(x => $"{x.Key} {string.Join("/", x.Value)}"),
			actual.Select(x => $"{x.Key} {string.Join("/", x.Value)}"));
	}

	// ---- 諸掛 ----

	static async Task SundryAsync(VmSession session, Cost3MonthSeeder.Result s, string month, long expected) {
		var d = session.OpenView<SundryChargesUpdateView, SundryChargesUpdateViewModel>();
		d.Vm.TargetMonth = Slash(month);
		await d.RunAsync($"諸掛確認 {Slash(month)}", vm => vm.ConfirmCommand);
		var row = d.Vm.SummaryRows.SingleOrDefault(x => x.Id_Shohin == s.A);
		session.Check($"諸掛確認 {Slash(month)} A={expected}・エラー0", row?.SundryAmount == expected && d.Vm.ErrorCount == 0,
			new { rows = d.Vm.SummaryRows.Select(x => new { x.CodeShohin, x.SundryAmount, x.SeverityText, x.ErrorMessage }), d.Vm.ErrorCount });
		d.View.Close();
	}

	// ---- 総平均 ----

	static async Task<bool> TotalAverageAsync(VmSession session, Dictionary<long, string> names, string month, string label, IReadOnlyList<Avg> expectedPreview,
		bool continueOnPreviewMismatch = false) {
		var d = session.OpenView<TotalAverageCostUpdateView, TotalAverageCostUpdateViewModel>();
		d.Vm.TargetMonth = Slash(month);
		await d.RunAsync($"総平均確認 {Slash(month)}（{label}）", vm => vm.ConfirmCommand);
		var actual = d.Vm.Rows.Where(r => names.ContainsKey(r.Id_Shohin))
			.Select(r => new Avg(r.SumMonth, names[r.Id_Shohin], r.BeforeCost, r.OpeningQty, r.OpeningAmount, r.PurchaseQty, r.PurchaseAmount, r.SundryAmount, r.AfterCost, r.IsTarget).Key(true));
		var previewOk = Compare(session, $"総平均確認一覧 {Slash(month)}（{label}）", expectedPreview.Select(x => x.Key(true)), actual);
		var others = d.Vm.Rows.Count(r => !names.ContainsKey(r.Id_Shohin));
		session.Check($"総平均確認 {Slash(month)}（{label}）エラー0・試験外商品なし", d.Vm.ErrorCount == 0 && others == 0, new { d.Vm.ErrorCount, others, d.Vm.StatusMessage });
		if (!previewOk && !continueOnPreviewMismatch) { d.View.Close(); return false; }
		session.ClearDialogs();
		await d.RunAsync($"総平均更新 {Slash(month)}（{label}）", vm => vm.UpdateCommand);
		var ok = CheckCompleted(session, $"総平均更新 {Slash(month)}（{label}）完了");
		d.View.Close();
		return ok;
	}

	static async Task CheckSavedAverageAsync(VmSession session, string idsCsv, Dictionary<long, string> names, string label, IReadOnlyList<Avg> expected) {
		var rows = await session.QueryAsync<TranGenka>($"where Id_Shohin IN ({idsCsv}) AND CostMethod = {(int)EnumCostMethod.TotalAverage} AND ChangeKind = {(int)EnumCostChangeKind.Monthly}");
		Compare(session, $"総平均 保存値 TranGenka（{label}）", expected.Select(x => x.Key(false)),
			rows.Select(r => new Avg(r.SumMonth, names[r.Id_Shohin], r.BeforeCost, r.OpeningQty, r.OpeningAmount, r.PurchaseQty, r.PurchaseAmount, r.SundryAmount, r.AfterCost).Key(false)));
		var dup = rows.GroupBy(r => (r.Id_Shohin, r.SumMonth)).Where(g => g.Count() > 1).Select(g => $"{names[g.Key.Id_Shohin]} {g.Key.SumMonth}").ToList();
		session.Check($"総平均 保存値（{label}）商品×月の重複なし", dup.Count == 0, new { dup });
	}

	static async Task CheckCurrentCostAsync(VmSession session, string idsCsv, Dictionary<long, string> names, string label, Dictionary<string, long> expected) {
		var rows = await session.QueryAsync<MasterShohin>($"where Id IN ({idsCsv})");
		Compare(session, $"現在原価 TankaGenka（{label}）", expected.Select(x => $"{x.Key} {x.Value}"),
			rows.Where(r => expected.ContainsKey(names[r.Id])).Select(r => $"{names[r.Id]} {r.TankaGenka}"));
	}

	// ---- 評価替え ----

	static async Task<bool> RevaluationAsync(VmSession session, Cost3MonthSeeder.Result s) {
		var d = session.OpenView<CostRevaluationView, CostRevaluationViewModel>();
		d.Input("評価替え条件", vm => {
			vm.TargetMonth = "2026/12";
			vm.ApplyPoint = EnumCostRevalApplyPoint.MonthEnd;
			vm.Method = EnumCostRevaluationMethod.ByRate;
			vm.RatePercent = 80;
			vm.RoundingUnit = 1;
			vm.Rounding = EnumRounding.Round;
			vm.CondRows.Add(new CostRevalCondRowVm {
				Field = CostRevaluationViewModel.CondFieldOptionsStatic.Single(x => x.Value == EnumCostRevalCondField.ShohinCode),
				CodeFrom = Cost3MonthSeeder.CodeB,
				CodeTo = Cost3MonthSeeder.CodeB,
			});
		}, new { month = "2026/12", rate = 80, code = Cost3MonthSeeder.CodeB });
		await d.RunAsync("評価替え確認 2026/12", vm => vm.ConfirmCommand);
		var row = d.Vm.DetailRows.SingleOrDefault(x => x.Id_Shohin == s.B);
		var ok = session.Check("評価替え確認 B 2010→1608・数量16・金額32160→25728",
			d.Vm.TargetCount == 1 && d.Vm.ErrorCount == 0 && row is { BeforeCost: 2010, AfterCost: 1608, Qty: 16, BeforeAmount: 32_160, AfterAmount: 25_728 },
			new { d.Vm.TargetCount, d.Vm.ErrorCount, rows = d.Vm.DetailRows.Select(x => new { x.CodeShohin, x.BeforeCost, x.AfterCost, x.Qty, x.BeforeAmount, x.AfterAmount, x.ReasonText }) });
		if (ok) {
			await d.RunAsync("評価替え更新 2026/12", vm => vm.UpdateCommand);
			var saved = await session.QueryAsync<TranGenka>($"where Id_Shohin = {s.B} AND ChangeKind = {(int)EnumCostChangeKind.Reval}");
			ok = session.Check("評価替え保存 B 2026/12 1608・適用日20261231", saved.Count == 1 && saved[0] is { AfterCost: 1608, SumMonth: M2, EffectiveDay: "20261231" },
				new { saved = saved.Select(x => new { x.SumMonth, x.EffectiveDay, x.BeforeCost, x.AfterCost }), d.Vm.StatusMessage });
		}
		d.View.Close();
		return ok;
	}

	// ---- 最終仕入 ----

	static async Task<bool> LastPurchaseAsync(VmSession session, Dictionary<long, string> names, string month, IReadOnlyList<Last> expected) {
		var d = session.OpenView<LastPurchaseCostRefreshView, LastPurchaseCostRefreshViewModel>();
		d.Vm.TargetMonth = Slash(month);
		await d.RunAsync($"最終仕入確認 {Slash(month)}", vm => vm.ConfirmCommand);
		var previewOk = Compare(session, $"最終仕入確認一覧 {Slash(month)}", expected.Select(x => x.Key),
			d.Vm.Rows.Where(r => names.ContainsKey(r.Id_Shohin)).Select(r => new Last(month, names[r.Id_Shohin], r.BeforeCost, r.AfterCost, r.SourceDay, r.Qty, r.Amount).Key));
		var others = d.Vm.Rows.Count(r => !names.ContainsKey(r.Id_Shohin));
		session.Check($"最終仕入確認 {Slash(month)} エラー0・試験外商品なし", d.Vm.ErrorCount == 0 && others == 0, new { d.Vm.ErrorCount, others, d.Vm.StatusMessage, d.Vm.CostMethodMismatchMessage });
		if (!previewOk) { d.View.Close(); return false; }
		session.ClearDialogs();
		await d.RunAsync($"最終仕入更新 {Slash(month)}", vm => vm.UpdateCommand);
		var ok = CheckCompleted(session, $"最終仕入更新 {Slash(month)} 完了");
		if (ok) {
			var ids = string.Join(",", names.Keys);
			var saved = await session.QueryAsync<TranGenka>($"where Id_Shohin IN ({ids}) AND SumMonth = '{month}' AND CostMethod = {(int)EnumCostMethod.LastPurchase}");
			ok = Compare(session, $"最終仕入 保存値 TranGenka {Slash(month)}", expected.Select(x => $"{x.P} 前原価={x.Before} 原価={x.After} 適用日={x.Day}"),
				saved.Select(r => $"{names[r.Id_Shohin]} 前原価={r.BeforeCost} 原価={r.AfterCost} 適用日={r.EffectiveDay}"));
		}
		d.View.Close();
		return ok;
	}

	// ---- 共通 ----

	static string Slash(string yyyymm) => $"{yyyymm[..4]}/{yyyymm[4..]}";

	/// <summary>
	/// 原価更新画面の完了判定。成功時はViewModelが状態を再取得してStatusMessageを上書きするため、
	/// 完了の情報ダイアログ（「…が完了しました」）が出て、エラー・警告ダイアログが出ていないことで判定する。
	/// </summary>
	static bool CheckCompleted(VmSession session, string name) {
		var dialogs = session.Dialogs.Select(x => new { x.Request.Image, x.Request.Message }).ToList();
		var completed = dialogs.Any(x => x.Image == MessageBoxImage.Information && x.Message.Contains("が完了しました", StringComparison.Ordinal));
		var problems = dialogs.Where(x => x.Image is MessageBoxImage.Error or MessageBoxImage.Warning).ToList();
		return session.Check(name, completed && problems.Count == 0, new { dialogs });
	}

	/// <summary>期待値と実際値を行文字列の集合として比較し、差分（不足・余分）を証跡へ残す。</summary>
	static bool Compare(VmSession session, string name, IEnumerable<string> expected, IEnumerable<string> actual) {
		var e = expected.Order(StringComparer.Ordinal).ToList();
		var a = actual.Order(StringComparer.Ordinal).ToList();
		var missing = e.Except(a).ToList();
		var extra = a.Except(e).ToList();
		return session.Check(name, missing.Count == 0 && extra.Count == 0 && e.Count == a.Count, new { missing, extra, expectedCount = e.Count, actualCount = a.Count });
	}
}
