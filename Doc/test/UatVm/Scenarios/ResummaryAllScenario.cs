using CvBase;
using CvWpfclient.ViewModels._00System;
using CvWpfclient.Views._00System;
using System.Globalization;
using System.Windows;

namespace UatVm.Scenarios;

/// <summary>
/// 移行後などの全件再集計を「在庫・掛再更新」画面の「全て」で実行し、冪等性を検証する。
/// </summary>
/// <remarks>
/// <para>
/// 「全て」は在庫(SummaryStock/SummaryRealStock)・売掛・買掛に加え、対象各月×実在締日の
/// 請求残(SummaryUriSei)・支払残(SummaryKaiShi)を再計算する（<see cref="SummaryRebuildRequestPlanner"/>）。
/// 請求・支払は画面と同じく再発行なし(IsReissue=false)で計算される。
/// </para>
/// <para>
/// 冪等性: 同じ条件で2回実行し、6つのSummaryテーブルを集計キー(年月/日付)ごとの指紋
/// （件数・数値列の合計・文字列列の数値換算と長さの合計・自然キー重複数）で比較する。
/// Id・Vdc・Vdu は再作成で変わり得るため比較から除く。シナリオ自身は伝票・マスタを投入せず、
/// 書き込みは正規の再集計だけなので、何回実行しても同じ状態に収束する。
/// </para>
/// <para>
/// 対象範囲は既定で期首年月(MasterSysman.FiscalStartDate)～当月。<c>--from</c>/<c>--to</c>(yyyy/MM)で変更できる。
/// 更新を伴うため、対象DBは <c>--sqlite</c> で明示し <c>--manage-server</c> で専用サーバを起動して使う。
/// </para>
/// </remarks>
public static class ResummaryAllScenario {
	/// <summary>開始年月(yyyy/MM)。未指定時は期首年月。</summary>
	public static string? YearMonthFrom { get; set; }
	/// <summary>終了年月(yyyy/MM)。未指定時は当月。</summary>
	public static string? YearMonthTo { get; set; }

	private const string UpdateTarget = "全て";

	/// <summary>比較対象のSummaryテーブル。GroupKey=指紋を分ける列、NaturalKey=重複検出に使う自然キー。</summary>
	private sealed record SummaryTable(string Name, string GroupKey, string[] NaturalKey, string[] NumberColumns, string[] TextColumns);

	private static readonly SummaryTable[] Tables = [
		new(nameof(SummaryStock), "SumMonth", ["SumMonth", "Id_Soko", "Id_Shohin", "Id_Col", "Id_Siz"],
			["Id_Soko", "Id_Shohin", "Id_Col", "Id_Siz", "Su", "ReserveQty", "CumulativeSu", "InQty", "OutQty", "TransitQty", "AdjustQty", "BookQty", "ActualQty"],
			["StocktakeDdate"]),
		new(nameof(SummaryRealStock), "''", ["Id_Soko", "Id_Shohin", "Id_Col", "Id_Siz"],
			["Id_Soko", "Id_Shohin", "Id_Col", "Id_Siz", "Su", "ReserveQty"],
			[]),
		new(nameof(SummaryUriKake), "DenMonth", ["Id_Tokui", "DenMonth"],
			["Id_Tokui", .. KakeAmountColumns("TotalIn", "TotalSales", "Uriage")],
			[]),
		new(nameof(SummaryKaiKake), "DenMonth", ["Id_Shiire", "DenMonth"],
			["Id_Shiire", .. KakeAmountColumns("TotalOut", "TotalShiire", "Shiire")],
			[]),
		new(nameof(SummaryUriSei), "substr(DenDay, 1, 6)", ["Id_Tokui", "DenDay"],
			["Id_Tokui", "Renban", .. KakeAmountColumns("TotalIn", "TotalSales", "Uriage")],
			["DenDay", "DayFrom", "DayTo", "SeikyuNo", "NyukinYoteiDay"]),
		new(nameof(SummaryKaiShi), "substr(DenDay, 1, 6)", ["Id_Shiire", "DenDay"],
			["Id_Shiire", .. KakeAmountColumns("TotalOut", "TotalShiire", "Shiire")],
			["DenDay", "DayFrom", "DayTo", "ShiharaiYoteiDay"]),
	];

