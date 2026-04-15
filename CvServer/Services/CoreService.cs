using CodeShare;
using CvBase;
using CvBaseOracle;
using CvDomainLogic;
using Microsoft.AspNetCore.Authorization;
using ProtoBuf.Grpc;


namespace CvServer.Services;

public partial class CoreService : ICoreService {
	private readonly ILogger<CoreService> _logger;
	private readonly IConfiguration _configuration;
	private readonly IWebHostEnvironment _env;
	private readonly ExDatabase _db;
	// private readonly IScheduler _scheduler;
	private readonly IHttpContextAccessor _httpContextAccessor;

	// フラグ -> ハンドラマップ
	private readonly Dictionary<CvFlag, Func<CvMsg, CallContext, CvMsg>> _handlers;

	public CoreService(ILogger<CoreService> logger, IConfiguration configuration, IWebHostEnvironment env, IHttpContextAccessor httpContextAccessor, ExDatabase db) {
		ArgumentNullException.ThrowIfNull(logger);
		ArgumentNullException.ThrowIfNull(configuration);
		ArgumentNullException.ThrowIfNull(env);
		ArgumentNullException.ThrowIfNull(httpContextAccessor);
		ArgumentNullException.ThrowIfNull(db);
		_logger = logger;
		_configuration = configuration;
		_env = env;
		_db = db;
		// _scheduler = scheduler;
		_httpContextAccessor = httpContextAccessor;

		// ハンドラ登録
		_handlers = new Dictionary<CvFlag, Func<CvMsg, CallContext, CvMsg>> {
			[CvFlag.Msg001_CopyReply] = HandleCopyReply,
			[CvFlag.Msg002_GetVersion] = HandleGetVersion,
			[CvFlag.Msg003_GetEnv] = HandleGetEnv,
			[CvFlag.Msg042_GetTableCounts] = HandlerGetTableCounts,
			[CvFlag.Msg101_Op_Query] = (req, ctx) => HandleOpQuery(req, ctx),
			[CvFlag.Msg201_Op_Execute] = (req, ctx) => HandleOpExecute(req, ctx),
			[CvFlag.Msg300_Op_OutData] = (req, ctx) => HandleOutData(req, ctx),
			[CvFlag.Msg700_Test_Start] = (req, ctx) => HandleTestLogicMsg700(req, ctx),
			[CvFlag.Msg701_TestCase001] = (req, ctx) => HandleTestLogicMsg701(req, ctx),
			[CvFlag.Msg702_TestCase002] = (req, ctx) => HandleTestLogicMsg702(req, ctx),
		};
	}
	// ToDo : テストが終わったら、[AllowAnonymous] を [Authorize] へ変更
	[AllowAnonymous]
	//[Authorize]
	public Task<CvMsg> QueryMsgAsync(CvMsg request, CallContext context = default) {
		_logger.LogInformation($"gRPCリクエストQueryMsgAsync Flag: {request.Flag}, DataType: {request.DataType.ToString()}");
		ArgumentNullException.ThrowIfNull(request);

		if (_handlers.TryGetValue(request.Flag, out var handler)) {
			try {
				var result = handler(request, context) ?? new CvMsg() { Flag = CvFlag.Msg800_Error_Start, Code = -1, DataType = typeof(string), DataMsg = "Handler returned null." };
				return Task.FromResult(result);
			}
			catch (Exception ex) {
				_logger.LogError(ex, "QueryMsgAsync handler error Flag:{Flag}", request.Flag);
				var err = new CvMsg() { Flag = request.Flag, Code = -9902, Option = ex.Message, DataType = typeof(string), DataMsg = ex.Message };
				return Task.FromResult(err);
			}
		}

		// 未実装フラグ
		var defaultErr = new CvMsg {
			Flag = CvFlag.Msg800_Error_Start,
			Code = -1,
			DataType = typeof(string),
			DataMsg = "Unimplemented function."
		};
		return Task.FromResult(defaultErr);
	}


	/// <summary>
	/// ストリーミングメッセージを処理する
	/// </summary>
	/// <param name="request"></param>
	/// <param name="context"></param>
	/// <returns></returns>
	[AllowAnonymous]
	//[Authorize]
	public async IAsyncEnumerable<StreamMsg> QueryMsgStreamAsync(CvMsg request, CallContext context = default) {
		ArgumentNullException.ThrowIfNull(request);
		var ct = context.CancellationToken;
		_logger.LogInformation("gRPCストリーミングリクエスト QueryMsgStreamAsync Flag: {Flag}, DataType: {DataType}", request.Flag, request.DataType);
		await Task.Yield();

		// ConvertDb関連フラグの処理
		if (request.Flag is CvFlag.MSg040_ConvertDb or CvFlag.MSg041_ConvertDbInit) {
			var isInit = request.Flag == CvFlag.MSg041_ConvertDbInit;

			// HandleConvertDbStreamAsyncの結果をそのまま返す
			await foreach (var msg in HandleConvertDbStreamAsync(isInit, ct, request.Flag)) {
				yield return msg;
			}
			yield break;
		}
		// 	集計処理
		else if (request.Flag is CvFlag.MSg050_Summary) {
			await foreach (var msg in HandleConvertTestStreamAsync(ct, request.Flag)) {
				yield return msg;
			}
			yield break;
		}
		// テストストリーミング処理（既存）
		else if (request.Flag is CvFlag.MSg710_StreamingTest) {
			// 追加：HandleConvertTestStreamAsync を呼ぶ
			await foreach (var msg in HandleConvertTestStreamAsync(ct, request.Flag)) {
				yield return msg;
			}
			yield break;
		}
	}

