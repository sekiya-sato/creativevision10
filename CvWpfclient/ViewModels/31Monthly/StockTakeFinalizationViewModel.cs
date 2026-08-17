using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;

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
/// </summary>
public partial class StockTakeFinalizationViewModel : BaseStocktakeViewModel {
	protected override CvFlag TargetFlag => CvFlag.Msg055_StocktakeFix;
	protected override string ActionName => "棚卸確定処理";
	protected override string ResultUnit => "件の在庫調整伝票を作成";

	/// <summary>
	/// 生成する在庫調整伝票の在庫計上日。既定は棚卸年月の月末
	/// </summary>
	[ObservableProperty]
	public partial string DenDay { get; set; } =
		DateTime.Now.ToString("yyyy/MM/", CultureInfo.InvariantCulture)
			+ DateTime.DaysInMonth(DateTime.Now.Year, DateTime.Now.Month).ToString("00", CultureInfo.InvariantCulture);

	protected override bool ValidateBeforeExecute(out string errorMessage) {
		if (!TryParseDate(DenDay, out _)) {
			errorMessage = $"調整伝票日付の形式が不正です: {DenDay}";
			return false;
		}
		errorMessage = string.Empty;
		return true;
	}

	protected override string BuildDenDay(string yyyymm) =>
		TryParseDate(DenDay, out var day) ? day : LastDayOfMonth(yyyymm);
}
