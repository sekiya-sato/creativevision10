using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CvBase;
using CvWpfclient.Helpers;
using System.Windows;

namespace CvWpfclient.ViewModels._31Monthly;

/// <summary>
/// 棚卸確定処理。実棚数(<c>Tran60Tana</c>)と帳簿在庫の差を在庫調整伝票(<c>Tran61Chosei</c>)へ起こす。
/// <para>
/// 棚卸確定処理を行わないと、棚卸データは在庫へ反映されない（旧CV.netと同じ）。
/// 差は集計テーブルへ直接書かずに伝票として持つため、全件Rebuild しても在庫が一致する（仕様 8.4 F0 / F2）。
/// </para>
/// <para>
/// <b>再確定できる。</b>確定後に棚卸対象日以前の伝票を修正した場合は、もう一度実行すれば
/// 前回この処理が作った調整伝票を取り消してから作り直す（仕様 F0''）。
/// </para>
/// <para>
/// 調整伝票の計上日は店舗ごとの棚卸基準日(<c>Tran60TanaDate.TanaDay</c>)になったため、日付入力欄は無い
/// （設計書2.4）。基準日以外の日付で棚卸入力された伝票があれば、実行前に補正するか確認する（設計書4）。
/// </para>
/// </summary>
public partial class StockTakeFinalizationViewModel : BaseStocktakeViewModel {
	protected override CvFlag TargetFlag => CvFlag.Msg055_StocktakeFix;
	protected override string ActionName => "棚卸確定処理";
	protected override string ResultUnit => "件の在庫調整伝票を作成";

	/// <summary>
	/// 基準日以外の日付で入力された棚卸伝票を基準日へ補正してから確定するか。
	/// <see cref="ConfirmBeforeExecute"/> の確認結果で決まる。
	/// </summary>
	[ObservableProperty]
	protected override partial bool AlignMisdated { get; set; }

	/// <summary>
	/// 基準日以外の棚卸入力(<see cref="BaseStocktakeViewModel.MisdatedRows"/>)があれば、
	/// 計上日を基準日へ補正してから確定するかを Yes/No で確認する(設計書4)。
	/// </summary>
	protected override bool ConfirmBeforeExecute() {
		if (MisdatedRows.Count == 0) {
			AlignMisdated = false;
			return true;
		}

		var slipCount = MisdatedRows.Sum(x => x.SlipCount);
		var result = MessageEx.ShowQuestionDialog(
			$"基準日以外の日付で入力された棚卸伝票が {slipCount} 件あります。計上日を棚卸基準日へ補正してから確定しますか？\n"
				+ "（いいえ を選ぶと、これらの棚卸入力は集計されません）",
			owner: ClientLib.GetActiveView(this));
		if (result == MessageBoxResult.Yes) {
			AlignMisdated = true;
			return true;
		}
		if (result == MessageBoxResult.No) {
			AlignMisdated = false;
			return true;
		}
		return false;
	}
}
