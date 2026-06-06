using CommunityToolkit.Mvvm.ComponentModel;
using CvBase.Share;
using Newtonsoft.Json;
using NPoco;


namespace CvBase;

/// <summary>
/// 共通取引先テーブル
/// </summary>
public partial class MasterTorihiki : BaseDbHasAddress, IBaseCodeName {
	/// <summary>
	/// コード
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(12)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("得意先CD", "MasterShiire は 仕入先CD")]
	string code = string.Empty;
	/// <summary>
	/// 名前
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(80)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("得意先名", "MasterShiire は 仕入先名")]
	string name = string.Empty;
	/// <summary>
	/// 略称
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("略称")]
	string ryaku = string.Empty;
	/// <summary>
	/// カナ
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("カナ")]
	string kana = string.Empty;
	/// <summary>
	/// 担当者
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("営業担当CD", "MasterShiire は 入力社員CD")]
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
	[OldTableCommentAttr("掛率")]
	int rateProper;
	/// <summary>
	/// セール掛率
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("セール掛率", "MasterShiire は 掛率2")]
	int rateSale;
	/// <summary>
	/// 締日1
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnShime1))]
	[OldTableCommentAttr("締日")]
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
	[OldTableCommentAttr("締日2")]
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
	[OldTableCommentAttr("締日3")]
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
	[OldTableCommentAttr("入金予定月")]
	int payMonth;
	/// <summary>
	/// 入金/支払日
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnPayDay))]
	[OldTableCommentAttr("入金予定日")]
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
	[OldTableCommentAttr("請求印刷", "MasterShiire は 支払印刷")]
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
	[property: System.ComponentModel.DefaultValue("")]
	[property: Newtonsoft.Json.JsonProperty("Bank1")]
	[OldTableCommentAttr("振込先1", "MasterShiire は 振込銀行/振込支店/振込種別/振込口座 を連結")]
	string bankAccount1 = string.Empty;
	/// <summary>
	/// 振込先2
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(30)]
	[property: System.ComponentModel.DefaultValue("")]
	[property: Newtonsoft.Json.JsonProperty("Bank2")]
	[OldTableCommentAttr("振込先2")]
	string bankAccount2 = string.Empty;
	/// <summary>
	/// 振込先3
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(30)]
	[property: System.ComponentModel.DefaultValue("")]
	[property: Newtonsoft.Json.JsonProperty("Bank3")]
	[OldTableCommentAttr("振込先3")]
	string bankAccount3 = string.Empty;
}
/// <summary>
/// 得意先マスター
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("uq1", true, "Code")]
[Comment("マスター：得意先マスター TenType(0=倉庫, 1=卸先, 3=売仕店, 6=直営店)")]
[OldTableCommentAttr("HC$MASTER_TOKUI")]
public sealed partial class MasterTokui : MasterTorihiki {
	/// <summary>
	/// 得意先種別
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnTenType))]
	[OldTableCommentAttr("店種区分")]
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
	[OldTableCommentAttr("在庫管理FLG")]
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
	[OldTableCommentAttr("名称CD01 - 名称CD10")]
	List<MasterGeneralMeisho>? jsub;
}

/// <summary>
/// 仕入先マスター
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("uq1", true, "Code")]
[Comment("マスター：仕入先マスター")]
[OldTableCommentAttr("HC$MASTER_SIIRE")]
public sealed partial class MasterShiire : MasterTorihiki {
	/// <summary>
	/// 名称リスト
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(1000)]
	[OldTableCommentAttr("名称CD01 - 名称CD10")]
	List<MasterGeneralMeisho>? jsub;
}

/* VShain などが物理DBに存在せず、Class定義上で存在している場合の、SQLでの結合例
 SELECT
    si.Id,
    si.Code,
    si.Name,
    si.Id_Shain,
    json_object('Sid', s.Id, 'Cd', s.Code, 'Mei', s.Name) AS Vshaindata
FROM MasterShiire si
LEFT OUTER JOIN MasterShain s ON s.Id = si.Id_Shain
LIMIT 20;
 */
