using CodeShare;
using CvBase;
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

}
