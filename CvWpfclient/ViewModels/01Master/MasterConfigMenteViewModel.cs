using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._01Master;

public partial class MasterConfigMenteViewModel : Helpers.BaseMenteViewModel<MasterConfig> {
	[ObservableProperty]
	public partial string Title { get; set; } = "設定フラグマスタメンテ";

	protected override string? ListOrder => "Category,Name";

	protected override string? FormFile => "MasterConfigMente.qfm";

	protected override QueryListSqlParam? PrintBySqlParam {
		get {
			var query = CreateListQueryParam();
			var sql = @$"
select Id, __serverdate__(Vdc) Vdcdate, __serverdate__(Vdu) Vdudate,
Category, Name, Val, Example, Memo
from MasterConfig {query.AddWhereOrder()}
";
			return new QueryListSqlParam(typeof(MasterConfig), sql, query.Parameters);
		}
	}

	[RelayCommand]
	Task Init() => DoList(CancellationToken.None);

	protected override bool ConfirmAction(string message) {
		if ((message.StartsWith("追加", StringComparison.Ordinal) || message.StartsWith("修正", StringComparison.Ordinal)) && !ValidateCurrentEdit()) {
			return false;
		}

		return base.ConfirmAction(message);
	}

	protected override object CreateInsertParam() {
		NormalizeCurrentEdit();
		return base.CreateInsertParam();
	}

	protected override object CreateUpdateParam() {
		NormalizeCurrentEdit();
		return base.CreateUpdateParam();
	}

	protected override string GetInsertConfirmMessage() => $"追加しますか？ (Name={CurrentEdit.Name})";

	protected override string GetUpdateConfirmMessage() => $"修正しますか？ (Name={CurrentEdit.Name}, Id={CurrentEdit.Id})";

	protected override string GetDeleteConfirmMessage() => $"削除しますか？ (Name={CurrentEdit.Name}, Id={CurrentEdit.Id})";

	bool ValidateCurrentEdit() {
		NormalizeCurrentEdit();
		if (string.IsNullOrWhiteSpace(CurrentEdit.Category)) {
			Message = "カテゴリを入力してください";
			MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
			return false;
		}
		if (string.IsNullOrWhiteSpace(CurrentEdit.Name)) {
			Message = "フラグ名を入力してください";
			MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
			return false;
		}

		return true;
	}

	void NormalizeCurrentEdit() {
		CurrentEdit.Category = CurrentEdit.Category.Trim();
		CurrentEdit.Name = CurrentEdit.Name.Trim();
	}
}
