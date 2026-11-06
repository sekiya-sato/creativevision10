using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using CvWpfclient.ViewModels._00System;
using CvWpfclient.ViewModels._31Monthly;
using CvWpfclient.Views._00System;
using CvWpfclient.Views._31Monthly;
using Microsoft.Data.Sqlite;

namespace UatVm.Scenarios;

/// <summary>
/// 2022年の実データ（旧CV CV52 から移行した開発DBの複製）で原価計算を試算する。
/// 旧CVは固定原価運用で総平均原価の記録が無いため、旧CVとの突合ではなく実データでの妥当性確認として行う。
/// 総平均原価 2022/02〜04 を更新し、最終仕入原価は同じ3か月をプレビューだけする。
/// 確認一覧は CSV（UTF-8 BOM）で複製DBと同じフォルダへ出し、利用者の確認に使う。
/// 検算は製品の計算ロジックを呼ばず、仕入伝票・在庫集計から独立に集計した値と算式で行う。
/// 手順・結果は Doc/test/2026-11-06_原価2022年試算.md。
/// </summary>
public static class Cost2022TrialScenario {
	static readonly string[] Months = ["202202", "202203", "202204"];
	static string? _outDir;

	/// <summary>複製DBの原価方式を総平均にし、出力先を複製DBのフォルダに決める。</summary>
	public static void Seeder(string dbPath) {
		_outDir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
		var cs = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString();
		using var connection = new SqliteConnection(cs);
		connection.Open();
		var db = new ExDatabaseSqlite(connection) { KeepConnectionAlive = true };
		var sys = db.Fetch<MasterSysman>("order by Id").FirstOrDefault() ?? throw new InvalidOperationException("システムマスタがありません。");
		if (sys.ShimeBi != (int)EnumShime.DayLast) throw new InvalidOperationException($"自社締日が末日ではありません（ShimeBi={sys.ShimeBi}）。");
		if (db.FirstOrDefault<int>("SELECT COUNT(*) FROM TranGenka") > 0) throw new InvalidOperationException("TranGenka に既存行があります。開発DBの新しい複製で実行してください。");
		sys.CostMethod = (int)EnumCostMethod.TotalAverage;
		db.Update(sys);
		Console.WriteLine($"原価2022年試算: 原価方式=総平均、出力先={_outDir}");
	}

	public static async Task RunAsync(VmSession session) {
		var outDir = _outDir ?? throw new InvalidOperationException("シードが実行されていません（--sqlite を指定してください）。");
		session.SetDialogResponder(_ => MessageBoxResult.Yes);
		try {
			if (!await RebuildStockAsync(session)) return;
			var summary = new List<string> { "区分,年月,対象,対象外,エラー,原価上昇,原価下落,変化なし" };
			foreach (var month in Months) {
				await SundryAsync(session, month);
				var rows = await TotalAverageAsync(session, month, outDir);
				if (rows is null) return;
				summary.Add(string.Join(",", "総平均", month, rows.Count(r => r.IsTarget), rows.Count(r => !r.IsTarget), rows.Count(r => r.IsError),
					rows.Count(r => r.IsTarget && r.AfterCost > r.BeforeCost), rows.Count(r => r.IsTarget && r.AfterCost < r.BeforeCost), rows.Count(r => r.IsTarget && r.AfterCost == r.BeforeCost)));
			}

			var sysman = (await session.QueryAsync<MasterSysman>("ORDER BY Id LIMIT 1")).Single();
			sysman.CostMethod = (int)EnumCostMethod.LastPurchase;
			await session.UpdateAsync(sysman);
			session.Note("原価方式を最終仕入原価(1)へ切替（プレビューのみ。システム管理画面の代用としてgRPC汎用更新）");
			foreach (var month in Months) {
				var rows = await LastPurchasePreviewAsync(session, month, outDir);
				if (rows is null) return;
				summary.Add(string.Join(",", "最終仕入", month, rows.Count, 0, rows.Count(r => r.IsError),
					rows.Count(r => r.AfterCost > r.BeforeCost), rows.Count(r => r.AfterCost < r.BeforeCost), rows.Count(r => r.AfterCost == r.BeforeCost)));
			}
			WriteCsv(Path.Combine(outDir, "00_summary.csv"), summary);
			session.Note("集計", new { file = Path.Combine(outDir, "00_summary.csv"), summary });
		}
		finally {
			session.SetDialogResponder(null);
		}
	}

