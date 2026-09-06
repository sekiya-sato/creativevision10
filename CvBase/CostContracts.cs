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
	/// <summary>
	/// 確認(Preview)結果が返した<see cref="CostConfirmSnapshot"/>をそのまま渡す（原価4項目 詳細設計 §2.4-4、
	/// 2026-09-06追記）。更新実行時に現在の指紋と照合し、不一致（確認後にデータが変更された）なら更新を
	/// 中断する。<c>null</c>の場合はこの再検査を行わない（既存の「省略時は検査しない」性質を維持する）。
	/// </summary>
	public CostConfirmSnapshot? Confirmed { get; set; }
}

/// <summary>
/// 原価4処理（消化仕入更新・最終仕入原価更新・総平均原価更新・評価替え）共通の「確認後の変更検知」用
/// 指紋（原価4項目 詳細設計 §2.4-4、§2.5.6、2026-09-06追記でStep 9として4処理へ統一）。
/// <para>
/// 商品Idごとの辞書（評価替えのStep 8時点の実装、<c>ConfirmedShohinVdu</c>）ではなく、
/// §2.5.6が月次状態の判定で既に採用している「入力データの最大<c>Vdu</c>と件数を組み合わせた指紋」を
/// 4処理共通の方式として採用する。4処理は入力データのテーブルが異なる（消化仕入は売上、
/// 原価更新は仕入・諸掛・在庫、評価替えは商品マスタ・在庫）ため、商品Idの辞書では伝票側だけの変更
/// （例: 対象商品を変えない伝票の追加・削除）を検知できない。§2.5.6は「削除だけが起きた場合は
/// 最大Vduが前進しないため、件数を見ないと検出できない」と指摘しており、同じ考え方をそのまま使う。
/// 対象が数万件でも指紋は2つの数値で済むため、確認〜更新間で往復するデータ量が対象件数に依存せず一定になる。
/// </para>
/// </summary>
public sealed class CostConfirmSnapshot {
	/// <summary>確認時点の入力データの最大<c>Vdu</c>。<c>0</c>は「対象データなし」を意味する。</summary>
	public long SourceMaxVdu { get; set; }
	/// <summary>確認時点の入力データの件数。最大<c>Vdu</c>が前進しない削除だけの変更を検知するために使う。</summary>
	public long SourceCount { get; set; }
	/// <summary>確認時点の自社締日（<c>MasterSysman.ShimeBi</c>）。</summary>
	public int ShimeBi { get; set; }
	/// <summary>確認時点の<c>MasterSysman.CostMethod</c>。</summary>
	public int CostMethod { get; set; }
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
/// 最終仕入原価更新・総平均原価更新の確認（プレビュー）結果全体（原価4項目 詳細設計 §8.4・§8.5、§2.4-4）。
/// 両処理で列構成が共通するため1つのDTOで共有する（<see cref="CostPreviewRow"/>と同じ理由）。
/// </summary>
public sealed class CostPreviewResult {
	/// <summary>プレビュー一覧行。</summary>
	public IReadOnlyList<CostPreviewRow> Rows { get; set; } = [];
	/// <summary>
	/// 確認時点の指紋（設計書§2.4-4）。<see cref="CostUpdateParameter.Confirmed"/>へそのまま渡すことで、
	/// 更新実行時に確認後の変更を検知できる。
	/// </summary>
	public CostConfirmSnapshot Confirmed { get; set; } = new();
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
	/// <summary>
	/// 対象商品か（設計書§6.5「2026-09-06改訂」）。総平均原価更新のみ<see langword="false"/>になりうる
	/// （負在庫／前月在庫があるが原価0円）。<see cref="RevaluationDetailRow.IsTarget"/>と同じ名前・同じ意味。
	/// 最終仕入原価更新は§6.5の対象外の対象ではないため常に既定値<see langword="true"/>のまま。
	/// </summary>
	public bool IsTarget { get; set; } = true;
	/// <summary>
	/// 対象外の理由（前月在庫が負／前月在庫があるが原価が未設定）。<see cref="IsTarget"/>=falseの
	/// ときのみ設定する。<see cref="RevaluationDetailRow.ExcludeReason"/>と同じ名前・同じ意味。
	/// 対象外はエラーではない（設計書§6.5）。
	/// </summary>
	public string ExcludeReason { get; set; } = string.Empty;
}

/// <summary>
/// 消化仕入更新の確認（プレビュー）結果全体（原価4項目 詳細設計 §8.3、§2.4-4）。
/// </summary>
public sealed class ConsumptionPreviewResult {
	/// <summary>プレビュー一覧行。</summary>
	public IReadOnlyList<ConsumptionPreviewRow> Rows { get; set; } = [];
	/// <summary>
	/// 確認時点の指紋（設計書§2.4-4）。<see cref="CostUpdateParameter.Confirmed"/>へそのまま渡すことで、
	/// 更新実行時に確認後の変更を検知できる。
	/// </summary>
	public CostConfirmSnapshot Confirmed { get; set; } = new();
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
	/// 確認時点の指紋（設計書§2.4-4）。Step 8時点では商品Id→<c>Vdu</c>の辞書
	/// （<c>ConfirmedShohinVdu</c>）＋締日＋原価方式の3項目だったが、Step 9で他の3処理と同じ
	/// <see cref="CostConfirmSnapshot"/>（入力データの最大<c>Vdu</c>＋件数の指紋）へ統一した。
	/// <see cref="CostRevaluationParameter.Confirmed"/>へそのまま渡す。
	/// </summary>
	public CostConfirmSnapshot Confirmed { get; set; } = new();
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
