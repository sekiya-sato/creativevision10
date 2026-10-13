using System.IO;
using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using CodeShare;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels._06Uriage;
using CvWpfclient.Views._06Uriage;

namespace UatVm.Scenarios;

/// <summary>
/// 納品書（R2）と専用伝票納品書で、PDF出力の失敗時に IsPrint を更新せず、成功時だけ発行済みにすることを確認する。
/// <list type="bullet">
/// <item>エラー: サーバの qfm を一時的にリネームして PDF 生成を失敗させる（finally で必ず戻す）</item>
/// <item>正常: qfm を戻して印刷し、IsPrint=1 になること</item>
/// </list>
/// <para>
/// 対象は1伝票（既定 Tran00Uriage Id=50317 / 2026/07/21、環境変数 UATVM_DENID で変更可）。IsPrint を更新し、終了時に元の値へ戻す。
/// 実行: <c>UatVm nouhinprintfail --manage-server</c>（CvWpfclient フォルダから）
/// </para>
/// </summary>
public static class NouhinPrintFailScenario {
	const long DefaultDenId = 50317;
	const string PdfShownPrefix = "PDFを表示しました: ";

	public static async Task RunAsync(VmSession session) {
		var denId = long.TryParse(Environment.GetEnvironmentVariable("UATVM_DENID"), out var v) ? v : DefaultDenId;
		var formDir = FindFormDir();
		RestoreForms(formDir);
		var before = await GetDenAsync(session, denId);
		if (!session.Check("対象伝票あり", before is not null, new { denId })) return;
		var originalIsPrint = before!.IsPrint;
		var day = $"{before.DenDay[..4]}/{before.DenDay[4..6]}/{before.DenDay[6..]}";
		session.Note("対象", new { denId, before.DenDay, before.Kubun, originalIsPrint, formDir });
		session.SetDialogResponder(request => request.Button is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel
			? MessageBoxResult.Yes : MessageBoxResult.OK);
		try {
			await R2Async(session, formDir, denId, day);
			await CustomAsync(session, formDir, denId, day);
		}
		finally {
			RestoreForms(formDir);
			session.SetDialogResponder(null);
			await SetIsPrintAsync(session, denId, originalIsPrint);
			var last = await GetDenAsync(session, denId);
			session.Check("終了時 IsPrint を元に戻した", last?.IsPrint == originalIsPrint, new { originalIsPrint, last?.IsPrint });
			ClosePdfViews();
		}
	}

	/// <summary>納品書（R2）: 印刷成功後に IsPrint を更新する</summary>
	static async Task R2Async(VmSession session, string formDir, long denId, string day) {
		const string form = "NouhinBookPrint.qfm";
		await SetIsPrintAsync(session, denId, 0);
		var d = session.OpenView<NouhinBookPrintView, NouhinBookPrintViewModel>();
		d.Input("R2:条件", vm => {
			vm.DenDayFrom = day; vm.DenDayTo = day;
			vm.DenNoFrom = vm.DenNoTo = denId.ToString();
			vm.IsPendingOnly = true;
		}, new { day, denId });

		// エラー: qfm が無いため PDF 生成に失敗する
		HideForm(formDir, form);
		session.ClearDialogs();
		await d.RunAsync("R2:印刷(エラー)", vm => vm.PrintCommand);
		RestoreForms(formDir);
		var err = await GetDenAsync(session, denId);
		session.Check("R2:エラー時はエラー表示", HasError(session), Dialogs(session, d.Vm.Message));
		session.Check("R2:エラー時 IsPrint=0 のまま", err?.IsPrint == 0, new { err?.IsPrint });

		// 正常
		session.ClearDialogs();
		await d.RunAsync("R2:印刷(正常)", vm => vm.PrintCommand);
		var ok = await GetDenAsync(session, denId);
		session.Check("R2:正常時は発行済みメッセージ", d.Vm.Message == "1件を発行済みにしました" && !HasError(session), Dialogs(session, d.Vm.Message));
		session.Check("R2:正常時 IsPrint=1", ok?.IsPrint == 1, new { ok?.IsPrint });
		ClosePdfViews();
		d.View.Close();
	}

