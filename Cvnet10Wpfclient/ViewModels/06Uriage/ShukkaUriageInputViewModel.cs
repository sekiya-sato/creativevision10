using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Asset;
using Cvnet10Base;
using Cvnet10Wpfclient.Helpers;
using Cvnet10Wpfclient.ViewModels.Sub;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace Cvnet10Wpfclient.ViewModels._06Uriage;

public partial class ShukkaUriageInputViewModel : Helpers.BaseMenteViewModel<Tran00Uriage> {
	SelectInputParameter selectInputParam = new();
	bool suppressDetailRecalc;

	public IReadOnlyList<EnumUri00> KubunItems { get; } = Enum.GetValues<EnumUri00>();
	[ObservableProperty]
	public int selectedTabIndex;
	[RelayCommand]
	void GoToDetailTab() {
		SelectedTabIndex = 1;
	}


	[ObservableProperty]
	ObservableCollection<Tran99Meisai> detailItems = [];

	[ObservableProperty]
	Tran99Meisai? selectedDetail;

	public DateTime UriageDate {
		get => ConvertToDate(CurrentEdit.DenDay);
		set {
			CurrentEdit.DenDay = value.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
			OnPropertyChanged(nameof(UriageDate));
		}
	}

	protected override void OnCurrentChangedCore(Tran00Uriage? oldValue, Tran00Uriage newValue) {
		base.OnCurrentChangedCore(oldValue, newValue);
		ApplyDetailFromCurrent();
		OnPropertyChanged(nameof(UriageDate));
	}

	protected override object CreateInsertParam() {
		SyncDetailToCurrent();
		return base.CreateInsertParam();
	}

	protected override object CreateUpdateParam() {
		SyncDetailToCurrent();
		return base.CreateUpdateParam();
	}

	protected override QueryListParam CreateListQueryParam() =>
		new(
			itemType: typeof(Tran00Uriage),
			where: BuildListWhere(),
			order: "DenDay desc, Id desc",
			maxCount: selectInputParam.MaxCount
		);

	string? BuildListWhere() {
		List<string> clauses = [];
		if (selectInputParam.FromId.HasValue) {
			clauses.Add($"Id >= {selectInputParam.FromId.Value}");
		}
		if (selectInputParam.ToId.HasValue) {
			clauses.Add($"Id <= {selectInputParam.ToId.Value}");
		}
		if (!string.IsNullOrWhiteSpace(selectInputParam.FromDate)) {
			clauses.Add($"DenDay >= '{EscapeSqlLiteral(selectInputParam.FromDate)}'");
		}
		if (!string.IsNullOrWhiteSpace(selectInputParam.ToDate)) {
			clauses.Add($"DenDay <= '{EscapeSqlLiteral(selectInputParam.ToDate)}'");
		}

		return clauses.Count == 0 ? null : string.Join(" AND ", clauses);
	}

	partial void OnDetailItemsChanged(ObservableCollection<Tran99Meisai>? oldValue, ObservableCollection<Tran99Meisai> newValue) {
		if (oldValue != null) {
			oldValue.CollectionChanged -= OnDetailItemsCollectionChanged;
			foreach (var item in oldValue) {
				item.PropertyChanged -= OnDetailItemPropertyChanged;
			}
		}

		newValue.CollectionChanged += OnDetailItemsCollectionChanged;
		foreach (var item in newValue) {
			item.PropertyChanged += OnDetailItemPropertyChanged;
		}
		RecalculateSummaryInternal();
	}

