using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CodeShare;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using System.Collections;
using System.Collections.ObjectModel;
using System.Net.Mail;

namespace CvWpfclient.ViewModels._01Master;

public partial class MasterShainMenteViewModel : Helpers.BaseCodeNameLightMenteViewModel<MasterShain> {
	[ObservableProperty]
	public partial string Title { get; set; } = "社員マスターメンテ";

	[ObservableProperty]
	public partial MasterGeneralMeisho? SelectedJsub { get; set; }

	[ObservableProperty]
	public partial ObservableCollection<MasterGeneralMeisho> EditJsub { get; set; } = [];

	public ObservableCollection<string> KubunOptions { get; } = new([
		"E01", "E02", "E03", "E04", "E05"
	]);
	public List<MasterMeisho> KubunList = [];

	protected override string[] AdditionalLightweightColumns => ["Mail", "ExpireDate", "VTenpo", "VBumon"];

	protected override string? SelectCodeDisplayName => "社員";
	protected override string? FormFile => "MasterShainMente.qfm";
	protected override QueryListSqlParam? PrintBySqlParam {
		get {
			var query = CreateListQueryParam();
			var sql = @$"
select Id, __serverdate__(Vdc) Vdcdate, __serverdate__(Vdu) Vdudate,
Code, Name, Ryaku, Kana, Mail,
'' Spare,
trim(ifnull(json_extract(VTenpo,'$.Cd'),'') || ' ' || ifnull(json_extract(VTenpo,'$.Mei'),'')) Tenpo,
trim(ifnull(json_extract(VBumon,'$.Cd'),'') || ' ' || ifnull(json_extract(VBumon,'$.Mei'),'')) Bumon
from MasterShain {query.AddWhereOrder()}
";
			return new QueryListSqlParam(typeof(MasterShain), sql, query.Parameters);
		}
	}

	[RelayCommand]
	async Task Init() {
		await DoGetKubun(CancellationToken.None);
		await DoList(CancellationToken.None);
	}

	protected override void OnCurrentEditChangedCore(MasterShain? oldValue, MasterShain newValue) {
		var jsubClones = (CurrentEdit.Jsub?.Select(Common.CloneObject) ?? []).ToList();
		foreach (var item in jsubClones) item.SetBaseList(KubunList);
		EditJsub = new ObservableCollection<MasterGeneralMeisho>(jsubClones);
	}

	protected override bool ConfirmAction(string message) {
		if ((message.StartsWith("追加", StringComparison.Ordinal) || message.StartsWith("修正", StringComparison.Ordinal)) && !ClientLib.ValidateMail(CurrentEdit.Mail, ActiveWindow)) {
			return false;
		}

		return base.ConfirmAction(message);
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
			var param = new QueryListParam(typeof(MasterMeisho), "Kubun='IDX' and Code between 'E01' and 'E05'", "Code");
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
			Message = $"Cancelエラー：{cancel.Message}";
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
	void DoSelectTenpo() {
		var meisho = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType=6", "Code", startPos: CurrentEdit.Id_Tenpo);
		CurrentEdit.Id_Tenpo = meisho?.Id ?? 0;
		CurrentEdit.VTenpo = new() { Sid = meisho?.Id ?? 0, Cd = meisho?.Code ?? "", Mei = meisho?.Name ?? "" };
	}

	[RelayCommand]
	void DoSelectBumon() {
		var meisho = ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), "Kubun='BMN'", "Code", startPos: CurrentEdit.Id_Bumon);
		CurrentEdit.Id_Bumon = meisho?.Id ?? 0;
		CurrentEdit.VBumon = new() { Sid = meisho?.Id ?? 0, Cd = meisho?.Code ?? "", Mei = meisho?.Name ?? "" };
	}

	[RelayCommand]
	void CheckMail() => ClientLib.ValidateMail(CurrentEdit.Mail, ActiveWindow,showSuccess: true);


	[RelayCommand]
	void AddJsub() {
		var newItem = new MasterGeneralMeisho(KubunList);
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

}
