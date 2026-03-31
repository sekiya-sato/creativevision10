using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Asset;
using Cvnet10Base;
using Cvnet10Base.Share;
using Cvnet10Wpfclient.Helpers;
using Cvnet10Wpfclient.ViewModels.Sub;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Cvnet10Wpfclient.ViewModels._06Uriage;

public partial class ShopUriageInputViewModel : Helpers.BasePlainLightMenteViewModel<Tran01Tenuri> {

	[ObservableProperty]
	int selectedTabIndex;

	[ObservableProperty]
	ObservableCollection<Tran99Meisai> editMeisai = [];

	[ObservableProperty]
	Tran99Meisai? selectedMeisai;

	SelectInputParameter? selectParam;

	public List<EnumUri01> KubunOptions { get; } = [
		EnumUri01.Uriage,
		EnumUri01.UriSale,
		EnumUri01.Henpin,
		EnumUri01.HenSale,
		EnumUri01.Other
	];

	protected override Type Tabletype => typeof(Tran01Tenuri);
	protected override string? ListOrder => "DenDay desc, Id desc";
	protected override int? ListMaxCount => selectParam?.MaxCount;
	protected override string LightweightSelectColumns =>
		"Id,Vdc,Vdu,DenDay,Id_Tenpo,VTenpo,Id_Soko,VSoko,SuTotal,KingakuTotal";

	protected override ValueTask<bool> BeforeListAsync(CancellationToken ct) {
		ct.ThrowIfCancellationRequested();
		var win = new Views.Sub.RangeInputParamView();
		if (win.DataContext is not RangeInputParamViewModel vm) return new ValueTask<bool>(false);
		selectParam ??= new SelectInputParameter {
			DisplayName = "店舗売上",
			ToriLabel = "店舗CD",
			IsToriVisible = true,
		};
		vm.Initialize(selectParam);
		if (ClientLib.ShowDialogView(win, this, true) != true) return new ValueTask<bool>(false);
		selectParam = vm.Parameter;
		return new ValueTask<bool>(true);
	}

	protected override string? ListWhere {
		get {
			if (selectParam == null) return null;
			List<string> clauses = [];
			if (selectParam.FromId.HasValue) clauses.Add($"Id >= {selectParam.FromId.Value}");
			if (selectParam.ToId.HasValue) clauses.Add($"Id <= {selectParam.ToId.Value}");
			if (!string.IsNullOrWhiteSpace(selectParam.FromDate)) clauses.Add($"DenDay >= '{EscapeSqlLiteral(selectParam.FromDate)}'");
			if (!string.IsNullOrWhiteSpace(selectParam.ToDate)) clauses.Add($"DenDay <= '{EscapeSqlLiteral(selectParam.ToDate)}'");
			if (!string.IsNullOrWhiteSpace(selectParam.FromToriCd)) clauses.Add($"json_extract(VTenpo,'$.Cd') >= '{EscapeSqlLiteral(selectParam.FromToriCd)}'");
			if (!string.IsNullOrWhiteSpace(selectParam.ToToriCd)) clauses.Add($"json_extract(VTenpo,'$.Cd') <= '{EscapeSqlLiteral(selectParam.ToToriCd)}'");
			if (!string.IsNullOrWhiteSpace(selectParam.FromSokoCd)) clauses.Add($"json_extract(VSoko,'$.Cd') >= '{EscapeSqlLiteral(selectParam.FromSokoCd)}'");
			if (!string.IsNullOrWhiteSpace(selectParam.ToSokoCd)) clauses.Add($"json_extract(VSoko,'$.Cd') <= '{EscapeSqlLiteral(selectParam.ToSokoCd)}'");
			if (!string.IsNullOrWhiteSpace(selectParam.ShohinCdLike)) clauses.Add($"Jmeisai LIKE '%{EscapeSqlLiteral(selectParam.ShohinCdLike)}%'");
			return clauses.Count == 0 ? null : string.Join(" AND ", clauses);
		}
	}

	protected override void OnCurrentEditChangedCore(Tran01Tenuri? oldValue, Tran01Tenuri newValue) {
		if (newValue == null) return;
		ApplyMeisaiFromCurrentEdit();
	}

	void ApplyMeisaiFromCurrentEdit() {
		foreach (var m in EditMeisai) m.PropertyChanged -= OnMeisaiPropertyChanged;
		EditMeisai = new ObservableCollection<Tran99Meisai>(
			CurrentEdit.Jmeisai?.Select(Common.CloneObject) ?? []);
		foreach (var m in EditMeisai) m.PropertyChanged += OnMeisaiPropertyChanged;
		UpdateTotals();
	}

