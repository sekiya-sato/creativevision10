using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;
using System.Collections.ObjectModel;

namespace CvWpfclient.ViewModels._08Zaiko;

/// <summary>
/// 棚卸差異問合せ。棚卸入力(Tran60Tana)の数量と理論在庫(SummaryRealStock)を突き合わせ、
/// 差異が出ているSKUを画面で確認する。棚卸明細表(帳票)の画面版で、印刷せずに絞り込みたい場合に使う。
///
/// 棚卸数は同一SKUへ複数棚（棚番）から入力されることがあるためSKU単位に合計してから比較する。
/// 理論在庫は現在庫を見るので、棚卸後に在庫が動くと差異がずれる。棚卸直後の確認を前提にしている。
///
/// 集計は Msg101 の生SQLで行い、行はテーブルクラス(Tran60Tana / SummaryRealStock)へ受けてから
/// クライアント側で突き合わせる。Msg101 は任意列を返せないため（BaseQueryViewModel のコメント参照）。
/// </summary>
public partial class StockDifferenceQueryViewModel : Helpers.BaseQueryViewModel {
	protected override string QueryTitle => "棚卸差異問合せ";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.ToString("yyyy/MM/01");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string SokoCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SokoCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinCodeTo { get; set; } = string.Empty;

	/// <summary>true=差異があるSKUのみ / false=全て。</summary>
	[ObservableProperty]
	public partial bool IsDiffOnly { get; set; } = true;

	[ObservableProperty]
	public partial ObservableCollection<StockDifferenceRow> Rows { get; set; } = [];

	[ObservableProperty]
	public partial StockDifferenceRow? SelectedRow { get; set; }

	[ObservableProperty]
	public partial int RowCount { get; set; }

	/// <summary>差異数量の合計（プラスとマイナスが相殺されるので実棚の増減として読む）</summary>
	[ObservableProperty]
	public partial int TotalDiffSu { get; set; }

	/// <summary>差異金額の合計（原価ベース）</summary>
	[ObservableProperty]
	public partial int TotalDiffKingaku { get; set; }

	[RelayCommand]
	void SelectSokoCodeFrom() => SokoCodeFrom = SelectSokoCode() ?? SokoCodeFrom;

	[RelayCommand]
	void SelectSokoCodeTo() => SokoCodeTo = SelectSokoCode() ?? SokoCodeTo;

	[RelayCommand]
	void SelectShohinCodeFrom() => ShohinCodeFrom = SelectShohinCode() ?? ShohinCodeFrom;

	[RelayCommand]
	void SelectShohinCodeTo() => ShohinCodeTo = SelectShohinCode() ?? ShohinCodeTo;

	protected override void OnClearConditions() {
		DenDayFrom = DateTime.Today.ToString("yyyy/MM/01");
		DenDayTo = DateTime.Today.ToString("yyyy/MM/dd");
		SokoCodeFrom = string.Empty;
		SokoCodeTo = string.Empty;
		ShohinCodeFrom = string.Empty;
		ShohinCodeTo = string.Empty;
		IsDiffOnly = true;
		Rows = [];
		RowCount = 0;
		TotalDiffSu = 0;
		TotalDiffKingaku = 0;
	}

