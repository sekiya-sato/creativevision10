using CommunityToolkit.Mvvm.ComponentModel;
using Cvnet10Base.Share;
using Newtonsoft.Json;
using NPoco;

namespace Cvnet10Base;

/// <summary>
/// 共通トランザクション（ヘッダ）
/// </summary>
public partial class TranAllHeader : BaseDbClass {
	/// <summary>
	/// 計上日（yyyyMMdd）
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	string denDay = "19010101";
	/// <summary>
	/// 社員ユニークキー
	/// </summary>
	[ObservableProperty]
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
	int calcFlag = 1;
	/// <summary>
	/// 数量合計
	/// </summary>
	[ObservableProperty]
	int suTotal;
	/// <summary>
	/// 金額合計
	/// </summary>
	[ObservableProperty]
	int kingakuTotal;
	/// <summary>
	/// 上代合計
	/// </summary>
	[ObservableProperty]
	int jodaiTotal;
	/// <summary>
	/// 下代合計
	/// </summary>
	[ObservableProperty]
	int gedaiTotal;
	/// <summary>
	/// 値引: 合計からの
	/// </summary>
	[ObservableProperty]
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
public sealed partial class Tran99Meisai : ObservableObject {
	/// <summary>
	/// 行No
	/// </summary>
	[ObservableProperty]
	int no;
	/// <summary>
	/// 区分（2桁 10-19,20-29,30,99）
	/// </summary>
	[ObservableProperty]
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
	string code_Shohin = string.Empty;
	/// <summary>
	/// 商品名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: System.ComponentModel.DefaultValue("")]
	string mei_Shohin = string.Empty;
	/// <summary>
	/// 入力JANコード
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
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
	int su;
	/// <summary>
	/// 単価
	/// </summary>
	[ObservableProperty]
	int tanka;
	/// <summary>
	/// 金額
	/// </summary>
	[ObservableProperty]
	int kingaku;
	/// <summary>
	/// 上代
	/// </summary>
	[ObservableProperty]
	int jodai;
	/// <summary>
	/// 下代
	/// </summary>
	[ObservableProperty]
	int gedai;
	/// <summary>
	/// 値引: 合計からの
	/// </summary>
	[ObservableProperty]
	int nebiki00;
	/// <summary>
	/// 値引: 明細1
	/// </summary>
	[ObservableProperty]
	int nebiki01;
	/// <summary>
	/// 値引: 明細2
	/// </summary>
	[ObservableProperty]
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
	string denDay = "19010101";
	/// <summary>
	/// 社員ユニークキー
	/// </summary>
	[ObservableProperty]
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
	int kingakuTotal;
	/// <summary>
	/// 手入力No
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	string manualNo = string.Empty;
	/// <summary>
	/// ヘッダメモ
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(200)]
	[property: System.ComponentModel.DefaultValue("")]
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
public sealed partial class TranKinMeisai : ObservableObject {
	/// <summary>
	/// 行No
	/// </summary>
	[ObservableProperty]
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
	int kingaku;
	/// <summary>
	/// 明細メモ
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(200)]
	[property: System.ComponentModel.DefaultValue("")]
	string memo = string.Empty;
}

/// <summary>
/// 入金 06 (取引先 売掛-)
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("nk1", false, "DenDay")]
[KeyDml("nk2", false, ["Id_Torisaki"])]
public sealed partial class Tran06Nyukin : TranKinHeader {
}
/// <summary>
/// 支払 07 (取引先 買掛-)
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("nk1", false, "DenDay")]
[KeyDml("nk2", false, ["Id_Torisaki"])]
public sealed partial class Tran07Shiharai : TranKinHeader {
}

