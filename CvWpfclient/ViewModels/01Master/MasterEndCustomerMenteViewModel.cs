using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._01Master;

public partial class MasterEndCustomerMenteViewModel : Helpers.BaseCodeNameLightMenteViewModel<MasterEndCustomer> {
	[ObservableProperty]
	string title = "顧客マスターメンテ";

	protected override string[] AdditionalLightweightColumns => ["Rank", "VTenpo"];

	protected override string? SelectCodeDisplayName => "顧客";
	protected override string? FormFile => "MasterEndCustomerMente.qfm";
	protected override QueryListSqlParam? PrintBySqlParam {
		get {
			var query = CreateListQueryParam();
			var sql = @$"
select Id, __serverdate__(Vdc) Vdcdate, __serverdate__(Vdu) Vdudate,
Code, Name, Ryaku, Kana, Rank,
trim(ifnull(json_extract(VTenpo,'$.Cd'),'') || ' ' || ifnull(json_extract(VTenpo,'$.Mei'),'')) Tenpo,
Birthday, BirthNoyear, Memo,
case Gendar when 1 then '男性' when 2 then '女性' else '不明' end GendarText,
Point, SalesCount, SalesKingaku,
PostalCode, Address1, Address2, Address3, Tel, Mail
from MasterEndCustomer {query.AddWhereOrder()}
";
			return new QueryListSqlParam(typeof(MasterEndCustomer), sql, query.Parameters);
		}
	}

	[RelayCommand]
	async Task Init() => await DoList(CancellationToken.None);

	[RelayCommand]
	void DoSelectTenpo() {
		var meisho = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType=6", "Code", startPos: CurrentEdit.Id_Tenpo);
		CurrentEdit.Id_Tenpo = meisho?.Id ?? 0;
		CurrentEdit.VTenpo = new() { Sid = meisho?.Id ?? 0, Cd = meisho?.Code ?? "", Mei = meisho?.Name ?? "" };
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