	// ---- 在庫 ----

	static async Task<bool> RebuildStockAsync(VmSession session) {
		var d = session.OpenView<StockKakeUpdateView, StockKakeUpdateViewModel>();
		await d.WaitAsync("init:自社締日の取得", vm => !vm.ClosingPeriodText.Contains("読み込んでいます", StringComparison.Ordinal));
		session.ClearDialogs();
		d.Input("再集計条件", vm => { vm.YearMonthFrom = "2022/01"; vm.YearMonthTo = "2022/04"; vm.UpdateTarget = "在庫のみ"; },
			new { from = "2022/01", to = "2022/04", target = "在庫のみ" });
		await d.RunAsync("在庫のみ再集計 2022/01〜04", vm => vm.ExecuteCommand);
		var problems = session.Dialogs.Where(x => x.Request.Button != MessageBoxButton.YesNo).Select(x => new { x.Request.Kind, x.Request.Message }).ToList();
		var ok = session.Check("在庫のみ再集計が警告・エラーなしで完走", d.Vm.ProgressValue == 100 && d.Vm.StatusMessage.Contains("が完了しました", StringComparison.Ordinal) && problems.Count == 0,
			new { d.Vm.ProgressValue, d.Vm.StatusMessage, problems });
		d.View.Close();
		return ok;
	}

	// ---- 諸掛 ----

	static async Task SundryAsync(VmSession session, string month) {
		var d = session.OpenView<SundryChargesUpdateView, SundryChargesUpdateViewModel>();
		d.Vm.TargetMonth = Slash(month);
		await d.RunAsync($"諸掛確認 {Slash(month)}", vm => vm.ConfirmCommand);
		session.Check($"諸掛確認 {Slash(month)} エラー0", d.Vm.ErrorCount == 0, new {
			d.Vm.ErrorCount, d.Vm.WarningCount, info = d.Vm.InfoMessages,
			rows = d.Vm.SummaryRows.Select(x => new { x.CodeShohin, x.SundryAmount, x.PurchaseQty, x.OpeningQty, x.SeverityText, x.ErrorMessage }),
		});
		d.View.Close();
	}

	// ---- 総平均 ----

