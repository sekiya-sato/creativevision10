using CvBase;
using CvWpfclient.ViewModels._00System;
using CvWpfclient.Views._00System;
using System.Windows;
using UatVm.Seed;

namespace UatVm.Scenarios;

/// <summary>
/// C-07 Rebuild時の締日変更ブロック（D-02）を「在庫・掛再更新」画面から検証する。
/// </summary>
/// <remarks>
/// <para>
/// `SummaryRebuildClosingCheck`は、保存済み`SummaryUriSei.DayTo`と現在の`MasterTokui.Shime1`から
/// 導ける締日を比較し、食い違えば**全要求を送信しない**（`summaryreconcile -- closingcheck`が
/// 計算層で確認済み）。ここでは画面（`StockKakeUpdateViewModel`）経由でも同じ振る舞いになることを確認する。
/// </para>
/// <para>
/// 手順:
/// <list type="number">
/// <item>C-01で作った専用得意先`UATVM-T20`（締日20）の請求残（202607）を使う。</item>
/// <item>まず締日を変えずに「売掛のみ」を実行し、正常に完走すること（不一致なし）を確認する。</item>
/// <item>締日を20→15へ変更する（UAT専用得意先のみ。実マスタには触らない）。</item>
/// <item>同じ画面で再度実行し、**送信されずブロックされる**こと、DBの値が変わらないことを確認する。</item>
/// <item>締日を20へ戻す（後始末）。</item>
/// </list>
/// </para>
/// </remarks>
public static class ClosingChangeBlockScenario {
	private const string TargetMonth = "202607";
	private const int ChangedShime = 15;

	private static ShimeBoundarySeeder.Result? _seeded;

	/// <summary>C-01と同じ専用得意先を使う。既に202607の請求残があることが前提。</summary>
	public static void Seeder(string dbPath) {
		_seeded = ShimeBoundarySeeder.Seed(dbPath, message => Console.WriteLine($"[seed] {message}"));
	}

	public static async Task RunAsync(VmSession session) {
		var seeded = _seeded
			?? throw new InvalidOperationException("シードが実行されていません。Options.Seed に Seeder を設定してください。");

		var d = session.OpenView<StockKakeUpdateView, StockKakeUpdateViewModel>();
		await d.WaitAsync("init:自社締日の取得", vm => !vm.ClosingPeriodText.Contains("読み込んでいます", StringComparison.Ordinal));

		session.SetDialogResponder(request => request.Button == MessageBoxButton.YesNo ? MessageBoxResult.Yes : MessageBoxResult.OK);

		// 1) 変更前：まず202607の請求残を作っておく（締日一致の状態で正しく積む）。
		var before = await ExecuteAsync(session, d, "変更前(売掛のみ)");
		if (before == null) return;
		var beforeRow = await ReadUriSeiAsync(session, seeded.TokuiCode, TargetMonth);
		session.Check("C-07 変更前は締日不一致なしで完走", before.Value, new { d.Vm.StatusMessage });
		session.Check("C-07 変更前は請求残が作られる", beforeRow != null, new { beforeRow });

		// 2) 得意先の締日を変更する（UAT専用得意先のみ。実マスタには触らない）。
		await ChangeShimeAsync(session, seeded.TokuiId, ChangedShime);

		// 3) 同じ画面で再実行 → 締日変更検出でブロックされるはず。
		session.ClearDialogs();
		var blocked = await ExecuteAsync(session, d, "変更後(売掛のみ)");
		var afterRow = await ReadUriSeiAsync(session, seeded.TokuiCode, TargetMonth);

		session.Check("C-07 締日変更後は完走せずブロックされる（例外にならない）", blocked != null,
			new { d.Vm.StatusMessage });
		if (blocked != null) {
			session.Check("C-07 締日変更を検出した旨の警告が出る",
				d.Vm.StatusMessage.Contains("締日変更を検出したため", StringComparison.Ordinal),
				new { d.Vm.StatusMessage });
			var warningDialogs = session.Dialogs.Where(x => x.Request.Image == MessageBoxImage.Warning).ToList();
			session.Check("C-07 警告ダイアログが1回出る", warningDialogs.Count == 1,
				new { messages = warningDialogs.Select(x => x.Request.Message) });
			session.Check("C-07 進捗が進まない（送信されていない）", d.Vm.ProgressValue == 0,
				new { d.Vm.ProgressValue });
		}
		session.Check("C-07 ブロック中はDBが変わらない",
			beforeRow != null && afterRow != null && SameRow(beforeRow, afterRow),
			new { before = beforeRow, after = afterRow });

		// 4) 後始末：締日を20へ戻す。ここを飛ばすと以後のC-01系シナリオが壊れる。
		var restored = await ChangeShimeAsync(session, seeded.TokuiId, seeded.Shime);
		session.Check("C-07 後始末：締日を20へ復元", restored.Shime1 == seeded.Shime,
			new { shime = restored.Shime1 });

		session.SetDialogResponder(null);
	}

	private static async Task<bool?> ExecuteAsync(VmSession session, ViewDriver<StockKakeUpdateViewModel> d, string label) {
		session.ClearDialogs();
		d.Input($"{label} 実行条件", vm => {
			vm.YearMonthFrom = "2026/07";
			vm.YearMonthTo = "2026/07";
			vm.UpdateTarget = "売掛のみ";
		}, new { UpdateTarget = "売掛のみ", Month = "2026/07" });

		await d.RunAsync($"execute:{label}", vm => vm.ExecuteCommand);
		d.Snapshot($"{label} execute後", vm => new { vm.StatusMessage, vm.ProgressValue, vm.IsProcessing });

		var errors = session.Dialogs.Where(x => x.Request.Image == MessageBoxImage.Error).ToList();
		if (errors.Count > 0) {
			session.Fail($"{label}:予期しないエラー", string.Join(" / ", errors.Select(x => x.Request.Message)));
			return null;
		}
		return d.Vm.ProgressValue == 100;
	}

	private static async Task<MasterTokui> ChangeShimeAsync(VmSession session, long tokuiId, int shime) {
		var rows = await session.QueryAsync<MasterTokui>("SELECT * FROM MasterTokui WHERE Id=@0", tokuiId.ToString());
		var current = rows.Single();
		current.Shime1 = shime;
		var updated = await session.UpdateAsync(current);
		session.Note("締日変更", new { tokuiId, shime });
		return updated;
	}

	private static async Task<SummaryUriSei?> ReadUriSeiAsync(VmSession session, string tokuiCode, string billingMonth) {
		var dayTo = billingMonth + "20"; // 締日20のため、期間終了日は当月20日固定。
		var rows = await session.QueryAsync<SummaryUriSei>(@"
SELECT s.* FROM SummaryUriSei AS s
INNER JOIN MasterTokui AS t ON t.Id = s.Id_Tokui
WHERE t.Code = @0 AND s.DenDay = @1", tokuiCode, dayTo);
		return rows.FirstOrDefault();
	}

	private static bool SameRow(SummaryUriSei a, SummaryUriSei b) =>
		a.SeikyuNo == b.SeikyuNo && a.Renban == b.Renban && a.TotalSales == b.TotalSales && a.Balance == b.Balance;
}
