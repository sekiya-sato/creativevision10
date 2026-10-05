using CodeShare;
using CvAsset;
using CvBase;
using CvDomainLogic;

namespace CvServer.Services;

public partial class CoreService {
	private CvMsg HandleAutoReplenish(CvFlag flag, AutoReplenishParam param) {
		try {
			if (param.Operation is AutoReplenishOperation.Preview or AutoReplenishOperation.Save)
				param = param with { Id_Shain = ResolveLoginShainId() };
			var logic = new AutoReplenishDb(_db);
			var result = param.Operation switch {
				AutoReplenishOperation.Preview => logic.Preview(param),
				AutoReplenishOperation.Save => logic.Save(param),
				AutoReplenishOperation.Load => logic.Load(param.Id_Batch),
				AutoReplenishOperation.Commit => logic.Commit(param, row => Effects.After(WriteOp.Insert, row.GetType(), row, null, row.Vdu)),
				AutoReplenishOperation.Cancel => logic.Cancel(param),
				AutoReplenishOperation.History => logic.History(param.Id_Soko),
				AutoReplenishOperation.LoadSettings => logic.LoadSettings(param.Id_Soko),
				AutoReplenishOperation.SaveStockSetting => logic.SaveStockSetting(param),
				AutoReplenishOperation.SaveExcludeSetting => logic.SaveExcludeSetting(param),
				_ => throw new ArgumentException("自動補充の操作が不正です。")
			};
			return CreateSuccessResponse(flag, typeof(AutoReplenishResult), Common.SerializeObject(result));
		}
		catch (AutoReplenishConflictException ex) {
			return CreateErrorResponse(flag, CvMsgErrorCode.ConcurrentUpdate, ex.Message, typeof(string), ex.Message);
		}
		catch (ArgumentException ex) {
			return CreateErrorResponse(flag, CvMsgErrorCode.InvalidParameter, ex.Message, typeof(string), ex.Message);
		}
		catch (OverflowException) {
			const string message = "補充数量・金額が保存可能な範囲を超えています。";
			return CreateErrorResponse(flag, CvMsgErrorCode.InvalidParameter, message, typeof(string), message);
		}
		catch (Exception ex) { return CreateExceptionResponse(flag, ex, typeof(string), ex.Message); }
	}
}
