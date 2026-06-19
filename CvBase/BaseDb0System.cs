using CommunityToolkit.Mvvm.ComponentModel;
using CvBase.Share;
using Newtonsoft.Json;
using NPoco;

namespace CvBase;


/// <summary>
/// マスター：システム管理テーブル(1レコードのみ)
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[Comment("マスター：システム管理テーブル 会社名、消費税設定など")]
[OldTableCommentAttr("HC$MASTER_SYSKANRI", "HC$MASTER_SYSTAX を含むシステム設定項目")]
public sealed partial class MasterSysman : BaseDbHasAddress {
	/// <summary>
	/// 自社名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("自社名")]
	string name = string.Empty;
	/// <summary>
	/// ホームページ
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(30)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("ホームページ")]
	string hp = string.Empty;
	/// <summary>
	/// 自社締め日 1-31,99
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnShimeBi))]
	[OldTableCommentAttr("自社締日")]
	int shimeBi;

	[Ignore]
	[JsonIgnore]
	public EnumShime EnShimeBi {
		get => (EnumShime)ShimeBi;
		set => ShimeBi = (int)value;

	}

	/// <summary>
	/// 修正有効日数
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("修正有効日数")]
	int modifyDaysEx;
	/// <summary>
	/// 先付有効日数
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("先付有効日数")]
	int modifyDaysPre;
	/// <summary>
	/// 振込先1
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(30)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("振込先1")]
	string bankAccount1 = string.Empty;
	/// <summary>
	/// 振込先2
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(30)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("振込先2")]
	string bankAccount2 = string.Empty;
	/// <summary>
	/// 振込先3
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(30)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("振込先3")]
	string bankAccount3 = string.Empty;
	/// <summary>
	/// 期首年月日
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: System.ComponentModel.DefaultValue("19010101")]
	[OldTableCommentAttr("期首年月日")]
	string fiscalStartDate = "19010101";
	/// <summary>
	/// 消費税率リスト
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	List<MasterSysTax>? jsub;

	[ObservableProperty]
	[property: ColumnSizeDml(14)]
	[OldTableCommentAttr("事業者登録番号", "T+13桁 select 名称 from HC$master_meisho where 名称区分='IBS' and 名称CD='01'")]
	string taxRegistrationNumber = string.Empty;
}
/// <summary>
/// 消費税率テーブル
/// </summary>
[NoCreate]
public sealed partial class MasterSysTax : ObservableObject {
	[ObservableProperty]
	[OldTableCommentAttr("消費税CD")]
	long id;
	/// <summary>
	/// 消費税率 (%) 例:10
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("消費税率")]
	int taxRate;
	/// <summary>
	/// 新消費税開始日(yyyyMMdd)
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: System.ComponentModel.DefaultValue("19010101")]
	[OldTableCommentAttr("新消費税開始日")]
	string dateFrom = "19010101";
	/// <summary>
	/// 新消費税率 (%) 例:10
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("新消費税率")]
	int taxNewRate;
}
/// <summary>
/// 名称テーブル
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uq1", true, [nameof(Kubun), nameof(Code)])]
[KeyDml("nk2", false, [nameof(Kubun), nameof(Odr), nameof(Code)])]
[Comment("マスター：名称テーブル 汎用 区分+名称コード")]
[OldTableCommentAttr("HC$MASTER_MEISHO")]
public sealed partial class MasterMeisho : BaseDbClass, IBaseCodeName {
	/// <summary>
	/// 区分
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("名称区分")]
	string kubun = string.Empty;
	/// <summary>
	/// 区分名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(40)]
	[property: System.ComponentModel.DefaultValue("")]
	string kubunName = string.Empty;
	/// <summary>
	/// 名称コード
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("名称CD")]
	string code = "";
	/// <summary>
	/// 名称
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("名称")]
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
	/// よみがな
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("カナ")]
	string kana = string.Empty;
	/// <summary>
	/// 並び順
	/// </summary>
	[ObservableProperty]
	int odr;
}
