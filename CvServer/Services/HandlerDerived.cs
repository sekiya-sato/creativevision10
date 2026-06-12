using CvAsset;
using CvBase;
using CvBase.Share;

namespace CvServer.Services;

public class HandlerDerived(ExDatabase db) {
	private readonly ExDatabase _db = db;
	/// <summary>
	/// 追加時の派生classに対する処理を行う(Id指定)
	/// </summary>
	/// <param name="itemType"></param>
	/// <param name="item"></param>
	/// <param name="id"></param>
	public void Insert(Type itemType, object item, long id) {
		if (item is not IDerivedOrigin origin) {
			return;
		}
		var insertSql = Common.GetRequiredSql(origin.DerivedClass, "InsertSql");
		_db.Execute(insertSql, id);
	}
	/// <summary>
	/// 更新時の派生classに対する処理を行う(Id指定)
	/// </summary>
	/// <param name="itemType"></param>
	/// <param name="item"></param>
	/// <param name="id"></param>
	public void Update(Type itemType, object item, long id) {
		if (item is not IDerivedOrigin origin) {
			return;
		}
		var deleteSql = Common.GetRequiredSql(origin.DerivedClass, "DeleteSql");
		var insertSql = Common.GetRequiredSql(origin.DerivedClass, "InsertSql");
		_db.Execute(deleteSql, id);
		_db.Execute(insertSql, id);
	}
	/// <summary>
	/// 削除時の派生classに対する処理を行う(Id指定)
	/// </summary>
	/// <param name="itemType"></param>
	/// <param name="item"></param>
	/// <param name="id"></param>
	public void Delete(Type itemType, object item, long id) {
		if (item is not IDerivedOrigin origin) {
			return;
		}
		var deleteSql = Common.GetRequiredSql(origin.DerivedClass, "DeleteSql");
		_db.Execute(deleteSql, id);
	}

}
