using CommunityToolkit.Mvvm.ComponentModel;
using CvBase.Share;
using Newtonsoft.Json;
using NPoco;

namespace CvBase;


public interface ITranDetail {
	public string DenDay { get; set; }
	public long Id_Soko { get; set; }
	public int CalcFlag { get; set; }
	public List<Tran99Meisai>? Jmeisai { get; set; }
}
public interface ITranIdo {
	public long Id { get; set; }
	public string DenDay { get; set; }
	public long Id_Ido { get; set; }
	public int CalcFlag { get; set; }
}
public interface ITranSoko {
	public long Id { get; set; }
	public string DenDay { get; set; }
	public long Id_Soko { get; set; }
	public int CalcFlag { get; set; }
}




/// <summary>
/// Tran系ファイルの出庫・入庫の区分、売上・仕入の区分などの共通的なコードを定義するクラス
/// </summary>
public class TranCalcBase {
	/// <summary>
	/// 在庫、入庫、出庫、移動中のフラグを取得する
	/// </summary>
	/// <param name="tableName"></param>
	/// <returns></returns>
	public static Tuple<int, int, int, int> GetCalcSoko(string tableName, bool invertFlg = false) {
		var ret = new Tuple<int, int, int, int>(0, 0, 0, 0);
		if (tableName == nameof(Tran00Uriage)) {
			ret = new Tuple<int, int, int, int>(-1, 0, 1, 0);
		}
		else if (tableName == nameof(Tran01Tenuri)) {
			ret = new Tuple<int, int, int, int>(-1, 0, 1, 0);
		}
		else if (tableName == nameof(Tran03Shiire)) {
			ret = new Tuple<int, int, int, int>(1, 1, 0, 0);
		}
		else if (tableName == nameof(Tran05Ido)) {
			ret = new Tuple<int, int, int, int>(-1, 0, 1, 0);
		}
		else if (tableName == nameof(Tran10IdoOut)) {
			ret = new Tuple<int, int, int, int>(-1, 0, 1, 0);
		}
		else if (tableName == nameof(Tran11IdoIn)) {
			ret = new Tuple<int, int, int, int>(0, 0, 0, 0);
		}
		// Tran12Jyuchu Tran13Hachu Tran60Tana
		if (invertFlg) {
			var inverted = new Tuple<int, int, int, int>(
				ret.Item1 * -1,
				ret.Item2 * -1,
				ret.Item3 * -1,
				ret.Item4 * -1
			);
			ret = inverted;
		}
		return ret;
	}

	/// <summary>
	/// 移動先基準で在庫計算のためのフラグを取得する。移動中は移動先の在庫に予定として割り当てる
	/// </summary>
	/// <param name="tableName"></param>
	/// <returns></returns>
	public static Tuple<int, int, int, int> GetCalcIdosaki(string tableName, bool invertFlg = false) {
		var ret = new Tuple<int, int, int, int>(0, 0, 0, 0);
		if (tableName == nameof(Tran05Ido)) {
			ret = new Tuple<int, int, int, int>(1, 1, 0, 0);
		}
		else if (tableName == nameof(Tran10IdoOut)) {
			ret = new Tuple<int, int, int, int>(0, 0, 0, 1);
		}
		else if (tableName == nameof(Tran11IdoIn)) {
			ret = new Tuple<int, int, int, int>(1, 1, 0, -1);
		}
		if (invertFlg) {
			var inverted = new Tuple<int, int, int, int>(
				ret.Item1 * -1,
				ret.Item2 * -1,
				ret.Item3 * -1,
				ret.Item4 * -1
			);
			ret = inverted;
		}
		return ret;
	}

}

