using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._01Master;

/// <summary>
/// 仕入先マスターメンテ ViewModel
/// </summary>
public partial class MasterShiireMenteViewModel : Helpers.BaseCodeNameLightMenteViewModel<MasterShiire> {
	[ObservableProperty]
	string title = "仕入先マスターメンテ";

	protected override string? SelectCodeDisplayName => "仕入先";
	protected override string? FormFile => "MasterShiireMente.qfm";
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
	async Task Init() => await DoList(CancellationToken.None);

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
