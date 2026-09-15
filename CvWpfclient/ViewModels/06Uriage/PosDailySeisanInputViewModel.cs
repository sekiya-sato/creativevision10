using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;
using System.Globalization;

namespace CvWpfclient.ViewModels._06Uriage;

/// <summary>
/// POS日別精算入力 — Tran04PosSeisan の一覧・登録・修正・削除。
/// </summary>
public partial class PosDailySeisanInputViewModel : Helpers.BaseMenteViewModel<Tran04PosSeisan> {
	/// <summary>額面と枚数プロパティ名の対応（大きい順）</summary>
	static readonly (int Menmen, string PropertyName)[] KinshuMenmen = [
		(10000, nameof(Tran04PosSeisan.Mai10000)),
		(5000, nameof(Tran04PosSeisan.Mai5000)),
		(2000, nameof(Tran04PosSeisan.Mai2000)),
		(1000, nameof(Tran04PosSeisan.Mai1000)),
		(500, nameof(Tran04PosSeisan.Mai500)),
		(100, nameof(Tran04PosSeisan.Mai100)),
		(50, nameof(Tran04PosSeisan.Mai50)),
		(10, nameof(Tran04PosSeisan.Mai10)),
		(5, nameof(Tran04PosSeisan.Mai5)),
		(1, nameof(Tran04PosSeisan.Mai1))
	];

	[ObservableProperty]
	public partial string Title { get; set; } = "POS日別精算入力";

	[ObservableProperty]
	public partial string? SearchFromDenDay { get; set; }

	[ObservableProperty]
	public partial string? SearchToDenDay { get; set; }

	[ObservableProperty]
	public partial long SearchId_Tenpo { get; set; }

	[ObservableProperty]
	public partial string SearchTenpoName { get; set; } = string.Empty;

	bool recalculating;

	protected override string? ListOrder => "DenDay DESC, Id_Tenpo, RegisterNo, SeisanCnt";

	protected override string? ListWhere {
		get {
			List<string> clauses = [];
			List<string> parameters = [];

			if (!string.IsNullOrWhiteSpace(SearchFromDenDay)) {
				clauses.Add($"DenDay >= {AddSqlParameter(parameters, SearchFromDenDay.Trim())}");
			}
			if (!string.IsNullOrWhiteSpace(SearchToDenDay)) {
				clauses.Add($"DenDay <= {AddSqlParameter(parameters, SearchToDenDay.Trim())}");
			}
			if (SearchId_Tenpo > 0) {
				// 数値列は基底 BuildSelectCodeWhere と同様にリテラル埋め込みにする（文字列パラメータでは PostgreSQL 等で型不一致になるため）
				clauses.Add($"Id_Tenpo = {SearchId_Tenpo}");
			}

			SelectCodeWhereParameters = [.. parameters];
			return clauses.Count == 0 ? null : string.Join(" AND ", clauses);
		}
	}

	[RelayCommand]
	Task Init() => DoList(CancellationToken.None);

	[RelayCommand]
	void DoSelectSearchTenpo() {
		var tenpo = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType in (1,3,6)", "Code", startPos: SearchId_Tenpo);
		if (tenpo == null) return;

		SearchId_Tenpo = tenpo.Id;
		SearchTenpoName = tenpo.Name ?? string.Empty;
	}

	[RelayCommand]
	void DoSelectTenpo() {
		var tenpo = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType in (1,3,6)", "Code", startPos: CurrentEdit.Id_Tenpo);
		if (tenpo == null) return;

		CurrentEdit.Id_Tenpo = tenpo.Id;
		CurrentEdit.VTenpo = new() { Sid = tenpo.Id, Cd = tenpo.Code ?? string.Empty, Mei = tenpo.Name ?? string.Empty };
	}

	[RelayCommand]
	void DoSelectShain() {
		var shain = ShowSelectDialog<MasterShain>(typeof(MasterShain), "", "Code", startPos: CurrentEdit.Id_Shain);
		if (shain == null) return;

		CurrentEdit.Id_Shain = shain.Id;
		CurrentEdit.VShain = new() { Sid = shain.Id, Cd = shain.Code ?? string.Empty, Mei = shain.Name ?? string.Empty };
	}

	protected override void OnCurrentEditChangedCore(Tran04PosSeisan? oldValue, Tran04PosSeisan newValue) {
		if (oldValue != null) oldValue.PropertyChanged -= OnCurrentEditPropertyChanged;
		if (newValue == null) return;

		newValue.PropertyChanged += OnCurrentEditPropertyChanged;
		Recalculate();
	}

	void OnCurrentEditPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
		if (recalculating) return;

