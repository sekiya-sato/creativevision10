/*
# description
BaseMatchingViewModel は消込画面（入金消込 / 支払消込）の共通基底クラスです。
債権(売上)/債務(仕入)の伝票と、入金/支払の伝票を取引先・期間で並べ、
取引先ごとの FIFO で自動充当した結果（未消込残 / 未充当入金）を表示します。

【重要: 保存は行いません】
消込結果を永続化する場所がスキーマに存在しません（詳細は .omo/2026-07-31_kesikomi_design.md）。
`Tran00Uriage.IsPay` は旧システムの「掛計上FLG」の移行値で回収済フラグではなく、
集計テーブル(SummaryUriKake/SummaryUriSei)は取引先×年月/請求日の粒度で伝票単位の消込を表せません。
よってこの画面は**突合（消込シミュレーション）まで**を担当し、保存コマンドは持ちません。
保存方式が決まったら充当結果(MatchingDenRow.Allocated / MatchingKinRow.Allocated)を
そのまま書き出すコマンドを足せるようにしてあります。

# example
public partial class NyukinMatchingViewModel : Helpers.BaseMatchingViewModel<Tran00Uriage, Tran06Nyukin> {
	protected override string QueryTitle => "入金消込";
	protected override string DenTableName => nameof(Tran00Uriage);
	...
}
 */
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace CvWpfclient.Helpers;

/// <summary>債権(売上)/債務(仕入)伝票1件。</summary>
public sealed partial class MatchingDenRow : ObservableObject {
	public long Id { get; set; }
	public string KakeDay { get; set; } = string.Empty;
	public string DenDay { get; set; } = string.Empty;
	public long Id_Tori { get; set; }
	public string ToriCode { get; set; } = string.Empty;
	public string ToriName { get; set; } = string.Empty;
	public string KubunText { get; set; } = string.Empty;
	public string ManualNo { get; set; } = string.Empty;

	/// <summary>債権/債務金額。返品・値引は CalcFlag=-1 によりマイナスになる。</summary>
	public long Amount { get; set; }

	/// <summary>充当済額。</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(Remain))]
	public partial long Allocated { get; set; }

	/// <summary>未消込残。</summary>
	public long Remain => Amount - Allocated;
}

/// <summary>入金/支払伝票1件。</summary>
public sealed partial class MatchingKinRow : ObservableObject {
	public long Id { get; set; }
	public string DenDay { get; set; } = string.Empty;
	public long Id_Tori { get; set; }
	public string ToriCode { get; set; } = string.Empty;
	public string ToriName { get; set; } = string.Empty;
	public string ManualNo { get; set; } = string.Empty;
	public string Memo { get; set; } = string.Empty;

	/// <summary>入金/支払金額。</summary>
	public long Amount { get; set; }

	/// <summary>充当済額。</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(Unapplied))]
	public partial long Allocated { get; set; }

	/// <summary>未充当額（前受金、または債権伝票の入力漏れの疑い）。</summary>
	public long Unapplied => Amount - Allocated;
}

