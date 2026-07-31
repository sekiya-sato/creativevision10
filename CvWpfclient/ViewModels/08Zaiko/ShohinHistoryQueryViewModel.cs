using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;
using System.Collections.ObjectModel;

namespace CvWpfclient.ViewModels._08Zaiko;

/// <summary>
/// 商品履歴問合せ。1つの商品(SKU)について、在庫を動かした伝票を時系列に並べて表示する。
/// 「なぜ今この在庫数なのか」を追うための画面で、残高は表示順に積み上げた running total。
///
/// 対象伝票は在庫に影響する6種:
///   仕入(Tran03Shiire)=入 / 卸売上(Tran00Uriage)=出 / 店舗売上(Tran01Tenuri)=出 /
///   即時移動(Tran05Ido)=出入 / 移動出庫(Tran10IdoOut)=出 / 移動入庫(Tran11IdoIn)=入
/// 棚卸(Tran60Tana)は在庫を直接動かす伝票ではないので参考行として区別して出す。
///
/// 各テーブルを個別に取得してクライアント側で時系列マージする。
/// Msg101 は任意列を返せず UNION した結果を1つの型で受けられないため（BaseQueryViewModel のコメント参照）。
/// </summary>
public partial class ShohinHistoryQueryViewModel : Helpers.BaseQueryViewModel {
	protected override string QueryTitle => "商品履歴問合せ";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.AddMonths(-3).ToString("yyyy/MM/01");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	/// <summary>対象商品コード（必須）</summary>
	[ObservableProperty]
	public partial string ShohinCode { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SokoCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SokoCodeTo { get; set; } = string.Empty;

	/// <summary>true=棚卸入力も参考表示する</summary>
	[ObservableProperty]
	public partial bool IncludeTana { get; set; } = true;

	[ObservableProperty]
	public partial ObservableCollection<ShohinHistoryRow> Rows { get; set; } = [];

	[ObservableProperty]
	public partial ShohinHistoryRow? SelectedRow { get; set; }

	[ObservableProperty]
	public partial int RowCount { get; set; }

	/// <summary>対象商品の表示名（検索後に埋まる）</summary>
	[ObservableProperty]
	public partial string ShohinName { get; set; } = string.Empty;

	[RelayCommand]
	void SelectShohin() => ShohinCode = SelectShohinCode() ?? ShohinCode;

	[RelayCommand]
	void SelectSokoCodeFrom() => SokoCodeFrom = SelectSokoCode() ?? SokoCodeFrom;

	[RelayCommand]
	void SelectSokoCodeTo() => SokoCodeTo = SelectSokoCode() ?? SokoCodeTo;

	protected override void OnClearConditions() {
		DenDayFrom = DateTime.Today.AddMonths(-3).ToString("yyyy/MM/01");
		DenDayTo = DateTime.Today.ToString("yyyy/MM/dd");
		ShohinCode = string.Empty;
		ShohinName = string.Empty;
		SokoCodeFrom = string.Empty;
		SokoCodeTo = string.Empty;
		IncludeTana = true;
		Rows = [];
		RowCount = 0;
	}

	protected override async Task OnSearchAsync(CancellationToken ct) {
		if (string.IsNullOrWhiteSpace(ShohinCode)) {
			MessageEx.ShowWarningDialog("商品コードを指定してください。", owner: ActiveWindow);
			return;
		}
		if (!TryParseDate(DenDayFrom, out var from) || !TryParseDate(DenDayTo, out var to)) return;
		if (from > to) {
			MessageEx.ShowWarningDialog("期間の範囲が逆転しています。", owner: ActiveWindow);
			return;
		}
		if (!TryGetMaxCount(out var maxCount)) return;

		// 在庫に影響する伝票種別。InSign/OutSign で入出庫の向きを指定する。
		// 抽出は伝票の倉庫(VSoko)で絞っているので、その倉庫から見た向きを入れる。
		// 即時移動は VSoko が出庫元・VIdo が入庫先なので、この画面では「出」だけを計上する
		// （入と出の両方に立てると同一倉庫視点で二重計上になり残高が合わなくなる）。
		var sources = new (string Table, string Kind, int InSign, int OutSign)[] {
			("Tran03Shiire",  "仕入",      1,  0),
			("Tran00Uriage",  "卸売上",    0,  1),
			("Tran01Tenuri",  "店舗売上",  0,  1),
			("Tran05Ido",     "即時移動",  0,  1),
			("Tran10IdoOut",  "移動出庫",  0,  1),
			("Tran11IdoIn",   "移動入庫",  1,  0),
		};

		List<ShohinHistoryRow> all = [];
		foreach (var (table, kind, inSign, outSign) in sources) {
			ct.ThrowIfCancellationRequested();
			var rows = await LoadHistoryAsync(table, kind, inSign, outSign, from, to, maxCount, isTana: false, ct);
			all.AddRange(rows);
		}
		if (IncludeTana) {
			var tana = await LoadHistoryAsync("Tran60Tana", "棚卸", 0, 0, from, to, maxCount, isTana: true, ct);
			all.AddRange(tana);
		}

		// 日付→伝票種別→伝票NO で時系列に並べ、在庫増減を積み上げる
		var ordered = all
			.OrderBy(x => x.DenDaySort)
			.ThenBy(x => x.Kind)
			.ThenBy(x => x.DenNo)
			.Take(maxCount)
			.ToList();

		var running = 0;
		foreach (var row in ordered) {
			// 棚卸は在庫を動かす伝票ではないので残高へ積まない
			if (!row.IsReference) {
				running += row.InSu - row.OutSu;
			}
			row.Balance = running;
		}

		Rows = [.. ordered];
		RowCount = ordered.Count;
		SelectedRow = ordered.FirstOrDefault();
		ShohinName = ordered.FirstOrDefault(x => x.ShohinName.Length > 0)?.ShohinName ?? string.Empty;
		Message = $"{DateTime.Now:MM/dd HH:mm:ss} {RowCount:N0}件（期間内の在庫増減 {running:N0}）";
	}

	/// <summary>
	/// 1テーブル分の履歴を取得する。明細を展開して対象商品の行だけに絞る。
	/// 受け皿は Tran99Meisai ではなく SummaryRealStock を使い、Su に数量、Id_* にキーを入れる。
	/// （任意列を返せないため、既存のテーブルクラスへ意味を割り当てて運ぶ）
	/// </summary>
	async Task<List<ShohinHistoryRow>> LoadHistoryAsync(
		string table, string kind, int inSign, int outSign,
		DateTime from, DateTime to, int maxCount, bool isTana, CancellationToken ct) {

		List<string> parameters = [];
		var where = $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
			+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}"
			+ $" AND {TranMeisaiSql.Str("Code_Shohin")} = {AddSqlParameter(parameters, ShohinCode.Trim())}";
		where += BuildCodeRangeWhere(parameters, TranMeisaiSql.HeaderCode("VSoko"), SokoCodeFrom, SokoCodeTo);

		// 1テーブルあたりの取得も maxCount で抑える（全部足してから再度 Take するため多めでも問題ない）
		var sql = $@"
SELECT
    h.Id                             AS Id, 0 AS Vdc, 0 AS Vdu,
    h.Id_Soko                        AS Id_Soko,
    {TranMeisaiSql.Num("Id_Shohin")} AS Id_Shohin,
    {TranMeisaiSql.Num("Id_Col")}    AS Id_Col,
    {TranMeisaiSql.Num("Id_Siz")}    AS Id_Siz,
    {TranMeisaiSql.Num("Su")}        AS Su
FROM {table} h, {TranMeisaiSql.From}
WHERE {TranMeisaiSql.Guard}
  AND {where}
ORDER BY h.DenDay, h.Id
LIMIT {maxCount}";

		var raw = await QuerySqlListAsync<SummaryRealStock>(sql, parameters, ct);
		if (raw.Count == 0) return [];

		// 伝票日付・倉庫名・商品名・色サイズ名は別途引く（上のSQLは数量とキーだけを運んでいる）
		var meta = await LoadMetaAsync(table, raw, ct);

		List<ShohinHistoryRow> result = [];
		foreach (var r in raw) {
			meta.TryGetValue(r.Id, out var m);
			result.Add(new ShohinHistoryRow {
				Kind = kind,
				IsReference = isTana,
				DenDaySort = m?.DenDay ?? string.Empty,
				DenDayLabel = FormatDay(m?.DenDay),
				DenNo = r.Id,
				SokoCode = m?.SokoCode ?? string.Empty,
				SokoName = m?.SokoName ?? string.Empty,
				AiteName = m?.AiteName ?? string.Empty,
				ShohinCode = ShohinCode.Trim(),
				ShohinName = m?.ShohinName ?? string.Empty,
				ColName = m?.ColName ?? string.Empty,
				SizName = m?.SizName ?? string.Empty,
				InSu = inSign * r.Su,
				OutSu = outSign * r.Su,
				TanaSu = isTana ? r.Su : 0,
			});
		}
		return result;
	}

