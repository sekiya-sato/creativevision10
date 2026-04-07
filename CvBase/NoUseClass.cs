namespace CvBase;

/* ここには、以前使用していて、現在は使用していないクラスを置いておく。基本はすべてコメントアウト
[Comment("マスター：名称テーブル 汎用 区分+名称コード")]
public sealed partial class MasterMeisho : BaseDbClass, IBaseCodeName {
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
	/// <summary>
	/// JSON シリアライズ時に Mei_Col / Mei_Siz を含めるか (デフォルト: false)
	/// </summary>
	[JsonIgnore]
	public bool Ser { get; set; } = false;
	public bool ShouldSerializeCode_Col() => Ser;
	public bool ShouldSerializeMei_Col() => Ser;
	public bool ShouldSerializeCode_Siz() => Ser;
	public bool ShouldSerializeMei_Siz() => Ser;

	readonly static public string ViewSql = """
select * from (
select T.*, m1.Name as Mei_Col, m2.Name as  Mei_Siz
from MasterShohinColSiz T
left join MasterMeisho m1 on T.id_MeiCol = m1.Id
left join MasterMeisho m2 on T.id_MeiSiz = m2.Id
) as Vw_MasterShohinColSiz
""";
 }
 
 */



