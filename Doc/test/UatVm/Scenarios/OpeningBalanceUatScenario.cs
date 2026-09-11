using System.Windows;
using System.IO;
using CvBase;
using CvWpfclient.ViewModels._00System;
using CvWpfclient.ViewModels._31Monthly;
using CvWpfclient.Views._00System;
using CvWpfclient.Views._31Monthly;
using UatVm.Seed;

namespace UatVm.Scenarios;

/// <summary>UAT-08の期首残高登録、代表取引、再作成後の凍結・繰越を検証する。</summary>
public static class OpeningBalanceUatScenario {
	private static OpeningBalanceUatSeeder.Result? _seeded;

	public static void Seeder(string dbPath) =>
		_seeded = OpeningBalanceUatSeeder.Seed(dbPath, message => Console.WriteLine($"[seed] {message}"));

	public static async Task RunAsync(VmSession session) {
		var seeded = _seeded ?? throw new InvalidOperationException("シードが実行されていません。");
		session.SetDialogResponder(request => request.Button is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel
			? MessageBoxResult.Yes : MessageBoxResult.OK);

		var files = new List<string>();
		try {
			await RegisterAsync(session, EnumOpeningBalanceKind.UriKake, OpeningBalanceUatSeeder.UriOpening, seeded, files);
			await RegisterAsync(session, EnumOpeningBalanceKind.UriSei, OpeningBalanceUatSeeder.UriOpening, seeded, files);
			await RegisterAsync(session, EnumOpeningBalanceKind.KaiKake, OpeningBalanceUatSeeder.KaiOpening, seeded, files);
			await RegisterAsync(session, EnumOpeningBalanceKind.KaiShi, OpeningBalanceUatSeeder.KaiOpening, seeded, files);

			var openingBefore = await ReadOpeningAsync(session, seeded);
			if (!CheckOpening(session, "登録直後", openingBefore)) return;

			var rebuild = session.OpenView<StockKakeUpdateView, StockKakeUpdateViewModel>();
			await rebuild.RunAsync("UAT-08:再作成初期化", vm => vm.InitCommand);
			await RunRebuildAsync(session, rebuild, "売掛のみ");
			await RunRebuildAsync(session, rebuild, "買掛のみ");

			var afterFirst = await ReadSnapshotAsync(session, seeded);
			CheckSnapshot(session, "初回", afterFirst);
			if (!CheckOpening(session, "再作成後", afterFirst.Opening)) return;

			await RunRebuildAsync(session, rebuild, "売掛のみ");
			await RunRebuildAsync(session, rebuild, "買掛のみ");
			var afterSecond = await ReadSnapshotAsync(session, seeded);
			CheckSnapshot(session, "再実行", afterSecond);
			session.CheckEqual("UAT-08 再実行で4区分の結果が不変", afterFirst.Fingerprint, afterSecond.Fingerprint);
		}
		finally {
			foreach (var file in files) {
				try { File.Delete(file); }
				catch (IOException) { }
			}
			session.SetDialogResponder(null);
		}
	}

