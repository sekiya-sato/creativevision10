using CommunityToolkit.Mvvm.ComponentModel;
using NPoco;
using Newtonsoft.Json;
using Cvnet10Base.Share;
using System.ComponentModel;


namespace Cvnet10Base;

/// <summary>
/// 共通取引先テーブル
/// </summary>
public partial class MasterTorihiki : BaseDbHasAddress, IBaseCodeName {
	/// <summary>
	/// コード
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(12)]
	[property: DefaultValue("")]
	string code = string.Empty;
	/// <summary>
	/// 名前
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(80)]
	[property: DefaultValue("")]
	string name = string.Empty;
	/// <summary>
	/// 略称
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: DefaultValue("")]
	string ryaku = string.Empty;
	/// <summary>
	/// カナ
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: DefaultValue("")]
	string kana = string.Empty;
	/// <summary>
	/// 担当者CD
	/// </summary>
	[ObservableProperty]
	long id_Shain;
	/// <summary>
	/// 掛率
	/// </summary>
	[ObservableProperty]
	int rateProper;
	/// <summary>
	/// セール掛率
	/// </summary>
	[ObservableProperty]
	int rateSale;
	/// <summary>
	/// 締日1
	/// </summary>
	[ObservableProperty]
	int shimeBi1;
	/// <summary>
	/// 締日2
	/// </summary>
	[ObservableProperty]
	int shimeBi2;
	/// <summary>
	/// 締日3
	/// </summary>
	[ObservableProperty]
	int shimeBi3;
	/// <summary>
	/// 入金/支払月
	/// </summary>
	[ObservableProperty]
	int payMonth;
	/// <summary>
	/// 入金/支払日
	/// </summary>
	[ObservableProperty]
	int payDay;
	/// <summary>
	/// 入金/支払方法
	/// </summary>
	[ObservableProperty]
	long id_MeishoPay;
	/// <summary>
	/// 請求/支払フラグ
	/// </summary>
	[ObservableProperty]
	int isPay = 0;
	/// <summary>
	/// 請求/支払先
	/// </summary>
	[ObservableProperty]
	long id_Paysaki;
	/// <summary>
	/// 取引先詳細
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(1000)]
	MasterToriDetail? jdetail;
}
/// <summary>
/// 取引先詳細
/// </summary>
public sealed partial class MasterToriDetail : ObservableObject {
	/// <summary>
	/// 振込先1
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(30)]
	[property: DefaultValue("")]
	[property: JsonProperty("Bank1")]
	string bankAccount1 = string.Empty;
	/// <summary>
	/// 振込先2
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(30)]
	[property: DefaultValue("")]
	[property: JsonProperty("Bank2")]
	string bankAccount2 = string.Empty;
	/// <summary>
	/// 振込先3
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(30)]
	[property: DefaultValue("")]
	[property: JsonProperty("Bank3")]
	string bankAccount3 = string.Empty;
}
/// <summary>
/// 得意先マスター
/// </summary>
public sealed partial class MasterTokui : MasterTorihiki {
	/// <summary>
	/// 得意先種別
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(ColumnType.Enum)]
	EnumTokui tenType;
	/// <summary>
	/// 在庫管理フラグ
	/// </summary>
	[ObservableProperty]
	int isZaiko = 1;
	/// <summary>
	/// 名称リスト
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(1000)]
	List<MasterGeneralMeisho>? jsub;
}
/// <summary>
/// 得意先種別
/// </summary>
public enum EnumTokui {
	/// <summary>
	/// 倉庫
	/// </summary>
	Soko = 0,
	/// <summary>
	/// 卸先
	/// </summary>
	Oroshi = 1,
	/// <summary>
	/// 売仕店
	/// </summary>
	UriShi = 3,
	/// <summary>
	/// 直営店
	/// </summary>
	Tenpo = 6,
}

/// <summary>
/// 仕入先マスター
/// </summary>
public partial class MasterShiire : MasterTorihiki {
	/// <summary>
	/// 名称リスト
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(1000)]
	List<MasterGeneralMeisho>? jsub;
}
