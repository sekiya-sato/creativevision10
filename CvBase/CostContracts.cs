using CvBase.Share;

namespace CvBase;

/// <summary>
/// 原価4処理（消化仕入更新・最終仕入原価更新・総平均原価更新・評価替え）の実行パラメータ。
/// gRPC経由でクライアントと共有する（原価4項目 詳細設計 §2.4、§9.1）。
/// </summary>
public sealed class CostUpdateParameter {
	/// <summary>対象計上月 yyyyMM。</summary>
	public string TargetMonth { get; set; } = string.Empty;
	/// <summary>処理区分。</summary>
	public EnumCostProcessKind ProcessKind { get; set; }
	/// <summary>原価方式。原価更新（<see cref="EnumCostProcessKind.CostUpdate"/>）のときのみ意味を持つ。</summary>
	public EnumCostMethod CostMethod { get; set; }
	/// <summary>実行社員Id。</summary>
	public long Id_Shain { get; set; }
	/// <summary>更新実行Id(GUID D形式)。確認と更新で同一値を使う。</summary>
	public string BatchId { get; set; } = string.Empty;
	/// <summary>確認(プレビュー)のみで更新を伴わないか。</summary>
	public bool IsPreview { get; set; }
}

/// <summary>
/// 原価4処理のうち消化仕入・原価更新の月次状態（原価4項目 詳細設計 §2.5.6）。
/// <para>
/// 状態テーブル(`SysCostMonthState`)は新設せず(U-13)、成果テーブル(`TranConsumptionPurchaseLink` /
/// `TranGenka`)と入力データを画面表示のたびに都度突合して算出する。本DTOはその算出結果を
/// 画面へ返すためのものであり、DBの永続列とは対応しない。
/// </para>
/// </summary>
public sealed class CostMonthStatus {
	/// <summary>対象計上月 yyyyMM。</summary>
	public string SumMonth { get; set; } = string.Empty;
	/// <summary>処理区分。</summary>
	public EnumCostProcessKind ProcessKind { get; set; }
	/// <summary>算出した実行状態。</summary>
	public EnumCostProcessStatus Status { get; set; }
	/// <summary>最終成功時刻(UTC Ticks)。未実行は0。</summary>
	public long LastRunAt { get; set; }
	/// <summary>最終成功実行の更新実行Id。</summary>
	public string BatchId { get; set; } = string.Empty;
	/// <summary>最終成功時の原価方式。原価更新のみ意味を持つ。</summary>
	public EnumCostMethod CostMethod { get; set; }
	/// <summary>算出根拠にした入力データの件数。</summary>
	public long SourceCount { get; set; }
}

