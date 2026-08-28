using CvBase;
using CvBaseSqlite;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

[TestClass]
public class SysPermissionProfileDefaultDataTests {
	private ExDatabaseSqlite? _db;

	private ExDatabaseSqlite Db => _db ?? throw new AssertFailedException("Database not initialized");

	[TestInitialize]
	public void Initialize() {
		var connection = new SqliteConnection("Data Source=:memory:");
		connection.Open();
		_db = new ExDatabaseSqlite(connection);
		Db.CreateTable(typeof(SysPermissionProfile), true, false);
		Db.CreateTable(typeof(SysPermissionProfileDetail), true, false);
	}

	[TestCleanup]
	public void Cleanup() {
		_db?.Close();
		(_db?.Connection as SqliteConnection)?.Close();
	}

	[TestMethod]
	public void CreateDefaultData_EmptyPermissionTables_InsertsDefaultProfilesAndDetails() {
		SysPermissionProfile.CreateDefaultData(Db);

		Assert.AreEqual(4, Db.Fetch<SysPermissionProfile>("").Count, "標準プロファイル4件を登録する");
		Assert.AreEqual(11, Db.Fetch<SysPermissionProfileDetail>("").Count, "標準権限明細11件を登録する");
	}

}
