using CodeShare;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using CvDomainLogic;
using CvServer;
using CvServer.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NCrontab;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Tests.CvServer;

public class FakeWebHostEnvironment : IWebHostEnvironment {
	public string ApplicationName { get; set; } = "CvTests";
	public string EnvironmentName { get; set; } = "Development";
	public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
	public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
	public string WebRootPath { get; set; } = AppContext.BaseDirectory;
	public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}

[TestClass]
public class CoreServiceTests {
	private ExDatabaseSqlite? _db;
	private CoreService? _service;
	private NCrontab.Scheduler.Scheduler? _scheduler;
	private SchedulerService? _schedulerService;
	private ServiceProvider? _serviceProvider;

	[TestInitialize]
	public void Initialize() {
		// In-memory SQLite を準備
		var conn = new SqliteConnection("Data Source=:memory:");
		conn.Open();
		_db = new ExDatabaseSqlite(conn);

		// 必要な依存をダミーで作成
		var logger = NullLogger<CoreService>.Instance;
		var schedulerLogger = NullLogger<SchedulerService>.Instance;
		var config = new ConfigurationBuilder().AddInMemoryCollection([]).Build();
		var env = new FakeWebHostEnvironment();
		var httpAccessor = new HttpContextAccessor();
		var appInit = new AppGlobal();
		appInit.InitAsync(_db).Wait();
		// サービスを作成
		_service = new CoreService(logger, config, env, httpAccessor, _db,
			new PointOfSaleService(_db, NullLogger<PointOfSaleService>.Instance));
		_scheduler = new NCrontab.Scheduler.Scheduler(NullLogger<NCrontab.Scheduler.Scheduler>.Instance);
		_serviceProvider = new ServiceCollection()
			.AddSingleton<ExDatabase>(_db)
			.BuildServiceProvider();
		_schedulerService = new SchedulerService(schedulerLogger, _scheduler, _serviceProvider.GetRequiredService<IServiceScopeFactory>(), config, env);
	}

	[TestCleanup]
	public void Cleanup() {
		_db?.Close();
		(_db?.Connection as SqliteConnection)?.Close();
		_scheduler?.Dispose();
		_serviceProvider?.Dispose();
	}

	[TestMethod]
	public async Task CopyReply_ReturnsSamePayload() {
		var request = new CvMsg {
			Flag = CvFlag.Msg001_CopyReply,
			Code = 0,
			DataType = typeof(string),
			DataMsg = "hello-copy"
		};
		if (_service == null) {
			Assert.Fail("Service not initialized");
			return;
		}
		var result = await _service.QueryMsgAsync(request);

		Assert.IsNotNull(result);
		Assert.AreEqual(0, result.Code);
		Assert.AreEqual(request.Flag, result.Flag);
		Assert.AreEqual(request.DataType, result.DataType);
		Assert.AreEqual(request.DataMsg, result.DataMsg);
	}

	[TestMethod]
	public async Task GetVersion_ReturnsVersionInfoSerialized() {
		var request = new CvMsg {
			Flag = CvFlag.Msg002_GetVersion,
		};
		if (_service == null) {
			Assert.Fail("Service not initialized");
			return;
		}

		var result = await _service.QueryMsgAsync(request);

		Assert.IsNotNull(result);
		Assert.AreEqual(0, result.Code);
		Assert.AreEqual(request.Flag, result.Flag);
		Assert.AreEqual(typeof(InfoServer), result.DataType);
		Assert.IsFalse(string.IsNullOrWhiteSpace(result.DataMsg ?? ""));
		// JSON 解析は不要だが、空でないことを確認
	}

	[TestMethod]
	public async Task GetEnv_ReturnsDictionarySerialized() {
		var request = new CvMsg {
			Flag = CvFlag.Msg003_GetEnv,
		};
		if (_service == null) {
			Assert.Fail("Service not initialized");
			return;
		}

		var result = await _service.QueryMsgAsync(request);

		Assert.IsNotNull(result);
		Assert.AreEqual(0, result.Code);
		Assert.AreEqual(request.Flag, result.Flag);
		Assert.AreEqual(typeof(Dictionary<string, string>), result.DataType);
		Assert.IsFalse(string.IsNullOrWhiteSpace(result.DataMsg ?? ""));
	}

