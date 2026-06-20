using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._01Master;

/// <summary>
/// 得意先マスターメンテ ViewModel
/// </summary>
public partial class MasterTokuiMenteViewModel : Helpers.BaseCodeNameLightMenteViewModel<MasterTokui> {
	[ObservableProperty]
	string title = "得意先マスターメンテ";

	protected override string[] AdditionalLightweightColumns => ["TenType"];

	protected override string? SelectCodeDisplayName => "得意先";
	protected override string? FormFile => "MasterTokuiMente.qfm";
	public IReadOnlyList<EnumShime> ShimeBiItems { get; } = Enum.GetValues<EnumShime>();
	public IReadOnlyList<PayMonthItem> PayMonthItems { get; } = [
		new(0, "当月"),
		new(1, "翌月"),
		new(2, "翌々月"),
		new(3, "3ヶ月後"),
		new(4, "4ヶ月後"),
	];

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
ifnull(json_extract(Jdetail,'$.Bank3'),'') BankAccount3,
case TenType when 0 then '倉庫' when 1 then '卸先' when 3 then '売仕店' when 6 then '直営店' else cast(TenType as text) end TenTypeText,
case IsZaiko when 1 then 'する' else 'しない' end IsZaikoText
from MasterTokui {query.AddWhereOrder()}
";
			return new QueryListSqlParam(typeof(MasterTokui), sql, query.Parameters);
		}
	}

	[RelayCommand]
	async Task Init() => await DoList(CancellationToken.None);

	// ---- 担当者 (MasterShain) 選択 ----
	[RelayCommand]
	void DoSelectShain() {
		var shain = ShowSelectDialog<MasterShain>(typeof(MasterShain), "", "Code", startPos: CurrentEdit.Id_Shain);
		if (shain == null) return;
		CurrentEdit.Id_Shain = shain.Id;
		CurrentEdit.VShain = new() { Sid = shain.Id, Cd = shain.Code ?? "", Mei = shain.Name ?? "" };
	}

	// ---- 支払方法 (MasterMeisho) 選択 ----
	[RelayCommand]
	void DoSelectPayMethod() {
		var meisho = ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), "Kubun='PAY'", "Code", startPos: CurrentEdit.Id_PayMethod);
		if (meisho == null) return;
		CurrentEdit.Id_PayMethod = meisho.Id;
		CurrentEdit.VPayMethod = new() { Sid = meisho.Id, Cd = meisho.Code ?? "", Mei = meisho.Name ?? "" };
	}

	// ---- 請求先 (MasterTokui 自テーブル) 選択 ----
	[RelayCommand]
	void DoSelectPaysaki() {
		var tokui = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "", "Code", startPos: CurrentEdit.Id_Paysaki);
		if (tokui == null) return;
		CurrentEdit.Id_Paysaki = tokui.Id;
		CurrentEdit.VPaysaki = new() { Sid = tokui.Id, Cd = tokui.Code ?? "", Mei = tokui.Name ?? "" };
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