/// <summary>
/// 共通トランザクション（ヘッダ）
/// </summary>
public partial class TranAllHeader : BaseDbClass, ITranDetail {
	/// <summary>
	/// 計上日（yyyyMMdd）
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[OldTableCommentAttr("在庫計上日")]
	string denDay = "19010101";
	/// <summary>
	/// 社員ユニークキー
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("入力社員CD")]
	long id_Shain;
	/// <summary>
	/// 社員データ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vShain = new();
	/// <summary>
	/// 倉庫キー
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("倉庫CD")]
	long id_Soko;
	/// <summary>
	/// 倉庫データ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vSoko = new();
	/// <summary>
	/// 計算フラグ（1:+ -1:-, 0:計算除外）
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("取引区分", "getCalcFlag により算出")]
	int calcFlag = 1;
	/// <summary>
	/// 数量合計
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("数量合計")]
	int suTotal;
	/// <summary>
	/// 金額合計
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("明細金額合計")]
	int kingakuTotal;
	/// <summary>
	/// 上代合計
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("上代合計")]
	int jodaiTotal;
	/// <summary>
	/// 下代合計
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("下代合計")]
	int gedaiTotal;
	/// <summary>
	/// 値引: 合計からの
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("値引1 + 値引2 + 値引3")]
	int nebiki00Total;
	/// <summary>
	/// 値引: 明細積上げ
	/// </summary>
	[ObservableProperty]
	int nebiki01Meisai;
	/// <summary>
	/// ヘッダメモ
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(200)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("メモ")]
	string memo = string.Empty;
	/// <summary>
	/// 詳細内容
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(1000)]
	BaseDetailClass? jdetail;
	/// <summary>
	/// 明細リスト
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(4000)]
	List<Tran99Meisai>? jmeisai;
}

/// <summary>
/// 共通トランザクション（明細）
/// </summary>
[OldTableCommentAttr("HC$tran_tori1", "Tran60Tana は HC$tran_tana1")]
public sealed partial class Tran99Meisai : ObservableObject {
	/// <summary>
	/// 行No
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("行NO")]
	int no;
	/// <summary>
	/// 区分（2桁 10-19,20-29,30,99）
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("明細取引区分")]
	int kubun = 10;
	/// <summary>
	/// 商品ユニークキー
	/// </summary>
	[ObservableProperty]
	long id_Shohin;
	/// <summary>
	/// 商品CD
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("商品CD")]
	string code_Shohin = string.Empty;
	/// <summary>
	/// 商品名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("明細名称")]
	string mei_Shohin = string.Empty;
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
	/// カラーCD
	/// </summary>
	[ObservableProperty]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("色CD")]
	string code_Col = string.Empty;
	/// <summary>
	/// カラー名
	/// </summary>
	[ObservableProperty]
	[property: System.ComponentModel.DefaultValue("")]
	string mei_Col = string.Empty;
	/// <summary>
	/// サイズ
	/// </summary>
	[ObservableProperty]
	long id_Siz;
	/// <summary>
	/// サイズCD
	/// </summary>
	[ObservableProperty]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("サイズCD")]
	string code_Siz = string.Empty;
	/// <summary>
	/// サイズ名
	/// </summary>
	[ObservableProperty]
	[property: System.ComponentModel.DefaultValue("")]
	string mei_Siz = string.Empty;
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
	/// 値引: 合計からの
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("明細値引")]
	int nebiki00;
	/// <summary>
	/// 値引: 明細1
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("明細値引1")]
	int nebiki01;
	/// <summary>
	/// 値引: 明細2
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("小計値引 + 小計値引1")]
	int nebiki02;
	/// <summary>
	/// 社員ユニークキー
	/// </summary>
	[ObservableProperty]
	long id_Shain;
	/// <summary>
	/// 社員CD
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	string code_Shain = string.Empty;
	/// <summary>
	/// 社員名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: System.ComponentModel.DefaultValue("")]
	string mei_Shain = string.Empty;
	/// <summary>
	/// 明細メモ
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(200)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("明細メモ")]
	string memo = string.Empty;
}

/// <summary>
/// 共通トランザクション（入金/支払ヘッダ）
/// </summary>
public partial class TranKinHeader : BaseDbClass {
	/// <summary>
	/// 計上日（yyyyMMdd）
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[OldTableCommentAttr("在庫計上日")]
	string denDay = "19010101";
	/// <summary>
	/// 社員ユニークキー
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("入力社員CD")]
	long id_Shain;
	/// <summary>
	/// 社員データ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vShain = new();
	/// <summary>
	/// 取引先キー
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("取引先CD1")]
	long id_Torisaki;
	/// <summary>
	/// 取引先データ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vTori = new();
	/// <summary>
	/// 金額合計
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("明細金額合計")]
	int kingakuTotal;
	/// <summary>
	/// 手入力No
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("手入力伝票NO")]
	string manualNo = string.Empty;
	/// <summary>
	/// ヘッダメモ
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(200)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("メモ")]
	string memo = string.Empty;
	/// <summary>
	/// 明細リスト
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(4000)]
	List<TranKinMeisai>? jmeisai;
}

