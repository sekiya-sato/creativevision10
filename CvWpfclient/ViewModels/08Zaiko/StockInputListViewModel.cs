using CommunityToolkit.Mvvm.ComponentModel;
using CvBase;
using CvWpfclient.Helpers;
using System.Linq;

namespace CvWpfclient.ViewModels._08Zaiko;

/// <summary>
/// 棚卸入力(一覧方式) — 倉庫の対象SKUを品番順に並べ、実棚数をまとめて入力して Tran60Tana を1伝票登録する。
/// <para>
/// 伝票明細方式の 棚卸入力(<see cref="StockInputViewModel"/>) と同じテーブルへ登録するが、
/// あちらは「明細行を1件ずつ追加して商品を選ぶ」ので数百SKUの実棚作業には向かない。
/// こちらは在庫のあるSKU（+ 品番範囲のSKU）を先に全部並べて数量だけ埋める運用を想定している。
/// </para>
/// <para>
/// 【在庫には影響しない】`TranCalcBase.GetCalcSoko(nameof(Tran60Tana))` は (0,0,0,0) を返すので、
/// 棚卸伝票を登録しても SummaryStock / SummaryRealStock の在庫数は変わらない。
/// 実棚と理論在庫の差を在庫へ反映するのは棚卸確定（Phase 15 の月次更新処理）側の仕事で、
/// 本画面は「数えた結果を記録する」ところまでを担当する。
/// 差異の確認は 棚卸差異問合せ(<see cref="StockDifferenceQueryViewModel"/>) で行う。
/// </para>
/// <para>
/// 【在庫0のSKUも出す理由】実棚では「理論在庫0なのに現物があった」を記録する必要があるため、
/// 品番範囲を指定した場合は在庫レコードが無いSKUも DerivedShohinColSiz から補って並べる。
/// 品番範囲が空のときは在庫のあるSKUだけに絞る（全SKU展開は件数が過大になるため）。
/// </para>
/// </summary>
public partial class StockInputListViewModel : Helpers.BaseStockSheetInputViewModel<Tran60Tana> {
	protected override string QueryTitle => "棚卸入力(一覧方式)";
	protected override string InputSuHeader => "実棚数";

	/// <summary>棚番（Tran60Tana.TanaNo）。伝票単位で1つ。</summary>
	[ObservableProperty]
	public partial string TanaNo { get; set; } = string.Empty;

	public StockInputListViewModel() {
		// 実棚は「差異のある行だけ直す」運用が多いので理論在庫を初期値に入れておく
		IsPrefillTheoretical = true;
	}

	protected override void OnClearConditions() {
		base.OnClearConditions();
		TanaNo = string.Empty;
		IsPrefillTheoretical = true;
	}

	protected override async Task<List<StockSheetRow>> LoadRowsAsync(CancellationToken ct) {
		var stock = await LoadStockAsync(ct);
		var stockMap = stock
			.GroupBy(s => (s.Id_Shohin, s.Id_Col, s.Id_Siz))
			.ToDictionary(g => g.Key, g => g.Sum(x => x.Su));

		// 品番範囲を指定したときだけ、在庫0のSKUも並べる（実棚で「現物があった」を記録するため）
		var hasShohinRange = !string.IsNullOrWhiteSpace(ShohinCodeFrom) || !string.IsNullOrWhiteSpace(ShohinCodeTo);
		var skuList = hasShohinRange ? await LoadSkuAsync(ct) : [];

		var keys = new HashSet<(long Shohin, long Col, long Siz)>(stockMap.Keys);
		foreach (var sku in skuList) keys.Add((sku.Id_Shohin, sku.Id_Col, sku.Id_Siz));

		var shohinMap = await LoadShohinMapAsync(keys.Select(k => k.Shohin), ct);
		var skuMap = await LoadSkuMapAsync(keys.Select(k => k.Shohin), ct);

		List<StockSheetRow> rows = [];
		foreach (var key in keys) {
			shohinMap.TryGetValue(key.Shohin, out var shohin);
			skuMap.TryGetValue(key, out var sku);
			rows.Add(CreateRow(
				key.Shohin, key.Col, key.Siz,
				stockMap.TryGetValue(key, out var su) ? su : 0,
				shohin, sku));
		}
		return rows;
	}

	protected override Tran60Tana BuildDenpyo(List<Tran99Meisai> meisai) => new() {
		TanaNo = TanaNo.Trim(),
	};
}
