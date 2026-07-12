using CommunityToolkit.Mvvm.ComponentModel;
using NPoco;

namespace CvBase;

/// <summary>
/// 派生型テーブルI/F (派生元のI/Fは IDerivedOrigin )
/// </summary>
public interface IDerivedClass {
	static abstract string CreateSql { get; }
	static abstract string InsertSql { get; }
	static abstract string DeleteSql { get; }
}

// Todo: キーの重複を確認し、対応する
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("nuniq", false, [nameof(Id_Shohin), nameof(Id_Col), nameof(Id_Siz)])]
[KeyDml("n1", false, nameof(Id_Shohin))]
[KeyDml("n2", false, nameof(Code))]
[KeyDml("njan1", false, nameof(Jan1))]
[KeyDml("njan2", false, nameof(Jan2))]
[KeyDml("njan3", false, nameof(Jan3))]
[Comment("派生マスタ：商品マスタMasterShohinから商品、色、サイズに展開したマスタ")]
public partial class DerivedShohinColSiz : BaseDbClass, IDerivedClass {
	/// <summary>
	/// 商品Id
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterShohin))]
	public partial long Id_Shohin { get; set; }
	/// <summary>
	/// 色サイズ行Index
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterShohin), additionalInfo: $"{nameof(MasterShohin)}のJcolsizに存在する行")]
	public partial int RowIdx { get; set; }
	/// <summary>
	/// コード
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(16)]
	public partial string Code { get; set; } = string.Empty;
	/*
/// <summary>
/// 名前
/// </summary>
[ObservableProperty]
[property: ColumnSizeDml(80)]

string name = string.Empty;
/// <summary>
/// 略称
/// </summary>
[ObservableProperty]
[property: ColumnSizeDml(100)]

string ryaku = string.Empty;
*/
	/// <summary>
	/// 色
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterShohin), additionalInfo: $"{nameof(MasterShohin)}のJcolsizに存在する色, {nameof(MasterMeisho)}のId")]
	public partial long Id_Col { get; set; }
	/// <summary>
	/// カラーCD
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	public partial string Code_Col { get; set; } = string.Empty;
	/// <summary>
	/// カラー名
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	public partial string Mei_Col { get; set; } = string.Empty;
	/// <summary>
	/// サイズ
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterShohin), additionalInfo: $"{nameof(MasterShohin)}のJcolsizに存在するサイズ, {nameof(MasterMeisho)}のId")]
	public partial long Id_Siz { get; set; }
	/// <summary>
	/// サイズCD
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	public partial string Code_Siz { get; set; } = string.Empty;
	/// <summary>
	/// サイズ名
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	public partial string Mei_Siz { get; set; } = string.Empty;
	/// <summary>
	/// JANコード1
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	public partial string Jan1 { get; set; } = string.Empty;
	/// <summary>
	/// JANコード2
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	public partial string Jan2 { get; set; } = string.Empty;
	/// <summary>
	/// JANコード3
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	public partial string Jan3 { get; set; } = string.Empty;
	[Ignore]
	/// <summary>
	/// SqlDepends: View作成のSQL
	/// </summary>
	public static string CreateSql => @$"
Insert into {nameof(DerivedShohinColSiz)}
SELECT
  (M.Id * 100 + ROW_NUMBER() OVER (PARTITION BY M.Id)) Id,M.vdc,M.vdu,
  ifnull(M.Id,0) Id_Shohin,
  ROW_NUMBER() OVER (PARTITION BY M.Id) RowIdx,
  M.Code,
  ifnull(json_extract(J.value, '$.Id_Col'), 0) AS Id_Col,
  ifnull(json_extract(J.value, '$.Code_Col'), '') AS Code_Col,
  ifnull(json_extract(J.value, '$.Mei_Col'), '') AS Mei_Col,
  ifnull(json_extract(J.value, '$.Id_Siz'), 0) AS Id_Siz,
  ifnull(json_extract(J.value, '$.Code_Siz'), '') AS Code_Siz,
  ifnull(json_extract(J.value, '$.Mei_Siz'), '') AS Mei_Siz,
  ifnull(json_extract(J.value, '$.Jan1'), '') AS Jan1,
  ifnull(json_extract(J.value, '$.Jan2'), '') AS Jan2,
  ifnull(json_extract(J.value, '$.Jan3'), '') AS Jan3
FROM MasterShohin M, json_each(M.Jcolsiz) J
"; //   M.Name, M.Ryaku,
	[Ignore]
	public static string InsertSql => CreateSql + " where M.Id = @0";
	[Ignore]
	public static string DeleteSql => $"Delete from {nameof(DerivedShohinColSiz)} where Id_Shohin = @0";
}
