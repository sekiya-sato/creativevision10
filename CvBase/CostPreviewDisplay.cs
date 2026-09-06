using CvBase.Share;
using System.Globalization;

namespace CvBase;

/// <summary>
/// 原価4画面（消化仕入更新・最終仕入原価更新・総平均原価更新・評価替え）の確認一覧が共通で使う、
/// 画面表示用の純粋な整形・判定ロジック（原価4項目 詳細設計 §8.1・§8.3）。
/// <para>
/// 本来は `CvWpfclient` の ViewModel に置く内容だが、`Tests/TestServer` から `CvWpfclient`
/// （WPFプロジェクト）を直接参照できないため、既存の <see cref="TaxRateResolver"/> と同じ理由で
/// テスト可能な `CvBase` へ切り出す。整形対象のenum（<see cref="EnumCostCalcError"/> ほか）と
/// 同じアセンブリに置くことで、引数をenumのまま受け取れる。
/// </para>
/// <para>
/// enumを <c>int</c> で受けないのは、<see cref="EnumCostCalcError"/> と
/// <see cref="EnumCostProcessStatus"/> のように「どちらも0始まりのint」である区分が複数あり、
/// 取り違えてもコンパイルが通ってしまうためである。
/// </para>
/// </summary>
public static class CostPreviewDisplay {
	/// <summary>
	/// 掛率(1/100%単位。例: 6500 = 65.00%)を画面表示用の"65.00%"形式へ変換する
	/// （原価4項目 詳細設計 §8.3 消化仕入更新一覧の「掛率」列、<c>ConsumptionPreviewRow.RateBasisPoints</c>）。
	/// </summary>
	public static string FormatRateBasisPoints(int rateBasisPoints) =>
		(rateBasisPoints / 100.0).ToString("0.00", CultureInfo.InvariantCulture) + "%";

	/// <summary>
	/// 行がエラー行かどうかを判定する。<c>CostUpdateDbConsumption.PreviewConsumptionPurchases</c>の
	/// エラー件数集計（<c>errorCount = computation.Rows.Count(r =&gt; r.Error != EnumCostCalcError.None
	/// || !string.IsNullOrEmpty(r.ErrorMessage))</c>）と同じ基準を、画面側でも一貫して使うために切り出す。
	/// </summary>
	public static bool IsErrorRow(EnumCostCalcError error, string? errorMessage) =>
		error != EnumCostCalcError.None || !string.IsNullOrEmpty(errorMessage);

	/// <summary>「状態」列の表示文言（エラー行か正常行か）。</summary>
	public static string FormatRowStatus(EnumCostCalcError error, string? errorMessage) =>
		IsErrorRow(error, errorMessage) ? "エラー" : "正常";

	/// <summary>
	/// 消化仕入の生成元売上テーブル種別（0=卸売上 <c>Tran00Uriage</c>、1=店舗売上 <c>Tran01Tenuri</c>）の表示文言。
	/// </summary>
	public static string FormatConsumptionSourceType(EnumConsumptionSourceType sourceType) => sourceType switch {
		EnumConsumptionSourceType.Uriage => "卸売上",
		EnumConsumptionSourceType.Tenuri => "店舗売上",
		_ => $"不明({(int)sourceType})",
	};

	/// <summary>
	/// 消化仕入計算区分（0=原価代用、1=上代×掛率）の表示文言（原価4項目 詳細設計 §4.4）。
	/// </summary>
	public static string FormatConsumptionCalcType(EnumConsumptionCalcType calcType) => calcType switch {
		EnumConsumptionCalcType.CostBased => "原価代用",
		EnumConsumptionCalcType.RateBased => "上代×掛率",
		_ => $"不明({(int)calcType})",
	};

	/// <summary>
	/// 原価4処理の画面表示用実行状態（0=未実行、1=完了、2=再実行要、3=エラー）
	/// の表示文言（原価4項目 詳細設計 §2.5.6）。
	/// </summary>
	public static string FormatCostProcessStatus(EnumCostProcessStatus status) => status switch {
		EnumCostProcessStatus.NotRun => "未実行",
		EnumCostProcessStatus.Completed => "完了",
		EnumCostProcessStatus.RerunRequired => "再実行要",
		EnumCostProcessStatus.Error => "エラー",
		_ => $"不明({(int)status})",
	};

	/// <summary>yyyyMMdd を yyyy/MM/dd へ整形する。変換できなければそのまま返す。</summary>
	public static string FormatYmd8ToSlash(string yyyymmdd) =>
		DateTime.TryParseExact(yyyymmdd, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
			? day.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)
			: yyyymmdd;

	/// <summary>yyyyMM を yyyy/MM へ整形する。変換できなければそのまま返す。</summary>
	public static string FormatYm6ToSlash(string yyyymm) =>
		DateTime.TryParseExact(yyyymm + "01", "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
			? day.ToString("yyyy/MM", CultureInfo.InvariantCulture)
			: yyyymm;

	/// <summary>
	/// 諸掛確認画面（原価4項目 詳細設計 §8.2）の判定重み(<see cref="EnumSundryCheckSeverity"/>)の表示文言。
	/// </summary>
	public static string FormatSundryCheckSeverity(EnumSundryCheckSeverity severity) => severity switch {
		EnumSundryCheckSeverity.Info => "情報",
		EnumSundryCheckSeverity.Warning => "警告",
		EnumSundryCheckSeverity.Error => "エラー",
		_ => $"不明({(int)severity})",
	};

	/// <summary>
	/// 生地・付属仕入(<see cref="Tran02Material"/>)の取引区分(<see cref="EnumShiire"/>: 10=仕入、20=仕入返品、
	/// 30=値引、99=その他)の表示文言。諸掛確認画面（原価4項目 詳細設計 §8.2）の「取引区分」列で使う。
	/// </summary>
	public static string FormatShiireKubun(int kubun) => kubun switch {
		(int)EnumShiire.Shiire => "仕入",
		(int)EnumShiire.Henpin => "仕入返品",
		(int)EnumShiire.Nebiki => "値引",
		(int)EnumShiire.Other => "その他",
		_ => $"不明({kubun})",
	};

	/// <summary>
	/// 最終仕入原価更新・総平均原価更新のプレビュー結果が「原価方式不一致」1行だけかどうかを判定する
	/// （原価4項目 詳細設計 §2.3、§8.4・§8.5）。サーバーは<c>MasterSysman.CostMethod</c>が画面の方式と
	/// 一致しないとき、商品を特定しない<see cref="EnumCostCalcError.CostMethodMismatch"/>の1行だけを返す
	/// （<c>CostUpdateDbCost.NewCostMethodMismatchRow</c>）。画面はこれを確認一覧の1行として埋もれさせず、
	/// 「現在の原価方式では実行できません」という専用メッセージとして表示するために使う。
	/// </summary>
	public static bool IsCostMethodMismatchOnly(IReadOnlyList<CostPreviewRow> rows) =>
		rows.Count == 1 && rows[0].Error == EnumCostCalcError.CostMethodMismatch;
}
