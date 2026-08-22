/*
# description
BaseZanCompletionViewModel は残完了設定画面（発注残完了設定 / 受注残完了設定）の共通基底クラスです。

発注残・受注残は「完納・全量出荷した時点で自動的に完了になる」のが基本で、この画面は
その自動判定に対する**手動の上書き**を行います。残っていてもこれ以上入荷・出荷しないと決めた伝票を
まとめて完了にしたり、誤って完了にしたものを解除したりします。
旧CV.netの「発注残完了設定」「受注残完了設定」に相当します。

完了は伝票単位で `Tran13Hachu.EndFlag` / `Tran12Jyuchu.EndFlag` に 1 を立てます。
完了にすると残管理表の「残のみ」出力と配分入力の検索から外れます。SKUに残があっても完了扱いです。
仕様は `Doc/spec/2026-08-17_旧cvnet比較_仕様決定判断材料.md` 4.2 / 4.2.1 / 4.3 を参照してください。

画面は消込画面（BaseMatchingViewModel）と同じ2段構成です。
- `一覧取得`: 日付範囲と取引先で伝票を取得し、伝票単位の残数を付けて完了Flg付きで並べる。
- `完了実行`: 一覧取得時点からCheckBoxが変化した伝票だけを `EndFlag` へ書き戻す。

残数は明細をSKU単位に畳んで「不足しているぶんだけ」を合計します。自動完了の判定
（CompletionDb、明細単位で全SKU充足）と同じ見方にするためで、超過分で不足分が相殺されません。

# example
public partial class HachuZanCompletionSettingViewModel : Helpers.BaseZanCompletionViewModel<Tran13Hachu> {
	protected override string QueryTitle => "発注残完了設定";
	protected override string DenTableName => nameof(Tran13Hachu);
	...
}
 */
using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;

namespace CvWpfclient.Helpers;

/// <summary>残完了設定の一覧1行。完了Flgの入力行。</summary>
public sealed partial class ZanCompletionRow : ObservableObject {
	public long Id { get; set; }

	/// <summary>一覧取得時点の更新日時。完了実行時の楽観排他に使う。</summary>
	public long Vdu { get; set; }

	public string DenDay { get; set; } = string.Empty;

	/// <summary>取引先の「(Id) コード 名称」表示。</summary>
	public string ToriDisplay { get; set; } = string.Empty;

	public int RelateNo1 { get; set; }

	/// <summary>伝票の数量合計（発注数 / 受注数）。</summary>
	public int DenSu { get; set; }

	/// <summary>紐付く実績の数量合計（納品数 / 出荷数）。</summary>
	public int ActualSu { get; set; }

	/// <summary>SKU単位の不足を合計した残数。超過分で相殺しない。</summary>
	public int ZanSu { get; set; }

	public long Amount { get; set; }

	/// <summary>一覧取得時点の `EndFlag`。完了実行時の差分判定に使う。</summary>
	public int OriginalEndFlag { get; set; }

	/// <summary>完了Flg（入力可）。</summary>
	[ObservableProperty]
	public partial bool IsCompleted { get; set; }

	/// <summary>一覧取得時点から完了状態が変化したか。</summary>
	public bool IsChanged => IsCompleted != (OriginalEndFlag == 1);
}