	[TestMethod]
	public async Task GetConnectionStatus_ReturnsConnectionStringKeysSerialized() {
		var request = new CvMsg {
			Flag = CvFlag.Msg004_GetConnectionStatus,
		};
		if (_service == null) {
			Assert.Fail("Service not initialized");
			return;
		}

		var result = await _service.QueryMsgAsync(request);

		Assert.IsNotNull(result);
		Assert.AreEqual(0, result.Code);
		Assert.AreEqual(request.Flag, result.Flag);
		Assert.AreEqual(typeof(List<string>), result.DataType);
		Assert.IsFalse(string.IsNullOrWhiteSpace(result.DataMsg ?? ""));
	}

	[TestMethod]
	public async Task QueryListSql_WithSummaryClosingCheckRow_ResolvesSharedDtoOnServer() {
		var service = _service ?? throw new AssertFailedException("Service not initialized");
		var request = new CvMsg {
			Flag = CvFlag.Msg101_Op_Query,
			DataType = typeof(QueryListSqlParam),
			DataMsg = Common.SerializeObject(new QueryListSqlParam(
				typeof(SummaryClosingCheckRow),
				"SELECT 'T001' AS TorihikiCode, '20260228' AS DayTo, 31 AS Shime1"))
		};

		var result = await service.QueryMsgAsync(request);
		var rows = Common.DeserializeObject(result.DataMsg ?? "[]", result.DataType) as List<SummaryClosingCheckRow>;

		Assert.AreEqual(0, result.Code);
		Assert.AreEqual(typeof(List<SummaryClosingCheckRow>), result.DataType);
		Assert.IsNotNull(rows);
		Assert.AreEqual(1, rows.Count);
		Assert.AreEqual("T001", rows[0].TorihikiCode);
		Assert.AreEqual("20260228", rows[0].DayTo);
		Assert.AreEqual(31, rows[0].Shime1);
	}

	[TestMethod]
	public async Task QueryById_WithStaleVdu_ReturnsConcurrentUpdate() {
		var db = _db ?? throw new AssertFailedException("Database not initialized");
		var service = _service ?? throw new AssertFailedException("Service not initialized");
		db.CreateTable(typeof(MasterMeisho), true, false);
		var item = new MasterMeisho { Kubun = MasterMeisho.KubunIndex, KubunName = "名称区分", Code = "T01", Name = "テスト", Vdc = 100, Vdu = 200 };
		db.Insert(item);

		var request = new CvMsg {
			Flag = CvFlag.Msg101_Op_Query,
			Code = 0,
			DataType = typeof(QueryByIdParam),
			DataMsg = Common.SerializeObject(new QueryByIdParam(typeof(MasterMeisho), item.Id, expectedVdu: 100))
		};

		var result = await service.QueryMsgAsync(request);

		Assert.AreEqual(CvMsgErrorCode.ConcurrentUpdate, result.Code);
		Assert.AreEqual("他で更新されています", result.Option);

		request.DataMsg = Common.SerializeObject(new QueryByIdParam(typeof(MasterMeisho), item.Id, expectedVdu: 200));
		var latestResult = await service.QueryMsgAsync(request);

		Assert.AreEqual(0, latestResult.Code);
		Assert.AreEqual(typeof(MasterMeisho), latestResult.DataType);
	}

