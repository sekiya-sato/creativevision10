using CvAsset;
using CvBase;
using CvBase.Share;

namespace CvServer.Services;

public class HandleDerived(ExDatabase db) {
	private readonly ExDatabase _db = db;

	public void Insert(Type itemType, object item) {
		if (item is not IDerivedOrigin origin) {
			return;
		}
		var id = Common.GetId(item);
		var insertSql = Common.GetRequiredSql(origin.DerivedClass, "InsertSql");
		_db.Execute(insertSql, id);
	}

	public void Update(Type itemType, object item) {
		if (item is not IDerivedOrigin origin) {
			return;
		}
		var id = Common.GetId(item);
		var deleteSql = Common.GetRequiredSql(origin.DerivedClass, "DeleteSql");
		var insertSql = Common.GetRequiredSql(origin.DerivedClass, "InsertSql");

		_db.Execute(deleteSql, id);
		_db.Execute(insertSql, id);
	}

	public void Delete(Type itemType, object item) {
		if (item is not IDerivedOrigin origin) {
			return;
		}
		var id = Common.GetId(item);
		var deleteSql = Common.GetRequiredSql(origin.DerivedClass, "DeleteSql");
		_db.Execute(deleteSql, id);
	}

}