/// <summary>
/// 入金・支払トランザクション（明細）
/// </summary>
[OldTableCommentAttr("HC$tran_tori1")]
public sealed partial class TranKinMeisai : ObservableObject {
	/// <summary>
	/// 行No
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("行NO")]
	int no;
	/// <summary>
	/// 区分ユニークキー
	/// </summary>
	[ObservableProperty]
	long id_Kin;
	/// <summary>
	/// 入金・支払CD
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("明細取引区分")]
	string code_Kin = string.Empty;
	/// <summary>
	/// 品名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: System.ComponentModel.DefaultValue("")]
	string mei_Kin = string.Empty;
	/// <summary>
	/// 金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("金額")]
	int kingaku;
	/// <summary>
	/// 明細メモ
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(200)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("明細メモ")]
	string memo = string.Empty;
}

/// <summary>
/// 入金 06 (取引先 売掛-)
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("nk1", false, "DenDay")]
[KeyDml("nk2", false, ["Id_Torisaki"])]
[Comment("トランザクション：入金データ 売掛に対する入金")]
[OldTableCommentAttr("HC$tran_tori0")]
public sealed partial class Tran06Nyukin : TranKinHeader {
}
/// <summary>
/// 支払 07 (取引先 買掛-)
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("nk1", false, "DenDay")]
[KeyDml("nk2", false, ["Id_Torisaki"])]
[Comment("トランザクション：支払データ 買掛に対する支払")]
[OldTableCommentAttr("HC$tran_tori0")]
public sealed partial class Tran07Shiharai : TranKinHeader {
}

/// <summary>
/// 棚卸 60 (倉庫 現在値)
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("nk1", false, "DenDay")]
[KeyDml("nk2", false, ["Id_Soko"])]
[Comment("トランザクション：棚卸データ 月末あるいは特定日の倉庫現在値")]
[OldTableCommentAttr("HC$tran_tana0")]
public sealed partial class Tran60Tana : TranAllHeader {
	/// <summary>
	/// 棚番
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	string tanaNo = string.Empty;
}

/// <summary>
/// 本部売上 00 (倉庫 出)
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("nk1", false, "DenDay")]
[KeyDml("nk2", false, "KakeDay")]
[KeyDml("nk3", false, ["Id_Soko"])]
[KeyDml("nk4", false, ["Id_Tokui"])]
[Comment("トランザクション：本部売上データ 得意先に対する売掛計上と倉庫からの出庫")]
[OldTableCommentAttr("HC$tran_tori0")]
public sealed partial class Tran00Uriage : TranAllHeader, ITranSoko {
	/// <summary>
	/// 掛計上日（yyyyMMdd）
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[OldTableCommentAttr("掛計上日")]
	string kakeDay = "19010101";
	/// <summary>
	/// 得意先キー
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("取引先CD1")]
	long id_Tokui;
	/// <summary>
	/// 得意先データ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vTokui = new();
	/// <summary>
	/// 請求フラグ
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnIsPay))]
	[OldTableCommentAttr("掛計上FLG")]
	int isPay;

	[Ignore]
	[JsonIgnore]
	public EnumYesNo EnIsPay {
		get => (EnumYesNo)IsPay;
		set => IsPay = (int)value;
	}
	/// <summary>
	/// 区分（2桁 10-19,20-29,30,99）
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnKubun))]
	[OldTableCommentAttr("取引区分")]
	int kubun = 10;
	[Ignore]
	[JsonIgnore]
	public EnumUri00 EnKubun {
		get => (EnumUri00)Kubun;
		set => Kubun = (int)value;
	}
	/// <summary>
	/// 手入力No
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("手入力伝票NO")]
	string manualNo = string.Empty;
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
	/// 掛率
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("掛率1")]
	int rate;
}

public enum EnumUri00 : int {
	Uriage = 10,
	UriSale = 11,
	Henpin = 20,
	HenSale = 21,
	Nebiki = 30,
	Other = 99
}


