using System.Windows;
using CvWpfclient.ViewModels._04Juchu;
using CvWpfclient.ViewModels._06Uriage;
using CvWpfclient.ViewModels._08Zaiko;
using CvWpfclient.ViewModels.Sub;
using CvWpfclient.Views._04Juchu;
using CvWpfclient.Views._06Uriage;
using CvWpfclient.Views._08Zaiko;
using CvWpfclient.Views.Sub;

namespace UatVm.Scenarios;

/// <summary>
/// 伝票入力12画面に追加した「新規登録」ボタン（GoToNewCommand）の実行時確認。
/// 一覧に既存伝票がある状態から新規登録ボタンを押しても、詳細タブへ新規伝票（Id=0）で遷移し、
/// 一覧選択中の伝票を巻き込まないことを検証する。DBへの書き込みは行わない。
/// </summary>
public static class DenpyoNewButtonScenario {
	static bool _rangeDialogHookRegistered;

	public static async Task RunAsync(VmSession session) {
		// 一覧取得前に必ず出る条件入力ダイアログ（RangeInputParamView）を、
		// 何も絞り込まない「OK」相当（RangeInputParamViewModel.OkCommand）で自動的に閉じる。
		// マウス・キー操作は使わず、既存のViewModelコマンドを直接駆動する点はハーネスの方針と同じ。
		EnsureRangeDialogAutoAnswered();

		await VerifyJuchuAsync(session);
		await VerifyShopUriageAsync(session);
		await VerifyStockAsync(session);
		await VerifyNyukinAsync(session);
	}