		if (KinshuMenmen.Any(k => k.PropertyName == e.PropertyName)) {
			RecalculateRealAmount();
			return;
		}
		if (e.PropertyName is nameof(Tran04PosSeisan.RealAmount) or nameof(Tran04PosSeisan.CalcAmount)) {
			RecalculateAmountDiff();
		}
	}

	void Recalculate() {
		RecalculateRealAmount();
		RecalculateAmountDiff();
	}

	void RecalculateRealAmount() {
		var realAmount = KinshuMenmen.Sum(k => GetMaiValue(k.PropertyName) * k.Menmen);
		if (CurrentEdit.RealAmount == realAmount) return;

		recalculating = true;
		try {
			CurrentEdit.RealAmount = realAmount;
		}
		finally {
			recalculating = false;
		}
		RecalculateAmountDiff();
	}

	void RecalculateAmountDiff() {
		var amountDiff = CurrentEdit.RealAmount - CurrentEdit.CalcAmount;
		if (CurrentEdit.AmountDiff == amountDiff) return;

		recalculating = true;
		try {
			CurrentEdit.AmountDiff = amountDiff;
		}
		finally {
			recalculating = false;
		}
	}

	int GetMaiValue(string propertyName) =>
		propertyName switch {
			nameof(Tran04PosSeisan.Mai10000) => CurrentEdit.Mai10000,
			nameof(Tran04PosSeisan.Mai5000) => CurrentEdit.Mai5000,
			nameof(Tran04PosSeisan.Mai2000) => CurrentEdit.Mai2000,
			nameof(Tran04PosSeisan.Mai1000) => CurrentEdit.Mai1000,
			nameof(Tran04PosSeisan.Mai500) => CurrentEdit.Mai500,
			nameof(Tran04PosSeisan.Mai100) => CurrentEdit.Mai100,
			nameof(Tran04PosSeisan.Mai50) => CurrentEdit.Mai50,
			nameof(Tran04PosSeisan.Mai10) => CurrentEdit.Mai10,
			nameof(Tran04PosSeisan.Mai5) => CurrentEdit.Mai5,
			nameof(Tran04PosSeisan.Mai1) => CurrentEdit.Mai1,
			_ => 0
		};

	protected override bool CanUpdate() => CurrentEdit.Id > 0;

	protected override bool CanDelete() => ListData.Count > 0 && CurrentEdit.Id > 0;

	protected override bool ConfirmAction(string message) {
		if ((message.StartsWith("追加", StringComparison.Ordinal) || message.StartsWith("修正", StringComparison.Ordinal)) && !ValidateCurrentEdit()) {
			return false;
		}

		return base.ConfirmAction(message);
	}

	protected override object CreateInsertParam() {
		Recalculate();
		NormalizeCurrentEdit();
		CurrentEdit.SeisanCnt = GetNextSeisanCnt(CurrentEdit.DenDay, CurrentEdit.Id_Tenpo, CurrentEdit.RegisterNo);
		return base.CreateInsertParam();
	}

	protected override object CreateUpdateParam() {
		Recalculate();
		NormalizeCurrentEdit();
		return base.CreateUpdateParam();
	}

	protected override string GetInsertConfirmMessage() => $"追加しますか？ (営業日={CurrentEdit.DenDay}, 店舗={CurrentEdit.VTenpo?.Mei}, レジ={CurrentEdit.RegisterNo})";

	protected override string GetUpdateConfirmMessage() => $"修正しますか？ (営業日={CurrentEdit.DenDay}, 店舗={CurrentEdit.VTenpo?.Mei}, レジ={CurrentEdit.RegisterNo}, Id={CurrentEdit.Id})";

	protected override string GetDeleteConfirmMessage() => $"削除しますか？ (営業日={CurrentEdit.DenDay}, 店舗={CurrentEdit.VTenpo?.Mei}, レジ={CurrentEdit.RegisterNo}, Id={CurrentEdit.Id})";

	/// <summary>同一営業日・店舗・レジの最大 SeisanCnt + 1 を返す（一覧に無ければ1）。DB照会は行わず ListData から求める。</summary>
	int GetNextSeisanCnt(string denDay, long idTenpo, string registerNo) {
		var maxCnt = ListData
			.Where(x => x.DenDay == denDay && x.Id_Tenpo == idTenpo && x.RegisterNo == registerNo)
			.Select(x => x.SeisanCnt)
			.DefaultIfEmpty(0)
			.Max();
		return maxCnt + 1;
	}

	bool ValidateCurrentEdit() {
		NormalizeCurrentEdit();

		if (!DateTime.TryParseExact(CurrentEdit.DenDay, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var denDay)) {
			Message = "営業日は yyyyMMdd の8桁で入力してください";
			MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
			return false;
		}
		if (CurrentEdit.Id_Tenpo <= 0) {
			Message = "店舗を選択してください";
			MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
			return false;
		}
		if (string.IsNullOrWhiteSpace(CurrentEdit.RegisterNo)) {
			Message = "レジ番号を入力してください";
			MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
			return false;
		}
		if (CurrentEdit.Id_Shain <= 0) {
			Message = "担当者を選択してください";
			MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
			return false;
		}
		if (KinshuMenmen.Any(k => GetMaiValue(k.PropertyName) < 0)) {
			Message = "金種枚数は0以上で入力してください";
			MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
			return false;
		}

		CurrentEdit.DenDay = denDay.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		return true;
	}

	void NormalizeCurrentEdit() {
		CurrentEdit.DenDay = (CurrentEdit.DenDay ?? string.Empty).Trim();
		CurrentEdit.RegisterNo = (CurrentEdit.RegisterNo ?? string.Empty).Trim();
		CurrentEdit.Memo = (CurrentEdit.Memo ?? string.Empty).Trim();
	}
}