public abstract partial class BaseMatchingViewModel<TDen, TKin> : BaseQueryViewModel
	where TDen : TranAllHeader, new()
	where TKin : TranKinHeader, new() {

	/// <summary>債権/債務側のテーブル名（Tran00Uriage / Tran03Shiire）</summary>
	protected abstract string DenTableName { get; }

	/// <summary>債権/債務側の取引先Id列名（Id_Tokui / Id_Shiire）</summary>
	protected abstract string DenToriIdColumn { get; }

	/// <summary>入金/支払側のテーブル名（Tran06Nyukin / Tran07Shiharai）</summary>
	protected abstract string KinTableName { get; }

	/// <summary>取引先マスタのテーブル名（MasterTokui / MasterShiire）</summary>
	protected abstract string ToriMasterTableName { get; }

	/// <summary>
	/// 取引先マスタの絞り込み条件。副問い合わせでも使うのでテーブル別名を受け取る形にする
	/// （文字列置換で別名を差し込むと条件を書き換える事故になるため）。
	/// 得意先なら "m.TenType = 1"、仕入先なら絞り込み不要。
	/// </summary>
	protected virtual string ToriMasterWhereFor(string alias) => "1 = 1";

	/// <summary>債権/債務側の画面上の呼び名（"売上" / "仕入"）</summary>
	protected abstract string DenLabel { get; }

	/// <summary>入金/支払側の画面上の呼び名（"入金" / "支払"）</summary>
	protected abstract string KinLabel { get; }

	/// <summary>伝票から取引先Idを取り出す（TranAllHeader に無いため派生で橋渡し）</summary>
	protected abstract long GetDenToriId(TDen den);

	/// <summary>伝票の掛計上日を取り出す（TranAllHeader に無いため派生で橋渡し）</summary>
	protected abstract string GetDenKakeDay(TDen den);

	/// <summary>伝票の総合計を取り出す。0 なら KingakuTotal + Tax で代替する</summary>
	protected abstract long GetDenTotal(TDen den);

	/// <summary>伝票の区分表示</summary>
	protected abstract string GetDenKubunText(TDen den);

	/// <summary>伝票の手入力No</summary>
	protected abstract string GetDenManualNo(TDen den);

	/// <summary>債権/債務側で読み込む列（軽量化のため明細JSONは読まない）</summary>
	protected abstract string DenSelectColumns { get; }

	// ---- 検索条件 ----------------------------------------------------------------

	[ObservableProperty]
	public partial string ToriCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ToriCodeTo { get; set; } = string.Empty;

	/// <summary>対象期間 開始 yyyy/MM/dd（債権側は掛計上日、入金側は計上日で切る）</summary>
	[ObservableProperty]
	public partial string DayFromText { get; set; } = DateTime.Now.AddMonths(-3).ToString("yyyy/MM/01", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string DayToText { get; set; } = DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

	/// <summary>突合が取れていない行だけ残す（未消込残≠0 / 未充当≠0）</summary>
	[ObservableProperty]
	public partial bool IsUnmatchedOnly { get; set; }

	// ---- 結果 --------------------------------------------------------------------

	[ObservableProperty]
	public partial ObservableCollection<MatchingDenRow> DenRows { get; set; } = [];

	[ObservableProperty]
	public partial MatchingDenRow? SelectedDenRow { get; set; }

	[ObservableProperty]
	public partial ObservableCollection<MatchingKinRow> KinRows { get; set; } = [];

	[ObservableProperty]
	public partial MatchingKinRow? SelectedKinRow { get; set; }

	[ObservableProperty]
	public partial long DenTotal { get; set; }

	[ObservableProperty]
	public partial long KinTotal { get; set; }

	/// <summary>未消込残の合計</summary>
	[ObservableProperty]
	public partial long RemainTotal { get; set; }

	/// <summary>未充当入金の合計</summary>
	[ObservableProperty]
	public partial long UnappliedTotal { get; set; }

	protected override void OnClearConditions() {
		ToriCodeFrom = string.Empty;
		ToriCodeTo = string.Empty;
		DayFromText = DateTime.Now.AddMonths(-3).ToString("yyyy/MM/01", CultureInfo.InvariantCulture);
		DayToText = DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
		IsUnmatchedOnly = false;
		DenRows = [];
		KinRows = [];
		UpdateTotals();
	}

	protected override async Task OnSearchAsync(CancellationToken ct) {
		if (!TryParseDate(DayFromText, out var dayFrom)) return;
		if (!TryParseDate(DayToText, out var dayTo)) return;
		if (dayFrom > dayTo) {
			MessageEx.ShowWarningDialog("対象期間の開始日が終了日より後になっています。", owner: ActiveWindow);
			return;
		}
		if (!TryGetMaxCount(out var maxCount)) return;

		var toriMap = await LoadToriMapAsync(ct);
		var denList = await LoadDenAsync(ToDenDay(dayFrom), ToDenDay(dayTo), maxCount, ct);
		var kinList = await LoadKinAsync(ToDenDay(dayFrom), ToDenDay(dayTo), maxCount, ct);

		var denRows = denList
			.Select(d => {
				var toriId = GetDenToriId(d);
				toriMap.TryGetValue(toriId, out var tori);
				var total = GetDenTotal(d);
				if (total == 0) total = d.KingakuTotal + GetDenTax(d);
				return new MatchingDenRow {
					Id = d.Id,
					KakeDay = GetDenKakeDay(d),
					DenDay = d.DenDay,
					Id_Tori = toriId,
					ToriCode = tori?.Code ?? string.Empty,
					ToriName = tori?.Name ?? string.Empty,
					KubunText = GetDenKubunText(d),
					ManualNo = GetDenManualNo(d),
					// 返品・値引は CalcFlag=-1。元帳(Phase 3a)と同じ規則で符号を掛ける。
					Amount = total * d.CalcFlag,
				};
			})
			.OrderBy(r => r.ToriCode, StringComparer.Ordinal)
			.ThenBy(r => r.KakeDay, StringComparer.Ordinal)
			.ThenBy(r => r.Id)
			.ToList();

		var kinRows = kinList
			.Select(k => {
				toriMap.TryGetValue(k.Id_Torisaki, out var tori);
				return new MatchingKinRow {
					Id = k.Id,
					DenDay = k.DenDay,
					Id_Tori = k.Id_Torisaki,
					ToriCode = tori?.Code ?? string.Empty,
					ToriName = tori?.Name ?? string.Empty,
					ManualNo = k.ManualNo,
					Memo = k.Memo,
					Amount = k.KingakuTotal,
				};
			})
			.OrderBy(r => r.ToriCode, StringComparer.Ordinal)
			.ThenBy(r => r.DenDay, StringComparer.Ordinal)
			.ThenBy(r => r.Id)
			.ToList();

		DenRows = new ObservableCollection<MatchingDenRow>(denRows);
		KinRows = new ObservableCollection<MatchingKinRow>(kinRows);
		ApplyFifoAllocation();
		if (IsUnmatchedOnly) {
			DenRows = new ObservableCollection<MatchingDenRow>(DenRows.Where(r => r.Remain != 0));
			KinRows = new ObservableCollection<MatchingKinRow>(KinRows.Where(r => r.Unapplied != 0));
		}
		UpdateTotals();
		Message = $"{DenLabel} {DenRows.Count} 件 / {KinLabel} {KinRows.Count} 件を突合しました（保存はしません）";
	}

	/// <summary>伝票の消費税。Total が 0 の伝票の代替計算に使う。</summary>
	protected abstract long GetDenTax(TDen den);

	/// <summary>取引先ごとの FIFO で入金を債権へ充当する。</summary>
	[RelayCommand]
	protected void AutoMatch() {
		ApplyFifoAllocation();
		UpdateTotals();
		Message = $"取引先ごとに古い{KinLabel}から順に充当しました（保存はしません）";
	}

	/// <summary>充当をすべて取り消す。</summary>
	[RelayCommand]
	protected void ClearMatch() {
		foreach (var r in DenRows) r.Allocated = 0;
		foreach (var r in KinRows) r.Allocated = 0;
		UpdateTotals();
		Message = "充当をクリアしました";
	}

	void ApplyFifoAllocation() {
		foreach (var r in DenRows) r.Allocated = 0;
		foreach (var r in KinRows) r.Allocated = 0;

		var denByTori = DenRows.GroupBy(r => r.Id_Tori).ToDictionary(g => g.Key, g => g.ToList());
		foreach (var kinGroup in KinRows.GroupBy(r => r.Id_Tori)) {
			if (!denByTori.TryGetValue(kinGroup.Key, out var dens)) continue;
			// 債権は古い順。返品(マイナス)は充当対象にしないので残高が正の行だけを見る。
			var queue = dens.Where(d => d.Amount > 0).ToList();
			var idx = 0;
			foreach (var kin in kinGroup) {
				var rest = kin.Amount;
				while (rest > 0 && idx < queue.Count) {
					var den = queue[idx];
					var can = den.Remain;
					if (can <= 0) { idx++; continue; }
					var apply = Math.Min(can, rest);
					den.Allocated += apply;
					kin.Allocated += apply;
					rest -= apply;
					if (den.Remain <= 0) idx++;
				}
			}
		}
	}

	void UpdateTotals() {
		DenTotal = DenRows.Sum(r => r.Amount);
		KinTotal = KinRows.Sum(r => r.Amount);
		RemainTotal = DenRows.Sum(r => r.Remain);
		UnappliedTotal = KinRows.Sum(r => r.Unapplied);
	}

	// ---- データ取得 ---------------------------------------------------------------

	async Task<Dictionary<long, MasterTokui>> LoadToriMapAsync(CancellationToken ct) {
		// 得意先/仕入先どちらも Code/Name を持つので MasterTokui 型で受ける（列構成が同じ）。
		List<string> parameters = [];
		var where = BuildCodeRangeWhere(parameters, "m.Code", ToriCodeFrom, ToriCodeTo);
		var sql = $@"
SELECT m.Id, m.Vdc, m.Vdu, m.Code, m.Name, m.Ryaku, m.Kana
FROM {ToriMasterTableName} m
WHERE {ToriMasterWhereFor("m")}{where}";
		var list = await QuerySqlListAsync<MasterTokui>(sql, parameters, ct);
		return list.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
	}

	async Task<List<TDen>> LoadDenAsync(string dayFrom, string dayTo, int maxCount, CancellationToken ct) {
		List<string> parameters = [];
		var toriWhere = BuildToriIdWhere(parameters, $"h.{DenToriIdColumn}");
		var sql = $@"
SELECT {DenSelectColumns}
FROM {DenTableName} h
WHERE h.KakeDay >= {AddSqlParameter(parameters, dayFrom)}
  AND h.KakeDay <= {AddSqlParameter(parameters, dayTo)}{toriWhere}
ORDER BY h.KakeDay, h.Id
LIMIT {maxCount}";
		return await QuerySqlListAsync<TDen>(sql, parameters, ct);
	}

	async Task<List<TKin>> LoadKinAsync(string dayFrom, string dayTo, int maxCount, CancellationToken ct) {
		List<string> parameters = [];
		var toriWhere = BuildToriIdWhere(parameters, "h.Id_Torisaki");
		var sql = $@"
SELECT h.Id, h.Vdc, h.Vdu, h.DenDay, h.Id_Shain, h.VShain, h.Id_Torisaki, h.VTori,
       h.KingakuTotal, h.ManualNo, h.Memo
FROM {KinTableName} h
WHERE h.DenDay >= {AddSqlParameter(parameters, dayFrom)}
  AND h.DenDay <= {AddSqlParameter(parameters, dayTo)}{toriWhere}
ORDER BY h.DenDay, h.Id
LIMIT {maxCount}";
		return await QuerySqlListAsync<TKin>(sql, parameters, ct);
	}

	/// <summary>取引先コード範囲を取引先Idの副問い合わせへ変換する。</summary>
	string BuildToriIdWhere(List<string> parameters, string column) {
		if (string.IsNullOrWhiteSpace(ToriCodeFrom) && string.IsNullOrWhiteSpace(ToriCodeTo)) return string.Empty;
		var codeWhere = BuildCodeRangeWhere(parameters, "m.Code", ToriCodeFrom, ToriCodeTo);
		return $@"
  AND {column} IN (SELECT m.Id FROM {ToriMasterTableName} m WHERE {ToriMasterWhereFor("m")}{codeWhere})";
	}

	// ---- 選択ダイアログ ----------------------------------------------------------

	[RelayCommand]
	void SelectToriFrom() => ToriCodeFrom = PickToriCode() ?? ToriCodeFrom;

	[RelayCommand]
	void SelectToriTo() => ToriCodeTo = PickToriCode() ?? ToriCodeTo;

	/// <summary>取引先コードを選ばせる。得意先/仕入先でマスタが違うので派生で実装する。</summary>
	protected abstract string? PickToriCode();
}
