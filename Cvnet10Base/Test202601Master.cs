using CommunityToolkit.Mvvm.ComponentModel;
using Cvnet10Base.Share;
using Newtonsoft.Json.Linq;
using NPoco;
using System.Xml.Linq;

namespace Cvnet10Base {
	/// <summary>
	/// テスト用商品マスター
	/// </summary>
	[PrimaryKey("Id", AutoIncrement = true)]
	public partial class Test202601Master: BaseDbHasAddress, IBaseCodeName,IBaseGetListSql {
		/// <summary>
		/// コード
		/// </summary>
		[ObservableProperty]
		[property: ColumnSizeDml(12)]
		string code = "";
		/// <summary>
		/// 名前
		/// </summary>
		[ObservableProperty]
		[property: ColumnSizeDml(80)]
		string? name;
		/// <summary>
		/// 略称
		/// </summary>
		[ObservableProperty]
		[property: ColumnSizeDml(100)]
		string? ryaku;
		/// <summary>
		/// カナ
		/// </summary>
		[ObservableProperty]
		[property: ColumnSizeDml(100)]
		string? kana;
		/// <summary>
		/// ブランド
		/// </summary>
		[ObservableProperty]
		long id_MeiBrand;
		/// <summary>
		/// アイテム
		/// </summary>
		[ObservableProperty]
		long id_MeiItem;
		/// <summary>
		/// 展示会
		/// </summary>
		[ObservableProperty]
		long id_MeiTenji;
		/// <summary>
		/// 色サイズリスト
		/// </summary>
		[ObservableProperty]
		[property: SerializedColumn]
		[property: ColumnSizeDml(1000)]
		List<MasterShohinColSiz>? jcolsiz;
		/// <summary>
		/// ブランド名
		/// </summary>
		[ObservableProperty]
		[property: ColumnSizeDml(100)]
		[property: ResultColumn]
		string? mei_Brand;
		/// <summary>
		/// アイテム名
		/// </summary>
		[ObservableProperty]
		[property: ColumnSizeDml(100)]
		[property: ResultColumn]
		string? mei_Item;
		/// <summary>
		/// 展示会名
		/// </summary>
		[ObservableProperty]
		[property: ColumnSizeDml(100)]
		[property: ResultColumn]
		string? mei_Tenji;


		readonly static string listSql = """
select T.*, m1.Name as Mei_Brand, m2.Name as  Mei_Item, m3.Name as  Mei_Tenji
from Test202601Master T
left join MasterMeisho m1 on T.Id_MeiBrand = m1.Id
left join MasterMeisho m2 on T.Id_MeiItem = m2.Id
left join MasterMeisho m3 on T.Id_MeiTenji = m3.Id
order by T.Id
""";
	readonly static string listSqlForJcolsiz = """
	SELECT
		m.Id,
		m.Code, -- PRD001等
		m.Name, -- ブランドA...等
		-- JSON内の各フィールドを展開
		json_extract(value, '$.Id_MeiCol') AS Id_MeiCol,
		json_extract(value, '$.Id_MeiSiz') AS Id_MeiSiz,
		json_extract(value, '$.Mei_Col')   AS ColorName,
		json_extract(value, '$.Mei_Siz')   AS SizeName,
		json_extract(value, '$.Jan2')      AS SizeCode
	FROM
		Test202601Master m,
		json_each(m.Jcolsiz);
""";
		public string GetListSql() {
			return listSql;
		}
	}


}
