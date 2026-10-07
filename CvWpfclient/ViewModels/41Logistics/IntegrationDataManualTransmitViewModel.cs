using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace CvWpfclient.ViewModels._41Logistics;

/// <summary>
/// L02 物流連携の手動送信。出荷指示・入荷予定・在庫をcv10-v1ファイルとしてサーバの連携フォルダへ作成する。
/// 対象取得 → 確認 → 送信ファイル作成。出荷指示・入荷予定は行を選んで送る（在庫は全件）。
/// </summary>
public partial class IntegrationDataManualTransmitViewModel : LogisticsViewModelBase {
	/// <summary>行チェックの連動中（入荷予定は伝票単位で選ぶため同じ伝票の行をそろえる）</summary>
	bool syncingChecks;

	/// <summary>種別（<see cref="LogisticsDataKind.SendKinds"/>）</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsOrder), nameof(IsStock), nameof(IsZaiko), nameof(IsDateEnabled))]
	public partial string Kind { get; set; } = LogisticsDataKind.ORDER;

	public bool IsOrder {
		get => Kind == LogisticsDataKind.ORDER;
		set { if (value) Kind = LogisticsDataKind.ORDER; }
	}

	public bool IsStock {
		get => Kind == LogisticsDataKind.STOCK;
		set { if (value) Kind = LogisticsDataKind.STOCK; }
	}

	public bool IsZaiko {
		get => Kind == LogisticsDataKind.ZAIKO;
		set { if (value) Kind = LogisticsDataKind.ZAIKO; }
	}

	/// <summary>指定日は在庫では使わない</summary>
	public bool IsDateEnabled => !IsZaiko;

	/// <summary>指定日（出荷指示は納品日、入荷予定は計上日がこの日以前）</summary>
	[ObservableProperty]
	public partial DateTime? TargetDate { get; set; } = DateTime.Today;

	/// <summary>倉庫すべて（SokoIds 空）</summary>
	[ObservableProperty]
	public partial bool AllSoko { get; set; } = true;

	/// <summary>対象倉庫の選択肢</summary>
	public ObservableCollection<LogisticsSokoOption> SokoOptions { get; } = [];

	[ObservableProperty]
	public partial ObservableCollection<LogisticsSendCandidateRow> Candidates { get; set; } = [];

	[ObservableProperty]
	public partial string SummaryText { get; set; } = string.Empty;

	/// <summary>一覧を取得した条件の種別（条件を変えたら一覧を消す）</summary>
	string loadedKind = string.Empty;

	bool CanQuery() => CanRunBase && (IsZaiko || TargetDate != null) && (AllSoko || SokoOptions.Any(s => s.IsChecked));

	bool CanSend() => CanRunBase && loadedKind == Kind && Candidates.Count > 0 && (IsZaiko || Candidates.Any(c => c.IsChecked));

	bool CanSelect() => !IsProcessing && !IsZaiko && Candidates.Count > 0;

	protected override void RefreshCommands() {
		QueryCommand.NotifyCanExecuteChanged();
		SendCommand.NotifyCanExecuteChanged();
		SelectAllCommand.NotifyCanExecuteChanged();
		UnselectAllCommand.NotifyCanExecuteChanged();
	}

	partial void OnKindChanged(string value) {
		ResultText = string.Empty;
		ClearCandidates();
	}

	partial void OnTargetDateChanged(DateTime? value) => ClearCandidates();

	partial void OnAllSokoChanged(bool value) => ClearCandidates();

	[RelayCommand]
	async Task Init() {
		if (!await LoadSettingsAsync()) {
			RefreshCommands();
			return;
		}
		foreach (var soko in SettingsInfo?.Soko ?? []) {
			var option = new LogisticsSokoOption { Id = soko.Id, Code = soko.Code, Name = soko.Name };
			option.PropertyChanged += (_, _) => ClearCandidates();
			SokoOptions.Add(option);
		}
		if (!HasUnusableReason) {
			StatusMessage = "種別・指定日・倉庫を指定して「対象取得」を実行してください。";
		}
		RefreshCommands();
	}

