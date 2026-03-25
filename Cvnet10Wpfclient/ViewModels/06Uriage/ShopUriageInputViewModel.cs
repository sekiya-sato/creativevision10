using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Asset;
using Cvnet10Base;
using Cvnet10Wpfclient.ViewModels.Sub;
using Cvnet10Wpfclient.ViewServices;
using System.Collections.ObjectModel;

namespace Cvnet10Wpfclient.ViewModels._06Uriage;

public partial class ShopUriageInputViewModel : Helpers.BaseMenteViewModel<Tran01Tenuri> {
	// タブ選択インデックス
	[ObservableProperty]
	public int selectedTabIndex;

	// 売上日 (DenDay の DateTime ラッパー)
	DateTime _uriageDate = DateTime.Today;
	public DateTime UriageDate {
		get => _uriageDate;
		set {
			if (SetProperty(ref _uriageDate, value)) {
				CurrentEdit.DenDay = value.ToString("yyyyMMdd");
			}
		}
	}

	// 取引区分リスト
	public IEnumerable<EnumUri01> KubunItems => Enum.GetValues<EnumUri01>();

	// 明細リスト (CurrentEdit.Jmeisai の ObservableCollection ラッパー)
	[ObservableProperty]
	ObservableCollection<Tran99Meisai> detailItems = [];

	[ObservableProperty]
	Tran99Meisai? selectedDetail;

	// 一覧検索パラメータ
	SelectInputParameter? selectInputParam;

	protected override string? ListWhere => selectInputParam != null ? BuildTenUriWhere(selectInputParam) : "1=0";
	protected override string? ListOrder => "DenDay DESC, Id DESC";

	// CurrentEdit変更時に UriageDate と DetailItems を同期
	protected override void OnCurrentChangedCore(Tran01Tenuri? oldValue, Tran01Tenuri newValue) {
		base.OnCurrentChangedCore(oldValue, newValue);
		SyncFromCurrentEdit();
	}

	void SyncFromCurrentEdit() {
		if (DateTime.TryParseExact(CurrentEdit.DenDay, "yyyyMMdd",
			System.Globalization.CultureInfo.InvariantCulture,
			System.Globalization.DateTimeStyles.None, out var dt)) {
			SetProperty(ref _uriageDate, dt, nameof(UriageDate));
		}
		DetailItems = CurrentEdit.Jmeisai != null
			? new ObservableCollection<Tran99Meisai>(CurrentEdit.Jmeisai)
			: [];
	}

	// ヘッダ一覧表示 (RangeInputParamView で条件設定 → DoList)
	[RelayCommand]
	async Task ShowHeaderList(CancellationToken ct) {
		var selWin = new Views.Sub.RangeInputParamView();
		if (selWin.DataContext is not RangeInputParamViewModel vm) return;
		vm.Initialize(selectInputParam ?? new SelectInputParameter { DisplayName = "店舗売上" });
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		selectInputParam = vm.Parameter;
		await DoList(ct);
	}

	// 明細タブへ移動
	[RelayCommand]
	void GoToDetailTab() {
		SelectedTabIndex = 1;
	}

	// 伝票確定: CurrentEdit の DenDay を今日に設定
	[RelayCommand]
	void FixSlipNo() {
		UriageDate = DateTime.Today;
		Message = $"売上日を {DateTime.Today:yyyy/MM/dd} に確定しました";
	}

	// 採番: 新規エントリ準備 (Id=0, 明細クリア)
	[RelayCommand]
	void IssueSlipNo() {
		CurrentEdit = new Tran01Tenuri { DenDay = DateTime.Today.ToString("yyyyMMdd") };
		SetProperty(ref _uriageDate, DateTime.Today, nameof(UriageDate));
		DetailItems = [];
		SelectedTabIndex = 1;
		Message = "新規入力モードです";
	}

	// 再読込: Current から CurrentEdit を復元
	[RelayCommand]
	void ReloadSlip() {
		if (Current.Id <= 0) return;
		CurrentEdit = Common.CloneObject(Current);
		SyncFromCurrentEdit();
		Message = "再読込しました";
	}

