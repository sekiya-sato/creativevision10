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
	/// 担当者
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
	[NotifyPropertyChangedFor(nameof(EnShime1))]
	int shime1;

	[Ignore]
	[JsonIgnore]
	public EnumShime EnShime1 {
		get => (EnumShime)Shime1;
		set => Shime1 = (int)value;
	}
	/// <summary>
	/// 締日2
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnShime2))]
	int shime2;

	[Ignore]
	[JsonIgnore]
	public EnumShime EnShime2 {
		get => (EnumShime)Shime2;
		set => Shime2 = (int)value;
	}
	/// <summary>
	/// 締日3
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnShime3))]
	int shime3;

	[Ignore]
	[JsonIgnore]
	public EnumShime EnShime3 {
		get => (EnumShime)Shime3;
		set => Shime3 = (int)value;
	}
	/// <summary>
	/// 入金/支払月
	/// </summary>
	[ObservableProperty]
	int payMonth;
	/// <summary>
	/// 入金/支払日
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnPayDay))]
	int payDay;

	[Ignore]
	[JsonIgnore]
	public EnumShime EnPayDay {
		get => (EnumShime)PayDay;
		set => PayDay = (int)value;
	}

	/// <summary>
	/// 入金/支払方法
	/// </summary>
	[ObservableProperty]
	long id_PayMethod;
	/// <summary>
	/// 入金方法データ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vPayMethod = new();
	/// <summary>
	/// 請求/支払フラグ
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
	/// 請求/支払先
	/// </summary>
	[ObservableProperty]
	long id_Paysaki;
	/// <summary>
	/// 請求先データ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vPaysaki = new();
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
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("uq1", true, "Code")]
public sealed partial class MasterTokui : MasterTorihiki {
	/// <summary>
	/// 得意先種別
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnTenType))]
	int tenType;

	[Ignore]
	[JsonIgnore]
	public EnumTokui EnTenType {
		get => (EnumTokui)TenType;
		set => TenType = (int)value;
	}
	/// <summary>
	/// 在庫管理フラグ
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnIsZaiko))]
	int isZaiko = 1;

	[Ignore]
	[JsonIgnore]
	public EnumYesNo EnIsZaiko {
		get => (EnumYesNo)IsZaiko;
		set => IsZaiko = (int)value;
	}
	/// <summary>
	/// 名称リスト
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(1000)]
	List<MasterGeneralMeisho>? jsub;
}

/// <summary>
/// 仕入先マスター
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("uq1", true, "Code")]
public partial class MasterShiire : MasterTorihiki {
	/// <summary>
	/// 名称リスト
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(1000)]
	List<MasterGeneralMeisho>? jsub;
}
