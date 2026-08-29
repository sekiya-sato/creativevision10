using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;
using System.Collections.ObjectModel;
using System.Globalization;

namespace CvWpfclient.ViewModels._03Hatchu;

/// <summary>
/// 納品予定照会（発注側）。発注(<see cref="Tran13Hachu"/>)を納品予定日(<see cref="Tran13Hachu.NouhinDay"/>)で並べ、
/// 入荷予定と<b>納期遅れ</b>を画面で確認する。納品予定日は 2026-08-18 に伝票ヘッダへ追加した（決定 6.2 / H1）。
/// <para>
/// 納期遅れ = 納品予定日を過ぎても未完了(<c>EndFlag=0</c>)。判定は納品日と完了フラグで行う（リードタイム自動計算は 2.0 以降）。
/// 読み取りは既存の照会パターン（`QuerySqlListAsync&lt;Tran13Hachu&gt;`）。仕様は
/// `Doc/spec/2026-08-18_H1-H4_納品予定日_詳細設計.md` を参照する。
/// </para>
/// </summary>
public partial class DeliveryScheduleInquiryViewModel : BaseQueryViewModel {
	protected override string QueryTitle => "納品予定照会";

	[ObservableProperty]
	public partial string NouhinFromText { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string NouhinToText { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShiireCode { get; set; } = string.Empty;

	/// <summary>納期遅れ（予定日超過かつ未完了）だけに絞る</summary>
	[ObservableProperty]
	public partial bool OverdueOnly { get; set; }

	/// <summary>未完了だけに絞る</summary>
	[ObservableProperty]
	public partial bool IncompleteOnly { get; set; } = true;

	[ObservableProperty]
	public partial ObservableCollection<DeliveryScheduleRow> Rows { get; set; } = [];

	[ObservableProperty]
	public partial int OverdueCount { get; set; }

	protected override void Init() {
		Title = QueryTitle;
		Message = "納品予定日の範囲などを指定して［検索実行］を押してください。";
	}

	protected override void OnClearConditions() {
		NouhinFromText = string.Empty;
		NouhinToText = string.Empty;
		ShiireCode = string.Empty;
		OverdueOnly = false;
		IncompleteOnly = true;
		Rows = [];
		OverdueCount = 0;
	}

	[RelayCommand]
	void SelectShiire() { var c = SelectCode<MasterShiire>(""); if (c != null) ShiireCode = c; }

	protected override async Task OnSearchAsync(CancellationToken ct) {
		if (!TryGetMaxCount(out var maxCount)) return;
		DateTime? from = null, to = null;
		if (!string.IsNullOrWhiteSpace(NouhinFromText)) {
			if (!TryParseDate(NouhinFromText, out var f)) return;
			from = f;
		}
		if (!string.IsNullOrWhiteSpace(NouhinToText)) {
			if (!TryParseDate(NouhinToText, out var t)) return;
			to = t;
		}
		if (from.HasValue && to.HasValue && from > to) {
			MessageEx.ShowWarningDialog("納品予定日の開始日が終了日より後になっています。", owner: ActiveWindow);
			return;
		}

		var todayYmd = DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		List<string> parameters = [];
		// 納品予定日が入っている発注だけを対象にする
		var clauses = new List<string> { "ifnull(h.NouhinDay,'') <> ''" };
		if (from.HasValue) clauses.Add($"h.NouhinDay >= {AddSqlParameter(parameters, ToDenDay(from.Value))}");
		if (to.HasValue) clauses.Add($"h.NouhinDay <= {AddSqlParameter(parameters, ToDenDay(to.Value))}");
		if (!string.IsNullOrWhiteSpace(ShiireCode)) {
			clauses.Add($"sh.Code = {AddSqlParameter(parameters, ShiireCode.Trim())}");
		}
		if (IncompleteOnly || OverdueOnly) clauses.Add("h.EndFlag = 0");
		if (OverdueOnly) clauses.Add($"h.NouhinDay < {AddSqlParameter(parameters, todayYmd)}");

		var sql = $@"
SELECT h.*
FROM {nameof(Tran13Hachu)} h
LEFT JOIN {nameof(MasterShiire)} sh ON sh.Id = h.Id_Shiire
WHERE {string.Join(" AND ", clauses)}
ORDER BY h.NouhinDay, h.Id
LIMIT {maxCount.ToString(CultureInfo.InvariantCulture)}";

		var list = await QuerySqlListAsync<Tran13Hachu>(sql, parameters, ct);
		Rows = [.. list.Select(h => new DeliveryScheduleRow(h))];
		OverdueCount = Rows.Count(r => r.OverdueDays > 0);
		Message = Rows.Count == 0
			? "該当する発注がありません。"
			: $"{Rows.Count:N0} 件を取得しました（納期遅れ {OverdueCount:N0} 件）。";
	}
}

/// <summary>納品予定照会の一覧1行。発注(Tran13Hachu)をラップして表示と納期遅れを出す。</summary>
public sealed class DeliveryScheduleRow(Tran13Hachu hachu) {
	public long Id => hachu.Id;
	public string DenDayDisplay => FormatDay(hachu.DenDay);
	public string NouhinDayDisplay => FormatDay(hachu.NouhinDay);
	public string ShiireDisplay => hachu.VShiire == null ? string.Empty
		: CodeNameDisplay.Format(hachu.VShiire.Sid, hachu.VShiire.Cd, hachu.VShiire.Mei);
	public int SuTotal => hachu.SuTotal;
	public long KingakuTotal => hachu.KingakuTotal;
	public string StatusDisplay => hachu.EndFlag == 1 ? "完了" : "未完了";

	/// <summary>納期遅れ日数（今日 − 納品予定日）。未完了かつ予定日超過のときだけ正。それ以外は0。</summary>
	public int OverdueDays {
		get {
			if (hachu.EndFlag != 0) return 0;
			if (!DateTime.TryParseExact(hachu.NouhinDay, "yyyyMMdd", CultureInfo.InvariantCulture,
				DateTimeStyles.None, out var d)) return 0;
			var days = (DateTime.Today - d.Date).Days;
			return days > 0 ? days : 0;
		}
	}

	/// <summary>納期遅れ表示（遅延日数 or 空）。DataGridの色分けと表示に使う。</summary>
	public string OverdueDisplay => OverdueDays > 0 ? $"{OverdueDays} 日超過" : string.Empty;

	static string FormatDay(string yyyymmdd) =>
		yyyymmdd is { Length: 8 } ? $"{yyyymmdd[..4]}/{yyyymmdd.Substring(4, 2)}/{yyyymmdd.Substring(6, 2)}" : yyyymmdd;
}
