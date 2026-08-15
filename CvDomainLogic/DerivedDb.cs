using CvAsset;
using CvBase;
using CvBase.Share;

namespace CvDomainLogic;

/// <summary>
/// 派生テーブル(<see cref="IDerivedOrigin.DerivedClass"/>)の展開を行う。
/// <para>
/// 元テーブルの追加・更新・削除に追随して、派生テーブルの行を作り直す。
/// 展開SQLは派生クラス側の <c>InsertSql</c> / <c>DeleteSql</c> 定数が持つ(元テーブルのIdを@0で受ける)。
/// 呼び出し元(CvServer)が張ったトランザクション内で実行される前提。
/// </para>
/// </summary>
public sealed class DerivedDb(ExDatabase db) {
	private readonly ExDatabase _db = db;

	/// <summary>
	/// 追加時の展開を行う(Id指定)。<paramref name="item"/> が <see cref="IDerivedOrigin"/> でなければ何もしない。
	/// </summary>
	/// <returns>展開した行数</returns>
	public int Insert(object item, long id) {
		if (item is not IDerivedOrigin origin) {
			return 0;
		}
		var insertSql = Common.GetRequiredSql(origin.DerivedClass, "InsertSql");
		return _db.Execute(insertSql, id);
	}

	/// <summary>
	/// 更新時の展開を行う(Id指定)。削除してから再展開する。
	/// </summary>
	/// <returns>削除と展開を合わせた行数</returns>
	public int Update(object item, long id) {
		if (item is not IDerivedOrigin origin) {
			return 0;
		}
		var deleteSql = Common.GetRequiredSql(origin.DerivedClass, "DeleteSql");
		var insertSql = Common.GetRequiredSql(origin.DerivedClass, "InsertSql");
		var cnt = _db.Execute(deleteSql, id);
		return cnt + _db.Execute(insertSql, id);
	}

	/// <summary>
	/// 削除時の展開解除を行う(Id指定)。
	/// </summary>
	/// <returns>削除した行数</returns>
	public int Delete(object item, long id) {
		if (item is not IDerivedOrigin origin) {
			return 0;
		}
		var deleteSql = Common.GetRequiredSql(origin.DerivedClass, "DeleteSql");
		return _db.Execute(deleteSql, id);
	}
}
