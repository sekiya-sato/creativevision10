using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels._30HHT;
using CvWpfclient.Views._30HHT;

namespace UatVm.Scenarios;

/// <summary>
/// HHT メニューの帳票3画面（出荷指示明細書・移動明細書・即時移動明細書）を実画面で開いて印刷する。
/// <list type="bullet">
/// <item>3画面の JPG 保存と表示崩れ判定</item>
/// <item>各画面で条件を入れて印刷し、PDF が生成されること（0件メッセージでないこと）。PDF は画面画像フォルダへ保存する</item>
/// <item>移動明細書・即時移動明細書: 通常発行で IsPrint=1、再発行は IsPrint を変えずに印刷、再度の通常発行は0件</item>
/// </list>
/// <para>
/// データは開発DBの既存伝票を使う（TranVulcanHht 入庫 2026/09/09、Tran10IdoOut 2024/12/10、Tran05Ido 2022/05/13）。
/// IsPrint を更新するので、必ず開発DBの複製で実行する（<c>--sqlite &lt;複製DB&gt; --manage-server</c>）。
/// </para>
/// </summary>
public static class HhtPrintScenario {
	const string ScreenDirectory = "..\\Doc\\test\\uat20261005\\hht\\screens";
	const string HhtDay = "2026/09/09";
	const string IdoOutDay = "2024/12/10";
	const string IdoDay = "2022/05/13";
	const string PdfShownPrefix = "PDFを表示しました: ";

	public static async Task RunAsync(VmSession session) {
		var screens = Path.Combine(Path.GetFullPath(ScreenDirectory), DateTime.Now.ToString("yyyyMMdd_HHmmss"));
		Directory.CreateDirectory(screens);
		session.Note("実行方法", new { Command = "UatVm hhtprint --sqlite <複製DB> --manage-server", Screens = screens });
		session.SetDialogResponder(request => request.Button is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel
			? MessageBoxResult.Yes : MessageBoxResult.OK);
		try {
			await ShippingAsync(session, screens);
			await IdoAsync<IdoDetailBookPrintView, IdoDetailBookPrintViewModel, Tran10IdoOut>(session, screens, "02_IdoDetailBook", IdoOutDay);
			await IdoAsync<IdoSokuDetailBookPrintView, IdoSokuDetailBookPrintViewModel, Tran05Ido>(session, screens, "03_IdoSokuDetailBook", IdoDay);
		}
		finally {
			session.SetDialogResponder(null);
		}
	}

	/// <summary>出荷指示明細書（TranVulcanHht）</summary>
	static async Task ShippingAsync(VmSession session, string screens) {
		var d = session.OpenView<ShippingConfirmDetailPrintView, ShippingConfirmDetailPrintViewModel>();
		d.Input("出荷指示明細書:条件", vm => {
			vm.SelectedKubun = 3;
			vm.DenDayFrom = HhtDay;
			vm.DenDayTo = HhtDay;
		}, new { Kubun = 3, HhtDay });
		await HaibunScreenScenario.CaptureAsync(session, d.View, screens, "01_ShippingConfirmDetailPrint");
		session.ClearDialogs();
		await d.RunAsync("出荷指示明細書:印刷", vm => GetDoOutputPdfCommand(vm));
		await CheckPdfAsync(session, d.Vm.Message, screens, "01_ShippingConfirmDetailPrint");

		// 対象外の日付は0件（PDFを出さない）
		d.Input("出荷指示明細書:0件条件", vm => { vm.DenDayFrom = "2000/01/01"; vm.DenDayTo = "2000/01/01"; });
		session.ClearDialogs();
		await d.RunAsync("出荷指示明細書:0件印刷", vm => GetDoOutputPdfCommand(vm));
		session.Check("出荷指示明細書:0件はPDFを出さずメッセージ", !d.Vm.Message.StartsWith(PdfShownPrefix) && session.Dialogs.Count > 0,
			new { d.Vm.Message, Dialogs = session.Dialogs.Select(x => x.Request.Message).ToList() });
		ClosePdfViews();
	}

