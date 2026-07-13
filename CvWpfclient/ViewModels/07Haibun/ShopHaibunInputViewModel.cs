using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels.Sub;
using System.Collections.ObjectModel;

namespace CvWpfclient.ViewModels._07Haibun;

/// <summary>店舗配分データを <see cref="TranHaibun"/> に登録・保守する画面の ViewModel。</summary>
public partial class ShopHaibunInputViewModel : BasePlainLightMenteViewModel<TranHaibun> {
	public const int KubunHatsukai = 0;
	public const int KubunZaiko = 1;

	public sealed record HaibunKubunOption(int Value, string Name);

	public IReadOnlyList<HaibunKubunOption> KubunOptions { get; } = [
		new(KubunHatsukai, "初回配分"),
		new(KubunZaiko, "在庫配分"),
	];

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(DoSearchCommand))]
	[NotifyCanExecuteChangedFor(nameof(GoToEditCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoInsertOnDetailTabCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoUpdateOnDetailTabCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoDeleteOnDetailTabCommand))]
	public partial int SelectedTabIndex { get; set; }

	[ObservableProperty]
	public partial long Id_Soko { get; set; }

	[ObservableProperty]
	public partial string SokoCode { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SokoName { get; set; } = string.Empty;

	[ObservableProperty]
	public partial int Kubun { get; set; } = KubunHatsukai;

	[ObservableProperty]
	public partial DateTime? UriageDayFrom { get; set; }

	[ObservableProperty]
	public partial DateTime? UriageDayTo { get; set; }

	// 既存レイアウトの入力欄。TranHaibun 単体検索では日付・倉庫・区分を条件に使用する。
	[ObservableProperty] public partial string SeasonFrom { get; set; } = string.Empty;
	[ObservableProperty] public partial string SeasonTo { get; set; } = string.Empty;
	[ObservableProperty] public partial string BrandFrom { get; set; } = string.Empty;
	[ObservableProperty] public partial string BrandTo { get; set; } = string.Empty;
	[ObservableProperty] public partial string ItemFrom { get; set; } = string.Empty;
	[ObservableProperty] public partial string ItemTo { get; set; } = string.Empty;
	[ObservableProperty] public partial DateTime? NyukaDayFrom { get; set; }

	[ObservableProperty]
	public partial ObservableCollection<ShopHaibunSearchRow> SearchRows { get; set; } = [];

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(GoToEditCommand))]
	public partial ShopHaibunSearchRow? SelectedSearchRow { get; set; }

	[ObservableProperty]
	public partial ObservableCollection<TranHaibun> EntryRows { get; set; } = [];

	[ObservableProperty]
	public partial TranHaibun? SelectedEntryRow { get; set; }

	[ObservableProperty]
	public partial string TargetShohinCode { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TargetShohinName { get; set; } = string.Empty;

	[ObservableProperty]
	public partial decimal TargetJodai { get; set; }

	[ObservableProperty]
	public partial DateTime? ShijiDay { get; set; } = DateTime.Today;

	[ObservableProperty]
	public partial DateTime? NouhinDay { get; set; } = DateTime.Today;

	[ObservableProperty]
	public partial string Nyuryokusha { get; set; } = string.Empty;

	public int SearchCount => Count;
	public int TotalSu => CurrentEdit.Su;

	bool IsListTabSelected() => SelectedTabIndex == 0;
	bool IsDetailTabSelected() => SelectedTabIndex == 1;
	bool CanGoToEdit() => IsListTabSelected() && SelectedSearchRow != null;

	protected override Type Tabletype => typeof(TranHaibun);
	protected override string? ListOrder => "DenDay desc, Id desc";
	protected override int? ListMaxCount => AppGlobal.Limit;
	protected override string LightweightSelectColumns =>
		"Id,Vdc,Vdu,DenDay,NouhinDay,Id_Soko,Id_Tenpo,Kubun,SendFlg,Id_Shohin,JanCode,Id_Col,Id_Siz,Su,Tanka,Kingaku,Jodai,Gedai,RelateNo1,RelateNo2,Memo,KakuteiDay,JitsuSu,Id_Shain";

	protected override string? ListWhere {
		get {
			List<string> clauses = [];
			if (Id_Soko > 0) clauses.Add($"Id_Soko = {Id_Soko}");
			clauses.Add($"Kubun = {Kubun}");
			if (UriageDayFrom.HasValue) clauses.Add($"DenDay >= '{ToYmd8(UriageDayFrom)}'");
			if (UriageDayTo.HasValue) clauses.Add($"DenDay <= '{ToYmd8(UriageDayTo)}'");
			return string.Join(" AND ", clauses);
		}
	}

	protected override void AfterList(System.Collections.IList list) {
		SearchRows = new ObservableCollection<ShopHaibunSearchRow>(
			ListData.Select(item => new ShopHaibunSearchRow(item)));
		OnPropertyChanged(nameof(SearchCount));
	}

	protected override void OnCurrentEditChangedCore(TranHaibun? oldValue, TranHaibun newValue) {
		EntryRows = [newValue];
		SelectedEntryRow = newValue;
		ShijiDay = FromYmd8(newValue.DenDay);
		NouhinDay = FromYmd8(newValue.NouhinDay);
		TargetShohinCode = newValue.Id_Shohin > 0 ? newValue.Id_Shohin.ToString() : string.Empty;
		TargetShohinName = newValue.JanCode;
		TargetJodai = newValue.Jodai;
		Nyuryokusha = newValue.Id_Shain > 0 ? newValue.Id_Shain.ToString() : string.Empty;
	}

	partial void OnShijiDayChanged(DateTime? value) => CurrentEdit.DenDay = ToYmd8(value);
	partial void OnNouhinDayChanged(DateTime? value) => CurrentEdit.NouhinDay = ToYmd8(value);
	partial void OnNyuryokushaChanged(string value) {
		if (long.TryParse(value, out var id)) CurrentEdit.Id_Shain = id;
	}

	[RelayCommand]
	async Task Init() => await DoList(CancellationToken.None);

	[RelayCommand(CanExecute = nameof(IsListTabSelected), IncludeCancelCommand = true)]
	async Task DoSearch(CancellationToken ct) => await DoList(ct);

	[RelayCommand]
	void DoSelectSoko() {
		var soko = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType=0", "Code", Id_Soko);
		if (soko == null) return;
		Id_Soko = soko.Id;
		SokoCode = soko.Code;
		SokoName = soko.Name;
	}

	[RelayCommand(CanExecute = nameof(CanGoToEdit))]
	void GoToEdit() {
		if (SelectedSearchRow == null) return;
		Current = SelectedSearchRow.Source;
		SelectedTabIndex = 1;
	}

	[RelayCommand]
	void GoToNew() {
		Current = new TranHaibun {
			DenDay = ToYmd8(DateTime.Today),
			NouhinDay = ToYmd8(DateTime.Today),
			Id_Soko = Id_Soko,
			Kubun = Kubun,
		};
		SelectedTabIndex = 1;
	}

	[RelayCommand]
	void GoToSearch() => SelectedTabIndex = 0;

	[RelayCommand]
	void DoSelectTenpo() {
		var tenpo = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType>=0", "Code", CurrentEdit.Id_Tenpo);
		if (tenpo == null) return;
		CurrentEdit.Id_Tenpo = tenpo.Id;
	}

	[RelayCommand]
	void DoSelectShohin() {
		var shohin = ShowSelectDialog<MasterShohin>(typeof(MasterShohin), string.Empty, "Code", CurrentEdit.Id_Shohin);
		if (shohin == null) return;
		CurrentEdit.Id_Shohin = shohin.Id;
		CurrentEdit.JanCode = string.Empty;
		CurrentEdit.Jodai = shohin.TankaJodai;
		CurrentEdit.Gedai = shohin.TankaGenka;
		CurrentEdit.Tanka = shohin.TankaJodai;
		TargetShohinCode = shohin.Code;
		TargetShohinName = shohin.Name;
		TargetJodai = shohin.TankaJodai;
	}

	[RelayCommand]
	void DoSelectSku() {
		if (CurrentEdit.Id_Shohin <= 0) {
			MessageEx.ShowWarningDialog("先に商品を選択してください", owner: ClientLib.GetActiveView(this));
			return;
		}
		var win = new Views.Sub.SelectShohinColSizView();
		if (win.DataContext is not SelectShohinColSizViewModel vm) return;
		vm.SetParam(CurrentEdit.Id_Shohin, CurrentEdit.Id_Col, CurrentEdit.Id_Siz, filterByColor: false);
		if (ClientLib.ShowDialogView(win, this) != true) return;
		var sku = vm.Current;
		CurrentEdit.Id_Col = sku.Id_Col;
		CurrentEdit.Id_Siz = sku.Id_Siz;
		CurrentEdit.JanCode = sku.Jan1;
	}

	[RelayCommand(CanExecute = nameof(IsDetailTabSelected), IncludeCancelCommand = true)]
	async Task DoInsertOnDetailTab(CancellationToken ct) => await DoInsert(ct);

	[RelayCommand(CanExecute = nameof(IsDetailTabSelected), IncludeCancelCommand = true)]
	async Task DoUpdateOnDetailTab(CancellationToken ct) => await DoUpdate(ct);

	[RelayCommand(CanExecute = nameof(IsDetailTabSelected), IncludeCancelCommand = true)]
	async Task DoDeleteOnDetailTab(CancellationToken ct) => await DoDelete(ct);

	static string ToYmd8(DateTime? value) => value?.ToString("yyyyMMdd") ?? string.Empty;

	static DateTime? FromYmd8(string value) =>
		DateTime.TryParseExact(value, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var result)
			? result
			: null;
}

/// <summary>一覧で表示する配分レコード。</summary>
public sealed class ShopHaibunSearchRow(TranHaibun source) {
	public TranHaibun Source { get; } = source;
	public long Id => source.Id;
	public string DenDay => source.DenDay;
	public string NouhinDay => source.NouhinDay;
	public long Id_Soko => source.Id_Soko;
	public long Id_Tenpo => source.Id_Tenpo;
	public long Id_Shohin => source.Id_Shohin;
	public long Id_Col => source.Id_Col;
	public long Id_Siz => source.Id_Siz;
	public string JanCode => source.JanCode;
	public int Su => source.Su;
	public int Jodai => source.Jodai;
}