	protected override async Task OnSearchAsync(CancellationToken ct) {
		if (!TryParseDate(DenDayFrom, out var from) || !TryParseDate(DenDayTo, out var to)) return;
		if (from > to) {
			MessageEx.ShowWarningDialog("棚卸日の範囲が逆転しています。", owner: ActiveWindow);
			return;
		}
		if (!TryGetMaxCount(out var maxCount)) return;

		// 棚卸入力をSKU単位に集計する。SummaryRealStock の列名へ合わせて受け取り、
		// Su に「棚卸数」、Id_* にSKUキーを入れる（型は使い回すが意味は棚卸数である点に注意）。
		var tanaList = await LoadTanaAsync(from, to, maxCount, ct);
		if (tanaList.Count == 0) {
			Rows = [];
			RowCount = 0;
			TotalDiffSu = 0;
			TotalDiffKingaku = 0;
			Message = "該当する棚卸入力がありません";
			return;
		}

		var stockMap = await LoadTheoreticalAsync(tanaList, ct);
		var nameMap = await LoadNamesAsync(tanaList, ct);

		ObservableCollection<StockDifferenceRow> rows = [];
		foreach (var tana in tanaList) {
			var key = new SkuKey(tana.Id_Soko, tana.Id_Shohin, tana.Id_Col, tana.Id_Siz);
			var theoretical = stockMap.GetValueOrDefault(key);
			var diff = tana.Su - theoretical;
			if (IsDiffOnly && diff == 0) continue;

			var info = nameMap.GetValueOrDefault(key) ?? new SkuInfo();
			rows.Add(new StockDifferenceRow {
				SokoCode = info.SokoCode,
				SokoName = info.SokoName,
				ShohinCode = info.ShohinCode,
				ShohinName = info.ShohinName,
				ColName = info.ColName,
				SizName = info.SizName,
				TheoreticalSu = theoretical,
				ActualSu = tana.Su,
				DiffSu = diff,
				GenkaTanka = info.GenkaTanka,
				DiffKingaku = diff * info.GenkaTanka,
			});
		}

		Rows = rows;
		RowCount = rows.Count;
		TotalDiffSu = rows.Sum(x => x.DiffSu);
		TotalDiffKingaku = rows.Sum(x => x.DiffKingaku);
		SelectedRow = rows.FirstOrDefault();
		Message = $"{DateTime.Now:MM/dd HH:mm:ss} {RowCount:N0}件（差異数量計 {TotalDiffSu:N0} / 差異金額計 {TotalDiffKingaku:N0}）";
	}

	/// <summary>棚卸入力をSKU単位に合計して取得する。</summary>
	async Task<List<SummaryRealStock>> LoadTanaAsync(DateTime from, DateTime to, int maxCount, CancellationToken ct) {
		List<string> parameters = [];
		var where = $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
			+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}";
		where += BuildCodeRangeWhere(parameters, TranMeisaiSql.HeaderCode("VSoko"), SokoCodeFrom, SokoCodeTo);
		where += BuildCodeRangeWhere(parameters, TranMeisaiSql.Str("Code_Shohin"), ShohinCodeFrom, ShohinCodeTo);

		var sql = $@"
SELECT
    0 AS Id, 0 AS Vdc, 0 AS Vdu,
    h.Id_Soko                        AS Id_Soko,
    {TranMeisaiSql.Num("Id_Shohin")} AS Id_Shohin,
    {TranMeisaiSql.Num("Id_Col")}    AS Id_Col,
    {TranMeisaiSql.Num("Id_Siz")}    AS Id_Siz,
    SUM({TranMeisaiSql.Num("Su")})   AS Su
FROM Tran60Tana h, {TranMeisaiSql.From}
WHERE {TranMeisaiSql.Guard}
  AND {where}
GROUP BY h.Id_Soko, Id_Shohin, Id_Col, Id_Siz
ORDER BY h.Id_Soko, Id_Shohin, Id_Col, Id_Siz
LIMIT {maxCount}";