	/// <summary>移動明細書 / 即時移動明細書: 通常発行 → 再発行 → 再度の通常発行</summary>
	static async Task IdoAsync<TView, TViewModel, TDen>(VmSession session, string screens, string name, string day)
		where TView : Window, new()
		where TViewModel : BaseIdoDetailBookPrintViewModel<TDen>
		where TDen : TranAllHeader {
		var table = typeof(TDen).Name;
		var denDay = day.Replace("/", string.Empty);
		var before = await session.QueryAsync<TDen>($"where DenDay=@0 order by Id", denDay);
		if (!session.Check($"{name}:対象伝票あり・全件未発行", before.Count > 0 && before.All(x => IsPrint(x) == 0),
			new { table, denDay, Ids = before.Select(x => x.Id).ToList(), IsPrint = before.Select(IsPrint).ToList() })) return;

		var d = session.OpenView<TView, TViewModel>();
		d.Input($"{name}:条件(通常発行)", vm => {
			vm.IsNormalIssue = true;
			vm.DenDayFrom = day;
			vm.DenDayTo = day;
		}, new { IsNormalIssue = true, day });
		await HaibunScreenScenario.CaptureAsync(session, d.View, screens, name);

		session.ClearDialogs();
		await d.RunAsync($"{name}:通常発行", vm => vm.PrintCommand);
		session.Check($"{name}:通常発行の完了メッセージ", d.Vm.Message == $"{before.Count}件を発行済みにしました",
			new { d.Vm.Message, expected = before.Count });
		await CheckPdfAsync(session, LastPdfMessage(session, d.Vm.Message), screens, $"{name}_normal");
		var afterNormal = await session.QueryAsync<TDen>($"where DenDay=@0 order by Id", denDay);
		session.Check($"{name}:通常発行後 IsPrint=1", afterNormal.Count == before.Count && afterNormal.All(x => IsPrint(x) == 1),
			new { IsPrint = afterNormal.Select(IsPrint).ToList() });

		d.Input($"{name}:条件(再発行)", vm => vm.IsNormalIssue = false, new { IsNormalIssue = false });
		session.ClearDialogs();
		await d.RunAsync($"{name}:再発行", vm => vm.PrintCommand);
		await CheckPdfAsync(session, d.Vm.Message, screens, $"{name}_reissue");
		var afterReissue = await session.QueryAsync<TDen>($"where DenDay=@0 order by Id", denDay);
		session.Check($"{name}:再発行でIsPrint・Vdu不変",
			afterReissue.Count == afterNormal.Count && afterReissue.Zip(afterNormal).All(p => IsPrint(p.First) == 1 && p.First.Vdu == p.Second.Vdu),
			new { IsPrint = afterReissue.Select(IsPrint).ToList() });

		d.Input($"{name}:条件(再度通常発行)", vm => vm.IsNormalIssue = true, new { IsNormalIssue = true });
		session.ClearDialogs();
		await d.RunAsync($"{name}:再度通常発行", vm => vm.PrintCommand);
		session.Check($"{name}:再度通常発行は0件メッセージ",
			d.Vm.Message.StartsWith("未発行の伝票がありません") && session.Dialogs.Any(x => x.Request.Message.StartsWith("未発行の伝票がありません")),
			new { d.Vm.Message });
		ClosePdfViews();
	}

	static int IsPrint<TDen>(TDen den) => (int)(typeof(TDen).GetProperty("IsPrint")!.GetValue(den) ?? -1);

	/// <summary>
	/// 通常発行は PDF 表示後に「n件を発行済みにしました」でメッセージが上書きされるため、
	/// PDF名は開いた PDF表示画面のタイトル（URL入り）から取る。
	/// </summary>
	static string LastPdfMessage(VmSession session, string message) {
		var title = Application.Current.Windows.OfType<CvWpfclient.Views.Sub.WebPdfView>().LastOrDefault()?.Title ?? string.Empty;
		var i = title.LastIndexOf("/wrk/", StringComparison.Ordinal);
		session.Note("PDF表示画面", new { title, message });
		return i >= 0 ? PdfShownPrefix + title[(i + 5)..] : message;
	}

	/// <summary>PDF 生成を確認し、サーバの wrk から取得して保存する</summary>
	static async Task CheckPdfAsync(VmSession session, string message, string screens, string name) {
		var errors = session.Dialogs.Where(x => x.Request.Image is MessageBoxImage.Warning or MessageBoxImage.Error)
			.Select(x => x.Request.Message).ToList();
		if (!session.Check($"{name}:PDF生成", message.StartsWith(PdfShownPrefix) && errors.Count == 0, new { message, errors })) return;
		var file = message[PdfShownPrefix.Length..].Trim();
		try {
			// CvServer は HTTP/2 のみで待ち受けるため、HTTP/1.1 の既定では 400 になる
			using var http = new HttpClient {
				DefaultRequestVersion = System.Net.HttpVersion.Version20,
				DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
			};
			var bytes = await http.GetByteArrayAsync($"{AppGlobal.Url}/wrk/{file}");
			var path = Path.Combine(screens, $"{name}.pdf");
			await File.WriteAllBytesAsync(path, bytes);
			session.Check($"{name}:PDF取得", bytes.Length > 1000 && bytes.AsSpan(0, 4).SequenceEqual("%PDF"u8),
				new { path, bytes = bytes.Length });
		}
		catch (Exception ex) {
			session.Fail($"{name}:PDF取得", ex.Message);
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