	/// <summary>
	/// Phase3: HandleUpdate のV*列伝播フックが gRPC 経路で実際に動作することを確認する
	/// (名称マスタの改名 → 参照している商品マスタの VBrand が現行名称になる)
	/// </summary>
	[TestMethod]
	public async Task Update_MasterMeishoRename_CascadesToReferencingMasterVColumn() {
		var db = _db ?? throw new AssertFailedException("Database not initialized");
		var service = _service ?? throw new AssertFailedException("Service not initialized");
		db.CreateTable(typeof(MasterMeisho), true, false);
		db.CreateTable(typeof(MasterShohin), true, false);

		var brand = new MasterMeisho { Kubun = MasterMeisho.KubunBrand, KubunName = "ブランド", Code = "01", Name = "旧ブランド", Vdc = 1, Vdu = 1 };
		db.Insert(brand);
		var shohin = new MasterShohin {
			Code = "0001",
			Name = "サンプル商品",
			Id_Brand = brand.Id,
			VBrand = new CodeNameView { Sid = brand.Id, Cd = "01", Mei = "旧ブランド" },
			Vdc = 1,
			Vdu = 1,
		};
		db.Insert(shohin);

		// クライアントと同じ手順: DBから取得 → Name変更 → UpdateParam で送信
		var edit = db.SingleById<MasterMeisho>(brand.Id);
		edit.Name = "新ブランド";
		var request = new CvMsg {
			Flag = CvFlag.Msg201_Op_Execute,
			Code = 0,
			DataType = typeof(UpdateParam),
			DataMsg = Common.SerializeObject(new UpdateParam(typeof(MasterMeisho), Common.SerializeObject(edit)))
		};

		var result = await service.QueryMsgAsync(request);

		Assert.AreEqual(0, result.Code, $"更新が成功する: {result.Option} {result.DataMsg}");
		Assert.AreEqual("新ブランド", db.SingleById<MasterMeisho>(brand.Id).Name, "名称マスタ自体が更新される");
		var after = db.SingleById<MasterShohin>(shohin.Id);
		Assert.AreEqual("新ブランド", after.VBrand.Mei, "商品マスタのVBrandへ伝播している");
		Assert.AreEqual("01", after.VBrand.Cd, "VBrand.Cd");
		Assert.AreEqual(brand.Id, after.VBrand.Sid, "VBrand.Sid");

		// 名称以外の変更では伝播しない(無駄なUPDATEを流さない)
		var edit2 = db.SingleById<MasterMeisho>(brand.Id);
		var vduBefore = after.Vdu;
		edit2.Ryaku = "略称のみ変更";
		var request2 = new CvMsg {
			Flag = CvFlag.Msg201_Op_Execute,
			Code = 0,
			DataType = typeof(UpdateParam),
			DataMsg = Common.SerializeObject(new UpdateParam(typeof(MasterMeisho), Common.SerializeObject(edit2)))
		};
		var result2 = await service.QueryMsgAsync(request2);

		Assert.AreEqual(0, result2.Code, "略称のみの更新も成功する");
		Assert.AreEqual(vduBefore, db.SingleById<MasterShohin>(shohin.Id).Vdu, "Code/Name以外の変更では参照側のVduが動かない");
	}

	/// <summary>
	/// Phase5: Msg047 でV*列とJSON内スナップショットが現在のマスタ内容へ再同期される
	/// </summary>
	[TestMethod]
	public async Task MasterVColumnResync_SyncsStaleSnapshotsAndIsIdempotent() {
		var db = _db ?? throw new AssertFailedException("Database not initialized");
		var service = _service ?? throw new AssertFailedException("Service not initialized");
		foreach (var t in new[] { typeof(MasterMeisho), typeof(MasterShohin), typeof(MasterTokui), typeof(DerivedShohinColSiz) }) {
			db.CreateTable(t, true, false);
		}

		var brand = new MasterMeisho { Kubun = MasterMeisho.KubunBrand, KubunName = "ブランド", Code = "01", Name = "現ブランド", Vdc = 1, Vdu = 1 };
		db.Insert(brand);
		var soko = new MasterTokui { Code = "9001", Name = "現倉庫", TenType = 0, Vdc = 1, Vdu = 1 };
		db.Insert(soko);
		// V*列とJSON内スナップショットが古い/空の状態を作る
		var shohin = new MasterShohin {
			Code = "0001",
			Name = "商品",
			Id_Brand = brand.Id,
			VBrand = new CodeNameView { Sid = brand.Id, Cd = "01", Mei = "旧ブランド" },
			Id_Soko = soko.Id,
			Jsub = [new MasterGeneralMeisho { Kb = "B01", Kbname = "区分", Sid = brand.Id, Cd = "01", Mei = "旧ブランド" }],
			Vdc = 1,
			Vdu = 1,
		};
		db.Insert(shohin);
		db.Execute("update MasterShohin set VSoko = ''");

		var request = new CvMsg { Flag = CvFlag.Msg047_MasterVColumnResync, Code = 0, DataType = typeof(string), DataMsg = string.Empty };
		var result = await service.QueryMsgAsync(request);

		Assert.AreEqual(0, result.Code, $"再同期が成功する: {result.Option} {result.DataMsg}");
		// 実行結果メッセージに開始/終了/所要時間が含まれること
		StringAssert.Contains(result.DataMsg, "開始 ", "開始時刻が含まれる");
		StringAssert.Contains(result.DataMsg, "終了 ", "終了時刻が含まれる");
		StringAssert.Contains(result.DataMsg, "所要 ", "所要時間が含まれる");
		var after = db.SingleById<MasterShohin>(shohin.Id);
		Assert.AreEqual("現ブランド", after.VBrand.Mei, "VBrandが現行名称になる");
		Assert.AreEqual("現倉庫", after.VSoko.Mei, "空だったVSokoが埋まる");
		Assert.AreEqual(soko.Id, after.VSoko.Sid, "VSoko.Sid");
		Assert.AreEqual("現ブランド", after.Jsub![0].Mei, "Jsub内のMeiも現行名称になる");

		// 2回目は更新0件(冪等)
		var result2 = await service.QueryMsgAsync(request);
		Assert.AreEqual(0, result2.Code, "2回目も成功する");
		StringAssert.StartsWith(result2.DataMsg, "更新行数=0", "2回目は更新0件");
	}

