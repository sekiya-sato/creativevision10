using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels.Sub;
using System.Globalization;

namespace CvWpfclient.ViewModels._07Haibun;

/// <summary>
/// 配分データメンテ（管理者用）。<see cref="TranHaibun"/> の確定日・実数量・欠品数・完了FLG などを直接修正・削除する。
/// <para>
/// 新規追加は各配分入力画面で行うため、この画面では扱わない。
/// 配分のキー（日付・倉庫・店舗・区分・SKU・数量）は読取専用とし、引当数の再計算はサーバの汎用更新・削除が行う。
/// </para>
/// </summary>
public partial class HaibunDataMenteViewModel : Helpers.BaseMenteViewModel<TranHaibun> {
	[ObservableProperty]
	public partial string Title { get; set; } = "配分データメンテ";

	SelectInputParameter? selectParam;

	public IReadOnlyList<KeyValuePair<int, string>> EndFlagOptions { get; } = [
		new(0, "0:未完了"),
		new(1, "1:完了"),
	];

	public IReadOnlyList<KeyValuePair<int, string>> SendFlgOptions { get; } = [
		new(0, "0:未送信"),
		new(1, "1:送信中"),
		new(2, "2:送信済み"),
	];

	public IReadOnlyList<KeyValuePair<int, string>> EndReasonOptions { get; } = [
		new((int)EnumHaibunEndReason.Normal, "0:通常"),
		new((int)EnumHaibunEndReason.Converted, "1:売上変換"),
		new((int)EnumHaibunEndReason.Cancelled, "2:取消"),
		new((int)EnumHaibunEndReason.Expired, "3:期限切れ"),
	];

	protected override string? ListOrder => "h.DenDay DESC, h.Id_Soko, h.Id_Tenpo, h.Id_Shohin, h.Id_Col, h.Id_Siz, h.Id";
	protected override int? ListMaxCount => selectParam?.MaxCount;