	static async Task<List<TotalAveragePreviewRowVm>?> TotalAverageAsync(VmSession session, string month, string outDir) {
		var d = session.OpenView<TotalAverageCostUpdateView, TotalAverageCostUpdateViewModel>();
		d.Vm.TargetMonth = Slash(month);
		await d.RunAsync($"総平均確認 {Slash(month)}", vm => vm.ConfirmCommand);
		var rows = d.Vm.Rows.Where(r => r.SumMonth == month).ToList();
		WriteCsv(Path.Combine(outDir, $"10_総平均_{month}.csv"), [
			"商品CD,商品名,前原価,前月在庫数,前月在庫金額,当月仕入数,当月仕入金額,諸掛,計算後原価,差額,変化率%,対象,対象外理由,エラー",
			.. rows.OrderBy(r => r.CodeShohin, StringComparer.Ordinal).Select(r => Csv(r.CodeShohin, r.MeiShohin, r.BeforeCost, r.OpeningQty, r.OpeningAmount, r.PurchaseQty,
				r.PurchaseAmount, r.SundryAmount, r.AfterCost, r.IsTarget ? r.AfterCost - r.BeforeCost : 0, Rate(r.BeforeCost, r.IsTarget ? r.AfterCost : r.BeforeCost),
				r.IsTarget ? "対象" : "対象外", r.ExcludeReason, r.ErrorMessage)),
		]);
		var ok = session.Check($"総平均確認 {Slash(month)} エラー0", d.Vm.ErrorCount == 0 && rows.Count > 0,
			new { rows = rows.Count, d.Vm.TargetCount, d.Vm.ExcludedCount, d.Vm.ErrorCount, d.Vm.StatusMessage, errors = rows.Where(r => r.IsError).Select(r => new { r.CodeShohin, r.ErrorMessage }) });
		await CheckAverageInputsAsync(session, month, rows);
		if (!ok) { d.View.Close(); return null; }

		session.ClearDialogs();
		await d.RunAsync($"総平均更新 {Slash(month)}", vm => vm.UpdateCommand);
		ok = CheckCompleted(session, $"総平均更新 {Slash(month)} 完了");
		d.View.Close();
		if (!ok) return null;

		// 保存値（TranGenka）と確認一覧の対象行が一致すること
		var saved = await session.QueryAsync<TranGenka>($"where SumMonth = '{month}' AND CostMethod = {(int)EnumCostMethod.TotalAverage} AND ChangeKind = {(int)EnumCostChangeKind.Monthly}");
		Compare(session, $"総平均 保存値＝確認一覧 {Slash(month)}",
			rows.Where(r => r.IsTarget).Select(r => $"{r.Id_Shohin} {r.BeforeCost} {r.OpeningQty} {r.OpeningAmount} {r.PurchaseQty} {r.PurchaseAmount} {r.SundryAmount} {r.AfterCost}"),
			saved.Select(g => $"{g.Id_Shohin} {g.BeforeCost} {g.OpeningQty} {g.OpeningAmount} {g.PurchaseQty} {g.PurchaseAmount} {g.SundryAmount} {g.AfterCost}"));
		return rows;
	}