	private static async Task RegisterAsync(
		VmSession session,
		EnumOpeningBalanceKind kind,
		long amount,
		OpeningBalanceUatSeeder.Result seeded,
		List<string> files) {
		var spec = OpeningBalanceCsv.GetSpec(kind);
		var code = spec.IsPayable ? OpeningBalanceUatSeeder.ShiireCode : OpeningBalanceUatSeeder.TokuiCode;
		var name = spec.IsPayable ? "UAT-08期首仕入先" : "UAT-08期首得意先";
		var keyDate = spec.IsClosingBased ? OpeningBalanceUatSeeder.OpeningDay : OpeningBalanceUatSeeder.OpeningMonth;
		var breakdown = new OpeningBalanceBreakdown { Main = amount };
		var lines = OpeningBalanceCsv.BuildTemplateLines(
			kind,
			includeBreakdown: true,
			OpeningBalanceUatSeeder.FiscalStartDate,
			keyDate,
			OpeningBalanceUatSeeder.Shime,
			[new OpeningBalanceTemplateRow(code, name, OpeningBalanceUatSeeder.Shime, amount, breakdown, OpeningBalanceUatSeeder.TargetDay)]);
		var path = Path.Combine(Path.GetTempPath(), $"cv10-uat08-{kind}-{Environment.ProcessId}.csv");
		await File.WriteAllLinesAsync(path, lines);
		files.Add(path);

		var driver = session.OpenView<BalanceRegistrationView, BalanceRegistrationViewModel>();
		driver.Input($"UAT-08:{spec.DisplayName}区分", vm => vm.SelectedKind = kind, new { kind, keyDate, amount });
		await driver.RunAsync($"UAT-08:{spec.DisplayName}初期化", vm => vm.InitCommand);
		if (spec.IsClosingBased) {
			driver.Input($"UAT-08:{spec.DisplayName}最終締日", vm => vm.SelectedShime = OpeningBalanceUatSeeder.Shime,
				new { shime = OpeningBalanceUatSeeder.Shime });
		}
		driver.Input($"UAT-08:{spec.DisplayName}CSV", vm => vm.FilePath = path, new { code, path });
		await driver.RunAsync($"UAT-08:{spec.DisplayName}CSV検証", vm => vm.ValidateFileCommand);
		if (!session.Check($"UAT-08 {spec.DisplayName}CSVを登録可能", driver.Vm.CanRegister && driver.Vm.ErrorCount == 0
			&& driver.Vm.NewCount == 1 && driver.Vm.TotalAmount == amount,
			new { driver.Vm.Message, driver.Vm.ErrorCount, driver.Vm.NewCount, driver.Vm.TotalAmount, errors = driver.Vm.ErrorRows.Select(x => x.Detail) })) return;
		await driver.RunAsync($"UAT-08:{spec.DisplayName}期首残高登録", vm => vm.RegisterCommand);
		var ownerId = spec.IsPayable ? seeded.ShiireId : seeded.TokuiId;
		var count = kind switch {
			EnumOpeningBalanceKind.UriKake => (await session.QueryAsync<SummaryUriKake>("where Id_Tokui=@0 AND DenMonth=@1", ownerId.ToString(), keyDate)).Count,
			EnumOpeningBalanceKind.UriSei => (await session.QueryAsync<SummaryUriSei>("where Id_Tokui=@0 AND DenDay=@1", ownerId.ToString(), keyDate)).Count,
			EnumOpeningBalanceKind.KaiKake => (await session.QueryAsync<SummaryKaiKake>("where Id_Shiire=@0 AND DenMonth=@1", ownerId.ToString(), keyDate)).Count,
			_ => (await session.QueryAsync<SummaryKaiShi>("where Id_Shiire=@0 AND DenDay=@1", ownerId.ToString(), keyDate)).Count,
		};
		session.CheckEqual($"UAT-08 {spec.DisplayName}期首行を画面登録", 1, count);
	}

	private static async Task RunRebuildAsync(VmSession session, ViewDriver<StockKakeUpdateViewModel> driver, string target) {
		driver.Input($"UAT-08:{target}再作成条件", vm => {
			vm.YearMonthFrom = "2026/07";
			vm.YearMonthTo = "2026/07";
			vm.UpdateTarget = target;
		}, new { target, month = OpeningBalanceUatSeeder.TargetMonth });
		await driver.RunAsync($"UAT-08:{target}再作成", vm => vm.ExecuteCommand);
		session.Check($"UAT-08 {target}再作成が完走", driver.Vm.ProgressValue == 100 && driver.Vm.StatusMessage.Contains("完了", StringComparison.Ordinal),
			new { driver.Vm.ProgressValue, driver.Vm.StatusMessage });
	}

	private static async Task<OpeningRows> ReadOpeningAsync(VmSession session, OpeningBalanceUatSeeder.Result seeded) => new(
		(await session.QueryAsync<SummaryUriKake>("where Id_Tokui=@0 AND DenMonth=@1", seeded.TokuiId.ToString(), OpeningBalanceUatSeeder.OpeningMonth)).Single(),
		(await session.QueryAsync<SummaryUriSei>("where Id_Tokui=@0 AND DenDay=@1", seeded.TokuiId.ToString(), OpeningBalanceUatSeeder.OpeningDay)).Single(),
		(await session.QueryAsync<SummaryKaiKake>("where Id_Shiire=@0 AND DenMonth=@1", seeded.ShiireId.ToString(), OpeningBalanceUatSeeder.OpeningMonth)).Single(),
		(await session.QueryAsync<SummaryKaiShi>("where Id_Shiire=@0 AND DenDay=@1", seeded.ShiireId.ToString(), OpeningBalanceUatSeeder.OpeningDay)).Single());