	protected override CvMsg CreateListMessage() {
		var query = CreateListQueryParam();
		var sql = $"""
			select
				h.*,
				ifnull(Soko.Name, '') SokoName,
				ifnull(Tenpo.Name, '') TenpoName,
				ifnull(S.Code, '') ShohinCode,
				ifnull(S.Name, '') ShohinName,
				ifnull(D.Mei_Col, '') ColName,
				ifnull(D.Mei_Siz, '') SizName
			from TranHaibun h
				left join MasterTokui Soko on Soko.Id = h.Id_Soko
				left join MasterTokui Tenpo on Tenpo.Id = h.Id_Tenpo
				left join MasterShohin S on S.Id = h.Id_Shohin
				left join DerivedShohinColSiz D
					on D.Id_Shohin = h.Id_Shohin
					and D.Id_Col = h.Id_Col
					and D.Id_Siz = h.Id_Siz
			{query.AddWhereOrder()}
			""";
		return new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg101_Op_Query,
			DataType = typeof(QueryListSqlParam),
			DataMsg = Common.SerializeObject(new QueryListSqlParam(typeof(TranHaibun), sql, query.Parameters))
		};
	}

	protected override ValueTask<bool> BeforeListAsync(CancellationToken ct) {
		ct.ThrowIfCancellationRequested();
		var win = new Views.Sub.RangeInputParamView();
		if (win.DataContext is not RangeInputParamViewModel vm) return new ValueTask<bool>(false);
		selectParam ??= new SelectInputParameter {
			DisplayName = "配分データ",
			ToriLabel = "店舗Id",
			IsToriVisible = true,
			RequireDirectConditionForShohin = false,
			MaxCount = AppGlobal.Limit,
		};
		vm.Initialize(selectParam);
		if (ClientLib.ShowDialogView(win, this, true) != true) return new ValueTask<bool>(false);
		selectParam = vm.Parameter;
		return new ValueTask<bool>(true);
	}

	protected override string? ListWhere {
		get {
			if (selectParam == null) return null;
			List<string> clauses = [];
			List<string> parameters = [];
			if (selectParam.FromId.HasValue) clauses.Add($"h.Id >= {selectParam.FromId.Value}");
			if (selectParam.ToId.HasValue) clauses.Add($"h.Id <= {selectParam.ToId.Value}");
			if (!string.IsNullOrWhiteSpace(selectParam.FromDate)) clauses.Add($"h.DenDay >= {AddSqlParameter(parameters, selectParam.FromDate.Trim())}");
			if (!string.IsNullOrWhiteSpace(selectParam.ToDate)) clauses.Add($"h.DenDay <= {AddSqlParameter(parameters, selectParam.ToDate.Trim())}");
			AddSelectedIdInClause(clauses, "h.Id_Tenpo", selectParam.ToriIds);
			AddSelectedIdInClause(clauses, "h.Id_Soko", selectParam.SokoIds);
			AddSelectedIdInClause(clauses, "h.Id_Shohin", selectParam.ShohinIds);
			if (!string.IsNullOrWhiteSpace(selectParam.InputBarcode)) {
				clauses.Add($"h.JanCode = {AddSqlParameter(parameters, selectParam.InputBarcode.Trim())}");
			}
			if (!string.IsNullOrWhiteSpace(selectParam.ShohinNameLike)) {
				clauses.Add($"S.Name LIKE {AddSqlParameter(parameters, $"%{EscapeSqlLikePattern(selectParam.ShohinNameLike)}%")} ESCAPE '\\'");
			}
			SelectCodeWhereParameters = [.. parameters];
			return clauses.Count == 0 ? null : string.Join(" AND ", clauses);
		}
	}

	protected override bool CanUpdate() {
		if (CurrentEdit.Id <= 0) {
			MessageEx.ShowWarningDialog("修正対象を選択してください", owner: ActiveWindow);
			return false;
		}
		return ValidateCurrentEdit();
	}

	protected override string GetUpdateConfirmMessage() => $"修正しますか？ (Id={CurrentEdit.Id}, 日付={CurrentEdit.DenDay}, 商品={CurrentEdit.ShohinCode})";

	protected override string GetDeleteConfirmMessage() =>
		$"削除しますか？ (Id={CurrentEdit.Id}, 日付={CurrentEdit.DenDay}, 商品={CurrentEdit.ShohinCode})\n削除すると引当数を引き直します。元に戻せません。";

	protected override void AfterUpdate(TranHaibun item) => Message = $"修正しました (Id={item.Id})";
	protected override void AfterDelete(TranHaibun removedItem) => Message = $"削除しました (Id={removedItem.Id})";

	bool ValidateCurrentEdit() {
		CurrentEdit.NouhinDay = (CurrentEdit.NouhinDay ?? string.Empty).Trim();
		CurrentEdit.KakuteiDay = (CurrentEdit.KakuteiDay ?? string.Empty).Trim();
		CurrentEdit.Memo = (CurrentEdit.Memo ?? string.Empty).Trim();

		string? error = null;
		if (!IsEmptyOrYmd(CurrentEdit.NouhinDay)) error = "納品日は空、または yyyyMMdd の8桁で入力してください";
		else if (!IsEmptyOrYmd(CurrentEdit.KakuteiDay)) error = "確定日は空、または yyyyMMdd の8桁で入力してください";
		else if (CurrentEdit.JitsuSu < 0 || CurrentEdit.ShortSu < 0) error = "実数量・欠品数は0以上で入力してください";
		else if (CurrentEdit.RelateNo2 < 0) error = "関連No2は0以上で入力してください";
		else if (CurrentEdit.EndFlag is not (0 or 1)) error = "完了FLGを選択してください";
		else if (CurrentEdit.SendFlg is < 0 or > 2) error = "送信FLGを選択してください";
		else if (!Enum.IsDefined(typeof(EnumHaibunEndReason), CurrentEdit.EndReason)) error = "完了理由を選択してください";
		if (error != null) {
			Message = error;
			MessageEx.ShowWarningDialog(error, owner: ActiveWindow);
			return false;
		}

		// 配分確定で完了した行は Su = JitsuSu + ShortSu が成り立つ。崩れる修正は確認してから保存する
		if (CurrentEdit.EndFlag == 1
			&& CurrentEdit.EndReason == (int)EnumHaibunEndReason.Normal
			&& CurrentEdit.Su != CurrentEdit.JitsuSu + CurrentEdit.ShortSu) {
			return MessageEx.ShowQuestionDialog(
				$"完了なのに 数量({CurrentEdit.Su}) ≠ 実数量({CurrentEdit.JitsuSu}) + 欠品数({CurrentEdit.ShortSu}) です。\nこのまま保存しますか？",
				owner: ActiveWindow) == System.Windows.MessageBoxResult.Yes;
		}
		return true;
	}

	static bool IsEmptyOrYmd(string value) =>
		value.Length == 0
		|| DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
