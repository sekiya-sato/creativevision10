/*
# description
納入一覧表（旧CVnet「納入一覧表」相当、新規実装）。
品番×出庫倉庫×表示基準(色 or サイズ)ごとに改ページし、縦軸=得意先／横軸=表示基準の逆軸(サイズ or 色)の
マトリクスで配分数(TranHaibun.Su または JitsuSu)を印刷する。ピッキング用の種まき一覧。

qfmに動的列生成の仕組みが無いため、横軸は固定10列とし、対象値が10を超える場合は
続きの表(同じ品番×倉庫×基準値の次ページ)へ折り返す。ピボット集計はSQLではなくここ(C#)で行い、
結果を PrintByCsvParam(CSV文字列)としてサーバへ渡す。CSVは printform/ShippingDeliveryList.qfm の
item1〜item28 と厳密に列順を一致させること。

完了FLG(EndFlag)・確定FLG(KakuteiDay)は絞り込みに使わない(旧仕様を踏襲、全件対象)。
 */
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;
using System.Globalization;
using System.Text;

namespace CvWpfclient.ViewModels._07Haibun;

public partial class ShippingListReportViewModel : Helpers.BaseQueryViewModel {
	protected override string QueryTitle => "納入一覧表";

	/// <summary>印刷フォーム(qfm)ファイル名</summary>
	const string FormFile = "ShippingDeliveryList.qfm";

	/// <summary>横軸の最大列数（qfmが固定10列のため）</summary>
	const int MaxColumns = 10;

	static readonly string[] DisplayBasisKinds = ["色基準", "サイズ基準"];
	static readonly string[] QuantityModeKinds = ["予定数量", "実数量"];

	public IReadOnlyList<string> DisplayBasisKindList { get; } = DisplayBasisKinds;
	public IReadOnlyList<string> QuantityModeKindList { get; } = QuantityModeKinds;

	/// <summary>区分(EnumHaibun)選択肢。先頭は「指定なし」</summary>
	public IReadOnlyList<string> KubunKindList { get; } = ["指定なし", .. Enum.GetNames<EnumHaibun>()];

	[ObservableProperty]
	public partial string DisplayBasisKind { get; set; } = DisplayBasisKinds[0];

	[ObservableProperty]
	public partial string QuantityModeKind { get; set; } = QuantityModeKinds[0];

	[ObservableProperty]
	public partial string ShohinCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string DenDayFromText { get; set; } = DateTime.Now.AddMonths(-3).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string DenDayToText { get; set; } = DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

