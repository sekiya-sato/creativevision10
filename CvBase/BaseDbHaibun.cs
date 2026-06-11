using CommunityToolkit.Mvvm.ComponentModel;
using NPoco;

namespace CvBase;

// 配分トランザクション
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("nk1", false, "DenDay")]
[Comment("トランザクション：配分データ 倉庫からの移動指示：日付、配分CD、倉庫Id、[商品Id、色サイズ、予定数量、実数量、完了FLG]")]
public sealed partial class TranHaibun : BaseDbClass {
	/// <summary>
	/// 日付 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("配分指示日")]
	string denDay = "19010101";
	/// <summary>
	/// 納品日 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("納品日")]
	string nouhinDay = string.Empty;
	/// <summary>
	/// 倉庫Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("倉庫CD")]
	long id_Soko;
	/// <summary>
	/// 店舗Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("得意先CD")]
	long id_Tenpo;
	/// <summary>
	/// 区分
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("区分")]
	int kubun;
	/// <summary>
	/// 送信フラグ 0:未送信 1:送信中 2:送信済み
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("送信FLG")]
	int sendFlg;
	/// <summary>
	/// 商品ユニークキー
	/// </summary>
	[ObservableProperty]
	long id_Shohin;
	/// <summary>
	/// 入力JANコード
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("JANCODE")]
	string janCode = string.Empty;
	/// <summary>
	/// 色
	/// </summary>
	[ObservableProperty]
	long id_Col;
	/// <summary>
	/// サイズ
	/// </summary>
	[ObservableProperty]
	long id_Siz;
	/// <summary>
	/// 数量
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("数量")]
	int su;
	/// <summary>
	/// 単価
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("単価")]
	int tanka;
	/// <summary>
	/// 金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("金額")]
	int kingaku;
	/// <summary>
	/// 上代
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("上代金額")]
	int jodai;
	/// <summary>
	/// 下代
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("下代金額")]
	int gedai;
	/// <summary>
	///	関連No1
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("関連伝票NO")]
	int relateNo1;
	/// <summary>
	///	関連No2
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("関連伝票NO2")]
	int relateNo2;
	/// <summary>
	/// 明細メモ
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(200)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("明細メモ")]
	string memo = string.Empty;
	/// <summary>
	/// 確定日 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("確定日")]
	string kakuteiDay = string.Empty;
	/// <summary>
	/// 実数量
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("実数量")]
	int jitsuSu;
	/// <summary>
	/// 入力社員Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("入力社員CD")]
	long id_Shain;
}

// 補充トランザクション
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("nk1", false, "DenDay")]
[Comment("トランザクション：補充データ 仕入先への補充発注依頼：日付、補充CD、倉庫Id、[商品Id、色サイズ、予定数量、実数量、完了FLG]")]
public sealed partial class TranHoju : BaseDbClass {
	/// <summary>
	/// 日付 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("補充指示日")]
	string denDay = "19010101";
	/// <summary>
	/// 納品日 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("納品日")]
	string nouhinDay = string.Empty;
	/// <summary>
	/// 倉庫Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("倉庫CD")]
	long id_Soko;
	/// <summary>
	/// 店舗Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("仕入先CD")]
	long id_Shiire;
	/// <summary>
	/// 区分
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("区分")]
	int kubun;
	/// <summary>
	/// 送信フラグ 0:未送信 1:送信中 2:送信済み
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("送信FLG")]
	int sendFlg;
	/// <summary>
	/// 商品ユニークキー
	/// </summary>
	[ObservableProperty]
	long id_Shohin;
	/// <summary>
	/// 入力JANコード
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("JANCODE")]
	string janCode = string.Empty;
	/// <summary>
	/// 色
	/// </summary>
	[ObservableProperty]
	long id_Col;
	/// <summary>
	/// サイズ
	/// </summary>
	[ObservableProperty]
	long id_Siz;
	/// <summary>
	/// 数量
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("数量")]
	int su;
	/// <summary>
	/// 単価
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("単価")]
	int tanka;
	/// <summary>
	/// 金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("金額")]
	int kingaku;
	/// <summary>
	/// 上代
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("上代金額")]
	int jodai;
	/// <summary>
	/// 下代
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("下代金額")]
	int gedai;
	/// <summary>
	///	関連No1
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("関連伝票NO")]
	int relateNo1;
	/// <summary>
	///	関連No2
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("関連伝票NO2")]
	int relateNo2;
	/// <summary>
	/// 明細メモ
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(200)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("明細メモ")]
	string memo = string.Empty;
	/// <summary>
	/// 確定日 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("確定日")]
	string kakuteiDay = string.Empty;
	/// <summary>
	/// 実数量
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("実数量")]
	int jitsuSu;
	/// <summary>
	/// 入力社員Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("入力社員CD")]
	long id_Shain;
}