/// <summary>
/// 棚卸 60 (倉庫 現在値)
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("nk1", false, "DenDay")]
[KeyDml("nk2", false, ["Id_Soko"])]
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
public sealed partial class Tran00Uriage : TranAllHeader {
	/// <summary>
	/// 掛計上日（yyyyMMdd）
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	string kakeDay = "19010101";
	/// <summary>
	/// 得意先キー
	/// </summary>
	[ObservableProperty]
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
	string manualNo = string.Empty;
	/// <summary>
	///	関連No1
	/// </summary>
	[ObservableProperty]
	int relateNo1;
	/// <summary>
	///	関連No2
	/// </summary>
	[ObservableProperty]
	int relateNo2;
	/// <summary>
	/// 掛率
	/// </summary>
	[ObservableProperty]
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
public sealed partial class Tran01Tenuri : TranAllHeader {
	/// <summary>
	/// 店舗キー
	/// </summary>
	[ObservableProperty]
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
	string code_Customer = string.Empty;
	/// <summary>
	/// 区分（2桁 10-19,20-29,30,99）
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnKubun))]
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
	int relateNo1;
	/// <summary>
	/// 掛率
	/// </summary>
	[ObservableProperty]
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
public sealed partial class Tran03Shiire : TranAllHeader {
	/// <summary>
	/// 掛計上日（yyyyMMdd）
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	string kakeDay = "19010101";
	/// <summary>
	/// 仕入先キー
	/// </summary>
	[ObservableProperty]
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
	string manualNo = string.Empty;
	/// <summary>
	///	関連No1
	/// </summary>
	[ObservableProperty]
	int relateNo1;
	/// <summary>
	/// 掛率
	/// </summary>
	[ObservableProperty]
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
public sealed partial class Tran05Ido : TranAllHeader {
	/// <summary>
	/// 移動先キー
	/// </summary>
	[ObservableProperty]
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
	int relateNo1;
	/// <summary>
	/// 手入力No
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	string manualNo = string.Empty;
}

/// <summary>
/// 積送移動 10 (倉庫 出, 移動先 入) 仮
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("nk1", false, "DenDay")]
[KeyDml("nk2", false, ["Id_Soko"])]
[KeyDml("nk3", false, ["Id_Ido"])]
public sealed partial class Tran10IdoOut : TranAllHeader {
	/// <summary>
	/// 移動先キー
	/// </summary>
	[ObservableProperty]
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
	int relateNo1;
	/// <summary>
	/// 手入力No
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	string manualNo = string.Empty;
}
/// <summary>
/// 積送移動 11 (倉庫 出, 移動先 入) 実
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("nk1", false, "DenDay")]
[KeyDml("nk2", false, ["Id_Soko"])]
[KeyDml("nk3", false, ["Id_Ido"])]
public sealed partial class Tran11IdoIn : TranAllHeader {
	/// <summary>
	/// 移動先キー
	/// </summary>
	[ObservableProperty]
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
	int relateNo1;
	/// <summary>
	/// 手入力No
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	string manualNo = string.Empty;
}
/// <summary>
/// 受注 12
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("nk1", false, "DenDay")]
[KeyDml("nk3", false, ["Id_Soko"])]
[KeyDml("nk4", false, ["Id_Tokui"])]
public sealed partial class Tran12Jyuchu : TranAllHeader {
	/// <summary>
	/// 得意先キー
	/// </summary>
	[ObservableProperty]
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
	int relateNo1;
	/// <summary>
	/// 掛率
	/// </summary>
	[ObservableProperty]
	int rate;
}

/// <summary>
/// 発注 13
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("nk1", false, "DenDay")]
[KeyDml("nk3", false, ["Id_Soko"])]
[KeyDml("nk4", false, ["Id_Tokui"])]
public sealed partial class Tran13Hachu : TranAllHeader {
	/// <summary>
	/// 仕入先キー
	/// </summary>
	[ObservableProperty]
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
	int relateNo1;
	/// <summary>
	/// 掛率
	/// </summary>
	[ObservableProperty]
	int rate;
}


/// <summary>
/// ハンディターミナル用のデータ
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("nk1", false, "DenDay")]
public sealed partial class TranHhtdata : BaseDbClass {
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
	[property: ColumnSizeDml(10)]
	[property: System.ComponentModel.DefaultValue("")]
	string tanaNo = string.Empty;
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
	string fileName = string.Empty;
	/// <summary>
	/// 行No
	/// </summary>
	[ObservableProperty]
	int lineNo;


}