	[RelayCommand(CanExecute = nameof(CanQuery))]
	async Task Query() {
		var kind = Kind;
		var rows = await CallAsync<List<LogisticsSendCandidate>>(new LogisticsSendQueryParam(kind, ToDay(), SokoIds()), "対象取得", isUpdate: false);
		if (rows == null) return;
		SetCandidates(kind, rows);
		StatusMessage = rows.Count == 0
			? "送信する対象はありません。"
			: kind == LogisticsDataKind.ZAIKO
				? "在庫は全件を送信します。内容を確認して「送信ファイル作成」を実行してください。"
				: "送信する行を選んで「送信ファイル作成」を実行してください。";
	}

	[RelayCommand(CanExecute = nameof(CanSelect))]
	void SelectAll() => SetAllChecks(true);

	[RelayCommand(CanExecute = nameof(CanSelect))]
	void UnselectAll() => SetAllChecks(false);

	[RelayCommand(CanExecute = nameof(CanSend))]
	async Task Send() {
		var kind = Kind;
		var picked = Candidates.Where(c => IsZaiko || c.IsChecked).ToList();
		// 在庫は選択できないので全件。出荷指示・入荷予定は画面で見た行だけを送るため参照Idを明示する
		long[] refIds = kind == LogisticsDataKind.ZAIKO ? [] : [.. picked.Select(c => c.RefId).Distinct()];
		var message = new StringBuilder();
		message.AppendLine($"{LogisticsDataKind.DisplayName(kind)} {picked.Count:N0}行の送信ファイルを作成しますか？");
		message.AppendLine("作成したファイルは連携フォルダの送信フォルダへ配置されます。");
		switch (kind) {
		case LogisticsDataKind.ORDER:
			message.AppendLine("送信した配分は配分修正画面で修正できなくなります。");
			break;
		case LogisticsDataKind.STOCK:
			message.AppendLine("入荷予定は伝票単位で送信します。送信後に元伝票を修正しても自動では再送しません。");
			break;
		default:
			message.AppendLine("在庫は照合用の参考データです（在庫・伝票は変わりません）。");
			break;
		}
		if (!Confirm(message.ToString().TrimEnd())) return;

		var result = await CallAsync<LogisticsRunResult>(new LogisticsSendParam(kind, ToDay(), SokoIds(), refIds, 0), "送信ファイル作成", isUpdate: true);
		if (result == null) return;
		ResultText = LogisticsText.RunResult(result);
		var summary = result.Message ?? string.Empty;
		// 送信済みを一覧から外すため再取得する
		var rows = await CallAsync<List<LogisticsSendCandidate>>(new LogisticsSendQueryParam(kind, ToDay(), SokoIds()), "対象の再取得", isUpdate: false);
		if (rows != null) {
			SetCandidates(kind, rows);
		}
		StatusMessage = $"送信ファイル作成が完了しました。{summary}";
	}

	string ToDay() => IsZaiko || TargetDate is not DateTime date ? string.Empty : date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

	long[] SokoIds() => AllSoko ? [] : [.. SokoOptions.Where(s => s.IsChecked).Select(s => s.Id)];

	void SetCandidates(string kind, List<LogisticsSendCandidate> rows) {
		foreach (var row in Candidates) {
			row.PropertyChanged -= OnCandidatePropertyChanged;
		}
		var list = new ObservableCollection<LogisticsSendCandidateRow>(rows.Select(c => new LogisticsSendCandidateRow(kind, c)));
		foreach (var row in list) {
			row.PropertyChanged += OnCandidatePropertyChanged;
		}
		Candidates = list;
		loadedKind = kind;
		UpdateSummary();
		RefreshCommands();
	}

	void ClearCandidates() {
		if (Candidates.Count == 0 && loadedKind.Length == 0) {
			RefreshCommands();
			return;
		}
		foreach (var row in Candidates) {
			row.PropertyChanged -= OnCandidatePropertyChanged;
		}
		Candidates = [];
		loadedKind = string.Empty;
		UpdateSummary();
		RefreshCommands();
	}

	void SetAllChecks(bool value) {
		syncingChecks = true;
		try {
			foreach (var row in Candidates) {
				row.IsChecked = value;
			}
		}
		finally {
			syncingChecks = false;
		}
		UpdateSummary();
		RefreshCommands();
	}

