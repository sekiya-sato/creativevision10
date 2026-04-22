using CvBase;
using CvBase.Share;
using System.Reflection;

namespace CvServer.Services;

public class HandleDerived(ExDatabase db) {
	private readonly ExDatabase _db = db;

	public void Insert(Type itemType, object item) {
		if (item is not IDerivedOrigin origin) {
			return;
		}
		var id = GetId(item);
		var insertSql = GetRequiredSql(origin.DerivedClass, "InsertSql");
		_db.Execute(insertSql, id);
	}

	public void Update(Type itemType, object item) {
		if (item is not IDerivedOrigin origin) {
			return;
		}
		var id = GetId(item);
		var deleteSql = GetRequiredSql(origin.DerivedClass, "DeleteSql");
		var insertSql = GetRequiredSql(origin.DerivedClass, "InsertSql");

		_db.Execute(deleteSql, id);
		_db.Execute(insertSql, id);
	}

	public void Delete(Type itemType, object item) {
		if (item is not IDerivedOrigin origin) {
			return;
		}
		var id = GetId(item);
		var deleteSql = GetRequiredSql(origin.DerivedClass, "DeleteSql");
		_db.Execute(deleteSql, id);
	}

	private static string GetRequiredSql(Type derivedClassType, string propertyName) {
		var property = derivedClassType.GetProperty(propertyName);
		if (property?.GetValue(null) is not string sql || string.IsNullOrWhiteSpace(sql)) {
			throw new InvalidOperationException($"{derivedClassType.FullName}.{propertyName} が定義されていません。");
		}
		return sql;
	}

	private static object GetId(object item) {
		var idProperty = item.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
		if (idProperty == null) {
			throw new InvalidOperationException($"{item.GetType().FullName}.Id が見つかりません。");
		}
		return idProperty.GetValue(item)
			?? throw new InvalidOperationException($"{item.GetType().FullName}.Id が null です。");
	}
}