/// <summary>
/// 店舗売上 01 (倉庫 出)
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("nk1", false, "DenDay")]
[KeyDml("nk2", false, ["Id_Soko"])]
[KeyDml("nk3", false, ["Id_Tenpo"])]
[KeyDml("nk4", false, "Id_Customer")]
[Comment("トランザクション：店舗売上データ 店舗に対する売上と店舗(倉庫)からの出庫")]
[OldTableCommentAttr("HC$tran_tori0")]
public sealed partial class Tran01Tenuri : TranAllHeader, ITranSoko {
	/// <summary>
	/// 店舗キー
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("取引先CD1")]
	long id_Tenpo;
	/// <summary>
	/// 店舗データ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vTenpo = new();
	/// <summary>
	/// 顧客キー
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("顧客TEL")]
	long id_Customer;
	/// <summary>
	/// 顧客データ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vCustomer = new();
	/// <summary>
	/// オフライン用顧客CD
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("顧客TEL")]
	string code_Customer = string.Empty;
	/// <summary>
	/// 区分（2桁 10-19,20-29,30,99）
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnKubun))]
	[OldTableCommentAttr("取引区分")]
	int kubun = 10;
	[Ignore]
	[JsonIgnore]
	public EnumUri01 EnKubun {
		get => (EnumUri01)Kubun;
		set => Kubun = (int)value;
	}
	/// <summary>
	///	関連No1
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("関連伝票NO")]
	int relateNo1;
	/// <summary>
	/// 掛率
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("掛率1")]
	int rate;

}
public enum EnumUri01 : int {
	Uriage = 10,
	UriSale = 11,
	Henpin = 20,
	HenSale = 21,
	Other = 99
}


/// <summary>
/// 仕入 03 (倉庫 入)
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("nk1", false, "DenDay")]
[KeyDml("nk2", false, "KakeDay")]
[KeyDml("nk3", false, ["Id_Soko"])]
[KeyDml("nk4", false, ["Id_Shiire"])]
[Comment("トランザクション：仕入データ 仕入先に対する買掛計上と倉庫への入庫")]
[OldTableCommentAttr("HC$tran_tori0")]
public sealed partial class Tran03Shiire : TranAllHeader, ITranSoko {
	/// <summary>
	/// 掛計上日（yyyyMMdd）
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[OldTableCommentAttr("掛計上日")]
	string kakeDay = "19010101";
	/// <summary>
	/// 仕入先キー
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("取引先CD1")]
	long id_Shiire;
	/// <summary>
	/// 仕入先データ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vShiire = new();
	/// <summary>
	/// 支払フラグ
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnIsPay))]
	[OldTableCommentAttr("掛計上FLG")]
	int isPay;

	[Ignore]
	[JsonIgnore]
	public EnumYesNo EnIsPay {
		get => (EnumYesNo)IsPay;
		set => IsPay = (int)value;
	}
	/// <summary>
	/// 区分（2桁 10-19,20-29,30,99）
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnKubun))]
	[OldTableCommentAttr("取引区分")]
	int kubun = 10;
	[Ignore]
	[JsonIgnore]
	public EnumShiire EnKubun {
		get => (EnumShiire)Kubun;
		set => Kubun = (int)value;
	}
	/// <summary>
	/// 手入力No
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("手入力伝票NO")]
	string manualNo = string.Empty;
	/// <summary>
	///	関連No1
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("関連伝票NO")]
	int relateNo1;
	/// <summary>
	/// 掛率
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("掛率1")]
	int rate;
}
public enum EnumShiire : int {
	Shiire = 10,
	Henpin = 20,
	Nebiki = 30,
	Other = 99
}


/// <summary>
/// 移動 05 (倉庫 出, 移動先 入)
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("nk1", false, "DenDay")]
[KeyDml("nk2", false, ["Id_Soko"])]
[KeyDml("nk3", false, ["Id_Ido"])]
[Comment("トランザクション：移動データ(即時) 倉庫からの出庫と移動先への入庫")]
[OldTableCommentAttr("HC$tran_tori0")]
public sealed partial class Tran05Ido : TranAllHeader, ITranIdo, ITranSoko {
	/// <summary>
	/// 移動先キー
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("取引先CD1")]
	long id_Ido;
	/// <summary>
	/// 移動先データ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vIdo = new();
	/// <summary>
	///	関連No1
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("関連伝票NO")]
	int relateNo1;
	/// <summary>
	/// 手入力No
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("手入力伝票NO")]
	string manualNo = string.Empty;
}

