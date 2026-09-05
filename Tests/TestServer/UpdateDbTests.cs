using CvBase;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Threading.Tasks;

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

/// <summary>
/// 原価4項目 Step3 (2026-09-05_原価4項目_詳細設計.md §2.5.7〜§2.5.10) の <see cref="UpdateDb"/> 新バージョン(26_09_06_01)適用検証。
/// 移行前(列追加前)のスキーマを模した最小テーブルへ実際にバージョンアップSQLを適用し、
/// 新列が既存業務動作を維持する初期値(既定値)で補完されることを確認する。
/// </summary>
[TestClass]
public class UpdateDbCost4ItemsStep3Tests {
	private ExDatabaseSqlite? _db;

	private ExDatabaseSqlite Db => _db ?? throw new AssertFailedException("Database not initialized");

	[TestInitialize]
	public void Initialize() {
		var connection = new SqliteConnection("Data Source=:memory:");
		connection.Open();
		_db = new ExDatabaseSqlite(connection);
		// SysUpdateDbはUpdateDb.WriteVersionInfoAsyncが読み書きするため現行スキーマで作成する
		Db.CreateTable(typeof(SysUpdateDb), true, false);
		// 対象3テーブルは列追加(ALTER TABLE ADD COLUMN)前の最小スキーマを模して直接作成する
		Db.Execute("CREATE TABLE MasterSysman (Id INTEGER PRIMARY KEY AUTOINCREMENT);");
		Db.Execute("INSERT INTO MasterSysman (Id) VALUES (1);");
		Db.Execute("CREATE TABLE MasterShohin (Id INTEGER PRIMARY KEY AUTOINCREMENT);");
		Db.Execute("INSERT INTO MasterShohin (Id) VALUES (1);");
		Db.Execute("CREATE TABLE Tran03Shiire (Id INTEGER PRIMARY KEY AUTOINCREMENT);");
		Db.Execute("INSERT INTO Tran03Shiire (Id) VALUES (1);");
	}

	[TestCleanup]
	public void Cleanup() {
		_db?.Close();
		(_db?.Connection as SqliteConnection)?.Close();
	}

	/// <summary>
	/// バージョン26_09_03_01(直前バージョン)まで適用済みのDBに対して更新をかけると、
	/// 26_09_06_01のALTER TABLE群だけが実行され、既存行が現行動作を維持する初期値で補完されること。
	/// </summary>
	[TestMethod]
	public async Task WriteVersionInfoAsync_直前バージョンから適用_原価4項目Step3の列が既定値で追加される() {
		await Db.InsertAsync(new SysUpdateDb {
			DbVersion = 26_09_03_01,
			DateStart = DateTime.Now.ToString("yyyyMMddHHmmss"),
			Sql = "",
			Memo = "テスト用の直前バージョン",
			PreVersion = 26_09_03_01,
		});

		await UpdateDb.WriteVersionInfoAsync(Db);

		dynamic? sysman = Db.FirstOrDefault<dynamic>("SELECT CostMethod FROM MasterSysman WHERE Id = 1");
		Assert.IsNotNull(sysman);
		Assert.AreEqual(0L, (long)sysman!.CostMethod, "MasterSysman.CostMethodは既存行を0(固定原価)で補完すること");

		dynamic? shohin = Db.FirstOrDefault<dynamic>("SELECT PurchaseType, Id_ConsignmentShiire, VConsignmentShiire, ConsumptionCalcType, ConsumptionRateBasisPoints, ConsumptionRoundingUnit, ConsumptionRounding FROM MasterShohin WHERE Id = 1");
		Assert.IsNotNull(shohin);
		Assert.AreEqual(0L, (long)shohin!.PurchaseType, "MasterShohin.PurchaseTypeは0(通常仕入)で補完すること");
		Assert.AreEqual(0L, (long)shohin.Id_ConsignmentShiire);
		Assert.AreEqual("{}", (string)shohin.VConsignmentShiire, "VConsignmentShiireは空のJSONオブジェクトで補完すること");
		Assert.AreEqual(0L, (long)shohin.ConsumptionCalcType);
		Assert.AreEqual(0L, (long)shohin.ConsumptionRateBasisPoints);
		Assert.AreEqual(1L, (long)shohin.ConsumptionRoundingUnit, "ConsumptionRoundingUnitは既定1円で補完すること");
		Assert.AreEqual(0L, (long)shohin.ConsumptionRounding);

		dynamic? shiire = Db.FirstOrDefault<dynamic>("SELECT IsStock, GeneratedKind FROM Tran03Shiire WHERE Id = 1");
		Assert.IsNotNull(shiire);
		Assert.AreEqual(1L, (long)shiire!.IsStock, "既存仕入はIsStock=1で補完し在庫・買掛集計結果を維持すること");
		Assert.AreEqual(0L, (long)shiire.GeneratedKind, "既存仕入はGeneratedKind=0(手動・通常)で補完すること");

		var latest = await Db.FirstOrDefaultAsync<SysUpdateDb>("order by DbVersion desc");
		Assert.IsNotNull(latest);
		Assert.AreEqual(26_09_06_01, latest.DbVersion, "DBバージョンが26_09_06_01まで進むこと");
		Assert.IsTrue(string.IsNullOrEmpty(latest.Memo) || !latest.Memo!.Contains("Error", StringComparison.OrdinalIgnoreCase), $"バージョンアップSQLがエラーなく実行されること: {latest.Memo}");
	}

