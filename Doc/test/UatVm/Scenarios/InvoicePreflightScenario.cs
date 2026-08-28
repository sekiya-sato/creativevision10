using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels._06Uriage;
using CvWpfclient.Views._06Uriage;

namespace UatVm.Scenarios;

/// <summary>
/// 請求書印刷の税率別内訳に対する印刷前検査を、実View・実gRPC経路で実行する。
/// 税率別内訳を復元済みの対象で、印刷前検査が警告なく通過することを確認する。
/// </summary>
public static class InvoicePreflightScenario {
	private const string TargetDay = "2026/07/31";

	public static async Task RunAsync(VmSession session) {
		var d = session.OpenView<SeikyuBalanceDetailView, SeikyuBalanceDetailViewModel>();
		d.Input("請求日", vm => vm.SeikyuDay = TargetDay, new { SeikyuDay = TargetDay });
		session.ClearDialogs();

		await d.RunAsync("execute:請求書印刷前検査", vm => GetOutputCommand(vm));

		var warning = session.Dialogs.LastOrDefault(x => x.Request.Image == System.Windows.MessageBoxImage.Warning);
		session.Check("D-05 印刷前検査が警告なく通過する", warning == null);
	}

	private static IAsyncRelayCommand GetOutputCommand(SeikyuBalanceDetailViewModel viewModel) {
		var property = typeof(BaseReportViewModel).GetProperty(
			"DoOutputPdfCommand",
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		return property?.GetValue(viewModel) as IAsyncRelayCommand
			?? throw new InvalidOperationException("DoOutputPdfCommand を取得できません。");
	}
}