	void OnDetailItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
		if (e.OldItems != null) {
			foreach (Tran99Meisai item in e.OldItems) {
				item.PropertyChanged -= OnDetailItemPropertyChanged;
			}
		}
		if (e.NewItems != null) {
			foreach (Tran99Meisai item in e.NewItems) {
				item.PropertyChanged += OnDetailItemPropertyChanged;
			}
		}
		NormalizeDetailNumbers();
		RecalculateSummaryInternal();
	}

	void OnDetailItemPropertyChanged(object? sender, PropertyChangedEventArgs e) {
		if (sender is not Tran99Meisai item) return;
		if (suppressDetailRecalc) return;
		if (e.PropertyName is nameof(Tran99Meisai.Su) or nameof(Tran99Meisai.Tanka)) {
			var next = item.Su * item.Tanka;
			if (item.Kingaku != next) {
				suppressDetailRecalc = true;
				item.Kingaku = next;
				suppressDetailRecalc = false;
			}
		}
		RecalculateSummaryInternal();
	}

	void ApplyDetailFromCurrent() {
		var list = CurrentEdit.Jmeisai ?? [];
		DetailItems = new ObservableCollection<Tran99Meisai>(list.Select(item => Common.CloneObject(item)));
		SelectedDetail = DetailItems.FirstOrDefault();
	}

	void SyncDetailToCurrent() {
		CurrentEdit.Jmeisai = DetailItems.Select(item => Common.CloneObject(item)).ToList();
	}

	void NormalizeDetailNumbers() {
		for (var i = 0; i < DetailItems.Count; i++) {
			if (DetailItems[i].No != i + 1) {
				DetailItems[i].No = i + 1;
			}
		}
	}

	void RecalculateSummaryInternal() {
		CurrentEdit.SuTotal = DetailItems.Sum(x => x.Su);
		CurrentEdit.KingakuTotal = DetailItems.Sum(x => x.Kingaku);
		CurrentEdit.JodaiTotal = DetailItems.Sum(x => x.Jodai);
		CurrentEdit.GedaiTotal = DetailItems.Sum(x => x.Gedai);
		CurrentEdit.Nebiki01Meisai = DetailItems.Sum(x => x.Nebiki01);
		CurrentEdit.Nebiki00Total = DetailItems.Sum(x => x.Nebiki00);
		SyncDetailToCurrent();
	}

	static DateTime ConvertToDate(string? value) {
		if (DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) {
			return date;
		}
		return DateTime.Today;
	}

	[RelayCommand]
	async Task ShowHeaderListAsync() {
		var selWin = new Views.Sub.RangeInputParamView();
		var vm = selWin.DataContext as RangeInputParamViewModel;
		if (vm == null) return;
		vm.Initialize(selectInputParam);
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		selectInputParam = vm.Parameter;
		await DoList(CancellationToken.None);
	}

	[RelayCommand]
	async Task LookupTokuiByCodeAsync() {
		var code = CurrentEdit.VTokui?.Cd ?? string.Empty;
		if (string.IsNullOrWhiteSpace(code)) return;
		var tokui = await FindTokuiByCodeAsync(code.Trim(), CancellationToken.None);
		if (tokui == null) {
			Message = $"取引先が見つかりません (CD={code})";
			return;
		}
		ApplyTokui(tokui);
	}

	[RelayCommand]
	void SelectTokui() {
		var selWin = new Views.Sub.SelectWinView();
		var vm = selWin.DataContext as Sub.SelectWinViewModel;
		if (vm == null) return;
		vm.SetParam(typeof(MasterTokui), "TenType<>0", "Code", startPos: CurrentEdit.Id_Tokui);
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		if (vm.Current is not MasterTokui tokui) return;
		ApplyTokui(tokui);
	}

	[RelayCommand]
	void SelectShain() {
		var selWin = new Views.Sub.SelectWinView();
		var vm = selWin.DataContext as Sub.SelectWinViewModel;
		if (vm == null) return;
		vm.SetParam(typeof(MasterShain), string.Empty, "Code", startPos: CurrentEdit.Id_Shain);
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		if (vm.Current is not MasterShain shain) return;
		CurrentEdit.Id_Shain = shain.Id;
		CurrentEdit.VShain = new() { Sid = shain.Id, Cd = shain.Code ?? string.Empty, Mei = shain.Name ?? string.Empty };
	}

	[RelayCommand]
	void SelectSoko() {
		var selWin = new Views.Sub.SelectWinView();
		var vm = selWin.DataContext as Sub.SelectWinViewModel;
		if (vm == null) return;
		vm.SetParam(typeof(MasterTokui), "TenType=0", "Code", startPos: CurrentEdit.Id_Soko);
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		if (vm.Current is not MasterTokui soko) return;
		CurrentEdit.Id_Soko = soko.Id;
		CurrentEdit.VSoko = new() { Sid = soko.Id, Cd = soko.Code ?? string.Empty, Mei = soko.Name ?? string.Empty };
	}

	[RelayCommand]
	void SelectShohin(Tran99Meisai? row) {
		if (row == null) return;
		var selWin = new Views.Sub.SelectWinView();
		var vm = selWin.DataContext as Sub.SelectWinViewModel;
		if (vm == null) return;
		vm.SetParam(typeof(MasterShohin), string.Empty, "Code", startPos: row.Id_Shohin);
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		if (vm.Current is not MasterShohin shohin) return;
		row.Id_Shohin = shohin.Id;
		row.Code_Shohin = shohin.Code ?? string.Empty;
		row.Mei_Shohin = shohin.Name ?? string.Empty;
	}

	[RelayCommand]
	void SelectColor(Tran99Meisai? row) {
		if (row == null) return;
		var selWin = new Views.Sub.SelectWinView();
		var vm = selWin.DataContext as Sub.SelectWinViewModel;
		if (vm == null) return;
		vm.SetParam(typeof(MasterMeisho), "Kubun='COL'", "Code", startPos: row.Id_Col);
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		if (vm.Current is not MasterMeisho col) return;
		row.Id_Col = col.Id;
		row.Code_Col = col.Code ?? string.Empty;
		row.Mei_Col = col.Name ?? string.Empty;
	}

	[RelayCommand]
	void SelectSize(Tran99Meisai? row) {
		if (row == null) return;
		var selWin = new Views.Sub.SelectWinView();
		var vm = selWin.DataContext as Sub.SelectWinViewModel;
		if (vm == null) return;
		vm.SetParam(typeof(MasterMeisho), "Kubun='SIZ'", "Code", startPos: row.Id_Siz);
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		if (vm.Current is not MasterMeisho size) return;
		row.Id_Siz = size.Id;
		row.Code_Siz = size.Code ?? string.Empty;
		row.Mei_Siz = size.Name ?? string.Empty;
	}

	[RelayCommand]
	void AddDetailRow() {
		var nextNo = DetailItems.Count == 0 ? 1 : DetailItems.Max(x => x.No) + 1;
		var item = new Tran99Meisai { No = nextNo };
		DetailItems.Add(item);
		SelectedDetail = item;
	}

	[RelayCommand]
	void RemoveDetailRow() {
		if (SelectedDetail == null) return;
		DetailItems.Remove(SelectedDetail);
		SelectedDetail = DetailItems.FirstOrDefault();
	}

	[RelayCommand]
	void RecalculateSummary() {
		RecalculateSummaryInternal();
	}

	[RelayCommand]
	void FixSlipNo() {
		Message = "伝票番号を確定しました";
	}

	[RelayCommand]
	void IssueSlipNo() {
		if (string.IsNullOrWhiteSpace(CurrentEdit.ManualNo)) {
			CurrentEdit.ManualNo = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
		}
		Message = $"採番しました (伝票番号={CurrentEdit.ManualNo})";
	}

	[RelayCommand]
	void ReloadSlip() {
		CurrentEdit = Common.CloneObject(Current);
		ApplyDetailFromCurrent();
		OnPropertyChanged(nameof(UriageDate));
		Message = "再読込しました";
	}

	void ApplyTokui(MasterTokui tokui) {
		CurrentEdit.Id_Tokui = tokui.Id;
		CurrentEdit.VTokui = new() { Sid = tokui.Id, Cd = tokui.Code ?? string.Empty, Mei = tokui.Name ?? string.Empty };
	}

	static async Task<MasterTokui?> FindTokuiByCodeAsync(string code, CancellationToken ct) {
		try {
			var coreService = AppGlobal.GetGrpcService<ICvnetCoreService>();
			var msg = new CvnetMsg {
				Code = 0,
				Flag = CvnetFlag.Msg101_Op_Query,
				DataType = typeof(QueryListParam),
				DataMsg = Common.SerializeObject(new QueryListParam(
					itemType: typeof(MasterTokui),
					where: $"Code='{EscapeSqlLiteral(code)}'",
					order: "Code",
					maxCount: 1
				))
			};
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
			if (Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is System.Collections.IList list) {
				return list.Cast<MasterTokui>().FirstOrDefault();
			}
		}
		catch (Exception ex) {
			Debug.WriteLine(ex);
		}
		return null;
	}
}
