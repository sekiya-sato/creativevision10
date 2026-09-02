using CommunityToolkit.Mvvm.ComponentModel;
using NPoco;

namespace CvBase;


/// <summary>
/// 現在庫集計ファイル: 在庫
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("unq1", true, [nameof(Id_Soko), nameof(Id_Shohin), nameof(Id_Col), nameof(Id_Siz)])]
[KeyDml("nk1", false, [nameof(Id_Soko)])]
[KeyDml("nk2", false, [nameof(Id_Shohin)])]
[Comment("集計データ：倉庫、商品、色、サイズで集計した在庫データ")]
public partial class SummaryRealStock : BaseDbClass {
	/// <summary>
	/// 倉庫ID
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterTokui), tenType: 0, additionalInfo: "TenType in (0,3,6)")]
	[Comment("倉庫ID")]
	public partial long Id_Soko { get; set; }
	/// <summary>
	/// 商品Id
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterShohin))]
	[Comment("商品Id")]
	public partial long Id_Shohin { get; set; }
	/// <summary>
	/// 色
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(DerivedShohinColSiz), additionalInfo: $"{nameof(DerivedShohinColSiz)}に存在する色")]
	[Comment("色")]
	public partial long Id_Col { get; set; }
	/// <summary>
	/// サイズ
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(DerivedShohinColSiz), additionalInfo: $"{nameof(DerivedShohinColSiz)}に存在するサイズ")]
	[Comment("サイズ")]
	public partial long Id_Siz { get; set; }
	/// <summary>
	/// 数量
	/// </summary>
	[ObservableProperty]
	[Comment("数量")]
	public partial int Su { get; set; }
	/// <summary>
	/// 引当数（振り分け予定数）。<see cref="TranHaibun"/> の <see cref="TranHaibun.EndFlag"/>=0 の <see cref="TranHaibun.Su"/> 合計。
	/// <para>
	/// 有効在庫 = <see cref="Su"/> - <see cref="ReserveQty"/>。この列は <see cref="TranHaibun"/> だけが源泉であり、
	/// Tran系伝票の在庫計算（<c>SummaryDb.CalcTran2SummaryStock()</c>）では変化しない。
	/// <see cref="SummaryRealStock"/> は全月合計、<see cref="SummaryStock"/> は <c>SumMonth</c> 単位の合計を保持する。
	/// 仕様は `Doc/spec/archive/2026-08-12_phase1_業務仕様決定ドラフト.md` 2.2 / 2.8 を参照する。
	/// </para>
	/// </summary>
	[ObservableProperty]
	[Comment("引当数（振り分け予定数）。TranHaibun の TranHaibun.EndFlag=0 の TranHaibun.Su 合計。 有効在庫 = Su - ReserveQty。")]
	public partial int ReserveQty { get; set; }
}
/// <summary>
/// 年月集計ファイル: 在庫
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("unq1", true, [nameof(SumMonth), nameof(Id_Soko), nameof(Id_Shohin), nameof(Id_Col), nameof(Id_Siz)])]
[KeyDml("nk1", false, [nameof(Id_Soko)])]
[KeyDml("nk2", false, [nameof(Id_Shohin)])]
[Comment("集計データ：yyyyMM年月、倉庫、商品、色、サイズで集計した在庫データ Suは当月のみ、CumulativeSuは累計")]
public partial class SummaryStock : SummaryRealStock {
	/// <summary>
	/// 年月
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(6)]
	[Comment("年月")]
	public partial string SumMonth { get; set; } = "190101";
	/// <summary>
	///	当月までの累計数量
	/// </summary>
	[ObservableProperty]
	[Comment("当月までの累計数量")]
	public partial int CumulativeSu { get; set; }
	/// <summary>
	/// 入庫数
	/// </summary>
	[ObservableProperty]
	[Comment("入庫数")]
	public partial int InQty { get; set; }
	/// <summary>
	/// 出庫数
	/// </summary>
	[ObservableProperty]
	[Comment("出庫数")]
	public partial int OutQty { get; set; }
	/// <summary>
	/// 移動中(入庫予定)
	/// </summary>
	[ObservableProperty]
	[Comment("移動中(入庫予定)")]
	public partial int TransitQty { get; set; }
	/// <summary>
	/// 調整数
	/// <para>
	/// 在庫調整伝票(<see cref="Tran61Chosei"/>)から積む。棚卸確定と在庫強制調整の増減がここに入る。
	/// 当月の在庫増減は <c>Su = InQty + OutQty + AdjustQty</c> の構成になる（仕様 8.4.1）。
	/// </para>
	/// </summary>
	[ObservableProperty]
	[Comment("調整数 在庫調整伝票(Tran61Chosei)から積む。Su = InQty + OutQty + AdjustQty")]
	public partial int AdjustQty { get; set; }
	/// <summary>
	/// 棚卸日
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[Comment("棚卸日")]
	public partial string StocktakeDdate { get; set; } = "19010101";
	/// <summary>
	/// 帳簿在庫（棚卸開始処理が保存する対象年月末時点のスナップショット）
	/// <para>
	/// 棚卸中に伝票が入っても差異表の「帳簿在庫数」が動かないよう凍結するための列。
	/// 棚卸確定処理は <c>ActualQty - BookQty</c> を調整数として調整伝票へ起こす（仕様 8.1 / F0'）。
	/// </para>
	/// </summary>
	[ObservableProperty]
	[Comment("帳簿在庫 棚卸開始処理が保存する対象年月末時点のスナップショット")]
	public partial int BookQty { get; set; }
	/// <summary>
	/// 棚卸数（実棚数。棚卸確定処理が <see cref="Tran60Tana"/> から集計して入れる）
	/// </summary>
	[ObservableProperty]
	[Comment("棚卸数 実棚数。棚卸確定処理がTran60Tanaから集計して入れる")]
	public partial int ActualQty { get; set; }
}

