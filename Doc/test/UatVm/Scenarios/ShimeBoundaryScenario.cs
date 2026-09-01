using CvBase;
using CvWpfclient.ViewModels._31Monthly;
using CvWpfclient.Views._31Monthly;
using System.Windows;
using UatVm.Seed;

namespace UatVm.Scenarios;

/// <summary>
/// C-01 締日20の請求期間境界を、請求計算画面から検証する。
/// </summary>
/// <remarks>
/// 締日20のとき請求月の対象期間は「前月21日〜当月20日」になる。境界日ちょうどに金額の違う
/// 売上を置き、請求月を変えて画面から実行して、どの売上がどの月へ計上されるかを確認する。
/// 金額をすべて異なる値にしてあるため、隣の月へ混入すれば必ず金額で判別できる。
///
/// 自社締日(`MasterSysman.ShimeBi`)は99のまま触らない。検証は得意先の締日(`Shime1`)だけで行う。
/// </remarks>
public static class ShimeBoundaryScenario {
	/// <summary>シード結果（期待値）。<see cref="Seeder"/> が投入時に設定する。</summary>
	private static ShimeBoundarySeeder.Result? _seeded;

	/// <summary>ハーネスへ渡すシード処理。</summary>
	public static void Seeder(string dbPath) {
		_seeded = ShimeBoundarySeeder.Seed(dbPath, message => Console.WriteLine($"[seed] {message}"));
	}

	public static async Task RunAsync(VmSession session) {
		var seeded = _seeded
			?? throw new InvalidOperationException("シードが実行されていません。Options.Seed に Seeder を設定してください。");

		session.Note("seed:投入内容", new {
			seeded.TokuiCode,
			seeded.Shime,
			sales = seeded.Sales.Select(x => new { x.KakeDay, x.Total, x.Tax, x.ExpectedBillingMonth }),
		});

		var d = session.OpenView<BillingCalculationView, BillingCalculationViewModel>();
		await d.WaitAsync("init:締日一覧の取得", vm => vm.ShimeItems.Count > 0);

		// 締日20の得意先を追加したので、画面の締日候補に20が現れるはずである。
		// 現れなければ以降の検証が成立しないため、ここで止める。
		var hasShime20 = d.Vm.ShimeItems.Any(x => x.Value == seeded.Shime);
		if (!session.Check($"締日{seeded.Shime}が画面の候補に現れる", hasShime20,
			new { items = d.Vm.ShimeItems.Select(x => new { x.Value, x.Name }) })) {
			return;
		}

		// 想定した実行確認だけを進める。想定外のYes/No確認はNoで止める。
		session.SetDialogResponder(request => request.Button == MessageBoxButton.YesNo
			? (request.Message.Contains("請求計算を実行しますか", StringComparison.Ordinal)
				? MessageBoxResult.Yes
				: MessageBoxResult.No)
			: MessageBoxResult.OK);

		foreach (var expected in seeded.Expectations) {
			await RunMonthAsync(session, d, seeded, expected);
		}

		session.SetDialogResponder(null);
	}

	private static async Task RunMonthAsync(
		VmSession session,
		ViewDriver<BillingCalculationViewModel> d,
		ShimeBoundarySeeder.Result seeded,
		ShimeBoundarySeeder.Expected expected) {

		session.ClearDialogs();
		var month = $"{expected.BillingMonth[..4]}/{expected.BillingMonth[4..]}";

		d.Input($"{expected.BillingMonth} 実行条件", vm => {
			vm.BillingMonth = month;
			vm.SelectedShime = seeded.Shime;
			vm.TorihikiCodeFrom = seeded.TokuiCode;
			vm.TorihikiCodeTo = seeded.TokuiCode;
			vm.IsReissue = false;
		}, new { month, shime = seeded.Shime, code = seeded.TokuiCode });

		await d.RunAsync($"execute:{expected.BillingMonth}", vm => vm.ExecuteCommand);
		d.Snapshot($"{expected.BillingMonth} execute後", vm => new { vm.StatusMessage, vm.ProgressValue });

		var errors = session.Dialogs.Where(x => x.Request.Image == MessageBoxImage.Error).ToList();
		if (!session.Check($"C-01 {expected.BillingMonth} エラーなく完走", errors.Count == 0 && d.Vm.ProgressValue == 100,
			new { errors = errors.Select(x => x.Request.Message), d.Vm.ProgressValue })) {
			return;
		}

		// 画面の完了を確認したうえで、DB側の計上結果を突合する。
		// 期間の切れ目が正しいかは、この金額が期待どおりかで決まる。
		// Id_Tokui(整数列)は文字列パラメータと一致しないため、得意先コードでJOINして絞る。
		var rows = await session.QueryAsync<SummaryUriSei>(@"
SELECT s.* FROM SummaryUriSei AS s
INNER JOIN MasterTokui AS t ON t.Id = s.Id_Tokui
WHERE t.Code = @0 AND s.DenDay = @1", seeded.TokuiCode, expected.DayTo);
		if (!session.Check($"C-01 {expected.BillingMonth} 請求残が1件作られる", rows.Count == 1,
			new { count = rows.Count, expected.DayTo })) {
			return;
		}

		var actual = rows[0];
		session.CheckEqual($"C-01 {expected.BillingMonth} 期間開始", expected.DayFrom, actual.DayFrom);
		session.CheckEqual($"C-01 {expected.BillingMonth} 期間終了", expected.DayTo, actual.DayTo);
		session.CheckEqual($"C-01 {expected.BillingMonth} 売上", expected.Uriage, actual.Uriage);
		session.CheckEqual($"C-01 {expected.BillingMonth} 税", expected.Tax, actual.Tax1 + actual.Tax2 + actual.Tax3);
		session.CheckEqual($"C-01 {expected.BillingMonth} 売上額", expected.TotalSales, actual.TotalSales);
		session.CheckEqual($"C-01 {expected.BillingMonth} 当期間残高", expected.Balance, actual.Balance);
	}
}
