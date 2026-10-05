using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;
using Grpc.Core;
using System.Collections.ObjectModel;
using System.Text;

namespace CvWpfclient.ViewModels._41Logistics;

/// <summary>
/// L01 物流連携 マスタデータ作成。仕様は `Doc/spec/2026-10-05_WMS連携_旧AMS連携調査と仮実装仕様.md` 3.1・7章。
/// </summary>
public partial class LogisticsMasterDataCreateViewModel : BaseViewModel {
	/// <summary>設定照会に成功したか（失敗時は実行させない）</summary>
	bool settingsLoaded;
	/// <summary>直前のプレビューの条件と警告件数（作成の確認ダイアログで使う）</summary>
	string lastPreviewKey = string.Empty;
	int lastPreviewWarningCount;

	[ObservableProperty]
	public partial string LinkCode { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string BaseFolder { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TargetSoko { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string EncodingName { get; set; } = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasUnusableReason))]
	[NotifyCanExecuteChangedFor(nameof(PreviewCommand), nameof(CreateCommand))]
	public partial string UnusableReason { get; set; } = string.Empty;

	public bool HasUnusableReason => !string.IsNullOrEmpty(UnusableReason);

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(PreviewCommand), nameof(CreateCommand))]
	public partial bool IncludePd { get; set; } = true;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(PreviewCommand), nameof(CreateCommand))]
	public partial bool IncludeBsy { get; set; } = true;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsDiff))]
	public partial bool IsFull { get; set; } = true;

	/// <summary>差分（ラジオ用。<see cref="IsFull"/> の反転）</summary>
	public bool IsDiff {
		get => !IsFull;
		set => IsFull = !value;
	}

	/// <summary>差分の基準日。空欄なら前回作成以降</summary>
	[ObservableProperty]
	public partial DateTime? SinceDate { get; set; }

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(PreviewCommand), nameof(CreateCommand))]
	public partial bool IsProcessing { get; set; }

	[ObservableProperty]
	public partial ObservableCollection<LogisticsKindResultRow> ResultRows { get; set; } = [];

	[ObservableProperty]
	public partial string ResultText { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string StatusMessage { get; set; } = "連携設定を取得しています...";

	bool CanRun() => settingsLoaded && !IsProcessing && !HasUnusableReason && (IncludePd || IncludeBsy);

	[RelayCommand]
	async Task Init() {
		IsProcessing = true;
		try {
			ClientLib.Cursor2Wait();
			var (info, error) = await LogisticsClient.ExecuteAsync<LogisticsSettingsInfo>(new LogisticsSettingsQueryParam(), CancellationToken.None);
			if (info == null) {
				UnusableReason = $"連携設定の取得に失敗しました。{error}";
				StatusMessage = UnusableReason;
				return;
			}
			var settings = info.Settings;
			LinkCode = settings.LinkCode;
			BaseFolder = settings.BaseFolder;
			EncodingName = settings.EncodingName;
			// 倉庫マスタに無いコードは照会結果に含まれないので、設定コードを基準に表示する
			var sokoMap = info.Soko.GroupBy(x => x.Code).ToDictionary(g => g.Key, g => g.First().Name);
			TargetSoko = string.Join("、", settings.TargetSokoCodes.Select(code =>
				sokoMap.TryGetValue(code, out var name) ? $"{code} {name}" : $"{code} (倉庫マスタなし)"));
			settingsLoaded = true;
			UnusableReason = info.UnusableReason ?? string.Empty;
			StatusMessage = HasUnusableReason
				? "連携設定に不備があるため実行できません。"
				: "種別と条件を指定して、プレビューまたは作成を実行してください。";
		}
		catch (Exception ex) {
			UnusableReason = $"連携設定の取得に失敗しました。{ex.Message}";
			StatusMessage = UnusableReason;
		}
		finally {
			IsProcessing = false;
			ClientLib.Cursor2Normal();
			PreviewCommand.NotifyCanExecuteChanged();
			CreateCommand.NotifyCanExecuteChanged();
		}
	}

	[RelayCommand(CanExecute = nameof(CanRun))]
	async Task Preview() => await RunAsync(previewOnly: true);

	[RelayCommand(CanExecute = nameof(CanRun))]
	async Task Create() {
		var param = BuildParam(previewOnly: false);
		var message = new StringBuilder();
		message.AppendLine($"{string.Join("・", param.Kinds.Select(LogisticsDataKind.DisplayName))} のマスタデータファイルを{(param.IsFull ? "全件" : "差分")}で作成しますか？");
		message.AppendLine("作成したファイルは連携フォルダの送信フォルダへ配置されます。");
		if (lastPreviewKey == ParamKey(param) && lastPreviewWarningCount > 0) {
			message.AppendLine();
			message.AppendLine($"直前のプレビューで警告が {lastPreviewWarningCount:N0} 件あります。");
			message.AppendLine("JAN重複がある場合、連携先でSKUを特定できません。警告の内容を確認してください。");
		}
		if (MessageEx.ShowQuestionDialog(message.ToString().TrimEnd(), owner: ClientLib.GetActiveView(this)) != System.Windows.MessageBoxResult.Yes) {
			return;
		}
		await RunAsync(previewOnly: false);
	}

	async Task RunAsync(bool previewOnly) {
		if (!CanRun()) return;
		var param = BuildParam(previewOnly);
		var label = previewOnly ? "プレビュー" : "作成";
		IsProcessing = true;
		StatusMessage = $"{label}を実行しています...";
		try {
			ClientLib.Cursor2Wait();
			var (result, error) = await LogisticsClient.ExecuteAsync<LogisticsRunResult>(param, CancellationToken.None);
			if (result == null) {
				StatusMessage = $"{label}に失敗しました。{error}";
				MessageEx.ShowErrorDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
				return;
			}
			ShowResult(result, previewOnly);
			if (previewOnly) {
				lastPreviewKey = ParamKey(param);
				lastPreviewWarningCount = result.Warnings?.Length ?? 0;
			}
			StatusMessage = $"{label}が完了しました。";
		}
		catch (OperationCanceledException) {
			StatusMessage = $"{label}を中止しました。";
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
			StatusMessage = $"{label}を中止しました。";
		}
		catch (Exception ex) {
			// 作成は通信エラーでもサーバ側で完了している可能性がある
			StatusMessage = previewOnly
				? $"{label}に失敗しました。{ex.Message}"
				: $"{label}の結果が不明です。連携フォルダと送受信履歴を確認してください。{ex.Message}";
			MessageEx.ShowErrorDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
		}
		finally {
			IsProcessing = false;
			ClientLib.Cursor2Normal();
		}
	}

	void ShowResult(LogisticsRunResult result, bool previewOnly) {
		ResultRows = new ObservableCollection<LogisticsKindResultRow>((result.Kinds ?? []).Select(x => new LogisticsKindResultRow {
			Kind = x.Kind,
			KindName = LogisticsDataKind.DisplayName(x.Kind),
			Count = x.Count,
			FileName = !string.IsNullOrEmpty(x.FileName) ? x.FileName : previewOnly ? "(プレビューのため作成なし)" : "(0件のため作成なし)",
		}));
		var text = new StringBuilder();
		text.AppendLine(result.Message ?? string.Empty);
		var warnings = result.Warnings ?? [];
		if (warnings.Length > 0) {
			text.AppendLine();
			text.AppendLine($"警告 {warnings.Length:N0} 件:");
			foreach (var warning in warnings) {
				text.AppendLine($"・{warning}");
			}
		}
		ResultText = text.ToString().TrimEnd();
	}

	LogisticsMasterParam BuildParam(bool previewOnly) {
		string[] kinds = [.. LogisticsDataKind.MasterKinds.Where(k => k == LogisticsDataKind.PD ? IncludePd : IncludeBsy)];
		// 差分基準は指定日 00:00（ローカル）を UTC Ticks にする。空欄・全件は 0（前回作成以降）
		var sinceVdu = !IsFull && SinceDate is DateTime date
			? new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Local).ToUniversalTime().Ticks
			: 0L;
		return new LogisticsMasterParam(kinds, IsFull, sinceVdu, previewOnly, 0);
	}

	/// <summary>プレビューと作成で条件が同じか判定するキー</summary>
	static string ParamKey(LogisticsMasterParam param) => $"{string.Join(",", param.Kinds)}|{param.IsFull}|{param.SinceVdu}";
}

/// <summary>種別ごとの結果表示行</summary>
public sealed class LogisticsKindResultRow {
	public string Kind { get; init; } = string.Empty;
	public string KindName { get; init; } = string.Empty;
	public int Count { get; init; }
	public string FileName { get; init; } = string.Empty;
}