	/// <summary>
	/// <see cref="DefineDataTable.TableTypes"/> へ登録した新規3テーブルが、DefineDataTableのDDL生成器で
	/// 実際に作成できること（原価4項目 詳細設計 §2.5.10「新規テーブルの作成はDefineDataTableへの型登録で
	/// 全方言のDDL生成器を通る」）。3DB分言のDDL生成一致は Tests/TestSqlDialect/DdlSnapshotTests.cs が
	/// DefineDataTable.TableTypes全件を対象に検証済みのため、ここではSQLiteで実際にテーブルが作成でき、
	/// 期待する列を持つことだけを確認する。
	/// </summary>
	[TestMethod]
	public void DefineDataTable_原価4項目Step3の新規3テーブルをSQLiteへ作成できる() {
		using var connection = new SqliteConnection("Data Source=:memory:");
		connection.Open();
		using var db = new ExDatabaseSqlite(connection);

		Assert.IsTrue(db.CreateTable(typeof(TranGenka), true, false));
		Assert.IsTrue(db.CreateTable(typeof(TranConsumptionPurchaseLink), true, false));
		Assert.IsTrue(db.CreateTable(typeof(TranGenkaReval), true, false));

		var genkaColumns = db.GetSqlColumns(typeof(TranGenka)).Select(c => c.Trim().Split(' ')[0]).ToList();
		foreach (var expected in new[] { "BatchId", "SumMonth", "EffectiveDay", "CostMethod", "ChangeKind", "SourceRevalId", "Id_Shohin", "VShohin", "BeforeCost", "AfterCost", "OpeningQty", "OpeningAmount", "PurchaseQty", "PurchaseAmount", "SundryAmount", "SourceTranId", "SourceLineNo", "Id_Shain", "VShain" }) {
			Assert.IsTrue(genkaColumns.Contains(expected), $"TranGenkaに{expected}列が無い");
		}

		var linkColumns = db.GetSqlColumns(typeof(TranConsumptionPurchaseLink)).Select(c => c.Trim().Split(' ')[0]).ToList();
		foreach (var expected in new[] { "BatchId", "SourceType", "SourceId", "SourceLineNo", "SourceDay", "SourceVdu", "GeneratedShiireId", "GeneratedLineNo", "Id_Shohin", "Id_Shiire" }) {
			Assert.IsTrue(linkColumns.Contains(expected), $"TranConsumptionPurchaseLinkに{expected}列が無い");
		}

		var revalColumns = db.GetSqlColumns(typeof(TranGenkaReval)).Select(c => c.Trim().Split(' ')[0]).ToList();
		foreach (var expected in new[] { "BatchId", "SumMonth", "EffectiveDay", "ApplyPoint", "CostMethod", "Method", "RatePercent", "FixedCost", "RoundingUnit", "Rounding", "GroupKey", "JCond", "TargetCount", "TargetQty", "JodaiAmount", "BeforeAmount", "AfterAmount", "Status", "Id_Shain", "VShain" }) {
			Assert.IsTrue(revalColumns.Contains(expected), $"TranGenkaRevalに{expected}列が無い");
		}

		db.Close();
	}
}