	[TestMethod]
	public void RegisterDailySqliteWalCheckpointTask_RegistersTwoAmSchedule() {
		var schedulerService = _schedulerService ?? throw new AssertFailedException("SchedulerService not initialized");

		var result = schedulerService.RegisterDailySqliteWalCheckpointTask();
		var taskId = Guid.Parse(result.TaskId);
		var scheduledTask = _scheduler!.GetTaskById(taskId);

		Assert.AreEqual(0, result.Result);
		Assert.IsNotNull(scheduledTask);

		var next = scheduledTask!.CrontabSchedule.GetNextOccurrence(new DateTime(2026, 5, 23, 1, 0, 0));
		Assert.AreEqual(new DateTime(2026, 5, 23, 2, 0, 0), next);
	}

	[TestMethod]
	public void RegisterMonthlyResummaryTask_RegistersOneTenAmSchedule() {
		var schedulerService = _schedulerService ?? throw new AssertFailedException("SchedulerService not initialized");

		var result = schedulerService.RegisterMonthlyResummaryTask();
		var taskId = Guid.Parse(result.TaskId);
		var scheduledTask = _scheduler!.GetTaskById(taskId);

		Assert.AreEqual(0, result.Result);
		Assert.IsNotNull(scheduledTask);
		Assert.AreEqual(SchedulerService.MonthlyResummaryTaskId, taskId);

		var next = scheduledTask!.CrontabSchedule.GetNextOccurrence(new DateTime(2026, 5, 23, 1, 0, 0));
		Assert.AreEqual(new DateTime(2026, 5, 23, 1, 10, 0), next);
	}

	/// <summary>
	/// 自動実行フラグ改修: SchedulerService.CalculateMinIntervalMinutes がcron式ごとの最小発生間隔(分)を正しく算出すること
	/// </summary>
	[TestMethod]
	public void CalculateMinIntervalMinutes_ReturnsMinimumIntervalMinutesForVariousCronExpressions() {
		// 毎日1回 → 1440分
		Assert.AreEqual(1440, SchedulerService.CalculateMinIntervalMinutes(CrontabSchedule.Parse("0 3 * * *")));
		// 毎時 → 60分
		Assert.AreEqual(60, SchedulerService.CalculateMinIntervalMinutes(CrontabSchedule.Parse("0 * * * *")));
		// 30分毎 → 30分(下限60分未満)
		Assert.AreEqual(30, SchedulerService.CalculateMinIntervalMinutes(CrontabSchedule.Parse("*/30 * * * *")));
		// 既存のワークファイル削除ジョブ(1日2回) → 720分(60分以上)
		Assert.AreEqual(720, SchedulerService.CalculateMinIntervalMinutes(CrontabSchedule.Parse("30 0,12 * * *")));
	}

	/// <summary>
	/// 自動実行フラグ改修: SystemJobDefinitions が7件で、TaskId・JobKeyが全件ユニークであること
	/// </summary>
	[TestMethod]
	public void SystemJobDefinitions_HasSevenUniqueEntries() {
		var defs = SchedulerService.SystemJobDefinitions;

		Assert.AreEqual(7, defs.Count);
		Assert.AreEqual(defs.Count, defs.Select(d => d.TaskId).Distinct().Count(), "TaskIdが重複している");
		Assert.AreEqual(defs.Count, defs.Select(d => d.JobKey).Distinct().Count(), "JobKeyが重複している");
	}

