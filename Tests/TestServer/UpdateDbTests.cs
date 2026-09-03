using CvBase;
using CvBaseSqlite;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

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

/// <summary>
/// MasterConfig一元化: CreateDefaultDataが「不足行のみ追加」方式であることの検証。
/// JodaiKeepDays 1行 + 自動実行ジョブ7件×2行(実行フラグ・cron式) = 15行が候補になる。
/// </summary>
[TestClass]
public class MasterConfigAutoExecDefaultDataTests {
	private ExDatabaseSqlite? _db;

	private ExDatabaseSqlite Db => _db ?? throw new AssertFailedException("Database not initialized");

	[TestInitialize]
	public void Initialize() {
		var connection = new SqliteConnection("Data Source=:memory:");
		connection.Open();
		_db = new ExDatabaseSqlite(connection);
		Db.CreateTable(typeof(MasterConfig), true, false);
	}

	[TestCleanup]
	public void Cleanup() {
		_db?.Close();
		(_db?.Connection as SqliteConnection)?.Close();
	}

	/// <summary>
	/// 空のテーブルに対しては、JodaiKeepDays 1行 + 自動実行ジョブ7件×2行(実行フラグ・cron式) = 15行を
	/// すべてInsertすること。
	/// </summary>
	[TestMethod]
	public void CreateDefaultData_EmptyTable_InsertsFifteenRows() {
		var inserted = MasterConfig.CreateDefaultData(Db);

		Assert.AreEqual(15, inserted.Count, "JodaiKeepDays 1行 + 自動実行ジョブ7件×2行 = 15行を挿入すること");
		Assert.AreEqual(15, Db.Fetch<MasterConfig>("").Count);
	}

	/// <summary>
	/// 自動実行ジョブの実行フラグ行(Category=自動実行管理)が7件×2=14行登録され、
	/// 既存4ジョブ(WalCheckpoint/WorkFileCleanup/MonthlyResummary/JodaiPurge)="1"、
	/// 新規3ジョブ(MasterShohinMeishoRebuild/MasterVColumnResync/TranTaxRebuild)="0"であること。
	/// </summary>
	[TestMethod]
	public void CreateDefaultData_AutoExecJobRows_HaveExpectedEnabledDefaults() {
		MasterConfig.CreateDefaultData(Db);

		var autoExecRows = Db.Fetch<MasterConfig>("WHERE Category = @0", MasterConfig.CategoryAutoExec);
		Assert.AreEqual(14, autoExecRows.Count, "自動実行ジョブ7件×2行(実行フラグ・cron式)=14行であること");

		foreach (var job in MasterConfig.AutoExecJobDefaults) {
			var enabledRow = autoExecRows.Single(r => r.Name == MasterConfig.AutoExecEnabledName(job.TaskId));
			Assert.AreEqual(job.Enabled, enabledRow.Val, $"{job.TaskName} の実行フラグ既定値が一致しない");

			var cronRow = autoExecRows.Single(r => r.Name == MasterConfig.AutoExecCronName(job.TaskId));
			Assert.AreEqual(job.Cron, cronRow.Val, $"{job.TaskName} の既定cron式が一致しない");
		}
	}

	/// <summary>
	/// 2回目の呼び出しでは不足行が無いため、重複行を追加せず戻り値も空リストであること。
	/// </summary>
	[TestMethod]
	public void CreateDefaultData_CalledTwice_SecondCallInsertsNothing() {
		MasterConfig.CreateDefaultData(Db);
		var secondResult = MasterConfig.CreateDefaultData(Db);

		Assert.AreEqual(0, secondResult.Count, "2回目は不足行が無いため空リストであること");
		Assert.AreEqual(15, Db.Fetch<MasterConfig>("").Count, "2回目の呼び出しで行が増えないこと");
	}

}