/// <summary>
/// 積送移動 10 (倉庫 出, 移動先 入) 仮
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("nk1", false, "DenDay")]
[KeyDml("nk2", false, ["Id_Soko"])]
[KeyDml("nk3", false, ["Id_Ido"])]
[Comment("トランザクション：移動データ(積送出庫) 倉庫からの出庫、積送中在庫へ(移動先への入庫予定)")]
[OldTableCommentAttr("HC$tran_tori0")]
public sealed partial class Tran10IdoOut : TranAllHeader, ITranIdo, ITranSoko {
	/// <summary>
	/// 移動先キー
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("取引先CD1")]
	long id_Ido;
	/// <summary>
	/// 移動先データ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vIdo = new();
	/// <summary>
	///	関連No1
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("関連伝票NO")]
	int relateNo1;
	/// <summary>
	/// 手入力No
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("手入力伝票NO")]
	string manualNo = string.Empty;
}
/// <summary>
/// 積送移動 11 (倉庫 出, 移動先 入) 実
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("nk1", false, "DenDay")]
[KeyDml("nk2", false, ["Id_Soko"])]
[KeyDml("nk3", false, ["Id_Ido"])]
[Comment("トランザクション：移動データ(積送入庫) 積送中在庫(倉庫からの出庫)から移動先への入庫")]
[OldTableCommentAttr("HC$tran_tori0")]
public sealed partial class Tran11IdoIn : TranAllHeader, ITranIdo, ITranSoko {
	/// <summary>
	/// 移動先キー
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("取引先CD1")]
	long id_Ido;
	/// <summary>
	/// 移動先データ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vIdo = new();
	/// <summary>
	///	関連No1
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("関連伝票NO")]
	int relateNo1;
	/// <summary>
	/// 手入力No
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("手入力伝票NO")]
	string manualNo = string.Empty;
}
/// <summary>
/// 受注 12
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("nk1", false, "DenDay")]
[KeyDml("nk3", false, ["Id_Soko"])]
[KeyDml("nk4", false, ["Id_Tokui"])]
[Comment("トランザクション：受注データ 得意先に対する受注、本部売上になる場合は、本部売上データのRelateNo1に受注データのIdをセット")]
[OldTableCommentAttr("HC$tran_tori0")]
public sealed partial class Tran12Jyuchu : TranAllHeader {
	/// <summary>
	/// 得意先キー
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("取引先CD1")]
	long id_Tokui;
	/// <summary>
	/// 得意先データ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vTokui = new();
	/// <summary>
	/// 区分（2桁 10-19,20-29,30,99）
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnKubun))]
	[OldTableCommentAttr("取引区分")]
	int kubun = 10;
	[Ignore]
	[JsonIgnore]
	public EnumUri01 EnKubun {
		get => (EnumUri01)Kubun;
		set => Kubun = (int)value;
	}
	/// <summary>
	///	関連No1
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("関連伝票NO")]
	int relateNo1;
	/// <summary>
	/// 掛率
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("掛率1")]
	int rate;
}

/// <summary>
/// 発注 13
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("nk1", false, "DenDay")]
[KeyDml("nk3", false, ["Id_Soko"])]
[KeyDml("nk4", false, ["Id_Shiire"])]
[Comment("トランザクション：発注データ 仕入先に対する発注、仕入になる場合は、仕入データのRelateNo1に発注データのIdをセット")]
[OldTableCommentAttr("HC$tran_tori0")]
public sealed partial class Tran13Hachu : TranAllHeader {
	/// <summary>
	/// 仕入先キー
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("取引先CD1")]
	long id_Shiire;
	/// <summary>
	/// 仕入先データ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vShiire = new();
	/// <summary>
	/// 区分（2桁 10-19,20-29,30,99）
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnKubun))]
	[OldTableCommentAttr("取引区分")]
	int kubun = 10;
	[Ignore]
	[JsonIgnore]
	public EnumShiire EnKubun {
		get => (EnumShiire)Kubun;
		set => Kubun = (int)value;
	}
	/// <summary>
	///	関連No1
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("関連伝票NO")]
	int relateNo1;
	/// <summary>
	/// 掛率
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("掛率1")]
	int rate;
}


