using CommunityToolkit.Mvvm.ComponentModel;
using NPoco;

namespace CvBase;

// 配分トランザクション
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("nk1", false, nameof(DenDay))]
[Comment("トランザクション：配分データ 倉庫からの移動指示：日付、配分CD、倉庫Id、[商品Id、色サイズ、予定数量、実数量、完了FLG]")]
public sealed partial class TranHaibun : BaseDbClass {
	/// <summary>
	/// 日付 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("配分指示日")]
	public partial string DenDay { get; set; } = "19010101";
	/// <summary>
	/// 納品日 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("納品日")]
	public partial string NouhinDay { get; set; } = string.Empty;
	/// <summary>
	/// 倉庫Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("倉庫CD")]
	public partial long Id_Soko { get; set; }
	/// <summary>
	/// 店舗Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("得意先CD")]
	public partial long Id_Tenpo { get; set; }
	/// <summary>
	/// 区分
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("区分")]
	public partial int Kubun { get; set; }
	/// <summary>
	/// 送信フラグ 0:未送信 1:送信中 2:送信済み
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("送信FLG")]
	public partial int SendFlg { get; set; }
	/// <summary>
	/// 商品ユニークキー
	/// </summary>
	[ObservableProperty]
	public partial long Id_Shohin { get; set; }
	/// <summary>
	/// 入力JANコード
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	[OldTableCommentAttr("JANCODE")]
	public partial string JanCode { get; set; } = string.Empty;
	/// <summary>
	/// 色
	/// </summary>
	[ObservableProperty]
	public partial long Id_Col { get; set; }
	/// <summary>
	/// サイズ
	/// </summary>
	[ObservableProperty]
	public partial long Id_Siz { get; set; }
	/// <summary>
	/// 数量
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("数量")]
	public partial int Su { get; set; }
	/// <summary>
	/// 単価
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("単価")]
	public partial int Tanka { get; set; }
	/// <summary>
	/// 金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("金額")]
	public partial int Kingaku { get; set; }
	/// <summary>
	/// 上代
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("上代金額")]
	public partial int Jodai { get; set; }
	/// <summary>
	/// 下代
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("下代金額")]
	public partial int Gedai { get; set; }
	/// <summary>
	///	関連No1
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("関連伝票NO")]
	public partial int RelateNo1 { get; set; }
	/// <summary>
	///	関連No2
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("関連伝票NO2")]
	public partial int RelateNo2 { get; set; }
	/// <summary>
	/// 明細メモ
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(200)]
	[OldTableCommentAttr("明細メモ")]
	public partial string Memo { get; set; } = string.Empty;
	/// <summary>
	/// 確定日 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("確定日")]
	public partial string KakuteiDay { get; set; } = string.Empty;
	/// <summary>
	/// 実数量
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("実数量")]
	public partial int JitsuSu { get; set; }
	/// <summary>
	/// 入力社員Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("入力社員CD")]
	public partial long Id_Shain { get; set; }
}

// 補充トランザクション
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("nk1", false, nameof(DenDay))]
[Comment("トランザクション：補充データ 仕入先への補充発注依頼：日付、補充CD、倉庫Id、[商品Id、色サイズ、予定数量、実数量、完了FLG]")]
public sealed partial class TranHoju : BaseDbClass {
	/// <summary>
	/// 日付 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("補充指示日")]
	public partial string DenDay { get; set; } = "19010101";
	/// <summary>
	/// 納品日 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("納品日")]
	public partial string NouhinDay { get; set; } = string.Empty;
	/// <summary>
	/// 倉庫Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("倉庫CD")]
	public partial long Id_Soko { get; set; }
	/// <summary>
	/// 店舗Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("仕入先CD")]
	public partial long Id_Shiire { get; set; }
	/// <summary>
	/// 区分
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("区分")]
	public partial int Kubun { get; set; }
	/// <summary>
	/// 送信フラグ 0:未送信 1:送信中 2:送信済み
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("送信FLG")]
	public partial int SendFlg { get; set; }
	/// <summary>
	/// 商品ユニークキー
	/// </summary>
	[ObservableProperty]
	public partial long Id_Shohin { get; set; }
	/// <summary>
	/// 入力JANコード
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	[OldTableCommentAttr("JANCODE")]
	public partial string JanCode { get; set; } = string.Empty;
	/// <summary>
	/// 色
	/// </summary>
	[ObservableProperty]
	public partial long Id_Col { get; set; }
	/// <summary>
	/// サイズ
	/// </summary>
	[ObservableProperty]
	public partial long Id_Siz { get; set; }
	/// <summary>
	/// 数量
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("数量")]
	public partial int Su { get; set; }
	/// <summary>
	/// 単価
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("単価")]
	public partial int Tanka { get; set; }
	/// <summary>
	/// 金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("金額")]
	public partial int Kingaku { get; set; }
	/// <summary>
	/// 上代
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("上代金額")]
	public partial int Jodai { get; set; }
	/// <summary>
	/// 下代
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("下代金額")]
	public partial int Gedai { get; set; }
	/// <summary>
	///	関連No1
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("関連伝票NO")]
	public partial int RelateNo1 { get; set; }
	/// <summary>
	///	関連No2
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("関連伝票NO2")]
	public partial int RelateNo2 { get; set; }
	/// <summary>
	/// 明細メモ
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(200)]
	[OldTableCommentAttr("明細メモ")]
	public partial string Memo { get; set; } = string.Empty;
	/// <summary>
	/// 確定日 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("確定日")]
	public partial string KakuteiDay { get; set; } = string.Empty;
	/// <summary>
	/// 実数量
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("実数量")]
	public partial int JitsuSu { get; set; }
	/// <summary>
	/// 入力社員Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("入力社員CD")]
	public partial long Id_Shain { get; set; }
}
