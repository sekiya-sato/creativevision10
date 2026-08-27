using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;

namespace CvWpfclient.ViewModels._01Master;

/// <summary>
/// 生地・付属マスターメンテ ViewModel
/// </summary>
public partial class MasterMaterialMenteViewModel : Helpers.BaseCodeNameLightMenteViewModel<MasterMaterial> {
	[ObservableProperty]
	public partial string Title { get; set; } = "生地・付属マスターメンテ";

	protected override string[] AdditionalLightweightColumns => ["VKubun", "VShiire"];
	protected override string? SelectCodeDisplayName => "生地・付属";
	protected override string? FormFile => "MasterMaterialMente.qfm";

	protected override QueryListSqlParam? PrintBySqlParam {
		get {
			var query = CreateListQueryParam();
			var sql = @$"
select Id, __serverdate__(Vdc) Vdcdate, __serverdate__(Vdu) Vdudate,
Code, Name, Ryaku, Kana,
trim(ifnull(json_extract(VKubun,'$.Cd'),'') || ' ' || ifnull(json_extract(VKubun,'$.Mei'),'')) Kubun,
trim(ifnull(json_extract(VShiire,'$.Cd'),'') || ' ' || ifnull(json_extract(VShiire,'$.Mei'),'')) Shiire,
CodeShiire, TankaShiire, Memo
from MasterMaterial {query.AddWhereOrder()}
";
			return new QueryListSqlParam(typeof(MasterMaterial), sql, query.Parameters);
		}
	}

	[RelayCommand]
	void DoSelectKubun() {
		var meisho = ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), "Kubun='KIJ'", "Code", startPos: CurrentEdit.Id_Kubun);
		if (meisho == null) return;
		CurrentEdit.Id_Kubun = meisho.Id;
		CurrentEdit.VKubun = new() { Sid = meisho.Id, Cd = meisho.Code ?? "", Mei = meisho.Name ?? "" };
	}

	[RelayCommand]
	void DoSelectShiire() {
		var shiire = ShowSelectDialog<MasterShiire>(typeof(MasterShiire), "", "Code", startPos: CurrentEdit.Id_Shiire);
		if (shiire == null) return;
		CurrentEdit.Id_Shiire = shiire.Id;
		CurrentEdit.VShiire = new() { Sid = shiire.Id, Cd = shiire.Code ?? "", Mei = shiire.Name ?? "" };
	}
}
