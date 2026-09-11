using System.Windows;
using CvBase;
using CvWpfclient.ViewModels._31Monthly;
using CvWpfclient.Views._31Monthly;
using UatVm.Seed;

namespace UatVm.Scenarios;

/// <summary>UAT-06の支払境界を支払計算画面から検証する。</summary>
public static class Uat06PaymentScenario {
	private static Uat06PaymentSeeder.Result? _seeded;

	public static void Seeder(string dbPath) =>
		_seeded = Uat06PaymentSeeder.Seed(dbPath, message => Console.WriteLine($"[seed] {message}"));

	public static async Task RunAsync(VmSession session) {
		var seeded = _seeded
			?? throw new InvalidOperationException("シードが実行されていません。");
		var driver = session.OpenView<PaymentCalculationView, PaymentCalculationViewModel>();
		session.SetDialogResponder(request => request.Button == MessageBoxButton.YesNo
			? (request.Message.Contains("支払計算を実行しますか", StringComparison.Ordinal)
				? MessageBoxResult.Yes
				: MessageBoxResult.No)
			: MessageBoxResult.OK);

		await ExecuteAsync(session, driver, "初回");
		var first = await VerifyAsync(session, seeded, "初回");
		await ExecuteAsync(session, driver, "再実行");
		var second = await VerifyAsync(session, seeded, "再実行");
		session.Check("UAT-06 再実行でSummaryKaiShiが不変", first.SequenceEqual(second), new { first, second });
		session.SetDialogResponder(null);
	}

	private static async Task ExecuteAsync(
		VmSession session,
		ViewDriver<PaymentCalculationViewModel> driver,
		string label) {
		session.ClearDialogs();
		driver.Input($"{label}:実行条件", vm => {
			vm.BillingMonth = "2026/07";
			vm.SelectedShime = Uat06PaymentSeeder.Shime;
			vm.TorihikiCodeFrom = Uat06PaymentSeeder.CodeFrom;
			vm.TorihikiCodeTo = Uat06PaymentSeeder.CodeTo;
		}, new {
			month = Uat06PaymentSeeder.BillingMonth,
			shime = Uat06PaymentSeeder.Shime,
			Uat06PaymentSeeder.CodeFrom,
			Uat06PaymentSeeder.CodeTo,
		});
		await driver.RunAsync($"execute:{label}", vm => vm.ExecuteCommand);
		var errors = session.Dialogs.Where(x => x.Request.Image == MessageBoxImage.Error).ToList();
		session.Check($"UAT-06 {label}はエラーなく完走", errors.Count == 0 && driver.Vm.ProgressValue == 100,
			new { errors = errors.Select(x => x.Request.Message), driver.Vm.ProgressValue, driver.Vm.StatusMessage });
	}

	private static async Task<string[]> VerifyAsync(
		VmSession session,
		Uat06PaymentSeeder.Result seeded,
		string label) {
		var rows = await session.QueryAsync<SummaryKaiShi>(@"
SELECT s.* FROM SummaryKaiShi AS s
INNER JOIN MasterShiire AS t ON t.Id = s.Id_Shiire
WHERE t.Code >= @0 AND t.Code <= @1 AND s.DenDay = @2
ORDER BY t.Code", Uat06PaymentSeeder.CodeFrom, Uat06PaymentSeeder.CodeTo, Uat06PaymentSeeder.DayTo);
		session.CheckEqual($"UAT-06 {label}で支払残が3件作られる", 3, rows.Count);

		for (var i = 0; i < Math.Min(rows.Count, seeded.Expectations.Count); i++) {
			var actual = rows[i];
			var expected = seeded.Expectations[i];
			session.CheckEqual($"UAT-06 {label} {expected.Code} 仕入", expected.Shiire, actual.Shiire);
			session.CheckEqual($"UAT-06 {label} {expected.Code} 税", expected.Tax, actual.Tax1 + actual.Tax2 + actual.Tax3);
			session.CheckEqual($"UAT-06 {label} {expected.Code} 仕入額", expected.TotalShiire, actual.TotalShiire);
			session.CheckEqual($"UAT-06 {label} {expected.Code} 現金", expected.Cash, actual.Cash);
			session.CheckEqual($"UAT-06 {label} {expected.Code} 手数料", expected.Fee, actual.Fee);
			session.CheckEqual($"UAT-06 {label} {expected.Code} 相殺", expected.Offset, actual.Offset);
			session.CheckEqual($"UAT-06 {label} {expected.Code} 支払額", expected.TotalOut, actual.TotalOut);
			session.CheckEqual($"UAT-06 {label} {expected.Code} 残高", expected.Balance, actual.Balance);
			session.CheckEqual($"UAT-06 {label} {expected.Code} 支払予定日", expected.ShiharaiYoteiDay, actual.ShiharaiYoteiDay);
		}

		return [.. rows.Select(x => $"{x.Id_Shiire}:{x.Shiire}:{x.Tax1}:{x.Tax2}:{x.Tax3}:{x.TotalShiire}:{x.Cash}:{x.Fee}:{x.Offset}:{x.TotalOut}:{x.Balance}:{x.ShiharaiYoteiDay}")];
	}
}