	/// <summary>伝票の日付・倉庫・相手先と、商品/色サイズの名称をまとめて取得する。</summary>
	async Task<Dictionary<long, HistoryMeta>> LoadMetaAsync(string table, List<SummaryRealStock> raw, CancellationToken ct) {
		var denNos = raw.Select(x => x.Id).Distinct().ToList();
		if (denNos.Count == 0) return [];

		// 相手先のV*列はテーブルごとに名前が違う。無いテーブルは空文字にする。
		var aiteColumn = table switch {
			"Tran03Shiire" => "VShiire",
			"Tran00Uriage" => "VTokui",
			"Tran01Tenuri" => "VTenpo",
			"Tran05Ido" or "Tran10IdoOut" or "Tran11IdoIn" => "VIdo",
			_ => "",
		};
		var aiteExpr = aiteColumn.Length > 0 ? $"ifnull(json_extract(h.{aiteColumn},'$.Mei'),'')" : "''";

		// Id は内部生成値なので IN 句へ直接埋め込む
		var sql = $@"
SELECT
    h.Id                                AS Id, 0 AS Vdc, 0 AS Vdu,
    h.DenDay                            AS Code,
    ifnull(json_extract(h.VSoko,'$.Cd'),'')  AS Name,
    ifnull(json_extract(h.VSoko,'$.Mei'),'') AS Ryaku,
    {aiteExpr}                          AS Kana
FROM {table} h
WHERE h.Id IN ({string.Join(",", denNos)})";

		// MasterShain は Id/Code/Name/Ryaku/Kana を持つ汎用の受け皿として使う
		var headers = await QuerySqlListAsync<MasterShain>(sql, [], ct);
		var headerMap = headers.ToDictionary(x => x.Id);

		var shohinIds = raw.Select(x => x.Id_Shohin).Distinct().ToList();
		var shohinList = await QuerySqlListAsync<MasterShohin>($@"
SELECT Id, Vdc, Vdu, Code, Name FROM MasterShohin WHERE Id IN ({string.Join(",", shohinIds)})", [], ct);
		var shohinMap = shohinList.ToDictionary(x => x.Id);

		var csList = await QuerySqlListAsync<DerivedShohinColSiz>($@"
SELECT Id, Vdc, Vdu, Id_Shohin, Id_Col, Id_Siz, Mei_Col, Mei_Siz
FROM DerivedShohinColSiz WHERE Id_Shohin IN ({string.Join(",", shohinIds)})", [], ct);
		var csMap = new Dictionary<(long, long, long), DerivedShohinColSiz>();
		foreach (var cs in csList) csMap[(cs.Id_Shohin, cs.Id_Col, cs.Id_Siz)] = cs;

		var result = new Dictionary<long, HistoryMeta>();
		foreach (var r in raw) {
			headerMap.TryGetValue(r.Id, out var h);
			shohinMap.TryGetValue(r.Id_Shohin, out var sh);
			csMap.TryGetValue((r.Id_Shohin, r.Id_Col, r.Id_Siz), out var cs);
			result[r.Id] = new HistoryMeta {
				DenDay = h?.Code ?? string.Empty,
				SokoCode = h?.Name ?? string.Empty,
				SokoName = h?.Ryaku ?? string.Empty,
				AiteName = h?.Kana ?? string.Empty,
				ShohinName = sh?.Name ?? string.Empty,
				ColName = cs?.Mei_Col ?? string.Empty,
				SizName = cs?.Mei_Siz ?? string.Empty,
			};
		}
		return result;
	}

	static string FormatDay(string? yyyymmdd) =>
		yyyymmdd is { Length: 8 }
			? $"{yyyymmdd[..4]}/{yyyymmdd[4..6]}/{yyyymmdd[6..]}"
			: yyyymmdd ?? string.Empty;

	sealed class HistoryMeta {
		public string DenDay { get; set; } = string.Empty;
		public string SokoCode { get; set; } = string.Empty;
		public string SokoName { get; set; } = string.Empty;
		public string AiteName { get; set; } = string.Empty;
		public string ShohinName { get; set; } = string.Empty;
		public string ColName { get; set; } = string.Empty;
		public string SizName { get; set; } = string.Empty;
	}
}

/// <summary>商品履歴問合せの1行</summary>
public sealed class ShohinHistoryRow {
	/// <summary>伝票種別の表示名</summary>
	public string Kind { get; set; } = string.Empty;
	/// <summary>true=在庫を動かさない参考行（棚卸）。残高へ積まない。</summary>
	public bool IsReference { get; set; }
	/// <summary>並び替え用の yyyyMMdd</summary>
	public string DenDaySort { get; set; } = string.Empty;
	public string DenDayLabel { get; set; } = string.Empty;
	public long DenNo { get; set; }
	public string SokoCode { get; set; } = string.Empty;
	public string SokoName { get; set; } = string.Empty;
	/// <summary>相手先（仕入先／得意先／店舗／移動先）</summary>
	public string AiteName { get; set; } = string.Empty;
	public string ShohinCode { get; set; } = string.Empty;
	public string ShohinName { get; set; } = string.Empty;
	public string ColName { get; set; } = string.Empty;
	public string SizName { get; set; } = string.Empty;
	public int InSu { get; set; }
	public int OutSu { get; set; }
	public int TanaSu { get; set; }
	/// <summary>表示順に積み上げた在庫増減の running total</summary>
	public int Balance { get; set; }
}