/// <summary>
/// ハンディターミナルのデータ
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("nk1", false, "DenDay")]
[Comment("トランザクション：ハンディターミナルのデータ")]
public sealed partial class TranHhtData : BaseDbClass {
	/// <summary>
	/// 店舗 文字  8
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: System.ComponentModel.DefaultValue("")]
	string store = string.Empty;
	/// <summary>
	/// 日付 文字  8
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	string denDay = "19010101";
	/// <summary>
	/// 処理区分 文字  2
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(2)]
	[property: System.ComponentModel.DefaultValue("")]
	string kubun = string.Empty;
	/// <summary>
	/// 伝票NO	数値	8
	/// </summary>
	[ObservableProperty]
	long denNo;
	/// <summary>
	/// 担当者	文字	6
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(6)]
	[property: System.ComponentModel.DefaultValue("")]
	string tanto = string.Empty;
	/// <summary>
	/// 取引先	文字	8
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: System.ComponentModel.DefaultValue("")]
	string tori = string.Empty;
	/// <summary>
	/// 品番	文字	20
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	string hinban = string.Empty;
	/// <summary>
	/// カラー	文字	8
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: System.ComponentModel.DefaultValue("")]
	string color = string.Empty;
	/// <summary>
	/// サイズ	文字	8
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: System.ComponentModel.DefaultValue("")]
	string size = string.Empty;
	/// <summary>
	/// 元上代	数値	8
	/// </summary>
	[ObservableProperty]
	int motoJodai;
	/// <summary>
	/// 上代	数値	8
	/// </summary>
	[ObservableProperty]
	int jodai;
	/// <summary>
	/// 下代	数値	8
	/// </summary>
	[ObservableProperty]
	int gedai;
	/// <summary>
	/// 数量	数値	5
	/// </summary>
	[ObservableProperty]
	int su;
	/// <summary>
	/// 店舗2	文字	8
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: System.ComponentModel.DefaultValue("")]
	string store2 = string.Empty;
	/// <summary>
	/// セールFLG	文字	1
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(1)]
	[property: System.ComponentModel.DefaultValue("")]
	string saleFlg = string.Empty;
	/// <summary>
	/// 棚番	文字	10
	/// </summary>
	[ObservableProperty]
	int tanaNo = 0;
	/// <summary>
	/// 関連伝票NO	数値	8
	/// </summary>
	[ObservableProperty]
	long relateDenNo;
	/// <summary>
	/// 掛率	数値	6.3
	/// </summary>
	[ObservableProperty]
	decimal kakeritsu;
	/// <summary>
	/// 納品日	文字	8
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: System.ComponentModel.DefaultValue("")]
	string nouhinDay = string.Empty;
	/// <summary>
	/// JANコード1	文字	13
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(13)]
	[property: System.ComponentModel.DefaultValue("")]
	string jan1 = string.Empty;
	/// <summary>
	/// JANコード2	文字	13
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(13)]
	[property: System.ComponentModel.DefaultValue("")]
	string jan2 = string.Empty;
	/// <summary>
	/// 予備03	文字	20
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	string yobi03 = string.Empty;
	/// <summary>
	/// 予備04	文字	20
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	string yobi04 = string.Empty;
	/// <summary>
	/// 予備05	文字	20
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	string yobi05 = string.Empty;
	/// <summary>
	/// 予備06	文字	20
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	string yobi06 = string.Empty;
	/// <summary>
	/// 予備07	文字	20
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	string yobi07 = string.Empty;
	/// <summary>
	/// 予備08	文字	20
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	string yobi08 = string.Empty;
	/// <summary>
	/// 予備09	文字	20
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	string yobi09 = string.Empty;
	/// <summary>
	/// 予備10	文字	20
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	string yobi10 = string.Empty;
	/// <summary>
	/// 予備11	文字	20
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	string yobi11 = string.Empty;
	/// <summary>
	/// 予備12	文字	20
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	string yobi12 = string.Empty;
	/// <summary>
	/// 入力ファイル名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	string fileName = string.Empty;
	/// <summary>
	/// 行No
	/// </summary>
	[ObservableProperty]
	int lineNo;
	/// <summary>
	/// HhtdataからTran系各テーブルへの変換日時(vdu相当の日時データ)
	/// </summary>
	[ObservableProperty]
	long vdCnvDate;

}