	private static string[] KakeAmountColumns(string total1, string total2, string main) => [
		"Balance", total1, total2, main, "Henpin", "Nebiki", "Sonota", "Tax1", "Tax2", "Tax3",
		"TaxableAmount1", "TaxableAmount2", "TaxableAmount3", "Cash", "Fee", "Densai", "Offset", "Other",
	];

	public static async Task RunAsync(VmSession session) {
		var (from, to) = await ResolveRangeAsync(session);
		session.Note("再集計の対象範囲", new { from, to, UpdateTarget });

		var d = session.OpenView<StockKakeUpdateView, StockKakeUpdateViewModel>();
		await d.WaitAsync("init:自社締日の取得", vm => !vm.ClosingPeriodText.Contains("読み込んでいます", StringComparison.Ordinal));
		if (!session.Check("自社締日を取得できる", !d.Vm.ClosingPeriodText.Contains("失敗", StringComparison.Ordinal), new { d.Vm.ClosingPeriodText }))
			return;

		// 確認(Yes/No)には Yes、それ以外(警告・エラー)は OK で閉じる。警告・エラーは記録から失敗判定する。
		session.SetDialogResponder(request => request.Button == MessageBoxButton.YesNo ? MessageBoxResult.Yes : MessageBoxResult.OK);
		try {
			if (!await RunRebuildAsync(session, d, "1回目", from, to))
				return;
			var first = await TakeFingerprintAsync(session);

			if (!await RunRebuildAsync(session, d, "2回目", from, to))
				return;
			var second = await TakeFingerprintAsync(session);

			CompareFingerprints(session, first, second);
		}
		finally {
			session.SetDialogResponder(null);
		}
	}

	/// <summary>開始・終了年月を決める。未指定の開始は期首年月、終了は当月。</summary>
	private static async Task<(string From, string To)> ResolveRangeAsync(VmSession session) {
		var from = YearMonthFrom;
		if (string.IsNullOrWhiteSpace(from)) {
			var sysman = (await session.QueryAsync<MasterSysman>("SELECT * FROM MasterSysman ORDER BY Id LIMIT 1")).FirstOrDefault()
				?? throw new InvalidOperationException("MasterSysman がありません。");
			var fiscal = sysman.FiscalStartDate;
			// 未設定(19010101)のまま全月を回すと百年以上の空集計になるため、明示指定を求める
			if (string.IsNullOrEmpty(fiscal) || fiscal.Length < 6 || fiscal == OpeningBalanceCsv.UnsetFiscalStartDate)
				throw new InvalidOperationException($"期首年月日(FiscalStartDate)が不正です: '{fiscal}'。--from で開始年月を指定してください。");
			from = $"{fiscal[..4]}/{fiscal[4..6]}";
		}
		var to = string.IsNullOrWhiteSpace(YearMonthTo) ? DateTime.Now.ToString("yyyy/MM", CultureInfo.InvariantCulture) : YearMonthTo;
		return (from, to);
	}

	private static async Task<bool> RunRebuildAsync(VmSession session, ViewDriver<StockKakeUpdateViewModel> d, string label, string from, string to) {
		session.ClearDialogs();
		d.Input($"{label}:実行条件", vm => {
			vm.YearMonthFrom = from;
			vm.YearMonthTo = to;
			vm.UpdateTarget = UpdateTarget;
		}, new { from, to, UpdateTarget });
		await d.RunAsync($"{label}:{UpdateTarget}再集計", vm => vm.ExecuteCommand);
		d.Snapshot($"{label}:実行後", vm => new { vm.StatusMessage, vm.ProgressValue, vm.IsProcessing });

		var problems = session.Dialogs
			.Where(x => x.Request.Button != MessageBoxButton.YesNo)
			.Select(x => new { x.Request.Kind, x.Request.Message })
			.ToList();
		var completed = d.Vm.ProgressValue == 100 && d.Vm.StatusMessage.Contains("が完了しました", StringComparison.Ordinal);
		return session.Check($"{label}:{UpdateTarget}再集計が警告・エラーなしで完走する", completed && problems.Count == 0,
			new { d.Vm.ProgressValue, d.Vm.StatusMessage, problems });
	}

