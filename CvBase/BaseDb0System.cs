using CommunityToolkit.Mvvm.ComponentModel;
using CvBase.Share;
using Newtonsoft.Json;
using NPoco;

namespace CvBase;


/// <summary>
/// マスター：システム管理テーブル(Id 1 の1レコードのみ)
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[Comment("マスター：システム管理テーブル 会社名、消費税設定など")]
[OldTableCommentAttr("HC$MASTER_SYSKANRI", "HC$MASTER_SYSTAX を含むシステム設定項目")]
public sealed partial class MasterSysman : BaseDbHasAddress {
	/// <summary>
	/// 自社名
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	[OldTableCommentAttr("自社名")]
	public partial string Name { get; set; } = string.Empty;
	/// <summary>
	/// ホームページ
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(30)]
	[OldTableCommentAttr("ホームページ")]
	public partial string Hp { get; set; } = string.Empty;
	/// <summary>
	/// 自社締め日 1-31,99
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnShimeBi))]
	[OldTableCommentAttr("自社締日")]
	public partial int ShimeBi { get; set; }
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
	public partial int ModifyDaysEx { get; set; }
	/// <summary>
	/// 先付有効日数
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("先付有効日数")]
	public partial int ModifyDaysPre { get; set; }
	/// <summary>
	/// 振込先1
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(30)]
	[OldTableCommentAttr("振込先1")]
	public partial string BankAccount1 { get; set; } = string.Empty;
	/// <summary>
	/// 振込先2
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(30)]
	[OldTableCommentAttr("振込先2")]
	public partial string BankAccount2 { get; set; } = string.Empty;
	/// <summary>
	/// 振込先3
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(30)]
	[OldTableCommentAttr("振込先3")]
	public partial string BankAccount3 { get; set; } = string.Empty;
	/// <summary>
	/// 期首年月日
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("期首年月日")]
	public partial string FiscalStartDate { get; set; } = "19010101";
	/// <summary>
	/// 消費税率リスト
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	public partial List<MasterSysTax>? Jsub { get; set; }
	[ObservableProperty]
	[ColumnSizeDml(14)]
	[OldTableCommentAttr("事業者登録番号", "T+13桁 select 名称 from HC$master_meisho where 名称区分='IBS' and 名称CD='01'")]
	public partial string TaxRegistrationNumber { get; set; } = string.Empty;
	/// <summary>
	/// 標準倉庫
	/// </summary>
	[ObservableProperty]
	public partial long Id_Soko { get; set; }
	/// <summary>
	/// 倉庫データ
	/// </summary>
	[ObservableProperty]
	[ComputedColumn]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	public partial CodeNameView VSoko { get; set; } = new();
}
/// <summary>
/// 消費税率テーブル(Id 1-3)
/// </summary>
[SubTableDefine]
public sealed partial class MasterSysTax : ObservableObject {
	[ObservableProperty]
	[OldTableCommentAttr("消費税CD")]
	public partial long Id { get; set; }
	/// <summary>
	/// 消費税率 (%) 例:10
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("消費税率")]
	public partial int TaxRate { get; set; }
	/// <summary>
	/// 新消費税開始日(yyyyMMdd)
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("新消費税開始日")]
	public partial string DateFrom { get; set; } = "19010101";
	/// <summary>
	/// 新消費税率 (%) 例:10
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("新消費税率")]
	public partial int TaxNewRate { get; set; }
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
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("名称区分")]
	public partial string Kubun { get; set; } = string.Empty;
	/// <summary>
	/// 区分名
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(40)]
	public partial string KubunName { get; set; } = string.Empty;
	/// <summary>
	/// 名称コード
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	[OldTableCommentAttr("名称CD")]
	public partial string Code { get; set; } = "";
	/// <summary>
	/// 名称
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	[OldTableCommentAttr("名称")]
	public partial string Name { get; set; } = string.Empty;
	/// <summary>
	/// 略称
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	[OldTableCommentAttr("略称")]
	public partial string Ryaku { get; set; } = string.Empty;
	/// <summary>
	/// よみがな
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	[OldTableCommentAttr("カナ")]
	public partial string Kana { get; set; } = string.Empty;
	/// <summary>
	/// 並び順
	/// </summary>
	[ObservableProperty]
	public partial int Odr { get; set; }
}