/// <summary>
/// VULCANデータレイアウト 一次取込用テーブル
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("nk1", false, "BackupFileName")]
[KeyDml("nk2", false, "VdCnvDate")]
[Comment("トランザクション：VULCANデータレイアウトハンディのデータ")]
public sealed partial class TranVulcanHht : BaseDbClass {
	/// <summary>
	/// VULCANタイプ  1:売上, 2:返品, 3:入庫, 4:出庫, 5:仕入, 6:仕入返品, 7:棚卸, 8:発注, 9:卸売, 10:卸返品, 11:移動, 12:客数
	/// ファイルレイアウト 1桁:1-9,A-Cで表現されているが、数値に変換して格納する
	/// </summary>
	[ObservableProperty]
	int type0 = 0;
	/// <summary>
	/// HT No  1-999の数値を格納する。VULCANのファイルレイアウトでは3桁の文字列で表現されているが、数値に変換して格納する
	/// </summary>
	[ObservableProperty]
	int hhtNo = 0;
	/// <summary>
	/// SerialNo 1-9999の数値を格納する。VULCANのファイルレイアウトでは5桁の文字列で表現されているが、数値に変換して格納する
	/// </summary>
	[ObservableProperty]
	int serial = 0;
	/// <summary>
	/// 日付 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	string denDay = "19010101";
	/// <summary>
	/// 店舗 文字  8 前'0'埋め
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: System.ComponentModel.DefaultValue("")]
	string store = string.Empty;
	/// <summary>
	/// 担当者	文字	6 前'0'埋め
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(6)]
	[property: System.ComponentModel.DefaultValue("")]
	string tanto = string.Empty;
	/// <summary>
	/// 販売区分 1桁 0:プロパー,1:セール, 9:未使用 (入庫と出庫の場合は 0:買取, 1:委託)
	/// </summary>
	[ObservableProperty]
	int hanKubun = 9;
	/// <summary>
	/// 伝票番号 13桁 先頭8桁に0埋で数値を格納する。(売上と返品の場合は顧客CD 13桁、客数の場合は先頭8桁0埋で客数)
	/// </summary>
	[property: ColumnSizeDml(13)]
	[property: System.ComponentModel.DefaultValue("")]
	[ObservableProperty]
	string denNo = string.Empty;
	/// <summary>
	/// JAN 1段目
	/// </summary>
	[property: ColumnSizeDml(13)]
	[property: System.ComponentModel.DefaultValue("")]
	[ObservableProperty]
	string jan1 = string.Empty;
	/// <summary>
	/// JAN 2段目
	/// </summary>
	[property: ColumnSizeDml(13)]
	[property: System.ComponentModel.DefaultValue("")]
	[ObservableProperty]
	string jan2 = string.Empty;
	/// <summary>
	/// 数量 6桁 先頭に'0'か'-'、5桁数値を格納する。
	/// </summary>
	[ObservableProperty]
	int su = 9;
	/// <summary>
	/// 単価 9桁数値を格納する。
	/// </summary>
	[ObservableProperty]
	int tanka = 9;
	/// <summary>
	/// 取引先 文字 8 前'0'埋め
	/// </summary>
	[property: ColumnSizeDml(8)]
	[property: System.ComponentModel.DefaultValue("")]
	[ObservableProperty]
	string toriSaki = string.Empty;
	/// <summary>
	/// 掛率 文字 5桁 前'0'埋めで 999.9 を格納する。仕入の場合は発注番号8桁、発注の場合は納品日8桁を格納する。
	/// </summary>
	[property: ColumnSizeDml(8)]
	[property: System.ComponentModel.DefaultValue("")]
	[ObservableProperty]
	string kakeRitsu = string.Empty;
	/// <summary>
	/// 1取込ファイルの総件数 5桁数値
	/// </summary>
	[ObservableProperty]
	int totalCnt = 9;
	/// <summary>
	/// 予備空白	文字	6
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(6)]
	[property: System.ComponentModel.DefaultValue("")]
	string filler = string.Empty;
	/// <summary>
	/// バックアップファイル名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	string backupFileName = string.Empty;
	/// <summary>
	/// 行No
	/// </summary>
	[ObservableProperty]
	int lineNo;
	/// <summary>
	/// Local PCのコンピュータ名
	/// </summary>
	[ObservableProperty]
	string? computerName = null;
	/// <summary>
	/// Local PCのユーザ名
	/// </summary>
	[ObservableProperty]
	string? userName = null;
	/// <summary>
	/// HhtdataからTran系各テーブルへの変換日時(vdu相当の日時データ)
	/// </summary>
	[ObservableProperty]
	long vdCnvDate;
	/// <summary>
	/// 対象テーブル名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(30)]
	string targetTableName = string.Empty;
	/// <summary>
	/// 対象テーブルの対象レコードID
	/// </summary>
	[ObservableProperty]
	long targetId;
	/// <summary>
	/// 変換エラー内容
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(1000)]
	string errorMsg = string.Empty;
}