	static void EnsureRangeDialogAutoAnswered() {
		if (_rangeDialogHookRegistered) return;
		_rangeDialogHookRegistered = true;
		EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
			new RoutedEventHandler((sender, _) => {
				if (sender is RangeInputParamView view && view.DataContext is RangeInputParamViewModel vm) {
					vm.OkCommand.Execute(null);
				}
			}));
	}

	static string Today() => DateTime.Now.ToString("yyyyMMdd");

	static async Task VerifyJuchuAsync(VmSession session) {
		const string label = "受注入力";
		var d = session.OpenView<JuchuInputView, JuchuInputViewModel>();
		if (!await d.WaitAsync($"{label}:一覧取得完了", vm => vm.ListData.Count > 0)) return;
		if (!session.Check($"{label}:前提（一覧に既存伝票がある）", d.Vm.ListData.Count > 0, new { d.Vm.ListData.Count })) return;

		d.Run($"{label}:新規登録", vm => vm.GoToNewCommand);
		session.Check($"{label}:新規登録で詳細タブへ遷移", d.Vm.SelectedTabIndex == 1, new { d.Vm.SelectedTabIndex });
		session.Check($"{label}:新規登録でId=0", d.Vm.Current.Id == 0, new { d.Vm.Current.Id });
		session.Check($"{label}:新規登録で伝票日付が当日", d.Vm.Current.DenDay == Today(), new { d.Vm.Current.DenDay, Today = Today() });
		session.Check($"{label}:新規登録で明細が空", (d.Vm.Current.Jmeisai?.Count ?? 0) == 0, new { Count = d.Vm.Current.Jmeisai?.Count ?? 0 });

		d.Run($"{label}:一覧へ戻る", vm => vm.GoToListCommand);
		session.Check($"{label}:一覧へ戻るとタブが0", d.Vm.SelectedTabIndex == 0, new { d.Vm.SelectedTabIndex });
	}

	static async Task VerifyShopUriageAsync(VmSession session) {
		const string label = "店舗売上入力";
		var d = session.OpenView<ShopUriageInputView, ShopUriageInputViewModel>();
		if (!await d.WaitAsync($"{label}:一覧取得完了", vm => vm.ListData.Count > 0)) return;
		if (!session.Check($"{label}:前提（一覧に既存伝票がある）", d.Vm.ListData.Count > 0, new { d.Vm.ListData.Count })) return;

		d.Run($"{label}:新規登録", vm => vm.GoToNewCommand);
		session.Check($"{label}:新規登録で詳細タブへ遷移", d.Vm.SelectedTabIndex == 1, new { d.Vm.SelectedTabIndex });
		session.Check($"{label}:新規登録でId=0", d.Vm.Current.Id == 0, new { d.Vm.Current.Id });
		session.Check($"{label}:新規登録で伝票日付が当日", d.Vm.Current.DenDay == Today(), new { d.Vm.Current.DenDay, Today = Today() });
		session.Check($"{label}:新規登録で明細が空", (d.Vm.Current.Jmeisai?.Count ?? 0) == 0, new { Count = d.Vm.Current.Jmeisai?.Count ?? 0 });

		d.Run($"{label}:一覧へ戻る", vm => vm.GoToListCommand);
		session.Check($"{label}:一覧へ戻るとタブが0", d.Vm.SelectedTabIndex == 0, new { d.Vm.SelectedTabIndex });
	}

	static async Task VerifyStockAsync(VmSession session) {
		const string label = "棚卸入力";
		var d = session.OpenView<StockInputView, StockInputViewModel>();
		if (!await d.WaitAsync($"{label}:一覧取得完了", vm => vm.ListData.Count > 0)) return;
		if (!session.Check($"{label}:前提（一覧に既存伝票がある）", d.Vm.ListData.Count > 0, new { d.Vm.ListData.Count })) return;

		d.Run($"{label}:新規登録", vm => vm.GoToNewCommand);
		session.Check($"{label}:新規登録で詳細タブへ遷移", d.Vm.SelectedTabIndex == 1, new { d.Vm.SelectedTabIndex });
		session.Check($"{label}:新規登録でId=0", d.Vm.Current.Id == 0, new { d.Vm.Current.Id });
		session.Check($"{label}:新規登録で伝票日付が当日", d.Vm.Current.DenDay == Today(), new { d.Vm.Current.DenDay, Today = Today() });
		session.Check($"{label}:新規登録で明細が空", (d.Vm.Current.Jmeisai?.Count ?? 0) == 0, new { Count = d.Vm.Current.Jmeisai?.Count ?? 0 });

		d.Run($"{label}:一覧へ戻る", vm => vm.GoToListCommand);
		session.Check($"{label}:一覧へ戻るとタブが0", d.Vm.SelectedTabIndex == 0, new { d.Vm.SelectedTabIndex });
	}

	static async Task VerifyNyukinAsync(VmSession session) {
		const string label = "入金入力";
		var d = session.OpenView<NyukinInputView, NyukinInputViewModel>();
		if (!await d.WaitAsync($"{label}:一覧取得完了", vm => vm.ListData.Count > 0)) return;
		if (!session.Check($"{label}:前提（一覧に既存伝票がある）", d.Vm.ListData.Count > 0, new { d.Vm.ListData.Count })) return;

		d.Run($"{label}:新規登録", vm => vm.GoToNewCommand);
		session.Check($"{label}:新規登録で詳細タブへ遷移", d.Vm.SelectedTabIndex == 1, new { d.Vm.SelectedTabIndex });
		session.Check($"{label}:新規登録でId=0", d.Vm.Current.Id == 0, new { d.Vm.Current.Id });
		// 入金/支払はDenDayではなくKakeDay（掛計上日）を使う（BaseKinInputViewModel）。
		session.Check($"{label}:新規登録で掛計上日が当日", d.Vm.Current.KakeDay == Today(), new { d.Vm.Current.KakeDay, Today = Today() });
		session.Check($"{label}:新規登録で明細が空", (d.Vm.Current.Jmeisai?.Count ?? 0) == 0, new { Count = d.Vm.Current.Jmeisai?.Count ?? 0 });

		d.Run($"{label}:一覧へ戻る", vm => vm.GoToListCommand);
		session.Check($"{label}:一覧へ戻るとタブが0", d.Vm.SelectedTabIndex == 0, new { d.Vm.SelectedTabIndex });
	}
}