	// 店舗選択
	[RelayCommand]
	void SelectTenpo() {
		var selWin = new Views.Sub.SelectWinView();
		if (selWin.DataContext is not Sub.SelectWinViewModel vm) return;
		vm.SetParam(typeof(MasterTokui), "TenType=6", "Code", startPos: CurrentEdit.Id_Tenpo);
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		if (vm.Current is MasterTokui tenpo) {
			CurrentEdit.Id_Tenpo = tenpo.Id;
			CurrentEdit.VTenpo = new() { Sid = tenpo.Id, Cd = tenpo.Code, Mei = tenpo.Name };
		}
	}

	// 顧客コードで検索
	[RelayCommand]
	async Task LookupCustomerByCode(CancellationToken ct) {
		if (string.IsNullOrWhiteSpace(CurrentEdit.Code_Customer)) return;
		var msg = new CvnetMsg {
			Code = 0,
			Flag = CvnetFlag.Msg101_Op_Query,
			DataType = typeof(QueryListParam),
			DataMsg = Common.SerializeObject(new QueryListParam(
				itemType: typeof(MasterEndCustomer),
				where: $"Code='{CurrentEdit.Code_Customer}'",
				order: "Code",
				maxCount: 1))
		};
		var reply = await SendMessageAsync(msg, ct);
		if (Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType)
			is System.Collections.IList list && list.Count > 0 && list[0] is MasterEndCustomer cust) {
			CurrentEdit.Id_Customer = cust.Id;
			CurrentEdit.VCustomer = new() { Sid = cust.Id, Cd = cust.Code, Mei = cust.Name };
		}
	}

	// 顧客選択ダイアログ
	[RelayCommand]
	void SelectCustomer() {
		var selWin = new Views.Sub.SelectWinView();
		if (selWin.DataContext is not Sub.SelectWinViewModel vm) return;
		vm.SetParam(typeof(MasterEndCustomer), "", "Code", startPos: CurrentEdit.Id_Customer);
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		if (vm.Current is MasterEndCustomer cust) {
			CurrentEdit.Id_Customer = cust.Id;
			CurrentEdit.Code_Customer = cust.Code;
			CurrentEdit.VCustomer = new() { Sid = cust.Id, Cd = cust.Code, Mei = cust.Name };
		}
	}

	// 担当者選択
	[RelayCommand]
	void SelectShain() {
		var selWin = new Views.Sub.SelectWinView();
		if (selWin.DataContext is not Sub.SelectWinViewModel vm) return;
		vm.SetParam(typeof(MasterShain), "", "Code", startPos: CurrentEdit.Id_Shain);
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		if (vm.Current is MasterShain shain) {
			CurrentEdit.Id_Shain = shain.Id;
			CurrentEdit.VShain = new() { Sid = shain.Id, Cd = shain.Code, Mei = shain.Name };
		}
	}

	// 倉庫選択 (MasterTokui TenType=0)
	[RelayCommand]
	void SelectSoko() {
		var selWin = new Views.Sub.SelectWinView();
		if (selWin.DataContext is not Sub.SelectWinViewModel vm) return;
		vm.SetParam(typeof(MasterTokui), "TenType=0", "Code", startPos: CurrentEdit.Id_Soko);
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		if (vm.Current is MasterTokui soko) {
			CurrentEdit.Id_Soko = soko.Id;
			CurrentEdit.VSoko = new() { Sid = soko.Id, Cd = soko.Code, Mei = soko.Name };
		}
	}

	// 商品選択 (明細行)
	[RelayCommand]
	void SelectShohin(Tran99Meisai? row) {
		if (row == null) return;
		var selWin = new Views.Sub.SelectWinView();
		if (selWin.DataContext is not Sub.SelectWinViewModel vm) return;
		vm.SetParam(typeof(MasterShohin), "", "Code", startPos: row.Id_Shohin);
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		if (vm.Current is MasterShohin shohin) {
			row.Id_Shohin = shohin.Id;
			row.Code_Shohin = shohin.Code;
			row.Mei_Shohin = shohin.Name;
			row.Tanka = shohin.TankaJodai;
			RecalculateRow(row);
			RecalculateSummary();
		}
	}

	// 色選択 (明細行)
	[RelayCommand]
	void SelectColor(Tran99Meisai? row) {
		if (row == null) return;
		var selWin = new Views.Sub.SelectWinView();
		if (selWin.DataContext is not Sub.SelectWinViewModel vm) return;
		vm.SetParam(typeof(MasterMeisho), "Kubun='COL'", "Code", startPos: row.Id_Col);
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		if (vm.Current is MasterMeisho col) {
			row.Id_Col = col.Id;
			row.Code_Col = col.Code;
			row.Mei_Col = col.Name;
		}
	}

	// サイズ選択 (明細行)
	[RelayCommand]
	void SelectSize(Tran99Meisai? row) {
		if (row == null) return;
		var selWin = new Views.Sub.SelectWinView();
		if (selWin.DataContext is not Sub.SelectWinViewModel vm) return;
		vm.SetParam(typeof(MasterMeisho), "Kubun='SIZ'", "Code", startPos: row.Id_Siz);
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		if (vm.Current is MasterMeisho siz) {
			row.Id_Siz = siz.Id;
			row.Code_Siz = siz.Code;
			row.Mei_Siz = siz.Name;
		}
	}

	// 明細行追加
	[RelayCommand]
	void AddDetailRow() {
		var newRow = new Tran99Meisai { No = DetailItems.Count + 1 };
		DetailItems.Add(newRow);
		SyncDetailToCurrentEdit();
	}

	// 明細行削除
	[RelayCommand]
	void RemoveDetailRow() {
		if (SelectedDetail == null) return;
		DetailItems.Remove(SelectedDetail);
		for (int i = 0; i < DetailItems.Count; i++) {
			DetailItems[i].No = i + 1;
		}
		SyncDetailToCurrentEdit();
		RecalculateSummary();
	}

	// サマリ再計算
	[RelayCommand]
	void RecalculateSummary() {
		CurrentEdit.SuTotal = DetailItems.Sum(r => r.Su);
		CurrentEdit.KingakuTotal = DetailItems.Sum(r => r.Kingaku);
		CurrentEdit.JodaiTotal = DetailItems.Sum(r => r.Jodai);
		CurrentEdit.Nebiki00Total = DetailItems.Sum(r => r.Nebiki00);
		SyncDetailToCurrentEdit();
	}

	void SyncDetailToCurrentEdit() {
		CurrentEdit.Jmeisai = [.. DetailItems];
	}

	static void RecalculateRow(Tran99Meisai row) {
		row.Kingaku = row.Su * row.Tanka;
	}

	static string BuildTenUriWhere(SelectInputParameter param) {
		var clauses = new System.Collections.Generic.List<string>();
		if (param.FromId.HasValue) clauses.Add($"Id >= {param.FromId.Value}");
		if (param.ToId.HasValue) clauses.Add($"Id <= {param.ToId.Value}");
		if (!string.IsNullOrWhiteSpace(param.FromDate)) clauses.Add($"DenDay >= '{param.FromDate}'");
		if (!string.IsNullOrWhiteSpace(param.ToDate)) clauses.Add($"DenDay <= '{param.ToDate}'");
		return clauses.Count > 0 ? string.Join(" AND ", clauses) : "1=1";
	}

	protected override string GetInsertConfirmMessage() =>
		$"追加しますか？ (売上日={CurrentEdit.DenDay})";

	protected override string GetUpdateConfirmMessage() =>
		$"修正しますか？ (Id={CurrentEdit.Id}, 売上日={CurrentEdit.DenDay})";

	protected override string GetDeleteConfirmMessage() =>
		$"削除しますか？ (Id={CurrentEdit.Id})";

	protected override void AfterInsert(Tran01Tenuri item) {
		Message = $"追加しました (Id={item.Id})";
		Current = item;
		SyncFromCurrentEdit();
	}

	protected override void AfterUpdate(Tran01Tenuri item) {
		Message = $"修正しました (Id={item.Id})";
	}

	protected override void AfterDelete(Tran01Tenuri removedItem) {
		Message = $"削除しました (Id={removedItem.Id})";
		DetailItems = [];
	}
}