	void SyncMeisaiToCurrentEdit() {
		foreach (var m in EditMeisai) m.Kubun = CurrentEdit.Kubun;
		CurrentEdit.Jmeisai = [.. EditMeisai];
		UpdateTotals();
	}

	void OnMeisaiPropertyChanged(object? sender, PropertyChangedEventArgs e) {
		if (e.PropertyName is nameof(Tran99Meisai.Su) or nameof(Tran99Meisai.Kingaku)) {
			UpdateTotals();
		}
	}

	void UpdateTotals() {
		CurrentEdit.SuTotal = EditMeisai.Sum(m => m.Su);
		CurrentEdit.KingakuTotal = EditMeisai.Sum(m => m.Kingaku);
	}

	protected override object CreateInsertParam() {
		SyncMeisaiToCurrentEdit();
		return base.CreateInsertParam();
	}

	protected override object CreateUpdateParam() {
		SyncMeisaiToCurrentEdit();
		return base.CreateUpdateParam();
	}

	[RelayCommand]
	void GoToDetail() {
		if (Current.Id > 0) SelectedTabIndex = 1;
	}

	[RelayCommand]
	async Task Init() {
		await DoList(CancellationToken.None);
	}

	[RelayCommand]
	void AddMeisai() {
		var nextNo = EditMeisai.Count > 0 ? EditMeisai.Max(m => m.No) + 1 : 1;
		var newMeisai = new Tran99Meisai { No = nextNo, Kubun = CurrentEdit.Kubun };
		newMeisai.PropertyChanged += OnMeisaiPropertyChanged;
		EditMeisai.Add(newMeisai);
	}

	[RelayCommand]
	void DeleteMeisai() {
		if (SelectedMeisai == null) return;
		SelectedMeisai.PropertyChanged -= OnMeisaiPropertyChanged;
		EditMeisai.Remove(SelectedMeisai);
		SelectedMeisai = null;
		UpdateTotals();
	}

	[RelayCommand]
	void DoSelectTenpo() {
		var tokui = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType>=0", "Code", startPos: CurrentEdit.Id_Tenpo);
		if (tokui == null) return;
		CurrentEdit.Id_Tenpo = tokui.Id;
		CurrentEdit.VTenpo = new CodeNameView { Sid = tokui.Id, Cd = tokui.Code ?? "", Mei = tokui.Name ?? "" };
	}

	[RelayCommand]
	void DoSelectSoko() {
		var tokui = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType=0", "Code", startPos: CurrentEdit.Id_Soko);
		if (tokui == null) return;
		CurrentEdit.Id_Soko = tokui.Id;
		CurrentEdit.VSoko = new CodeNameView { Sid = tokui.Id, Cd = tokui.Code ?? "", Mei = tokui.Name ?? "" };
	}

	[RelayCommand]
	void DoSelectShohin() {
		if (SelectedMeisai == null) return;
		var shohin = ShowSelectDialog<MasterShohin>(typeof(MasterShohin), "", "Code", startPos: SelectedMeisai.Id_Shohin);
		if (shohin == null) return;
		SelectedMeisai.Id_Shohin = shohin.Id;
		SelectedMeisai.Code_Shohin = shohin.Code ?? "";
		SelectedMeisai.Mei_Shohin = shohin.Name ?? "";
	}

	[RelayCommand]
	void DoSelectCol() {
		if (SelectedMeisai == null) return;
		var meisho = ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), "Kubun='COL'", "Code", startPos: SelectedMeisai.Id_Col);
		if (meisho == null) return;
		SelectedMeisai.Id_Col = meisho.Id;
		SelectedMeisai.Code_Col = meisho.Code ?? "";
		SelectedMeisai.Mei_Col = meisho.Name ?? "";
	}

	[RelayCommand]
	void DoSelectSiz() {
		if (SelectedMeisai == null) return;
		var meisho = ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), "Kubun='SIZ'", "Code", startPos: SelectedMeisai.Id_Siz);
		if (meisho == null) return;
		SelectedMeisai.Id_Siz = meisho.Id;
		SelectedMeisai.Code_Siz = meisho.Code ?? "";
		SelectedMeisai.Mei_Siz = meisho.Name ?? "";
	}

	protected override string GetInsertConfirmMessage() => $"追加しますか？ (伝票No={CurrentEdit.Id})";
	protected override string GetUpdateConfirmMessage() => $"修正しますか？ (伝票No={CurrentEdit.Id})";
	protected override string GetDeleteConfirmMessage() => $"削除しますか？ (伝票No={CurrentEdit.Id})";
}