/// <summary>
/// 残完了設定画面の共通基底。<typeparamref name="TDen"/> は <see cref="Tran13Hachu"/> / <see cref="Tran12Jyuchu"/>。
/// </summary>
public abstract partial class BaseZanCompletionViewModel<TDen> : BaseQueryViewModel
	where TDen : TranAllHeader, new() {

	/// <summary>残を管理する伝票テーブル名（発注 / 受注）</summary>
	protected abstract string DenTableName { get; }

	/// <summary>残を消化する実績テーブル名（仕入 / 出荷売上）</summary>
	protected abstract string ActualTableName { get; }

	/// <summary>伝票側の取引先Id列名（`Id_Shiire` / `Id_Tokui`）</summary>
	protected abstract string DenToriIdColumn { get; }

	/// <summary>取引先マスタのテーブル名</summary>
	protected abstract string ToriMasterTableName { get; }

	/// <summary>実績側に足す追加の絞り込み。受注は出荷先の店種区分で絞る</summary>
	protected virtual string ActualExtraJoin => string.Empty;

	/// <summary>画面に出す日付の名称（「発注日」「受注日」）</summary>
	protected abstract string DenDayLabel { get; }

	/// <summary>画面に出す取引先の名称（「仕入先」「得意先」）</summary>
	protected abstract string ToriLabel { get; }

	/// <summary>取引先を選ばせる。キャンセル時は null</summary>
	protected abstract (long Id, string Code, string Name)? PickToriMaster(long startPos);

	[ObservableProperty]
	public partial string DenDayFromText { get; set; } = DateTime.Now.AddMonths(-3).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string DenDayToText { get; set; } = DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial long ToriId { get; set; }

	[ObservableProperty]
	public partial string ToriDisplay { get; set; } = string.Empty;

	/// <summary>表示区分。旧CV.netの残管理表と同じ2択</summary>
	public IReadOnlyList<string> ViewKinds { get; } = ["残のみ", "全て"];

	[ObservableProperty]
	public partial string ViewKind { get; set; } = "残のみ";

	[ObservableProperty]
	public partial ObservableCollection<ZanCompletionRow> DenRows { get; set; } = [];

	[ObservableProperty]
	public partial int TargetCount { get; set; }

	[ObservableProperty]
	public partial int ChangedCount { get; set; }

	protected override void Init() {
		Title = QueryTitle;
		Message = $"{DenDayLabel}の範囲を指定して［一覧取得］を押してください。";
	}

	protected override void OnClearConditions() {
		DenDayFromText = DateTime.Now.AddMonths(-3).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
		DenDayToText = DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
		ToriId = 0;
		ToriDisplay = string.Empty;
		ViewKind = "残のみ";
		DetachRows(DenRows);
		DenRows = [];
		UpdateCounts();
	}

	/// <summary>取引先を選択する。未選択でも一覧取得できる（全取引先が対象）</summary>
	[RelayCommand]
	protected void SelectTori() {
		var picked = PickToriMaster(ToriId);
		if (picked == null) {
			return;
		}
		ToriId = picked.Value.Id;
		ToriDisplay = CodeNameDisplay.Format(picked.Value.Id, picked.Value.Code, picked.Value.Name);
	}

	/// <summary>取引先の絞り込みを解除する</summary>
	[RelayCommand]
	protected void ClearTori() {
		ToriId = 0;
		ToriDisplay = string.Empty;
	}

	protected override async Task OnSearchAsync(CancellationToken ct) {
		if (!TryParseDate(DenDayFromText, out var from)) return;
		if (!TryParseDate(DenDayToText, out var to)) return;
		if (from > to) {
			MessageEx.ShowWarningDialog($"{DenDayLabel}の開始日が終了日より後になっています。", owner: ActiveWindow);
			return;
		}
		if (!TryGetMaxCount(out var maxCount)) return;

		var rows = await LoadRowsAsync(ToDenDay(from), ToDenDay(to), maxCount, ct);
		DetachRows(DenRows);
		DenRows = [.. rows];
		AttachRows(DenRows);
		UpdateCounts();
		Message = DenRows.Count == 0
			? "該当する伝票がありません。"
			: $"{DenRows.Count:N0} 件を取得しました。（{ViewKind}）";
	}

	/// <summary>
	/// 伝票と残数を1回のSQLで取得する。
	/// <para>
	/// 残数は明細をSKU単位に畳み、実績が足りないぶんだけを <c>max(不足, 0)</c> で合計する。
	/// 単純な「伝票数量 − 実績数量」にすると、あるSKUの超過が別のSKUの不足を隠してしまうため。
	/// 自動完了の判定（サーバー側 <c>CompletionDb</c>）と同じ見方になる。
	/// </para>
	/// </summary>
	async Task<List<ZanCompletionRow>> LoadRowsAsync(string dayFrom, string dayTo, int maxCount, CancellationToken ct) {
		// 明細SKU単位の不足合計。実績が無ければ発注(受注)数がそのまま残になる
		var zanExpr = $@"(
  SELECT ifnull(SUM(max(zan.Su - ifnull((
      SELECT SUM({MeisaiNum("Su")} * a.CalcFlag)
      FROM {ActualTableName} a, json_each(a.Jmeisai) m
      {ActualExtraJoin}
      WHERE a.RelateNo1 = h.Id AND json_valid(a.Jmeisai)
        AND {MeisaiNum("Id_Shohin")} = zan.Id_Shohin
        AND {MeisaiNum("Id_Col")}    = zan.Id_Col
        AND {MeisaiNum("Id_Siz")}    = zan.Id_Siz
    ), 0), 0)), 0)
  FROM (
    SELECT {MeisaiNum("Id_Shohin")} AS Id_Shohin,
           {MeisaiNum("Id_Col")}    AS Id_Col,
           {MeisaiNum("Id_Siz")}    AS Id_Siz,
           SUM({MeisaiNum("Su")})   AS Su
    FROM json_each(h.Jmeisai) m
    WHERE json_valid(h.Jmeisai)
    GROUP BY 1, 2, 3
  ) zan
)";
		var actualExpr = $@"ifnull((
  SELECT SUM({MeisaiNum("Su")} * a.CalcFlag)
  FROM {ActualTableName} a, json_each(a.Jmeisai) m
  {ActualExtraJoin}
  WHERE a.RelateNo1 = h.Id AND json_valid(a.Jmeisai)
), 0)";

		List<string> parameters = [dayFrom, dayTo];
		var toriWhere = ToriId > 0 ? $" AND h.{DenToriIdColumn} = {ToriId.ToString(CultureInfo.InvariantCulture)}" : string.Empty;
		// 「残のみ」は残がある伝票だけを出す。完了済みは残の有無にかかわらず出して解除できるようにする
		var zanWhere = ViewKind == "残のみ" ? $" AND ({zanExpr} > 0 OR h.EndFlag = 1)" : string.Empty;
		var sql = $@"
