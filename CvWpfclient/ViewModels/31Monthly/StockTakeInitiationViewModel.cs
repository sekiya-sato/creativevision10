using CodeShare;

namespace CvWpfclient.ViewModels._31Monthly;

/// <summary>
/// 棚卸開始処理。棚卸年月末時点の帳簿在庫を <c>SummaryStock.BookQty</c> へ保存して凍結する。
/// <para>
/// これを実行しておくと、棚卸作業中に伝票が入っても棚卸差異の「帳簿在庫数」が動かない。
/// 差異調査で伝票を修正したあとは、確定処理の前にもう一度実行して帳簿在庫を取り直す
/// （旧CV.netの棚卸7段階の 4→5→6 に相当する運用。仕様 8.1 / F0'）。
/// </para>
/// <para>
/// 実行前に「棚卸日一括メンテナンス」で倉庫ごとの棚卸日を設定しておく。
/// </para>
/// </summary>
public partial class StockTakeInitiationViewModel : BaseStocktakeViewModel {
	protected override CvFlag TargetFlag => CvFlag.Msg054_StocktakeStart;
	protected override string ActionName => "棚卸開始処理";
	protected override string ResultUnit => "件の帳簿在庫を保存";
}
