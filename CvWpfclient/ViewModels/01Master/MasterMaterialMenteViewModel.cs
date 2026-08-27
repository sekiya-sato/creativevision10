using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using System.Collections.ObjectModel;

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

	/// <summary>
	/// 消費税区分(<see cref="MasterMaterial.Id_Tax"/>)の選択肢。<see cref="MasterSysTax"/>の定義から作る。
	/// </summary>
	[ObservableProperty]
	public partial ObservableCollection<TaxKubunOption> TaxKubunList { get; set; } = [];

	/// <summary>消費税区分コンボの表示項目</summary>
	public sealed record TaxKubunOption(long Id, string Name);

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
	async Task Init() {
		await LoadTaxKubunAsync();
		await DoList(CancellationToken.None);
	}

	/// <summary>
	/// 消費税区分の選択肢を<see cref="MasterSysTax"/>から作る。表示税率は今日時点で適用される率。
	/// </summary>
	async Task LoadTaxKubunAsync() {
		if (TaxKubunList.Count > 0) return;
		var today = DateTime.Now.ToString("yyyyMMdd");
		var options = new List<TaxKubunOption> { new(0, "0: 非課税") };
		var sysman = await AppGlobal.LogicGetSysman();
		foreach (var systax in (sysman.Jsub ?? []).OrderBy(x => x.Id)) {
			var rate = await AppGlobal.LogicGetTax((int)systax.Id, today);
			options.Add(new TaxKubunOption(systax.Id, $"{systax.Id}: {rate}%"));
		}
		TaxKubunList = new ObservableCollection<TaxKubunOption>(options);
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