/// <summary>
/// 最終仕入原価更新・総平均原価更新のプレビュー一覧行（原価4項目 詳細設計 §8.4・§8.5）。
/// 両処理で列構成が共通するため1つのDTOで共有する。
/// </summary>
public sealed class CostPreviewRow {
	/// <summary>
	/// 計上月 yyyyMM。総平均原価更新の対象月自身は画面入力の<c>TargetMonth</c>と同じ値、
	/// §6.6で再計算される後続月はその後続月自身の値になる。最終仕入原価更新は常に<c>TargetMonth</c>。
	/// 後続月再計算の対象月と変更前後差額を確認一覧で区別できるようにするため、Step 7で追加した。
	/// </summary>
	public string SumMonth { get; set; } = string.Empty;
	/// <summary>商品Id。</summary>
	public long Id_Shohin { get; set; }
	/// <summary>商品コード。</summary>
	public string CodeShohin { get; set; } = string.Empty;
	/// <summary>商品名。</summary>
	public string MeiShohin { get; set; } = string.Empty;
	/// <summary>計算前原価。</summary>
	public long BeforeCost { get; set; }
	/// <summary>計算後原価。</summary>
	public long AfterCost { get; set; }
	/// <summary>前月在庫数。最終仕入原価方式は0。</summary>
	public long OpeningQty { get; set; }
	/// <summary>前月在庫金額。最終仕入原価方式は0。</summary>
	public long OpeningAmount { get; set; }
	/// <summary>対象期間の在庫加算仕入数。最終仕入原価方式は0。</summary>
	public long PurchaseQty { get; set; }
	/// <summary>対象期間の在庫加算仕入金額。最終仕入原価方式は0。</summary>
	public long PurchaseAmount { get; set; }
	/// <summary>対象期間に算入した諸掛額。総平均原価方式のみ。最終仕入原価方式は0。</summary>
	public long SundryAmount { get; set; }
	/// <summary>最終仕入根拠の`Tran03Shiire.Id`。総平均原価方式は0。</summary>
	public long SourceTranId { get; set; }
	/// <summary>最終仕入根拠の明細No。総平均原価方式は0。</summary>
	public int SourceLineNo { get; set; }
	/// <summary>最終仕入根拠の伝票日 yyyyMMdd。総平均原価方式は空文字。</summary>
	public string SourceDay { get; set; } = string.Empty;
	/// <summary>この行のエラー種別。</summary>
	public EnumCostCalcError Error { get; set; }
	/// <summary>画面表示用のエラーメッセージ。</summary>
	public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// 消化仕入更新のプレビュー一覧行（原価4項目 詳細設計 §8.3）。
/// </summary>
public sealed class ConsumptionPreviewRow {
	/// <summary>生成元売上テーブル種別。</summary>
	public EnumConsumptionSourceType SourceType { get; set; }
	/// <summary>生成元売上ヘッダId。</summary>
	public long SourceId { get; set; }
	/// <summary>生成元売上明細No。</summary>
	public int SourceLineNo { get; set; }
	/// <summary>生成元売上計上日 yyyyMMdd。</summary>
	public string SourceDay { get; set; } = string.Empty;
	/// <summary>対象商品Id。</summary>
	public long Id_Shohin { get; set; }
	/// <summary>商品コード。</summary>
	public string CodeShohin { get; set; } = string.Empty;
	/// <summary>商品名。</summary>
	public string MeiShohin { get; set; } = string.Empty;
	/// <summary>数量。</summary>
	public long Su { get; set; }
	/// <summary>委託仕入先Id。</summary>
	public long Id_Shiire { get; set; }
	/// <summary>委託仕入先名。</summary>
	public string MeiShiire { get; set; } = string.Empty;
	/// <summary>消化仕入計算区分。</summary>
	public EnumConsumptionCalcType CalcType { get; set; }
	/// <summary>掛率(1/100%単位)。計算区分0は0。</summary>
	public int RateBasisPoints { get; set; }
	/// <summary>生成単価。</summary>
	public long UnitCost { get; set; }
	/// <summary>生成金額。</summary>
	public long Kingaku { get; set; }
	/// <summary>税額。</summary>
	public long Tax { get; set; }
	/// <summary>この行のエラー種別。</summary>
	public EnumCostCalcError Error { get; set; }
	/// <summary>画面表示用のエラーメッセージ。</summary>
	public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// 諸掛確認一覧の明細行（原価4項目 詳細設計 §8.2）。<c>Tran02Material</c>ヘッダ1件・明細1件に対応する。
/// 更新を伴わない参照専用画面のためエラー行も本行に含めて返す（§3.8）。
/// </summary>
public sealed class SundryChargeDetailRow {
	/// <summary>伝票のId(<c>Tran02Material.Id</c>)。</summary>
	public long Id_Material_Slip { get; set; }
	/// <summary>伝票No。<c>Tran02Material</c>は`Id`をそのまま伝票Noとして表示する（既存一覧の作法に合わせる）。</summary>
	public long DenNo { get; set; }
	/// <summary>伝票日 yyyyMMdd。</summary>
	public string DenDay { get; set; } = string.Empty;
	/// <summary>取引区分（<c>EnumShiire</c>: 10=仕入、20=仕入返品、30=値引、99=その他）。</summary>
	public int Kubun { get; set; }
	/// <summary>仕入先Id。</summary>
	public long Id_Shiire { get; set; }
	/// <summary>仕入先名。</summary>
	public string MeiShiire { get; set; } = string.Empty;
	/// <summary>明細No(<c>Tran99MaterialMeisai.No</c>)。</summary>
	public int MeisaiNo { get; set; }
	/// <summary>費目Id(生地・付属マスタ)。</summary>
	public long Id_Material { get; set; }
	/// <summary>費目名。</summary>
	public string MeiMaterial { get; set; } = string.Empty;
	/// <summary>費用を負担する商品Id。0=諸掛ではない明細（設計書§3.3）。</summary>
	public long Id_Shohin { get; set; }
	/// <summary>商品コード。</summary>
	public string CodeShohin { get; set; } = string.Empty;
	/// <summary>商品名。</summary>
	public string MeiShohin { get; set; } = string.Empty;
	/// <summary>数量。</summary>
	public int Su { get; set; }
	/// <summary>金額。ヘッダ<c>CalcFlag</c>を適用した符号付き・税抜（設計書§3.4）。</summary>
	public long Kingaku { get; set; }
	/// <summary>この行の判定重み。</summary>
	public EnumSundryCheckSeverity Severity { get; set; }
	/// <summary>画面表示用のエラー・警告・情報メッセージ。</summary>
	public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// 諸掛確認一覧の商品別集計行（原価4項目 詳細設計 §8.2）。総平均原価更新の分子・分母（§6.3）と
/// 同じ集計を先に見せる（§6.5のエラーをこの画面で発見できるようにするため）。
/// </summary>
public sealed class SundryChargeSummaryRow {
	/// <summary>商品Id。</summary>
	public long Id_Shohin { get; set; }
	/// <summary>商品コード。</summary>
	public string CodeShohin { get; set; } = string.Empty;
	/// <summary>商品名。</summary>
	public string MeiShohin { get; set; } = string.Empty;
	/// <summary>諸掛件数。</summary>
	public long SundryCount { get; set; }
	/// <summary>諸掛金額（設計書§3.5の合計。符号付き）。</summary>
	public long SundryAmount { get; set; }
	/// <summary>当月仕入数（設計書§6.3と同じ定義）。</summary>
	public long PurchaseQty { get; set; }
	/// <summary>当月仕入金額（設計書§6.3と同じ定義。諸掛は含まない）。</summary>
	public long PurchaseAmount { get; set; }
	/// <summary>前月在庫数（設計書§6.2と同じ定義）。</summary>
	public long OpeningQty { get; set; }
	/// <summary>この商品の判定重み（明細側で検出した最大の重みを表示する）。</summary>
	public EnumSundryCheckSeverity Severity { get; set; }
	/// <summary>画面表示用のエラー・警告・情報メッセージ。</summary>
	public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// 諸掛確認画面の結果全体（原価4項目 詳細設計 §3.8、§8.2）。保存を伴わない参照専用であり、
/// 本結果に対応する更新(Apply)メソッドは存在しない。
/// </summary>
public sealed class SundryChargeCheckResult {
	/// <summary>明細行一覧。</summary>
	public IReadOnlyList<SundryChargeDetailRow> DetailRows { get; set; } = [];
	/// <summary>商品別集計行一覧。</summary>
	public IReadOnlyList<SundryChargeSummaryRow> SummaryRows { get; set; } = [];
	/// <summary>画面上部に表示する情報メッセージ（例: 現在の原価方式=最終仕入原価、対象月に諸掛明細が0件）。</summary>
	public IReadOnlyList<string> InfoMessages { get; set; } = [];
	/// <summary>エラー件数（明細・集計行の合算。総平均原価更新の実行可否判定に使う）。</summary>
	public long ErrorCount { get; set; }
	/// <summary>警告件数。</summary>
	public long WarningCount { get; set; }
}

/// <summary>
/// 消化仕入更新の対象期間が支払計算済み範囲に含まれるため、更新を中断したことを表す（原価4項目 詳細設計 §4.6）。
/// <para>
/// <see cref="CvDomainLogic.StocktakeDb"/> の <c>StocktakeMisdatedException</c>（棚卸確定処理の中断例外、
/// `CvBase/StocktakeContracts.cs`）と同じ前例に倣い、確認が必要な中断を例外で表に出す。
/// </para>
/// </summary>
public sealed class ConsumptionPurchasePaidPeriodException(string targetMonth)
	: Exception($"対象月 {targetMonth} は支払計算済み範囲に含まれるため、消化仕入更新を中断しました。支払計算を取り消してから再実行してください。") {
	/// <summary>対象計上月 yyyyMM。</summary>
	public string TargetMonth { get; } = targetMonth;
}

/// <summary>
/// 評価替え一覧の集計行（原価4項目 詳細設計 §16.6.1）。<c>GroupKey</c>で選択した軸1件に対応する。
/// </summary>
public sealed class RevaluationSummaryRow {
	/// <summary>集計単位のコード（<c>GroupKey</c>で選択した軸のコード）。</summary>
	public string GroupCode { get; set; } = string.Empty;
	/// <summary>集計単位の名称。</summary>
	public string GroupName { get; set; } = string.Empty;
	/// <summary>対象品番数。</summary>
	public long TargetCount { get; set; }
	/// <summary>数量（Σ Qty）。</summary>
	public long Qty { get; set; }
	/// <summary>元上代金額（Σ MasterShohin.TankaJodai × Qty）。</summary>
	public long JodaiAmount { get; set; }
	/// <summary>在庫金額（Σ BeforeCost × Qty）。</summary>
	public long BeforeAmount { get; set; }
	/// <summary>評価減後金額（Σ AfterCost × Qty）。</summary>
	public long AfterAmount { get; set; }
	/// <summary>
	/// 評価減差額（在庫金額－評価減後金額）。設計書§2.5.11が明示するとおり導出値であり、
	/// <see cref="TranGenkaReval"/>には列を持たない。本DTOでは表示の便宜上、読み取り専用プロパティとして公開する。
	/// </summary>
	public long DiffAmount => BeforeAmount - AfterAmount;
}

/// <summary>
/// 評価替え一覧の明細行（原価4項目 詳細設計 §16.6.2）。
/// </summary>
public sealed class RevaluationDetailRow {
	/// <summary>商品Id。</summary>
	public long Id_Shohin { get; set; }
	/// <summary>商品コード。</summary>
	public string CodeShohin { get; set; } = string.Empty;
	/// <summary>商品名。</summary>
	public string MeiShohin { get; set; } = string.Empty;
	/// <summary>シーズン名。</summary>
	public string MeiSeason { get; set; } = string.Empty;
	/// <summary>ブランド名。</summary>
	public string MeiBrand { get; set; } = string.Empty;
	/// <summary>アイテム名。</summary>
	public string MeiItem { get; set; } = string.Empty;
	/// <summary>上代。</summary>
	public long Jodai { get; set; }
	/// <summary>対象計上月末の在庫数（設計書§16.5）。</summary>
	public long Qty { get; set; }
	/// <summary>計算前原価（対象計上月時点の解決原価）。</summary>
	public long BeforeCost { get; set; }
	/// <summary>計算後原価。対象外・エラー行は0。</summary>
	public long AfterCost { get; set; }
	/// <summary>在庫金額（BeforeCost × Qty）。</summary>
	public long BeforeAmount { get; set; }
	/// <summary>評価減後金額（AfterCost × Qty）。対象外・エラー行は0。</summary>
	public long AfterAmount { get; set; }
	/// <summary>対象商品か（設計書§16.5の条件1～6を全て満たすか）。</summary>
	public bool IsTarget { get; set; }
	/// <summary>
	/// 対象外の理由（在庫0／原価0／引き下げにならない）。<see cref="IsTarget"/>=falseかつ
	/// <see cref="Error"/>=Noneのときのみ設定する。対象外はエラーではない（設計書§16.9）。
	/// </summary>
	public string ExcludeReason { get; set; } = string.Empty;
	/// <summary>この行の計算エラー種別。<c>AfterCost&lt;=0</c>のときのみ設定する（設計書§16.9）。</summary>
	public EnumCostCalcError Error { get; set; }
	/// <summary>画面表示用のエラーメッセージ。</summary>
	public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// 評価替えの確認（プレビュー）結果全体（原価4項目 詳細設計 §16.6）。
/// </summary>
public sealed class RevaluationPreviewResult {
	/// <summary>集計行一覧（<c>GroupKey</c>で選択した軸ごと）。</summary>
	public IReadOnlyList<RevaluationSummaryRow> SummaryRows { get; set; } = [];
	/// <summary>明細行一覧（対象外・エラー行を含む）。</summary>
	public IReadOnlyList<RevaluationDetailRow> DetailRows { get; set; } = [];
	/// <summary>全体の合計行（設計書§16.6.1「最下部に全体の合計行を表示する」）。</summary>
	public RevaluationSummaryRow Total { get; set; } = new();
	/// <summary>エラー件数（<c>AfterCost&lt;=0</c>の行数。1件でもあれば更新不可、設計書§16.9）。</summary>
	public long ErrorCount { get; set; }
	/// <summary>画面上部に表示する情報メッセージ（例: データが存在しません、更新対象がありませんでした＋対象外内訳）。</summary>
	public IReadOnlyList<string> InfoMessages { get; set; } = [];
	/// <summary>
	/// 確認時点の対象商品Id→<c>MasterShohin.Vdu</c>。<see cref="CostRevaluationParameter.ConfirmedShohinVdu"/>へ
	/// そのまま渡すことで、更新実行時に確認後の変更を検知できる（設計書§2.4-4）。
	/// </summary>
	public IReadOnlyDictionary<long, long> ConfirmedShohinVdu { get; set; } = new Dictionary<long, long>();
	/// <summary>確認時点の自社締日。<see cref="CostRevaluationParameter.ConfirmedShimeBi"/>へそのまま渡す。</summary>
	public int ConfirmedShimeBi { get; set; }
	/// <summary>確認時点の<c>MasterSysman.CostMethod</c>。<see cref="CostRevaluationParameter.ConfirmedCostMethod"/>へそのまま渡す。</summary>
	public int ConfirmedCostMethod { get; set; }
}

/// <summary>
/// 評価替えの対象計上月が支払計算済み範囲に含まれるため、更新を中断したことを表す（原価4項目 詳細設計 §16.9、§4.6準拠）。
/// <see cref="ConsumptionPurchasePaidPeriodException"/>と同じ前例に倣う。
/// </summary>
public sealed class CostRevaluationPaidPeriodException(string targetMonth)
	: Exception($"対象月 {targetMonth} は支払計算済み範囲に含まれるため、評価替えを中断しました。支払計算を取り消してから再実行してください。") {
	/// <summary>対象計上月 yyyyMM。</summary>
	public string TargetMonth { get; } = targetMonth;
}

/// <summary>
/// 原価4処理の更新結果（原価4項目 詳細設計 §2.4、§10.2）。
/// </summary>
public sealed class CostUpdateResult {
	/// <summary>更新が成功したか。</summary>
	public bool IsSuccess { get; set; }
	/// <summary>更新実行Id。</summary>
	public string BatchId { get; set; } = string.Empty;
	/// <summary>対象計上月 yyyyMM。</summary>
	public string TargetMonth { get; set; } = string.Empty;
	/// <summary>更新件数。</summary>
	public long UpdatedCount { get; set; }
	/// <summary>エラー件数。</summary>
	public long ErrorCount { get; set; }
	/// <summary>画面表示用の結果メッセージ。</summary>
	public string Message { get; set; } = string.Empty;
	/// <summary>開始時刻(UTC Ticks)。</summary>
	public long StartedAt { get; set; }
	/// <summary>終了時刻(UTC Ticks)。</summary>
	public long FinishedAt { get; set; }
}