	/// <summary>
	/// 自動実行フラグ改修: 新規3ジョブ(商品名称再構築/V*列再同期/伝票税額再更新)は既定で実行フラグOFFかつ起動間隔チェック対象であること
	/// </summary>
	[TestMethod]
	public void SystemJobDefinitions_NewHeavyJobsAreDefaultDisabledWithMinIntervalCheck() {
		var defs = SchedulerService.SystemJobDefinitions;
		var newJobKeys = new[] {
			SchedulerService.JobKeyMasterShohinMeishoRebuild,
			SchedulerService.JobKeyMasterVColumnResync,
			SchedulerService.JobKeyTranTaxRebuild,
		};

		foreach (var jobKey in newJobKeys) {
			var def = defs.Single(d => d.JobKey == jobKey);
			Assert.IsFalse(def.DefaultEnabled, $"{jobKey} は既定で実行フラグOFFであること");
			Assert.IsTrue(def.CheckMinInterval, $"{jobKey} は起動間隔チェック対象であること");
		}
	}

	/// <summary>
	/// 自動実行フラグ改修: 既存4ジョブは既定で実行フラグONかつ起動間隔チェック対象外であること
	/// </summary>
	[TestMethod]
	public void SystemJobDefinitions_ExistingJobsAreDefaultEnabledWithoutMinIntervalCheck() {
		var defs = SchedulerService.SystemJobDefinitions;
		var existingJobKeys = new[] {
			SchedulerService.JobKeyWalCheckpoint,
			SchedulerService.JobKeyWorkFileCleanup,
			SchedulerService.JobKeyMonthlyResummary,
			SchedulerService.JobKeyJodaiPurge,
		};

		foreach (var jobKey in existingJobKeys) {
			var def = defs.Single(d => d.JobKey == jobKey);
			Assert.IsTrue(def.DefaultEnabled, $"{jobKey} は既定で実行フラグONであること");
			Assert.IsFalse(def.CheckMinInterval, $"{jobKey} は起動間隔チェック対象外であること");
		}
	}

	/// <summary>
	/// 自動実行フラグ改修の要: 起動間隔チェック対象(CheckMinInterval=true)の全ジョブについて、
	/// 既定cron式の最小発生間隔が下限(SchedulerService.MinIntervalMinutes)を満たしていること
	/// </summary>
	[TestMethod]
	public void SystemJobDefinitions_CheckMinIntervalJobsSatisfyMinimumIntervalThreshold() {
		var defs = SchedulerService.SystemJobDefinitions.Where(d => d.CheckMinInterval);

		foreach (var def in defs) {
			var schedule = CrontabSchedule.Parse(def.DefaultCronExpression);
			var minutes = SchedulerService.CalculateMinIntervalMinutes(schedule);
			Assert.IsTrue(
				minutes == null || minutes.Value >= SchedulerService.MinIntervalMinutes,
				$"{def.JobKey} の既定cron式({def.DefaultCronExpression})の最小間隔({minutes}分)が下限({SchedulerService.MinIntervalMinutes}分)を下回っている");
		}
	}

	/// <summary>
	/// 自動実行フラグ改修の回帰防止: protobuf-net は bool の false をワイヤに載せないため、
	/// <see cref="SchedulerTaskInfo.IsEnabled"/> に既定値 true の初期化子を付けると、
	/// サーバが返した false が受信側で true のままになってしまう。既定値は false でなければならない。
	/// </summary>
	[TestMethod]
	public void SchedulerTaskInfo_IsEnabledDefaultsToFalseForProtobufWireCompatibility() {
		var info = new SchedulerTaskInfo();

		Assert.IsFalse(info.IsEnabled, "IsEnabledの既定値はfalseであること(protobuf-netのワイヤ互換のため)");
	}

	/// <summary>
	/// キー体系変更の回帰防止: MasterConfig の Name は "GenericSQLRegAutoExec"+TaskId先頭8桁 で構成されるため、
	/// TaskId の先頭8桁が衝突すると別ジョブの実行フラグ/cron式を上書きしてしまう。
	/// </summary>
	[TestMethod]
	public void SystemJobDefinitions_TaskIdFirstEightCharsAreUnique() {
		var defs = SchedulerService.SystemJobDefinitions;

		var prefixes = defs.Select(d => d.TaskId.ToString()[..8]).Distinct().Count();
		Assert.AreEqual(defs.Count, prefixes, "TaskIdの先頭8桁が重複している(MasterConfigのName衝突につながる)");
	}