	/// <summary>納品日範囲。空欄可（指定なし）</summary>
	[ObservableProperty]
	public partial string NouhinDayFromText { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string NouhinDayToText { get; set; } = string.Empty;

	/// <summary>出庫倉庫コード（単一、空欄なら全倉庫）</summary>
	[ObservableProperty]
	public partial string SokoCode { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TokuiCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TokuiCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string KubunKind { get; set; } = "指定なし";

	protected override void Init() {
		Title = QueryTitle;
		Message = "条件を指定して［印刷］を押してください。";
	}

	protected override void OnClearConditions() {
		DisplayBasisKind = DisplayBasisKinds[0];
		QuantityModeKind = QuantityModeKinds[0];
		ShohinCodeFrom = string.Empty;
		ShohinCodeTo = string.Empty;
		DenDayFromText = DateTime.Now.AddMonths(-3).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
		DenDayToText = DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
		NouhinDayFromText = string.Empty;
		NouhinDayToText = string.Empty;
		SokoCode = string.Empty;
		TokuiCodeFrom = string.Empty;
		TokuiCodeTo = string.Empty;
		KubunKind = "指定なし";
	}

	/// <summary>
	/// この画面は一覧照会を持たず印刷専用のため、基底(BaseQueryViewModel)が要求する検索コマンドの実体は使わない
	/// （F5は割り当てず、UI上のボタンも無い）。印刷本体は <see cref="DoOutputPdf"/> (F6) が担う。
	/// </summary>
	protected override Task OnSearchAsync(CancellationToken ct) => Task.CompletedTask;

	[RelayCommand]
	void SelectSoko() { var c = SelectSokoCode(); if (c != null) SokoCode = c; }

	[RelayCommand]
	void SelectShohinFrom() { var c = SelectShohinCode(); if (c != null) ShohinCodeFrom = c; }

	[RelayCommand]
	void SelectShohinTo() { var c = SelectShohinCode(); if (c != null) ShohinCodeTo = c; }

	[RelayCommand]
	void SelectTokuiFrom() { var c = SelectTokuiCode(); if (c != null) TokuiCodeFrom = c; }

	[RelayCommand]
	void SelectTokuiTo() { var c = SelectTokuiCode(); if (c != null) TokuiCodeTo = c; }

	/// <summary>
	/// 表示中の絞込条件でPDF帳票(品番×倉庫×表示基準ごとに改ページするマトリクス表)を出力する。
	/// </summary>
	[RelayCommand(IncludeCancelCommand = true)]
	async Task DoOutputPdf(CancellationToken ct) {
		if (IsBusy) return;
		if (!TryParseDate(DenDayFromText, out var denFrom)) return;
		if (!TryParseDate(DenDayToText, out var denTo)) return;
		if (denFrom > denTo) {
			MessageEx.ShowWarningDialog("配分指示日の開始日が終了日より後になっています。", owner: ActiveWindow);
			return;
		}
		DateTime? nouhinFrom = null, nouhinTo = null;
		if (!string.IsNullOrWhiteSpace(NouhinDayFromText)) {
			if (!TryParseDate(NouhinDayFromText, out var f)) return;
			nouhinFrom = f;
		}
		if (!string.IsNullOrWhiteSpace(NouhinDayToText)) {
			if (!TryParseDate(NouhinDayToText, out var t)) return;
			nouhinTo = t;
		}
		if (nouhinFrom is not null && nouhinTo is not null && nouhinFrom > nouhinTo) {
			MessageEx.ShowWarningDialog("納品日の開始日が終了日より後になっています。", owner: ActiveWindow);
			return;
		}

		StartBusy("PDF出力中...");
		try {
			var candidates = await LoadCandidatesAsync(denFrom, denTo, nouhinFrom, nouhinTo, ct);
			if (candidates.Count == 0) {
				MessageEx.ShowWarningDialog("出力対象データがありません。条件を確認してください。", owner: ActiveWindow);
				return;
			}
			var csv = await BuildCsvAsync(candidates, denFrom, denTo, nouhinFrom, nouhinTo, ct);
			if (string.IsNullOrWhiteSpace(csv)) {
				MessageEx.ShowWarningDialog("出力対象データがありません。条件を確認してください。", owner: ActiveWindow);
				return;
			}
			await PrintPdfHelper.RunPrintPdfAsync(this, ActiveWindow, m => Message = m, FormFile, new PrintByCsvParam(csv), null, ct);
		}
		catch (OperationCanceledException) {
			Message = "PDF出力を中断しました";
		}
		catch (Exception ex) {
			Message = $"PDF出力に失敗しました。{ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			FinishBusy();
		}
	}

	async Task<List<TranHaibun>> LoadCandidatesAsync(DateTime denFrom, DateTime denTo, DateTime? nouhinFrom, DateTime? nouhinTo, CancellationToken ct) {
		List<string> parameters = [ToDenDay(denFrom), ToDenDay(denTo)];
		var where = "h.DenDay BETWEEN @0 AND @1";
		if (nouhinFrom is not null) {
			where += $" AND h.NouhinDay >= {AddSqlParameter(parameters, ToDenDay(nouhinFrom.Value))}";
		}
		if (nouhinTo is not null) {
			where += $" AND h.NouhinDay <= {AddSqlParameter(parameters, ToDenDay(nouhinTo.Value))}";
		}
		where += BuildCodeRangeWhere(parameters, "sh.Code", ShohinCodeFrom, ShohinCodeTo);
		where += BuildCodeRangeWhere(parameters, "ten.Code", TokuiCodeFrom, TokuiCodeTo);
		if (!string.IsNullOrWhiteSpace(SokoCode)) {
			where += $" AND soko.Code = {AddSqlParameter(parameters, SokoCode.Trim())}";
		}
		if (KubunKind != "指定なし" && Enum.TryParse<EnumHaibun>(KubunKind, out var kubun)) {
			where += $" AND h.Kubun = {AddSqlParameter(parameters, (int)kubun)}";
		}

		// 「品番×出庫倉庫×表示基準」でグルーピングするため、並び順もその順に揃える。
		var sql = $@"
SELECT h.*
FROM {nameof(TranHaibun)} h
LEFT JOIN {nameof(MasterShohin)} sh ON sh.Id = h.Id_Shohin
LEFT JOIN {nameof(MasterTokui)} soko ON soko.Id = h.Id_Soko
LEFT JOIN {nameof(MasterTokui)} ten ON ten.Id = h.Id_Tenpo
WHERE {where}
ORDER BY h.Id_Shohin, h.Id_Soko, h.Id_Tenpo, h.Id";
		return await QuerySqlListAsync<TranHaibun>(sql, parameters, ct);
	}

	async Task<Dictionary<long, MasterTokui>> LoadTokuiMapAsync(IEnumerable<long> ids, CancellationToken ct) {
		var list = ids.Where(x => x > 0).Distinct().ToList();
		if (list.Count == 0) return [];
		var rows = await QuerySqlListAsync<MasterTokui>($"SELECT * FROM {nameof(MasterTokui)} WHERE Id IN ({string.Join(",", list)})", [], ct);
		return rows.ToDictionary(x => x.Id);
	}

	async Task<Dictionary<long, MasterShohin>> LoadShohinMapAsync(IEnumerable<long> ids, CancellationToken ct) {
		var list = ids.Where(x => x > 0).Distinct().ToList();
		if (list.Count == 0) return [];
		var rows = await QuerySqlListAsync<MasterShohin>($"SELECT * FROM {nameof(MasterShohin)} WHERE Id IN ({string.Join(",", list)})", [], ct);
		return rows.ToDictionary(x => x.Id);
	}

	async Task<Dictionary<(long shohin, long col, long siz), DerivedShohinColSiz>> LoadSkuMapAsync(IEnumerable<long> shohinIds, CancellationToken ct) {
		var list = shohinIds.Where(x => x > 0).Distinct().ToList();
		if (list.Count == 0) return [];
		var rows = await QuerySqlListAsync<DerivedShohinColSiz>($"SELECT * FROM {nameof(DerivedShohinColSiz)} WHERE Id_Shohin IN ({string.Join(",", list)})", [], ct);
		var map = new Dictionary<(long, long, long), DerivedShohinColSiz>();
		foreach (var d in rows) {
			map[(d.Id_Shohin, d.Id_Col, d.Id_Siz)] = d;
		}
		return map;
	}

	/// <summary>
	/// 明細行(candidates)を「品番×倉庫×表示基準値」でグルーピングし、縦軸=得意先/横軸=基準の逆軸(最大10列)の
	/// マトリクスCSVを組み立てる。CSV列順は ShippingDeliveryList.qfm の item1〜item28 と一致させること。
	/// </summary>
	async Task<string> BuildCsvAsync(List<TranHaibun> candidates, DateTime denFrom, DateTime denTo, DateTime? nouhinFrom, DateTime? nouhinTo, CancellationToken ct) {
		var tokuiMap = await LoadTokuiMapAsync(candidates.Select(x => x.Id_Soko).Concat(candidates.Select(x => x.Id_Tenpo)), ct);
		var shohinMap = await LoadShohinMapAsync(candidates.Select(x => x.Id_Shohin), ct);
		var skuMap = await LoadSkuMapAsync(candidates.Select(x => x.Id_Shohin), ct);
		var isColorBasis = DisplayBasisKind == DisplayBasisKinds[0];
		var isPlannedQty = QuantityModeKind == QuantityModeKinds[0];
		var conditionText = BuildConditionText(denFrom, denTo, nouhinFrom, nouhinTo);

		List<string> lines = [];
		// 品番×倉庫×基準値でグループ化（基準値=色基準ならId_Col、サイズ基準ならId_Siz）
		var groups = candidates
			.GroupBy(h => (h.Id_Shohin, h.Id_Soko, Basis: isColorBasis ? h.Id_Col : h.Id_Siz))
			.OrderBy(g => shohinMap.GetValueOrDefault(g.Key.Id_Shohin)?.Code, StringComparer.OrdinalIgnoreCase)
			.ThenBy(g => tokuiMap.GetValueOrDefault(g.Key.Id_Soko)?.Code, StringComparer.OrdinalIgnoreCase);

		foreach (var group in groups) {
			var groupRows = group.ToList();
			var soko = tokuiMap.GetValueOrDefault(group.Key.Id_Soko);
			var shohin = shohinMap.GetValueOrDefault(group.Key.Id_Shohin);
			var sokoDisp = CodeNameDisplay.Format(group.Key.Id_Soko, soko?.Code, soko?.Name);
			var shohinDisp = CodeNameDisplay.Format(group.Key.Id_Shohin, shohin?.Code, shohin?.Name);
			var jodaiDisp = groupRows.Max(x => x.Jodai).ToString("N0", CultureInfo.InvariantCulture);

			// 横軸（基準の逆軸）の値一覧。色基準ならサイズ、サイズ基準なら色。コード順に並べて10列ずつ折り返す。
			var otherAxisIds = groupRows.Select(x => isColorBasis ? x.Id_Siz : x.Id_Col).Distinct()
				.OrderBy(id => SkuAxisCode(skuMap, group.Key.Id_Shohin, id, isColorBasis), StringComparer.OrdinalIgnoreCase)
				.ToList();
			var chunks = otherAxisIds.Chunk(MaxColumns).ToList();

			for (var partIndex = 0; partIndex < chunks.Count; partIndex++) {
				var chunk = chunks[partIndex];
				var basisDisp = BuildBasisDisplay(skuMap, group.Key.Id_Shohin, group.Key.Basis, isColorBasis, partIndex, chunks.Count);
				var groupKey = $"{group.Key.Id_Shohin}|{group.Key.Id_Soko}|{group.Key.Basis}|{partIndex}";
				var colHeads = new string[MaxColumns];
				for (var i = 0; i < MaxColumns; i++) {
					colHeads[i] = i < chunk.Length ? SkuAxisDisplay(skuMap, group.Key.Id_Shohin, chunk[i], isColorBasis) : string.Empty;
				}

				// このチャンクに属する明細だけに絞り、得意先×横軸値で数量を合算する（同一SKUで複数の指示日行がありうるためSUMする）。
				var chunkRows = groupRows.Where(x => chunk.Contains(isColorBasis ? x.Id_Siz : x.Id_Col)).ToList();
				var tenpoGroups = chunkRows.GroupBy(x => x.Id_Tenpo)
					.OrderBy(g => tokuiMap.GetValueOrDefault(g.Key)?.Code, StringComparer.OrdinalIgnoreCase);

				foreach (var tenpoGroup in tenpoGroups) {
					var ten = tokuiMap.GetValueOrDefault(tenpoGroup.Key);
					var tenpoDisp = CodeNameDisplay.Format(tenpoGroup.Key, ten?.Code, ten?.Name);
					var vals = new long[MaxColumns];
					for (var i = 0; i < chunk.Length; i++) {
						var axisId = chunk[i];
						vals[i] = tenpoGroup.Where(x => (isColorBasis ? x.Id_Siz : x.Id_Col) == axisId)
							.Sum(x => (long)(isPlannedQty ? x.Su : x.JitsuSu));
					}
					var rowTotal = vals.Sum();

					List<string> fields = [
						groupKey,
						sokoDisp,
						shohinDisp,
						basisDisp,
						jodaiDisp,
						.. colHeads,
						tenpoDisp,
						.. vals.Select(v => v.ToString(CultureInfo.InvariantCulture)),
						rowTotal.ToString(CultureInfo.InvariantCulture),
						conditionText,
					];
					lines.Add(BuildCsvLine(fields));
				}
			}
		}
		return lines.Count == 0 ? string.Empty : string.Join("\r\n", lines) + "\r\n";
	}

	static string SkuAxisCode(Dictionary<(long, long, long), DerivedShohinColSiz> skuMap, long idShohin, long axisId, bool isColorBasis) {
		var entry = skuMap.Values.FirstOrDefault(x => x.Id_Shohin == idShohin && (isColorBasis ? x.Id_Siz == axisId : x.Id_Col == axisId));
		return (isColorBasis ? entry?.Code_Siz : entry?.Code_Col) ?? string.Empty;
	}

	static string SkuAxisDisplay(Dictionary<(long, long, long), DerivedShohinColSiz> skuMap, long idShohin, long axisId, bool isColorBasis) {
		var entry = skuMap.Values.FirstOrDefault(x => x.Id_Shohin == idShohin && (isColorBasis ? x.Id_Siz == axisId : x.Id_Col == axisId));
		var code = (isColorBasis ? entry?.Code_Siz : entry?.Code_Col) ?? string.Empty;
		var name = (isColorBasis ? entry?.Mei_Siz : entry?.Mei_Col) ?? string.Empty;
		return $"{code} {name}".Trim();
	}

	static string BuildBasisDisplay(Dictionary<(long, long, long), DerivedShohinColSiz> skuMap, long idShohin, long basisId, bool isColorBasis, int partIndex, int partCount) {
		var entry = skuMap.Values.FirstOrDefault(x => x.Id_Shohin == idShohin && (isColorBasis ? x.Id_Col == basisId : x.Id_Siz == basisId));
		var code = (isColorBasis ? entry?.Code_Col : entry?.Code_Siz) ?? string.Empty;
		var name = (isColorBasis ? entry?.Mei_Col : entry?.Mei_Siz) ?? string.Empty;
		var label = isColorBasis ? "色基準" : "サイズ基準";
		var disp = $"{label}：{code} {name}".Trim();
		return partCount > 1 ? $"{disp}（続き {partIndex + 1}/{partCount}）" : disp;
	}

	string BuildConditionText(DateTime denFrom, DateTime denTo, DateTime? nouhinFrom, DateTime? nouhinTo) {
		var nouhin = nouhinFrom is null && nouhinTo is null
			? "指定なし"
			: $"{(nouhinFrom?.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) ?? "指定なし")}〜{(nouhinTo?.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) ?? "指定なし")}";
		var soko = string.IsNullOrWhiteSpace(SokoCode) ? "指定なし" : SokoCode.Trim();
		var shohin = string.IsNullOrWhiteSpace(ShohinCodeFrom) && string.IsNullOrWhiteSpace(ShohinCodeTo)
			? "指定なし" : $"{ShohinCodeFrom}〜{ShohinCodeTo}";
		var tokui = string.IsNullOrWhiteSpace(TokuiCodeFrom) && string.IsNullOrWhiteSpace(TokuiCodeTo)
			? "指定なし" : $"{TokuiCodeFrom}〜{TokuiCodeTo}";
		return $"表示基準:{DisplayBasisKind} 出力数量:{QuantityModeKind} 配分指示日:{denFrom:yyyy/MM/dd}〜{denTo:yyyy/MM/dd} 納品日:{nouhin} 商品:{shohin} 倉庫:{soko} 得意先:{tokui} 区分:{KubunKind}";
	}

	static string BuildCsvLine(IEnumerable<string> fields) => string.Join(",", fields.Select(CsvField));

	static string CsvField(string? value) {
		var v = value ?? string.Empty;
		return v.Contains(',') || v.Contains('"') || v.Contains('\n') ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
	}
}