		return await QuerySqlListAsync<SummaryRealStock>(sql, parameters, ct);
	}

	/// <summary>棚卸対象SKUの理論在庫を取得する。</summary>
	async Task<Dictionary<SkuKey, int>> LoadTheoreticalAsync(List<SummaryRealStock> tanaList, CancellationToken ct) {
		var sokoIds = tanaList.Select(x => x.Id_Soko).Distinct().ToList();
		var shohinIds = tanaList.Select(x => x.Id_Shohin).Distinct().ToList();
		if (sokoIds.Count == 0 || shohinIds.Count == 0) return [];

		// Id は内部生成値でユーザ入力を含まないため IN 句へ直接埋め込む
		var sql = $@"
SELECT Id, Vdc, Vdu, Id_Soko, Id_Shohin, Id_Col, Id_Siz, Su
FROM SummaryRealStock
WHERE Id_Soko IN ({string.Join(",", sokoIds)})
  AND Id_Shohin IN ({string.Join(",", shohinIds)})";

		var list = await QuerySqlListAsync<SummaryRealStock>(sql, [], ct);
		var map = new Dictionary<SkuKey, int>();
		foreach (var s in list) {
			map[new SkuKey(s.Id_Soko, s.Id_Shohin, s.Id_Col, s.Id_Siz)] = s.Su;
		}
		return map;
	}

	/// <summary>SKUの名称と原価を取得する。倉庫名は取引先マスタ、色サイズ名は派生テーブルから。</summary>
	async Task<Dictionary<SkuKey, SkuInfo>> LoadNamesAsync(List<SummaryRealStock> tanaList, CancellationToken ct) {
		var shohinIds = tanaList.Select(x => x.Id_Shohin).Distinct().ToList();
		var sokoIds = tanaList.Select(x => x.Id_Soko).Distinct().ToList();
		if (shohinIds.Count == 0) return [];

		var shohinList = await QuerySqlListAsync<MasterShohin>($@"
SELECT Id, Vdc, Vdu, Code, Name, TankaGenka
FROM MasterShohin WHERE Id IN ({string.Join(",", shohinIds)})", [], ct);
		var shohinMap = shohinList.ToDictionary(x => x.Id);

		var sokoList = await QuerySqlListAsync<MasterTokui>($@"
SELECT Id, Vdc, Vdu, Code, Name
FROM MasterTokui WHERE Id IN ({string.Join(",", sokoIds)})", [], ct);
		var sokoMap = sokoList.ToDictionary(x => x.Id);

		var csList = await QuerySqlListAsync<DerivedShohinColSiz>($@"
SELECT Id, Vdc, Vdu, Id_Shohin, Id_Col, Id_Siz, Code_Col, Mei_Col, Code_Siz, Mei_Siz
FROM DerivedShohinColSiz WHERE Id_Shohin IN ({string.Join(",", shohinIds)})", [], ct);
		var csMap = new Dictionary<(long, long, long), DerivedShohinColSiz>();
		foreach (var cs in csList) {
			csMap[(cs.Id_Shohin, cs.Id_Col, cs.Id_Siz)] = cs;
		}

		var result = new Dictionary<SkuKey, SkuInfo>();
		foreach (var t in tanaList) {
			var key = new SkuKey(t.Id_Soko, t.Id_Shohin, t.Id_Col, t.Id_Siz);
			shohinMap.TryGetValue(t.Id_Shohin, out var sh);
			sokoMap.TryGetValue(t.Id_Soko, out var so);
			csMap.TryGetValue((t.Id_Shohin, t.Id_Col, t.Id_Siz), out var cs);
			result[key] = new SkuInfo {
				SokoCode = so?.Code ?? string.Empty,
				SokoName = so?.Name ?? string.Empty,
				ShohinCode = sh?.Code ?? string.Empty,
				ShohinName = sh?.Name ?? string.Empty,
				ColName = cs?.Mei_Col ?? string.Empty,
				SizName = cs?.Mei_Siz ?? string.Empty,
				GenkaTanka = sh?.TankaGenka ?? 0,
			};
		}
		return result;
	}

	readonly record struct SkuKey(long IdSoko, long IdShohin, long IdCol, long IdSiz);

	sealed class SkuInfo {
		public string SokoCode { get; set; } = string.Empty;
		public string SokoName { get; set; } = string.Empty;
		public string ShohinCode { get; set; } = string.Empty;
		public string ShohinName { get; set; } = string.Empty;
		public string ColName { get; set; } = string.Empty;
		public string SizName { get; set; } = string.Empty;
		public int GenkaTanka { get; set; }
	}
}

/// <summary>棚卸差異問合せの1行</summary>
public sealed class StockDifferenceRow {
	public string SokoCode { get; set; } = string.Empty;
	public string SokoName { get; set; } = string.Empty;
	public string ShohinCode { get; set; } = string.Empty;
	public string ShohinName { get; set; } = string.Empty;
	public string ColName { get; set; } = string.Empty;
	public string SizName { get; set; } = string.Empty;
	public int TheoreticalSu { get; set; }
	public int ActualSu { get; set; }
	public int DiffSu { get; set; }
	public int GenkaTanka { get; set; }
	public int DiffKingaku { get; set; }
}