	/// <summary>
	/// 各Summaryテーブルの集計キーごとの指紋を取得する。キー=「テーブル|集計キー」、値=指紋文字列。
	/// </summary>
	/// <remarks>
	/// 専用DTOをサーバーへ追加しないため、共有型 <see cref="SummaryClosingCheckRow"/> を借用する
	/// （<c>TorihikiCode</c>=集計キー、<c>DayTo</c>=指紋、<c>Shime1</c>=自然キー重複数）。
	/// 数百万行のテーブルを全件転送せず、サーバ側で集約した結果だけを受け取る。
	/// </remarks>
	private static async Task<Dictionary<string, (string Fingerprint, int Duplicates)>> TakeFingerprintAsync(VmSession session) {
		var result = new Dictionary<string, (string, int)>(StringComparer.Ordinal);
		foreach (var table in Tables) {
			var rows = await session.QueryAsync<SummaryClosingCheckRow>(BuildFingerprintSql(table));
			foreach (var row in rows)
				result[$"{table.Name}|{row.TorihikiCode}"] = (row.DayTo ?? string.Empty, row.Shime1);
			session.Note($"指紋取得:{table.Name}", new { groups = rows.Count, rows = rows.Sum(x => long.Parse(x.DayTo!.Split('|')[0], CultureInfo.InvariantCulture)) });
		}
		return result;
	}

	private static string BuildFingerprintSql(SummaryTable table) {
		var parts = new List<string> { "count(*)" };
		parts.AddRange(table.NumberColumns.Select(c => $"ifnull(sum({c}), 0)"));
		// 文字列列は数値換算の合計と長さの合計で内容の変化を検出する（日付・番号は数字列）。
		parts.AddRange(table.TextColumns.Select(c => $"ifnull(sum(cast({c} as integer)), 0) || ':' || ifnull(sum(length({c})), 0)"));
		var fingerprint = string.Join(" || '|' || ", parts);
		var naturalKey = string.Join(" || ',' || ", table.NaturalKey.Select(c => $"ifnull({c}, '')"));
		return $"""
SELECT {table.GroupKey} AS TorihikiCode,
       {fingerprint} AS DayTo,
       count(*) - count(DISTINCT {naturalKey}) AS Shime1
FROM {table.Name}
GROUP BY {table.GroupKey}
""";
	}

	private static void CompareFingerprints(
		VmSession session,
		Dictionary<string, (string Fingerprint, int Duplicates)> first,
		Dictionary<string, (string Fingerprint, int Duplicates)> second) {
		var differences = first.Keys.Union(second.Keys)
			.Where(key => !first.TryGetValue(key, out var a) || !second.TryGetValue(key, out var b) || a.Fingerprint != b.Fingerprint)
			.Order(StringComparer.Ordinal)
			.Select(key => new {
				key,
				first = first.TryGetValue(key, out var a) ? a.Fingerprint : "(なし)",
				second = second.TryGetValue(key, out var b) ? b.Fingerprint : "(なし)",
			})
			.ToList();
		session.Check("冪等性: 2回目の再集計結果が1回目と全テーブル・全集計キーで一致する", differences.Count == 0,
			new { compared = first.Count, differenceCount = differences.Count, differences = differences.Take(20) });

		var duplicates = second
			.Where(x => x.Value.Duplicates != 0)
			.Select(x => new { x.Key, x.Value.Duplicates })
			.Take(20)
			.ToList();
		session.Check("自然キーの重複行がない", duplicates.Count == 0, new { duplicates });

		foreach (var table in Tables) {
			var groups = second.Where(x => x.Key.StartsWith(table.Name + "|", StringComparison.Ordinal)).ToList();
			session.Note($"再集計結果:{table.Name}", new {
				groups = groups.Count,
				rows = groups.Sum(x => long.Parse(x.Value.Fingerprint.Split('|')[0], CultureInfo.InvariantCulture)),
			});
		}
	}
}
