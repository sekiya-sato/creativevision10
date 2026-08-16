/*
# description
BaseKinInputViewModel は入金(Tran06Nyukin)・支払(Tran07Shiharai)の伝票入力画面の共通基底クラスです。

この2伝票はどちらも TranKinHeader / TranKinMeisai という同じ構造で、
違うのは「取引先が得意先か仕入先か」と画面名だけです（在庫を動かさないので明細に商品を持たない）。
伝票明細は Tran99Meisai ではなく TranKinMeisai なので BaseTranInputViewModel は使えず、
BasePlainLightMenteViewModel の上に独自の明細管理を持ちます。

# example
public partial class NyukinInputViewModel : Helpers.BaseKinInputViewModel<Tran06Nyukin> {
	protected override string KinDisplayName => "入金";
	protected override string ToriLabel => "得意先Id";
}
 */
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.ViewModels.Sub;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace CvWpfclient.Helpers;

public abstract partial class BaseKinInputViewModel<TDen> : BasePlainLightMenteViewModel<TDen>, ITranInputTab
	where TDen : TranKinHeader, new() {

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(DoListOnListTabCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoUpdateOnDetailTabCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoDeleteOnDetailTabCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoInsertOnDetailTabCommand))]
	public partial int SelectedTabIndex { get; set; }

	[ObservableProperty]
	public partial ObservableCollection<TranKinMeisai> EditMeisai { get; set; } = [];

	[ObservableProperty]
	public partial TranKinMeisai? SelectedMeisai { get; set; }

	/// <summary>画面名（例: "入金" / "支払"）。一覧条件ダイアログと確認メッセージに使う。</summary>
	protected abstract string KinDisplayName { get; }

	/// <summary>一覧条件ダイアログの取引先ラベル（例: "得意先Id"）。</summary>
	protected abstract string ToriLabel { get; }

	SelectInputParameter? selectParam;

	bool IsListTabSelected() => SelectedTabIndex == 0;
	bool IsDetailTabSelected() => SelectedTabIndex == 1;

	protected override Type Tabletype => typeof(TDen);
	protected override string? ListOrder => "KakeDay desc, Id desc";
	protected override int? ListMaxCount => selectParam?.MaxCount;
	protected override string LightweightSelectColumns =>
		"Id,Vdc,Vdu,KakeDay,Id_Shain,VShain,Id_Torisaki,VTori,KingakuTotal,ManualNo,Memo";

	protected override ValueTask<bool> BeforeListAsync(CancellationToken ct) {
		ct.ThrowIfCancellationRequested();
		var win = new Views.Sub.RangeInputParamView();
		if (win.DataContext is not RangeInputParamViewModel vm) return new ValueTask<bool>(false);
		selectParam ??= new SelectInputParameter {
			DisplayName = KinDisplayName,
			ToriLabel = ToriLabel,
			IsToriVisible = true,
			MaxCount = AppGlobal.Limit,
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
			if (!string.IsNullOrWhiteSpace(selectParam.FromDate)) clauses.Add($"KakeDay >= '{EscapeSqlLiteral(selectParam.FromDate)}'");
			if (!string.IsNullOrWhiteSpace(selectParam.ToDate)) clauses.Add($"KakeDay <= '{EscapeSqlLiteral(selectParam.ToDate)}'");
			AddIdInClause(clauses, "Id_Torisaki", selectParam.ToriIds);
			return clauses.Count == 0 ? null : string.Join(" AND ", clauses);
		}
	}

	static void AddIdInClause(List<string> clauses, string column, IEnumerable<long>? ids) {
		var values = ids?
			.Where(id => id > 0)
			.Distinct()
			.Select(id => id.ToString(CultureInfo.InvariantCulture))
			.ToArray() ?? [];
		if (values.Length > 0) clauses.Add($"{column} IN ({string.Join(",", values)})");
	}

	protected override void OnCurrentEditChangedCore(TDen? oldValue, TDen newValue) {
		if (newValue == null) return;
		ApplyMeisaiFromCurrentEdit();
	}

	void ApplyMeisaiFromCurrentEdit() {
		foreach (var meisai in EditMeisai) meisai.PropertyChanged -= OnMeisaiPropertyChanged;
		EditMeisai = new ObservableCollection<TranKinMeisai>(
			CurrentEdit.Jmeisai?.Select(Common.CloneObject) ?? []);
		foreach (var meisai in EditMeisai) meisai.PropertyChanged += OnMeisaiPropertyChanged;
		SelectedMeisai = EditMeisai.FirstOrDefault();
		UpdateTotal();
	}

	void SyncMeisaiToCurrentEdit() {
		CurrentEdit.Jmeisai = [.. EditMeisai];
		UpdateTotal();
	}

	void OnMeisaiPropertyChanged(object? sender, PropertyChangedEventArgs e) {
		if (e.PropertyName == nameof(TranKinMeisai.Kingaku)) UpdateTotal();
	}

	void UpdateTotal() => CurrentEdit.KingakuTotal = EditMeisai.Sum(x => x.Kingaku);

	protected override object CreateInsertParam() {
		SyncMeisaiToCurrentEdit();
		return base.CreateInsertParam();
	}

	protected override object CreateUpdateParam() {
		SyncMeisaiToCurrentEdit();
		return base.CreateUpdateParam();
	}

	[RelayCommand]
	void GoToDetail(TDen? item) {
		if (item != null && item.Id > 0 && !ReferenceEquals(Current, item)) Current = item;
		if (Current.Id <= 0) {
			Current = new TDen {
				KakeDay = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
				Jmeisai = [],
			};
		}
		SelectedTabIndex = 1;
	}

	[RelayCommand]
	void GoToList() => SelectedTabIndex = 0;

	[RelayCommand]
	async Task Init() => await DoList(CancellationToken.None);

	[RelayCommand(CanExecute = nameof(IsListTabSelected), IncludeCancelCommand = true)]
	async Task DoListOnListTab(CancellationToken ct) => await DoList(ct);

	[RelayCommand(CanExecute = nameof(IsDetailTabSelected), IncludeCancelCommand = true)]
	async Task DoUpdateOnDetailTab(CancellationToken ct) => await DoUpdate(ct);

	[RelayCommand(CanExecute = nameof(IsDetailTabSelected), IncludeCancelCommand = true)]
	async Task DoDeleteOnDetailTab(CancellationToken ct) => await DoDelete(ct);

	[RelayCommand(CanExecute = nameof(IsDetailTabSelected), IncludeCancelCommand = true)]
	async Task DoInsertOnDetailTab(CancellationToken ct) => await DoInsert(ct);

	[RelayCommand]
	void AddMeisai() {
		var meisai = new TranKinMeisai {
			No = EditMeisai.Count > 0 ? EditMeisai.Max(x => x.No) + 1 : 1,
		};
		meisai.PropertyChanged += OnMeisaiPropertyChanged;
		EditMeisai.Add(meisai);
		SelectedMeisai = meisai;
		UpdateTotal();
	}

	[RelayCommand]
	void DeleteMeisai() {
		if (SelectedMeisai == null) return;
		SelectedMeisai.PropertyChanged -= OnMeisaiPropertyChanged;
		EditMeisai.Remove(SelectedMeisai);
		for (int i = 0; i < EditMeisai.Count; i++) EditMeisai[i].No = i + 1;
		SelectedMeisai = EditMeisai.LastOrDefault();
		UpdateTotal();
	}

	/// <summary>入金・支払区分(MasterMeisho Kubun='KIN')を明細へ設定する。</summary>
	[RelayCommand]
	void DoSelectKin(TranKinMeisai? meisai) {
		if (meisai != null) SelectedMeisai = meisai;
		if (SelectedMeisai == null) return;
		var meisho = ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), "Kubun='KIN'", "Code", startPos: SelectedMeisai.Id_Kin);
		if (meisho == null) return;
		SelectedMeisai.Id_Kin = meisho.Id;
		SelectedMeisai.Code_Kin = meisho.Code ?? string.Empty;
		SelectedMeisai.Mei_Kin = meisho.Name ?? string.Empty;
	}

	[RelayCommand]
	void DoSelectShain() {
		var shain = ShowSelectDialog<MasterShain>(typeof(MasterShain), "", "Code", startPos: CurrentEdit.Id_Shain);
		if (shain == null) return;
		CurrentEdit.Id_Shain = shain.Id;
		CurrentEdit.VShain = new CodeNameView { Sid = shain.Id, Cd = shain.Code ?? string.Empty, Mei = shain.Name ?? string.Empty };
	}

	/// <summary>取引先(得意先/仕入先)をヘッダへ設定する。派生の選択コマンドから呼ぶ。</summary>
	protected void ApplyTorisaki(long id, string? code, string? name) {
		CurrentEdit.Id_Torisaki = id;
		CurrentEdit.VTori = new CodeNameView { Sid = id, Cd = code ?? string.Empty, Mei = name ?? string.Empty };
	}

	protected override string GetInsertConfirmMessage() => $"追加しますか？ ({KinDisplayName}No={CurrentEdit.Id})";
	protected override string GetUpdateConfirmMessage() => $"修正しますか？ ({KinDisplayName}No={CurrentEdit.Id})";
	protected override string GetDeleteConfirmMessage() => $"削除しますか？ ({KinDisplayName}No={CurrentEdit.Id})";
}
