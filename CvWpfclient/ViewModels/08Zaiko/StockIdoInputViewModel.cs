using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using System.Linq;

namespace CvWpfclient.ViewModels._08Zaiko;

/// <summary>
/// 在庫移動入力(一覧方式) — 出庫元倉庫の在庫を一覧で引き、移動数をまとめて入力して Tran05Ido を1伝票登録する。
/// <para>
/// 移動入力(即時)(<see cref="IdoInputSokuViewModel"/>) と同じ Tran05Ido へ登録するが、
/// あちらは伝票明細方式（明細行ごとに商品を選ぶ）。こちらは「今ある在庫から選んで動かす」画面で、
/// 在庫のあるSKUだけを並べるため**在庫を超える移動を作りにくい**という違いがある。
/// </para>
/// <para>
/// 【在庫の動き】Tran05Ido は出庫元(Id_Soko)で在庫−、移動先(Id_Ido)で在庫＋が同時に立つ（即時移動）。
/// 積送中在庫を経由しないので、実際にモノが同時に動くケースで使う。
/// 積送（出庫と入庫が別日）なら 移動入力(積送) → 移動受入力 を使う。
/// </para>
/// </summary>
public partial class StockIdoInputViewModel : Helpers.BaseStockSheetInputViewModel<Tran05Ido> {
	protected override string QueryTitle => "在庫移動入力";
	protected override string InputSuHeader => "移動数";

	/// <summary>移動先倉庫コード</summary>
	[ObservableProperty]
	public partial string IdoSokoCode { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string IdoSokoName { get; set; } = string.Empty;

	/// <summary>手入力No（Tran05Ido.ManualNo）</summary>
	[ObservableProperty]
	public partial string ManualNo { get; set; } = string.Empty;

	long idIdoSoko;

	public StockIdoInputViewModel() {
		// 移動数は在庫数とは別物なので初期値を入れない（誤って全在庫を移動しないため）
		IsPrefillTheoretical = false;
		// 在庫0のSKUを移動対象に出す意味はない
		IsZeroExcluded = true;
	}

	protected override void OnClearConditions() {
		base.OnClearConditions();
		IdoSokoCode = string.Empty;
		IdoSokoName = string.Empty;
		ManualNo = string.Empty;
		idIdoSoko = 0;
		IsPrefillTheoretical = false;
		IsZeroExcluded = true;
	}

	[RelayCommand]
	void SelectIdoSoko() => IdoSokoCode = SelectSokoCode() ?? IdoSokoCode;

	protected override async Task<List<StockSheetRow>> LoadRowsAsync(CancellationToken ct) {
		var stock = await LoadStockAsync(ct);
		var stockMap = stock
			.GroupBy(s => (s.Id_Shohin, s.Id_Col, s.Id_Siz))
			.ToDictionary(g => g.Key, g => g.Sum(x => x.Su));

		var shohinMap = await LoadShohinMapAsync(stockMap.Keys.Select(k => k.Id_Shohin), ct);
		var skuMap = await LoadSkuMapAsync(stockMap.Keys.Select(k => k.Id_Shohin), ct);

		List<StockSheetRow> rows = [];
		foreach (var (key, su) in stockMap) {
			shohinMap.TryGetValue(key.Id_Shohin, out var shohin);
			skuMap.TryGetValue(key, out var sku);
			rows.Add(CreateRow(key.Id_Shohin, key.Id_Col, key.Id_Siz, su, shohin, sku));
		}
		return rows;
	}

	protected override async Task<bool> ValidateBeforeRegisterAsync(CancellationToken ct) {
		if (string.IsNullOrWhiteSpace(IdoSokoCode)) {
			MessageEx.ShowWarningDialog("移動先倉庫を指定してください。", owner: ActiveWindow);
			return false;
		}
		if (IdoSokoCode.Trim() == SokoCode.Trim()) {
			MessageEx.ShowWarningDialog("出庫元倉庫と移動先倉庫が同じです。", owner: ActiveWindow);
			return false;
		}
		// 在庫を超える移動は伝票としては作れてしまうので、ここで気付けるよう警告する
		var over = Rows.Where(r => r.InputSu > r.TheoreticalSu).ToList();
		if (over.Count > 0) {
			var head = string.Join("\n", over.Take(5).Select(r =>
				$"{r.Code_Shohin} {r.Code_Col} {r.Code_Siz}: 移動数{r.InputSu} > 在庫{r.TheoreticalSu}"));
			var more = over.Count > 5 ? $"\n… 他 {over.Count - 5} 件" : string.Empty;
			if (MessageEx.ShowQuestionDialog(
					$"在庫数を超える移動数の行が {over.Count} 件あります。続行しますか？\n{head}{more}",
					owner: ActiveWindow) != System.Windows.MessageBoxResult.Yes) {
				return false;
			}
		}
		// 移動先Idは登録直前に解決する（検索とは別条件なので OnSearchAsync では引かない）
		idIdoSoko = await ResolveIdoSokoIdAsync(ct);
		if (idIdoSoko <= 0) {
			MessageEx.ShowWarningDialog($"移動先倉庫コード {IdoSokoCode} が見つかりません。", owner: ActiveWindow);
			return false;
		}
		return true;
	}

	async Task<long> ResolveIdoSokoIdAsync(CancellationToken ct) {
		List<string> parameters = [];
		var sql = $@"
SELECT Id, Vdc, Vdu, Code, Name, Ryaku, Kana
FROM MasterTokui
WHERE TenType IN (0, 3, 6) AND Code = {AddSqlParameter(parameters, IdoSokoCode.Trim())}
LIMIT 1";
		var list = await QuerySqlListAsync<MasterTokui>(sql, parameters, ct);
		var soko = list.FirstOrDefault();
		if (soko == null) return 0;
		IdoSokoName = soko.Name ?? string.Empty;
		return soko.Id;
	}

	protected override Tran05Ido BuildDenpyo(List<Tran99Meisai> meisai) => new() {
		Id_Ido = idIdoSoko,
		VIdo = new CodeNameView { Sid = idIdoSoko, Cd = IdoSokoCode.Trim(), Mei = IdoSokoName },
		ManualNo = ManualNo.Trim(),
	};
}
