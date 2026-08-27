using CvWpfclient.ViewModels._31Monthly;
using CvWpfclient.Views._31Monthly;
using System.Windows;
using UatVm.Seed;

namespace UatVm.Scenarios;

/// <summary>
/// C-03 E7（親子締日ワーニング）を請求計算画面から検証する。
/// </summary>
/// <remarks>
/// <para>
/// E7は**非ブロック**警告である。「警告が出ること」と「それでも処理が続くこと」の両方が要件で、
/// どちらもSQL層では確かめられない。`summaryreconcile -- paysakicheck` は検査SQLの発火までを見るので、
/// ここでは画面の`WarningMessage`と実際に出るダイアログ、そして完走までを確認する。
/// </para>
/// <para>
/// 不一致の組と一致の組を両方用意してあるため、コード範囲を変えるだけで
/// 「警告が出る」「出ない」を同一実行内で確認できる（途中でDBを書き換えない）。
/// </para>
/// </remarks>
public static class PaysakiWarningScenario {
	private static PaysakiSeeder.Result? _seeded;

	public static void Seeder(string dbPath) {
		_seeded = PaysakiSeeder.Seed(dbPath, message => Console.WriteLine($"[seed] {message}"));
	}

	public static async Task RunAsync(VmSession session) {
		var seeded = _seeded
			?? throw new InvalidOperationException("シードが実行されていません。Options.Seed に Seeder を設定してください。");

		session.Note("seed:親子関係", new {
			mismatch = new { child = seeded.MismatchChildCode, childShime = seeded.ChildShime, parent = seeded.MismatchParentCode, parentShime = seeded.MismatchParentShime },
			match = new { child = seeded.MatchChildCode, childShime = seeded.ChildShime, parent = seeded.MatchParentCode, parentShime = seeded.MatchParentShime },
		});

		var d = session.OpenView<BillingCalculationView, BillingCalculationViewModel>();
		await d.WaitAsync("init:締日一覧の取得", vm => vm.ShimeItems.Count > 0);

		session.SetDialogResponder(request => request.Button == MessageBoxButton.YesNo
			? (request.Message.Contains("請求計算を実行しますか", StringComparison.Ordinal)
				? MessageBoxResult.Yes
				: MessageBoxResult.No)
			: MessageBoxResult.OK);

		// 不一致の子だけを対象にする → E7が出て、かつ処理は続くこと。
		await RunCaseAsync(session, d, seeded.MismatchChildCode, expectWarning: true, label: "不一致");

		// 一致の子だけを対象にする → E7が出ないこと。
		await RunCaseAsync(session, d, seeded.MatchChildCode, expectWarning: false, label: "一致");

		session.SetDialogResponder(null);
	}

	private static async Task RunCaseAsync(
		VmSession session,
		ViewDriver<BillingCalculationViewModel> d,
		string childCode,
		bool expectWarning,
		string label) {

		session.ClearDialogs();
		d.Input($"{label} 実行条件", vm => {
			vm.BillingMonth = "2026/07";
			vm.SelectedShime = PaysakiSeeder.ChildShime;
			vm.TorihikiCodeFrom = childCode;
			vm.TorihikiCodeTo = childCode;
			vm.IsReissue = false;
		}, new { code = childCode, shime = PaysakiSeeder.ChildShime });

		await d.RunAsync($"execute:{label}", vm => vm.ExecuteCommand);
		d.Snapshot($"{label} execute後", vm => new { vm.WarningMessage, vm.StatusMessage, vm.ProgressValue });

		var warningDialogs = session.Dialogs
			.Where(x => x.Request.Image == MessageBoxImage.Warning)
			.ToList();
		var hasWarningText = !string.IsNullOrEmpty(d.Vm.WarningMessage);

		if (expectWarning) {
			session.Check("C-03 親子締日が不一致なら画面へ警告文が出る", hasWarningText,
				new { d.Vm.WarningMessage });
			session.Check("C-03 警告ダイアログが1回出る", warningDialogs.Count == 1,
				new { count = warningDialogs.Count, messages = warningDialogs.Select(x => x.Request.Message) });
			// 非ブロックであることが要件。警告が出ても処理は完走しなければならない。
			session.Check("C-03 警告はブロックせず処理が完走する", d.Vm.ProgressValue == 100,
				new { d.Vm.ProgressValue, d.Vm.StatusMessage });
			var errors = session.Dialogs.Where(x => x.Request.Image == MessageBoxImage.Error).ToList();
			session.Check("C-03 警告はエラー扱いにならない", errors.Count == 0,
				new { errors = errors.Select(x => x.Request.Message) });
		}
		else {
			session.Check("C-03 親子締日が一致なら警告文が出ない", !hasWarningText,
				new { d.Vm.WarningMessage });
			session.Check("C-03 一致時は警告ダイアログが出ない", warningDialogs.Count == 0,
				new { count = warningDialogs.Count, messages = warningDialogs.Select(x => x.Request.Message) });
			session.Check("C-03 一致時も処理が完走する", d.Vm.ProgressValue == 100,
				new { d.Vm.ProgressValue });
		}
	}
}
