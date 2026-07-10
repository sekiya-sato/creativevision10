using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CodeShare;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using System.Collections;
using System.Collections.ObjectModel;

namespace CvWpfclient.ViewModels._01Master;

/// <summary>
/// 仕入先マスターメンテ ViewModel
/// </summary>
public partial class MasterShiireMenteViewModel : Helpers.BaseCodeNameLightMenteViewModel<MasterShiire> {
	[ObservableProperty]
	public partial string Title { get; set; } = "仕入先マスターメンテ";

	protected override string? SelectCodeDisplayName => "仕入先";
	protected override string? FormFile => "MasterShiireMente.qfm";
	public IReadOnlyList<EnumShime> ShimeBiItems { get; } = Enum.GetValues<EnumShime>();
	public IReadOnlyList<PayMonthItem> PayMonthItems { get; } = [
		new(0, "当月"),
		new(1, "翌月"),
		new(2, "翌々月"),
		new(3, "3ヶ月後"),
		new(4, "4ヶ月後"),
	];

	[ObservableProperty]
	public partial MasterGeneralMeisho? SelectedJsub { get; set; }

	[ObservableProperty]
	public partial ObservableCollection<MasterGeneralMeisho> EditJsub { get; set; } = [];

	public ObservableCollection<string> KubunOptions { get; } = new(Enumerable.Range(1, 10).Select(i => $"D{i:D2}"));
	public List<MasterMeisho> KubunList = [];

	protected override QueryListSqlParam? PrintBySqlParam {
		get {
			var query = CreateListQueryParam();
			var sql = @$"
select Id, __serverdate__(Vdc) Vdcdate, __serverdate__(Vdu) Vdudate,
Code, Name, Ryaku, Kana,
trim(ifnull(json_extract(VShain,'$.Cd'),'') || ' ' || ifnull(json_extract(VShain,'$.Mei'),'')) Shain,
RateProper, RateSale,
PostalCode, Address1, Address2, Address3, Tel,
case when Shime1 = 99 then '末日' when Shime1 > 0 then cast(Shime1 as text) else '' end Shime1Text,
case when Shime2 = 99 then '末日' when Shime2 > 0 then cast(Shime2 as text) else '' end Shime2Text,
case when Shime3 = 99 then '末日' when Shime3 > 0 then cast(Shime3 as text) else '' end Shime3Text,
case when PayMonth > 0 then cast(PayMonth as text) else '' end PayMonthText,
case when PayDay = 99 then '末日' when PayDay > 0 then cast(PayDay as text) else '' end PayDayText,
case IsPay when 1 then 'する' else 'しない' end IsPayText,
trim(ifnull(json_extract(VPayMethod,'$.Cd'),'') || ' ' || ifnull(json_extract(VPayMethod,'$.Mei'),'')) PayMethod,
trim(ifnull(json_extract(VPaysaki,'$.Cd'),'') || ' ' || ifnull(json_extract(VPaysaki,'$.Mei'),'')) Paysaki,
ifnull(json_extract(Jdetail,'$.Bank1'),'') BankAccount1,
ifnull(json_extract(Jdetail,'$.Bank2'),'') BankAccount2,
ifnull(json_extract(Jdetail,'$.Bank3'),'') BankAccount3
from MasterShiire {query.AddWhereOrder()}
";
			return new QueryListSqlParam(typeof(MasterShiire), sql, query.Parameters);
		}
	}

	[RelayCommand]
	async Task Init() {
		await DoGetKubun(CancellationToken.None);
		await DoList(CancellationToken.None);
	}

	protected override void OnCurrentEditChangedCore(MasterShiire? oldValue, MasterShiire newValue) {
		if (newValue == null) {
			EditJsub = [];
			return;
		}
		ApplySubListsFromCurrentEdit();
	}

	void ApplySubListsFromCurrentEdit() {
		var jsubClones = (CurrentEdit.Jsub?.Select(Common.CloneObject) ?? []).ToList();
		foreach (var item in jsubClones) item.BaseList = KubunList;
		EditJsub = new ObservableCollection<MasterGeneralMeisho>(jsubClones);
	}

	void SyncSubListsToCurrentEdit() => CurrentEdit.Jsub = [.. EditJsub];

	protected override object CreateInsertParam() {
		SyncSubListsToCurrentEdit();
		return base.CreateInsertParam();
	}

