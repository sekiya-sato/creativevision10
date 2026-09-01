using CvBase;
using CvWpfclient.ViewModels._31Monthly;
using CvWpfclient.Views._31Monthly;
using System.Windows;
using UatVm.Seed;

namespace UatVm.Scenarios;

/// <summary>
/// C-09 生地・付属仕入（<see cref="Tran02Material"/>）の買掛合算を支払計算画面から検証する。
/// </summary>
/// <remarks>
/// `SummaryDb.CalcSummaryKaiShi`は`Tran03Shiire`と`Tran02Material`を合算するが、
/// `Tran02Material`の区分99（その他）は**仕入ではなく消費税へ全額を積む**点が
/// `Tran03Shiire`と異なる（UAT-06残作業）。UAT専用仕入先1件に限定して検証するため、
/// 他の仕入先の支払残には影響しない。
/// </remarks>
public static class MaterialPurchaseScenario {
	private static MaterialSeeder.Result? _seeded;

	public static void Seeder(string dbPath) {
		_seeded = MaterialSeeder.Seed(dbPath, message => Console.WriteLine($"[seed] {message}"));
	}

	public static async Task RunAsync(VmSession session) {
		var seeded = _seeded
			?? throw new InvalidOperationException("シードが実行されていません。Options.Seed に Seeder を設定してください。");

		session.Note("seed:投入内容", new {
			seeded.ShiireCode,
			seeded.Shime,
			仕入 = MaterialSeeder.Shiire,
			返品 = MaterialSeeder.Henpin,
			値引 = MaterialSeeder.Nebiki,
			その他 = MaterialSeeder.Other,
		});

		var d = session.OpenView<PaymentCalculationView, PaymentCalculationViewModel>();
		await d.WaitAsync("init:締日一覧の取得", vm => vm.ShimeItems.Count > 0);

		session.SetDialogResponder(request => request.Button == MessageBoxButton.YesNo
			? (request.Message.Contains("支払計算を実行しますか", StringComparison.Ordinal)
				? MessageBoxResult.Yes
				: MessageBoxResult.No)
			: MessageBoxResult.OK);

		d.Input("実行条件", vm => {
			vm.BillingMonth = "2026/07";
			vm.SelectedShime = seeded.Shime;
			vm.TorihikiCodeFrom = seeded.ShiireCode;
			vm.TorihikiCodeTo = seeded.ShiireCode;
		}, new { seeded.ShiireCode, seeded.Shime });

		await d.RunAsync("execute:支払計算", vm => vm.ExecuteCommand);
		d.Snapshot("execute後", vm => new { vm.StatusMessage, vm.ProgressValue });

		var errors = session.Dialogs.Where(x => x.Request.Image == MessageBoxImage.Error).ToList();
		if (!session.Check("C-09 エラーなく完走", errors.Count == 0 && d.Vm.ProgressValue == 100,
			new { errors = errors.Select(x => x.Request.Message), d.Vm.ProgressValue })) {
			return;
		}

		var rows = await session.QueryAsync<SummaryKaiShi>(@"
SELECT s.* FROM SummaryKaiShi AS s
INNER JOIN MasterShiire AS t ON t.Id = s.Id_Shiire
WHERE t.Code = @0 AND s.DenDay = @1", seeded.ShiireCode, seeded.DayTo);

		if (!session.Check("C-09 支払残が1件作られる", rows.Count == 1, new { count = rows.Count })) return;

		var actual = rows[0];
		var actualTax = actual.Tax1 + actual.Tax2 + actual.Tax3;
		session.Note("C-09 実際値", new {
			actual.Shiire,
			actual.Henpin,
			actual.Nebiki,
			Tax = actualTax,
			actual.TotalShiire,
		});

		session.CheckEqual("C-09 仕入額（区分99は仕入へ畳み込まない）", seeded.ExpectedShiire, actual.Shiire);
		session.CheckEqual("C-09 返品額", seeded.ExpectedHenpin, actual.Henpin);
		session.CheckEqual("C-09 値引額", seeded.ExpectedNebiki, actual.Nebiki);
		session.CheckEqual("C-09 税額（区分99の全額を消費税へ計上）", seeded.ExpectedTax, actualTax);
		session.CheckEqual("C-09 仕入額合計（TotalShiire）", seeded.ExpectedTotalShiire, actual.TotalShiire);

		session.SetDialogResponder(null);
	}
}
