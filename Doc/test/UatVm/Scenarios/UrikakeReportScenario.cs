using System.Globalization;
using System.IO;
using System.Windows;
using CvWpfclient.ViewModels._06Uriage;
using CvWpfclient.Views._06Uriage;

namespace UatVm.Scenarios;

/// <summary>
/// 売掛金管理表の目視用PDFを出力する（照合はしない）。
/// <para>印刷のみでDBを更新しないが、他のUATと同じく複製DBと専用サーバで実行する（<c>--sqlite &lt;複製DB&gt; --manage-server</c>）。</para>
/// <para>年月は開発DBで SummaryUriKake の行がある直近月（2024/08）。PDFは out/urikakereport-&lt;日時&gt; へ保存する。</para>
/// </summary>
public static class UrikakeReportScenario {
	const string TargetYearMonth = "2024/08";

	public static async Task RunAsync(VmSession session) {
		var outDir = Path.GetFullPath(Path.Combine("..", "Doc", "test", "UatVm", "out", "urikakereport-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)));
		Directory.CreateDirectory(outDir);
		session.Note("実行条件", new { outDir, TargetYearMonth });

		session.SetDialogResponder(request => request.Button is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel
			? MessageBoxResult.Yes : MessageBoxResult.OK);
		try {
			var d = session.OpenView<UrikakeBalanceReportView, UrikakeBalanceReportViewModel>();
			d.Input("売掛金管理表 PDF条件", vm => { vm.TargetYearMonth = TargetYearMonth; vm.IsActiveOnly = true; });
			await PaymentReportsScenario.PrintAsync(session, d, outDir, "01_UrikakeBalance_" + TargetYearMonth.Replace("/", string.Empty));
		}
		finally {
			PaymentReportsScenario.ClosePdfViews();
			session.SetDialogResponder(null);
		}
	}
}
