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

/*
public interface IViewClass {
	public string CreateSql { get; }
}
public partial class ViewShohinColSiz : ObservableObject, IViewClass {
	/// <summary>
	/// ユニークキー
	/// </summary>
	[ObservableProperty]
	[property: Comment("仮想ユニークキー")]
	long id;
	/// <summary>
	/// 商品Id
	/// </summary>
	[ObservableProperty]
	long id_Shohin;
	/// <summary>
	/// 色サイズ行Index
	/// </summary>
	[ObservableProperty]
	int rowIdx;
	/// <summary>
	/// コード
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(16)]
	string code = "";
	/// <summary>
	/// 名前
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(80)]
	[property: System.ComponentModel.DefaultValue("")]
	string name = string.Empty;
	/// <summary>
	/// 略称
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: System.ComponentModel.DefaultValue("")]
	string ryaku = string.Empty;
	/// <summary>
	/// 色
	/// </summary>
	[ObservableProperty]
	long id_Col;
	/// <summary>
	/// カラーCD
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	string code_Col = string.Empty;
	/// <summary>
	/// カラー名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
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
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	string code_Siz = string.Empty;
	/// <summary>
	/// サイズ名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: System.ComponentModel.DefaultValue("")]
	string mei_Siz = string.Empty;
	/// <summary>
	/// JANコード1
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	string jan1 = string.Empty;
	/// <summary>
	/// JANコード2
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	string jan2 = string.Empty;
	/// <summary>
	/// JANコード3
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	string jan3 = string.Empty;

	[Ignore]
	// CREATE VIEW IF NOT EXISTS DerivedShohinColSiz AS
	/// <summary>
	/// SqlDepends: View作成のSQL
	/// </summary>
	public string CreateSql => @"
WITH Flattened AS (
    SELECT
        M.*,
        J.value AS colsize_json,
        ROW_NUMBER() OVER (PARTITION BY M.Id) AS RowIdx
    FROM
        MasterShohin M,
        json_each(M.Jcolsiz) J
)
SELECT
    (Id * 100 + RowIdx) Id,
    ifnull(Id,0) Id_Shohin,
	RowIdx,
	Code,
    Name,
    Ryaku,
    json_extract(colsize_json, '$.Id_Col') AS Id_Col,
    json_extract(colsize_json, '$.Code_Col') AS Code_Col,
    json_extract(colsize_json, '$.Mei_Col') AS Mei_Col,
	json_extract(colsize_json, '$.Id_Siz') AS Id_Siz,
    json_extract(colsize_json, '$.Code_Siz') AS Code_Siz,
    json_extract(colsize_json, '$.Mei_Siz') AS Mei_Siz,
    json_extract(colsize_json, '$.Jan1') AS Jan1,
    json_extract(colsize_json, '$.Jan2') AS Jan2,
    json_extract(colsize_json, '$.Jan3') AS Jan3
FROM Flattened
";
}*/


[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("unq1", true, ["Id_Shohin", "Id_Col", "Id_Siz"])]
[KeyDml("n1", false, "Id_Shohin")]
[KeyDml("n2", false, "Code")]
[KeyDml("njan1", false, "Jan1")]
[KeyDml("njan2", false, "Jan2")]
[KeyDml("njan3", false, "Jan3")]
public partial class DerivedShohinColSiz : ObservableObject, IDerivedClass {
	/// <summary>
	/// ユニークキー
	/// </summary>
	[ObservableProperty]
	[property: Comment("仮想ユニークキー")]
	long id;
	/// <summary>
	/// 商品Id
	/// </summary>
	[ObservableProperty]
	long id_Shohin;
	/// <summary>
	/// 色サイズ行Index
	/// </summary>
	[ObservableProperty]
	int rowIdx;
	/// <summary>
	/// コード
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(16)]
	string code = "";
	/*
	/// <summary>
	/// 名前
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(80)]
	[property: System.ComponentModel.DefaultValue("")]
	string name = string.Empty;
	/// <summary>
	/// 略称
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: System.ComponentModel.DefaultValue("")]
	string ryaku = string.Empty;
	*/
	/// <summary>
	/// 色
	/// </summary>
	[ObservableProperty]
	long id_Col;
	/// <summary>
	/// カラーCD
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	string code_Col = string.Empty;
	/// <summary>
	/// カラー名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
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
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	string code_Siz = string.Empty;
	/// <summary>
	/// サイズ名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: System.ComponentModel.DefaultValue("")]
	string mei_Siz = string.Empty;
	/// <summary>
	/// JANコード1
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	string jan1 = string.Empty;
	/// <summary>
	/// JANコード2
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	string jan2 = string.Empty;
	/// <summary>
	/// JANコード3
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	string jan3 = string.Empty;

	[Ignore]
	/// <summary>
	/// SqlDepends: View作成のSQL
	/// </summary>
	public static string CreateSql => @$"
Insert into {nameof(DerivedShohinColSiz)}
SELECT
  (M.Id * 100 + ROW_NUMBER() OVER (PARTITION BY M.Id)) Id,
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