	/// <summary>
	/// 確認一覧の入力値を独立に検算する。当月仕入は仕入伝票（IsStock=1・IsPay=1・区分10/20、返品は CalcFlag で負）を、
	/// 前月在庫は在庫集計の対象月より前の合計を、諸掛は生地付属仕入（区分10/20）を、それぞれ C# で集計し直す。
	/// 結果の原価は算式 floor((前月在庫数×前原価＋仕入金額＋諸掛)／(前月在庫数＋仕入数)) で確かめる。
	/// </summary>
	static async Task CheckAverageInputsAsync(VmSession session, string month, List<TotalAveragePreviewRowVm> rows) {
		var (from, to) = Period(month);
		var slips = await session.QueryAsync<Tran03Shiire>($"where DenDay BETWEEN '{from}' AND '{to}' AND IsStock = 1 AND IsPay = 1 AND Kubun IN (10, 20)");
		var purchase = slips.SelectMany(h => (h.Jmeisai ?? []).Select(m => (m.Id_Shohin, Su: (long)m.Su * h.CalcFlag, Kin: m.Kingaku * h.CalcFlag)))
			.GroupBy(x => x.Id_Shohin).ToDictionary(g => g.Key, g => (Su: g.Sum(x => x.Su), Kin: g.Sum(x => x.Kin)));
		var materials = await session.QueryAsync<Tran02Material>($"where DenDay BETWEEN '{from}' AND '{to}' AND Kubun IN (10, 20)");
		var sundry = materials.SelectMany(h => (h.Jmeisai ?? []).Where(m => m.Id_Shohin > 0).Select(m => (m.Id_Shohin, Kin: m.Kingaku * h.CalcFlag)))
			.GroupBy(x => x.Id_Shohin).ToDictionary(g => g.Key, g => g.Sum(x => x.Kin));
		var ids = rows.Select(r => r.Id_Shohin).Distinct().ToList();
		var opening = new Dictionary<long, long>();
		if (ids.Count > 0) {
			var stock = await session.QueryAsync<SummaryStock>($"where Id_Shohin IN ({string.Join(",", ids)}) AND SumMonth < '{month}'");
			opening = stock.GroupBy(s => s.Id_Shohin).ToDictionary(g => g.Key, g => g.Sum(s => (long)s.Su));
		}

		// 対象商品の集合: 当月に在庫加算仕入（区分10/20）がある在庫管理商品
		var purchasedIds = purchase.Keys.Where(k => k > 0).ToList();
		var shohin = purchasedIds.Count == 0 ? [] : await session.QueryAsync<MasterShohin>($"where Id IN ({string.Join(",", purchasedIds)})");
		var expectedIds = shohin.Where(s => s.IsZaiko == 1).Select(s => s.Id).Order().ToList();
		session.CheckEqual($"総平均 対象商品の集合（独立集計） {Slash(month)}", string.Join(",", expectedIds), string.Join(",", ids.Order()));

		var mismatches = new List<string>();
		foreach (var r in rows) {
			var p = purchase.GetValueOrDefault(r.Id_Shohin);
			var s = sundry.GetValueOrDefault(r.Id_Shohin);
			var o = opening.GetValueOrDefault(r.Id_Shohin);
			if (p.Su != r.PurchaseQty || p.Kin != r.PurchaseAmount || s != r.SundryAmount || o != r.OpeningQty || r.OpeningAmount != r.OpeningQty * r.BeforeCost) {
				mismatches.Add($"{r.CodeShohin} 仕入数 {p.Su}/{r.PurchaseQty} 仕入金額 {p.Kin}/{r.PurchaseAmount} 諸掛 {s}/{r.SundryAmount} 前月在庫 {o}/{r.OpeningQty} 前在庫金額 {r.OpeningQty * r.BeforeCost}/{r.OpeningAmount}");
				continue;
			}
			var excluded = r.OpeningQty < 0 || (r.OpeningQty > 0 && r.BeforeCost <= 0);
			if (excluded != !r.IsTarget) {
				mismatches.Add($"{r.CodeShohin} 対象外判定 期待={excluded} 実際={!r.IsTarget}");
				continue;
			}
			if (excluded) continue;
			var den = r.OpeningQty + r.PurchaseQty;
			var num = r.OpeningAmount + r.PurchaseAmount + r.SundryAmount;
			var expected = den > 0 && num > 0 ? (long)Math.Floor((decimal)num / den) : -1;
			if (expected != r.AfterCost) mismatches.Add($"{r.CodeShohin} 原価 期待={expected}（{num}/{den}） 実際={r.AfterCost}");
		}
		session.Check($"総平均 確認一覧の検算（入力の独立集計・算式） {Slash(month)}", mismatches.Count == 0, new { rows = rows.Count, mismatches = mismatches.Take(30) });
	}

	// ---- 最終仕入（プレビューのみ） ----

