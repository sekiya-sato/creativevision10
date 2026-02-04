using CommunityToolkit.Mvvm.ComponentModel;
using Cvnet10Base.Share;
using Newtonsoft.Json.Linq;
using NPoco;
using System.Diagnostics.Metrics;
using System.Xml.Linq;

namespace Cvnet10Base;
/// <summary>
/// テスト用商品マスター
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
public partial class Test202601Master : BaseDbHasAddress, IBaseCodeName, IBaseGetViewDefinition {
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

	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(1000)]
	List<MasterGeneralMeisho>? listMeisho;

	readonly static string viewSql = """
select T.*, m1.Name as Mei_Brand, m2.Name as  Mei_Item, m3.Name as  Mei_Tenji
from Test202601Master T
left join MasterMeisho m1 on T.Id_MeiBrand = m1.Id
left join MasterMeisho m2 on T.Id_MeiItem = m2.Id
left join MasterMeisho m3 on T.Id_MeiTenji = m3.Id
order by T.Id
""";
	/*
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
	*/
	public string GetViewDefinition() {
		return viewSql;
	}

}


public partial class Test202601MasterJan : Test202601Master {

	/// <summary>
	/// 色
	/// </summary>
	[ObservableProperty]
	long id_MeiCol;
	/// <summary>
	/// サイズ
	/// </summary>
	[ObservableProperty]
	long id_MeiSiz;
	/// <summary>
	/// JANコード1
	/// </summary>
	[ObservableProperty]
	string? jan1;
	/// <summary>
	/// JANコード2
	/// </summary>
	[ObservableProperty]
	string? jan2;
	/// <summary>
	/// JANコード3
	/// </summary>
	[ObservableProperty]
	string? jan3;

	/// <summary>
	/// カラー名
	/// </summary>
	[ObservableProperty]
	[property: ResultColumn]
	string? mei_Col;
	[ObservableProperty]
	[property: ResultColumn]
	/// <summary>
	/// サイズ名
	/// </summary>
	string? mei_Siz;

}




/// <summary>
/// Test202601Master の拡張メソッド
/// </summary>
public static class Test202601MasterExtensions {

	/// <summary>
	/// jcolsiz 内の Mei_Col / Mei_Siz を MasterMeisho から取得してセット
	/// </summary>
	/// <param name="master">対象のマスタ</param>
	/// <param name="db">NPoco データベース</param>
	/// <param name="serializeMeisho">名称をシリアライズに含めるか (デフォルト: false)</param>
	public static void LoadJcolsizMeishoNames(
		this Test202601Master master,
		IDatabase db,
		bool serializeMeisho = false) {

		if (master.Jcolsiz == null || master.Jcolsiz.Count == 0) return;

		// ID のリストを取得
		var colIds = master.Jcolsiz.Select(x => x.Id_MeiCol).Where(x => x > 0).Distinct().ToList();
		var sizIds = master.Jcolsiz.Select(x => x.Id_MeiSiz).Where(x => x > 0).Distinct().ToList();
		var allIds = colIds.Union(sizIds).ToList();

		if (allIds.Count == 0) return;

		// MasterMeisho を一度だけ取得 (Dictionary でキャッシュ)
		var meishoDict = db.Fetch<MasterMeisho>(
			"SELECT * FROM MasterMeisho WHERE Id IN (@0)",
			allIds
		).ToDictionary(x => x.Id);

		// 各 MasterShohinColSiz に名称をセット
		foreach (var item in master.Jcolsiz) {
			if (item.Id_MeiCol > 0 && meishoDict.TryGetValue(item.Id_MeiCol, out var colMei)) {
				item.Code_Col = colMei.Code;
				item.Mei_Col = colMei.Name;
			}
			if (item.Id_MeiSiz > 0 && meishoDict.TryGetValue(item.Id_MeiSiz, out var sizMei)) {
				item.Code_Siz = sizMei.Code;
				item.Mei_Siz = sizMei.Name;
			}

			// ✅ シリアライズ設定を適用
			item.Ser = serializeMeisho;
		}
	}

