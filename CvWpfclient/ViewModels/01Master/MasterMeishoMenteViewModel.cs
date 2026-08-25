using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels.Sub;
using System.Collections;

namespace CvWpfclient.ViewModels._01Master;

public partial class MasterMeishoMenteViewModel : Helpers.BaseCodeNameLightMenteViewModel<MasterMeisho> {
	[ObservableProperty]
	public partial string Title { get; set; } = "名称マスターメンテ";

	protected override string[] AdditionalLightweightColumns => ["Kubun", "Odr", "KubunName"];

	List<MasterMeisho>? kubunListCache;
	CategoryRangeParameter? listCondition;

	protected override string? ListOrder => "Kubun,Code";
	protected override string? FormFile => "MasterMeishoMente.qfm";
	protected override QueryListSqlParam? PrintBySqlParam {
		get {
			var kubunCode = ExtractKubunCode(listCondition?.SelectedCategory);
			if (string.IsNullOrWhiteSpace(kubunCode)) {
				return null;
			}
			var sql = @"
select Id, __serverdate__(Vdc) Vdcdate, __serverdate__(Vdu) Vdudate,
Kubun||' '||KubunName kubunstr,
Code,Name,Ryaku,'' rank,Odr,Kana
from MasterMeisho where Kubun=@0
";
			return new QueryListSqlParam(typeof(MasterMeisho), sql, [kubunCode]);
		}
	}

	protected override string? ListWhere => BuildListConditionWhere(listCondition);

	protected override int? ListMaxCount => listCondition == null ? AppGlobal.Limit : listCondition.MaxCount;

	string? BuildListConditionWhere(CategoryRangeParameter? condition) {
		if (condition == null) {
			return null;
		}

		List<string> clauses = [];
		List<string> parameters = [];
		var kubunCode = ExtractKubunCode(condition.SelectedCategory);
		if (!string.IsNullOrWhiteSpace(kubunCode)) {
			clauses.Add($"Kubun = {AddSqlParameter(parameters, kubunCode)}");
		}
		if (condition.FromId.HasValue) {
			clauses.Add($"Id >= {condition.FromId.Value}");
		}
		if (condition.ToId.HasValue) {
			clauses.Add($"Id <= {condition.ToId.Value}");
		}

		SelectCodeWhereParameters = [.. parameters];
		return clauses.Count == 0 ? null : string.Join(" AND ", clauses);
	}

	static string FormatKubun(MasterMeisho kubun) => $"{kubun.Code} {kubun.Name}";

	static string? ExtractKubunCode(string? formattedKubun) =>
		string.IsNullOrWhiteSpace(formattedKubun) ? null : formattedKubun.Split(' ', 2)[0];

	[RelayCommand]
	async Task Init(CancellationToken ct) {
		kubunListCache ??= await LoadKubunListAsync(ct);
		if (kubunListCache.Count == 0) {
			ListData.Clear();
			Count = 0;
			return;
		}

		var defaultKubun = kubunListCache.FirstOrDefault(c => c.Code == "BRD") ?? kubunListCache.FirstOrDefault();
		listCondition = new CategoryRangeParameter {
			DisplayName = "区分",
			SelectedCategory = defaultKubun == null ? null : FormatKubun(defaultKubun),
			MaxCount = AppGlobal.Limit
		};
		await DoList(ct);
	}

	[RelayCommand]
	async Task SelectCondition(CancellationToken ct) {
		kubunListCache ??= await LoadKubunListAsync(ct);
		if (kubunListCache.Count == 0) {
			return;
		}

		var dlg = new Views.Sub.CategoryRangeView();
		if (dlg.DataContext is not CategoryRangeViewModel vm) {
			return;
		}

		vm.Initialize(listCondition ?? new CategoryRangeParameter { DisplayName = "区分", MaxCount = AppGlobal.Limit });
		vm.Parameter = vm.Parameter with {
			DisplayName = "区分",
			CategoryList = [.. kubunListCache.Select(FormatKubun)]
		};

		if (ClientLib.ShowDialogView(dlg, this, true) != true) {
			return;
		}

		listCondition = vm.Parameter;
		await DoList(ct);
	}

	async Task<List<MasterMeisho>> LoadKubunListAsync(CancellationToken ct) {
		try {
			ClientLib.Cursor2Wait();
			var msg = new CodeShare.CvMsg {
				Code = 0,
				Flag = CodeShare.CvFlag.Msg101_Op_Query,
				DataType = typeof(QueryListParam),
				DataMsg = Common.SerializeObject(new QueryListParam(
					itemType: typeof(MasterMeisho),
					where: "Kubun='IDX'",
					order: "Code"
				))
			};

			var reply = await SendMessageAsync(msg, ct);
			if (Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is IList list) {
				return list.Cast<MasterMeisho>().ToList();
			}
			return [];
		}
		catch (OperationCanceledException) {
			Message = "区分一覧取得がキャンセルされました";
			return [];
		}
		catch (Exception ex) {
			Message = $"区分一覧取得失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ClientLib.GetActiveView(this));
			return [];
		}
		finally {
			ClientLib.Cursor2Normal();
		}
	}

	[RelayCommand]
	void DoSelectKubun() {
		var selWin = new Views.Sub.SelectKubunView();
		var vm = selWin.DataContext as Sub.SelectKubunViewModel;
		if (vm == null) return;
		vm.SetParam("Kubun='IDX'", CurrentEdit.Kubun);
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		var meisho = vm.Current as MasterMeisho;
		CurrentEdit.Kubun = meisho?.Code ?? CurrentEdit.Kubun;
		CurrentEdit.KubunName = meisho?.Name ?? CurrentEdit.KubunName;
	}
}
