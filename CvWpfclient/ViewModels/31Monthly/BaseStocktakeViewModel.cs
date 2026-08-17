using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using Grpc.Core;
using System.Globalization;
using System.Windows;

namespace CvWpfclient.ViewModels._31Monthly;

/// <summary>
/// 棚卸開始処理・棚卸確定処理の共通部分。
/// <para>
/// 旧CV.netの棚卸7段階のうち、システムが行う「4. 棚卸開始処理」と「7. 棚卸確定処理」に対応する。
/// 仕様は `Doc/spec/2026-08-17_旧cvnet比較_仕様決定判断材料.md` 8.1 / 8.4 を参照する。
/// </para>
/// <para>
/// どちらも棚卸年月と対象倉庫を指定して実行するバッチで、サーバー側の <c>StocktakeDb</c> を
/// ストリーミング(<c>QueryMsgStreamAsync</c>)で呼ぶ。進捗表示と取り消しの作りは
/// 在庫・掛再更新(<see cref="StockKakeUpdateViewModel"/>)に合わせている。
/// </para>
/// <para>
/// 対象倉庫を選ばなかった場合は全倉庫が対象になる。旧CV.netは得意先をFROM-TOで範囲指定して
/// 一覧から1件ずつ「する/しない」を選ぶ形だったが、CV10 では複数選択ダイアログで同じことを行う。
/// </para>
/// </summary>
public abstract partial class BaseStocktakeViewModel : BaseViewModel {
	/// <summary>実行するサーバー処理</summary>
	protected abstract CvFlag TargetFlag { get; }
	/// <summary>確認ダイアログとメッセージに出す処理名</summary>
	protected abstract string ActionName { get; }
	/// <summary>実行結果の件数につける単位（「保存しました」「作成しました」の主語）</summary>
	protected abstract string ResultUnit { get; }

	[ObservableProperty]
	public partial string TanaMonth { get; set; } = DateTime.Now.ToString("yyyy/MM", CultureInfo.InvariantCulture);

	/// <summary>対象倉庫。空なら全倉庫</summary>
	[ObservableProperty]
	public partial string SokoText { get; set; } = "（全倉庫）";

	[ObservableProperty]
	public partial string StatusMessage { get; set; } = "棚卸年月を yyyy/MM 形式で入力し、実行を押してください。";

	[ObservableProperty]
	public partial bool IsProcessing { get; set; }

	[ObservableProperty]
	public partial int ProgressValue { get; set; }

	/// <summary>選択した倉庫Id。空なら全倉庫</summary>
	protected List<long> SokoIds { get; } = [];

	/// <summary>入力社員Id。調整伝票の入力者になる</summary>
	protected long IdShain { get; private set; }

	[ObservableProperty]
	public partial string ShainText { get; set; } = "（未設定）";

	/// <summary>
	/// 対象倉庫を複数選択する。在庫を持つ店種区分(0:倉庫 / 3:売仕店 / 6:直営店)だけを候補にする。
	/// </summary>
	[RelayCommand]
	private void SelectSoko() {
		var selected = PrintPdfHelper.ShowMultiSelectDialog<MasterTokui>(
			this, typeof(MasterTokui), "TenType in (0,3,6)", "Code", SokoIds);
		if (selected == null) {
			return;
		}
		SokoIds.Clear();
		SokoIds.AddRange(selected.Select(x => x.Id));
		SokoText = SokoIds.Count == 0
			? "（全倉庫）"
			: $"{SokoIds.Count} 件選択：{string.Join(" / ", selected.Take(5).Select(x => x.Code))}"
				+ (SokoIds.Count > 5 ? " ほか" : string.Empty);
	}

	/// <summary>対象倉庫の選択を解除して全倉庫へ戻す</summary>
	[RelayCommand]
	private void ClearSoko() {
		SokoIds.Clear();
		SokoText = "（全倉庫）";
	}

