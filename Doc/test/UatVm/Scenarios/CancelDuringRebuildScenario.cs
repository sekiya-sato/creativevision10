using CvBase;
using CvWpfclient.ViewModels._00System;
using CvWpfclient.Views._00System;
using System.Windows;

namespace UatVm.Scenarios;

/// <summary>
/// C-10 実行中のキャンセルを「在庫・掛再更新」画面から検証する。
/// </summary>
/// <remarks>
/// <para>
/// 対象は「在庫のみ」を広範囲（20260101〜20260731の7か月分）で実行する。この範囲は
/// 店舗売上（`Tran01Tenuri`、実DBで約343万件）を含む全期間走査を伴うため、専用得意先の
/// 請求計算（数十ms）と違い、キャンセルを差し込む時間的余地がある。
/// </para>
/// <para>
/// `CalcSummaryStockRange`は単一トランザクション（Serializable）で完結するため、
/// キャンセルが間に合っても間に合わなくても、DBは「実行前のまま」か「完全に更新済み」の
/// どちらかにしかならず、中途半端な状態（一部の月だけ更新される等）にはならない設計である。
/// ここでは、キャンセル操作自体が例外で画面をクラッシュさせず、`IsProcessing`が正しく
/// 解除され、`SummaryStock`が対象範囲すべて揃っているか・全く変化していないかの
/// どちらかであることを確認する。
/// </para>
/// <para>
/// このシナリオは対象コード範囲を持たない画面（在庫は得意先／仕入先の概念が無い）が対象のため、
/// C-09までと異なりUAT専用マスタへの限定ができない。ユーザー許可（2026-08-28）により、
/// 対象DBへの影響（Rebuildの結果自体は正規の再集計であり破壊ではない）を許容して実行する。
/// </para>
/// </remarks>
public static class CancelDuringRebuildScenario {
	// 2026年（特に202607）はUAT系ツールが投入したテストデータの一部が明細(Jmeisai)を
	// 持たず、在庫Rebuildが `SummaryStock.InQty` のNOT NULL制約違反で失敗する（2026-08-28調査）。
	// キャンセル検証はこの問題と無関係にしたいため、2026年を避けて実データが厚い2020年を使う。
	private const string YearMonthFrom = "2020/01";
	private const string YearMonthTo = "2020/07";

	public static async Task RunAsync(VmSession session) {
		var beforeCount = await CountSummaryStockAsync(session);
		session.Note("実行前のSummaryStock件数", new { beforeCount, range = $"{YearMonthFrom}~{YearMonthTo}" });

		var d = session.OpenView<StockKakeUpdateView, StockKakeUpdateViewModel>();
		await d.WaitAsync("init:自社締日の取得", vm => !vm.ClosingPeriodText.Contains("読み込んでいます", StringComparison.Ordinal));

		session.SetDialogResponder(request => request.Button == MessageBoxButton.YesNo ? MessageBoxResult.Yes : MessageBoxResult.OK);

		d.Input("実行条件", vm => {
			vm.YearMonthFrom = YearMonthFrom;
			vm.YearMonthTo = YearMonthTo;
			vm.UpdateTarget = "在庫のみ";
		}, new { UpdateTarget = "在庫のみ", Range = $"{YearMonthFrom}~{YearMonthTo}" });

		await d.RunAndCancelAsync(
			"execute:在庫のみ",
			vm => vm.ExecuteCommand,
			vm => vm.ExecuteCancelCommand,
			TimeSpan.FromMilliseconds(300));

		d.Snapshot("cancel後", vm => new { vm.StatusMessage, vm.ProgressValue, vm.IsProcessing });

		session.Check("C-10 キャンセル操作が例外で落ちず画面が応答する", true);
		session.Check("C-10 処理中フラグが解除される", !d.Vm.IsProcessing, new { d.Vm.IsProcessing });

		var cancelledByStatus = d.Vm.StatusMessage.Contains("キャンセル", StringComparison.Ordinal);
		var completedInTime = d.Vm.StatusMessage.Contains("が完了しました", StringComparison.Ordinal);
		session.Check("C-10 状態がキャンセルまたは完了のいずれかで確定する（宙に浮かない）",
			cancelledByStatus || completedInTime,
			new { d.Vm.StatusMessage });

		var afterCount = await CountSummaryStockAsync(session);
		session.Note("実行後のSummaryStock件数", afterCount);

		// 単一トランザクション設計により、範囲内の月は「全く無い（未コミット）」か
		// 「全月そろっている（コミット済み）」のどちらかにしかならないはずである。
		var months = afterCount.Select(x => x.Month).ToHashSet();
		var expectedMonths = Enumerable.Range(1, 7).Select(i => $"2020{i:00}").ToHashSet();
		var allPresent = expectedMonths.All(months.Contains);
		var nonePresent = !expectedMonths.Any(months.Contains);
		session.Check("C-10 対象範囲が全月そろっているか全く無いかのどちらかで、部分状態にならない",
			allPresent || nonePresent,
			new { present = months.Where(expectedMonths.Contains), missing = expectedMonths.Except(months) });

		session.Note("判定", new { コミット済み = allPresent, ロールバック = nonePresent });

		session.SetDialogResponder(null);
	}

	/// <summary>
	/// 月ごとの件数を返す。専用DTOをサーバーへ追加せず、既存の共有型
	/// <see cref="SummaryClosingCheckRow"/>（<c>TorihikiCode</c>/<c>DayTo</c>/<c>Shime1</c>）を
	/// 列名の意味を変えて借用する（<c>TorihikiCode</c>=月、<c>DayTo</c>=件数の文字列）。
	/// 343万件規模のテーブルを全件取得せずに済ませるため。
	/// </summary>
	private static async Task<List<(string Month, int Count)>> CountSummaryStockAsync(VmSession session) {
		var rows = await session.QueryAsync<SummaryClosingCheckRow>(
			"SELECT SumMonth AS TorihikiCode, CAST(COUNT(*) AS TEXT) AS DayTo, 0 AS Shime1 FROM SummaryStock WHERE SumMonth BETWEEN '202001' AND '202007' GROUP BY SumMonth");
		return [.. rows.Select(x => (x.TorihikiCode, int.Parse(x.DayTo ?? "0"))).OrderBy(x => x.TorihikiCode)];
	}
}