	static async Task<List<LastPurchasePreviewRowVm>?> LastPurchasePreviewAsync(VmSession session, string month, string outDir) {
		var d = session.OpenView<LastPurchaseCostRefreshView, LastPurchaseCostRefreshViewModel>();
		d.Vm.TargetMonth = Slash(month);
		await d.RunAsync($"最終仕入確認 {Slash(month)}", vm => vm.ConfirmCommand);
		var rows = d.Vm.Rows.ToList();
		d.View.Close();
		WriteCsv(Path.Combine(outDir, $"20_最終仕入_{month}.csv"), [
			"商品CD,商品名,前原価,採用日,数量,金額,計算後原価,差額,変化率%,エラー",
			.. rows.OrderBy(r => r.CodeShohin, StringComparer.Ordinal).Select(r => Csv(r.CodeShohin, r.MeiShohin, r.BeforeCost, r.SourceDay, r.Qty, r.Amount, r.AfterCost,
				r.AfterCost - r.BeforeCost, Rate(r.BeforeCost, r.AfterCost), r.ErrorMessage)),
		]);

		// 独立検算: 区分10・在庫加算・数量>0・金額>0 の明細のうち、日付→伝票Id→行No が最大の明細を採用し、金額/数量を四捨五入（0.5は0から遠い方）
		// 金額0円以下の明細（打ち消し伝票等）は採用しない（2026-11-06 利用者判断）
		var (from, to) = Period(month);
		var slips = await session.QueryAsync<Tran03Shiire>($"where DenDay BETWEEN '{from}' AND '{to}' AND IsStock = 1 AND Kubun = 10");
		var lines = slips.SelectMany(h => (h.Jmeisai ?? []).Where(m => m.Su > 0 && m.Kingaku > 0 && m.Id_Shohin > 0).Select(m => (h.DenDay, h.Id, m.No, m.Id_Shohin, m.Su, m.Kingaku))).ToList();
		var zaiko = lines.Count == 0 ? [] : (await session.QueryAsync<MasterShohin>($"where Id IN ({string.Join(",", lines.Select(l => l.Id_Shohin).Distinct())}) AND IsZaiko = 1")).Select(s => s.Id).ToHashSet();
		var expected = lines.Where(l => zaiko.Contains(l.Id_Shohin)).GroupBy(l => l.Id_Shohin).Select(g => {
			var best = g.OrderByDescending(l => l.DenDay, StringComparer.Ordinal).ThenByDescending(l => l.Id).ThenByDescending(l => l.No).First();
			var cost = (long)Math.Round((decimal)best.Kingaku / best.Su, 0, MidpointRounding.AwayFromZero);
			return $"{g.Key} {best.DenDay} {best.Su} {best.Kingaku} {cost}";
		});
		Compare(session, $"最終仕入 確認一覧の検算（採用明細・四捨五入） {Slash(month)}", expected,
			rows.Select(r => $"{r.Id_Shohin} {r.SourceDay} {r.Qty} {r.Amount} {r.AfterCost}"));
		session.Check($"最終仕入確認 {Slash(month)} エラー0", d.Vm.ErrorCount == 0 && string.IsNullOrEmpty(d.Vm.CostMethodMismatchMessage),
			new { rows = rows.Count, d.Vm.ErrorCount, d.Vm.CostMethodMismatchMessage, errors = rows.Where(r => r.IsError).Select(r => new { r.CodeShohin, r.ErrorMessage }) });
		return rows;
	}

	// ---- 共通 ----

	static string Slash(string yyyymm) => $"{yyyymm[..4]}/{yyyymm[4..]}";

	/// <summary>末日締めの暦月（Seederで ShimeBi=99 を確認済み）。</summary>
	static (string From, string To) Period(string yyyymm) {
		var first = DateTime.ParseExact(yyyymm + "01", "yyyyMMdd", CultureInfo.InvariantCulture);
		return (first.ToString("yyyyMMdd", CultureInfo.InvariantCulture), first.AddMonths(1).AddDays(-1).ToString("yyyyMMdd", CultureInfo.InvariantCulture));
	}

	static string Rate(long before, long after) => before > 0 ? ((after - before) * 100m / before).ToString("0.0", CultureInfo.InvariantCulture) : "";

	static string Csv(params object?[] values) => string.Join(",", values.Select(v => {
		var s = Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
		return s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : s;
	}));

	static void WriteCsv(string path, IEnumerable<string> lines) =>
		File.WriteAllText(path, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

	static bool CheckCompleted(VmSession session, string name) {
		var dialogs = session.Dialogs.Select(x => new { x.Request.Image, x.Request.Message }).ToList();
		var completed = dialogs.Any(x => x.Image == MessageBoxImage.Information && x.Message.Contains("が完了しました", StringComparison.Ordinal));
		var problems = dialogs.Where(x => x.Image is MessageBoxImage.Error or MessageBoxImage.Warning).ToList();
		return session.Check(name, completed && problems.Count == 0, new { dialogs });
	}

	static bool Compare(VmSession session, string name, IEnumerable<string> expected, IEnumerable<string> actual) {
		var e = expected.Order(StringComparer.Ordinal).ToList();
		var a = actual.Order(StringComparer.Ordinal).ToList();
		var missing = e.Except(a).ToList();
		var extra = a.Except(e).ToList();
		return session.Check(name, missing.Count == 0 && extra.Count == 0 && e.Count == a.Count,
			new { missing = missing.Take(30), extra = extra.Take(30), expectedCount = e.Count, actualCount = a.Count });
	}
}