	[RelayCommand]
	private void SelectShain() {
		var shain = PrintPdfHelper.ShowSelectDialog<MasterShain>(this, typeof(MasterShain), "", "Code", IdShain);
		if (shain == null) {
			return;
		}
		IdShain = shain.Id;
		ShainText = $"({shain.Code}) {shain.Name}";
	}

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task ExecuteAsync(CancellationToken cancellationToken) {
		if (!TryParseYearMonth(TanaMonth, out var yyyymm)) {
			ShowWarn($"棚卸年月の形式が不正です: {TanaMonth}");
			return;
		}
		if (!ValidateBeforeExecute(out var errorMessage)) {
			ShowWarn(errorMessage);
			return;
		}
		var target = SokoIds.Count == 0 ? "全倉庫" : $"{SokoIds.Count} 倉庫";
		if (MessageEx.ShowQuestionDialog($"{yyyymm} / {target} の{ActionName}を実行しますか？",
			owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) {
			return;
		}

		IsProcessing = true;
		ProgressValue = 0;
		StatusMessage = $"{ActionName}を開始します...";
		ClientLib.Cursor2Wait();
		try {
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var message = new CvMsg {
				Code = 0,
				Flag = TargetFlag,
				DataType = typeof(StocktakeParameter),
				DataMsg = Common.SerializeObject(
					new StocktakeParameter(yyyymm, BuildDenDay(yyyymm), IdShain, [.. SokoIds])),
			};
			// 件数はサーバー側が本文へ「件数=N」の形で載せてくる(CreateProgressStreamMsg)。
			// 最後に届く Complete は件数0なので、その手前のステップ行を控えておく
			var stepMessage = string.Empty;
			await foreach (var streamMsg in coreService.QueryMsgStreamAsync(message, AppGlobal.GetDefaultCallContext(cancellationToken))) {
				if (!string.IsNullOrEmpty(streamMsg.DataMsg)) {
					StatusMessage = streamMsg.DataMsg;
					if (!streamMsg.IsCompleted) {
						stepMessage = streamMsg.DataMsg;
					}
				}
				ProgressValue = Math.Clamp(streamMsg.Progress, 0, 100);
				if (streamMsg.IsError) {
					throw new InvalidOperationException(streamMsg.DataMsg);
				}
				if (streamMsg.IsCompleted) {
					break;
				}
			}
			ProgressValue = 100;
			StatusMessage = $"{ActionName}が完了しました。{yyyymm} / {target}\n{stepMessage}";
			MessageEx.ShowInformationDialog(
				$"{ActionName}が完了しました。\n{yyyymm} / {target}\n{ExtractCount(stepMessage)} {ResultUnit}",
				owner: ClientLib.GetActiveView(this));
		}
		catch (OperationCanceledException) {
			StatusMessage = $"{ActionName}をキャンセルしました。";
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
			StatusMessage = $"{ActionName}をキャンセルしました。";
		}
		catch (Exception ex) {
			StatusMessage = $"エラーが発生しました: {ex.Message}";
			MessageEx.ShowErrorDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
		}
		finally {
			IsProcessing = false;
			ClientLib.Cursor2Normal();
		}
	}

	/// <summary>
	/// 進捗メッセージから「件数=N」を取り出す。取れなければ空文字を返す。
	/// サーバー側の <c>CreateProgressStreamMsg</c> が本文へ埋め込む形式に合わせている。
	/// </summary>
	private static string ExtractCount(string message) {
		const string marker = "件数=";
		var pos = message.IndexOf(marker, StringComparison.Ordinal);
		if (pos < 0) {
			return string.Empty;
		}
		var rest = message[(pos + marker.Length)..];
		var digits = new string([.. rest.TakeWhile(char.IsDigit)]);
		return digits.Length == 0 ? string.Empty
			: int.Parse(digits, CultureInfo.InvariantCulture).ToString("N0", CultureInfo.InvariantCulture);
	}

	/// <summary>派生側の追加検証。既定は常に成功する</summary>
	protected virtual bool ValidateBeforeExecute(out string errorMessage) {
		errorMessage = string.Empty;
		return true;
	}

	/// <summary>調整伝票の在庫計上日。開始処理では使わないので既定は棚卸年月の月末</summary>
	protected virtual string BuildDenDay(string yyyymm) => LastDayOfMonth(yyyymm);

	/// <summary>yyyyMM から月末日 yyyyMMdd を作る</summary>
	protected static string LastDayOfMonth(string yyyymm) {
		var first = DateTime.ParseExact(yyyymm + "01", "yyyyMMdd", CultureInfo.InvariantCulture);
		return first.AddMonths(1).AddDays(-1).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
	}

	/// <summary>yyyy/MM または yyyyMM を yyyyMM へ正規化する</summary>
	protected static bool TryParseYearMonth(string input, out string yyyymm) {
		yyyymm = string.Empty;
		if (string.IsNullOrWhiteSpace(input)) {
			return false;
		}
		var trimmed = input.Trim().Replace("/", string.Empty, StringComparison.Ordinal);
		if (trimmed.Length != 6
			|| !DateTime.TryParseExact(trimmed + "01", "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) {
			return false;
		}
		yyyymm = trimmed;
		return true;
	}

	/// <summary>yyyy/MM/dd または yyyyMMdd を yyyyMMdd へ正規化する</summary>
	protected static bool TryParseDate(string input, out string yyyymmdd) {
		yyyymmdd = string.Empty;
		if (string.IsNullOrWhiteSpace(input)) {
			return false;
		}
		var trimmed = input.Trim().Replace("/", string.Empty, StringComparison.Ordinal);
		if (trimmed.Length != 8
			|| !DateTime.TryParseExact(trimmed, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) {
			return false;
		}
		yyyymmdd = trimmed;
		return true;
	}

	private void ShowWarn(string message) {
		StatusMessage = message;
		MessageEx.ShowWarningDialog(message, owner: ClientLib.GetActiveView(this));
	}
}