	/// <summary>
	/// 複数の Test202601Master の jcolsiz 名称を一括設定 (パフォーマンス最適化版)
	/// </summary>
	/// <param name="masters">対象のマスタリスト</param>
	/// <param name="db">NPoco データベース</param>
	/// <param name="serializeMeisho">名称をシリアライズに含めるか (デフォルト: false)</param>
	public static void LoadAllJcolsizMeishoNames(
		this IEnumerable<Test202601Master> masters,
		IDatabase db,
		bool serializeMeisho = false) {

		var masterList = masters.Where(m => m.Jcolsiz != null && m.Jcolsiz.Count > 0).ToList();
		if (masterList.Count == 0) return;

		// 全レコードから ID を収集
		var allColIds = masterList.SelectMany(m => m.Jcolsiz!).Select(x => x.Id_MeiCol).Where(x => x > 0).Distinct();
		var allSizIds = masterList.SelectMany(m => m.Jcolsiz!).Select(x => x.Id_MeiSiz).Where(x => x > 0).Distinct();
		var allIds = allColIds.Union(allSizIds).ToList();

		if (allIds.Count == 0) return;

		// MasterMeisho を一度だけ取得 (全レコード分)
		var meishoDict = db.Fetch<MasterMeisho>(
			"SELECT * FROM MasterMeisho WHERE Id IN (@0)",
			allIds
		).ToDictionary(x => x.Id);

		// 各マスタの jcolsiz に名称をセット
		foreach (var master in masterList) {
			foreach (var item in master.Jcolsiz!) {
				if (item.Id_MeiCol > 0 && meishoDict.TryGetValue(item.Id_MeiCol, out var colMei)) {
					item.Code_Col = colMei.Code;
					item.Mei_Col = colMei.Name;
				}
				if (item.Id_MeiSiz > 0 && meishoDict.TryGetValue(item.Id_MeiSiz, out var sizMei)) {
					item.Code_Siz = sizMei.Code;
					item.Mei_Siz = sizMei.Name;
				}

				// ✅ シリアライズ設定を適用
				item.Ser = serializeMeisho;
			}
		}
	}
	/// <summary>
	/// jcolsiz 内の KubunName / Name を MasterMeisho から取得してセット
	/// </summary>
	/// <param name="master">対象のマスタ</param>
	/// <param name="db">NPoco データベース</param>
	/// <param name="serializeMeisho">名称をシリアライズに含めるか (デフォルト: false)</param>
	public static void LoadGeneralMeishoNames(
		this Test202601Master master,
		IDatabase db,
		bool serializeMeisho = false) {

		if (master.ListMeisho == null || master.ListMeisho.Count == 0) {
			master.ListMeisho =
				[
					..Enumerable.Range(1, 10)
						.Select((Func<int, MasterGeneralMeisho>)(i => new MasterGeneralMeisho { Kubun = $"B{i:D2}" }))
				];
		}

		// ID のリストを取得
		var kubuns = master.ListMeisho.Select(x => x.Kubun).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
		var names = master.ListMeisho.Select(x => x.Id_MeiCode).Where(x => x > 0).Distinct().ToList();


		// MasterMeisho を一度だけ取得 (Dictionary でキャッシュ)
		var meishoKubuns = db.Fetch<MasterMeisho>(
			"SELECT * FROM MasterMeisho WHERE Kubun = 'IDX' and Code IN (@0)",
			kubuns
		).ToDictionary(x => x.Code);
		var meishoNames = db.Fetch<MasterMeisho>(
			"SELECT * FROM MasterMeisho WHERE Id IN (@0)",
			names
		).ToDictionary(x => x.Id);

		// 各 MasterShohinColSiz に名称をセット
		foreach (var item in master.ListMeisho) {
			if (!string.IsNullOrEmpty(item.Kubun) && meishoKubuns.TryGetValue(item.Kubun, out var codeKubun))
				item.KubunName = codeKubun.Name;
			if (item.Id_MeiCode > 0 && meishoNames.TryGetValue(item.Id_MeiCode, out var nameMei)) {
				item.Code = nameMei.Code;
				item.Name = nameMei.Name;
			}
			item.Ser = serializeMeisho;
		}
	}

	/// <summary>
	/// 複数の Test202601Master の ListMeisho 名称を一括設定 (パフォーマンス最適化版)
	/// </summary>
	/// <param name="masters">対象のマスタリスト</param>
	/// <param name="db">NPoco データベース</param>
	/// <param name="serializeMeisho">名称をシリアライズに含めるか (デフォルト: false)</param>
	public static void LoadAllGeneralMeishoNames(
		this IEnumerable<Test202601Master> masters,
		IDatabase db,
		bool serializeMeisho = false) {

		foreach(var list in masters) {
			if (list.ListMeisho == null || list.ListMeisho.Count == 0) {
				list.ListMeisho =
					[
						..Enumerable.Range(1, 10)
							.Select((Func<int, MasterGeneralMeisho>)(i => new MasterGeneralMeisho { Kubun = $"B{i:D2}" }))
					];
			}
		}

		var masterList = masters.Where(m => m.ListMeisho != null && m.ListMeisho.Count > 0).ToList();
		if (masterList.Count == 0) return;

		// 全レコードから ID を収集
		var allColIds = masterList.SelectMany(m => m.Jcolsiz!).Select(x => x.Id_MeiCol).Where(x => x > 0).Distinct();
		var allSizIds = masterList.SelectMany(m => m.Jcolsiz!).Select(x => x.Id_MeiSiz).Where(x => x > 0).Distinct();
		var allIds = allColIds.Union(allSizIds).ToList();

		var listMeishoSource = masterList.SelectMany(
			m => m.ListMeisho ?? Enumerable.Empty<MasterGeneralMeisho>());

		var kubuns = listMeishoSource.Select(x => x.Kubun)
			.Where(x => !string.IsNullOrEmpty(x))
			.Distinct();
		var names = listMeishoSource.Select(x => x.Id_MeiCode)
			.Where(x => x > 0)
			.Distinct();


		// MasterMeisho を一度だけ取得 (Dictionary でキャッシュ)
		var meishoKubuns = db.Fetch<MasterMeisho>(
			"SELECT * FROM MasterMeisho WHERE Kubun = 'IDX' and Code IN (@0)",
			kubuns
		).ToDictionary(x => x.Code);
		var meishoNames = db.Fetch<MasterMeisho>(
			"SELECT * FROM MasterMeisho WHERE Id IN (@0)",
			names
		).ToDictionary(x => x.Id);

		// 各マスタの jcolsiz に名称をセット
		foreach (var master in masterList) {
			if(master.ListMeisho == null) continue;
			foreach (var item in master.ListMeisho) {
				if (!string.IsNullOrEmpty(item.Kubun) && meishoKubuns.TryGetValue(item.Kubun, out var codeKubun))
					item.KubunName = codeKubun.Name;
				if (item.Id_MeiCode > 0 && meishoNames.TryGetValue(item.Id_MeiCode, out var nameMei)) {
					item.Code = nameMei.Code;
					item.Name = nameMei.Name;
				}
				item.Ser = serializeMeisho;
			}
		}
	}


}
