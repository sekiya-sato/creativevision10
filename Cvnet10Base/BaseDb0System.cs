using CommunityToolkit.Mvvm.ComponentModel;
using Cvnet10Base.Share;
using NPoco;
using System.ComponentModel;

namespace Cvnet10Base;


/// <summary>
/// システム：システム管理テーブル
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
public sealed partial class MasterSysman : BaseDbHasAddress {
	/// <summary>
	/// 自社名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: DefaultValue("")]
	string name =string.Empty;
	/// <summary>
	/// ホームページ
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(30)]
	[property: DefaultValue("")]
	string hp =string.Empty;
	/// <summary>
	/// 自社締め日 1-31,99
	/// </summary>
	[ObservableProperty]
	int shimeBi;
	/// <summary>
	/// 修正有効日数
	/// </summary>
	[ObservableProperty]
	int modifyDaysEx;
	/// <summary>
	/// 先付有効日数
	/// </summary>
	[ObservableProperty]
	int modifyDaysPre;
	/// <summary>
	/// 振込先1
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(30)]
	[property: DefaultValue("")]
	string bankAccount1 =string.Empty;
	/// <summary>
	/// 振込先2
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(30)]
	[property: DefaultValue("")]
	string bankAccount2 =string.Empty;
	/// <summary>
	/// 振込先3
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(30)]
	[property: DefaultValue("")]
	string bankAccount3 =string.Empty;
	/// <summary>
	/// 期首年月日
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: DefaultValue("19010101")]
	string fiscalStartDate="19010101";
	/// <summary>
	/// 消費税率リスト
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	List<MasterSysTax>? jsub;
}
/// <summary>
/// 消費税率テーブル
/// </summary>
[NoCreate]
public sealed partial class MasterSysTax: ObservableObject {
	[ObservableProperty]
	long id;
	/// <summary>
	/// 消費税率 (%) 例:10
	/// </summary>
	[ObservableProperty]
	int taxRate;
	/// <summary>
	/// 新消費税開始日(yyyyMMdd)
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: DefaultValue("19010101")]
	string dateFrom = "19010101";
	/// <summary>
	/// 新消費税率 (%) 例:10
	/// </summary>
	[ObservableProperty]
	int taxNewRate;
}
/// <summary>
/// 名称テーブル
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("MasterMeisho_uq1", false, ["Kubun","Code"])]
[KeyDml("MasterMeisho_nk2", false, ["Kubun","Odr" , "Code"])]
public sealed partial class MasterMeisho : BaseDbClass {
	/// <summary>
	/// 区分
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: DefaultValue("")]
	string kubun = string.Empty;
	/// <summary>
	/// 区分名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(40)]
	[property: DefaultValue("")]
	string kubunName = string.Empty;
	/// <summary>
	/// 名称コード
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: DefaultValue("")]
	string code = "";
	/// <summary>
	/// 名称
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: DefaultValue("")]
	string name =string.Empty;
	/// <summary>
	/// 略称
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: DefaultValue("")]
	string ryaku =string.Empty;
	/// <summary>
	/// よみがな
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: DefaultValue("")]
	string kana =string.Empty;
	/// <summary>
	/// 並び順
	/// </summary>
	[ObservableProperty]
	int odr;
	readonly public static string ViewSql = """
SELECT * FROM (
    SELECT 
        T.*, 
        m1.Name AS KubunName
    FROM MasterMeisho T
    LEFT OUTER JOIN MasterMeisho m1 
        ON m1.Kubun = 'IDX' 
        AND T.Kubun = m1.Code
) MasterMeishoView
""";

}
