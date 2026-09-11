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
	/// 商品マスタ <c>MasterShohin.ConsumptionRateBasisPoints</c>（1/100%単位。6500=65.00%）を
	/// 編集画面の%入力欄用の値へ変換する（原価4項目 詳細設計 §2.5.8・§4.2）。
	/// 利用者には「65.00」のように%単位で入力させ、DBには1/100%単位のまま保存するための往復変換の片側。
	/// 表示専用の<see cref="FormatRateBasisPoints(int)"/>と異なり、"%"記号を付けない編集用の数値を返す。
	/// </summary>
	public static decimal ConsumptionRateBasisPointsToPercent(int rateBasisPoints) => rateBasisPoints / 100m;

	/// <summary>
	/// 編集画面で入力された%単位の掛率文字列を、DB保存用の1/100%単位(<c>ConsumptionRateBasisPoints</c>)へ変換する。
	/// <see cref="ConsumptionRateBasisPointsToPercent(int)"/>の逆変換。空文字は0として成功扱いする。
	/// 数値として解釈できない、または負値の場合は<see langword="false"/>を返し<paramref name="rateBasisPoints"/>は0のままにする。
	/// </summary>
	public static bool TryParseConsumptionRatePercent(string? text, out int rateBasisPoints) {
		rateBasisPoints = 0;
		if (string.IsNullOrWhiteSpace(text)) return true;
		if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var percent)) return false;
		if (percent < 0) return false;
		rateBasisPoints = (int)Math.Round(percent * 100m, MidpointRounding.AwayFromZero);
		return true;
	}

	/// <summary>
	/// 商品マスタの消化仕入設定に対する保存時検査（原価4項目 詳細設計 §4.2）。
	/// エラーが無ければ<see langword="null"/>を返す。サーバー側に専用APIは無いため、
	/// 画面（<c>MasterShohinMenteViewModel</c>）の保存前チェックがこの規則を実施する唯一の場所になる。
	/// </summary>
	public static string? ValidateShohinConsumptionSettings(
		EnumPurchaseType purchaseType,
		long idConsignmentShiire,
		EnumConsumptionCalcType consumptionCalcType,
		int tankaShiire,
		int tankaGenka,
		int consumptionRateBasisPoints,
		int consumptionRoundingUnit) {

		// 消化仕入(PurchaseType=3)以外は、設定値を保持するだけで処理に使用しない(§2.5.8・§4.2)。
		// したがって検査もしない。ここで検査すると、原価も仕入単価も未設定の通常商品
		// (計算区分は既定0=原価代用)が保存できなくなり、商品マスタの大半が登録不能になる。
		if (purchaseType != EnumPurchaseType.Consumption) {
			return null;
		}
		if (idConsignmentShiire <= 0) {
			return "消化仕入（仕入区分=消化仕入）を選択した場合、委託仕入先の指定が必須です。";
		}
		if (consumptionCalcType == EnumConsumptionCalcType.CostBased) {
			if (tankaShiire <= 0 && tankaGenka <= 0) {
				return "消化仕入計算区分が「原価代用」の場合、仕入単価または原価のいずれかを正値で設定してください。";
			}
		}
		else if (consumptionCalcType == EnumConsumptionCalcType.RateBased) {
			if (consumptionRateBasisPoints is < 1 or > 10000) {
				return "消化仕入計算区分が「上代×掛率」の場合、掛率は0.01%～100.00%の範囲で指定してください。";
			}
			if (consumptionRoundingUnit is not (1 or 10 or 100 or 1000)) {
				return "消化仕入の端数単位は1、10、100、1000円のいずれかで指定してください。";
			}
		}
		return null;
	}

	/// <summary>原価方式（0=固定、1=最終仕入、2=総平均）の表示文言（原価4項目 詳細設計 §2.3）。</summary>
	public static string FormatCostMethod(EnumCostMethod costMethod) => costMethod switch {
		EnumCostMethod.Fixed => "固定原価",
		EnumCostMethod.LastPurchase => "最終仕入原価",
		EnumCostMethod.TotalAverage => "総平均原価",
		_ => $"不明({(int)costMethod})",
	};

	/// <summary>
	/// 原価履歴(<see cref="TranGenka"/>)の発生要因（0=月次原価計算、1=評価替え）の表示文言。
	/// 商品マスタの原価履歴参照（原価4項目 詳細設計 §2.6・§9.4・§16.11）で、
	/// 月次バッチによる行と評価替えによる行を区別するために表示する。
	/// </summary>
	public static string FormatCostChangeKind(EnumCostChangeKind changeKind) => changeKind switch {
		EnumCostChangeKind.Monthly => "月次原価計算",
		EnumCostChangeKind.Reval => "評価替え",
		_ => $"不明({(int)changeKind})",
	};

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
	/// 総平均原価更新一覧の「状態」列（設計書§6.5「2026-09-06改訂」、§8.5）。エラー行を最優先し、
	/// 次に対象外・正常を判定する。<see cref="FormatRevaluationRowStatus"/>と同じ考え方（対象外はエラーでは
	/// ない）だが、対象行は評価替えの「対象」ではなく既存の「正常」という文言を維持する
	/// （最終仕入原価更新・消化仕入更新など、対象外を持たない他の原価4画面と表示文言をそろえるため）。
	/// </summary>
	public static string FormatCostPreviewRowStatus(bool isTarget, EnumCostCalcError error, string? errorMessage) =>
		IsErrorRow(error, errorMessage) ? "エラー" : isTarget ? "正常" : "対象外";

	/// <summary>
	/// 総平均原価更新一覧の「エラー」列（設計書§6.5「2026-09-06改訂」、§8.5）。エラー行は
	/// <paramref name="errorMessage"/>、対象外行は<paramref name="excludeReason"/>（負在庫／原価0円）を表示する。
	/// 正常行はいずれも空文字。<see cref="FormatRevaluationRowReason"/>と同じ組み立て方。
	/// </summary>
	public static string FormatCostPreviewRowReason(bool isTarget, EnumCostCalcError error, string? errorMessage, string? excludeReason) =>
		IsErrorRow(error, errorMessage) ? errorMessage ?? string.Empty : !isTarget ? excludeReason ?? string.Empty : string.Empty;

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

	// ------------------------------------------------------------------
	// 評価替え（原価4項目 詳細設計 §16、§8.6）
	// ------------------------------------------------------------------

	/// <summary>
	/// 評価替えの指定方式（1=率一括、2=金額一括）の表示文言（設計書§16.4）。
	/// </summary>
	public static string FormatCostRevaluationMethod(EnumCostRevaluationMethod method) => method switch {
		EnumCostRevaluationMethod.ByRate => "率一括指定",
		EnumCostRevaluationMethod.ByFixed => "金額一括指定",
		_ => $"不明({(int)method})",
	};

	/// <summary>評価替えの適用時点（0=月末、1=期末）の表示文言（設計書§16.4）。</summary>
	public static string FormatCostRevalApplyPoint(EnumCostRevalApplyPoint applyPoint) => applyPoint switch {
		EnumCostRevalApplyPoint.MonthEnd => "月末",
		EnumCostRevalApplyPoint.FiscalEnd => "期末",
		_ => $"不明({(int)applyPoint})",
	};

	/// <summary>評価替えの集計単位（設計書§16.4・§16.6.1）の表示文言。</summary>
	public static string FormatCostRevalGroupKey(EnumCostRevalGroupKey groupKey) => groupKey switch {
		EnumCostRevalGroupKey.Brand => "ブランド",
		EnumCostRevalGroupKey.Item => "アイテム",
		EnumCostRevalGroupKey.Season => "シーズン",
		EnumCostRevalGroupKey.Maker => "メーカー",
		EnumCostRevalGroupKey.Tenji => "展示会",
		_ => $"不明({(int)groupKey})",
	};

	/// <summary>評価替え抽出条件の項目種別（設計書§16.4。年度は含まない）の表示文言。</summary>
	public static string FormatCostRevalCondField(EnumCostRevalCondField field) => field switch {
		EnumCostRevalCondField.ShohinCode => "商品CD",
		EnumCostRevalCondField.MakerCode => "メーカー品番",
		EnumCostRevalCondField.Brand => "ブランド",
		EnumCostRevalCondField.Item => "アイテム",
		EnumCostRevalCondField.Maker => "メーカー",
		EnumCostRevalCondField.Season => "シーズン",
		EnumCostRevalCondField.Tenji => "展示会",
		EnumCostRevalCondField.Material => "素材",
		EnumCostRevalCondField.Country => "原産国",
		_ => $"不明({(int)field})",
	};

	/// <summary>端数処理（0=四捨五入、1=切上、2=切捨）の表示文言（評価替え画面の端数処理選択。設計書§16.4）。</summary>
	public static string FormatRounding(EnumRounding rounding) => rounding switch {
		EnumRounding.Round => "四捨五入",
		EnumRounding.Ceiling => "切上",
		EnumRounding.Floor => "切捨",
		_ => $"不明({(int)rounding})",
	};

	/// <summary>評価替えヘッダの実行状態（0=有効、1=取消）の表示文言（設計書§16.7、<see cref="TranGenkaReval"/>）。</summary>
	public static string FormatCostRevalStatus(EnumCostRevalStatus status) => status switch {
		EnumCostRevalStatus.Active => "有効",
		EnumCostRevalStatus.Canceled => "取消",
		_ => $"不明({(int)status})",
	};

	/// <summary>
	/// 評価替え明細行の「状態」列（設計書§16.6.2、§16.9）。エラー行(<c>AfterCost&lt;=0</c>)を最優先し、
	/// 次に対象外・対象を判定する。対象外はエラーではない（§16.9）ため区別して表示する。
	/// </summary>
	public static string FormatRevaluationRowStatus(bool isTarget, EnumCostCalcError error, string? errorMessage) =>
		IsErrorRow(error, errorMessage) ? "エラー" : isTarget ? "対象" : "対象外";

	/// <summary>
	/// 評価替え明細行の「エラー」列（設計書§16.6.2）。エラー行は<paramref name="errorMessage"/>、
	/// 対象外行は<paramref name="excludeReason"/>（在庫0／原価0／引き下げにならない）を表示する。
	/// 対象行はいずれも空文字。
	/// </summary>
	public static string FormatRevaluationRowReason(bool isTarget, EnumCostCalcError error, string? errorMessage, string? excludeReason) =>
		IsErrorRow(error, errorMessage) ? errorMessage ?? string.Empty : !isTarget ? excludeReason ?? string.Empty : string.Empty;

	/// <summary>
	/// 掛率入力欄の直後に表示する計算式（設計書§16.5「画面ラベルは『率』ではなく『掛率』とし、
	/// 入力欄の直後に計算式`新原価 = 元原価 × 掛率%`（四捨五入等は指定した端数処理）を表示して
	/// 誤入力を防ぐ」）。
	/// </summary>
	public static string BuildRevaluationRateFormulaText(int ratePercent) =>
		$"新原価 = 元原価 × 掛率{ratePercent}%（四捨五入等は指定した端数処理）";

	/// <summary>
	/// 評価替えの掛率(<c>RatePercent</c>)入力検証（設計書§16.4・§16.9「指定方式1で率が1～100の外」）。
	/// 純関数として切り出し、サーバー(<c>CostUpdateDbReval.ValidateRevaluationInputs</c>)と同じ規則を
	/// 画面側でも即時に適用できるようにする。エラーが無ければ<c>null</c>を返す。
	/// </summary>
	public static string? ValidateRevaluationRatePercent(int ratePercent) =>
		ratePercent is < 1 or > 100 ? "掛率は1～100の範囲で指定してください。" : null;

	/// <summary>
	/// 評価替えの指定単価(<c>FixedCost</c>)入力検証（設計書§16.4・§16.9「指定方式2で金額が0以下」）。
	/// エラーが無ければ<c>null</c>を返す。
	/// </summary>
	public static string? ValidateRevaluationFixedCost(int fixedCost) =>
		fixedCost < 1 ? "指定単価は1円以上で指定してください。" : null;

	/// <summary>
	/// 評価替えの端数単位(<c>RoundingUnit</c>)入力検証（設計書§16.4・§16.9「端数単位が1／10／100以外」）。
	/// エラーが無ければ<c>null</c>を返す。
	/// </summary>
	public static string? ValidateRevaluationRoundingUnit(int roundingUnit) =>
		roundingUnit is not (1 or 10 or 100) ? "端数単位は1、10、100円のいずれかで指定してください。" : null;

	/// <summary>
	/// 評価替えの指定方式(<see cref="EnumCostRevaluationMethod"/>)に応じた率・金額の入力検証をまとめて行う。
	/// サーバー側(<c>CostUpdateDbReval.ValidateRevaluationInputs</c>)と同じ判定を、確認(サーバー往復)の
	/// 前に画面側で即座に行うための純関数（設計書§16.9「入力エラー（確認を実行させない）」）。
	/// </summary>
	public static string? ValidateRevaluationMethodValue(EnumCostRevaluationMethod method, int ratePercent, int fixedCost) => method switch {
		EnumCostRevaluationMethod.ByRate => ValidateRevaluationRatePercent(ratePercent),
		EnumCostRevaluationMethod.ByFixed => ValidateRevaluationFixedCost(fixedCost),
		_ => "未定義の指定方式です。",
	};
}
