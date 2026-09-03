using CvBase;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
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
/// JodaiKeepDays 1行 + 自動実行ジョブ7件×3行(実行フラグ・cron式・メール送信フラグ) + メール共通設定9行 = 31行が候補になる。
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
	/// 空のテーブルに対しては、JodaiKeepDays 1行 + 自動実行ジョブ7件×3行(実行フラグ・cron式・メール送信フラグ) + メール共通設定9行 = 31行を
	/// すべてInsertすること。
	/// </summary>
	[TestMethod]
	public void CreateDefaultData_EmptyTable_InsertsThirtyOneRows() {
		var inserted = MasterConfig.CreateDefaultData(Db);

		Assert.AreEqual(31, inserted.Count, "JodaiKeepDays 1行 + 自動実行ジョブ7件×3行 + メール共通設定9行 = 31行を挿入すること");
		Assert.AreEqual(31, Db.Fetch<MasterConfig>("").Count);
		Assert.AreEqual(30, Db.Fetch<MasterConfig>("WHERE Category = @0", MasterConfig.CategoryAutoExec).Count);
	}

	/// <summary>
	/// 自動実行ジョブの設定行(Category=自動実行管理)が7件×3行登録され、
	/// 既存4ジョブ(WalCheckpoint/WorkFileCleanup/MonthlyResummary/JodaiPurge)="1"、
	/// 新規3ジョブ(MasterShohinMeishoRebuild/MasterVColumnResync/TranTaxRebuild)="0"であること。
	/// </summary>
	[TestMethod]
	public void CreateDefaultData_AutoExecJobRows_HaveExpectedEnabledDefaults() {
		MasterConfig.CreateDefaultData(Db);

		var autoExecRows = Db.Fetch<MasterConfig>("WHERE Category = @0", MasterConfig.CategoryAutoExec);
		Assert.AreEqual(30, autoExecRows.Count, "自動実行ジョブ7件×3行とメール共通設定9行で30行であること");

		foreach (var job in MasterConfig.AutoExecJobDefaults) {
			var enabledRow = autoExecRows.Single(r => r.Name == MasterConfig.AutoExecEnabledName(job.TaskId));
			Assert.AreEqual(job.Enabled, enabledRow.Val, $"{job.TaskName} の実行フラグ既定値が一致しない");

			var cronRow = autoExecRows.Single(r => r.Name == MasterConfig.AutoExecCronName(job.TaskId));
			Assert.AreEqual(job.Cron, cronRow.Val, $"{job.TaskName} の既定cron式が一致しない");

			var isSendMailRow = autoExecRows.Single(r => r.Name == MasterConfig.AutoExecIsSendMailName(job.TaskId));
			Assert.AreEqual(job.IsSendMail, isSendMailRow.Val, $"{job.TaskName} のメール送信フラグ既定値が一致しない");
			Assert.AreEqual(MasterConfig.ValAutoExecDisabled, isSendMailRow.Val, $"{job.TaskName} のメール送信フラグは無効であること");
		}
	}

	[TestMethod]
	public void CreateDefaultData_AutoExecMailCommonRows_HaveEmptyValues() {
		MasterConfig.CreateDefaultData(Db);
		var names = new[] {
			MasterConfig.NameAutoExecMailServerIp,
			MasterConfig.NameAutoExecMailServerPort,
			MasterConfig.NameAutoExecMailUserId,
			MasterConfig.NameAutoExecMailUserPass,
			MasterConfig.NameAutoExecMailSecurity,
			MasterConfig.NameAutoExecMailAuthMode,
			MasterConfig.NameAutoExecMailFromAddr,
			MasterConfig.NameAutoExecMailFromName,
			MasterConfig.NameAutoExecMailToAddr,
		};

		foreach (var name in names) {
			var row = Db.FirstOrDefault<MasterConfig>("WHERE Name = @0", name);
			Assert.IsNotNull(row, $"{name} が登録されること");
			Assert.AreEqual(MasterConfig.CategoryAutoExec, row.Category);
			Assert.AreEqual(string.Empty, row.Val);
			Assert.IsFalse(string.IsNullOrWhiteSpace(row.Example), $"{name} の日本語設定例があること");
			Assert.IsFalse(string.IsNullOrWhiteSpace(row.Memo), $"{name} の日本語説明があること");
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
		Assert.AreEqual(31, Db.Fetch<MasterConfig>("").Count, "2回目の呼び出しで行が増えないこと");
	}

	[TestMethod]
	public void CreateDefaultData_ExistingValues_DoesNotOverwrite() {
		const string existingValue = "既存の設定値";
		Db.Insert(new MasterConfig {
			Category = MasterConfig.CategoryAutoExec,
			Name = MasterConfig.NameAutoExecMailServerIp,
			Val = existingValue,
			Example = "既存の設定例",
			Memo = "既存の説明",
		});

		var inserted = MasterConfig.CreateDefaultData(Db);
		var existing = Db.FirstOrDefault<MasterConfig>("WHERE Name = @0", MasterConfig.NameAutoExecMailServerIp);

		Assert.AreEqual(30, inserted.Count, "既存行を除く不足30行だけ追加すること");
		Assert.IsNotNull(existing);
		Assert.AreEqual(existingValue, existing.Val, "既存値を上書きしないこと");
		Assert.AreEqual("既存の設定例", existing.Example, "既存の設定例を上書きしないこと");
		Assert.AreEqual("既存の説明", existing.Memo, "既存の説明を上書きしないこと");
	}

	[TestMethod]
	[DataRow("1", true)]
	[DataRow("true", true)]
	[DataRow("on", true)]
	[DataRow("0", false)]
	[DataRow("false", false)]
	[DataRow("off", false)]
	public void GetIsSendMail_SupportedBooleanValues_ReturnsExpected(string value, bool expected) {
		var taskId = Guid.Parse(MasterConfig.AutoExecTaskIdWalCheckpoint);
		Db.Insert(new MasterConfig {
			Category = MasterConfig.CategoryAutoExec,
			Name = MasterConfig.AutoExecIsSendMailName(taskId.ToString()),
			Val = value,
		});

		Assert.AreEqual(expected, new SchedulerJobConfigDb(Db).GetIsSendMail(taskId));
	}

	[TestMethod]
	public void GetIsSendMail_MissingOrInvalidValue_ReturnsNull() {
		var taskId = Guid.Parse(MasterConfig.AutoExecTaskIdWalCheckpoint);
		var configDb = new SchedulerJobConfigDb(Db);
		Assert.IsNull(configDb.GetIsSendMail(taskId));

		Db.Insert(new MasterConfig {
			Category = MasterConfig.CategoryAutoExec,
			Name = MasterConfig.AutoExecIsSendMailName(taskId.ToString()),
			Val = "不正値",
		});

		Assert.IsNull(configDb.GetIsSendMail(taskId));
	}

	[TestMethod]
	public void SetIsSendMail_UpsertsZeroOrOne() {
		var taskId = Guid.Parse(MasterConfig.AutoExecTaskIdWalCheckpoint);
		var configDb = new SchedulerJobConfigDb(Db);

		configDb.SetIsSendMail(taskId, true);
		Assert.AreEqual(MasterConfig.ValAutoExecEnabled, Db.FirstOrDefault<string>("SELECT Val FROM MasterConfig WHERE Name = @0", MasterConfig.AutoExecIsSendMailName(taskId.ToString())));

		configDb.SetIsSendMail(taskId, false);
		Assert.AreEqual(MasterConfig.ValAutoExecDisabled, Db.FirstOrDefault<string>("SELECT Val FROM MasterConfig WHERE Name = @0", MasterConfig.AutoExecIsSendMailName(taskId.ToString())));
	}

	[TestMethod]
	public void SetIsSendMail_システムジョブ以外のTaskId_動的追加タスクでも保存して読み戻せる() {
		// 動的追加タスクは MasterConfig に定義行を持たないが、TaskId先頭8桁のキーで同じように保存できる。
		var taskId = Guid.NewGuid();
		var configDb = new SchedulerJobConfigDb(Db);

		configDb.SetIsSendMail(taskId, true);

		Assert.IsTrue(configDb.GetIsSendMail(taskId));
		var row = Db.FirstOrDefault<MasterConfig>("WHERE Name = @0", MasterConfig.AutoExecIsSendMailName(taskId.ToString()));
		Assert.IsNotNull(row);
		Assert.AreEqual(MasterConfig.CategoryAutoExec, row.Category);
	}

	[TestMethod]
	public void RemoveIsSendMail_保存済みのフラグ行_削除して未設定に戻る() {
		var taskId = Guid.NewGuid();
		var configDb = new SchedulerJobConfigDb(Db);
		configDb.SetIsSendMail(taskId, true);

		configDb.RemoveIsSendMail(taskId);

		Assert.IsNull(configDb.GetIsSendMail(taskId));
		Assert.IsNull(Db.FirstOrDefault<MasterConfig>("WHERE Name = @0", MasterConfig.AutoExecIsSendMailName(taskId.ToString())));
	}

	[TestMethod]
	public void RemoveIsSendMail_フラグ行がない_例外にならず何もしない() {
		var configDb = new SchedulerJobConfigDb(Db);

		configDb.RemoveIsSendMail(Guid.NewGuid());
	}
}
