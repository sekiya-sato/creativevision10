using System.Windows;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels._31Monthly;
using CvWpfclient.Views._31Monthly;

namespace UatVm.Scenarios;

/// <summary>
/// 請求計算画面（`BillingCalculationView`）をVM駆動で通す。
/// Phase 0 の完了条件（実Viewを生成して請求計算が1件完走し、証跡が出ること）を満たすためのシナリオ。
/// </summary>
/// <remarks>
/// 画面の入力値がサーバーへ渡り、結果が画面へ戻ることを確認する。金額の再検算は
/// `Doc/spec/tools/summaryreconcile` が担うため、ここでは行わない。
/// </remarks>
public static class BillingCalculationScenario {
	/// <summary>請求月（yyyy/MM）。</summary>
	public static string BillingMonth { get; set; } = "2026/07";
	/// <summary>対象得意先コード（範囲の開始・終了に同じ値を使う）。</summary>
	public static string TokuiCode { get; set; } = "000002";
	/// <summary>実際に請求計算を実行するか。falseなら入力検証だけを確認しDBへ書かない。</summary>
	public static bool Execute { get; set; } = true;

	public static async Task RunAsync(VmSession session) {
		var d = session.OpenView<BillingCalculationView, BillingCalculationViewModel>();

		// BaseWindow が表示時に InitCommand を自動実行する（実利用と同じ経路）。その完了を待つ。
		await d.WaitAsync("init:締日一覧の取得", vm => vm.ShimeItems.Count > 0);
		d.Snapshot("init後", vm => new {
			vm.StatusMessage,
			shime = vm.ShimeItems.Select(x => new { x.Value, x.Name }),
			vm.SelectedShime,
		});
		session.Check("init:締日が1件以上取得できる", d.Vm.ShimeItems.Count > 0, new { count = d.Vm.ShimeItems.Count });

		await CheckInputValidationAsync(session, d);

		if (!Execute) {
			session.Note("execute:スキップ", new { reason = "--no-execute が指定されました" });
			return;
		}

		await ExecuteBillingAsync(session, d);
	}

	/// <summary>
	/// 入力検証（C-08）。不正入力ではサーバーへ送信せず、警告ダイアログだけが出ることを確認する。
	/// </summary>
	private static async Task CheckInputValidationAsync(VmSession session, ViewDriver<BillingCalculationViewModel> d) {
		// 請求月の形式不正
		session.ClearDialogs();
		d.Input("請求月=不正値", vm => vm.BillingMonth = "2026/13/99", new { BillingMonth = "2026/13/99" });
		await d.RunAsync("execute:請求月不正", vm => vm.ExecuteCommand);
		var warned = session.Dialogs.Where(x => x.Request.Image == MessageBoxImage.Warning).ToList();
		session.Check("C-08 請求月が不正なら警告のみで送信しない",
			warned.Count == 1 && !d.Vm.IsProcessing && d.Vm.ProgressValue == 0,
			new { warnings = warned.Select(x => x.Request.Message), d.Vm.ProgressValue, d.Vm.IsProcessing });

		// コード範囲の逆転
		session.ClearDialogs();
		d.Input("コード範囲=逆転", vm => {
			vm.BillingMonth = BillingMonth;
			vm.TorihikiCodeFrom = "999999";
			vm.TorihikiCodeTo = "000001";
		}, new { From = "999999", To = "000001" });
		await d.RunAsync("execute:コード範囲逆転", vm => vm.ExecuteCommand);
		warned = [.. session.Dialogs.Where(x => x.Request.Image == MessageBoxImage.Warning)];
		session.Check("C-08 コード範囲が逆なら警告のみで送信しない",
			warned.Count == 1 && d.Vm.ProgressValue == 0,
			new { warnings = warned.Select(x => x.Request.Message), d.Vm.ProgressValue });
	}

	/// <summary>
	/// 請求計算を実際に実行し、画面へ結果が戻ることを確認する。
	/// </summary>
	private static async Task ExecuteBillingAsync(VmSession session, ViewDriver<BillingCalculationViewModel> d) {
		session.ClearDialogs();

		// 実行確認ダイアログの本文も検証対象にする。想定外の問い合わせにはNoを返して止める。
		session.SetDialogResponder(request => {
			if (request.Button == MessageBoxButton.YesNo) {
				return request.Message.Contains("請求計算を実行しますか", StringComparison.Ordinal)
					? MessageBoxResult.Yes
					: MessageBoxResult.No;
			}
			return MessageBoxResult.OK;
		});

		d.Input("実行条件", vm => {
			vm.BillingMonth = BillingMonth;
			vm.SelectedShime = vm.ShimeItems.First().Value;
			vm.TorihikiCodeFrom = TokuiCode;
			vm.TorihikiCodeTo = TokuiCode;
			vm.IsReissue = false;
		}, new { BillingMonth, TokuiCode, shime = d.Vm.ShimeItems.First().Value, IsReissue = false });

		await d.RunAsync("execute:請求計算", vm => vm.ExecuteCommand);
		d.Snapshot("execute後", vm => new { vm.StatusMessage, vm.WarningMessage, vm.ProgressValue, vm.IsProcessing });

		var confirm = session.Dialogs.FirstOrDefault(x => x.Request.Button == MessageBoxButton.YesNo);
		session.Check("実行確認ダイアログが出てYesで進む",
			confirm != null && confirm.Result == MessageBoxResult.Yes,
			new { message = confirm?.Request.Message, answered = confirm?.Result.ToString() });

		var errors = session.Dialogs.Where(x => x.Request.Image == MessageBoxImage.Error).ToList();
		session.Check("エラーダイアログが出ない", errors.Count == 0,
			new { errors = errors.Select(x => x.Request.Message) });

		session.CheckEqual("進捗が100になる", 100, d.Vm.ProgressValue);
		session.Check("完了メッセージが画面へ戻る",
			d.Vm.StatusMessage.Contains("完了", StringComparison.Ordinal),
			new { d.Vm.StatusMessage });
		session.Check("処理中フラグが解除される", !d.Vm.IsProcessing);

		var completed = session.Dialogs.FirstOrDefault(x =>
			x.Request.Image == MessageBoxImage.Information && x.Request.Message.Contains("完了", StringComparison.Ordinal));
		session.Check("完了ダイアログに件数が入る",
			completed != null && completed.Request.Message.Contains("件を処理しました", StringComparison.Ordinal),
			new { message = completed?.Request.Message });

		// E7（親子締日不一致）は非ブロック警告。出た場合は内容を証跡へ残す。
		session.Note("E7:警告の有無", new {
			hasWarning = !string.IsNullOrEmpty(d.Vm.WarningMessage),
			d.Vm.WarningMessage,
		});

		session.SetDialogResponder(null);
	}
}
