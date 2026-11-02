using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels._00System;
using CvWpfclient.ViewModels._05Shiire;
using CvWpfclient.Views._00System;
using CvWpfclient.Views._05Shiire;
using Microsoft.Data.Sqlite;
using UatVm.Seed;
using Supplier = UatVm.Seed.PaymentReportSeeder.Supplier;

namespace UatVm.Scenarios;

/// <summary>
/// 支払系3帳票（買掛金管理表・支払台帳・月別支払予定表）の独立照合。
/// <list type="bullet">
/// <item>「在庫・掛再更新」画面の「買掛のみ」で 2026/04〜07 の買掛・支払残を作る</item>
/// <item>各帳票ViewModelが組んだ印刷SQLを複製DBで実行し、<see cref="PaymentReportSeeder"/> の手計算値から組んだ期待行と照合する</item>
/// <item>同じ条件でPDFを生成して runDirectory へ保存する（見出し・日付・桁数・改ページは画像で目視）。0件はPDFを出さないことを確認する</item>
/// </list>
/// <para>DBを更新するため、必ず開発DBの複製で実行する（<c>--sqlite &lt;複製DB&gt; --manage-server</c>）。</para>
/// </summary>
public static class PaymentReportsScenario {
	const string PdfShownPrefix = "PDFを表示しました: ";
	const string UpdateTarget = "買掛のみ";
	const int MaxDiffs = 20;

	static PaymentReportSeeder.Result? _seeded;

	public static void Seeder(string dbPath) =>
		_seeded = PaymentReportSeeder.Seed(dbPath, message => Console.WriteLine($"[seed] {message}"));

	public static async Task RunAsync(VmSession session) {
		var seeded = _seeded ?? throw new InvalidOperationException("シードが実行されていません（--no-seed は使えません）。");
		var outDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(seeded.DbPath))!, "paymentreports-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
		Directory.CreateDirectory(outDir);
		session.Note("実行条件", new { seeded.DbPath, outDir, suppliers = seeded.Suppliers.Count });