	void OnCandidatePropertyChanged(object? sender, PropertyChangedEventArgs e) {
		if (e.PropertyName != nameof(LogisticsSendCandidateRow.IsChecked) || syncingChecks || sender is not LogisticsSendCandidateRow changed) return;
		if (loadedKind == LogisticsDataKind.STOCK) {
			// 入荷予定は伝票（RefId）単位で送るので、同じ伝票の行をそろえる
			syncingChecks = true;
			try {
				foreach (var row in Candidates.Where(r => r.RefId == changed.RefId && r != changed)) {
					row.IsChecked = changed.IsChecked;
				}
			}
			finally {
				syncingChecks = false;
			}
		}
		UpdateSummary();
		RefreshCommands();
	}

	void UpdateSummary() {
		if (Candidates.Count == 0) {
			SummaryText = loadedKind.Length == 0 ? string.Empty : "対象 0 件";
			return;
		}
		if (loadedKind == LogisticsDataKind.ZAIKO) {
			SummaryText = $"対象 {Candidates.Count:N0} 件　有効在庫 {Candidates.Sum(c => c.Su):N0}　積送中 {Candidates.Sum(c => c.Candidate.Su2):N0}";
			return;
		}
		var picked = Candidates.Where(c => c.IsChecked).ToList();
		SummaryText = $"対象 {Candidates.Count:N0} 件　選択 {picked.Count:N0} 件　選択数量 {picked.Sum(c => c.Su):N0}（全体 {Candidates.Sum(c => c.Su):N0}）";
	}
}

/// <summary>倉庫の選択肢</summary>
public sealed partial class LogisticsSokoOption : ObservableObject {
	public long Id { get; init; }
	public string Code { get; init; } = string.Empty;
	public string Name { get; init; } = string.Empty;
	public string DisplayText => LogisticsText.CodeName(Code, Name);

	[ObservableProperty]
	public partial bool IsChecked { get; set; }
}

/// <summary>送信対象の表示行（行チェック付き）</summary>
public sealed partial class LogisticsSendCandidateRow : ObservableObject {
	public LogisticsSendCandidateRow(string kind, LogisticsSendCandidate candidate) {
		Kind = kind;
		Candidate = candidate;
		IsChecked = true;
	}

	public string Kind { get; }
	public LogisticsSendCandidate Candidate { get; }

	[ObservableProperty]
	public partial bool IsChecked { get; set; }

	/// <summary>在庫は全件送信のため選択できない</summary>
	public bool IsSelectable => Kind != LogisticsDataKind.ZAIKO;
	public long RefId => Candidate.RefId;
	public string KubunText => (Kind, Candidate.Kubun) switch {
		(LogisticsDataKind.ORDER, "10") => "10 移動",
		(LogisticsDataKind.ORDER, "20") => "20 出荷売上",
		(LogisticsDataKind.STOCK, "10") => "10 移動入荷",
		(LogisticsDataKind.STOCK, "20") => "20 仕入入荷",
		_ => Candidate.Kubun,
	};
	public string DayText => LogisticsText.Day(Candidate.Day);
	public string SokoText => LogisticsText.CodeName(Candidate.SokoCode, Candidate.SokoName);
	public string PartnerText => LogisticsText.CodeName(Candidate.PartnerCode, Candidate.PartnerName);
	public string SkuText => string.Join(" - ", new[] { Candidate.ShohinCode, Candidate.ColCode, Candidate.SizCode }.Where(s => !string.IsNullOrWhiteSpace(s)));
	public string Jan => Candidate.Jan;
	public int Su => Candidate.Su;
	/// <summary>積送中（在庫のときだけ表示）</summary>
	public string Su2Text => Kind == LogisticsDataKind.ZAIKO ? Candidate.Su2.ToString("N0", CultureInfo.CurrentCulture) : string.Empty;
	public string Memo => Candidate.Memo;
	public string RefText => Candidate.RefId > 0
		? $"{Candidate.RefTable} {Candidate.RefId}{(Candidate.RefNo > 0 ? $"-{Candidate.RefNo}" : string.Empty)}"
		: Candidate.RefTable;
}
