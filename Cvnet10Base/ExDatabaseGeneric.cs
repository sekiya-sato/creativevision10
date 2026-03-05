namespace Cvnet10DomainLogic;

public partial class ExDatabase {
	public string GetSqlCreateTable<T>() {
		return GetSqlCreateTable(typeof(T));
	}
	public string GetTableName<T>() {
		return GetTableName(typeof(T));
	}
	public virtual List<string> GetSqlColumns<T>() {
		return GetSqlColumns(typeof(T));
	}
	public bool IsExistTable<T>() {
		return IsExistTable(typeof(T));
	}
	public bool CreateTable<T>(bool isForce = false, bool isCreateIndex = true) {
		return CreateTable(typeof(T), isForce, isCreateIndex);
	}
	public void DropTable<T>() {
		DropTable(typeof(T));
	}
	public int CreateComment<T>() {
		return CreateComment(typeof(T));
	}
}