	protected override object CreateUpdateParam() {
		SyncSubListsToCurrentEdit();
		return base.CreateUpdateParam();
	}

	async Task DoGetKubun(CancellationToken ct) {
		if (KubunList.Count > 0) return;
		try {
			ClientLib.Cursor2Wait();
			var param = new QueryListParam(typeof(MasterMeisho), $"Kubun='IDX' and Code in ({string.Join(",", KubunOptions.Select(x => $"'{x}'"))})", "Code");
			var msg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg101_Op_Query,
				DataType = typeof(QueryListParam),
				DataMsg = Common.SerializeObject(param)
			};
			var reply = await SendMessageAsync(msg, ct);
			if (Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is IList list) {
				KubunList.Clear();
				foreach (var item in list.Cast<MasterMeisho>()) KubunList.Add(item);
			}
		}
		catch (OperationCanceledException cancel) {
			Message = $"Cancelエラー: {cancel.Message}";
			return;
		}
		catch (Exception ex) {
			Message = $"データ取得失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			ClientLib.Cursor2Normal();
		}
	}

	[RelayCommand]
	void DoSelectShain() {
		var shain = ShowSelectDialog<MasterShain>(typeof(MasterShain), "", "Code", startPos: CurrentEdit.Id_Shain);
		if (shain == null) return;
		CurrentEdit.Id_Shain = shain.Id;
		CurrentEdit.VShain = new() { Sid = shain.Id, Cd = shain.Code ?? "", Mei = shain.Name ?? "" };
	}

	[RelayCommand]
	void DoSelectPayMethod() {
		var meisho = ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), "Kubun='PAY'", "Code", startPos: CurrentEdit.Id_PayMethod);
		if (meisho == null) return;
		CurrentEdit.Id_PayMethod = meisho.Id;
		CurrentEdit.VPayMethod = new() { Sid = meisho.Id, Cd = meisho.Code ?? "", Mei = meisho.Name ?? "" };
	}

	[RelayCommand]
	void DoSelectPaysaki() {
		var shiire = ShowSelectDialog<MasterShiire>(typeof(MasterShiire), "", "Code", startPos: CurrentEdit.Id_Paysaki);
		if (shiire == null) return;
		CurrentEdit.Id_Paysaki = shiire.Id;
		CurrentEdit.VPaysaki = new() { Sid = shiire.Id, Cd = shiire.Code ?? "", Mei = shiire.Name ?? "" };
	}

	[RelayCommand]
	void AddJsub() {
		var newItem = new MasterGeneralMeisho { BaseList = KubunList };
		EditJsub.Add(newItem);
		SortJsub();
		SelectedJsub = newItem;
	}

	[RelayCommand]
	void DeleteJsub() {
		if (SelectedJsub == null) return;
		EditJsub.Remove(SelectedJsub);
		SelectedJsub = EditJsub.LastOrDefault();
	}

	[RelayCommand]
	void DoSelectJsubCode() {
		if (SelectedJsub == null) return;
		var kb = (SelectedJsub.Kb ?? string.Empty).Replace("'", "''");
		if (string.IsNullOrEmpty(kb)) return;
		var meisho = ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), $"Kubun='{kb}'", "Code", startPos: SelectedJsub.Sid);
		if (meisho == null) return;
		SelectedJsub.Cd = meisho.Code ?? "";
		SelectedJsub.Mei = meisho.Name ?? "";
	}

	void SortJsub() {
		var sorted = EditJsub.OrderBy(x => x.Kb).ToList();
		EditJsub.Clear();
		foreach (var item in sorted) EditJsub.Add(item);
	}

	[RelayCommand]
	async Task SearchPostalCode() => await PostalAddressSearchHelper.SearchAndApplyAsync(this, CurrentEdit.PostalCode ?? string.Empty, item => {
		var currentAddress1 = CurrentEdit.Address1;
		var currentAddress2 = CurrentEdit.Address2;
		var currentAddress3 = CurrentEdit.Address3;
		CurrentEdit.PostalCode = item.PostalCode;
		CurrentEdit.Address1 = item.Address1;
		CurrentEdit.Address2 = item.Address2;
		CurrentEdit.Address3 = PostalAddressSearchHelper.MergeAddress3(currentAddress1, currentAddress2, currentAddress3, item);
	});
}

public sealed record PayMonthItem(int Value, string Text);