	private static bool CheckOpening(VmSession session, string label, OpeningRows rows) => session.Check(
		$"UAT-08 {label}も期首日前4行を保持",
		rows.UriKake.Balance == OpeningBalanceUatSeeder.UriOpening
		&& rows.UriSei.Balance == OpeningBalanceUatSeeder.UriOpening
		&& rows.KaiKake.Balance == OpeningBalanceUatSeeder.KaiOpening
		&& rows.KaiShi.Balance == OpeningBalanceUatSeeder.KaiOpening,
		new { uriKake = rows.UriKake.Balance, uriSei = rows.UriSei.Balance, kaiKake = rows.KaiKake.Balance, kaiShi = rows.KaiShi.Balance });

	private static async Task<Snapshot> ReadSnapshotAsync(VmSession session, OpeningBalanceUatSeeder.Result seeded) {
		var opening = await ReadOpeningAsync(session, seeded);
		var uriKake = (await session.QueryAsync<SummaryUriKake>("where Id_Tokui=@0 AND DenMonth=@1", seeded.TokuiId.ToString(), OpeningBalanceUatSeeder.TargetMonth)).Single();
		var uriSei = (await session.QueryAsync<SummaryUriSei>("where Id_Tokui=@0 AND DenDay=@1", seeded.TokuiId.ToString(), "20260731")).Single();
		var kaiKake = (await session.QueryAsync<SummaryKaiKake>("where Id_Shiire=@0 AND DenMonth=@1", seeded.ShiireId.ToString(), OpeningBalanceUatSeeder.TargetMonth)).Single();
		var kaiShi = (await session.QueryAsync<SummaryKaiShi>("where Id_Shiire=@0 AND DenDay=@1", seeded.ShiireId.ToString(), "20260731")).Single();
		var sale = (await session.QueryAsync<Tran00Uriage>("where Id=@0", seeded.SalesId.ToString())).Single();
		var purchase = (await session.QueryAsync<Tran03Shiire>("where Id=@0", seeded.PurchaseId.ToString())).Single();
		var uriKakePrevious = (await session.QueryAsync<SummaryUriKake>("where Id_Tokui=@0 AND DenMonth < @1", seeded.TokuiId.ToString(), OpeningBalanceUatSeeder.TargetMonth))
			.Sum(x => x.TotalSales - x.TotalIn);
		var uriSeiPrevious = (await session.QueryAsync<SummaryUriSei>("where Id_Tokui=@0 AND DayTo < @1", seeded.TokuiId.ToString(), "20260701"))
			.Sum(x => x.TotalSales - x.TotalIn);
		var kaiKakePrevious = (await session.QueryAsync<SummaryKaiKake>("where Id_Shiire=@0 AND DenMonth < @1", seeded.ShiireId.ToString(), OpeningBalanceUatSeeder.TargetMonth))
			.Sum(x => x.TotalShiire - x.TotalOut);
		var kaiShiPrevious = (await session.QueryAsync<SummaryKaiShi>("where Id_Shiire=@0 AND DayTo < @1", seeded.ShiireId.ToString(), "20260701"))
			.Sum(x => x.TotalShiire - x.TotalOut);
		return new Snapshot(opening, uriKake, uriSei, kaiKake, kaiShi, sale, purchase,
			uriKakePrevious, uriSeiPrevious, kaiKakePrevious, kaiShiPrevious);
	}