	/// <summary>
	/// キー体系変更: MasterConfig の自動実行管理用定数が仕様どおりであること
	/// </summary>
	[TestMethod]
	public void MasterConfig_AutoExecConstants_MatchSpecifiedNamingScheme() {
		// MSTEST0032: 定数同士の比較は静的に真と判定されるが、定数値の変更検知が目的の意図した比較のため抑止する。
#pragma warning disable MSTEST0032
		Assert.AreEqual("自動実行管理", MasterConfig.CategoryAutoExec);
		Assert.AreEqual("GenericSQLRegAutoExec", MasterConfig.NameAutoExecEnabledPrefix);
		Assert.AreEqual("GenericSQLRegAutoExecCron", MasterConfig.NameAutoExecCronPrefix);
		Assert.AreEqual("1", MasterConfig.ValAutoExecEnabled);
		Assert.AreEqual("0", MasterConfig.ValAutoExecDisabled);
#pragma warning restore MSTEST0032
	}

	/// <summary>
	/// MasterConfig一元化の要: 自動実行ジョブ定義の出典は MasterConfig.AutoExecJobDefaults(CvBase)であり、
	/// SchedulerService.SystemJobDefinitions は同じ内容を参照するだけで、二重管理になっていないことを保証する。
	/// 件数・並び順・TaskId・タスク名・cron式・既定の実行フラグが完全に一致することを確認する。
	/// </summary>
	[TestMethod]
	public void MasterConfigAutoExecJobDefaults_MatchesSchedulerServiceSystemJobDefinitions() {
		var masterDefs = MasterConfig.AutoExecJobDefaults;
		var schedulerDefs = SchedulerService.SystemJobDefinitions;

		Assert.AreEqual(masterDefs.Count, schedulerDefs.Count, "件数が一致しない(MasterConfigとSchedulerServiceで二重管理になっている疑いがある)");

		for (int i = 0; i < masterDefs.Count; i++) {
			var m = masterDefs[i];
			var s = schedulerDefs[i];

			Assert.AreEqual(Guid.Parse(m.TaskId), s.TaskId, $"[{i}] TaskIdが一致しない");
			Assert.AreEqual(m.TaskName, s.TaskName, $"[{i}] TaskNameが一致しない");
			Assert.AreEqual(m.Cron, s.DefaultCronExpression, $"[{i}] 既定cron式が一致しない");
			Assert.AreEqual(m.Enabled == MasterConfig.ValAutoExecEnabled, s.DefaultEnabled, $"[{i}] 既定の実行フラグが一致しない");
		}
	}

	/// <summary>
	/// MasterConfig一元化: AutoExecEnabledName/AutoExecCronName が「接頭辞+TaskId先頭8桁」で
	/// 設定名(MasterConfig.Name)を組み立てること。
	/// </summary>
	[TestMethod]
	public void MasterConfigAutoExecNames_BuildPrefixPlusTaskIdFirstEightChars() {
		const string taskId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";

		Assert.AreEqual("GenericSQLRegAutoExeca1b2c3d4", MasterConfig.AutoExecEnabledName(taskId));
		Assert.AreEqual("GenericSQLRegAutoExecCrona1b2c3d4", MasterConfig.AutoExecCronName(taskId));
	}