		session.SetDialogResponder(request => request.Button is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel
			? MessageBoxResult.Yes : MessageBoxResult.OK);
		try {
			if (!await RebuildAsync(session)) return;
			CheckSummaries(session, seeded);
			await KaikakeBalanceAsync(session, seeded, outDir);
			await ShiharaiLedgerAsync(session, seeded, outDir);
			await MonthlyYoteiAsync(session, seeded, outDir);
		}
		finally {
			ClosePdfViews();
			session.SetDialogResponder(null);
		}
	}

	// ---- 再集計 ----------------------------------------------------------------

	static async Task<bool> RebuildAsync(VmSession session) {
		var d = session.OpenView<StockKakeUpdateView, StockKakeUpdateViewModel>();
		await d.WaitAsync("init:自社締日の取得", vm => !vm.ClosingPeriodText.Contains("読み込んでいます", StringComparison.Ordinal));
		session.ClearDialogs();
		d.Input("再集計条件", vm => {
			vm.YearMonthFrom = PaymentReportSeeder.MonthFrom;
			vm.YearMonthTo = PaymentReportSeeder.MonthTo;
			vm.UpdateTarget = UpdateTarget;
		}, new { PaymentReportSeeder.MonthFrom, PaymentReportSeeder.MonthTo, UpdateTarget });
		await d.RunAsync($"{UpdateTarget}再集計", vm => vm.ExecuteCommand);
		var problems = session.Dialogs.Where(x => x.Request.Button != MessageBoxButton.YesNo)
			.Select(x => new { x.Request.Kind, x.Request.Message }).ToList();
		var completed = d.Vm.ProgressValue == 100 && d.Vm.StatusMessage.Contains("が完了しました", StringComparison.Ordinal);
		return session.Check($"{UpdateTarget}再集計が警告・エラーなしで完走する", completed && problems.Count == 0,
			new { d.Vm.ProgressValue, d.Vm.StatusMessage, problems });
	}

	/// <summary>帳票の手前で、Summary の締期間ごと・月ごとの値を期待値と突き合わせる（帳票不一致の切り分け用）。</summary>
	static void CheckSummaries(VmSession session, PaymentReportSeeder.Result seeded) {
		var kaiShi = Query(seeded.DbPath, new QueryListSqlParam(typeof(object), $@"
SELECT t.Code, k.DenDay, k.DayFrom, k.DayTo, k.ShiharaiYoteiDay, k.Shiire, k.Henpin, k.Nebiki,
       k.Tax1 + k.Tax2 + k.Tax3 AS Tax, k.Cash, k.Fee, k.TotalShiire, k.TotalOut, k.Balance
FROM SummaryKaiShi k JOIN MasterShiire t ON t.Id = k.Id_Shiire
WHERE t.Code >= @0 AND t.Code <= @1 AND k.DenDay >= '20260401' AND k.DenDay <= '20260731'
ORDER BY t.Code, k.DenDay", [PaymentReportSeeder.CodeFrom, PaymentReportSeeder.CodeTo]));
		var expectedKaiShi = seeded.Suppliers.OrderBy(s => s.Code, StringComparer.Ordinal)
			.SelectMany(s => s.Periods.Select(p => Row(s.Code, p.DayTo, p.DayFrom, p.DayTo, p.ShiharaiYoteiDay,
				p.Shiire, p.Henpin, p.Nebiki, p.Tax, p.Cash, p.Fee, p.TotalShiire, p.TotalOut, p.TotalShiire - p.TotalOut)))
			.ToList();
		Compare(session, "Summary:支払残(SummaryKaiShi)", expectedKaiShi, kaiShi);

		var kaiKake = Query(seeded.DbPath, new QueryListSqlParam(typeof(object), $@"
SELECT t.Code, k.DenMonth, k.Shiire, k.Henpin, k.Nebiki, k.Tax1 + k.Tax2 + k.Tax3 AS Tax, k.Cash, k.Fee,
       k.TotalShiire, k.TotalOut, k.Balance
FROM SummaryKaiKake k JOIN MasterShiire t ON t.Id = k.Id_Shiire
WHERE t.Code >= @0 AND t.Code <= @1 AND k.DenMonth >= '202604' AND k.DenMonth <= '202607'
ORDER BY t.Code, k.DenMonth", [PaymentReportSeeder.CodeFrom, PaymentReportSeeder.CodeTo]));
		// 買掛は動きのある月だけ行ができる。行の有無は帳票側で ifnull するため、動きのある月だけを期待する。
		var expectedKaiKake = seeded.Suppliers.OrderBy(s => s.Code, StringComparer.Ordinal)
			.SelectMany(s => Months.Select(ym => (s, ym, m: MonthOf(s, ym))))
			.Where(x => x.m.HasMovement)
			.Select(x => Row(x.s.Code, x.ym, x.m.Shiire, x.m.Henpin, x.m.Nebiki, x.m.Tax, x.m.Cash, x.m.Fee,
				x.m.TotalShiire, x.m.TotalOut, x.m.TotalShiire - x.m.TotalOut))
			.ToList();
		Compare(session, "Summary:買掛(SummaryKaiKake)", expectedKaiKake, kaiKake);
	}

	// ---- 買掛金管理表 ------------------------------------------------------------

	static readonly string[] Months = ["202604", "202605", "202606", "202607"];

	sealed record MonthFigures(long Shiire, long Henpin, long Nebiki, long Tax, long Cash, long Fee, long Prev, long Balance) {
		public long TotalShiire => Shiire - Henpin - Nebiki + Tax;
		public long TotalOut => Cash + Fee;
		public bool HasMovement => Shiire != 0 || Henpin != 0 || Nebiki != 0 || Tax != 0 || Cash != 0 || Fee != 0;
	}

	/// <summary>自社締日=末日の月で締期間を合算する。前月残は月初より前に終わった締期間の累積、当月残は月内最後の締期間の累積。</summary>
	static MonthFigures MonthOf(Supplier s, string ym) {
		var inMonth = s.Periods.Where(p => p.DayTo[..6] == ym).ToList();
		var prev = s.Periods.Where(p => string.CompareOrdinal(p.DayTo[..6], ym) < 0).OrderBy(p => p.DayTo).LastOrDefault()?.Cumulative ?? 0;
		var balance = inMonth.Count > 0 ? inMonth.OrderBy(p => p.DayTo).Last().Cumulative : prev;
		return new(inMonth.Sum(p => p.Shiire), inMonth.Sum(p => p.Henpin), inMonth.Sum(p => p.Nebiki), inMonth.Sum(p => p.Tax),
			inMonth.Sum(p => p.Cash), inMonth.Sum(p => p.Fee), prev, balance);
	}

	static async Task KaikakeBalanceAsync(VmSession session, PaymentReportSeeder.Result seeded, string outDir) {
		var d = session.OpenView<KaikakeBalanceReportView, KaikakeBalanceReportViewModel>();
		foreach (var (ym, activeOnly) in new[] { ("2026/05", true), ("2026/06", true), ("2026/07", true), ("2026/07", false) }) {
			var name = $"買掛金管理表 {ym} {(activeOnly ? "動きあり" : "全て")}";
			d.Input(name, vm => {
				vm.TargetYearMonth = ym;
				vm.ShiireCodeFrom = PaymentReportSeeder.CodeFrom;
				vm.ShiireCodeTo = PaymentReportSeeder.CodeTo;
				vm.IsActiveOnly = activeOnly;
			});
			var key = ym.Replace("/", string.Empty);
			var expected = seeded.Suppliers.OrderBy(s => s.Code, StringComparer.Ordinal)
				.Select(s => (s, m: MonthOf(s, key)))
				.Where(x => !activeOnly || x.m.Prev != 0 || x.m.TotalShiire != 0 || x.m.TotalOut != 0 || x.m.Balance != 0)
				.Select(x => Row(x.s.Code, x.m.Prev, x.m.TotalShiire, x.m.Tax, x.m.Henpin, x.m.Nebiki, x.m.TotalOut, x.m.Cash, x.m.Fee, x.m.Balance))
				.ToList();
			var actual = await QueryReportAsync(d.Vm, seeded.DbPath,
				"shiireCode", "prevBalance", "totalShiire", "tax", "henpin", "nebiki", "totalOut", "cash", "fee", "balance");
			Compare(session, name, expected, actual);
		}

		// 改ページ（6月は主要5社＋改ページ60社）
		d.Input("買掛金管理表 PDF条件", vm => { vm.TargetYearMonth = "2026/06"; vm.IsActiveOnly = true; });
		await PrintAsync(session, d, outDir, "01_KaikakeBalance_202606");

		// 0件: 取引ゼロの仕入先だけを「動きあり」で出す
		d.Input("買掛金管理表 0件条件", vm => {
			vm.ShiireCodeFrom = "UATVM-PRP-006";
			vm.ShiireCodeTo = "UATVM-PRP-006";
			vm.IsActiveOnly = true;
		});
		await PrintEmptyAsync(session, d, "買掛金管理表 0件");
	}

	// ---- 支払台帳 ------------------------------------------------------------------

	static async Task ShiharaiLedgerAsync(VmSession session, PaymentReportSeeder.Result seeded, string outDir) {
		var d = session.OpenView<ShiharaiLedgerReportView, ShiharaiLedgerReportViewModel>();
		foreach (var activeOnly in new[] { true, false }) {
			var name = $"支払台帳 2026/04/01〜07/31 {(activeOnly ? "動きあり" : "全て")}";
			d.Input(name, vm => {
				vm.PayDayFrom = "2026/04/01";
				vm.PayDayTo = "2026/07/31";
				vm.ShiireCodeFrom = PaymentReportSeeder.CodeFrom;
				vm.ShiireCodeTo = PaymentReportSeeder.CodeTo;
				vm.IsActiveOnly = activeOnly;
			});
			// 支払額または残高（累積）があるもの。並びは支払日（締日）→仕入先コード。
			var expected = seeded.Suppliers
				.SelectMany(s => s.Periods.Select(p => (s, p)))
				.Where(x => !activeOnly || x.p.Cumulative != 0 || x.p.TotalOut != 0)
				.OrderBy(x => x.p.DayTo, StringComparer.Ordinal).ThenBy(x => x.s.Code, StringComparer.Ordinal)
				.Select(x => Row(Label(x.p.DayTo), x.s.Code, $"{Label(x.p.DayFrom)}～{Label(x.p.DayTo)}",
					x.p.TotalShiire, x.p.Tax, x.p.TotalOut, x.p.Cumulative, Label(x.p.ShiharaiYoteiDay)))
				.ToList();
			var actual = await QueryReportAsync(d.Vm, seeded.DbPath,
				"payDayLabel", "shiireCode", "termLabel", "totalShiire", "tax", "totalOut", "balance", "shiharaiYoteiLabel");
			Compare(session, name, expected, actual);
		}

		d.Input("支払台帳 PDF条件", vm => vm.IsActiveOnly = true);
		await PrintAsync(session, d, outDir, "02_ShiharaiLedger_202604-202607");

		d.Input("支払台帳 0件条件", vm => {
			vm.ShiireCodeFrom = "UATVM-PRP-006";
			vm.ShiireCodeTo = "UATVM-PRP-006";
			vm.IsActiveOnly = true;
		});
		await PrintEmptyAsync(session, d, "支払台帳 0件");
	}

	// ---- 月別支払予定表 ------------------------------------------------------------

	static async Task MonthlyYoteiAsync(VmSession session, PaymentReportSeeder.Result seeded, string outDir) {
		const string Start = "2026/05";
		const int MonthCount = 4;
		string[] dueMonths = ["202605", "202606", "202607", "202608"];

		// 支払予定日ごとの締め。予定額は同じ予定日の最後の締めの累積残高（複数締日を合算しない）。
		var byDue = seeded.Suppliers
			.SelectMany(s => s.Periods.GroupBy(p => p.ShiharaiYoteiDay).Select(g => (s, due: g.Key, periods: g.OrderBy(p => p.DayTo).ToList())))
			.Where(x => dueMonths.Contains(x.due[..6]))
			.Select(x => new {
				x.s.Code, Ym = x.due[..6], Due = x.due,
				TotalShiire = x.periods.Sum(p => p.TotalShiire), TotalOut = x.periods.Sum(p => p.TotalOut),
				Yotei = x.periods[^1].Cumulative, Count = x.periods.Count, LastShime = x.periods[^1].DayTo,
			})
			.ToList();

		var d = session.OpenView<MonthlyShiharaiYoteiTableView, MonthlyShiharaiYoteiTableViewModel>();
		foreach (var byShiire in new[] { true, false }) {
			var name = $"月別支払予定表 {Start}から{MonthCount}か月 {(byShiire ? "仕入先別" : "月合計")}";
			d.Input(name, vm => {
				vm.StartYearMonth = Start;
				vm.MonthCountText = MonthCount.ToString(CultureInfo.InvariantCulture);
				vm.ShiireCodeFrom = PaymentReportSeeder.CodeFrom;
				vm.ShiireCodeTo = PaymentReportSeeder.CodeTo;
				vm.IsByShiire = byShiire;
				vm.IsActiveOnly = true;
			});
			var expected = byShiire
				? byDue.Where(x => x.Yotei != 0)
					.OrderBy(x => x.Due, StringComparer.Ordinal).ThenBy(x => x.Code, StringComparer.Ordinal)
					.Select(x => Row($"{x.Ym[..4]}/{x.Ym[4..]}", Label(x.Due), x.Code, x.TotalShiire, x.TotalOut, x.Yotei, x.Count, Label(x.LastShime)))
					.ToList()
				: byDue.GroupBy(x => x.Ym).OrderBy(g => g.Key, StringComparer.Ordinal)
					.Where(g => g.Sum(x => x.Yotei) != 0)
					.Select(g => Row($"{g.Key[..4]}/{g.Key[4..]}", "", "", g.Sum(x => x.TotalShiire), g.Sum(x => x.TotalOut),
						g.Sum(x => x.Yotei), g.Sum(x => x.Count), Label(g.Max(x => x.LastShime)!)))
					.ToList();
			var actual = await QueryReportAsync(d.Vm, seeded.DbPath,
				"yoteiYmLabel", "yoteiDayLabel", "shiireCode", "totalShiire", "totalOut", "yoteiKingaku", "shimeCount", "lastShimeDay");
			Compare(session, name, expected, actual);
		}

		d.Input("月別支払予定表 PDF条件", vm => vm.IsByShiire = true);
		await PrintAsync(session, d, outDir, "03_MonthlyShiharaiYotei_202605-202608");

		d.Input("月別支払予定表 0件条件", vm => {
			vm.ShiireCodeFrom = "UATVM-PRP-006";
			vm.ShiireCodeTo = "UATVM-PRP-006";
		});
		await PrintEmptyAsync(session, d, "月別支払予定表 0件");
	}

	// ---- 共通 ----------------------------------------------------------------------

	static string Label(string yyyymmdd) =>
		yyyymmdd.Length == 8 ? $"{yyyymmdd[..4]}/{yyyymmdd[4..6]}/{yyyymmdd[6..]}" : yyyymmdd;

	static string Row(params object[] values) =>
		string.Join(" | ", values.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)));

	/// <summary>ViewModel が組んだ印刷SQL（BuildPrintSqlParamAsync）をそのまま複製DBで実行する。</summary>
	static async Task<List<string>> QueryReportAsync(BaseReportViewModel vm, string dbPath, params string[] columns) {
		var method = vm.GetType().GetMethod("BuildPrintSqlParamAsync", BindingFlags.Instance | BindingFlags.NonPublic)
			?? throw new InvalidOperationException("BuildPrintSqlParamAsync を取得できません。");
		var param = await (Task<QueryListSqlParam?>)method.Invoke(vm, [CancellationToken.None])!
			?? throw new InvalidOperationException("印刷SQLが作られませんでした（入力不正）。");
		return Query(dbPath, param, columns);
	}

	static List<string> Query(string dbPath, QueryListSqlParam param, params string[] columns) {
		var builder = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
		using var connection = new SqliteConnection(builder.ToString());
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = param.Sql;
		for (var i = 0; i < param.Parameters.Length; i++) {
			command.Parameters.AddWithValue($"@{i}", param.Parameters[i]);
		}
		using var reader = command.ExecuteReader();
		var ordinals = columns.Length > 0
			? columns.Select(reader.GetOrdinal).ToArray()
			: [.. Enumerable.Range(0, reader.FieldCount)];
		var rows = new List<string>();
		while (reader.Read()) {
			rows.Add(Row([.. ordinals.Select(i => reader.IsDBNull(i) ? "NULL" : reader.GetValue(i))]));
		}
		return rows;
	}

	static void Compare(VmSession session, string name, IReadOnlyList<string> expected, IReadOnlyList<string> actual) {
		var diffs = new List<object>();
		for (var i = 0; i < Math.Max(expected.Count, actual.Count) && diffs.Count < MaxDiffs; i++) {
			var e = i < expected.Count ? expected[i] : "(なし)";
			var a = i < actual.Count ? actual[i] : "(なし)";
			if (e != a) diffs.Add(new { row = i + 1, expected = e, actual = a });
		}
		session.Check($"{name}: 独立期待値と一致", diffs.Count == 0,
			new { expectedCount = expected.Count, actualCount = actual.Count, diffs, sample = actual.Take(3) });
	}

	static async Task PrintAsync<TViewModel>(VmSession session, ViewDriver<TViewModel> d, string outDir, string name)
		where TViewModel : BaseReportViewModel {
		session.ClearDialogs();
		await d.RunAsync($"{name}:印刷", vm => GetDoOutputPdfCommand(vm));
		var message = d.Vm.Message;
		var errors = session.Dialogs.Where(x => x.Request.Image is MessageBoxImage.Warning or MessageBoxImage.Error)
			.Select(x => x.Request.Message).ToList();
		if (!session.Check($"{name}:PDF生成", message.StartsWith(PdfShownPrefix) && errors.Count == 0, new { message, errors })) return;
		var file = message[PdfShownPrefix.Length..].Trim();
		try {
			// CvServer は HTTP/2 のみで待ち受けるため、HTTP/1.1 の既定では 400 になる
			using var http = new HttpClient {
				DefaultRequestVersion = System.Net.HttpVersion.Version20,
				DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
			};
			var bytes = await http.GetByteArrayAsync($"{AppGlobal.Url}/wrk/{file}");
			var path = Path.Combine(outDir, $"{name}.pdf");
			await File.WriteAllBytesAsync(path, bytes);
			session.Check($"{name}:PDF取得", bytes.Length > 1000 && bytes.AsSpan(0, 4).SequenceEqual("%PDF"u8), new { path, bytes = bytes.Length });
		}
		catch (Exception ex) {
			session.Fail($"{name}:PDF取得", ex.Message);
		}
		ClosePdfViews();
	}

	static async Task PrintEmptyAsync<TViewModel>(VmSession session, ViewDriver<TViewModel> d, string name)
		where TViewModel : BaseReportViewModel {
		session.ClearDialogs();
		await d.RunAsync($"{name}:印刷", vm => GetDoOutputPdfCommand(vm));
		session.Check($"{name}:PDFを出さずメッセージ", !d.Vm.Message.StartsWith(PdfShownPrefix) && session.Dialogs.Count > 0,
			new { d.Vm.Message, Dialogs = session.Dialogs.Select(x => x.Request.Message).ToList() });
		ClosePdfViews();
	}

	static void ClosePdfViews() {
		foreach (var view in Application.Current.Windows.OfType<CvWpfclient.Views.Sub.WebPdfView>().ToList()) {
			try { view.Close(); }
			catch (InvalidOperationException) { /* 既に閉じている */ }
		}
	}

	static IAsyncRelayCommand GetDoOutputPdfCommand(BaseReportViewModel viewModel) =>
		typeof(BaseReportViewModel).GetProperty("DoOutputPdfCommand", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
			?.GetValue(viewModel) as IAsyncRelayCommand
		?? throw new InvalidOperationException("DoOutputPdfCommand を取得できません。");
}