	private static void CheckSnapshot(VmSession session, string label, Snapshot data) {
		var uriExpected = OpeningBalanceUatSeeder.SalesAmount + OpeningBalanceUatSeeder.SalesTax - OpeningBalanceUatSeeder.ReceiptAmount;
		var kaiExpected = OpeningBalanceUatSeeder.PurchaseAmount + OpeningBalanceUatSeeder.PurchaseTax - OpeningBalanceUatSeeder.PaymentAmount;
		session.Check($"UAT-08 {label}売掛・請求の当期純増減", data.UriKake.Balance == uriExpected && data.UriSei.Balance == uriExpected,
			new { uriKake = data.UriKake.Balance, uriSei = data.UriSei.Balance, uriExpected });
		session.Check($"UAT-08 {label}買掛・支払の当期純増減", data.KaiKake.Balance == kaiExpected && data.KaiShi.Balance == kaiExpected,
			new { kaiKake = data.KaiKake.Balance, kaiShi = data.KaiShi.Balance, kaiExpected });
		session.Check($"UAT-08 {label}4区分の税・取引・決済内訳が一致",
			data.UriKake.Tax1 == OpeningBalanceUatSeeder.SalesTax && data.UriKake.TotalSales == 11_000 && data.UriKake.TotalIn == 1_000
			&& data.UriSei.Tax1 == OpeningBalanceUatSeeder.SalesTax && data.UriSei.TotalSales == 11_000 && data.UriSei.TotalIn == 1_000
			&& data.KaiKake.Tax1 == OpeningBalanceUatSeeder.PurchaseTax && data.KaiKake.TotalShiire == 22_000 && data.KaiKake.TotalOut == 2_000
			&& data.KaiShi.Tax1 == OpeningBalanceUatSeeder.PurchaseTax && data.KaiShi.TotalShiire == 22_000 && data.KaiShi.TotalOut == 2_000,
			new { data.UriKake.Tax1, data.UriKake.TotalSales, data.UriKake.TotalIn, kaiTax = data.KaiKake.Tax1, data.KaiKake.TotalShiire, data.KaiKake.TotalOut });
		session.Check($"UAT-08 {label}帳票前残は期首残を起点にする",
			data.UriKakePrevious == OpeningBalanceUatSeeder.UriOpening
			&& data.UriSeiPrevious == OpeningBalanceUatSeeder.UriOpening
			&& data.KaiKakePrevious == OpeningBalanceUatSeeder.KaiOpening
			&& data.KaiShiPrevious == OpeningBalanceUatSeeder.KaiOpening,
			new { data.UriKakePrevious, data.UriSeiPrevious, data.KaiKakePrevious, data.KaiShiPrevious });
		session.Check($"UAT-08 {label}代表伝票のRate・Tax・Total・IsPayを保持",
			data.Sale.Rate == 100 && data.Sale.Tax1 == OpeningBalanceUatSeeder.SalesTax
			&& data.Sale.Total == OpeningBalanceUatSeeder.SalesAmount + OpeningBalanceUatSeeder.SalesTax && data.Sale.IsPay == 1
			&& data.Purchase.Rate == 100 && data.Purchase.Tax1 == OpeningBalanceUatSeeder.PurchaseTax
			&& data.Purchase.Total == OpeningBalanceUatSeeder.PurchaseAmount + OpeningBalanceUatSeeder.PurchaseTax && data.Purchase.IsPay == 1,
			new { sale = new { data.Sale.Rate, data.Sale.Tax1, data.Sale.Total, data.Sale.IsPay }, purchase = new { data.Purchase.Rate, data.Purchase.Tax1, data.Purchase.Total, data.Purchase.IsPay } });
	}

	private sealed record OpeningRows(SummaryUriKake UriKake, SummaryUriSei UriSei, SummaryKaiKake KaiKake, SummaryKaiShi KaiShi);

	private sealed record Snapshot(
		OpeningRows Opening,
		SummaryUriKake UriKake,
		SummaryUriSei UriSei,
		SummaryKaiKake KaiKake,
		SummaryKaiShi KaiShi,
		Tran00Uriage Sale,
		Tran03Shiire Purchase,
		long UriKakePrevious,
		long UriSeiPrevious,
		long KaiKakePrevious,
		long KaiShiPrevious) {
		public string Fingerprint => string.Join("|", [
			UriKake.Balance, UriKake.TotalSales, UriKake.TotalIn,
			UriSei.Balance, UriSei.TotalSales, UriSei.TotalIn,
			KaiKake.Balance, KaiKake.TotalShiire, KaiKake.TotalOut,
			KaiShi.Balance, KaiShi.TotalShiire, KaiShi.TotalOut,
			UriKakePrevious, UriSeiPrevious, KaiKakePrevious, KaiShiPrevious,
		]);
	}
}
