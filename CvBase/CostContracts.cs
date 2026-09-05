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