	/// <summary>
	/// MasterConfig一元化: MasterConfig.AutoExecEnabledName/AutoExecCronName で組み立てたNameが、
	/// CvDomainLogic.SchedulerJobConfigDb が実際にDBへ書き込む行のNameと一致すること。
	/// SchedulerJobConfigDb のキー組み立て(EnabledKey/CronKey)はprivateのため、
	/// SetEnabled/SetCron でDBに書いた行をMasterConfig側の組み立てNameで検索できるかで間接的に検証する。
	/// </summary>
	[TestMethod]
	public void MasterConfigAutoExecNames_MatchSchedulerJobConfigDbPersistedKeys() {
		var db = _db ?? throw new AssertFailedException("Database not initialized");
		var configDb = new SchedulerJobConfigDb(db);
		var taskId = SchedulerService.MonthlyResummaryTaskId;

		configDb.SetEnabled(taskId, false);
		configDb.SetCron(taskId, "*/5 * * * *");

		var enabledRow = db.FirstOrDefault<MasterConfig>(
			$"SELECT * FROM {nameof(MasterConfig)} WHERE Name = @0", MasterConfig.AutoExecEnabledName(taskId.ToString()));
		var cronRow = db.FirstOrDefault<MasterConfig>(
			$"SELECT * FROM {nameof(MasterConfig)} WHERE Name = @0", MasterConfig.AutoExecCronName(taskId.ToString()));

		Assert.IsNotNull(enabledRow, "MasterConfig.AutoExecEnabledNameで組み立てたNameでSchedulerJobConfigDbが書いた行が見つからない(キー組み立てが不一致)");
		Assert.AreEqual(MasterConfig.ValAutoExecDisabled, enabledRow!.Val);
		Assert.IsNotNull(cronRow, "MasterConfig.AutoExecCronNameで組み立てたNameでSchedulerJobConfigDbが書いた行が見つからない(キー組み立てが不一致)");
		Assert.AreEqual("*/5 * * * *", cronRow!.Val);

		// 逆方向: SchedulerJobConfigDb.GetEnabled/GetCronが書き込んだ内容を正しく読み戻せること
		Assert.AreEqual(false, configDb.GetEnabled(taskId));
		Assert.AreEqual("*/5 * * * *", configDb.GetCron(taskId));
	}

	[TestMethod]
	public void ExecuteSqliteWalCheckpoint_ReturnsCheckpointRow() {
		var dbPath = Path.Combine(Path.GetTempPath(), $"sqlite-wal-checkpoint-{Guid.NewGuid():N}.db");
		ExDatabaseSqlite? db = null;
		try {
			db = ExDatabaseSqlite.GetDbConn(dbPath);
			db.Execute("CREATE TABLE IF NOT EXISTS Sample(Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);");
			db.Execute("INSERT INTO Sample(Name) VALUES (@0);", "checkpoint-test");

			var result = SchedulerService.ExecuteSqliteWalCheckpoint(db);
			Assert.IsNotNull(result);
			Assert.IsTrue(result.ContainsKey("busy"));
			Assert.IsTrue(result.ContainsKey("log"));
			Assert.IsTrue(result.ContainsKey("checkpointed"));
		}
		finally {
			db?.Close();
			db?.Dispose();
			(db?.Connection as SqliteConnection)?.Close();
			SqliteConnection.ClearAllPools();
			DeleteFileWithRetry(dbPath);
			DeleteFileWithRetry(dbPath + "-wal");
			DeleteFileWithRetry(dbPath + "-shm");
		}
	}

	[TestMethod]
	public void ExDatabaseOptionClearPools_RemovesWalSidecarFiles() {
		var dbPath = Path.Combine(Path.GetTempPath(), $"sqlite-clear-pools-{Guid.NewGuid():N}.db");
		ExDatabaseSqlite? db = null;
		try {
			db = ExDatabaseSqlite.GetDbConn(dbPath);
			db.Execute("CREATE TABLE IF NOT EXISTS Sample(Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);");
			db.Execute("INSERT INTO Sample(Name) VALUES (@0);", "clear-pools-test");

			Assert.IsTrue(File.Exists(dbPath));
			Assert.IsTrue(
				File.Exists(dbPath + "-wal") || File.Exists(dbPath + "-shm"),
				"WAL sidecar file was not created before cleanup.");

			db.Close();
			db.Dispose();
			ExDatabaseOption.ClearPools(dbPath);

			Assert.IsFalse(File.Exists(dbPath + "-wal"));
			Assert.IsFalse(File.Exists(dbPath + "-shm"));
		}
		finally {
			db?.Close();
			db?.Dispose();
			ExDatabaseOption.ClearPools(dbPath);
			DeleteFileWithRetry(dbPath);
		}
	}

	static void DeleteFileWithRetry(string path) {
		for (int i = 0; i < 5; i++) {
			if (!File.Exists(path)) {
				return;
			}

			try {
				File.Delete(path);
				return;
			}
			catch (IOException) when (i < 4) {
				SqliteConnection.ClearAllPools();
				System.Threading.Thread.Sleep(100);
			}
		}

		if (File.Exists(path)) {
			File.Delete(path);
		}
	}
}
