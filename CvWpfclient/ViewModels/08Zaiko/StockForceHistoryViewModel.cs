using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;

namespace CvWpfclient.ViewModels._08Zaiko;

/// <summary>在庫強制調整実績照会の一覧1行（強制調整伝票 Tran61Chosei をラップ）</summary>
public sealed class StockForceHeaderRow(Tran61Chosei chosei, string riyuName) {
	public Tran61Chosei Chosei { get; } = chosei;
	public long Id => Chosei.Id;
	public long Vdu => Chosei.Vdu;
	public string DenDayDisplay => FormatDay(Chosei.DenDay);
	public string SokoDisplay => Chosei.VSoko == null ? string.Empty
		: CodeNameDisplay.Format(Chosei.VSoko.Sid, Chosei.VSoko.Cd, Chosei.VSoko.Mei);
	/// <summary>調整理由名（<see cref="Tran61Chosei.Id_Riyu"/> を MasterMeisho で解決）。</summary>
	public string RiyuDisplay { get; } = riyuName;
	public int SuTotal => Chosei.SuTotal;
	public int MeisaiCount => Chosei.Jmeisai?.Count ?? 0;
	public string ShainDisplay => Chosei.VShain == null ? string.Empty
		: CodeNameDisplay.Format(Chosei.VShain.Sid, Chosei.VShain.Cd, Chosei.VShain.Mei);
	public string Memo => Chosei.Memo;

	static string FormatDay(string s) => s is { Length: 8 } ? $"{s[..4]}/{s.Substring(4, 2)}/{s.Substring(6, 2)}" : s;
}

/// <summary>
/// 在庫強制調整実績照会。在庫強制調整入力で登録した <see cref="Tran61Chosei"/>（区分=強制調整）を照会し、
/// 誤登録を取消（削除）する。<see cref="Tran61Chosei"/> は <see cref="ITranSoko"/> なので、サーバの汎用削除が
/// 在庫を反転して戻す（調整前へ復元）。棚卸確定が作った調整は対象外。
/// <para>仕様は `Doc/spec/2026-08-18_F2fu_強制調整の取消・実績照会_詳細設計.md` を参照する。</para>
/// </summary>
public partial class StockForceHistoryViewModel : BaseQueryViewModel {
	protected override string QueryTitle => "在庫強制調整実績照会";

	[ObservableProperty]
	public partial string SokoCode { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string DenFromText { get; set; } = DateTime.Now.AddMonths(-3).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string DenToText { get; set; } = DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial ObservableCollection<StockForceHeaderRow> Rows { get; set; } = [];

	[ObservableProperty]
	public partial StockForceHeaderRow? SelectedRow { get; set; }

	protected override void Init() {
		Title = QueryTitle;
		Message = "倉庫・調整日の範囲を指定して［検索実行］を押してください。";
	}

	protected override void OnClearConditions() {
		SokoCode = string.Empty;
		DenFromText = DateTime.Now.AddMonths(-3).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
		DenToText = DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
		Rows = [];
		SelectedRow = null;
	}

	[RelayCommand]
	void SelectSoko() { var c = SelectSokoCode(); if (c != null) SokoCode = c; }

	protected override async Task OnSearchAsync(CancellationToken ct) {
		if (!TryParseDate(DenFromText, out var from)) return;
		if (!TryParseDate(DenToText, out var to)) return;
		if (from > to) {
			MessageEx.ShowWarningDialog("調整日の開始日が終了日より後になっています。", owner: ActiveWindow);
			return;
		}
		if (!TryGetMaxCount(out var maxCount)) return;

		List<string> parameters = [ToDenDay(from), ToDenDay(to)];
		// 強制調整のみ。棚卸確定が作った調整(Tanaoroshi)は対象外
		var where = $"h.Kubun = {(int)EnumChosei.Kyosei} AND h.DenDay BETWEEN @0 AND @1";
		where += RangeEq(parameters, "soko.Code", SokoCode);
		var sql = $@"
SELECT h.*
FROM {nameof(Tran61Chosei)} h
LEFT JOIN {nameof(MasterTokui)} soko ON soko.Id = h.Id_Soko
WHERE {where}
ORDER BY h.DenDay DESC, h.Id DESC
LIMIT {maxCount.ToString(CultureInfo.InvariantCulture)}";

		var list = await QuerySqlListAsync<Tran61Chosei>(sql, parameters, ct);
		var reasonMap = await LoadReasonMapAsync(ct);
		Rows = [.. list.Select(x => new StockForceHeaderRow(
			x, x.Id_Riyu > 0 && reasonMap.TryGetValue(x.Id_Riyu, out var n) ? n : string.Empty))];
		SelectedRow = Rows.FirstOrDefault();
		Message = Rows.Count == 0 ? "該当する強制調整がありません。" : $"{Rows.Count:N0} 件を取得しました。";
	}

	/// <summary>調整理由(<c>CHR</c>区分)の Id→名称 辞書を取得する。</summary>
	async Task<Dictionary<long, string>> LoadReasonMapAsync(CancellationToken ct) {
		var sql = $@"
SELECT Id, Vdc, Vdu, Kubun, Code, Name
FROM {nameof(MasterMeisho)}
WHERE Kubun = '{ChoseiRiyu.Kubun}'";
		var list = await QuerySqlListAsync<MasterMeisho>(sql, [], ct);
		return list.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First().Name);
	}

	/// <summary>選択した強制調整伝票を取り消す（削除）。サーバが在庫を反転して調整前へ戻す。</summary>
	[RelayCommand(IncludeCancelCommand = true)]
	async Task CancelChosei(CancellationToken ct) {
		if (IsBusy) return;
		var row = SelectedRow;
		if (row == null) {
			MessageEx.ShowWarningDialog("取消する伝票を選択してください。", owner: ActiveWindow);
			return;
		}
		if (MessageEx.ShowQuestionDialog(
			$"強制調整 伝票No {row.Id} を取り消しますか。\n調整数計 {row.SuTotal:N0} 分、在庫が調整前へ戻ります。",
			owner: ActiveWindow) != MessageBoxResult.Yes) return;

		StartBusy("取消中...");
		try {
			var param = new DeleteByIdParam(typeof(Tran61Chosei), row.Id, row.Vdu);
			var reply = await SendExecuteAsync(param, ct);
			if (reply.Code == CvMsgErrorCode.ConcurrentUpdate) {
				Message = "他端末で更新/削除されたため取り消せませんでした。［検索実行］で再取得してください。";
				MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
				return;
			}
			if (reply.Code < 0) {
				var detail = string.IsNullOrEmpty(reply.Option) ? reply.DataMsg : reply.Option;
				Message = $"取消に失敗しました。{detail}";
				MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
				return;
			}
			await OnSearchAsync(ct);
			Message = $"強制調整 伝票No {row.Id} を取り消しました（在庫を戻しました）。";
			MessageEx.ShowInformationDialog(Message, owner: ActiveWindow);
		}
		catch (OperationCanceledException) { Message = "取消を中断しました"; }
		catch (Exception ex) {
			Message = $"取消に失敗しました。{ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally { FinishBusy(); }
	}

	Task<CvMsg> SendExecuteAsync(object parameter, CancellationToken ct) =>
		CoreServiceClient.SendExecuteAsync(parameter, ct);

	static string RangeEq(List<string> parameters, string column, string? code) {
		var c = (code ?? string.Empty).Trim();
		return string.IsNullOrEmpty(c) ? string.Empty : $" AND {column} = {AddSqlParameter(parameters, c)}";
	}
}
