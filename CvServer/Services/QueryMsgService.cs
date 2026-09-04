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
	// private readonly ISchedulerService _scheduler;
	private readonly IHttpContextAccessor _httpContextAccessor;
	private readonly AppGlobal _appGlobal;
	private readonly PointOfSaleService _pointOfSaleService;

	// フラグ -> ハンドラマップ
	private readonly Dictionary<CvFlag, Func<CvMsg, CallContext, Task<CvMsg>>> _handlers;

	public CoreService(ILogger<CoreService> logger, IConfiguration configuration, IWebHostEnvironment env, IHttpContextAccessor httpContextAccessor,
		ExDatabase db, PointOfSaleService pointOfSaleService, AppGlobal? appGlobal = null) {
		ArgumentNullException.ThrowIfNull(logger);
		ArgumentNullException.ThrowIfNull(configuration);
		ArgumentNullException.ThrowIfNull(env);
		ArgumentNullException.ThrowIfNull(httpContextAccessor);
		ArgumentNullException.ThrowIfNull(db);
		ArgumentNullException.ThrowIfNull(pointOfSaleService);
		_logger = logger;
		_configuration = configuration;
		_env = env;
		_db = db;
		_pointOfSaleService = pointOfSaleService;
		// _scheduler = scheduler;
		_httpContextAccessor = httpContextAccessor;
		_appGlobal = appGlobal ?? AppGlobal.Shared;

		// ハンドラ登録
		_handlers = new Dictionary<CvFlag, Func<CvMsg, CallContext, Task<CvMsg>>> {
			[CvFlag.Msg001_CopyReply] = (req, ctx) => Task.FromResult(HandleCopyReply(req, ctx)),
			[CvFlag.Msg002_GetVersion] = (req, ctx) => Task.FromResult(HandleGetVersion(req, ctx)),
			[CvFlag.Msg003_GetEnv] = (req, ctx) => Task.FromResult(HandleGetEnv(req, ctx)),
			[CvFlag.Msg004_GetConnectionStatus] = (req, ctx) => Task.FromResult(HandleGetConnectionStatus(req, ctx)),
			[CvFlag.Msg041_ConvertList] = (req, ctx) => Task.FromResult(HandlerGetConvertTaskList(req, ctx)),
			[CvFlag.Msg042_GetTableList] = (req, ctx) => Task.FromResult(HandlerGetTableList(req, ctx)),
			[CvFlag.Msg101_Op_Query] = (req, ctx) => Task.FromResult(HandleOpQuery(req, ctx)),
			[CvFlag.Msg201_Op_Execute] = (req, ctx) => Task.FromResult(HandleOpExecute(req, ctx)),
			[CvFlag.Msg300_Op_OutData] = (req, ctx) => Task.FromResult(HandleOutData(req, ctx)),
			[CvFlag.Msg700_Test_Start] = (req, ctx) => Task.FromResult(NotImplementedTask(req, ctx)),
			[CvFlag.Msg701_TestCase001] = (req, ctx) => Task.FromResult(NotImplementedTask(req, ctx)),
			[CvFlag.Msg702_TestCase002] = (req, ctx) => Task.FromResult(NotImplementedTask(req, ctx)),
			[CvFlag.Msg046_MasterShohinMeishoRebuild] = (req, ctx) => Task.FromResult(HandleConvertMasterShohin(req, ctx)),
			[CvFlag.Msg047_MasterVColumnResync] = (req, ctx) => Task.FromResult(HandleMasterVColumnResync(req, ctx)),
			[CvFlag.Msg059_TranTaxRebuild] = (req, ctx) => Task.FromResult(HandleTranTaxRebuild(req, ctx)),
			[CvFlag.Msg060_StocktakeStatus] = (req, ctx) => Task.FromResult(HandleStocktakeStatus(req, ctx)),
			[CvFlag.Msg070_PosLookupProduct] = HandlePosLookupProductAsync,
			[CvFlag.Msg071_PosCheckout] = HandlePosCheckoutAsync,
			[CvFlag.Msg072_PosCancelSale] = HandlePosCancelSaleAsync,
			[CvFlag.Msg073_PosSaveSeisan] = HandlePosSaveSeisanAsync,
		};
	}
	// Product : テストが終わったら、[AllowAnonymous] を [Authorize] へ変更
	[AllowAnonymous]
	//[Authorize]
	public async Task<CvMsg> QueryMsgAsync(CvMsg request, CallContext context = default) {
		_logger.LogInformation($"gRPCリクエストQueryMsgAsync Flag: {request.Flag}, DataType: {request.DataType.ToString()}");
		ArgumentNullException.ThrowIfNull(request);

		if (_handlers.TryGetValue(request.Flag, out var handler)) {
			try {
				var result = await handler(request, context) ?? new CvMsg() { Flag = CvFlag.Msg800_Error_Start, Code = -1, DataType = typeof(string), DataMsg = "Handler returned null." };
				return result;
			}
			catch (Exception ex) {
				_logger.LogError(ex, "QueryMsgAsync handler error Flag:{Flag}", request.Flag);
				var err = new CvMsg() { Flag = request.Flag, Code = -9902, Option = ex.Message, DataType = typeof(string), DataMsg = ex.Message };
				return err;
			}
		}

		// 未実装フラグ
		var defaultErr = new CvMsg {
			Flag = CvFlag.Msg800_Error_Start,
			Code = -1,
			DataType = typeof(string),
			DataMsg = "Unimplemented function."
		};
		return defaultErr;
	}
	/// <summary>
	/// 未実装タスクの共通ハンドラ
	/// </summary>
	/// <param name="request"></param>
	/// <param name="context"></param>
	/// <returns></returns>
	public CvMsg NotImplementedTask(CvMsg request, CallContext context = default) {
		var defaultErr = new CvMsg {
			Flag = CvFlag.Msg800_Error_Start,
			Code = -1,
			DataType = typeof(string),
			DataMsg = $"Unimplemented function. QueryFlag: {request.Flag}"
		};
		return defaultErr;
	}


}
