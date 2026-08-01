using CommunityToolkit.Mvvm.ComponentModel;
using CvBase.Share;
using Newtonsoft.Json;
using NPoco;

namespace CvBase;

/// <summary>
/// POS日次精算（精算確定の履歴。同一店舗・同一日で SeisanCnt をインクリメントして履歴化する）
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("nk1", false, [nameof(DenDay), nameof(Id_Tenpo)])]
[Comment("トランザクション：POS日次精算データ（金種枚数・集計スナップショット）")]
public sealed partial class Tran02PosSeisan : BaseDbClass {
	/// <summary>
	/// 営業日（yyyyMMdd）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	public partial string DenDay { get; set; } = "19010101";
	/// <summary>
	/// 店舗キー
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterTokui), tenType: 6)]
	public partial long Id_Tenpo { get; set; }
	/// <summary>
	/// 店舗データ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	public partial CodeNameView VTenpo { get; set; } = new();
	/// <summary>
	/// 社員ユニークキー（精算担当）
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterShain))]
	public partial long Id_Shain { get; set; }
	/// <summary>
	/// 社員データ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	public partial CodeNameView VShain { get; set; } = new();
	/// <summary>
	/// 精算回数（同一営業日・店舗で連番）
	/// </summary>
	[ObservableProperty]
	public partial int SeisanCnt { get; set; }
	/// <summary>
	/// 来店客数
	/// </summary>
	[ObservableProperty]
	public partial int KyakuSu { get; set; }
	/// <summary>1万円札枚数</summary>
	[ObservableProperty]
	public partial int Mai10000 { get; set; }
	/// <summary>5千円札枚数</summary>
	[ObservableProperty]
	public partial int Mai5000 { get; set; }
	/// <summary>2千円札枚数</summary>
	[ObservableProperty]
	public partial int Mai2000 { get; set; }
	/// <summary>千円札枚数</summary>
	[ObservableProperty]
	public partial int Mai1000 { get; set; }
	/// <summary>500円玉枚数</summary>
	[ObservableProperty]
	public partial int Mai500 { get; set; }
	/// <summary>100円玉枚数</summary>
	[ObservableProperty]
	public partial int Mai100 { get; set; }
	/// <summary>50円玉枚数</summary>
	[ObservableProperty]
	public partial int Mai50 { get; set; }
	/// <summary>10円玉枚数</summary>
	[ObservableProperty]
	public partial int Mai10 { get; set; }
	/// <summary>5円玉枚数</summary>
	[ObservableProperty]
	public partial int Mai5 { get; set; }
	/// <summary>1円玉枚数</summary>
	[ObservableProperty]
	public partial int Mai1 { get; set; }
	/// <summary>釣銭準備金</summary>
	[ObservableProperty]
	public partial int JunbiAmount { get; set; }
	/// <summary>現金残（金種合計）</summary>
	[ObservableProperty]
	public partial int RealAmount { get; set; }
	/// <summary>計算残（準備金＋現金売上）</summary>
	[ObservableProperty]
	public partial int CalcAmount { get; set; }
	/// <summary>差異（現金残－計算残）</summary>
	[ObservableProperty]
	public partial int AmountDiff { get; set; }
	/// <summary>集計スナップショット（精算時点の売上集計）</summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(2000)]
	public partial PosSeisanSummary? Jsummary { get; set; }
	/// <summary>
	/// メモ
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(200)]
	public partial string Memo { get; set; } = string.Empty;
}

/// <summary>精算時点の売上集計スナップショット（JSON 列 Jsummary に格納）</summary>
public sealed class PosSeisanSummary {
	public int TotalAmount { get; init; }
	public int CashAmount { get; init; }
	public int CardAmount { get; init; }
	public int OtherAmount { get; init; }
	public int TransactionCount { get; init; }
	public int ReturnCount { get; init; }
	public int TotalQuantity { get; init; }
}