SELECT h.Id, h.Vdu, h.DenDay, h.RelateNo1, h.SuTotal, h.KingakuTotal, h.EndFlag,
       h.{DenToriIdColumn} AS Id_Tori,
       ifnull(t.Code, '') AS ToriCode,
       ifnull(t.Name, '') AS ToriName,
       {actualExpr} AS ActualSu,
       {zanExpr} AS ZanSu
FROM {DenTableName} h
LEFT JOIN {ToriMasterTableName} t ON t.Id = h.{DenToriIdColumn}
WHERE h.DenDay BETWEEN @0 AND @1{toriWhere}{zanWhere}
ORDER BY h.DenDay, h.Id
LIMIT {maxCount.ToString(CultureInfo.InvariantCulture)}
";
		var list = await QuerySqlListAsync<ZanCompletionQueryRow>(sql, parameters, ct);
		return [.. list.Select(x => new ZanCompletionRow {
			Id = x.Id,
			Vdu = x.Vdu,
			DenDay = x.DenDay,
			ToriDisplay = CodeNameDisplay.Format(x.Id_Tori, x.ToriCode, x.ToriName),
			RelateNo1 = x.RelateNo1,
			DenSu = x.SuTotal,
			ActualSu = x.ActualSu,
			ZanSu = x.ZanSu,
			Amount = x.KingakuTotal,
			OriginalEndFlag = x.EndFlag,
			// 完了済みは初期ONで表示する。チェックを外して完了実行すると解除になる
			IsCompleted = x.EndFlag == 1,
		})];
	}

	/// <summary>明細JSONの数値項目。エイリアスは m 固定</summary>
	static string MeisaiNum(string property) =>
		$"cast(ifnull(json_extract(m.value,'$.{property}'),0) as integer)";

	/// <summary>
	/// 一覧取得時点から完了Flgが変化した伝票だけを `EndFlag` へ書き戻す。
	/// <para>
	/// 消込と同じく <see cref="PartialUpdateParam"/> で該当列だけを更新する。
	/// 楽観排他は行単位で、1件でも競合すればサーバーが全件rollbackするため一覧を破棄して再取得を促す。
	/// </para>
	/// </summary>
	[RelayCommand(IncludeCancelCommand = true)]
	protected async Task ExecuteCompletion(CancellationToken ct) {
		if (IsBusy) return;
		var setRows = DenRows.Where(r => r.IsCompleted && r.OriginalEndFlag == 0).ToList();
		var clearRows = DenRows.Where(r => !r.IsCompleted && r.OriginalEndFlag == 1).ToList();
		if (setRows.Count == 0 && clearRows.Count == 0) {
			Message = "完了状態に変更がありません。";
			MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
			return;
		}
		var zanCount = setRows.Count(r => r.ZanSu > 0);
		var confirm = clearRows.Count == 0
			? $"{setRows.Count:N0} 件を完了にしますか。"
			: $"{setRows.Count:N0} 件を完了、{clearRows.Count:N0} 件を解除しますか。";
		if (zanCount > 0) {
			confirm += $"\n完了にする {zanCount:N0} 件は残があります。完了にすると残管理表に出なくなります。";
		}
		if (MessageEx.ShowQuestionDialog(confirm, owner: ActiveWindow) != MessageBoxResult.Yes) return;

		StartBusy("完了設定中...");
		try {
			PartialUpdateRow[] rows = [
				.. setRows.Select(r => new PartialUpdateRow(r.Id, r.Vdu, ["1"])),
				.. clearRows.Select(r => new PartialUpdateRow(r.Id, r.Vdu, ["0"])),
			];
			var param = new PartialUpdateParam(typeof(TDen), [nameof(Tran13Hachu.EndFlag)], rows);
			var reply = await SendExecuteAsync(param, ct);
			if (reply.Code == CvMsgErrorCode.ConcurrentUpdate) {
				DetachRows(DenRows);
				DenRows = [];
				UpdateCounts();
				Message = "他端末で更新されたため設定しませんでした（1件も更新していません）。［一覧取得］で最新の一覧を再取得してください。";
				MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
				return;
			}
			if (reply.Code < 0) {
				var detail = string.IsNullOrEmpty(reply.Option) ? reply.DataMsg : reply.Option;
				Message = $"完了設定に失敗しました。{detail}";
				MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
				return;
			}
			var done = clearRows.Count == 0
				? $"{setRows.Count:N0}件を完了にしました"
				: $"{setRows.Count:N0}件を完了、{clearRows.Count:N0}件を解除しました";
			// Vdu が変わったので一覧を取り直す
			await OnSearchAsync(ct);
			Message = done;
			MessageEx.ShowInformationDialog(Message, owner: ActiveWindow);
		}
		catch (OperationCanceledException) {
			Message = "完了設定を中断しました";
		}
		catch (Exception ex) {
			Message = $"完了設定に失敗しました。{ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			FinishBusy();
		}
	}

	/// <summary>残のある伝票をまとめて完了にする。旧CV.netの一括完了に相当する</summary>
	[RelayCommand]
	protected void CheckAll() => SetAllChecked(true);

	/// <summary>完了のチェックをすべて外す</summary>
	[RelayCommand]
	protected void UncheckAll() => SetAllChecked(false);

	void SetAllChecked(bool value) {
		foreach (var row in DenRows) row.IsCompleted = value;
		UpdateCounts();
	}

	async Task<CvMsg> SendExecuteAsync(object parameter, CancellationToken ct) {
		var msg = new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg201_Op_Execute,
			DataType = parameter.GetType(),
			DataMsg = Common.SerializeObject(parameter),
		};
		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		return await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
	}

	void AttachRows(IEnumerable<ZanCompletionRow> rows) {
		foreach (var row in rows) row.PropertyChanged += OnRowPropertyChanged;
	}

	void DetachRows(IEnumerable<ZanCompletionRow> rows) {
		foreach (var row in rows) row.PropertyChanged -= OnRowPropertyChanged;
	}

	void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
		if (e.PropertyName == nameof(ZanCompletionRow.IsCompleted)) UpdateCounts();
	}

	void UpdateCounts() {
		TargetCount = DenRows.Count(r => r.IsCompleted);
		ChangedCount = DenRows.Count(r => r.IsChanged);
	}

}
