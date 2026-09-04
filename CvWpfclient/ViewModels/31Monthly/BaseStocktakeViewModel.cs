using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using Grpc.Core;
using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;

namespace CvWpfclient.ViewModels._31Monthly;

/// <summary>
/// 棚卸開始処理・棚卸確定処理の共通部分。
/// <para>
/// 旧CV.netの棚卸7段階のうち、システムが行う「4. 棚卸開始処理」と「7. 棚卸確定処理」に対応する。
/// 仕様は `Doc/spec/2026-09-05_倉庫別棚卸日_詳細設計.md` を参照する。
/// </para>
/// <para>
/// 棚卸日は店舗ごとに <c>Tran60TanaDate.TanaDay</c> で設定されているため、画面は店舗一覧を出して
/// 店舗単位でチェックした行だけを対象に実行する（<see cref="StockDateBulkMenteViewModel"/> と同じ形）。
/// 棚卸日が未設定の店舗は <see cref="FallbackMonth"/> の月末を基準日として扱う。
/// </para>
/// </summary>
public abstract partial class BaseStocktakeViewModel : BaseViewModel {
	/// <summary>実行するサーバー処理</summary>
	protected abstract CvFlag TargetFlag { get; }
	/// <summary>確認ダイアログとメッセージに出す処理名</summary>
	protected abstract string ActionName { get; }
	/// <summary>実行結果の件数につける単位（「保存しました」「作成しました」の主語）</summary>
	protected abstract string ResultUnit { get; }

	/// <summary>棚卸日が未設定の店舗を処理する際のフォールバック計上月</summary>
	[ObservableProperty]
	public partial string FallbackMonth { get; set; } = DateTime.Now.ToString("yyyy/MM", CultureInfo.InvariantCulture);

	/// <summary>対象倉庫。空なら全倉庫</summary>
	[ObservableProperty]
	public partial string SokoText { get; set; } = "（全倉庫）";

	[ObservableProperty]
	public partial string StatusMessage { get; set; } = "対象月を yyyy/MM 形式で入力し、一覧取得を押してください。";

	[ObservableProperty]
	public partial bool IsProcessing { get; set; }

	[ObservableProperty]
	public partial int ProgressValue { get; set; }

	/// <summary>店舗別の棚卸状況一覧</summary>
	[ObservableProperty]
	public partial ObservableCollection<StocktakeShopRow> Rows { get; set; } = [];

	/// <summary>基準日以外の日付で入力された棚卸伝票の内訳</summary>
	[ObservableProperty]
	public partial ObservableCollection<StocktakeMisdated> MisdatedRows { get; set; } = [];

	/// <summary>基準日以外の棚卸入力の要約文言。無ければ空文字</summary>
	[ObservableProperty]
	public partial string MisdatedSummary { get; set; } = string.Empty;

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

	/// <summary>店舗一覧の全行を対象にする</summary>
	[RelayCommand]
	private void SelectAllTargets() {
		foreach (var row in Rows) {
			row.IsTarget = true;
		}
	}

	/// <summary>店舗一覧の全行を対象から外す</summary>
	[RelayCommand]
	private void ClearAllTargets() {
		foreach (var row in Rows) {
			row.IsTarget = false;
		}
	}

