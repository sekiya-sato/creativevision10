/*
# description
BaseStockSheetInputViewModel は「倉庫のSKUを一覧で引き、行ごとに数量を入力して1伝票にまとめて登録する」
いわゆる**一覧方式**の入力画面の共通基底クラスです。棚卸入力(一覧方式) / 在庫移動入力 / 仕入返品入力 が使います。

伝票明細方式(BaseTranInputViewModel)は「明細行を1件ずつ追加して商品を選ぶ」ので
実棚のように数百SKUを順に埋める作業には向きません。一覧方式は逆に
「対象SKUを先に全部並べて、数量だけ埋める」ことに特化しています。

【Msg101 の制約】QueryListSqlParam はサーバ側で必ず「DBにマップされた型」へ materialize するため
任意の列形状は返せません。よって
  (1) 在庫数は SummaryRealStock、(2) 色サイズ名は DerivedShohinColSiz、(3) 商品名と単価は MasterShohin
と**テーブル単位に型付きで取得し、クライアント側で1行へ合成**します（Phase 9 と同じ方針）。

# example
public partial class StockInputListViewModel : Helpers.BaseStockSheetInputViewModel<Tran60Tana> {
	protected override string QueryTitle => "棚卸入力(一覧方式)";
	protected override string InputSuHeader => "実棚数";
	protected override Task<List<StockSheetRow>> LoadRowsAsync(CancellationToken ct) { ... }
	protected override Tran60Tana BuildDenpyo(List<Tran99Meisai> meisai) { ... }
}
 */
using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace CvWpfclient.Helpers;

/// <summary>
/// 一覧方式入力の1行。理論在庫と入力数の両方を持ち、差異を自動計算する。
/// </summary>
public sealed partial class StockSheetRow : ObservableObject {
	public long Id_Shohin { get; set; }
	public string Code_Shohin { get; set; } = string.Empty;
	public string Mei_Shohin { get; set; } = string.Empty;
	public long Id_Col { get; set; }
	public string Code_Col { get; set; } = string.Empty;
	public string Mei_Col { get; set; } = string.Empty;
	public long Id_Siz { get; set; }
	public string Code_Siz { get; set; } = string.Empty;
	public string Mei_Siz { get; set; } = string.Empty;
	public string JanCode { get; set; } = string.Empty;

	/// <summary>理論在庫（SummaryRealStock.Su）。表示専用。</summary>
	public int TheoreticalSu { get; set; }

	/// <summary>原価単価。伝票明細の Tanka / Gedai に入れる。</summary>
	public int TankaGenka { get; set; }

	/// <summary>上代単価。伝票明細の Jodai に入れる。</summary>
	public int TankaJodai { get; set; }

	/// <summary>入力数（実棚数 / 移動数 / 返品数）。0 の行は伝票明細に含めない。</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(DiffSu))]
	[NotifyPropertyChangedFor(nameof(RemainSu))]
	[NotifyPropertyChangedFor(nameof(JodaiKingaku))]
	[NotifyPropertyChangedFor(nameof(GedaiKingaku))]
	public partial int InputSu { get; set; }

	/// <summary>入力数 − 理論在庫。棚卸差異（プラスなら現物が多い）。</summary>
	public int DiffSu => InputSu - TheoreticalSu;

	/// <summary>理論在庫 − 入力数。移動後に出庫元へ残る数の目安。</summary>
	public int RemainSu => TheoreticalSu - InputSu;

	/// <summary>上代金額（入力数 × 上代単価）。伝票の JodaiTotal と同じ積み方。</summary>
	public int JodaiKingaku => InputSu * TankaJodai;

	/// <summary>下代金額（入力数 × 下代単価）。伝票の GedaiTotal と同じ積み方。</summary>
	public int GedaiKingaku => InputSu * TankaGenka;