	/// <summary>専用伝票納品書: 印刷成功した伝票だけを［発行済にする］で更新する</summary>
	static async Task CustomAsync(VmSession session, string formDir, long denId, string day) {
		const string form = "NouhinBookPrintCustom.qfm";
		await SetIsPrintAsync(session, denId, 0);
		var d = session.OpenView<NouhinBookPrintCustomView, NouhinBookPrintCustomViewModel>();
		d.Input("専用:条件", vm => {
			vm.DenDayFrom = day; vm.DenDayTo = day;
			vm.DenNoFrom = vm.DenNoTo = denId.ToString();
			vm.IsPendingOnly = true;
		}, new { day, denId });

		// エラー: 印刷失敗後の［発行済にする］は対象なし
		HideForm(formDir, form);
		session.ClearDialogs();
		await d.RunAsync("専用:印刷(エラー)", vm => GetDoOutputPdfCommand(vm));
		RestoreForms(formDir);
		session.Check("専用:エラー時はエラー表示", HasError(session), Dialogs(session, d.Vm.Message));
		session.ClearDialogs();
		await d.RunAsync("専用:発行済にする(エラー後)", vm => vm.MarkPrintedCommand);
		var err = await GetDenAsync(session, denId);
		session.Check("専用:エラー後は印刷済み対象なし", d.Vm.Message.StartsWith("印刷が完了した伝票がありません"), Dialogs(session, d.Vm.Message));
		session.Check("専用:エラー後 IsPrint=0 のまま", err?.IsPrint == 0, new { err?.IsPrint });

		// 正常: 印刷 → ［発行済にする］
		session.ClearDialogs();
		await d.RunAsync("専用:印刷(正常)", vm => GetDoOutputPdfCommand(vm));
		session.Check("専用:正常時はPDF表示", d.Vm.Message.StartsWith(PdfShownPrefix) && !HasError(session), Dialogs(session, d.Vm.Message));
		var printed = await GetDenAsync(session, denId);
		session.Check("専用:印刷だけでは IsPrint=0", printed?.IsPrint == 0, new { printed?.IsPrint });
		session.ClearDialogs();
		await d.RunAsync("専用:発行済にする(正常)", vm => vm.MarkPrintedCommand);
		var ok = await GetDenAsync(session, denId);
		session.Check("専用:発行済みメッセージ", d.Vm.Message == "1件を発行済みにしました", Dialogs(session, d.Vm.Message));
		session.Check("専用:正常時 IsPrint=1", ok?.IsPrint == 1, new { ok?.IsPrint });
		ClosePdfViews();
		d.View.Close();
	}

	static bool HasError(VmSession session) =>
		session.Dialogs.Any(x => x.Request.Image is MessageBoxImage.Warning or MessageBoxImage.Error);

	static object Dialogs(VmSession session, string message) =>
		new { message, dialogs = session.Dialogs.Select(x => $"{x.Request.Image}: {x.Request.Message}").ToList() };

	static async Task<Tran00Uriage?> GetDenAsync(VmSession session, long denId) =>
		(await session.QueryAsync<Tran00Uriage>($"where Id={denId}")).FirstOrDefault();

	/// <summary>IsPrint だけを Vdu 照合付きで更新する（伝票の他の列は変えない）</summary>
	static async Task SetIsPrintAsync(VmSession session, long denId, int isPrint) {
		var den = await GetDenAsync(session, denId) ?? throw new InvalidOperationException($"伝票 {denId} がありません");
		if (den.IsPrint == isPrint) return;
		var update = new PartialUpdateParam(typeof(Tran00Uriage), [nameof(Tran00Uriage.IsPrint)],
			[new PartialUpdateRow(den.Id, den.Vdu, [isPrint.ToString()])]);
		var reply = await AppGlobal.GetGrpcService<ICoreService>().QueryMsgAsync(new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg201_Op_Execute,
			DataType = typeof(PartialUpdateParam),
			DataMsg = Common.SerializeObject(update),
		}, AppGlobal.GetDefaultCallContext());
		if (reply.Code < 0) throw new InvalidOperationException($"IsPrint 更新失敗: {reply.DataMsg}");
		session.Note("IsPrint 設定", new { denId, isPrint });
	}

	const string HiddenSuffix = ".uatvm-hidden";

	static string FindFormDir() {
		for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir is not null; dir = dir.Parent) {
			var path = Path.Combine(dir.FullName, "printform");
			if (File.Exists(Path.Combine(path, "NouhinBookPrint.qfm")) || File.Exists(Path.Combine(path, "NouhinBookPrint.qfm" + HiddenSuffix))) return path;
		}
		throw new DirectoryNotFoundException("printform が見つかりません。CvWpfclient フォルダから実行してください。");
	}

	static void HideForm(string formDir, string form) =>
		File.Move(Path.Combine(formDir, form), Path.Combine(formDir, form + HiddenSuffix));

	/// <summary>隠した qfm を戻す（前回の異常終了で残ったものも戻す）</summary>
	static void RestoreForms(string formDir) {
		foreach (var hidden in Directory.GetFiles(formDir, "*" + HiddenSuffix)) {
			var original = hidden[..^HiddenSuffix.Length];
			if (!File.Exists(original)) File.Move(hidden, original);
		}
	}

	static void ClosePdfViews() {
		foreach (var view in Application.Current.Windows.OfType<CvWpfclient.Views.Sub.WebPdfView>().ToList()) {
			try { view.Close(); }
			catch (InvalidOperationException) { /* 既に閉じている */ }
		}
	}

	static IAsyncRelayCommand GetDoOutputPdfCommand(BaseReportViewModel viewModel) =>
		typeof(BaseReportViewModel).GetProperty("DoOutputPdfCommand", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
			?.GetValue(viewModel) as IAsyncRelayCommand
		?? throw new InvalidOperationException("DoOutputPdfCommand を取得できません。");
}