	/// <summary>
	/// 店舗別の棚卸状況一覧を取得する(`Msg060_StocktakeStatus`)。取得後は全行を対象にする。
	/// </summary>
	[RelayCommand(IncludeCancelCommand = true)]
	private async Task LoadStatusAsync(CancellationToken cancellationToken) {
		if (!TryParseYearMonth(FallbackMonth, out var yyyymm)) {
			ShowWarn($"対象月の形式が不正です: {FallbackMonth}");
			return;
		}

		IsProcessing = true;
		StatusMessage = "一覧を取得しています...";
		ClientLib.Cursor2Wait();
		try {
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var message = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg060_StocktakeStatus,
				DataType = typeof(StocktakeParameter),
				DataMsg = Common.SerializeObject(new StocktakeParameter(yyyymm, IdShain, [.. SokoIds])),
			};
			var reply = await coreService.QueryMsgAsync(message, AppGlobal.GetDefaultCallContext(cancellationToken));
			if (reply.Code < 0) {
				StatusMessage = $"一覧取得に失敗しました。{reply.DataMsg}";
				MessageEx.ShowErrorDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
				return;
			}
			if (Common.DeserializeObject(reply.DataMsg ?? string.Empty, reply.DataType) is not StocktakeStatusReply statusReply) {
				StatusMessage = "一覧取得に失敗しました。応答の形式が不正です。";
				MessageEx.ShowErrorDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
				return;
			}

			var shopIds = statusReply.Shops.Select(x => x.Id_Soko).Distinct().ToArray();
			var shopMap = await LoadShopMapAsync(shopIds, cancellationToken);
			var rows = statusReply.Shops
				.Select(x => CreateRow(x, shopMap.GetValueOrDefault(x.Id_Soko)))
				.OrderBy(x => x.ShopCode, StringComparer.Ordinal)
				.ToList();

			Rows = new ObservableCollection<StocktakeShopRow>(rows);
			MisdatedRows = new ObservableCollection<StocktakeMisdated>(statusReply.Misdated);
			MisdatedSummary = BuildMisdatedSummary(statusReply.Misdated);
			StatusMessage = $"{Rows.Count:N0} 件の店舗を取得しました。";
		}
		catch (OperationCanceledException) {
			StatusMessage = "一覧取得をキャンセルしました。";
		}
		catch (Exception ex) {
			StatusMessage = $"一覧取得に失敗しました。{ex.Message}";
			MessageEx.ShowErrorDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
		}
		finally {
			IsProcessing = false;
			ClientLib.Cursor2Normal();
		}
	}

	/// <summary>店舗Idからコード・名称を引くための <see cref="MasterTokui"/> 検索</summary>
	private async Task<Dictionary<long, MasterTokui>> LoadShopMapAsync(long[] ids, CancellationToken ct) {
		if (ids.Length == 0) {
			return [];
		}
		var where = $"Id IN ({string.Join(",", ids.Select(id => id.ToString(CultureInfo.InvariantCulture)))})";
		var param = new QueryListParam(typeof(MasterTokui), where: where);
		var list = await QueryListAsync<MasterTokui>(param, typeof(QueryListParam), ct);
		return list.ToDictionary(x => x.Id);
	}

	private async Task<IReadOnlyList<T>> QueryListAsync<T>(object parameter, Type dataType, CancellationToken ct) {
		var msg = new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg101_Op_Query,
			DataType = dataType,
			DataMsg = Common.SerializeObject(parameter)
		};
		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
		ct.ThrowIfCancellationRequested();
		if (Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is not IList list) {
			return [];
		}
		return [.. list.Cast<T>()];
	}

	private static StocktakeShopRow CreateRow(StocktakeShopStatus status, MasterTokui? shop) =>
		new() {
			IsTarget = true,
			Id_Soko = status.Id_Soko,
			ShopCode = shop?.Code ?? string.Empty,
			ShopName = shop?.Name ?? string.Empty,
			TanaDay = FormatYmd8ToSlash(status.TanaDay),
			SumMonth = FormatYm6ToSlash(status.SumMonth),
			BookQtyTotal = status.BookQtyTotal,
			ActualQtyTotal = status.ActualQtyTotal,
			DiffSkuCount = status.DiffSkuCount,
			FixDayText = status.FixDay == StocktakeDaySet.UnsetDay ? "－" : FormatYmd8ToSlash(status.FixDay),
			IsRefixRequired = status.IsRefixRequired,
			StatusText = status.IsRefixRequired ? "再確定要"
				: status.IsFixed ? "確定済"
				: status.IsStarted ? "開始済"
				: "未開始",
		};

	/// <summary>
	/// 基準日以外の棚卸入力の要約文言を作る。例: "基準日以外の棚卸入力 3件（2026/08/20:1件、2026/08/22:2件）"
	/// </summary>
	private static string BuildMisdatedSummary(List<StocktakeMisdated> misdated) {
		if (misdated.Count == 0) {
			return string.Empty;
		}
		var total = misdated.Sum(x => x.SlipCount);
		var days = misdated
			.GroupBy(x => x.DenDay)
			.OrderBy(g => g.Key, StringComparer.Ordinal)
			.Select(g => $"{FormatYmd8ToSlash(g.Key)}:{g.Sum(x => x.SlipCount)}件");
		return $"基準日以外の棚卸入力 {total}件（{string.Join("、", days)}）";
	}

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task ExecuteAsync(CancellationToken cancellationToken) {
		if (!TryParseYearMonth(FallbackMonth, out var yyyymm)) {
			ShowWarn($"対象月の形式が不正です: {FallbackMonth}");
			return;
		}
		if (!ValidateBeforeExecute(out var errorMessage)) {
			ShowWarn(errorMessage);
			return;
		}
		var targetIds = Rows.Where(x => x.IsTarget).Select(x => x.Id_Soko).ToList();
		if (targetIds.Count == 0) {
			ShowWarn("対象の店舗がありません。一覧取得のうえ、対象店舗にチェックを付けてください。");
			return;
		}
		if (!ConfirmBeforeExecute()) {
			return;
		}
		if (MessageEx.ShowQuestionDialog($"{targetIds.Count} 店舗の{ActionName}を実行しますか？",
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
				// 基準日は店舗ごとに Tran60TanaDate.TanaDay から解決される。yyyymm は棚卸日が
				// 未設定の店舗に使うフォールバック計上月として渡す(設計書2.1)。
				DataMsg = Common.SerializeObject(
					new StocktakeParameter(yyyymm, IdShain, [.. targetIds], AlignMisdated)),
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
			StatusMessage = $"{ActionName}が完了しました。{targetIds.Count} 店舗\n{stepMessage}";
			MessageEx.ShowInformationDialog(
				$"{ActionName}が完了しました。\n{targetIds.Count} 店舗\n{ExtractCount(stepMessage)} {ResultUnit}",
				owner: ClientLib.GetActiveView(this));
			IsProcessing = false;
			ClientLib.Cursor2Normal();
			await LoadStatusAsync(CancellationToken.None);
			return;
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

	/// <summary>
	/// 実行直前の追加確認。既定は常に許可する(true)。
	/// 確定画面は基準日以外の棚卸入力(<see cref="MisdatedRows"/>)がある場合にここで確認ダイアログを出す。
	/// </summary>
	protected virtual bool ConfirmBeforeExecute() => true;

	/// <summary>
	/// 基準日以外の日付で入力された棚卸伝票の計上日を基準日へ補正してから確定するか。
	/// 既定は false で、該当があればサーバは何も変更せず中断する。確定画面は
	/// <see cref="ConfirmBeforeExecute"/> の確認結果に応じてこの値を設定するため書き込み可能にしている。
	/// </summary>
	protected virtual bool AlignMisdated { get; set; }

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

	/// <summary>yyyyMMdd を yyyy/MM/dd へ整形する。変換できなければそのまま返す</summary>
	private static string FormatYmd8ToSlash(string yyyymmdd) =>
		DateTime.TryParseExact(yyyymmdd, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
			? day.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)
			: yyyymmdd;

	/// <summary>yyyyMM を yyyy/MM へ整形する。変換できなければそのまま返す</summary>
	private static string FormatYm6ToSlash(string yyyymm) =>
		DateTime.TryParseExact(yyyymm + "01", "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
			? day.ToString("yyyy/MM", CultureInfo.InvariantCulture)
			: yyyymm;

	private void ShowWarn(string message) {
		StatusMessage = message;
		MessageEx.ShowWarningDialog(message, owner: ClientLib.GetActiveView(this));
	}
}

/// <summary>
/// 店舗一覧の1行。<see cref="StockDateBulkRow"/>(棚卸日一括メンテナンス)に倣った作り。
/// </summary>
public partial class StocktakeShopRow : ObservableObject {
	[ObservableProperty]
	public partial bool IsTarget { get; set; } = true;

	public long Id_Soko { get; set; }
	public string ShopCode { get; set; } = string.Empty;
	public string ShopName { get; set; } = string.Empty;
	/// <summary>棚卸基準日 yyyy/MM/dd 表示</summary>
	public string TanaDay { get; set; } = string.Empty;
	/// <summary>計上月 yyyy/MM 表示</summary>
	public string SumMonth { get; set; } = string.Empty;
	public int BookQtyTotal { get; set; }
	public int ActualQtyTotal { get; set; }
	public int DiffSkuCount { get; set; }
	/// <summary>最終確定日 yyyy/MM/dd 表示。未確定なら "－"</summary>
	public string FixDayText { get; set; } = "－";
	public string StatusText { get; set; } = string.Empty;
	public bool IsRefixRequired { get; set; }
}