	/// <summary>伝票明細へ変換する。</summary>
	public Tran99Meisai ToMeisai(int no) => new() {
		No = no,
		Id_Shohin = Id_Shohin,
		Code_Shohin = Code_Shohin,
		Mei_Shohin = Mei_Shohin,
		Id_Col = Id_Col,
		Code_Col = Code_Col,
		Mei_Col = Mei_Col,
		Id_Siz = Id_Siz,
		Code_Siz = Code_Siz,
		Mei_Siz = Mei_Siz,
		JanCode = JanCode,
		Su = InputSu,
		Tanka = TankaGenka,
		Kingaku = InputSu * TankaGenka,
		Jodai = TankaJodai,
		Gedai = TankaGenka,
	};
}

public abstract partial class BaseStockSheetInputViewModel<TDen> : BaseQueryViewModel
	where TDen : TranAllHeader, new() {

	/// <summary>入力数列の名称（"実棚数" / "移動数"）。メッセージに使う。</summary>
	protected abstract string InputSuHeader { get; }

	/// <summary>対象SKU行を読み込む。派生で在庫起点かSKUマスタ起点かを決める。</summary>
	protected abstract Task<List<StockSheetRow>> LoadRowsAsync(CancellationToken ct);

	/// <summary>入力済み明細から登録する伝票を組み立てる。</summary>
	protected abstract TDen BuildDenpyo(List<Tran99Meisai> meisai);

	/// <summary>
	/// 登録前の追加検証。false を返すと登録しない（警告表示は実装側の責任）。
	/// マスタ照会を伴うことがあるので async。同期版にすると UI スレッドで待つことになり危険。
	/// </summary>
	protected virtual Task<bool> ValidateBeforeRegisterAsync(CancellationToken ct) => Task.FromResult(true);

	// ---- 検索条件 ----------------------------------------------------------------

	/// <summary>対象倉庫コード（棚卸なら棚卸対象、移動なら出庫元）</summary>
	[ObservableProperty]
	public partial string SokoCode { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SokoName { get; set; } = string.Empty;

	/// <summary>計上日 yyyy/MM/dd</summary>
	[ObservableProperty]
	public partial string DenDayText { get; set; } = DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string ShohinCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinCodeTo { get; set; } = string.Empty;

	/// <summary>在庫0のSKUを一覧から除く</summary>
	[ObservableProperty]
	public partial bool IsZeroExcluded { get; set; }

	/// <summary>入力数の初期値に理論在庫を入れる（棚卸で「差異のある行だけ直す」運用向け）</summary>
	[ObservableProperty]
	public partial bool IsPrefillTheoretical { get; set; }

	/// <summary>入力担当者コード（任意）。他の伝票入力画面と同じく画面で選ばせる。</summary>
	[ObservableProperty]
	public partial string ShainCode { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShainName { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string Memo { get; set; } = string.Empty;

	// ---- 結果 --------------------------------------------------------------------

	[ObservableProperty]
	public partial ObservableCollection<StockSheetRow> Rows { get; set; } = [];

	[ObservableProperty]
	public partial StockSheetRow? SelectedRow { get; set; }

	[ObservableProperty]
	public partial int RowCount { get; set; }

	[ObservableProperty]
	public partial int InputSuTotal { get; set; }

	/// <summary>上代金額の合計（入力数 × 上代単価の総和）</summary>
	[ObservableProperty]
	public partial int JodaiKingakuTotal { get; set; }

	/// <summary>下代金額の合計（入力数 × 下代単価の総和）</summary>
	[ObservableProperty]
	public partial int GedaiKingakuTotal { get; set; }

	/// <summary>登録済み伝票のId（登録後に画面へ出す）</summary>
	[ObservableProperty]
	public partial long RegisteredDenId { get; set; }

	/// <summary>解決済みの対象倉庫Id。検索時に SokoCode から引く。</summary>
	protected long IdSoko { get; private set; }

	protected override void OnClearConditions() {
		SokoCode = string.Empty;
		SokoName = string.Empty;
		ShohinCodeFrom = string.Empty;
		ShohinCodeTo = string.Empty;
		IsZeroExcluded = false;
		IsPrefillTheoretical = false;
		ShainCode = string.Empty;
		ShainName = string.Empty;
		Memo = string.Empty;
		DenDayText = DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
		Rows = [];
		RowCount = 0;
		InputSuTotal = 0;
		JodaiKingakuTotal = 0;
		GedaiKingakuTotal = 0;
		RegisteredDenId = 0;
	}

	protected override async Task OnSearchAsync(CancellationToken ct) {
		if (string.IsNullOrWhiteSpace(SokoCode)) {
			MessageEx.ShowWarningDialog("倉庫を指定してください。", owner: ActiveWindow);
			return;
		}
		if (!TryParseDate(DenDayText, out _)) return;
		if (!TryGetMaxCount(out var maxCount)) return;

		var soko = await ResolveSokoAsync(SokoCode, ct);
		if (soko == null) {
			MessageEx.ShowWarningDialog($"倉庫コード {SokoCode} が見つかりません。", owner: ActiveWindow);
			return;
		}
		IdSoko = soko.Id;
		SokoName = soko.Name ?? string.Empty;

		var rows = await LoadRowsAsync(ct);
		if (IsZeroExcluded) rows = [.. rows.Where(r => r.TheoreticalSu != 0)];
		rows = [.. rows
			.OrderBy(r => r.Code_Shohin, StringComparer.Ordinal)
			.ThenBy(r => r.Code_Col, StringComparer.Ordinal)
			.ThenBy(r => r.Code_Siz, StringComparer.Ordinal)
			.Take(maxCount)];

		foreach (var row in rows) {
			if (IsPrefillTheoretical) row.InputSu = row.TheoreticalSu;
			row.PropertyChanged += OnRowPropertyChanged;
		}

		foreach (var old in Rows) old.PropertyChanged -= OnRowPropertyChanged;
		Rows = new ObservableCollection<StockSheetRow>(rows);
		RowCount = Rows.Count;
		RegisteredDenId = 0;
		UpdateTotals();
		Message = $"{RowCount} 件を表示しました（倉庫 {SokoCode} {SokoName}）";
	}

	void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
		if (e.PropertyName == nameof(StockSheetRow.InputSu)) UpdateTotals();
	}

	void UpdateTotals() {
		InputSuTotal = Rows.Sum(r => r.InputSu);
		JodaiKingakuTotal = Rows.Sum(r => r.JodaiKingaku);
		GedaiKingakuTotal = Rows.Sum(r => r.GedaiKingaku);
	}

	/// <summary>入力済み(数量≠0)の行を伝票にして登録する。</summary>
	[RelayCommand(IncludeCancelCommand = true)]
	protected async Task Register(CancellationToken ct) {
		if (IsBusy) return;
		if (Rows.Count == 0) {
			MessageEx.ShowWarningDialog("先に検索して対象SKUを表示してください。", owner: ActiveWindow);
			return;
		}
		if (!TryParseDate(DenDayText, out var denDay)) return;
		if (!await ValidateBeforeRegisterAsync(ct)) return;

		var targets = Rows.Where(r => r.InputSu != 0).ToList();
		if (targets.Count == 0) {
			MessageEx.ShowWarningDialog($"{InputSuHeader}が入力された行がありません。", owner: ActiveWindow);
			return;
		}

		var confirm = $"{targets.Count} 明細（{InputSuHeader}計 {targets.Sum(r => r.InputSu):N0}）を登録しますか？";
		if (MessageEx.ShowQuestionDialog(confirm, owner: ActiveWindow) != System.Windows.MessageBoxResult.Yes) return;

		StartBusy("登録中...");
		try {
			var meisai = targets.Select((r, i) => r.ToMeisai(i + 1)).ToList();
			var den = BuildDenpyo(meisai);
			den.DenDay = denDay.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
			den.Id_Soko = IdSoko;
			den.VSoko = new CodeNameView { Sid = IdSoko, Cd = SokoCode.Trim(), Mei = SokoName };
			var shain = await ResolveShainAsync(ShainCode, ct);
			if (shain != null) {
				ShainName = shain.Name ?? string.Empty;
				den.Id_Shain = shain.Id;
				den.VShain = new CodeNameView { Sid = shain.Id, Cd = shain.Code ?? string.Empty, Mei = shain.Name ?? string.Empty };
			}
			den.Memo = Memo;
			den.Jmeisai = meisai;
			den.SuTotal = meisai.Sum(m => m.Su);
			den.KingakuTotal = meisai.Sum(m => m.Kingaku);
			den.JodaiTotal = meisai.Sum(m => m.Su * m.Jodai);
			den.GedaiTotal = meisai.Sum(m => m.Su * m.Gedai);

			var inserted = await InsertDenpyoAsync(den, ct);
			RegisteredDenId = inserted?.Id ?? 0;
			Message = $"登録しました（伝票No={RegisteredDenId}, {meisai.Count} 明細）";
		}
		catch (OperationCanceledException) {
			Message = "登録を中断しました";
		}
		catch (Exception ex) {
			Message = $"登録失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			FinishBusy();
		}
	}

	async Task<TDen?> InsertDenpyoAsync(TDen den, CancellationToken ct) {
		ct.ThrowIfCancellationRequested();
		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		var msg = new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg201_Op_Execute,
			DataType = typeof(InsertParam),
			DataMsg = Common.SerializeObject(new InsertParam(typeof(TDen), Common.SerializeObject(den))),
		};
		var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
		if (reply.Code < 0) {
			var detail = reply.Code < -9000 ? reply.Option : reply.DataMsg;
			throw new InvalidOperationException($"{detail} ({reply.Code})");
		}
		return Common.DeserializeObject(reply.DataMsg ?? string.Empty, reply.DataType) as TDen;
	}

	// ---- データ取得ヘルパー（派生の LoadRowsAsync から使う） ----------------------

	async Task<MasterShain?> ResolveShainAsync(string code, CancellationToken ct) {
		if (string.IsNullOrWhiteSpace(code)) return null;
		List<string> parameters = [];
		var sql = $@"
SELECT Id, Vdc, Vdu, Code, Name, Ryaku, Kana
FROM MasterShain
WHERE Code = {AddSqlParameter(parameters, code.Trim())}
LIMIT 1";
		var list = await QuerySqlListAsync<MasterShain>(sql, parameters, ct);
		return list.FirstOrDefault();
	}

	async Task<MasterTokui?> ResolveSokoAsync(string code, CancellationToken ct) {
		List<string> parameters = [];
		var sql = $@"
SELECT Id, Vdc, Vdu, Code, Name, Ryaku, Kana
FROM MasterTokui
WHERE TenType IN (0, 3, 6) AND Code = {AddSqlParameter(parameters, code.Trim())}
LIMIT 1";
		var list = await QuerySqlListAsync<MasterTokui>(sql, parameters, ct);
		return list.FirstOrDefault();
	}

	/// <summary>対象倉庫の在庫（SummaryRealStock）を品番範囲で取得する。</summary>
	protected async Task<List<SummaryRealStock>> LoadStockAsync(CancellationToken ct) {
		List<string> parameters = [];
		var where = BuildShohinCodeWhere(parameters, "sh.Code");
		var sql = $@"
SELECT s.Id, s.Vdc, s.Vdu, s.Id_Soko, s.Id_Shohin, s.Id_Col, s.Id_Siz, s.Su
FROM SummaryRealStock s
    LEFT JOIN MasterShohin sh ON sh.Id = s.Id_Shohin
WHERE s.Id_Soko = {AddSqlParameter(parameters, IdSoko)}{where}";
		return await QuerySqlListAsync<SummaryRealStock>(sql, parameters, ct);
	}

	/// <summary>品番範囲に該当するSKU(色サイズ展開)を取得する。在庫0のSKUも含める用途。</summary>
	protected async Task<List<DerivedShohinColSiz>> LoadSkuAsync(CancellationToken ct) {
		List<string> parameters = [];
		var where = BuildShohinCodeWhere(parameters, "d.Code");
		var sql = $@"
SELECT d.Id, d.Vdc, d.Vdu, d.Id_Shohin, d.RowIdx, d.Code,
       d.Id_Col, d.Code_Col, d.Mei_Col, d.Id_Siz, d.Code_Siz, d.Mei_Siz,
       d.Jan1, d.Jan2, d.Jan3
FROM DerivedShohinColSiz d
WHERE 1 = 1{where}";
		return await QuerySqlListAsync<DerivedShohinColSiz>(sql, parameters, ct);
	}

	/// <summary>指定Idの商品マスタ（名称と単価）を取得する。</summary>
	protected async Task<Dictionary<long, MasterShohin>> LoadShohinMapAsync(IEnumerable<long> shohinIds, CancellationToken ct) {
		var ids = shohinIds.Where(id => id > 0).Distinct().ToArray();
		if (ids.Length == 0) return [];
		var idList = string.Join(",", ids.Select(id => id.ToString(CultureInfo.InvariantCulture)));
		var sql = $@"
SELECT Id, Vdc, Vdu, Code, Name, Ryaku, Kana, TankaGenka, TankaJodai
FROM MasterShohin
WHERE Id IN ({idList})";
		var list = await QuerySqlListAsync<MasterShohin>(sql, [], ct);
		return list.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
	}

	/// <summary>指定商品のSKU(色サイズ名/JAN)を取得する。在庫行の名称解決用。</summary>
	protected async Task<Dictionary<(long Shohin, long Col, long Siz), DerivedShohinColSiz>> LoadSkuMapAsync(
		IEnumerable<long> shohinIds, CancellationToken ct) {
		var ids = shohinIds.Where(id => id > 0).Distinct().ToArray();
		if (ids.Length == 0) return [];
		var idList = string.Join(",", ids.Select(id => id.ToString(CultureInfo.InvariantCulture)));
		var sql = $@"
SELECT Id, Vdc, Vdu, Id_Shohin, RowIdx, Code,
       Id_Col, Code_Col, Mei_Col, Id_Siz, Code_Siz, Mei_Siz,
       Jan1, Jan2, Jan3
FROM DerivedShohinColSiz
WHERE Id_Shohin IN ({idList})";
		var list = await QuerySqlListAsync<DerivedShohinColSiz>(sql, [], ct);
		return list
			.GroupBy(x => (x.Id_Shohin, x.Id_Col, x.Id_Siz))
			.ToDictionary(g => g.Key, g => g.First());
	}

	string BuildShohinCodeWhere(List<string> parameters, string column) {
		var where = "";
		if (!string.IsNullOrWhiteSpace(ShohinCodeFrom)) {
			where += $" AND {column} >= {AddSqlParameter(parameters, ShohinCodeFrom.Trim())}";
		}
		if (!string.IsNullOrWhiteSpace(ShohinCodeTo)) {
			where += $" AND {column} <= {AddSqlParameter(parameters, ShohinCodeTo.Trim())}";
		}
		return where;
	}

	/// <summary>在庫行 + 名称マスタから一覧行を組み立てる。</summary>
	protected static StockSheetRow CreateRow(
		long idShohin, long idCol, long idSiz, int theoreticalSu,
		MasterShohin? shohin, DerivedShohinColSiz? sku) => new() {
			Id_Shohin = idShohin,
			Code_Shohin = shohin?.Code ?? sku?.Code ?? string.Empty,
			Mei_Shohin = shohin?.Name ?? string.Empty,
			Id_Col = idCol,
			Code_Col = sku?.Code_Col ?? string.Empty,
			Mei_Col = sku?.Mei_Col ?? string.Empty,
			Id_Siz = idSiz,
			Code_Siz = sku?.Code_Siz ?? string.Empty,
			Mei_Siz = sku?.Mei_Siz ?? string.Empty,
			JanCode = sku?.Jan1 ?? string.Empty,
			TheoreticalSu = theoreticalSu,
			TankaGenka = shohin?.TankaGenka ?? 0,
			TankaJodai = shohin?.TankaJodai ?? 0,
		};

	// ---- 選択ダイアログ ----------------------------------------------------------

	[RelayCommand]
	void SelectSoko() {
		var code = SelectSokoCode();
		if (code == null) return;
		SokoCode = code;
	}

	[RelayCommand]
	void SelectShain() => ShainCode = SelectCode<MasterShain>("") ?? ShainCode;

	[RelayCommand]
	void SelectShohinFrom() => ShohinCodeFrom = SelectShohinCode() ?? ShohinCodeFrom;

	[RelayCommand]
	void SelectShohinTo() => ShohinCodeTo = SelectShohinCode() ?? ShohinCodeTo;
}
