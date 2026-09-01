using CvBase;
using CvWpfclient.ViewModels._31Monthly;
using CvWpfclient.Views._31Monthly;
using System.Windows;
using UatVm.Seed;

namespace UatVm.Scenarios;

/// <summary>
/// C-05 冪等性 / C-06 明示的再発行（D-03）を請求計算画面から検証する。
/// </summary>
/// <remarks>
/// <para>
/// D-03は「通常再計算・Rebuildは番号維持で冪等、明示的再発行だけ`Renban+1`」を原則とする。
/// 計算層では`summaryreconcile -- idempotent`で確認済みなので、ここでは**画面経路**で確認する。
/// 再発行チェックボックスの状態が`BillingParameter`へ正しく渡ることが要点である。
/// </para>
/// <para>
/// 対象は締日20の専用得意先（<see cref="ShimeBoundarySeeder"/>が投入）に限定する。
/// 他の得意先の請求残に影響しないため、繰り返し実行しても安全である。
/// </para>
/// </remarks>
public static class BillingNumberingScenario {
	private static ShimeBoundarySeeder.Result? _seeded;

	/// <summary>ハーネスへ渡すシード処理。C-01と同じ専用得意先を使う。</summary>
	public static void Seeder(string dbPath) {
		_seeded = ShimeBoundarySeeder.Seed(dbPath, message => Console.WriteLine($"[seed] {message}"));
	}

	public static async Task RunAsync(VmSession session) {
		var seeded = _seeded
			?? throw new InvalidOperationException("シードが実行されていません。Options.Seed に Seeder を設定してください。");

		// 最初の請求月だけを使う。期間境界そのものはC-01で確認済み。
		var target = seeded.Expectations[0];
		var month = $"{target.BillingMonth[..4]}/{target.BillingMonth[4..]}";

		var d = session.OpenView<BillingCalculationView, BillingCalculationViewModel>();
		await d.WaitAsync("init:締日一覧の取得", vm => vm.ShimeItems.Count > 0);

		session.SetDialogResponder(request => request.Button == MessageBoxButton.YesNo
			? (request.Message.Contains("請求計算を実行しますか", StringComparison.Ordinal)
				? MessageBoxResult.Yes
				: MessageBoxResult.No)
			: MessageBoxResult.OK);

		// 1回目：初回作成。連番は1から始まる。
		var first = await ExecuteAsync(session, d, seeded, month, target, isReissue: false, label: "1回目(通常)");
		if (first == null) return;
		session.CheckEqual("C-05 初回の連番は1", 1, first.Renban);
		session.CheckEqual("C-05 初回の請求書番号",
			$"{first.Id_Tokui}-{target.DayTo}-01", first.SeikyuNo);

		// 2回目：通常再計算。番号・連番・金額が変わらないこと（冪等）。
		var second = await ExecuteAsync(session, d, seeded, month, target, isReissue: false, label: "2回目(通常)");
		if (second == null) return;
		session.CheckEqual("C-05 通常再計算で請求書番号を維持", first.SeikyuNo, second.SeikyuNo);
		session.CheckEqual("C-05 通常再計算で連番を維持", first.Renban, second.Renban);
		session.Check("C-05 通常再計算で金額が変わらない", SameAmounts(first, second), Compare(first, second));

		// 3回目：明示的再発行。連番が+1され、番号の枝番も追随すること。
		var reissued = await ExecuteAsync(session, d, seeded, month, target, isReissue: true, label: "3回目(再発行)");
		if (reissued == null) return;
		session.CheckEqual("C-06 再発行で連番が+1", first.Renban + 1, reissued.Renban);
		session.CheckEqual("C-06 再発行で請求書番号の枝番が追随",
			$"{first.Id_Tokui}-{target.DayTo}-02", reissued.SeikyuNo);
		session.Check("C-06 再発行でも金額は変わらない", SameAmounts(first, reissued), Compare(first, reissued));

		// 4回目：再発行後に通常再計算。連番を戻さず維持すること（世代が巻き戻らない）。
		var afterReissue = await ExecuteAsync(session, d, seeded, month, target, isReissue: false, label: "4回目(再発行後の通常)");
		if (afterReissue == null) return;
		session.CheckEqual("C-05 再発行後の通常再計算で連番を維持（巻き戻らない）", reissued.Renban, afterReissue.Renban);
		session.CheckEqual("C-05 再発行後の通常再計算で請求書番号を維持", reissued.SeikyuNo, afterReissue.SeikyuNo);

		session.SetDialogResponder(null);
	}

	/// <summary>請求計算を1回実行し、作られた請求残を読み戻す。</summary>
	private static async Task<SummaryUriSei?> ExecuteAsync(
		VmSession session,
		ViewDriver<BillingCalculationViewModel> d,
		ShimeBoundarySeeder.Result seeded,
		string month,
		ShimeBoundarySeeder.Expected target,
		bool isReissue,
		string label) {

		session.ClearDialogs();
		d.Input($"{label} 実行条件", vm => {
			vm.BillingMonth = month;
			vm.SelectedShime = seeded.Shime;
			vm.TorihikiCodeFrom = seeded.TokuiCode;
			vm.TorihikiCodeTo = seeded.TokuiCode;
			vm.IsReissue = isReissue;
		}, new { month, code = seeded.TokuiCode, isReissue });

		await d.RunAsync($"execute:{label}", vm => vm.ExecuteCommand);

		var errors = session.Dialogs.Where(x => x.Request.Image == MessageBoxImage.Error).ToList();
		if (!session.Check($"{label} エラーなく完走", errors.Count == 0 && d.Vm.ProgressValue == 100,
			new { errors = errors.Select(x => x.Request.Message), d.Vm.ProgressValue })) {
			return null;
		}

		var rows = await session.QueryAsync<SummaryUriSei>(@"
SELECT s.* FROM SummaryUriSei AS s
INNER JOIN MasterTokui AS t ON t.Id = s.Id_Tokui
WHERE t.Code = @0 AND s.DenDay = @1", seeded.TokuiCode, target.DayTo);

		if (!session.Check($"{label} 請求残が1件", rows.Count == 1, new { count = rows.Count })) {
			return null;
		}

		var row = rows[0];
		session.Note($"{label} 結果", new {
			row.SeikyuNo,
			row.Renban,
			row.Uriage,
			Tax = row.Tax1 + row.Tax2 + row.Tax3,
			row.TotalSales,
			row.Balance,
			row.NyukinYoteiDay,
		});
		return row;
	}

	private static bool SameAmounts(SummaryUriSei a, SummaryUriSei b) =>
		a.Uriage == b.Uriage && a.Henpin == b.Henpin && a.Nebiki == b.Nebiki && a.Sonota == b.Sonota
		&& a.Tax1 + a.Tax2 + a.Tax3 == b.Tax1 + b.Tax2 + b.Tax3 && a.TotalSales == b.TotalSales && a.TotalIn == b.TotalIn
		&& a.Balance == b.Balance && a.NyukinYoteiDay == b.NyukinYoteiDay;

	private static object Compare(SummaryUriSei a, SummaryUriSei b) => new {
		before = new { a.Uriage, Tax = a.Tax1 + a.Tax2 + a.Tax3, a.TotalSales, a.TotalIn, a.Balance, a.NyukinYoteiDay },
		after = new { b.Uriage, Tax = b.Tax1 + b.Tax2 + b.Tax3, b.TotalSales, b.TotalIn, b.Balance, b.NyukinYoteiDay },
	};
}