	/// <summary>
	/// ConvertDbのストリーミング処理ハンドラ
	/// </summary>
	private async IAsyncEnumerable<StreamMsg> HandleConvertDbStreamAsync(bool isInit, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct, CvFlag flag) {
		var oracleConnectionString = _configuration.GetConnectionString("oracle") ?? string.Empty;
		var fromDb = ExDatabaseOracle.GetDbConn(oracleConnectionString);

		var convertDb = new ConvertDb(fromDb, _db);

		// ストリーミングをメッセージに変換
		// ConvertAllAsyncStream()が既にエラーハンドリングしているため、try-catchは不要
		await foreach (var progress in convertDb.ConvertAllAsyncStream(isInit).WithCancellation(ct)) {
			yield return new StreamMsg {
				Flag = flag,
				Code = progress.IsError ? -1 : 0,
				DataType = typeof(string),
				DataMsg = progress.IsError
					? $"エラー: {progress.StepName} - {progress.ErrorMessage} ----{DateTime.Now: MM/dd HH:mm:ss.fff}"
					: $"{(progress.IsCompleted ? "完了" : "処理中")}: {progress.StepName} 件数={progress.Count} ----{DateTime.Now: MM/dd HH:mm:ss.fff}",
				Progress = progress.Progress,
				IsCompleted = progress.IsCompleted,
				IsError = progress.IsError
			};
		}
	}
	/// <summary>
	/// MSg050_Summaryのストリーミング処理ハンドラ
	/// </summary>
	private async IAsyncEnumerable<StreamMsg> HandleSummaryStreamAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct, CvFlag flag) {
		var summaryDb = new SummaryDb(_db);

		// ストリーミングをメッセージに変換
		// ConvertAllAsyncStream()が既にエラーハンドリングしているため、try-catchは不要
		await foreach (var progress in summaryDb.SummaryAllAsyncStream().WithCancellation(ct)) {
			yield return new StreamMsg {
				Flag = flag,
				Code = progress.IsError ? -1 : 0,
				DataType = typeof(string),
				DataMsg = progress.IsError
					? $"エラー: {progress.StepName} - {progress.ErrorMessage} ----{DateTime.Now: MM/dd HH:mm:ss.fff}"
					: $"{(progress.IsCompleted ? "完了" : "処理中")}: {progress.StepName} 件数={progress.Count} ----{DateTime.Now: MM/dd HH:mm:ss.fff}",
				Progress = progress.Progress,
				IsCompleted = progress.IsCompleted,
				IsError = progress.IsError
			};
		}
	}

	#region テストストリーミング処理
	/// <summary>
	/// ダミーのタスク(時間がかかる処理のシミュレート) — 非同期＆キャンセル対応
	/// </summary>
	/// <returns></returns>
	static async Task<int> SleepTaskAsync(int miliSeconds = 1000, CancellationToken ct = default) {
		for (int i = 0; i < 3; i++) {
			await Task.Delay(miliSeconds, ct);
		}
		return 0;
	}

	private async IAsyncEnumerable<StreamMsg> HandleConvertTestStreamAsync(
		[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct,
		CvFlag flag) {
		var start = DateTime.Now;
		var stepNames = new[] {
			"This is First Step",
			"This is Second Step",
			"This is Third Step",
			"This is 4th Step",
			"This is 5th Step",
			"This is 6th Step",
			"This is 7th Step",
			"This is 8th Step",
		};

		for (var index = 0; index < stepNames.Length; index++) {
			ct.ThrowIfCancellationRequested();
			var name = stepNames[index];
			var startProgress = index * 100 / stepNames.Length;
			yield return new StreamMsg {
				Flag = flag,
				Code = 0,
				DataType = typeof(string),
				DataMsg = $"開始: {name} ---- {DateTime.Now: MM/dd HH:mm:ss.fff}",
				Progress = startProgress
			};

			var count = await SleepTaskAsync(1000, ct);
			var endProgress = (int)Math.Round((index + 1) * 100d / stepNames.Length, MidpointRounding.AwayFromZero);
			yield return new StreamMsg {
				Flag = flag,
				Code = 0,
				DataType = typeof(string),
				DataMsg = $"完了: {name} 件数={count} ----{DateTime.Now: MM/dd HH:mm:ss.fff}",
				Progress = endProgress
			};
		}

		var elapsed = DateTime.Now - start;
		yield return new StreamMsg {
			Flag = flag,
			Code = 0,
			DataType = typeof(string),
			DataMsg = $"完了: {elapsed.TotalSeconds:0.0}s  ----{DateTime.Now: MM/dd HH:mm:ss.fff}",
			Progress = 100,
			IsCompleted = true
		};
	}
	#endregion
}