/* ToDo: 未作成テーブル(上代、原価)

// 上代一括変更の伝票データ
[Comment("トランザクション：上代一括変更、伝票No、登録日、日付Frm-To、セールCD(区分'S01')、タイトル、[店舗CD] [商品CD、色サイズ、掛率、上代]")]
public sealed partial class TranJodai : BaseDbClass {
}

[Comment("派生テーブル：商品上代テーブル：商品Id、店舗Id、日付Fm-To、上代、元TranId")
public sealed partial class DerivedJodai : BaseDbClass {
}
			派生上代テーブルとマスタから上代を決定する
			   SELECT COALESCE(d.Jodai, m.Jodai) AS FinalJodai
				FROM MasterShohin m
				LEFT JOIN DerivedShohin d ON m.Id = d.Id_Shohin 
				    AND d.Id_Tenpo = 10
				    AND 20260415 BETWEEN d.DayFrom AND d.DayTo
				WHERE m.Id = 1;

// 原価変更の伝票データ
[Comment("トランザクション：原価変更、伝票No、登録日、評価区分(評価替、その他)、[商品CD、OFF率、(上代、掛率、原価)、新原価]")]
public sealed partial class TranGenka : BaseDbClass {
}
*/


/* ToDo: 未作成テーブル(配分)
[Comment("トランザクション：配分データ：日付、配分CD、倉庫Id、[商品Id、色サイズ、予定数量、実数量、完了FLG]")]
public sealed partial class TranHaibun : BaseDbClass {
}
[Comment("派生テーブル：配分明細：日付、倉庫Id、商品Id、色サイズ、予定数量、実数量、完了FLG、元伝票Id")]
public sealed partial class DerivedHaibun : BaseDbClass {
}
 */

/* ToDo: 未作成テーブル(補充)
[Comment("トランザクション：補充データ：日付、配分CD、倉庫Id、[商品Id、色サイズ、予定数量、実数量、完了FLG]")]
public sealed partial class TranHojyu : BaseDbClass {
}
 */

/* ToDo: 未作成テーブル(集計)
[Comment("集計テーブル：売掛データ：年月、得意先Id、前月残、当月残、売上、入金")]
public sealed partial class SummaryUrikake : BaseDbClass {
}
[Comment("集計テーブル：請求データ：年月+締日、得意先Id、前月残、当月残、売上、入金")]
public sealed partial class SummaryUriSei : BaseDbClass {
}
[Comment("集計テーブル：買掛データ：年月、仕入先Id、前月残、当月残、売上、入金")]
public sealed partial class SummaryKaikake : BaseDbClass {
}
[Comment("集計テーブル：支払データ：年月、仕入先Id、前月残、当月残、売上、入金")]
public sealed partial class SummaryKaiShi : BaseDbClass {
}
 */

/* ToDo: 未作成テーブル(顧客)
[Comment("ベースポイントトランク別ポイント、ボーナスポイント")]
public sealed partial class MasterPointRank : BaseDbClass {
}
[Comment("ポイント履歴テーブル：日付、顧客Id、取得ポイント、使用ポイント、残")]
public sealed partial class TranPointRireki : BaseDbClass {
}
 */

